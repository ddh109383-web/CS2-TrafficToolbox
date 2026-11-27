using Game.Common;
using Game.Net;
using Game.Pathfind;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VehicleFlags=Game.Vehicles.CarLaneFlags;

namespace RoadLaneLock.Experimental
{
 public enum LockAction : byte { FixCurrent, FixNext, BeginDetour, HoldCurrent, AvoidTurn, BlockedTurnDetour, SpillbackTurnDetour, LoopReplan, CongestionDetour, EmergencyBorrow, EmergencyYield, TaxiEntryAligned, ApproachAligned }
 public struct PlanObservation { public Entity Vehicle, CurrentLane; public LockAction Action; }
 public enum PlanResult : byte { Unaffected, Continuous, NeedsLegalExit, ExistingLaneChange, Unsupported }
 public struct PhysicalCandidate
 {
  public Entity Entity;
  public PathNode Start,End;
  public float Cost;
 }
 // Native-container counterpart. Input consists only of
 // physical, admissible lanes. No finite penalty can authorize a missing edge.
 public static class NativeHorizonPlanner
 {
  public static bool Solve(NativeList<PhysicalCandidate> candidates,NativeList<int2> layers,ref NativeList<Entity> output)
  {
   output.Clear();
   if(layers.Length==0||layers.Length>13||candidates.Length>1664)return false;
   var costs=new NativeArray<float>(candidates.Length,Allocator.Temp);
   var successors=new NativeArray<int>(candidates.Length,Allocator.Temp);
   try
   {
    for(int n=layers.Length-1;n>=0;n--)
    {
     int2 row=layers[n];
     if(row.x<0||row.y<=row.x||row.y>candidates.Length)return false;
     for(int i=row.x;i<row.y;i++)
     {
      var candidate=candidates[i];costs[i]=float.PositiveInfinity;successors[i]=-1;
      if(!math.isfinite(candidate.Cost)||candidate.Cost<0)continue;
      if(n==layers.Length-1){costs[i]=candidate.Cost;continue;}
      int2 next=layers[n+1];
      for(int j=next.x;j<next.y;j++)
      {
       if(!candidate.End.Equals(candidates[j].Start)||!math.isfinite(costs[j]))continue;
       float cost=candidate.Cost+costs[j];
       if(cost<costs[i]||(cost==costs[i]&&successors[i]>=0&&candidates[j].Entity.Index<candidates[successors[i]].Entity.Index))
       {costs[i]=cost;successors[i]=j;}
      }
     }
    }
    int best=-1;float bestCost=float.PositiveInfinity;
    for(int i=layers[0].x;i<layers[0].y;i++)
     if(costs[i]<bestCost||(math.isfinite(costs[i])&&costs[i]==bestCost&&best>=0&&candidates[i].Entity.Index<candidates[best].Entity.Index))
     {best=i;bestCost=costs[i];}
    if(best<0)return false;
    for(int n=0;n<layers.Length;n++){output.Add(candidates[best].Entity);best=successors[best];}
    return true;
   }
   finally{costs.Dispose();successors.Dispose();}
  }
 }

