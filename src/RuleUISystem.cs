using System;
using Colossal.Mathematics;
using Unity.Mathematics;
using Colossal.UI.Binding;
using Game.UI;
using Game.UI.InGame;
using Newtonsoft.Json;
using Unity.Entities;
using Colossal.Serialization.Entities;
using Game;

namespace RoadLaneLock
{
 public partial class RuleUISystem:UISystemBase
 {
  private ValueBinding<string> state;
  private RoadPickTool picker;
  private SelectedInfoUISystem selectedInfo;
  private bool viewingVehicles,monitorOpen;
  private string recordFeedback,lastRoadShape;
  private int recordedSelection;
  private string RoadShape(Entity road)
  {
   if(!EntityManager.HasComponent<Game.Net.Curve>(road))return null;
   var curve=EntityManager.GetComponentData<Game.Net.Curve>(road).m_Bezier;
   var lo=math.min(math.min(curve.a.xz,curve.b.xz),math.min(curve.c.xz,curve.d.xz));
   var hi=math.max(math.max(curve.a.xz,curve.b.xz),math.max(curve.c.xz,curve.d.xz));
   var scale=math.min(136f/math.max(1f,hi.x-lo.x),52f/math.max(1f,hi.y-lo.y));
   string Point(float3 p){
    var xy=(p.xz-(hi+lo)*.5f)*scale;
    return string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0:0.#} {1:0.#}",80+xy.x,32-xy.y);
   }
   return "M"+Point(curve.a)+" C"+Point(curve.b)+" "+Point(curve.c)+" "+Point(curve.d);
  }
  private void ShowVehicles()
  {
   parkingOpen=false;monitorOpen=false;transitOpen=false;viewingVehicles=true;picker.UseNativeSelection();trafficCache=null;hoverTraffic=Entity.Null;
   if(recorder.Recording)recorder.Stop(); // the old per-vehicle recorder is retired
  }
  private string SelectedVehicleId()
  {
   var e=selectedInfo.selectedEntity;
   return EntityManager.Exists(e)&&EntityManager.HasComponent<Game.Vehicles.Vehicle>(e)&&!EntityManager.HasComponent<Game.Common.Deleted>(e)?Id(e):null;
  }
  private string status="测试版：选择路段后开启规则。当前尚未完成实机验收。";
  private RuntimeCoordinator runtime;
  private string session=Guid.NewGuid().ToString("N"),last;
  private int ticks;
  private ValueBinding<string> pose,strings;
  private string lastPose;
  private Entity anchorRoad,previewRoad;
  private byte previewDirection;
  private RoadPickTool.Anchor? first,second;
  private static object Project(RoadPickTool.Anchor? anchor,UnityEngine.Camera camera)
  {
   if(!anchor.HasValue||camera==null)return null;
   var a=anchor.Value;
   var p=camera.WorldToViewportPoint(a.Center);
   if(p.z<=0||p.x<0||p.x>1||p.y<0||p.y>1)return null;
   var ahead=camera.WorldToViewportPoint(a.Ahead);
   return new {X=p.x,Y=1-p.y,Angle=math.degrees(math.atan2((p.y-ahead.y)*camera.pixelHeight,(ahead.x-p.x)*camera.pixelWidth))};
  }
  private void PublishPose()
  {
   string json="{}";
   if(picker.Active&&anchorRoad!=Entity.Null&&anchorRoad==picker.Selected&&(first.HasValue||second.HasValue||parkingAnchor.HasValue))
   {
    var camera=World.GetExistingSystemManaged<Game.Rendering.CameraUpdateSystem>()?.activeCamera;
    json=JsonConvert.SerializeObject(new {Session=session,Index=anchorRoad.Index,Version=anchorRoad.Version,
     Parking=parkingOpen?Project(parkingAnchor,camera):null,First=Project(first,camera),Second=Project(second,camera),Mid=Project(MidAnchor(anchorRoad),camera),
     Lanes=laneTab=="truck"&&!parkingOpen&&!monitorOpen&&!transitOpen&&!junctionOpen?LaneAnchors(anchorRoad,camera):null});
   }
   if(json!=lastPose){lastPose=json;pose.Update(json);}
  }
  private VehicleRecorder recorder;
  // Middle of the chosen road, a little above it: where the read-only badges of other active rules sit.
  private RoadPickTool.Anchor? MidAnchor(Entity road)
  {
   if(!EntityManager.HasComponent<Game.Net.Curve>(road))return null;
   var c=EntityManager.GetComponentData<Game.Net.Curve>(road).m_Bezier;
   var p=MathUtils.Position(c,.5f);
   return new RoadPickTool.Anchor{Center=p,Ahead=p+math.normalizesafe(MathUtils.Tangent(c,.5f))*5f};
  }
  
