using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using Game;
using Game.Common;
using Game.Vehicles;
using Game.Pathfind;
using Colossal.Serialization.Entities;
using Newtonsoft.Json;
using Unity.Entities;
using Unity.Collections;

namespace RoadLaneLock
{
 public partial class VehicleRecorder:GameSystemBase
 {
  private EntityQuery cars;
  private StreamWriter writer;
  private const long MaxBytes=1L<<30;
  private long sequence;
  private double nextFlushSeconds;
  private readonly Dictionary<EntityArchetype,string[]> vehicleTypeCache=new Dictionary<EntityArchetype,string[]>();
  private string[] GetVehicleTypeNames(Entity vehicle)
  {
   var archetype=EntityManager.GetChunk(vehicle).Archetype;
   if(vehicleTypeCache.TryGetValue(archetype,out var cached))return cached;
   var names=new List<string>();
   using(var types=EntityManager.GetComponentTypes(vehicle))
    foreach(var type in types)
    {
     var managed=type.GetManagedType();
     if(managed!=null&&managed.Namespace=="Game.Vehicles")names.Add(managed.Name);
    }
   return vehicleTypeCache[archetype]=names.ToArray();
  }
  private readonly List<Entity> recordingRoads=new List<Entity>();
  private readonly List<object> recordingRoadIds=new List<object>();
  private readonly HashSet<Entity> area=new HashSet<Entity>();
  private readonly Dictionary<Entity,PathElement[]> pathCache=new Dictionary<Entity,PathElement[]>();
  private readonly Dictionary<Entity,long> pathRevisions=new Dictionary<Entity,long>();
  private readonly Dictionary<Entity,LaneSnapshot> laneCache=new Dictionary<Entity,LaneSnapshot>();
  private readonly List<Entity> included=new List<Entity>();
  private readonly HashSet<Entity> laneSet=new HashSet<Entity>();
  private struct LaneSnapshot
  {
   public byte Direction;public Game.Net.Lane Lane;public Entity Owner;public bool HasCurve;public Game.Net.Curve Curve;
   public bool HasCar;public Game.Net.CarLaneFlags Flags;
   public bool Same(LaneSnapshot b)=>Direction==b.Direction&&Owner==b.Owner&&HasCurve==b.HasCurve&&HasCar==b.HasCar&&Flags==b.Flags&&
    Lane.m_StartNode.Equals(b.Lane.m_StartNode)&&Lane.m_MiddleNode.Equals(b.Lane.m_MiddleNode)&&Lane.m_EndNode.Equals(b.Lane.m_EndNode)&&
    (!HasCurve||(Curve.m_Length==b.Curve.m_Length&&Curve.m_Bezier.Equals(b.Curve.m_Bezier)));
  }
  // Format 4: an integer Navigation refers to the last full list for this entity/version.
  private sealed class NavigationHistory
  {
   public readonly List<NavigationEntry> Entries = new List<NavigationEntry>();
   public long Revision;
  }
  private struct NavigationEntry
  {
   public CarNavigationLane Lane;
   public Game.Net.LaneSignal? Signal;
   public bool Matches(NavigationEntry other)
   {
    if (Lane.m_Lane != other.Lane.m_Lane || Lane.m_Flags != other.Lane.m_Flags ||
        !Lane.m_CurvePosition.Equals(other.Lane.m_CurvePosition) || Signal.HasValue != other.Signal.HasValue)
     return false;
    if (!Signal.HasValue) return true;
    var a = Signal.Value;
    var b = other.Signal.Value;
    return a.m_Signal == b.m_Signal && a.m_Flags == b.m_Flags && a.m_Priority == b.m_Priority &&
     a.m_Default == b.m_Default && a.m_GroupMask == b.m_GroupMask &&
     a.m_Petitioner == b.m_Petitioner && a.m_Blocker == b.m_Blocker;
   }
  }
  private readonly Dictionary<Entity, NavigationHistory> navigationCache = new Dictionary<Entity, NavigationHistory>();
  private readonly Dictionary<Entity, Game.Net.LaneSignal?> frameSignals = new Dictionary<Entity, Game.Net.LaneSignal?>();
  private readonly List<NavigationEntry> currentNavigation = new List<NavigationEntry>();
  private Game.Net.LaneSignal? ReadSignal(Entity lane)
  {
   if (!frameSignals.TryGetValue(lane, out var signal))
   {
    signal = EntityManager.HasComponent<Game.Net.LaneSignal>(lane)
     ? EntityManager.GetComponentData<Game.Net.LaneSignal>(lane) : (Game.Net.LaneSignal?)null;
    frameSignals.Add(lane, signal);
   }
   return signal;
  }
  private object RecordNavigation(Entity vehicle, HashSet<Entity> lanes)
  {
   if (!navigationCache.TryGetValue(vehicle, out var history))
    navigationCache.Add(vehicle, history = new NavigationHistory());
   currentNavigation.Clear();
   bool same = history.Revision != 0;
   if (EntityManager.HasBuffer<CarNavigationLane>(vehicle))
   {
    var buffer = EntityManager.GetBuffer<CarNavigationLane>(vehicle, true);
    for (int i = 0; i < buffer.Length; i++)
    {
     var lane = buffer[i];
     lanes.Add(lane.m_Lane);
     var entry = new NavigationEntry {Lane = lane, Signal = ReadSignal(lane.m_Lane)};
     currentNavigation.Add(entry);
     same = same && i < history.Entries.Count && entry.Matches(history.Entries[i]);
    }
   }
   if (same && currentNavigation.Count == history.Entries.Count) return history.Revision;
   history.Revision = sequence;
   history.Entries.Clear();
   history.Entries.AddRange(currentNavigation);
   var records = new List<object>(currentNavigation.Count);
   foreach (var entry in currentNavigation)
   {
    var lane = entry.Lane;
    records.Add(new {Lane=Id(lane.m_Lane), Flags=lane.m_Flags.ToString(),
     Curve=new[]{lane.m_CurvePosition.x,lane.m_CurvePosition.y}, Signal=SignalRecord(entry.Signal)});
   }
   return records;
  }
  private static object SignalRecord(Game.Net.LaneSignal? signal)
  {
   if (!signal.HasValue) return null;
   var s = signal.Value;
   return new {Signal=s.m_Signal.ToString(),Flags=s.m_Flags.ToString(),Priority=s.m_Priority,Default=s.m_Default,
    GroupMask=s.m_GroupMask,Petitioner=Id(s.m_Petitioner),Blocker=Id(s.m_Blocker)};
  }
  private long RecordPath(Entity vehicle,HashSet<Entity> lanes)
  {
   bool has=EntityManager.HasBuffer<PathElement>(vehicle);
   var buffer=has?EntityManager.GetBuffer<PathElement>(vehicle,true):default;
   int length=has?buffer.Length:0;
   bool same=pathCache.TryGetValue(vehicle,out var old)&&old.Length==length;
   for(int i=0;i<length;i++){
    var v=buffer[i];lanes.Add(v.m_Target);
    if(same){var a=old[i];same=a.m_Target==v.m_Target&&a.m_Flags==v.m_Flags&&a.m_TargetDelta.Equals(v.m_TargetDelta);}
   }
   if(same)return pathRevisions[vehicle];
   var snapshot=new PathElement[length];var records=new List<object>(length);
   for(int i=0;i<length;i++){var v=buffer[i];snapshot[i]=v;records.Add(new {Target=Id(v.m_Target),Flags=v.m_Flags.ToString(),Delta=new[]{v.m_TargetDelta.x,v.m_TargetDelta.y}});}
   pathCache[vehicle]=snapshot;pathRevisions[vehicle]=sequence;
   Write(new {Kind="path",Sequence=sequence,Entity=Id(vehicle),Revision=sequence,Elements=records});
   return sequence;
  }
  private readonly RoadLaneLock.Core.RecordingWindow<Entity> tracking=new RoadLaneLock.Core.RecordingWindow<Entity>();
  private readonly Stopwatch clock=new Stopwatch();
  private readonly Dictionary<Entity,string> selectedRoads=new Dictionary<Entity,string>();
  public IEnumerable<Entity> RecordingRoads=>recordingRoads;
  public bool IsRecordingRoad(Entity road)=>Recording&&recordingRoads.Contains(road);
  private bool includeEnabledRoads;
  private bool ManualRecording=>Recording&&!includeEnabledRoads;
  private string RecordingMode=>!includeEnabledRoads?"SelectedRoads":selectedRoads.Count==0?"AllEnabledRoads":"AllEnabledPlusSelectedRoads";
  public void RecordRoad(Entity road,string name)
  {
   if(!RoadInspector.IsRoad(EntityManager,road))throw new InvalidOperationException("所选路段已失效。");
   if(!Recording){Start(road,name);return;}
   selectedRoads[road]=name;
   RefreshArea();
  }
  private void RefreshArea()
  {
   area.Clear();recordingRoadIds.Clear();
   if(includeEnabledRoads)World.GetOrCreateSystemManaged<RuntimeCoordinator>().Executor.CopyProtectedRoads(recordingRoads);
   else recordingRoads.Clear();
   foreach(var road in selectedRoads.Keys)if(!includeEnabledRoads||!recordingRoads.Contains(road))recordingRoads.Add(road);
   foreach(var road in recordingRoads)
   {
    if(!RoadInspector.IsRoad(EntityManager,road))continue;
    area.Add(road);
    var edge=EntityManager.GetComponentData<Game.Net.Edge>(road);
    recordingRoadIds.Add(new {road.Index,road.Version,Name=selectedRoads.TryGetValue(road,out var name)?name:null,Start=Id(edge.m_Start),End=Id(edge.m_End),DirectionMask=World.GetExistingSystemManaged<RuntimeCoordinator>().Executor.DirectionMask(road)});
    foreach(var node in new[]{edge.m_Start,edge.m_End}){
     if(!EntityManager.Exists(node))continue;
     area.Add(node);
     if(EntityManager.HasBuffer<Game.Net.ConnectedEdge>(node))
      foreach(var c in EntityManager.GetBuffer<Game.Net.ConnectedEdge>(node,true))area.Add(c.m_Edge);
    }
   }
  }
  private bool InArea(Entity lane)=>EntityManager.Exists(lane)&&EntityManager.HasComponent<Owner>(lane)&&area.Contains(EntityManager.GetComponentData<Owner>(lane).m_Owner);
  public string Status {get;private set;}="开启至少一段禁变道规则后，可记录所有已启用路口。";
  public bool Recording=>writer!=null;
  public int RoadCount=>recordingRoadIds.Count;
  public string FilePath {get;private set;}="";
  public int Vehicles {get;private set;}
  public long Frames=>sequence;
  public double LastMilliseconds {get;private set;}
  private static object Id(Entity e)=>new {e.Index,e.Version};
  protected override void OnCreate(){base.OnCreate();cars=GetEntityQuery(ComponentType.ReadOnly<CarCurrentLane>());}
  public void Start(Entity road=default,string name=null)
  {
   if(Recording)return;
   includeEnabledRoads=road==Entity.Null;
   selectedRoads.Clear();if(road!=Entity.Null)selectedRoads.Add(road,name);
   EntityManager.CompleteAllTrackedJobs();RefreshArea();
   if(RoadCount==0)throw new InvalidOperationException("请先开启至少一段道路的禁止变道规则。");
   tracking.Clear();clock.Restart();
   var folder=Diagnostics.Folder;
   FilePath=Path.Combine(folder,"RoadLaneLock-vehicles-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".jsonl");
   writer=new StreamWriter(new FileStream(FilePath,FileMode.CreateNew,FileAccess.Write,FileShare.Read),System.Text.Encoding.UTF8,65536);
   nextFlushSeconds=1;vehicleTypeCache.Clear();sequence=0;Vehicles=0;pathCache.Clear();pathRevisions.Clear();laneCache.Clear();navigationCache.Clear();frameSignals.Clear();
   Write(new {Kind="header",Format=4,RecordingMode=RecordingMode,RecordingRoads=recordingRoadIds,FollowSeconds=120,UTC=DateTime.UtcNow,Version=Mod.Version,Scope="Union of the roads identified by RecordingMode and RecordingRoads, their two nodes and directly adjacent edges; scope refreshed each sample, deduplicated; follow for 120 wall-clock seconds after leaving. Per simulation update. Actions records applied directional locks and new mod detours. Mask 1 goes toward Start, 2 toward End, 3 both. Delta paths referenced by Entity+PathRevision; lanes emitted on first sight or change, latest definition persists. Navigation is an array on change (including signal changes), otherwise the sequence of the last array for this entity/version."});writer.Flush();
   World.GetExistingSystemManaged<RuntimeCoordinator>().Executor.RecordLocks=true;
   Status=ManualRecording?"正在记录自选路段。":"正在记录全部已启用路口；范围随规则更新，驶离后继续跟踪120秒。";
  }
  private readonly JsonSerializer serializer = JsonSerializer.CreateDefault();
  private void Write(object value)
  {
   serializer.Serialize(writer, value);
   writer.WriteLine();
  }
  public void Stop(string reason="手动停止")
  {
   if(writer==null)return;
   World.GetExistingSystemManaged<RuntimeCoordinator>().Executor.RecordLocks=false;
   try{Write(new {Kind="end",UTC=DateTime.UtcNow,Reason=reason,Frames=sequence});writer.Flush();}
   finally{writer.Dispose();writer=null;selectedRoads.Clear();vehicleTypeCache.Clear();tracking.Clear();pathCache.Clear();pathRevisions.Clear();laneCache.Clear();navigationCache.Clear();frameSignals.Clear();included.Clear();laneSet.Clear();clock.Stop();Status="记录已停止："+reason;}
  }
  protected override void OnUpdate()
  {
   if(!Recording)return;
   var watch=Stopwatch.StartNew();
   try
   {
    EntityManager.CompleteAllTrackedJobs();
    RefreshArea();if(ManualRecording&&RoadCount==0){Stop("所选路段已移除");return;}var now=clock.Elapsed.TotalSeconds;tracking.Prune(now);
    var runtime=World.GetOrCreateSystemManaged<RuntimeCoordinator>();
    var detail=runtime.Executor.CaptureRecordingState();
    using(var entities=cars.ToEntityArray(Allocator.Temp))
    {
     included.Clear();
     for(int i=0;i<entities.Length;i++){
      var e=entities[i];if(EntityManager.HasComponent<Deleted>(e))continue;
      var c=EntityManager.GetComponentData<CarCurrentLane>(e);
      if(tracking.Include(e,InArea(c.m_Lane)||InArea(c.m_ChangeLane),now))included.Add(e);
     }
     Vehicles=included.Count;
     Write(new {Kind="frame",Sequence=++sequence,UTC=DateTime.UtcNow,Count=Vehicles,RecordingMode=RecordingMode,RecordingRoads=recordingRoadIds,ElapsedSeconds=now,OwnsNavigation=runtime.OwnsNavigation,Problem=runtime.Problem,Rules=detail});
     var lanes=laneSet;lanes.Clear();frameSignals.Clear();
     for(int i=0;i<included.Count;i++)
     {
      var e=included[i];var c=EntityManager.GetComponentData<CarCurrentLane>(e);
      lanes.Add(c.m_Lane);if(c.m_ChangeLane!=Entity.Null)lanes.Add(c.m_ChangeLane);
      object position=null,motion=null,path=null;
      var vehicleTypes=GetVehicleTypeNames(e);
      string carFlags=EntityManager.HasComponent<Car>(e)?EntityManager.GetComponentData<Car>(e).m_Flags.ToString():null;
      string garbage=EntityManager.HasComponent<GarbageTruck>(e)?EntityManager.GetComponentData<GarbageTruck>(e).m_State.ToString():null;
      string maintenance=EntityManager.HasComponent<MaintenanceVehicle>(e)?EntityManager.GetComponentData<MaintenanceVehicle>(e).m_State.ToString():null;
      object blockedBy=EntityManager.HasComponent<Blocker>(e)?Id(EntityManager.GetComponentData<Blocker>(e).m_Blocker):null;
      if(EntityManager.HasComponent<Game.Objects.Transform>(e)){var t=EntityManager.GetComponentData<Game.Objects.Transform>(e);position=new[]{t.m_Position.x,t.m_Position.y,t.m_Position.z};}
      if(EntityManager.HasComponent<Game.Objects.Moving>(e)){var m=EntityManager.GetComponentData<Game.Objects.Moving>(e);motion=new[]{m.m_Velocity.x,m.m_Velocity.y,m.m_Velocity.z};}
      if(EntityManager.HasComponent<PathOwner>(e)){var p=EntityManager.GetComponentData<PathOwner>(e);path=new {State=p.m_State.ToString(),Index=p.m_ElementIndex};}
      var nav = RecordNavigation(e, lanes);
      long pathRevision=RecordPath(e,lanes);
      bool isTaxi=EntityManager.HasComponent<Game.Vehicles.Taxi>(e);
      Write(new {Kind="vehicle",Sequence=sequence,Entity=Id(e),TaxiState=isTaxi?EntityManager.GetComponentData<Game.Vehicles.Taxi>(e).m_State.ToString():null,PassengerCount=isTaxi&&EntityManager.HasBuffer<Game.Vehicles.Passenger>(e)?(int?)EntityManager.GetBuffer<Game.Vehicles.Passenger>(e,true).Length:null,VehicleTypes=vehicleTypes,CarFlags=carFlags,MaintenanceState=maintenance,GarbageState=garbage,Signal=SignalRecord(ReadSignal(c.m_Lane)),BlockedBy=blockedBy,Deleted=EntityManager.HasComponent<Deleted>(e),Position=position,Velocity=motion,Lane=Id(c.m_Lane),ChangeLane=Id(c.m_ChangeLane),ChangeProgress=c.m_ChangeProgress,Flags=c.m_LaneFlags.ToString(),Curve=new[]{c.m_CurvePosition.x,c.m_CurvePosition.y,c.m_CurvePosition.z},Path=path,Navigation=nav,PathRevision=pathRevision});
     }
     foreach(var e in lanes)
     {
      if(!EntityManager.Exists(e)||!EntityManager.HasComponent<Game.Net.Lane>(e))continue;
      var l=EntityManager.GetComponentData<Game.Net.Lane>(e);
      var state=new LaneSnapshot{Direction=RoadPickTool.LaneDirection(EntityManager,e),Lane=l,Owner=EntityManager.HasComponent<Owner>(e)?EntityManager.GetComponentData<Owner>(e).m_Owner:Entity.Null,
       HasCurve=EntityManager.HasComponent<Game.Net.Curve>(e),HasCar=EntityManager.HasComponent<Game.Net.CarLane>(e)};
      if(state.HasCurve)state.Curve=EntityManager.GetComponentData<Game.Net.Curve>(e);
      if(state.HasCar)state.Flags=EntityManager.GetComponentData<Game.Net.CarLane>(e).m_Flags;
      if(laneCache.TryGetValue(e,out var previous)&&state.Same(previous))continue;
      laneCache[e]=state;
      object curve=null;string flags=null;
      if(EntityManager.HasComponent<Game.Net.Curve>(e)){var c=EntityManager.GetComponentData<Game.Net.Curve>(e);curve=new {Length=c.m_Length,A=new[]{c.m_Bezier.a.x,c.m_Bezier.a.y,c.m_Bezier.a.z},B=new[]{c.m_Bezier.b.x,c.m_Bezier.b.y,c.m_Bezier.b.z},C=new[]{c.m_Bezier.c.x,c.m_Bezier.c.y,c.m_Bezier.c.z},D=new[]{c.m_Bezier.d.x,c.m_Bezier.d.y,c.m_Bezier.d.z}};}
      if(EntityManager.HasComponent<Game.Net.CarLane>(e))flags=EntityManager.GetComponentData<Game.Net.CarLane>(e).m_Flags.ToString();
      Write(new {Kind="lane",Direction=state.Direction,Sequence=sequence,Entity=Id(e),Owner=Id(EntityManager.HasComponent<Owner>(e)?EntityManager.GetComponentData<Owner>(e).m_Owner:Entity.Null),Start=RoadInspector.Key(l.m_StartNode),Middle=RoadInspector.Key(l.m_MiddleNode),End=RoadInspector.Key(l.m_EndNode),Flags=flags,Curve=curve});
     }
     // Bound vehicle path cache to the actively followed set. Lane definitions are
      // session-wide so a returning path can reuse unchanged geometry.
      if(sequence%256==0){var active=new HashSet<Entity>(included);var expired=new List<Entity>();foreach(var e in pathCache.Keys)if(!active.Contains(e))expired.Add(e);foreach(var e in expired){pathCache.Remove(e);pathRevisions.Remove(e);navigationCache.Remove(e);}}
      Write(new {Kind="frameEnd",Sequence=sequence,CaptureMilliseconds=watch.Elapsed.TotalMilliseconds});
      if(now>=nextFlushSeconds){writer.Flush();nextFlushSeconds=now+1;
       // Hard cap: a forgotten recording must not fill the disk.
       if(writer.BaseStream.Length>MaxBytes){Stop("录制文件达到 1 GB 上限，已自动停止");Diagnostics.Event("recordingCapped",new {FilePath});return;}}
    }
   }
   catch(Exception ex){Mod.Log.Warn(ex.ToString());try{Stop("写入失败："+ex.Message);}catch{if(writer!=null){writer.Dispose();writer=null;}}Status="记录失败："+ex.Message;}
   finally{LastMilliseconds=watch.Elapsed.TotalMilliseconds;}
  }
  protected override void OnGameLoadingComplete(Purpose purpose,GameMode mode){Stop("城市加载完成，结束上一记录");base.OnGameLoadingComplete(purpose,mode);}
  protected override void OnDestroy(){Stop("记录系统关闭");base.OnDestroy();}
 }
}
