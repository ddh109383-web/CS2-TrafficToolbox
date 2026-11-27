using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using VFlags=Game.Vehicles.CarLaneFlags;
using NetFlags=Game.Net.CarLaneFlags;

namespace RoadLaneLock
{
 public enum HookResult:byte{None,Locked,NoExitInView,Unreachable,Unsupported,Changing,Held,Handoff,Released,Bypass,Rerouted}
 // Stop timer input: a vehicle on a monitored lane is standing (true) or moving / gone (false).
 // Red: the head is held by a red light at the junction stop line; its timer restarts, so only the
 // time after the light turns green counts (vehicles queued mid-road keep their full time).
 public struct StopSample{public Entity Vehicle;public bool Stopped,Red;}
 // Route check after a handoff: 1 the new route is fine, 2 still an unsafe U-turn after 3 tries,
 // 3 emergency vehicle (not checked), 4 no straight way past the U-turn; 2-4 leave it to the game.
 public struct WatchEvent{public Entity Vehicle;public byte Kind;}
 // A standing queue head on a monitored lane that was not released, and why:
 // 1 stopping / working, 2 no blocker, 3 red light (no longer logged: red time resets the timer), 4 not turning,
 // 5 a vehicle ahead in the junction lane, 6 queue behind not long enough yet, 7 waiting for a new route,
 // 8 qualifies but no straight way out (map shows it red).
 // "No blocker" (2) excludes left turns / U-turns waiting at a Yield or Stop junction lane: those are gap waits.
 // Detail: route state, game flags, signals on its lane and the next junction lane, the next move
 // (0 none in view, 1 straight, 2 left, 3 right, 4 U-turn), blocker type and the next lane.
 public struct JamMiss{public Entity Vehicle,Lane,Next;public byte Reason;public int Queue;public uint Standing;
  public PathFlags Path;public CarFlags Car;public Game.Vehicles.CarLaneFlags LaneFlags,NextFlags;public sbyte Signal,NextSignal;public byte Move,Blocker,Nav;}
 public enum HookReason:byte{None,Emergency,PathState,CurrentSpecial,NoEntry,EmptyLayer,Unsolvable}
 public struct HookTrace
 {
  public Entity Vehicle,Lane,ChangeLane,PlannedEntry,ExitLane,TurnLane;
  public bool Relevant,OnProtected;
  public HookResult Result;
  public HookReason Reason;
  public ushort PathState;
  public bool Early;
  public Entity StopLane;
  public byte StopKind; // 1 stop on the way (kerb, station), 2 route ends here
  // Jam relief. Released: JamWait 1 U-turn / 2 left / 3 right, JamFull the turn's target lane is full,
  // JamTrigger 1 queue back to the previous junction / 2 queue over 60% of the lane / 3 standing 15 s.
  // Bypass: JamHead is the head the vehicle queues behind. Rerouted: JamHead is the avoided U-turn lane.
  public Entity JamHead;
  public byte JamWait,JamTrigger;
  public bool JamFull;
 }
 // Kerb work (garbage, mail, road maintenance) inside a stretch: 1 served as routed, 2 skipped
 // because it conflicts with the exit lane, 3 served after too many skips (then leaves on its lane).
 public struct StopEvent{public Entity Vehicle;public ulong Key;public byte Kind;}

 // Hooked lane control (prototype). Runs before the game's CarNavigationSystem, which stays
 // enabled: it only rewrites CarNavigationLane entries (lane + FixedLane) for vehicles whose
 // look-ahead touches a protected stretch. The exit is read from the full PathElement route,
 // so the entry lane is chosen backwards from where the vehicle leaves the stretch.
 public partial class LaneHookSystem:GameSystemBase
 {
  const int MaxLayers=32,MaxCandidates=MaxLayers*64;
  const int SkipLimit=3;
  // Work: a kerb stop of a garbage truck / post van / maintenance vehicle on this lane.
  // Decision: 0 undecided, 1 serve, 2 skip. Sidewalk/positions locate the kerb lane for stops beyond the look-ahead.
  struct Step{public Entity Lane;public int Nav;public bool Pinned;public byte Stop;public bool Work,Tentative;public byte Decision;public ulong Key;public Entity Sidewalk;public float SidewalkPos,StopPos;}
  struct Candidate{public Entity Lane;public PathNode Start,End;public float Cost;}

  [BurstCompile]
  struct HookJob:IJobChunk
  {
   [ReadOnly] public EntityTypeHandle EntityType;
   [ReadOnly] public ComponentTypeHandle<Car> CarType;
   public ComponentTypeHandle<PathOwner> PathOwnerType;
   public BufferTypeHandle<PathElement> PathElementType;
   public ComponentTypeHandle<CarCurrentLane> CurrentLaneType;
   public BufferTypeHandle<CarNavigationLane> NavigationType;
   [ReadOnly] public ComponentLookup<Owner> Owners;
   [ReadOnly] public ComponentLookup<Lane> Lanes;
   [ReadOnly] public ComponentLookup<Game.Net.CarLane> CarLanes;
   [ReadOnly] public ComponentLookup<MasterLane> Masters;
   [ReadOnly] public ComponentLookup<SlaveLane> Slaves;
   [ReadOnly] public ComponentLookup<EdgeLane> EdgeLanes;
   [ReadOnly] public ComponentLookup<Deleted> DeletedLanes;
   [ReadOnly] public BufferLookup<Game.Net.SubLane> SubLanes;
   [ReadOnly] public ComponentLookup<Game.Net.Edge> Edges;
   [ReadOnly] public BufferLookup<ConnectedEdge> ConnectedEdges;
   [ReadOnly] public NativeParallelHashMap<Entity,Entity> Held; // vehicle -> road it must leave before the handoff
   [ReadOnly] public NativeParallelHashMap<Entity,byte> Rules;
   [ReadOnly] public NativeParallelHashSet<Entity> Tracked;
   public NativeQueue<HookTrace>.ParallelWriter Traces;
   public bool Record;
   [ReadOnly] public ComponentTypeHandle<Game.Vehicles.GarbageTruck> GarbageType;
   [ReadOnly] public ComponentTypeHandle<Game.Vehicles.PostVan> PostVanType;
   [ReadOnly] public ComponentTypeHandle<Game.Vehicles.MaintenanceVehicle> MaintenanceType;
   [ReadOnly] public ComponentLookup<Curve> Curves;
   [ReadOnly] public ComponentLookup<Game.Net.PedestrianLane> Pedestrians;
   [ReadOnly] public NativeParallelMultiHashMap<Entity,ulong> Decisions; // value: key<<2 | decision
   [ReadOnly] public NativeParallelHashMap<ulong,int> SkipCounts;
   public NativeQueue<StopEvent>.ParallelWriter StopEvents;
   // Jam relief on monitored road directions: standing time per vehicle (simulation frame it stopped).
   [ReadOnly] public ComponentLookup<Moving> Movings;
   [ReadOnly] public ComponentLookup<Blocker> Blockers;
   [ReadOnly] public ComponentLookup<Controller> Controllers;
   [ReadOnly] public BufferLookup<LaneObject> LaneObjects;
   [ReadOnly] public NativeParallelHashMap<Entity,uint> StoppedSince;
   [ReadOnly] public NativeParallelHashMap<Entity,uint> StallFree; // stalled on a locked lane: left to the game for a while
   public NativeQueue<StopSample>.ParallelWriter StopSamples;
   public uint Frame;
   [ReadOnly] public NativeParallelHashMap<Entity,byte> Monitors;  // road -> monitored direction mask
   [ReadOnly] public NativeParallelHashMap<Entity,uint> JamHeads;  // released heads (frame)
   [ReadOnly] public NativeParallelHashMap<Entity,int2> Watch;     // handed-off vehicles: x reroutes so far, y frame
   public NativeQueue<WatchEvent>.ParallelWriter WatchEvents;
   public NativeQueue<JamMiss>.ParallelWriter JamMisses;
   [ReadOnly] public ComponentLookup<LaneSignal> Signals;
   // Lane bans (车道规则 · 分道): lane -> vehicle kinds that may not use it; road -> directions with bans
   // per kind (bit kind*2 + direction-1). Kinds: RoadRules.KCar .. KService.
   [ReadOnly] public NativeParallelHashMap<Entity,byte> BanLanes;
   [ReadOnly] public NativeParallelHashMap<Entity,ushort> BanRoads;
   [ReadOnly] public ComponentTypeHandle<Game.Vehicles.PersonalCar> PersonalType;
   [ReadOnly] public ComponentTypeHandle<Game.Vehicles.Bicycle> BicycleType;
   [ReadOnly] public ComponentTypeHandle<Game.Vehicles.Taxi> TaxiType;
   [ReadOnly] public ComponentTypeHandle<Game.Vehicles.PublicTransport> BusType;
   [ReadOnly] public ComponentTypeHandle<Game.Vehicles.DeliveryTruck> TruckType;
   [ReadOnly] public ComponentTypeHandle<Game.Vehicles.Hearse> HearseType;
   [ReadOnly] public ComponentTypeHandle<Game.Prefabs.PrefabRef> PrefabType;
   [ReadOnly] public NativeParallelHashSet<Entity> MotoPrefabs; // personal vehicle prefabs that are motorbikes
   // Fixed-route transit: a line whose stop on a lane-change road cannot be reached under the lock
   // drives that road the game's way (key: line<<32 | road). Lines found unsolvable are queued.
   [ReadOnly] public ComponentTypeHandle<Game.Routes.CurrentRoute> RouteType;
   [ReadOnly] public NativeParallelHashSet<ulong> LineExempt;
   public NativeQueue<ulong>.ParallelWriter Exempt;
   byte kind;int kindIndex;Entity line;

   public void Execute(in ArchetypeChunk chunk,int unfilteredChunkIndex,bool useEnabledMask,in v128 chunkEnabledMask)
   {
    var entities=chunk.GetNativeArray(EntityType);
    var cars=chunk.GetNativeArray(ref CarType);
    var pathOwners=chunk.GetNativeArray(ref PathOwnerType);
    var currents=chunk.GetNativeArray(ref CurrentLaneType);
    var navigation=chunk.GetBufferAccessor(ref NavigationType);
    var paths=chunk.GetBufferAccessor(ref PathElementType);
    var steps=new NativeList<Step>(MaxLayers,Allocator.Temp);
    var candidates=new NativeList<Candidate>(64,Allocator.Temp);
    var layers=new NativeList<int3>(MaxLayers,Allocator.Temp);
    var plan=new NativeList<Entity>(MaxLayers,Allocator.Temp);
    bool work=chunk.Has(ref GarbageType)||chunk.Has(ref PostVanType)||chunk.Has(ref MaintenanceType);
    // Which 分道 kind this chunk's vehicles are (emergency vehicles: none, never restricted).
    kind=0;
    if(BanRoads.Count()>0)
    {
     if(chunk.Has(ref TaxiType))kind=RoadRules.KTaxi;
     else if(chunk.Has(ref BusType))kind=RoadRules.KBus;
     else if(work||chunk.Has(ref HearseType))kind=RoadRules.KService;
     else if(chunk.Has(ref TruckType))kind=RoadRules.KTruck;
     // Bicycles carry PersonalCar too; their bans are the vanilla ForbidBicycles flag, never steered here.
     else if(chunk.Has(ref BicycleType))kind=0;
     else if(chunk.Has(ref PersonalType))kind=RoadRules.KCar;
    }
    byte chunkKind=kind;
    var prefabs=chunkKind==RoadRules.KCar&&MotoPrefabs.Count()>0?chunk.GetNativeArray(ref PrefabType):default;
    bool routed=chunk.Has(ref RouteType);
    var routes=routed?chunk.GetNativeArray(ref RouteType):default;
    for(int i=0;i<chunk.Count;i++)
    {
     kind=prefabs.IsCreated&&MotoPrefabs.Contains(prefabs[i].m_Prefab)?RoadRules.KMoto:chunkKind;
     kindIndex=kind==0?-1:math.tzcnt((int)kind);
     line=routed?routes[i].m_Route:Entity.Null;
     var current=currents[i];var owner=pathOwners[i];
     if(Process(entities[i],cars[i],work,ref owner,ref current,navigation[i],paths[i],ref steps,ref candidates,ref layers,ref plan))
     {currents[i]=current;pathOwners[i]=owner;}
    }
    steps.Dispose();candidates.Dispose();layers.Dispose();plan.Dispose();
   }

   static ulong LineKey(Entity line,Entity road)=>((ulong)(uint)line.Index<<32)|(uint)road.Index;
   bool Protected(Entity lane)
   {
    if(lane==Entity.Null||!Owners.TryGetComponent(lane,out var owner))return false;
    bool rule=Rules.TryGetValue(owner.m_Owner,out var mask);
    if(rule&&line!=Entity.Null&&LineExempt.Contains(LineKey(line,owner.m_Owner))){rule=false;mask=0;}
    // A vehicle kind banned from some lanes treats that road like a lane-change road: it keeps to an allowed lane.
    if(kind!=0&&BanRoads.TryGetValue(owner.m_Owner,out var bans)){byte own=(byte)((bans>>(kindIndex*2))&3);if(own!=0){mask|=own;rule=true;}}
    if(!rule)return false;
    if(!EdgeLanes.TryGetComponent(lane,out var edge)||!CarLanes.TryGetComponent(lane,out var car)||DeletedLanes.HasComponent(lane))return false;
    if((car.m_Flags&(NetFlags.SideConnection|NetFlags.Twoway))!=0)return false;
    return (mask&Core.NavigationSafety.Direction(edge.m_EdgeDelta.x,edge.m_EdgeDelta.y))!=0;
   }

