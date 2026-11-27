using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace RoadLaneLock
{
 [FileLocation("ModsSettings/RoadLaneLock/RoadLaneLock")]
 public sealed class Setting:ModSetting
 {
  public const string Section="Main",Group="LaneControl",TransitGroup="Transit",ParkingGroup="Parking",LanguageGroup="Language";
  public Setting(IMod mod):base(mod){}
  // Prototype: hook before the game's navigation instead of replacing it.
  [SettingsUISection(Section,Group)]
  public bool HookExecutor{get;set;}
  // Selecting a public transport vehicle or stop opens the transit page on its line.
  [SettingsUISection(Section,TransitGroup)]
  public bool AutoOpenTransit{get;set;}=true;
  // Banning kerb parking also removes cars already parked there.
  [SettingsUISection(Section,ParkingGroup)]
  public bool ClearParkedOnBan{get;set;}=true;
  // Translation entry for players: export a template, open the folder where <locale>.json files go.
  [SettingsUIButton,SettingsUISection(Section,LanguageGroup)]
  public bool ExportTranslation{set{var path=L.ExportTemplate();if(path!=null){Mod.Log.Info("翻译模板已导出："+path);L.OpenFolder();}}}
  [SettingsUIButton,SettingsUISection(Section,LanguageGroup)]
  public bool OpenLanguageFolder{set{L.OpenFolder();L.Refresh(true);}}
  public override void SetDefaults(){HookExecutor=false;AutoOpenTransit=true;ClearParkedOnBan=true;}
 }

 // Settings page text for one game language, from the same tables as the panel (source = Simplified Chinese).
 internal sealed class SettingLocale:IDictionarySource
 {
  readonly Setting setting;readonly string locale;
  public SettingLocale(Setting setting,string locale){this.setting=setting;this.locale=locale;}
  public IEnumerable<KeyValuePair<string,string>> ReadEntries(IList<IDictionaryEntryError> errors,Dictionary<string,int> indexCounts)
  {
   var table=L.IsSource(locale)?new Dictionary<string,string>():L.TableFor(locale);
   string T(string source)=>table.TryGetValue(source,out var text)&&!string.IsNullOrEmpty(text)?text:source;
   return new Dictionary<string,string>{
    {setting.GetSettingsLocaleID(),T("交通工具箱")},
    {setting.GetOptionTabLocaleID(Setting.Section),T("通用")},
    {setting.GetOptionGroupLocaleID(Setting.Group),T("车道规则")},
    {setting.GetOptionGroupLocaleID(Setting.TransitGroup),T("公共交通信息")},
    {setting.GetOptionGroupLocaleID(Setting.ParkingGroup),T("路边停车")},
    {setting.GetOptionGroupLocaleID(Setting.LanguageGroup),T("语言")},
    {setting.GetOptionLabelLocaleID(nameof(Setting.AutoOpenTransit)),T("点选公共交通车辆或站点时自动打开")},
    {setting.GetOptionDescLocaleID(nameof(Setting.AutoOpenTransit)),T("在游戏里点选公交、地铁等载客车辆或站点时，自动打开「公共交通信息」并定位到对应线路。")},
    {setting.GetOptionLabelLocaleID(nameof(Setting.ClearParkedOnBan)),T("开启禁停时清走已停车辆")},
    {setting.GetOptionDescLocaleID(nameof(Setting.ClearParkedOnBan)),T("开启禁止路边停车后，把这段路边已经停着的车直接清走（和原版删除车辆一样）；读档后也会对已禁停路段清一次。")},
    {setting.GetOptionLabelLocaleID(nameof(Setting.HookExecutor)),T("新执行器（测试）")},
    {setting.GetOptionDescLocaleID(nameof(Setting.HookExecutor)),T("开启后不再替换原版车辆导航，改为在原版导航前锁定车道，并按完整路线选择入口车道。旧执行器停用；绕行、市政保底暂不可用。逐车诊断写入 Logs/RoadLaneLock-events.jsonl。")},
    {setting.GetOptionLabelLocaleID(nameof(Setting.ExportTranslation)),T("导出翻译模板")},
    {setting.GetOptionDescLocaleID(nameof(Setting.ExportTranslation)),T("把全部界面文字和英文原文写到语言文件夹的 template.json，照着翻译后另存为「语言代码.json」（如 ja-JP.json）即可。")},
    {setting.GetOptionLabelLocaleID(nameof(Setting.OpenLanguageFolder)),T("打开语言文件夹")},
    {setting.GetOptionDescLocaleID(nameof(Setting.OpenLanguageFolder)),T("放在这里的「语言代码.json」会覆盖或补充内置文字；没翻译的条目显示英文。")},
   };
  }
  public void Unload(){}
 }
}
