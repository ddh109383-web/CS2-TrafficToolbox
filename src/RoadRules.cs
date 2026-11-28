using System.Collections.Generic;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Pathfind;
using Unity.Entities;
using Unity.Mathematics;
using SubLane=Game.Net.SubLane;
using NetFlags=Game.Net.CarLaneFlags;
using Edge=Game.Net.Edge;

namespace RoadLaneLock
{
 // Lane rules beyond lane-change control (车道规则 page) and junction rules (路口规则 page).
 // Chosen roads/nodes are saved as mod system data. Every lane change is withdrawn before the city is
 // saved and put back afterwards, so the save never carries modified lanes (same as kerb parking bans).
 //  - Speed limit: CarLane.m_SpeedLimit of the road's car lanes (vehicles and pathfinding both read it).
 //  - Lane bans (分道): per lane, the vehicle kinds that may not use it. Bicycles: the game's own
 //    ForbidBicycles lane flag. Cars, taxis, buses, trucks, service vehicles: the lane hook (LaneHookSystem).
 //  - Double yellow: building access lanes that cross the centre line get the game's Forbidden flag
 //    (no left turn in or out across oncoming traffic, no mid-block U-turn through a driveway), and plain
 //    joints at its ends (2 roads) ban pedestrians crossing.
 //  - No pedestrian crossing at a node: crossings (marked or not) get an access restriction, so the
 //    pathfinder treats them as closed and people walk around (bridge, another junction).
 public sealed class RoadRules:GameSystemBase,ISerializable,IDefaultSerializable
 {
  internal readonly Dictionary<Entity,int> Speed=new Dictionary<Entity,int>();          // road -> km/h
  internal readonly HashSet<Entity> NoBikes=new HashSet<Entity>();                       // old saves only: turned into bicycle bans on every lane
  // 分道: vehicle kinds banned per lane. road -> (direction*16 + index from the kerb) -> banned kinds.
  internal readonly Dictionary<Entity,Dictionary<int,byte>> LaneBans=new Dictionary<Entity,Dictionary<int,byte>>();
  internal readonly HashSet<Entity> DoubleYellow=new HashSet<Entity>();
  internal readonly HashSet<Entity> NoCrossing=new HashSet<Entity>();                    // nodes
  internal int Revision{get;private set;}
  internal bool Suspended;

  // Vehicle kinds of the 分道 rule. Bicycles use the game's own lane flag; the others the lane hook.
  // Whole-direction bans: bicycles through the game's ForbidBicycles flag; private cars through the game's
  // bus-lane flag (PublicOnly = the pathfinder's ForbidPrivateTraffic), which keeps out trucks and motorbikes
  // too, so a whole-direction car ban takes them along. Other kinds always keep one lane per direction.
  internal const byte KCar=1,KTaxi=2,KBus=4,KTruck=8,KService=16,KBike=32,KMoto=64,KHook=KCar|KTaxi|KBus|KTruck|KService|KMoto;
  internal static readonly byte[] Kinds={KCar,KTaxi,KBus,KTruck,KService,KMoto,KBike};
  internal static readonly string[] KindNames={"私家车","出租车","公交","货车","市政车","摩托车","自行车"};
  internal static string KindName(byte kind){int i=System.Array.IndexOf(Kinds,kind);return i<0?"":KindNames[i];}
  internal static RoadRules Current;

  readonly Dictionary<Entity,float> speedOwned=new Dictionary<Entity,float>();   // lane -> original limit
  readonly HashSet<Entity> bikeOwned=new HashSet<Entity>(),forbidOwned=new HashSet<Entity>(),publicOwned=new HashSet<Entity>();
  readonly Dictionary<Entity,Entity> crossingOwned=new Dictionary<Entity,Entity>(); // lane -> original restriction
  readonly List<Entity> scratch=new List<Entity>();
  float nextApply;int applied=-1;string lastSummary;

  void Changed(){Revision++;nextApply=0;}
  protected override void OnCreate(){base.OnCreate();Current=this;}

  internal void SetSpeed(Entity road,int kmh){if(kmh<=0){if(Speed.Remove(road))Changed();}else{Speed[road]=kmh;Changed();}Mod.Log.Info("限速 "+road+": "+kmh);}
  internal void SetDoubleYellow(Entity road,bool on){if(on?DoubleYellow.Add(road):DoubleYellow.Remove(road))Changed();}
  internal void SetNoCrossing(Entity node,bool on){if(on?NoCrossing.Add(node):NoCrossing.Remove(node))Changed();}
  internal byte Banned(Entity road,byte direction,int index)=>LaneBans.TryGetValue(road,out var map)&&map.TryGetValue(direction*16+index,out var m)?m:(byte)0;
  // Returns null when done, or why not: every direction keeps at least one lane for each motor vehicle kind.
  internal string SetLaneBan(Entity road,byte direction,int index,byte kind,bool banned)
  {
   if(System.Array.IndexOf(Kinds,kind)<0||index<0||index>15)return "无效";
   int lanes=LaneGroups(road,direction).Count;
   if(index>=lanes)return "无效";
   bool wholeCar=true;
   for(int i=0;i<lanes;i++)if(i!=index&&(Banned(road,direction,i)&KCar)==0)wholeCar=false;
   // Cars and bicycles may be banned from the whole direction (the pathfinder then avoids it); motorbikes and
   // trucks only when cars are (the same game flag covers them); the others keep at least one lane.
   if(banned&&kind!=KBike&&kind!=KCar&&!((kind==KMoto||kind==KTruck)&&wholeCar))
   {
    int open=0;
    for(int i=0;i<lanes;i++)if(i!=index&&(Banned(road,direction,i)&kind)==0)open++;
    if(open==0)return "每个方向至少留一条车道给"+KindName(kind);
   }
   if(!LaneBans.TryGetValue(road,out var map)){if(!banned)return null;LaneBans[road]=map=new Dictionary<int,byte>();}
   int code=direction*16+index;
   map.TryGetValue(code,out var old);
   byte now=banned?(byte)(old|kind):(byte)(old&~kind);
   if(now==old)return null;
   if(now==0)map.Remove(code);else map[code]=now;
   if(map.Count==0)LaneBans.Remove(road);
   Changed();
   // The last lane of a direction losing cars: the whole direction is closed to private traffic, which in the
   // game also means trucks and motorbikes. Mark them too, so the lanes show what really applies.
   if(banned&&kind==KCar&&wholeCar)
   {
    for(int i=0;i<lanes;i++)SetLaneBan(road,direction,i,KTruck,true);
    for(int i=0;i<lanes;i++)SetLaneBan(road,direction,i,KMoto,true);
    return "整段禁私家车";
   }
   return null;
  }
  // Kinds banned from every lane of a direction (whole-direction bans).
  internal byte WholeBan(Entity road,byte direction)
  {
   int lanes=LaneGroups(road,direction).Count;
   if(lanes==0)return 0;
   byte all=0xFF;
   for(int i=0;i<lanes;i++)all&=Banned(road,direction,i);
   return all;
  }
  internal bool Any(Entity road)=>Speed.ContainsKey(road)||LaneBans.ContainsKey(road)||DoubleYellow.Contains(road);

