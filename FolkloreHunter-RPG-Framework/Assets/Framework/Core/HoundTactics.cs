using System;

namespace FH.Core
{
    // The Briarhound fights and rides like Monster Hunter Rise's Palamute: a three-hit blade combo, a pounce to close
    // distance, a flanking (pincer) position across the monster from the hunter, hopping clear of a monster's wind-up;
    // ridden, it lopes, dashes, drifts through sharp turns, attacks on the rider's command and sidesteps. Engine-independent.

    public enum HoundAction { Follow, Approach, Attack, Pounce, Evade, Reposition }

    /// <summary>One strike of the hound's combo: its clip, when it lands (seconds), its reach, damage and stun.</summary>
    public struct HoundStrike
    {
        public string Clip; public float Length, HitAt, Reach, Radius, Damage, Stun, Speed;
        public bool AllAround;   // the spin strikes everything around it
    }

    public static class HoundTactics
    {
        /// <summary>Blade bite (right to left), return slash (left to right), spinning slash.</summary>
        public static readonly HoundStrike[] Combo =
        {
            new HoundStrike { Clip = "Bite", Length = .82f, HitAt = .36f, Reach = 2.4f, Radius = 1.1f, Damage = 9, Stun = 3, Speed = 1.1f },
            new HoundStrike { Clip = "Slash_R", Length = .55f, HitAt = .24f, Reach = 2.4f, Radius = 1.1f, Damage = 8, Stun = 2, Speed = 1.1f },
            new HoundStrike { Clip = "Spin", Length = .85f, HitAt = .42f, Reach = 2.6f, Radius = 2.6f, Damage = 14, Stun = 4, Speed = 1f, AllAround = true },
        };
        public static readonly HoundStrike PounceStrike = new HoundStrike { Clip = "Pounce", Length = .9f, HitAt = .55f, Reach = 2.6f, Radius = 1.3f, Damage = 12, Stun = 5, Speed = 1 };

        public const float ComboCooldown = 2.4f, PounceMin = 4.5f, PounceMax = 9f, PounceCooldown = 6f, EvadeCooldown = 1.2f;
        public const float EvadeDistance = 3.2f, EvadeTime = .45f;
        /// <summary>Extra clearance (metres) the hound keeps from a monster's swing when it reads a wind-up.</summary>
        public const float DangerMargin = 1.5f;

        /// <summary>What the hound does next while it assists. <paramref name="gap"/>: distance from its muzzle to the
        /// monster's body; <paramref name="danger"/>: the monster is winding up and the hound is inside its reach.</summary>
        public static HoundAction Choose(float gap, float reach, bool danger, bool canEvade, bool comboReady, bool pounceReady, bool atFlank)
        {
            if (danger && canEvade) return HoundAction.Evade;
            if (danger) return HoundAction.Reposition;
            if (gap <= reach && comboReady) return HoundAction.Attack;
            if (gap >= PounceMin && gap <= PounceMax && pounceReady && comboReady) return HoundAction.Pounce;
            if (!atFlank || gap > reach) return HoundAction.Approach;
            return HoundAction.Reposition;   // in position, waiting for the combo to come back: circle a little
        }

        /// <summary>Pincer position (x, z): across the monster from the hunter, turned <paramref name="spreadDeg"/>
        /// off the far side so the two do not stand in one line, at <paramref name="distance"/> from its centre.</summary>
        public static (float x, float z) FlankPoint(float mx, float mz, float hx, float hz, float distance, float spreadDeg = 35)
        {
            float dx = mx - hx, dz = mz - hz;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-3f) { dx = 0; dz = 1; len = 1; }
            dx /= len; dz /= len;
            double a = spreadDeg * Math.PI / 180;
            float rx = (float)(dx * Math.Cos(a) - dz * Math.Sin(a)), rz = (float)(dx * Math.Sin(a) + dz * Math.Cos(a));
            return (mx + rx * distance, mz + rz * distance);
        }

        /// <summary>Angle (degrees) at the monster between the hunter and the hound: 180 is a perfect pincer.</summary>
        public static float PincerAngle(float mx, float mz, float hx, float hz, float dx, float dz)
        {
            float ax = hx - mx, az = hz - mz, bx = dx - mx, bz = dz - mz;
            double la = Math.Sqrt(ax * ax + az * az), lb = Math.Sqrt(bx * bx + bz * bz);
            if (la < 1e-4 || lb < 1e-4) return 0;
            double c = Math.Max(-1, Math.Min(1, (ax * bx + az * bz) / (la * lb)));
            return (float)(Math.Acos(c) * 180 / Math.PI);
        }

        /// <summary>The next strike of a combo started at index 0, or -1 when the combo is over.</summary>
        public static int NextStrike(int current) => current + 1 < Combo.Length ? current + 1 : -1;

        // ---------------- ridden ----------------
        /// <summary>Ridden speeds as fractions of the gallop: walk under half stick, lope at full stick, dash on RT.</summary>
        public const float LopeFraction = .62f, DriftSpeedFraction = .8f;
        /// <summary>Turn rates (1/s for the exponential steer): walking, loping, dashing, drifting.</summary>
        public const float TurnWalk = 8, TurnLope = 6, TurnDash = 3.2f, TurnDrift = 11;
        public const float RideHopDistance = 2.6f, RideHopTime = .4f, RideHopInvulnerable = .3f, RideHopCooldown = .5f;

        public static float RideSpeed(float stick, bool dash, bool drift, float walkSpeed, float gallopSpeed)
        {
            if (stick < .05f) return 0;
            if (drift) return gallopSpeed * DriftSpeedFraction;
            if (dash) return gallopSpeed * stick;
            return stick < .5f ? walkSpeed * stick * 2 : gallopSpeed * LopeFraction * stick;
        }

        public static float TurnRate(float stick, bool dash, bool drift) => drift ? TurnDrift : dash ? TurnDash : stick < .5f ? TurnWalk : TurnLope;
    }
}
