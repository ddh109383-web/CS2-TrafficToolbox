using System;
using System.Collections.Generic;
using Colossal.UI.Binding;
using Game.Net;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace RoadLaneLock
{
 // Street names: a street (the game's aggregate) is cut into segments at junctions, so lists show the
 // same name several times. Segments get a chome label along the street (一丁目 from its south / west
 // end); different streets with the same name are counted; the whole street can be renamed.
 public partial class RuleUISystem
 {
  sealed class StreetRename {public string Session {get;set;} public int Index {get;set;} public int Version {get;set;} public string Name {get;set;}}
  readonly Dictionary<string,int> streetsByName=new Dictionary<string,int>();
  readonly Dictionary<Entity,(int chome,int count)> chomeCache=new Dictionary<Entity,(int,int)>();
  float streetsCheckedAt=-100f;
  EntityQuery streetQuery;
  string renameFeedback;
  float renameFeedbackUntil;

  void InitStreetNames()
  {
   streetQuery=GetEntityQuery(ComponentType.ReadOnly<Aggregate>(),ComponentType.ReadOnly<AggregateElement>(),ComponentType.Exclude<Game.Common.Deleted>());
   AddBinding(new TriggerBinding<string>("RoadLaneLock","renameStreet",json=>{
    if(!TryReadRename(json,out var r,out var road))return;
    var name=(r.Name??"").Trim();
    if(name.Length==0||name.Length>32)return;
    var street=StreetOf(road);
    names.SetCustomName(street,name);
    Mod.Log.Info("街道改名："+Id(street)+" → "+name);
    AfterRename("✓ 已改名为 "+name);
   }));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","resetStreetName",json=>{
    if(!TryReadRename(json,out var r,out var road))return;
    var street=StreetOf(road);
    // An empty custom name drops it, so the game's own generated name shows again.
    names.SetCustomName(street,"");
    Mod.Log.Info("街道恢复原名："+Id(street));
    AfterRename("✓ 已恢复原名");
   }));
  }

  void AfterRename(string feedback)
  {
   streetsCheckedAt=-100f;ruleRoadInfo.Clear();chomeCache.Clear();
   renameFeedback=feedback;renameFeedbackUntil=UnityEngine.Time.unscaledTime+3f;
   Publish();
  }

  bool TryReadRename(string json,out StreetRename r,out Entity road)
  {
   r=null;road=picker.Selected;
   if(json==null||json.Length>512)return false;
   try{r=JsonConvert.DeserializeObject<StreetRename>(json);}catch{return false;}
   return r!=null&&r.Session==session&&r.Index==road.Index&&r.Version==road.Version&&RoadInspector.IsRoad(EntityManager,road);
  }

  Entity StreetOf(Entity road)
  {
   if(EntityManager.HasComponent<Aggregated>(road)){var s=EntityManager.GetComponentData<Aggregated>(road).m_Aggregate;if(EntityManager.Exists(s))return s;}
   return road;
  }

  // Rendered name -> number of streets carrying it; refreshed every few seconds while a list needs it.
  void RefreshStreetNames()
  {
   float now=UnityEngine.Time.unscaledTime;
   if(now-streetsCheckedAt<5f)return;
   streetsCheckedAt=now;streetsByName.Clear();chomeCache.Clear();
   using var streets=streetQuery.ToEntityArray(Allocator.Temp);
   foreach(var s in streets)
   {
    string n;try{n=names.GetRenderedLabelName(s);}catch{continue;}
    if(string.IsNullOrEmpty(n))continue;
    streetsByName.TryGetValue(n,out int c);streetsByName[n]=c+1;
   }
  }

  // Other streets with the same name (0 = unique).
  int SameNameStreets(Entity road)
  {
   RefreshStreetNames();
   var n=RoadName(road);
   return n!=null&&streetsByName.TryGetValue(n,out int c)?math.max(0,c-1):0;
  }

  // Position of the segment along its street, counted from the south end (west end for an east-west street).
  (int chome,int count) Chome(Entity road)
  {
   RefreshStreetNames();
   if(chomeCache.TryGetValue(road,out var cached))return cached;
   var result=(0,1);
   var street=StreetOf(road);
   if(street!=road&&EntityManager.HasBuffer<AggregateElement>(street))
   {
    var elements=EntityManager.GetBuffer<AggregateElement>(street,true);
    int count=elements.Length,at=-1;
    for(int i=0;i<count;i++)if(elements[i].m_Edge==road){at=i;break;}
    if(at>=0&&count>1)
    {
     float3 a=Middle(elements[0].m_Edge),b=Middle(elements[count-1].m_Edge);
     var d=(b-a).xz;
     bool reverse=math.abs(d.y)>=math.abs(d.x)?d.y<0:d.x<0; // start at the south / west end
     result=(reverse?count-at:at+1,count);
    }
   }
   chomeCache[road]=result;
   return result;
  }

  float3 Middle(Entity edge)=>EntityManager.HasComponent<Curve>(edge)?Colossal.Mathematics.MathUtils.Position(EntityManager.GetComponentData<Curve>(edge).m_Bezier,.5f):float3.zero;

  static readonly string[] digits={"","一","二","三","四","五","六","七","八","九"};
  public static string ChomeLabel(int n)
  {
   if(n<=0)return null;
   if(n<10)return digits[n]+"丁目";
   if(n<100)return (n/10==1?"":digits[n/10])+"十"+digits[n%10]+"丁目";
   return n+"丁目";
  }
  string ChomeOf(Entity road){var (c,count)=Chome(road);return count>1?ChomeLabel(c):null;}

  // Japanese-style suggestions from the current name's stem; names already used by another street are skipped.
  static readonly string[] streetSuffixes={"大街","大道","大路","街道","本通","通","筋","坂","街","路","道","巷"};
  static readonly string[] stemForms={"通","筋","坂","大路","本通"};
  static readonly string[] commonNames={"川端通","本町通","中央通","駅前通","寺町通","桜坂"};
  public static string StreetStem(string name)
  {
   name=(name??"").Trim();
   foreach(var s in streetSuffixes)if(name.Length>s.Length&&name.EndsWith(s,StringComparison.Ordinal))return name.Substring(0,name.Length-s.Length);
   return name;
  }
  List<string> StreetSuggestions(string current)
  {
   var list=new List<string>();var stem=StreetStem(current);
   void Add(string n){if(list.Count<6&&!string.IsNullOrEmpty(n)&&n!=current&&!list.Contains(n)&&!streetsByName.ContainsKey(n))list.Add(n);}
   if(stem.Length>0)foreach(var f in stemForms)Add(stem+f);
   foreach(var n in commonNames)Add(n);
   return list;
  }

  // Selected road: its street name, chome, duplicates and rename suggestions.
  object SelectedStreet(Entity road)
  {
   if(!RoadInspector.IsRoad(EntityManager,road))return null;
   var street=StreetOf(road);
   var name=RoadName(road);
   var (c,count)=Chome(road);
   bool custom=false;
   try{custom=names.TryGetCustomName(street,out _);}catch{}
   return new{Name=name,Chome=count>1?ChomeLabel(c):null,Segments=count,SameName=SameNameStreets(road),Custom=custom,
    Suggestions=StreetSuggestions(name),Feedback=UnityEngine.Time.unscaledTime<renameFeedbackUntil?renameFeedback:null};
  }
 }
}
