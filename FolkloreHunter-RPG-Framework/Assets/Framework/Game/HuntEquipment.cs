using System.Linq;
using System.Collections.Generic;
using UnityEngine;
namespace FH.Game {
 public class HuntEquipment:MonoBehaviour {
  GameObject root;Renderer[] original;SkinnedMeshRenderer blade;Vector3 bladeA,bladeB;Transform bladeBone;Matrix4x4 bladeBind;bool cleaver;
  public int MissingBones{get;private set;}
  void OnDestroy(){if(root)Destroy(root);}
  public void Refresh(GameSession session,HunterController hero) {
   // The Thornback set and the cleaver are fitted to the classic knight's body: other looks keep their own (stats still apply).
   if(hero.Look!="Hunter"){if(root)root.SetActive(false);return;}
   if(!root)Build(hero);
   if(!root)return;
   var state=session.Core.State;
   bool armor=state.inventory.Any(i=>i.uid==state.equipment.body&&i.itemId=="thornback_set"&&!string.IsNullOrEmpty(i.uid));
   cleaver=session.Core.Stats.EquippedWeapon()?.id=="tusk_cleaver";
   root.SetActive(armor||cleaver);
   foreach(var r in original)if(r&&!r.name.StartsWith("Hunt_")) {
    if(r.name=="Knight_Head")r.enabled=true;
    else if(r.name=="1H_Sword")r.enabled=!cleaver;
    else r.enabled=!armor;
   }
   foreach(var r in root.GetComponentsInChildren<Renderer>(true))r.enabled=r.name.StartsWith("Tusk_Cleaver")?cleaver:armor;
  }
  void Build(HunterController hero) {
   var template=Resources.Load<GameObject>("Models/ThornbackGear");if(!template)return;
   original=hero.Visual.GetComponentsInChildren<Renderer>(true);
   root=Instantiate(template,hero.transform);root.name="Thornback fitted equipment";
   root.transform.localPosition=hero.Visual.localPosition;root.transform.localRotation=hero.Visual.localRotation;root.transform.localScale=hero.Visual.localScale;
   var bones=hero.Visual.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
   foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
    skin.bones=skin.bones.Select(b=>{if(b&&bones.TryGetValue(b.name,out var target))return target;MissingBones++;return b;}).ToArray();
    if(skin.rootBone&&bones.TryGetValue(skin.rootBone.name,out var rb))skin.rootBone=rb;skin.updateWhenOffscreen=true;
    if(skin.name=="Tusk_Cleaver_Blade")blade=skin;
   }
   foreach(var a in root.GetComponentsInChildren<Animation>())a.enabled=false;
   if(blade) {
    var mesh=blade.sharedMesh;var b=mesh.bounds;
    var axis=b.size.x>b.size.y&&b.size.x>b.size.z?Vector3.right*b.extents.x:b.size.y>b.size.z?Vector3.up*b.extents.y:Vector3.forward*b.extents.z;
    bladeA=b.center-axis;bladeB=b.center+axis;
    int i=mesh.boneWeights[0].boneIndex0;bladeBone=blade.bones[i];bladeBind=mesh.bindposes[i];
   }
  }
  public bool TryBlade(out Vector3 a,out Vector3 b) {
   a=b=Vector3.zero;if(!cleaver||!blade||!bladeBone)return false;
   a=bladeBone.TransformPoint(bladeBind.MultiplyPoint3x4(bladeA));b=bladeBone.TransformPoint(bladeBind.MultiplyPoint3x4(bladeB));return true;
  }
 }
}