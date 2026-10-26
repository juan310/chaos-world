using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using FH.Core;
using UnityEngine;
namespace FH.Game {
 // Deterministic review uses a separate save directory. Fixture grants are explicitly logged.
 // Combat shown in the second half uses the ordinary AI, controller, hit volumes and InputRouter.
 public class CreatureReview:MonoBehaviour {
  GameSession S;string dir;bool record;int frame,failures;List<string> log=new List<string>();
  bool showcase;const int Fps=24;
  IEnumerator Start(){
   S=GetComponent<GameSession>();showcase=Environment.GetCommandLineArgs().Contains("--show-hunts");
   dir=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","Captures","ReferenceHunts"));
   Directory.CreateDirectory(dir);Application.runInBackground=true;
   yield return null;S.NewGame(3,"Reference Hunt");InputRouter.UseVirtual=true;
   Check("content database valid",S.Db.Errors.Count==0,string.Join(";",S.Db.Errors));
   foreach(var m in S.Monsters)m.Calm(120);
   if(!showcase){Time.captureFramerate=Fps;record=true;StartCoroutine(Record());}
   S.CameraRig.Distance=9;S.CameraRig.Pitch=26;S.CameraRig.Yaw=20;S.CameraRig.Snap();
   S.Hud.Notify("Reference hunts • modular starting cottage",NoticeKind.Story);
   yield return Wait(2);
   // Review loadout fixture: craft through the real crafting system using granted materials.
   foreach(var id in new[]{"r_thornback_set","r_tusk_cleaver"})if(!S.Core.State.knownRecipes.Contains(id))S.Core.State.knownRecipes.Add(id);
   foreach(var a in new[]{new ItemAmount{itemId="thorn_bark",count=11},new ItemAmount{itemId="swept_tusk",count=3},new ItemAmount{itemId="amber_resin",count=2}})
    S.Core.Inventory.Add(a.itemId,a.count,ItemSource.Start);
   S.Core.Wallet.Earn(100);
   log.Add("FIXTURE granted recipes, materials and 100 gold in isolated review save; no normal save modified");
   Check("armor crafts",S.Core.Crafting.Craft("r_thornback_set",Station.Forge).Ok);
   Check("cleaver crafts",S.Core.Crafting.Craft("r_tusk_cleaver",Station.Forge).Ok);
   foreach(var id in new[]{"thornback_set","tusk_cleaver"}){
    var stack=S.Core.State.inventory.First(i=>i.itemId==id);Check(id+" equips",S.Core.Inventory.Equip(stack.uid).Ok);
   }
   S.Hunter.RefreshStats(true);S.Hunter.RefreshEquipmentPresentation();yield return Wait(.3f);
   var gear=S.Hunter.GetComponent<HuntEquipment>();
   Check("armor retarget has no missing bones",gear&&gear.MissingBones==0,gear?gear.MissingBones.ToString():"missing component");
   Vector3 a0,b0;Check("cleaver provides actual moving blade endpoints",gear&&gear.TryBlade(out a0,out b0));
   S.Hud.Notify("Thornback armor + Tusk Cleaver • crafted equipment",NoticeKind.Reward);
   S.CameraRig.Yaw=180;S.CameraRig.Distance=5.5f;S.CameraRig.Pitch=18;S.CameraRig.Snap();
   yield return Wait(2);
   if(showcase){
    foreach(var x in S.Monsters)x.Calm(3);
    var target=S.Monsters.First(m=>m.MonsterId=="thornback");
    Place(target.Home+Vector3.back*7,target.Home);
    S.CameraRig.Manual=false;S.CameraRig.Distance=10;S.CameraRig.Pitch=28;
    S.Hud.Notify("Thornback hunt • LMB attack • Space dodge • Tab lock on • Esc menus",NoticeKind.Story);
    InputRouter.UseVirtual=false;yield break;
   }
   foreach(var id in new[]{"thornback","mirejaw"}){
    var m=S.Monsters.First(x=>x.MonsterId==id);
    foreach(var other in S.Monsters)other.gameObject.SetActive(other==m);
    m.enabled=false;var origin=m.transform.position;m.transform.rotation=Quaternion.identity;
    Place(origin+new Vector3(11,0,11),origin);S.Hunter.LockTarget=m;
    S.CameraRig.Manual=true;var cam=S.CameraRig.Camera;
    cam.transform.position=origin+new Vector3(6,4,7);cam.transform.LookAt(origin+Vector3.up*1.1f);
    var skin=m.Visual.GetComponentInChildren<SkinnedMeshRenderer>();
    Check(id+" has real skinned geometry",skin&&skin.sharedMesh.vertexCount>50000);
    Check(id+" 4K material",skin&&skin.sharedMaterial.mainTexture&&skin.sharedMaterial.mainTexture.width==4096);
    foreach(string clip in id=="thornback"?new[]{"Walking_A","TuskSweep","TailSweep","Charge"}:new[]{"Walking_A","Bite","TailSweep","Poison"}){
     m.Anim.Play(clip,clip=="Walking_A",1,true,.05f);
     S.Hud.Notify(m.DisplayName+" • "+clip,NoticeKind.Story);
     float t=0;while(t<2){
      if(clip=="Walking_A"){m.transform.position=S.SafeGround(origin+Vector3.forward*t*.6f);}
      t+=Time.deltaTime;yield return null;
     }
     m.transform.position=origin;
    }
    m.enabled=true;m.Brain.Grace=2.5f;S.CameraRig.Manual=false;S.CameraRig.Distance=11;S.CameraRig.Pitch=32;
    Place(origin+Vector3.forward*7,origin);S.Hunter.LockTarget=m;
    S.Hud.Notify(m.DisplayName+" • live AI combat",NoticeKind.Warning);
    int landed=S.Hunter.SwingsLanded;float startHP=S.Hunter.Health;float next=0,time=0;
    var modes=new HashSet<string>();float travelled=0;Vector3 last=m.transform.position;
    while(time<12&&!S.Hunter.Dead&&m.Alive){
     modes.Add(m.Brain.Mode.ToString());travelled+=Vector3.Distance(last,m.transform.position);last=m.transform.position;
     var to=m.transform.position-S.Hunter.transform.position;to.y=0;
     var desired=time>2.5f&&to.magnitude>2.5f?to.normalized:Vector3.zero;
     var f=Vector3.ProjectOnPlane(S.CameraRig.Camera.transform.forward,Vector3.up).normalized;var r=Vector3.Cross(Vector3.up,f);
     InputRouter.VirtualMove=new Vector2(Vector3.Dot(desired,r),Vector3.Dot(desired,f));
     if(time>2.5f&&time>=next){InputRouter.Press(GameAction.Attack);next=time+1.6f;}
     if(m.Brain.Mode==MonsterMode.Telegraph&&m.Brain.ModeTime>m.Brain.CurrentAttack.telegraph*.7f)InputRouter.Press(GameAction.Dodge);
     time+=Time.deltaTime;yield return null;
    }
    InputRouter.VirtualMove=Vector2.zero;
    Check(id+" AI enters attack",modes.Contains("Attack"),string.Join(",",modes));
    Check(id+" AI moves across ground",travelled>.2f,travelled.ToString("F2"));
    Check(id+" blade contacts real hurtboxes",S.Hunter.SwingsLanded>landed,(S.Hunter.SwingsLanded-landed).ToString());
    log.Add(id+" health loss "+(startHP-S.Hunter.Health).ToString("F1"));
    if(S.Hunter.Dead)S.Hunter.Revive(S.SafeGround(origin+Vector3.back*6),0);
    // Isolated part fixtures validate the actual actor break/drop event path.
    var parts=id=="thornback"?new[]{"tusk_l","tusk_r"}:new[]{"gland_l","gland_r"};
    foreach(var part in parts){
     var box=m.Hurtboxes.First(h=>h.Parts.Contains(part));
     m.ReceiveHit(box,box.transform.position,(p,broken)=>new HitInfo{Damage=80,Poise=0});
     Check(id+" "+part+" breaks",m.Parts.Broken.Contains(part));
    }
    if(id=="mirejaw")Check("broken sacs disable venom selection",!m.Brain.AttackFilter(m.Def.attacks.First(x=>x.id=="venom_spray")));
    m.Calm(30);yield return Wait(1);
   }
   // Native status check uses the real status component and health path.
   var venom=S.Hunter.GetComponent<VenomStatus>()??S.Hunter.gameObject.AddComponent<VenomStatus>();
   S.Hunter.Health=S.Hunter.Stats[Stat.MaxHealth];float before=S.Hunter.Health;venom.Set(S,4,3);yield return Wait(.6f);
   Check("venom drains health",S.Hunter.Health<before);venom.Clear();before=S.Hunter.Health;yield return Wait(.4f);
   Check("cleared venom stops health drain",Mathf.Abs(before-S.Hunter.Health)<.01f);
   S.Save();var loaded=S.LoadSlot(3);Check("review save reloads",loaded.Ok);
   Check("crafted armor and weapon persist",S.Core.Inventory.Equipped(EquipSlot.Body)?.itemId=="thornback_set"&&S.Core.Stats.EquippedWeapon()?.id=="tusk_cleaver");
   record=false;yield return null;InputRouter.UseVirtual=false;
   File.WriteAllText(Path.Combine(dir,"native-checks.txt"),string.Join("\n",log)+"\nFAILURES "+failures+"\n");
   File.WriteAllText(Path.Combine(dir,"capture.json"),"{\"source\":\"actual Unity Windows player, including UI\",\"fps\":"+Fps+",\"frames\":"+frame+",\"fixture\":\"separate save; granted craft materials; controlled animation review followed by live AI combat\"}");
   Debug.Log("CREATURE_REVIEW failures "+failures+" frames "+frame);Application.Quit(failures>0?1:0);
  }
  void Check(string name,bool ok,string detail=""){log.Add((ok?"PASS ":"FAIL ")+name+" "+detail);if(!ok)failures++;Debug.Log("CREATURE_REVIEW "+log.Last());}
  IEnumerator Wait(float seconds){float t=0;while(t<seconds){t+=Time.deltaTime;yield return null;}}
  void Place(Vector3 p,Vector3 look){var d=look-p;d.y=0;S.Hunter.Teleport(S.SafeGround(p),Quaternion.LookRotation(d).eulerAngles.y);S.CameraRig.Yaw=S.Hunter.transform.eulerAngles.y;S.CameraRig.Snap();}
  IEnumerator Record(){while(record){yield return new WaitForEndOfFrame();var tex=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(dir,"f"+frame.ToString("00000")+".jpg"),tex.EncodeToJPG(85));Destroy(tex);frame++;}}
 }
}