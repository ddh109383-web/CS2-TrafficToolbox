using System;
using Colossal.Serialization.Entities;
using Game.Common;
using RoadLaneLock.Experimental;
using Unity.Collections;
using Unity.Entities;
namespace Game.Simulation
{
 public partial class RoadLaneLockNavigationSystem : ISerializable, IDefaultSerializable
 {
  private NativeParallelHashMap<Entity,DetourRoute> m_Detours;
  private NativeQueue<DetourEvent> m_DetourEvents;
  private NativeParallelHashMap<Entity,TurnWatch> m_TurnWatches;
  private NativeQueue<TurnWatch> m_TurnUpdates;
  private uint m_CleanupTick;
  private NativeParallelHashMap<Decision,ExitHistory> m_ExitHistory;
  private NativeParallelHashMap<Entity,ReplanBudget> m_ReplanBudgets;
  private double m_ActiveSeconds;
  private int m_LastRenderFrame=-1;
  public void SampleTurnClock()
  {
   int frame=UnityEngine.Time.frameCount;
   if(frame==m_LastRenderFrame)return;
   m_LastRenderFrame=frame;
   if(m_SimulationSystem!=null&&m_SimulationSystem.selectedSpeed>0)m_ActiveSeconds+=UnityEngine.Time.unscaledDeltaTime;
  }
  private void RememberExit(DetourRoute route)
  {
   var key=route.Decision;
   if(key.Target==Entity.Null||route.ExitIndex<1||route.ExitIndex>=route.Lanes.Length)return;
   m_ExitHistory.TryGetValue(key,out var history);
   var from=route.Lanes[route.ExitIndex-1];var to=route.Lanes[route.ExitIndex];
   m_ReplanBudgets.Remove(key.Vehicle);
   if(!history.Connections.Remember(from,to))return;
   if(m_ExitHistory.Count()==m_ExitHistory.Capacity)m_ExitHistory.Capacity*=2;
   history.LastUsed=m_SimulationSystem.frameIndex;
   m_ExitHistory[key]=history;
  }
  private NativeParallelHashMap<Entity,ServiceAttemptState> m_ServiceAttempts;
  private NativeQueue<ServiceAttemptEvent> m_ServiceEvents;
  private NativeParallelHashMap<Entity,byte> m_RuleRoads;
  public int RuleRevision {get;private set;}
  public bool ExecutionEnabled {get;private set;}
  public bool HasRulesOrDetours=>monitors.Count>0||m_RuleRoads.Count()>0||m_Detours.Count()>0;
  public void CopyProtectedRoads(System.Collections.Generic.List<Entity> output,bool includePending=false)
  {
   DrainExecution();output.Clear();
   using(var keys=m_RuleRoads.GetKeyArray(Allocator.Temp))
    for(int i=0;i<keys.Length;i++)if(includePending||m_RuleRoads[keys[i]]!=0)output.Add(keys[i]);
  }
  public object CaptureRecordingState()
  {
   DrainExecution();
   var roads=new System.Collections.Generic.List<object>();var routes=new System.Collections.Generic.List<object>();
   using(var keys=m_RuleRoads.GetKeyArray(Allocator.Temp))for(int i=0;i<keys.Length;i++)roads.Add(new {keys[i].Index,keys[i].Version,DirectionMask=m_RuleRoads[keys[i]]});
   using(var pairs=m_Detours.GetKeyValueArrays(Allocator.Temp))for(int i=0;i<pairs.Length;i++){
    var route=pairs.Values[i];var lanes=new System.Collections.Generic.List<object>();
    for(int n=0;n<route.Lanes.Length;n++)lanes.Add(new {route.Lanes[n].Index,route.Lanes[n].Version});
    routes.Add(new {Vehicle=new {pairs.Keys[i].Index,pairs.Keys[i].Version},route.ExitIndex,Lanes=lanes});
   }
   var services=new System.Collections.Generic.List<object>();
   using(var pairs=m_ServiceAttempts.GetKeyValueArrays(Allocator.Temp))for(int i=0;i<pairs.Length;i++){
    var value=pairs.Values[i];services.Add(new {Vehicle=new {pairs.Keys[i].Index,pairs.Keys[i].Version},value.Policy.Failures,NativeFallback=value.Policy.Native,value.Released,Target=new{value.Target.Index,value.Target.Version}});
   }
   var actions=new System.Collections.Generic.List<object>();
   while(m_PlanObservations.TryDequeue(out var o))actions.Add(new {Vehicle=new{o.Vehicle.Index,o.Vehicle.Version}, Lane=new{o.CurrentLane.Index,o.CurrentLane.Version},Action=o.Action.ToString()});
   return new {ExecutionEnabled,Actions=actions,Roads=roads,Detours=routes,ServiceFallbacks=services};
  }
  public void CompleteExecution(){DrainExecution();}
  public void ClearRulesAndRequestOriginalPaths()
  {
   DrainExecution();EntityManager.CompleteAllTrackedJobs();
   using(var vehicles=m_Detours.GetKeyArray(Allocator.Temp))
    for(int i=0;i<vehicles.Length;i++)
    {
     var vehicle=vehicles[i];
     if(!EntityManager.Exists(vehicle)||!EntityManager.HasComponent<Game.Pathfind.PathOwner>(vehicle))continue;
     var path=EntityManager.GetComponentData<Game.Pathfind.PathOwner>(vehicle);
     path.m_State|=Game.Pathfind.PathFlags.Obsolete;path.m_ElementIndex=0;
     EntityManager.SetComponentData(vehicle,path);
     if(EntityManager.HasBuffer<Game.Pathfind.PathElement>(vehicle))EntityManager.GetBuffer<Game.Pathfind.PathElement>(vehicle).Clear();
    }
   ResetExecution(true);
  }
  public int SavedRoadCount=>m_RuleRoads.Count();
  public int ProtectedRoadCount {get{DrainExecution();return m_ProtectedOwners.Count();}}
  public int DetourCount {get{DrainExecution();return m_Detours.Count();}}
  private void CreateExecution()
  {
   offers=new NativeParallelHashMap<Entity,Candidate>(32,Allocator.Persistent);monitorResults=new NativeQueue<MonitorResult>(Allocator.Persistent);
   m_ReplanBudgets=new NativeParallelHashMap<Entity,ReplanBudget>(128,Allocator.Persistent);
   m_ExitHistory=new NativeParallelHashMap<Decision,ExitHistory>(128,Allocator.Persistent);
   m_TurnWatches=new NativeParallelHashMap<Entity,TurnWatch>(64,Allocator.Persistent);
   m_TurnUpdates=new NativeQueue<TurnWatch>(Allocator.Persistent);
   m_ServiceAttempts=new NativeParallelHashMap<Entity,ServiceAttemptState>(128,Allocator.Persistent);
   m_ServiceEvents=new NativeQueue<ServiceAttemptEvent>(Allocator.Persistent);
   m_RuleRoads=new NativeParallelHashMap<Entity,byte>(8,Allocator.Persistent);
   m_Detours=new NativeParallelHashMap<Entity,DetourRoute>(128,Allocator.Persistent);
   m_DetourEvents=new NativeQueue<DetourEvent>(Allocator.Persistent);
  }
  private void DisposeExecution()
  {if(m_ReplanBudgets.IsCreated)m_ReplanBudgets.Dispose();if(offers.IsCreated)offers.Dispose();if(monitorResults.IsCreated)monitorResults.Dispose();if(m_ExitHistory.IsCreated)m_ExitHistory.Dispose();if(m_TurnWatches.IsCreated)m_TurnWatches.Dispose();if(m_TurnUpdates.IsCreated)m_TurnUpdates.Dispose();if(m_ServiceAttempts.IsCreated)m_ServiceAttempts.Dispose();if(m_ServiceEvents.IsCreated)m_ServiceEvents.Dispose();if(m_RuleRoads.IsCreated)m_RuleRoads.Dispose();if(m_Detours.IsCreated)m_Detours.Dispose();if(m_DetourEvents.IsCreated)m_DetourEvents.Dispose();}
  private void DrainExecution()
  {
   Dependency.Complete();
   DrainMonitorResults();
   while(m_TurnUpdates.TryDequeue(out var watch))
   {
    if(watch.Entry==Entity.Null){m_TurnWatches.Remove(watch.Vehicle);continue;}
    if(!m_TurnWatches.ContainsKey(watch.Vehicle)&&m_TurnWatches.Count()==m_TurnWatches.Capacity)m_TurnWatches.Capacity*=2;
    m_TurnWatches[watch.Vehicle]=watch;
   }
   while(m_DetourEvents.TryDequeue(out var item))
   {
    if(item.FailedDecision.Vehicle!=Entity.Null)
    {
     if(m_ExitHistory.TryGetValue(item.FailedDecision,out var history))
     {
      history.RetryAfter=m_ActiveSeconds+2.5;
      var vehicle=item.FailedDecision.Vehicle;
      if(!m_ReplanBudgets.ContainsKey(vehicle) && m_ReplanBudgets.Count()==m_ReplanBudgets.Capacity)m_ReplanBudgets.Capacity*=2;
      m_ReplanBudgets[vehicle]=item.Budget;
      history.LastUsed=m_SimulationSystem.frameIndex;
      m_ExitHistory[item.FailedDecision]=history;
     }
     continue;
    }
    if(item.Remove){RememberExit(item.Route);m_Detours.Remove(item.Vehicle);continue;}
    if(!EntityManager.Exists(item.Vehicle))continue;
    if(!m_Detours.ContainsKey(item.Vehicle)&&m_Detours.Count()==m_Detours.Capacity)m_Detours.Capacity*=2;
    if(!m_TurnWatches.TryGetValue(item.Vehicle,out var recent)||recent.AvoidNode==Entity.Null)m_TurnWatches.Remove(item.Vehicle);
    m_Detours[item.Vehicle]=item.Route;
   }
   while(m_ServiceEvents.TryDequeue(out var update))
   {
    if(update.Remove||!EntityManager.Exists(update.Vehicle)){m_ServiceAttempts.Remove(update.Vehicle);continue;}
    if(!m_ServiceAttempts.ContainsKey(update.Vehicle)&&m_ServiceAttempts.Count()==m_ServiceAttempts.Capacity)m_ServiceAttempts.Capacity*=2;
    m_ServiceAttempts[update.Vehicle]=update.State;
   }
   if(++m_CleanupTick%256==0)
   {
    using(var vehicles=m_ReplanBudgets.GetKeyArray(Allocator.Temp))
     foreach(var vehicle in vehicles)
     {
      var budget=m_ReplanBudgets[vehicle];
      if(!EntityManager.Exists(vehicle)||EntityManager.HasComponent<Deleted>(vehicle)||
         !EntityManager.HasComponent<Target>(vehicle)||EntityManager.GetComponentData<Target>(vehicle).m_Target!=budget.Target||
         !EntityManager.HasComponent<Game.Vehicles.CarCurrentLane>(vehicle)||
         EntityManager.GetComponentData<Game.Vehicles.CarCurrentLane>(vehicle).m_Lane!=budget.Entry)m_ReplanBudgets.Remove(vehicle);
     }
    using(var decisions=m_ExitHistory.GetKeyArray(Allocator.Temp))
     foreach(var key in decisions)
     {
      if(!EntityManager.Exists(key.Vehicle)||EntityManager.HasComponent<Deleted>(key.Vehicle)||
       !EntityManager.HasComponent<Target>(key.Vehicle)||EntityManager.GetComponentData<Target>(key.Vehicle).m_Target!=key.Target||
       !EntityManager.Exists(key.Entry)||
       (m_SimulationSystem.frameIndex-m_ExitHistory[key].LastUsed>32768 &&
        !(m_ReplanBudgets.TryGetValue(key.Vehicle,out var budget) && budget.Requests>=3 && EntityManager.HasComponent<Game.Vehicles.CarCurrentLane>(key.Vehicle) &&
          EntityManager.GetComponentData<Game.Vehicles.CarCurrentLane>(key.Vehicle).m_Lane==key.Entry)))m_ExitHistory.Remove(key);
     }
    using(var watches=m_TurnWatches.GetKeyArray(Allocator.Temp))
     for(int i=0;i<watches.Length;i++)if(!EntityManager.Exists(watches[i])||EntityManager.HasComponent<Deleted>(watches[i]))m_TurnWatches.Remove(watches[i]);
    using(var released=m_ServiceAttempts.GetKeyArray(Allocator.Temp))
     for(int i=0;i<released.Length;i++)if(!EntityManager.Exists(released[i])||EntityManager.HasComponent<Deleted>(released[i]))m_ServiceAttempts.Remove(released[i]);
    using(var roads=m_RuleRoads.GetKeyArray(Allocator.Temp))
     for(int i=0;i<roads.Length;i++)
      if(!EntityManager.Exists(roads[i])||EntityManager.HasComponent<Deleted>(roads[i])||!EntityManager.HasComponent<Game.Net.Edge>(roads[i])){m_RuleRoads.Remove(roads[i]);RuleRevision++;}
    RefreshProtectedOwners();
    using(var vehicles=m_Detours.GetKeyArray(Allocator.Temp))
     for(int i=0;i<vehicles.Length;i++)
      if(!EntityManager.Exists(vehicles[i])||EntityManager.HasComponent<Deleted>(vehicles[i]))m_Detours.Remove(vehicles[i]);
   }
  }
  public byte DirectionMask(Entity road)
  {return m_RuleRoads.TryGetValue(road,out var mask)?mask:(byte)0;}
  // Vehicles currently detouring, attributed to the road owning the lane where the detour was decided.
  public void CountDetoursByRoad(System.Collections.Generic.Dictionary<Entity,int> output)
  {
   DrainExecution();output.Clear();
   using(var routes=m_Detours.GetValueArray(Allocator.Temp))
    for(int i=0;i<routes.Length;i++){
     var lane=routes[i].Decision.Entry;
     if(lane==Entity.Null||!EntityManager.Exists(lane)||!EntityManager.HasComponent<Owner>(lane))continue;
     var road=EntityManager.GetComponentData<Owner>(lane).m_Owner;
     output.TryGetValue(road,out int n);output[road]=n+1;
    }
  }
  public bool NeedsDirection(Entity road)=>m_RuleRoads.TryGetValue(road,out var mask)&&mask==0;
  public void ConfigureExecution(Entity road,byte direction,bool enable)
  {
   DrainExecution();
   if(!EntityManager.Exists(road)||!EntityManager.HasComponent<Game.Net.Edge>(road)||EntityManager.HasComponent<Deleted>(road))throw new ArgumentException("请选择有效路段。");
   if(direction!=1&&direction!=2)throw new ArgumentException("请点选行驶方向。");
   byte previous=DirectionMask(road);
   byte mask=(byte)(enable?previous|direction:previous&~direction);
   if(mask!=0)
   {
    if(m_RuleRoads.Count()==m_RuleRoads.Capacity)m_RuleRoads.Capacity*=2;
    m_RuleRoads[road]=mask;
   }
   else m_RuleRoads.Remove(road);
   RuleRevision++;m_ReplanBudgets.Clear();m_ExitHistory.Clear();
   RefreshProtectedOwners();
   ExecutionEnabled=true;
  }
  public void Serialize<W>(W writer) where W:IWriter
  {
   DrainExecution();writer.Write(3);
   using(var roads=m_RuleRoads.GetKeyArray(Allocator.Temp))
   {writer.Write(roads.Length);for(int i=0;i<roads.Length;i++){writer.Write(roads[i]);writer.Write((int)m_RuleRoads[roads[i]]);}}
   using(var routes=m_Detours.GetKeyValueArrays(Allocator.Temp))
   {
    writer.Write(routes.Length);
    for(int i=0;i<routes.Length;i++)
    {
     writer.Write(routes.Keys[i]);var route=routes.Values[i];writer.Write(route.ExitIndex);writer.Write(route.Lanes.Length);
     for(int n=0;n<route.Lanes.Length;n++)writer.Write(route.Lanes[n]);
    }
   }
   writer.Write(monitors.Count);foreach(var key in monitors.Keys){writer.Write(key.Road);writer.Write((int)key.Direction);}
  }
  public void Deserialize<R>(R reader) where R:IReader
  {
   ResetExecution();reader.Read(out int version);
   if(version<1||version>3)throw new InvalidOperationException("不支持的道路规则存档版本。");
   reader.Read(out int count);CheckCount(count,100000);
   if(count>m_RuleRoads.Capacity)m_RuleRoads.Capacity=count;
   for(int i=0;i<count;i++){reader.Read(out Entity road);int mask=3;if(version>=2)reader.Read(out mask);CheckCount(mask,3);if(road!=Entity.Null)m_RuleRoads.TryAdd(road,RoadLaneLock.Core.NavigationSafety.RestoreMask(mask));}
   reader.Read(out count);CheckCount(count,1000000);
   if(count>m_Detours.Capacity)m_Detours.Capacity=count;
   for(int i=0;i<count;i++)
   {
    reader.Read(out Entity vehicle);reader.Read(out int exit);reader.Read(out int length);CheckCount(length,32);
    var route=new DetourRoute{ExitIndex=exit};
    for(int n=0;n<length;n++){reader.Read(out Entity lane);route.Lanes.Add(lane);}
    if(length<2||exit<1||exit>=length)throw new InvalidOperationException("绕行记录损坏。");
    if(version>=2&&vehicle!=Entity.Null)m_Detours.TryAdd(vehicle,route);
   }
   if(version>=3){reader.Read(out count);CheckCount(count,200000);for(int i=0;i<count;i++){reader.Read(out Entity road);reader.Read(out int dir);if(road!=Entity.Null&&(dir==1||dir==2))monitors[(road,(byte)dir)]=new DirectionMonitor();}}
   ExecutionEnabled=true;
  }
  private static void CheckCount(int value,int max)
  {if(value<0||value>max)throw new InvalidOperationException("道路规则存档长度无效。");}
  private void ResetExecution(bool keepMonitoring=false)
  {Dependency.Complete();offers.Clear();monitorResults.Clear();nextMonitorSample=0;MonitorMessage=null;if(!keepMonitoring)monitors.Clear();RuleRevision++;m_ReplanBudgets.Clear();m_ExitHistory.Clear();m_TurnWatches.Clear();m_TurnUpdates.Clear();m_ServiceAttempts.Clear();m_ServiceEvents.Clear();m_RuleRoads.Clear();m_ProtectedOwners.Clear();m_Detours.Clear();m_DetourEvents.Clear();m_PlanObservations.Clear();ExecutionEnabled=monitors.Count>0;}
  public void SetDefaults(Context context){ResetExecution();}
  public void RefreshProtectedOwners()
  {
   Dependency.Complete();m_ProtectedOwners.Clear();
   int capacity=mathMax(8,m_RuleRoads.Count()*3);
   if(m_ProtectedOwners.Capacity<capacity)m_ProtectedOwners.Capacity=capacity;
   using(var roads=m_RuleRoads.GetKeyArray(Allocator.Temp))
    for(int i=0;i<roads.Length;i++)
    {
     var road=roads[i];
     if(!EntityManager.Exists(road)||!EntityManager.HasComponent<Game.Net.Edge>(road)||EntityManager.HasComponent<Deleted>(road))continue;
     byte mask=m_RuleRoads[road];
     if(mask!=0)m_ProtectedOwners.TryAdd(road,mask);
    }
  }
  private static int mathMax(int a,int b)=>a>b?a:b;
 }
}
