using System;
using System.Linq;
using FH.Core;
using NUnit.Framework;

namespace FH.Tests
{
    /// <summary>Boss rules for the hunt combat: tougher stats, area-attack geometry, phases, situational choice, combos.</summary>
    public class HuntBossTests
    {
        static MonsterDef Def(string id) => TestSupport.Db().Monsters[id];

        [Test]
        public void HuntRulesMakeTheMonstersTougherAndAddBossAttacks()
        {
            foreach (var id in new[] { "grave_warden", "thornback", "mirejaw" })
            {
                var d = Def(id); var h = HuntBoss.MakeHuntDef(d);
                Assert.That(h.maxHealth, Is.GreaterThanOrEqualTo(d.maxHealth * 2), id + " health");
                Assert.That(h.attacks.Count, Is.GreaterThan(d.attacks.Count), id + " gains boss attacks");
                Assert.That(h.attacks.Any(a => a.Shape != AoeShape.None), id + " has area attacks");
                Assert.That(h.attacks.Any(a => a.phase >= 2) && h.attacks.Any(a => a.phase == 3), id + " has enraged and desperate moves");
                Assert.That(h.attacks.FirstOrDefault(a => a.id == d.roarAttack)?.Shape, Is.EqualTo(AoeShape.Roar), id + " roars");
                Assert.That(d.attacks.All(a => a.Shape == AoeShape.None) && d.maxHealth < h.maxHealth, "the valley's version is untouched");
            }
        }

        [Test]
        public void AreaGeometry()
        {
            var circle = new MonsterAttackDef { aoe = "Circle", aoeRadius = 3 };
            Assert.That(HuntBoss.Hits(circle, 0, 0, 0, 1, 2, 2, 0) && !HuntBoss.Hits(circle, 0, 0, 0, 1, 3, 3, 0));
            var ring = new MonsterAttackDef { aoe = "Ring", aoeRadius = 8, aoeSpeed = 10, aoeWidth = 1 };
            Assert.That(HuntBoss.Hits(ring, 0, 0, 0, 1, 0, 5, .5f), "the wave front reaches 5 m at 0.5 s");
            Assert.That(HuntBoss.Hits(ring, 0, 0, 0, 1, 0, 5, .2f), Is.False, "not yet");
            Assert.That(HuntBoss.Hits(ring, 0, 0, 0, 1, 0, 5, .8f), Is.False, "already past: rolled through or behind it");
            Assert.That(HuntBoss.Hits(ring, 0, 0, 0, 1, 0, 9, 2f), Is.False, "beyond its reach");
            var cone = new MonsterAttackDef { aoe = "Cone", aoeLength = 6, aoeAngle = 60 };
            Assert.That(HuntBoss.Hits(cone, 0, 0, 0, 1, 1, 5, 0) && !HuntBoss.Hits(cone, 0, 0, 0, 1, 4, 3, 0) && !HuntBoss.Hits(cone, 0, 0, 0, 1, 0, -2, 0));
            var line = new MonsterAttackDef { aoe = "Line", aoeLength = 10, aoeWidth = 2 };
            Assert.That(HuntBoss.Hits(line, 0, 0, 1, 0, 6, .8f, 0) && !HuntBoss.Hits(line, 0, 0, 1, 0, 6, 1.5f, 0) && !HuntBoss.Hits(line, 0, 0, 1, 0, -1, 0, 0));
            Assert.That(HuntBoss.ThreatRadius(ring), Is.EqualTo(8));
            var pts = HuntBoss.BurstPoints(4, 2.5f, 10, 10, new Random(1));
            Assert.That(pts[0], Is.EqualTo((10f, 10f)), "the first eruption is under the hunter");
            Assert.That(pts.Skip(1).All(p => Math.Abs(Math.Sqrt((p.x - 10) * (p.x - 10) + (p.z - 10) * (p.z - 10)) - 2.5) < 1e-3));
        }

        [Test]
        public void PhasesUnlockTheirMoves()
        {
            Assert.That(HuntBoss.Phase(.8f, false), Is.EqualTo(1));
            Assert.That(HuntBoss.Phase(.45f, true), Is.EqualTo(2));
            Assert.That(HuntBoss.Phase(.25f, true), Is.EqualTo(3));
            var desperate = new MonsterAttackDef { id = "d", phase = 3, range = 10 };
            Assert.That(HuntBoss.Usable(desperate, new BossSituation { Distance = 2, Phase = 2 }), Is.False);
            Assert.That(HuntBoss.Usable(desperate, new BossSituation { Distance = 2, Phase = 3 }));
        }