   bool Process(Entity entity,Car car,bool work,ref PathOwner pathOwner,ref CarCurrentLane current,DynamicBuffer<CarNavigationLane> nav,DynamicBuffer<PathElement> path,
    ref NativeList<Step> steps,ref NativeList<Candidate> candidates,ref NativeList<int3> layers,ref NativeList<Entity> plan)
   {
    bool onProtected=Protected(current.m_Lane)||Protected(current.m_ChangeLane);
    bool relevant=onProtected;
    for(int i=0;i<nav.Length&&!relevant;i++)relevant=Protected(nav[i].m_Lane);
    var trace=new HookTrace{Vehicle=entity,Lane=current.m_Lane,ChangeLane=current.m_ChangeLane,Relevant=relevant,OnProtected=onProtected,PathState=(ushort)pathOwner.m_State};
    // A vehicle held on a fixed exit (strict hold, jam release, U-turn reroute): once it is on an
    // ordinary road past the one it had to leave, give it back to the game's pathfinding.
    if(!onProtected&&Held.TryGetValue(entity,out var leave)&&current.m_ChangeLane==Entity.Null&&EdgeLanes.HasComponent(current.m_Lane)&&
       (!Owners.TryGetComponent(current.m_Lane,out var here)||here.m_Owner!=leave))
    {
     current.m_CurvePosition.z=1;
     current.m_LaneFlags|=VFlags.FixedLane|VFlags.ClearedForPathfind;
     current.m_LaneFlags&=~(VFlags.EndOfPath|VFlags.GroupTarget|VFlags.TransformTarget);
     nav.Clear();path.Clear();pathOwner.m_ElementIndex=0;pathOwner.m_State|=PathFlags.Obsolete;
     trace.Result=HookResult.Handoff;
     Traces.Enqueue(trace);
     return true;
    }
    var forbidden=VehicleUtils.GetForbiddenLaneFlags(car,false);
    // After a handoff: the game's new route must not start with an unsafe mid-block U-turn.
    if(Watch.TryGetValue(entity,out var watch)&&!Held.ContainsKey(entity))
    {
     // Emergency vehicles on duty are never steered (they may U-turn wherever they need to).
     if((car.m_Flags&CarFlags.Emergency)!=0)WatchEvents.Enqueue(new WatchEvent{Vehicle=entity,Kind=3});
     else if(CheckNewRoute(watch,forbidden,ref current,ref pathOwner,nav,path,ref trace)){Traces.Enqueue(trace);return true;}
    }
    // Jam relief on a monitored road direction. A head whose own lane ahead is clear but which has
    // stood 4 s waiting to turn (not at a red light) while the queue behind it backs up gives up the
    // turn and drives straight on; vehicles queued behind such a head change lanes the game's way.
    if(Monitored(current.m_Lane)&&current.m_ChangeLane==Entity.Null)
    {
     bool standing=Standing(entity);
     StopSamples.Enqueue(new StopSample{Vehicle=entity,Stopped=standing});
     if(standing&&!Held.ContainsKey(entity)&&(car.m_Flags&CarFlags.Emergency)==0)
     {
      byte trigger=JamHeadTrigger(entity,pathOwner.m_State,current,nav,ref trace,out byte miss,out int queue,out uint standingFrames);
      if(miss!=0)JamMisses.Enqueue(Miss(entity,car,pathOwner,current,nav,miss,queue,standingFrames));
      if(trigger!=0)
      {
       if(Release(ref current,ref pathOwner,nav,path,forbidden,trace.TurnLane))
       {trace.Result=HookResult.Released;trace.JamTrigger=trigger;Traces.Enqueue(trace);return true;}
       // Qualifies but there is no straight way out: shown red on the map.
       JamMisses.Enqueue(Miss(entity,car,pathOwner,current,nav,8,queue,standingFrames));
      }
      else if(!Protected(current.m_Lane)&&BehindJamHead(entity,out var head))
      {
       Bypass(ref current,nav);
       trace.Result=HookResult.Bypass;trace.JamHead=head;Traces.Enqueue(trace);return true;
      }
     }
    }
    else if(StoppedSince.ContainsKey(entity)&&!onProtected)StopSamples.Enqueue(new StopSample{Vehicle=entity,Stopped=false});
    // Safety valve: a vehicle standing ~20 s on a locked lane with nothing in front of it (no vehicle
    // ahead on its lane, no red light, not stopping) is waiting for a lane change the lock forbids.
    // Release it to the game's pathfinding and leave it alone for a minute.
    if(onProtected&&current.m_ChangeLane==Entity.Null&&(car.m_Flags&CarFlags.Emergency)==0)
    {
     if(StallFree.ContainsKey(entity)){if(Record&&Tracked.Contains(entity))Traces.Enqueue(trace);return false;}
     if(!Monitored(current.m_Lane))StopSamples.Enqueue(new StopSample{Vehicle=entity,Stopped=Standing(entity)});
     if(Stalled(entity,current,nav,out uint stood))
     {
      JamMisses.Enqueue(Miss(entity,car,pathOwner,current,nav,9,0,stood));
      current.m_CurvePosition.z=1;
      current.m_LaneFlags&=~(VFlags.FixedLane|VFlags.EndOfPath|VFlags.GroupTarget|VFlags.TransformTarget);
      current.m_LaneFlags|=VFlags.ClearedForPathfind;
      nav.Clear();path.Clear();pathOwner.m_ElementIndex=0;pathOwner.m_State|=PathFlags.Obsolete;
      trace.Result=HookResult.Handoff;Traces.Enqueue(trace);
      return true;
     }
    }
    if(!relevant)
    {
     if(Record&&Tracked.Contains(entity))Traces.Enqueue(trace);
     return false;
    }
    bool written=Plan(car,work,ref pathOwner,ref current,nav,path,ref steps,ref candidates,ref layers,ref plan,ref trace);
    if(Record)Traces.Enqueue(trace);
    return written;
   }

   bool Plan(Car car,bool work,ref PathOwner pathOwner,ref CarCurrentLane current,DynamicBuffer<CarNavigationLane> nav,DynamicBuffer<PathElement> path,
    ref NativeList<Step> steps,ref NativeList<Candidate> candidates,ref NativeList<int3> layers,ref NativeList<Entity> plan,ref HookTrace trace)
   {
    trace.Result=HookResult.Unsupported;
    if((car.m_Flags&CarFlags.Emergency)!=0){trace.Reason=HookReason.Emergency;return false;}
    // Service vehicles keep driving on the current route while the next leg is searched
    // (Pending|Append); only a discarded or failed route is skipped.
    var state=pathOwner.m_State;
    if((state&(PathFlags.Obsolete|PathFlags.Failed))!=0||((state&(PathFlags.Pending|PathFlags.Updated))!=0&&(state&PathFlags.Append)==0))
    {trace.Reason=HookReason.PathState;return false;}
    const VFlags special=VFlags.ParkingSpace|VFlags.Connection|VFlags.Area|VFlags.TransformTarget|VFlags.GroupTarget;
    if((current.m_LaneFlags&special)!=0){trace.Reason=HookReason.CurrentSpecial;return false;}
    if(current.m_ChangeLane!=Entity.Null&&Protected(current.m_Lane)){trace.Result=HookResult.Changing;return false;}

    // 1. Lane sequence: current lane, navigation look-ahead, then the rest of the route.
    // Stops (Waypoint) and partial lanes do not cut the route; only its real end does.
    steps.Clear();
    Entity first=current.m_ChangeLane!=Entity.Null?current.m_ChangeLane:current.m_Lane;
    steps.Add(new Step{Lane=first,Nav=-1,Pinned=true,Work=work&&(current.m_LaneFlags&VFlags.Waypoint)!=0});
    bool ended=(current.m_LaneFlags&VFlags.EndOfPath)!=0,routeEnd=ended;
    for(int i=0;i<nav.Length&&!ended&&steps.Length<MaxLayers;i++)
    {
     var part=nav[i];
     if((part.m_Flags&special)!=0){ended=true;break;}
     bool target=(part.m_Flags&(VFlags.Waypoint|VFlags.EndOfPath))!=0&&(part.m_Flags&VFlags.FixedLane)!=0;
     byte stop=(byte)((part.m_Flags&VFlags.EndOfPath)!=0?2:((part.m_Flags&VFlags.Waypoint)!=0||part.m_CurvePosition.y<1f)?1:0);
     steps.Add(new Step{Lane=part.m_Lane,Nav=i,Pinned=target||(part.m_Flags&(VFlags.Reserved|VFlags.FixedStart))!=0,Stop=stop,Work=work&&(part.m_Flags&VFlags.Waypoint)!=0});
     if((part.m_Flags&VFlags.EndOfPath)!=0)ended=routeEnd=true;
    }
    if(!ended)
    {
     int p=pathOwner.m_ElementIndex;
     for(;p<path.Length&&steps.Length<MaxLayers;p++)
     {
      var element=path[p];
      if(!CarLanes.HasComponent(element.m_Target))
      {
       // A kerb stop of a service vehicle: the route continues after the sidewalk element.
       if(work&&steps.Length>1&&p<path.Length-1&&Pedestrians.HasComponent(element.m_Target))
       {
        var s=steps[steps.Length-1];
        s.Stop=1;s.Work=true;s.Sidewalk=element.m_Target;s.SidewalkPos=element.m_TargetDelta.y;
        steps[steps.Length-1]=s;continue;
       }
       ended=routeEnd=true;break;
      }
      steps.Add(new Step{Lane=element.m_Target,Nav=-1,Stop=(byte)(element.m_TargetDelta.y<1f?(p==path.Length-1?2:1):0),StopPos=element.m_TargetDelta.y});
     }
     if(p>=path.Length&&(state&PathFlags.Pending)==0)routeEnd=true;
    }

    // 2. Protected stretch [entry..lastProtected] and the first ordinary road lane after it.
    int entry=-1;
    for(int k=0;k<steps.Length;k++)if(Protected(steps[k].Lane)){entry=k;break;}
    if(entry<0){trace.Reason=HookReason.NoEntry;return false;}
    int lastProtected=entry,exit=-1;
    for(int k=entry+1;k<steps.Length;k++)
    {
     var lane=steps[k].Lane;
     if(Protected(lane)){lastProtected=k;continue;}
     if(EdgeLanes.HasComponent(lane)){exit=k;break;}
    }
    int last=exit>=0?exit:steps.Length-1;
    // Diagnostics: a stop (kerb work, station) or the route's end inside the stretch.
    for(int k=entry;k<=lastProtected;k++)
     if(steps[k].Stop!=0&&Protected(steps[k].Lane)){trace.StopLane=steps[k].Lane;trace.StopKind=steps[k].Stop;break;}
    if(trace.StopKind==0&&exit<0&&routeEnd&&Protected(steps[steps.Length-1].Lane)){trace.StopLane=steps[steps.Length-1].Lane;trace.StopKind=2;}
    trace.ExitLane=exit>=0?steps[exit].Lane:Entity.Null;
    trace.TurnLane=exit>lastProtected+1?steps[lastProtected+1].Lane:Entity.Null;

    // 3. Kerb work inside the stretch: look up what was decided for this vehicle and road side.
    var forbidden=VehicleUtils.GetForbiddenLaneFlags(car,false);
    // The route's own end inside the stretch (e.g. a kerbside stop) is a fixed target lane.
    bool endTarget=exit<0&&routeEnd&&last>0&&!Masters.HasComponent(steps[last].Lane);
    bool kerbWork=false;
    for(int k=entry;k<=lastProtected;k++)
    {
     var s=steps[k];
     if(!s.Work||!Protected(s.Lane))continue;
     s.Key=StopKey(s.Lane);s.Decision=Decided(trace.Vehicle,s.Key);steps[k]=s;kerbWork=true;
    }

    // 4. Solve backwards; choose the cheapest legal first lane. Undecided kerb stops are first
    // planned as routed (served at the kerb lane).
    int r=TrySolve(current,steps,entry,lastProtected,exit,last,endTarget,-1,forbidden,ref candidates,ref layers,ref plan,out int solvedExit,out int solvedLast);
    if(r<0){trace.Reason=HookReason.EmptyLayer;return false;}
    if(r==0&&kerbWork)
    {
     // The kerb stop and the exit need different lanes: skip the stop this time (the next vehicle
     // passing collects it), unless this road side was skipped too often already.
     bool retry=false;
     for(int k=entry;k<=lastProtected;k++)
     {
      var s=steps[k];
      if(s.Key==0||s.Decision!=0)continue;
      SkipCounts.TryGetValue(s.Key,out int skipped);
      s.Decision=(byte)(skipped<SkipLimit?2:1);s.Tentative=true;steps[k]=s;retry|=s.Decision==2;
     }
     if(retry)r=TrySolve(current,steps,entry,lastProtected,exit,last,endTarget,-1,forbidden,ref candidates,ref layers,ref plan,out solvedExit,out solvedLast);
     // Serving it: plan only up to the stop; afterwards the vehicle leaves on its lane (strict hold).
     if(r==0)
     {
      int serve=-1;
      for(int k=math.max(1,entry);k<=lastProtected&&serve<0;k++)if(steps[k].Key!=0&&steps[k].Decision==1)serve=k;
      if(serve>=1)r=TrySolve(current,steps,entry,lastProtected,exit,last,endTarget,serve,forbidden,ref candidates,ref layers,ref plan,out solvedExit,out solvedLast);
     }
     if(r<0)r=0;
    }
    if(r==0)
    {
     trace.Result=exit>=0||endTarget?HookResult.Unreachable:HookResult.Unsupported;
     trace.Reason=HookReason.Unsolvable;
     // A transit line that stops on this road and cannot both reach the stop and leave on its route:
     // the whole line drives this road the game's way from now on, so it never misses the stop.
     if(line!=Entity.Null&&trace.StopKind!=0&&trace.StopLane!=Entity.Null&&Owners.TryGetComponent(trace.StopLane,out var stopRoad))
     {Exempt.Enqueue(LineKey(line,stopRoad.m_Owner));return false;}
     return Hold(ref current,ref pathOwner,nav,path,forbidden,ref trace);
    }
    if(solvedExit<0&&exit>=0&&solvedLast>lastProtected)trace.ExitLane=Entity.Null; // route beyond the stretch not usable
    exit=solvedExit;last=solvedLast;
    if(kerbWork)
     for(int k=entry;k<=math.min(lastProtected,last);k++)
     {
      var s=steps[k];
      if(s.Key==0||(!s.Tentative&&s.Decision!=0))continue;
      StopEvents.Enqueue(new StopEvent{Vehicle=trace.Vehicle,Key=s.Key,Kind=(byte)(!s.Tentative?1:s.Decision==2?2:3)});
     }
    // Early alignment: when the road just before the stretch is still ahead, fix its lane too,
    // so the vehicle changes lanes on the road before and enters it already aligned.
    int approach=-1;
    for(int k=entry-1;k>=1;k--)if(EdgeLanes.HasComponent(steps[k].Lane)){approach=k;break;}
    if(approach>=1&&steps[approach].Nav>=0)
    {
     var approachOwner=Owners.TryGetComponent(steps[approach].Lane,out var ao)?ao.m_Owner:Entity.Null;
     int first0=approach;
     while(first0-1>=1&&steps[first0-1].Nav>=0&&Owners.TryGetComponent(steps[first0-1].Lane,out var po)&&po.m_Owner==approachOwner)first0--;
     var saved=new NativeArray<int3>(approach-first0+1,Allocator.Temp);
     for(int k=first0;k<=approach;k++){saved[k-first0]=layers[k];var l=layers[k];l.z=k==first0?0:2;layers[k]=l;}
     var early=new NativeList<Entity>(layers.Length,Allocator.Temp);
     if(Solve(candidates,layers,ref early))
     {
      plan.Clear();for(int k=0;k<early.Length;k++)plan.Add(early[k]);
      trace.Early=true;
     }
     else for(int k=first0;k<=approach;k++)layers[k]=saved[k-first0];
     early.Dispose();saved.Dispose();
     approach=trace.Early?first0:-1;
    }
    else approach=-1;
    trace.PlannedEntry=plan[entry];
    trace.Result=exit>=0?HookResult.Locked:HookResult.NoExitInView;

    // 5. Write: fix the occupied protected lane, and pin every look-ahead lane from the entry
    // turn through the exit turn. Lanes on the approach stay free so the game changes lanes there.
    bool changed=false;
    if(Protected(current.m_Lane)&&(current.m_LaneFlags&VFlags.FixedLane)==0)
    {current.m_LaneFlags|=VFlags.FixedLane;changed=true;}
    // Skipped kerb stops: drop the stop the way the game does when nothing is left to collect.
    for(int k=0;k<=last;k++)
    {
     var s=steps[k];
     if(s.Key==0||s.Decision!=2)continue;
     if(k==0){if((current.m_LaneFlags&VFlags.Waypoint)!=0){current.m_LaneFlags&=~VFlags.Waypoint;changed=true;}continue;}
     if(s.Nav<0)continue;
     var part=nav[s.Nav];
     if((part.m_Flags&VFlags.Waypoint)!=0){part.m_Flags&=~VFlags.Waypoint;nav[s.Nav]=part;changed=true;}
    }
    int writeStart=entry;
    if(entry-1>=1&&!EdgeLanes.HasComponent(steps[entry-1].Lane))writeStart=entry-1;
    if(approach>=1)writeStart=approach;
    int writeEnd=exit>=0?exit-1:last;
    for(int k=math.max(1,writeStart);k<=writeEnd;k++)
    {
     var step=steps[k];
     if(step.Nav<0)break;
     var part=nav[step.Nav];
     if(part.m_Lane!=plan[k])
     {
      part.m_Lane=plan[k];
      part.m_Flags=(part.m_Flags&~(VFlags.TurnLeft|VFlags.TurnRight|VFlags.Roundabout))|TurnFlags(plan[k]);
      part.m_Flags|=VFlags.UpdateOptimalLane;
      changed=true;
     }
     if((part.m_Flags&VFlags.FixedLane)==0){part.m_Flags|=VFlags.FixedLane;changed=true;}
     nav[step.Nav]=part;
    }
    if(changed)current.m_LaneFlags|=VFlags.UpdateOptimalLane;
    return changed;
   }

