using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Colossal.UI.Binding;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Tools;
using Game.Vehicles;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RoadLaneLock
{
 // 交通信息 page: vehicles in the city by kind, average road flow speed (the game's traffic flow
 // data), the game's own traffic bottlenecks, and the most congested places.
 public partial class RuleUISystem
 {
  sealed class TrafficSpot{public Entity Owner;public float3 Position;public string Name;public float Flow;public bool Bottleneck,Monitored;public int Queue;public string QueueParts;public Cause Cause;}
  EntityQuery movingCars,parkedCars,roadQuery,bottleneckQuery;
  EntityQuery[] kindQueries;
  static readonly string[] kindNames={"私家车","出租","货车","公交","市政/紧急","自行车"};
  object trafficCache;
  float trafficAt;
  readonly List<TrafficSpot> spots=new List<TrafficSpot>();
  readonly List<int> flowTrend=new List<int>(),bottleneckTrend=new List<int>();
  Entity hoverTraffic;

  void InitTrafficInfo()
  {
   var common=new[]{ComponentType.ReadOnly<Deleted>(),ComponentType.ReadOnly<Temp>()};
   EntityQuery Cars(params ComponentType[] with)
   {
    var all=new List<ComponentType>{ComponentType.ReadOnly<Car>(),ComponentType.ReadOnly<CarCurrentLane>()};all.AddRange(with);
    return GetEntityQuery(new EntityQueryDesc{All=all.ToArray(),None=common});
   }
   // Bicycles carry PersonalCar too: counted on their own.
   var noBike=new[]{ComponentType.ReadOnly<Deleted>(),ComponentType.ReadOnly<Temp>(),ComponentType.ReadOnly<Bicycle>()};
   movingCars=Cars();
   parkedCars=GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Car>(),ComponentType.ReadOnly<ParkedCar>()},None=common});
   kindQueries=new[]{
    GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Car>(),ComponentType.ReadOnly<CarCurrentLane>(),ComponentType.ReadOnly<PersonalCar>()},None=noBike}),Cars(ComponentType.ReadOnly<Taxi>()),Cars(ComponentType.ReadOnly<DeliveryTruck>()),Cars(ComponentType.ReadOnly<PublicTransport>()),
    GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Car>(),ComponentType.ReadOnly<CarCurrentLane>()},None=common,
     Any=new[]{ComponentType.ReadOnly<Ambulance>(),ComponentType.ReadOnly<FireEngine>(),ComponentType.ReadOnly<PoliceCar>(),ComponentType.ReadOnly<GarbageTruck>(),
      ComponentType.ReadOnly<PostVan>(),ComponentType.ReadOnly<MaintenanceVehicle>(),ComponentType.ReadOnly<Hearse>()}}),
    Cars(ComponentType.ReadOnly<Bicycle>())};
   InitTrafficLive();
   roadQuery=GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Road>(),ComponentType.ReadOnly<Edge>(),ComponentType.ReadOnly<Curve>()},None=common});
   bottleneckQuery=GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Bottleneck>(),ComponentType.ReadOnly<Owner>(),ComponentType.ReadOnly<Curve>()},None=common});
   // A row in 最堵的地方: hover highlights it on the map, click moves the camera there.
   AddBinding(new TriggerBinding<string>("RoadLaneLock","hoverTraffic",id=>{hoverTraffic=viewingVehicles&&TryParseEntity(id??"",out var e)?e:Entity.Null;}));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","focusTraffic",id=>{
    if(!viewingVehicles||!TryParseEntity(id??"",out var e))return;
    foreach(var s in spots)if(s.Owner==e){MoveCamera(s.Position);break;}
   }));
  }

  object TrafficInfo()
  {
   if(trafficCache!=null&&UnityEngine.Time.unscaledTime<trafficAt)return trafficCache;
   trafficAt=UnityEngine.Time.unscaledTime+5f;
   try{trafficCache=BuildTraffic();}catch(Exception ex){Mod.Log.Warn("交通信息统计失败："+ex.Message);}
   return trafficCache;
  }

  object BuildTraffic()
  {
   int moving=movingCars.CalculateEntityCount(),parked=parkedCars.CalculateEntityCount();
   var kinds=new List<object>();int counted=0;
   int motos=0;
   if(Hook!=null){var refs=kindQueries[0].ToComponentDataArray<Game.Prefabs.PrefabRef>(Allocator.Temp);foreach(var p in refs)if(Hook.IsMotoPrefab(p.m_Prefab))motos++;refs.Dispose();}
   for(int i=0;i<kindQueries.Length;i++)
   {
    int n=kindQueries[i].CalculateEntityCount();counted+=n;
    if(i==0)n-=motos;
    if(i==5)kinds.Add(new{Name="摩托车",Count=motos});
    kinds.Add(new{Name=kindNames[i],Count=n});
   }
   kinds.Add(new{Name="其他",Count=math.max(0,moving-counted)});

   // Average flow speed weighted by road length; roads without recorded traffic are skipped.
   var executor=runtime.Executor;
   var roads=roadQuery.ToEntityArray(Allocator.Temp);
   var flows=new List<(Entity road,float flow)>();
   double sum=0,weight=0;
   foreach(var road in roads)
   {
    var data=EntityManager.GetComponentData<Road>(road);
    var duration=data.m_TrafficFlowDuration0+data.m_TrafficFlowDuration1;
    if(math.csum(duration)<=0.01f)continue;
    float flow=math.csum(NetUtils.GetTrafficFlowSpeed(data))/4f;
    float length=EntityManager.GetComponentData<Curve>(road).m_Length;
    sum+=flow*length;weight+=length;
    if(flow<0.6f)flows.Add((road,flow));
   }
   roads.Dispose();
   SampleLive();
   int average=(int)math.round(liveFlow*100); // live: cars' speed against their lanes' limits (the game's flow lags by hours)

   // The game's bottlenecks (its red traffic-jam icon): lanes whose timer passed the icon threshold, one per owner.
   spots.Clear();
   var seen=new HashSet<Entity>();
   var lanes=bottleneckQuery.ToEntityArray(Allocator.Temp);
   foreach(var lane in lanes)
   {
    var b=EntityManager.GetComponentData<Bottleneck>(lane);
    if(b.m_Timer<20)continue;
    var owner=EntityManager.GetComponentData<Owner>(lane).m_Owner;
    var pos=MathUtils.Position(EntityManager.GetComponentData<Curve>(lane).m_Bezier,b.m_Position/255f);
    // A jam on a road right next to a junction belongs to that junction.
    var junction=NearJunction(owner,pos);
    if(junction!=Entity.Null){owner=junction;pos=EntityManager.GetComponentData<Node>(junction).m_Position;}
    if(!seen.Add(owner))continue;
    spots.Add(new TrafficSpot{Owner=owner,Position=pos,Name=PlaceName(owner),Flow=FlowOf(owner),Bottleneck=true});
   }
   lanes.Dispose();
   int bottlenecks=spots.Count;
   // Radar: each bottleneck by direction and distance from the map centre (north up), scaled to the farthest one.
   float far=1500f;foreach(var sp in spots)far=math.max(far,math.length(sp.Position.xz));far*=1.1f;
   var radar=spots.ConvertAll(sp=>(object)new{X=(float)math.round(sp.Position.x/far*100)/100,Y=(float)math.round(-sp.Position.z/far*100)/100,Name=sp.Name});
   // Then the slowest roads, one per street name.
   flows.Sort((a,b)=>a.flow.CompareTo(b.flow));
   var names=new HashSet<string>();foreach(var s in spots)names.Add(s.Name??"");
   foreach(var (road,flow) in flows)
   {
    if(spots.Count>=6)break;
    if(seen.Contains(road))continue;
    // A slow road whose standing cars queue toward a junction is reported as that junction.
    var junction=QueueJunction(road);
    var owner=junction!=Entity.Null?junction:road;
    if(junction!=Entity.Null&&seen.Contains(junction))continue;
    var name=PlaceName(owner);
    if(!names.Add(name??""))continue;
    seen.Add(owner);
    var at=junction!=Entity.Null?EntityManager.GetComponentData<Node>(junction).m_Position:MathUtils.Position(EntityManager.GetComponentData<Curve>(road).m_Bezier,.5f);
    spots.Add(new TrafficSpot{Owner=owner,Position=at,Name=name,Flow=flow});
   }
   spots.Sort((a,b)=>a.Bottleneck!=b.Bottleneck?(a.Bottleneck?-1:1):a.Flow.CompareTo(b.Flow));
   if(spots.Count>4)spots.RemoveRange(4,spots.Count-4);
   causes.Clear();
   var keep=new HashSet<Entity>();
   foreach(var s in spots){keep.Add(s.Owner);SampleUsage(s.Owner,s.Position);}
   PruneUsage(keep);
   foreach(var s in spots){s.Monitored=executor.Monitoring(s.Owner,1)||executor.Monitoring(s.Owner,2);s.Queue=Queue(s.Owner,out s.QueueParts);s.Cause=Diagnose(s.Owner);if(s.Cause.Text!=null)causes[s.Owner]=s.Cause;}

   RecordHistory(average,bottlenecks);
   int managed=0,red=0;
   foreach(var road in executor.MonitoredRoads)for(byte d=1;d<=2;d++){if(!executor.Monitoring(road,d))continue;managed++;if((Hook?.RoadState(road,d)??0)==1)red++;}
   flowTrend.Add(average);bottleneckTrend.Add(bottlenecks);
   if(flowTrend.Count>12){flowTrend.RemoveAt(0);bottleneckTrend.RemoveAt(0);}
   return new{Moving=moving,Parked=parked,Kinds=kinds,Flow=average,Bottlenecks=bottlenecks,LiveCars=liveCars,Standing=liveStanding,FlowHourAgo=FlowHoursAgo(1),Managed=managed,ManagedRed=red,History=HistoryRows(),Radar=radar,
    Spots=spots.ConvertAll(s=>(object)new{Id=s.Owner.Index+"/"+s.Owner.Version,s.Name,Flow=(int)math.round(s.Flow*100),s.Bottleneck,s.Monitored,s.Queue,s.QueueParts,Cause=s.Cause.Text,Suggest=s.Cause.Suggest,Junction=!EntityManager.HasComponent<Edge>(s.Owner),Usage=UsageOf(s.Owner)}),Effects=EffectRows(),
    FlowTrend=flowTrend.ToArray(),BottleneckTrend=bottleneckTrend.ToArray(),Jams=Hook?.JamCount??0};
  }

  float FlowOf(Entity owner)
  {
   // Live figure first (same as the map layer); the game's flow only where no car is on it now.
   if(roadLive.TryGetValue(owner,out var live)&&live.Cars>0)return live.Sum/live.Cars;
   if(EntityManager.HasComponent<Node>(owner)&&EntityManager.HasBuffer<ConnectedEdge>(owner))
   {
    float worst=2f;
    foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(owner,true))if(roadLive.TryGetValue(c.m_Edge,out var e)&&e.Cars>0)worst=math.min(worst,e.Sum/e.Cars);
    if(worst<=1f)return worst;
   }
   if(EntityManager.HasComponent<Road>(owner)){var d=EntityManager.GetComponentData<Road>(owner);if(math.csum(d.m_TrafficFlowDuration0+d.m_TrafficFlowDuration1)>0.01f)return math.csum(NetUtils.GetTrafficFlowSpeed(d))/4f;}
   // A junction: slowest of its roads.
   float flow=1f;
   if(EntityManager.HasBuffer<ConnectedEdge>(owner))
    foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(owner,true))
     if(EntityManager.HasComponent<Road>(c.m_Edge)){var d=EntityManager.GetComponentData<Road>(c.m_Edge);if(math.csum(d.m_TrafficFlowDuration0+d.m_TrafficFlowDuration1)>0.01f)flow=math.min(flow,math.csum(NetUtils.GetTrafficFlowSpeed(d))/4f);}
   return flow;
  }

  // Road name, or "A × B" for a junction.
  string PlaceName(Entity owner)
  {
   if(EntityManager.HasComponent<Edge>(owner))return RoadName(owner)??"未命名道路";
   var parts=new List<string>();
   if(EntityManager.HasBuffer<ConnectedEdge>(owner))
    foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(owner,true))
    {
     if(!RoadInspector.IsRoad(EntityManager,c.m_Edge))continue;
     var n=RoadName(c.m_Edge);
     if(!string.IsNullOrEmpty(n)&&!parts.Contains(n))parts.Add(n);
     if(parts.Count==2)break;
    }
   return parts.Count==0?"路口":parts.Count==1?parts[0]+"路口":parts[0]+"与"+parts[1]+"路口";
  }

  // The junction at the end of a road when a point on the road is within 40 m of it and it joins 3+ roads.
  Entity NearJunction(Entity road,float3 pos)
  {
   if(!EntityManager.HasComponent<Edge>(road))return Entity.Null;
   var edge=EntityManager.GetComponentData<Edge>(road);
   Entity best=Entity.Null;float bestDist=40f;
   foreach(var node in new[]{edge.m_Start,edge.m_End})
   {
    if(!EntityManager.HasComponent<Node>(node)||!EntityManager.HasBuffer<ConnectedEdge>(node))continue;
    int roads=0;foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(node,true))if(RoadInspector.IsRoad(EntityManager,c.m_Edge))roads++;
    if(roads<3)continue;
    float d=math.distance(pos.xz,EntityManager.GetComponentData<Node>(node).m_Position.xz);
    if(d<bestDist){bestDist=d;best=node;}
   }
   return best;
  }

  // Standing vehicles on the car lanes of a lane owner; only lanes ending near `toward` when given.
  int StandingOn(Entity owner,float3? toward=null)
  {
   if(!EntityManager.HasBuffer<Game.Net.SubLane>(owner))return 0;
   int n=0;
   foreach(var sub in EntityManager.GetBuffer<Game.Net.SubLane>(owner,true))
   {
    if(!EntityManager.HasComponent<Game.Net.CarLane>(sub.m_SubLane)||!EntityManager.HasBuffer<LaneObject>(sub.m_SubLane))continue;
    if(toward.HasValue)
    {
     var c=EntityManager.GetComponentData<Curve>(sub.m_SubLane).m_Bezier;
     if(math.distancesq(c.d.xz,toward.Value.xz)>=math.distancesq(c.a.xz,toward.Value.xz))continue;
    }
    foreach(var o in EntityManager.GetBuffer<LaneObject>(sub.m_SubLane,true))
    {
     var v=o.m_LaneObject;
     if(!EntityManager.HasComponent<CarCurrentLane>(v)||!EntityManager.HasComponent<Moving>(v))continue;
     if(math.lengthsq(EntityManager.GetComponentData<Moving>(v).m_Velocity)<1f)n++;
    }
   }
   return n;
  }

  // Queue at a place. A junction counts its own lanes plus every approach lane heading into it,
  // grouped by street name ("沙洲大道 4 / 越海路 1").
  int Queue(Entity owner,out string parts)
  {
   parts=null;
   if(EntityManager.HasComponent<Edge>(owner)||!EntityManager.HasComponent<Node>(owner))return StandingOn(owner);
   var at=EntityManager.GetComponentData<Node>(owner).m_Position;
   int total=StandingOn(owner);
   var byName=new List<KeyValuePair<string,int>>();
   if(EntityManager.HasBuffer<ConnectedEdge>(owner))
    foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(owner,true))
    {
     if(!RoadInspector.IsRoad(EntityManager,c.m_Edge))continue;
     int n=StandingOn(c.m_Edge,at);
     if(n==0)continue;
     total+=n;
     var name=RoadName(c.m_Edge)??"未命名道路";
     int i=byName.FindIndex(p=>p.Key==name);
     if(i<0)byName.Add(new KeyValuePair<string,int>(name,n));else byName[i]=new KeyValuePair<string,int>(name,byName[i].Value+n);
    }
   // Always break the queue down, even for one approach; cars inside the junction are listed too,
   // so the parts add up to the total.
   int inside=StandingOn(owner);
   if(total>0)
   {
    byName.Sort((a,b)=>b.Value.CompareTo(a.Value));
    var list=byName.ConvertAll(p=>p.Key+" "+p.Value);
    if(inside>0)list.Add(L.T("路口内")+" "+inside);
    parts=string.Join(" / ",list);
   }
   return total;
  }

  // The junction (3+ roads) at the end of a road that most of its standing cars queue toward.
  Entity QueueJunction(Entity road)
  {
   if(!EntityManager.HasComponent<Edge>(road))return Entity.Null;
   var edge=EntityManager.GetComponentData<Edge>(road);
   Entity best=Entity.Null;int most=0;
   foreach(var node in new[]{edge.m_Start,edge.m_End})
   {
    if(!EntityManager.HasComponent<Node>(node)||!EntityManager.HasBuffer<ConnectedEdge>(node))continue;
    int roads=0;foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(node,true))if(RoadInspector.IsRoad(EntityManager,c.m_Edge))roads++;
    if(roads<3)continue;
    int n=StandingOn(road,EntityManager.GetComponentData<Node>(node).m_Position);
    if(n>most){most=n;best=node;}
   }
   return best;
  }

  void DrawTrafficOverlay(Game.Rendering.OverlayRenderSystem.Buffer buffer)
  {
   if(hoverTraffic==Entity.Null||!EntityManager.Exists(hoverTraffic))return;
   var mint=new UnityEngine.Color32(112,212,196,230);
   if(EntityManager.HasComponent<Edge>(hoverTraffic)&&EntityManager.HasComponent<Curve>(hoverTraffic))
   {buffer.DrawCurve(mint,EntityManager.GetComponentData<Curve>(hoverTraffic).m_Bezier,4f);return;}
   foreach(var s in spots)if(s.Owner==hoverTraffic){buffer.DrawCircle(mint,new UnityEngine.Color32(112,212,196,50),0.8f,0,new float2(0,1),s.Position,24f);break;}
  }
 }
}
