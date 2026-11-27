using System;
using System.Collections.Generic;
using Colossal.UI.Binding;
using Game.Net;
using Game.Rendering;
using Newtonsoft.Json;
using Unity.Entities;
using Unity.Mathematics;

namespace RoadLaneLock
{
 public partial class RuleUISystem
 {
  sealed class RuleRoadRow
  {
   public int Index,Version,Mask,Detours,Directions,Speed,BannedLanes,Whole;
   public bool Yellow;
   public string Name,Chome;
   public int SameName,Exits;
   [JsonIgnore] public int ChomeNo;
  }
  sealed class RoadTarget {public string Session {get;set;} public int Index {get;set;} public int Version {get;set;} public bool Enable {get;set;}}
  readonly List<Entity> ruleRoadBuffer=new List<Entity>();
  readonly Dictionary<Entity,int> detoursByRoad=new Dictionary<Entity,int>();
  readonly Dictionary<Entity,(string name,int dirs)> ruleRoadInfo=new Dictionary<Entity,(string,int)>();
  int ruleInfoRevision=-1;
  void InitLaneControl()
  {
   // Jump to a road from the rule list: select it in the pick tool and move the camera there.
   AddBinding(new TriggerBinding<string>("RoadLaneLock","focusRuleRoad",json=>{
    if(!TryReadTarget(json,out var t,out var road)||(runtime.Executor.DirectionMask(road)==0&&!Rules.Any(road)))return;
    parkingOpen=false;monitorOpen=false;viewingVehicles=false;transitOpen=false;
    var mid=EntityManager.GetComponentData<Curve>(road).m_Bezier;
    float3 center=Colossal.Mathematics.MathUtils.Position(mid,.5f);
    picker.Select(road,center);
    MoveCamera(center);
    Publish();
   }));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","focusSelectedRoad",json=>{
    if(!TryReadTarget(json,out var t,out var road)||road!=picker.Selected)return;
    MoveCamera(Colossal.Mathematics.MathUtils.Position(EntityManager.GetComponentData<Curve>(road).m_Bezier,.5f));
   }));
   // 分道 buttons folded away when zoomed out: move in close enough to tell the lanes apart.
   AddBinding(new TriggerBinding<string>("RoadLaneLock","zoomSelectedRoad",json=>{
    if(!TryReadTarget(json,out var t,out var road)||road!=picker.Selected)return;
    MoveCamera(Colossal.Mathematics.MathUtils.Position(EntityManager.GetComponentData<Curve>(road).m_Bezier,.5f),120f);
   }));
   // Both travel directions at once (only directions the road actually has).
   AddBinding(new TriggerBinding<string>("RoadLaneLock","requestBoth",json=>{
    try{
     if(parkingOpen||monitorOpen||!TryReadTarget(json,out var t,out var road)||road!=picker.Selected)return;
     for(byte d=1;d<=2;d++)if(HasMotorDirection(road,d))runtime.SetRule(road,d,t.Enable);
     NoteEffect(road,"禁止变道",t.Enable);
     description=RoadRuleService.Describe(EntityManager,road);describedRoad=road;
     status=t.Enable?"已启用两个方向的车道保持规则。":"已关闭此路段两个方向的规则。";
     Publish();
    }catch(Exception ex){status="设置失败："+ex.Message;Mod.Log.Warn(ex.ToString());Publish();}
   }));
  }
  bool TryReadTarget(string json,out RoadTarget t,out Entity road)
  {
   t=null;road=Entity.Null;
   if(json==null||json.Length>512)return false;
   try{t=JsonConvert.DeserializeObject<RoadTarget>(json);}catch{return false;}
   if(t==null||t.Session!=session||t.Index<=0)return false;
   road=new Entity{Index=t.Index,Version=t.Version};
   return RoadInspector.IsRoad(EntityManager,road)&&EntityManager.HasComponent<Curve>(road);
  }
  void MoveCamera(float3 target,float zoomAtMost=0)
  {
   var cameras=World.GetExistingSystemManaged<CameraUpdateSystem>();
   var game=cameras?.gamePlayController;
   if(game==null)return;
   if(cameras.activeCameraController!=game){game.TryMatchPosition(cameras.activeCameraController);cameras.activeCameraController=game;}
   game.pivot=target;
   if(zoomAtMost>0&&game.zoom>zoomAtMost)game.zoom=zoomAtMost;
  }
  List<RuleRoadRow> RuleRoads()
  {
   var executor=runtime.Executor;
   executor.CopyProtectedRoads(ruleRoadBuffer);
   if(ruleInfoRevision!=executor.RuleRevision){ruleRoadInfo.Clear();ruleInfoRevision=executor.RuleRevision;}
   executor.CountDetoursByRoad(detoursByRoad);
   var rows=new List<RuleRoadRow>();
   // Roads with any 车道规则: lane change (executor) and the others (RoadRules).
   var r=Rules;
   foreach(var road in r.Speed.Keys)if(!ruleRoadBuffer.Contains(road))ruleRoadBuffer.Add(road);
   foreach(var road in r.LaneBans.Keys)if(!ruleRoadBuffer.Contains(road))ruleRoadBuffer.Add(road);
   foreach(var road in r.DoubleYellow)if(!ruleRoadBuffer.Contains(road))ruleRoadBuffer.Add(road);
   foreach(var road in ruleRoadBuffer){
    if(!RoadInspector.IsRoad(EntityManager,road))continue;
    if(!ruleRoadInfo.TryGetValue(road,out var info))
     ruleRoadInfo[road]=info=(RoadName(road),(HasMotorDirection(road,1)?1:0)|(HasMotorDirection(road,2)?2:0));
    detoursByRoad.TryGetValue(road,out int detours);
    r.Speed.TryGetValue(road,out int speed);
    int banned=0,whole=0;
    if(r.LaneBans.TryGetValue(road,out var map))banned=map.Count;
    for(byte d=1;d<=2;d++)if((r.WholeBan(road,d)&RoadRules.KCar)!=0)whole|=d;
    rows.Add(new RuleRoadRow{Index=road.Index,Version=road.Version,Name=info.name,Chome=ChomeOf(road),ChomeNo=Chome(road).chome,SameName=SameNameStreets(road),Exits=Count(stats.Exits,road),Mask=executor.DirectionMask(road),Directions=info.dirs,Detours=detours,
     Speed=speed,Yellow=r.DoubleYellow.Contains(road),BannedLanes=banned,Whole=whole});
   }
   rows.Sort((a,b)=>{int c=string.CompareOrdinal(a.Name??"",b.Name??"");return c!=0?c:a.ChomeNo.CompareTo(b.ChomeNo);});
   return rows;
  }
  // Name of a street met at the given node, other than the selected street itself.
  string TowardName(Entity road,Entity node,string own)
  {
   if(!EntityManager.Exists(node)||!EntityManager.HasBuffer<ConnectedEdge>(node))return null;
   foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(node,true)){
    if(c.m_Edge==road||!RoadInspector.IsRoad(EntityManager,c.m_Edge))continue;
    var name=RoadName(c.m_Edge);
    if(!string.IsNullOrEmpty(name)&&name!=own)return name;
   }
   return null;
  }
  object SelectedRoadExtras(Entity road)
  {
   if(!RoadInspector.IsRoad(EntityManager,road)||!EntityManager.HasComponent<Curve>(road))return null;
   var own=RoadName(road);
   var edge=EntityManager.GetComponentData<Edge>(road);
   // Direction 1 travels toward the start node, direction 2 toward the end node (same as the monitor labels).
   return new{Length=(int)math.round(EntityManager.GetComponentData<Curve>(road).m_Length),Toward1=TowardName(road,edge.m_Start,own),Toward2=TowardName(road,edge.m_End,own)};
  }
 }
}