  bool LeftHand=>World.GetOrCreateSystemManaged<Game.City.CityConfigurationSystem>().leftHandTraffic;
  // Physical car lanes of one driving direction, kerb side first. A long road can carry its lanes in two
  // halves (split at a building connection): pieces at the same distance from the centre are one lane.
  internal List<List<Entity>> LaneGroups(Entity road,byte direction)=>LaneGroups(EntityManager,road,direction,LeftHand);
  internal static List<List<Entity>> LaneGroups(EntityManager em,Entity road,byte direction,bool leftHand)
  {
   var pieces=new List<(Entity lane,float side)>();
   var result=new List<List<Entity>>();
   if(!em.HasBuffer<SubLane>(road)||!em.HasComponent<Curve>(road))return result;
   var axis=em.GetComponentData<Curve>(road).m_Bezier;
   var ours=Current?.publicOwned;
   foreach(var sub in em.GetBuffer<SubLane>(road,true))
   {
    var lane=sub.m_SubLane;
    if(RoadPickTool.LaneDirection(em,lane)!=direction)continue;
    // The game's own dedicated lanes are not ours to split: bicycle lanes and bus lanes.
    if(em.HasComponent<Game.Prefabs.PrefabRef>(lane))
    {
     var prefab=em.GetComponentData<Game.Prefabs.PrefabRef>(lane).m_Prefab;
     if(em.HasComponent<Game.Prefabs.CarLaneData>(prefab)&&(em.GetComponentData<Game.Prefabs.CarLaneData>(prefab).m_RoadTypes&Game.Net.RoadTypes.Car)==0)continue;
    }
    if((em.GetComponentData<CarLane>(lane).m_Flags&NetFlags.PublicOnly)!=0&&(ours==null||!ours.Contains(lane)))continue;
    var curve=em.GetComponentData<Curve>(lane).m_Bezier;
    var mid=MathUtils.Position(curve,.5f);
    var forward=math.normalizesafe(curve.d-curve.a);
    MathUtils.Distance(axis.xz,mid.xz,out float t);
    var centre=MathUtils.Position(axis,t);
    var tangent=math.normalizesafe(MathUtils.Tangent(axis,t));
    float side=math.dot(mid.xz-centre.xz,new float2(tangent.z,-tangent.x));
    if(math.dot(forward.xz,tangent.xz)<0)side=-side;  // measured to the driving direction's right
    pieces.Add((lane,side));
   }
   pieces.Sort((a,b)=>leftHand?a.side.CompareTo(b.side):b.side.CompareTo(a.side));
   float last=float.NaN;
   foreach(var p in pieces)
   {
    if(result.Count>0&&math.abs(p.side-last)<1.2f)result[result.Count-1].Add(p.lane);
    else result.Add(new List<Entity>{p.lane});
    last=p.side;
   }
   return result;
  }
  internal static int LaneCount(EntityManager em,Entity road)=>LaneGroups(em,road,1,false).Count+LaneGroups(em,road,2,false).Count;

  // Lanes each hook kind may not use (lane -> kinds) and per road the directions with bans
  // (bit kind*2 + direction-1). A kind is never banned from every lane of a direction.
  internal void CopyLaneBans(Dictionary<Entity,byte> lanes,Dictionary<Entity,ushort> roads)
  {
   lanes.Clear();roads.Clear();
   foreach(var pair in LaneBans)
   {
    if(!RoadInspector.IsRoad(EntityManager,pair.Key))continue;
    for(byte d=1;d<=2;d++)
    {
     var groups=LaneGroups(pair.Key,d);
     if(groups.Count==0)continue;
     byte all=KHook;
     for(int i=0;i<groups.Count;i++)all&=Banned(pair.Key,d,i);
     ushort roadBits=0;
     for(int i=0;i<groups.Count;i++)
     {
      byte m=(byte)(Banned(pair.Key,d,i)&KHook&~all);
      if(m==0)continue;
      foreach(var lane in groups[i]){lanes.TryGetValue(lane,out var had);lanes[lane]=(byte)(had|m);}
      for(int k=0;k<7;k++)if((m&(1<<k))!=0)roadBits|=(ushort)(1<<(k*2+d-1));
     }
     if(roadBits!=0){roads.TryGetValue(pair.Key,out var had);roads[pair.Key]=(ushort)(had|roadBits);}
    }
   }
  }

