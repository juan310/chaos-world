using System;
using System.Collections.Generic;
using System.Linq;

namespace FH.Core
{
    // Monster Hunter-style weapons for the hunter: committed moves with motion values, routes between moves chosen by
    // input during a chain window, roll cancels late in a move, charged heavy attacks, shield guard with a perfect
    // window, hit reactions by attack weight, hitstop, and monster flinch / topple / enrage. Three weapon classes share
    // it: sword and shield, lance (guard, counter stance, hops, dash attack) and bow (charge levels, charging
    // sidestep, power shot, arc shot, dragon piercer). Engine-independent.

    public enum HuntInput { None, Light, Heavy, LightHeavy, ForwardLight }
    public enum WeaponClass { SwordShield, Lance, Bow }
    public enum DodgeStyle { Roll, Hop, Sidestep }

    /// <summary>What a ranged move fires. Arrows fan out by <see cref="spread"/> degrees; pierce passes through and hits
    /// each body part once per <see cref="pierceInterval"/>; arc arrows rise and rain down around the aim point.</summary>
    [Serializable]
    public class ShotDef
    {
        public int arrows = 1;
        public float spread = 6, speed = 55, range = 40;
        public bool usesChargeLevel = true;          // arrows and per-arrow power from the bow's charge level table
        public int bonusArrows; public float spreadScale = 1;   // power shot: a wider fan with extra arrows
        public bool pierce; public float pierceInterval = .1f; public int pierceHits = 6;
        public bool arc; public float arcRadius = 2.6f, arcDelay = .8f;
    }

    /// <summary>One bow charge level: how many arrows a normal shot looses and each arrow's motion value.</summary>
    [Serializable]
    public class ShotLevelDef
    {
        public int arrows = 1;
        public float spread = 5, motionValue = .5f, poise = .4f;
    }
    public enum HitReaction { None, Flinch, Knockback, Knockdown }

    /// <summary>One move. Timings, reach and multipliers come from <see cref="AttackDef"/>; the rest is hunt-specific.</summary>
    [Serializable]
    public class HuntMoveDef : AttackDef
    {
        public string name;
        public float hitstop = .05f;        // seconds both fighters freeze when this move registers a hit
        public float koDamage;              // stun towards a topple (shield bash, overheads)
        public float chargeTime;            // > 0: holding the button charges; each chargeTime adds a level (max 2)
        public float rollCancel = .55f;     // normalized time after which a roll may cancel the move
        public float chainFrom = .45f;      // normalized time from which the next move may start (buffered earlier)
        public bool guardPoint;             // the shield blocks frontal hits during windup
        public float shake = .12f, rumble = .25f;
        public string onLight, onHeavy, onLightHeavy;   // next move by input
        public string maxChargeMove;        // released at full charge instead of this move (e.g. the charged spin)
        public bool guardPointActive;       // the shield also blocks during the active window (lance guard dash, charge)
        public float travel;                // m/s carried forward during the active window (lance dash attack)
        public float steer = 90;            // degrees per second the stick can turn a travelling move
        public float rehit;                 // > 0: the active window strikes again every rehit seconds (multi-hit)
        public float staminaPerSecond;      // drained during the active window; empty stamina ends it early
        public ShotDef shot;                // ranged: what the move fires at the start of its active window
        public HuntMoveDef Clone() => (HuntMoveDef)MemberwiseClone();
    }

    [Serializable]
    public class HuntMovesetDef
    {
        public string id, name;
        public string weaponClass = "SwordShield";
        public WeaponDef weapon;                                // own damage (null: the equipped weapon's)
        public string light, heavy, lightHeavy, forwardLight;   // openers from neutral
        public string drawAttack, rollAttack, counter;          // special openers
        public float rollAttackWindow = .4f, counterWindow = .6f, perfectGuardWindow = .15f;
        public float guardAngle = 70, guardChip = .12f, guardStaminaPerDamage = 1.1f, guardPushback = .45f;
        public float drawnSpeed = .85f, sheatheTime = .45f, drawTime = .25f;
        public List<float> chargeMultipliers = new List<float> { 1f, 1.45f, 1.9f };
        public List<HuntMoveDef> moves = new List<HuntMoveDef>();

