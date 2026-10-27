using System.Linq;
using FH.Core;
using NUnit.Framework;

namespace FH.Tests
{
    /// <summary>0.9 battle rules: wounds and Rend, Resolve, Unravel, the Thorn Rush's perfect links, the power clash
    /// and the Thorn Tracer.</summary>
    public class HuntWoundTests
    {
        [Test]
        public void BlowsOnOneSpotOpenAWoundThatTakesMoreDamage()
        {
            var w = new WoundTracker(300);
            Assert.That(w.Threshold, Is.EqualTo(30));
            Assert.That(w.Hit("head", 20, 0), Is.False);
            Assert.That(w.Hit("tail", 20, 1), Is.False, "another spot builds on its own");
            Assert.That(w.Hit("head", 12, 2), Is.True, "the head opens at 32 of 30");
            Assert.That(w.IsOpen("head", 3) && !w.IsOpen("tail", 3));
            Assert.That(w.Multiplier("head", 3), Is.EqualTo(WoundTracker.DamageBonus));
            Assert.That(w.Multiplier("tail", 3), Is.EqualTo(1));
            Assert.That(w.IsOpen("head", 2 + WoundTracker.Life + .1f), Is.False, "a wound left alone closes");
        }

        [Test]
        public void BuildUpFadesWhenTheSpotIsLeftAlone()
        {
            var w = new WoundTracker(300);
            w.Hit("head", 25, 0);
            Assert.That(w.Hit("head", 10, WoundTracker.BuildDecayDelay + 1), Is.False, "the earlier blows no longer count");
            Assert.That(w.Build("head"), Is.EqualTo(10));
        }

        [Test]
        public void TearingAWoundScarsItAndEveryThirdTearUnravels()
        {
            var w = new WoundTracker(100);
            Assert.That(w.Tear("head", 0), Is.False, "no wound, nothing to tear");
            string[] spots = { "head", "chest", "tail" };
            float t = 0;
            foreach (var spot in spots) { w.Hit(spot, 20, t); t += 1; }
            Assert.That(w.OpenCount(t), Is.EqualTo(3));
            Assert.That(w.Hit("leg", 40, t), Is.False, "at most three wounds are open at once");
            Assert.That(w.Tear("head", t) && !w.Unravels);
            Assert.That(w.IsScarred("head", t + 1));
            Assert.That(w.Hit("head", 50, t + 1), Is.False, "a scarred spot does not wound again");
            Assert.That(w.Hit("head", 50, t + WoundTracker.ScarTime + 2), Is.True, "until the scar fades");
            Assert.That(w.Tear("chest", t) && !w.Unravels);
            Assert.That(w.Tear("tail", t) && w.Unravels, "the third tear topples the monster");
            Assert.That(w.Torn, Is.EqualTo(3));
        }

        [Test]
        public void ResolveStacksToThreeAndFades()
        {
            var r = new HunterResolve();
            Assert.That(r.DamageMultiplier(0), Is.EqualTo(1));
            for (int i = 0; i < 5; i++) r.Add(i);
            Assert.That(r.Stacks(5), Is.EqualTo(HunterResolve.Max));
            Assert.That(r.DamageMultiplier(5), Is.EqualTo(1 + 3 * HunterResolve.DamagePerStack).Within(1e-5));
            Assert.That(r.RegenMultiplier(5), Is.EqualTo(1 + 3 * HunterResolve.RegenPerStack).Within(1e-5));
            Assert.That(r.Stacks(4 + HunterResolve.Duration + .1f), Is.EqualTo(0), "the last tear's time runs out");
        }

        [Test]
        public void ThornRushRewardsThePressInTheGlint()
        {
            var set = TestSupport.Db().HuntMovesets["sword_shield"];
            var leap = set.Move("leaping_slash");
            Assert.That(RushTiming.Judge(leap, leap.perfectFrom - .1f), Is.EqualTo(RushTiming.Link.Plain), "early: a plain link");
            Assert.That(RushTiming.Judge(leap, (leap.perfectFrom + leap.perfectTo) / 2), Is.EqualTo(RushTiming.Link.Perfect));
            Assert.That(RushTiming.Judge(set.Move("slash_1"), .7f), Is.EqualTo(RushTiming.Link.None), "ordinary moves have no glint");
            var next = HuntRoutes.Next(set, leap, HuntInput.Light);
            var perfect = RushTiming.Perfect(next, leap.perfectBonus);
            Assert.That(perfect.damageMultiplier, Is.EqualTo(next.damageMultiplier * (1 + leap.perfectBonus)).Within(1e-5));
            Assert.That(perfect.windup, Is.LessThan(next.windup));
            Assert.That(set.Move("rush_1").damageMultiplier, Is.EqualTo(next.damageMultiplier), "the shipped move is untouched");
        }