  // The road's own speed limit (km/h): what its lanes drive at now, or before our limit if one is set.
  // Lanes store the road prefab's RoadData.m_SpeedLimit (via RoadComposition), which is twice the shown
  // speed: km/h = m/s / 2 * 3.6 (same as the build menu). Our limits are written back the same way.
  internal const float KmhPerUnit=1.8f;
  internal int DefaultSpeed(Entity road)
  {
   float best=0;
   for(byte d=1;d<=2;d++)foreach(var g in LaneGroups(road,d))foreach(var lane in g)
    best=math.max(best,speedOwned.TryGetValue(lane,out var original)?original:EntityManager.GetComponentData<CarLane>(lane).m_SpeedLimit);
   return (int)math.round(best*KmhPerUnit);
  }

  // Plain joints (exactly two roads) at the ends of double-yellow roads.
  internal void CrossingNodes(HashSet<Entity> nodes)
  {
   nodes.Clear();nodes.UnionWith(NoCrossing);
   foreach(var road in DoubleYellow)
   {
    if(!EntityManager.HasComponent<Edge>(road))continue;
    var edge=EntityManager.GetComponentData<Edge>(road);
    foreach(var node in new[]{edge.m_Start,edge.m_End})
     if(EntityManager.HasBuffer<ConnectedEdge>(node)&&EntityManager.GetBuffer<ConnectedEdge>(node,true).Length==2)nodes.Add(node);
   }
  }

  void Dirty(Entity lane)
  {
   if(!EntityManager.HasComponent<Updated>(lane)&&!EntityManager.HasComponent<PathfindUpdated>(lane)&&!EntityManager.HasComponent<Created>(lane))EntityManager.AddComponent<PathfindUpdated>(lane);
  }

  bool Usable(Entity e)=>EntityManager.Exists(e)&&!EntityManager.HasComponent<Deleted>(e)&&!EntityManager.HasComponent<Game.Tools.Temp>(e);

  // Side connection lanes of a road that cross its centre line.
  void CrossingAccessLanes(Entity road,List<Entity> output)
  {
   output.Clear();
   if(!EntityManager.HasBuffer<SubLane>(road)||!EntityManager.HasComponent<Curve>(road))return;
   var axis=EntityManager.GetComponentData<Curve>(road).m_Bezier;
   float Side(float3 p){MathUtils.Distance(axis.xz,p.xz,out float t);var c=MathUtils.Position(axis,t);var f=math.normalizesafe(MathUtils.Tangent(axis,t));return math.dot(p.xz-c.xz,new float2(f.z,-f.x));}
   foreach(var sub in EntityManager.GetBuffer<SubLane>(road,true))
   {
    var lane=sub.m_SubLane;
    if(!EntityManager.HasComponent<CarLane>(lane)||!EntityManager.HasComponent<Curve>(lane))continue;
    if((EntityManager.GetComponentData<CarLane>(lane).m_Flags&NetFlags.SideConnection)==0)continue;
    var c=EntityManager.GetComponentData<Curve>(lane).m_Bezier;
    float a=Side(c.a),d=Side(c.d);
    if(a*d<0&&math.abs(a)>.5f&&math.abs(d)>.5f)output.Add(lane);
   }
  }

  // Driveways: a parking lot or building road (an edge owned by a building) joining a double-yellow
  // road at a node. The node's car lanes that cross the centre line (left out, left in, U-turn)
  // are closed; right in / right out stay. Junctions with ordinary streets are left alone.
  void DrivewayCrossingLanes(Entity road,List<Entity> output)
  {
   output.Clear();
   if(!EntityManager.HasComponent<Edge>(road)||!EntityManager.HasComponent<Curve>(road))return;
   var axis=EntityManager.GetComponentData<Curve>(road).m_Bezier;
   float Side(float3 p){MathUtils.Distance(axis.xz,p.xz,out float t);var c=MathUtils.Position(axis,t);var f=math.normalizesafe(MathUtils.Tangent(axis,t));return math.dot(p.xz-c.xz,new float2(f.z,-f.x));}
   var edge=EntityManager.GetComponentData<Edge>(road);
   foreach(var node in new[]{edge.m_Start,edge.m_End})
   {
    if(!Usable(node)||!EntityManager.HasBuffer<ConnectedEdge>(node)||!EntityManager.HasBuffer<SubLane>(node))continue;
    bool driveway=false;
    foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(node,true))
    {
     var other=c.m_Edge;
     if(other==road||!Usable(other))continue;
     if(IsDriveway(other)){driveway=true;break;}
    }
    if(!driveway)continue;
    foreach(var sub in EntityManager.GetBuffer<SubLane>(node,true))
    {
     var lane=sub.m_SubLane;
     if(!EntityManager.HasComponent<CarLane>(lane)||!EntityManager.HasComponent<Curve>(lane))continue;
     var b=EntityManager.GetComponentData<Curve>(lane).m_Bezier;
     float a=Side(b.a),d=Side(b.d);
     if(a*d<0&&math.abs(a)>.5f&&math.abs(d)>.5f)output.Add(lane);
    }
   }
  }

