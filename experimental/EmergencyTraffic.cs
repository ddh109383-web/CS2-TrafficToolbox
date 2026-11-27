using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Pathfind;

using Game.Tools;
using Game.Vehicles;
using RoadLaneLock.Experimental;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using NavFlags=Game.Vehicles.CarLaneFlags;
using NetFlags=Game.Net.CarLaneFlags;

namespace Game.Simulation
{
 public partial class RoadLaneLockNavigationSystem
 {
  private EntityQuery responders;
  private NativeParallelHashMap<Entity,Entity> yieldFor;

  private void CreateEmergencyTraffic()
  {
   responders=GetEntityQuery(new EntityQueryDesc{
    All=new[]{ComponentType.ReadOnly<Car>(),ComponentType.ReadOnly<CarCurrentLane>(),ComponentType.ReadOnly<Moving>()},
    Any=new[]{ComponentType.ReadOnly<Ambulance>(),ComponentType.ReadOnly<FireEngine>(),ComponentType.ReadOnly<PoliceCar>()},
    None=new[]{ComponentType.ReadOnly<Deleted>(),ComponentType.ReadOnly<Temp>(),ComponentType.ReadOnly<ParkedCar>()}
   });
   yieldFor=new NativeParallelHashMap<Entity,Entity>(128,Allocator.Persistent);
  }

  // Sample only responders and the short stretch of their planned path ahead.
  private void SampleEmergencyTraffic()
  {
   if(m_SimulationSystem.frameIndex%8!=0)return;
   yieldFor.Clear();
   using(var units=responders.ToEntityArray(Allocator.Temp)){
    foreach(var unit in units){
     if((EntityManager.GetComponentData<Car>(unit).m_Flags&CarFlags.Emergency)==0)continue;
     var current=EntityManager.GetComponentData<CarCurrentLane>(unit);
     float reach=45f;
     MarkYielding(unit,current.m_Lane,current.m_CurvePosition.x,ref reach);
     if(!EntityManager.HasBuffer<CarNavigationLane>(unit))continue;
     var nav=EntityManager.GetBuffer<CarNavigationLane>(unit,true);
     for(int i=0;i<nav.Length&&reach>0;i++){
      if(nav[i].m_CurvePosition.y<=nav[i].m_CurvePosition.x)break;
      MarkYielding(unit,nav[i].m_Lane,nav[i].m_CurvePosition.x,ref reach);
     }
    }
   }
  }
  private void MarkYielding(Entity unit,Entity lane,float start,ref float reach)
  {
   if(!EntityManager.HasComponent<Curve>(lane)){reach=0;return;}
   float length=EntityManager.GetComponentData<Curve>(lane).m_Length;
   if(EntityManager.HasComponent<EdgeLane>(lane)&&EntityManager.HasBuffer<LaneObject>(lane)){
    foreach(var item in EntityManager.GetBuffer<LaneObject>(lane,true)){
     float ahead=(math.cmax(item.m_CurvePosition)-start)*length;
     var vehicle=item.m_LaneObject;
     if(vehicle==unit||ahead<=0||ahead>reach||!EntityManager.HasComponent<Car>(vehicle))continue;
     if(yieldFor.Count()==yieldFor.Capacity)yieldFor.Capacity*=2;
     yieldFor.TryAdd(vehicle,unit);
    }
   }
   reach-=(1-start)*length;
  }

  private partial struct UpdateNavigationJob
  {

   private bool ShouldYield(Entity entity,Car car,CarCurrentLane current)
   {
    return (car.m_Flags&CarFlags.Emergency)==0 && current.m_ChangeLane==Entity.Null &&
     m_DirectionalLanes.HasComponent(current.m_Lane) && m_YieldFor.TryGetValue(entity,out var responder) &&
     m_CarData.TryGetComponent(responder,out var emergencyCar) && (emergencyCar.m_Flags&CarFlags.Emergency)!=0;
   }

   private bool EmergencyLaneClear(Entity vehicle,Entity lane,float start,float behind,float ahead)
   {
    if(!m_CurveData.TryGetComponent(lane,out var geometry))return false;
    if(m_LaneObjects.TryGetBuffer(lane,out var objects))
     foreach(var item in objects){
      if(item.m_LaneObject==vehicle)continue;
      var limits=(item.m_CurvePosition-start)*geometry.m_Length;
      if(math.cmax(limits)>=-behind&&math.cmin(limits)<=ahead)return false;
     }
    return true;
   }

   private struct BorrowStep { public Entity Lane;public int Parent,Depth; }

