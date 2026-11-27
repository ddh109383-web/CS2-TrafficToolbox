using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;
namespace RoadLaneLock
{
 internal static class SignHardware
 {
  internal const float PostDiameter=.075f,BodyDepth=.03f,MountOffset=.065f;
  static readonly Mesh[] plateBoxes=new Mesh[2],discBodies=new Mesh[2];
  internal static int ValidationFailures;internal static string LastValidationError;
  // Runtime HDRP materials must be validated, or keywords/passes stay at shader defaults.
  internal static Material Validate(Material material)
  {
   // Signs stand inside the road/sidewalk decal projectors; receiving decals paints paving onto the faces.
   if(material.HasProperty("_SupportDecals"))material.SetFloat("_SupportDecals",0);
   try{UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(material);}catch(System.Exception ex){ValidationFailures++;LastValidationError=ex.Message;Mod.Log.Warn("Sign material validation: "+ex.Message);Diagnostics.Event("materialValidationFailed",new {Material=material.name,ex.Message});}
   return material;
  }
  internal static Material Lit(Color color,float smoothness,float metallic=0,Texture texture=null)
  {
   var template=GameTemplate();
   if(template){
    // The game's ground/lot decals skip surfaces drawn with its own shader (as the CDLC.TS sign mod does):
    // copy a plain opaque game material, strip its maps and options, keep only our colour and texture.
    var g=new Material(template){hideFlags=HideFlags.DontSave,name="RoadLaneLock sign"};
    var keep=new List<string>();foreach(var k in g.shaderKeywords)if(k=="_DISABLE_SSR_TRANSPARENT")keep.Add(k);
    g.shaderKeywords=keep.ToArray();
    foreach(var name in g.GetTexturePropertyNames())if(name!=BaseMap)g.SetTexture(name,null);
    g.SetTexture(BaseMap,texture);
    if(g.HasProperty("_BaseColor"))g.SetColor("_BaseColor",color);
    if(g.HasProperty("_Smoothness"))g.SetFloat("_Smoothness",smoothness);
    if(g.HasProperty("_Metallic"))g.SetFloat("_Metallic",metallic);
    // The game selects which decal layers paint an object with colossal_DecalLayerMask; the template's value
    // let one layer (present at some junctions) grey the faces. Signs take none; weather overlays off too.
    foreach(var p in OptOut)if(g.HasProperty(p))g.SetFloat(p,0);
    Validate(g);g.EnableKeyword("_DISABLE_DECALS");
    return AsProp(g);
   }
   var shader=Shader.Find("HDRP/Lit");if(!shader)throw new System.InvalidOperationException("HDRP/Lit unavailable");
   var m=new Material(shader){hideFlags=HideFlags.DontSave};
   m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",smoothness);m.SetFloat("_Metallic",metallic);
   if(texture)m.SetTexture("_BaseColorMap",texture);
   return AsProp(Validate(m));
  }
  static readonly string[] OptOut={"colossal_DecalLayerMask","_Snow","_Wetness"};
  const string GameShader="BH/SG_DefaultShader",BaseMap="_BaseColorMap";
  static Material template;static bool searched;
  // Plainest opaque, non-virtual-textured material of the game's default object shader.
  static Material GameTemplate()
  {
   if(searched)return template;
   int best=int.MaxValue;
   foreach(var m in Resources.FindObjectsOfTypeAll<Material>()){
    if(!m||!m.shader||m.shader.name!=GameShader||!m.HasProperty(BaseMap)||m.renderQueue<2000||m.renderQueue>=2500)continue;
    if(m.name.StartsWith("CDLTS")||m.name.StartsWith("RoadLaneLock"))continue;
    var keywords=m.shaderKeywords;bool skip=false;
    foreach(var k in keywords)if(k=="ENABLE_VT"||k=="_SURFACE_TYPE_TRANSPARENT"||k.StartsWith("_ALPHATEST")){skip=true;break;}
    if(skip||keywords.Length>=best)continue;
    best=keywords.Length;template=m;
   }
   searched=template!=null; // the library may not be loaded yet (main menu): search again next time
   Diagnostics.Event("signShader",template?(object)new {Template=template.name,template.renderQueue,Values=System.Array.ConvertAll(OptOut,p=>p+"="+(template.HasProperty(p)?template.GetFloat(p).ToString("R")+" bits=0x"+System.BitConverter.ToInt32(System.BitConverter.GetBytes(template.GetFloat(p)),0).ToString("x"):"-")),Keywords=template.shaderKeywords,Textures=template.GetTexturePropertyNames(),Floats=template.GetPropertyNames(MaterialPropertyType.Float),Colors=template.GetPropertyNames(MaterialPropertyType.Vector)}:new {Template="(none) HDRP/Lit fallback"});
   return template;
  }
  // Match the game's own props (material census): decals disabled, yet kept in HDRP's OpaqueDecal queue (2225).
  // Validation moves non-receivers to 2000, which misses the depth pass the ground decals project against,
  // so paving and lane colour from behind were painted over the sign.
  internal const int PropQueue=2225;
  internal static Material AsProp(Material m){m.renderQueue=PropQueue;return m;}
  internal static GameObject Solid(GameObject root,string name,PrimitiveType type,Vector3 center,Quaternion rotation,Vector3 scale,Material material)
  {
   var part=GameObject.CreatePrimitive(type);part.name=name;part.hideFlags=HideFlags.DontSave;
   part.transform.SetParent(root.transform,false);part.transform.SetPositionAndRotation(center,rotation);part.transform.localScale=scale;
   var collider=part.GetComponent<Collider>();if(collider){collider.enabled=false;Object.Destroy(collider);}
   var renderer=part.GetComponent<Renderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;NoDecals(renderer);return part;
  }
  // Artwork is part of the body's own faces: no stacked decals, so nothing can z-fight at distance.
  internal static GameObject Body(GameObject root,string name,Mesh mesh,Vector3 center,Quaternion rotation,Vector3 scale,Material face,Material edge)
  {
   var part=new GameObject(name){hideFlags=HideFlags.DontSave};
   part.transform.SetParent(root.transform,false);part.transform.SetPositionAndRotation(center,rotation);part.transform.localScale=scale;
   part.AddComponent<MeshFilter>().sharedMesh=mesh;
   var renderer=part.AddComponent<MeshRenderer>();renderer.sharedMaterials=new[]{face,edge};renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;NoDecals(renderer);return part;
  }
  // Keep only the default light layer so decal layers never target sign renderers.
  internal static void NoDecals(Renderer renderer){renderer.renderingLayerMask=1;}
  internal static void Post(GameObject root,Vector3 ground,float height,Material silver)
  {
   Solid(root,"Silver cylindrical post",PrimitiveType.Cylinder,ground+Vector3.up*(height*.5f),Quaternion.identity,new Vector3(PostDiameter,height*.5f,PostDiameter),silver);
   Solid(root,"Post cap",PrimitiveType.Sphere,ground+Vector3.up*height,Quaternion.identity,new Vector3(PostDiameter,PostDiameter*.6f,PostDiameter),silver);
   Solid(root,"Post base plate",PrimitiveType.Cylinder,ground+Vector3.up*.012f,Quaternion.identity,new Vector3(.2f,.012f,.2f),silver);
  }
  internal static void Collar(GameObject root,Vector3 at,Material silver)
  {Solid(root,"Post clamp collar",PrimitiveType.Cylinder,at,Quaternion.identity,new Vector3(PostDiameter+.03f,.035f,PostDiameter+.03f),silver);}
  internal static void Bracket(GameObject root,Vector3 at,Quaternion facing,Material silver)
  {Solid(root,"Rear mounting bracket",PrimitiveType.Cube,at-facing*Vector3.forward*(MountOffset*.5f),facing,new Vector3(.06f,.06f,MountOffset),silver);}
  // Unit box; submesh 0 = front (-Z) and back (+Z) with readable UVs from both sides, submesh 1 = edges.
  // backArt=false: the rear face is plain metal (submesh 1), as on regulatory signs.
  internal static Mesh PlateBox(bool backArt=true)
  {
   int key=backArt?1:0;if(plateBoxes[key])return plateBoxes[key];
   var v=new List<Vector3>();var uv=new List<Vector2>();var faces=new List<int>();var edges=new List<int>();
   void Quad(Vector3 n,Vector3 up,List<int> target,bool art)
   {
    var right=Vector3.Cross(up,-n);int i=v.Count;
    v.Add((n-right-up)*.5f);v.Add((n-right+up)*.5f);v.Add((n+right+up)*.5f);v.Add((n+right-up)*.5f);
    if(art){uv.Add(new Vector2(0,0));uv.Add(new Vector2(0,1));uv.Add(new Vector2(1,1));uv.Add(new Vector2(1,0));}
    else for(int k=0;k<4;k++)uv.Add(new Vector2(.5f,.5f));
    target.Add(i);target.Add(i+1);target.Add(i+2);target.Add(i);target.Add(i+2);target.Add(i+3);
   }
   Quad(Vector3.back,Vector3.up,faces,true);Quad(Vector3.forward,Vector3.up,backArt?faces:edges,backArt);
   Quad(Vector3.left,Vector3.up,edges,false);Quad(Vector3.right,Vector3.up,edges,false);
   Quad(Vector3.up,Vector3.forward,edges,false);Quad(Vector3.down,Vector3.forward,edges,false);
   return plateBoxes[key]=Build("Sign plate box",v,uv,faces,edges);
  }
  // Unit-diameter disc of unit thickness along Z; front/back faces map the artwork, rim is submesh 1.
  internal static Mesh Disc(bool backArt=true)
  {
   int key=backArt?1:0;if(discBodies[key])return discBodies[key];
   const int segments=48;
   var v=new List<Vector3>();var uv=new List<Vector2>();var faces=new List<int>();var rim=new List<int>();
   for(int side=-1;side<=1;side+=2){
    int center=v.Count;v.Add(new Vector3(0,0,side*.5f));uv.Add(new Vector2(.5f,.5f));
    for(int k=0;k<segments;k++){float a=k*Mathf.PI*2/segments;var p=new Vector3(Mathf.Cos(a)*.5f,Mathf.Sin(a)*.5f,side*.5f);v.Add(p);
     // Viewed from +Z the X axis is mirrored, so flip U on the back face to keep text readable.
     uv.Add(new Vector2(.5f+(side<0?p.x:-p.x),.5f+p.y));}
    for(int k=0;k<segments;k++){int a=center+1+k,b=center+1+(k+1)%segments;
     if(side<0){faces.Add(center);faces.Add(b);faces.Add(a);}else {var back=backArt?faces:rim;back.Add(center);back.Add(a);back.Add(b);}}
   }
   for(int k=0;k<segments;k++){
    float a0=k*Mathf.PI*2/segments,a1=(k+1)*Mathf.PI*2/segments;int i=v.Count;
    v.Add(new Vector3(Mathf.Cos(a0)*.5f,Mathf.Sin(a0)*.5f,-.5f));v.Add(new Vector3(Mathf.Cos(a0)*.5f,Mathf.Sin(a0)*.5f,.5f));
    v.Add(new Vector3(Mathf.Cos(a1)*.5f,Mathf.Sin(a1)*.5f,.5f));v.Add(new Vector3(Mathf.Cos(a1)*.5f,Mathf.Sin(a1)*.5f,-.5f));
    for(int q=0;q<4;q++)uv.Add(new Vector2(.5f,.5f));
    rim.Add(i);rim.Add(i+2);rim.Add(i+1);rim.Add(i);rim.Add(i+3);rim.Add(i+2);
   }
   return discBodies[key]=Build("Sign disc body",v,uv,faces,rim);
  }
  static Mesh Build(string name,List<Vector3> v,List<Vector2> uv,List<int> a,List<int> b)
  {
   var mesh=new Mesh{name=name,hideFlags=HideFlags.DontSave,subMeshCount=2};
   mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(a,0);mesh.SetTriangles(b,1);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
  }
  // Mipmapped artwork averages cleanly at distance instead of breaking into sub-pixel strips.
  // dump: also write the exact pixels handed to the GPU as PNG, so the in-game artwork can be inspected.
  internal static Texture2D Texture(string name,int width,int height,Color32[] pixels,string dump=null)
  {
   var texture=new Texture2D(width,height,TextureFormat.RGBA32,true){name=name,hideFlags=HideFlags.DontSave,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear,anisoLevel=8,ignoreMipmapLimit=true};
   texture.SetPixels32(pixels);
   if(dump!=null){texture.Apply(true,false);try{System.IO.File.WriteAllBytes(dump,ImageConversion.EncodeToPNG(texture));}catch(System.Exception ex){Mod.Log.Warn("Texture dump: "+ex.Message);}texture.Apply(false,true);}
   else texture.Apply(true,true);
   return texture;
  }
  internal static void Dispose(){for(int i=0;i<2;i++){if(plateBoxes[i])Object.Destroy(plateBoxes[i]);if(discBodies[i])Object.Destroy(discBodies[i]);plateBoxes[i]=discBodies[i]=null;}}
 }
}
