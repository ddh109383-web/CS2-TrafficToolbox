using Colossal.Mathematics;
using Game.Net;
using SubLane=Game.Net.SubLane;
using ParkingLane=Game.Net.ParkingLane;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
namespace RoadLaneLock
{
 public partial class RoadMarkings
 {
  Material parkingRed,signSilver;
  string parkingLocale;
  static string ParkingLocale()
  {
   var id=(Game.SceneFlow.GameManager.instance?.localizationManager?.activeLocaleId??"en-US").ToLowerInvariant();
   if(id=="zh-cn"||id=="zh-hans"||id=="zh-sg")return "zh-Hans";
   if(id=="zh-tw"||id=="zh-hant"||id=="zh-hk")return "zh-Hant";
   return id.Split('-')[0];
  }
  // Composition coordinates locate the paved edge, not the outside of the sidewalk.
  bool Curb(Drawing drawing,int side,float t,out Vector3 point)
  {
   point=default;float edge=0;bool found=false;
   float width=EntityManager.GetComponentData<NetCompositionData>(drawing.Composition).m_Width;
   foreach(var lane in EntityManager.GetBuffer<NetCompositionLane>(drawing.Composition,true)){
    if((lane.m_Flags&(Game.Prefabs.LaneFlags.Road|Game.Prefabs.LaneFlags.Parking))==0||
       (lane.m_Flags&Game.Prefabs.LaneFlags.Master)!=0||!EntityManager.HasComponent<NetLaneData>(lane.m_Lane))continue;
    float outer=lane.m_Position.x+side*EntityManager.GetComponentData<NetLaneData>(lane.m_Lane).m_Width*.5f;
    if(!found||side*outer>side*edge){edge=outer;found=true;}
   }
   if(!found||width<=0)return false;
   float across=math.saturate(.5f+(edge+side*.22f)/width);
   var section=t<.5f?drawing.Geometry.m_Start:drawing.Geometry.m_End;
   point=MathUtils.Position(MathUtils.Lerp(section.m_Left,section.m_Right,across),t<.5f?t*2:(t-.5f)*2);
   return true;
  }
  internal bool TryStreetCurb(Entity road,int side,float t,out Vector3 point)
  {
   point=default;
   if(!EntityManager.HasComponent<Composition>(road)||!EntityManager.HasComponent<EdgeGeometry>(road))return false;
   var composition=EntityManager.GetComponentData<Composition>(road).m_Edge;
   if(!EntityManager.HasComponent<NetCompositionData>(composition)||!EntityManager.HasBuffer<NetCompositionLane>(composition))return false;
   // Explicitly selected roads don't require an intersection or pedestrian verge.
   return Curb(new Drawing{Composition=composition,Geometry=EntityManager.GetComponentData<EdgeGeometry>(road)},side,t,out point);
  }
  void EnsureSignPlate()
  {
   if(!signSilver)signSilver=SignHardware.Lit(new Color(.62f,.66f,.69f),.45f,.25f);
   if(!charcoal)charcoal=Tint(new Color(.09f,.11f,.12f),.12f);
   if(!plate){plate=new Mesh{name="Sign plate",hideFlags=HideFlags.DontSave,
    vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(-.5f,.5f),new Vector3(.5f,.5f),new Vector3(.5f,-.5f)},triangles=new[]{0,1,2,0,2,3}};plate.RecalculateNormals();plate.RecalculateBounds();}
  }
  void PlaceParkingSigns(Entity road,Drawing drawing)
  {
   if(!drawing.Parking||!EnsureMaterial())return;
   var parking=World.GetExistingSystemManaged<ParkingSystem>();if(parking==null)return;
   bool left=false,right=false;
   var axis=EntityManager.GetComponentData<Curve>(road).m_Bezier;
   Vector3 center=MathUtils.Position(axis,.5f),lateral=Vector3.Cross(Vector3.up,(Vector3)MathUtils.Tangent(axis,.5f)).normalized;
   foreach(var sub in EntityManager.GetBuffer<SubLane>(road,true)){
    var lane=sub.m_SubLane;if(!parking.Eligible(lane,road)||!EntityManager.HasComponent<Curve>(lane))continue;
    Vector3 p=MathUtils.Position(EntityManager.GetComponentData<Curve>(lane).m_Bezier,.5f);
    if(Vector3.Dot(p-center,lateral)<0)left=true;else right=true;
   }
   EnsureSignPlate();if(!parkingRed)parkingRed=Tint(new Color(.9f,.035f,.045f),.08f);
   for(int side=-1;side<=1;side+=2){
    if(side<0?!left:!right)continue;
    float t=side<0?.22f:.78f;
    if(!Curb(drawing,side,t,out var curb))continue;
    byte direction=RoadPickTool.DirectionAt(EntityManager,road,curb);
    Vector3 travel=(Vector3)MathUtils.Tangent(axis,t)*(direction==1?-1:1);
    var rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(travel,Vector3.up),Vector3.up);
    Vector3 face=curb+Vector3.up*2.65f,front=rotation*Vector3.forward;
    if(!drawing.Root)drawing.Root=new GameObject("RoadLaneLock markings"){hideFlags=HideFlags.DontSave};
    SignHardware.Post(drawing.Root,curb,2.7f,signSilver);
    SignHardware.Bracket(drawing.Root,face,rotation,signSilver);
    // Disc and caption carry their artwork in their own faces (no stacked decals to z-fight).
    var disc=ParkingFace("disc",256,256,(u,v)=>{
     var c=Over(new Color(.93f,.93f,.91f),new Color(.09f,.11f,.12f),Mask("Parking-P",u,v));
     return Over(c,new Color(.86f,.05f,.06f),Mask("Parking-ban",u,v));});
    SignHardware.Body(drawing.Root,"Parking disc body",SignHardware.Disc(false),face-front*(SignHardware.MountOffset+SignHardware.BodyDepth*.5f),rotation,new Vector3(.9f,.9f,SignHardware.BodyDepth),disc,signSilver);
    string locale="Parking-"+ParkingLocale();if(!HasResource(locale))locale="Parking-en";
    var text=ParkingFace(locale,512,160,(u,v)=>Over(new Color(.93f,.93f,.91f),new Color(.09f,.11f,.12f),Mask(locale,(u-.5f)*(.95f/.86f)+.5f,(v-.5f)*(.3f/.22f)+.5f)));
    var captionMount=curb+Vector3.up*1.99f;
    SignHardware.Bracket(drawing.Root,captionMount,rotation,signSilver);
    SignHardware.Body(drawing.Root,"Parking caption body",SignHardware.PlateBox(false),captionMount-front*(SignHardware.MountOffset+SignHardware.BodyDepth*.5f),rotation,new Vector3(.95f,.3f,SignHardware.BodyDepth),text,signSilver);
   }
  }
  internal object ParkingDiagnostics(Camera camera)
  {
   var list=new System.Collections.Generic.List<object>();
   foreach(var pair in drawings)if(pair.Value.Parking)list.Add(new {Road=pair.Key.Index,Render=Diagnostics.Render(pair.Value.Root,camera)});
   return new {Signs=list.Count,Faces=parkingFaces.Count,Roads=list};
  }
  readonly System.Collections.Generic.Dictionary<string,Material> parkingFaces=new System.Collections.Generic.Dictionary<string,Material>();
  readonly System.Collections.Generic.Dictionary<string,Texture2D> masks=new System.Collections.Generic.Dictionary<string,Texture2D>();
  Material signWhite;
  static Color Over(Color under,Color ink,float a)=>Color.Lerp(under,ink,a);
  static bool HasResource(string name){using(var s=typeof(RoadMarkings).Assembly.GetManifestResourceStream(name))return s!=null;}
  float Mask(string name,float u,float v)
  {
   if(u<0||u>1||v<0||v>1)return 0;
   if(!masks.TryGetValue(name,out var texture)){
    texture=null;
    using(var stream=typeof(RoadMarkings).Assembly.GetManifestResourceStream(name))if(stream!=null){
     byte[] bytes;using(var reader=new System.IO.BinaryReader(stream))bytes=reader.ReadBytes((int)stream.Length);
     texture=new Texture2D(2,2){hideFlags=HideFlags.DontSave,wrapMode=TextureWrapMode.Clamp};
     if(!ImageConversion.LoadImage(texture,bytes)){Object.Destroy(texture);texture=null;}
    }
    masks[name]=texture;
   }
   return texture?texture.GetPixelBilinear(u,v).a:0;
  }
  Material ParkingFace(string key,int width,int height,System.Func<float,float,Color> paintAt)
  {
   if(!signWhite)signWhite=SignHardware.Lit(new Color(.93f,.93f,.91f),.3f);
   if(parkingFaces.TryGetValue(key,out var cached)&&cached)return cached;
   var pixels=new Color32[width*height];
   for(int y=0;y<height;y++)for(int x=0;x<width;x++)pixels[y*width+x]=paintAt((x+.5f)/width,(y+.5f)/height);
   var material=SignHardware.Lit(Color.white,.3f,0,SignHardware.Texture("Parking "+key,width,height,pixels,System.IO.Path.Combine(Diagnostics.Folder,"RoadLaneLock-face-parking-"+key+".png")));
   parkingFaces[key]=material;
   foreach(var mask in masks.Values)if(mask)Object.Destroy(mask);masks.Clear();
   return material;
  }
 }
}