   // Candidates per layer, then solve. Ordinary road lanes allow lateral changes (any lane of the
   // group can be entered); protected lanes must chain end-to-start on one physical lane.
   // truncate>=0 plans only up to that step (a kerb stop that is served). Returns 1 solved, 0 no
   // legal lane sequence, -1 a layer without usable lanes.
   int TrySolve(in CarCurrentLane current,NativeList<Step> steps,int entry,int lastProtected,int exit,int last,bool endTarget,int truncate,NetFlags forbidden,
    ref NativeList<Candidate> candidates,ref NativeList<int3> layers,ref NativeList<Entity> plan,out int outExit,out int outLast)
   {
    if(truncate>=0){last=truncate;exit=-1;endTarget=true;}
    candidates.Clear();layers.Clear();
    Entity previousOwner=Entity.Null;
    for(int k=0;k<=last;k++)
    {
     var step=steps[k];
     bool isProtected=Protected(step.Lane);
     bool edge=EdgeLanes.HasComponent(step.Lane);
     Entity owner=Owners.TryGetComponent(step.Lane,out var o)?o.m_Owner:Entity.Null;
     // A second piece of the same road (route split at a stop) continues on the same lane
     // inside the stretch, and may change lanes elsewhere.
     bool sameEdge=k>0&&edge&&owner!=Entity.Null&&owner==previousOwner;
     previousOwner=edge?owner:Entity.Null;
     bool skip=step.Key!=0&&step.Decision==2;
     bool kerb=k>0&&step.Key!=0&&!skip&&step.Nav<0;
     bool expand=k==0?!isProtected&&edge&&current.m_ChangeLane==Entity.Null:skip||(!step.Pinned&&!(endTarget&&k==last));
     int start=candidates.Length;
     if(kerb)AddLane(KerbLane(step),0f,forbidden,ref candidates);
     else if(expand)AddGroup(step.Lane,k==exit,forbidden,ref candidates);
     else AddLane(step.Lane,0f,forbidden,ref candidates);
     if(candidates.Length==start||candidates.Length>MaxCandidates)
     {
      if(k>lastProtected+1&&k>entry){candidates.Length=start;last=k-1;exit=-1;break;}
      outExit=exit;outLast=last;return -1;
     }
     int mode=sameEdge?(isProtected?2:3):(k>0&&edge&&!isProtected?1:0);
     layers.Add(new int3(start,candidates.Length,mode));
    }
    outExit=exit;outLast=last;
    return Solve(candidates,layers,ref plan)?1:0;
   }

   // Road side of a stop: the edge and its driving direction.
   ulong StopKey(Entity lane)
   {
    if(!Owners.TryGetComponent(lane,out var owner)||!EdgeLanes.TryGetComponent(lane,out var edge))return 0;
    return ((ulong)(uint)owner.m_Owner.Index<<1)|(edge.m_EdgeDelta.x<edge.m_EdgeDelta.y?1UL:0UL)|(1UL<<40);
   }

   byte Decided(Entity vehicle,ulong key)
   {
    if(Decisions.TryGetFirstValue(vehicle,out var value,out var it))
     do{if((value>>2)==key)return (byte)(value&3);}while(Decisions.TryGetNextValue(out value,ref it));
    return 0;
   }

   // The lane the game will stop on: closest lane of the group to the sidewalk point.
   Entity KerbLane(Step step)
   {
    if(step.Sidewalk==Entity.Null||!Curves.TryGetComponent(step.Sidewalk,out var sidewalk)||!Owners.TryGetComponent(step.Lane,out var owner)||!SubLanes.TryGetBuffer(owner.m_Owner,out var lanes))return step.Lane;
    int min,max;
    if(Slaves.TryGetComponent(step.Lane,out var slave)){min=slave.m_MinIndex;max=slave.m_MaxIndex;}
    else if(Masters.TryGetComponent(step.Lane,out var master)){min=master.m_MinIndex;max=master.m_MaxIndex;}
    else return step.Lane;
    max=math.min(max,lanes.Length-1);
    if(min<0||min>max)return step.Lane;
    var position=MathUtils.Position(sidewalk.m_Bezier,step.SidewalkPos);
    int index=NetUtils.ChooseClosestLane(min,max,position,PathMethod.Road,lanes,ref Curves,step.StopPos);
    return lanes[index].m_SubLane;
   }

   // Strict lock: the vehicle is on a protected lane but its route needs a lane change.
   // Keep the lane, drive it out of the stretch on connected lanes, then hand back.
   bool Hold(ref CarCurrentLane current,ref PathOwner pathOwner,DynamicBuffer<CarNavigationLane> nav,DynamicBuffer<PathElement> path,NetFlags forbidden,ref HookTrace trace)
   {
    if(!Protected(current.m_Lane)||current.m_ChangeLane!=Entity.Null)return false;
    current.m_LaneFlags|=VFlags.FixedLane;
    if((pathOwner.m_State&PathFlags.Pending)!=0)return true; // a new route is on its way; just keep the lane
    var route=new NativeList<Entity>(32,Allocator.Temp);
    if(LegalExit(current.m_Lane,forbidden,ref route))
    {
     WriteExit(ref current,ref pathOwner,nav,path,route);
     trace.Result=HookResult.Held;
    }
    route.Dispose();
    return true;
   }

   void WriteExit(ref CarCurrentLane current,ref PathOwner pathOwner,DynamicBuffer<CarNavigationLane> nav,DynamicBuffer<PathElement> path,NativeList<Entity> route)
   {
    current.m_CurvePosition.z=1;
    current.m_LaneFlags&=~(VFlags.EndOfPath|VFlags.GroupTarget|VFlags.TransformTarget|VFlags.Waypoint);
    nav.Clear();path.Clear();
    for(int i=1;i<route.Length;i++)
    {
     if(i<=12)nav.Add(new CarNavigationLane{m_Lane=route[i],m_CurvePosition=new float2(0,1),m_Flags=VFlags.FixedLane|TurnFlags(route[i])});
     else path.Add(new PathElement{m_Target=route[i],m_TargetDelta=new float2(0,1)});
    }
    pathOwner.m_ElementIndex=0;
    current.m_LaneFlags|=VFlags.UpdateOptimalLane;
   }

   const int HeadFrames=240,LongFrames=900; // 4 s / 15 s at 60 frames per second
   const float QueueGap=10f,CarSpacing=8f;
   const int MaxReroutes=3;

   const int StallFrames=1200; // 20 s
   const int YieldStallFrames=2400; // 40 s in front of a yield / stop sign: crossing traffic can be slow
   // Stalled = nothing at all in front of it: no blocker, nobody in the junction lanes it is heading for.
   // A vehicle queueing behind one already in the junction (Blocker Continuing) is ordinary traffic.
   bool Stalled(Entity vehicle,in CarCurrentLane current,DynamicBuffer<CarNavigationLane> nav,out uint stood)
   {
    const VFlags busy=VFlags.EndOfPath|VFlags.Waypoint|VFlags.ParkingSpace|VFlags.TransformTarget|VFlags.GroupTarget|VFlags.Area|VFlags.Connection;
    stood=0;
    if(!StoppedSince.TryGetValue(vehicle,out uint since))return false;
    stood=Frame-since;
    if(stood<StallFrames||(current.m_LaneFlags&busy)!=0)return false;
    Blockers.TryGetComponent(vehicle,out var blocker);
    if(blocker.m_Type==BlockerType.Signal){StopSamples.Enqueue(new StopSample{Vehicle=vehicle,Stopped=true,Red=true});return false;}
    if(blocker.m_Type!=BlockerType.None||blocker.m_Blocker!=Entity.Null)return false;
    var road=Owners.TryGetComponent(current.m_Lane,out var o)?o.m_Owner:Entity.Null;
    for(int i=0,junction=0;i<nav.Length&&junction<2;i++)
    {
     var lane=nav[i].m_Lane;
     if(EdgeLanes.HasComponent(lane)&&Owners.TryGetComponent(lane,out var lo)&&lo.m_Owner==road)continue;
     if(LaneObjects.TryGetBuffer(lane,out var objects))
      for(int k=0;k<objects.Length;k++)if(Root(objects[k].m_LaneObject)!=vehicle)return false;
     if(junction==0)
     {
      sbyte signal=SignalOf(lane);
      if((signal==(sbyte)LaneSignalType.Yield||signal==(sbyte)LaneSignalType.Stop)&&stood<YieldStallFrames)return false;
     }
     junction++;
    }
    return true;
   }
   bool Standing(Entity vehicle)=>Movings.TryGetComponent(vehicle,out var moving)&&math.lengthsq(moving.m_Velocity)<0.25f;
   Entity Root(Entity part)=>Controllers.TryGetComponent(part,out var c)&&c.m_Controller!=Entity.Null?c.m_Controller:part;

