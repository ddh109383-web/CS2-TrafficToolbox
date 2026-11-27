using Colossal.UI.Binding;
using Colossal.Mathematics;
using Game.Net;
using Unity.Entities;
namespace RoadLaneLock
{
 public partial class RuleUISystem
 {
  bool parkingOpen;
  ParkingSystem parking;
  RoadPickTool.Anchor? parkingAnchor;
  void InitParking()
  {
   parking=World.GetOrCreateSystemManaged<ParkingSystem>();
   AddBinding(new TriggerBinding("RoadLaneLock","showParking",()=>{TakeStats();parkingOpen=true;monitorOpen=false;transitOpen=false;viewingVehicles=false;previewDirection=0;picker.Begin();Publish();}));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","parkingRule",json=>{
    if(!parkingOpen||!picker.Active||picker.Picking||!Validate(json,out var request,out var road))return;
    parking.SetRule(road,request.Enable);NoteEffect(road,"禁止路边停车",request.Enable);Publish();
   }));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","clearParking",token=>{
    if(token!=session||!parkingOpen)return;
    var roads=new System.Collections.Generic.List<Entity>(parking.Roads);
    foreach(var road in roads)parking.SetRule(road,false);Publish();
   }));
  }
  RoadPickTool.Anchor? ParkingAnchor(Entity road)
  {
   if(!EntityManager.HasComponent<Curve>(road))return null;
   var curve=EntityManager.GetComponentData<Curve>(road).m_Bezier;
   var center=MathUtils.Position(curve,.5f);center.y+=.5f;
   return new RoadPickTool.Anchor{Center=center,Ahead=center+MathUtils.Tangent(curve,.5f)};
  }
  string ParkingStatus(Entity road)
  {
   if(!Mod.SupportedGame)return "当前游戏版本不受支持，停车操作已停用。";
   if(!RoadInspector.IsRoad(EntityManager,road))return "选择一个路段，设置路边停车。";
   var count=parking.LaneCount(road);
   if(count==0)return parking.Contains(road)?"禁停规则已设；此路段当前没有可控制的路边泊位。":"此路段当前没有可控制的路边泊位。";
   if(!parking.Contains(road))return "沿用原版停车资格";
   bool clear=Mod.Settings==null||Mod.Settings.ClearParkedOnBan;
   return parking.LaneCount(road,true)==count?(clear?"已禁止路边停车 · 已停车辆已清走":"已禁止路边停车 · 已停车辆可自行离开"):"正在应用禁停规则…";
  }
  void DrawParkingOverlay()
  {
   var road=picker.Picking?picker.Hovered:picker.Selected;
   var buffer=World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>().GetBuffer(out var writer);writer.Complete();
   DrawParkingBans(buffer);
   if(EntityManager.HasComponent<Curve>(road))buffer.DrawCurve(new UnityEngine.Color(.44f,.83f,.77f,.85f),EntityManager.GetComponentData<Curve>(road).m_Bezier,2f);
   var other=picker.Hovered;
   if(!picker.Picking&&other!=Entity.Null&&other!=picker.Selected&&EntityManager.HasComponent<Curve>(other))
    buffer.DrawCurve(new UnityEngine.Color(.44f,.83f,.77f,.35f),EntityManager.GetComponentData<Curve>(other).m_Bezier,1.4f);
  }
 }
}

