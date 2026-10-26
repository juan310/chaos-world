using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Monster Hunter-style sword and shield on top of the hunter controller: draw/sheathe with the weapons on
    /// the back, light/heavy/charged routes with buffered input and simultaneous Y+B, roll cancels late in moves,
    /// shield guard with a perfect window and counter, hit reactions (flinch, knockback, knockdown with a wake-up
    /// roll), hitstop and rumble on hits, strafing while locked on, and item use with the drink animation.</summary>
    public class HuntFighter : MonoBehaviour
    {
        public HuntMovesetDef Set { get; private set; }
        public bool Sheathed { get; private set; } = true;
        public bool Guarding { get; private set; }
        public bool Charging { get; private set; }
        public int ChargeLevel { get; private set; }
        public bool Sprinting { get; private set; }
        public bool Downed => Time.time < downUntil;
        public HitReaction LastReaction { get; private set; }
        public string LastMove { get; private set; } = "";
        public string LastEvent { get; private set; } = "";
        public float LastHitstop { get; private set; }
        public int SelectedItem { get; private set; }
        public float SincePerfectGuard => Time.time - lastPerfectGuard;
        public float ChargeHeld => Charging ? Time.time - chargeStart : 0;
        public HuntMoveDef Current => H.Attack.Running ? H.Attack.Def as HuntMoveDef : null;

        HunterController H;
        GameSession S;
        HuntInput buffered;
        float bufferedAt, pendingLight = -1, pendingHeavy = -1;
        float guardRaisedAt = -99, lastPerfectGuard = -99, lastRollEnd = -99, sheatheDoneAt = -1, reactionUntil, downStart, downUntil, getUpUntil;
        HuntMoveDef chargeMove;
        float chargeStart;
        Transform sword, shield, swordHand, shieldHand, chest;
        Vector3 swordHandPos, shieldHandPos, swordBackPos, shieldBackPos;
        Quaternion swordHandRot, shieldHandRot, swordBackRot, shieldBackRot;
        const float SimultaneousWindow = .07f, BufferLife = .5f;

        public void Initialize(GameSession s, HunterController h, HuntMovesetDef set)
        {
            S = s; H = h; Set = set;
            sword = H.SwordTransform;
            shield = WorldKit.Find(H.Visual, "Round_Shield");
            chest = WorldKit.Find(H.Visual, "chest");
            PrepareBackPoses();
            SetSheathed(true);
            H.BladeHit -= OnBladeHit;
            H.BladeHit += OnBladeHit;
        }

        void OnDisable() { if (H) { SetSheathed(false); H.BladeHit -= OnBladeHit; } GamePad.StopRumble(); }

        // ---------------- state the controller asks about ----------------
        /// <summary>Movement locked: charging, knocked down, getting up, staggering from a blow, or sheathing.</summary>
        public bool Locked => Charging || Time.time < reactionUntil || Downed || Time.time < getUpUntil;
        public float SpeedScale => Guarding ? .3f : Sheathed ? 1f : Set.drawnSpeed;
        public bool KeepFacing => H.LockTarget && H.LockTarget.Alive;
        /// <summary>Guarding owns the idle pose (the raised shield).</summary>
        public bool OwnsIdlePose => Guarding || Charging || Downed || Time.time < getUpUntil;

        /// <summary>Locked on and drawn: the hunter faces the target while moving (strafing), not the stick.</summary>
        public bool FacesTarget(out Vector3 dir)
        {
            dir = Vector3.zero;
            if (!KeepFacing || Sheathed || H.Dodging || H.Attack.Committed || Locked) return false;
            dir = H.LockTarget.transform.position - H.transform.position; dir.y = 0;
            return dir.sqrMagnitude > .01f;
        }

        /// <summary>Strafe and backpedal clips while locked on, so the feet move the way the body travels.</summary>
        public bool LocomotionClip(Vector3 dir, bool running, out string clip, out float natural)
        {
            clip = null; natural = 0;
            if (Guarding) { clip = "Blocking"; return true; }
            if (!KeepFacing || Sheathed) return false;
            float side = Vector3.Dot(dir, H.transform.right), front = Vector3.Dot(dir, H.transform.forward);
            if (front > Mathf.Abs(side)) return false;            // mostly forward: the normal walk/run
            if (front < -Mathf.Abs(side)) { clip = "Walking_Backwards"; natural = 1.1f; return true; }
            clip = side > 0 ? "Running_Strafe_Right" : "Running_Strafe_Left"; natural = 2.0f;
            return true;
        }

        // ---------------- input ----------------
        public void HandleInput(Vector3 dir)
        {
            float now = Time.time;
            if (H.Dead) { Guarding = Charging = false; return; }
            if (InputRouter.Down(GameAction.LockOn)) H.ToggleLock();
            if (InputRouter.Down(GameAction.CycleSpell)) H.CycleSpell();
            if (InputRouter.Down(GameAction.Whistle) && S.Hound) S.Hound.Call();
            if (InputRouter.Down(GameAction.Spell) && !Locked && !H.Attack.Running) H.TryCast();
            HandleItems();

            // Knocked down: lie there; a roll after half a second is a wake-up roll (invulnerable on the way up).
            if (Downed)
            {
                if (InputRouter.Down(GameAction.Dodge) && now - downStart > .5f) { downUntil = 0; H.InvulnerableFor(.45f); H.Roll(dir, false); LastEvent = "wake-up roll"; }
                return;
            }
            if (getUpUntil > 0 && now >= getUpUntil) getUpUntil = 0;
            if (sheatheDoneAt > 0 && now >= sheatheDoneAt) { sheatheDoneAt = -1; SetSheathed(true); }

            bool wantRun = InputRouter.Held(GameAction.Run) && dir.sqrMagnitude > .01f;
            if (wantRun && !Sheathed && !H.Attack.Running && !Charging && sheatheDoneAt < 0) BeginSheathe();
            Sprinting = wantRun && Sheathed && sheatheDoneAt < 0;
            if (InputRouter.Down(GameAction.Sheathe) && !Sheathed && !S.Focus && !H.Attack.Running && !Charging) BeginSheathe();

            // Guard: hold RB / Left Ctrl. Raising it from sheathed draws the weapon at once.
            bool wantGuard = InputRouter.Held(GameAction.Guard) && !H.Attack.Running && !H.Dodging && !Charging && !Locked && !H.Hurt;
            if (wantGuard && Sheathed) Draw();
            if (wantGuard && !Guarding) { guardRaisedAt = now; H.Anim.Play("Block", false, 1.6f, true, .05f); LastEvent = "guard"; }
            if (!wantGuard && Guarding) H.Anim.Play("Idle", true, 1, false, .12f);
            Guarding = wantGuard;
            if (Guarding && H.Anim.Current.EndsWith("Block") && !H.Anim.Anim.isPlaying) H.Anim.Play("Blocking");

            // Roll: always from neutral; inside a move only once the move allows it.
            if (InputRouter.Down(GameAction.Dodge))
            {
                var cur = Current;
                if (Charging) Charging = false;
                if (!H.Hurt && HuntRoutes.CanRollCancel(cur, H.Attack.Normalized) && H.Roll(dir, KeepFacing && !Sheathed))
                {
                    lastRollEnd = now + .42f; buffered = HuntInput.None; Guarding = false; LastEvent = "roll";
                }
            }

            // Attacks: Y and B pressed together (within 70 ms) are the shield bash.
            // The first press opens the 70 ms window; mashing must not keep pushing it back.
            if (InputRouter.Down(GameAction.Attack) && pendingLight < 0) pendingLight = now;
            if (InputRouter.Down(GameAction.HeavyAttack) && pendingHeavy < 0) pendingHeavy = now;
            if (pendingLight >= 0 && pendingHeavy >= 0) { Queue(HuntInput.LightHeavy); pendingLight = pendingHeavy = -1; }
            else if (pendingLight >= 0 && now - pendingLight >= SimultaneousWindow)
            {
                bool forward = dir.sqrMagnitude > .25f && Vector3.Dot(dir.normalized, H.transform.forward) > .7f && !H.Attack.Running;
                Queue(forward ? HuntInput.ForwardLight : HuntInput.Light); pendingLight = -1;
            }
            else if (pendingHeavy >= 0 && now - pendingHeavy >= SimultaneousWindow) { Queue(HuntInput.Heavy); pendingHeavy = -1; }

            if (buffered != HuntInput.None && now - bufferedAt > BufferLife) buffered = HuntInput.None;
            if (buffered != HuntInput.None) TryExecute(dir);
            if (Charging) UpdateCharge(dir);
        }

        void Queue(HuntInput input) { buffered = input; bufferedAt = Time.time; }

        void TryExecute(Vector3 dir)
        {
            if (H.Dodging || Locked || H.Hurt || Guarding && buffered != HuntInput.LightHeavy && buffered != HuntInput.Light) return;
            var cur = Current;
            HuntMoveDef next;
            if (cur != null)
            {
                if (!HuntRoutes.CanChain(cur, H.Attack.Normalized)) return;     // keep it buffered until the chain window
                next = HuntRoutes.Next(Set, cur, buffered);
                if (next == null) { if (H.Attack.Running) return; next = HuntRoutes.Opener(Set, buffered, Sheathed, 99, 99); }
            }
            else next = HuntRoutes.Opener(Set, buffered, Sheathed, Time.time - lastRollEnd, Time.time - lastPerfectGuard);
            var input = buffered;
            buffered = HuntInput.None;
            if (next == null) return;
            if (Sheathed) Draw();
            Guarding = false;
            if (next.chargeTime > 0 && input == HuntInput.Heavy && InputRouter.Held(GameAction.HeavyAttack)) { BeginCharge(next, dir); return; }
            Execute(next, dir);
        }

        void Execute(HuntMoveDef move, Vector3 dir)
        {
            // Aim: the stick if pushed, else the locked target, else the nearest monster ahead.
            Vector3 aim = dir;
            if (aim.sqrMagnitude < .05f)
            {
                var target = H.LockTarget && H.LockTarget.Alive ? H.LockTarget : S.NearestMonster(H.transform.position, 3.2f, H.transform.forward, 100);
                if (target) { aim = target.transform.position - H.transform.position; aim.y = 0; }
            }
            if (aim.sqrMagnitude > .01f) H.transform.rotation = Quaternion.LookRotation(aim);
            if (H.StartMove(move)) { LastMove = move.name ?? move.id; LastEvent = LastMove; }
        }

        // ---------------- charge ----------------
        void BeginCharge(HuntMoveDef move, Vector3 dir)
        {
            Charging = true; chargeMove = move; chargeStart = Time.time; ChargeLevel = 0;
            if (dir.sqrMagnitude > .05f) H.transform.rotation = Quaternion.LookRotation(dir);
            H.CancelActions();
            H.Anim.Hold(move.anim, .3f);
            LastEvent = "charging";
        }

        void UpdateCharge(Vector3 dir)
        {
            float held = Time.time - chargeStart;
            if (dir.sqrMagnitude > .05f) H.transform.rotation = Quaternion.Slerp(H.transform.rotation, Quaternion.LookRotation(dir), 1 - Mathf.Exp(-Time.deltaTime * 4));
            int level = ChargeMeter.Level(chargeMove, held);
            if (level > ChargeLevel)
            {
                ChargeLevel = level;
                WorldKit.Burst(H.Hand ? H.Hand.position : H.transform.position + Vector3.up, level == 2 ? new Color(1f, .45f, .15f) : new Color(1f, .85f, .4f), 10 + level * 6);
                WorldKit.Sound(H.transform.position, "chime", .2f + .1f * level);
                GamePad.Rumble(.2f * level, .35f * level, .12f);
                S.Hud?.Flash("Charge " + level);
            }
            if (!InputRouter.Held(GameAction.HeavyAttack) || held > 3f) ReleaseCharge(dir);
        }

        void ReleaseCharge(Vector3 dir)
        {
            Charging = false;
            var move = ChargeLevel >= 2 && !string.IsNullOrEmpty(chargeMove.maxChargeMove) ? Set.Move(chargeMove.maxChargeMove) ?? chargeMove : chargeMove;
            Execute(ChargeMeter.Charged(Set, move, ChargeLevel), dir);
            if (ChargeLevel > 0) LastEvent = LastMove + " (charge " + ChargeLevel + ")";
        }

        // ---------------- guard ----------------
        /// <summary>Called before damage: a raised shield (or a guard-point move's windup) facing the blow stops it.</summary>
        public bool TryGuard(MonsterAttackDef attack, Vector3 from)
        {
            var cur = Current;
            bool guardPoint = cur != null && cur.guardPoint && H.Attack.Phase == AttackPhase.Windup;
            if (!Guarding && !guardPoint) return false;
            var to = from - H.transform.position; to.y = 0;
            float angle = Vector3.Angle(H.transform.forward, to);
            float since = guardPoint ? 0 : Time.time - guardRaisedAt;
            var r = GuardMath.Resolve(Set, attack.damage, angle, since, H.Stamina.Current);
            if (!r.Blocked) return false;
            Vector3 spark = H.transform.position + Vector3.up * 1.0f + H.transform.forward * .4f;
            if (r.Perfect)
            {
                lastPerfectGuard = Time.time;
                WorldKit.Burst(spark, new Color(.6f, .9f, 1f), 18);
                WorldKit.Sound(spark, "chime", .35f);
                GamePad.Rumble(.3f, .7f, .1f);
                S.Hud?.Flash("Perfect guard! Counter now");
                H.HitstopUntil = Time.time + .06f;
                LastEvent = "perfect guard";
                return true;
            }
            H.Health = Mathf.Max(1, H.Health - r.Damage);
            H.Stamina.TrySpend(Mathf.Min(r.Stamina, H.Stamina.Current));
            var away = -to.normalized;
            H.Push(away * (r.Pushback / .25f) * 2, .25f);
            if (!guardPoint) { H.Anim.Play("Block_Hit", false, 1.4f, true, .04f); reactionUntil = Time.time + .25f; }
            WorldKit.Burst(spark, new Color(1f, .85f, .5f), 10);
            WorldKit.Sound(spark, "hit", .3f);
            GamePad.Rumble(.5f, .3f, .15f);
            LastEvent = guardPoint ? "guard point" : "guarded";
            return true;
        }

        // ---------------- reactions ----------------
        public void React(HitReaction reaction, Vector3 away)
        {
            LastReaction = reaction;
            Charging = false; Guarding = false; buffered = HuntInput.None;
            H.CancelActions();
            float now = Time.time;
            switch (reaction)
            {
                case HitReaction.Knockdown:
                    H.Anim.Play("Lie_Down", false, 1.25f, true, .05f);
                    H.Push(away * 5f, .35f);
                    downStart = now; downUntil = now + 1.25f;
                    H.HurtFor(1.25f);
                    GamePad.Rumble(1f, .6f, .35f);
                    LastEvent = "knocked down";
                    break;
                case HitReaction.Knockback:
                    H.Anim.Play("Hit_B", false, 1.2f, true, .05f);
                    H.Push(away * 4f, .3f);
                    reactionUntil = now + .6f; H.HurtFor(.6f);
                    LastEvent = "knocked back";
                    break;
                default:
                    H.Anim.Play("Hit_A", false, 1.3f, true, .05f);
                    H.Push(away * 2.5f, .2f);
                    reactionUntil = now + .38f; H.HurtFor(.42f);
                    LastEvent = "flinched";
                    break;
            }
        }

        void LateUpdate()
        {
            // Getting up after a knockdown that was not rolled out of: invulnerable while standing up.
            if (downUntil > 0 && Time.time >= downUntil && !H.Dead && !H.Dodging)
            {
                downUntil = 0;
                H.Anim.Play("Lie_StandUp", false, 1.3f, true, .05f);
                getUpUntil = Time.time + .75f;
                H.InvulnerableFor(.75f);
                LastEvent = "getting up";
            }
        }

        // ---------------- hits dealt ----------------
        void OnBladeHit(MonsterActor monster, HitInfo hit)
        {
            if (!enabled) return;
            var move = Current;
            float stop = move != null ? move.hitstop : .04f;
            if (monster.LastHitZone >= 1.3f || hit.WeakPoint) stop *= 1.25f;
            H.HitstopUntil = Time.time + stop;
            monster.HitstopUntil = Time.time + stop;
            LastHitstop = stop;
            S.CameraRig.Shake(move != null ? move.shake : .12f);
            float rumble = move != null ? move.rumble : .2f;
            GamePad.Rumble(rumble * .7f, rumble, stop + .08f);
        }

        // ---------------- items ----------------
        void HandleItems()
        {
            var bar = S.Core.State.player.hotbar;
            int count = bar.Count;
            if (count == 0) return;
            if (InputRouter.Down(GameAction.ItemNext)) { SelectedItem = Next(bar, SelectedItem, 1); ShowItem(bar); }
            if (InputRouter.Down(GameAction.ItemPrev)) { SelectedItem = Next(bar, SelectedItem, -1); ShowItem(bar); }
            bool locked = Locked || H.Attack.Running || H.Dodging || Guarding;
            if (InputRouter.Down(GameAction.UseItem) && !locked) H.UseHotbar(SelectedItem);
            for (int i = 0; i < 4; i++) if (InputRouter.Down(GameAction.Hotbar1 + i) && !locked) H.UseHotbar(i);
        }

        static int Next(System.Collections.Generic.List<string> bar, int from, int step)
        {
            for (int k = 1; k <= bar.Count; k++)
            {
                int i = ((from + step * k) % bar.Count + bar.Count) % bar.Count;
                if (!string.IsNullOrEmpty(bar[i])) return i;
            }
            return from;
        }

        void ShowItem(System.Collections.Generic.List<string> bar)
        {
            var id = bar[SelectedItem];
            if (string.IsNullOrEmpty(id)) return;
            int have = S.Core.Inventory.Count(id);
            S.Hud?.Flash(S.Core.Db.Item(id).name + "  x" + have);
        }

        // ---------------- draw / sheathe (weapons move between the hands and the back) ----------------
        void Draw() { sheatheDoneAt = -1; SetSheathed(false); }

        void BeginSheathe()
        {
            Guarding = false;
            sheatheDoneAt = Time.time + Set.sheatheTime;
            LastEvent = "sheathing";
        }

        public void SetSheathed(bool on)
        {
            Sheathed = on;
            Place(sword, on, chest, swordBackPos, swordBackRot, swordHand, swordHandPos, swordHandRot);
            Place(shield, on, chest, shieldBackPos, shieldBackRot, shieldHand, shieldHandPos, shieldHandRot);
        }

        static void Place(Transform t, bool back, Transform backParent, Vector3 backPos, Quaternion backRot, Transform handParent, Vector3 handPos, Quaternion handRot)
        {
            if (!t || !backParent || !handParent) return;
            if (back) { t.SetParent(backParent, false); t.localPosition = backPos; t.localRotation = backRot; }
            else { t.SetParent(handParent, false); t.localPosition = handPos; t.localRotation = handRot; }
        }

        /// <summary>Works out once where the sword and shield sit on the back, relative to the chest bone: the sword
        /// hangs blade-down behind the right shoulder, the shield lies flat on the back, outer face out.</summary>
        void PrepareBackPoses()
        {
            if (!sword || !shield || !chest) return;
            swordHand = sword.parent; shieldHand = shield.parent;
            swordHandPos = sword.localPosition; swordHandRot = sword.localRotation;
            shieldHandPos = shield.localPosition; shieldHandRot = shield.localRotation;
            Vector3 fwd = H.transform.forward, right = H.transform.right, up = Vector3.up;

            var sm = sword.GetComponent<MeshFilter>() ? sword.GetComponent<MeshFilter>().sharedMesh : null;
            if (sm)
            {
                var b = sm.bounds;
                Vector3 axis = b.size.x > b.size.y && b.size.x > b.size.z ? Vector3.right * b.extents.x : b.size.y > b.size.z ? Vector3.up * b.extents.y : Vector3.forward * b.extents.z;
                Vector3 gripLocal = H.Hand ? sword.InverseTransformPoint(H.Hand.position) : b.center - axis;
                Vector3 tipLocal = Vector3.Distance(b.center + axis, gripLocal) > Vector3.Distance(b.center - axis, gripLocal) ? b.center + axis : b.center - axis;
                Vector3 tipDir = (sword.TransformPoint(tipLocal) - sword.TransformPoint(gripLocal)).normalized;
                Vector3 want = Quaternion.AngleAxis(-28, fwd) * -up;                       // blade down, angled across the back
                Quaternion rot = Quaternion.FromToRotation(tipDir, want) * sword.rotation;
                Vector3 gripTarget = chest.position - fwd * .2f + up * .22f + right * .14f;
                Vector3 pos = gripTarget - rot * Vector3.Scale(gripLocal, sword.lossyScale);
                swordBackRot = Quaternion.Inverse(chest.rotation) * rot;
                swordBackPos = chest.InverseTransformPoint(pos);
                swordBackPos = Vector3.Scale(swordBackPos, Vector3.one);
            }
            var shm = shield.GetComponent<MeshFilter>() ? shield.GetComponent<MeshFilter>().sharedMesh : null;
            if (shm)
            {
                var b = shm.bounds;
                Vector3 normalLocal = b.size.x < b.size.y && b.size.x < b.size.z ? Vector3.right : b.size.y < b.size.z ? Vector3.up : Vector3.forward;
                Vector3 n = shield.rotation * normalLocal;
                if (Vector3.Dot(n, shield.TransformPoint(b.center) - chest.position) < 0) n = -n;   // outer face
                Quaternion rot = Quaternion.FromToRotation(n, -fwd) * shield.rotation;
                Vector3 centerTarget = chest.position - fwd * .24f + up * .02f;
                Vector3 pos = centerTarget - rot * Vector3.Scale(b.center, shield.lossyScale);
                shieldBackRot = Quaternion.Inverse(chest.rotation) * rot;
                shieldBackPos = chest.InverseTransformPoint(pos);
            }
        }
    }
}
