using System;
using System.Collections.Generic;
using System.Linq;

namespace FH.Core
{
    public enum AttackPhase { None, Windup, Active, Recovery }

    /// <summary>Deterministic attack timing. Damage windows come from data, not from animation events,
    /// so hit timing is identical whichever animation asset plays.</summary>
    public class AttackTimeline
    {
        public AttackDef Def { get; private set; }
        public float Time { get; private set; }
        public AttackPhase Phase { get; private set; }
        public int SwingId { get; private set; }
        public bool Running => Phase != AttackPhase.None;
        public bool ChainBuffered;

        public void Start(AttackDef def)
        {
            Def = def; Time = 0; Phase = def.windup > 0 ? AttackPhase.Windup : AttackPhase.Active; SwingId++; ChainBuffered = false;
        }

        public void Cancel() { Phase = AttackPhase.None; ChainBuffered = false; }

        /// <summary>Advances time; returns the phase transitions crossed (in order) so short frames never skip an active window.</summary>
        public List<AttackPhase> Tick(float dt)
        {
            var crossed = new List<AttackPhase>();
            if (!Running) return crossed;
            Time += dt;
            var target = Time < Def.windup ? AttackPhase.Windup
                : Time < Def.windup + Def.active ? AttackPhase.Active
                : Time < Def.Total ? AttackPhase.Recovery : AttackPhase.None;
            while (Phase != target)
            {
                Phase = Phase == AttackPhase.Windup ? AttackPhase.Active : Phase == AttackPhase.Active ? AttackPhase.Recovery : AttackPhase.None;
                crossed.Add(Phase);
            }
            return crossed;
        }

        /// <summary>True from windup start until the end of the active window: damage may be dealt during Active only.</summary>
        public bool DamageWindowOpen => Phase == AttackPhase.Active;
        public bool InChainWindow => Running && Phase == AttackPhase.Recovery && Time >= Def.Total - Def.comboWindow - .0001f;
        public bool Committed => Phase == AttackPhase.Windup || Phase == AttackPhase.Active;
        public float Normalized => Def == null ? 0 : Math.Min(1, Time / Math.Max(.0001f, Def.Total));
    }

    /// <summary>Ensures each target is damaged at most once per swing.</summary>
    public class SwingHits
    {
        int swing = -1;
        readonly HashSet<string> hit = new HashSet<string>();
        public bool TryRegister(int swingId, string targetId)
        {
            if (swingId != swing) { swing = swingId; hit.Clear(); }
            return hit.Add(targetId);
        }
        public int Count => hit.Count;
    }

    public class StaminaPool
    {
        public float Max, Current, Regen, Delay, ExhaustThreshold;
        public bool Exhausted { get; private set; }
        float sinceSpend;

        public StaminaPool(float max, float regen, float delay, float exhaustThreshold)
        {
            Max = max; Current = max; Regen = regen; Delay = delay; ExhaustThreshold = exhaustThreshold; sinceSpend = delay;
        }

        public bool CanSpend(float cost) => !Exhausted && Current >= Math.Min(cost, Max) && Current > 0;

        public bool TrySpend(float cost)
        {
            if (!CanSpend(cost)) return false;
            Current = Math.Max(0, Current - cost);
            sinceSpend = 0;
            if (Current <= 0) Exhausted = true;
            return true;
        }

        public void Drain(float perSecond, float dt)
        {
            Current = Math.Max(0, Current - perSecond * dt);
            sinceSpend = 0;
            if (Current <= 0) Exhausted = true;
        }

        public void Tick(float dt)
        {
            sinceSpend += dt;
            if (sinceSpend >= Delay) Current = Math.Min(Max, Current + Regen * dt * (Exhausted ? .6f : 1));
            if (Exhausted && Current >= ExhaustThreshold) Exhausted = false;
        }
    }

    public class PoiseMeter
    {
        public float Max, Current, Regen;
        public PoiseMeter(float max, float regen) { Max = max; Current = max; Regen = regen; }
        /// <summary>Returns true when this hit breaks poise (the target staggers). The meter refills after a break.</summary>
        public bool Apply(float amount)
        {
            if (Max <= 0 || amount <= 0) return false;
            Current -= amount;
            if (Current > 0) return false;
            Current = Max;
            return true;
        }
        public void Tick(float dt) { Current = Math.Min(Max, Current + Regen * dt); }
    }

