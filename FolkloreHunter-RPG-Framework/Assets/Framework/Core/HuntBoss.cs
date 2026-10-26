using System;
using System.Collections.Generic;
using System.Linq;

namespace FH.Core
{
    // Monster Hunter-style bosses for the hunt rules: tougher stats, area attacks with ground telegraphs (slams,
    // shockwaves, sweeps all round, breath cones, charges, eruptions under the hunter, leaps, roars), phases (enraged
    // attacks, desperate attacks at low health), and a choice of attack that reads the fight: what is behind it, a hunter
    // at range or drinking a potion, combos, not the same move twice in a row. Engine-independent.

    public enum AoeShape { None, Circle, Ring, Cone, Line, Roar, Burst, Leap }

    /// <summary>What the monster knows when it picks an attack.</summary>
    public struct BossSituation
    {
        public float Distance, Angle;       // to the hunter; angle 0 = straight ahead, 180 = behind
        public int Phase;
        public bool HunterHealing;
        public string Last;                 // the previous attack's id
    }

    public static class HuntBoss
    {
        /// <summary>The hunt-rules version of a monster: health, poise and damage scaled, the boss attacks added.</summary>
        public static MonsterDef MakeHuntDef(MonsterDef def)
        {
            var h = def.CloneShallow();
            h.maxHealth = def.maxHealth * Math.Max(1, def.huntHealth);
            h.poise = def.poise * Math.Max(1, def.huntPoise);
            h.attacks = def.attacks.Concat(def.huntAttacks ?? new List<MonsterAttackDef>()).Select(a =>
            {
                var c = a.Clone(); c.damage = a.damage * Math.Max(.1f, def.huntDamage); return c;
            }).ToList();
            h.huntAttacks = new List<MonsterAttackDef>();
            return h;
        }

        /// <summary>1 normal, 2 enraged, 3 desperate (under 30 % health: the most dangerous moves come out).</summary>
        public static int Phase(float healthFraction, bool enraged) => healthFraction <= .3f ? 3 : enraged ? 2 : 1;

        /// <summary>Can this attack be used from here (range, angle, phase)?</summary>
        public static bool Usable(MonsterAttackDef a, BossSituation s)
            => a.weight >= 0 && a.phase <= s.Phase && s.Distance <= a.range && s.Distance >= a.minRange && s.Angle >= a.minAngle && s.Angle <= a.maxAngle;

        /// <summary>How much the monster wants this attack now.</summary>
        public static float Weight(MonsterAttackDef a, BossSituation s)
        {
            float w = a.weight > 0 ? a.weight : 1;
            bool behind = s.Angle > 100, far = s.Distance > 6;
            if (behind && a.minAngle >= 90) w *= 3;                                    // a hunter at its back meets the tail
            if (far && a.gapCloser) w *= 3;                                            // a hunter out of reach gets charged
            if (s.HunterHealing && (a.gapCloser || a.Shape == AoeShape.Burst || a.Shape == AoeShape.Leap)) w *= 5;   // punish the potion
            if (s.Phase > 1 && a.phase == s.Phase) w *= 2;                             // the new phase shows off its new moves
            if (a.id == s.Last) w *= .35f;                                             // rarely the same move twice
            return w;
        }

        /// <summary>A weighted random pick among the usable attacks (null when none can reach).</summary>
        public static MonsterAttackDef Choose(IEnumerable<MonsterAttackDef> attacks, BossSituation s, Random rng, Func<MonsterAttackDef, bool> filter = null)
        {
            var usable = attacks.Where(a => Usable(a, s) && (filter == null || filter(a))).ToList();
            if (usable.Count == 0) return null;
            float total = usable.Sum(a => Weight(a, s)), pick = (float)rng.NextDouble() * total;
            foreach (var a in usable) { pick -= Weight(a, s); if (pick <= 0) return a; }
            return usable[usable.Count - 1];
        }

