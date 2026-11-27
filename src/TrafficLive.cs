using System.Collections.Generic;
using Colossal.Mathematics;
using Colossal.UI.Binding;
using Game.Common;
using Game.Net;
using Game.Objects;
using PrefabRef=Game.Prefabs.PrefabRef;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VFlags=Game.Vehicles.CarLaneFlags;

namespace RoadLaneLock
{
 // 交通信息, our own live figures (the game's traffic flow is a slow average over hours):
 //  - every 5 s one pass over the cars on the road: speed against the lane's limit, per road and city-wide,
 //    and how many stand still. Drives the gauges and the congestion map layer (greener = flowing).
 //  - history: average flow and jam count per 5 game minutes for the last game day, with the moments
 //    rules were switched (the trend chart).
 //  - who uses the most congested places: vehicles seen there in the last 10 game minutes by kind,
 //    and whether their destination is nearby or they only pass through.
 public partial class RuleUISystem
 {
  struct RoadLive{public float Sum;public int Cars,Standing;}
  readonly Dictionary<Entity,RoadLive> roadLive=new Dictionary<Entity,RoadLive>();
  struct HeatSeg{public Bezier4x3 Curve;public UnityEngine.Color32 Color;public float Width;}
  readonly List<HeatSeg> heat=new List<HeatSeg>();
  int liveCars,liveStanding;float liveFlow=1f;

  sealed class Bucket{public long Index;public float FlowSum;public int Samples,Jams;}
  readonly List<Bucket> history=new List<Bucket>();
  struct Marker{public uint Frame;public string Text;}
  readonly List<Marker> markers=new List<Marker>();
  static uint TicksPerDay=>(uint)Game.Simulation.TimeSystem.kTicksPerDay;
  static uint BucketTicks=>TicksPerDay/288; // 5 game minutes

  sealed class Seen{public byte Kind;public bool Near,Known;public uint Frame;}
  readonly Dictionary<Entity,Dictionary<Entity,Seen>> usage=new Dictionary<Entity,Dictionary<Entity,Seen>>();
  static readonly string[] usageKinds={"私家车","出租","货车","公交","市政/紧急","摩托车","自行车","其他"};
  const float NearRange=400f;

  uint SimFrame{get{simulationSystem??=World.GetOrCreateSystemManaged<Game.Simulation.SimulationSystem>();return simulationSystem.frameIndex;}}

  // One pass over the cars currently on lanes.
  void SampleLive()
  {
   roadLive.Clear();liveCars=0;liveStanding=0;float sum=0;
   var cars=movingCars.ToEntityArray(Allocator.Temp);
   foreach(var v in cars)
   {
    if(!EntityManager.HasComponent<Moving>(v))continue;
    var current=EntityManager.GetComponentData<CarCurrentLane>(v);
    if((current.m_LaneFlags&(VFlags.ParkingSpace|VFlags.Connection))!=0)continue;
    var lane=current.m_Lane;
    if(!EntityManager.HasComponent<Game.Net.CarLane>(lane)||!EntityManager.HasComponent<Owner>(lane))continue;
    float limit=math.max(1f,EntityManager.GetComponentData<Game.Net.CarLane>(lane).m_SpeedLimit);
    float speed=math.length(EntityManager.GetComponentData<Moving>(v).m_Velocity);
    float ratio=math.saturate(speed/limit);bool standing=speed<1f;
    sum+=ratio;liveCars++;if(standing)liveStanding++;
    var owner=EntityManager.GetComponentData<Owner>(lane).m_Owner;
    roadLive.TryGetValue(owner,out var r);r.Sum+=ratio;r.Cars++;if(standing)r.Standing++;roadLive[owner]=r;
   }
   cars.Dispose();
   liveFlow=liveCars==0?1f:sum/liveCars;
   // Map layer: roads with traffic, coloured by flow; a road whose cars mostly stand counts as jammed.
   heat.Clear();
   foreach(var pair in roadLive)
   {
    var road=pair.Key;var r=pair.Value;
    if(r.Cars<2||!EntityManager.HasComponent<Edge>(road)||!EntityManager.HasComponent<Curve>(road)||!RoadInspector.IsRoad(EntityManager,road))continue;
    float flow=r.Sum/r.Cars;
    if(r.Standing*2>r.Cars&&r.Standing>=4)flow=math.min(flow,.2f);
    UnityEngine.Color32 color=flow>=.7f?new UnityEngine.Color32(110,220,170,120):flow>=.45f?new UnityEngine.Color32(240,205,90,170):flow>=.25f?new UnityEngine.Color32(240,150,70,200):new UnityEngine.Color32(235,80,70,225);
    heat.Add(new HeatSeg{Curve=EntityManager.GetComponentData<Curve>(road).m_Bezier,Color=color,Width=flow<.45f?7f:4.5f});
   }
  }