  // A driveway edge: owned by a building or lot, or built from an alley / parking prefab (ParkingLotTool
  // builds its aisles from the vanilla "Alley" roads, and not every aisle carries an Owner).
  readonly Dictionary<Entity,bool> drivewayPrefabs=new Dictionary<Entity,bool>();
  bool IsDriveway(Entity edge)
  {
   if(EntityManager.HasComponent<Owner>(edge)&&EntityManager.GetComponentData<Owner>(edge).m_Owner!=Entity.Null)return true;
   if(!EntityManager.HasComponent<Game.Prefabs.PrefabRef>(edge))return false;
   var prefab=EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(edge).m_Prefab;
   if(!drivewayPrefabs.TryGetValue(prefab,out bool yes))
   {
    var name=PrefabName(prefab);
    yes=name.IndexOf("Alley",System.StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("Parking",System.StringComparison.OrdinalIgnoreCase)>=0;
    drivewayPrefabs[prefab]=yes;
   }
   return yes;
  }
  string PrefabName(Entity prefab){try{return World.GetOrCreateSystemManaged<Game.Prefabs.PrefabSystem>().GetPrefabName(prefab)??"";}catch{return "";}}
  string EdgeName(Entity e)=>EntityManager.HasComponent<Game.Prefabs.PrefabRef>(e)?PrefabName(EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(e).m_Prefab):"?";

  // Live check for the double yellow line (G25): every node at the ends of double-yellow roads, what
  // joins there and how many of its centre-crossing car lanes are closed. Logged when the set changes.
  string lastDrivewayReport;int forbidRebuilt;
  void LogDoubleYellowNodes()
  {
   var lines=new List<string>();var seen=new HashSet<Entity>();
   foreach(var road in DoubleYellow)
   {
    if(!Usable(road)||!EntityManager.HasComponent<Edge>(road)||!EntityManager.HasComponent<Curve>(road))continue;
    var axis=EntityManager.GetComponentData<Curve>(road).m_Bezier;
    float Side(float3 p){MathUtils.Distance(axis.xz,p.xz,out float t);var c=MathUtils.Position(axis,t);var f=math.normalizesafe(MathUtils.Tangent(axis,t));return math.dot(p.xz-c.xz,new float2(f.z,-f.x));}
    var edge=EntityManager.GetComponentData<Edge>(road);
    foreach(var node in new[]{edge.m_Start,edge.m_End})
    {
     if(!Usable(node)||!seen.Add(node)||!EntityManager.HasBuffer<ConnectedEdge>(node))continue;
     var arms=new List<string>();bool driveway=false;
     foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(node,true))
     {
      if(c.m_Edge==road||!Usable(c.m_Edge))continue;
      bool owned=EntityManager.HasComponent<Owner>(c.m_Edge)&&EntityManager.GetComponentData<Owner>(c.m_Edge).m_Owner!=Entity.Null;
      bool d=IsDriveway(c.m_Edge);driveway|=d;
      arms.Add(EdgeName(c.m_Edge)+(DoubleYellow.Contains(c.m_Edge)?"[双黄]":"")+(owned?"[有归属]":"")+(d?"[出入口]":""));
     }
     if(arms.Count<2&&!driveway)continue; // plain joint or dead end: nothing to report
     int cross=0,closed=0;
     if(EntityManager.HasBuffer<SubLane>(node))
      foreach(var sub in EntityManager.GetBuffer<SubLane>(node,true))
      {
       var lane=sub.m_SubLane;
       if(!EntityManager.HasComponent<CarLane>(lane)||!EntityManager.HasComponent<Curve>(lane))continue;
       var b=EntityManager.GetComponentData<Curve>(lane).m_Bezier;float a=Side(b.a),dd=Side(b.d);
       if(a*dd<0&&math.abs(a)>.5f&&math.abs(dd)>.5f){cross++;if((EntityManager.GetComponentData<CarLane>(lane).m_Flags&NetFlags.Forbidden)!=0)closed++;}
      }
     lines.Add(EdgeName(road)+" 节点"+node.Index+"：接 "+string.Join("、",arms)+"；跨中线车道 "+cross+"，已封 "+closed+(driveway?"":"（当路口，不封）"));
    }
   }
   var report=string.Join("\n  ",lines);
   if(report==lastDrivewayReport)return;
   lastDrivewayReport=report;
   Diagnostics.Event("doubleYellowNodes",new{Nodes=lines,Rebuilt=forbidRebuilt});
   Mod.Log.Info("双黄线节点 "+lines.Count+" 个（被重建过的封闭车道 "+forbidRebuilt+" 条）：\n  "+report);
  }

