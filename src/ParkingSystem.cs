using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using SubLane=Game.Net.SubLane;
using ParkingLane=Game.Net.ParkingLane;
using Game.Prefabs;
using Unity.Entities;
namespace RoadLaneLock
{
 // Chosen roads are saved as mod system data in the city save (ignored by the game if the mod is removed).
 // Lane flags themselves are withdrawn before each save, so the save never carries our ParkingDisabled bits.
 // Never touch prefabs, garages or navigation rules. Vehicles only when the player's setting asks:
 // cars already parked on a banned road's kerb are removed (like the game's own vehicle delete).
 public sealed class ParkingSystem:GameSystemBase,ISerializable,IDefaultSerializable
 {
  // Session statistics (reset on load): parked cars cleared per road.
  internal readonly Dictionary<Entity,int> ClearedByRoad=new Dictionary<Entity,int>();
  internal int TotalCleared;
  readonly HashSet<Entity> clearQueue=new HashSet<Entity>();
  bool sweepAll;
  EntityQuery parkedQuery;
  protected override void OnCreate()
  {
   base.OnCreate();
   parkedQuery=GetEntityQuery(ComponentType.ReadOnly<Game.Vehicles.ParkedCar>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
  }
  // Remove cars parked on the road's eligible kerb lanes (and their trailers). Returns how many.
  int ClearParked(HashSet<Entity> roadsToClear)
  {
   if(roadsToClear.Count==0)return 0;
   var laneRoad=new Dictionary<Entity,Entity>();
   foreach(var road in roadsToClear)
    if(RoadInspector.IsRoad(EntityManager,road)&&EntityManager.HasBuffer<SubLane>(road))
     foreach(var sub in EntityManager.GetBuffer<SubLane>(road,true))if(Eligible(sub.m_SubLane,road))laneRoad[sub.m_SubLane]=road;
   if(laneRoad.Count==0)return 0;
   int removed=0;
   using(var cars=parkedQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
   foreach(var car in cars)
   {
    var lane=EntityManager.GetComponentData<Game.Vehicles.ParkedCar>(car).m_Lane;
    if(!laneRoad.TryGetValue(lane,out var road))continue;
    if(EntityManager.HasBuffer<Game.Vehicles.LayoutElement>(car))
     foreach(var part in EntityManager.GetBuffer<Game.Vehicles.LayoutElement>(car,true))
      if(part.m_Vehicle!=car&&EntityManager.Exists(part.m_Vehicle)&&!EntityManager.HasComponent<Deleted>(part.m_Vehicle))EntityManager.AddComponent<Deleted>(part.m_Vehicle);
    EntityManager.AddComponent<Deleted>(car);
    removed++;TotalCleared++;ClearedByRoad.TryGetValue(road,out int n);ClearedByRoad[road]=n+1;
   }
   if(removed>0)Mod.Log.Info("禁停清车："+removed+" 辆");
   return removed;
  }
  bool ClearEnabled=>Mod.Settings==null||Mod.Settings.ClearParkedOnBan;
  readonly HashSet<Entity> roads=new HashSet<Entity>();
  readonly HashSet<Entity> owned=new HashSet<Entity>();
  readonly HashSet<Entity> current=new HashSet<Entity>();
  readonly List<Entity> stale=new List<Entity>();
  internal int Revision {get;private set;}
  internal IEnumerable<Entity> Roads=>roads;
  internal int Count=>roads.Count;
  internal bool Suspended;
  internal bool Contains(Entity road)=>roads.Contains(road);
  internal bool Eligible(Entity lane,Entity road)
  {
   if(!RoadInspector.IsRoad(EntityManager,road)||!EntityManager.HasComponent<Game.Net.Road>(road)||!EntityManager.HasComponent<ParkingLane>(lane)||
      !EntityManager.HasComponent<Owner>(lane)||EntityManager.GetComponentData<Owner>(lane).m_Owner!=road||
      EntityManager.HasComponent<Deleted>(lane)||EntityManager.HasComponent<Game.Tools.Temp>(lane)||
      !EntityManager.HasComponent<PrefabRef>(lane))return false;
   var flags=EntityManager.GetComponentData<ParkingLane>(lane).m_Flags;
   if((flags&(ParkingLaneFlags.VirtualLane|ParkingLaneFlags.SpecialVehicles))!=0)return false;
   var prefab=EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
   return EntityManager.HasComponent<ParkingLaneData>(prefab)&&
    (EntityManager.GetComponentData<ParkingLaneData>(prefab).m_RoadTypes&RoadTypes.Car)!=0;
  }
  internal int LaneCount(Entity road,bool disabledOnly=false)
  {
   int count=0;
   if(EntityManager.HasBuffer<SubLane>(road))foreach(var sub in EntityManager.GetBuffer<SubLane>(road,true))
    if(Eligible(sub.m_SubLane,road)&&(!disabledOnly||(EntityManager.GetComponentData<ParkingLane>(sub.m_SubLane).m_Flags&ParkingLaneFlags.ParkingDisabled)!=0))count++;
   return count;
  }
  internal void SetRule(Entity road,bool enabled)
  {
   if(!Mod.SupportedGame||!RoadInspector.IsRoad(EntityManager,road))return;
   if(enabled?roads.Add(road):roads.Remove(road)){Revision++;Mod.Log.Info("Parking rule "+road+": "+enabled);}
   if(enabled)clearQueue.Add(road);else clearQueue.Remove(road);
  }
  void Dirty(Entity lane)
  {
   if(!EntityManager.HasComponent<Updated>(lane)&&!EntityManager.HasComponent<PathfindUpdated>(lane)&&!EntityManager.HasComponent<Created>(lane))EntityManager.AddComponent<PathfindUpdated>(lane);
  }
  void Release(Entity lane,bool notify)
  {
   if(!EntityManager.HasComponent<ParkingLane>(lane))return;
   var data=EntityManager.GetComponentData<ParkingLane>(lane);
   data.m_Flags&=~ParkingLaneFlags.ParkingDisabled;EntityManager.SetComponentData(lane,data);
   if(notify)Dirty(lane); // Native lane data recalculates all other restrictions.
  }
  internal void BeforeSave()
  {
   Suspended=true;
   foreach(var lane in owned)Release(lane,false);
  }
  internal void AfterSave()
  {
   foreach(var lane in owned)if(EntityManager.HasComponent<ParkingLane>(lane)){
    var data=EntityManager.GetComponentData<ParkingLane>(lane);data.m_Flags|=ParkingLaneFlags.ParkingDisabled;EntityManager.SetComponentData(lane,data);
   }
   Suspended=false;
  }
  internal void Shutdown()
  {
   foreach(var lane in owned)Release(lane,true);
   owned.Clear();roads.Clear();Revision++;Suspended=false;Enabled=false;
  }
  // Loaded roads come from Deserialize/SetDefaults; only per-session lane ownership resets here.
  protected override void OnGameLoadingComplete(Purpose purpose,GameMode mode)
  {base.OnGameLoadingComplete(purpose,mode);owned.Clear();current.Clear();Suspended=false;Revision++;ClearedByRoad.Clear();TotalCleared=0;clearQueue.Clear();sweepAll=true;Diagnostics.Event("parkingLoaded",new {Roads=roads.Count});}
  public void Serialize<W>(W writer) where W:IWriter
  {writer.Write(1);writer.Write(roads.Count);foreach(var road in roads)writer.Write(road);}
  public void Deserialize<R>(R reader) where R:IReader
  {
   roads.Clear();reader.Read(out int version);if(version!=1)throw new System.InvalidOperationException("不支持的停车规则存档版本。");
   reader.Read(out int count);if(count<0||count>100000)throw new System.InvalidOperationException("停车规则存档长度无效。");
   for(int i=0;i<count;i++){reader.Read(out Entity road);if(road!=Entity.Null)roads.Add(road);}
  }
  public void SetDefaults(Context context){roads.Clear();}
  protected override void OnUpdate()
  {
   if(Suspended||!Mod.SupportedGame||(roads.Count==0&&owned.Count==0))return;
   // Newly banned roads, and every banned road once after loading: clear cars parked before the ban.
   if(ClearEnabled&&(sweepAll||clearQueue.Count>0))
   {
    if(sweepAll)foreach(var road in roads)clearQueue.Add(road);
    ClearParked(clearQueue);
   }
   sweepAll=false;clearQueue.Clear();
   current.Clear();stale.Clear();
   foreach(var road in roads){
    if(!RoadInspector.IsRoad(EntityManager,road)){stale.Add(road);continue;}
    if(EntityManager.HasBuffer<SubLane>(road))foreach(var sub in EntityManager.GetBuffer<SubLane>(road,true))
     if(Eligible(sub.m_SubLane,road))current.Add(sub.m_SubLane);
   }
   foreach(var road in stale){roads.Remove(road);Revision++;Mod.Log.Info("Parking road removed; reselect rebuilt road: "+road);}
   stale.Clear();foreach(var lane in owned)if(!current.Contains(lane))stale.Add(lane);
   foreach(var lane in stale){Release(lane,true);owned.Remove(lane);}
   foreach(var lane in current){
    var data=EntityManager.GetComponentData<ParkingLane>(lane);
    if((data.m_Flags&ParkingLaneFlags.ParkingDisabled)!=0)continue;
    owned.Add(lane);data.m_Flags|=ParkingLaneFlags.ParkingDisabled;EntityManager.SetComponentData(lane,data);Dirty(lane);
   }
  }
 }
 public sealed class ParkingBeforeSave:GameSystemBase
 {protected override void OnUpdate(){var p=World.GetExistingSystemManaged<ParkingSystem>();if(p!=null&&p.Enabled)p.BeforeSave();}}
 public sealed class ParkingAfterSave:GameSystemBase
 {protected override void OnUpdate(){var p=World.GetExistingSystemManaged<ParkingSystem>();if(p!=null&&p.Enabled)p.AfterSave();}}
}