   bool Monitored(Entity lane)
   {
    if(lane==Entity.Null||!Owners.TryGetComponent(lane,out var owner)||!Monitors.TryGetValue(owner.m_Owner,out var mask))return false;
    if(!EdgeLanes.TryGetComponent(lane,out var edge)||!CarLanes.TryGetComponent(lane,out var car)||DeletedLanes.HasComponent(lane))return false;
    if((car.m_Flags&(NetFlags.SideConnection|NetFlags.Twoway))!=0)return false;
    return (mask&Core.NavigationSafety.Direction(edge.m_EdgeDelta.x,edge.m_EdgeDelta.y))!=0;
   }

   bool OnLane(Entity vehicle,Entity lane)
   {
    if(!LaneObjects.TryGetBuffer(lane,out var objects))return false;
    for(int i=0;i<objects.Length;i++)if(Root(objects[i].m_LaneObject)==vehicle)return true;
    return false;
   }

   // A queue head: standing 4 s, waiting to turn (not at a red light) with its own lane ahead clear,
   // and the queue behind it backs up. Returns the trigger (0 = not a jam head).
   sbyte SignalOf(Entity lane)=>Signals.TryGetComponent(lane,out var signal)?(sbyte)signal.m_Signal:(sbyte)-1;

   JamMiss Miss(Entity vehicle,Car car,in PathOwner owner,in CarCurrentLane current,DynamicBuffer<CarNavigationLane> nav,byte reason,int queue,uint standing)
   {
    var m=new JamMiss{Vehicle=vehicle,Lane=current.m_Lane,Reason=reason,Queue=queue,Standing=standing,Path=owner.m_State,Car=car.m_Flags,
     LaneFlags=current.m_LaneFlags,Signal=SignalOf(current.m_Lane),NextSignal=-1,Nav=(byte)math.min(nav.Length,255)};
    if(Blockers.TryGetComponent(vehicle,out var blocker))m.Blocker=(byte)blocker.m_Type;
    var road=Owners.TryGetComponent(current.m_Lane,out var o)?o.m_Owner:Entity.Null;
    for(int i=0;i<nav.Length&&i<4;i++)
    {
     var lane=nav[i].m_Lane;
     if(EdgeLanes.HasComponent(lane)&&Owners.TryGetComponent(lane,out var lo)&&lo.m_Owner==road)continue;
     m.Next=lane;m.NextFlags=nav[i].m_Flags;m.NextSignal=SignalOf(lane);
     if(CarLanes.TryGetComponent(lane,out var c))
     {
      var f=c.m_Flags;
      m.Move=(byte)((f&(NetFlags.UTurnLeft|NetFlags.UTurnRight))!=0?4:(f&(NetFlags.TurnLeft|NetFlags.GentleTurnLeft))!=0?2:(f&(NetFlags.TurnRight|NetFlags.GentleTurnRight))!=0?3:1);
     }
     break;
    }
    return m;
   }

   // miss: why a standing head with vehicles waiting behind it was not released (see JamMiss).
   // Ordinary queue members (blocked by a vehicle ahead on the same lane) are not reported.
   byte JamHeadTrigger(Entity vehicle,PathFlags pathState,in CarCurrentLane current,DynamicBuffer<CarNavigationLane> nav,ref HookTrace trace,out byte miss,out int queue,out uint standing)
   {
    const VFlags busy=VFlags.EndOfPath|VFlags.Waypoint|VFlags.ParkingSpace|VFlags.TransformTarget|VFlags.GroupTarget|VFlags.Area|VFlags.Connection;
    miss=0;queue=0;standing=0;
    if(!StoppedSince.TryGetValue(vehicle,out uint since))return 0;
    standing=Frame-since;
    Blockers.TryGetComponent(vehicle,out var blocker);
    // Red light at the stop line: restart the timer (red time does not count toward a jam).
    if(blocker.m_Type==BlockerType.Signal){StopSamples.Enqueue(new StopSample{Vehicle=vehicle,Stopped=true,Red=true});standing=0;return 0;}
    if(standing<HeadFrames)return 0;
    var ahead=Root(blocker.m_Blocker);
    if(ahead!=Entity.Null&&OnLane(ahead,current.m_Lane))return 0;
    QueueBehind(vehicle,current,out int count,out float tail);
    queue=count;
    if(count==0)return 0;
    if((current.m_LaneFlags&busy)!=0){miss=1;return 0;}
    // Waiting for a new route: leave it alone, the route will move it.
    if((pathState&(PathFlags.Pending|PathFlags.Obsolete|PathFlags.Failed))!=0){miss=7;return 0;}
    // The turn it waits for: the first junction lane ahead (pieces of the same road skipped).
    var road=Owners.TryGetComponent(current.m_Lane,out var o)?o.m_Owner:Entity.Null;
    int turnAt=-1;
    for(int i=0;i<nav.Length&&i<4;i++)
    {
     if((nav[i].m_Flags&busy)!=0){miss=1;return 0;}
     var lane=nav[i].m_Lane;
     if(EdgeLanes.HasComponent(lane)){if(Owners.TryGetComponent(lane,out var lo)&&lo.m_Owner==road)continue;break;}
     turnAt=i;break;
    }
    if(turnAt<0||!CarLanes.TryGetComponent(nav[turnAt].m_Lane,out var turnLane)){miss=4;return 0;}
    var f=turnLane.m_Flags;
    byte wait=(byte)((f&(NetFlags.UTurnLeft|NetFlags.UTurnRight))!=0?1:(f&(NetFlags.TurnLeft|NetFlags.GentleTurnLeft))!=0?2:(f&(NetFlags.TurnRight|NetFlags.GentleTurnRight))!=0?3:0);
    if(wait==0){miss=4;return 0;}
    // The game sets no blocker while a turning car waits for a gap at a Yield / Stop junction lane
    // (permissive left turn on green, stop sign): such a left turn or U-turn counts as a gap wait.
    if(blocker.m_Type==BlockerType.None)
    {
     sbyte signal=SignalOf(nav[turnAt].m_Lane);
     bool gapWait=(wait==1||wait==2)&&(signal==(sbyte)LaneSignalType.Yield||signal==(sbyte)LaneSignalType.Stop);
     if(!gapWait){miss=2;return 0;}
    }
    // Its own way ahead is clear: the blocker is not in the junction lane it enters.
    bool full=false;
    if(ahead!=Entity.Null)
    {
     for(int i=0;i<=turnAt;i++)if(OnLane(ahead,nav[i].m_Lane)){miss=5;return 0;}
     full=turnAt+1<nav.Length&&OnLane(ahead,nav[turnAt+1].m_Lane);
    }
    trace.JamWait=wait;trace.JamFull=full;trace.TurnLane=nav[turnAt].m_Lane;
    if(tail<=CarSpacing)return 1;
    float length=Curves.TryGetComponent(current.m_Lane,out var curve)?curve.m_Length:0f;
    if(count>=3&&count>=0.6f*length/CarSpacing)return 2;
    if(standing>=LongFrames)return 3;
    miss=6;
    return 0;
   }

   // Standing vehicles queued bumper to bumper behind the head on its lane: how many, and where
   // the queue ends (metres from the lane start).
   void QueueBehind(Entity vehicle,in CarCurrentLane current,out int count,out float tail)
   {
    count=0;tail=float.MaxValue;
    if(!LaneObjects.TryGetBuffer(current.m_Lane,out var objects)||!Curves.TryGetComponent(current.m_Lane,out var curve))return;
    float length=math.max(1f,curve.m_Length);
    float edge=current.m_CurvePosition.x*length;
    for(int i=0;i<objects.Length;i++)if(Root(objects[i].m_LaneObject)==vehicle)edge=math.min(edge,math.cmin(objects[i].m_CurvePosition)*length);
    for(int guard=0;guard<64;guard++)
    {
     float best=-1f,bestLow=0f;bool front=false;
     for(int i=0;i<objects.Length;i++)
     {
      var part=objects[i].m_LaneObject;var root=Root(part);
      if(root==vehicle)continue;
      float high=math.cmax(objects[i].m_CurvePosition)*length,low=math.cmin(objects[i].m_CurvePosition)*length;
      if(high>edge+0.1f||high<edge-QueueGap||low>=edge||!Standing(root))continue;
      if(high>best){best=high;bestLow=low;front=root==part;}
     }
     if(best<0f)break;
     edge=bestLow;if(front)count++;
    }
    tail=edge;
   }

   // Follow the queue (each vehicle blocked by the one ahead) to a released jam head.
   bool BehindJamHead(Entity vehicle,out Entity head)
   {
    head=Entity.Null;Entity x=vehicle;
    for(int i=0;i<64;i++)
    {
     if(!Blockers.TryGetComponent(x,out var blocker)||blocker.m_Type!=BlockerType.Continuing)return false;
     var next=Root(blocker.m_Blocker);
     if(next==Entity.Null||next==x||next==vehicle)return false;
     if(JamHeads.ContainsKey(next)){head=next;return true;}
     x=next;
    }
    return false;
   }

   static bool UnsafeUTurn(NetFlags f)=>(f&NetFlags.Unsafe)!=0&&(f&(NetFlags.UTurnLeft|NetFlags.UTurnRight))!=0;

   // The route the game found after a handoff: if its first steps contain an unsafe U-turn, drive
   // straight past the next junction and hand back again (at most 3 times, then leave it to the game).
   bool CheckNewRoute(int2 watch,NetFlags forbidden,ref CarCurrentLane current,ref PathOwner pathOwner,DynamicBuffer<CarNavigationLane> nav,DynamicBuffer<PathElement> path,ref HookTrace trace)
   {
    if((pathOwner.m_State&(PathFlags.Pending|PathFlags.Obsolete|PathFlags.Failed))!=0)return false;
    if(nav.Length==0&&pathOwner.m_ElementIndex>=path.Length)return false; // new route not here yet
    Entity bad=Entity.Null;int seen=0;
    for(int i=0;i<nav.Length&&seen<10&&bad==Entity.Null;i++,seen++)
     if(CarLanes.TryGetComponent(nav[i].m_Lane,out var c)&&UnsafeUTurn(c.m_Flags))bad=nav[i].m_Lane;
    for(int p=pathOwner.m_ElementIndex;p<path.Length&&seen<10&&bad==Entity.Null;p++,seen++)
     if(CarLanes.TryGetComponent(path[p].m_Target,out var c)&&UnsafeUTurn(c.m_Flags))bad=path[p].m_Target;
    if(bad==Entity.Null){WatchEvents.Enqueue(new WatchEvent{Vehicle=trace.Vehicle,Kind=1});return false;}
    if(watch.x>=MaxReroutes){WatchEvents.Enqueue(new WatchEvent{Vehicle=trace.Vehicle,Kind=2});return false;}
    if(current.m_ChangeLane!=Entity.Null||!EdgeLanes.HasComponent(current.m_Lane)||Protected(current.m_Lane))return false; // wait until on an ordinary road
    var route=new NativeList<Entity>(32,Allocator.Temp);
    bool ok=StraightExit(current.m_Lane,bad,forbidden,ref route);
    if(ok)WriteExit(ref current,ref pathOwner,nav,path,route);
    else WatchEvents.Enqueue(new WatchEvent{Vehicle=trace.Vehicle,Kind=4});
    route.Dispose();
    if(!ok)return false;
    trace.Result=HookResult.Rerouted;trace.JamHead=bad;
    return true;
   }

   // Give up the waited-for turn: drive straight on the lane's own continuation out of the stretch,
   // then hand back to the game's pathfinding (same as the strict hold).
   bool Release(ref CarCurrentLane current,ref PathOwner pathOwner,DynamicBuffer<CarNavigationLane> nav,DynamicBuffer<PathElement> path,NetFlags forbidden,Entity avoid)
   {
    var route=new NativeList<Entity>(32,Allocator.Temp);
    bool ok=StraightExit(current.m_Lane,avoid,forbidden,ref route);
    if(ok)WriteExit(ref current,ref pathOwner,nav,path,route);
    route.Dispose();
    return ok;
   }

   bool StraightExit(Entity start,Entity avoid,NetFlags forbidden,ref NativeList<Entity> route)
   {
    route.Clear();route.Add(start);
    var startOwner=Owners[start].m_Owner;
    var next=new NativeList<Entity>(16,Allocator.Temp);
    Entity lane=start;bool found=false;
    for(int step=0;step<32&&!found;step++)
    {
     Successors(lane,forbidden,ref next);
     Entity chosen=Entity.Null;int best=int.MaxValue;
     for(int n=0;n<next.Length;n++)
     {
      var c=next[n];
      if(c==avoid)continue;
      var f=CarLanes[c].m_Flags;
      if((f&(NetFlags.UTurnLeft|NetFlags.UTurnRight))!=0)continue;
      int score=(f&(NetFlags.TurnLeft|NetFlags.TurnRight))!=0?2:(f&(NetFlags.GentleTurnLeft|NetFlags.GentleTurnRight))!=0?1:0;
      if(score<best||(score==best&&chosen!=Entity.Null&&c.Index<chosen.Index)){best=score;chosen=c;}
     }
     if(chosen==Entity.Null)break;
     route.Add(chosen);lane=chosen;
     found=EdgeLanes.HasComponent(lane)&&!Protected(lane)&&Owners[lane].m_Owner!=startOwner;
    }
    if(found)
     for(int extra=0;extra<2&&route.Length<30;extra++)
     {
      Successors(route[route.Length-1],forbidden,ref next);
      Entity chosen=Entity.Null;int best=int.MaxValue;
      for(int n=0;n<next.Length;n++)
      {
       if(Protected(next[n]))continue;
       int score=((CarLanes[next[n]].m_Flags&NetFlags.Forward)!=0?0:1000000)+next[n].Index;
       if(score<best){best=score;chosen=next[n];}
      }
      if(chosen==Entity.Null)break;
      route.Add(chosen);
     }
    next.Dispose();
    if(found)return true;
    return LegalExit(start,forbidden,ref route)&&(route.Length<2||route[1]!=avoid);
   }

