using System;
using System.Collections.Generic;
using System.Linq;

namespace FH.Core
{
    // 0.9 battle additions, after Monster Hunter Wilds (wounds, focus strikes, power clash, perfect rush, tracer
    // arrows) and done this game's own way: torn wounds give the hunter Resolve, every third tear in a fight topples
    // the monster ("Unravel"), and the shield weapons' clash is won by mashing the attack button. Engine-independent.

    /// <summary>Wounds: blows that keep landing on the same spot (a hurtbox: head, foreleg, tail...) open a wound there.
    /// An open wound glows, takes more damage and can be torn by a Rend strike: big damage, the monster flinches, and the
    /// spot scars over (it cannot wound again for a while). Wounds left alone close after <see cref="Life"/> seconds.</summary>
    public class WoundTracker
    {
        public const float Life = 75, ScarTime = 60, DamageBonus = 1.25f, BuildDecayDelay = 12;
        public const int MaxOpen = 3, UnravelEvery = 3;
        public readonly float Threshold;
        class Spot { public float build, lastHit = -99, openUntil = -1, scarUntil = -1; }
        readonly Dictionary<string, Spot> spots = new Dictionary<string, Spot>();

        public int Opened { get; private set; }
        public int Torn { get; private set; }

        /// <summary>The damage one spot must take (within a fight) to open: a tenth of the monster's health, at least 15.</summary>
        public WoundTracker(float maxHealth) { Threshold = Math.Max(15, maxHealth * .1f); }

        Spot Get(string key) { if (!spots.TryGetValue(key, out var s)) spots[key] = s = new Spot(); return s; }

        public bool IsOpen(string key, float now) => key != null && spots.TryGetValue(key, out var s) && now < s.openUntil;
        public bool IsScarred(string key, float now) => key != null && spots.TryGetValue(key, out var s) && now < s.scarUntil;
        public float Multiplier(string key, float now) => IsOpen(key, now) ? DamageBonus : 1;
        public float Build(string key) => key != null && spots.TryGetValue(key, out var s) ? s.build : 0;
        public IEnumerable<string> Open(float now) => spots.Where(kv => now < kv.Value.openUntil).Select(kv => kv.Key);
        public int OpenCount(float now) => spots.Values.Count(s => now < s.openUntil);

        /// <summary>A blow of <paramref name="damage"/> on <paramref name="key"/>. True when it opened a wound there.
        /// Build-up on a spot left alone for <see cref="BuildDecayDelay"/> seconds starts again from nothing.</summary>
        public bool Hit(string key, float damage, float now)
        {
            if (string.IsNullOrEmpty(key) || damage <= 0) return false;
            var s = Get(key);
            if (now < s.openUntil || now < s.scarUntil) return false;
            if (now - s.lastHit > BuildDecayDelay) s.build = 0;
            s.lastHit = now; s.build += damage;
            if (s.build < Threshold || OpenCount(now) >= MaxOpen) return false;
            s.build = 0; s.openUntil = now + Life; Opened++;
            return true;
        }

        /// <summary>A Rend strike reached <paramref name="key"/>: an open wound there is torn (true) and scars over.</summary>
        public bool Tear(string key, float now)
        {
            if (!IsOpen(key, now)) return false;
            var s = spots[key];
            s.openUntil = -1; s.scarUntil = now + ScarTime; s.build = 0;
            Torn++;
            return true;
        }

        /// <summary>Unravel: the tear just made was the third (sixth...) of the fight, and topples the monster.</summary>
        public bool Unravels => Torn > 0 && Torn % UnravelEvery == 0;

        public void Reset() { spots.Clear(); Opened = Torn = 0; }
    }

    /// <summary>Resolve: each wound the hunter tears adds a stack (up to <see cref="Max"/>) for <see cref="Duration"/>
    /// seconds (a new tear refreshes them all): more damage and quicker stamina.</summary>
    public class HunterResolve
    {
        public const int Max = 3;
        public const float Duration = 40, DamagePerStack = .06f, RegenPerStack = .15f;
        int stacks; float until = -99;
        public int Stacks(float now) => now < until ? stacks : 0;
        public void Add(float now) { stacks = Math.Min(Max, Stacks(now) + 1); until = now + Duration; }
        public float DamageMultiplier(float now) => 1 + DamagePerStack * Stacks(now);
        public float RegenMultiplier(float now) => 1 + RegenPerStack * Stacks(now);
        public float Remaining(float now) => Stacks(now) > 0 ? until - now : 0;
        public void Reset() { stacks = 0; until = -99; }
    }