 // Read-only adapter for the navigation job. Does not change a car, path or lane.
 // Real control must consume Continuous and implement the other outcomes, rather
 // than falling back to unrestricted lane selection when a legal route is absent.
 public struct GameHorizonPlanner
 {
  public ComponentLookup<Owner> Owners;
  public ComponentLookup<Lane> Lanes;
  public ComponentLookup<CarLane> Cars;
  public ComponentLookup<MasterLane> Masters;
  public ComponentLookup<SlaveLane> Slaves;
  public BufferLookup<SubLane> SubLanes;
  public NativeParallelHashMap<Entity,byte> ProtectedOwners;
  public Game.Net.CarLaneFlags Forbidden;
  public PathMethod Method;
  public ComponentLookup<EdgeLane> DirectionalLanes;
  public ComponentLookup<Curve> Curves;
  public ComponentLookup<Deleted> DeletedLanes;
  internal static bool MatchesDirection(Entity lane,ComponentLookup<Owner> owners,ComponentLookup<EdgeLane> lanes,
   ComponentLookup<CarLane> cars,ComponentLookup<MasterLane> masters,ComponentLookup<Curve> curves,
   ComponentLookup<Deleted> deleted,NativeParallelHashMap<Entity,byte> rules)
  {
   if(!owners.TryGetComponent(lane,out var owner)||!rules.TryGetValue(owner.m_Owner,out var mask)||!lanes.TryGetComponent(lane,out var data))return false;
   if(!cars.TryGetComponent(lane,out var car)||masters.HasComponent(lane)||deleted.HasComponent(lane)||!curves.HasComponent(lane))return false;
   if((car.m_Flags&(Game.Net.CarLaneFlags.SideConnection|Game.Net.CarLaneFlags.Twoway))!=0)return false;
   var delta=data.m_EdgeDelta;
   return (mask&RoadLaneLock.Core.NavigationSafety.Direction(delta.x,delta.y))!=0;
  }
  public bool Protected(Entity lane)=>MatchesDirection(lane,Owners,DirectionalLanes,Cars,Masters,Curves,DeletedLanes,ProtectedOwners);
  public PlanResult Evaluate(CarCurrentLane current,DynamicBuffer<CarNavigationLane> navigation,ref NativeList<Entity> output)
  {
   output.Clear();
   bool relevant=Protected(current.m_Lane);
   for(int i=0;i<navigation.Length&&!relevant;i++)relevant=Protected(navigation[i].m_Lane);
   if(!relevant)return PlanResult.Unaffected;
   if(current.m_ChangeLane!=Entity.Null)return PlanResult.ExistingLaneChange;
   if(navigation.Length>12)return PlanResult.Unsupported;
   int lastProtected=-1;
   for(int i=0;i<navigation.Length;i++)if(Protected(navigation[i].m_Lane))lastProtected=i;
   int limit=math.min(navigation.Length,lastProtected+3);
   if((current.m_LaneFlags&(VehicleFlags.ParkingSpace|VehicleFlags.Waypoint))!=0)return PlanResult.Unsupported;
   var candidates=new NativeList<PhysicalCandidate>(32,Allocator.Temp);
   var layers=new NativeList<int2>(13,Allocator.Temp);
   try
   {
    if(!Append(current.m_Lane,Protected(current.m_Lane)||(current.m_LaneFlags&VehicleFlags.FixedLane)!=0,ref candidates,ref layers))return PlanResult.Unsupported;
    for(int i=0;i<limit;i++)
    {
     var nav=navigation[i];
     if((nav.m_Flags&(VehicleFlags.TransformTarget|VehicleFlags.GroupTarget|VehicleFlags.ParkingSpace|VehicleFlags.Waypoint))!=0)
     {
      if(i>lastProtected)break;
      return PlanResult.Unsupported;
     }
     bool fixedLane=(nav.m_Flags&(VehicleFlags.Reserved|VehicleFlags.FixedLane|VehicleFlags.FixedStart))!=0;
     if(!Append(nav.m_Lane,fixedLane,ref candidates,ref layers))return PlanResult.Unsupported;
    }
    return NativeHorizonPlanner.Solve(candidates,layers,ref output)?PlanResult.Continuous:PlanResult.NeedsLegalExit;
   }
   finally{candidates.Dispose();layers.Dispose();}
  }
  // Anchor the first protected stretch to the actual outgoing junction lane.
  // A tentative fixed through-lane on that stretch may be replaced before
  // reservation. The occupied lane and reservations are never rewritten.
  public bool PlanApproach(CarCurrentLane current,DynamicBuffer<CarNavigationLane> nav,ref NativeList<Entity> output)
  {
   if(current.m_ChangeLane!=Entity.Null||Protected(current.m_Lane)||!DirectionalLanes.HasComponent(current.m_Lane))return false;
   const VehicleFlags special=VehicleFlags.Waypoint|VehicleFlags.EndOfPath|VehicleFlags.ParkingSpace|VehicleFlags.Connection|
    VehicleFlags.Area|VehicleFlags.GroupTarget|VehicleFlags.TransformTarget|VehicleFlags.Queue|VehicleFlags.Interruption;
   if((current.m_LaneFlags&special)!=0)return false;
   int exit=-1;bool entered=false;
   for(int i=0;i<nav.Length;i++){
    var part=nav[i];
    if((part.m_Flags&special)!=0||math.any(part.m_CurvePosition!=new float2(0,1)))return false;
    if(Protected(part.m_Lane)){entered=true;continue;}
    if(entered){exit=i;break;}
   }
   if(exit<1||DirectionalLanes.HasComponent(nav[exit].m_Lane))return false;
   var candidates=new NativeList<PhysicalCandidate>(32,Allocator.Temp);
   var layers=new NativeList<int2>(exit+2,Allocator.Temp);
   try{
    if(!Append(current.m_Lane,(current.m_LaneFlags&VehicleFlags.FixedLane)!=0,ref candidates,ref layers))return false;
    for(int i=0;i<=exit;i++){
     var part=nav[i];
     bool pinned=i==exit||(part.m_Flags&(VehicleFlags.Reserved|VehicleFlags.FixedStart|VehicleFlags.ClearedForPathfind))!=0||
      ((part.m_Flags&VehicleFlags.FixedLane)!=0&&!Protected(part.m_Lane));
     if(!Append(part.m_Lane,pinned,ref candidates,ref layers))return false;
    }
    return NativeHorizonPlanner.Solve(candidates,layers,ref output);
   }finally{candidates.Dispose();layers.Dispose();}
  }
  bool Append(Entity source,bool fixedLane,ref NativeList<PhysicalCandidate> candidates,ref NativeList<int2> layers)
  {
   int start=candidates.Length;
   if(!Slaves.TryGetComponent(source,out var slave)||fixedLane)
   {
    if(!Add(source,0,ref candidates))return false;
   }
   else
   {
    if((slave.m_Flags&(SlaveLaneFlags.MiddleStart|SlaveLaneFlags.MiddleEnd))!=0)return false;
    if(!Owners.TryGetComponent(source,out var owner)||!SubLanes.HasBuffer(owner.m_Owner))return false;
    var group=SubLanes[owner.m_Owner];
    if(slave.m_MinIndex>slave.m_MaxIndex||slave.m_MaxIndex>=group.Length||slave.m_MaxIndex-slave.m_MinIndex>=128)return false;
    for(int i=slave.m_MinIndex;i<=slave.m_MaxIndex;i++)
    {
     var sub=group[i];
     if((sub.m_PathMethods&Method)==0)continue;
     Add(sub.m_SubLane,sub.m_SubLane==source?0:1,ref candidates);
    }
   }
   if(candidates.Length==start)return false;
   layers.Add(new int2(start,candidates.Length));return true;
  }
  bool Add(Entity entity,float cost,ref NativeList<PhysicalCandidate> candidates)
  {
   if(Masters.HasComponent(entity)||!Lanes.TryGetComponent(entity,out var lane)||!Cars.TryGetComponent(entity,out var car))return false;
   if(!Owners.TryGetComponent(entity,out var owner)||!SubLanes.HasBuffer(owner.m_Owner))return false;
   var siblings=SubLanes[owner.m_Owner];bool allowed=false;
   for(int i=0;i<siblings.Length;i++)if(siblings[i].m_SubLane==entity){allowed=(siblings[i].m_PathMethods&Method)!=0;break;}
   if(!allowed)return false;
   if((car.m_Flags&(Forbidden|Game.Net.CarLaneFlags.SideConnection|Game.Net.CarLaneFlags.Forbidden))!=0)return false;
   if(Slaves.TryGetComponent(entity,out var slave)&&(slave.m_Flags&(SlaveLaneFlags.MiddleStart|SlaveLaneFlags.MiddleEnd))!=0)return false;
   candidates.Add(new PhysicalCandidate{Entity=entity,Start=lane.m_StartNode,End=lane.m_EndNode,Cost=cost});return true;
  }
 }
}

