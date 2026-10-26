using System.IO;
using UnityEditor;
using UnityEngine;
public static class ReferenceMonsterAssets {
 public static void Prepare() {
  AssetDatabase.Refresh();
  foreach(var name in new[]{"Thornback","Mirejaw"}) {
   string path="Assets/Resources/Monsters/"+name+".mat";
   var material=AssetDatabase.LoadAssetAtPath<Material>(path);
   if(!material){material=new Material(Shader.Find("Standard"));material.name=name+" PBR";AssetDatabase.CreateAsset(material,path);}
   material.color=Color.white;
   material.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Monsters/"+name+"_basecolor.jpg");
   material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Monsters/"+name+"_normal.png"));
   material.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Monsters/"+name+"_metallic.png"));
   material.EnableKeyword("_NORMALMAP");material.EnableKeyword("_METALLICGLOSSMAP");material.SetFloat("_BumpScale",.75f);material.SetFloat("_GlossMapScale",.85f);
   EditorUtility.SetDirty(material);
   var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Models/"+name+".fbx");
   if(!model||model.GetComponentsInChildren<SkinnedMeshRenderer>().Length==0)throw new System.Exception(name+" skinned model missing");
   if(!material.mainTexture)throw new System.Exception(name+" source textures missing");
   var clips=AssetDatabase.LoadAllAssetsAtPath("Assets/Resources/Models/"+name+".fbx");
   int n=0;foreach(var c in clips)if(c is AnimationClip clip&&!clip.name.StartsWith("__preview"))n++;
   if(n<8)throw new System.Exception(name+" expected eight creature clips, got "+n);
   Debug.Log("CREATURE_ASSET "+name+" clips "+n+" PBR4K");
  }
  AssetDatabase.SaveAssets();
 }
 /// <summary>The Briarhound companion (studio model from the concept art): albedo-only material for the creature path of
 /// CharacterFactory. Skipped when the model is not in this project.</summary>
 public static void PrepareCompanion() {
  const string name="Briarhound";
  var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Models/"+name+".fbx");
  var tex=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Monsters/"+name+"_basecolor.png");
  if(!model||!tex)return;
  string path="Assets/Resources/Monsters/"+name+".mat";
  var material=AssetDatabase.LoadAssetAtPath<Material>(path);
  if(!material){material=new Material(Shader.Find("Standard"));material.name=name;AssetDatabase.CreateAsset(material,path);}
  material.color=Color.white;material.mainTexture=tex;material.SetFloat("_Glossiness",.18f);material.SetFloat("_Metallic",0);
  EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();
  int n=0;foreach(var c in AssetDatabase.LoadAllAssetsAtPath("Assets/Resources/Models/"+name+".fbx"))if(c is AnimationClip clip&&!clip.name.StartsWith("__preview"))n++;
  Debug.Log("COMPANION_ASSET "+name+" clips "+n);
 }
 public static void Build(){Prepare();FHBuild.Build();}
}