        /// <summary>The combo follow-up of an attack (if it has one, the dice allow it and its phase is open).</summary>
        public static MonsterAttackDef FollowUp(IEnumerable<MonsterAttackDef> attacks, MonsterAttackDef done, int phase, Random rng)
        {
            if (done == null || string.IsNullOrEmpty(done.follow) || rng.NextDouble() >= done.followChance) return null;
            var f = attacks.FirstOrDefault(a => a.id == done.follow);
            return f != null && f.phase <= phase ? f : null;
        }

        /// <summary>Does the area attack <paramref name="a"/>, fired from (ox, oz) facing (fx, fz), hit a target at (tx, tz)
        /// <paramref name="t"/> seconds after it went off? (Ring: only the expanding wave front hits: roll through it.)</summary>
        public static bool Hits(MonsterAttackDef a, float ox, float oz, float fx, float fz, float tx, float tz, float t)
        {
            float dx = tx - ox, dz = tz - oz, d = (float)Math.Sqrt(dx * dx + dz * dz);
            switch (a.Shape)
            {
                case AoeShape.Circle: case AoeShape.Roar: case AoeShape.Burst: case AoeShape.Leap:
                    return d <= a.aoeRadius;
                case AoeShape.Ring:
                {
                    float r = WaveRadius(a, t);
                    return r > 0 && r < a.aoeRadius + .001f && Math.Abs(d - r) <= a.aoeWidth * .5f;
                }
                case AoeShape.Cone:
                {
                    if (d > a.aoeLength) return false;
                    if (d < .5f) return true;
                    float fl = (float)Math.Sqrt(fx * fx + fz * fz); if (fl < 1e-4f) return false;
                    double cos = (dx * fx + dz * fz) / (d * fl);
                    return Math.Acos(Math.Max(-1, Math.Min(1, cos))) * 180 / Math.PI <= a.aoeAngle * .5f;
                }
                case AoeShape.Line:
                {
                    float fl = (float)Math.Sqrt(fx * fx + fz * fz); if (fl < 1e-4f) return false;
                    float ux = fx / fl, uz = fz / fl;
                    float along = dx * ux + dz * uz, across = Math.Abs(-dx * uz + dz * ux);
                    return along >= 0 && along <= a.aoeLength && across <= a.aoeWidth * .5f;
                }
                default: return false;
            }
        }

        /// <summary>A shockwave's radius <paramref name="t"/> seconds after the slam (it stops at its full size).</summary>
        public static float WaveRadius(MonsterAttackDef a, float t) => Math.Min(a.aoeRadius, Math.Max(0, t) * a.aoeSpeed);

        /// <summary>How long the area stays dangerous after it goes off.</summary>
        public static float Lifetime(MonsterAttackDef a) => a.Shape == AoeShape.Ring ? a.aoeRadius / Math.Max(.1f, a.aoeSpeed) + .05f : .12f;

        /// <summary>How far from the monster the attack can reach (companions keep out of it).</summary>
        public static float ThreatRadius(MonsterAttackDef a)
        {
            switch (a.Shape)
            {
                case AoeShape.Circle: case AoeShape.Ring: case AoeShape.Roar: return a.aoeRadius;
                case AoeShape.Cone: case AoeShape.Line: return a.aoeLength;
                case AoeShape.Burst: case AoeShape.Leap: return a.range + a.aoeRadius;
                default: return a.range + a.radius;
            }
        }

        /// <summary>Where eruptions break out: the first under the hunter, the others around it.</summary>
        public static List<(float x, float z)> BurstPoints(int count, float spread, float tx, float tz, Random rng)
        {
            var r = new List<(float, float)> { (tx, tz) };
            double start = rng.NextDouble() * Math.PI * 2;
            for (int i = 1; i < count; i++)
            {
                double ang = start + i * Math.PI * 2 / Math.Max(1, count - 1);
                r.Add(((float)(tx + Math.Cos(ang) * spread), (float)(tz + Math.Sin(ang) * spread)));
            }
            return r;
        }
    }
}