   // Let the game choose lanes again (it changes lanes around a standing queue).
   void Bypass(ref CarCurrentLane current,DynamicBuffer<CarNavigationLane> nav)
   {
    current.m_LaneFlags&=~VFlags.FixedLane;
    current.m_LaneFlags|=VFlags.UpdateOptimalLane;
    for(int i=0;i<nav.Length;i++)
    {
     var part=nav[i];
     if((part.m_Flags&(VFlags.Waypoint|VFlags.EndOfPath|VFlags.ParkingSpace|VFlags.Connection|VFlags.Area))!=0)break;
     part.m_Flags&=~VFlags.FixedLane;part.m_Flags|=VFlags.UpdateOptimalLane;nav[i]=part;
    }
   }

   // Breadth-first over end-to-start connected lanes (no lateral moves) to the first ordinary
   // road lane, plus two lanes beyond it (straight preferred) so long vehicles clear the junction.
   bool LegalExit(Entity start,NetFlags forbidden,ref NativeList<Entity> route)
   {
    route.Clear();
    var visited=new NativeList<Entity>(64,Allocator.Temp);
    var parents=new NativeList<int>(64,Allocator.Temp);
    var next=new NativeList<Entity>(32,Allocator.Temp);
    visited.Add(start);parents.Add(-1);int exit=-1;
    var startOwner=Owners[start].m_Owner;
    for(int p=0;p<visited.Length&&visited.Length<256&&exit<0;p++)
    {
     var lane=visited[p];
     if(p>0&&EdgeLanes.HasComponent(lane)&&!Protected(lane)&&Owners[lane].m_Owner!=startOwner){exit=p;break;}
     Successors(lane,forbidden,ref next);
     for(int n=0;n<next.Length;n++)
     {
      bool seen=false;for(int v=0;v<visited.Length&&!seen;v++)seen=visited[v]==next[n];
      if(!seen){visited.Add(next[n]);parents.Add(p);}
     }
    }
    if(exit>=0)
    {
     var reversed=new NativeList<Entity>(32,Allocator.Temp);
     for(int p=exit;p>=0&&reversed.Length<28;p=parents[p])reversed.Add(visited[p]);
     for(int i=reversed.Length-1;i>=0;i--)route.Add(reversed[i]);
     reversed.Dispose();
     for(int extra=0;extra<2&&route.Length<30;extra++)
     {
      Successors(route[route.Length-1],forbidden,ref next);
      Entity chosen=Entity.Null;int best=int.MaxValue;
      for(int n=0;n<next.Length;n++)
      {
       if(Protected(next[n]))continue;
       int score=((CarLanes[next[n]].m_Flags&NetFlags.Forward)!=0?0:1000000)+next[n].Index;
       if(score<best){best=score;chosen=next[n];}
      }
      if(chosen==Entity.Null)break;
      route.Add(chosen);
     }
    }
    visited.Dispose();parents.Dispose();next.Dispose();
    return exit>=0&&route.Length>1&&route[0]==start;
   }

   void Successors(Entity lane,NetFlags forbidden,ref NativeList<Entity> next)
   {
    next.Clear();
    if(!Owners.TryGetComponent(lane,out var owner)||!Lanes.TryGetComponent(lane,out var from))return;
    var owners=new NativeList<Entity>(8,Allocator.Temp);
    owners.Add(owner.m_Owner);
    if(Edges.TryGetComponent(owner.m_Owner,out var edge)){owners.Add(edge.m_Start);owners.Add(edge.m_End);}
    else if(ConnectedEdges.TryGetBuffer(owner.m_Owner,out var edges))for(int i=0;i<edges.Length;i++)owners.Add(edges[i].m_Edge);
    var scratch=new NativeList<Candidate>(4,Allocator.Temp);
    for(int o=0;o<owners.Length;o++)
    {
     if(!SubLanes.TryGetBuffer(owners[o],out var subs))continue;
     for(int i=0;i<subs.Length&&next.Length<64;i++)
     {
      var candidate=subs[i].m_SubLane;
      if(candidate==lane||(subs[i].m_PathMethods&PathMethod.Road)==0||!Lanes.TryGetComponent(candidate,out var to)||!from.m_EndNode.Equals(to.m_StartNode))continue;
      scratch.Clear();AddLane(candidate,0f,forbidden,ref scratch);
      if(scratch.Length>0&&(CarLanes[candidate].m_Flags&NetFlags.SideConnection)==0)next.Add(candidate);
     }
    }
    scratch.Dispose();owners.Dispose();
   }

   VFlags TurnFlags(Entity lane)
   {
    VFlags flags=0;
    if(!CarLanes.TryGetComponent(lane,out var car))return flags;
    if((car.m_Flags&NetFlags.Forward)==0)
    {
     bool left=(car.m_Flags&(NetFlags.UTurnLeft|NetFlags.TurnLeft|NetFlags.GentleTurnLeft))!=0;
     bool right=(car.m_Flags&(NetFlags.UTurnRight|NetFlags.TurnRight|NetFlags.GentleTurnRight))!=0;
     if(left&&!right)flags|=VFlags.TurnLeft;
     if(right&&!left)flags|=VFlags.TurnRight;
    }
    if((car.m_Flags&(NetFlags.Approach|NetFlags.Roundabout))==NetFlags.Roundabout)flags|=VFlags.Roundabout;
    return flags;
   }

   void AddGroup(Entity lane,bool flat,NetFlags forbidden,ref NativeList<Candidate> candidates)
   {
    int min,max;
    if(Slaves.TryGetComponent(lane,out var slave)){min=slave.m_MinIndex;max=slave.m_MaxIndex;}
    else if(Masters.TryGetComponent(lane,out var master)){min=master.m_MinIndex;max=master.m_MaxIndex;}
    else{AddLane(lane,0f,forbidden,ref candidates);return;}
    if(!Owners.TryGetComponent(lane,out var owner)||!SubLanes.TryGetBuffer(owner.m_Owner,out var group))return;
    max=math.min(max,group.Length-1);
    if(min<0||max-min>=64)return;
    for(int i=min;i<=max;i++)
    {
     var sub=group[i];
     if((sub.m_PathMethods&PathMethod.Road)==0)continue;
     AddLane(sub.m_SubLane,flat||sub.m_SubLane==lane?0f:1f,forbidden,ref candidates);
    }
   }

   void AddLane(Entity lane,float cost,NetFlags forbidden,ref NativeList<Candidate> candidates)
   {
    if(Masters.HasComponent(lane)||DeletedLanes.HasComponent(lane)||!Lanes.TryGetComponent(lane,out var data)||!CarLanes.TryGetComponent(lane,out var car))return;
    if((car.m_Flags&(forbidden|NetFlags.Forbidden))!=0)return;
    if(kind!=0&&BanLanes.TryGetValue(lane,out var banned)&&(banned&kind)!=0)return;
    if(Slaves.TryGetComponent(lane,out var slave)&&(slave.m_Flags&(SlaveLaneFlags.MiddleStart|SlaveLaneFlags.MiddleEnd))!=0)return;
    candidates.Add(new Candidate{Lane=lane,Start=data.m_StartNode,End=data.m_EndNode,Cost=cost});
   }

   static bool Solve(NativeList<Candidate> candidates,NativeList<int3> layers,ref NativeList<Entity> output)
   {
    output.Clear();
    int count=candidates.Length;
    var costs=new NativeArray<float>(count,Allocator.Temp);
    var next=new NativeArray<int>(count,Allocator.Temp);
    for(int n=layers.Length-1;n>=0;n--)
    {
     int3 row=layers[n];
     for(int i=row.x;i<row.y;i++)
     {
      costs[i]=float.PositiveInfinity;next[i]=-1;
      if(n==layers.Length-1){costs[i]=candidates[i].Cost;continue;}
      int3 after=layers[n+1];
      // z: 0 end-to-start chain; 1 lateral (entering any lane of the layer is enough);
      // 2 next piece of the same protected road (chained, or the same lane repeated at a stop);
      // 3 next piece of a free road (any lane).
      bool enters=after.z==3;
      if(after.z==1)for(int j=after.x;j<after.y&&!enters;j++)enters=candidates[i].End.Equals(candidates[j].Start);
      for(int j=after.x;j<after.y;j++)
      {
       if(!math.isfinite(costs[j]))continue;
       bool chained=candidates[i].End.Equals(candidates[j].Start);
       bool linked=after.z==2?chained||candidates[i].Lane==candidates[j].Lane:enters||chained;
       if(!linked)continue;
       float cost=candidates[i].Cost+costs[j];
       if(cost<costs[i]||(cost==costs[i]&&next[i]>=0&&candidates[j].Lane.Index<candidates[next[i]].Lane.Index)){costs[i]=cost;next[i]=j;}
      }
     }
    }
    int best=-1;float bestCost=float.PositiveInfinity;
    for(int i=layers[0].x;i<layers[0].y;i++)
     if(costs[i]<bestCost||(math.isfinite(costs[i])&&costs[i]==bestCost&&best>=0&&candidates[i].Lane.Index<candidates[best].Lane.Index)){best=i;bestCost=costs[i];}
    bool ok=best>=0;
    for(int n=0;ok&&n<layers.Length;n++){output.Add(candidates[best].Lane);best=next[best];if(best<0&&n<layers.Length-1)ok=false;}
    costs.Dispose();next.Dispose();
    return ok;
   }
  }

  private SimulationSystem simulation;
  private RoadLaneLockNavigationSystem rules;
  private EntityQuery query;
  private NativeParallelHashMap<Entity,byte> ruleMap;
  private NativeParallelHashSet<Entity> tracked;
  private NativeParallelHashMap<Entity,Entity> held;
  private NativeParallelHashMap<Entity,byte> monitorMap;
  private NativeParallelHashMap<Entity,uint> jamHeads;
  private NativeParallelHashMap<Entity,int2> watch;
  private NativeQueue<WatchEvent> watchEvents;
  private NativeQueue<JamMiss> jamMisses;
  private readonly Dictionary<Entity,byte> missLogged=new Dictionary<Entity,byte>();
  private int monitorRevision=-1;
  private uint nextHeldSweep;
  private readonly List<Entity> monitorBuffer=new List<Entity>();
  // Recent jam releases on monitored roads (newest first) for the panel and the map overlay.
  public sealed class JamEvent
  {
   public Entity Head,Road,Lane,Turn;
   public byte Direction,Wait,Trigger;
   public bool Full;
   public uint Frame;
   public bool Moved,Stuck;   // the head drove off / still standing 10 s after the release
   public uint MovedFrame;
   public readonly List<Entity> Followers=new List<Entity>();
  }
  // Heads that qualify but have no straight way out (refreshed while it lasts), by road direction.
  sealed class StuckHead{public Entity Head,Lane;public uint Seen;}
  private readonly Dictionary<(Entity,byte),StuckHead> stuckHeads=new Dictionary<(Entity,byte),StuckHead>();
  public int JamCount{get;private set;}
  // Session statistics for the panel (reset on load): releases and unlocked followers per road.
  public readonly Dictionary<Entity,int> JamsByRoad=new Dictionary<Entity,int>(),FollowersByRoad=new Dictionary<Entity,int>();
  public int JamFollowers{get;private set;}
  // Vehicles that left a protected stretch without changing lanes inside it, per road and in total.
  public Dictionary<Entity,int> ExitsByRoad=>log.ExitsByRoad;
  public int TotalExits=>log.TotalExits;
  public void ResetStats(){JamsByRoad.Clear();FollowersByRoad.Clear();JamFollowers=0;JamCount=0;log.ExitsByRoad.Clear();log.TotalExits=0;}
  // Map colour of a monitored road direction: 0 normal, 1 red (qualifies, head not gone), 2 green (head driven off).
  public byte RoadState(Entity road,byte direction)
  {
   if(stuckHeads.ContainsKey((road,direction)))return 1;
   byte state=0;
   foreach(var e in jams)
   {
    if(e.Road!=road||e.Direction!=direction)continue;
    if(e.Stuck&&!e.Moved&&IsHeld(e.Head))return 1;
    if(e.Moved&&Frame-e.MovedFrame<600)state=2;
   }
   return state;
  }
  // Heads to ring on the map: red (not gone) or mint (driving off).
  public List<(Entity Head,bool Red)> MarkedHeads()
  {
   var list=new List<(Entity,bool)>();
   foreach(var s in stuckHeads.Values)list.Add((s.Head,true));
   foreach(var e in jams)
   {
    if(e.Stuck&&!e.Moved&&IsHeld(e.Head))list.Add((e.Head,true));
    else if(e.Moved&&Frame-e.MovedFrame<600)list.Add((e.Head,false));
   }
   return list;
  }
  private readonly List<JamEvent> jams=new List<JamEvent>();
  public IReadOnlyList<JamEvent> RecentJams=>jams;
  public int JamRevision{get;private set;}
  public uint Frame=>simulation.frameIndex;
  public bool IsHeld(Entity vehicle)=>held.IsCreated&&held.ContainsKey(vehicle);
  private NativeQueue<HookTrace> traces;
  private NativeQueue<StopEvent> stopEvents;
  private NativeParallelMultiHashMap<Entity,ulong> decisions;
  private NativeParallelHashMap<ulong,int> skipCounts;
  private NativeQueue<StopSample> stopSamples;
  private NativeParallelHashMap<Entity,uint> stoppedSince;
  private NativeParallelHashMap<Entity,uint> stallFree;internal int StallReleases;
  private uint nextStopSweep;
  private JobHandle job;
  private int ruleRevision=-1;
  private readonly List<Entity> ruleBuffer=new List<Entity>();
  private readonly LaneHookLog log=new LaneHookLog();

