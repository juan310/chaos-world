using System.Linq;
using FH.Core;
using NUnit.Framework;

namespace FH.Tests
{
    /// <summary>The two-handed katana (Monster Hunter's long sword: spirit gauge, spirit blade combo, roundslash level-up,
    /// foresight and iai) and the evasion choice (roll, side slide, back-step, dive, hop, sidestep).</summary>
    public class KatanaEvadeTests
    {
        static GameDatabase Db() => TestSupport.Db();
        static HuntMovesetDef Katana() => Db().HuntMovesets["katana"];

        [Test]
        public void SpiritBladeComboEndsInTheLevelRaisingRoundslash()
        {
            var set = Katana();
            var a = HuntRoutes.Opener(set, HuntInput.Spirit, false, 99, 99);
            var b = HuntRoutes.Next(set, a, HuntInput.Spirit);
            var c = HuntRoutes.Next(set, b, HuntInput.Spirit);
            var d = HuntRoutes.Next(set, c, HuntInput.Spirit);
            Assert.That(new[] { a.id, b.id, c.id, d.id }, Is.EqualTo(new[] { "spirit_1", "spirit_2", "spirit_3", "roundslash" }));
            Assert.That(d.spiritLevelUp && !a.spiritLevelUp);
            Assert.That(HuntRoutes.Next(set, set.Move("overhead"), HuntInput.Spirit).id, Is.EqualTo("spirit_1"), "any normal slash flows into the spirit blade");
            Assert.That(set.Move("spirit_2").rehit > 0 && set.Move("spirit_3").rehit > 0, "the spirit blades cut several times");
            Assert.That(set.Move("fade_slash").travel, Is.LessThan(0), "the fade slash carries the hunter backwards");
        }

        [Test]
        public void GaugeFillsOnHitsSpendsOnSpiritAndDrains()
        {
            var g = new SpiritGauge();
            Assert.That(g.Spend(0, 12), Is.False, "an empty gauge: the spirit move goes ahead, weaker");
            for (int i = 0; i < 4; i++) g.Hit(1, 9);
            Assert.That(g.Value, Is.EqualTo(36).Within(.01));
            Assert.That(g.Spend(1, 12), Is.True);
            Assert.That(g.Value, Is.EqualTo(24).Within(.01));
            g.Tick(3, 1);
            Assert.That(g.Value, Is.EqualTo(24).Within(.01), "no drain right after fighting");
            g.Tick(10, 2);
            Assert.That(g.Value, Is.EqualTo(14).Within(.01), "drains once the hunter stops hitting");
        }

        [Test]
        public void RoundslashRaisesLevelsThatFade()
        {
            var g = new SpiritGauge();
            Assert.That(g.DamageMultiplier, Is.EqualTo(1f));
            g.LevelUp(0); g.LevelUp(1); g.LevelUp(2); g.LevelUp(3);
            Assert.That(g.Level, Is.EqualTo(3), "white, yellow, red, capped");
            Assert.That(g.DamageMultiplier, Is.EqualTo(1.2f));
            g.Tick(3 + g.LevelDuration + .1f, .1f);
            Assert.That(g.Level, Is.EqualTo(2), "a level fades after a while");
        }

        [Test]
        public void IaiRefillsAndForesightFills()
        {
            var g = new SpiritGauge();
            g.Iai(0, 5);
            g.Tick(1, 2);
            Assert.That(g.Value, Is.EqualTo(g.IaiFillPerSecond * 2).Within(.01));
            Assert.That(g.IaiActive(4.9f) && !g.IaiActive(5.1f));
            g.Fill(6);
            Assert.That(g.Value, Is.EqualTo(SpiritGauge.Max));
            var set = Katana();
            Assert.That(set.Move(set.iaiSpiritSlash).spiritCounter && set.Move(set.iaiSpiritSlash).guardPoint);
            Assert.That(set.Move(set.foresightCounter).damageMultiplier, Is.GreaterThan(set.Move(set.foresightSlash).damageMultiplier));
        }

        [Test]
        public void EvadeChoiceFollowsStickAndStance()
        {
            var sns = Db().HuntMovesets["sword_shield"]; var lance = Db().HuntMovesets["lance"]; var bow = Db().HuntMovesets["bow"];
            Assert.That(Evade.Choose(sns, false, false, false, 1, 0), Is.EqualTo(EvadeKind.Roll), "sheathed: always a roll");
            Assert.That(Evade.Choose(sns, true, false, false, 1, 0), Is.EqualTo(EvadeKind.SideSlide), "drawn, stick to the side: side slide");
            Assert.That(Evade.Choose(sns, true, false, false, -.9f, .2f), Is.EqualTo(EvadeKind.SideSlide));
            Assert.That(Evade.Choose(sns, true, false, false, 0, -1), Is.EqualTo(EvadeKind.Backstep), "drawn, stick back: back-step");
            Assert.That(Evade.Choose(sns, true, false, false, .2f, 1), Is.EqualTo(EvadeKind.Roll), "drawn, stick forward: roll");
            Assert.That(Evade.Choose(sns, false, true, true, 0, 1), Is.EqualTo(EvadeKind.Dive), "sprinting away from a monster: dive");
            Assert.That(Evade.Choose(sns, false, true, false, 0, 1), Is.EqualTo(EvadeKind.Roll), "sprinting with nothing to flee: roll");
            Assert.That(Evade.Choose(lance, true, false, false, 1, 0), Is.EqualTo(EvadeKind.Hop));
            Assert.That(Evade.Choose(bow, true, false, false, 0, 0), Is.EqualTo(EvadeKind.Sidestep));
        }

        [Test]
        public void EvasionsTradeDistanceForSafety()
        {
            var sns = Db().HuntMovesets["sword_shield"];
            var roll = Evade.Spec(sns, EvadeKind.Roll, .25f); var slide = Evade.Spec(sns, EvadeKind.SideSlide, .25f);
            var back = Evade.Spec(sns, EvadeKind.Backstep, .25f); var dive = Evade.Spec(sns, EvadeKind.Dive, .25f);
            Assert.That(slide.Distance, Is.LessThan(roll.Distance)); Assert.That(slide.KeepFacing && back.KeepFacing && !roll.KeepFacing);
            Assert.That(back.Time, Is.LessThan(roll.Time), "the back-step is the quickest");
            Assert.That(dive.Distance, Is.GreaterThan(roll.Distance)); Assert.That(dive.Invulnerable, Is.GreaterThan(roll.Invulnerable));
            Assert.That(dive.Recover, Is.GreaterThan(0), "a dive ends on the ground");
        }
    }
}
