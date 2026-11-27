using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Colossal.Logging;
using Colossal.UI.Binding;
using Game;
using Game.Modding;
using Game.UI;
using Newtonsoft.Json;
namespace RoadLaneLock
{
 public class Swatch {public string Hex;public string Name;}
 public class PaletteData {public List<Swatch> Favorites=new List<Swatch>();public List<string> Recent=new List<string>();}
 public partial class PaletteSystem:UISystemBase
 {
  bool reported;PaletteData data=new PaletteData();ValueBinding<string> state,status;string path;bool damaged;
  static string Hex(string s){s=(s??"").Trim().ToUpperInvariant();if(!s.StartsWith("#"))s="#"+s;return Regex.IsMatch(s,"^#[0-9A-F]{6}$")?s:null;}
  protected override void OnCreate()
  {
   base.OnCreate();
   var root=Environment.GetEnvironmentVariable("CSII_USERDATAPATH",EnvironmentVariableTarget.User);
   if(string.IsNullOrWhiteSpace(root))root=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"..","LocalLow","Colossal Order","Cities Skylines II"));
   path=Path.Combine(root,"ModsSettings","TransitPalette","palette.json");
   AddBinding(new TriggerBinding("RoadLaneLock.Palette","uiReady",()=>{if(!reported){reported=true;Mod.Log.Info("线路色板已在全城交通面板中渲染");}}));
   AddBinding(state=new ValueBinding<string>("RoadLaneLock.Palette","data","{}"));
   AddBinding(status=new ValueBinding<string>("RoadLaneLock.Palette","status",""));
   try{if(File.Exists(path))data=JsonConvert.DeserializeObject<PaletteData>(File.ReadAllText(path))??new PaletteData();
    data.Favorites=data.Favorites??new List<Swatch>();data.Recent=data.Recent??new List<string>();
    data.Favorites.RemoveAll(x=>x==null||Hex(x.Hex)==null);data.Recent.RemoveAll(x=>Hex(x)==null);
    foreach(var x in data.Favorites)x.Hex=Hex(x.Hex);
   }catch(Exception e){damaged=true;status.Update("色板文件读取失败：本次不覆盖原文件");Mod.Log.Warn(e.Message);}
   Publish();
   AddBinding(new TriggerBinding<string>("RoadLaneLock.Palette","recent",Remember));
   AddBinding(new TriggerBinding<string>("RoadLaneLock.Palette","favorite",s=>{
    if(!Enabled||s==null||s.Length>500)return;
    try{var x=JsonConvert.DeserializeObject<Swatch>(s);var hex=Hex(x?.Hex);if(hex==null)return;
     var name=(x.Name??"").Trim();if(name.Length>32)name=name.Substring(0,32);if(name.Length==0)name=hex;
     var old=data.Favorites.Find(v=>v.Hex==hex);
     if(old!=null)old.Name=name;else if(data.Favorites.Count<32)data.Favorites.Add(new Swatch{Hex=hex,Name=name});else{status.Update("最多收藏 32 个颜色，请先删除部分收藏");return;}Save();
    }catch(Exception e){Mod.Log.Warn(e.Message);}
   }));
   AddBinding(new TriggerBinding<string>("RoadLaneLock.Palette","remove",s=>{if(!Enabled)return;data.Favorites.RemoveAll(x=>x.Hex==Hex(s));Save();}));
  }
  internal void Remember(string s){var hex=Hex(s);if(!Enabled||hex==null)return;data.Recent.RemoveAll(x=>Hex(x)==hex);data.Recent.Insert(0,hex);if(data.Recent.Count>12)data.Recent.RemoveRange(12,data.Recent.Count-12);Save();}
  void Publish(){state.Update(JsonConvert.SerializeObject(data));}
  void Save(){Publish();if(damaged)return;try{Directory.CreateDirectory(Path.GetDirectoryName(path));var temp=path+".tmp";File.WriteAllText(temp,JsonConvert.SerializeObject(data,Formatting.Indented));if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);status.Update("色板已保存");}catch(Exception e){status.Update("保存失败，本次仅临时记忆");Mod.Log.Warn(e.Message);}}
  protected override void OnUpdate(){}
 }
}

