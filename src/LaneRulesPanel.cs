using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Colossal.UI.Binding;
using Game.Net;
using Newtonsoft.Json;
using Unity.Entities;
using Unity.Mathematics;

namespace RoadLaneLock
{
 // 车道规则 page (lane change, speed limit, double yellow, lane bans per vehicle kind: one rule edited at a
 // time, chosen with the switcher) and 路口规则 page (closing a junction's pedestrian crossings).
 public partial class RuleUISystem
 {
  static readonly string[] laneTabs={"lane","speed","yellow","truck"};
  string laneNotice;float laneNoticeUntil;
  string laneTab="lane";
  bool junctionOpen;
  RoadRules rules;
  RoadRules Rules=>rules??=World.GetOrCreateSystemManaged<RoadRules>();
  sealed class RuleChange{public string Session{get;set;}public int Index{get;set;}public int Version{get;set;}public byte Direction{get;set;}public int Lane{get;set;}public int Value{get;set;}public bool Enable{get;set;}}

  bool ReadChange(string json,out RuleChange c,out Entity road)
  {
   c=null;road=picker.Selected;
   if(json==null||json.Length>512)return false;
   try{c=JsonConvert.DeserializeObject<RuleChange>(json);}catch{return false;}
   return c!=null&&c.Session==session&&c.Index==road.Index&&c.Version==road.Version&&RoadInspector.IsRoad(EntityManager,road);
  }
  bool ReadNode(string json,out RuleChange c,out Entity node)
  {
   c=null;node=Entity.Null;
   if(json==null||json.Length>512)return false;
   try{c=JsonConvert.DeserializeObject<RuleChange>(json);}catch{return false;}
   if(c==null||c.Session!=session||c.Index<=0)return false;
   node=new Entity{Index=c.Index,Version=c.Version};
   return EntityManager.Exists(node)&&EntityManager.HasComponent<Node>(node);
  }

