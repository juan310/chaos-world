using UnityEngine;
using FH.Core;
namespace FH.Game {
 public class MonsterVenom:MonoBehaviour {
  GameSession session;MonsterAttackDef attack;Vector3 velocity;float age,strength;
  public static void Fire(GameSession s,MonsterAttackDef attack,Vector3 origin,Vector3 direction,float strength) {
   var o=WorldKit.Orb("Mirejaw venom",origin,.18f,WorldKit.Glow(new Color(.48f,.72f,.07f),2));
   var v=o.AddComponent<MonsterVenom>();v.session=s;v.attack=attack;v.strength=strength;v.velocity=direction.normalized*9;
   var trail=o.AddComponent<TrailRenderer>();trail.time=.18f;trail.startWidth=.26f;trail.endWidth=.01f;trail.material=WorldKit.Transparent("Venom trail",new Color(.5f,.8f,.05f,.65f));
  }
  void Update() {
   if(session==null||!session.Simulating)return;float dt=Time.deltaTime;age+=dt;var from=transform.position;var to=from+velocity*dt;
   if(Physics.Linecast(from,to,Layers.WorldMask,QueryTriggerInteraction.Ignore)){Destroy(gameObject);return;}
   foreach(var c in Physics.OverlapCapsule(from,to,.20f,1<<Layers.Actor,QueryTriggerInteraction.Ignore)) {
    var hero=c.GetComponent<HunterController>();if(!hero)continue;
    float before=hero.Health;hero.ReceiveHit(attack,from);
    if(hero.Health<before&&!hero.Dead){var st=hero.GetComponent<VenomStatus>();if(!st)st=hero.gameObject.AddComponent<VenomStatus>();st.Set(session,4,3*strength);session.Hud?.Notify("Poisoned! Use an antidote.",NoticeKind.Warning);}
    WorldKit.Burst(to,new Color(.6f,.85f,.1f),12);Destroy(gameObject);return;
   }
   transform.position=to;if(age>1.4f)Destroy(gameObject);
  }
 }
}