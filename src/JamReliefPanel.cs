using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Colossal.UI.Binding;
using Game.Net;
using Game.Objects;
using Game.Vehicles;
using Newtonsoft.Json;
using Unity.Entities;
using Unity.Mathematics;

namespace RoadLaneLock
{
 // 拥堵疏导 page: monitored road directions, recent jam releases on them, and the map overlay.
 public partial class RuleUISystem
 {
  sealed class DirectionTarget {public string Session {get;set;} public int Index {get;set;} public int Version {get;set;} public byte Direction {get;set;} public bool Enable {get;set;}}
  readonly List<Entity> monitorRoadBuffer=new List<Entity>();
  LaneHookSystem hookSystem;
  LaneHookSystem Hook=>hookSystem??=World.GetExistingSystemManaged<LaneHookSystem>();

  void InitJamRelief()
  {
   // From the monitored list: jump to a road (selects it and moves the camera).
   AddBinding(new TriggerBinding<string>("RoadLaneLock","focusMonitorRoad",json=>{
    if(!monitorOpen||!TryReadTarget(json,out var t,out var road)||MonitorMask(road)==0)return;
    var center=MathUtils.Position(EntityManager.GetComponentData<Curve>(road).m_Bezier,.5f);
    picker.Select(road,center);MoveCamera(center);Publish();
   }));
   // Direction pills in the list: switch monitoring of any listed road.
   AddBinding(new TriggerBinding<string>("RoadLaneLock","monitorRoadDirection",json=>{
    if(!monitorOpen||!TryReadDirection(json,out var t,out var road)||(t.Direction!=1&&t.Direction!=2)||!HasMotorDirection(road,t.Direction))return;
    if(!t.Enable||runtime.EnsureActive())runtime.Executor.SetMonitoring(road,t.Direction,t.Enable);
    Publish();
   }));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","removeMonitorRoad",json=>{
    if(!monitorOpen||!TryReadTarget(json,out var t,out var road))return;
    runtime.Executor.SetMonitoring(road,1,false);runtime.Executor.SetMonitoring(road,2,false);Publish();
   }));
   // Event card: camera to the vehicle and open the game's own vehicle panel.
   AddBinding(new TriggerBinding<string>("RoadLaneLock","focusJamVehicle",id=>{
    if(!monitorOpen||!TryParseEntity(id,out var vehicle)||!EntityManager.Exists(vehicle)||EntityManager.HasComponent<Game.Common.Deleted>(vehicle)||!EntityManager.HasComponent<Vehicle>(vehicle))return;
    picker.UseNativeSelection();SelectNative(vehicle);selectedInfo.Focus(vehicle);
   }));
  }

  bool TryReadDirection(string json,out DirectionTarget t,out Entity road)
  {
   t=null;road=Entity.Null;
   if(json==null||json.Length>512)return false;
   try{t=JsonConvert.DeserializeObject<DirectionTarget>(json);}catch{return false;}
   if(t==null||t.Session!=session||t.Index<=0)return false;
   road=new Entity{Index=t.Index,Version=t.Version};
   return RoadInspector.IsRoad(EntityManager,road);
  }

  int MonitorMask(Entity road)=>(runtime.Executor.Monitoring(road,1)?1:0)|(runtime.Executor.Monitoring(road,2)?2:0);

  // Compass heading of a travel direction: 1 toward the start node, 2 toward the end node.
  string Heading(Entity road,byte direction)
  {
   if(!EntityManager.HasComponent<Curve>(road))return direction==1?"A 向":"B 向";
   var c=EntityManager.GetComponentData<Curve>(road).m_Bezier;
   var v=(c.d-c.a).xz;if(direction==1)v=-v;
   return Compass(v);
  }
  static string Compass(float2 v)=>math.abs(v.x)>=math.abs(v.y)?(v.x>=0?"东行":"西行"):(v.y>=0?"北行":"南行");

  object MonitorRoads()
  {
   monitorRoadBuffer.Clear();
   foreach(var road in runtime.Executor.MonitoredRoads)if(!monitorRoadBuffer.Contains(road))monitorRoadBuffer.Add(road);
   var rows=new List<(string name,int chome,object row)>();
   foreach(var road in monitorRoadBuffer)
   {
    if(!RoadInspector.IsRoad(EntityManager,road))continue;
    int dirs=(HasMotorDirection(road,1)?1:0)|(HasMotorDirection(road,2)?2:0);
    var name=RoadName(road);
    rows.Add((name??"",Chome(road).chome,new{Index=road.Index,Version=road.Version,Name=name,Chome=ChomeOf(road),SameName=SameNameStreets(road),Jams=Count(stats.Jams,road),Mask=MonitorMask(road),Directions=dirs,Heading1=Heading(road,1),Heading2=Heading(road,2)}));
   }
   // Segments of one street together, in chome order.
   rows.Sort((a,b)=>{int c=string.CompareOrdinal(a.name,b.name);return c!=0?c:a.chome.CompareTo(b.chome);});
   return rows.ConvertAll(x=>x.row);
  }

  int MonitoredDirections()
  {
   int n=0;monitorRoadBuffer.Clear();
   foreach(var road in runtime.Executor.MonitoredRoads)if(!monitorRoadBuffer.Contains(road)){monitorRoadBuffer.Add(road);n++;}
   return n;
  }

  static readonly string[] waits={"等转弯","等掉头","等左转","等右转"};
  static readonly string[] triggers={"","倒灌到路口","排队超过六成","停满 15 秒"};

  // Recent releases, only on road directions that are still monitored.
  object JamEvents()
  {
   var hook=Hook;var rows=new List<object>();
   if(hook==null)return rows;
   uint now=hook.Frame;
   foreach(var e in hook.RecentJams)
   {
    if(!runtime.Executor.Monitoring(e.Road,e.Direction)||!RoadInspector.IsRoad(EntityManager,e.Road))continue;
    string reason=(e.Full?"转入车道已满，":"")+waits[e.Wait<4?e.Wait:0];
    uint ago=(now-e.Frame)/60;
    var chome=ChomeOf(e.Road);
    rows.Add(new{Id=Id(e.Head),Road=RoadName(e.Road)+(chome!=null?" "+chome:""),Dir=Heading(e.Road,e.Direction),Reason=reason,Trigger=triggers[e.Trigger<4?e.Trigger:0],
     Followers=e.Followers.Count,Live=hook.IsHeld(e.Head),Ago=ago<60?ago+" 秒前":(ago/60)+" 分钟前"});
    if(rows.Count>=4)break;
   }
   return rows;
  }

  // Live releases on the map: mint ring on the head, mint dashes along its straight exit,
  // red cross on the abandoned turn, small mint rings on the vehicles unlocked behind it.
  void DrawJamOverlay(Game.Rendering.OverlayRenderSystem.Buffer buffer)
  {
   var hook=Hook;if(hook==null)return;
   var mint=new UnityEngine.Color32(112,212,196,235);var red=new UnityEngine.Color32(237,84,72,235);
   // Heads that qualify but have not left: red ring.
   foreach(var (head,isRed) in hook.MarkedHeads())
    if(isRed&&EntityManager.Exists(head)&&EntityManager.HasComponent<Transform>(head))
     buffer.DrawCircle(new UnityEngine.Color32(255,70,62,245),new UnityEngine.Color32(255,70,62,45),0.6f,0,new float2(0,1),EntityManager.GetComponentData<Transform>(head).m_Position,7f);
   foreach(var e in hook.RecentJams)
   {
    if(!hook.IsHeld(e.Head)||!runtime.Executor.Monitoring(e.Road,e.Direction))continue;
    if(EntityManager.Exists(e.Head)&&EntityManager.HasComponent<Transform>(e.Head))
    {
     var p=EntityManager.GetComponentData<Transform>(e.Head).m_Position;
     buffer.DrawCircle(mint,new UnityEngine.Color32(112,212,196,40),0.5f,0,new float2(0,1),p,7f);
     if(EntityManager.HasBuffer<CarNavigationLane>(e.Head))
     {
      var nav=EntityManager.GetBuffer<CarNavigationLane>(e.Head,true);
      for(int i=0;i<nav.Length&&i<4;i++)
       if(EntityManager.HasComponent<Curve>(nav[i].m_Lane))buffer.DrawDashedCurve(mint,EntityManager.GetComponentData<Curve>(nav[i].m_Lane).m_Bezier,0.8f,2.5f,2f);
     }
    }
    if(e.Turn!=Entity.Null&&EntityManager.Exists(e.Turn)&&EntityManager.HasComponent<Curve>(e.Turn))
    {
     var c=EntityManager.GetComponentData<Curve>(e.Turn).m_Bezier;
     var m=MathUtils.Position(c,.5f)+new float3(0,.2f,0);
     buffer.DrawLine(red,new Line3.Segment(m+new float3(-1.6f,0,-1.6f),m+new float3(1.6f,0,1.6f)),0.55f);
     buffer.DrawLine(red,new Line3.Segment(m+new float3(-1.6f,0,1.6f),m+new float3(1.6f,0,-1.6f)),0.55f);
    }
    foreach(var f in e.Followers)
     if(EntityManager.Exists(f)&&EntityManager.HasComponent<Transform>(f))buffer.DrawCircle(mint,EntityManager.GetComponentData<Transform>(f).m_Position,3.5f);
   }
  }
 }
}
