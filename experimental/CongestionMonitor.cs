using System;
using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Vehicles;
using RoadLaneLock.Experimental;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CarLane=Game.Net.CarLane;
using SubLane=Game.Net.SubLane;
using NetFlags=Game.Net.CarLaneFlags;
using NavFlags=Game.Vehicles.CarLaneFlags;

namespace Game.Simulation
{
 public partial class RoadLaneLockNavigationSystem
 {
  private struct Candidate
  {
   public Entity Vehicle,Entry,Turn,Landing,Goal;
   public float Tail,Room,Size,Position;
  }
  private struct MonitorResult { public Candidate Candidate; public bool Applied; }
  private sealed class DirectionMonitor
  {
   public Candidate Head,Previous;
   public double Since,NextAttempt,ObserveUntil;
   public bool Observing;
   public readonly Dictionary<Entity,Crossing> Crossings=new Dictionary<Entity,Crossing>();
   public readonly HashSet<Game.Pathfind.PathNode> Entrances=new HashSet<Game.Pathfind.PathNode>();
  }
  private sealed class Crossing
  {
   public double StoppedSince=-1;
   public Entity Blocker;
   public bool Reported;
  }
  private readonly List<Entity> finishedCrossings=new List<Entity>();
  private readonly Dictionary<(Entity Road,byte Direction),DirectionMonitor> monitors=new Dictionary<(Entity,byte),DirectionMonitor>();
  private readonly List<(Entity Road,byte Direction)> retired=new List<(Entity,byte)>();
  private readonly List<(float Low,float High,Entity Vehicle)> intervals=new List<(float,float,Entity)>();
  private NativeParallelHashMap<Entity,Candidate> offers;
  private NativeQueue<MonitorResult> monitorResults;
  private double nextMonitorSample;
  public int MonitorRevision {get;private set;}
  public IEnumerable<Entity> MonitoredRoads {get{foreach(var key in monitors.Keys)yield return key.Road;}}
  public bool Monitoring(Entity road,byte direction)=>monitors.ContainsKey((road,direction));
  public string MonitorMessage {get;private set;}
  public void SetMonitoring(Entity road,byte direction,bool enable)
  {
   DrainExecution();
   var key=(road,direction);
   if(enable){if(!monitors.ContainsKey(key)){monitors.Add(key,new DirectionMonitor());MonitorRevision++;}ExecutionEnabled=true;}
   else if(monitors.Remove(key))MonitorRevision++;
   offers.Clear();nextMonitorSample=0;
  }
  private bool LowSpeed(Entity v,float limit)=>EntityManager.HasComponent<Game.Vehicles.Vehicle>(v)&&
   EntityManager.HasComponent<Moving>(v)&&math.lengthsq(EntityManager.GetComponentData<Moving>(v).m_Velocity)<limit*limit;
  private bool ClearSignal(Entity l)=>!EntityManager.HasComponent<LaneSignal>(l)||EntityManager.GetComponentData<LaneSignal>(l).m_Signal==LaneSignalType.Go;
  private Entity ControllerOf(Entity v)=>EntityManager.HasComponent<Controller>(v)?EntityManager.GetComponentData<Controller>(v).m_Controller:v;
  private bool MeasureQueue(Entity lane,out Candidate q)
  {
   q=default;
   if(!EntityManager.HasBuffer<LaneObject>(lane)||!EntityManager.HasComponent<Curve>(lane))return false;
   float length=EntityManager.GetComponentData<Curve>(lane).m_Length;
   intervals.Clear();
   foreach(var obj in EntityManager.GetBuffer<LaneObject>(lane,true))
   {
    if(!LowSpeed(obj.m_LaneObject,1f))continue;
    var v=ControllerOf(obj.m_LaneObject);
    intervals.Add((math.cmin(obj.m_CurvePosition)*length,math.cmax(obj.m_CurvePosition)*length,v==Entity.Null?obj.m_LaneObject:v));
   }
   if(intervals.Count==0)return false;
   intervals.Sort((a,b)=>a.Low.CompareTo(b.Low));
   var first=intervals[0];float front=first.High;
   q.Entry=lane;q.Tail=first.Low;q.Vehicle=first.Vehicle;
   bool follower=false;
   for(int i=1;i<intervals.Count;i++){
    var v=intervals[i];if(v.Low>front+7f)break;
    if(v.Vehicle!=first.Vehicle)follower=true;
    if(v.High>=front){front=v.High;q.Vehicle=v.Vehicle;}
   }
   return follower;
  }
  private bool ReadTurn(ref Candidate q)
  {
   Entity v=q.Vehicle;
   if(!EntityManager.HasComponent<CarCurrentLane>(v)||!EntityManager.HasBuffer<CarNavigationLane>(v)||
      !EntityManager.HasComponent<PrefabRef>(v)||!ClearSignal(q.Entry))return false;
   var current=EntityManager.GetComponentData<CarCurrentLane>(v);
   var nav=EntityManager.GetBuffer<CarNavigationLane>(v,true);
   const NavFlags access=NavFlags.EndOfPath|NavFlags.Waypoint|NavFlags.ParkingSpace|NavFlags.TransformTarget|NavFlags.GroupTarget;
   if(current.m_Lane!=q.Entry||current.m_ChangeLane!=Entity.Null||(current.m_LaneFlags&access)!=0||nav.Length<2)return false;
   if(EntityManager.HasComponent<Blocker>(v)){
    var blocker=EntityManager.GetComponentData<Blocker>(v).m_Blocker;
    if(blocker!=Entity.Null&&!EntityManager.HasComponent<Vehicle>(blocker))return false;
   }
   q.Turn=nav[0].m_Lane;
   if(!EntityManager.HasComponent<CarLane>(q.Turn)||!ClearSignal(q.Turn)||!EntityManager.HasComponent<Owner>(q.Turn))return false;
   var flags=EntityManager.GetComponentData<CarLane>(q.Turn).m_Flags;
   if((flags&NetFlags.SideConnection)!=0)return false;
   Entity road=EntityManager.GetComponentData<Owner>(q.Entry).m_Owner;
   bool middle=EntityManager.GetComponentData<Owner>(q.Turn).m_Owner==road;
   if(middle){
    if(RoadLaneLock.RoadPickTool.LaneDirection(EntityManager,q.Turn)+RoadLaneLock.RoadPickTool.LaneDirection(EntityManager,q.Entry)!=3)return false;
   }else if((flags&(NetFlags.TurnLeft|NetFlags.TurnRight|NetFlags.UTurnLeft|NetFlags.UTurnRight))==0)return false;
   int landing=middle?0:1;q.Landing=nav[landing].m_Lane;
   for(int i=0;i<=landing;i++)if((nav[i].m_Flags&access)!=0)return false;
   if(!EntityManager.HasComponent<Curve>(q.Landing)||!EntityManager.HasBuffer<LaneObject>(q.Landing))return false;
   float length=EntityManager.GetComponentData<Curve>(q.Landing).m_Length;
   float entry=math.saturate(nav[landing].m_CurvePosition.x)*length;
   q.Room=float.MaxValue;
   foreach(var obj in EntityManager.GetBuffer<LaneObject>(q.Landing,true)){
    if(obj.m_LaneObject==v||ControllerOf(obj.m_LaneObject)==v||!LowSpeed(obj.m_LaneObject,1f))continue;
    if(math.cmax(obj.m_CurvePosition)*length<entry)continue;
    q.Room=math.min(q.Room,math.max(0,math.cmin(obj.m_CurvePosition)*length-entry));
   }
   if(!PartLength(v,out q.Size))return false;
   if(EntityManager.HasBuffer<LayoutElement>(v))
    foreach(var part in EntityManager.GetBuffer<LayoutElement>(v,true)){
     if(part.m_Vehicle==v)continue;
     if(!PartLength(part.m_Vehicle,out float extra))return false;q.Size+=extra;
    }
   q.Position=current.m_CurvePosition.x*EntityManager.GetComponentData<Curve>(q.Entry).m_Length;
   float remaining=(middle?current.m_CurvePosition.z:1f)*EntityManager.GetComponentData<Curve>(q.Entry).m_Length-q.Position;
   float approach=10f;
   if(!middle&&EntityManager.HasComponent<Moving>(v))
    approach=math.clamp(math.length(EntityManager.GetComponentData<Moving>(v).m_Velocity)*2f,10f,35f);
   if(remaining>q.Size*0.5f+approach||q.Room>=q.Size+2f)return false;
   for(int i=landing+1;i<nav.Length;i++){
    var n=nav[i];if((n.m_Flags&access)!=0)break;
    if(!EntityManager.HasComponent<Owner>(n.m_Lane))break;
    var owner=EntityManager.GetComponentData<Owner>(n.m_Lane).m_Owner;
    if(owner!=road&&EntityManager.HasComponent<Edge>(owner)){q.Goal=n.m_Lane;return true;}
   }
   // A normal turn may be rejoined on the destination road past its occupied entrance.
   return false;
  }
  private bool PartLength(Entity v,out float length){
   length=0;if(!EntityManager.HasComponent<PrefabRef>(v))return false;
   var p=EntityManager.GetComponentData<PrefabRef>(v).m_Prefab;
   if(!EntityManager.HasComponent<ObjectGeometryData>(p))return false;
   length=EntityManager.GetComponentData<ObjectGeometryData>(p).m_Size.z;return length>0;
  }
  private Entity ApproachLeader(Entity entry)
  {
   if(!EntityManager.HasBuffer<LaneObject>(entry))return Entity.Null;
   float front=-1;Entity leader=Entity.Null;
   foreach(var obj in EntityManager.GetBuffer<LaneObject>(entry,true)){
    var owner=ControllerOf(obj.m_LaneObject);
    var vehicle=owner==Entity.Null?obj.m_LaneObject:owner;
    if(!EntityManager.HasComponent<CarCurrentLane>(vehicle))continue;
    float end=math.cmax(obj.m_CurvePosition);
    if(end>front){front=end;leader=vehicle;}
   }
   return leader;
  }

