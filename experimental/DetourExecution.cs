using Game.Common;
using Game.Net;
using Game.Pathfind;
using Game.Prefabs;
using Game.Vehicles;
using Game.Objects;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VFlags=Game.Vehicles.CarLaneFlags;

namespace RoadLaneLock.Experimental
{
 public struct TurnWatch
 {
  public Entity Vehicle, Entry, Landing, Blocker, Goal;
  public float Position;
  public double Since,Retry;
  public uint LastSeen,AvoidUntil;
  public Entity AvoidNode;
 }
 // Scoped by trip and directed entry lane. Only completed exits enter the history.
 public struct Decision:System.IEquatable<Decision>
 {
  public Entity Vehicle,Target,Entry,Maneuver;
  public bool Equals(Decision d)=>Vehicle==d.Vehicle&&Target==d.Target&&Entry==d.Entry&&Maneuver==d.Maneuver;
  public override int GetHashCode()=>unchecked((Vehicle.GetHashCode()*397^Target.GetHashCode())*397^Entry.GetHashCode()^Maneuver.GetHashCode());
 }
 public struct ExitHistory
 {
  public RoadLaneLock.Core.CompletedExits<Entity> Connections;
  public uint LastUsed;
  public double RetryAfter;
 }
 public struct ReplanBudget
 {
  public Entity Target,Entry;
  public float3 Origin;
  public int Requests;

 }
 public struct DetourRoute
 {
  public FixedList512Bytes<Entity> Lanes;
  public int ExitIndex;
  public Decision Decision;
 }
 public struct ServiceAttemptState {public RoadLaneLock.Core.ServiceFallback Policy;public Entity Target;public bool Released;
  public bool SameAs(ServiceAttemptState other)
  {
   return Target==other.Target && Released==other.Released &&
    Policy.Failures==other.Policy.Failures && Policy.Counted==other.Policy.Counted &&
    Policy.Waiting==other.Policy.Waiting && Policy.Native==other.Policy.Native;
  }
 }
 public struct ServiceAttemptEvent {public Entity Vehicle;public ServiceAttemptState State;public bool Remove;}
 public struct DetourEvent {public Entity Vehicle;public DetourRoute Route;public bool Remove;public Decision FailedDecision;public ReplanBudget Budget;}
}
namespace Game.Simulation
{
 public partial class RoadLaneLockNavigationSystem
 {
  private partial struct UpdateNavigationJob
  {
   // Release mod locks after two failures; preserve native work and parking constraints.
   private bool ReleaseServiceLocks(Entity entity,Moving moving,CarData prefab,
    ref CarCurrentLane current,ref PathOwner path,DynamicBuffer<CarNavigationLane> nav,DynamicBuffer<PathElement> elements)
   {
    if(current.m_ChangeLane!=Entity.Null)return false;
    bool detouring=m_Detours.ContainsKey(entity);
    if((path.m_State&(PathFlags.Pending|PathFlags.Scheduled|PathFlags.Updated))!=0)return false;
    if(CanReleaseOrdinaryServiceLane(current.m_Lane,current.m_LaneFlags))
     current.m_LaneFlags&=~VFlags.FixedLane;
    for(int i=0;i<nav.Length;i++)
    {
     var n=nav[i];
     if(CanReleaseOrdinaryServiceLane(n.m_Lane,n.m_Flags))n.m_Flags&=~VFlags.FixedLane;
     nav[i]=n;
    }
    current.m_LaneFlags|=VFlags.UpdateOptimalLane;
    if(detouring)
    {
     // Native braking-aware path origin, without deleting or moving the vehicle.
     VehicleUtils.ClearNavigationForPathfind(moving,prefab,false,ref current,nav,
      ref m_CarLaneData,ref m_PedestrianLaneData,ref m_CurveData);
     elements.Clear();path.m_ElementIndex=0;path.m_State|=PathFlags.Obsolete;
    }
    m_DetourEvents.Enqueue(new RoadLaneLock.Experimental.DetourEvent{Vehicle=entity,Remove=true});
    return true;
   }
   private bool CanReleaseOrdinaryServiceLane(Entity lane,VFlags flags)
   {
    const VFlags nativeTarget=VFlags.Waypoint|VFlags.ParkingSpace|VFlags.EndOfPath|VFlags.TransformTarget|VFlags.GroupTarget|VFlags.Connection|VFlags.Area|VFlags.FixedStart;
    return (flags&nativeTarget)==0&&ProtectedLane(lane)&&m_SlaveLaneData.HasComponent(lane)&&
     m_CarLaneData.TryGetComponent(lane,out var carLane)&&(carLane.m_Flags&Game.Net.CarLaneFlags.SideConnection)==0;
   }
   private bool ProtectedLane(Entity lane)=>RoadLaneLock.Experimental.GameHorizonPlanner.MatchesDirection(lane,m_OwnerData,m_DirectionalLanes,m_CarLaneData,m_MasterLaneData,m_CurveData,m_Deleted,m_ProtectedOwners);
   private bool RejectMidroadReversal(CarCurrentLane current,DynamicBuffer<CarNavigationLane> navigation)
   {
    if(navigation.Length==0||!ProtectedLane(current.m_Lane))return false;
    var next=navigation[0].m_Lane;
    if(!m_OwnerData.TryGetComponent(current.m_Lane,out var a)||!m_OwnerData.TryGetComponent(next,out var b)||
     !m_Edges.HasComponent(a.m_Owner)||!m_LaneData.TryGetComponent(current.m_Lane,out var from)||
     !m_LaneData.TryGetComponent(next,out var to)||!m_CurveData.TryGetComponent(current.m_Lane,out var c)||
     !m_CurveData.TryGetComponent(next,out var d))return false;
    float dot=math.dot(math.normalizesafe(c.m_Bezier.d-c.m_Bezier.a),math.normalizesafe(d.m_Bezier.d-d.m_Bezier.a));
    return RoadLaneLock.Core.NavigationSafety.RejectReversal(true,a.m_Owner==b.m_Owner,from.m_EndNode.Equals(to.m_StartNode),current.m_ChangeLane!=Entity.Null,dot);
   }
   // Follow the exact landing lane, never a sibling that would require crossing a solid line.
   private bool NearRule(Entity node)
   {
    if(!m_ConnectedEdges.HasBuffer(node))return false;
    foreach(var edge in m_ConnectedEdges[node])if(m_ProtectedOwners.ContainsKey(edge.m_Edge))return true;
    return false;
   }
   private bool InspectTurn(CarCurrentLane current,DynamicBuffer<CarNavigationLane> nav,out bool mismatch,out Entity goal)
   {
    mismatch=false;goal=Entity.Null;
    const VFlags access=VFlags.ParkingSpace|VFlags.Waypoint|VFlags.EndOfPath|VFlags.TransformTarget|VFlags.GroupTarget;
    if(nav.Length==0||current.m_ChangeLane!=Entity.Null||((current.m_LaneFlags|nav[0].m_Flags)&access)!=0)return false;
    Entity entry=current.m_Lane,landing=nav[0].m_Lane;
    if(!m_OwnerData.TryGetComponent(landing,out var destination)||!m_OwnerData.TryGetComponent(entry,out var owner)||
     owner.m_Owner!=destination.m_Owner||!m_Edges.TryGetComponent(owner.m_Owner,out var edge)||
     !m_LaneData.TryGetComponent(entry,out var a)||!m_LaneData.TryGetComponent(landing,out var b)||
     !m_CurveData.TryGetComponent(entry,out var c)||!m_CurveData.TryGetComponent(landing,out var d))return false;
    float dot=math.dot(math.normalizesafe(c.m_Bezier.d-c.m_Bezier.a),math.normalizesafe(d.m_Bezier.d-d.m_Bezier.a));
    if(!RoadLaneLock.Core.NavigationSafety.RejectReversal(true,true,a.m_EndNode.Equals(b.m_StartNode),false,dot))return false;
    if(!ProtectedLane(landing)&&!NearRule(edge.m_Start)&&!NearRule(edge.m_End))return false;
    Entity previous=landing;
    for(int i=1;i<nav.Length;i++)
    {
     Entity lane=nav[i].m_Lane;
     if(!m_LaneData.TryGetComponent(lane,out var next)||!m_OwnerData.TryGetComponent(lane,out var nextOwner))return false;
     if(ProtectedLane(previous)&&!m_LaneData[previous].m_EndNode.Equals(next.m_StartNode))mismatch=true;
     if((nav[i].m_Flags&access)!=0)return false;
     if(nextOwner.m_Owner!=owner.m_Owner&&m_Edges.HasComponent(nextOwner.m_Owner)){goal=lane;break;}
     previous=lane;
    }
    return true;
   }
   private const Game.Net.CarLaneFlags UTurnFlags=Game.Net.CarLaneFlags.UTurnLeft|Game.Net.CarLaneFlags.UTurnRight;
   private bool IsSlowTraffic(Entity vehicle,Entity candidate)
   {
    if(candidate==vehicle||(m_ControllerData.TryGetComponent(candidate,out var ctrl)&&ctrl.m_Controller==vehicle))return false;
    return m_VehicleData.HasComponent(candidate)&&m_MovingData.TryGetComponent(candidate,out var motion)&&math.lengthsq(motion.m_Velocity)<1f;
   }
   private bool GetVehicleLength(Entity vehicle,out float length)
   {
    length=0;
    if(!m_PrefabRefData.TryGetComponent(vehicle,out var prefab)||!m_PrefabObjectGeometryData.TryGetComponent(prefab.m_Prefab,out var body))return false;
    length=body.m_Size.z;
    if(m_Layouts.TryGetBuffer(vehicle,out var parts))
     for(int i=0;i<parts.Length;i++){
      var part=parts[i].m_Vehicle;
      if(part==vehicle)continue;
      if(!m_PrefabRefData.TryGetComponent(part,out var p)||!m_PrefabObjectGeometryData.TryGetComponent(p.m_Prefab,out var geometry))return false;
      length+=geometry.m_Size.z;
     }
    return length>0;
   }
   private bool QueueReachesEntrance(Entity vehicle,CarCurrentLane current,float size)
   {
    if(!m_LaneObjects.TryGetBuffer(current.m_Lane,out var objects))return false;
    float length=m_CurveData[current.m_Lane].m_Length;
    float rear=math.max(0,current.m_CurvePosition.x*length-size*0.5f);
    bool follower=false;
    // Extend only across small gaps between slow occupied intervals. An isolated
    // vehicle waiting at the far end of an otherwise empty road is not a queue.
    for(int pass=0;pass<objects.Length;pass++){
     float before=rear;
     foreach(var obj in objects){
      float lo=math.cmin(obj.m_CurvePosition)*length,hi=math.cmax(obj.m_CurvePosition)*length;
      if(!RoadLaneLock.Core.NavigationSafety.ExtendsQueue(rear,lo,hi)||!IsSlowTraffic(vehicle,obj.m_LaneObject))continue;
      rear=lo;follower=true;
     }
     if(rear==before||rear<=8f)break;
    }
    return RoadLaneLock.Core.NavigationSafety.QueueAtEntrance(follower,rear);
   }
   private bool SpillbackTurn(Entity vehicle,CarCurrentLane current,DynamicBuffer<CarNavigationLane> nav,
    bool cooling,out Entity goal)
   {
    goal=Entity.Null;
    const VFlags access=VFlags.Waypoint|VFlags.EndOfPath|VFlags.ParkingSpace|VFlags.TransformTarget|VFlags.GroupTarget;
    if(nav.Length<3||current.m_ChangeLane!=Entity.Null||((current.m_LaneFlags|nav[0].m_Flags|nav[1].m_Flags)&access)!=0)return false;
    Entity turn=nav[0].m_Lane,landing=nav[1].m_Lane;
    if(!m_CarLaneData.TryGetComponent(turn,out var link)||(link.m_Flags&UTurnFlags)==0||
       !m_OwnerData.TryGetComponent(current.m_Lane,out var origin)||!m_Edges.TryGetComponent(origin.m_Owner,out var edge)||
       !m_OwnerData.TryGetComponent(turn,out var node)||(node.m_Owner!=edge.m_Start&&node.m_Owner!=edge.m_End)||
       !m_OwnerData.TryGetComponent(landing,out var dest)||dest.m_Owner!=origin.m_Owner||
       !m_LaneData[current.m_Lane].m_EndNode.Equals(m_LaneData[turn].m_StartNode)||!m_LaneData[turn].m_EndNode.Equals(m_LaneData[landing].m_StartNode)||
       (!NearRule(edge.m_Start)&&!NearRule(edge.m_End)))return false;
    if(m_LaneSignalData.TryGetComponent(turn,out var signal)&&signal.m_Signal==LaneSignalType.Stop)return false;
    if(!cooling){
     if(!GetVehicleLength(vehicle,out float size)||!m_LaneObjects.TryGetBuffer(landing,out var traffic))return false;
     bool full=false;
     foreach(var obj in traffic)
      if(math.cmin(obj.m_CurvePosition)*m_CurveData[landing].m_Length<size+2f&&IsSlowTraffic(vehicle,obj.m_LaneObject)){full=true;break;}
     if(!full||!QueueReachesEntrance(vehicle,current,size))return false;
    }
    for(int i=2;i<nav.Length;i++){
     if((nav[i].m_Flags&access)!=0)break;
     var lane=nav[i].m_Lane;
     if(m_OwnerData.TryGetComponent(lane,out var o)&&o.m_Owner!=origin.m_Owner&&m_Edges.HasComponent(o.m_Owner)){goal=lane;return true;}
    }
    return false;
   }
   private bool TryAvoidTurn(RoadLaneLock.Experimental.Decision decision,Entity vehicle,Car car,bool bicycle,Moving moving,CarCurrentLane current,Blocker blocker,
    DynamicBuffer<CarNavigationLane> nav,out RoadLaneLock.Experimental.DetourRoute route,out bool blockedWait,out bool spillback,out bool noExit)
   {
    route=default;blockedWait=false;spillback=false;noExit=false;
    bool watched=m_TurnWatches.TryGetValue(vehicle,out var watch);
    bool cooling=watched&&watch.AvoidNode!=Entity.Null&&(int)(watch.AvoidUntil-m_SimulationFrame)>0;
    bool atCooledNode=cooling&&nav.Length>0&&m_OwnerData.TryGetComponent(nav[0].m_Lane,out var owner)&&owner.m_Owner==watch.AvoidNode;
    bool nearStop=math.lengthsq(moving.m_Velocity)<0.04f;
    Entity goal=Entity.Null;
    bool mismatch=false;
    if(nearStop||atCooledNode)spillback=SpillbackTurn(vehicle,current,nav,atCooledNode,out goal);
    if(!spillback&&!InspectTurn(current,nav,out mismatch,out goal))
    {
     if(watched&&!cooling)m_TurnUpdates.Enqueue(new RoadLaneLock.Experimental.TurnWatch{Vehicle=vehicle});
     return false;
    }
    bool stopped=spillback||(nearStop&&(m_CarData.HasComponent(blocker.m_Blocker)||m_TrailerLaneData.HasComponent(blocker.m_Blocker)));
    Entity obstruction=spillback?nav[1].m_Lane:stopped?blocker.m_Blocker:Entity.Null;
    float position=current.m_CurvePosition.x*m_CurveData[current.m_Lane].m_Length;
    if(!mismatch&&!stopped){if(watched&&!cooling)m_TurnUpdates.Enqueue(new RoadLaneLock.Experimental.TurnWatch{Vehicle=vehicle});return false;}
    if(!watched||m_SimulationFrame-watch.LastSeen>32||watch.Entry!=current.m_Lane||watch.Landing!=nav[0].m_Lane||(watch.Blocker==Entity.Null)!=(obstruction==Entity.Null)||
       math.abs(position-watch.Position)>1f)
    {
     watch=new RoadLaneLock.Experimental.TurnWatch{Vehicle=vehicle,Entry=current.m_Lane,Landing=nav[0].m_Lane,
      AvoidNode=watch.AvoidNode,AvoidUntil=watch.AvoidUntil,Goal=goal,Blocker=obstruction,Position=position,Since=m_ActiveSeconds};
    }
    watch.Blocker=obstruction;watch.Goal=goal;watch.LastSeen=m_SimulationFrame;
    blockedWait=RoadLaneLock.Core.NavigationSafety.WaitedAtTurn(stopped,m_ActiveSeconds-watch.Since);
    if((!mismatch&&!blockedWait&&!(spillback&&atCooledNode))||(watch.Retry!=0&&m_ActiveSeconds<watch.Retry)){m_TurnUpdates.Enqueue(watch);return false;}
    watch.Retry=m_ActiveSeconds+2.5;
    if(spillback){
     var node=m_OwnerData[nav[0].m_Lane].m_Owner;
     bool found=BuildLegalExit(decision,current.m_Lane,car,bicycle,out route,true,goal,avoidNode:node);
     if(found){watch.AvoidNode=node;watch.AvoidUntil=m_SimulationFrame+1024;}
     noExit=!found&&m_ExitHistory.ContainsKey(decision);
     m_TurnUpdates.Enqueue(watch);return found;
    }
    m_TurnUpdates.Enqueue(watch);
    if(goal!=Entity.Null&&BuildLegalExit(decision,current.m_Lane,car,bicycle,out route,true,goal))return true;
    // The destination may lie outside the local search. Cross a real junction
    // before requesting a new native route to the original target.
    bool leaving=BuildLegalExit(decision,current.m_Lane,car,bicycle,out route,true,Entity.Null,m_OwnerData[current.m_Lane].m_Owner);
    noExit=!leaving&&m_ExitHistory.ContainsKey(decision);return leaving;
   }
   private void RequestLoopReplan(RoadLaneLock.Experimental.Decision key,PathOwner path,DynamicBuffer<PathElement> elements,float3 position,ref int invalidPath)
   {
    if((path.m_State&(PathFlags.Pending|PathFlags.Scheduled|PathFlags.Obsolete))!=0||
       !m_ExitHistory.TryGetValue(key,out var history)||m_ActiveSeconds<history.RetryAfter)return;
    if(elements.Length==0)return;
    m_ReplanBudgets.TryGetValue(key.Vehicle,out var budget);
    bool same=budget.Target==key.Target && budget.Entry==key.Entry &&
              math.distancesq(budget.Origin,position)<4f;
    if(!same)budget=new RoadLaneLock.Experimental.ReplanBudget{Target=key.Target,Entry=key.Entry,Origin=position};
    if(budget.Requests>=3)return;
    budget.Requests++;
    invalidPath=0;
    RecordLock(key.Vehicle,key.Entry,RoadLaneLock.Experimental.LockAction.LoopReplan);
    m_DetourEvents.Enqueue(new RoadLaneLock.Experimental.DetourEvent{FailedDecision=key,Budget=budget});
   }
   private bool PhysicalLane(Entity lane,Game.Net.CarLaneFlags forbidden,PathMethod method,bool occupied=false)
   {
    if(!m_EntityStorageInfoLookup.Exists(lane)||m_Deleted.HasComponent(lane)||m_MasterLaneData.HasComponent(lane)||!m_LaneData.HasComponent(lane)||!m_CurveData.HasComponent(lane))return false;
    if(!m_CarLaneData.TryGetComponent(lane,out var car))return false;
    // Admission restrictions must not strand a vehicle already on an explicit
    // U-turn connector. Only that occupied lane may relax Unsafe, never its
    // successors or structural restrictions (Forbidden, SideConnection, Twoway).
    bool uturn=(car.m_Flags&(Game.Net.CarLaneFlags.UTurnLeft|Game.Net.CarLaneFlags.UTurnRight))!=0;
    forbidden=(Game.Net.CarLaneFlags)RoadLaneLock.Core.NavigationSafety.RecoveryForbiddenMask(
     (uint)forbidden,(uint)Game.Net.CarLaneFlags.Unsafe,occupied,uturn);
    if((car.m_Flags&(forbidden|Game.Net.CarLaneFlags.Forbidden|Game.Net.CarLaneFlags.SideConnection|Game.Net.CarLaneFlags.Twoway))!=0)return false;
    if(m_SlaveLaneData.TryGetComponent(lane,out var slave)&&(slave.m_Flags&(SlaveLaneFlags.MiddleStart|SlaveLaneFlags.MiddleEnd))!=0)return false;
    if(IsBlockedLane(lane,new float2(0,1)))return false;
    if(!m_OwnerData.TryGetComponent(lane,out var owner)||!m_Lanes.HasBuffer(owner.m_Owner))return false;
    var siblings=m_Lanes[owner.m_Owner];
    for(int i=0;i<siblings.Length;i++)if(siblings[i].m_SubLane==lane)return (siblings[i].m_PathMethods&method)!=0;
    return false;
   }
   private void CollectOwner(Entity owner,ref NativeList<Entity> owners)
   {
    if(owner==Entity.Null||!m_EntityStorageInfoLookup.Exists(owner)||m_Deleted.HasComponent(owner))return;
    for(int i=0;i<owners.Length;i++)if(owners[i]==owner)return;
    if(owners.Length<64)owners.Add(owner);
   }
   private void CollectNodeAndEdges(Entity node,ref NativeList<Entity> owners)
   {
    CollectOwner(node,ref owners);
    if(!m_ConnectedEdges.HasBuffer(node))return;
    var edges=m_ConnectedEdges[node];
    for(int i=0;i<edges.Length;i++)CollectOwner(edges[i].m_Edge,ref owners);
   }
   private void Successors(Entity lane,Game.Net.CarLaneFlags forbidden,PathMethod method,ref NativeList<Entity> next)
   {
    next.Clear();
    if(!m_OwnerData.TryGetComponent(lane,out var owner)||!m_LaneData.TryGetComponent(lane,out var from))return;
    var owners=new NativeList<Entity>(8,Allocator.Temp);
    CollectOwner(owner.m_Owner,ref owners);
    if(m_Edges.TryGetComponent(owner.m_Owner,out var edge))
    {CollectNodeAndEdges(edge.m_Start,ref owners);CollectNodeAndEdges(edge.m_End,ref owners);}
    else if(m_ConnectedEdges.HasBuffer(owner.m_Owner))
    {var edges=m_ConnectedEdges[owner.m_Owner];for(int i=0;i<edges.Length;i++)CollectOwner(edges[i].m_Edge,ref owners);}
    for(int o=0;o<owners.Length;o++)
    {
     if(!m_Lanes.HasBuffer(owners[o]))continue;
     var lanes=m_Lanes[owners[o]];
     for(int i=0;i<lanes.Length;i++)
     {
      Entity candidate=lanes[i].m_SubLane;
      // Most siblings are not connected: reject them before expensive physical checks.
      if(candidate!=lane&&next.Length<256&&m_LaneData.TryGetComponent(candidate,out var candidateLane)&&
       from.m_EndNode.Equals(candidateLane.m_StartNode)&&PhysicalLane(candidate,forbidden,method))next.Add(candidate);
     }
    }
    owners.Dispose();
   }
   private bool MonitorStillBlocked(Candidate offer,CarCurrentLane current,Blocker blocker,DynamicBuffer<CarNavigationLane> nav)
   {
    const VFlags access=VFlags.Waypoint|VFlags.EndOfPath|VFlags.ParkingSpace|VFlags.TransformTarget|VFlags.GroupTarget;
    if((current.m_LaneFlags&access)!=0||
       (m_LaneSignalData.TryGetComponent(offer.Entry,out var signal)&&signal.m_Signal!=LaneSignalType.Go)||
       (blocker.m_Blocker!=Entity.Null&&!m_VehicleData.HasComponent(blocker.m_Blocker)))return false;
    int landing=offer.Turn==offer.Landing?0:1;
    if(nav.Length<=landing||nav[landing].m_Lane!=offer.Landing||!m_LaneObjects.TryGetBuffer(offer.Landing,out var traffic))return false;
    bool goal=false;
    for(int i=0;i<nav.Length;i++){if((nav[i].m_Flags&access)!=0)break;if(i>landing&&nav[i].m_Lane==offer.Goal){goal=true;break;}}
    if(!goal)return false;
    float length=m_CurveData[offer.Landing].m_Length,start=nav[landing].m_CurvePosition.x*length;
    foreach(var obj in traffic)
     if(math.cmax(obj.m_CurvePosition)*length>=start&&math.cmin(obj.m_CurvePosition)*length<start+offer.Size+2f&&IsSlowTraffic(offer.Vehicle,obj.m_LaneObject))return true;
    return false;
   }
   private bool BuildLegalExit(RoadLaneLock.Experimental.Decision decision,Entity entry,Car car,bool bicycle,out RoadLaneLock.Experimental.DetourRoute route,bool recovering=false,Entity goal=default,Entity leaveRoad=default,Entity avoidNode=default,Entity skipTurn=default,float room=0)
   {
    route=default;
    m_ExitHistory.TryGetValue(decision,out var history);
    if(history.Connections.Count>=3)return false;
    var forbidden=VehicleUtils.GetForbiddenLaneFlags(car,bicycle);var method=bicycle?PathMethod.Bicycle:PathMethod.Road;
    if((!recovering&&!ProtectedLane(entry))||!PhysicalLane(entry,forbidden,method,true))return false;
    int limit=avoidNode==Entity.Null&&skipTurn==Entity.Null?256:1536;
    var seenLanes=new NativeParallelHashSet<Entity>(limit,Allocator.Temp);
    var visited=new NativeList<Entity>(64,Allocator.Temp);
    var parents=new NativeList<int>(64,Allocator.Temp);
    var next=new NativeList<Entity>(32,Allocator.Temp);
    try
    {
     seenLanes.Add(entry);visited.Add(entry);parents.Add(-1);int exit=-1;
     for(int p=0;p<visited.Length&&p<limit&&exit<0;p++)
     {
      Entity lane=visited[p];var owner=m_OwnerData[lane].m_Owner;
      if(p>0&&m_Edges.HasComponent(owner)&&owner!=leaveRoad&&(goal!=Entity.Null?lane==goal:!ProtectedLane(lane))){exit=p;break;}
      Successors(lane,forbidden,method,ref next);
      for(int n=0;n<next.Length&&visited.Length<limit;n++)
      {
       if(skipTurn!=Entity.Null){
        var candidate=next[n];if(candidate==skipTurn)continue;
        var flags=m_CarLaneData[candidate].m_Flags;
        if(p==0&&(flags&(UTurnFlags|Game.Net.CarLaneFlags.TurnLeft|Game.Net.CarLaneFlags.TurnRight|Game.Net.CarLaneFlags.SideConnection))!=0)continue;
        if((p==0||parents[p]==0)&&m_Edges.HasComponent(m_OwnerData[candidate].m_Owner)){
         bool free=m_CurveData[candidate].m_Length>=room;
         if(m_LaneObjects.TryGetBuffer(candidate,out var traffic))foreach(var obj in traffic)
          if(math.cmin(obj.m_CurvePosition)*m_CurveData[candidate].m_Length<room&&IsSlowTraffic(decision.Vehicle,obj.m_LaneObject)){free=false;break;}
         if(!free)continue;
        }
       }
       if(history.Connections.Contains(lane,next[n]))continue;
       if(avoidNode!=Entity.Null){
        var flags=m_CarLaneData[next[n]].m_Flags;
        if(m_OwnerData[next[n]].m_Owner==avoidNode&&(flags&UTurnFlags)!=0)continue;
        if(p==0&&((flags&Game.Net.CarLaneFlags.Forward)==0||(flags&(UTurnFlags|Game.Net.CarLaneFlags.TurnLeft|Game.Net.CarLaneFlags.TurnRight))!=0))continue;
       }
       if(seenLanes.Add(next[n])){visited.Add(next[n]);parents.Add(p);}
      }
     }
     if(exit<0)return false;
     FixedList512Bytes<Entity> reversed=default;
     for(int p=exit;p>=0;p=parents[p]){if(reversed.Length>=32)return false;reversed.Add(visited[p]);}
     for(int i=reversed.Length-1;i>=0;i--)route.Lanes.Add(reversed[i]);
     route.ExitIndex=route.Lanes.Length-1;
     route.Decision=decision;
     // Include clearance beyond the exit so a long articulated vehicle can clear
     // the boundary before handing back to global pathfinding.
     float clearance=m_CurveData[route.Lanes[route.ExitIndex]].m_Length;
     while(clearance<200f&&route.Lanes.Length<32)
     {
      Successors(route.Lanes[route.Lanes.Length-1],forbidden,method,ref next);
      Entity chosen=Entity.Null;int best=int.MaxValue;
      for(int n=0;n<next.Length;n++)
      {
       var lane=next[n];if(lane==skipTurn||ProtectedLane(lane)||(avoidNode!=Entity.Null&&m_OwnerData[lane].m_Owner==avoidNode&&(m_CarLaneData[lane].m_Flags&UTurnFlags)!=0))continue;
       bool seen=false;for(int k=0;k<route.Lanes.Length;k++)if(route.Lanes[k]==lane){seen=true;break;}
       if(seen)continue;
       int score=((m_CarLaneData[lane].m_Flags&Game.Net.CarLaneFlags.Forward)!=0?0:10000000)+lane.Index;
       if(score<best){chosen=lane;best=score;}
      }
      if(chosen==Entity.Null)break;
      route.Lanes.Add(chosen);clearance+=m_CurveData[chosen].m_Length;
     }
     return route.Lanes.Length>1;
    }
    finally{seenLanes.Dispose();visited.Dispose();parents.Dispose();next.Dispose();}
   }
   private int RouteIndex(RoadLaneLock.Experimental.DetourRoute route,Entity lane)
   {for(int i=0;i<route.Lanes.Length;i++)if(route.Lanes[i]==lane)return i;return -1;}
   private bool ClearedBoundary(RoadLaneLock.Experimental.DetourRoute route,Entity lane,float position,Entity vehicle)
   {
    int index=RouteIndex(route,lane);if(index<route.ExitIndex)return false;
    float distance=0;
    for(int i=route.ExitIndex;i<index;i++)
    {if(!m_CurveData.HasComponent(route.Lanes[i]))return false;distance+=m_CurveData[route.Lanes[i]].m_Length;}
    if(!m_CurveData.TryGetComponent(lane,out var curve))return false;
    distance+=curve.m_Length*math.saturate(position);
    if(!m_PrefabRefData.TryGetComponent(vehicle,out var prefab)||!m_PrefabObjectGeometryData.TryGetComponent(prefab.m_Prefab,out var geometry))return false;
    return distance>math.max(1f,geometry.m_Size.z*0.5f);
   }
   private bool WholeVehicleCleared(Entity vehicle,CarCurrentLane current,RoadLaneLock.Experimental.DetourRoute route)
   {
    if(!ClearedBoundary(route,current.m_Lane,current.m_CurvePosition.x,vehicle))return false;
    if(m_Layouts.HasBuffer(vehicle))
    {
     var layout=m_Layouts[vehicle];
     for(int i=1;i<layout.Length;i++)
     {
      Entity trailer=layout[i].m_Vehicle;
      if(!m_TrailerLaneData.TryGetComponent(trailer,out var lane)||!ClearedBoundary(route,lane.m_Lane,lane.m_CurvePosition.x,trailer))return false;
     }
    }
    return true;
   }
   // Run after native lane selection, before reservation. Keep both ends of
   // the original route: approach lane -> junction -> protected lane -> exit.
   private void AlignEmptyTaxiApproach(Entity vehicle,Car car,CarCurrentLane current,PathOwner path,DynamicBuffer<CarNavigationLane> nav)
   {
    if(current.m_ChangeLane!=Entity.Null || path.m_State!=0)return;
    const VFlags special=VFlags.EndOfPath|VFlags.Waypoint|VFlags.ParkingSpace|VFlags.Connection|
     VFlags.Area|VFlags.GroupTarget|VFlags.TransformTarget|VFlags.Queue;
    var forbidden=VehicleUtils.GetForbiddenLaneFlags(car,false);
    for(int i=0;i+2<nav.Length;i++)
    {
     var turn=nav[i];var landing=nav[i+1];var exit=nav[i+2];
     if(((turn.m_Flags|landing.m_Flags|exit.m_Flags)&special)!=0)break;
     if(((turn.m_Flags|landing.m_Flags)&(VFlags.Reserved|VFlags.ClearedForPathfind))!=0 ||
        !m_NodeLaneData.HasComponent(turn.m_Lane)||!ProtectedLane(landing.m_Lane)||!m_NodeLaneData.HasComponent(exit.m_Lane)||
        math.any(turn.m_CurvePosition!=new float2(0,1))||math.any(landing.m_CurvePosition!=new float2(0,1))||exit.m_CurvePosition.x!=0)continue;
     var approach=i==0?current.m_Lane:nav[i-1].m_Lane;
     if((i==0?current.m_CurvePosition.z:nav[i-1].m_CurvePosition.y)!=1 ||
        !m_LaneData.TryGetComponent(approach,out var from)||!m_LaneData.TryGetComponent(exit.m_Lane,out var onward)||
        !m_LaneData.TryGetComponent(landing.m_Lane,out var oldLane)||oldLane.m_EndNode.Equals(onward.m_StartNode)||
        !m_OwnerData.TryGetComponent(turn.m_Lane,out var junction)||!m_Lanes.TryGetBuffer(junction.m_Owner,out var turns)||
        !m_OwnerData.TryGetComponent(landing.m_Lane,out var road)||!m_Lanes.TryGetBuffer(road.m_Owner,out var lanes))continue;
     foreach(var candidate in lanes)
     {
      var lane=candidate.m_SubLane;
      if(!m_LaneData.TryGetComponent(lane,out var line)||!line.m_EndNode.Equals(onward.m_StartNode)||
         !ProtectedLane(lane)||!PhysicalLane(lane,forbidden,PathMethod.Road))continue;
      foreach(var link in turns)
      {
       var connector=link.m_SubLane;
       if(!m_NodeLaneData.HasComponent(connector)||!m_LaneData.TryGetComponent(connector,out var bend)||
          !bend.m_StartNode.Equals(from.m_EndNode)||!bend.m_EndNode.Equals(line.m_StartNode)||
          !PhysicalLane(connector,forbidden,PathMethod.Road))continue;
       nav[i]=DetourLane(connector);
       nav[i+1]=DetourLane(lane);
       RecordLock(vehicle,lane,RoadLaneLock.Experimental.LockAction.TaxiEntryAligned);
       return;
      }
     }
    }
   }
   private CarNavigationLane DetourLane(Entity entity)
   {
    var flags=VFlags.FixedLane;
    var car=m_CarLaneData[entity];
    if((car.m_Flags&Game.Net.CarLaneFlags.Forward)==0)
    {
     if((car.m_Flags&(Game.Net.CarLaneFlags.TurnLeft|Game.Net.CarLaneFlags.GentleTurnLeft|Game.Net.CarLaneFlags.UTurnLeft))!=0)flags|=VFlags.TurnLeft;
     if((car.m_Flags&(Game.Net.CarLaneFlags.TurnRight|Game.Net.CarLaneFlags.GentleTurnRight|Game.Net.CarLaneFlags.UTurnRight))!=0)flags|=VFlags.TurnRight;
    }
    if((car.m_Flags&Game.Net.CarLaneFlags.Roundabout)!=0)flags|=VFlags.Roundabout;
    // Two-way lanes need direction-aware path nodes and curve ranges. They are
    // rejected by PhysicalLane instead of guessing a forward traversal.
    return new CarNavigationLane{m_Lane=entity,m_CurvePosition=new float2(0,1),m_Flags=flags};
   }
   private bool FollowDetour(Entity vehicle,Car car,bool bicycle,Moving moving,CarData prefab,
    ref CarCurrentLane current,ref PathOwner path,DynamicBuffer<CarNavigationLane> navigation,DynamicBuffer<PathElement> pathElements,
    RoadLaneLock.Experimental.DetourRoute route)
   {
    int index=RouteIndex(route,current.m_Lane);
    bool valid=index>=0&&current.m_ChangeLane==Entity.Null;
    var forbidden=VehicleUtils.GetForbiddenLaneFlags(car,bicycle);var method=bicycle?PathMethod.Bicycle:PathMethod.Road;
    for(int i=math.max(0,index);i<route.Lanes.Length&&valid;i++)
    {
     valid=PhysicalLane(route.Lanes[i],forbidden,method,i==index);
     if(valid&&i>index)valid=m_LaneData[route.Lanes[i-1]].m_EndNode.Equals(m_LaneData[route.Lanes[i]].m_StartNode);
    }
    if(!valid)
    {
     var decision=route.Decision;decision.Entry=current.m_Lane;
     if(current.m_ChangeLane==Entity.Null && BuildLegalExit(decision,current.m_Lane,car,bicycle,out var replacement,true))
     {
      m_DetourEvents.Enqueue(new RoadLaneLock.Experimental.DetourEvent{Vehicle=vehicle,Route=replacement});
      route=replacement;index=0;
     }
     else
     {
     // Never hand an invalid protected route back to unrestricted lane choice.
     // Hold the current trajectory for recovery; no teleport or lane substitution.
     current.m_LaneFlags|=VFlags.FixedLane;
     current.m_CurvePosition.z=current.m_CurvePosition.y;
     navigation.Clear();
     return current.m_ChangeLane!=Entity.Null;
     }
    }
    if(RoadLaneLock.Core.NavigationSafety.CanHandoff(WholeVehicleCleared(vehicle,current,route),
     ProtectedLane(current.m_Lane),index<route.Lanes.Length-1))
    {
     // Target is untouched: the service AI still requests its destination.
     // Replan from the forward END of this physical lane. The native braking
     // cutoff made a mid-edge origin, admitting immediate opposite-carriageway
     // interruptions (recorded 117660 -> 123132) and a circular waiting queue.
     current.m_CurvePosition.z=1;
     current.m_LaneFlags|=VFlags.FixedLane|VFlags.ClearedForPathfind;
     current.m_LaneFlags&=~(VFlags.EndOfPath|VFlags.GroupTarget|VFlags.TransformTarget);
     navigation.Clear();
     pathElements.Clear();path.m_ElementIndex=0;path.m_State|=PathFlags.Obsolete;
     m_DetourEvents.Enqueue(new RoadLaneLock.Experimental.DetourEvent{Vehicle=vehicle,Remove=true,Route=route});
     return true;
    }
    current.m_LaneFlags|=VFlags.FixedLane;
    current.m_LaneFlags&=~(VFlags.EndOfPath|VFlags.GroupTarget|VFlags.TransformTarget);
    current.m_CurvePosition.z=1;
    int count=math.min(12,route.Lanes.Length-index-1);
    for(int i=0;i<count;i++)
    {
     var next=DetourLane(route.Lanes[index+1+i]);
     if(i<navigation.Length)
     {
      if(navigation[i].m_Lane==next.m_Lane)next.m_Flags|=navigation[i].m_Flags&VFlags.Reserved;
      navigation[i]=next;
     }
     else navigation.Add(next);
    }
    if(navigation.Length>count)navigation.RemoveRange(count,navigation.Length-count);
    return true;
   }
  }
 }
}