   // Follow physical car connections on the original road sequence. A painted turn
   // restriction may be bypassed, but the selected junction lane still participates
   // in native signals, overlap/reservation arbitration and pedestrian detection.
   private bool ConnectBorrowedLane(Entity vehicle,Entity entry,DynamicBuffer<CarNavigationLane> nav)
   {
    var steps=new NativeList<BorrowStep>(32,Allocator.Temp);
    try{
     steps.Add(new BorrowStep{Lane=entry,Parent=-1,Depth=0});
     for(int p=0;p<steps.Length;p++){
      var step=steps[p];int depth=step.Depth;
      if(depth>=nav.Length||depth>=6)continue;
      var original=nav[depth];
      if(original.m_CurvePosition.x!=0f||original.m_CurvePosition.y!=1f||
         (original.m_Flags&(NavFlags.Waypoint|NavFlags.EndOfPath|NavFlags.Connection|NavFlags.ParkingSpace|NavFlags.TransformTarget|NavFlags.GroupTarget|NavFlags.Area))!=0||
         !m_OwnerData.TryGetComponent(original.m_Lane,out var owner)||!m_Lanes.TryGetBuffer(owner.m_Owner,out var siblings))continue;
      var end=m_LaneData[step.Lane].m_EndNode;
      foreach(var sub in siblings){
       Entity lane=sub.m_SubLane;
       if((sub.m_PathMethods&PathMethod.Road)==0||m_MasterLaneData.HasComponent(lane)||m_Deleted.HasComponent(lane)||
          !m_LaneData.TryGetComponent(lane,out var topology)||!end.Equals(topology.m_StartNode)||
          !m_CarLaneData.TryGetComponent(lane,out var data)||(data.m_Flags&(NetFlags.SideConnection|NetFlags.Twoway|UTurnFlags))!=0||
          IsBlockedLane(lane,new float2(0,1)))continue;
       if(!EmergencyLaneClear(vehicle,lane,0,0,m_NodeLaneData.HasComponent(lane)?float.MaxValue:12f))continue;
       if(lane==original.m_Lane){
        if(m_NodeLaneData.HasComponent(lane)){
         int exit=depth+1;
         while(exit<nav.Length&&m_NodeLaneData.HasComponent(nav[exit].m_Lane)){
          if(!EmergencyLaneClear(vehicle,nav[exit].m_Lane,0,0,float.MaxValue))break;
          exit++;
         }
         if(exit>=nav.Length||!m_DirectionalLanes.HasComponent(nav[exit].m_Lane)||
            !EmergencyLaneClear(vehicle,nav[exit].m_Lane,nav[exit].m_CurvePosition.x,0,12f))continue;
        }
        for(int i=p;steps[i].Parent>=0;i=steps[i].Parent){
         var part=steps[i];
         nav[part.Depth-1]=DetourLane(part.Lane);
        }
        return true;
       }
       if(steps.Length>=96)continue;
       bool seen=false;
       for(int i=0;i<steps.Length;i++)if(steps[i].Lane==lane){seen=true;break;}
       if(!seen)steps.Add(new BorrowStep{Lane=lane,Parent=p,Depth=depth+1});
      }
     }
     return false;
    }finally{steps.Dispose();}
   }

   private void TryBorrowLane(Entity vehicle,Moving moving,ref CarCurrentLane current,Blocker blocker,PathOwner path,DynamicBuffer<CarNavigationLane> nav)
   {
    if(current.m_ChangeLane!=Entity.Null||nav.Length==0||math.lengthsq(moving.m_Velocity)>36f||
       (path.m_State&(PathFlags.Failed|PathFlags.Pending|PathFlags.Obsolete|PathFlags.Updated))!=0||
       (current.m_LaneFlags&(NavFlags.ParkingSpace|NavFlags.EndOfPath|NavFlags.Connection|NavFlags.Waypoint|NavFlags.Area))!=0||
       !m_DirectionalLanes.TryGetComponent(current.m_Lane,out var direction)||
       !m_SlaveLaneData.TryGetComponent(current.m_Lane,out var group)||
       !m_OwnerData.TryGetComponent(current.m_Lane,out var owner)||!m_Lanes.TryGetBuffer(owner.m_Owner,out var siblings))return;
    if(!IsSlowTraffic(vehicle,blocker.m_Blocker)){
     bool queue=false;
     if(m_LaneObjects.TryGetBuffer(current.m_Lane,out var objects)){
      foreach(var item in objects){
       float gap=(math.cmin(item.m_CurvePosition)-current.m_CurvePosition.x)*m_CurveData[current.m_Lane].m_Length;
       if(gap>=0 && gap<30f && IsSlowTraffic(vehicle,item.m_LaneObject)){queue=true;break;}
      }
     }
     if(!queue)return;
    }
    var axis=m_CurveData[current.m_Lane].m_Bezier;
    var center=MathUtils.Position(axis,current.m_CurvePosition.x);
    var tangent=math.normalizesafe(MathUtils.Tangent(axis,current.m_CurvePosition.x));
    int max=math.min(group.m_MaxIndex,siblings.Length-1);
    for(int i=group.m_MinIndex;i<=max;i++){
     if(siblings[i].m_SubLane!=current.m_Lane)continue;
     for(int delta=-1;delta<=1;delta+=2){
      int j=i+delta;
      if(j<group.m_MinIndex||j>max)continue;
      var side=siblings[j].m_SubLane;
      if((siblings[j].m_PathMethods&PathMethod.Road)==0 || !m_DirectionalLanes.TryGetComponent(side,out var other)||
         math.any(math.abs(other.m_EdgeDelta-direction.m_EdgeDelta)>.025f)||
         !PhysicalLane(side,0,PathMethod.Road)||!EmergencyLaneClear(vehicle,side,current.m_CurvePosition.x,18f,32f))continue;
      var curve=m_CurveData[side].m_Bezier;
      var lateral=MathUtils.Position(curve,current.m_CurvePosition.x)-center;
      if(math.lengthsq(lateral)>30.25f||math.abs(lateral.y)>1f||
         math.dot(tangent,math.normalizesafe(MathUtils.Tangent(curve,current.m_CurvePosition.x)))<.95f)continue;
      if(!ConnectBorrowedLane(vehicle,side,nav))continue;
      current.m_ChangeLane=side;current.m_ChangeProgress=0;
      current.m_LaneFlags&=~(NavFlags.TurnLeft|NavFlags.TurnRight|NavFlags.FixedLane);
      current.m_LaneFlags|=math.dot(lateral,math.cross(new float3(0,1,0),tangent))>0?NavFlags.TurnRight:NavFlags.TurnLeft;
      RecordLock(vehicle,side,LockAction.EmergencyBorrow);
      return;
     }
     break;
    }
   }
  }
 }
}