  float LiveFlowOfRoad(Entity road)=>roadLive.TryGetValue(road,out var r)&&r.Cars>0?r.Sum/r.Cars:1f;

  void DrawHeatOverlay(Game.Rendering.OverlayRenderSystem.Buffer buffer)
  {
   foreach(var s in heat)buffer.DrawCurve(s.Color,s.Curve,s.Width);
  }

  void RecordHistory(int flow,int jams)
  {
   uint frame=SimFrame;long index=frame/BucketTicks;
   var last=history.Count>0?history[history.Count-1]:null;
   if(last==null||last.Index!=index){last=new Bucket{Index=index};history.Add(last);}
   last.FlowSum+=flow;last.Samples++;last.Jams=math.max(last.Jams,jams);
   while(history.Count>0&&index-history[0].Index>288)history.RemoveAt(0);
   markers.RemoveAll(m=>frame-m.Frame>TicksPerDay);
  }

  void AddMarker(string place,string rule,bool on)
  {
   markers.Add(new Marker{Frame=SimFrame,Text=place+" · "+L.T(rule)+" "+(on?L.T("开"):L.T("关"))});
   if(markers.Count>40)markers.RemoveAt(0);
  }

  int FlowHoursAgo(float hours)
  {
   long want=(SimFrame-(long)(TicksPerDay*hours/24f))/BucketTicks;
   foreach(var b in history)if(b.Index>=want&&b.Samples>0)return (int)math.round(b.FlowSum/b.Samples);
   return -1;
  }

  object HistoryRows()
  {
   uint frame=SimFrame;
   var points=history.ConvertAll(b=>(object)new{H=-(float)((frame/BucketTicks)-b.Index)*24f/288f,F=b.Samples>0?(int)math.round(b.FlowSum/b.Samples):100,J=b.Jams});
   var marks=markers.ConvertAll(m=>(object)new{H=-(float)(frame-m.Frame)*24f/TicksPerDay,m.Text});
   return new{Points=points,Markers=marks};
  }

  byte KindOf(Entity v)
  {
   if(EntityManager.HasComponent<Bicycle>(v))return 6;
   if(EntityManager.HasComponent<Taxi>(v))return 1;
   if(EntityManager.HasComponent<PublicTransport>(v))return 3;
   if(EntityManager.HasComponent<Ambulance>(v)||EntityManager.HasComponent<FireEngine>(v)||EntityManager.HasComponent<PoliceCar>(v)||EntityManager.HasComponent<GarbageTruck>(v)||
      EntityManager.HasComponent<PostVan>(v)||EntityManager.HasComponent<MaintenanceVehicle>(v)||EntityManager.HasComponent<Hearse>(v))return 4;
   if(EntityManager.HasComponent<DeliveryTruck>(v))return 2;
   if(EntityManager.HasComponent<PersonalCar>(v))
    return EntityManager.HasComponent<PrefabRef>(v)&&(Hook?.IsMotoPrefab(EntityManager.GetComponentData<PrefabRef>(v).m_Prefab)??false)?(byte)5:(byte)0;
   return 7;
  }

  // Destination near the place (within 400 m), far, or unknown.
  bool Destination(Entity v,float3 at,out bool near)
  {
   near=false;
   if(!EntityManager.HasComponent<Target>(v))return false;
   var target=EntityManager.GetComponentData<Target>(v).m_Target;
   if(target==Entity.Null||!EntityManager.Exists(target))return false;
   float3 pos;
   if(EntityManager.HasComponent<Game.Objects.Transform>(target))pos=EntityManager.GetComponentData<Game.Objects.Transform>(target).m_Position;
   else if(EntityManager.HasComponent<Curve>(target))pos=MathUtils.Position(EntityManager.GetComponentData<Curve>(target).m_Bezier,.5f);
   else if(EntityManager.HasComponent<Node>(target))pos=EntityManager.GetComponentData<Node>(target).m_Position;
   else return false;
   near=math.distance(pos.xz,at.xz)<=NearRange;return true;
  }

