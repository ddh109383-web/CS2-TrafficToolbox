using System.Collections.Generic;
using Colossal.Mathematics;
using Colossal.UI.Binding;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using SubLane=Game.Net.SubLane;

namespace RoadLaneLock
{
 // Shared page behaviour: road deselect, session statistics, transit auto-open and map overlay shapes.
 public partial class RuleUISystem
 {
  // ---- Statistics: counted all session, shown as a snapshot taken when a module page opens. ----
  sealed class StatsSnapshot
  {
   public readonly Dictionary<Entity,int> Exits=new Dictionary<Entity,int>(),Jams=new Dictionary<Entity,int>(),Followers=new Dictionary<Entity,int>(),Cleared=new Dictionary<Entity,int>();
   public int TotalExits,TotalJams,TotalFollowers,TotalCleared;
  }
  StatsSnapshot stats=new StatsSnapshot();
  void TakeStats()
  {
   var s=new StatsSnapshot();var h=Hook;
   if(h!=null)
   {
    foreach(var p in h.ExitsByRoad)s.Exits[p.Key]=p.Value;
    foreach(var p in h.JamsByRoad)s.Jams[p.Key]=p.Value;
    foreach(var p in h.FollowersByRoad)s.Followers[p.Key]=p.Value;
    s.TotalExits=h.TotalExits;s.TotalJams=h.JamCount;s.TotalFollowers=h.JamFollowers;
   }
   if(parking!=null){foreach(var p in parking.ClearedByRoad)s.Cleared[p.Key]=p.Value;s.TotalCleared=parking.TotalCleared;}
   stats=s;
  }
  static int Count(Dictionary<Entity,int> map,Entity road)=>map.TryGetValue(road,out int n)?n:0;

  // ---- Road text painted by the mod on lane-change roads, in the game language (null: lines only). ----
  static string PaintText()=>RoadMarkings.LegendFor(L.GameLocale()).Text;

  // ---- Transit auto-open: selecting a passenger vehicle or stop opens the transit page on its line. ----
  Entity lastNative,ownSelection;
  object transitFocus;
  int focusRevision;
  // Selections the panel makes itself (focus buttons) must not re-trigger the auto-open.
  void SelectNative(Entity e){ownSelection=e;selectedInfo.SetSelection(e);}

  void InitPanelExtras()
  {
   AddBinding(new TriggerBinding("RoadLaneLock","deselectRoad",()=>{if(picker.Active)picker.Deselect();Publish();}));
  }

  void CheckTransitAutoOpen()
  {
   var e=selectedInfo.selectedEntity;
   if(e==lastNative)return;
   lastNative=e;
   if(e==ownSelection){ownSelection=Entity.Null;return;}
   if(Mod.Settings!=null&&!Mod.Settings.AutoOpenTransit)return;
   if(e==Entity.Null||!EntityManager.Exists(e)||!ResolveTransit(e,out var line,out var vehicle,out var stop))return;
   if(!transitOpen){parkingOpen=false;viewingVehicles=false;monitorOpen=false;picker.End();transitOpen=true;activeMode="*";refreshAt=0;}
   transitFocus=new{Line=line==Entity.Null?null:Id(line),Vehicle=vehicle==Entity.Null?null:Id(vehicle),Stop=stop==Entity.Null?null:Id(stop),Rev=++focusRevision};
   Publish();
  }

  bool PassengerLine(Entity route)
  {
   if(route==Entity.Null||!EntityManager.Exists(route)||!EntityManager.HasComponent<Game.Routes.TransportLine>(route)||!EntityManager.HasComponent<PrefabRef>(route))return false;
   var prefab=EntityManager.GetComponentData<PrefabRef>(route).m_Prefab;
   return EntityManager.HasComponent<TransportLineData>(prefab)&&EntityManager.GetComponentData<TransportLineData>(prefab).m_PassengerTransport;
  }

  bool StopLine(Entity stop,out Entity line)
  {
   line=Entity.Null;
   if(!EntityManager.HasBuffer<Game.Routes.ConnectedRoute>(stop))return false;
   foreach(var c in EntityManager.GetBuffer<Game.Routes.ConnectedRoute>(stop,true))
   {
    var w=c.m_Waypoint;
    if(!EntityManager.HasComponent<Game.Common.Owner>(w))continue;
    var route=EntityManager.GetComponentData<Game.Common.Owner>(w).m_Owner;
    if(PassengerLine(route)){line=route;return true;}
   }
   return false;
  }

  bool ResolveTransit(Entity e,out Entity line,out Entity vehicle,out Entity stop)
  {
   line=vehicle=stop=Entity.Null;
   var v=e;
   if(EntityManager.HasComponent<Game.Vehicles.Controller>(v)){var main=EntityManager.GetComponentData<Game.Vehicles.Controller>(v).m_Controller;if(main!=Entity.Null&&EntityManager.Exists(main))v=main;}
   if(EntityManager.HasComponent<Game.Vehicles.PublicTransport>(v)&&EntityManager.HasComponent<Game.Routes.CurrentRoute>(v))
   {
    var route=EntityManager.GetComponentData<Game.Routes.CurrentRoute>(v).m_Route;
    if(PassengerLine(route)){line=route;vehicle=v;return true;}
   }
   // A stop, or a station building whose platforms are its sub-objects. The panel picks the line.
   if(StopLine(e,out _)){stop=e;return true;}
   if(EntityManager.HasBuffer<Game.Objects.SubObject>(e))
    foreach(var sub in EntityManager.GetBuffer<Game.Objects.SubObject>(e,true))
     if(StopLine(sub.m_SubObject,out _)){stop=sub.m_SubObject;return true;}
   return false;
  }

