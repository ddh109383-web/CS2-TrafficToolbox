using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Colossal.UI.Binding;
using Game.Net;
using Game.Vehicles;
using Unity.Entities;
using Unity.Mathematics;
using VFlags=Game.Vehicles.CarLaneFlags;
using NetFlags=Game.Net.CarLaneFlags;

namespace RoadLaneLock
{
 // 交通信息 additions:
 //  - Rule effects: when a rule is switched on or off, the place's flow and queue are noted; after two game
 //    minutes the same figures are sampled again (averaged while it is watched), so the page can show
 //    "开启后 流速 23%→31%，排队 14→6 辆".
 //  - Jam causes: for each congested place, the main reason among a few we can see (kerb parking
 //    manoeuvres, a bus standing in the road, turning cars queueing) and the rule that helps.
 public partial class RuleUISystem
 {
  sealed class RuleEffect
  {
   public Entity Owner;public string Name,Rule;public bool On;
   public uint Frame;public float FlowBefore;public int QueueBefore;
   public float FlowSum;public int QueueSum,Samples;public float NextSample;
   public readonly Queue<(float flow,int queue)> Recent=new Queue<(float,int)>(); // last samples: what it is like now
  }
  readonly List<RuleEffect> effects=new List<RuleEffect>();
  Game.Simulation.SimulationSystem simulationSystem;
  const uint SettleFrames=7200; // two game minutes

  // Called by every trigger that changes a rule.
  void NoteEffect(Entity owner,string rule,bool on)
  {
   if(owner==Entity.Null||!EntityManager.Exists(owner))return;
   simulationSystem??=World.GetOrCreateSystemManaged<Game.Simulation.SimulationSystem>();
   // One row per place and rule: segments of the same street count as one place.
   var name=PlaceName(owner);
   effects.RemoveAll(e=>(e.Owner==owner||e.Name==name)&&e.Rule==rule);
   effects.Insert(0,new RuleEffect{Owner=owner,Name=name,Rule=rule,On=on,Frame=simulationSystem.frameIndex,FlowBefore=LiveFlow(owner),QueueBefore=Queue(owner,out _)});
   if(effects.Count>6)effects.RemoveAt(effects.Count-1);
   AddMarker(name,rule,on);
  }

  void SampleEffects()
  {
   if(effects.Count==0)return;
   simulationSystem??=World.GetOrCreateSystemManaged<Game.Simulation.SimulationSystem>();
   float now=UnityEngine.Time.unscaledTime;
   foreach(var e in effects)
   {
    if(now<e.NextSample||simulationSystem.frameIndex-e.Frame<SettleFrames||e.Samples>=40)continue;
    e.NextSample=now+15f;
    if(!EntityManager.Exists(e.Owner))continue;
    float flow=LiveFlow(e.Owner);int queue=Queue(e.Owner,out _);
    e.FlowSum+=flow;e.QueueSum+=queue;e.Samples++;
    e.Recent.Enqueue((flow,queue));while(e.Recent.Count>4)e.Recent.Dequeue();
   }
  }

  object EffectRows()
  {
   var rows=new List<object>();
   foreach(var e in effects)
   {
    bool ready=e.Recent.Count>0;
    float flowNow=0;int queueNow=0;
    foreach(var x in e.Recent){flowNow+=x.flow;queueNow+=x.queue;}
    rows.Add(new{e.Name,Rule=L.T(e.Rule),e.On,Ready=ready,
     FlowBefore=(int)math.round(e.FlowBefore*100),QueueBefore=e.QueueBefore,
     FlowAfter=ready?(int)math.round(flowNow/e.Recent.Count*100):0,QueueAfter=ready?(int)math.round((float)queueNow/e.Recent.Count):0,Samples=e.Samples});
   }
   return rows;
  }

  // Live flow of a place for rule effects: average speed of the cars on it against their lanes' limits
  // (the game's own traffic flow is a slow average over hours). No cars = free flowing.
  float LiveFlow(Entity owner)
  {
   float sum=0;int count=0;
   void Scan(Entity laneOwner)
   {
    if(!EntityManager.HasBuffer<Game.Net.SubLane>(laneOwner))return;
    foreach(var sub in EntityManager.GetBuffer<Game.Net.SubLane>(laneOwner,true))
    {
     var lane=sub.m_SubLane;
     if(!EntityManager.HasComponent<Game.Net.CarLane>(lane)||!EntityManager.HasBuffer<LaneObject>(lane))continue;
     float limit=math.max(1f,EntityManager.GetComponentData<Game.Net.CarLane>(lane).m_SpeedLimit);
     foreach(var o in EntityManager.GetBuffer<LaneObject>(lane,true))
     {
      var v=o.m_LaneObject;
      if(!EntityManager.HasComponent<Game.Objects.Moving>(v)||!EntityManager.HasComponent<CarCurrentLane>(v))continue;
      if((EntityManager.GetComponentData<CarCurrentLane>(v).m_LaneFlags&(VFlags.ParkingSpace|VFlags.Connection))!=0)continue;
      sum+=math.saturate(math.length(EntityManager.GetComponentData<Game.Objects.Moving>(v).m_Velocity)/limit);count++;
     }
    }
   }
   Scan(owner);
   if(EntityManager.HasComponent<Node>(owner)&&EntityManager.HasBuffer<ConnectedEdge>(owner))
    foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(owner,true))if(RoadInspector.IsRoad(EntityManager,c.m_Edge))Scan(c.m_Edge);
   return count==0?1f:sum/count;
  }