  protected override void OnCreate()
  {
   base.OnCreate();
   simulation=World.GetOrCreateSystemManaged<SimulationSystem>();
   rules=World.GetOrCreateSystemManaged<RoadLaneLockNavigationSystem>();
   ruleMap=new NativeParallelHashMap<Entity,byte>(16,Allocator.Persistent);
   tracked=new NativeParallelHashSet<Entity>(256,Allocator.Persistent);
   held=new NativeParallelHashMap<Entity,Entity>(64,Allocator.Persistent);
   monitorMap=new NativeParallelHashMap<Entity,byte>(16,Allocator.Persistent);
   jamHeads=new NativeParallelHashMap<Entity,uint>(16,Allocator.Persistent);
   watch=new NativeParallelHashMap<Entity,int2>(64,Allocator.Persistent);
   watchEvents=new NativeQueue<WatchEvent>(Allocator.Persistent);
   jamMisses=new NativeQueue<JamMiss>(Allocator.Persistent);
   traces=new NativeQueue<HookTrace>(Allocator.Persistent);
   stopEvents=new NativeQueue<StopEvent>(Allocator.Persistent);
   decisions=new NativeParallelMultiHashMap<Entity,ulong>(256,Allocator.Persistent);
   skipCounts=new NativeParallelHashMap<ulong,int>(256,Allocator.Persistent);
   stopSamples=new NativeQueue<StopSample>(Allocator.Persistent);
   stoppedSince=new NativeParallelHashMap<Entity,uint>(256,Allocator.Persistent);
   stallFree=new NativeParallelHashMap<Entity,uint>(16,Allocator.Persistent);
   banLanes=new NativeParallelHashMap<Entity,byte>(16,Allocator.Persistent);
   banRoads=new NativeParallelHashMap<Entity,ushort>(16,Allocator.Persistent);
   motoPrefabs=new NativeParallelHashSet<Entity>(16,Allocator.Persistent);
   lineExempt=new NativeParallelHashSet<ulong>(16,Allocator.Persistent);
   exempt=new NativeQueue<ulong>(Allocator.Persistent);
   query=GetEntityQuery(ComponentType.ReadOnly<Car>(),ComponentType.ReadOnly<UpdateFrame>(),ComponentType.ReadWrite<CarCurrentLane>(),
    ComponentType.ReadWrite<CarNavigationLane>(),ComponentType.ReadWrite<PathOwner>(),ComponentType.ReadWrite<PathElement>(),
    ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Temp>(),ComponentType.Exclude<TripSource>(),ComponentType.Exclude<ParkedCar>(),
    ComponentType.Exclude<OutOfControl>(),ComponentType.Exclude<Bicycle>());
  }

  protected override void OnDestroy()
  {
   job.Complete();
   if(ruleMap.IsCreated)ruleMap.Dispose();
   if(tracked.IsCreated)tracked.Dispose();
   if(held.IsCreated)held.Dispose();
   if(monitorMap.IsCreated)monitorMap.Dispose();
   if(jamHeads.IsCreated)jamHeads.Dispose();
   if(watch.IsCreated)watch.Dispose();
   if(watchEvents.IsCreated)watchEvents.Dispose();
   if(jamMisses.IsCreated)jamMisses.Dispose();
   if(traces.IsCreated)traces.Dispose();
   if(stopEvents.IsCreated)stopEvents.Dispose();
   if(decisions.IsCreated)decisions.Dispose();
   if(skipCounts.IsCreated)skipCounts.Dispose();
   if(stopSamples.IsCreated)stopSamples.Dispose();
   if(stoppedSince.IsCreated)stoppedSince.Dispose();
   if(stallFree.IsCreated)stallFree.Dispose();
   if(banLanes.IsCreated)banLanes.Dispose();
   if(banRoads.IsCreated)banRoads.Dispose();
   if(motoPrefabs.IsCreated)motoPrefabs.Dispose();
   if(lineExempt.IsCreated)lineExempt.Dispose();
   if(exempt.IsCreated)exempt.Dispose();
   base.OnDestroy();
  }

  protected override void OnStopRunning()
  {
   job.Complete();exempt.Clear();traces.Clear();tracked.Clear();held.Clear();stopEvents.Clear();decisions.Clear();skipCounts.Clear();stopSamples.Clear();stoppedSince.Clear();stallFree.Clear();
   jamHeads.Clear();watch.Clear();watchEvents.Clear();jamMisses.Clear();missLogged.Clear();stuckHeads.Clear();jams.Clear();JamRevision++;monitorRevision=-1;log.Flush(EntityManager,World,"执行器关闭");
   base.OnStopRunning();
  }

  public int ProtectedRoadCount=>ruleMap.Count();
  NativeParallelHashMap<Entity,byte> banLanes;NativeParallelHashMap<Entity,ushort> banRoads;
  NativeParallelHashSet<Entity> motoPrefabs;bool motoScanned;
  internal bool IsMotoPrefab(Entity prefab)=>motoPrefabs.IsCreated&&motoPrefabs.Contains(prefab);
  // Personal vehicles that are motorbikes: the game has no motorbike component, so they are told apart by
  // prefab name. Every personal vehicle prefab is logged once with the verdict, to check the names live.
  void ScanMotoPrefabs()
  {
   motoScanned=true;motoPrefabs.Clear();
   var prefabSystem=World.GetOrCreateSystemManaged<Game.Prefabs.PrefabSystem>();
   using var q=EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Game.Prefabs.PersonalCarData>(),ComponentType.ReadOnly<Game.Prefabs.PrefabData>());
   using var list=q.ToEntityArray(Allocator.Temp);
   var seen=new List<object>();var motoNames=new List<string>();
   foreach(var prefab in list)
   {
    string name=null;
    try{name=prefabSystem.GetPrefabName(prefab);}catch{}
    var n=(name??"").ToLowerInvariant();
    bool bike=EntityManager.HasComponent<Game.Prefabs.BicycleData>(prefab)||n.Contains("bicycle");
    bool moto=!bike&&(n.Contains("motor")||n.Contains("moped")||n.Contains("scooter")||n.Contains("bike"));
    if(moto)motoPrefabs.Add(prefab);
    int seats=EntityManager.GetComponentData<Game.Prefabs.PersonalCarData>(prefab).m_PassengerCapacity;
    seen.Add(new{Name=name,Moto=moto,Bike=bike,Seats=seats});
    if(moto)motoNames.Add(name);
   }
   Diagnostics.Event("motoPrefabs",new{Count=motoPrefabs.Count(),Prefabs=seen});
   Mod.Log.Info($"摩托车车型 {motoNames.Count}/{list.Length}：{string.Join(", ",motoNames)}");
  }
  NativeParallelHashSet<ulong> lineExempt;NativeQueue<ulong> exempt;
  readonly Dictionary<Entity,byte> banLaneBuffer=new Dictionary<Entity,byte>();readonly Dictionary<Entity,ushort> banRoadBuffer=new Dictionary<Entity,ushort>();
  RoadRules roadRules;int truckRevision=-1;uint nextTruckSync;
  // Transit line/road pairs released from the lane lock this session (a stop there could not be reached).
  internal int ExemptCount;
  internal bool LineExempted(Entity line,Entity road)=>lineExempt.IsCreated&&lineExempt.Contains(((ulong)(uint)line.Index<<32)|(uint)road.Index);

