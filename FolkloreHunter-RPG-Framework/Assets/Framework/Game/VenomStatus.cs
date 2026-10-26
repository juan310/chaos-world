using UnityEngine;
namespace FH.Game {
 public class VenomStatus:MonoBehaviour {
  GameSession session;HunterController hunter;float remaining,dps;
  public bool Active=>remaining>0;
  public void Set(GameSession s,float duration,float damagePerSecond){session=s;hunter=GetComponent<HunterController>();remaining=Mathf.Max(remaining,duration);dps=damagePerSecond;}
  public void Clear(){remaining=0;}
  void Update(){if(session==null||!session.Simulating||remaining<=0||!hunter||hunter.Dead)return;float dt=Mathf.Min(remaining,Time.deltaTime);remaining-=dt;hunter.TakeVenomDamage(dps*dt);}
 }
}