        // Dodge: sword and shield rolls; the lance hops (short, quick, chainable); the bow sidesteps (and charges).
        public string dodge = "Roll";
        public float dodgeDistance = 3f, dodgeTime = .42f, dodgeStamina = 25, dodgeInvulnerable = .25f;
        public int maxChainedDodges = 1;
        public float chainedDodgeWindow = .35f;

        // Lance: guard moves.
        public string guardAttack, guardDash, counterRelease, retribution, dashAttack, dashFinisher;
        public float counterStanceTime = 1.1f;

        // Bow: shots and charge.
        public string shot, chargedShot, powerShot, arcShot, dragonPiercer;
        public List<float> chargeTimes = new List<float> { .4f, .85f };   // hold to reach level 2, then level 3
        public List<ShotLevelDef> shotLevels = new List<ShotLevelDef>();
        public float chainShotWindow = .8f, chargeStaminaPerSecond = 10;

        public HuntMoveDef Move(string id) => string.IsNullOrEmpty(id) ? null : moves.FirstOrDefault(m => m.id == id);
        public WeaponClass Class => Enum.TryParse(weaponClass, true, out WeaponClass c) ? c : WeaponClass.SwordShield;
        public DodgeStyle Dodge => Enum.TryParse(dodge, true, out DodgeStyle d) ? d : DodgeStyle.Roll;
        public ShotLevelDef Level(int level) => shotLevels.Count == 0 ? new ShotLevelDef() : shotLevels[Math.Max(0, Math.Min(level, shotLevels.Count) - 1)];
        public int MaxLevel => Math.Max(1, shotLevels.Count);
    }

    /// <summary>Monster Hunter bow charge: holding the draw climbs levels over time; each shot that follows a shot
    /// quickly is one level higher; a charging sidestep adds a level (and keeps it); a power shot fires at the current
    /// level without raising it. Levels run 1..max; a pause longer than the chain window drops back to 1.</summary>
    public class BowCharge
    {
        readonly HuntMovesetDef set;
        int carried = 1, drawBase = 1;
        float carriedUntil = -99, drawStart = -1;
        public bool Drawing => drawStart >= 0;

        public BowCharge(HuntMovesetDef set) { this.set = set; }

        /// <summary>The level the next shot fires at if loosed now (without drawing).</summary>
        public int Ready(float now) => now <= carriedUntil ? carried : 1;

        public void BeginDraw(float now) { drawBase = Ready(now); drawStart = now; }
        public void CancelDraw() { drawStart = -1; }

        /// <summary>Level while the draw is held at <paramref name="now"/>.</summary>
        public int Held(float now)
        {
            if (!Drawing) return Ready(now);
            float held = now - drawStart; int add = 0;
            foreach (var t in set.chargeTimes) if (held >= t) add++;
            return Math.Min(set.MaxLevel, drawBase + add);
        }

        /// <summary>A shot was loosed at <paramref name="level"/>: the next one, if quick, is a level higher.</summary>
        public void Fired(int level, float now) { drawStart = -1; carried = Math.Min(set.MaxLevel, level + 1); carriedUntil = now + set.chainShotWindow; }

        /// <summary>Power shot: fires at the current level, keeps the chain going, does not raise it.</summary>
        public void PowerShot(int level, float now) { drawStart = -1; carried = level; carriedUntil = now + set.chainShotWindow; }

        /// <summary>Charging sidestep: +1 level. While drawing, the draw continues from the raised level.</summary>
        public int Stepped(float now)
        {
            int level = Math.Min(set.MaxLevel, Held(now) + 1);
            carried = level; carriedUntil = now + set.chainShotWindow + .4f;
            if (Drawing) { drawBase = level; drawStart = now; }
            return level;
        }

        public void Reset() { carried = 1; carriedUntil = -99; drawStart = -1; }
    }

    /// <summary>Yaw offsets (degrees) of a fan of arrows, centred on the aim.</summary>
    public static class ShotPattern
    {
        public static float[] Fan(int arrows, float spread)
        {
            var r = new float[Math.Max(1, arrows)];
            for (int i = 0; i < r.Length; i++) r[i] = (i - (r.Length - 1) * .5f) * spread;
            return r;
        }