    public class StatusTracker
    {
        class Entry { public StatusDef def; public float buildup, remaining, tick; }
        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        public readonly HashSet<string> Immunities = new HashSet<string>();

        public bool IsActive(string id) => entries.TryGetValue(id, out var e) && e.remaining > 0;
        public float Buildup(string id) => entries.TryGetValue(id, out var e) ? e.buildup : 0;

        /// <summary>Adds buildup; returns true if the status triggered.</summary>
        public bool Add(StatusDef def, float amount)
        {
            if (def == null || amount <= 0 || Immunities.Contains(def.id)) return false;
            if (!entries.TryGetValue(def.id, out var e)) entries[def.id] = e = new Entry { def = def };
            if (e.remaining > 0) return false;
            e.buildup += amount;
            if (e.buildup < def.buildup) return false;
            e.buildup = 0; e.remaining = def.duration; e.tick = def.tickInterval;
            return true;
        }

        /// <summary>Returns damage dealt by active statuses this frame.</summary>
        public float Tick(float dt)
        {
            float damage = 0;
            foreach (var e in entries.Values)
            {
                if (e.remaining > 0)
                {
                    e.remaining -= dt; e.tick -= dt;
                    while (e.tick <= 0 && e.def.tickInterval > 0) { e.tick += e.def.tickInterval; damage += e.def.tickDamage; }
                }
                else e.buildup = Math.Max(0, e.buildup - dt * 5);
            }
            return damage;
        }

        public float SpeedMultiplier => entries.Values.Where(e => e.remaining > 0).Select(e => e.def.slowMultiplier).DefaultIfEmpty(1).Aggregate(1f, (a, b) => a * b);
        public IEnumerable<string> ActiveIds => entries.Values.Where(e => e.remaining > 0).Select(e => e.def.id);
    }

    public struct HitInfo
    {
        public float Damage, Poise;
        public bool WeakPoint, Critical;
    }

    public static class DamageMath
    {
        public static float Resist(IEnumerable<Resistance> list, DamageType t)
        {
            float m = 1;
            if (list != null) foreach (var r in list) if (r.type == t) m *= r.multiplier;
            return m;
        }

        /// <summary>Player to monster.</summary>
        public static HitInfo PlayerHit(WeaponDef weapon, AttackDef attack, StatSheet stats, MonsterDef monster, MonsterPartDef part, bool partBroken, bool critical)
        {
            float raw = (weapon.baseDamage + stats[Stat.Attack]) * attack.damageMultiplier;
            float partMul = part == null ? 1 : (partBroken && part.brokenDamageMultiplier > 0 ? part.brokenDamageMultiplier : part.damageMultiplier);
            float dmg = raw * partMul * Resist(monster.resistances, weapon.damageType) * (critical ? 1.5f : 1);
            float poise = (weapon.poiseDamage + stats[Stat.PoiseDamage]) * attack.poiseMultiplier * (part != null && part.weakPoint ? 1.5f : 1);
            return new HitInfo { Damage = Math.Max(1, (float)Math.Round(dmg)), Poise = poise, WeakPoint = part != null && part.weakPoint, Critical = critical };
        }

        public static HitInfo SpellHit(SpellDef spell, StatSheet stats, MonsterDef monster, MonsterPartDef part, bool partBroken)
        {
            float partMul = part == null ? 1 : (partBroken && part.brokenDamageMultiplier > 0 ? part.brokenDamageMultiplier : part.damageMultiplier);
            float dmg = (spell.damage + stats[Stat.SpellPower]) * partMul * Resist(monster.resistances, spell.damageType);
            return new HitInfo { Damage = Math.Max(1, (float)Math.Round(dmg)), Poise = spell.poiseDamage, WeakPoint = part != null && part.weakPoint };
        }

        /// <summary>Monster to player, after defense and armor resistances.</summary>
        public static float MonsterHit(MonsterAttackDef attack, float defense, IEnumerable<Resistance> resist)
        {
            float d = attack.damage * 100f / (100f + Math.Max(0, defense)) * Resist(resist, attack.damageType);
            return Math.Max(1, (float)Math.Round(d));
        }
    }

    public enum MonsterMode { Idle, Patrol, Alert, Chase, Telegraph, Attack, Recover, Stagger, Return, Dead }

