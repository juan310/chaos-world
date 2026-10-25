using System.Linq;
using FH.Core;
using NUnit.Framework;

namespace FH.Tests
{
    public class CombatTests
    {
        static AttackDef Swing => new AttackDef { id = "s", windup = .2f, active = .1f, recovery = .3f, comboWindow = .15f };

        [Test]
        public void TimelineNeverSkipsTheActiveWindowOnLongFrames()
        {
            var t = new AttackTimeline();
            t.Start(Swing);
            Assert.That(t.Phase, Is.EqualTo(AttackPhase.Windup));
            Assert.That(t.DamageWindowOpen, Is.False);
            var crossed = t.Tick(.5f); // one huge frame jumps from windup into recovery
            Assert.That(crossed, Is.EqualTo(new[] { AttackPhase.Active, AttackPhase.Recovery }));
            Assert.That(t.InChainWindow, Is.True);
            t.Tick(.2f);
            Assert.That(t.Running, Is.False);
        }

        [Test]
        public void DamageOnlyDuringActiveAndChainOnlyLateInRecovery()
        {
            var t = new AttackTimeline();
            t.Start(Swing);
            t.Tick(.19f); Assert.That(t.DamageWindowOpen, Is.False);
            t.Tick(.02f); Assert.That(t.DamageWindowOpen, Is.True);
            t.Tick(.1f); Assert.That(t.DamageWindowOpen, Is.False);
            Assert.That(t.InChainWindow, Is.False, "too early in recovery to chain");
            t.Tick(.18f); Assert.That(t.InChainWindow, Is.True);
        }

        [Test]
        public void EachTargetIsHitOncePerSwing()
        {
            var hits = new SwingHits();
            Assert.That(hits.TryRegister(1, "a"), Is.True);
            Assert.That(hits.TryRegister(1, "a"), Is.False);
            Assert.That(hits.TryRegister(1, "b"), Is.True);
            Assert.That(hits.TryRegister(2, "a"), Is.True, "a new swing may hit again");
        }

        [Test]
        public void StaminaGatesActionsAndExhaustionLasts()
        {
            var s = new StaminaPool(100, 25, .6f, 20);
            Assert.That(s.TrySpend(60), Is.True);
            Assert.That(s.TrySpend(60), Is.False, "insufficient stamina");
            Assert.That(s.Current, Is.EqualTo(40).Within(.001));
            s.Tick(.5f);
            Assert.That(s.Current, Is.EqualTo(40).Within(.001), "no regen inside the delay");
            s.Tick(.2f);
            Assert.That(s.Current, Is.GreaterThan(40));
            s.Drain(1000, 1);
            Assert.That(s.Exhausted, Is.True);
            Assert.That(s.CanSpend(1), Is.False);
            for (int i = 0; i < 20 && s.Exhausted; i++) s.Tick(.1f);
            for (int i = 0; i < 30 && s.Exhausted; i++) s.Tick(.1f);
            Assert.That(s.Exhausted, Is.False);
            Assert.That(s.Current, Is.GreaterThanOrEqualTo(20));
        }

        [Test]
        public void WeakPointIsGatedByBreakablePartAndResistancesApply()
        {
            var db = TestSupport.Db();
            var m = db.Monsters["gravebound"];
            var w = db.Weapons["hunter_blade"];
            var stats = new StatSheet();
            var parts = new PartTracker(m);
            var helmHit = DamageMath.PlayerHit(w, w.combo[0], stats, m, parts.Effective("helmet"), false, false);
            var skullHidden = parts.Effective("skull");
            Assert.That(skullHidden.weakPoint, Is.False, "skull is covered while the helm stands");
            Assert.That(helmHit.Damage, Is.EqualTo(15), "the helm absorbs 30% of the blow");
            Assert.That(parts.Damage("helmet", helmHit.Damage), Is.False, "one blow does not break a 22 hp helm");
            Assert.That(parts.Damage("helmet", helmHit.Damage), Is.True, "the second blow breaks it");
            var skull = parts.Effective("skull");
            Assert.That(skull.weakPoint, Is.True);
            var skullHit = DamageMath.PlayerHit(w, w.combo[0], stats, m, skull, false, false);
            Assert.That(skullHit.Damage, Is.GreaterThan(helmHit.Damage * 2));
            Assert.That(parts.Damage("helmet", 100), Is.False, "a broken part does not break twice");
            var fire = DamageMath.SpellHit(db.Spells["ember_bolt"], stats, m, null, false);
            Assert.That(fire.Damage, Is.EqualTo((float)System.Math.Round(24 * 1.3f)));
        }