    /// <summary>The sword and shield's Thorn Rush: inside each rush move there is a moment when the blade glints
    /// (<see cref="HuntMoveDef.perfectFrom"/> .. <see cref="HuntMoveDef.perfectTo"/>, normalized time). The next press
    /// made then is a perfect link: the next move is quicker and stronger. Pressed earlier it is a plain link.</summary>
    public static class RushTiming
    {
        public enum Link { None, Plain, Perfect }
        public static bool HasWindow(HuntMoveDef move) => move != null && move.perfectTo > move.perfectFrom && move.perfectBonus > 0;
        public static bool InWindow(HuntMoveDef move, float normalized) => HasWindow(move) && normalized >= move.perfectFrom && normalized <= move.perfectTo;
        public static Link Judge(HuntMoveDef move, float normalizedAtPress)
        {
            if (!HasWindow(move)) return Link.None;
            return InWindow(move, normalizedAtPress) ? Link.Perfect : Link.Plain;
        }

        /// <summary>The perfect version of <paramref name="next"/>: <paramref name="bonus"/> more damage and stun,
        /// a quicker windup.</summary>
        public static HuntMoveDef Perfect(HuntMoveDef next, float bonus)
        {
            if (next == null) return null;
            var c = next.Clone();
            c.id = next.id + "_perfect";
            c.damageMultiplier = next.damageMultiplier * (1 + bonus);
            c.koDamage = next.koDamage * (1 + bonus);
            c.windup = next.windup * .7f;
            c.hitstop = next.hitstop * 1.3f;
            c.shake = next.shake * 1.3f;
            return c;
        }
    }

    /// <summary>Power clash (the shield weapons): perfect guards build it; once enough have landed, the next blow the
    /// shield meets locks hunter and monster together. Each clash makes the next one need one more perfect guard.</summary>
    public class ClashMeter
    {
        public const int BaseNeed = 3, MaxNeed = 6;
        public int Perfects { get; private set; }
        public int Clashes { get; private set; }
        public int Need => Math.Min(MaxNeed, BaseNeed + Clashes);
        public bool Ready => Perfects >= Need;
        public void PerfectGuard() { Perfects = Math.Min(Need, Perfects + 1); }
        /// <summary>A clash starts: the meter empties; returns the presses needed to win it.</summary>
        public int Begin() { Perfects = 0; Clashes++; return ClashBout.BaseTarget + Clashes; }
        public void Reset() { Perfects = Clashes = 0; }
    }

    /// <summary>One clash: the hunter has <see cref="Time"/> seconds to press attack <see cref="Target"/> times.</summary>
    public class ClashBout
    {
        public const float Time = 1.6f;
        public const int BaseTarget = 6;
        public enum State { Running, Won, Lost }
        public readonly int Target;
        public int Presses { get; private set; }
        public float Elapsed { get; private set; }
        public State Result { get; private set; }
        public float Progress => Target > 0 ? Math.Min(1, Presses / (float)Target) : 1;
        public ClashBout(int target) { Target = Math.Max(1, target); }
        public State Press() { if (Result == State.Running && ++Presses >= Target) Result = State.Won; return Result; }
        public State Tick(float dt)
        {
            if (Result != State.Running) return Result;
            Elapsed += dt;
            if (Elapsed >= Time) Result = State.Lost;
            return Result;
        }
    }

    /// <summary>The bow's Thorn Tracer: the arrow stays in the monster where it struck for <see cref="Duration"/>
    /// seconds; the hunter's arrows bend toward it, and after <see cref="Needed"/> of them land it bursts.</summary>
    public class TracerMark
    {
        public const float Duration = 25, BurstMotionValue = 2.4f, HomingDegrees = 540;
        public const int Needed = 6;
        float until = -99;
        public int Hits { get; private set; }
        public int Bursts { get; private set; }
        public bool Active(float now) => now < until;
        public void Plant(float now) { until = now + Duration; Hits = 0; }
        /// <summary>An arrow landed while the tracer is in: true when it was the one that bursts it.</summary>
        public bool ArrowLanded(float now)
        {
            if (!Active(now)) return false;
            if (++Hits < Needed) return false;
            until = -99; Bursts++;
            return true;
        }
        public void Clear() { until = -99; Hits = 0; }
    }
}