        [Test]
        public void ReadsTheFight()
        {
            var front = new MonsterAttackDef { id = "bite", range = 4, maxAngle = 100 };
            var tail = new MonsterAttackDef { id = "tail", range = 4, minAngle = 100 };
            var spin = new MonsterAttackDef { id = "spin", range = 4, aoe = "Circle", aoeRadius = 4 };
            var charge = new MonsterAttackDef { id = "charge", range = 12, minRange = 3, gapCloser = true, maxAngle = 90 };
            var behind = new BossSituation { Distance = 3, Angle = 150, Phase = 1 };
            Assert.That(HuntBoss.Weight(tail, behind), Is.GreaterThan(HuntBoss.Weight(spin, behind) * 2), "a hunter at its back meets the tail");
            Assert.That(HuntBoss.Usable(front, behind), Is.False);
            var far = new BossSituation { Distance = 9, Angle = 10, Phase = 1 };
            Assert.That(HuntBoss.Choose(new[] { front, tail, spin, charge }, far, new Random(3)).id, Is.EqualTo("charge"), "only the charge reaches 9 m");
            var healing = new BossSituation { Distance = 5, Angle = 10, Phase = 1, HunterHealing = true };
            Assert.That(HuntBoss.Weight(charge, healing), Is.GreaterThan(HuntBoss.Weight(charge, new BossSituation { Distance = 5, Angle = 10, Phase = 1 }) * 4), "drinking a potion invites a charge");
            var again = new BossSituation { Distance = 2, Angle = 10, Phase = 1, Last = "bite" };
            Assert.That(HuntBoss.Weight(front, again), Is.LessThan(HuntBoss.Weight(spin, again)), "rarely the same move twice");
            // Over many picks, a hunter behind is answered by the tail far more than by anything else.
            var rng = new Random(7);
            int tails = Enumerable.Range(0, 400).Count(_ => HuntBoss.Choose(new[] { front, tail, spin, charge }, behind, rng).id == "tail");
            Assert.That(tails, Is.GreaterThan(250));
        }

        [Test]
        public void CombosFollowUpWithinTheirPhase()
        {
            var a = new MonsterAttackDef { id = "charge", follow = "charge2", followChance = 1 };
            var b = new MonsterAttackDef { id = "charge2", phase = 2 };
            Assert.That(HuntBoss.FollowUp(new[] { a, b }, a, 1, new Random(1)), Is.Null, "the second charge is an enraged move");
            Assert.That(HuntBoss.FollowUp(new[] { a, b }, a, 2, new Random(1)).id, Is.EqualTo("charge2"));
            a.followChance = 0;
            Assert.That(HuntBoss.FollowUp(new[] { a, b }, a, 2, new Random(1)), Is.Null);
        }

        [Test]
        public void ForcedRoarAndCombosInTheBrain()
        {
            var def = HuntBoss.MakeHuntDef(Def("grave_warden"));
            var brain = new MonsterBrain(def) { Grace = 0 };
            var senses = new MonsterBrain.Senses { distanceToPlayer = 2, angleToPlayer = 0, lineOfSight = true, playerAlive = true };
            brain.Tick(.1f, senses); brain.Tick(.5f, senses);            // notices, closes in
            var roar = def.attacks.First(x => x.id == def.roarAttack);
            brain.ForceNext(roar);
            brain.Tick(.01f, senses);
            Assert.That(brain.Mode == MonsterMode.Telegraph && brain.CurrentAttack == roar, brain.Mode + " " + brain.CurrentAttack?.id);
            // A follow-up goes straight from recovery into the next move, no cooldown.
            var next = def.attacks.First(x => x.id != roar.id);
            brain.FollowUp = done => done == roar ? next : null;
            for (int i = 0; i < 400 && brain.CurrentAttack == roar; i++) brain.Tick(.02f, senses);
            Assert.That(brain.CurrentAttack, Is.EqualTo(next));
            Assert.That(brain.Mode, Is.EqualTo(MonsterMode.Telegraph));
        }
    }
}