        [Test]
        public void HitsOnASharedBoneStrikeTheHelmUntilItBreaksThenTheSkull()
        {
            var m = TestSupport.Db().Monsters["grave_warden"];
            var parts = new PartTracker(m);
            var head = m.parts.Where(p => p.bone == "head").Select(p => p.id).ToList();
            Assert.That(parts.Resolve(head).id, Is.EqualTo("helmet"));
            Assert.That(parts.Resolve(head).weakPoint, Is.False);
            parts.Damage("helmet", 999);
            var exposed = parts.Resolve(head);
            Assert.That(exposed.id, Is.EqualTo("skull"));
            Assert.That(exposed.weakPoint, Is.True);
            Assert.That(parts.Resolve(new[] { "body" }).id, Is.EqualTo("body"));
            var restored = new PartTracker(m, new[] { "helmet" });
            Assert.That(restored.Resolve(head).id, Is.EqualTo("skull"), "a helm broken before saving stays broken");
        }

        [Test]
        public void PoiseBreakCausesStaggerWhichEnds()
        {
            var def = TestSupport.Db().Monsters["gravebound"];
            var b = new MonsterBrain(def);
            var senses = new MonsterBrain.Senses { distanceToPlayer = 1.5f, lineOfSight = true, playerAlive = true, distanceFromHome = 1 };
            b.TakeHit(new HitInfo { Damage = 5, Poise = 20 });
            Assert.That(b.Mode, Is.Not.EqualTo(MonsterMode.Stagger));
            b.TakeHit(new HitInfo { Damage = 5, Poise = 20 });
            Assert.That(b.Mode, Is.EqualTo(MonsterMode.Stagger));
            Assert.That(b.Vulnerable);
            for (int i = 0; i < 20; i++) b.Tick(.1f, senses);
            Assert.That(b.Mode, Is.Not.EqualTo(MonsterMode.Stagger));
        }

        [Test]
        public void MonsterTelegraphsBeforeEveryAttackAndRespectsSpawnGrace()
        {
            var def = TestSupport.Db().Monsters["gravebound"];
            var b = new MonsterBrain(def);
            var near = new MonsterBrain.Senses { distanceToPlayer = 1.2f, lineOfSight = true, playerAlive = true, distanceFromHome = 1 };
            float t = 0, telegraphStart = -1, firstTelegraph = -1;
            int attacks = 0;
            MonsterMode last = b.Mode;
            for (int i = 0; i < 200; i++)
            {
                b.Tick(.05f, near); t += .05f;
                if (b.Mode == MonsterMode.Telegraph && last != MonsterMode.Telegraph) { telegraphStart = t; if (firstTelegraph < 0) firstTelegraph = t; }
                if (b.Mode == MonsterMode.Attack && last != MonsterMode.Attack)
                {
                    attacks++;
                    Assert.That(last, Is.EqualTo(MonsterMode.Telegraph), "every attack is preceded by its telegraph");
                    Assert.That(t - telegraphStart, Is.GreaterThanOrEqualTo(b.CurrentAttack.telegraph - .051f));
                }
                last = b.Mode;
            }
            Assert.That(attacks, Is.GreaterThanOrEqualTo(2));
            Assert.That(firstTelegraph, Is.GreaterThanOrEqualTo(def.spawnGrace - .001f), "no attack wind-up during spawn grace");
        }

        [Test]
        public void GraceAfterLoadOrPauseCancelsAPendingTelegraph()
        {
            var def = TestSupport.Db().Monsters["gravebound"];
            var b = new MonsterBrain(def);
            var near = new MonsterBrain.Senses { distanceToPlayer = 1.2f, lineOfSight = true, playerAlive = true, distanceFromHome = 1 };
            for (int i = 0; i < 200 && b.Mode != MonsterMode.Telegraph; i++) b.Tick(.05f, near);
            Assert.That(b.Mode, Is.EqualTo(MonsterMode.Telegraph));
            b.AddGrace(1.0f);
            Assert.That(b.Mode, Is.EqualTo(MonsterMode.Chase));
            for (int i = 0; i < 19; i++) { b.Tick(.05f, near); Assert.That(b.AttackActive, Is.False); }
        }

        [Test]
        public void LeashSendsMonsterHomeAndResetsIt()
        {
            var def = TestSupport.Db().Monsters["gravebound"];
            var b = new MonsterBrain(def);
            b.Grace = 0;
            var seen = new MonsterBrain.Senses { distanceToPlayer = 6, lineOfSight = true, playerAlive = true, distanceFromHome = 2 };
            for (int i = 0; i < 20; i++) b.Tick(.05f, seen);
            Assert.That(b.Mode, Is.EqualTo(MonsterMode.Chase));
            b.TakeHit(new HitInfo { Damage = 40, Poise = 0 });
            var far = seen; far.distanceFromHome = def.leashRadius + 1;
            b.Tick(.05f, far);
            Assert.That(b.Mode, Is.EqualTo(MonsterMode.Return));
            var home = far; home.distanceFromHome = .5f; home.distanceToPlayer = 30; home.lineOfSight = false;
            b.Tick(.05f, home);
            Assert.That(b.Mode, Is.EqualTo(MonsterMode.Idle));
            Assert.That(b.Health, Is.EqualTo(def.maxHealth));
        }