  // A tractor can be on the exit while its trailer is still in the junction.
  private bool PartInCrossing(Entity vehicle,Entity lane,float position,Entity road,Entity node)
  {
   if(!EntityManager.HasComponent<Owner>(lane))return false;
   var owner=EntityManager.GetComponentData<Owner>(lane).m_Owner;
   if(owner==node)return true;
   if(!EntityManager.HasComponent<Edge>(owner)||!EntityManager.HasComponent<Curve>(lane))return false;
   var edge=EntityManager.GetComponentData<Edge>(owner);
   if(edge.m_Start!=node&&edge.m_End!=node)return false;
   if(!EntityManager.HasComponent<EdgeLane>(lane))return false;
   var delta=EntityManager.GetComponentData<EdgeLane>(lane).m_EdgeDelta;
   if(owner==road&&(delta.y>delta.x?edge.m_End:edge.m_Start)==node)return true;
   if((delta.y>delta.x?edge.m_Start:edge.m_End)!=node)return false;
   return PartLength(vehicle,out float length)&&position*EntityManager.GetComponentData<Curve>(lane).m_Length<=length*0.5f;
  }
  private bool StillCrossing(Entity vehicle,Entity road,Entity node)
  {
   if(!EntityManager.HasComponent<CarCurrentLane>(vehicle)||EntityManager.HasComponent<Deleted>(vehicle))return false;
   var current=EntityManager.GetComponentData<CarCurrentLane>(vehicle);
   if(PartInCrossing(vehicle,current.m_Lane,current.m_CurvePosition.x,road,node))return true;
   if(EntityManager.HasBuffer<LayoutElement>(vehicle))
    foreach(var part in EntityManager.GetBuffer<LayoutElement>(vehicle,true)){
     if(!EntityManager.HasComponent<CarTrailerLane>(part.m_Vehicle))continue;
     var trailer=EntityManager.GetComponentData<CarTrailerLane>(part.m_Vehicle);
     if(PartInCrossing(part.m_Vehicle,trailer.m_Lane,trailer.m_CurvePosition.x,road,node))return true;
    }
   return false;
  }
  private void ObserveJunction(Entity road,byte direction,DirectionMonitor state)
  {
   var edge=EntityManager.GetComponentData<Edge>(road);
   var node=direction==1?edge.m_Start:edge.m_End;
   if(EntityManager.HasBuffer<SubLane>(node))
    foreach(var sub in EntityManager.GetBuffer<SubLane>(node,true)){
     var lane=sub.m_SubLane;
     if(!EntityManager.HasComponent<CarLane>(lane)||!EntityManager.HasComponent<Game.Net.Lane>(lane)||
        !state.Entrances.Contains(EntityManager.GetComponentData<Game.Net.Lane>(lane).m_StartNode)||
        !EntityManager.HasBuffer<LaneObject>(lane))continue;
     foreach(var obj in EntityManager.GetBuffer<LaneObject>(lane,true)){
      var owner=ControllerOf(obj.m_LaneObject);
      var v=owner==Entity.Null?obj.m_LaneObject:owner;
      if(EntityManager.HasComponent<CarCurrentLane>(v)&&!state.Crossings.ContainsKey(v))
       state.Crossings.Add(v,new Crossing());
     }
    }
   finishedCrossings.Clear();
   foreach(var item in state.Crossings){
    var v=item.Key;var watch=item.Value;
    if(!StillCrossing(v,road,node)){
     if(watch.Reported)ReportCrossing(node,v,EntityManager.HasComponent<CarCurrentLane>(v)&&!EntityManager.HasComponent<Deleted>(v)?"已离开路口监测范围":"车辆已移除");
     finishedCrossings.Add(v);continue;
    }
    if(!LowSpeed(v,0.2f)){watch.StoppedSince=-1;continue;}
    if(watch.StoppedSince<0)watch.StoppedSince=m_ActiveSeconds;
    if(m_ActiveSeconds-watch.StoppedSince<2.5)continue;
    var blocker=EntityManager.HasComponent<Blocker>(v)?EntityManager.GetComponentData<Blocker>(v).m_Blocker:Entity.Null;
    if(watch.Reported&&watch.Blocker==blocker)continue;
    watch.Reported=true;watch.Blocker=blocker;
    ReportCrossing(node,v,blocker==Entity.Null?"停滞，暂无阻挡对象":$"停滞，阻挡对象 {blocker.Index}/{blocker.Version}");
   }
   foreach(var v in finishedCrossings)state.Crossings.Remove(v);
  }
  private void ReportCrossing(Entity node,Entity vehicle,string detail)
  {
   MonitorMessage=$"节点 #{node.Index} · 车辆 {vehicle.Index}/{vehicle.Version} {detail}";
   RoadLaneLock.Mod.Log.Info(MonitorMessage);
  }
  private void SampleMonitors()
  {
   if(monitors.Count==0||m_ActiveSeconds<nextMonitorSample)return;
   nextMonitorSample=m_ActiveSeconds+0.5;offers.Clear();retired.Clear();
   foreach(var item in monitors){
    var key=item.Key;var state=item.Value;
    if(!RoadLaneLock.RoadInspector.IsRoad(EntityManager,key.Road)||!EntityManager.HasBuffer<SubLane>(key.Road)){retired.Add(key);continue;}
    state.Entrances.Clear();Candidate head=default;float after=float.PositiveInfinity;
    foreach(var sub in EntityManager.GetBuffer<SubLane>(key.Road,true)){
     if(RoadLaneLock.RoadPickTool.LaneDirection(EntityManager,sub.m_SubLane)!=key.Direction)continue;
     bool queued=MeasureQueue(sub.m_SubLane,out var q);
     if(state.Observing&&sub.m_SubLane==state.Previous.Entry&&queued)after=q.Tail;
     var entry=sub.m_SubLane;
     if(EntityManager.HasComponent<Game.Net.Lane>(entry))state.Entrances.Add(EntityManager.GetComponentData<Game.Net.Lane>(entry).m_EndNode);
     var leader=ApproachLeader(entry);
     if(leader==Entity.Null)continue;
     bool spillback=queued&&q.Vehicle==leader&&q.Tail<=8f;
     q.Entry=entry;q.Vehicle=leader;
     if(!ReadTurn(ref q))continue;
     if(q.Turn==q.Landing&&(!spillback||!LowSpeed(leader,0.2f)))continue;
     if(!spillback)q.Tail=float.PositiveInfinity;
     if(head.Vehicle==Entity.Null||q.Tail<head.Tail)head=q;
    }
    ObserveJunction(key.Road,key.Direction,state);
    if(state.Observing){
     if(m_ActiveSeconds<state.ObserveUntil)continue;
     var prior=state.Previous;
     MonitorMessage=$"车辆 {prior.Vehicle.Index}/{prior.Vehicle.Version}：入口队列 {(float.IsInfinity(prior.Tail)?"未回堵":prior.Tail.ToString("F0")+" 米")} → "+(float.IsInfinity(after)?"连续车队已散开":$"{after:F0} 米");
     RoadLaneLock.Mod.Log.Info($"拥堵监测 路段 {key.Road.Index}/{key.Road.Version} 方向 {key.Direction} "+MonitorMessage);
     if(!float.IsInfinity(after)&&after>prior.Tail+2f){state.Previous.Tail=after;state.ObserveUntil=m_ActiveSeconds+4;continue;}
     state.Observing=false;state.Head=default;
    }
    if(head.Vehicle==Entity.Null||head.Vehicle!=state.Head.Vehicle||head.Turn!=state.Head.Turn||head.Entry!=state.Head.Entry||(head.Turn==head.Landing&&math.abs(head.Position-state.Head.Position)>1f))state.Since=m_ActiveSeconds;
    state.Head=head;
    if(head.Vehicle!=Entity.Null&&m_ActiveSeconds-state.Since>=(head.Turn==head.Landing?2.5:0.5)&&m_ActiveSeconds>=state.NextAttempt){
     if(offers.Count()==offers.Capacity)offers.Capacity*=2;offers.TryAdd(head.Vehicle,head);
    }
   }
   foreach(var key in retired)if(monitors.Remove(key))MonitorRevision++;
  }
  private void DrainMonitorResults(){
   while(monitorResults.TryDequeue(out var r)){
    var q=r.Candidate;
    if(!EntityManager.HasComponent<Owner>(q.Entry))continue;
    var key=(EntityManager.GetComponentData<Owner>(q.Entry).m_Owner,RoadLaneLock.RoadPickTool.LaneDirection(EntityManager,q.Entry));
    if(!monitors.TryGetValue(key,out var s))continue;
    offers.Remove(q.Vehicle);s.NextAttempt=m_ActiveSeconds+2.5;
    if(!r.Applied)continue;
    s.Previous=q;s.Observing=true;s.ObserveUntil=m_ActiveSeconds+4;
    MonitorMessage=$"车辆 {q.Vehicle.Index}/{q.Vehicle.Version} 绕行：出口余量 {q.Room:F1} 米，车长 {q.Size:F1} 米，{(float.IsInfinity(q.Tail)?"进弯前分流":$"队尾距入口 {q.Tail:F1} 米")}。";
    RoadLaneLock.Mod.Log.Info($"拥堵监测 路段 {key.Item1.Index}/{key.Item1.Version} 方向 {key.Item2} "+MonitorMessage);
   }
  }
 }
}

