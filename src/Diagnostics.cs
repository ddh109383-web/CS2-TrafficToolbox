using System;
using System.Collections.Generic;
using System.IO;
using Game;
using Newtonsoft.Json;
using UnityEngine;
namespace RoadLaneLock
{
 // Machine-readable diagnostics: a small state snapshot rewritten every few seconds, plus a
 // size-capped event log. Both live next to the game's own logs.
 internal static class Diagnostics
 {
  const long EventLimit=5L*1024*1024;
  static string folder;
  internal static string Folder
  {
   get{
    if(folder!=null)return folder;
    var root=Environment.GetEnvironmentVariable("CSII_USERDATAPATH",EnvironmentVariableTarget.User);
    if(string.IsNullOrWhiteSpace(root))root=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"..","LocalLow","Colossal Order","Cities Skylines II"));
    folder=Path.Combine(root,"Logs");Directory.CreateDirectory(folder);return folder;
   }
  }
  internal static string SnapshotPath=>Path.Combine(Folder,"RoadLaneLock-diag.json");
  internal static string EventPath=>Path.Combine(Folder,"RoadLaneLock-events.jsonl");
  static readonly object gate=new object();
  internal static void Event(string kind,object data=null)
  {
   try{
    var line=JsonConvert.SerializeObject(new {UTC=DateTime.UtcNow.ToString("o"),Version=Mod.Version,Kind=kind,Data=data});
    lock(gate){
     var path=EventPath;var info=new FileInfo(path);
     if(info.Exists&&info.Length>EventLimit){var old=Path.Combine(Folder,"RoadLaneLock-events.1.jsonl");if(File.Exists(old))File.Delete(old);File.Move(path,old);}
     File.AppendAllText(path,line+"\n",System.Text.Encoding.UTF8);
    }
   }catch(Exception ex){Mod.Log.Warn("Diagnostics event: "+ex.Message);}
  }
  internal static Dictionary<string,float> StencilOf(Material m)
  {
   var result=new Dictionary<string,float>();
   if(!m||!m.shader)return result;
   foreach(var name in m.GetPropertyNames(MaterialPropertyType.Float))if(name.IndexOf("Stencil",StringComparison.OrdinalIgnoreCase)>=0)result[name]=m.GetFloat(name);
   foreach(var name in m.GetPropertyNames(MaterialPropertyType.Int))if(name.IndexOf("Stencil",StringComparison.OrdinalIgnoreCase)>=0)result[name]=m.GetInteger(name);
   return result;
  }
  // One-off census of every loaded material: shader, queue and stencil state, to see how the game's
  // own objects opt out of ground projection.
  internal static void DumpMaterials()
  {
   var byShader=new SortedDictionary<string,List<object>>();
   foreach(var m in Resources.FindObjectsOfTypeAll<Material>()){
    if(!m||!m.shader)continue;var key=m.shader.name;
    if(!byShader.TryGetValue(key,out var list))byShader[key]=list=new List<object>();
    if(list.Count<4)list.Add(new {m.name,Queue=m.renderQueue,Keywords=string.Join(" ",m.shaderKeywords),Stencil=StencilOf(m)});
   }
   File.WriteAllText(Path.Combine(Folder,"RoadLaneLock-materials.json"),JsonConvert.SerializeObject(byShader,Formatting.Indented),System.Text.Encoding.UTF8);
   Event("materialsDumped",new {Shaders=byShader.Count});
  }
  internal static float[] V(Vector3 v)=>new[]{(float)Math.Round(v.x,2),(float)Math.Round(v.y,2),(float)Math.Round(v.z,2)};
  // Summarise what Unity actually does with a sign: renderer count, how many a camera drew, bounds and materials.
  internal static object Render(GameObject root,Camera view)
  {
   if(!root)return new {Exists=false};
   var camera=view?view.transform.position:Vector3.zero;
   var screen=new List<object>();
   var renderers=root.GetComponentsInChildren<Renderer>(true);
   int visible=0,enabled=0;Bounds bounds=default;bool any=false;
   var materials=new SortedDictionary<string,object>();
   foreach(var r in renderers){
    if(r.enabled&&r.gameObject.activeInHierarchy)enabled++;
    if(r.isVisible)visible++;
    if(!any){bounds=r.bounds;any=true;}else bounds.Encapsulate(r.bounds);
    // Screen rectangle (pixels, origin top-left) of each sign face, to match against player screenshots.
    if(view&&(r.name.Contains("plate")||r.name.Contains("body"))){
     var b=r.bounds;float x0=float.MaxValue,y0=float.MaxValue,x1=float.MinValue,y1=float.MinValue;bool front=true;
     for(int k=0;k<8;k++){var p=view.WorldToScreenPoint(b.center+Vector3.Scale(b.extents,new Vector3((k&1)==0?-1:1,(k&2)==0?-1:1,(k&4)==0?-1:1)));if(p.z<=0)front=false;x0=Mathf.Min(x0,p.x);x1=Mathf.Max(x1,p.x);y0=Mathf.Min(y0,view.pixelHeight-p.y);y1=Mathf.Max(y1,view.pixelHeight-p.y);}
     var filter=r.GetComponent<MeshFilter>();var mesh=filter?filter.sharedMesh:null;
     screen.Add(new {r.name,Mesh=mesh?mesh.name+" v="+mesh.vertexCount+" sub="+mesh.subMeshCount+" uv="+mesh.uv.Length:"(none)",Materials=System.Array.ConvertAll(r.sharedMaterials,x=>x?x.name+"#"+x.GetInstanceID():"null"),LayerMask=r.renderingLayerMask,InFront=front,Screen=new[]{Mathf.Round(x0),Mathf.Round(y0),Mathf.Round(x1),Mathf.Round(y1)},ScreenSize=new[]{view.pixelWidth,view.pixelHeight}});
    }
    foreach(var m in r.sharedMaterials){
     if(!m||materials.ContainsKey(m.name+"#"+m.GetInstanceID()))continue;
     var texture=m.HasProperty("_BaseColorMap")?m.GetTexture("_BaseColorMap"):m.HasProperty("_UnlitColorMap")?m.GetTexture("_UnlitColorMap"):null;
     var passes=new List<string>();foreach(var pass in new[]{"GBuffer","Forward","ForwardOnly","DepthOnly","DepthForwardOnly","ShadowCaster","MotionVectors"})if(m.GetShaderPassEnabled(pass))passes.Add(pass);
     materials[m.name+"#"+m.GetInstanceID()]=new {Shader=m.shader?m.shader.name:null,Texture=texture?texture.width+"x"+texture.height+(texture.mipmapCount>1?" mips="+texture.mipmapCount:""):null,
      Cull=m.HasProperty("_CullMode")?m.GetFloat("_CullMode"):-1,DoubleSided=m.HasProperty("_DoubleSidedEnable")?m.GetFloat("_DoubleSidedEnable"):-1,Queue=m.renderQueue,Decals=m.HasProperty("_SupportDecals")?m.GetFloat("_SupportDecals"):-1,Surface=m.HasProperty("_SurfaceType")?m.GetFloat("_SurfaceType"):-1,Stencil=StencilOf(m),m.name,TextureId=texture?texture.GetInstanceID():0,Keywords=string.Join(" ",m.shaderKeywords),Passes=passes,ShaderPasses=m.shader?m.shader.passCount:0,BaseColor=m.HasProperty("_BaseColor")?m.GetColor("_BaseColor").ToString():null};
    }
   }
   return new {Exists=true,Active=root.activeInHierarchy,Layer=root.layer,Renderers=renderers.Length,Enabled=enabled,VisibleToACamera=visible,
    BoundsCenter=any?V(bounds.center):null,BoundsSize=any?V(bounds.size):null,CameraDistance=any?(float)Math.Round(Vector3.Distance(camera,bounds.center),1):-1,Faces=screen,Materials=materials.Values};
  }
 }
 public sealed class DiagnosticsSystem:GameSystemBase
 {
  float next,dumpAt=-1;string last;bool dumped;
  protected override void OnUpdate()
  {
   if(!dumped&&Game.SceneFlow.GameManager.instance!=null&&Game.SceneFlow.GameManager.instance.gameMode==GameMode.Game){
    if(dumpAt<0)dumpAt=UnityEngine.Time.unscaledTime+20;
    else if(UnityEngine.Time.unscaledTime>=dumpAt){dumped=true;try{Diagnostics.DumpMaterials();}catch(Exception ex){Mod.Log.Warn("Material census: "+ex.Message);}}
   }
   if(UnityEngine.Time.unscaledTime<next)return;next=UnityEngine.Time.unscaledTime+3;
   try{
    var camera=World.GetExistingSystemManaged<Game.Rendering.CameraUpdateSystem>()?.activeCamera;
    var position=camera?camera.transform.position:Vector3.zero;
    var markings=World.GetExistingSystemManaged<RoadMarkings>();
    var snapshot=new {UTC=DateTime.UtcNow.ToString("o"),Version=Mod.Version,SupportedGame=Mod.SupportedGame,
     Camera=camera?new {Position=Diagnostics.V(position),Forward=Diagnostics.V(camera.transform.forward),camera.nearClipPlane,camera.farClipPlane,camera.fieldOfView}:null,
     MaterialValidation=new {SignHardware.ValidationFailures,SignHardware.LastValidationError},
     Quality=new {MipLimit=QualitySettings.globalTextureMipmapLimit,QualitySettings.streamingMipmapsActive,Pipeline=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name:null},
     Parking=markings?.ParkingDiagnostics(camera),
     Events=Diagnostics.EventPath};
    var json=JsonConvert.SerializeObject(snapshot,Formatting.Indented);
    if(json==last)return;last=json;
    var path=Diagnostics.SnapshotPath;var temp=path+".tmp";
    File.WriteAllText(temp,json,System.Text.Encoding.UTF8);
    if(File.Exists(path))File.Delete(path);File.Move(temp,path);
   }catch(Exception ex){Mod.Log.Warn("Diagnostics snapshot: "+ex.Message);next=UnityEngine.Time.unscaledTime+30;}
  }
 }
}
