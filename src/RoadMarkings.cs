using System.Collections.Generic;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using LaneFlags = Game.Prefabs.LaneFlags;

namespace RoadLaneLock
{
 public partial class RoadMarkings : GameSystemBase
 {
  sealed class Drawing
  {
   public Entity Composition;
   public bool WaitingForLanes,Parking;
   public byte Directions,Monitor;
   public readonly Renderer[] Signs=new Renderer[2];
   public EdgeGeometry Geometry;
   public GameObject Root;
   public readonly List<Mesh> Meshes=new List<Mesh>();
  }

  readonly List<Entity> roads = new List<Entity>();
  readonly HashSet<Entity> enabledRoads = new HashSet<Entity>();
  readonly Dictionary<Entity, Drawing> drawings = new Dictionary<Entity, Drawing>();
  readonly List<Entity> removed = new List<Entity>();
  Material paint;
  bool shaderUnavailable;
  int revision = -1,monitorRevision = -1,parkingRevision=-1;
  float nextCheck;bool navigationVisible;

  protected override void OnUpdate()
  {
   var coordinator = World.GetExistingSystemManaged<RuntimeCoordinator>();
   var executor = coordinator?.Executor;
   if(executor==null)return;
   try{UpdateRuleMarkings();}catch(System.Exception ex){nextRuleCheck=UnityEngine.Time.unscaledTime+30f;Mod.Log.Warn("车道规则标线失败："+ex.Message);}
   var parking=World.GetExistingSystemManaged<ParkingSystem>();
   bool changed=navigationVisible!=coordinator.RulesActive || revision!=executor.RuleRevision || monitorRevision!=executor.MonitorRevision || parkingRevision!=(parking?.Revision??0);
   if (!changed && UnityEngine.Time.unscaledTime < nextCheck) return;
   var language=LegendForLocale();
   if(language!=legend||parkingLocale!=ParkingLocale()){Clear();legend=language;parkingLocale=ParkingLocale();changed=true;}
   if(changed)
   {
    navigationVisible=coordinator.RulesActive;roads.Clear();
    if(navigationVisible)executor.CopyProtectedRoads(roads);
    enabledRoads.Clear();
    enabledRoads.UnionWith(roads);
    if(navigationVisible)foreach(var road in executor.MonitoredRoads)if(enabledRoads.Add(road))roads.Add(road);
    if(parking!=null)foreach(var road in parking.Roads)if(enabledRoads.Add(road))roads.Add(road);
    removed.Clear();
    foreach (var entry in drawings)
     if (!enabledRoads.Contains(entry.Key)) removed.Add(entry.Key);
    foreach (var road in removed) Remove(road);
    revision = executor.RuleRevision;
    monitorRevision = executor.MonitorRevision;parkingRevision=parking?.Revision??0;
   }

   nextCheck = UnityEngine.Time.unscaledTime + 1f;
   foreach (var road in roads)
   {
    if (!RoadInspector.IsRoad(EntityManager, road) || !EntityManager.HasComponent<EdgeGeometry>(road)||!EntityManager.HasComponent<Composition>(road))
    {
     Remove(road);
     continue;
    }
    var geometry = EntityManager.GetComponentData<EdgeGeometry>(road);
    var composition = EntityManager.GetComponentData<Composition>(road).m_Edge;
    byte directions = navigationVisible?executor.DirectionMask(road):(byte)0;
    byte monitor=navigationVisible?(byte)((executor.Monitoring(road,1)?1:0)|(executor.Monitoring(road,2)?2:0)):(byte)0;
    if (drawings.TryGetValue(road, out var cached) && !cached.WaitingForLanes && cached.Directions == directions && cached.Monitor==monitor && cached.Parking==(parking?.Contains(road)??false) && cached.Composition == composition && cached.Geometry.Equals(geometry)) continue;
    Remove(road);
    if (!EntityManager.HasComponent<NetCompositionData>(composition) || !EntityManager.HasBuffer<NetCompositionLane>(composition)) continue;
    var drawing = new Drawing { Directions = directions, Monitor=monitor, Parking=parking?.Contains(road)??false, Composition = composition, Geometry = geometry };
    drawings.Add(road, drawing);
    Draw(road,drawing);
   }
  }

