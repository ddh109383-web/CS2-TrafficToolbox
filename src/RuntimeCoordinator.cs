using System;
using Colossal.Serialization.Entities;
using Game;
using Game.Simulation;
using Unity.Entities;
namespace RoadLaneLock
{
 public partial class RuntimeCoordinator:GameSystemBase
 {
  private RoadLaneLockNavigationSystem executor;
  private CarNavigationSystem original;
  private LaneHookSystem hook;
  public bool OwnsNavigation {get;private set;}
  // New (hooked) executor running; the game's own navigation stays enabled.
  public bool HookActive {get;private set;}
  public bool RulesActive=>OwnsNavigation||HookActive;
  public string Problem {get;private set;}="";
  public RoadLaneLockNavigationSystem Executor=>executor;
  protected override void OnCreate()
  {
   base.OnCreate();executor=World.GetOrCreateSystemManaged<RoadLaneLockNavigationSystem>();
   original=World.GetOrCreateSystemManaged<CarNavigationSystem>();executor.Enabled=false;
   hook=World.GetOrCreateSystemManaged<LaneHookSystem>();hook.Enabled=false;
  }
  private int cachedSystemVersion=-1;
  private readonly System.Collections.Generic.List<ComponentSystemBase> navigationCandidates=new System.Collections.Generic.List<ComponentSystemBase>();
  private string FindConflict()
  {
   if(cachedSystemVersion!=World.Version)
   {
    navigationCandidates.Clear();
    foreach(var system in World.Systems)
    {
     if(system==executor||system==original||system is CarNavigationSystem.Actions)continue;
     var name=system.GetType().FullName;
     if(name!=null&&name.IndexOf("CarNavigation",StringComparison.OrdinalIgnoreCase)>=0)
      navigationCandidates.Add(system);
    }
    cachedSystemVersion=World.Version;
   }
   foreach(var candidate in navigationCandidates)
    if(candidate.Enabled)return candidate.GetType().FullName;
   return "";
  }
  private bool WantsHook=>Mod.Settings!=null&&Mod.Settings.HookExecutor;
  // Hooked mode: give navigation back to the game and run only the pre-navigation lane lock.
  private bool EnsureHook()
  {
   if(OwnsNavigation)
   {
    EntityManager.CompleteAllTrackedJobs();executor.CompleteExecution();executor.Enabled=false;
    original.Enabled=true;OwnsNavigation=false;
    Mod.Log.Info("RoadLaneLock released native navigation for the hooked executor.");
   }
   if(!original.Enabled)
   {
    if(HookActive){hook.Enabled=false;HookActive=false;}
    Problem="原版导航已被其他模组关闭，新执行器无法挂接。";return false;
   }
   if(!HookActive){hook.Enabled=true;HookActive=true;Mod.Log.Info("RoadLaneLock hooked executor started.");Diagnostics.Event("laneHookStarted");}
   Problem="";return true;
  }
  public bool EnsureActive()
  {
   if(WantsHook)return EnsureHook();
   if(HookActive){hook.Enabled=false;HookActive=false;Mod.Log.Info("RoadLaneLock hooked executor stopped.");}
   if(!Mod.SupportedGame){Problem="游戏版本与本次构建不一致，执行器未启用。";return false;}
   var conflict=FindConflict();
   if(conflict.Length>0||(!OwnsNavigation&&!original.Enabled))
   {
    Problem="检测到其他车辆导航控制器，请先停用重叠功能："+(conflict.Length>0?conflict:"原版导航已被其他模组关闭");
    executor.CompleteExecution();executor.Enabled=false;OwnsNavigation=false;return false;
   }
   if(OwnsNavigation&&original.Enabled)
   {
    executor.CompleteExecution();executor.Enabled=false;OwnsNavigation=false;
    Problem="其他模组重新启用了原版导航，本插件已暂停，避免重复执行。";return false;
   }
   if(!OwnsNavigation)
   {
    EntityManager.CompleteAllTrackedJobs();executor.RefreshProtectedOwners();
    original.Enabled=false;executor.Enabled=true;OwnsNavigation=true;
    Mod.Log.Info("RoadLaneLock acquired native navigation; original Actions retained.");
   }
   Problem="";return true;
  }
  public void SetRule(Entity road,byte direction,bool enabled)
  {
   if(enabled&&!EnsureActive())throw new InvalidOperationException(Problem);
   executor.ConfigureExecution(road,direction,enabled);
  }
  protected override void OnUpdate()
  {EnsureActive();}
  protected override void OnGameLoadingComplete(Purpose purpose,GameMode mode)
  {
   base.OnGameLoadingComplete(purpose,mode);executor.RefreshProtectedOwners();
   if(mode==GameMode.Game)EnsureActive();
  }
  public void Release()
  {
   if(HookActive){hook.Enabled=false;HookActive=false;}
   executor.CompleteExecution();executor.Enabled=false;
   if(OwnsNavigation&&FindConflict().Length==0)original.Enabled=true;
   OwnsNavigation=false;
  }
  public void ResetAll()
  {executor.ClearRulesAndRequestOriginalPaths();Mod.Log.Info("Road rules cleared; citywide emergency traffic remains active.");}
 }
}