  protected override void OnUpdate()
  {
   job.Complete();
   uint frame=simulation.frameIndex;
   log.Drain(traces,tracked,frame,EntityManager,World);
   foreach(var t in log.Extra)OnExtra(t,frame);
   log.Extra.Clear();
   while(watchEvents.TryDequeue(out var w))
   {
    if(!watch.ContainsKey(w.Vehicle))continue;
    watch.Remove(w.Vehicle);
    if(w.Kind==2||w.Kind==4)Diagnostics.Event("laneReroute",new{Id=Id(w.Vehicle),Kind=LaneHookLog.Kind(EntityManager,w.Vehicle),Result=w.Kind==2?"三次后交给原版":"找不到直行路线，交给原版"});
   }
   // Heads not released: logged once per vehicle and reason while it keeps standing.
   while(jamMisses.TryDequeue(out var m))
   {
    if(m.Reason==8)
    {
     byte md=0;
     if(EntityManager.Exists(m.Lane)&&EntityManager.HasComponent<EdgeLane>(m.Lane)){var d8=EntityManager.GetComponentData<EdgeLane>(m.Lane).m_EdgeDelta;md=Core.NavigationSafety.Direction(d8.x,d8.y);}
     var key=(OwnerOf(m.Lane),md);
     bool fresh=!stuckHeads.TryGetValue(key,out var sh)||sh.Head!=m.Vehicle;
     stuckHeads[key]=new StuckHead{Head=m.Vehicle,Lane=m.Lane,Seen=frame};
     if(fresh)Diagnostics.Event("laneJamStuck",new{Id=Id(m.Vehicle),Kind=LaneHookLog.Kind(EntityManager,m.Vehicle),RoadId=key.Item1.Index,Road=RoadName(key.Item1),Direction=md,Reason="找不到直行路线"});
    }
    if(m.Reason==9){stallFree.Remove(m.Vehicle);stallFree.TryAdd(m.Vehicle,frame);StallReleases++;}
    if(missLogged.TryGetValue(m.Vehicle,out byte last)&&last==m.Reason)continue;
    if(missLogged.Count>=5000)missLogged.Clear();
    missLogged[m.Vehicle]=m.Reason;
    byte dir=0;
    if(EntityManager.Exists(m.Lane)&&EntityManager.HasComponent<EdgeLane>(m.Lane)){var d=EntityManager.GetComponentData<EdgeLane>(m.Lane).m_EdgeDelta;dir=Core.NavigationSafety.Direction(d.x,d.y);}
    var roadEntity=OwnerOf(m.Lane);
    Diagnostics.Event("laneJamMiss",new{Id=Id(m.Vehicle),Kind=LaneHookLog.Kind(EntityManager,m.Vehicle),RoadId=roadEntity.Index,Road=RoadName(roadEntity),Direction=dir,
     Reason=m.Reason<missReasons.Length?missReasons[m.Reason]:"?",Queue=m.Queue,Seconds=(int)(m.Standing/60),
     Move=moves[m.Move<moves.Length?m.Move:0],Blocker=((BlockerType)m.Blocker).ToString(),Path=m.Path.ToString(),Car=m.Car.ToString(),
     LaneFlags=m.LaneFlags.ToString(),NextFlags=m.Next==Entity.Null?null:m.NextFlags.ToString(),NextIsJunction=m.Next!=Entity.Null&&!EntityManager.HasComponent<EdgeLane>(m.Next),
     Signal=SignalName(m.Signal),NextSignal=SignalName(m.NextSignal),Nav=m.Nav});
   }
   SweepJams(frame);
   // Kerb stop decisions: remembered per vehicle and road side; consecutive skips counted per road side.
   while(stopEvents.TryDequeue(out var e))
   {
    bool known=false;
    if(decisions.TryGetFirstValue(e.Vehicle,out var value,out var it))
     do{known|=(value>>2)==e.Key;}while(!known&&decisions.TryGetNextValue(out value,ref it));
    if(known)continue;
    decisions.Add(e.Vehicle,(e.Key<<2)|(e.Kind==2?2UL:1UL));
    skipCounts.TryGetValue(e.Key,out int skipped);skipCounts.Remove(e.Key);
    if(e.Kind==2)skipCounts.TryAdd(e.Key,skipped+1);
    log.OnStop(e.Vehicle,e.Kind);
   }
   foreach(var vehicle in log.Ended)decisions.Remove(vehicle);
   log.Ended.Clear();
   if(decisions.Count()>20000)decisions.Clear();
   // Standing timers: first frame a vehicle was seen standing on a protected lane.
   while(stopSamples.TryDequeue(out var sample))
   {
    if(sample.Red){stoppedSince.Remove(sample.Vehicle);stoppedSince.TryAdd(sample.Vehicle,frame);}
    else if(sample.Stopped)stoppedSince.TryAdd(sample.Vehicle,frame);
    else{stoppedSince.Remove(sample.Vehicle);missLogged.Remove(sample.Vehicle);}
   }
   if(frame>=nextStopSweep)
   {
    nextStopSweep=frame+4096;
    var keys=stoppedSince.GetKeyArray(Allocator.Temp);
    foreach(var key in keys)if(stoppedSince.TryGetValue(key,out uint since)&&frame-since>36000)stoppedSince.Remove(key);
    keys.Dispose();
    var free=stallFree.GetKeyArray(Allocator.Temp);
    foreach(var key in free)if(stallFree.TryGetValue(key,out uint at)&&frame-at>3600)stallFree.Remove(key);
    free.Dispose();
   }
   if(ruleRevision!=rules.RuleRevision)
   {
    ruleRevision=rules.RuleRevision;ruleMap.Clear();
    rules.CopyProtectedRoads(ruleBuffer);
    foreach(var road in ruleBuffer){byte mask=rules.DirectionMask(road);if(mask!=0)ruleMap.TryAdd(road,mask);}
    Diagnostics.Event("laneHookRules",new{Roads=ruleMap.Count(),Revision=ruleRevision});
   }
   if(monitorRevision!=rules.MonitorRevision)
   {
    monitorRevision=rules.MonitorRevision;monitorMap.Clear();monitorBuffer.Clear();
    foreach(var road in rules.MonitoredRoads)if(!monitorBuffer.Contains(road))monitorBuffer.Add(road);
    foreach(var road in monitorBuffer)
    {
     byte mask=(byte)((rules.Monitoring(road,1)?1:0)|(rules.Monitoring(road,2)?2:0));
     if(mask!=0)monitorMap.TryAdd(road,mask);
    }
    Diagnostics.Event("laneJamRoads",new{Roads=monitorMap.Count(),Revision=monitorRevision});
   }
   // Lane bans: refreshed when the rules change and every few seconds (roads get rebuilt).
   roadRules??=World.GetExistingSystemManaged<RoadRules>();
   if(roadRules!=null&&(truckRevision!=roadRules.Revision||frame>=nextTruckSync))
   {
    truckRevision=roadRules.Revision;nextTruckSync=frame+600;
    if(!motoScanned)ScanMotoPrefabs();
    roadRules.CopyLaneBans(banLaneBuffer,banRoadBuffer);
    banLanes.Clear();banRoads.Clear();
    foreach(var p in banLaneBuffer)banLanes.TryAdd(p.Key,p.Value);
    foreach(var p in banRoadBuffer)banRoads.TryAdd(p.Key,p.Value);
   }
   // Transit lines that cannot reach a stop on a lane-change road under the lock.
   while(exempt.TryDequeue(out var key))
   {
    if(!lineExempt.Add(key))continue;
    ExemptCount++;
    Diagnostics.Event("laneTransitExempt",new{Line=(int)(key>>32),RoadId=(int)(uint)key});
    Mod.Log.Info("公交整线放行：路段 #"+(int)(uint)key+" 线路 #"+(int)(key>>32));
   }
   // Runs for lane rules, monitored roads, truck lanes, and vehicles still held or being checked after a handoff.
   if(ruleMap.Count()==0&&held.Count()==0&&monitorMap.Count()==0&&watch.Count()==0&&banRoads.Count()==0)return;
   query.ResetFilter();
   query.SetSharedComponentFilter(new UpdateFrame(frame%16));
   job=JobChunkExtensions.ScheduleParallel(new HookJob
   {
    EntityType=GetEntityTypeHandle(),
    CarType=GetComponentTypeHandle<Car>(true),
    PathOwnerType=GetComponentTypeHandle<PathOwner>(false),
    PathElementType=GetBufferTypeHandle<PathElement>(false),
    CurrentLaneType=GetComponentTypeHandle<CarCurrentLane>(false),
    NavigationType=GetBufferTypeHandle<CarNavigationLane>(false),
    Owners=GetComponentLookup<Owner>(true),
    Lanes=GetComponentLookup<Lane>(true),
    CarLanes=GetComponentLookup<Game.Net.CarLane>(true),
    Masters=GetComponentLookup<MasterLane>(true),
    Slaves=GetComponentLookup<SlaveLane>(true),
    EdgeLanes=GetComponentLookup<EdgeLane>(true),
    DeletedLanes=GetComponentLookup<Deleted>(true),
    SubLanes=GetBufferLookup<Game.Net.SubLane>(true),
    Edges=GetComponentLookup<Game.Net.Edge>(true),
    ConnectedEdges=GetBufferLookup<ConnectedEdge>(true),
    Held=held,
    Rules=ruleMap,Tracked=tracked,Traces=traces.AsParallelWriter(),Record=true,
    GarbageType=GetComponentTypeHandle<Game.Vehicles.GarbageTruck>(true),
    PostVanType=GetComponentTypeHandle<Game.Vehicles.PostVan>(true),
    MaintenanceType=GetComponentTypeHandle<Game.Vehicles.MaintenanceVehicle>(true),
    Curves=GetComponentLookup<Curve>(true),
    Pedestrians=GetComponentLookup<Game.Net.PedestrianLane>(true),
    Decisions=decisions,SkipCounts=skipCounts,StopEvents=stopEvents.AsParallelWriter(),
    Movings=GetComponentLookup<Moving>(true),
    Blockers=GetComponentLookup<Blocker>(true),
    Controllers=GetComponentLookup<Controller>(true),
    LaneObjects=GetBufferLookup<LaneObject>(true),
    StoppedSince=stoppedSince,StallFree=stallFree,StopSamples=stopSamples.AsParallelWriter(),Frame=frame,
    Monitors=monitorMap,JamHeads=jamHeads,Watch=watch,WatchEvents=watchEvents.AsParallelWriter(),JamMisses=jamMisses.AsParallelWriter(),Signals=GetComponentLookup<LaneSignal>(true),
    BanLanes=banLanes,BanRoads=banRoads,PersonalType=GetComponentTypeHandle<Game.Vehicles.PersonalCar>(true),BicycleType=GetComponentTypeHandle<Game.Vehicles.Bicycle>(true),TaxiType=GetComponentTypeHandle<Game.Vehicles.Taxi>(true),BusType=GetComponentTypeHandle<Game.Vehicles.PublicTransport>(true),TruckType=GetComponentTypeHandle<Game.Vehicles.DeliveryTruck>(true),HearseType=GetComponentTypeHandle<Game.Vehicles.Hearse>(true),PrefabType=GetComponentTypeHandle<Game.Prefabs.PrefabRef>(true),MotoPrefabs=motoPrefabs,
    RouteType=GetComponentTypeHandle<Game.Routes.CurrentRoute>(true),LineExempt=lineExempt,Exempt=exempt.AsParallelWriter()
   },query,Dependency);
   Dependency=job;
  }

  static readonly string[] moves={"前方无路口","直行","左转","右转","掉头"};
  static string SignalName(sbyte s)=>s<0?null:((LaneSignalType)s).ToString();
  Game.UI.NameSystem nameSystem;
  string RoadName(Entity road)
  {
   if(road==Entity.Null||!EntityManager.Exists(road))return null;
   nameSystem??=World.GetOrCreateSystemManaged<Game.UI.NameSystem>();
   if(EntityManager.HasComponent<Aggregated>(road)){var street=EntityManager.GetComponentData<Aggregated>(road).m_Aggregate;if(EntityManager.Exists(street))road=street;}
   try{return nameSystem.GetRenderedLabelName(road);}catch{return null;}
  }
  static readonly string[] missReasons={"","停靠或作业","没有阻挡对象","等红灯超过15秒","不是转弯或掉头","路口车道里有车","排队还不够长","等新路线","找不到直行路线","锁定车道上停住20秒，已解锁交还原版"};
  static string Id(Entity e)=>e.Index+"/"+e.Version;
  Entity OwnerOf(Entity lane)=>lane!=Entity.Null&&EntityManager.Exists(lane)&&EntityManager.HasComponent<Owner>(lane)?EntityManager.GetComponentData<Owner>(lane).m_Owner:Entity.Null;

  // Results that change what the executor tracks: held exits, handoffs, jam heads and their followers.
  void OnExtra(HookTrace t,uint frame)
  {
   switch(t.Result)
   {
    case HookResult.Held:
     if(!held.ContainsKey(t.Vehicle))held.TryAdd(t.Vehicle,OwnerOf(t.Lane));
     break;
    case HookResult.Handoff:
    {
     held.Remove(t.Vehicle);
     // Check the route the game finds next (reroutes so far are kept).
     int tries=watch.TryGetValue(t.Vehicle,out var w)?w.x:0;
     watch.Remove(t.Vehicle);watch.TryAdd(t.Vehicle,new int2(tries,(int)frame));
     break;
    }
    case HookResult.Rerouted:
    {
     held.Remove(t.Vehicle);held.TryAdd(t.Vehicle,OwnerOf(t.Lane));
     int tries=watch.TryGetValue(t.Vehicle,out var w)?w.x:0;
     watch.Remove(t.Vehicle);watch.TryAdd(t.Vehicle,new int2(tries+1,(int)frame));
     Diagnostics.Event("laneReroute",new{Id=Id(t.Vehicle),Kind=LaneHookLog.Kind(EntityManager,t.Vehicle),Try=tries+1,Result="直行过路口后重新寻路"});
     break;
    }
    case HookResult.Released:
    {
     held.Remove(t.Vehicle);held.TryAdd(t.Vehicle,OwnerOf(t.Lane));
     jamHeads.Remove(t.Vehicle);jamHeads.TryAdd(t.Vehicle,frame);
     var road=OwnerOf(t.Lane);
     byte direction=0;
     if(EntityManager.Exists(t.Lane)&&EntityManager.HasComponent<EdgeLane>(t.Lane)){var d=EntityManager.GetComponentData<EdgeLane>(t.Lane).m_EdgeDelta;direction=Core.NavigationSafety.Direction(d.x,d.y);}
     var e=new JamEvent{Head=t.Vehicle,Road=road,Lane=t.Lane,Turn=t.TurnLane,Direction=direction,Wait=t.JamWait,Trigger=t.JamTrigger,Full=t.JamFull,Frame=frame};
     jams.Insert(0,e);if(jams.Count>8)jams.RemoveAt(jams.Count-1);JamRevision++;JamCount++;
     JamsByRoad.TryGetValue(road,out int released);JamsByRoad[road]=released+1;
     foreach(var key in new List<(Entity,byte)>(stuckHeads.Keys))if(stuckHeads[key].Head==t.Vehicle)stuckHeads.Remove(key);
     Diagnostics.Event("laneJam",new{Id=Id(t.Vehicle),Kind=LaneHookLog.Kind(EntityManager,t.Vehicle),RoadId=road.Index,Direction=direction,
      Wait=t.JamWait==1?"掉头":t.JamWait==2?"左转":"右转",TargetFull=t.JamFull,Trigger=t.JamTrigger==1?"倒灌到路口":t.JamTrigger==2?"排队超过六成":"停满15秒"});
     break;
    }
    case HookResult.Bypass:
     foreach(var e in jams)
      if(e.Head==t.JamHead){if(!e.Followers.Contains(t.Vehicle)&&e.Followers.Count<64){e.Followers.Add(t.Vehicle);JamRevision++;}break;}
     break;
   }
  }