        /// <summary>Ground points arc arrows land on: a ring around the aim point, the first one at its centre.</summary>
        public static List<(float x, float z)> Rain(int arrows, float radius)
        {
            var r = new List<(float, float)> { (0, 0) };
            for (int i = 1; i < arrows; i++)
            {
                double a = i * 2.39996;                                 // golden angle: even coverage
                float d = radius * (float)Math.Sqrt(i / (double)Math.Max(1, arrows - 1));
                r.Add(((float)(Math.Cos(a) * d), (float)(Math.Sin(a) * d)));
            }
            return r;
        }
    }

    /// <summary>Lance hops / bow sidesteps / rolls: consecutive dodges (inside the chain window) up to a limit.</summary>
    public class DodgeChain
    {
        int count; float lastEnd = -99;
        public int Count => count;
        /// <summary>Whether another dodge may start now; counts it if so. The last allowed hop of a chain is longer.</summary>
        public bool TryStart(HuntMovesetDef set, float now, out bool last)
        {
            if (now - lastEnd > set.chainedDodgeWindow) count = 0;
            last = false;
            if (count >= Math.Max(1, set.maxChainedDodges)) return false;
            count++;
            last = count == set.maxChainedDodges && set.maxChainedDodges > 1;
            return true;
        }
        public void Ended(float now) => lastEnd = now;
        public void Reset() { count = 0; lastEnd = -99; }
    }

    /// <summary>Which move an input starts, from neutral or from inside a move.</summary>
    public static class HuntRoutes
    {
        /// <summary>From neutral. Sheathed: any attack is the draw attack. Right after a roll: roll slash. Right after a
        /// perfect guard: counter. Otherwise the opener for the input.</summary>
        public static HuntMoveDef Opener(HuntMovesetDef set, HuntInput input, bool sheathed, float sinceRoll, float sincePerfectGuard)
        {
            if (input == HuntInput.None) return null;
            if (sheathed) return set.Move(set.drawAttack);
            if ((input == HuntInput.Light || input == HuntInput.ForwardLight) && sincePerfectGuard <= set.counterWindow && set.Move(set.counter) != null) return set.Move(set.counter);
            if ((input == HuntInput.Light || input == HuntInput.ForwardLight) && sinceRoll <= set.rollAttackWindow && set.Move(set.rollAttack) != null) return set.Move(set.rollAttack);
            switch (input)
            {
                case HuntInput.Light: return set.Move(set.light);
                case HuntInput.ForwardLight: return set.Move(set.forwardLight) ?? set.Move(set.light);
                case HuntInput.Heavy: return set.Move(set.heavy);
                case HuntInput.LightHeavy: return set.Move(set.lightHeavy);
                default: return null;
            }
        }

        /// <summary>The follow-up of <paramref name="current"/> for an input, or null when the move has no such route.</summary>
        public static HuntMoveDef Next(HuntMovesetDef set, HuntMoveDef current, HuntInput input)
        {
            if (current == null) return null;
            switch (input)
            {
                case HuntInput.Light: case HuntInput.ForwardLight: return set.Move(current.onLight);
                case HuntInput.Heavy: return set.Move(current.onHeavy);
                case HuntInput.LightHeavy: return set.Move(current.onLightHeavy) ?? set.Move(set.lightHeavy);
                default: return null;
            }
        }

        public static bool CanChain(HuntMoveDef move, float normalized) => move != null && normalized >= move.chainFrom;
        public static bool CanRollCancel(HuntMoveDef move, float normalized) => move == null || normalized >= move.rollCancel;
    }

    /// <summary>Holding the heavy button: level 0 until chargeTime, then 1, then 2 (max).</summary>
    public static class ChargeMeter
    {
        public static int Level(HuntMoveDef move, float held)
        {
            if (move == null || move.chargeTime <= 0) return 0;
            return held >= move.chargeTime * 2 ? 2 : held >= move.chargeTime ? 1 : 0;
        }