  private Entity describedRoad;
  private object description;
  private class Request {public string Session {get;set;} public int Index {get;set;} public int Version {get;set;} public byte Direction {get;set;} public bool Enable {get;set;}}
  protected override void OnCreate()
  {
   base.OnCreate();recorder=World.GetOrCreateSystemManaged<VehicleRecorder>();picker=World.GetOrCreateSystemManaged<RoadPickTool>();runtime=World.GetOrCreateSystemManaged<RuntimeCoordinator>();
   selectedInfo=World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
   InitTransit();InitParking();InitLaneControl();InitJamRelief();InitTrafficInfo();InitStreetNames();InitPanelExtras();InitLaneRules();InitTrafficInsights();
   AddBinding(new TriggerBinding("RoadLaneLock","showVehicles",()=>{ShowVehicles();Publish();}));
   AddBinding(new TriggerBinding("RoadLaneLock","showMonitor",()=>{TakeStats();parkingOpen=false;monitorOpen=true;transitOpen=false;viewingVehicles=false;picker.Begin();Publish();}));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","previewMonitor",json=>{
    if(monitorOpen&&picker.Active&&Validate(json,out var r,out var road)){previewRoad=road;previewDirection=r.Direction<=2?r.Direction:(byte)0;}
   }));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","monitorRule",json=>{if(!monitorOpen||!Validate(json,out var r,out var road)||(r.Direction!=1&&r.Direction!=2)||!HasMotorDirection(road,r.Direction))return;if(!r.Enable||runtime.EnsureActive()){runtime.Executor.SetMonitoring(road,r.Direction,r.Enable);NoteEffect(road,"拥堵疏导",r.Enable);}Publish();}));
   AddBinding(new TriggerBinding("RoadLaneLock","pick",()=>{TakeStats();parkingOpen=false;monitorOpen=false;viewingVehicles=false;transitOpen=false;picker.Begin();status="移动鼠标到路段，左键选定；右键取消。";Publish();}));
   AddBinding(new TriggerBinding("RoadLaneLock","cancelPick",()=>{parkingOpen=false;monitorOpen=false;viewingVehicles=false;transitOpen=false;picker.End();Publish();}));
   AddBinding(pose=new ValueBinding<string>("RoadLaneLock","pose","{}"));
   AddBinding(state=new ValueBinding<string>("RoadLaneLock","state","{}"));
   L.Refresh(true);AddBinding(strings=new ValueBinding<string>("RoadLaneLock","strings",L.UiJson()));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","resetAll",token=>
   {if(token!=session)return;runtime.ResetAll();status="已清除路段规则。全城紧急车辆优先通行仍然有效。";Publish();}));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","requestRule",json=>
   {
    try
    {
     if(parkingOpen||!Validate(json,out var r,out var e))return;
     if(r.Direction!=1&&r.Direction!=2)return;
     if(!HasMotorDirection(e,r.Direction))return;
     runtime.SetRule(e,r.Direction,r.Enable);NoteEffect(e,"禁止变道",r.Enable);
     // A legacy whole-road rule may include the absent side of a one-way street.
     byte other=(byte)(3-r.Direction);
     if(!r.Enable && (runtime.Executor.DirectionMask(e)&other)!=0 && !HasMotorDirection(e,other))
      runtime.SetRule(e,other,false);
     description=RoadRuleService.Describe(EntityManager,e);describedRoad=e;
     status=r.Enable?"已启用所选行驶方向的车道保持规则；已开始的变道先完成。请观察车辆实际行驶。":"已关闭此路段规则；正在绕行的车辆驶出后恢复正常寻路。";
     Publish();
    }catch(Exception ex){status="设置失败："+ex.Message;Mod.Log.Warn(ex.ToString());Publish();}
   }));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","inspect",json=>
   {
    try
    {
     if(parkingOpen||!Validate(json,out var r,out var e))return;
     status="已记录道路和相邻路口："+RoadInspector.Capture(World,e);Publish();
    }catch(Exception ex){status="记录失败，请将截图发给开发者。详细原因已写入日志。";Mod.Log.Warn(ex.ToString());Publish();}
   }));
  }
  private bool HasMotorDirection(Entity road,byte direction)
  {
   if(!EntityManager.HasBuffer<Game.Net.SubLane>(road))return false;
   foreach(var sub in EntityManager.GetBuffer<Game.Net.SubLane>(road,true))
    if(RoadPickTool.LaneDirection(EntityManager,sub.m_SubLane)==direction)return true;
   return false;
  }
  private bool Validate(string json,out Request r,out Entity e)
  {
   r=null;e=picker.Selected;
   if(json==null||json.Length>512)return false;
   r=JsonConvert.DeserializeObject<Request>(json);
   return r!=null&&r.Session==session&&r.Index==e.Index&&r.Version==e.Version&&RoadInspector.IsRoad(EntityManager,e);
  }
  protected override void OnUpdate(){picker.Tick();runtime.Executor.SampleTurnClock();
   // The game language changed: push the new text table and rebuild text made on this side.
   if(L.Refresh()){strings.Update(L.UiJson());last=null;trafficCache=null;}
   // Any other page (or closing) takes the picker out of junction mode.
   if(junctionOpen&&(!picker.Active||!picker.NodeMode))junctionOpen=false;
   SampleEffects();
   if(++ticks%10==0 || picker.Selected!=describedRoad)Publish();PublishPose();DrawRoadOverlays();DrawTransit();
   try{CheckNewLines();}catch(Exception ex){namingCheckAt=UnityEngine.Time.unscaledTime+30f;Mod.Log.Warn("线路自动命名失败："+ex);}
   try{CheckTransitAutoOpen();}catch(Exception ex){Mod.Log.Warn("公共交通自动打开失败："+ex.Message);}}
  protected override void OnGameLoadingComplete(Purpose purpose,GameMode mode)
  {base.OnGameLoadingComplete(purpose,mode);parkingOpen=false;monitorOpen=false;viewingVehicles=false;recordFeedback=null;lastRoadShape=null;recordedSelection=0;highlightedLine=null;transitOpen=false;hidden.Clear();lastTransit=null;activeMode="*";highlightedVehicle=null;vehicleDots.Clear();junctionOpen=false;laneTab="lane";focusable.Clear();issueStreaks.Clear();ticketPolicy=Entity.Null;lastTransitMeta=null;ResetNaming();picker.ResetSelection();Hook?.ResetStats();stats=new StatsSnapshot();transitFocus=null;lastNative=Entity.Null;ownSelection=Entity.Null;description=null;describedRoad=Entity.Null;session=Guid.NewGuid().ToString("N");last=null;status="规则随城市存档恢复。测试版尚需核对实际行驶效果。";}
  private readonly System.Collections.Generic.List<Entity> ruleOverlayRoads=new System.Collections.Generic.List<Entity>();
  private readonly System.Collections.Generic.HashSet<Entity> monitorOverlayRoads=new System.Collections.Generic.HashSet<Entity>();
  private int overlayRevision=-1;
  private void DrawRoadOverlays()
  {
   if(!picker.Active||picker.Picking||picker.Selected!=previewRoad)previewDirection=0;
   World.GetExistingSystemManaged<RoadMarkings>()?.PreviewSign(previewRoad,monitorOpen?previewDirection:(byte)0);
   if(transitOpen||(!picker.Active&&!monitorOpen&&!viewingVehicles)){overlayRevision=-1;return;}
   if(parkingOpen){DrawParkingOverlay();return;}
   if(junctionOpen){DrawJunctionOverlay();return;}
   var executor=runtime.Executor;
   if(overlayRevision!=executor.RuleRevision){
    executor.CopyProtectedRoads(ruleOverlayRoads);overlayRevision=executor.RuleRevision;
   }
   monitorOverlayRoads.Clear();monitorOverlayRoads.UnionWith(executor.MonitoredRoads);
   var buffer=World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>().GetBuffer(out var writer);
   writer.Complete();
   if(monitorOpen&&previewDirection!=0){
    var anchor=previewDirection==1?first:second;
    if(anchorRoad==previewRoad && anchor.HasValue){
     var pos=anchor.Value.Center+new float3(0,.12f,0);
     var dir=math.normalizesafe(anchor.Value.Ahead-anchor.Value.Center);
     var side=math.normalizesafe(math.cross(new float3(0,1,0),dir));
     var tip=pos+dir*2.5f;var color=new UnityEngine.Color32(255,183,56,255);
     buffer.DrawLine(color,new Line3.Segment(pos-dir*2.5f,tip),.28f);
     buffer.DrawLine(color,new Line3.Segment(tip-dir*1.5f+side,tip),.28f);
     buffer.DrawLine(color,new Line3.Segment(tip-dir*1.5f-side,tip),.28f);
    }
   }
   void Stripe(Entity road,float across,UnityEngine.Color color,float width,bool endpoints=false)
   {
    if(!RoadInspector.IsRoad(EntityManager,road)||!EntityManager.HasComponent<Game.Net.EdgeGeometry>(road))return;
    var geometry=EntityManager.GetComponentData<Game.Net.EdgeGeometry>(road);
    var start=geometry.m_Start;var end=geometry.m_End;
    var a=MathUtils.Lerp(start.m_Left,start.m_Right,across);
    var b=MathUtils.Lerp(end.m_Left,end.m_Right,across);
    if(endpoints){
     a=MathUtils.Cut(a,new float2(0,0.18f));
     b=MathUtils.Cut(b,new float2(0.82f,1));
    }
    buffer.DrawCurve(color,a,width);buffer.DrawCurve(color,b,width);
   }
   void DrawDirections(Entity road,int mask,bool monitor,bool hover=false)
   {
    if(mask==0||!RoadInspector.IsRoad(EntityManager,road)||!EntityManager.HasBuffer<Game.Net.SubLane>(road))return;
    foreach(var sub in EntityManager.GetBuffer<Game.Net.SubLane>(road,true)){
     var lane=sub.m_SubLane;
     if((RoadPickTool.LaneDirection(EntityManager,lane)&mask)==0)continue;
     var curve=EntityManager.GetComponentData<Game.Net.Curve>(lane);
     if(monitor){
      // Monitored direction: pale mint chevrons along every lane, pointing the way it is driven.
      DrawChevrons(buffer,curve.m_Bezier,curve.m_Length,new UnityEngine.Color32(143,227,207,170));
     }else{
      buffer.DrawCurve(hover?new UnityEngine.Color32(110,235,191,155):new UnityEngine.Color32(56,189,248,88),curve.m_Bezier,hover?2.65f:1.75f);
     }
    }
   }
   // Lane-change roads: glowing white solid lines, like the solid lines painted on the road.
   // Each module shows only its own layer: lane-change page = white lines, jam relief page = chevrons.
   // On the 车道规则 page only the rule being edited is drawn.
   // 交通信息 shows only the congestion layer (live flow per road).
   if(viewingVehicles)DrawHeatOverlay(buffer);
   if(!monitorOpen&&!viewingVehicles&&laneTab=="lane")foreach(var road in ruleOverlayRoads)DrawLaneDividers(buffer,road,executor.DirectionMask(road));
   if(!monitorOpen&&!viewingVehicles&&laneTab!="lane")DrawLaneRuleOverlay(buffer);
   if(recorder.Recording)foreach(var road in recorder.RecordingRoads)Stripe(road,0.50f,new UnityEngine.Color32(217,118,255,255),1.3f);
   // Monitored directions: pale mint chevrons; red while a head qualifies but has not left,
   // bright green for a while after a released head drove off.
   if(monitorOpen)foreach(var road in monitorOverlayRoads)
    for(byte d=1;d<=2;d++)
    {
     if(!executor.Monitoring(road,d))continue;
     byte jam=Hook?.RoadState(road,d)??0;
     if(jam==0){DrawDirections(road,d,true);continue;}
     if(!RoadInspector.IsRoad(EntityManager,road)||!EntityManager.HasBuffer<Game.Net.SubLane>(road))continue;
     var glow=jam==1?new UnityEngine.Color32(255,90,78,245):new UnityEngine.Color32(95,240,176,245);
     foreach(var sub in EntityManager.GetBuffer<Game.Net.SubLane>(road,true))
      if(RoadPickTool.LaneDirection(EntityManager,sub.m_SubLane)==d){var c=EntityManager.GetComponentData<Game.Net.Curve>(sub.m_SubLane);DrawChevrons(buffer,c.m_Bezier,c.m_Length,glow);}
    }
   if(viewingVehicles)DrawTrafficOverlay(buffer);
   if(monitorOpen)DrawJamOverlay(buffer);
   if(!monitorOpen&&previewDirection!=0)DrawDirections(previewRoad,previewDirection,false,true);
   if(monitorOpen&&picker.Picking)Stripe(picker.Hovered,0.5f,new UnityEngine.Color32(255,178,65,185),2.4f);
   if(picker.Active){
    var target=picker.Picking?picker.Hovered:picker.Selected;
    Stripe(target,0,new UnityEngine.Color32(255,255,255,160),0.55f,true);Stripe(target,1,new UnityEngine.Color32(255,255,255,160),0.55f,true);
    // With a road chosen, the road under the mouse stays outlined faintly: a click switches to it.
    var other=picker.Hovered;
    if(!picker.Picking&&other!=Entity.Null&&other!=picker.Selected){Stripe(other,0,new UnityEngine.Color32(255,255,255,90),0.45f,true);Stripe(other,1,new UnityEngine.Color32(255,255,255,90),0.45f,true);}
   }
  }
  private string RoadName(Entity e)
  {
   if(EntityManager.HasComponent<Game.Net.Aggregated>(e)){var street=EntityManager.GetComponentData<Game.Net.Aggregated>(e).m_Aggregate;if(EntityManager.Exists(street))e=street;}
   return names.GetRenderedLabelName(e);
  }
  private void Publish()
  {
   var e=picker.Selected;
   bool valid=RoadInspector.IsRoad(EntityManager,e);
   int mask=valid?runtime.Executor.DirectionMask(e):0;
   anchorRoad=valid&&picker.Active?e:Entity.Null;
   parkingAnchor=anchorRoad!=Entity.Null?ParkingAnchor(e):null;
   first=anchorRoad!=Entity.Null?picker.FindAnchor(e,1):null;
   second=anchorRoad!=Entity.Null?picker.FindAnchor(e,2):null;
   var controls=new[]{first.HasValue?new {Direction=1,On=(mask&1)!=0}:null,second.HasValue?new {Direction=2,On=(mask&2)!=0}:null};
   if(e!=describedRoad){describedRoad=e;description=RoadRuleService.Describe(EntityManager,e);}
   bool laneView=picker.Active&&!parkingOpen&&!monitorOpen&&!viewingVehicles&&!transitOpen&&!junctionOpen;
   var json=JsonConvert.SerializeObject(new {LaneTab=laneTab,LaneRules=laneView&&valid&&!picker.Picking?LaneRulesFor(e):null,JunctionOpen=junctionOpen,Junction=junctionOpen?JunctionInfo():null,TransitExempt=Hook?.ExemptCount??0,RuleRoads=laneView?RuleRoads():null,RoadExtras=laneView&&valid&&!picker.Picking?SelectedRoadExtras(e):null,Session=session,Index=e.Index,Version=e.Version,Valid=valid,Controls=controls,ParkingOpen=parkingOpen,ParkingOn=parking.Contains(e),ParkingCount=parking.Count,ParkingStatus=ParkingStatus(e),ParkingReady=Mod.SupportedGame,DiagPath=Diagnostics.SnapshotPath,
    RecordingSelected=valid&&recorder.IsRecordingRoad(e),MonitorOpen=monitorOpen,Traffic=viewingVehicles?TrafficInfo():null,MonitorRoads=monitorOpen?MonitorRoads():null,MonitorCount=monitorOpen?MonitoredDirections():0,JamEvents=monitorOpen?JamEvents():null,JamReady=runtime.HookActive,SelectedShape=monitorOpen&&valid&&!picker.Picking?RoadShape(e):null,Heading1=monitorOpen&&valid?Heading(e,1):null,Heading2=monitorOpen&&valid?Heading(e,2):null,MonitorMask=(runtime.Executor.Monitoring(e,1)?1:0)|(runtime.Executor.Monitoring(e,2)?2:0),MonitorMessage=runtime.Problem.Length>0?runtime.Problem:runtime.Executor.MonitorMessage,RoadName=valid?RoadName(e):null,Street=(laneView||monitorOpen||parkingOpen)&&valid&&!picker.Picking?SelectedStreet(e):null,PaintText=laneView?PaintText():null,NativePanel=EntityManager.Exists(selectedInfo.selectedEntity)&&selectedInfo.selectedEntity!=Entity.Null,TransitFocus=transitOpen?transitFocus:null,
    LaneStats=laneView?new{Total=stats.TotalExits,Road=valid?Count(stats.Exits,e):0}:null,JamStats=monitorOpen?new{Total=stats.TotalJams,Followers=stats.TotalFollowers,Road=valid?Count(stats.Jams,e):0,RoadFollowers=valid?Count(stats.Followers,e):0}:null,ParkStats=parkingOpen?new{Total=stats.TotalCleared,Road=valid?Count(stats.Cleared,e):0,Auto=Mod.Settings==null||Mod.Settings.ClearParkedOnBan}:null,TransitOpen=transitOpen,ViewingVehicles=viewingVehicles,PickingRecordRoad=viewingVehicles&&picker.Picking,RecordFeedback=recordFeedback,RecordedSelection=recordedSelection,LastRoadShape=lastRoadShape,HoverShape=picker.Picking?RoadShape(picker.Hovered):null,VehicleId=SelectedVehicleId(),ToolActive=parkingOpen||monitorOpen||picker.Active||transitOpen||viewingVehicles||junctionOpen,SelectionRevision=picker.SelectionRevision,AnchorX=picker.AnchorX,AnchorY=picker.AnchorY,RecordingRoadCount=recorder.RoadCount,Recording=recorder.Recording,RecordingStatus=recorder.Status,RecordingPath=recorder.FilePath,RecordingFrames=recorder.Frames,RecordingVehicles=recorder.Vehicles,RecordingMs=recorder.LastMilliseconds,Picking=picker.Picking,HoverName=picker.Picking&&RoadInspector.IsRoad(EntityManager,picker.Hovered)?RoadName(picker.Hovered):null,HoverHasRule=picker.HoveredHasRule,HoverIndex=picker.Hovered.Index,HoverValid=picker.Hovered!=Entity.Null,
    Requested=mask!=0,SavedCount=runtime.Executor.SavedRoadCount,PendingCount=runtime.Executor.ProtectedRoadCount,Effective=mask!=0&&runtime.RulesActive,ExecutorReady=(Mod.SupportedGame||runtime.HookActive)&&runtime.Problem.Length==0,Description=valid?description:null,Enabled=runtime.RulesActive,Detours=runtime.Executor.DetourCount,Status=runtime.Problem.Length>0?runtime.Problem:status});
   if(json!=last){last=json;state.Update(json);}
  }
 }
}






