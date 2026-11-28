using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Tools;
using Game.Prefabs;
using Game.Rendering;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace RoadLaneLock
{
 public partial class RoadPickTool : ToolBaseSystem
 {
  public override string toolID => "RoadLaneLock.Pick";
  public Entity Selected {get;private set;}
  public Entity Hovered {get;private set;}
  // Active = the panel's picker is engaged. While "parked" the game's default tool runs
  // (so its vehicle info panel and route view work) and the panel keeps its road.
  public bool Active=>OwnsTool||parked;
  private bool OwnsTool=>m_ToolSystem.activeTool==this;
  private bool selecting,parked;
  private Entity parkedSelection;
  private int parkRetries;
  public bool HoveredHasRule=>Hovered!=Entity.Null&&World.GetExistingSystemManaged<Game.Simulation.RoadLaneLockNavigationSystem>().DirectionMask(Hovered)!=0;
  public bool Picking=>Active&&selecting;
  public int SelectionRevision {get;private set;}
  public float AnchorX {get;private set;}=0.5f;
  public float AnchorY {get;private set;}=0.3f;
  public void Begin()
  {
   Selected=Entity.Null;Hovered=Entity.Null;selecting=true;parked=false;m_ToolSystem.activeTool=this;
   NodeMode=false;HoveredNode=Entity.Null;SelectedNode=Entity.Null;
   RangeMode=false;RangeStart=Entity.Null;RangeEnd=Entity.Null;
  }
  // G37 (限速 by range): hover junctions, click the start and then the end; the panel finds the roads between.
  public bool RangeMode;
  public Entity RangeStart{get;private set;}
  public Entity RangeEnd{get;private set;}
  public void BeginRange(){Begin();RangeMode=true;SelectionRevision++;}
  public void ResetRange(){RangeStart=Entity.Null;RangeEnd=Entity.Null;SelectionRevision++;}
  // Junction mode (路口规则): hovering/clicking picks the junction node nearest the pointer instead of a road.
  public bool NodeMode;
  public Entity HoveredNode{get;private set;}
  public Entity SelectedNode{get;private set;}
  public void BeginNodes(){Begin();NodeMode=true;}
  Entity ResolveNode(Entity hit,float3 position)
  {
   if(EntityManager.HasComponent<Node>(hit)&&EntityManager.HasBuffer<ConnectedEdge>(hit))return hit;
   if(!EntityManager.HasComponent<Edge>(hit))return Entity.Null;
   var edge=EntityManager.GetComponentData<Edge>(hit);
   Entity best=Entity.Null;float nearest=45f;
   foreach(var node in new[]{edge.m_Start,edge.m_End})
   {
    if(!EntityManager.HasComponent<Node>(node))continue;
    float d=math.distance(EntityManager.GetComponentData<Node>(node).m_Position.xz,position.xz);
    if(d<nearest){nearest=d;best=node;}
   }
   return best;
  }
  public void End(){NodeMode=false;RangeMode=false;RangeStart=Entity.Null;RangeEnd=Entity.Null;HoveredNode=Entity.Null;SelectedNode=Entity.Null;Selected=Entity.Null;if(OwnsTool)m_ToolSystem.activeTool=m_DefaultToolSystem;selecting=false;parked=false;Hovered=Entity.Null;}
  // Hand the mouse to the game's default tool and show this object in its info panel.
  private void Park(Entity entity)
  {
   parked=true;Hovered=Entity.Null;
   m_ToolSystem.activeTool=m_DefaultToolSystem;
   World.GetOrCreateSystemManaged<Game.UI.InGame.SelectedInfoUISystem>().SetSelection(entity);
   parkedSelection=entity;parkRetries=10;
  }
  // Called every UI frame. A road clicked with the default tool takes the panel back;
  // any other tool ends the parked state.
  public void Tick()
  {
   if(!parked)return;
   var tool=m_ToolSystem.activeTool;
   if(tool==this){parked=false;return;}
   if(tool!=m_DefaultToolSystem){parked=false;Selected=Entity.Null;selecting=false;return;}
   var native=m_ToolSystem.selected;
   // The default tool may reset the selection when it starts; re-apply it for a few frames.
   if(parkRetries>0){parkRetries--;if(native!=parkedSelection&&!RoadInspector.IsRoad(EntityManager,native)){World.GetOrCreateSystemManaged<Game.UI.InGame.SelectedInfoUISystem>().SetSelection(parkedSelection);return;}}
   if(native==parkedSelection||!RoadInspector.IsRoad(EntityManager,native)||!EntityManager.HasComponent<Curve>(native))return;
   var road=native;
   Select(road,Colossal.Mathematics.MathUtils.Position(EntityManager.GetComponentData<Curve>(road).m_Bezier,0.5f));
  }
  // Select a road from the panel (rule list) as if it had been clicked.
  public void Select(Entity road,float3 position)
  {
   parked=false;m_ToolSystem.activeTool=this;Hovered=Entity.Null;selecting=false;
   Selected=road;SelectedPosition=position;SelectionRevision++;
  }
  // Let go of the chosen road and go back to choosing (right click / "选择另一条道路").
  public void Deselect()
  {
   Selected=Entity.Null;Hovered=Entity.Null;SelectedNode=Entity.Null;selecting=true;parked=false;SelectionRevision++;
   if(!OwnsTool)m_ToolSystem.activeTool=this;
  }
  public void UseNativeSelection(){End();if(m_ToolSystem.activeTool!=m_DefaultToolSystem)m_ToolSystem.activeTool=m_DefaultToolSystem;}
  public void ResetSelection(){End();Selected=Entity.Null;}
  public override PrefabBase GetPrefab()=>null;
  public override bool TrySetPrefab(PrefabBase prefab)=>false;
  protected override bool GetAllowApply()=>true;
  protected override void OnStartRunning(){base.OnStartRunning();applyAction.shouldBeEnabled=true;secondaryApplyAction.shouldBeEnabled=true;}
  protected override void OnStopRunning(){applyAction.shouldBeEnabled=false;secondaryApplyAction.shouldBeEnabled=false;Hovered=Entity.Null;base.OnStopRunning();}
  public override void InitializeRaycast()
  {
   base.InitializeRaycast();
   // Vehicles stay clickable: they open the game's own info panel.
   m_ToolRaycastSystem.typeMask=TypeMask.Net|TypeMask.MovingObjects;
   m_ToolRaycastSystem.netLayerMask=Layer.Road|Layer.PublicTransportRoad;
   m_ToolRaycastSystem.collisionMask=CollisionMask.OnGround|CollisionMask.Overground;
   m_ToolRaycastSystem.raycastFlags=RaycastFlags.SubElements|RaycastFlags.Markers;
  }
  private Entity ResolveSegment(Entity hit,float3 position)
  {
   if(RoadInspector.IsRoad(EntityManager,hit))return hit;
   if(!EntityManager.HasComponent<Node>(hit)||!EntityManager.HasBuffer<ConnectedEdge>(hit))return Entity.Null;
   Entity best=Entity.Null;float nearest=20f;
   foreach(var connection in EntityManager.GetBuffer<ConnectedEdge>(hit,true)){
    var edge=connection.m_Edge;
    if(!RoadInspector.IsRoad(EntityManager,edge)||!EntityManager.HasComponent<Game.Net.Road>(edge)||!EntityManager.HasComponent<Curve>(edge))continue;
    var curve=EntityManager.GetComponentData<Curve>(edge);
    float distance=Colossal.Mathematics.MathUtils.Distance(curve.m_Bezier,position,out var t);
    if(distance<nearest){nearest=distance;best=edge;}
   }
   return best;
  }
  // Use the same physical lane direction as the navigation job.
  internal static byte LaneDirection(EntityManager em,Entity lane)
  {
   if(!em.HasComponent<Game.Net.CarLane>(lane)||!em.HasComponent<EdgeLane>(lane)||
      em.HasComponent<MasterLane>(lane)||em.HasComponent<Deleted>(lane)||!em.HasComponent<Curve>(lane))return 0;
   var flags=em.GetComponentData<Game.Net.CarLane>(lane).m_Flags;
   if((flags&(CarLaneFlags.SideConnection|CarLaneFlags.Twoway))!=0)return 0;
   var delta=em.GetComponentData<EdgeLane>(lane).m_EdgeDelta;
   return Core.NavigationSafety.Direction(delta.x,delta.y);
  }
  internal static byte DirectionAt(EntityManager em,Entity road,float3 position)
  {
   byte direction=0;
   float best=float.MaxValue;
   foreach(var sub in em.GetBuffer<Game.Net.SubLane>(road,true))
   {
    var lane=sub.m_SubLane;
    byte candidate=LaneDirection(em,lane);
    if(candidate==0)continue;
    float distance=Colossal.Mathematics.MathUtils.Distance(em.GetComponentData<Curve>(lane).m_Bezier,position,out var t);
    if(distance<best){best=distance;direction=candidate;}
   }
   return direction;
  }
  internal float3 SelectedPosition {get;private set;}
  internal float3 HoverPosition {get;private set;}
  internal struct Anchor { public float3 Center, Ahead; }
  internal Anchor? FindAnchor(Entity road,byte direction)
  {
   if(!EntityManager.HasBuffer<Game.Net.SubLane>(road))return null;
   float3 center=0,tangent=0;
   int count=0;
   foreach(var sub in EntityManager.GetBuffer<Game.Net.SubLane>(road,true))
   {
    var lane=sub.m_SubLane;
    if(LaneDirection(EntityManager,lane)!=direction)continue;
    var d=EntityManager.GetComponentData<EdgeLane>(lane).m_EdgeDelta;
    // Stagger opposite controls along the road so they remain distinct at city scale.
    float t=((direction==1?0.38f:0.62f)-d.x)/(d.y-d.x);
    if(t<0||t>1)continue;
    var curve=EntityManager.GetComponentData<Curve>(lane).m_Bezier;
    center+=Colossal.Mathematics.MathUtils.Position(curve,t);
    tangent+=math.normalizesafe(Colossal.Mathematics.MathUtils.Tangent(curve,t));
    count++;
   }
   if(count==0)return null;
   center/=count;
   return new Anchor {Center=center,Ahead=center+math.normalizesafe(tangent)*5f};
  }
  protected override JobHandle OnUpdate(JobHandle inputDeps)
  {
   // Right click lets go of the chosen road; with nothing chosen it does nothing (close with X / Esc).
   if(RangeMode)
   {
    // Right click steps back: end, then start.
    if(secondaryApplyAction.WasReleasedThisFrame()){if(RangeEnd!=Entity.Null)RangeEnd=Entity.Null;else if(RangeStart!=Entity.Null)RangeStart=Entity.Null;SelectionRevision++;return inputDeps;}
    if(OwnsTool)
    {
     HoveredNode=GetRaycastResult(out Entity hitNode,out Game.Common.RaycastHit nodeHit)?ResolveNode(hitNode,nodeHit.m_Position):Entity.Null;
     if(applyAction.WasReleasedThisFrame()&&HoveredNode!=Entity.Null)
     {
      if(RangeStart==Entity.Null||RangeEnd!=Entity.Null){RangeStart=HoveredNode;RangeEnd=Entity.Null;}
      else if(HoveredNode!=RangeStart)RangeEnd=HoveredNode;
      SelectionRevision++;
     }
    }
    return inputDeps;
   }
   if(NodeMode)
   {
    if(secondaryApplyAction.WasReleasedThisFrame()){if(SelectedNode!=Entity.Null){SelectedNode=Entity.Null;selecting=true;SelectionRevision++;}return inputDeps;}
    if(OwnsTool)
    {
     HoveredNode=GetRaycastResult(out Entity hitNode,out Game.Common.RaycastHit nodeHit)?ResolveNode(hitNode,nodeHit.m_Position):Entity.Null;
     if(applyAction.WasReleasedThisFrame()&&HoveredNode!=Entity.Null&&HoveredNode!=SelectedNode){SelectedNode=HoveredNode;selecting=false;SelectionRevision++;}
    }
    return inputDeps;
   }
   if(secondaryApplyAction.WasReleasedThisFrame()){if(!selecting&&Selected!=Entity.Null)Deselect();return inputDeps;}
   if(applyAction.WasReleasedThisFrame()&&GetRaycastResult(out Entity clicked,out Game.Common.RaycastHit _)&&EntityManager.HasComponent<Game.Objects.Moving>(clicked))
   {
    if(EntityManager.HasComponent<Game.Vehicles.Controller>(clicked)){var main=EntityManager.GetComponentData<Game.Vehicles.Controller>(clicked).m_Controller;if(EntityManager.Exists(main))clicked=main;}
    Park(clicked);
    return inputDeps;
   }
   // Roads stay hoverable after one is chosen: clicking another switches straight to it.
   if(OwnsTool){
    Hovered=GetRaycastResult(out Entity entity,out Game.Common.RaycastHit hit)?ResolveSegment(entity,hit.m_Position):Entity.Null;
    if(Hovered!=Entity.Null)HoverPosition=hit.m_Position;
    if(applyAction.WasReleasedThisFrame()&&Hovered!=Entity.Null&&(selecting||Hovered!=Selected)){
     Selected=Hovered;SelectedPosition=hit.m_Position;selecting=false;SelectionRevision++;
     var mouse=Game.Input.InputManager.instance.mousePosition;
     AnchorX=math.saturate(mouse.x/math.max(1,Screen.width));AnchorY=math.saturate(1f-mouse.y/math.max(1,Screen.height));
    }
   }
   return inputDeps;
  }
 }
}

