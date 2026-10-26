using System.Linq;
using FH.Core;
using NUnit.Framework;
namespace FH.Tests {
 public class ReferenceHuntTests {
  static MonsterBrain.Senses S(float distance,float angle=0)=>new MonsterBrain.Senses{distanceToPlayer=distance,angleToPlayer=angle,lineOfSight=true,playerAlive=true};
  static MonsterBrain Ready(MonsterDef d){var b=new MonsterBrain(d);b.Grace=0;b.Tick(.01f,S(2));b.Tick(.5f,S(2));return b;}
  [Test] public void DistantThornbackChoosesCharge(){var b=Ready(TestSupport.Db().Monsters["thornback"]);b.Tick(.01f,S(7));Assert.That(b.CurrentAttack.id,Is.EqualTo("charge"));}
  [Test] public void CloseThornbackCannotCharge(){var b=Ready(TestSupport.Db().Monsters["thornback"]);b.Tick(.01f,S(2));Assert.That(b.CurrentAttack.id,Is.EqualTo("tusk_sweep"));}
  [Test] public void RearApproachSelectsTail(){var b=Ready(TestSupport.Db().Monsters["mirejaw"]);b.Tick(.01f,S(3,170));Assert.That(b.CurrentAttack.id,Is.EqualTo("tail_sweep"));}
  [Test] public void BothBrokenSacsDisableDistantVenom(){
   var d=TestSupport.Db().Monsters["mirejaw"];var parts=new PartTracker(d);var b=Ready(d);
   b.AttackFilter=a=>a.id!="venom_spray"||!(parts.Broken.Contains("gland_l")&&parts.Broken.Contains("gland_r"));
   parts.Damage("gland_l",999);parts.Damage("gland_r",999);b.Tick(.01f,S(7));
   Assert.That(b.Mode,Is.EqualTo(MonsterMode.Chase));Assert.That(b.CurrentAttack,Is.Null);
  }
  [Test] public void OneBrokenSacStillAllowsVenom(){
   var d=TestSupport.Db().Monsters["mirejaw"];var p=new PartTracker(d);p.Damage("gland_l",999);var b=Ready(d);
   b.AttackFilter=a=>a.id!="venom_spray"||!(p.Broken.Contains("gland_l")&&p.Broken.Contains("gland_r"));
   b.Tick(.01f,S(7));Assert.That(b.CurrentAttack.id,Is.EqualTo("venom_spray"));
  }
  [Test] public void ThornbackContractFundsBothCraftedEquipmentItems(){
   var c=TestSupport.NewCore();TestSupport.SetRank(c,1);Assert.That(c.Contracts.Accept("c_thornback").Ok,Is.True);
   c.Events.RaisePartBroken("thornback","tusk_l");
   TestSupport.KillAndLoot(c,"thornback","s_thornback",new MaxRandom());
   Assert.That(c.Contracts.Claim("c_thornback").Ok,Is.True);
   Assert.That(c.Contracts.Claim("c_thornback").Ok,Is.False);
   Assert.That(c.Crafting.Craft("r_thornback_set",Station.Forge).Ok,Is.True);
   Assert.That(c.Crafting.Craft("r_tusk_cleaver",Station.Forge).Ok,Is.True);
   var armor=c.State.inventory.Single(i=>i.itemId=="thornback_set");var sword=c.State.inventory.Single(i=>i.itemId=="tusk_cleaver");
   Assert.That(c.Inventory.Equip(armor.uid).Ok,Is.True);Assert.That(c.Inventory.Equip(sword.uid).Ok,Is.True);
   Assert.That(c.Stats.EquippedWeapon().id,Is.EqualTo("tusk_cleaver"));
  }
  [Test] public void AntidoteIsAConsumablePoisonCure(){var i=TestSupport.Db().Item("antidote");Assert.That(i.curePoison,Is.True);Assert.That(i.kind,Is.EqualTo(ItemKind.Consumable));}
 }
}