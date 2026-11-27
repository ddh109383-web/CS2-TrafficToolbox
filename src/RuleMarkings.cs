using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoadLaneLock
{
 // Road paint for the 车道规则 rules, so each rule is visible on the road itself:
 //  speed limit = the number painted on every lane; 分道 = crossed pictograms of the banned kinds on each lane
 //  (or the one kind a lane is reserved for); double yellow = two yellow lines on the centre line plus a
 //  no-pedestrian and no-U-turn sign on every lane; closed crossings = a no-pedestrian sign on each lane
 //  driving into the junction, just before the crossing. Signs face the traffic of their lane.
 public partial class RoadMarkings
 {
  readonly Dictionary<Entity,Drawing> ruleDrawings=new Dictionary<Entity,Drawing>();
  readonly Dictionary<Entity,string> ruleKeys=new Dictionary<Entity,string>();
  readonly List<Entity> ruleStale=new List<Entity>();
  int ruleRevision=-1;float nextRuleCheck;
  Material yellow,asphalt;

  static readonly int[] SpeedArt={20,30,40,50,60,70,80,100,120};

  void UpdateRuleMarkings()
  {
   var rules=World.GetExistingSystemManaged<RoadRules>();
   if(rules==null)return;
   if(ruleRevision==rules.Revision&&UnityEngine.Time.unscaledTime<nextRuleCheck)return;
   ruleRevision=rules.Revision;nextRuleCheck=UnityEngine.Time.unscaledTime+1.5f;
   var wanted=new HashSet<Entity>();
   foreach(var r in rules.Speed.Keys)wanted.Add(r);
   wanted.UnionWith(rules.DoubleYellow);
   foreach(var r in rules.LaneBans.Keys)wanted.Add(r);
   // Junctions closed on the 路口规则 page (double-yellow joints are signed on the road itself).
   var crossingNodes=new HashSet<Entity>(rules.NoCrossing);
   wanted.UnionWith(crossingNodes);
   ruleStale.Clear();
   foreach(var road in ruleDrawings.Keys)if(!wanted.Contains(road))ruleStale.Add(road);
   foreach(var road in ruleStale)RemoveRule(road);
   foreach(var road in wanted)
   {
    if(crossingNodes.Contains(road)&&!RoadInspector.IsRoad(EntityManager,road))
    {
     if(ruleDrawings.ContainsKey(road)||!EntityManager.Exists(road))continue;
     var nodeDrawing=new Drawing();
     if(DrawCrossings(road,nodeDrawing)){ruleDrawings[road]=nodeDrawing;ruleKeys[road]="node";}else DestroyDrawing(nodeDrawing);
     continue;
    }
    if(!RoadInspector.IsRoad(EntityManager,road)||!EntityManager.HasComponent<EdgeGeometry>(road)){RemoveRule(road);continue;}
    var geometry=EntityManager.GetComponentData<EdgeGeometry>(road);
    string key=Key(rules,road)+"|"+geometry.m_Start.m_Left.a+geometry.m_End.m_Right.d;
    if(ruleKeys.TryGetValue(road,out var old)&&old==key)continue;
    RemoveRule(road);
    var drawing=new Drawing{Geometry=geometry};
    if(DrawRules(rules,road,drawing)){ruleDrawings[road]=drawing;ruleKeys[road]=key;}
    else DestroyDrawing(drawing);
   }
  }

  static string Key(RoadRules rules,Entity road)
  {
   rules.Speed.TryGetValue(road,out int speed);
   var bans=new System.Text.StringBuilder();
   if(rules.LaneBans.TryGetValue(road,out var map))foreach(var p in map)bans.Append(p.Key).Append(':').Append(p.Value).Append(',');
   return speed+"/"+rules.DoubleYellow.Contains(road)+"/"+bans;
  }

  void RemoveRule(Entity road)
  {
   if(ruleDrawings.TryGetValue(road,out var d)){DestroyDrawing(d);ruleDrawings.Remove(road);}
   ruleKeys.Remove(road);
  }
  void ClearRuleMarkings(){foreach(var d in ruleDrawings.Values)DestroyDrawing(d);ruleDrawings.Clear();ruleKeys.Clear();ruleRevision=-1;}

  static readonly string[] KindArt={"car","taxi","bus","truck","service","bike","moto"}; // by kind bit

  // The longest piece of a lane (long roads carry their lanes in two halves).
  Entity Longest(List<Entity> group)
  {
   Entity best=Entity.Null;float length=-1;
   foreach(var lane in group)if(EntityManager.HasComponent<Curve>(lane)){float l=EntityManager.GetComponentData<Curve>(lane).m_Length;if(l>length){length=l;best=lane;}}
   return best;
  }
  float LaneWidth(Entity lane)
  {
   float width=3f;
   if(EntityManager.HasComponent<Game.Prefabs.PrefabRef>(lane))
   {
    var prefab=EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(lane).m_Prefab;
    if(EntityManager.HasComponent<Game.Prefabs.NetLaneData>(prefab))width=EntityManager.GetComponentData<Game.Prefabs.NetLaneData>(prefab).m_Width;
   }
   return width;
  }

  bool DrawRules(RoadRules rules,Entity road,Drawing drawing)
  {
   if(!EnsureMaterial())return false;
   bool any=false;
   rules.Speed.TryGetValue(road,out int speed);
   string speedArt=null;
   if(speed>0){int best=SpeedArt[0];foreach(var n in SpeedArt)if(math.abs(n-speed)<math.abs(best-speed))best=n;speedArt="Paint-speed-"+best;}
   bool yellowRoad=rules.DoubleYellow.Contains(road);
   for(byte d=1;d<=2;d++)
   {
    var groups=rules.LaneGroups(road,d);
    for(int i=0;i<groups.Count;i++)
    {
     var lane=Longest(groups[i]);
     if(lane==Entity.Null)continue;
     var curve=EntityManager.GetComponentData<Curve>(lane);
     float across=math.min(LaneWidth(lane)*.8f,3f);
     float icon=across*.75f;
     if(speedArt!=null)any|=Symbol(drawing,speedArt,curve,.22f,across*.9f,across*1.5f);
     // 分道: one kind left (motor vehicles) = a reserved lane, its plain pictogram; otherwise each banned kind crossed.
     byte banned=rules.Banned(road,d,i);
     if(banned!=0)
     {
      var arts=new List<string>();
      byte motorOpen=(byte)(RoadRules.KHook&~banned);
      if(motorOpen!=0&&(motorOpen&(motorOpen-1))==0)arts.Add("Paint-only"+KindArt[math.tzcnt((int)motorOpen)]);
      else for(int k=0;k<7;k++)if(k!=5&&(banned&(1<<k))!=0)arts.Add("Paint-no"+KindArt[k]);
      if((banned&RoadRules.KBike)!=0)arts.Add("Paint-nobike");
      float step=icon*1.25f/math.max(curve.m_Length,1f);
      float start=.5f-step*(arts.Count-1)*.5f;
      for(int k=0;k<arts.Count;k++)any|=Symbol(drawing,arts[k],curve,math.clamp(start+step*k,.08f,.92f),icon,icon);
     }
     if(yellowRoad){any|=Symbol(drawing,"Paint-noped",curve,.7f,icon,icon);any|=Symbol(drawing,"Paint-nouturn",curve,.7f+icon*1.3f/math.max(curve.m_Length,1f),icon,icon);}
    }
   }
   if(yellowRoad)any|=CentreLines(rules,road,drawing);
   return any;
  }

  // A no-pedestrian sign on every lane driving into the junction, a few metres before the crossing.
  bool DrawCrossings(Entity node,Drawing drawing)
  {
   if(!EnsureMaterial()||!EntityManager.HasBuffer<ConnectedEdge>(node)||!EntityManager.HasComponent<Node>(node))return false;
   var rules=World.GetExistingSystemManaged<RoadRules>();
   var at=EntityManager.GetComponentData<Node>(node).m_Position;
   bool any=false;
   foreach(var c in EntityManager.GetBuffer<ConnectedEdge>(node,true))
   {
    var road=c.m_Edge;
    if(!RoadInspector.IsRoad(EntityManager,road))continue;
    for(byte d=1;d<=2;d++)
    foreach(var group in rules.LaneGroups(road,d))
    {
     // The piece that drives into the junction: its end is the nearer end to the node.
     Entity lane=Entity.Null;float near=float.MaxValue;
     foreach(var piece in group)
     {
      var b=EntityManager.GetComponentData<Curve>(piece).m_Bezier;
      float toEnd=math.distance(b.d.xz,at.xz);
      if(toEnd<math.distance(b.a.xz,at.xz)&&toEnd<near){near=toEnd;lane=piece;}
     }
     if(lane==Entity.Null)continue;
     var curve=EntityManager.GetComponentData<Curve>(lane);
     float icon=math.min(LaneWidth(lane)*.8f,3f)*.75f;
     float t=1f-(icon*.5f+2.5f)/math.max(curve.m_Length,1f);
     if(t>.3f)any|=Symbol(drawing,"Paint-noped",curve,t,icon,icon);
    }
   }
   return any;
  }
  // One stencil lying on the lane at fraction t of its length (in the driving direction).
  bool Symbol(Drawing drawing,string art,Curve curve,float t,float across,float along)
  {
   if(curve.m_Length<along*2.2f)return false;
   var mesh=Stencil(art);
   if(!mesh)return false;
   Vector3 point=MathUtils.Position(curve.m_Bezier,t);
   Vector3 forward=((Vector3)MathUtils.Tangent(curve.m_Bezier,t)).normalized;
   Vector3 normal=Vector3.up;
   Stamp(drawing,art,mesh,point+normal*.06f,Quaternion.LookRotation(-normal,forward),new Vector3(across,along,1),paint);
   return true;
  }

  // Two yellow lines midway between the innermost lanes of the two directions.
  bool CentreLines(RoadRules rules,Entity road,Drawing drawing)
  {
   var a=rules.LaneGroups(road,1);var b=rules.LaneGroups(road,2);
   if(a.Count==0||b.Count==0)return false;
   var inner1=EntityManager.GetComponentData<Curve>(Longest(a[a.Count-1])).m_Bezier;
   var inner2=EntityManager.GetComponentData<Curve>(Longest(b[b.Count-1])).m_Bezier;
   float length=math.distance(inner1.a,inner1.d);
   int steps=math.clamp((int)math.ceil(length/2f),2,512);
   var left=new List<Vector3>();var right=new List<Vector3>();var gap=new List<Vector3>();
   for(int i=0;i<=steps;i++)
   {
    float t=(float)i/steps;
    float3 p=MathUtils.Position(inner1,t);
    MathUtils.Distance(inner2,p,out float u);
    float3 q=MathUtils.Position(inner2,u);
    if(math.distance(p.xz,q.xz)>9f)return false; // a median or something else in between: no centre line
    float3 centre=(p+q)*.5f;
    float3 side=math.normalizesafe(new float3(q.x-p.x,0,q.z-p.z));
    centre.y+=.05f;
    // Two 15 cm lines with a 20 cm gap; the gap is painted asphalt-dark so the game's own single
    // centre line under it does not merge them into one wide band.
    left.Add(centre-side*.175f);right.Add(centre+side*.175f);gap.Add(centre+new float3(0,.004f,0));
   }
   if(!yellow){yellow=Tint(new Color(.93f,.72f,.16f),.04f);}
   if(!asphalt){asphalt=Tint(new Color(.13f,.135f,.14f),.02f);}
   void Line(List<Vector3> points,float half,Material material)
   {
    var mesh=Ribbon(points,half);
    drawing.Meshes.Add(mesh);
    var part=Part(drawing,"Double yellow");
    part.AddComponent<MeshFilter>().sharedMesh=mesh;
    var renderer=part.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
   }
   Line(gap,.1f,asphalt);
   Line(left,.075f,yellow);Line(right,.075f,yellow);
   return true;
  }
 }
}
