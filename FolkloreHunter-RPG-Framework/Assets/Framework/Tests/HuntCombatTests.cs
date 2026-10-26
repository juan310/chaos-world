using System.Linq;
using FH.Core;
using NUnit.Framework;

namespace FH.Tests
{
    public class HuntCombatTests
    {
        static HuntMovesetDef Set() => TestSupport.Db().HuntMovesets["sword_shield"];

        [Test]
        public void ShippedMovesetValidatesAndEveryRouteResolves()
        {
            var db = TestSupport.Db();
            Assert.That(db.Errors, Is.Empty);
            var set = db.HuntMovesets["sword_shield"];
            foreach (var m in set.moves)
                foreach (var next in new[] { m.onLight, m.onHeavy, m.onLightHeavy, m.maxChargeMove }.Where(x => !string.IsNullOrEmpty(x)))
                    Assert.That(set.Move(next), Is.Not.Null, m.id + " -> " + next);
        }

        [Test]
        public void LightChainLoopsThroughThreeSlashes()
        {
            var set = Set();
            var a = HuntRoutes.Opener(set, HuntInput.Light, false, 99, 99);
            var b = HuntRoutes.Next(set, a, HuntInput.Light);
            var c = HuntRoutes.Next(set, b, HuntInput.Light);
            var d = HuntRoutes.Next(set, c, HuntInput.Light);
            Assert.That(new[] { a.id, b.id, c.id, d.id }, Is.EqualTo(new[] { "slash_1", "slash_2", "chop_3", "slash_1" }));
        }

        [Test]
        public void SpecialOpenersDependOnContext()
        {
            var set = Set();
            Assert.That(HuntRoutes.Opener(set, HuntInput.Heavy, true, 99, 99).id, Is.EqualTo("draw_slash"), "any attack while sheathed draws");
            Assert.That(HuntRoutes.Opener(set, HuntInput.Light, false, .2f, 99).id, Is.EqualTo("roll_slash"), "light right after a roll");
            Assert.That(HuntRoutes.Opener(set, HuntInput.Light, false, .2f, .3f).id, Is.EqualTo("counter"), "a perfect guard beats the roll slash");
            Assert.That(HuntRoutes.Opener(set, HuntInput.Light, false, 1f, 1f).id, Is.EqualTo("slash_1"), "windows expire");
            Assert.That(HuntRoutes.Opener(set, HuntInput.ForwardLight, false, 99, 99).id, Is.EqualTo("lunge"));
            Assert.That(HuntRoutes.Opener(set, HuntInput.LightHeavy, false, 99, 99).id, Is.EqualTo("shield_bash"));
            Assert.That(HuntRoutes.Opener(set, HuntInput.None, false, 0, 0), Is.Null);
        }

        [Test]
        public void ChainAndRollCancelWindowsFollowTheMoveData()
        {
            var heavy = Set().Move("heavy_chop");
            Assert.That(HuntRoutes.CanChain(heavy, .3f), Is.False);
            Assert.That(HuntRoutes.CanChain(heavy, .56f), Is.True);
            Assert.That(HuntRoutes.CanRollCancel(heavy, .5f), Is.False, "heavy attacks commit");
            Assert.That(HuntRoutes.CanRollCancel(heavy, .61f), Is.True);
            Assert.That(HuntRoutes.CanRollCancel(null, 0), Is.True, "neutral can always roll");
        }

        [Test]
        public void ChargeLevelsScaleTheReleasedMoveWithoutChangingTheData()
        {
            var set = Set(); var heavy = set.Move("heavy_chop");
            Assert.That(ChargeMeter.Level(heavy, .3f), Is.EqualTo(0));
            Assert.That(ChargeMeter.Level(heavy, .6f), Is.EqualTo(1));
            Assert.That(ChargeMeter.Level(heavy, 5f), Is.EqualTo(2));
            Assert.That(ChargeMeter.Level(set.Move("slash_1"), 5f), Is.EqualTo(0), "light moves do not charge");
            var c2 = ChargeMeter.Charged(set, heavy, 2);
            Assert.That(c2.damageMultiplier, Is.EqualTo(heavy.damageMultiplier * 1.9f).Within(1e-4));
            Assert.That(c2.hitstop, Is.GreaterThan(heavy.hitstop));
            Assert.That(heavy.damageMultiplier, Is.EqualTo(1.7f).Within(1e-4), "the shared definition is untouched");
            Assert.That(ChargeMeter.Charged(set, heavy, 0), Is.SameAs(heavy));
        }

