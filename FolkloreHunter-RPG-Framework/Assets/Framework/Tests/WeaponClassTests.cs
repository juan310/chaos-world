using System.Linq;
using FH.Core;
using NUnit.Framework;

namespace FH.Tests
{
    /// <summary>Lance and bow: their movesets, the bow's charge-level rules, arrow fans, arc-shot rain and chained hops.</summary>
    public class WeaponClassTests
    {
        static HuntMovesetDef Lance() => TestSupport.Db().HuntMovesets["lance"];
        static HuntMovesetDef Bow() => TestSupport.Db().HuntMovesets["bow"];

        [Test]
        public void FourWeaponClassesShipAndEveryRouteResolves()
        {
            var db = TestSupport.Db();
            Assert.That(db.Errors, Is.Empty);
            Assert.That(db.HuntMovesets.Values.Select(s => s.Class), Is.EquivalentTo(new[] { WeaponClass.SwordShield, WeaponClass.Lance, WeaponClass.Bow, WeaponClass.Katana }));
            foreach (var set in db.HuntMovesets.Values)
                foreach (var m in set.moves)
                    foreach (var next in new[] { m.onLight, m.onHeavy, m.onLightHeavy, m.maxChargeMove, m.onSpirit }.Where(x => !string.IsNullOrEmpty(x)))
                        Assert.That(set.Move(next), Is.Not.Null, set.id + "." + m.id + " -> " + next);
        }

        [Test]
        public void LanceThrustsMixMidAndHighUpToThree()
        {
            var set = Lance();
            var a = HuntRoutes.Opener(set, HuntInput.Light, false, 99, 99);
            var b = HuntRoutes.Next(set, a, HuntInput.Heavy);
            var c = HuntRoutes.Next(set, b, HuntInput.Light);
            Assert.That(new[] { a.id, b.id, c.id }, Is.EqualTo(new[] { "mid_1", "high_2", "mid_3" }));
            Assert.That(HuntRoutes.Next(set, c, HuntInput.Light), Is.Null, "a fourth thrust needs a new string (Monster Hunter's triple thrust)");
            Assert.That(HuntRoutes.Next(set, c, HuntInput.LightHeavy).id, Is.EqualTo("wide_sweep"));
        }

        [Test]
        public void LanceGuardOutclassesTheSmallShield()
        {
            var lance = Lance(); var sns = TestSupport.Db().HuntMovesets["sword_shield"];
            var l = GuardMath.Resolve(lance, 30, 80, 1, 100);
            var s = GuardMath.Resolve(sns, 30, 80, 1, 100);
            Assert.That(l.Blocked && !s.Blocked, "the tower shield covers a wider arc");
            l = GuardMath.Resolve(lance, 30, 0, 1, 100); s = GuardMath.Resolve(sns, 30, 0, 1, 100);
            Assert.That(l.Damage, Is.LessThan(s.Damage * .5f));
            Assert.That(l.Pushback, Is.LessThan(s.Pushback));
            Assert.That(lance.Move(lance.dashAttack).travel, Is.GreaterThan(0));
            Assert.That(lance.Move(lance.dashAttack).rehit, Is.GreaterThan(0));
            Assert.That(lance.Move(lance.retribution).damageMultiplier, Is.GreaterThan(lance.Move(lance.counterRelease).damageMultiplier));
        }

        [Test]
        public void LanceHopsChainThreeTimesThenStop()
        {
            var set = Lance(); var chain = new DodgeChain();
            Assert.That(set.Dodge, Is.EqualTo(DodgeStyle.Hop));
            Assert.That(chain.TryStart(set, 0, out var last) && !last);
            chain.Ended(.26f);
            Assert.That(chain.TryStart(set, .4f, out last) && !last);
            chain.Ended(.66f);
            Assert.That(chain.TryStart(set, .8f, out last) && last, "the third hop is the long one");
            chain.Ended(1.06f);
            Assert.That(chain.TryStart(set, 1.2f, out _), Is.False, "no fourth hop in a row");
            Assert.That(chain.TryStart(set, 2.0f, out last) && !last, "after a pause the chain starts again");
        }

