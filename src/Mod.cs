using System;
using System.IO;
using System.Security.Cryptography;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.Simulation;
namespace RoadLaneLock
{
 public sealed class Mod:IMod
 {
  internal static readonly ILog Log=LogManager.GetLogger("RoadLaneLock").SetShowsErrorsInUI(false);
  internal const string Version="0.3.8-alpha";
  internal static bool SupportedGame;
  internal static Setting Settings;
  private RuntimeCoordinator coordinator;
  private string resourcePath;
  public void OnLoad(UpdateSystem u)
  {
   // Resolve the installed asset even when the loader reads the assembly from bytes.
   var executable=AssetDatabase.global.GetAsset<ExecutableAsset>(
    SearchFilter<ExecutableAsset>.ByCondition(a=>a.definition!=null&&a.definition.FullName==typeof(Mod).Assembly.FullName,false));
   resourcePath=executable!=null?Path.GetDirectoryName(executable.GetMeta().path):Path.GetDirectoryName(typeof(Mod).Assembly.Location);
   if(!string.IsNullOrEmpty(resourcePath))Colossal.UI.UIManager.defaultUISystem.AddHostLocation("roadlanelock",resourcePath,false);
   else Log.Warn("Cannot resolve installed icon resource directory.");
   using(var sha=SHA256.Create())using(var file=File.OpenRead(typeof(CarNavigationSystem).Assembly.Location))
    SupportedGame=BitConverter.ToString(sha.ComputeHash(file)).Replace("-","")=="F076DF4B70AA0F21A80345E03A052CA2273BC6754F41DAB7E63D837837A43745";
   // Player translations live with the mod settings so they survive mod updates.
   L.Folder=Path.Combine(Colossal.PSI.Environment.EnvPath.kUserDataPath,"ModsSettings","RoadLaneLock","lang");
   Settings=new Setting(this);Settings.RegisterInOptionsUI();
   var localization=Game.SceneFlow.GameManager.instance?.localizationManager;
   // Settings page text for every game language (same tables as the panel; unknown languages get English).
   if(localization!=null)foreach(var id in localization.GetSupportedLocales())localization.AddSource(id,new SettingLocale(Settings,id));
   AssetDatabase.global.LoadSettings("RoadLaneLock",Settings,new Setting(this));
   var existingPanel=u.World.GetExistingSystemManaged<RuleUISystem>();if(existingPanel!=null)existingPanel.Enabled=true;
   coordinator=u.World.GetOrCreateSystemManaged<RuntimeCoordinator>();coordinator.Enabled=true;
   u.UpdateBefore<RuntimeCoordinator,CarNavigationSystem>(SystemUpdatePhase.GameSimulation);
   u.UpdateBefore<RuntimeCoordinator,CarNavigationSystem>(SystemUpdatePhase.LoadSimulation);
   u.UpdateBefore<RuntimeCoordinator,RoadLaneLockNavigationSystem>(SystemUpdatePhase.GameSimulation);
   u.UpdateBefore<RuntimeCoordinator,RoadLaneLockNavigationSystem>(SystemUpdatePhase.LoadSimulation);
   u.UpdateBefore<RoadLaneLockNavigationSystem,CarNavigationSystem.Actions>(SystemUpdatePhase.GameSimulation);
   u.UpdateBefore<RoadLaneLockNavigationSystem,CarNavigationSystem.Actions>(SystemUpdatePhase.LoadSimulation);
   u.UpdateBefore<LaneHookSystem,CarNavigationSystem>(SystemUpdatePhase.GameSimulation);
   u.UpdateBefore<LaneHookSystem,CarNavigationSystem>(SystemUpdatePhase.LoadSimulation);
   u.UpdateAfter<LaneHookSystem,RuntimeCoordinator>(SystemUpdatePhase.GameSimulation);
   u.UpdateAfter<LaneHookSystem,RuntimeCoordinator>(SystemUpdatePhase.LoadSimulation);
   u.World.GetOrCreateSystemManaged<ParkingSystem>().Enabled=true;
   u.UpdateBefore<ParkingSystem,Game.Pathfind.LanesModifiedSystem>(SystemUpdatePhase.ModificationEnd);
   u.UpdateBefore<ParkingBeforeSave,Game.Serialization.SerializerSystem>(SystemUpdatePhase.Serialize);
   u.UpdateAfter<ParkingAfterSave,Game.Serialization.SerializerSystem>(SystemUpdatePhase.Serialize);
   u.World.GetOrCreateSystemManaged<RoadRules>().Enabled=true;
   u.UpdateBefore<RoadRules,Game.Pathfind.LanesModifiedSystem>(SystemUpdatePhase.ModificationEnd);
   u.UpdateBefore<RoadRulesBeforeSave,Game.Serialization.SerializerSystem>(SystemUpdatePhase.Serialize);
   u.UpdateAfter<RoadRulesAfterSave,Game.Serialization.SerializerSystem>(SystemUpdatePhase.Serialize);
   u.UpdateAt<TransitRidership>(SystemUpdatePhase.GameSimulation);
   u.UpdateAt<RoadPickTool>(SystemUpdatePhase.ToolUpdate);
   u.UpdateAfter<VehicleRecorder,CarNavigationSystem.Actions>(SystemUpdatePhase.GameSimulation);
   u.UpdateAt<RoadMarkings>(SystemUpdatePhase.UIUpdate);
   u.World.GetOrCreateSystemManaged<StreetNameSigns>().Enabled=false;
   
   u.World.GetOrCreateSystemManaged<PaletteSystem>().Enabled=true;
   u.UpdateAt<PaletteSystem>(SystemUpdatePhase.UIUpdate);
   u.UpdateAt<RuleUISystem>(SystemUpdatePhase.UIUpdate);
   u.World.GetOrCreateSystemManaged<DiagnosticsSystem>().Enabled=true;
   u.UpdateAt<DiagnosticsSystem>(SystemUpdatePhase.UIUpdate);
   Diagnostics.Event("loaded",new {SupportedGame,Snapshot=Diagnostics.SnapshotPath});
   Log.Info("RoadLaneLock "+Version+": citywide emergency traffic registered. Supported game="+SupportedGame);
  }
  public void OnDispose(){Settings?.UnregisterInOptionsUI();if(resourcePath!=null)Colossal.UI.UIManager.defaultUISystem?.RemoveHostLocation("roadlanelock",resourcePath);if(coordinator!=null&&coordinator.World.IsCreated){var palette=coordinator.World.GetExistingSystemManaged<PaletteSystem>();if(palette!=null)palette.Enabled=false;var panel=coordinator.World.GetExistingSystemManaged<RuleUISystem>();if(panel!=null)panel.Enabled=false;coordinator.World.GetExistingSystemManaged<RoadPickTool>()?.End();coordinator.World.GetOrCreateSystemManaged<VehicleRecorder>().Stop("插件卸载");coordinator.World.GetExistingSystemManaged<ParkingSystem>()?.Shutdown();coordinator.World.GetExistingSystemManaged<RoadRules>()?.Shutdown();coordinator.World.GetExistingSystemManaged<RoadMarkings>()?.Shutdown();coordinator.Enabled=false;coordinator.Release();}}
 }
}