  void InitLaneRules()
  {
   AddBinding(new TriggerBinding<string>("RoadLaneLock","laneTab",tab=>{if(Array.IndexOf(laneTabs,tab)>=0){laneTab=tab;Publish();}}));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","setSpeed",json=>{
    if(!ReadChange(json,out var c,out var road))return;
    Rules.SetSpeed(road,math.clamp(c.Value,0,200));NoteEffect(road,"限速",c.Value>0);Publish();
   }));

   AddBinding(new TriggerBinding<string>("RoadLaneLock","setDoubleYellow",json=>{if(ReadChange(json,out var c,out var road)){Rules.SetDoubleYellow(road,c.Enable);NoteEffect(road,"双黄线",c.Enable);Publish();}}));
   // 分道: Value = one vehicle kind bit (RoadRules.Kinds), Enable = banned.
   AddBinding(new TriggerBinding<string>("RoadLaneLock","setLaneBan",json=>{
    if(!ReadChange(json,out var c,out var road)||(c.Direction!=1&&c.Direction!=2)||c.Lane<0||c.Lane>15||c.Value<1||c.Value>255)return;
    var refused=Rules.SetLaneBan(road,c.Direction,c.Lane,(byte)c.Value,c.Enable);
    if(refused!=null)
    {
     int k=Array.IndexOf(RoadRules.Kinds,(byte)c.Value);
     if(refused=="整段禁私家车")
     {
      laneNotice=L.T("整段禁私家车：原版寻路会绕开这个方向；货车、摩托车也一并禁行");
      laneNoticeUntil=UnityEngine.Time.unscaledTime+6f;
      NoteEffect(road,"分道",true);
     }
     else
     {
      laneNotice=k>=0&&refused.StartsWith("每个方向")?L.F("每个方向至少留一条车道给{0}",L.T(RoadRules.KindNames[k])):L.T(refused);
      laneNoticeUntil=UnityEngine.Time.unscaledTime+4f;
     }
    }
    else{laneNotice=null;NoteEffect(road,"分道",Rules.LaneBans.ContainsKey(road));}
    Publish();
   }));
   AddBinding(new TriggerBinding("RoadLaneLock","showJunction",()=>{TakeStats();parkingOpen=false;monitorOpen=false;transitOpen=false;viewingVehicles=false;picker.BeginNodes();junctionOpen=true;Publish();}));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","junctionRule",json=>{
    if(!junctionOpen||!ReadNode(json,out var c,out var node)||node!=picker.SelectedNode)return;
    Rules.SetNoCrossing(node,c.Enable);NoteEffect(node,"禁止行人过街",c.Enable);Publish();
   }));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","removeJunction",json=>{if(ReadNode(json,out var c,out var node)){Rules.SetNoCrossing(node,false);Publish();}}));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","focusJunction",json=>{if(ReadNode(json,out var c,out var node))MoveCamera(EntityManager.GetComponentData<Node>(node).m_Position);}));
  }

  // Settings of the selected road for the 车道规则 page.
  object LaneRulesFor(Entity road)
  {
   var r=Rules;
   r.Speed.TryGetValue(road,out int speed);
   var extras=SelectedRoadExtrasDictionary(road);
   var bans=new List<object>();
   int directions=0,banned=0;
   for(byte d=1;d<=2;d++)
   {
    int count=r.LaneGroups(road,d).Count;
    if(count==0)continue;
    directions++;
    var masks=new int[count];
    for(int i=0;i<count;i++){masks[i]=r.Banned(road,d,i);if(masks[i]!=0)banned++;}
    extras.TryGetValue(d,out var toward);
    bans.Add(new{Direction=d,Toward=toward,Lanes=masks,Whole=(int)r.WholeBan(road,d)});
   }
   if(laneNotice!=null&&UnityEngine.Time.unscaledTime>laneNoticeUntil)laneNotice=null;
   int mask=runtime.Executor.DirectionMask(road);
   return new{Speed=speed,DefaultSpeed=r.DefaultSpeed(road),DoubleYellow=r.DoubleYellow.Contains(road),TwoWay=directions==2,Bans=bans,BannedLanes=banned,
    Kinds=RoadRules.Kinds,KindNames=RoadRules.KindNames,Notice=laneNotice,
    Active=new{Lane=mask!=0,Speed=speed>0,Yellow=r.DoubleYellow.Contains(road),Truck=r.LaneBans.ContainsKey(road)}};
  }

  // Screen points for the 分道 buttons: the middle of each lane of the chosen road.
  object LaneAnchors(Entity road,UnityEngine.Camera camera)
  {
   if(camera==null||!RoadInspector.IsRoad(EntityManager,road))return null;
   var list=new List<object>();
   for(byte d=1;d<=2;d++)
   {
    var groups=Rules.LaneGroups(road,d);
    for(int i=0;i<groups.Count;i++)
    {
     Entity longest=Entity.Null;float best=-1;
     foreach(var lane in groups[i]){float l=EntityManager.GetComponentData<Curve>(lane).m_Length;if(l>best){best=l;longest=lane;}}
     var c=EntityManager.GetComponentData<Curve>(longest).m_Bezier;
     var p=MathUtils.Position(c,.5f);
     var point=Project(new RoadPickTool.Anchor{Center=p,Ahead=p+math.normalizesafe(MathUtils.Tangent(c,.5f))*5f},camera);
     // One spot per vehicle kind, laid along the lane centre line around its middle (about 4 m apart),
     // each projected on its own so the icons sit on the road and follow the camera's perspective.
     int n=RoadRules.Kinds.Length;float step=math.min(4f,best*.9f/n);
     var spots=new object[n];
     for(int k=0;k<n;k++)
     {
      float t=math.clamp(.5f+(k-(n-1)*.5f)*step/math.max(best,1f),.02f,.98f);
      var q=MathUtils.Position(c,t);
      spots[k]=Project(new RoadPickTool.Anchor{Center=q,Ahead=q+math.normalizesafe(MathUtils.Tangent(c,t))*5f},camera);
     }
     if(point!=null)list.Add(new{D=d,I=i,P=point,K=spots});
    }
   }
   return list;
  }

  Dictionary<byte,string> SelectedRoadExtrasDictionary(Entity road)
  {
   var result=new Dictionary<byte,string>();
   if(!EntityManager.HasComponent<Edge>(road))return result;
   var edge=EntityManager.GetComponentData<Edge>(road);
   string Toward(Entity node,Entity except)
   {
    if(!EntityManager.HasBuffer<ConnectedEdge>(node))return null;
    foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(node,true))
     if(c.m_Edge!=except&&RoadInspector.IsRoad(EntityManager,c.m_Edge)){var n=RoadName(c.m_Edge);if(!string.IsNullOrEmpty(n)&&n!=RoadName(except))return n;}
    return null;
   }
   result[1]=Toward(edge.m_End,road);result[2]=Toward(edge.m_Start,road);
   return result;
  }

  // Pedestrian crossings (marked or not) inside a junction.
  int Crossings(Entity node)
  {
   int n=0;
   if(EntityManager.HasBuffer<Game.Net.SubLane>(node))
    foreach(var sub in EntityManager.GetBuffer<Game.Net.SubLane>(node,true))
     if(EntityManager.HasComponent<PedestrianLane>(sub.m_SubLane)&&(EntityManager.GetComponentData<PedestrianLane>(sub.m_SubLane).m_Flags&(PedestrianLaneFlags.Crosswalk|PedestrianLaneFlags.Unsafe))!=0)n++;
   return n;
  }

  readonly HashSet<Entity> closedNodes=new HashSet<Entity>();
  object JunctionInfo()
  {
   var r=Rules;r.CrossingNodes(closedNodes);
   var list=new List<object>();
   foreach(var node in r.NoCrossing)if(EntityManager.Exists(node)&&EntityManager.HasComponent<Node>(node))list.Add(new{node.Index,node.Version,Name=PlaceName(node)});
   var selected=picker.SelectedNode;
   if(selected==Entity.Null||!EntityManager.Exists(selected))
    return new{Selected=false,HoverName=picker.HoveredNode!=Entity.Null&&EntityManager.Exists(picker.HoveredNode)?PlaceName(picker.HoveredNode):null,List=list};
   int roads=0;
   if(EntityManager.HasBuffer<ConnectedEdge>(selected))foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(selected,true))if(RoadInspector.IsRoad(EntityManager,c.m_Edge))roads++;
   bool closed=r.NoCrossing.Contains(selected),auto=!closed&&closedNodes.Contains(selected);
   // Another place to cross nearby: a neighbouring node (one road away) with open crossings.
   bool alternative=false;
   if(EntityManager.HasBuffer<ConnectedEdge>(selected))
    foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(selected,true))
    {
     if(!EntityManager.HasComponent<Edge>(c.m_Edge))continue;
     var e=EntityManager.GetComponentData<Edge>(c.m_Edge);
     var other=e.m_Start==selected?e.m_End:e.m_Start;
     if(other!=selected&&!closedNodes.Contains(other)&&Crossings(other)>0){alternative=true;break;}
    }
   return new{Selected=true,selected.Index,selected.Version,Name=PlaceName(selected),Roads=roads,Crossings=Crossings(selected),Closed=closed,Auto=auto,Warn=closed&&!alternative,List=list};
  }

  void DrawJunctionOverlay()
  {
   var buffer=World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>().GetBuffer(out var writer);
   writer.Complete();
   Rules.CrossingNodes(closedNodes);
   var red=new UnityEngine.Color32(255,110,96,230);
   foreach(var node in closedNodes)
    if(EntityManager.Exists(node)&&EntityManager.HasComponent<Node>(node))
     buffer.DrawCircle(red,new UnityEngine.Color32(255,110,96,40),.9f,0,new float2(0,1),EntityManager.GetComponentData<Node>(node).m_Position,22f);
   void Ring(Entity node,UnityEngine.Color32 color,float size)
   {
    if(node==Entity.Null||!EntityManager.Exists(node)||!EntityManager.HasComponent<Node>(node))return;
    buffer.DrawCircle(color,new UnityEngine.Color32(color.r,color.g,color.b,30),1.1f,0,new float2(0,1),EntityManager.GetComponentData<Node>(node).m_Position,size);
   }
   Ring(picker.HoveredNode,new UnityEngine.Color32(255,255,255,150),26f);
   Ring(picker.SelectedNode,new UnityEngine.Color32(112,212,196,240),28f);
  }

  // Map layer of the rule being edited on the 车道规则 page (the lane-change white lines are drawn elsewhere).
  void DrawLaneRuleOverlay(Game.Rendering.OverlayRenderSystem.Buffer buffer)
  {
   var r=Rules;
   void Road(Entity road,UnityEngine.Color32 color,float width)
   {
    if(!RoadInspector.IsRoad(EntityManager,road)||!EntityManager.HasComponent<EdgeGeometry>(road))return;
    var g=EntityManager.GetComponentData<EdgeGeometry>(road);
    buffer.DrawCurve(color,g.m_Start.m_Left,width);buffer.DrawCurve(color,g.m_End.m_Left,width);
    buffer.DrawCurve(color,g.m_Start.m_Right,width);buffer.DrawCurve(color,g.m_End.m_Right,width);
   }
   switch(laneTab)
   {
    case "speed":foreach(var road in r.Speed.Keys)Road(road,new UnityEngine.Color32(120,190,255,200),1.2f);break;

    case "yellow":
     foreach(var road in r.DoubleYellow)
      if(RoadInspector.IsRoad(EntityManager,road)&&EntityManager.HasComponent<Curve>(road))
       buffer.DrawCurve(new UnityEngine.Color32(240,190,60,230),EntityManager.GetComponentData<Curve>(road).m_Bezier,1.4f);
     break;
    case "truck":
     foreach(var pair in r.LaneBans)
      for(byte d=1;d<=2;d++)
      {
       var groups=r.LaneGroups(pair.Key,d);
       for(int i=0;i<groups.Count;i++)
        if(r.Banned(pair.Key,d,i)!=0)foreach(var lane in groups[i])buffer.DrawCurve(new UnityEngine.Color32(255,190,90,190),EntityManager.GetComponentData<Curve>(lane).m_Bezier,1.6f);
      }
     break;
   }
  }
 }
}
