using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Colossal.UI.Binding;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.Routes;
using Game.UI;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Color = UnityEngine.Color;
using Material = UnityEngine.Material;
using Mesh = UnityEngine.Mesh;
using MaterialPropertyBlock = UnityEngine.MaterialPropertyBlock;

namespace RoadLaneLock
{
 public partial class RuleUISystem
 {
  sealed class TransitStopRow
  {
   public string Id,Name;
   public int? Waiting;
  }
  sealed class TransitIssue
  {
   public string Kind,Label,Detail;
   public int Level;
  }
  sealed class TransitVehicleRow
  {
   // At: index into Stops of the stop the vehicle is heading for (or boarding at); -1 when off route.
   public string Id,Code,State;
   public int At=-1;
   // Progress: 0..1 of the way from the previous stop to At (straight-line projection); 1 while boarding.
   public float Progress=.5f;
   public bool Boarding;
   public long Load,Capacity;
  }
  sealed class TransitRow
  {
   public string Id,Name,Mode,Color,Kind,Code;
   public int Vehicles;
   public long Load,Capacity;
   public bool Visible,Custom;
   public int? Ticket;
   public int Schedule=2;
   public int[] Fleetsize;
   public readonly List<TransitStopRow> Stops=new List<TransitStopRow>();
   public readonly List<TransitIssue> Issues=new List<TransitIssue>();
   public readonly List<TransitVehicleRow> Fleet=new List<TransitVehicleRow>();
  }
  sealed class VehicleDot
  {
   public Entity Vehicle;
   public Color Color;
   public string Line,Id;
   public bool Full;
  }
  readonly List<VehicleDot> vehicleDots=new List<VehicleDot>();
  // Entities the panel may focus: stops and vehicles of the last refresh.
  readonly HashSet<Entity> focusable=new HashSet<Entity>();
  string highlightedVehicle;
  sealed class Stroke
  {
   public Bezier4x3 Curve;
   public Color Color;
   public string Line;
   public float Slot;
  }
  readonly List<Stroke> strokes=new List<Stroke>();
  readonly List<TransitRow> lines=new List<TransitRow>();
  readonly HashSet<string> hidden=new HashSet<string>();
  readonly Dictionary<string,List<Color>> stationColors=new Dictionary<string,List<Color>>();
  readonly Dictionary<string,float3> stationPositions=new Dictionary<string,float3>();
  sealed class StationQueue
  {
   public int Total;
   public readonly Dictionary<Color,int> ByColor=new Dictionary<Color,int>();
   public void Add(Color color,int count)
   {
    ByColor.TryGetValue(color,out int previous);
    ByColor[color]=previous+count;
    Total+=count;
   }
  }
  readonly Dictionary<string,StationQueue> stationWaiting=new Dictionary<string,StationQueue>();
  EntityQuery lineQuery;
  ValueBinding<string> transitBinding;
  bool transitOpen;
  float refreshAt;
  string lastTransit;
  NameSystem names;
  static string Id(Entity e)=>e.Index+"/"+e.Version;
  string highlightedLine;
  // Map overlay follows the panel's category tab; "*" draws every visible line.
  string activeMode="*";
  EntityQuery transportConfigQuery;
  Entity ticketPolicy;
  Game.UI.InGame.PoliciesUISystem policies;
  ValueBinding<string> transitMetaBinding;
  string lastTransitMeta;
  // Consecutive refreshes (2 s apart) a health condition has held, keyed by line id + kind.
  readonly Dictionary<string,int> issueStreaks=new Dictionary<string,int>();
  const int IssueRefreshes=8;
  void InitTransit()
  {
   names=World.GetOrCreateSystemManaged<NameSystem>();
   policies=World.GetOrCreateSystemManaged<Game.UI.InGame.PoliciesUISystem>();
   lineQuery=GetEntityQuery(new EntityQueryDesc{All=new[]{ComponentType.ReadOnly<TransportLine>(),ComponentType.ReadOnly<PrefabRef>()},
    None=new[]{ComponentType.ReadOnly<Deleted>(),ComponentType.ReadOnly<Game.Tools.Temp>()}});
   transportConfigQuery=GetEntityQuery(ComponentType.ReadOnly<UITransportConfigurationData>());
   AddBinding(transitMetaBinding=new ValueBinding<string>("RoadLaneLock","transitMeta","{}"));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","transitMode",key=>{activeMode=string.IsNullOrEmpty(key)?"*":key;refreshAt=0;}));
   AddBinding(new TriggerBinding<string,int>("RoadLaneLock","setTicketPrice",SetTicketPrice));
   InitNaming();
   InitTransitSettings();
   AddBinding(new TriggerBinding<string>("RoadLaneLock","openTransitLine",id=>{
    if(!TryGetTransitLine(id,out var line))return;
    picker.UseNativeSelection();
    SelectNative(line);
   }));
   AddBinding(new TriggerBinding<string,string>("RoadLaneLock","applyLineColor",ApplyLineColor));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","highlightLine",id=>highlightedLine=id));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","highlightVehicle",id=>highlightedVehicle=id));
   // Same as the vanilla info panel: select (opens the panel) and follow with the orbit camera.
   AddBinding(new TriggerBinding<string>("RoadLaneLock","focusEntity",id=>{
    if(!transitOpen||!TryParseEntity(id,out var target)||!focusable.Contains(target)||!EntityManager.Exists(target)||EntityManager.HasComponent<Deleted>(target))return;
    picker.UseNativeSelection();
    SelectNative(target);
    selectedInfo.Focus(target);
   }));
   AddBinding(transitBinding=new ValueBinding<string>("RoadLaneLock","transit","[]"));
   AddBinding(new TriggerBinding("RoadLaneLock","showTransit",()=>{parkingOpen=false;viewingVehicles=false;monitorOpen=false;picker.End();transitOpen=true;activeMode="*";refreshAt=0;Publish();}));
   AddBinding(new TriggerBinding<string>("RoadLaneLock","visibility",key=>{
    if(!transitOpen)return;
    var group=lines.Where(l=>key=="*"||key==l.Mode||key==l.Id).ToArray();
    bool show=group.Any(l=>hidden.Contains(l.Id));
    foreach(var row in group){if(show)hidden.Remove(row.Id);else hidden.Add(row.Id);}
    refreshAt=0;
   }));
  }
  static bool TryParseEntity(string id,out Entity e)
  {
   e=Entity.Null;
   if(id==null)return false;
   var parts=id.Split('/');
   if(parts.Length!=2||!int.TryParse(parts[0],out var index)||!int.TryParse(parts[1],out var version)||index<=0)return false;
   e=new Entity{Index=index,Version=version};
   return true;
  }
  bool TryGetTransitLine(string id,out Entity line)
  {
   line=Entity.Null;
   if(!Enabled||!transitOpen||!TryParseEntity(id,out line))return false;
   return EntityManager.Exists(line)&&!EntityManager.HasComponent<Deleted>(line)&&EntityManager.HasComponent<TransportLine>(line);
  }
  string VehicleState(Entity vehicle,long load,long capacity,out bool boarding)
  {
   boarding=false;bool returning=false,disabled=false,full=capacity>0&&load*10>=capacity*9;
   if(EntityManager.HasComponent<Game.Vehicles.PublicTransport>(vehicle)){
    var s=EntityManager.GetComponentData<Game.Vehicles.PublicTransport>(vehicle).m_State;
    boarding=(s&Game.Vehicles.PublicTransportFlags.Boarding)!=0;
    returning=(s&(Game.Vehicles.PublicTransportFlags.Returning|Game.Vehicles.PublicTransportFlags.AbandonRoute))!=0;
    disabled=(s&Game.Vehicles.PublicTransportFlags.Disabled)!=0;
    full|=(s&Game.Vehicles.PublicTransportFlags.Full)!=0;
   }else if(EntityManager.HasComponent<Game.Vehicles.CargoTransport>(vehicle)){
    var s=EntityManager.GetComponentData<Game.Vehicles.CargoTransport>(vehicle).m_State;
    boarding=(s&Game.Vehicles.CargoTransportFlags.Boarding)!=0;
    returning=(s&(Game.Vehicles.CargoTransportFlags.Returning|Game.Vehicles.CargoTransportFlags.AbandonRoute))!=0;
    disabled=(s&Game.Vehicles.CargoTransportFlags.Disabled)!=0;
   }
   return disabled?"Disabled":returning?"Returning":full?"Full":boarding?"Boarding":"Moving";
  }
  void ApplyLineColor(string id,string hex)
  {
   if(!TryGetTransitLine(id,out var line)||hex==null||hex.Length!=7||hex[0]!='#'||!UnityEngine.ColorUtility.TryParseHtmlString(hex,out var color)||
      !EntityManager.HasComponent<Game.Routes.Color>(line)||!EntityManager.HasBuffer<RouteWaypoint>(line))return;
   var paint=new Game.Routes.Color(color);
   EntityManager.SetComponentData(line,paint);
   using(var commands=new EntityCommandBuffer(Allocator.Temp)){
    if(EntityManager.HasBuffer<RouteVehicle>(line))
     foreach(var member in EntityManager.GetBuffer<RouteVehicle>(line,true))
      if(EntityManager.Exists(member.m_Vehicle))commands.AddComponent(member.m_Vehicle,paint);
    var change=commands.CreateEntity();commands.AddComponent<Game.Common.Event>(change);
    commands.AddComponent(change,new ColorUpdated(line));commands.Playback(EntityManager);
   }
   World.GetOrCreateSystemManaged<PaletteSystem>().Remember(hex);
   selectedInfo.RequestUpdate();
   refreshAt=0;
  }
  Entity TicketPolicy()
  {
   if(ticketPolicy!=Entity.Null&&EntityManager.Exists(ticketPolicy))return ticketPolicy;
   ticketPolicy=Entity.Null;
   var prefabs=World.GetOrCreateSystemManaged<PrefabSystem>();
   if(!transportConfigQuery.IsEmptyIgnoreFilter&&prefabs.TryGetSingletonPrefab<UITransportConfigurationPrefab>(transportConfigQuery,out var config)&&config.m_TicketPricePolicy!=null)
    ticketPolicy=prefabs.GetEntity(config.m_TicketPricePolicy);
   return ticketPolicy;
  }
  bool TicketSlider(out PolicySliderData slider)
  {
   slider=default;
   var policy=TicketPolicy();
   if(policy==Entity.Null||!EntityManager.HasComponent<PolicySliderData>(policy))return false;
   slider=EntityManager.GetComponentData<PolicySliderData>(policy);
   return true;
  }
  int? ReadTicket(Entity line,TransportLineData data)
  {
   if(data.m_CargoTransport||!data.m_PassengerTransport||!EntityManager.HasBuffer<Game.Policies.Policy>(line))return null;
   var policy=TicketPolicy();
   if(policy==Entity.Null)return null;
   foreach(var p in EntityManager.GetBuffer<Game.Policies.Policy>(line,true))
    if(p.m_Policy==policy)return (p.m_Flags&Game.Policies.PolicyFlags.Active)!=0?(int)math.round(p.m_Adjustment):0;
   return 0;
  }
  // Same call the vanilla ticket slider makes (TicketPriceSection.OnSetTicketPrice).
  void SetTicketPrice(string id,int price)
  {
   if(!TryGetTransitLine(id,out var line)||!TicketSlider(out var slider))return;
   var prefab=EntityManager.GetComponentData<PrefabRef>(line).m_Prefab;
   if(!EntityManager.HasComponent<TransportLineData>(prefab))return;
   var data=EntityManager.GetComponentData<TransportLineData>(prefab);
   if(data.m_CargoTransport||!data.m_PassengerTransport||!EntityManager.HasBuffer<Game.Policies.Policy>(line))return;
   float value=math.clamp(price,slider.m_Range.min,slider.m_Range.max);
   // Vanilla: 0 means the policy is switched off (free); the stored adjustment stays clamped to the slider range.
   policies.SetPolicy(line,TicketPolicy(),price>0,value);
   selectedInfo.RequestUpdate();
   refreshAt=0;
  }
  void Streak(TransitRow row,string kind,bool holds,int level,string label,string detail)
  {
   string key=row.Id+"|"+kind;
   if(!holds){issueStreaks.Remove(key);return;}
   issueStreaks.TryGetValue(key,out int count);
   issueStreaks[key]=++count;
   if(count>=IssueRefreshes)row.Issues.Add(new TransitIssue{Kind=kind,Level=level,Label=label,Detail=detail});
  }
  void CheckHealth(TransitRow row,Entity line,TransportLineData data,List<Entity> waypoints)
  {
   // Bunching: several vehicles heading for the same stop although the line has room to spread them.
   var heading=new Dictionary<int,int>();
   int tracked=0;
   if(EntityManager.HasBuffer<RouteVehicle>(line)&&waypoints.Count>1){
    var index=new Dictionary<Entity,int>();
    for(int i=0;i<waypoints.Count;i++)index[waypoints[i]]=i;
    foreach(var member in EntityManager.GetBuffer<RouteVehicle>(line,true)){
     if(!EntityManager.HasComponent<Target>(member.m_Vehicle))continue;
     if(!index.TryGetValue(EntityManager.GetComponentData<Target>(member.m_Vehicle).m_Target,out int at))continue;
     heading.TryGetValue(at,out int n);heading[at]=n+1;tracked++;
    }
   }
   int worstAt=-1,worstCount=0;
   foreach(var pair in heading)if(pair.Value>worstCount){worstCount=pair.Value;worstAt=pair.Key;}
   bool bunched=tracked>=2&&waypoints.Count>=tracked*2&&worstCount>=2;
   string stopName=worstAt>=0&&worstAt<row.Stops.Count?StopLabel(row,worstAt):"同一站";
   Streak(row,"bunch",bunched,worstCount>=3||worstCount*2>=tracked&&tracked>=4?2:1,"串车",worstCount+" 辆车同时驶向"+stopName);
   if(data.m_PassengerTransport){
    long perVehicle=row.Vehicles>0&&row.Capacity>0?row.Capacity/row.Vehicles:50;
    long threshold=math.max(30,perVehicle);
    TransitStopRow worst=null;
    foreach(var stop in row.Stops)if(stop.Waiting.HasValue&&(worst==null||stop.Waiting>worst.Waiting))worst=stop;
    bool backlog=worst!=null&&worst.Waiting>=threshold;
    Streak(row,"backlog",backlog,backlog&&worst.Waiting>=threshold*2?2:1,"积压",backlog?(string.IsNullOrEmpty(worst.Name)?"某站":worst.Name)+" 候车 "+worst.Waiting+" 人":null);
   }
   double rate=row.Capacity>0?(double)row.Load/row.Capacity:-1;
   Streak(row,"empty",row.Vehicles>0&&rate>=0&&rate<.05,1,"空跑","满载率持续低于 5%");
   Streak(row,"full",row.Vehicles>0&&rate>.9,rate>=.98?2:1,"超载","满载率持续高于 90%");
  }
  static string StopLabel(TransitRow row,int index)=>string.IsNullOrEmpty(row.Stops[index].Name)?"第 "+(index+1)+" 站":row.Stops[index].Name;
  void PublishTransitMeta()
  {
   bool has=TicketSlider(out var slider);
   var json=JsonConvert.SerializeObject(new{TicketMin=0,TicketMax=has?slider.m_Range.max:0,TicketStep=has&&slider.m_Step>0?slider.m_Step:1,TicketReady=has,
    Auto=naming.Auto,Prefix=naming.Prefix,Preview=NamingPreview()});
   if(json!=lastTransitMeta){lastTransitMeta=json;transitMetaBinding.Update(json);}
  }
  void RefreshTransit()
  {
   lines.Clear();strokes.Clear();stationPositions.Clear();stationColors.Clear();stationWaiting.Clear();vehicleDots.Clear();focusable.Clear();
   var countedQueues=new HashSet<Entity>();
   var shared=new Dictionary<(int3,int3),List<Stroke>>();
   using(var entities=lineQuery.ToEntityArray(Allocator.Temp))
   foreach(var e in entities)
   {
    var prefab=EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
    if(!EntityManager.HasComponent<TransportLineData>(prefab))continue;
    var data=EntityManager.GetComponentData<TransportLineData>(prefab);
    Color color=EntityManager.HasComponent<Game.Routes.Color>(e)?(Color)EntityManager.GetComponentData<Game.Routes.Color>(e).m_Color:Color.white;
    color.a=1;
    var row=new TransitRow{Id=Id(e),Name=names.GetRenderedLabelName(e),Mode=data.m_CargoTransport&&!data.m_PassengerTransport?"Cargo":data.m_TransportType.ToString(),
     Color="#"+UnityEngine.ColorUtility.ToHtmlStringRGB(color),Visible=!hidden.Contains(Id(e)),Vehicles=EntityManager.HasBuffer<RouteVehicle>(e)?EntityManager.GetBuffer<RouteVehicle>(e,true).Length:0,
     Kind=NameKey(data),Custom=names.TryGetCustomName(e,out _),Ticket=ReadTicket(e,data)};
    row.Code=BadgeCode(row.Name,row.Kind);
    row.Schedule=ReadSchedule(e);
    try{row.Fleetsize=VehicleCounts(e,data);}catch(Exception ex){Mod.Log.Warn("车辆数量读取失败："+ex.Message);}
    bool drawn=row.Visible&&(activeMode=="*"||activeMode==row.Mode);
    var waypoints=new List<Entity>();var waypointPos=new List<float3?>();
    lines.Add(row);
    if(EntityManager.HasBuffer<RouteWaypoint>(e))
    foreach(var w in EntityManager.GetBuffer<RouteWaypoint>(e,true)){
     if(!EntityManager.HasComponent<Connected>(w.m_Waypoint))continue;
     var stop=EntityManager.GetComponentData<Connected>(w.m_Waypoint).m_Connected;
     if(!EntityManager.Exists(stop))continue;
     waypoints.Add(w.m_Waypoint);
     waypointPos.Add(EntityManager.HasComponent<Game.Objects.Transform>(stop)?EntityManager.GetComponentData<Game.Objects.Transform>(stop).m_Position:(float3?)null);
     focusable.Add(stop);
     var sid=Id(stop);
     // Vanilla LineVisualizerSection reads the queue from the line waypoint.
     int? waiting=EntityManager.HasComponent<WaitingPassengers>(w.m_Waypoint)?EntityManager.GetComponentData<WaitingPassengers>(w.m_Waypoint).m_Count:(int?)null;
     row.Stops.Add(new TransitStopRow{Id=sid,Name=names.GetRenderedLabelName(stop),Waiting=waiting});
     if(drawn&&EntityManager.HasComponent<Game.Objects.Transform>(stop)){
      if(data.m_PassengerTransport&&waiting>0&&countedQueues.Add(w.m_Waypoint)){
       if(!stationWaiting.TryGetValue(sid,out var queue))stationWaiting[sid]=queue=new StationQueue();
       queue.Add(color,waiting.Value);
      }
      stationPositions[sid]=EntityManager.GetComponentData<Game.Objects.Transform>(stop).m_Position;
      if(!stationColors.TryGetValue(sid,out var colors))stationColors[sid]=colors=new List<Color>();
      if(!colors.Contains(color))colors.Add(color);
     }
    }
    if(EntityManager.HasBuffer<RouteVehicle>(e)){
     var seen=new HashSet<Entity>();
     var index=new Dictionary<Entity,int>();
     for(int i=0;i<waypoints.Count;i++)index[waypoints[i]]=i;
     bool cargo=row.Mode=="Cargo";
     foreach(var member in EntityManager.GetBuffer<RouteVehicle>(e,true)){
      var vehicle=member.m_Vehicle;
      if(!EntityManager.Exists(vehicle)||EntityManager.HasComponent<Deleted>(vehicle))continue;
      long load=0,capacity=0;
      AccumulateVehicle(vehicle,cargo,seen,ref load,ref capacity);
      if(EntityManager.HasBuffer<Game.Vehicles.LayoutElement>(vehicle))
       foreach(var part in EntityManager.GetBuffer<Game.Vehicles.LayoutElement>(vehicle,true))
        AccumulateVehicle(part.m_Vehicle,cargo,seen,ref load,ref capacity);
      row.Load+=load;row.Capacity+=capacity;
      var state=VehicleState(vehicle,load,capacity,out bool boarding);
      int at=-1;
      if(state!="Returning"&&state!="Disabled"&&EntityManager.HasComponent<Target>(vehicle)&&index.TryGetValue(EntityManager.GetComponentData<Target>(vehicle).m_Target,out int found))at=found;
      float progress=boarding?1f:.5f;
      if(at>=0&&!boarding&&waypoints.Count>1&&EntityManager.HasComponent<Game.Objects.Transform>(vehicle))
      {
       var a=waypointPos[(at-1+waypoints.Count)%waypoints.Count];var b=waypointPos[at];
       if(a.HasValue&&b.HasValue){var ab=b.Value-a.Value;float len2=math.lengthsq(ab.xz);
        if(len2>1f)progress=math.clamp(math.dot((EntityManager.GetComponentData<Game.Objects.Transform>(vehicle).m_Position-a.Value).xz,ab.xz)/len2,.05f,.95f);}
      }
      row.Fleet.Add(new TransitVehicleRow{Id=Id(vehicle),Code="#"+vehicle.Index,State=state,At=at,Progress=progress,Boarding=boarding,Load=load,Capacity=capacity});
      focusable.Add(vehicle);
      if(drawn&&at>=0)vehicleDots.Add(new VehicleDot{Vehicle=vehicle,Color=color,Line=row.Id,Id=Id(vehicle),Full=state=="Full"});
     }
    }
    CheckHealth(row,e,data,waypoints);
    if(!drawn||!EntityManager.HasBuffer<RouteSegment>(e))continue;
    foreach(var seg in EntityManager.GetBuffer<RouteSegment>(e,true)){
     if(!EntityManager.HasBuffer<CurveElement>(seg.m_Segment))continue;
     foreach(var el in EntityManager.GetBuffer<CurveElement>(seg.m_Segment,true)){
      var c=el.m_Curve;
      var start=(int3)math.round(c.a);var end=(int3)math.round(c.d);
      if(start.Equals(end))continue;
      bool reverse=start.x>end.x||(start.x==end.x&&(start.z>end.z||(start.z==end.z&&start.y>end.y)));
      if(reverse)c=new Bezier4x3(c.d,c.c,c.b,c.a);
      var key=reverse?(end,start):(start,end);
      var stroke=new Stroke{Line=row.Id,Color=color,Curve=c};
      if(!shared.TryGetValue(key,out var group))shared[key]=group=new List<Stroke>();
      group.Add(stroke);strokes.Add(stroke);
     }
    }
   }
   foreach(var group in shared.Values){
    if(group.Count<2)continue;
    var ids=group.Select(s=>s.Line).Distinct().OrderBy(id=>id,StringComparer.Ordinal).ToList();
    foreach(var s in group)s.Slot=ids.IndexOf(s.Line)-(ids.Count-1)*0.5f;
   }
   string json=JsonConvert.SerializeObject(lines.OrderBy(l=>l.Name));
   if(json!=lastTransit){transitBinding.Update(json);lastTransit=json;}
   if(modelsLine!=null)PublishModels();
   PublishTransitMeta();
  }
  // ---- Station heat columns: real 3D pillars, one stacked layer per line sized by its share of the waiting passengers. ----
  static Mesh columnMesh;
  static Material columnMaterial;
  static MaterialPropertyBlock columnProps;
  static bool columnFailed;
  static Mesh ColumnMesh()
  {
   if(columnMesh)return columnMesh;
   // Unit cylinder: radius 0.5, base at y=0, height 1, with a top cap.
   const int n=24;var v=new List<UnityEngine.Vector3>();var tri=new List<int>();
   for(int i=0;i<=n;i++){float a=i*2*math.PI/n;var d=new UnityEngine.Vector3(math.cos(a)*.5f,0,math.sin(a)*.5f);v.Add(d);v.Add(d+UnityEngine.Vector3.up);}
   for(int i=0;i<n;i++){int b=i*2;tri.AddRange(new[]{b,b+1,b+2,b+2,b+1,b+3});}
   int top=v.Count;v.Add(UnityEngine.Vector3.up);
   for(int i=0;i<n;i++)tri.AddRange(new[]{top,i*2+3,i*2+1});
   // Both windings: visible from any side whatever the shader culling.
   for(int i=tri.Count-3;i>=0;i-=3)tri.AddRange(new[]{tri[i],tri[i+2],tri[i+1]});
   columnMesh=new Mesh{name="RoadLaneLock column",hideFlags=UnityEngine.HideFlags.DontSave};
   columnMesh.SetVertices(v);columnMesh.SetTriangles(tri,0);columnMesh.RecalculateNormals();columnMesh.RecalculateBounds();
   return columnMesh;
  }
  static Material ColumnMaterial()
  {
   if(columnMaterial||columnFailed)return columnMaterial;
   try
   {
    // Unlit so the line colours read the same by day and by night.
    var shader=UnityEngine.Shader.Find("HDRP/Unlit");
    if(shader){columnMaterial=new Material(shader){hideFlags=UnityEngine.HideFlags.DontSave,name="RoadLaneLock column"};columnMaterial.SetColor("_UnlitColor",Color.white);SignHardware.Validate(columnMaterial);}
    else columnMaterial=SignHardware.Lit(Color.white,.2f);
    columnProps=new MaterialPropertyBlock();
   }
   catch(Exception ex){columnFailed=true;Mod.Log.Warn("热力柱材质失败："+ex.Message);}
   return columnMaterial;
  }
  void DrawColumn(float3 at,List<Color> colors,StationQueue queue,float pixel,float zoom)
  {
   var mesh=ColumnMesh();var material=ColumnMaterial();
   if(!material||queue.Total<=0)return;
   float share=queue.Total/(queue.Total+40f);
   // World size with a screen-size floor: a proper pillar up close, still readable from far away.
   float height=math.max(6f+34f*share,pixel*(36f+150f*share)*math.lerp(1f,1.4f,zoom));
   float radius=math.max(1.6f,pixel*math.lerp(5f,9f,zoom));
   string colorProperty=material.HasProperty("_UnlitColor")?"_UnlitColor":"_BaseColor";
   float y=at.y;
   void Layer(Color color,float h,float r)
   {
    columnProps.SetColor(colorProperty,color);
    var matrix=UnityEngine.Matrix4x4.TRS(new UnityEngine.Vector3(at.x,y,at.z),UnityEngine.Quaternion.identity,new UnityEngine.Vector3(r,h,r));
    UnityEngine.Graphics.DrawMesh(mesh,matrix,material,0,null,0,columnProps,UnityEngine.Rendering.ShadowCastingMode.Off,false);
    y+=h;
   }
   foreach(var color in colors)
   {
    if(!queue.ByColor.TryGetValue(color,out var count)||count<=0)continue;
    Layer(color,height*count/queue.Total,radius*2f);
   }
   Layer(Color.white,math.max(.4f,radius*.35f),radius*2.15f);
  }
  void AccumulateVehicle(Entity vehicle,bool cargo,HashSet<Entity> seen,ref long load,ref long capacity)
  {
   if(!seen.Add(vehicle)||!EntityManager.HasComponent<PrefabRef>(vehicle)||EntityManager.HasComponent<Deleted>(vehicle))return;
   var prefab=EntityManager.GetComponentData<PrefabRef>(vehicle).m_Prefab;
   if(cargo){
    if(!EntityManager.HasComponent<CargoTransportVehicleData>(prefab))return;
    capacity+=EntityManager.GetComponentData<CargoTransportVehicleData>(prefab).m_CargoCapacity;
    if(EntityManager.HasBuffer<Game.Economy.Resources>(vehicle))
     foreach(var resource in EntityManager.GetBuffer<Game.Economy.Resources>(vehicle,true))load+=resource.m_Amount;
   }else{
    if(!EntityManager.HasComponent<PublicTransportVehicleData>(prefab))return;
    capacity+=EntityManager.GetComponentData<PublicTransportVehicleData>(prefab).m_PassengerCapacity;
    if(EntityManager.HasBuffer<Game.Vehicles.Passenger>(vehicle))load+=EntityManager.GetBuffer<Game.Vehicles.Passenger>(vehicle,true).Length;
   }
  }
  void DrawTransit()
  {
   if(!transitOpen)return;
   float now=UnityEngine.Time.unscaledTime;
   if(now>=refreshAt){refreshAt=now+2f;RefreshTransit();}
   var camera=World.GetExistingSystemManaged<CameraUpdateSystem>()?.activeCamera;
   if(camera==null)return;
   var buffer=World.GetOrCreateSystemManaged<OverlayRenderSystem>().GetBuffer(out var writer);writer.Complete();
   float3 eye=camera.transform.position;
   float scale=2f*math.tan(math.radians(camera.fieldOfView)*0.5f)/math.max(1,camera.pixelHeight);
   float3 forward=camera.transform.forward;
   float orthoPixel=camera.orthographic?2f*camera.orthographicSize/math.max(1,camera.pixelHeight):0f;
   float PixelAt(float3 position)=>orthoPixel>0?orthoPixel:math.max(.015f,math.dot(position-eye,forward)*scale);
   float ZoomAt(float pixel)=>math.smoothstep(160f,2300f,pixel*camera.pixelHeight);
   foreach(var stroke in strokes){
    var c=stroke.Curve;
    float pixel=PixelAt((c.a+c.d)*.5f);
    float width=math.max(1.3f,pixel*math.lerp(4f,8f,ZoomAt(pixel)));
    float3 offset=math.normalizesafe(math.cross(new float3(0,1,0),c.d-c.a))*width*stroke.Slot;
    c.a+=offset;c.b+=offset;c.c+=offset;c.d+=offset;
    buffer.DrawCurve(stroke.Color,c,stroke.Line==highlightedLine?width*1.8f:width);
   }
   float3 screenUp=camera.transform.up;
   foreach(var pair in stationPositions){
    var colors=stationColors[pair.Key];
    float pixel=PixelAt(pair.Value);
    float zoom=ZoomAt(pixel);
    float step=math.max(.12f,pixel*math.lerp(2f,2.8f,zoom)*math.lerp(.5f,1f,zoom));
    stationWaiting.TryGetValue(pair.Key,out var queue);
    if(queue!=null){
     float intensity=queue.Total/(queue.Total+32f);
     float diameter=step*(10f+colors.Count+18f*intensity);
     // Concentric bands keep the visible line colors distinct at shared stops.
     float inner=step*(4+colors.Count);
     int remaining=queue.ByColor.Count;
     foreach(var color in colors){
      if(!queue.ByColor.ContainsKey(color))continue;
      buffer.DrawCircle(color,pair.Value,inner+(diameter-inner)*remaining/queue.ByColor.Count);
      remaining--;
     }
    }
    for(int i=0;i<colors.Count;i++)buffer.DrawCircle(colors[i],pair.Value,step*(4+colors.Count-i));
    buffer.DrawCircle(Color.white,pair.Value,step*3);
    if(queue!=null)DrawColumn(pair.Value,colors,queue,pixel,zoom);
   }
   // Running vehicles, read live each frame so the dots move with the traffic.
   var amber=new Color(.95f,.75f,.45f,1f);var mint=new Color(.51f,.89f,.75f,1f);
   foreach(var dot in vehicleDots){
    if(!EntityManager.Exists(dot.Vehicle)||!EntityManager.HasComponent<Game.Objects.Transform>(dot.Vehicle))continue;
    var position=EntityManager.GetComponentData<Game.Objects.Transform>(dot.Vehicle).m_Position+new float3(0,1.5f,0);
    float pixel=PixelAt(position);
    float step=math.max(.8f,pixel*math.lerp(1.6f,2.3f,ZoomAt(pixel)));
    if(dot.Line==highlightedLine)step*=1.5f;
    if(dot.Id==highlightedVehicle)buffer.DrawCircle(mint,position,step*9f);
    if(dot.Full)buffer.DrawCircle(amber,position,step*6.2f);
    buffer.DrawCircle(Color.white,position,step*5f);
    buffer.DrawCircle(dot.Color,position,step*3.6f);
   }
  }
 }
}


