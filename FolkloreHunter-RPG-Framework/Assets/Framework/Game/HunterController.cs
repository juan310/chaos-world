using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>The player hunter. Movement, committed attacks on a deterministic timeline, blade hit volumes,
    /// stamina, dodge invulnerability, spells, items, hurt and death.</summary>
    public class HunterController : MonoBehaviour, IConsumer
    {
        public Transform Visual;
        public AnimationDriver Anim { get; private set; }
        public CharacterController Body { get; private set; }
        public float Health, Magic;
        public StaminaPool Stamina { get; private set; }
        public StatSheet Stats { get; private set; } = new StatSheet();
        public readonly AttackTimeline Attack = new AttackTimeline();
        public MonsterActor LockTarget;
        public bool Dead { get; private set; }
        public bool Ready { get; private set; }
        public Vector3 BladeBase { get; private set; }
        public Vector3 BladeTip { get; private set; }
        public int SwingsLanded { get; private set; }
        /// <summary>Recent registered hits as (swing id, monster key), for verifying once-per-swing.</summary>
        public readonly List<(int swing, string monster)> HitLog = new List<(int, string)>();
        public string SelectedSpell = "ember_bolt";
        public float SpellReadyAt { get; private set; }
        public bool Dodging => Time.time < dodgeUntil;
        public bool Invulnerable => Time.time >= invulnFrom && Time.time < invulnUntil;
        public float LastDodgeEvade { get; private set; } = -99;
        public Vector3 Velocity { get; private set; }
        /// <summary>Monster Hunter-style moveset (Proving Grounds until integrated). Null or disabled: classic combat.</summary>
        public HuntFighter Hunt;
        public bool HuntStyle => Hunt && Hunt.enabled;
        public float HitstopUntil;
        /// <summary>Raised when the blade registers a hit: (monster, damage dealt).</summary>
        public event System.Action<MonsterActor, HitInfo> BladeHit;

        GameSession session;
        Transform hand, footL, footR, sword;
        Vector3 swordLocalA, swordLocalB;
        WeaponDef weapon;
        int comboIndex;
        bool attackBuffered;
        float bufferExpires, dodgeUntil, invulnFrom, invulnUntil, lockUntil, hurtUntil, castAt = -1, deathAt = -1, vertical, stepClock;
        Vector3 dodgeDir, prevTip;
        bool prevTipValid;
        readonly SwingHits swingHits = new SwingHits();
        float walkNatural, runNatural;
        public float WalkSpeed { get; private set; } = 2.9f;
        public float RunSpeed { get; private set; } = 5.3f;
        public const float RunCost = 9;

        public void Initialize(GameSession s)
        {
            session = s;
            Body = GetComponent<CharacterController>();
            Anim = new AnimationDriver(Visual, "Hunter");
            hand = WorldKit.Find(Visual, "handslot.r");
            footL = WorldKit.Find(Visual, "foot.l");
            footR = WorldKit.Find(Visual, "foot.r");
            sword = WorldKit.Find(Visual, "1H_Sword");
            if (sword)
            {
                var mesh = sword.GetComponent<MeshFilter>()?.sharedMesh ?? sword.GetComponent<SkinnedMeshRenderer>()?.sharedMesh;
                if (mesh)
                {
                    var b = mesh.bounds;
                    var axis = b.size.x > b.size.y && b.size.x > b.size.z ? Vector3.right * b.extents.x : b.size.y > b.size.z ? Vector3.up * b.extents.y : Vector3.forward * b.extents.z;
                    swordLocalA = b.center - axis; swordLocalB = b.center + axis;
                }
            }
            walkNatural = Anim.NaturalSpeed("Walking_A", footL, footR, transform);
            runNatural = Anim.NaturalSpeed("Running_A", footL, footR, transform);
            // Travel speed follows the authored stride so planted feet do not slide; playback runs slightly fast for pace.
            if (walkNatural > .3f) WalkSpeed = Mathf.Clamp(walkNatural * 1.3f, 1.6f, 3.2f);
            if (runNatural > .6f) RunSpeed = Mathf.Clamp(runNatural * 1.3f, WalkSpeed * 1.5f, 6f);
            Debug.Log("FH_LOCO walk speed " + WalkSpeed.ToString("0.00") + ", run speed " + RunSpeed.ToString("0.00") + ",  walk natural " + walkNatural.ToString("0.00") + " m/s, run natural " + runNatural.ToString("0.00") + " m/s, feet " + (footL ? footL.name : "missing") + "/" + (footR ? footR.name : "missing"));
            Stamina = new StaminaPool(100, 24, s.Core.Db.Balance.staminaRegenDelay, s.Core.Db.Balance.exhaustedThreshold);
            RefreshStats(true);
            Anim.Play("Idle");
            Ready = true;
        }

        /// <summary>Recomputes derived stats (equipment, skills, attributes) keeping current health/stamina ratios.</summary>
        public void RefreshStats(bool fill = false)
        {
            var core = session.Core;
            float hpRatio = Stats[Stat.MaxHealth] > 0 ? Health / Stats[Stat.MaxHealth] : 1;
            float mpRatio = Stats[Stat.MaxMagic] > 0 ? Magic / Stats[Stat.MaxMagic] : 1;
            Stats = core.Stats.Compute();
            weapon = core.Stats.EquippedWeapon();
            Stamina.Max = Stats[Stat.MaxStamina];
            Stamina.Regen = Stats[Stat.StaminaRegen];
            if (fill) { Health = Stats[Stat.MaxHealth]; Magic = Stats[Stat.MaxMagic]; Stamina.Current = Stamina.Max; }
            else { Health = Mathf.Clamp(hpRatio * Stats[Stat.MaxHealth], 1, Stats[Stat.MaxHealth]); Magic = mpRatio * Stats[Stat.MaxMagic]; Stamina.Current = Mathf.Min(Stamina.Current, Stamina.Max); }
            ApplyEquipmentVisuals();
        }

        public void RefreshEquipmentPresentation() => ApplyEquipmentVisuals();

        void ApplyEquipmentVisuals()
        {
            // The rig has one sword mesh; upgraded blades are shown by tinting it rather than inventing new art.
            if (!sword || weapon == null) return;
            var item = session.Core.Db.Item(weapon.id);
            var r = sword.GetComponent<Renderer>();
            if (!r) return;
            var baseMat = CharacterFactory.Atlas("Hunter");
            r.sharedMaterial = item.rarity >= Rarity.Uncommon ? TintedBlade(baseMat, item.rarity) : baseMat;
            var equipment = GetComponent<HuntEquipment>(); if (!equipment) equipment = gameObject.AddComponent<HuntEquipment>();
            equipment.Refresh(session, this);
        }

        static Material TintedBlade(Material baseMat, Rarity rarity)
        {
            var c = rarity == Rarity.Uncommon ? new Color(.78f, 1f, .82f) : new Color(.85f, .8f, 1f);
            var m = WorldKit.Mat("Hunter Atlas blade " + rarity, c);
            m.mainTexture = baseMat.mainTexture;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * .25f);
            return m;
        }

        void Update()
        {
            if (!Ready) return;
            // A fade or a window that does not pause time (dialogue, shop) holds the world: stand, don't run in place.
            if (!session.Simulating) { if (Anim.Current.EndsWith("Walking_A") || Anim.Current.EndsWith("Running_A")) Anim.Play("Idle"); return; }
            var mv = InputRouter.Move();
            var cam = session.CameraRig.Camera.transform;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized, right = Vector3.Cross(Vector3.up, fwd);
            Vector3 dir = Vector3.ClampMagnitude(fwd * mv.y + right * mv.x, 1);
            if (HuntStyle) { Hunt.HandleInput(dir); Tick(dir, Hunt.Sprinting, Time.deltaTime); return; }
            if (InputRouter.Down(GameAction.Attack)) TryAttack();
            if (InputRouter.Down(GameAction.Dodge)) TryDodge(dir);
            if (InputRouter.Down(GameAction.Spell)) TryCast();
            if (InputRouter.Down(GameAction.CycleSpell)) CycleSpell();
            if (InputRouter.Down(GameAction.LockOn)) ToggleLock();
            for (int i = 0; i < 4; i++) if (InputRouter.Down(GameAction.Hotbar1 + i)) UseHotbar(i);
            if (InputRouter.Down(GameAction.UseItem)) UseHotbar(0);
            Tick(dir, InputRouter.Held(GameAction.Run), Time.deltaTime);
        }

        public void Tick(Vector3 dir, bool run, float dt)
        {
            if (Dead)
            {
                if (deathAt > 0 && Time.time - deathAt > 2.6f) { deathAt = -1; session.OnPlayerDefeated(); }
                Gravity(dt);
                return;
            }
            // Hitstop: on a registered blow the hunter freezes mid-swing for a few frames (weight, as in Monster Hunter).
            bool stopped = Time.time < HitstopUntil;
            Anim.Freeze(stopped);
            if (stopped) { Gravity(dt); return; }
            if (knock.sqrMagnitude > 1e-4f)
            {
                var push = session.ConstrainMove(transform.position, transform.position + knock * dt) - transform.position; push.y = 0;
                Body.Move(push);
                knock = Vector3.MoveTowards(knock, Vector3.zero, knockDecel * dt);
            }
            Stamina.Tick(dt);
            Magic = Mathf.Min(Stats[Stat.MaxMagic], Magic + dt * 1.5f);
            if (LockTarget && (!LockTarget.Alive || Vector3.Distance(LockTarget.transform.position, transform.position) > 22)) LockTarget = null;

            // Attack timeline drives damage windows; animation merely follows it.
            if (Attack.Running)
            {
                foreach (var phase in Attack.Tick(dt))
                {
                    if (phase == AttackPhase.Active) { prevTipValid = false; WorldKit.Sound(transform.position, "swish", .16f); }
                    if (phase == AttackPhase.None) comboIndex = 0;
                }
                if (Attack.Phase == AttackPhase.Windup && Attack.Def.forwardStep > 0)
                    Move(transform.forward * Attack.Def.forwardStep / Mathf.Max(.05f, Attack.Def.windup), dt);
                if (Attack.DamageWindowOpen) SweepBlade();
                if (attackBuffered && Attack.InChainWindow) { attackBuffered = false; StartNextAttack(); }
                if (attackBuffered && Time.time > bufferExpires) attackBuffered = false;
            }
            else if (attackBuffered) { attackBuffered = false; TryAttack(); }

            if (castAt > 0 && Time.time >= castAt) { castAt = -1; Launch(); }

            bool locked = Time.time < lockUntil || Time.time < hurtUntil || Attack.Committed || (HuntStyle && Hunt.Locked);
            float speed = 0;
            Vector3 moveDir = Vector3.zero;
            if (Dodging) { moveDir = dodgeDir; speed = 7.2f; }
            else if (Attack.Running && Attack.Phase == AttackPhase.Recovery) { moveDir = dir; speed = WalkSpeed * .3f; }
            else if (!locked && dir.sqrMagnitude > .01f)
            {
                bool canRun = run && !Stamina.Exhausted && Stamina.Current > 1;
                speed = (canRun ? RunSpeed : WalkSpeed) * Stats[Stat.MoveSpeed] * (HuntStyle ? Hunt.SpeedScale : 1);
                if (canRun) Stamina.Drain(RunCost, dt);
                moveDir = dir;
                // Pressing into a wall, fence or the river bank: stand instead of running in place (SightForge track).
                blockedFor = actualSpeed < speed * .25f ? blockedFor + dt : 0;
                if (blockedFor > .15f) Anim.Play("Idle");
                else if (HuntStyle && Hunt.LocomotionClip(dir, canRun, out var clip, out var natural2)) { Anim.Play(clip, true, natural2 > .1f ? Mathf.Clamp(speed / natural2, .5f, 2.5f) : 1); }
                else
                {
                    float natural = canRun ? runNatural : walkNatural;
                    float animSpeed = natural > .1f ? Mathf.Clamp(speed / natural, .5f, 2.5f) : 1;
                    Anim.Play(canRun ? "Running_A" : "Walking_A", true, animSpeed);
                    stepClock -= dt;
                    if (stepClock <= 0) { stepClock = canRun ? .24f : .36f; WorldKit.Sound(transform.position, "step", .08f); }
                }
            }
            else if (!locked && !Attack.Running && !Dodging && castAt < 0 && !(HuntStyle && Hunt.OwnsIdlePose)) Anim.Play(Anim.Current.Contains("Hit") && Time.time < hurtUntil + .2f ? Anim.Current : "Idle");

            if (HuntStyle && Hunt.FacesTarget(out var faceDir))
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(faceDir), 1 - Mathf.Exp(-dt * 12));
            else if (moveDir.sqrMagnitude > .01f && !Attack.Committed && !(LockTarget && Attack.Running) && !(HuntStyle && Dodging && Hunt.KeepFacing))
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), 1 - Mathf.Exp(-dt * 15));
            Move(moveDir * speed, dt);
            Gravity(dt);
        }

        void Gravity(float dt)
        {
            vertical = Body.isGrounded ? -2 : Mathf.Max(-20, vertical - dt * 24);
            Body.Move(Vector3.up * vertical * dt);
        }

        // Hit knockback: speed decays linearly to zero over KnockTime, covering KnockSpeed * KnockTime / 2 = 0.25 m.
        const float KnockSpeed = 2.5f, KnockTime = .2f;
        Vector3 knock;
        float knockDecel = KnockSpeed / KnockTime;

        /// <summary>A shove that decays to rest over <paramref name="seconds"/> (guard pushback, knockback).</summary>
        public void Push(Vector3 velocity, float seconds) { knock = velocity; knockDecel = velocity.magnitude / Mathf.Max(.05f, seconds); }
        public void LockFor(float seconds) => lockUntil = Mathf.Max(lockUntil, Time.time + seconds);
        public void HurtFor(float seconds) => hurtUntil = Time.time + seconds;
        public bool Hurt => Time.time < hurtUntil;
        public void InvulnerableFor(float seconds) { invulnFrom = Time.time; invulnUntil = Mathf.Max(invulnUntil, Time.time + seconds); }
        public void CancelActions() { Attack.Cancel(); attackBuffered = false; comboIndex = 0; castAt = -1; }
        public Transform Hand => hand;
        public Transform SwordTransform => sword;
        float actualSpeed, blockedFor;

        void Move(Vector3 velocity, float dt)
        {
            Velocity = velocity;
            Vector3 delta = velocity * dt;
            Vector3 next = transform.position + delta;
            delta = session.ConstrainMove(transform.position, next) - transform.position;
            delta.y = 0;
            var before = transform.position;
            Body.Move(delta);
            var moved = transform.position - before; moved.y = 0;
            actualSpeed = dt > 0 ? moved.magnitude / dt : 0;
        }

        // ---------- attacks ----------
        public bool TryAttack()
        {
            if (Dead || weapon == null) return false;
            if (Attack.Running)
            {
                // Buffer a follow-up during recovery (or the end of the active window) so combos feel responsive.
                if (Attack.Phase != AttackPhase.Windup && comboIndex + 1 < weapon.combo.Count && Attack.Def.comboWindow > 0)
                { attackBuffered = true; bufferExpires = Time.time + .6f; }
                return false;
            }
            if (Time.time < lockUntil || Time.time < hurtUntil || Dodging) { attackBuffered = true; bufferExpires = Time.time + .25f; return false; }
            comboIndex = 0;
            return Begin(weapon.combo[0]);
        }

        void StartNextAttack()
        {
            if (comboIndex + 1 >= weapon.combo.Count) return;
            if (Begin(weapon.combo[comboIndex + 1])) comboIndex++;
        }

        /// <summary>Hunt moveset: starts a move on the shared deterministic timeline (moves cost no stamina, as in MH).</summary>
        public bool StartMove(AttackDef a)
        {
            if (Dead) return false;
            if (a.staminaCost > 0 && !Stamina.TrySpend(a.staminaCost)) return false;
            castAt = -1;
            Attack.Start(a);
            prevTipValid = false;
            Anim.PlayStrike(a.anim, a.windup + a.active * .5f, hand);
            return true;
        }

        /// <summary>Hunt roll: in the stick direction; locked on it keeps facing the target and uses the matching
        /// directional roll (left, right, back). Cancels a move once the move allows it (checked by the caller).</summary>
        public bool Roll(Vector3 dir, bool keepFacing)
        {
            if (Dead || Dodging) return false;
            if (!Stamina.TrySpend(25)) { session.Hud?.Flash("Too tired to roll."); return false; }
            CancelActions(); knock = Vector3.zero;
            dodgeDir = dir.sqrMagnitude > .1f ? dir.normalized : (keepFacing ? -transform.forward : transform.forward);
            string clip = "Dodge_Forward";
            if (keepFacing)
            {
                float side = Vector3.Dot(dodgeDir, transform.right), front = Vector3.Dot(dodgeDir, transform.forward);
                clip = Mathf.Abs(side) > Mathf.Abs(front) ? (side > 0 ? "Dodge_Right" : "Dodge_Left") : front < 0 ? "Dodge_Backward" : "Dodge_Forward";
            }
            else transform.rotation = Quaternion.LookRotation(dodgeDir);
            dodgeUntil = Time.time + .42f;
            invulnFrom = Time.time + .03f;
            invulnUntil = invulnFrom + Stats[Stat.DodgeWindow];
            Anim.Play(clip, false, 1.35f, true, .05f);
            return true;
        }

        bool Begin(AttackDef a)
        {
            if (!Stamina.TrySpend(a.staminaCost)) { session.Hud?.Flash("Too tired to swing."); return false; }
            FaceTarget();
            Attack.Start(a);
            prevTipValid = false;
            Anim.PlayStrike(a.anim, a.windup + a.active * .5f, hand);
            return true;
        }

        void FaceTarget()
        {
            var target = LockTarget && LockTarget.Alive ? LockTarget : session.NearestMonster(transform.position, 3.2f, transform.forward, 100);
            if (!target) return;
            var d = target.transform.position - transform.position; d.y = 0;
            if (d.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(d);
        }

        void UpdateBlade()
        {
            if (!sword) { BladeBase = transform.position + Vector3.up; BladeTip = BladeBase + transform.forward; return; }
            Vector3 a = sword.TransformPoint(swordLocalA), b = sword.TransformPoint(swordLocalB);
            var equipment = GetComponent<HuntEquipment>();
            Vector3 equippedA, equippedB;
            if (equipment && equipment.TryBlade(out equippedA, out equippedB)) { a = equippedA; b = equippedB; }
            Vector3 grip = hand ? hand.position : a;
            if (Vector3.Distance(a, grip) > Vector3.Distance(b, grip)) { var t = a; a = b; b = t; }
            float reach = Attack.Def != null ? Attack.Def.reach : 1;
            BladeBase = Vector3.Lerp(grip, b, .2f);
            BladeTip = grip + (b - grip) * reach;
        }

        readonly Collider[] overlap = new Collider[24];

        /// <summary>Sweeps the actual blade (and the arc since last frame) during the active window.</summary>
        void SweepBlade()
        {
            UpdateBlade();
            Physics.SyncTransforms();
            float r = Attack.Def.radius;
            var found = new List<Collider>();
            int n = Physics.OverlapCapsuleNonAlloc(BladeBase, BladeTip, r, overlap, Layers.HurtboxMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++) found.Add(overlap[i]);
            if (prevTipValid)
            {
                n = Physics.OverlapCapsuleNonAlloc(prevTip, BladeTip, r, overlap, Layers.HurtboxMask, QueryTriggerInteraction.Collide);
                for (int i = 0; i < n; i++) found.Add(overlap[i]);
            }
            prevTip = BladeTip; prevTipValid = true;
            // Closest hurtbox per monster wins, so a swing through helm and ribs strikes once.
            foreach (var group in found.Select(c => c.GetComponent<Hurtbox>()).Where(h => h && h.Owner && h.Owner.Alive).GroupBy(h => h.Owner))
            {
                var monster = group.Key;
                if (!swingHits.TryRegister(Attack.SwingId, monster.InstanceKey)) continue;
                var box = group.OrderBy(h => DistanceToSegment(h.transform.position, BladeBase, BladeTip)).First();
                Vector3 point = ClosestOnSegment(box.transform.position, BladeBase, BladeTip);
                var atk = Attack.Def;
                bool crit = Random.value * 100 < Stats[Stat.CritChance];
                HitInfo dealt = default;
                if (monster.HuntRules && atk is HuntMoveDef hm) monster.PendingStun = hm.koDamage;
                monster.ReceiveHit(box, point, (part, broken) => dealt = DamageMath.PlayerHit(weapon, atk, Stats, monster.Def, part, broken, crit));
                BladeHit?.Invoke(monster, dealt);
                SwingsLanded++;
                HitLog.Add((Attack.SwingId, monster.InstanceKey));
                if (HitLog.Count > 200) HitLog.RemoveAt(0);
                session.CameraRig.Shake(.12f);
            }
        }

        static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return a + ab * t;
        }

        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b) => Vector3.Distance(p, ClosestOnSegment(p, a, b));

        // ---------- dodge ----------
        public bool TryDodge(Vector3 dir)
        {
            if (Dead || Dodging || Time.time < hurtUntil) return false;
            if (Attack.Committed) return false;  // committed swings cannot be cancelled; recovery can
            if (!Stamina.TrySpend(25)) { session.Hud?.Flash("Too tired to dodge."); return false; }
            Attack.Cancel(); attackBuffered = false; comboIndex = 0; castAt = -1;
            dodgeDir = dir.sqrMagnitude > .1f ? dir.normalized : transform.forward;
            transform.rotation = Quaternion.LookRotation(dodgeDir);
            dodgeUntil = Time.time + .42f;
            invulnFrom = Time.time + .03f;
            invulnUntil = invulnFrom + Stats[Stat.DodgeWindow];
            Anim.Play("Dodge_Forward", false, 1.35f, true, .05f);
            return true;
        }

        // ---------- spells ----------
        public bool TryCast()
        {
            if (Dead || Attack.Committed || Dodging || castAt > 0 || Time.time < hurtUntil) return false;
            var core = session.Core;
            if (!core.Db.Spells.TryGetValue(SelectedSpell, out var spell)) return false;
            if (!string.IsNullOrEmpty(spell.requiresSkill) && !core.State.player.skills.Contains(spell.requiresSkill)) { session.Hud?.Flash("You have not learned " + spell.name + "."); return false; }
            if (Time.time < SpellReadyAt) { session.Hud?.Flash(spell.name + " is not ready."); return false; }
            if (Magic < spell.magicCost) { session.Hud?.Flash("Not enough magic."); return false; }
            if (!Stamina.TrySpend(spell.staminaCost)) { session.Hud?.Flash("Too tired to cast."); return false; }
            Attack.Cancel();
            Magic -= spell.magicCost;
            SpellReadyAt = Time.time + spell.cooldown;
            FaceTarget();
            lockUntil = Time.time + spell.castTime + .18f;
            castAt = Time.time + spell.castTime;
            Anim.PlayStrike(spell.anim, spell.castTime, hand);
            return true;
        }

        void Launch()
        {
            var spell = session.Core.Db.Spells[SelectedSpell];
            Vector3 origin = (hand ? hand.position : transform.position + Vector3.up * 1.1f) + transform.forward * .3f;
            var target = LockTarget && LockTarget.Alive ? LockTarget : session.NearestMonster(transform.position, 16, transform.forward, 70);
            Vector3 dir = target ? (target.AimPoint.position - origin).normalized : transform.forward;
            Projectile.Fire(session, spell, origin, dir, Stats);
        }

        public void CycleSpell()
        {
            var known = session.Core.Db.Spells.Values.Where(s => string.IsNullOrEmpty(s.requiresSkill) || session.Core.State.player.skills.Contains(s.requiresSkill)).Select(s => s.id).ToList();
            if (known.Count < 2) return;
            SelectedSpell = known[(known.IndexOf(SelectedSpell) + 1) % known.Count];
            session.Hud?.Flash("Spell: " + session.Core.Db.Spells[SelectedSpell].name);
        }

        // ---------- items ----------
        public void UseHotbar(int slot)
        {
            var bar = session.Core.State.player.hotbar;
            if (slot >= bar.Count || string.IsNullOrEmpty(bar[slot])) return;
            if (Attack.Committed || Dodging || Dead) return;
            var r = session.Core.Inventory.Use(bar[slot], this);
            if (!r.Ok) session.Hud?.Flash(r.Reason);
        }

        public Result CanConsume(ItemDef item)
        {
            if (item.heal > 0 && Health >= Stats[Stat.MaxHealth] && item.restoreStamina <= 0) return Result.Fail("Already at full health.");
            return Result.Success;
        }

        public void Consume(ItemDef item)
        {
            if (item.curePoison) GetComponent<VenomStatus>()?.Clear();
            Health = Mathf.Min(Stats[Stat.MaxHealth], Health + item.heal);
            Magic = Mathf.Min(Stats[Stat.MaxMagic], Magic + item.restoreMagic);
            Stamina.Current = Mathf.Min(Stamina.Max, Stamina.Current + item.restoreStamina);
            Attack.Cancel();
            if (HuntStyle) { lockUntil = Time.time + 1f; Anim.Play("Use_Item", false, 1.2f, true); }   // drinking roots you, as in MH
            else { lockUntil = Time.time + .55f; Anim.Play("Interact", false, 1.6f, true); }
            WorldKit.Burst(transform.position + Vector3.up, new Color(.4f, .9f, .4f));
            WorldKit.Sound(transform.position, "chime", .2f);
        }

        // ---------- lock-on ----------
        public void ToggleLock()
        {
            if (LockTarget) { LockTarget = null; return; }
            var cam = session.CameraRig.Camera.transform;
            LockTarget = session.NearestMonster(transform.position, 16, Vector3.ProjectOnPlane(cam.forward, Vector3.up), 75)
                ?? session.NearestMonster(transform.position, 10, transform.forward, 180);
            if (!LockTarget) session.Hud?.Flash("No target in view.");
        }

        // ---------- taking damage ----------
        public bool Overlaps(Vector3 center, float radius)
        {
            Vector3 p = transform.position; p.y = 0; center.y = 0;
            return Vector3.Distance(p, center) <= radius + Body.radius;
        }

        public void ReceiveHit(MonsterAttackDef attack, Vector3 from)
        {
            if (Dead) return;
            if (Invulnerable)
            {
                LastDodgeEvade = Time.time;
                session.Hud?.Flash("Evaded!");
                return;
            }
            if (Time.time < hurtUntil - .2f) return;
            if (HuntStyle && Hunt.TryGuard(attack, from)) return;
            var core = session.Core;
            float dmg = DamageMath.MonsterHit(attack, Stats[Stat.Defense], core.Stats.PlayerResistances());
            Health -= dmg;
            WorldKit.Burst(transform.position + Vector3.up * 1.1f, new Color(.8f, .15f, .08f));
            WorldKit.Sound(transform.position, "hit", .35f);
            session.CameraRig.Shake(.25f);
            Attack.Cancel(); attackBuffered = false; comboIndex = 0; castAt = -1;
            if (Health <= 0)
            {
                Health = 0; Dead = true; deathAt = Time.time; LockTarget = null;
                Anim.Play("Death_A", false, 1, true, .05f);
                return;
            }
            GamePad.Rumble(.7f, .45f, .25f);
            var away = transform.position - from; away.y = 0;
            if (HuntStyle) { Hunt.React(Reactions.For(attack, dmg), away.sqrMagnitude > .01f ? away.normalized : -transform.forward); return; }
            hurtUntil = Time.time + .42f;
            if (away.sqrMagnitude > .01f) knock = away.normalized * KnockSpeed;   // 0.25 m pushed over 0.2 s, not one frame
            Anim.Play("Hit_A", false, 1.3f, true, .05f);
        }

        public void TakeVenomDamage(float damage) {
            if (Dead || damage <= 0) return;
            Health = Mathf.Max(0, Health - damage);
            if (Health <= 0) { Dead = true; deathAt = Time.time; LockTarget = null; Attack.Cancel(); Anim.Play("Death_A", false, 1, true, .05f); }
        }

        public void Revive(Vector3 position, float yaw)
        {
            Dead = false; deathAt = -1;
            GetComponent<VenomStatus>()?.Clear();
            Teleport(position, yaw);
            Health = Stats[Stat.MaxHealth]; Stamina.Current = Stamina.Max; Magic = Stats[Stat.MaxMagic];
            Attack.Cancel(); hurtUntil = 0; lockUntil = 0;
            Anim.Play("Idle", true, 1, true);
        }

        public void Teleport(Vector3 p, float yaw)
        {
            knock = Vector3.zero;
            Body.enabled = false;
            transform.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
            Body.enabled = true;
            vertical = 0; LockTarget = null;
        }

        public void InterruptForMenu()
        {
            attackBuffered = false;
            if (!Attack.Committed) Attack.Cancel();
        }
    }
}