        [Test]
        public void BowHoldingTheDrawClimbsLevels()
        {
            var set = Bow(); var c = new BowCharge(set);
            c.BeginDraw(10);
            Assert.That(c.Held(10.1f), Is.EqualTo(1));
            Assert.That(c.Held(10 + set.chargeTimes[0] + .01f), Is.EqualTo(2));
            Assert.That(c.Held(10 + set.chargeTimes[1] + .01f), Is.EqualTo(3));
            Assert.That(c.Held(20), Is.EqualTo(3), "capped at the top level");
        }

        [Test]
        public void BowQuickFollowUpShotsRaiseTheLevel()
        {
            var set = Bow(); var c = new BowCharge(set);
            Assert.That(c.Ready(0), Is.EqualTo(1));
            c.Fired(1, 0);
            Assert.That(c.Ready(.5f), Is.EqualTo(2));
            c.Fired(2, .5f);
            Assert.That(c.Ready(1f), Is.EqualTo(3));
            c.Fired(3, 1f);
            Assert.That(c.Ready(1.5f), Is.EqualTo(3));
            Assert.That(c.Ready(1f + set.chainShotWindow + .1f), Is.EqualTo(1), "a pause drops back to level 1");
        }

        [Test]
        public void ChargingSidestepAddsALevelPowerShotDoesNot()
        {
            var set = Bow(); var c = new BowCharge(set);
            Assert.That(c.Stepped(0), Is.EqualTo(2));
            Assert.That(c.Ready(.3f), Is.EqualTo(2));
            c.PowerShot(2, .4f);
            Assert.That(c.Ready(.6f), Is.EqualTo(2), "power shot keeps the level, does not raise it");
            Assert.That(new BowCharge(set) is var fresh && fresh.Ready(0) == 1);
            fresh.BeginDraw(5); fresh.Stepped(5.1f);
            Assert.That(fresh.Held(5.15f), Is.EqualTo(2), "a step while drawing continues the draw one level up");
            Assert.That(fresh.Held(5.1f + set.chargeTimes[0] + .01f), Is.EqualTo(3), "and keeps climbing from there");
        }

        [Test]
        public void ShotLevelsTradeArrowsForPowerPerArrow()
        {
            var set = Bow();
            Assert.That(set.Level(1).arrows, Is.EqualTo(1));
            Assert.That(set.Level(3).arrows, Is.EqualTo(3));
            float total1 = set.Level(1).arrows * set.Level(1).motionValue, total3 = set.Level(3).arrows * set.Level(3).motionValue;
            Assert.That(total3, Is.GreaterThan(total1 * 1.8f), "a full charge hits much harder in total");
            Assert.That(set.Level(3).motionValue, Is.LessThan(set.Level(1).motionValue));
            Assert.That(set.Move(set.dragonPiercer).shot.pierce);
            Assert.That(set.Move(set.arcShot).shot.arc);
        }

        [Test]
        public void ArrowFanIsCentredAndRainCoversTheCircle()
        {
            Assert.That(ShotPattern.Fan(1, 5), Is.EqualTo(new[] { 0f }));
            Assert.That(ShotPattern.Fan(3, 5), Is.EqualTo(new[] { -5f, 0f, 5f }));
            var rain = ShotPattern.Rain(7, 2.4f);
            Assert.That(rain.Count, Is.EqualTo(7));
            Assert.That(rain[0], Is.EqualTo((0f, 0f)), "the first arrow lands on the aim point");
            Assert.That(rain.All(p => System.Math.Sqrt(p.x * p.x + p.z * p.z) <= 2.4001));
            Assert.That(rain.Max(p => System.Math.Sqrt(p.x * p.x + p.z * p.z)), Is.GreaterThan(2.0));
        }

        [Test]
        public void TimelineCanRestrikeAndEndEarly()
        {
            var t = new AttackTimeline();
            t.Start(Lance().Move("dash_attack"));
            t.Tick(.3f);
            Assert.That(t.Phase, Is.EqualTo(AttackPhase.Active));
            int id = t.SwingId; t.NewSwing();
            Assert.That(t.SwingId, Is.EqualTo(id + 1));
            t.EndActive();
            Assert.That(t.Phase, Is.EqualTo(AttackPhase.Recovery));
            t.Tick(.4f);
            Assert.That(t.Running, Is.False);
        }
    }
}
