using UnityEngine;
using FH.Core;
namespace FH.Game {
 public class MonsterVenom:MonoBehaviour {
  GameSession session;MonsterAttackDef attack;Vector3 velocity;float age,strength;bool poison=true;Color colour=new Color(.6f,.85f,.1f);
  public static void Fire(GameSession s,MonsterAttackDef attack,Vector3 origin,Vector3 direction,float strength) {
   var o=WorldKit.Orb("Mirejaw venom",origin,.18f,WorldKit.Glow(new Color(.48f,.72f,.07f),2));
   var v=o.AddComponent<MonsterVenom>();v.session=s;v.attack=attack;v.strength=strength;v.velocity=direction.normalized*9;
   var trail=o.AddComponent<TrailRenderer>();trail.time=.18f;trail.startWidth=.26f;trail.endWidth=.01f;trail.material=WorldKit.Transparent("Venom trail",new Color(.5f,.8f,.05f,.65f));
  }
  /// <summary>The Gravebound Mage's grave bolt: a purple spirit fire that flies at the hunter (no poison).</summary>
  public static int GraveBolts{get;private set;}
  public static void GraveBolt(GameSession s,MonsterAttackDef attack,Vector3 origin,Vector3 direction) {
   GraveBolts++;
   var o=WorldKit.Orb("Grave bolt",origin,.2f,WorldKit.Glow(new Color(.62f,.3f,1f),3));
   var v=o.AddComponent<MonsterVenom>();v.session=s;v.attack=attack;v.strength=1;v.poison=false;v.colour=new Color(.7f,.4f,1f);v.velocity=direction.normalized*11;
   var trail=o.AddComponent<TrailRenderer>();trail.time=.25f;trail.startWidth=.32f;trail.endWidth=.01f;trail.material=WorldKit.Transparent("Grave bolt trail",new Color(.62f,.32f,1f,.7f));
   var l=new GameObject("Grave bolt light").AddComponent<Light>();l.transform.SetParent(o.transform,false);l.type=LightType.Point;l.color=new Color(.65f,.35f,1f);l.range=4;l.intensity=2;
  }
  void Update() {
   if(session==null||!session.Simulating)return;float dt=Time.deltaTime;age+=dt;var from=transform.position;var to=from+velocity*dt;
   if(Physics.Linecast(from,to,Layers.WorldMask,QueryTriggerInteraction.Ignore)){Destroy(gameObject);return;}
   // a mounted hunter has no collider of their own: test the rider and the mount
   var rider=session.Hunter;
   if(rider&&rider.Riding&&rider.RiddenHitBy(from,to,.2f)){rider.ReceiveHit(attack,from);WorldKit.Burst(to,colour,12);Destroy(gameObject);return;}
   foreach(var c in Physics.OverlapCapsule(from,to,.20f,1<<Layers.Actor,QueryTriggerInteraction.Ignore)) {
    var hero=c.GetComponent<HunterController>();if(!hero)continue;
    float before=hero.Health;hero.ReceiveHit(attack,from);
    if(poison&&hero.Health<before&&!hero.Dead){var st=hero.GetComponent<VenomStatus>();if(!st)st=hero.gameObject.AddComponent<VenomStatus>();st.Set(session,4,3*strength);session.Hud?.Notify("Poisoned! Use an antidote.",NoticeKind.Warning);}
    WorldKit.Burst(to,colour,12);Destroy(gameObject);return;
   }
   transform.position=to;if(age>1.4f)Destroy(gameObject);
  }
 }
}