    /// <summary>Engine-independent monster decision logic. The Unity actor feeds senses in and executes the intent.</summary>
    public class MonsterBrain
    {
        public readonly MonsterDef Def;
        public MonsterMode Mode { get; private set; } = MonsterMode.Idle;
        public MonsterAttackDef CurrentAttack { get; private set; }
        public float ModeTime { get; private set; }
        public float Health;
        public readonly PoiseMeter Poise;
        public readonly StatusTracker Status = new StatusTracker();
        public float Grace;            // seconds during which no attack may start (spawn, load, unpause)
        float cooldown, unseen;
        int attackIndex;
        public bool HitDelivered;      // current attack already landed
        public event Action<MonsterMode> ModeChanged;

        public MonsterBrain(MonsterDef def)
        {
            Def = def; Health = def.maxHealth; Poise = new PoiseMeter(def.poise, def.poiseRegen); Grace = def.spawnGrace;
            foreach (var s in def.statusImmunities) Status.Immunities.Add(s);
        }

        public bool Alive => Mode != MonsterMode.Dead;
        public bool Engaged => Mode == MonsterMode.Chase || Mode == MonsterMode.Telegraph || Mode == MonsterMode.Attack || Mode == MonsterMode.Recover || Mode == MonsterMode.Alert || Mode == MonsterMode.Stagger;
        public bool Vulnerable => Mode == MonsterMode.Recover || Mode == MonsterMode.Stagger;
        public bool AttackActive => Mode == MonsterMode.Attack;

        void Set(MonsterMode m)
        {
            if (Mode == m) return;
            Mode = m; ModeTime = 0;
            ModeChanged?.Invoke(m);
        }

        public void AddGrace(float seconds) { Grace = Math.Max(Grace, seconds); if (Mode == MonsterMode.Telegraph) Set(MonsterMode.Chase); }

        public struct Senses
        {
            public float distanceToPlayer, distanceFromHome, angleToPlayer;
            public bool lineOfSight, playerAlive, playerHidden;
        }

        public bool CanPerceive(Senses s)
        {
            if (!s.playerAlive || s.playerHidden) return false;
            if (s.distanceToPlayer <= Def.hearingRadius) return true;
            return s.distanceToPlayer <= Def.perceptionRadius && s.lineOfSight && Math.Abs(s.angleToPlayer) <= Def.fieldOfView * .5f;
        }

        public Func<MonsterAttackDef, bool> AttackFilter;

        MonsterAttackDef ChooseAttack(Senses senses)
        {
            var inRange = Def.attacks.Where(a => senses.distanceToPlayer <= a.range && senses.distanceToPlayer >= a.minRange && Math.Abs(senses.angleToPlayer) >= a.minAngle && Math.Abs(senses.angleToPlayer) <= a.maxAngle && (AttackFilter == null || AttackFilter(a))).ToList();
            if (inRange.Count == 0) return null;
            attackIndex++;
            return inRange[attackIndex % inRange.Count];
        }

        public void Tick(float dt, Senses s)
        {
            ModeTime += dt;
            Grace = Math.Max(0, Grace - dt);
            cooldown = Math.Max(0, cooldown - dt);
            if (Mode == MonsterMode.Dead) return;
            Poise.Tick(dt);
            bool sees = CanPerceive(s);
            unseen = sees ? 0 : unseen + dt;
            switch (Mode)
            {
                case MonsterMode.Idle:
                case MonsterMode.Patrol:
                    if (sees) { Set(MonsterMode.Alert); break; }
                    if (Mode == MonsterMode.Idle && ModeTime > 2.5f) Set(MonsterMode.Patrol);
                    else if (Mode == MonsterMode.Patrol && ModeTime > 5f) Set(MonsterMode.Idle);
                    break;
                case MonsterMode.Alert:
                    if (ModeTime >= .45f) Set(MonsterMode.Chase);
                    break;
                case MonsterMode.Chase:
                    if (s.distanceFromHome > Def.leashRadius || !s.playerAlive || unseen > Def.loseInterestTime) { Set(MonsterMode.Return); break; }
                    if (Grace <= 0 && cooldown <= 0)
                    {
                        var atk = ChooseAttack(s);
                        if (atk != null) { CurrentAttack = atk; HitDelivered = false; Set(MonsterMode.Telegraph); }
                    }
                    break;
                case MonsterMode.Telegraph:
                    if (ModeTime >= CurrentAttack.telegraph) Set(MonsterMode.Attack);
                    break;
                case MonsterMode.Attack:
                    if (ModeTime >= CurrentAttack.active) Set(MonsterMode.Recover);
                    break;
                case MonsterMode.Recover:
                    if (ModeTime >= CurrentAttack.recovery) { cooldown = CurrentAttack.cooldown; Set(MonsterMode.Chase); }
                    break;
                case MonsterMode.Stagger:
                    if (ModeTime >= Def.staggerTime) { cooldown = Math.Max(cooldown, .4f); Set(MonsterMode.Chase); }
                    break;
                case MonsterMode.Return:
                    // Leash reset: heal and forget the player once home; can re-aggro only near home.
                    Health = Math.Min(Def.maxHealth, Health + Def.maxHealth * .35f * dt);
                    if (s.distanceFromHome < 1.2f) { Health = Def.maxHealth; Poise.Current = Poise.Max; Set(MonsterMode.Idle); }
                    else if (sees && s.distanceFromHome < Def.leashRadius * .5f && ModeTime > 1.5f) Set(MonsterMode.Chase);
                    break;
            }
        }