  // ---- Jam causes ----
  struct Cause{public string Text,Suggest,Page,Tab;public Entity Road;}
  readonly Dictionary<Entity,Cause> causes=new Dictionary<Entity,Cause>();

  Cause Diagnose(Entity owner)
  {
   int standing=0,buses=0,parking=0,turners=0;
   Entity busiest=Entity.Null;int busiestCount=0;
   void Scan(Entity laneOwner,float3? toward)
   {
    if(!EntityManager.HasBuffer<Game.Net.SubLane>(laneOwner))return;
    int here=0;
    foreach(var sub in EntityManager.GetBuffer<Game.Net.SubLane>(laneOwner,true))
    {
     var lane=sub.m_SubLane;
     if(!EntityManager.HasComponent<Game.Net.CarLane>(lane)||!EntityManager.HasBuffer<LaneObject>(lane))continue;
     if(toward.HasValue){var c=EntityManager.GetComponentData<Curve>(lane).m_Bezier;if(math.distancesq(c.d.xz,toward.Value.xz)>=math.distancesq(c.a.xz,toward.Value.xz))continue;}
     foreach(var o in EntityManager.GetBuffer<LaneObject>(lane,true))
     {
      var v=o.m_LaneObject;
      if(!EntityManager.HasComponent<CarCurrentLane>(v))continue;
      var current=EntityManager.GetComponentData<CarCurrentLane>(v);
      if((current.m_LaneFlags&(VFlags.ParkingSpace|VFlags.Connection))!=0){parking++;continue;}
      if(!EntityManager.HasComponent<Game.Objects.Moving>(v)||math.lengthsq(EntityManager.GetComponentData<Game.Objects.Moving>(v).m_Velocity)>=1f)continue;
      standing++;here++;
      if(EntityManager.HasComponent<PublicTransport>(v))buses++;
      if(EntityManager.HasBuffer<CarNavigationLane>(v))
       foreach(var n in EntityManager.GetBuffer<CarNavigationLane>(v,true))
       {
        if(EntityManager.HasComponent<EdgeLane>(n.m_Lane)||!EntityManager.HasComponent<Game.Net.CarLane>(n.m_Lane))continue;
        if((EntityManager.GetComponentData<Game.Net.CarLane>(n.m_Lane).m_Flags&(NetFlags.TurnLeft|NetFlags.UTurnLeft|NetFlags.TurnRight|NetFlags.UTurnRight))!=0)turners++;
        break;
       }
     }
    }
    if(here>busiestCount&&EntityManager.HasComponent<Edge>(laneOwner)){busiestCount=here;busiest=laneOwner;}
   }
   if(EntityManager.HasComponent<Edge>(owner))Scan(owner,null);
   else if(EntityManager.HasComponent<Node>(owner))
   {
    var at=EntityManager.GetComponentData<Node>(owner).m_Position;
    Scan(owner,null);
    if(EntityManager.HasBuffer<ConnectedEdge>(owner))
     foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(owner,true))if(RoadInspector.IsRoad(EntityManager,c.m_Edge))Scan(c.m_Edge,at);
   }
   var road=EntityManager.HasComponent<Edge>(owner)?owner:busiest;
   if(parking>=2&&road!=Entity.Null&&!parking_Contains(road))
    return new Cause{Text=L.T("路边停车进出挡道"),Suggest=L.T("这段路禁止路边停车"),Page="parking",Road=road};
   if(buses>=1&&standing>=4)
    return new Cause{Text=L.T("公交在路中停靠挡道"),Suggest=L.T("车站前后路段禁止变道，后车提前避开停靠车道"),Page="lane",Tab="lane",Road=road};
   if(turners>=3&&turners*2>=standing)
    return new Cause{Text=L.T("转弯车排队占住直行车道"),Suggest=L.T("进口道禁止变道，转弯车提前排进转弯车道"),Page="lane",Tab="lane",Road=road};
   return default;
  }
  bool parking_Contains(Entity road)=>parking.Contains(road);

  void InitTrafficInsights()
  {
   // "去设置 ›" on a cause: open the suggested page with that road selected.
   AddBinding(new TriggerBinding<string>("RoadLaneLock","applyCause",id=>{
    if(!viewingVehicles||!TryParseEntity(id??"",out var owner)||!causes.TryGetValue(owner,out var cause)||cause.Road==Entity.Null||!RoadInspector.IsRoad(EntityManager,cause.Road))return;
    var center=MathUtils.Position(EntityManager.GetComponentData<Curve>(cause.Road).m_Bezier,.5f);
    viewingVehicles=false;monitorOpen=false;transitOpen=false;
    parkingOpen=cause.Page=="parking";
    if(cause.Tab!=null)laneTab=cause.Tab;
    picker.Begin();picker.Select(cause.Road,center);MoveCamera(center);
    Publish();
   }));
  }
 }
}