  void Draw(Entity road,Drawing drawing)
  {
   var width = EntityManager.GetComponentData<NetCompositionData>(drawing.Composition).m_Width;
   if(width<=0||!EntityManager.HasBuffer<Game.Net.SubLane>(road)||EntityManager.GetBuffer<Game.Net.SubLane>(road,true).Length==0){drawing.WaitingForLanes=true;return;}
   if(EntityManager.HasComponent<Curve>(road)){PlaceMarker(road,drawing,1);PlaceMarker(road,drawing,2);PlaceParkingSigns(road,drawing);}
   var markings = new List<Bezier4x3>();
   foreach(var sub in EntityManager.GetBuffer<Game.Net.SubLane>(road,true))
   {
    var lane=sub.m_SubLane;
    if(!EntityManager.HasComponent<LaneGeometry>(lane)||!EntityManager.HasComponent<Curve>(lane)||
       !EntityManager.HasComponent<PrefabRef>(lane)||EntityManager.HasComponent<Game.Common.Deleted>(lane))continue;
    var prefab=EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
    if(EntityManager.HasComponent<NetLaneData>(prefab)&&(EntityManager.GetComponentData<NetLaneData>(prefab).m_Flags&LaneFlags.Secondary)!=0)
     markings.Add(EntityManager.GetComponentData<Curve>(lane).m_Bezier);
   }
   var lanes = new List<NetCompositionLane>();
   foreach (var lane in EntityManager.GetBuffer<NetCompositionLane>(drawing.Composition, true))
    if ((lane.m_Flags & LaneFlags.Road) != 0 && (lane.m_Flags & LaneFlags.Master) == 0) lanes.Add(lane);
   if(drawing.Directions!=0)foreach(var lane in lanes)PaintLane(road,drawing,lane,width);
   lanes.Sort((a,b) => a.m_Position.x.CompareTo(b.m_Position.x));
   for (int i = 1; i < lanes.Count; ++i)
   {
    var a = lanes[i-1];
    var b = lanes[i];
    const LaneFlags excluded = LaneFlags.Parking | LaneFlags.BicyclesOnly | LaneFlags.Track | LaneFlags.Twoway;
    if (((a.m_Flags | b.m_Flags) & excluded) != 0 || ((a.m_Flags ^ b.m_Flags) & LaneFlags.Invert) != 0 ||
        a.m_Carriageway != b.m_Carriageway || math.abs(a.m_Position.y-b.m_Position.y) > 0.1f ||
        !EntityManager.HasComponent<NetLaneData>(a.m_Lane) || !EntityManager.HasComponent<NetLaneData>(b.m_Lane)) continue;
    float right = a.m_Position.x + EntityManager.GetComponentData<NetLaneData>(a.m_Lane).m_Width * 0.5f;
    float left = b.m_Position.x - EntityManager.GetComponentData<NetLaneData>(b.m_Lane).m_Width * 0.5f;
    if (math.abs(right-left) > 0.45f) continue;
    float t = 0.5f + (right+left) * 0.5f / width;
    if (t <= 0 || t >= 1) continue;
    var start = drawing.Geometry.m_Start;
    var end = drawing.Geometry.m_End;
    var first = MathUtils.Lerp(start.m_Left,start.m_Right,t);
    var last = MathUtils.Lerp(end.m_Left,end.m_Right,t);
    var centerA=MathUtils.Lerp(start.m_Left,start.m_Right,0.5f+a.m_Position.x/width);
    var centerB=MathUtils.Lerp(start.m_Left,start.m_Right,0.5f+b.m_Position.x/width);
    byte direction=RoadPickTool.DirectionAt(EntityManager,road,MathUtils.Position(centerA,0.5f));
    if((drawing.Directions&direction)==0||RoadPickTool.DirectionAt(EntityManager,road,MathUtils.Position(centerB,0.5f))!=direction)continue;
    var points = new List<Vector3>();
    float lift = (a.m_Position.y+b.m_Position.y)*0.5f + 0.035f;
    Sample(first, lift, points, markings);
    Sample(last, lift, points, markings);
    if (points.Count < 2 || !EnsureMaterial()) continue;
    if (drawing.Root == null) drawing.Root = new GameObject("RoadLaneLock markings") { hideFlags = HideFlags.DontSave };
    var stripe = new GameObject("Lane divider") { hideFlags = HideFlags.DontSave };
    stripe.transform.SetParent(drawing.Root.transform,false);
    var mesh=Ribbon(points);
    drawing.Meshes.Add(mesh);
    stripe.AddComponent<MeshFilter>().sharedMesh=mesh;
    var renderer=stripe.AddComponent<MeshRenderer>();
    renderer.sharedMaterial=paint;
    renderer.shadowCastingMode=ShadowCastingMode.Off;
   }
  }