  // Cars whose remaining route uses a lane we just closed plan again from where they are.
  readonly List<Entity> newlyForbidden=new List<Entity>();
  int RerouteThrough(List<Entity> lanes)
  {
   if(lanes.Count==0)return 0;
   var set=new HashSet<Entity>(lanes);int count=0;
   using var q=EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Game.Vehicles.Car>(),ComponentType.ReadWrite<PathOwner>(),ComponentType.ReadOnly<PathElement>());
   using var cars=q.ToEntityArray(Unity.Collections.Allocator.Temp);
   foreach(var car in cars)
   {
    var owner=EntityManager.GetComponentData<PathOwner>(car);
    if((owner.m_State&(PathFlags.Pending|PathFlags.Obsolete|PathFlags.Failed))!=0)continue;
    var path=EntityManager.GetBuffer<PathElement>(car,true);
    bool hit=false;
    for(int i=owner.m_ElementIndex;i<path.Length&&!hit;i++)hit=set.Contains(path[i].m_Target);
    if(!hit&&EntityManager.HasBuffer<Game.Vehicles.CarNavigationLane>(car))
     foreach(var n in EntityManager.GetBuffer<Game.Vehicles.CarNavigationLane>(car,true))if(set.Contains(n.m_Lane)){hit=true;break;}
    if(!hit)continue;
    owner.m_State|=PathFlags.Obsolete;EntityManager.SetComponentData(car,owner);count++;
   }
   return count;
  }

  // Does a walkway in a junction run across one of the junction's roads (ends on opposite sides of
  // the road's centre line)?
  bool CrossesRoad(Entity node,Entity lane)
  {
   if(!EntityManager.HasComponent<Curve>(lane)||!EntityManager.HasBuffer<ConnectedEdge>(node))return false;
   var b=EntityManager.GetComponentData<Curve>(lane).m_Bezier;
   foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(node,true))
   {
    if(!Usable(c.m_Edge)||!EntityManager.HasComponent<Curve>(c.m_Edge))continue;
    var axis=EntityManager.GetComponentData<Curve>(c.m_Edge).m_Bezier;
    float Side(float3 p){MathUtils.Distance(axis.xz,p.xz,out float t);var q=MathUtils.Position(axis,t);var f=math.normalizesafe(MathUtils.Tangent(axis,t));return math.dot(p.xz-q.xz,new float2(f.z,-f.x));}
    float a=Side(b.a),d=Side(b.d);
    if(a*d<0&&math.abs(a)>1f&&math.abs(d)>1f)return true;
   }
   return false;
  }

  // Live check for the game's own painted speed numbers (G27): on roads with our speed limit, list
  // every sub-object and every non-driving sub-lane by prefab name and where it sits along the road.
  readonly HashSet<Entity> speedObjectsLogged=new HashSet<Entity>();
  void LogSpeedRoadObjects()
  {
   var prefabs=World.GetOrCreateSystemManaged<Game.Prefabs.PrefabSystem>();
   string Name(Entity e){try{return EntityManager.HasComponent<Game.Prefabs.PrefabRef>(e)?prefabs.GetPrefabName(EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(e).m_Prefab):"?";}catch{return "?";}}
   foreach(var road in Speed.Keys)
   {
    if(speedObjectsLogged.Count>=6||!speedObjectsLogged.Add(road)||!EntityManager.HasComponent<Curve>(road))continue;
    var axis=EntityManager.GetComponentData<Curve>(road).m_Bezier;
    float At(float3 p){MathUtils.Distance(axis.xz,p.xz,out float t);return t;}
    var items=new List<string>();
    if(EntityManager.HasBuffer<Game.Objects.SubObject>(road))
     foreach(var s in EntityManager.GetBuffer<Game.Objects.SubObject>(road,true))
     {
      var o=s.m_SubObject;string where=EntityManager.HasComponent<Game.Objects.Transform>(o)?" t="+At(EntityManager.GetComponentData<Game.Objects.Transform>(o).m_Position).ToString("0.00"):"";
      items.Add("物件 "+Name(o)+where);
     }
    if(EntityManager.HasBuffer<SubLane>(road))
     foreach(var s in EntityManager.GetBuffer<SubLane>(road,true))
     {
      var l=s.m_SubLane;
      if(EntityManager.HasComponent<CarLane>(l)||EntityManager.HasComponent<PedestrianLane>(l)||EntityManager.HasComponent<TrackLane>(l))continue;
      string where=EntityManager.HasComponent<Curve>(l)?" t="+At(EntityManager.GetComponentData<Curve>(l).m_Bezier.a).ToString("0.00")+"~"+At(EntityManager.GetComponentData<Curve>(l).m_Bezier.d).ToString("0.00"):"";
      items.Add("车道 "+Name(l)+where);
     }
    Diagnostics.Event("speedRoadObjects",new{Road=road.Index,Name=Name(road),Items=items});
    Mod.Log.Info("限速路段 "+road.Index+"（"+Name(road)+"）上的物件："+string.Join("；",items));
   }
  }

  // Live check for 禁止行人过街: every walkway in the closed junctions, by flags and whether it is closed.
  int lastCrossingNodes=-1;
  void LogCrossingLanes()
  {
   var kinds=new SortedDictionary<string,int>();
   foreach(var node in nodes)
   {
    if(!Usable(node)||!EntityManager.HasBuffer<SubLane>(node))continue;
    foreach(var sub in EntityManager.GetBuffer<SubLane>(node,true))
    {
     var lane=sub.m_SubLane;
     if(!EntityManager.HasComponent<PedestrianLane>(lane))continue;
     var key=EntityManager.GetComponentData<PedestrianLane>(lane).m_Flags+(wantCrossing.ContainsKey(lane)?" → 关闭":" → 保留");
     kinds[key]=kinds.TryGetValue(key,out int n)?n+1:1;
    }
   }
   Diagnostics.Event("crossingLanes",new{Nodes=nodes.Count,Lanes=kinds});
   Mod.Log.Info("禁行人路口 "+nodes.Count+" 个，行人车道："+string.Join("；",System.Linq.Enumerable.Select(kinds,k=>k.Key+" ×"+k.Value)));
  }

  protected override void OnUpdate()
  {
   if(Suspended||!Mod.SupportedGame)return;
   bool empty=Speed.Count==0&&NoBikes.Count==0&&LaneBans.Count==0&&DoubleYellow.Count==0&&NoCrossing.Count==0;
   if(empty&&speedOwned.Count==0&&bikeOwned.Count==0&&forbidOwned.Count==0&&publicOwned.Count==0&&crossingOwned.Count==0&&marksRoads.Count==0)return;
   if(applied==Revision&&UnityEngine.Time.unscaledTime<nextApply)return;
   applied=Revision;nextApply=UnityEngine.Time.unscaledTime+1f;
   Apply();
  }

  readonly HashSet<Entity> wantBike=new HashSet<Entity>(),wantForbid=new HashSet<Entity>(),wantPublic=new HashSet<Entity>(),nodes=new HashSet<Entity>();
  readonly Dictionary<Entity,float> wantSpeed=new Dictionary<Entity,float>();
  readonly Dictionary<Entity,Entity> wantCrossing=new Dictionary<Entity,Entity>();
  readonly List<Entity> stale=new List<Entity>();

  void Apply()
  {
   // What each lane should be now.
   wantSpeed.Clear();wantBike.Clear();wantForbid.Clear();wantPublic.Clear();wantCrossing.Clear();
   stale.Clear();
   foreach(var road in Speed.Keys)if(!RoadInspector.IsRoad(EntityManager,road))stale.Add(road);
   foreach(var road in stale){Speed.Remove(road);Revision++;}
   stale.Clear();
   // Driving lanes only (not building connections); group lanes keep the limit too, the pathfinder reads them.
   foreach(var pair in Speed)
    foreach(var sub in EntityManager.GetBuffer<SubLane>(pair.Key,true))
     if(EntityManager.HasComponent<CarLane>(sub.m_SubLane)&&Usable(sub.m_SubLane)&&(EntityManager.GetComponentData<CarLane>(sub.m_SubLane).m_Flags&NetFlags.SideConnection)==0)wantSpeed[sub.m_SubLane]=pair.Value/KmhPerUnit;
   // Old saves: a road with 禁止自行车 becomes a bicycle ban on each of its lanes.
   if(NoBikes.Count>0)
   {
    foreach(var road in NoBikes)
     if(RoadInspector.IsRoad(EntityManager,road))
      for(byte d=1;d<=2;d++)for(int i=LaneGroups(road,d).Count-1;i>=0;i--)SetLaneBan(road,d,i,KBike,true);
    NoBikes.Clear();
   }
   stale.Clear();
   foreach(var road in LaneBans.Keys)if(!RoadInspector.IsRoad(EntityManager,road))stale.Add(road);
   foreach(var road in stale){LaneBans.Remove(road);Revision++;}
   stale.Clear();
   foreach(var pair in LaneBans)
    for(byte d=1;d<=2;d++)
    {
     var groups=LaneGroups(pair.Key,d);
     bool wholeCar=groups.Count>0;
     for(int i=0;i<groups.Count;i++)
     {
      byte m=Banned(pair.Key,d,i);
      if((m&KCar)==0)wholeCar=false;
      if((m&KBike)!=0)foreach(var lane in groups[i])if(Usable(lane))wantBike.Add(lane);
     }
     if(wholeCar)foreach(var g in groups)foreach(var lane in g)if(Usable(lane))wantPublic.Add(lane);
    }
   foreach(var road in DoubleYellow)
    if(RoadInspector.IsRoad(EntityManager,road))
    {
     CrossingAccessLanes(road,scratch);foreach(var l in scratch)if(Usable(l))wantForbid.Add(l);
     DrivewayCrossingLanes(road,scratch);foreach(var l in scratch)if(Usable(l))wantForbid.Add(l);
    }
   CrossingNodes(nodes);
   foreach(var node in nodes)
   {
    if(!Usable(node)||!EntityManager.HasBuffer<SubLane>(node))continue;
    foreach(var sub in EntityManager.GetBuffer<SubLane>(node,true))
    {
     var lane=sub.m_SubLane;
     if(!EntityManager.HasComponent<PedestrianLane>(lane)||!Usable(lane))continue;
     var flags=EntityManager.GetComponentData<PedestrianLane>(lane).m_Flags;
     bool crossing=(flags&(PedestrianLaneFlags.Crosswalk|PedestrianLaneFlags.Unsafe))!=0;
     // Any other non-sidewalk walkway in the junction that crosses one of its roads (unmarked
     // crossings over the road surface).
     if(!crossing&&(flags&PedestrianLaneFlags.SideConnection)==0)crossing=CrossesRoad(node,lane);
     if(crossing)wantCrossing[lane]=node;
    }
   }
   if(nodes.Count!=lastCrossingNodes){lastCrossingNodes=nodes.Count;LogCrossingLanes();}

   // Speed limits: remember the original, write ours, give back lanes no longer wanted.
   stale.Clear();foreach(var lane in speedOwned.Keys)if(!wantSpeed.ContainsKey(lane))stale.Add(lane);
   foreach(var lane in stale){RestoreSpeed(lane);speedOwned.Remove(lane);}
   foreach(var pair in wantSpeed)
   {
    var data=EntityManager.GetComponentData<CarLane>(pair.Key);
    if(!speedOwned.ContainsKey(pair.Key))speedOwned[pair.Key]=data.m_SpeedLimit;
    if(math.abs(data.m_SpeedLimit-pair.Value)<.01f)continue;
    data.m_SpeedLimit=pair.Value;EntityManager.SetComponentData(pair.Key,data);Dirty(pair.Key);
   }
   SyncFlag(bikeOwned,wantBike,NetFlags.ForbidBicycles);
   newlyForbidden.Clear();
   forbidRebuilt+=SyncFlag(forbidOwned,wantForbid,NetFlags.Forbidden,newlyForbidden);
   if(newlyForbidden.Count>0&&(Mod.Settings==null||Mod.Settings.DoubleYellowReroute))
   {
    int moved=RerouteThrough(newlyForbidden);
    if(moved>0){Diagnostics.Event("doubleYellowReroute",new{Lanes=newlyForbidden.Count,Cars=moved});Mod.Log.Info("双黄线新封 "+newlyForbidden.Count+" 条车道，"+moved+" 辆车改道");}
   }
   if(DoubleYellow.Count>0)LogDoubleYellowNodes();
   SyncFlag(publicOwned,wantPublic,NetFlags.PublicOnly);
   // For live checks: how many lanes each rule actually reached.
   var summary=wantSpeed.Count+"/"+wantBike.Count+"/"+wantPublic.Count+"/"+wantForbid.Count+"/"+wantCrossing.Count;
   if(summary!=lastSummary&&Speed.Count>0)LogSpeedRoadObjects();
   if(summary!=lastSummary){lastSummary=summary;Diagnostics.Event("roadRulesApplied",new{SpeedLanes=wantSpeed.Count,BikeLanes=wantBike.Count,NoPrivateLanes=wantPublic.Count,DoubleYellowAccessLanes=wantForbid.Count,ClosedCrossings=wantCrossing.Count,DoubleYellowRoads=DoubleYellow.Count});Mod.Log.Info("车道规则生效：限速车道 "+wantSpeed.Count+"，禁自行车车道 "+wantBike.Count+"，整段禁私家车车道 "+wantPublic.Count+"，双黄线封闭出入口 "+wantForbid.Count+"，关闭横道 "+wantCrossing.Count);}
   SyncSpeedMarks();
   // Crossings.
   stale.Clear();foreach(var lane in crossingOwned.Keys)if(!wantCrossing.ContainsKey(lane))stale.Add(lane);
   foreach(var lane in stale){RestoreCrossing(lane);crossingOwned.Remove(lane);}
   foreach(var pair in wantCrossing)
   {
    var data=EntityManager.GetComponentData<PedestrianLane>(pair.Key);
    if(!crossingOwned.ContainsKey(pair.Key))crossingOwned[pair.Key]=data.m_AccessRestriction;
    if(data.m_AccessRestriction==pair.Value)continue;
    data.m_AccessRestriction=pair.Value;EntityManager.SetComponentData(pair.Key,data);Dirty(pair.Key);
   }
  }

  // G27: the game's NA road prefabs carry their speed number as sub-objects ("NA LaneMarkings Speed55").
  // Hiding them (Game.Tools.Hidden) did not stop them drawing, so on roads with our speed limit they
  // are deleted. A rebuilt road grows them back (picked up again next pass); when our limit or the
  // setting goes away the road is marked Updated and the game places them again.
  readonly HashSet<Entity> marksRoads=new HashSet<Entity>(),wantMarkRoads=new HashSet<Entity>();
  readonly Dictionary<Entity,bool> speedMarkPrefabs=new Dictionary<Entity,bool>();
  bool IsSpeedMark(Entity obj)
  {
   if(!EntityManager.HasComponent<Game.Prefabs.PrefabRef>(obj))return false;
   var prefab=EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(obj).m_Prefab;
   if(!speedMarkPrefabs.TryGetValue(prefab,out bool yes))
   {
    var name=PrefabName(prefab);
    yes=name.IndexOf("LaneMarkings Speed",System.StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("LaneMarking Speed",System.StringComparison.OrdinalIgnoreCase)>=0;
    speedMarkPrefabs[prefab]=yes;
   }
   return yes;
  }
  int marksDeleted,lastMarksLogged=-1;
  // Deleting the marks crashed the game a minute later (a rendering job read the deleted entity),
  // so it is switched off: roads we touched in 0.3.7-0.3.9 are only handed back to the game.
  void SyncSpeedMarks()
  {
   if(marksRoads.Count==0)return;
   foreach(var road in marksRoads)Regrow(road);
   marksRoads.Clear();
  }
  // Let the game place a road's own markings again.
  void Regrow(Entity road){if(Usable(road)&&!EntityManager.HasComponent<Updated>(road))EntityManager.AddComponent<Updated>(road);}

  int SyncFlag(HashSet<Entity> owned,HashSet<Entity> want,NetFlags flag,List<Entity> added=null)
  {
   int gone=0;
   stale.Clear();foreach(var lane in owned)if(!want.Contains(lane))stale.Add(lane);
   foreach(var lane in stale){if(!Usable(lane))gone++;ClearFlag(lane,flag,true);owned.Remove(lane);}
   foreach(var lane in want)
   {
    var data=EntityManager.GetComponentData<CarLane>(lane);
    if((data.m_Flags&flag)!=0)continue; // already set (by us earlier, or by the game: then it is not ours to clear)
    owned.Add(lane);data.m_Flags|=flag;EntityManager.SetComponentData(lane,data);Dirty(lane);added?.Add(lane);
   }
   return gone;
  }

  void ClearFlag(Entity lane,NetFlags flag,bool notify)
  {
   if(!Usable(lane)||!EntityManager.HasComponent<CarLane>(lane))return;
   var data=EntityManager.GetComponentData<CarLane>(lane);data.m_Flags&=~flag;EntityManager.SetComponentData(lane,data);if(notify)Dirty(lane);
  }
  void RestoreSpeed(Entity lane,bool notify=true)
  {
   if(!Usable(lane)||!EntityManager.HasComponent<CarLane>(lane)||!speedOwned.TryGetValue(lane,out var original))return;
   var data=EntityManager.GetComponentData<CarLane>(lane);data.m_SpeedLimit=original;EntityManager.SetComponentData(lane,data);if(notify)Dirty(lane);
  }
  void RestoreCrossing(Entity lane,bool notify=true)
  {
   if(!Usable(lane)||!EntityManager.HasComponent<PedestrianLane>(lane)||!crossingOwned.TryGetValue(lane,out var original))return;
   var data=EntityManager.GetComponentData<PedestrianLane>(lane);data.m_AccessRestriction=original;EntityManager.SetComponentData(lane,data);if(notify)Dirty(lane);
  }

  // Take every change back before the city is written; put it back afterwards.
  internal void BeforeSave()
  {
   Suspended=true;
   foreach(var lane in speedOwned.Keys)RestoreSpeed(lane,false);
   foreach(var lane in bikeOwned)ClearFlag(lane,NetFlags.ForbidBicycles,false);
   foreach(var lane in forbidOwned)ClearFlag(lane,NetFlags.Forbidden,false);
   foreach(var lane in publicOwned)ClearFlag(lane,NetFlags.PublicOnly,false);
   foreach(var lane in crossingOwned.Keys)RestoreCrossing(lane,false);
  }
  internal void AfterSave()
  {
   speedOwned.Clear();bikeOwned.Clear();forbidOwned.Clear();publicOwned.Clear();crossingOwned.Clear();
   Suspended=false;applied=-1;Apply();
  }
  internal void Shutdown()
  {
   BeforeSave();
   foreach(var lane in speedOwned.Keys)if(Usable(lane))Dirty(lane);
   foreach(var lane in bikeOwned)if(Usable(lane))Dirty(lane);
   foreach(var lane in forbidOwned)if(Usable(lane))Dirty(lane);
   foreach(var lane in publicOwned)if(Usable(lane))Dirty(lane);
   foreach(var lane in crossingOwned.Keys)if(Usable(lane))Dirty(lane);
   foreach(var road in marksRoads)Regrow(road);marksRoads.Clear();
   speedOwned.Clear();bikeOwned.Clear();forbidOwned.Clear();publicOwned.Clear();crossingOwned.Clear();Enabled=false;
  }

  // Saves touched by 0.3.6-0.3.8 can hold road sub-object lists pointing at entities that are gone
  // (hidden or deleted speed marks). Drop those entries right after loading, before other mods read them.
  protected override void OnGameLoaded(Context serializationContext)
  {
   base.OnGameLoaded(serializationContext);
   try{RepairSubObjects();}catch(System.Exception ex){Mod.Log.Warn("子物件清单修复失败："+ex.Message);}
  }
  void RepairSubObjects()
  {
   int fixedEntries=0,owners=0;
   using var q=EntityManager.CreateEntityQuery(ComponentType.ReadWrite<Game.Objects.SubObject>());
   using var list=q.ToEntityArray(Unity.Collections.Allocator.Temp);
   foreach(var owner in list)
   {
    var subs=EntityManager.GetBuffer<Game.Objects.SubObject>(owner);bool any=false;
    for(int i=subs.Length-1;i>=0;i--)
     if(subs[i].m_SubObject==Entity.Null||!EntityManager.Exists(subs[i].m_SubObject)){subs.RemoveAt(i);fixedEntries++;any=true;}
    if(any)owners++;
   }
   // Old 0.3.6 saves: a speed mark may still carry our Hidden tag.
   int unhidden=0;
   using var hq=EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Game.Tools.Hidden>(),ComponentType.ReadOnly<Game.Prefabs.PrefabRef>(),ComponentType.ReadOnly<Owner>());
   using var hidden=hq.ToEntityArray(Unity.Collections.Allocator.Temp);
   foreach(var o in hidden)if(IsSpeedMark(o)){EntityManager.RemoveComponent<Game.Tools.Hidden>(o);unhidden++;}
   Diagnostics.Event("subObjectRepair",new{Entries=fixedEntries,Owners=owners,Unhidden=unhidden});
   Mod.Log.Info("读档修复：清掉指向不存在物件的子物件记录 "+fixedEntries+" 条（"+owners+" 个对象），取消隐藏 "+unhidden+" 个");
  }
  protected override void OnGameLoadingComplete(Purpose purpose,GameMode mode)
  {
   base.OnGameLoadingComplete(purpose,mode);
   speedOwned.Clear();bikeOwned.Clear();forbidOwned.Clear();publicOwned.Clear();crossingOwned.Clear();marksRoads.Clear();marksRoads.UnionWith(Speed.Keys);Suspended=false;applied=-1;Revision++; // give back marks a 0.3.7-0.3.9 save may lack
   Diagnostics.Event("roadRulesLoaded",new{Speed=Speed.Count,NoBikes=NoBikes.Count,LaneBanRoads=LaneBans.Count,DoubleYellow=DoubleYellow.Count,NoCrossing=NoCrossing.Count});
  }

  // Version 2: lane bans per vehicle kind. Version 1 (0.3.0) had truck lanes and whole-road bicycle bans.
  public void Serialize<W>(W writer) where W:IWriter
  {
   writer.Write(2);
   writer.Write(Speed.Count);foreach(var p in Speed){writer.Write(p.Key);writer.Write(p.Value);}
   writer.Write(NoBikes.Count);foreach(var e in NoBikes)writer.Write(e);
   writer.Write(LaneBans.Count);foreach(var p in LaneBans){writer.Write(p.Key);writer.Write(p.Value.Count);foreach(var c in p.Value){writer.Write(c.Key);writer.Write((int)c.Value);}}
   writer.Write(DoubleYellow.Count);foreach(var e in DoubleYellow)writer.Write(e);
   writer.Write(NoCrossing.Count);foreach(var e in NoCrossing)writer.Write(e);
  }
  public void Deserialize<R>(R reader) where R:IReader
  {
   Clear();
   reader.Read(out int version);if(version!=1&&version!=2)throw new System.InvalidOperationException("不支持的车道规则存档版本。");
   int Count(){reader.Read(out int n);if(n<0||n>100000)throw new System.InvalidOperationException("车道规则存档长度无效。");return n;}
   for(int i=Count();i>0;i--){reader.Read(out Entity e);reader.Read(out int v);if(e!=Entity.Null)Speed[e]=v;}
   for(int i=Count();i>0;i--){reader.Read(out Entity e);if(e!=Entity.Null)NoBikes.Add(e);}
   for(int i=Count();i>0;i--)
   {
    reader.Read(out Entity e);var map=new Dictionary<int,byte>();
    for(int j=Count();j>0;j--)
    {
     reader.Read(out int code);byte kinds=KTruck;
     if(version>=2){reader.Read(out int k);kinds=(byte)(k&127);}
     if(code>=16&&code<48&&kinds!=0)map[code]=(byte)kinds;
    }
    if(e!=Entity.Null&&map.Count>0)LaneBans[e]=map;
   }
   for(int i=Count();i>0;i--){reader.Read(out Entity e);if(e!=Entity.Null)DoubleYellow.Add(e);}
   for(int i=Count();i>0;i--){reader.Read(out Entity e);if(e!=Entity.Null)NoCrossing.Add(e);}
   Revision++;
  }
  void Clear(){Speed.Clear();NoBikes.Clear();LaneBans.Clear();DoubleYellow.Clear();NoCrossing.Clear();Revision++;}
  public void SetDefaults(Context context)=>Clear();
 }
 public sealed class RoadRulesBeforeSave:GameSystemBase
 {protected override void OnUpdate(){var r=World.GetExistingSystemManaged<RoadRules>();if(r!=null&&r.Enabled)r.BeforeSave();}}
 public sealed class RoadRulesAfterSave:GameSystemBase
 {protected override void OnUpdate(){var r=World.GetExistingSystemManaged<RoadRules>();if(r!=null&&r.Enabled)r.AfterSave();}}
}