        [Test]
        public void TheRushRunsFromTheBackstepToTheShieldCrash()
        {
            var set = TestSupport.Db().HuntMovesets["sword_shield"];
            var m = HuntRoutes.Opener(set, HuntInput.Light, false, 99, 99, .2f);
            var ids = new System.Collections.Generic.List<string> { m.id };
            for (int i = 0; i < 4; i++) { m = HuntRoutes.Next(set, m, HuntInput.Light); ids.Add(m.id); }
            Assert.That(ids, Is.EqualTo(new[] { "leaping_slash", "rush_1", "rush_2", "rush_3", "shield_crash" }));
            Assert.That(set.Move("shield_crash").koDamage, Is.GreaterThan(set.Move("shield_bash").koDamage), "the finisher stuns hardest");
            Assert.That(HuntRoutes.Opener(set, HuntInput.Light, false, 99, 99, 1f).id, Is.EqualTo("slash_1"), "the back-step window expires");
        }

        [Test]
        public void EveryWeaponHasARendAndTheShieldWeaponsClash()
        {
            var db = TestSupport.Db();
            Assert.That(db.Errors, Is.Empty);
            foreach (var set in db.HuntMovesets.Values)
            {
                var rend = set.Move(set.rend);
                Assert.That(rend, Is.Not.Null, set.id);
                Assert.That(rend.rend, set.id);
            }
            Assert.That(db.HuntMovesets["bow"].Move("tracer_shot").tracer && db.HuntMovesets["bow"].Move("tracer_shot").shot != null);
            Assert.That(db.HuntMovesets.Values.Where(s => s.clash).Select(s => s.id), Is.EquivalentTo(new[] { "sword_shield", "lance" }));
        }

        [Test]
        public void PerfectGuardsBuildAClashThatMashingWins()
        {
            var m = new ClashMeter();
            m.PerfectGuard(); m.PerfectGuard();
            Assert.That(m.Ready, Is.False);
            m.PerfectGuard(); m.PerfectGuard();
            Assert.That(m.Ready && m.Perfects == 3, "three perfect guards, no more stored");
            int target = m.Begin();
            Assert.That(m.Perfects, Is.EqualTo(0));
            Assert.That(m.Need, Is.EqualTo(4), "each clash asks one more perfect guard");
            var won = new ClashBout(target);
            for (int i = 0; i < target - 1; i++) Assert.That(won.Press(), Is.EqualTo(ClashBout.State.Running));
            Assert.That(won.Press(), Is.EqualTo(ClashBout.State.Won));
            var lost = new ClashBout(target);
            lost.Press();
            Assert.That(lost.Tick(ClashBout.Time + .01f), Is.EqualTo(ClashBout.State.Lost));
            Assert.That(lost.Press(), Is.EqualTo(ClashBout.State.Lost), "presses after the end change nothing");
            for (int i = 0; i < 10; i++) { m.Begin(); }
            Assert.That(m.Need, Is.EqualTo(ClashMeter.MaxNeed));
        }

        [Test]
        public void TheTracerBurstsAfterItsArrowsAndExpires()
        {
            var t = new TracerMark();
            Assert.That(t.ArrowLanded(0), Is.False, "no tracer, no count");
            t.Plant(0);
            for (int i = 0; i < TracerMark.Needed - 1; i++) Assert.That(t.ArrowLanded(1), Is.False);
            Assert.That(t.ArrowLanded(1), Is.True);
            Assert.That(t.Active(1), Is.False, "it burst");
            t.Plant(2);
            Assert.That(t.Active(2 + TracerMark.Duration + .1f), Is.False);
        }
    }
}
