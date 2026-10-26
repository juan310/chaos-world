using FH.Core;
using NUnit.Framework;

namespace FH.Tests
{
    /// <summary>The Briarhound's Palamute-style fighting and riding rules.</summary>
    public class HoundTacticsTests
    {
        [Test]
        public void ComboIsBiteSlashSpinThenOver()
        {
            Assert.That(HoundTactics.Combo[0].Clip, Is.EqualTo("Bite"));
            Assert.That(HoundTactics.Combo[1].Clip, Is.EqualTo("Slash_R"));
            Assert.That(HoundTactics.Combo[2].Clip, Is.EqualTo("Spin"));
            Assert.That(HoundTactics.NextStrike(0), Is.EqualTo(1));
            Assert.That(HoundTactics.NextStrike(1), Is.EqualTo(2));
            Assert.That(HoundTactics.NextStrike(2), Is.EqualTo(-1));
            Assert.That(HoundTactics.Combo[2].AllAround && HoundTactics.Combo[2].Damage > HoundTactics.Combo[0].Damage, "the spin finishes, strongest, all around");
            foreach (var s in HoundTactics.Combo) Assert.That(s.HitAt, Is.LessThan(s.Length));
        }

        [Test]
        public void ReadsTheWindUpBeforeAttacking()
        {
            Assert.That(HoundTactics.Choose(1, 2.4f, true, true, true, true, true), Is.EqualTo(HoundAction.Evade), "a wind-up beats an open attack");
            Assert.That(HoundTactics.Choose(1, 2.4f, true, false, true, true, true), Is.EqualTo(HoundAction.Reposition), "no hop ready: step out instead");
            Assert.That(HoundTactics.Choose(1, 2.4f, false, true, true, true, true), Is.EqualTo(HoundAction.Attack));
            Assert.That(HoundTactics.Choose(6, 2.4f, false, true, true, true, false), Is.EqualTo(HoundAction.Pounce), "mid range: pounce in");
            Assert.That(HoundTactics.Choose(6, 2.4f, false, true, true, false, false), Is.EqualTo(HoundAction.Approach));
            Assert.That(HoundTactics.Choose(12, 2.4f, false, true, true, true, false), Is.EqualTo(HoundAction.Approach), "too far to pounce");
            Assert.That(HoundTactics.Choose(1, 2.4f, false, true, false, true, true), Is.EqualTo(HoundAction.Reposition), "waiting for the combo: circle");
        }

        [Test]
        public void FlankPointIsAcrossTheMonsterFromTheHunter()
        {
            // Monster at the origin, hunter 5 m south: the hound goes north, a little off the line.
            var (x, z) = HoundTactics.FlankPoint(0, 0, 0, -5, 3);
            Assert.That(z, Is.GreaterThan(2));
            Assert.That(System.Math.Abs(x), Is.GreaterThan(.5f).And.LessThan(2.5f));
            Assert.That(System.Math.Sqrt(x * x + z * z), Is.EqualTo(3).Within(.001));
            Assert.That(HoundTactics.PincerAngle(0, 0, 0, -5, x, z), Is.GreaterThan(130));
            Assert.That(HoundTactics.PincerAngle(0, 0, 0, -5, 0, -3), Is.LessThan(1), "beside the hunter is no pincer");
        }

        [Test]
        public void RidingLopesDashesAndDriftsTighter()
        {
            float walk = 2.4f, gallop = 7.5f;
            Assert.That(HoundTactics.RideSpeed(0, false, false, walk, gallop), Is.EqualTo(0));
            Assert.That(HoundTactics.RideSpeed(.3f, false, false, walk, gallop), Is.LessThan(walk * 1.01f));
            float lope = HoundTactics.RideSpeed(1, false, false, walk, gallop), dash = HoundTactics.RideSpeed(1, true, false, walk, gallop);
            Assert.That(lope, Is.GreaterThan(walk * 1.5f), "full stick lopes, well above a walk");
            Assert.That(dash, Is.GreaterThan(lope * 1.4f), "RT dashes");
            Assert.That(HoundTactics.RideSpeed(1, true, true, walk, gallop), Is.LessThan(dash).And.GreaterThan(lope), "a drift keeps most of the speed");
            Assert.That(HoundTactics.TurnRate(1, true, true), Is.GreaterThan(HoundTactics.TurnRate(1, true, false) * 3), "drifting turns far tighter than dashing");
        }

        [Test]
        public void PicksTheMonsterThatMatters()
        {
            float locked = HoundTactics.TargetScore(true, false, false, 10, false);
            float threat = HoundTactics.TargetScore(false, true, false, 3, false);
            float hit = HoundTactics.TargetScore(false, false, true, 3, false);
            float idle = HoundTactics.TargetScore(false, false, false, 2, false);
            Assert.That(locked, Is.GreaterThan(threat), "the hunter's lock wins");
            Assert.That(threat, Is.GreaterThan(hit), "a monster about to hit the hunter beats the one being hit");
            Assert.That(hit, Is.GreaterThan(idle));
            Assert.That(HoundTactics.TargetScore(false, true, false, 6, true), Is.GreaterThan(HoundTactics.TargetScore(false, true, false, 6, false)), "the hunter down: rescue first");
        }

        [Test]
        public void CommitsOnOpenings()
        {
            Assert.That(HoundTactics.IsOpening(MonsterMode.Recover) && HoundTactics.IsOpening(MonsterMode.Stagger) && HoundTactics.IsOpening(MonsterMode.Downed));
            Assert.That(HoundTactics.IsOpening(MonsterMode.Telegraph) || HoundTactics.IsOpening(MonsterMode.Chase), Is.False);
            Assert.That(HoundTactics.CooldownAfterCombo(MonsterMode.Downed, false), Is.LessThan(HoundTactics.CooldownAfterCombo(MonsterMode.Chase, false) * .5f));
            Assert.That(HoundTactics.CooldownAfterCombo(MonsterMode.Chase, true), Is.LessThan(1), "the hunter down: no pause");
        }

        [Test]
        public void CutsTheSoftPartAndSteersRoundObstacles()
        {
            Assert.That(HoundTactics.PartScore(1.6f, false, false), Is.GreaterThan(HoundTactics.PartScore(1, false, true)));
            Assert.That(HoundTactics.PartScore(1, true, false), Is.GreaterThan(HoundTactics.PartScore(1.3f, false, false)), "a weak point is worth half again");
            Assert.That(HoundTactics.PartScore(.7f, false, false), Is.LessThan(HoundTactics.PartScore(1, false, false)), "hard parts last");
            Assert.That(HoundTactics.Steer(a => false, 1), Is.EqualTo(0f), "a clear path goes straight");
            Assert.That(HoundTactics.Steer(a => System.Math.Abs(a) < 40, 1), Is.EqualTo(70f), "blocked ahead: the first gap on its side");
            Assert.That(HoundTactics.Steer(a => System.Math.Abs(a) < 40, -1), Is.EqualTo(-70f));
            Assert.That(HoundTactics.Steer(a => a < 100, 1), Is.EqualTo(110f));
            Assert.That(HoundTactics.Steer(a => true, 1), Is.Null);
            Assert.That(HoundTactics.AutoEvadeRidden(true, true, false, false));
            Assert.That(HoundTactics.AutoEvadeRidden(true, true, false, true), Is.False, "the rider already steering away keeps control");
        }
    }
}