  // Note the vehicles on a place's car lanes (a junction: its own lanes and the lanes heading into it).
  void SampleUsage(Entity owner,float3 at)
  {
   if(!usage.TryGetValue(owner,out var seen))usage[owner]=seen=new Dictionary<Entity,Seen>();
   uint frame=SimFrame;
   void Scan(Entity laneOwner,float3? toward)
   {
    if(!EntityManager.HasBuffer<Game.Net.SubLane>(laneOwner))return;
    foreach(var sub in EntityManager.GetBuffer<Game.Net.SubLane>(laneOwner,true))
    {
     var lane=sub.m_SubLane;
     if(!EntityManager.HasComponent<Game.Net.CarLane>(lane)||!EntityManager.HasBuffer<LaneObject>(lane))continue;
     if(toward.HasValue){var c=EntityManager.GetComponentData<Curve>(lane).m_Bezier;if(math.distancesq(c.d.xz,toward.Value.xz)>=math.distancesq(c.a.xz,toward.Value.xz))continue;}
     foreach(var o in EntityManager.GetBuffer<LaneObject>(lane,true))
     {
      var v=o.m_LaneObject;
      if(!EntityManager.HasComponent<CarCurrentLane>(v))continue;
      if(seen.TryGetValue(v,out var s)){s.Frame=frame;continue;}
      s=new Seen{Kind=KindOf(v),Frame=frame};s.Known=Destination(v,at,out s.Near);
      seen[v]=s;
     }
    }
   }
   Scan(owner,null);
   if(EntityManager.HasComponent<Node>(owner)&&EntityManager.HasBuffer<ConnectedEdge>(owner))
    foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(owner,true))if(RoadInspector.IsRoad(EntityManager,c.m_Edge))Scan(c.m_Edge,at);
   uint window=TicksPerDay/144; // 10 game minutes
   var old=new List<Entity>();
   foreach(var p in seen)if(frame-p.Value.Frame>window)old.Add(p.Key);
   foreach(var v in old)seen.Remove(v);
  }

  void PruneUsage(HashSet<Entity> keep)
  {
   var drop=new List<Entity>();
   foreach(var k in usage.Keys)if(!keep.Contains(k))drop.Add(k);
   foreach(var k in drop)usage.Remove(k);
  }

  object UsageOf(Entity owner)
  {
   if(!usage.TryGetValue(owner,out var seen)||seen.Count==0)return null;
   var counts=new int[usageKinds.Length];int known=0,near=0,trucks=0,trucksFar=0;
   foreach(var s in seen.Values)
   {
    counts[s.Kind]++;
    if(s.Known){known++;if(s.Near)near++;}
    if(s.Kind==2&&s.Known){trucks++;if(!s.Near)trucksFar++;}
   }
   int total=seen.Count;
   var kinds=new List<object>();
   for(int i=0;i<counts.Length;i++)if(counts[i]>0)kinds.Add(new{I=i,Name=L.T(usageKinds[i]),Pct=(int)math.round(counts[i]*100f/total)});
   int nearPct=known>0?(int)math.round(near*100f/known):0;
   int farTruckPct=trucks>0?(int)math.round(trucksFar*100f/trucks):0;
   string suggest=null;
   bool road=EntityManager.HasComponent<Edge>(owner);
   if(road&&trucks>=5&&counts[2]*100>=15*total&&farTruckPct>=50)suggest=L.F("路过的货车占这条路货车的 {0}% · 建议：分道禁货车，让它们走干道",farTruckPct);
   return new{Total=total,Kinds=kinds,Known=known,Near=nearPct,Far=known>0?100-nearPct:0,FarTrucks=farTruckPct,Suggest=suggest,CanApply=suggest!=null};
  }

  void InitTrafficLive()
  {
   // "去设置 ›" on a usage suggestion: the 分道 page with that road selected.
   AddBinding(new TriggerBinding<string>("RoadLaneLock","applyUsage",id=>{
    if(!viewingVehicles||!TryParseEntity(id??"",out var road)||!RoadInspector.IsRoad(EntityManager,road))return;
    var center=MathUtils.Position(EntityManager.GetComponentData<Curve>(road).m_Bezier,.5f);
    viewingVehicles=false;monitorOpen=false;transitOpen=false;parkingOpen=false;laneTab="truck";
    picker.Begin();picker.Select(road,center);MoveCamera(center);
    Publish();
   }));
  }
 }
}
