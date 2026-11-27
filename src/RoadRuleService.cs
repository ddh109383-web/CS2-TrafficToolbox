using System;
using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Unity.Entities;

namespace RoadLaneLock
{
 // UI requests are staged separately from effective simulation rules. Never report
 // a requested rule as effective until an executor has acknowledged that revision.
 public sealed class RoadRuleService
 {
  readonly Dictionary<Entity, int> requests=new Dictionary<Entity,int>();
  int revision;
  public bool Requested(Entity road)=>requests.ContainsKey(road);
  public int Count=>requests.Count;
  public void Reset(){requests.Clear();revision=0;}
  public void Request(EntityManager em,Entity road,bool enable)
  {
   if(!RoadInspector.IsRoad(em,road))throw new InvalidOperationException("道路已失效，请重新选择。");
   if(enable)requests[road]=++revision;else requests.Remove(road);
  }
  public void RemoveDeleted(EntityManager em)
  {
   var dead=new List<Entity>();
   foreach(var road in requests.Keys)if(!RoadInspector.IsRoad(em,road))dead.Add(road);
   foreach(var road in dead)requests.Remove(road);
  }
  public static object Describe(EntityManager em,Entity road)
  {
   if(!RoadInspector.IsRoad(em,road))return null;
   var edge=em.GetComponentData<Edge>(road);
   int real=0,groups=0,side=0;
   if(em.HasBuffer<SubLane>(road))foreach(var sub in em.GetBuffer<SubLane>(road,true))
   {
    var lane=sub.m_SubLane;
    if(!em.Exists(lane)||!em.HasComponent<CarLane>(lane)||em.HasComponent<Deleted>(lane))continue;
    if(em.HasComponent<MasterLane>(lane)){groups++;continue;}
    real++;
    if(em.HasComponent<SlaveLane>(lane))
    {
     var flags=em.GetComponentData<SlaveLane>(lane).m_Flags;
     if((flags&(SlaveLaneFlags.MiddleStart|SlaveLaneFlags.MiddleEnd))!=0)side++;
    }
   }
   // Driving lanes as seen on the road: building connections and the second half of split lanes not counted.
   real=RoadRules.LaneCount(em,road);
   return new {Start=edge.m_Start.Index,End=edge.m_End.Index,RealLanes=real,Groups=groups,SideConnections=side};
  }
 }
}