        [Test]
        public void PerceptionUsesSightConeAndHearing()
        {
            var b = new MonsterBrain(TestSupport.Db().Monsters["gravebound"]);
            var behind = new MonsterBrain.Senses { distanceToPlayer = 8, angleToPlayer = 170, lineOfSight = true, playerAlive = true };
            Assert.That(b.CanPerceive(behind), Is.False);
            behind.distanceToPlayer = 2;
            Assert.That(b.CanPerceive(behind), Is.True, "heard up close");
            var blocked = new MonsterBrain.Senses { distanceToPlayer = 8, angleToPlayer = 0, lineOfSight = false, playerAlive = true };
            Assert.That(b.CanPerceive(blocked), Is.False);
        }

        [Test]
        public void BurnBuildsUpTriggersTicksAndRespectsImmunity()
        {
            var db = TestSupport.Db();
            var s = new StatusTracker();
            Assert.That(s.Add(db.Statuses["burn"], 45), Is.False);
            Assert.That(s.Add(db.Statuses["burn"], 60), Is.True);
            float dmg = 0;
            for (int i = 0; i < 50; i++) dmg += s.Tick(.1f);
            Assert.That(dmg, Is.EqualTo(24).Within(.01), "8 ticks of 3 over 4 seconds");
            Assert.That(s.IsActive("burn"), Is.False);
            var warden = new MonsterBrain(db.Monsters["grave_warden"]);
            Assert.That(warden.Status.Add(db.Statuses["chill"], 500), Is.False);
        }

        [Test]
        public void ArmorReducesIncomingDamage()
        {
            var atk = TestSupport.Db().Monsters["gravebound"].attacks[0];
            float bare = DamageMath.MonsterHit(atk, 0, null);
            float armored = DamageMath.MonsterHit(atk, 14, new[] { new Resistance { type = DamageType.Slash, multiplier = .9f } });
            Assert.That(bare, Is.EqualTo(13));
            Assert.That(armored, Is.LessThan(bare));
        }
    }

    public class ProgressionTests
    {
        [Test]
        public void XpLevelsUpAndGrantsPoints()
        {
            var core = TestSupport.NewCore();
            int need = core.Progression.XpToNext(1);
            Assert.That(core.Progression.AddXp(need - 1), Is.EqualTo(0));
            Assert.That(core.Progression.AddXp(1), Is.EqualTo(1));
            Assert.That(core.State.player.level, Is.EqualTo(2));
            Assert.That(core.State.player.attributePoints, Is.EqualTo(2));
            Assert.That(core.State.player.skillPoints, Is.EqualTo(1));
            Assert.That(core.Progression.AddXp(-50), Is.EqualTo(0));
        }

        [Test]
        public void SkillTreeEnforcesPrerequisitesLevelsAndCostsThenChangesStats()
        {
            var core = TestSupport.NewCore();
            Assert.That(core.Progression.Unlock("blade_edge").Reason, Does.Contain("skill point"));
            core.State.player.skillPoints = 5;
            Assert.That(core.Progression.Unlock("blade_weight").Reason, Does.Contain("level 2"));
            core.State.player.level = 2;
            Assert.That(core.Progression.Unlock("blade_weight").Reason, Does.Contain("Keen Edge first"));
            float atk = core.Stats.Compute()[Stat.Attack];
            Assert.That(core.Progression.Unlock("blade_edge").Ok);
            Assert.That(core.Stats.Compute()[Stat.Attack], Is.EqualTo(atk + 4).Within(.001));
            Assert.That(core.Progression.Unlock("blade_edge").Ok, Is.False, "cannot learn twice");
            Assert.That(core.State.player.skillPoints, Is.EqualTo(4));
        }

        [Test]
        public void AttributesRequirePointsAndScaleStats()
        {
            var core = TestSupport.NewCore();
            Assert.That(core.Progression.SpendAttribute("vigor").Ok, Is.False);
            core.State.player.attributePoints = 2;
            float hp = core.Stats.Compute()[Stat.MaxHealth];
            core.Progression.SpendAttribute("vigor");
            core.Progression.SpendAttribute("vigor");
            Assert.That(core.Stats.Compute()[Stat.MaxHealth], Is.EqualTo(hp + 16).Within(.001));
            Assert.That(core.State.player.attributePoints, Is.EqualTo(0));
        }
    }
}
