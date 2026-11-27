using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Colossal.UI.Binding;
using Game.Prefabs;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Entities;

namespace RoadLaneLock
{
 public class NamingSettings
 {
  public bool Auto=true;
  public Dictionary<string,string> Prefix=new Dictionary<string,string>();
 }
 public partial class RuleUISystem
 {
  // Templates per transport kind; cargo kinds are prefixed with "Cargo".
  static readonly Dictionary<string,string> DefaultPrefix=new Dictionary<string,string>{
   {"Bus","B"},{"Subway","M"},{"Train","R"},{"Tram","T"},{"Ferry","F"},{"Ship","S"},{"Airplane","A"},{"Helicopter","H"},
   {"CargoTrain","CR"},{"CargoShip","CS"},{"CargoAirplane","CA"},{"CargoBus","CB"}};
  static readonly Regex CodePattern=new Regex("^([A-Z]{1,3})(\\d{1,3})$");
  NamingSettings naming=new NamingSettings();
  string namingPath;
  readonly HashSet<Entity> knownLines=new HashSet<Entity>();
  bool linesSeeded;
  float namingCheckAt;

  static string NameKey(TransportLineData data)=>(data.m_CargoTransport&&!data.m_PassengerTransport?"Cargo":"")+data.m_TransportType;
  string PrefixFor(string kind)
  {
   if(naming.Prefix.TryGetValue(kind,out var p)&&!string.IsNullOrEmpty(p))return p;
   if(DefaultPrefix.TryGetValue(kind,out p))return p;
   return kind.StartsWith("Cargo")?"C":"L";
  }
  static string CleanPrefix(string value)
  {
   var s=new string((value??"").ToUpperInvariant().Where(c=>c>='A'&&c<='Z').ToArray());
   return s.Length>3?s.Substring(0,3):s;
  }
  // Badge text: the code itself for template-style names, otherwise just the kind's prefix.
  string BadgeCode(string name,string kind)
  {
   var m=CodePattern.Match(name??"");
   return m.Success?name:PrefixFor(kind);
  }
  void InitNaming()
  {
   var root=Environment.GetEnvironmentVariable("CSII_USERDATAPATH",EnvironmentVariableTarget.User);
   if(string.IsNullOrWhiteSpace(root))root=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"..","LocalLow","Colossal Order","Cities Skylines II"));
   namingPath=Path.Combine(root,"ModsSettings","RoadLaneLock","naming.json");
   try{
    if(File.Exists(namingPath))naming=JsonConvert.DeserializeObject<NamingSettings>(File.ReadAllText(namingPath))??new NamingSettings();
   }catch(Exception e){Mod.Log.Warn("线路命名设置读取失败："+e.Message);naming=new NamingSettings();}
   naming.Prefix=naming.Prefix??new Dictionary<string,string>();
   foreach(var key in naming.Prefix.Keys.ToList()){var p=CleanPrefix(naming.Prefix[key]);if(p.Length==0)naming.Prefix.Remove(key);else naming.Prefix[key]=p;}
   foreach(var pair in DefaultPrefix)if(!naming.Prefix.ContainsKey(pair.Key))naming.Prefix[pair.Key]=pair.Value;
   AddBinding(new TriggerBinding<string>("RoadLaneLock","namingSettings",json=>{
    if(json==null||json.Length>2000)return;
    try{
     var next=JsonConvert.DeserializeObject<NamingSettings>(json);if(next==null)return;
     naming.Auto=next.Auto;
     if(next.Prefix!=null)foreach(var pair in next.Prefix){var p=CleanPrefix(pair.Value);if(p.Length>0&&pair.Key!=null&&pair.Key.Length<=24)naming.Prefix[pair.Key]=p;}
     SaveNaming();refreshAt=0;lastTransitMeta=null;
    }catch(Exception e){Mod.Log.Warn(e.Message);}
   }));
   AddBinding(new TriggerBinding("RoadLaneLock","applyNaming",()=>{if(!transitOpen)return;ApplyNames(DefaultNamedLines());refreshAt=0;}));
  }
  void SaveNaming()
  {
   try{
    Directory.CreateDirectory(Path.GetDirectoryName(namingPath));
    var temp=namingPath+".tmp";File.WriteAllText(temp,JsonConvert.SerializeObject(naming,Formatting.Indented));
    if(File.Exists(namingPath))File.Replace(temp,namingPath,namingPath+".bak");else File.Move(temp,namingPath);
   }catch(Exception e){Mod.Log.Warn("线路命名设置保存失败："+e.Message);}
  }
  bool LineKind(Entity line,out string kind)
  {
   kind=null;
   var prefab=EntityManager.GetComponentData<PrefabRef>(line).m_Prefab;
   if(!EntityManager.HasComponent<TransportLineData>(prefab))return false;
   kind=NameKey(EntityManager.GetComponentData<TransportLineData>(prefab));
   return true;
  }
  List<Entity> DefaultNamedLines()
  {
   var result=new List<Entity>();
   using(var entities=lineQuery.ToEntityArray(Allocator.Temp))
    foreach(var e in entities)if(!names.TryGetCustomName(e,out _))result.Add(e);
   return result;
  }
  // Numbers already taken per prefix, read from every line's current name.
  Dictionary<string,HashSet<int>> UsedCodes()
  {
   var used=new Dictionary<string,HashSet<int>>();
   using(var entities=lineQuery.ToEntityArray(Allocator.Temp))
    foreach(var e in entities){
     var m=CodePattern.Match(names.GetRenderedLabelName(e)??"");
     if(!m.Success)continue;
     if(!used.TryGetValue(m.Groups[1].Value,out var set))used[m.Groups[1].Value]=set=new HashSet<int>();
     set.Add(int.Parse(m.Groups[2].Value));
    }
   return used;
  }
  List<(Entity line,string name)> PlanNames(IEnumerable<Entity> targets)
  {
   var used=UsedCodes();
   var plan=new List<(Entity,string)>();
   foreach(var e in targets.OrderBy(x=>EntityManager.HasComponent<Game.Routes.RouteNumber>(x)?EntityManager.GetComponentData<Game.Routes.RouteNumber>(x).m_Number:int.MaxValue).ThenBy(x=>x.Index)){
    if(!LineKind(e,out var kind))continue;
    var prefix=PrefixFor(kind);
    if(!used.TryGetValue(prefix,out var set))used[prefix]=set=new HashSet<int>();
    int n=1;while(set.Contains(n))n++;
    if(n>999)continue;
    set.Add(n);
    plan.Add((e,prefix+n.ToString("00")));
   }
   return plan;
  }
  void ApplyNames(IEnumerable<Entity> targets)
  {
   foreach(var (line,name) in PlanNames(targets)){names.SetCustomName(line,name);Mod.Log.Info("线路命名："+Id(line)+" → "+name);}
  }
  object NamingPreview()
  {
   if(!transitOpen)return new object[0];
   return PlanNames(DefaultNamedLines()).Select(x=>new{Id=Id(x.line),From=names.GetRenderedLabelName(x.line),To=x.name}).ToArray();
  }
  // New lines get a template name once; lines present at load are only renamed via the batch button.
  void CheckNewLines()
  {
   float now=UnityEngine.Time.unscaledTime;
   if(now<namingCheckAt)return;
   namingCheckAt=now+2f;
   var fresh=new List<Entity>();
   using(var entities=lineQuery.ToEntityArray(Allocator.Temp)){
    if(!linesSeeded){knownLines.Clear();foreach(var e in entities)knownLines.Add(e);linesSeeded=true;return;}
    foreach(var e in entities)
     if(knownLines.Add(e)&&naming.Auto&&!names.TryGetCustomName(e,out _))fresh.Add(e);
    if(knownLines.Count>entities.Length*2+64){knownLines.Clear();foreach(var e in entities)knownLines.Add(e);}
   }
   if(fresh.Count>0){ApplyNames(fresh);refreshAt=0;}
  }
  void ResetNaming(){knownLines.Clear();linesSeeded=false;namingCheckAt=0;}
 }
}
