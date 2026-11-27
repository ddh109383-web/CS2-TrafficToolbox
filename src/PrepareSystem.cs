using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace RoadLaneLock
{
 public partial class PrepareSystem:GameSystemBase
 {
  private EntityQuery rules;
  private sealed class Swap {public Entity Edge,Original,Clone;}
  private readonly List<Swap> swaps=new List<Swap>();
  public string Status="实验版：尚未经过游戏内通车验证";
  protected override void OnCreate()
  {
   base.OnCreate();rules=GetEntityQuery(new EntityQueryDesc {
    All=new[]{ComponentType.ReadOnly<RoadRule>(),ComponentType.ReadOnly<Edge>(),ComponentType.ReadOnly<Composition>()},
    None=new[]{ComponentType.ReadOnly<Deleted>(),ComponentType.ReadOnly<Game.Tools.Temp>()}});
  }
  public bool IsRoad(Entity e)=>e!=Entity.Null&&EntityManager.Exists(e)&&EntityManager.HasComponent<Edge>(e)&&EntityManager.HasComponent<Composition>(e)&&!EntityManager.HasComponent<Deleted>(e)&&!EntityManager.HasComponent<Game.Tools.Temp>(e);
  private void Dirty(Entity e){if(EntityManager.Exists(e)&&!EntityManager.HasComponent<Updated>(e))EntityManager.AddComponent<Updated>(e);}
  private void Rebuild(Entity e)
  {
   if(!IsRoad(e))return;
   var edge=EntityManager.GetComponentData<Edge>(e);Dirty(e);Dirty(edge.m_Start);Dirty(edge.m_End);
  }
  public void SetRule(Entity e,bool enable)
  {
   if(!IsRoad(e))throw new InvalidOperationException("请先选择一段道路");
   Restore();
   if(enable)
   {
    var c=EntityManager.GetComponentData<Composition>(e);
    var source=EntityManager.GetBuffer<NetCompositionLane>(c.m_Edge,true);
    var lanes=new NetCompositionLane[source.Length];for(int i=0;i<lanes.Length;i++)lanes[i]=source[i];
    CompositionPlan.Create(lanes);
    if(!EntityManager.HasComponent<RoadRule>(e))EntityManager.AddComponentData(e,new RoadRule{Version=1});
   }
   else if(EntityManager.HasComponent<RoadRule>(e))EntityManager.RemoveComponent<RoadRule>(e);
   Rebuild(e);Status=enable?"规则已提交；等待道路重建。实验版，效果待验证":"规则已撤销；等待恢复原版道路";
  }
  protected override void OnGameLoadingComplete(Purpose purpose,GameMode mode)
  {
   base.OnGameLoadingComplete(purpose,mode);swaps.Clear();
   using(var list=rules.ToEntityArray(Allocator.Temp))foreach(var e in list)Rebuild(e);
  }
  protected override void OnUpdate()
  {
   Restore();
   using(var list=rules.ToEntityArray(Allocator.Temp))foreach(var e in list)
   {
    var edge=EntityManager.GetComponentData<Edge>(e);
    if(!EntityManager.HasComponent<Updated>(e)&&!EntityManager.HasComponent<Updated>(edge.m_Start)&&!EntityManager.HasComponent<Updated>(edge.m_End))continue;
    try
    {
     var c=EntityManager.GetComponentData<Composition>(e);
     var buffer=EntityManager.GetBuffer<NetCompositionLane>(c.m_Edge,true);
     var input=new NetCompositionLane[buffer.Length];for(int i=0;i<input.Length;i++)input[i]=buffer[i];
     var plan=CompositionPlan.Create(input);
     // Private, temporary composition lives only across the lane-generation phase.
     // A shared composition is never edited, including its original lane buffer.
     var clone=EntityManager.Instantiate(c.m_Edge);
     swaps.Add(new Swap{Edge=e,Original=c.m_Edge,Clone=clone});
     var target=EntityManager.GetBuffer<NetCompositionLane>(clone);target.Clear();foreach(var lane in plan)target.Add(lane);
     c.m_Edge=clone;EntityManager.SetComponentData(e,c);
    }
    catch(Exception ex)
    {
     Status="此路段生成失败，已撤销规则："+ex.Message;Mod.Log.Error(ex);
     if(EntityManager.HasComponent<RoadRule>(e))EntityManager.RemoveComponent<RoadRule>(e);
     Rebuild(e);
    }
   }
  }
  public void Restore()
  {
   if(swaps.Count==0)return;
   // Structural operations complete ECS jobs reading the temporary compositions.
   EntityManager.CompleteAllTrackedJobs();
   foreach(var swap in swaps)
   {
    if(EntityManager.Exists(swap.Edge)&&EntityManager.HasComponent<Composition>(swap.Edge))
    {
     var c=EntityManager.GetComponentData<Composition>(swap.Edge);
     if(c.m_Edge==swap.Clone){c.m_Edge=swap.Original;EntityManager.SetComponentData(swap.Edge,c);}
    }
    if(EntityManager.Exists(swap.Clone))EntityManager.DestroyEntity(swap.Clone);
   }
   swaps.Clear();
  }
 }
}