        /// <summary>Applies damage; returns true if this killed the monster.</summary>
        public bool TakeHit(HitInfo hit)
        {
            if (!Alive) return false;
            Health -= hit.Damage;
            if (Health <= 0) { Health = 0; Set(MonsterMode.Dead); return true; }
            if (Mode == MonsterMode.Idle || Mode == MonsterMode.Patrol || Mode == MonsterMode.Return) Set(MonsterMode.Alert);
            if (Poise.Apply(hit.Poise) && Mode != MonsterMode.Stagger) Set(MonsterMode.Stagger);
            return false;
        }

        public bool TakeStatusDamage(float dmg)
        {
            if (!Alive || dmg <= 0) return false;
            Health -= dmg;
            if (Health <= 0) { Health = 0; Set(MonsterMode.Dead); return true; }
            return false;
        }

        public void ForceStagger() { if (Alive) Set(MonsterMode.Stagger); }
    }

    /// <summary>Tracks breakable parts of one monster instance.</summary>
    public class PartTracker
    {
        readonly MonsterDef def;
        readonly Dictionary<string, float> damage = new Dictionary<string, float>();
        public readonly HashSet<string> Broken = new HashSet<string>();
        public PartTracker(MonsterDef def, IEnumerable<string> alreadyBroken = null)
        {
            this.def = def;
            if (alreadyBroken != null) foreach (var p in alreadyBroken) Broken.Add(p);
        }

        public MonsterPartDef Part(string id) => def.parts.FirstOrDefault(p => p.id == id);

        /// <summary>A weak point gated behind another part only counts once that part is broken.</summary>
        public MonsterPartDef Effective(string id)
        {
            var p = Part(id);
            if (p == null) return null;
            if (!string.IsNullOrEmpty(p.requiresBrokenPart) && !Broken.Contains(p.requiresBrokenPart))
                return new MonsterPartDef { id = p.id, name = p.name, damageMultiplier = 1, weakPoint = false };
            return p;
        }

        /// <summary>Several parts can share a bone (a helm over a skull). An intact breakable cover takes the hit;
        /// once broken, the part it was protecting is struck instead.</summary>
        public MonsterPartDef Resolve(IList<string> partIdsOnBone)
        {
            var parts = partIdsOnBone.Select(Part).Where(p => p != null).ToList();
            if (parts.Count == 0) return null;
            foreach (var p in parts) if (p.breakHealth > 0 && !Broken.Contains(p.id)) return Effective(p.id);
            foreach (var p in parts) if (!string.IsNullOrEmpty(p.requiresBrokenPart) && Broken.Contains(p.requiresBrokenPart)) return Effective(p.id);
            return Effective((parts.FirstOrDefault(p => p.breakHealth <= 0) ?? parts[0]).id);
        }

        /// <summary>Returns true if this damage broke the part.</summary>
        public bool Damage(string partId, float amount)
        {
            var p = Part(partId);
            if (p == null || p.breakHealth <= 0 || Broken.Contains(partId)) return false;
            damage.TryGetValue(partId, out float d);
            d += amount;
            damage[partId] = d;
            if (d < p.breakHealth) return false;
            Broken.Add(partId);
            return true;
        }

        public float Progress(string partId)
        {
            var p = Part(partId);
            if (p == null || p.breakHealth <= 0) return 0;
            if (Broken.Contains(partId)) return 1;
            return Math.Min(1, (damage.TryGetValue(partId, out var d) ? d : 0) / p.breakHealth);
        }
    }
}