        /// <summary>A copy of the move with the charge level's damage, poise, stun and hitstop scaling applied.</summary>
        public static HuntMoveDef Charged(HuntMovesetDef set, HuntMoveDef move, int level)
        {
            if (level <= 0) return move;
            float k = set.chargeMultipliers[Math.Min(level, set.chargeMultipliers.Count - 1)];
            var c = move.Clone();
            c.id = move.id + "_charged" + level;
            c.damageMultiplier = move.damageMultiplier * k;
            c.poiseMultiplier = move.poiseMultiplier * k;
            c.koDamage = move.koDamage * k;
            c.hitstop = move.hitstop * (1 + .35f * level);
            c.shake = move.shake * (1 + .5f * level);
            c.rumble = Math.Min(1, move.rumble * (1 + .6f * level));
            c.radius = move.radius * (1 + .15f * level);
            return c;
        }
    }

    public struct GuardResult
    {
        public bool Blocked, Perfect;
        public float Damage, Stamina, Pushback;
    }

    public static class GuardMath
    {
        /// <summary>Resolves a monster hit against a raised shield. <paramref name="angleFromFront"/> is the angle
        /// between the hunter's facing and the direction to the attacker (0 = straight ahead).</summary>
        public static GuardResult Resolve(HuntMovesetDef set, float damage, float angleFromFront, float sinceGuardRaised, float stamina)
        {
            var r = new GuardResult();
            if (Math.Abs(angleFromFront) > set.guardAngle) return r;
            r.Blocked = true;
            r.Perfect = sinceGuardRaised <= set.perfectGuardWindow;
            if (r.Perfect) return r;                         // perfect guard: no chip, no stamina, no pushback
            r.Stamina = damage * set.guardStaminaPerDamage;
            r.Damage = damage * set.guardChip;
            r.Pushback = set.guardPushback * (damage >= 25 ? 1.8f : 1f);
            if (r.Stamina > stamina) { r.Damage += damage * .35f; r.Pushback *= 1.6f; }   // guard broken through: it still helps a little
            return r;
        }
    }

    public static class Reactions
    {
        /// <summary>How hard a monster hit throws the hunter around: explicit reaction, else by damage.</summary>
        public static HitReaction For(MonsterAttackDef attack, float damageTaken)
        {
            if (!string.IsNullOrEmpty(attack.reaction) && Enum.TryParse(attack.reaction, true, out HitReaction r)) return r;
            if (damageTaken >= 26 || attack.weight >= 2) return HitReaction.Knockdown;
            if (damageTaken >= 15) return HitReaction.Knockback;
            return HitReaction.Flinch;
        }
    }

    /// <summary>Monster Hunter-style monster reactions: part damage builds flinches, stun builds a topple, and the
    /// monster enrages below half health (faster, shorter cooldowns) until it has been toppled.</summary>
    public class MonsterReactions
    {
        readonly MonsterDef def;
        float flinchBuild, stunBuild;
        int topples;
        public bool Enraged { get; private set; }
        public int Flinches { get; private set; }
        public int Topples => topples;
        public float FlinchBuild => flinchBuild;
        public float StunBuild => stunBuild;

        public MonsterReactions(MonsterDef def) { this.def = def; }

        public float FlinchThreshold => def.flinchThreshold > 0 ? def.flinchThreshold : def.maxHealth * .14f;
        public float StunThreshold => (def.stunThreshold > 0 ? def.stunThreshold : def.maxHealth * .3f) * (1 + .5f * topples);
        public float SpeedMultiplier => Enraged ? 1.18f : 1f;
        public float CooldownMultiplier => Enraged ? .65f : 1f;

        public enum Outcome { None, Flinch, Topple }

        /// <summary>Registers one hit (damage, stun) and the monster's health after it; returns the reaction it causes.</summary>
        public Outcome Hit(float damage, float stun, float healthAfter)
        {
            if (!Enraged && healthAfter > 0 && healthAfter <= def.maxHealth * .5f) Enraged = true;
            stunBuild += stun;
            if (stun > 0 && stunBuild >= StunThreshold) { stunBuild = 0; topples++; Enraged = false; return Outcome.Topple; }
            flinchBuild += damage;
            if (flinchBuild >= FlinchThreshold) { flinchBuild -= FlinchThreshold; Flinches++; return Outcome.Flinch; }
            return Outcome.None;
        }
    }
}
