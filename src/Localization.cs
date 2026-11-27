using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace RoadLaneLock
{
 // Interface text follows the game language.
 // Source strings are the Simplified Chinese text written in the code; every other language is a
 // table mapping that source text to its translation. Built-in tables (lang/*.json, embedded) cover
 // en-US and zh-HANT; players can drop <locale>.json into the mod's lang folder to add or override a
 // language. Anything a table lacks falls back to English, then to the source text.
 internal static class L
 {
  static readonly Dictionary<string,Dictionary<string,string>> builtIn=new Dictionary<string,Dictionary<string,string>>(StringComparer.OrdinalIgnoreCase);
  static Dictionary<string,string> active=new Dictionary<string,string>(),english=new Dictionary<string,string>();
  static string loaded;
  internal static string Folder;
  internal static int Revision;
  internal static string Locale{get;private set;}="zh-HANS";

  internal static string GameLocale()=>Game.SceneFlow.GameManager.instance?.localizationManager?.activeLocaleId??"en-US";

  internal static bool IsSource(string locale)
  {
   var id=(locale??"").ToLowerInvariant();
   return id=="zh-hans"||id=="zh-cn"||id=="zh-sg";
  }

  static Dictionary<string,string> Embedded(string locale)
  {
   if(builtIn.TryGetValue(locale,out var table))return table;
   table=new Dictionary<string,string>();
   try
   {
    using(var stream=typeof(L).Assembly.GetManifestResourceStream("lang-"+locale))
     if(stream!=null)using(var reader=new StreamReader(stream))table=JsonConvert.DeserializeObject<Dictionary<string,string>>(reader.ReadToEnd())??table;
   }
   catch(Exception ex){Mod.Log.Warn("内置语言表读取失败 "+locale+"："+ex.Message);}
   builtIn[locale]=table;
   return table;
  }

  static Dictionary<string,string> PlayerFile(string locale)
  {
   if(string.IsNullOrEmpty(Folder))return null;
   try
   {
    var path=Path.Combine(Folder,locale+".json");
    if(!File.Exists(path))return null;
    return JsonConvert.DeserializeObject<Dictionary<string,string>>(File.ReadAllText(path));
   }
   catch(Exception ex){Mod.Log.Warn("语言文件读取失败 "+locale+"："+ex.Message);return null;}
  }

  static string Builtin(string locale)
  {
   var id=locale.ToLowerInvariant();
   if(id=="zh-hant"||id=="zh-tw"||id=="zh-hk")return "zh-HANT";
   if(id.StartsWith("en"))return "en-US";
   return locale;
  }

  // Merged table for a locale: English base, then the built-in table, then the player's file.
  internal static Dictionary<string,string> TableFor(string locale)
  {
   var table=new Dictionary<string,string>(Embedded("en-US"));
   foreach(var p in Embedded(Builtin(locale)))table[p.Key]=p.Value;
   var file=PlayerFile(locale);
   if(file!=null)foreach(var p in file)if(!string.IsNullOrEmpty(p.Value))table[p.Key]=p.Value;
   return table;
  }

  // Called every UI frame; reloads when the game language changes.
  internal static bool Refresh(bool force=false)
  {
   var locale=GameLocale();
   if(!force&&locale==loaded)return false;
   loaded=locale;Locale=locale;
   english=TableFor("en-US");
   active=IsSource(locale)?(PlayerFile(locale)??new Dictionary<string,string>()):TableFor(locale);
   Revision++;
   return true;
  }

  internal static string T(string source)
  {
   if(string.IsNullOrEmpty(source))return source;
   if(loaded==null)Refresh();
   return active.TryGetValue(source,out var text)&&!string.IsNullOrEmpty(text)?text:IsSource(Locale)?source:english.TryGetValue(source,out var en)&&!string.IsNullOrEmpty(en)?en:source;
  }

  internal static string F(string source,params object[] args)
  {
   try{return string.Format(T(source),args);}catch(FormatException){return string.Format(source,args);}
  }

  // The table the panel uses, as JSON.
  internal static string UiJson()=>JsonConvert.SerializeObject(new{Locale,Table=IsSource(Locale)?active:active});

  // A translation template: every source string with its English text.
  internal static string ExportTemplate()
  {
   if(string.IsNullOrEmpty(Folder))return null;
   Directory.CreateDirectory(Folder);
   var path=Path.Combine(Folder,"template.json");
   var sorted=new SortedDictionary<string,string>(Embedded("en-US"),StringComparer.Ordinal);
   File.WriteAllText(path,JsonConvert.SerializeObject(sorted,Formatting.Indented));
   File.WriteAllText(Path.Combine(Folder,"README.txt"),
    "Translate Traffic Toolbox / 交通工具箱\r\n\r\n"+
    "1. Copy template.json and rename it to your game language id, e.g. ja-JP.json, ko-KR.json, de-DE.json.\r\n"+
    "2. Keep every key (the left side). Replace each value (the right side) with your translation.\r\n"+
    "   Keep {0}, {1} ... placeholders. Leave a value empty to fall back to English.\r\n"+
    "3. Restart the game or switch the game language once. Your file overrides the built-in text.\r\n");
   return path;
  }

  internal static void OpenFolder()
  {
   if(string.IsNullOrEmpty(Folder))return;
   try{Directory.CreateDirectory(Folder);System.Diagnostics.Process.Start(Folder);}
   catch(Exception ex){Mod.Log.Warn("打开语言文件夹失败："+ex.Message);}
  }
 }
}
