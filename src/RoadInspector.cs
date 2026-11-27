using System;
using System.Collections.Generic;
using System.IO;

using System.Reflection;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Pathfind;
using Edge=Game.Net.Edge;
using Newtonsoft.Json;
using Unity.Entities;

namespace RoadLaneLock
{
 public static class RoadInspector
 {
  public static bool IsRoad(EntityManager em,Entity e)=>e!=Entity.Null&&em.Exists(e)&&em.HasComponent<Edge>(e)&&em.HasComponent<Composition>(e)&&!em.HasComponent<Deleted>(e)&&!em.HasComponent<Game.Tools.Temp>(e);
  static object Id(Entity e)=>new {e.Index,e.Version};
  internal static string Key(PathNode n)=>((ulong)typeof(PathNode).GetField("m_SearchKey",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(n)).ToString("X16");
  public static string Capture(World world,Entity selected)
  {
   var em=world.EntityManager;if(!IsRoad(em,selected))throw new InvalidOperationException("请选择道路段");
   var edge=em.GetComponentData<Edge>(selected);
   var owners=new HashSet<Entity>{selected,edge.m_Start,edge.m_End};
   foreach(var node in new[]{edge.m_Start,edge.m_End})
    if(em.HasBuffer<ConnectedEdge>(node))foreach(var c in em.GetBuffer<ConnectedEdge>(node,true))owners.Add(c.m_Edge);
   if(owners.Count>128)throw new InvalidOperationException("相邻道路过多，停止记录");
   var records=new List<object>();int count=0;
   foreach(var owner in owners)
   {
    if(!em.Exists(owner))continue;
    var lanes=new List<object>();var composition=new List<object>();
    if(em.HasComponent<Composition>(owner))
    {
     var c=em.GetComponentData<Composition>(owner);
     if(em.HasBuffer<NetCompositionLane>(c.m_Edge))foreach(var l in em.GetBuffer<NetCompositionLane>(c.m_Edge,true))
      composition.Add(new {l.m_Index,l.m_Group,l.m_Carriageway,Flags=l.m_Flags.ToString(),Prefab=Id(l.m_Lane)});
    }
    if(em.HasBuffer<Game.Net.SubLane>(owner))foreach(var sub in em.GetBuffer<Game.Net.SubLane>(owner,true))
    {
     var e=sub.m_SubLane;if(!em.Exists(e)||!em.HasComponent<Lane>(e)||!em.HasComponent<Game.Net.CarLane>(e))continue;
     if(++count>5000)throw new InvalidOperationException("车道数量超过诊断上限，停止记录");
     var l=em.GetComponentData<Lane>(e);var car=em.GetComponentData<Game.Net.CarLane>(e);
     object slave=null,master=null;
     if(em.HasComponent<SlaveLane>(e)) {var s=em.GetComponentData<SlaveLane>(e);slave=new {s.m_Group,s.m_MinIndex,s.m_MaxIndex,s.m_SubIndex,Flags=s.m_Flags.ToString()};}
     if(em.HasComponent<MasterLane>(e)){var m=em.GetComponentData<MasterLane>(e);master=new {m.m_Group,m.m_MinIndex,m.m_MaxIndex};}
     lanes.Add(new {Entity=Id(e),Start=Key(l.m_StartNode),Middle=Key(l.m_MiddleNode),End=Key(l.m_EndNode),
      Flags=car.m_Flags.ToString(),Methods=sub.m_PathMethods.ToString(),Slave=slave,Master=master,
      EdgeLane=em.HasComponent<EdgeLane>(e),NodeLane=em.HasComponent<NodeLane>(e)});
    }
    records.Add(new {Entity=Id(owner),IsEdge=em.HasComponent<Edge>(owner),Composition=composition,Lanes=lanes});
   }
   // World.Systems forbids IEnumerable boxing; enumerate its concrete collection.
   var systems=new List<object>();
   foreach(var system in world.Systems)
   {
    var type=system.GetType();
    if(type.Name.IndexOf("Lane",StringComparison.OrdinalIgnoreCase)>=0)
     systems.Add(new {Type=type.FullName,Assembly=type.Assembly.GetName().Name,system.Enabled});
   }
   var root=Environment.GetEnvironmentVariable("CSII_USERDATAPATH",EnvironmentVariableTarget.User);
   if(string.IsNullOrWhiteSpace(root))root=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"..","LocalLow","Colossal Order","Cities Skylines II"));
   var folder=Path.Combine(root,"Logs");Directory.CreateDirectory(folder);
   var path=Path.Combine(folder,"RoadLaneLock-topology.json");
   File.WriteAllText(path,JsonConvert.SerializeObject(new {Format=1,UTC=DateTime.UtcNow,Selected=Id(selected),
    StartNode=Id(edge.m_Start),EndNode=Id(edge.m_End),Systems=systems,Owners=records},Formatting.Indented));
   Mod.Log.Info("Read-only topology captured: "+count+" motor lanes; "+path);return path;
  }
 }
}