  static Mesh Ribbon(List<Vector3> points)=>Ribbon(points,0.16f);
  static Mesh Ribbon(List<Vector3> points,float half)
  {
   // Two vertices across each sample. Upward winding gives paint the same
   // light-facing surface on either travel direction, including sloped roads.
   var vertices=new Vector3[points.Count*2];
   var triangles=new int[(points.Count-1)*6];
   for(int i=0;i<points.Count;i++)
   {
    var forward=points[Mathf.Min(i+1,points.Count-1)]-points[Mathf.Max(i-1,0)];
    var side=Vector3.Cross(Vector3.up,forward).normalized*half;
    int v=i*2;
    vertices[v]=points[i]-side;
    vertices[v+1]=points[i]+side;
    if(i==points.Count-1)continue;
    int j=i*6;
    triangles[j]=v;triangles[j+1]=v+2;triangles[j+2]=v+1;
    triangles[j+3]=v+1;triangles[j+4]=v+2;triangles[j+5]=v+3;
   }
   var mesh=new Mesh {name="Lane divider paint",hideFlags=HideFlags.DontSave,vertices=vertices,triangles=triangles};
   mesh.RecalculateNormals();
   mesh.RecalculateBounds();
   return mesh;
  }

  static void Sample(Bezier4x3 curve,float lift,List<Vector3> points,List<Bezier4x3> markings)
  {
   float length = math.distance(curve.a,curve.b)+math.distance(curve.b,curve.c)+math.distance(curve.c,curve.d);
   int steps = math.clamp((int)math.ceil(length/2f),1,1024);
   for (int i=0;i<=steps;i++)
   {
    float t=(float)i/steps;
    float3 surface=MathUtils.Position(curve,t);
    surface.y+=lift-0.035f;
    float3 tangent=math.normalizesafe(MathUtils.Tangent(curve,t));
    float3 aligned=surface;
    float nearest=0.45f;
    foreach(var marking in markings)
    {
     float distance=MathUtils.Distance(marking,surface,out var along);
     if(distance>=nearest)continue;
     float3 axis=math.normalizesafe(MathUtils.Tangent(marking,along));
     if(math.abs(math.dot(axis,tangent))<0.97f)continue;
     var match=MathUtils.Position(marking,along);
     // Only snap across the lane; preserve the full node-to-node extent.
     if(math.abs(math.dot(match-surface,tangent))>0.07f)continue;
     aligned=match;
     nearest=distance;
    }
    Vector3 p=aligned;
    p.y+=0.035f;
    if (points.Count > 0 && (points[points.Count-1]-p).sqrMagnitude < 0.000001f) continue;
    points.Add(p);
   }
  }

  bool EnsureMaterial()
  {
   if (paint != null) return true;
   if (shaderUnavailable) return false;
   var shader = Shader.Find("HDRP/Lit");
   if (shader == null || !shader.isSupported)
   {
    shaderUnavailable = true;
    Mod.Log.Warn("Road markings unavailable: HDRP/Lit shader missing.");
    return false;
   }
   paint = new Material(shader) { hideFlags = HideFlags.DontSave };
   paint.SetColor("_BaseColor",new Color(0.85f,0.84f,0.80f));
   paint.SetFloat("_Smoothness",0.15f);
   // Small visibility floor in dark scenes; street lamps still light the base coat.
   paint.SetColor("_EmissiveColor",new Color(0.012f,0.012f,0.012f));
   // Road paint is visible from above regardless of the direction of the divider.
   paint.SetFloat("_CullMode",0);
   paint.SetFloat("_CullModeForward",0);
   // Validation derives culling from _DoubleSidedEnable, so declare it before validating.
   paint.SetFloat("_DoubleSidedEnable",1);
   SignHardware.Validate(paint);
   return true;
  }

  static void DestroyDrawing(Drawing drawing)
  {
   if(drawing.Root!=null)Object.Destroy(drawing.Root);
   foreach(var mesh in drawing.Meshes)Object.Destroy(mesh);
  }

  void Remove(Entity road)
  {
   if (!drawings.TryGetValue(road,out var drawing)) return;
   DestroyDrawing(drawing);
   drawings.Remove(road);
  }
  void Clear()
  {
   foreach (var drawing in drawings.Values) DestroyDrawing(drawing);
   drawings.Clear();
   roads.Clear();
   enabledRoads.Clear();
   revision = -1;
  }
  internal void Shutdown()
  {
   Clear();ClearRuleMarkings();
   if (paint != null) Object.Destroy(paint);
   DisposeMarkers();
   paint = null;
   Enabled = false;
  }
  protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
  {
   base.OnGameLoadingComplete(purpose,mode);
   Clear();ClearRuleMarkings();
  }
  protected override void OnDestroy() { Shutdown(); base.OnDestroy(); }
 }
}