  // ---- Overlay shapes ----
  static readonly float3 Up=new float3(0,1,0);
  // Chevrons along a lane, pointing the way it is driven.
  static void DrawChevrons(Game.Rendering.OverlayRenderSystem.Buffer buffer,Bezier4x3 curve,float length,UnityEngine.Color32 color)
  {
   int count=math.clamp((int)(length/7f),1,48);
   for(int i=0;i<count;i++)
   {
    float t=(i+.5f)/count;
    var p=MathUtils.Position(curve,t)+new float3(0,.15f,0);
    var d=MathUtils.Tangent(curve,t);d.y=0;d=math.normalizesafe(d);
    var side=math.normalizesafe(math.cross(Up,d));
    var tip=p+d*.9f;
    buffer.DrawLine(color,new Line3.Segment(p-d*.6f+side*1f,tip),.32f);
    buffer.DrawLine(color,new Line3.Segment(p-d*.6f-side*1f,tip),.32f);
   }
  }
  // A lane curve moved sideways (positive = to the right of travel).
  static Bezier4x3 Offset(Bezier4x3 c,float distance)
  {
   float3 Side(float t){var d=MathUtils.Tangent(c,t);d.y=0;return math.normalizesafe(math.cross(d,Up))*distance;}
   var s0=Side(0f);var s1=Side(1f);
   return new Bezier4x3(c.a+s0,c.b+s0,c.c+s1,c.d+s1);
  }
  float LaneWidth(Entity lane)
  {
   if(EntityManager.HasComponent<PrefabRef>(lane)){var prefab=EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;if(EntityManager.HasComponent<NetLaneData>(prefab)){var w=EntityManager.GetComponentData<NetLaneData>(prefab).m_Width;if(w>0.5f)return w;}}
   return 3f;
  }
  // Lane-change control: glowing white solid lines along both edges of every lane of a controlled direction.
  void DrawLaneDividers(Game.Rendering.OverlayRenderSystem.Buffer buffer,Entity road,int mask)
  {
   if(mask==0||!RoadInspector.IsRoad(EntityManager,road)||!EntityManager.HasBuffer<SubLane>(road))return;
   var white=new UnityEngine.Color32(255,255,255,235);var glow=new UnityEngine.Color32(159,216,255,48);
   foreach(var sub in EntityManager.GetBuffer<SubLane>(road,true))
   {
    var lane=sub.m_SubLane;
    if((RoadPickTool.LaneDirection(EntityManager,lane)&mask)==0)continue;
    var c=EntityManager.GetComponentData<Curve>(lane).m_Bezier;
    float half=LaneWidth(lane)*.5f;
    for(int s=-1;s<=1;s+=2)
    {
     var edge=Offset(c,s*half);
     buffer.DrawCurve(glow,edge,.9f);
     buffer.DrawCurve(white,edge,.3f);
    }
   }
  }
  // No parking: yellow line on the kerb side of each banned parking lane, red no-parking marks every ~20 m.
  void DrawParkingBans(Game.Rendering.OverlayRenderSystem.Buffer buffer)
  {
   var yellow=new UnityEngine.Color32(242,194,48,235);var red=new UnityEngine.Color32(224,59,48,240);var white=new UnityEngine.Color32(255,255,255,220);
   foreach(var road in parking.Roads)
   {
    if(!RoadInspector.IsRoad(EntityManager,road)||!EntityManager.HasBuffer<SubLane>(road)||!EntityManager.HasComponent<Curve>(road))continue;
    var rc=EntityManager.GetComponentData<Curve>(road).m_Bezier;
    var mid=MathUtils.Position(rc,.5f);var rt=MathUtils.Tangent(rc,.5f);rt.y=0;var right=math.normalizesafe(math.cross(rt,Up));
    foreach(var sub in EntityManager.GetBuffer<SubLane>(road,true))
    {
     var lane=sub.m_SubLane;
     if(!parking.Eligible(lane,road))continue;
     var curve=EntityManager.GetComponentData<Curve>(lane);
     var c=curve.m_Bezier;
     // Kerb side: away from the road centre. Offset() measures to the right of the lane's own direction.
     var lt=MathUtils.Tangent(c,.5f);lt.y=0;var laneRight=math.normalizesafe(math.cross(lt,Up));
     float outward=math.sign(math.dot(MathUtils.Position(c,.5f)-mid,right))*math.sign(math.dot(laneRight,right));
     if(outward==0)outward=1;
     buffer.DrawCurve(yellow,Offset(c,outward*LaneWidth(lane)*.45f),.4f);
     int marks=math.clamp((int)(curve.m_Length/20f),1,16);
     for(int i=0;i<marks;i++)
     {
      var p=MathUtils.Position(c,(i+.5f)/marks)+new float3(0,.2f,0);
      buffer.DrawCircle(red,white,.35f,0,new float2(0,1),p,2.2f);
      buffer.DrawLine(red,new Line3.Segment(p+new float3(-.75f,0,-.75f),p+new float3(.75f,0,.75f)),.3f);
     }
    }
   }
  }
 }
}
