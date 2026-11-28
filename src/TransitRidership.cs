using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Routes;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace RoadLaneLock
{
 // G32: riders per public transport line. The game keeps only citywide passenger counts, so boardings
 // are counted here: a passenger entity that was not on the vehicle at the last sample has boarded.
 // Counts run per in-game month (this month and last month) and are saved with the city.
 public sealed class TransitRidership:GameSystemBase,ISerializable,IDefaultSerializable
 {
  internal static TransitRidership Current;
  sealed class Count{public int Month,Last;}
  readonly Dictionary<Entity,Count> lines=new Dictionary<Entity,Count>();
  readonly Dictionary<Entity,HashSet<Entity>> aboard=new Dictionary<Entity,HashSet<Entity>>();
  readonly HashSet<Entity> sampled=new HashSet<Entity>();
  readonly List<Entity> stale=new List<Entity>();
  EntityQuery query;int monthKey=-1,ticks,sinceLog;

  protected override void OnCreate()
  {
   base.OnCreate();Current=this;
   query=GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Game.Vehicles.Passenger>()},None=new[]{ComponentType.ReadOnly<Deleted>(),ComponentType.ReadOnly<Temp>()}});
  }
  // About once a second at normal speed.
  public override int GetUpdateInterval(SystemUpdatePhase phase)=>64;

  int MonthNow()
  {
   try{var d=World.GetOrCreateSystemManaged<Game.Simulation.TimeSystem>().GetCurrentDateTime();return d.Year*12+d.Month-1;}catch{return monthKey;}
  }

  protected override void OnUpdate()
  {
   int now=MonthNow();
   if(monthKey<0)monthKey=now;
   else if(now!=monthKey)
   {
    foreach(var c in lines.Values){c.Last=now==monthKey+1?c.Month:0;c.Month=0;}
    monthKey=now;
   }
   sampled.Clear();
   using(var vehicles=query.ToEntityArray(Allocator.Temp))
   foreach(var v in vehicles)
   {
    var route=RouteOf(v);
    if(route==Entity.Null)continue;
    sampled.Add(v);
    var buffer=EntityManager.GetBuffer<Game.Vehicles.Passenger>(v,true);
    bool primed=aboard.TryGetValue(v,out var before);
    var after=new HashSet<Entity>();int boarded=0;
    for(int i=0;i<buffer.Length;i++){var p=buffer[i].m_Passenger;after.Add(p);if(primed&&!before.Contains(p))boarded++;}
    aboard[v]=after;
    if(boarded==0)continue;
    if(!lines.TryGetValue(route,out var c))lines[route]=c=new Count();
    c.Month+=boarded;sinceLog+=boarded;
   }
   stale.Clear();foreach(var v in aboard.Keys)if(!sampled.Contains(v))stale.Add(v);
   foreach(var v in stale)aboard.Remove(v);
   // About every 5 minutes at normal speed: a summary to check the counts against.
   if(++ticks>=300)
   {
    ticks=0;long month=0;foreach(var c in lines.Values)month+=c.Month;
    Diagnostics.Event("transitRiders",new{Boarded=sinceLog,Vehicles=sampled.Count,Lines=lines.Count,Month=month,MonthKey=monthKey});
    Mod.Log.Info("线路客流：近一段上车 "+sinceLog+" 人次，"+sampled.Count+" 辆车在跑，"+lines.Count+" 条线路本月共 "+month+" 人次");
    sinceLog=0;
   }
  }

  // The line a vehicle (or one car of a train) runs on.
  Entity RouteOf(Entity v)
  {
   if(EntityManager.HasComponent<CurrentRoute>(v))return EntityManager.GetComponentData<CurrentRoute>(v).m_Route;
   if(EntityManager.HasComponent<Game.Vehicles.Controller>(v))
   {
    var c=EntityManager.GetComponentData<Game.Vehicles.Controller>(v).m_Controller;
    if(c!=v&&EntityManager.Exists(c)&&EntityManager.HasComponent<CurrentRoute>(c))return EntityManager.GetComponentData<CurrentRoute>(c).m_Route;
   }
   return Entity.Null;
  }

  internal bool TryGet(Entity line,out int month,out int last)
  {
   if(lines.TryGetValue(line,out var c)){month=c.Month;last=c.Last;return true;}
   month=0;last=0;return false;
  }

  protected override void OnGameLoadingComplete(Purpose purpose,GameMode mode)
  {
   base.OnGameLoadingComplete(purpose,mode);
   aboard.Clear(); // passengers already aboard at load are not new boardings
  }

  public void Serialize<W>(W writer) where W:IWriter
  {
   writer.Write(1);writer.Write(monthKey);
   int n=0;foreach(var p in lines)if(EntityManager.Exists(p.Key))n++;
   writer.Write(n);
   foreach(var p in lines){if(!EntityManager.Exists(p.Key))continue;writer.Write(p.Key);writer.Write(p.Value.Month);writer.Write(p.Value.Last);}
  }
  public void Deserialize<R>(R reader) where R:IReader
  {
   lines.Clear();aboard.Clear();
   reader.Read(out int version);if(version!=1)throw new System.InvalidOperationException("不支持的线路客流存档版本。");
   reader.Read(out monthKey);
   reader.Read(out int n);if(n<0||n>100000)throw new System.InvalidOperationException("线路客流存档长度无效。");
   for(int i=0;i<n;i++){reader.Read(out Entity e);reader.Read(out int m);reader.Read(out int l);if(e!=Entity.Null)lines[e]=new Count{Month=m,Last=l};}
  }
  public void SetDefaults(Context context){lines.Clear();aboard.Clear();monthKey=-1;}
 }
}
