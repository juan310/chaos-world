using System;
using System.Collections.Generic;
using System.Linq;

namespace FH.Core
{
    // Monster Hunter-style melee for the hunter: committed moves with motion values, routes between moves chosen by
    // input during a chain window, roll cancels late in a move, charged heavy attacks, shield guard with a perfect
    // window, hit reactions by attack weight, hitstop, and monster flinch / topple / enrage. Engine-independent.

    public enum HuntInput { None, Light, Heavy, LightHeavy, ForwardLight }
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
        public HuntMoveDef Clone() => (HuntMoveDef)MemberwiseClone();
    }

    [Serializable]
    public class HuntMovesetDef
    {
        public string id, name;
        public string light, heavy, lightHeavy, forwardLight;   // openers from neutral
        public string drawAttack, rollAttack, counter;          // special openers
        public float rollAttackWindow = .4f, counterWindow = .6f, perfectGuardWindow = .15f;
        public float guardAngle = 70, guardChip = .12f, guardStaminaPerDamage = 1.1f, guardPushback = .45f;
        public float drawnSpeed = .85f, sheatheTime = .45f, drawTime = .25f;
        public List<float> chargeMultipliers = new List<float> { 1f, 1.45f, 1.9f };
        public List<HuntMoveDef> moves = new List<HuntMoveDef>();

        public HuntMoveDef Move(string id) => string.IsNullOrEmpty(id) ? null : moves.FirstOrDefault(m => m.id == id);
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
