using System;
using System.Collections.Generic;
using Colossal.UI.Binding;
using Game.Common;
using Game.Prefabs;
using Game.Routes;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace RoadLaneLock
{
 // Line settings popup: everything the vanilla line panel offers (name, vehicle count, schedule,
 // vehicle models, delete), done the way the vanilla sections do it.
 public partial class RuleUISystem
 {
  Entity vehicleCountPolicy,dayPolicy,nightPolicy;
  ComponentLookup<PolicySliderData> sliderLookup;
  TransportVehicleSelectData vehicleSelect;
  EntityQuery vehiclePrefabQuery,depotQuery;
  ValueBinding<string> modelsBinding;
  string modelsLine,lastModels;
  static System.Reflection.MethodInfo adjustmentFromCount;

  void InitTransitSettings()
  {
   sliderLookup=GetComponentLookup<PolicySliderData>(true);
   vehicleSelect=new TransportVehicleSelectData(this);
   vehiclePrefabQuery=GetEntityQuery(TransportVehicleSelectData.GetEntityQueryDesc());
   depotQuery=GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<Game.Buildings.TransportDepot>(),ComponentType.ReadOnly<PrefabRef>()},
    None=new[]{ComponentType.ReadOnly<Deleted>(),ComponentType.ReadOnly<Game.Tools.Temp>()}});
   AddBinding(modelsBinding=new ValueBinding<string>("RoadLaneLock","transitModels","{}"));
   AddBinding(new TriggerBinding<string,string>("RoadLaneLock","renameLine",(id,name)=>{
    if(!TryGetTransitLine(id,out var line))return;
    names.SetCustomName(line,(name??"").Trim());
    selectedInfo.RequestUpdate();refreshAt=0;
   }));
   AddBinding(new TriggerBinding<string,int>("RoadLaneLock","setLineVehicles",SetLineVehicles));
   AddBinding(new TriggerBinding<string,int>("RoadLaneLock","setLineSchedule",SetLineSchedule));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","deleteLine",id=>{
    if(!TryGetTransitLine(id,out var line))return;
    // Vanilla TransportationOverviewUISystem.DeleteLine: mark the line deleted at the end of the frame.
    World.GetOrCreateSystemManaged<Game.EndFrameBarrier>().CreateCommandBuffer().AddComponent<Deleted>(line);
    Mod.Log.Info("删除线路 "+id);
    refreshAt=0;
   }));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","lineModels",id=>{modelsLine=id;lastModels=null;PublishModels();}));
   AddBinding(new TriggerBinding<string,string,bool>("RoadLaneLock","setLineModel",SetLineModel));
  }

  bool LoadLinePolicies()
  {
   if(vehicleCountPolicy!=Entity.Null&&EntityManager.Exists(vehicleCountPolicy))return true;
   var prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();
   if(transportConfigQuery.IsEmptyIgnoreFilter||!prefabs.TryGetSingletonPrefab<UITransportConfigurationPrefab>(transportConfigQuery,out var config))return false;
   if(config.m_VehicleCountPolicy!=null)vehicleCountPolicy=prefabs.GetEntity(config.m_VehicleCountPolicy);
   if(config.m_DayRoutePolicy!=null)dayPolicy=prefabs.GetEntity(config.m_DayRoutePolicy);
   if(config.m_NightRoutePolicy!=null)nightPolicy=prefabs.GetEntity(config.m_NightRoutePolicy);
   return vehicleCountPolicy!=Entity.Null;
  }

  // ---- Schedule (vanilla RouteSchedule: Day=0, Night=1, DayAndNight=2) ----
  int ReadSchedule(Entity line)
  {
   if(!EntityManager.HasComponent<Route>(line))return 2;
   var route=EntityManager.GetComponentData<Route>(line);
   if(RouteUtils.CheckOption(route,RouteOption.Day))return 0;
   if(RouteUtils.CheckOption(route,RouteOption.Night))return 1;
   return 2;
  }
  void SetLineSchedule(string id,int schedule)
  {
   if(!TryGetTransitLine(id,out var line)||!LoadLinePolicies()||schedule<0||schedule>2)return;
   if(nightPolicy!=Entity.Null)policies.SetPolicy(line,nightPolicy,schedule==1);
   if(dayPolicy!=Entity.Null)policies.SetPolicy(line,dayPolicy,schedule==0);
   selectedInfo.RequestUpdate();refreshAt=0;
  }

  // ---- Vehicle count (vanilla VehicleCountSection) ----
  float StableDuration(Entity line,TransportLineData data)
  {
   float duration=0;
   if(EntityManager.HasBuffer<RouteSegment>(line))
    foreach(var s in EntityManager.GetBuffer<RouteSegment>(line,true))
     if(EntityManager.HasComponent<Game.Pathfind.PathInformation>(s.m_Segment))duration+=EntityManager.GetComponentData<Game.Pathfind.PathInformation>(s.m_Segment).m_Duration;
   if(EntityManager.HasBuffer<RouteWaypoint>(line))
    foreach(var w in EntityManager.GetBuffer<RouteWaypoint>(line,true))
     if(EntityManager.HasComponent<VehicleTiming>(w.m_Waypoint))duration+=data.m_StopDuration;
   return duration;
  }
  int CountFromAdjustment(float adjustment,float interval,float duration)
  {
   var modifier=new RouteModifier();
   foreach(var d in EntityManager.GetBuffer<RouteModifierData>(vehicleCountPolicy,true))
   {
    if(d.m_Type!=RouteModifierType.VehicleInterval)continue;
    float delta=Game.Policies.RouteModifierInitializeSystem.RouteModifierRefreshData.GetModifierDelta(d,adjustment,vehicleCountPolicy,sliderLookup);
    Game.Policies.RouteModifierInitializeSystem.RouteModifierRefreshData.AddModifierData(ref modifier,d,delta);
   }
   interval+=modifier.m_Delta.x;interval+=interval*modifier.m_Delta.y;
   return Game.Simulation.TransportLineSystem.CalculateVehicleCount(interval,duration);
  }
  // Target, minimum and maximum vehicle count of a line; null when the line has no vehicle count policy.
  int[] VehicleCounts(Entity line,TransportLineData data)
  {
   if(!LoadLinePolicies()||!EntityManager.HasComponent<PolicySliderData>(vehicleCountPolicy)||!EntityManager.HasBuffer<RouteModifierData>(vehicleCountPolicy))return null;
   float duration=StableDuration(line,data);
   if(duration<=0)return null;
   sliderLookup.Update(this);
   float interval=data.m_DefaultVehicleInterval;
   if(EntityManager.HasBuffer<RouteModifier>(line))RouteUtils.ApplyModifier(ref interval,EntityManager.GetBuffer<RouteModifier>(line,true),RouteModifierType.VehicleInterval);
   var slider=EntityManager.GetComponentData<PolicySliderData>(vehicleCountPolicy);
   int a=CountFromAdjustment(slider.m_Range.min,data.m_DefaultVehicleInterval,duration),b=CountFromAdjustment(slider.m_Range.max,data.m_DefaultVehicleInterval,duration);
   return new[]{Game.Simulation.TransportLineSystem.CalculateVehicleCount(interval,duration),math.min(a,b),math.max(a,b)};
  }
  void SetLineVehicles(string id,int count)
  {
   if(!TryGetTransitLine(id,out var line)||!LoadLinePolicies())return;
   var prefab=EntityManager.GetComponentData<PrefabRef>(line).m_Prefab;
   if(!EntityManager.HasComponent<TransportLineData>(prefab))return;
   var data=EntityManager.GetComponentData<TransportLineData>(prefab);
   var counts=VehicleCounts(line,data);
   if(counts==null)return;
   count=math.clamp(count,counts[1],counts[2]);
   try
   {
    // The vanilla conversion lives in a private job struct; call it rather than re-deriving the formula.
    if(adjustmentFromCount==null)
     adjustmentFromCount=typeof(Game.UI.InGame.VehicleCountSection).GetNestedType("CalculateVehicleCountJob",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public)
      ?.GetMethod("CalculateAdjustmentFromVehicleCount",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public);
    if(adjustmentFromCount==null){Mod.Log.Warn("车辆数量：找不到原版换算方法");return;}
    float adjustment=(float)adjustmentFromCount.Invoke(null,new object[]{count,data.m_DefaultVehicleInterval,StableDuration(line,data),
     EntityManager.GetBuffer<RouteModifierData>(vehicleCountPolicy,true),EntityManager.GetComponentData<PolicySliderData>(vehicleCountPolicy)});
    policies.SetPolicy(line,vehicleCountPolicy,true,adjustment);
   }
   catch(Exception ex){Mod.Log.Warn("车辆数量设置失败："+ex.Message);return;}
   selectedInfo.RequestUpdate();refreshAt=0;
  }

  // ---- Vehicle models (vanilla SelectVehiclesSection, passenger lines) ----
  void PublishModels()
  {
   string json="{}";
   try{json=BuildModels();}catch(Exception ex){Mod.Log.Warn("车型列表失败："+ex.Message);}
   if(json!=lastModels){lastModels=json;modelsBinding.Update(json);}
  }
  string BuildModels()
  {
   if(!TryGetTransitLine(modelsLine,out var line))return "{}";
   var prefab=EntityManager.GetComponentData<PrefabRef>(line).m_Prefab;
   if(!EntityManager.HasComponent<TransportLineData>(prefab))return "{}";
   var data=EntityManager.GetComponentData<TransportLineData>(prefab);
   if(!data.m_PassengerTransport)return JsonConvert.SerializeObject(new{Line=modelsLine,Supported=false});
   // Energy types the city's depots for this transport type can serve (vanilla TransportDepots job).
   var energy=Game.Vehicles.EnergyTypes.None;
   using(var depots=depotQuery.ToEntityArray(Allocator.Temp))
    foreach(var depot in depots)
    {
     void Add(Entity p){if(EntityManager.HasComponent<TransportDepotData>(p)){var d=EntityManager.GetComponentData<TransportDepotData>(p);if(d.m_TransportType==data.m_TransportType)energy|=d.m_EnergyTypes;}}
     Add(EntityManager.GetComponentData<PrefabRef>(depot).m_Prefab);
     if(EntityManager.HasBuffer<Game.Buildings.InstalledUpgrade>(depot))
      foreach(var u in EntityManager.GetBuffer<Game.Buildings.InstalledUpgrade>(depot,true))
       if(EntityManager.HasComponent<PrefabRef>(u.m_Upgrade))Add(EntityManager.GetComponentData<PrefabRef>(u.m_Upgrade).m_Prefab);
    }
   var primary=new NativeList<Entity>(Allocator.TempJob);var secondary=new NativeList<Entity>(Allocator.TempJob);
   var available=new List<object>();
   try
   {
    JobHandle handle;
    vehicleSelect.PreUpdate(this,World.GetOrCreateSystemManaged<Game.City.CityConfigurationSystem>(),vehiclePrefabQuery,Allocator.TempJob,out handle);
    handle.Complete();
    vehicleSelect.ListVehicles(data.m_TransportType,energy,data.m_SizeClass,PublicTransportPurpose.TransportLine,Game.Economy.Resource.NoResource,primary,secondary,false);
    vehicleSelect.PostUpdate(default);
    var prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();
    var images=World.GetOrCreateSystemManaged<Game.UI.ImageSystem>();
    var dictionary=Game.SceneFlow.GameManager.instance?.localizationManager?.activeDictionary;
    var seen=new HashSet<Entity>();
    foreach(var p in primary)
    {
     if(!seen.Add(p))continue;
     string name=prefabs.GetPrefabName(p);
     if(dictionary!=null&&dictionary.TryGetValue("Assets.NAME["+name+"]",out var localized))name=localized;
     available.Add(new{Id=Id(p),Name=name,Thumb=images.GetThumbnail(p)});
    }
   }
   finally{primary.Dispose();secondary.Dispose();}
   var selected=new List<string>();
   if(EntityManager.HasBuffer<VehicleModel>(line))
    foreach(var m in EntityManager.GetBuffer<VehicleModel>(line,true))if(m.m_PrimaryPrefab!=Entity.Null)selected.Add(Id(m.m_PrimaryPrefab));
   return JsonConvert.SerializeObject(new{Line=modelsLine,Supported=true,Available=available,Selected=selected});
  }
  void SetLineModel(string id,string model,bool on)
  {
   if(!TryGetTransitLine(id,out var line)||!TryParseEntity(model,out var prefab)||!EntityManager.Exists(prefab)||!EntityManager.HasBuffer<VehicleModel>(line))return;
   var buffer=EntityManager.GetBuffer<VehicleModel>(line);
   int at=-1;for(int i=0;i<buffer.Length;i++)if(buffer[i].m_PrimaryPrefab==prefab){at=i;break;}
   if(on&&at<0)buffer.Add(new VehicleModel{m_PrimaryPrefab=prefab,m_SecondaryPrefab=Entity.Null});
   if(!on&&at>=0)buffer.RemoveAtSwapBack(at);
   selectedInfo.RequestUpdate();
   modelsLine=id;lastModels=null;PublishModels();
  }
 }
}
