using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using FH.Game;
[Serializable] public class CreatureClipCheck {public string model,clip;public float maxDisplacement;public int worstTornEdges;public float worstGrowth; }
public static class CreatureImportChecks {
 public static void Run() {
  ReferenceMonsterAssets.Prepare();QualitySettings.skinWeights=SkinWeights.Unlimited;
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  var results=new List<CreatureClipCheck>();
  foreach(string name in new[]{"Thornback","Mirejaw"}) {
   var root=new GameObject("CHECK "+name);
   var visual=CharacterFactory.Create(name,root.transform,name=="Thornback"?2.75f:2.05f);
   var driver=new AnimationDriver(visual,name);
   var skin=visual.GetComponentInChildren<SkinnedMeshRenderer>();
   foreach(AnimationState s in driver.Anim){s.enabled=false;s.weight=0;}
   var idle=driver.State("Idle");idle.enabled=true;idle.weight=1;idle.time=0;driver.Anim.Sample();
   var wb=CharacterFactory.ActualBounds(visual.gameObject);
   Debug.Log("WORLD_BOUNDS "+name+" bottom "+wb.min.y+" height "+wb.size.y);
   if(Mathf.Abs(wb.min.y)>.001f||Mathf.Abs(wb.size.y-(name=="Thornback"?2.75f:2.05f))>.005f)throw new Exception("Creature scale or ground alignment mismatch");
   var mesh=new Mesh();skin.BakeMesh(mesh,true);var rest=mesh.vertices;var triangles=mesh.triangles;
   float height=mesh.bounds.size.y;if(height<=0)height=name=="Thornback"?2.75f:2.05f;
   idle.enabled=false;idle.weight=0;
   foreach(AnimationState clip in driver.Anim) {
    if(clip.name.StartsWith("__preview"))continue;
    var check=new CreatureClipCheck{model=name,clip=clip.name};
    clip.enabled=true;clip.weight=1;
    for(int k=0;k<=12;k++) {
     clip.time=clip.length*k/12;driver.Anim.Sample();skin.BakeMesh(mesh,true);var v=mesh.vertices;
     int torn=0;float growth=0,delta=0;
     for(int i=0;i<v.Length;i++)delta=Mathf.Max(delta,Vector3.Distance(v[i],rest[i]));
     for(int i=0;i<triangles.Length;i+=3)for(int j=0;j<3;j++) {
      int a=triangles[i+j],b=triangles[i+(j+1)%3];float before=Vector3.Distance(rest[a],rest[b]),after=Vector3.Distance(v[a],v[b]);
      growth=Mathf.Max(growth,(after-before)/height);
      if(after>2*before&&after-before>.03f*height)torn++;
     }
     check.maxDisplacement=Mathf.Max(check.maxDisplacement,delta);check.worstTornEdges=Mathf.Max(check.worstTornEdges,torn);check.worstGrowth=Mathf.Max(check.worstGrowth,growth);
    }
    clip.enabled=false;clip.weight=0;results.Add(check);Debug.Log("CREATURE_CLIP "+JsonUtility.ToJson(check));
   }
   UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(root);
  }
  Directory.CreateDirectory("Docs/MonsterIntegration");
  File.WriteAllText("Docs/MonsterIntegration/imported-animation-checks.json","["+string.Join(",",results.Select(JsonUtility.ToJson))+"]");
  if(results.Count<16||results.Any(r=>(r.clip.EndsWith("Walking_A")||r.clip.EndsWith("Run"))&&r.maxDisplacement<.01f)||results.Any(r=>r.worstTornEdges>0))throw new Exception("Missing or frozen creature animation after FBX import");
  Debug.Log("CREATURE_IMPORT_CHECKS "+results.Count);
 }
}