  // Released heads unlock followers for 20 s; the follower count is logged when that ends.
  // Held and watched vehicles that no longer exist are dropped.
  void SweepJams(uint frame)
  {
   // Stuck markers expire when the head stops qualifying (re-sent every 16 frames while it does).
   if(stuckHeads.Count>0)
    foreach(var key in new List<(Entity,byte)>(stuckHeads.Keys))if(frame-stuckHeads[key].Seen>64)stuckHeads.Remove(key);
   // Released heads: driven off (green) or still standing 10 s later (red, logged once).
   foreach(var e in jams)
   {
    if(e.Moved||frame-e.Frame>3600)continue;
    if(EntityManager.Exists(e.Head)&&EntityManager.HasComponent<Moving>(e.Head)&&math.lengthsq(EntityManager.GetComponentData<Moving>(e.Head).m_Velocity)>1f)
    {e.Moved=true;e.MovedFrame=frame;JamRevision++;continue;}
    if(!e.Stuck&&frame-e.Frame>=600)
    {
     e.Stuck=true;JamRevision++;
     Diagnostics.Event("laneJamStuck",new{Id=Id(e.Head),Kind=LaneHookLog.Kind(EntityManager,e.Head),RoadId=e.Road.Index,Road=RoadName(e.Road),e.Direction,Reason="疏导后10秒仍不动"});
    }
   }
   if(jamHeads.Count()>0)
   {
    var heads=jamHeads.GetKeyArray(Allocator.Temp);
    foreach(var head in heads)
    {
     if(!jamHeads.TryGetValue(head,out uint since)||frame-since<1200)continue;
     jamHeads.Remove(head);
     int followers=0;Entity jamRoad=Entity.Null;foreach(var e in jams)if(e.Head==head){followers=e.Followers.Count;jamRoad=e.Road;break;}
     if(followers>0){JamFollowers+=followers;if(jamRoad!=Entity.Null){FollowersByRoad.TryGetValue(jamRoad,out int f);FollowersByRoad[jamRoad]=f+followers;}}
     Diagnostics.Event("laneJamFollowers",new{Id=Id(head),Followers=followers});
    }
    heads.Dispose();
   }
   if(frame<nextHeldSweep)return;
   nextHeldSweep=frame+4096;
   var keys=held.GetKeyArray(Allocator.Temp);
   foreach(var v in keys)if(!EntityManager.Exists(v)||EntityManager.HasComponent<Deleted>(v))held.Remove(v);
   keys.Dispose();
   keys=watch.GetKeyArray(Allocator.Temp);
   foreach(var v in keys)if(!EntityManager.Exists(v)||EntityManager.HasComponent<Deleted>(v)||(watch.TryGetValue(v,out var w)&&frame-(uint)w.y>7200))watch.Remove(v);
   keys.Dispose();
  }
 }

 // Per-vehicle trace for live testing: route exit, chosen entry lane, lane changes inside the
 // stretch, and whether the vehicle left the stretch somewhere other than its route exit.
 internal sealed class LaneHookLog
 {
  sealed class Record
  {
   public uint First,LastSeen;
   public Entity PlannedEntry,ExitLane,TurnLane,EntryLane,LastProtectedLane,LastOwner;
   public bool Entered,LaneChanged;
   public int Locked,NoExit,Unreachable,Unsupported,Changing,Jumps,Held,Handoff;
   public bool Early;
   public Entity StopLane;public byte StopKind;
   public int Served,Skipped,Guarded,Released,Bypassed,Rerouted;
   public readonly Dictionary<string,int> Reasons=new Dictionary<string,int>();
   public int LastPathState;
   public readonly List<Entity> Roads=new List<Entity>(4); // protected roads driven through
  }
  public readonly Dictionary<Entity,int> ExitsByRoad=new Dictionary<Entity,int>();
  public int TotalExits;
  const int MaxRecords=3000;
  readonly Dictionary<Entity,Record> records=new Dictionary<Entity,Record>();
  readonly List<Entity> stale=new List<Entity>();
  uint nextSweep;
  Game.UI.NameSystem names;
  public readonly List<Entity> Ended=new List<Entity>();

  public void OnStop(Entity vehicle,byte kind)
  {
   if(!records.TryGetValue(vehicle,out var r))return;
   if(kind==1)r.Served++;else if(kind==2)r.Skipped++;else r.Guarded++;
  }

  // Held / handoff / jam / reroute results, for the executor's own bookkeeping (any vehicle).
  public readonly List<HookTrace> Extra=new List<HookTrace>();

  public void Drain(NativeQueue<HookTrace> queue,NativeParallelHashSet<Entity> tracked,uint frame,EntityManager em,World world)
  {
   while(queue.TryDequeue(out var t))
   {
    if(t.Result==HookResult.Held||t.Result==HookResult.Handoff||t.Result==HookResult.Released||t.Result==HookResult.Bypass||t.Result==HookResult.Rerouted)Extra.Add(t);
    if(!records.TryGetValue(t.Vehicle,out var r))
    {
     if(!t.Relevant||records.Count>=MaxRecords)continue;
     r=new Record{First=frame};records[t.Vehicle]=r;tracked.Add(t.Vehicle);
    }
    r.LastSeen=frame;
    if(t.Relevant)r.LastPathState=t.PathState;
    if(t.Reason!=HookReason.None){var key=t.Reason.ToString();r.Reasons.TryGetValue(key,out int n);r.Reasons[key]=n+1;}
    switch(t.Result){
     case HookResult.Locked:r.Locked++;break;case HookResult.NoExitInView:r.NoExit++;break;
     case HookResult.Unreachable:r.Unreachable++;break;case HookResult.Unsupported:r.Unsupported++;break;case HookResult.Changing:r.Changing++;break;
     case HookResult.Held:r.Held++;r.Unreachable++;break;
     case HookResult.Handoff:r.Handoff++;break;
     case HookResult.Released:r.Released++;break;
     case HookResult.Bypass:r.Bypassed++;break;
     case HookResult.Rerouted:r.Rerouted++;break;
    }
    if(!r.Entered&&t.Early)r.Early=true;
    if(!r.Entered&&t.StopKind!=0&&r.StopKind==0){r.StopLane=t.StopLane;r.StopKind=t.StopKind;}
    if(!r.Entered&&(t.Result==HookResult.Locked||t.Result==HookResult.NoExitInView||t.Result==HookResult.Unreachable))
    {
     if(t.PlannedEntry!=Entity.Null)r.PlannedEntry=t.PlannedEntry;
     if(t.ExitLane!=Entity.Null){r.ExitLane=t.ExitLane;r.TurnLane=t.TurnLane;}
    }
    if(t.OnProtected)
    {
     var owner=OwnerOf(em,t.Lane);
     if(!r.Entered){r.Entered=true;r.EntryLane=t.Lane;}
     // A lane is often split into consecutive pieces on one road: only a disconnected jump
     // between pieces, or an observed lane change, counts.
     else if(owner==r.LastOwner&&r.LastProtectedLane!=Entity.Null&&t.Lane!=r.LastProtectedLane&&!Continuous(em,r.LastProtectedLane,t.Lane)){r.LaneChanged=true;r.Jumps++;}
     if(t.ChangeLane!=Entity.Null)r.LaneChanged=true;
     if(owner!=Entity.Null&&!r.Roads.Contains(owner)&&r.Roads.Count<16)r.Roads.Add(owner);
     r.LastProtectedLane=t.Lane;r.LastOwner=owner;
    }
    else if(r.Entered&&em.Exists(t.Lane)&&em.HasComponent<EdgeLane>(t.Lane))
    {
     // First ordinary road lane after the stretch (node lanes in between are skipped).
     Finish(em,world,t.Vehicle,r,"驶出",OwnerOf(em,t.Lane));records.Remove(t.Vehicle);tracked.Remove(t.Vehicle);Ended.Add(t.Vehicle);
    }
   }
   if(frame>=nextSweep)
   {
    nextSweep=frame+1024;stale.Clear();
    foreach(var pair in records)if(frame-pair.Value.LastSeen>2048)stale.Add(pair.Key);
    foreach(var vehicle in stale){Finish(em,world,vehicle,records[vehicle],"丢失",Entity.Null);records.Remove(vehicle);tracked.Remove(vehicle);Ended.Add(vehicle);}
   }
  }

  public void Flush(EntityManager em,World world,string reason)
  {
   foreach(var pair in records)Finish(em,world,pair.Key,pair.Value,reason,Entity.Null);
   records.Clear();
  }

  static bool Continuous(EntityManager em,Entity from,Entity to)
  {
   if(!em.Exists(from)||!em.Exists(to)||!em.HasComponent<Lane>(from)||!em.HasComponent<Lane>(to))return false;
   var a=em.GetComponentData<Lane>(from);var b=em.GetComponentData<Lane>(to);
   return a.m_EndNode.Equals(b.m_StartNode)||a.m_EndNode.EqualsIgnoreCurvePos(b.m_StartNode)||a.m_MiddleNode.EqualsIgnoreCurvePos(b.m_StartNode)||a.m_EndNode.EqualsIgnoreCurvePos(b.m_MiddleNode);
  }

  internal static string Kind(EntityManager em,Entity v)
  {
   if(!em.Exists(v))return "已消失";
   if(em.HasComponent<Game.Vehicles.GarbageTruck>(v))return "垃圾车";
   if(em.HasComponent<Game.Vehicles.PostVan>(v))return "邮车";
   if(em.HasComponent<Game.Vehicles.MaintenanceVehicle>(v))return "养护车";
   if(em.HasComponent<Game.Vehicles.Ambulance>(v))return "救护车";
   if(em.HasComponent<Game.Vehicles.FireEngine>(v))return "消防车";
   if(em.HasComponent<Game.Vehicles.PoliceCar>(v))return "警车";
   if(em.HasComponent<Game.Vehicles.Hearse>(v))return "灵车";
   if(em.HasComponent<Game.Vehicles.Taxi>(v))return "出租车";
   if(em.HasComponent<Game.Vehicles.PublicTransport>(v))return "公交";
   if(em.HasComponent<Game.Vehicles.DeliveryTruck>(v))return "货车";
   if(em.HasComponent<Game.Vehicles.PersonalCar>(v))return "私家车";
   return "其他";
  }

  static Entity OwnerOf(EntityManager em,Entity lane)=>lane!=Entity.Null&&em.Exists(lane)&&em.HasComponent<Owner>(lane)?em.GetComponentData<Owner>(lane).m_Owner:Entity.Null;

  void Finish(EntityManager em,World world,Entity vehicle,Record r,string end,Entity actualExit)
  {
   if(!r.Entered&&end!="驶出")return; // planned but never entered (route changed / despawned before)
   // Panel statistics: kept to its lane through the stretch and drove out.
   if(end=="驶出"&&r.Entered&&!r.LaneChanged)
   {
    TotalExits++;
    foreach(var road in r.Roads){ExitsByRoad.TryGetValue(road,out int n);ExitsByRoad[road]=n+1;}
   }
   try{
    names??=world.GetOrCreateSystemManaged<Game.UI.NameSystem>();
    var plannedExit=OwnerOf(em,r.ExitLane);
    bool aligned=r.PlannedEntry!=Entity.Null&&r.EntryLane==r.PlannedEntry;
    bool offRoute=actualExit!=Entity.Null&&plannedExit!=Entity.Null&&actualExit!=plannedExit;
    Diagnostics.Event("laneHook",new{
     Vehicle=new{vehicle.Index,vehicle.Version},Id=vehicle.Index+"/"+vehicle.Version,Kind=Kind(em,vehicle),End=end,
     Route=new{ExitRoad=RoadName(em,plannedExit),ExitRoadId=plannedExit.Index,Turn=TurnName(em,r.TurnLane)},
     Entry=new{Planned=LaneName(em,r.PlannedEntry),Actual=LaneName(em,r.EntryLane),Aligned=aligned},
     LaneChangedInside=r.LaneChanged,LaneJumps=r.Jumps,EarlyAligned=r.Early,
     Stop=r.StopKind==0?null:new{Kind=r.StopKind==1?"中途停靠":"终点",Lane=LaneName(em,r.StopLane)},
     KerbWork=r.Served+r.Skipped+r.Guarded==0?null:new{r.Served,r.Skipped,r.Guarded},
     Jam=r.Released+r.Bypassed==0?null:new{r.Released,r.Bypassed},StrictHold=new{Times=r.Held,Handoff=r.Handoff,r.Rerouted},
     Exit=new{Actual=actualExit!=Entity.Null?RoadName(em,actualExit):null,ActualId=actualExit.Index,OffRoute=offRoute},
     Plans=new{r.Locked,r.NoExit,r.Unreachable,r.Unsupported,r.Changing},Skipped=r.Reasons,PathState=r.LastPathState.ToString("X")
    });
   }catch(System.Exception ex){Mod.Log.Warn("LaneHook trace: "+ex.Message);}
  }

  string RoadName(EntityManager em,Entity road)
  {
   if(road==Entity.Null||!em.Exists(road))return null;
   if(em.HasComponent<Aggregated>(road)){var street=em.GetComponentData<Aggregated>(road).m_Aggregate;if(em.Exists(street))road=street;}
   return names.GetRenderedLabelName(road);
  }

  static string TurnName(EntityManager em,Entity lane)
  {
   if(lane==Entity.Null||!em.Exists(lane)||!em.HasComponent<Game.Net.CarLane>(lane))return "直行/未知";
   var f=em.GetComponentData<Game.Net.CarLane>(lane).m_Flags;
   if((f&(NetFlags.UTurnLeft|NetFlags.UTurnRight))!=0)return "掉头";
   if((f&(NetFlags.TurnRight|NetFlags.GentleTurnRight))!=0)return "右转";
   if((f&(NetFlags.TurnLeft|NetFlags.GentleTurnLeft))!=0)return "左转";
   return "直行";
  }

  // "<road> 从右数第k/n" among same-direction physical car lanes of the edge.
  string LaneName(EntityManager em,Entity lane)
  {
   if(lane==Entity.Null||!em.Exists(lane)||!em.HasComponent<Curve>(lane))return null;
   var owner=OwnerOf(em,lane);
   if(!em.HasComponent<EdgeLane>(lane)||owner==Entity.Null||!em.HasBuffer<Game.Net.SubLane>(owner))return "路口车道#"+lane.Index;
   var curve=em.GetComponentData<Curve>(lane).m_Bezier;
   var origin=MathUtils.Position(curve,0.5f);var tangent=math.normalizesafe(MathUtils.Tangent(curve,0.5f));
   var right=new float3(tangent.z,0,-tangent.x);
   int rank=1,total=0;
   foreach(var sub in em.GetBuffer<Game.Net.SubLane>(owner,true))
   {
    var other=sub.m_SubLane;
    if(!em.Exists(other)||em.HasComponent<MasterLane>(other)||!em.HasComponent<Game.Net.CarLane>(other)||!em.HasComponent<Curve>(other))continue;
    if((em.GetComponentData<Game.Net.CarLane>(other).m_Flags&NetFlags.SideConnection)!=0)continue;
    // Bicycle lanes are car lanes too; count only lanes cars drive on.
    if(em.HasComponent<Game.Prefabs.PrefabRef>(other))
    {
     var prefab=em.GetComponentData<Game.Prefabs.PrefabRef>(other).m_Prefab;
     if(em.HasComponent<Game.Prefabs.CarLaneData>(prefab)&&(em.GetComponentData<Game.Prefabs.CarLaneData>(prefab).m_RoadTypes&RoadTypes.Car)==0)continue;
    }
    var oc=em.GetComponentData<Curve>(other).m_Bezier;
    if(math.dot(math.normalizesafe(MathUtils.Tangent(oc,0.5f)),tangent)<0.5f)continue;
    total++;
    if(other!=lane&&math.dot(MathUtils.Position(oc,0.5f)-origin,right)>0.5f)rank++;
   }
   return RoadName(em,owner)+" 从右数第"+rank+"/"+total+"条";
  }
 }
}
