using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoadLaneLock
{
 public partial class RoadMarkings
 {
  readonly Dictionary<string,Mesh> stencils=new Dictionary<string,Mesh>();
  Material charcoal,amber,litAmber;
  Renderer litSign;
  string legend;

  // Road paint legend per game language. Columns x Rows is the cell grid in the artwork, read row by row;
  // Ratio is a cell's length along the lane relative to its width. Artwork for languages other than
  // en/zh comes from tools/paint-legends.ps1. Unknown languages use English.
  internal sealed class Legend{public string Name,Text;public int Columns=1,Rows;public float Ratio;}
  internal static readonly Dictionary<string,Legend> Legends=new Dictionary<string,Legend>{
   {"zh-hans",new Legend{Name="Paint-zh-Hans",Text="禁止变道",Columns=2,Rows=2,Ratio=1.02f}},
   {"zh-hant",new Legend{Name="Paint-zh-Hant",Text="禁止變道",Columns=2,Rows=2,Ratio=1.02f}},
   {"en",new Legend{Name="Paint-en",Text="NO LANE CHANGE",Rows=3,Ratio=.64f}},
   {"ja",new Legend{Name="Paint-ja",Text="車線変更禁止",Rows=6,Ratio=1.02f}},
   {"ko",new Legend{Name="Paint-ko",Text="차로변경금지",Rows=6,Ratio=1.02f}},
   {"de",new Legend{Name="Paint-de",Text="KEIN SPURWECHSEL",Rows=2,Ratio=.7f}},
   {"fr",new Legend{Name="Paint-fr",Text="CHANGEMENT DE VOIE INTERDIT",Rows=3,Ratio=.64f}},
   {"es",new Legend{Name="Paint-es",Text="NO CAMBIAR CARRIL",Rows=3,Ratio=.64f}},
   {"it",new Legend{Name="Paint-it",Text="VIETATO CAMBIARE CORSIA",Rows=3,Ratio=.64f}},
   {"pt",new Legend{Name="Paint-pt",Text="PROIBIDO MUDAR DE FAIXA",Rows=3,Ratio=.64f}},
   {"pl",new Legend{Name="Paint-pl",Text="ZAKAZ ZMIANY PASA",Rows=3,Ratio=.64f}},
   {"ru",new Legend{Name="Paint-ru",Text="ПЕРЕСТРОЕНИЕ ЗАПРЕЩЕНО",Rows=2,Ratio=.75f}},
  };
  internal static Legend LegendFor(string locale)
  {
   var id=(locale??"").ToLowerInvariant();
   if(id=="zh-cn"||id=="zh-hans"||id=="zh-sg")return Legends["zh-hans"];
   if(id=="zh-tw"||id=="zh-hk"||id=="zh-hant")return Legends["zh-hant"];
   var prefix=id.Length>=2?id.Substring(0,2):id;
   return Legends.TryGetValue(prefix,out var legend)?legend:Legends["en"];
  }
  static Legend current;
  static string LegendForLocale(){current=LegendFor(L.GameLocale());return current.Name;}

  // Turn the opaque runs of a small artwork into one shared, double-sided stencil.
  // This uses the same lit paint as the divider, without depending on a UI font or decal shader.
  Mesh Stencil(string name,int cell=-1)
  {
   string key=cell<0?name:name+"/"+cell;
   if(stencils.TryGetValue(key,out var existing))return existing;
   var vertices=new List<Vector3>();var indices=new List<int>();
   using(var stream=typeof(RoadMarkings).Assembly.GetManifestResourceStream(name))
   {
    if(stream==null)return null;
    byte[] bytes;
    using(var reader=new System.IO.BinaryReader(stream))bytes=reader.ReadBytes((int)stream.Length);
    var texture=new Texture2D(2,2);
    try{
     if(!ImageConversion.LoadImage(texture,bytes))return null;
     var pixels=texture.GetPixels32();int w=texture.width,h=texture.height;
     var layout=current!=null&&current.Name==name?current:null;
     int columns=cell<0||layout==null?1:layout.Columns;
     int rows=cell<0||layout==null?1:layout.Rows;
     int column=cell<0?0:cell%columns,row=cell<0?0:cell/columns;
     int x0=w*column/columns,x1=w*(column+1)/columns;
     int y0=h-h*(row+1)/rows,y1=h-h*row/rows;
     for(int y=y0;y<y1;y++)for(int x=x0;x<x1;)
     {
      if(pixels[y*w+x].a<100){x++;continue;}
      int left=x++;
      while(x<x1&&pixels[y*w+x].a>=100)x++;
      float l=(float)(left-x0)/(x1-x0)-.5f,r=(float)(x-x0)/(x1-x0)-.5f;
      float b=(float)(y-y0)/(y1-y0)-.5f,t=(float)(y+1-y0)/(y1-y0)-.5f;
      int i=vertices.Count;
      vertices.Add(new Vector3(l,b,0));vertices.Add(new Vector3(l,t,0));
      vertices.Add(new Vector3(r,t,0));vertices.Add(new Vector3(r,b,0));
      indices.Add(i);indices.Add(i+1);indices.Add(i+2);indices.Add(i);indices.Add(i+2);indices.Add(i+3);
     }
    }finally{Object.Destroy(texture);}
   }
   if(cell>=0 && vertices.Count>0)
   {
    Vector3 low=vertices[0],high=low;
    foreach(var vertex in vertices){low=Vector3.Min(low,vertex);high=Vector3.Max(high,vertex);}
    Vector3 inkCenter=(low+high)*.5f;
    for(int i=0;i<vertices.Count;i++)vertices[i]-=inkCenter;
   }
   var mesh=new Mesh{name=name,hideFlags=HideFlags.DontSave};
   mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();stencils.Add(key,mesh);return mesh;
  }

  static GameObject Part(Drawing drawing,string name)
  {
   if(!drawing.Root)drawing.Root=new GameObject("RoadLaneLock markings"){hideFlags=HideFlags.DontSave};
   var part=new GameObject(name){hideFlags=HideFlags.DontSave};
   part.transform.SetParent(drawing.Root.transform,false);return part;
  }

  Renderer Stamp(Drawing drawing,string name,Mesh mesh,Vector3 position,Quaternion rotation,Vector3 scale,Material material)
  {
   var part=Part(drawing,name);part.transform.SetPositionAndRotation(position,rotation);
   part.transform.localScale=scale;part.AddComponent<MeshFilter>().sharedMesh=mesh;
   var renderer=part.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
   renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return renderer;
  }

  Material Tint(Color color,float glow)
  {
   var result=new Material(paint){hideFlags=HideFlags.DontSave};
   result.SetColor("_BaseColor",color);result.SetColor("_EmissiveColor",color*glow);return SignHardware.Validate(result);
  }
  // Use the same rendered road cross-sections as the divider. The navigation
  // curve may be split, off-centre or below a banked carriageway.
  void PaintLane(Entity road,Drawing drawing,NetCompositionLane lane,float roadWidth)
  {
   const Game.Prefabs.LaneFlags skip=Game.Prefabs.LaneFlags.Parking|Game.Prefabs.LaneFlags.BicyclesOnly|Game.Prefabs.LaneFlags.Track|Game.Prefabs.LaneFlags.Twoway;
   if(legend==null||(lane.m_Flags&skip)!=0||!EntityManager.HasComponent<NetLaneData>(lane.m_Lane))return;
   float width=EntityManager.GetComponentData<NetLaneData>(lane.m_Lane).m_Width;
   float across=.5f+lane.m_Position.x/roadWidth;
   var start=drawing.Geometry.m_Start;
   var end=drawing.Geometry.m_End;
   var first=MathUtils.Lerp(start.m_Left,start.m_Right,across);
   var last=MathUtils.Lerp(end.m_Left,end.m_Right,across);
   byte direction=RoadPickTool.DirectionAt(EntityManager,road,MathUtils.Position(first,.5f));
   if(direction==0){drawing.WaitingForLanes=true;return;}
   if((drawing.Directions&direction)==0||width<=0||!EnsureMaterial())return;

   // Arc-length lookup across both halves, including short approaches.
   var distances=new List<float>{0};
   var parameters=new List<float>{0};
   float3 previous=first.a;
   for(int half=0;half<2;half++){
    var curve=half==0?first:last;
    int samples=math.clamp((int)math.ceil((math.distance(curve.a,curve.b)+math.distance(curve.b,curve.c)+math.distance(curve.c,curve.d))/2f),4,1024);
    for(int j=1;j<=samples;j++){
     float t=(float)j/samples;
     float3 point=MathUtils.Position(curve,t);
     distances.Add(distances[distances.Count-1]+math.distance(previous,point));
     parameters.Add(half+t);previous=point;
    }
   }
   float length=distances[distances.Count-1];
   if(current==null||current.Name!=legend)return;
   int cells=current.Columns*current.Rows;
   float ratio=current.Ratio;
   // Letter size follows the lane width only; a lane too short for one whole legend gets no text.
   float inkWidth=math.min(width*.90f,3.8f);
   if(inkWidth<=0.01f)return;
   float step=inkWidth*ratio;
   float group=step*cells;
   if(length<group*1.25f)return;
   float travel=direction==2?1f:-1f;
   int repeats=math.clamp((int)math.ceil(length/95f),1,math.max(1,(int)(length/(group*1.6f))));
   for(int band=0;band<repeats;band++){
    for(int i=0;i<cells;i++){
     float meters=length*(band+.5f)/repeats+travel*(i-(cells-1)*.5f)*step;
     int hi=distances.BinarySearch(meters);
     if(hi<0)hi=~hi;
     hi=math.clamp(hi,1,distances.Count-1);
     float u=math.lerp(parameters[hi-1],parameters[hi],(meters-distances[hi-1])/math.max(.0001f,distances[hi]-distances[hi-1]));
     var section=u<1?start:end;
     var axis=u<1?first:last;
     float t=math.saturate(u<1?u:u-1);
     Vector3 forward=((Vector3)MathUtils.Tangent(axis,t)).normalized*travel;
     Vector3 lateral=MathUtils.Position(section.m_Right,t)-MathUtils.Position(section.m_Left,t);
     Vector3 normal=Vector3.Cross(forward,lateral).normalized;
     if(normal.y<0)normal=-normal;
     if(normal.y<=0)continue;
     Vector3 point=MathUtils.Position(axis,t);
     point.y+=lane.m_Position.y;
     var glyph=Stencil(legend,i);
     if(glyph)Stamp(drawing,legend,glyph,point+normal*.09f,Quaternion.LookRotation(-normal,forward),new Vector3(inkWidth,step,1),paint);
    }
   }
  }
  Mesh plate;
  void PlaceMarker(Entity road,Drawing drawing,byte direction)
  {
   bool monitor=(drawing.Monitor&direction)!=0;

   if(!monitor||!EnsureMaterial())return;
   bool leftTraffic=World.GetOrCreateSystemManaged<Game.City.CityConfigurationSystem>().leftHandTraffic;
   float edgeT=direction==1?.27f:.73f;
   float best=float.NegativeInfinity,along=0;
   Curve chosen=default;bool found=false;
   foreach(var sub in EntityManager.GetBuffer<Game.Net.SubLane>(road,true)){
    var lane=sub.m_SubLane;
    if(RoadPickTool.LaneDirection(EntityManager,lane)!=direction||!EntityManager.HasComponent<PrefabRef>(lane))continue;
    var prefab=EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
    if(!EntityManager.HasComponent<NetLaneData>(prefab))continue;
    var data=EntityManager.GetComponentData<NetLaneData>(prefab);
    if((data.m_Flags&(Game.Prefabs.LaneFlags.Parking|Game.Prefabs.LaneFlags.BicyclesOnly|Game.Prefabs.LaneFlags.Track))!=0)continue;
    var delta=EntityManager.GetComponentData<EdgeLane>(lane).m_EdgeDelta;
    var curve=EntityManager.GetComponentData<Curve>(lane);


    float t=(edgeT-delta.x)/(delta.y-delta.x);
    if(t<=0||t>=1)continue;
    Vector3 forward=MathUtils.Tangent(curve.m_Bezier,t);
    var curbSide=Vector3.Cross(Vector3.up,forward).normalized*(leftTraffic?-1:1);
    Vector3 center=MathUtils.Position(curve.m_Bezier,t);
    Vector3 roadCenter=MathUtils.Position(EntityManager.GetComponentData<Curve>(road).m_Bezier,edgeT);
    float score=Vector3.Dot(center-roadCenter,curbSide);
    if(score<=best)continue;
    best=score;chosen=curve;along=t;found=true;
   }
   if(!found)return;
   var line=chosen.m_Bezier;
   Vector3 point=MathUtils.Position(line,along),travel=((Vector3)MathUtils.Tangent(line,along)).normalized;
   Vector3 right=Vector3.Cross(Vector3.up,travel).normalized;
   if(right.sqrMagnitude<.5f)return;
   var side=right*(leftTraffic?-1:1);
   Vector3 axisRight=Vector3.Cross(Vector3.up,(Vector3)MathUtils.Tangent(EntityManager.GetComponentData<Curve>(road).m_Bezier,edgeT)).normalized;
   int curbSignSide=Vector3.Dot(side,axisRight)<0?-1:1;
   if(!Curb(drawing,curbSignSide,edgeT,out var curb))return;
   EnsureSignPlate();
   if(!amber){amber=Tint(new Color(1f,.51f,.07f),.45f);litAmber=Tint(new Color(1f,.76f,.24f),1.2f);}
   var facing=Quaternion.LookRotation(Vector3.ProjectOnPlane(travel,Vector3.up),Vector3.up);
   var face=curb+Vector3.up*2.65f;
   if(!drawing.Root)drawing.Root=new GameObject("RoadLaneLock markings"){hideFlags=HideFlags.DontSave};
   SignHardware.Post(drawing.Root,curb,2.7f,signSilver);
   SignHardware.Bracket(drawing.Root,face,facing,signSilver);
   face-=facing*Vector3.forward*SignHardware.MountOffset;
   SignHardware.Solid(drawing.Root,"Monitor sign body",PrimitiveType.Cube,face,facing,new Vector3(.9f,.9f,SignHardware.BodyDepth),charcoal);
   face-=facing*Vector3.forward*(SignHardware.BodyDepth*.5f);
   var icon=Stencil("Monitor-sign");
   if(icon)drawing.Signs[direction-1]=Stamp(drawing,"Detour",icon,face-facing*Vector3.forward*.003f,facing,Vector3.one*.8f,amber);
  }
  internal void PreviewSign(Entity road,byte direction)
  {
   Renderer next=null;
   if(direction>=1&&direction<=2&&drawings.TryGetValue(road,out var drawing))next=drawing.Signs[direction-1];
   if(next==litSign)return;
   if(litSign)litSign.sharedMaterial=amber;
   litSign=next;if(litSign)litSign.sharedMaterial=litAmber;
  }
  void DisposeMarkers()
  {
   foreach(var mesh in stencils.Values)Object.Destroy(mesh);stencils.Clear();
   foreach(var resource in new Object[]{plate,charcoal,amber,litAmber,parkingRed,signSilver,signWhite})if(resource)Object.Destroy(resource);
   foreach(var resource in parkingFaces.Values)if(resource){Object.Destroy(resource.GetTexture("_BaseColorMap"));Object.Destroy(resource);}
   parkingFaces.Clear();SignHardware.Dispose();
  }
 }
}