        [Test]
        public void GuardBlocksFrontOnlyAndPerfectGuardIsFree()
        {
            var set = Set();
            var side = GuardMath.Resolve(set, 30, 100, 1, 100);
            Assert.That(side.Blocked, Is.False, "a hit from behind the shield gets through");
            var perfect = GuardMath.Resolve(set, 30, 10, .1f, 100);
            Assert.That(perfect.Blocked && perfect.Perfect, Is.True);
            Assert.That(perfect.Damage + perfect.Stamina + perfect.Pushback, Is.EqualTo(0));
            var normal = GuardMath.Resolve(set, 30, 10, 1, 100);
            Assert.That(normal.Blocked && !normal.Perfect, Is.True);
            Assert.That(normal.Damage, Is.EqualTo(30 * .12f).Within(1e-4));
            Assert.That(normal.Stamina, Is.EqualTo(33).Within(1e-3));
            var tired = GuardMath.Resolve(set, 30, 10, 1, 5);
            Assert.That(tired.Damage, Is.GreaterThan(normal.Damage), "out of stamina the guard is pushed through");
        }

        [Test]
        public void ReactionsScaleWithTheBlow()
        {
            Assert.That(Reactions.For(new MonsterAttackDef(), 8), Is.EqualTo(HitReaction.Flinch));
            Assert.That(Reactions.For(new MonsterAttackDef(), 18), Is.EqualTo(HitReaction.Knockback));
            Assert.That(Reactions.For(new MonsterAttackDef(), 30), Is.EqualTo(HitReaction.Knockdown));
            Assert.That(Reactions.For(new MonsterAttackDef { reaction = "Knockdown" }, 2), Is.EqualTo(HitReaction.Knockdown), "data overrides damage");
        }

        [Test]
        public void MonsterFlinchesToppleAndEnragesFromHits()
        {
            var def = TestSupport.Db().Monsters["grave_warden"];
            var r = new MonsterReactions(def);
            float hp = def.maxHealth;
            int flinches = 0;
            for (int i = 0; i < 10; i++) { hp -= 10; if (r.Hit(10, 0, hp) == MonsterReactions.Outcome.Flinch) flinches++; }
            Assert.That(flinches, Is.EqualTo((int)(100 / r.FlinchThreshold)));
            Assert.That(r.Enraged, Is.False);
            Assert.That(r.Hit(0, 0, def.maxHealth * .45f), Is.EqualTo(MonsterReactions.Outcome.None));
            Assert.That(r.Enraged, Is.True, "below half health");
            Assert.That(r.CooldownMultiplier, Is.LessThan(1));
            float firstStun = r.StunThreshold;
            Assert.That(r.Hit(1, firstStun, def.maxHealth * .44f), Is.EqualTo(MonsterReactions.Outcome.Topple));
            Assert.That(r.Enraged, Is.False, "a topple calms it");
            Assert.That(r.StunThreshold, Is.GreaterThan(firstStun), "each topple makes the next harder");
        }

        [Test]
        public void BrainFlinchInterruptsAndToppleHoldsItDown()
        {
            var def = TestSupport.Db().Monsters["gravebound"];
            var b = new MonsterBrain(def) { Grace = 0 };
            var near = new MonsterBrain.Senses { distanceToPlayer = 1.5f, lineOfSight = true, playerAlive = true };
            b.Tick(.01f, near); b.Tick(.5f, near); b.Tick(.01f, near);
            Assert.That(b.Mode, Is.EqualTo(MonsterMode.Telegraph));
            b.Flinch(.6f);
            Assert.That(b.Mode, Is.EqualTo(MonsterMode.Stagger), "a flinch cancels the telegraphed attack");
            b.Tick(.65f, near);
            Assert.That(b.Mode, Is.EqualTo(MonsterMode.Chase));
            b.Topple(3);
            b.TakeHit(new HitInfo { Damage = 1, Poise = 999 });
            Assert.That(b.Mode, Is.EqualTo(MonsterMode.Downed), "poise breaks do not cut a topple short");
            b.Tick(2.9f, near);
            Assert.That(b.Mode, Is.EqualTo(MonsterMode.Downed));
            b.Tick(.2f, near);
            Assert.That(b.Mode, Is.EqualTo(MonsterMode.Chase));
        }
    }
}
