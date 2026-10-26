using System.Linq;
using UnityEngine;
using FH.Core;
namespace FH.Game {
 public class CreaturePresentation:MonoBehaviour {
  MonsterActor owner;GameSession session;LineRenderer lane;Transform[] breakBones;string[] breakIds;
  public void Initialize(MonsterActor actor,GameSession s) {
   owner=actor;session=s;var o=new GameObject("Creature warning lane");o.transform.SetParent(transform,false);lane=o.AddComponent<LineRenderer>();
   lane.useWorldSpace=true;lane.loop=false;lane.widthMultiplier=.075f;lane.material=WorldKit.Transparent("Creature lane",new Color(1,.25f,.08f,.85f));lane.enabled=false;
   breakIds=actor.MonsterId=="mirejaw"?new[]{"gland_l","gland_r"}:new[]{"tusk_l","tusk_r"};
   breakBones=breakIds.Select(id=>WorldKit.Find(actor.Visual,id.Replace("_","."))).ToArray();
  }
  void LateUpdate() {
   if(!owner||!owner.Ready)return;
   for(int i=0;i<breakIds.Length;i++)if(breakBones[i]&&owner.Parts.Broken.Contains(breakIds[i]))breakBones[i].localScale=Vector3.one*.18f;
   var a=owner.Brain.CurrentAttack;bool show=session.Simulating&&owner.Alive&&a!=null&&owner.Brain.Mode==MonsterMode.Telegraph&&(a.lungeSpeed>0||a.id=="venom_spray");
   lane.enabled=show;if(!show)return;
   var origin=owner.AttackCenter;origin.y=session.GroundHeight(origin)+.07f;var forward=transform.forward;var right=transform.right*(a.radius+.2f);var end=origin+forward*a.range;
   var pts=new[]{origin-right,origin+right,end+right,end-right,origin-right};for(int i=0;i<pts.Length;i++)pts[i].y=session.GroundHeight(pts[i])+.07f;
   lane.positionCount=pts.Length;lane.SetPositions(pts);var color=a.id=="venom_spray"?new Color(.65f,1,.1f,.9f):new Color(1,.25f,.08f,.9f);lane.startColor=lane.endColor=color;
  }
 }
}