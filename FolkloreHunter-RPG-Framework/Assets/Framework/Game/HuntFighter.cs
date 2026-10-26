using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Monster Hunter-style weapons on top of the hunter controller. Shared: draw/sheathe with the weapon on
    /// the back, light/heavy routes with buffered input and simultaneous Y+B, cancels late in moves, hit reactions
    /// (flinch, knockback, knockdown with a wake-up roll), hitstop and rumble, strafing while locked on, items.
    /// Sword and shield: rolls, charged heavy, shield guard with a perfect window and counter.
    /// Lance: tower-shield guard (wide, little chip), guard thrust and guard dash from the guard, counter stance that
    /// answers a blocked blow with a Retribution Thrust, dash attack with a finishing thrust, up to three hops.
    /// Bow: hold Y to draw through charge levels, quick follow-up shots rise a level, the charging sidestep adds one,
    /// B after a shot is a power shot (wider fan), B alone the arc shot (arrows rain on the target), Y+B the Dragon
    /// Piercer, RB aims over the shoulder. The bow is held in the left hand and drawn to the cheek (Monster Hunter's
    /// archer), with an arrow nocked while drawn.
    /// Katana (two-handed, Monster Hunter's long sword): RB is the Spirit Blade combo, hits fill the spirit gauge, a
    /// landed Spirit Roundslash raises the spirit level (white, yellow, red: more damage), RB+B is the Foresight Slash
    /// (a step back that counters a blow caught in it), RB+A the iai stance (Y: Iai Slash, RB: Iai Spirit Slash that
    /// counters a blow during its windup).
    /// Evasions: roll; drawn, a side slide or back-step that keeps facing; sprinting away from a monster, a dive.</summary>
    public class HuntFighter : MonoBehaviour
    {
        public HuntMovesetDef Set { get; private set; }
        public WeaponClass Class => Set != null ? Set.Class : WeaponClass.SwordShield;
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
        // Lance
        public bool CounterStance { get; private set; }
        public int Retributions { get; private set; }
        public int ChainedDodges => dodges.Count;
        // Bow
        public bool Drawing => bow != null && bow.Drawing;
        public bool Aiming { get; private set; }
        public int BowLevel => bow == null ? 0 : Drawing ? bow.Held(Time.time) : bow.Ready(Time.time);
        public int LastShotLevel { get; private set; }
        public int LastShotArrows { get; private set; }
        public Vector3 LastAimPoint { get; private set; }
        // Katana
        public SpiritGauge Spirit { get; private set; }
        public bool IaiStance { get; private set; }
        public bool Foresight => foresightAt > 0;
        public int ForesightCounters { get; private set; }
        public int SpiritCounters { get; private set; }
        public int SpiritLevel => Spirit != null ? Spirit.Level : 0;
        // Evasions
        public EvadeKind LastEvade { get; private set; }
        public int Evades { get; private set; }
        public bool Recovering => Time.time < recoverUntil;

        HunterController H;
        GameSession S;
        HuntInput buffered;
        float bufferedAt, pendingLight = -1, pendingHeavy = -1;
        float guardRaisedAt = -99, lastPerfectGuard = -99, lastRollEnd = -99, sheatheDoneAt = -1, reactionUntil, downStart, downUntil, getUpUntil;
        HuntMoveDef chargeMove;
        float chargeStart, counterStart, lastShotAt = -99, dodgeEndAt = -1;
        BowCharge bow;
        readonly DodgeChain dodges = new DodgeChain();
        int nextShotLevel = 1; bool nextIsPower;
        float iaiStart, foresightAt = -1, pendingSpirit = -1, recoverUntil;
        bool skipDodge, skipHeavy, spiritShort;
        int buffedSwing = -1, levelledSwing = -1, moveSerial;
        Transform chest, arrowTemplate, quiver, nocked, katanaGlow;
        Renderer glowRenderer; int shownGlow = -1;
        const float SimultaneousWindow = .07f, BufferLife = .5f;

        /// <summary>A weapon's two pieces (main hand, off hand or back piece) and where each sits in hand and on the back.</summary>
        class Piece { public Transform t, hand; public Vector3 handPos, backPos; public Quaternion handRot, backRot; public bool onBackAlways; }
        readonly Dictionary<WeaponClass, Piece[]> loadouts = new Dictionary<WeaponClass, Piece[]>();
        Piece[] pieces = new Piece[0];

        public void Initialize(GameSession s, HunterController h, HuntMovesetDef set)
        {
            S = s; H = h;
            chest = WorldKit.Find(H.Visual, "chest");
            arrowTemplate = WorldKit.Find(H.Visual, "Hunt_Arrow");
            quiver = WorldKit.Find(H.Visual, "Hunt_Quiver");
            nocked = WorldKit.Find(H.Visual, "Hunt_NockedArrow");
            var katana = WorldKit.Find(H.Visual, "Hunt_Katana");
            katanaGlow = WorldKit.Find(H.Visual, "Hunt_KatanaGlow");
            if (katana && katanaGlow && katanaGlow.parent != katana) katanaGlow.SetParent(katana, true);   // the edge goes where the blade goes
            glowRenderer = katanaGlow ? katanaGlow.GetComponent<Renderer>() : null;
            if (loadouts.Count == 0)
            {
                loadouts[WeaponClass.SwordShield] = new[] { Long(H.SwordTransform, -28, -.2f, .22f, .14f), Flat(WorldKit.Find(H.Visual, "Round_Shield")) };
                loadouts[WeaponClass.Lance] = new[] { Long(WorldKit.Find(H.Visual, "Hunt_Lance"), 152, -.22f, -.35f, -.1f), Flat(WorldKit.Find(H.Visual, "Hunt_TowerShield")) };
                var bowT = WorldKit.Find(H.Visual, "Hunt_Bow");
                loadouts[WeaponClass.Bow] = new[] { Long(bowT, 160, -.3f, .02f, .05f, bowT ? bowT.parent : null), new Piece { t = quiver, onBackAlways = true }, new Piece { t = nocked, onBackAlways = true } };
                loadouts[WeaponClass.Katana] = new[] { Long(katana, 52, -.26f, .32f, .22f) };
            }
            H.BladeHit -= OnBladeHit; H.BladeHit += OnBladeHit;
            H.ShotLoosed -= OnShotLoosed; H.ShotLoosed += OnShotLoosed;
            Equip(set);
        }

        /// <summary>Switches weapon class (the Proving Grounds ledger): shows its pieces, sheathes, resets its state.</summary>
        public void Equip(HuntMovesetDef set)
        {
            Set = set;
            CancelStates();
            bow = set.Class == WeaponClass.Bow ? new BowCharge(set) : null;
            Spirit = set.Class == WeaponClass.Katana ? new SpiritGauge() : null;
            shownGlow = -99;     // Equip just switched every katana renderer on: the glow is re-decided next frame
            dodges.Reset();
            foreach (var kv in loadouts)
                foreach (var p in kv.Value)
                    if (p != null && p.t) foreach (var r in p.t.GetComponentsInChildren<Renderer>(true)) r.enabled = kv.Key == set.Class;
            if (arrowTemplate) arrowTemplate.GetComponent<Renderer>().enabled = false;
            pieces = loadouts.TryGetValue(set.Class, out var ps) ? ps : new Piece[0];
            H.SetBlade((set.Class == WeaponClass.Lance || set.Class == WeaponClass.Katana) && pieces.Length > 0 ? pieces[0].t : H.SwordTransform);
            SetSheathed(true);
            LastEvent = "equipped " + set.name;
        }

        void CancelStates()
        {
            Guarding = Charging = CounterStance = Aiming = IaiStance = false;
            bow?.CancelDraw();
            buffered = HuntInput.None; pendingLight = pendingHeavy = pendingSpirit = foresightAt = -1;
            if (S && S.CameraRig) S.CameraRig.Aiming = false;
        }

        void OnDisable()
        {
            if (H)
            {
                SetSheathed(false);
                H.BladeHit -= OnBladeHit; H.ShotLoosed -= OnShotLoosed;
                // Back to the classic hunter: sword and shield in hand, the hunt-only weapons hidden.
                if (loadouts.TryGetValue(WeaponClass.SwordShield, out var sns))
                {
                    foreach (var kv in loadouts) foreach (var p in kv.Value) if (p != null && p.t && kv.Key != WeaponClass.SwordShield)
                    {
                        if (!p.onBackAlways && p.hand) { p.t.SetParent(p.hand, false); p.t.localPosition = p.handPos; p.t.localRotation = p.handRot; }
                        foreach (var r in p.t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                    }
                    foreach (var p in sns) if (p != null && p.t) foreach (var r in p.t.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                }
                H.SetBlade(H.SwordTransform);
                H.RefreshEquipmentPresentation();
            }
            if (S && S.CameraRig) S.CameraRig.Aiming = false;
            GamePad.StopRumble();
        }

        // ---------------- state the controller asks about ----------------
        /// <summary>Movement locked: charging, counter stance, knocked down, getting up, staggering, or sheathing.</summary>
        public bool Locked => Charging || CounterStance || IaiStance || Time.time < reactionUntil || Downed || Time.time < getUpUntil || Time.time < recoverUntil;
        public float SpeedScale => Guarding ? .3f : Drawing || Aiming ? .45f : Sheathed ? 1f : Set.drawnSpeed;
        public bool KeepFacing => H.LockTarget && H.LockTarget.Alive;
        /// <summary>A hop, sidestep, side slide, back-step or dive is under way: the hunter does not turn with the stick.</summary>
        public bool StepDodging => dodgeEndAt > 0 && H.Dodging;
        /// <summary>Guarding (raised shield), charging, the counter stance and the bow's draw own the idle pose.</summary>
        public bool OwnsIdlePose => Guarding || Charging || CounterStance || IaiStance || Drawing || Aiming || Downed || Time.time < getUpUntil || Time.time < recoverUntil;

        /// <summary>Locked on and drawn, or aiming / drawing the bow: the hunter faces the target or the aim while moving.</summary>
        public bool FacesTarget(out Vector3 dir)
        {
            dir = Vector3.zero;
            if (Sheathed || H.Dodging || H.Attack.Committed || Locked) return false;
            if (Aiming) { dir = Flat(S.CameraRig.Camera.transform.forward); return dir.sqrMagnitude > .01f; }
            if (!KeepFacing) return false;
            dir = H.LockTarget.transform.position - H.transform.position; dir.y = 0;
            return dir.sqrMagnitude > .01f;
        }

        /// <summary>Strafe and backpedal clips while locked on; the aiming walk with the bow; the raised shield.</summary>
        public bool LocomotionClip(Vector3 dir, bool running, out string clip, out float natural)
        {
            clip = null; natural = 0;
            if (Guarding) { clip = "Blocking"; return true; }
            if ((Drawing || Aiming) && !Sheathed) { clip = "Bow_Aim_Walk"; natural = 1.2f; return true; }
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
            if (H.Dead) { Guarding = Charging = CounterStance = IaiStance = false; bow?.CancelDraw(); SetAim(false); return; }
            Spirit?.Tick(now, Time.deltaTime);
            UpdateWeaponVisuals(now);
            if (InputRouter.Down(GameAction.LockOn)) H.ToggleLock();
            if (InputRouter.Down(GameAction.CycleSpell)) H.CycleSpell();
            if (InputRouter.Down(GameAction.Whistle) && S.Hound) S.Hound.Call();
            if (InputRouter.Down(GameAction.Spell) && !Locked && !H.Attack.Running && !Drawing) H.TryCast();
            HandleItems();

            // Knocked down: lie there; a roll after half a second is a wake-up roll (invulnerable on the way up).
            if (Downed)
            {
                if (InputRouter.Down(GameAction.Dodge) && now - downStart > .5f) { downUntil = 0; H.InvulnerableFor(.45f); H.Roll(dir, false); LastEvent = "wake-up roll"; }
                return;
            }
            if (getUpUntil > 0 && now >= getUpUntil) getUpUntil = 0;
            if (sheatheDoneAt > 0 && now >= sheatheDoneAt) { sheatheDoneAt = -1; SetSheathed(true); }
            if (dodgeEndAt > 0 && !H.Dodging) { dodges.Ended(now); dodgeEndAt = -1; EvadeEnded(dir, now); }

            bool wantRun = InputRouter.Held(GameAction.Run) && dir.sqrMagnitude > .01f;
            if (wantRun && !Sheathed && !H.Attack.Running && !Charging && !CounterStance && sheatheDoneAt < 0) { bow?.CancelDraw(); SetAim(false); BeginSheathe(); }
            Sprinting = wantRun && Sheathed && sheatheDoneAt < 0;
            if (InputRouter.Down(GameAction.Sheathe) && !Sheathed && !S.Focus && !H.Attack.Running && !Charging && !CounterStance) { bow?.CancelDraw(); SetAim(false); BeginSheathe(); }

            skipDodge = skipHeavy = false;
            if (Class == WeaponClass.Bow) HandleBow(dir, now);
            else if (Class == WeaponClass.Katana) HandleKatana(dir, now);
            else HandleGuard(now);

            // Evade: roll, side slide, back-step or dive; hop (lance); charging sidestep (bow). Inside a move only once
            // the move allows it.
            if (InputRouter.Down(GameAction.Dodge) && !skipDodge) TryDodge(dir, now);

            if (CounterStance) { UpdateCounter(dir, now); return; }
            if (IaiStance) { UpdateIai(dir, now); return; }

            // Attacks: Y and B pressed together (within 70 ms) are the special (shield bash, wide sweep, Dragon Piercer).
            // The first press opens the 70 ms window; mashing must not keep pushing it back.
            if (InputRouter.Down(GameAction.Attack) && pendingLight < 0) pendingLight = now;
            if (InputRouter.Down(GameAction.HeavyAttack) && pendingHeavy < 0 && !skipHeavy) pendingHeavy = now;
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
            if (Drawing) UpdateDraw(dir, now);
        }

        void Queue(HuntInput input) { buffered = input; bufferedAt = Time.time; }

        // ---------------- guard (sword and shield, lance) ----------------
        void HandleGuard(float now)
        {
            // Guard: hold RB / Left Ctrl. Raising it from sheathed draws the weapon at once.
            bool wantGuard = InputRouter.Held(GameAction.Guard) && !H.Attack.Running && !H.Dodging && !Charging && !CounterStance && !Locked && !H.Hurt;
            if (wantGuard && Sheathed) Draw();
            if (wantGuard && !Guarding) { guardRaisedAt = now; H.Anim.Play("Block", false, 1.6f, true, .05f); LastEvent = "guard"; }
            if (!wantGuard && Guarding && !CounterStance) H.Anim.Play("Idle", true, 1, false, .12f);
            Guarding = wantGuard;
            if (Guarding && H.Anim.Finished("Block")) H.Anim.Play("Blocking");
        }

        // ---------------- evasions ----------------
        void TryDodge(Vector3 dir, float now)
        {
            var cur = Current;
            if (H.Hurt || Foresight || now < recoverUntil || !HuntRoutes.CanRollCancel(cur, H.Attack.Normalized)) return;
            float side = Vector3.Dot(dir, H.transform.right), front = Vector3.Dot(dir, H.transform.forward);
            var kind = Evade.Choose(Set, !Sheathed, Sprinting, Sprinting && Fleeing(dir), side, front);
            if (kind == EvadeKind.Hop || kind == EvadeKind.Sidestep) { StepDodge(kind, dir, now); return; }
            var spec = Evade.Spec(Set, kind, -1);
            if (Charging) Charging = false;
            // A roll keeps facing only when locked on with the weapon out; side slides and back-steps always do.
            bool keep = spec.KeepFacing || (kind == EvadeKind.Roll && KeepFacing && !Sheathed);
            string clip = kind == EvadeKind.SideSlide ? (side > 0 ? "Dodge_Right" : "Dodge_Left") : kind == EvadeKind.Backstep ? "Dodge_Backward" : kind == EvadeKind.Dive ? "Jump_Full_Short" : null;
            float clipSpeed = kind == EvadeKind.Dive ? Mathf.Clamp(H.Anim.Length("Jump_Full_Short") / (spec.Time + .1f), .6f, 2.5f) : spec.ClipSpeed;
            if (!H.Roll(dir, keep, spec.Distance, spec.Time, spec.Stamina, spec.Invulnerable, clipSpeed, clip)) return;
            if (kind != EvadeKind.Roll) dodgeEndAt = now + spec.Time;    // steps and the dive: the stick does not turn the hunter
            lastRollEnd = now + spec.Time; buffered = HuntInput.None; Guarding = false; CounterStance = false; IaiStance = false;
            LastEvade = kind; Evades++;
            LastEvent = kind == EvadeKind.SideSlide ? "side slide " + (side > 0 ? "right" : "left") : kind == EvadeKind.Backstep ? "back-step" : kind == EvadeKind.Dive ? "dive" : "roll";
        }

        /// <summary>Sprinting with the stick pointing away from a monster within 12 m: a dive instead of a roll.</summary>
        bool Fleeing(Vector3 dir)
        {
            var m = S.NearestMonster(H.transform.position, 12, Vector3.zero, 180);
            if (!m || dir.sqrMagnitude < .1f) return false;
            var away = H.transform.position - m.transform.position; away.y = 0;
            return away.sqrMagnitude > .01f && Vector3.Dot(dir.normalized, away.normalized) > .3f;
        }

        /// <summary>Lance hop or bow charging sidestep: short, chainable, keeps facing.</summary>
        void StepDodge(EvadeKind kind, Vector3 dir, float now)
        {
            if (H.Dodging || !dodges.TryStart(Set, now, out bool last)) return;
            var spec = Evade.Spec(Set, kind, -1);
            float distance = spec.Distance * (last ? 1.6f : 1), time = spec.Time * (last ? 1.3f : 1);
            bool keepDraw = Drawing;
            if (Charging) Charging = false;
            CounterStance = false;
            // Hops and sidesteps keep facing the front (or the target); with no stick the lance hops back.
            Vector3 d = dir.sqrMagnitude > .1f ? dir : Class == WeaponClass.Lance ? -H.transform.forward : H.transform.right;
            string clip = Class == WeaponClass.Bow && !Sheathed ? (Vector3.Dot(d, H.transform.right) >= 0 ? "Bow_Step_R" : "Bow_Step_L") : null;
            if (Class == WeaponClass.Bow && Mathf.Abs(Vector3.Dot(d.normalized, H.transform.forward)) > .7f) clip = null;   // forward / back: the plain dodge clip
            if (!H.Roll(d, true, distance, time, spec.Stamina, spec.Invulnerable, spec.ClipSpeed, clip)) { dodges.Reset(); return; }
            dodgeEndAt = now + time; buffered = HuntInput.None; Guarding = false;
            LastEvade = kind; Evades++;
            if (Class == WeaponClass.Bow)
            {
                int level = bow.Stepped(now);
                if (keepDraw) bow.BeginDraw(now);   // the draw carries on from the raised level
                LastEvent = "charging sidestep (level " + level + ")";
                Pip(level);
            }
            else LastEvent = last ? "long hop" : "hop " + dodges.Count;
            lastRollEnd = now + time;
        }

        /// <summary>A step, slide or dive just ended: the draw resumes, the dive's landing, the foresight slash strikes.</summary>
        void EvadeEnded(Vector3 dir, float now)
        {
            if (Drawing) H.Anim.Play("Bow_Draw", false, 2f, true, .08f);
            if (LastEvade == EvadeKind.Dive)
            {
                recoverUntil = now + Evade.Spec(Set, EvadeKind.Dive, -1).Recover;
                H.Anim.Play("Lie_StandUp", false, 2.4f, true, .05f);
                LastEvent = "dive (landing)";
            }
            if (foresightAt > 0)
            {
                bool caught = H.LastDodgeEvade >= foresightAt;
                foresightAt = -1;
                if (caught)
                {
                    ForesightCounters++;
                    Spirit?.Fill(now);
                    WorldKit.Burst(H.transform.position + Vector3.up * 1.2f, new Color(.75f, .9f, 1f), 20);
                    WorldKit.Sound(H.transform.position, "chime", .4f);
                    GamePad.Rumble(.4f, .8f, .12f);
                    S.Hud?.Flash("Foresight! Spirit gauge full");
                    Execute(Set.Move(Set.foresightCounter), Vector3.zero);
                    LastEvent = "foresight counter";
                }
                else { Execute(Set.Move(Set.foresightSlash), Vector3.zero); LastEvent = "foresight slash"; }
            }
        }

        // ---------------- moves ----------------
        void TryExecute(Vector3 dir)
        {
            if (H.Dodging || Locked || H.Hurt) return;
            if (Class == WeaponClass.Lance && Guarding && TryGuardMove(dir)) return;
            if (Guarding && buffered != HuntInput.LightHeavy && buffered != HuntInput.Light) return;
            if (Class == WeaponClass.Bow && TryBowMove(dir)) return;
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
            // Katana: Y+B with the stick to the side is the Lateral Fade Slash (a slash that slides sideways).
            if (Class == WeaponClass.Katana && input == HuntInput.LightHeavy && dir.sqrMagnitude > .25f && Set.Move(Set.lateralFade) != null)
            {
                float side = Vector3.Dot(dir.normalized, H.transform.right);
                if (Mathf.Abs(side) > .6f)
                {
                    var slide = H.transform.right * Mathf.Sign(side);
                    Execute(Set.Move(Set.lateralFade), Vector3.zero);
                    H.Push(slide * (2 * 2.6f / .35f), .35f);    // about 2.6 m sideways
                    LastEvent = "lateral fade slash " + (side > 0 ? "right" : "left");
                    return;
                }
            }
            Execute(next, dir);
        }

        /// <summary>Lance moves from the raised guard: Y = guard thrust (with the stick: guard dash), B = counter stance,
        /// Y+B = dash attack.</summary>
        bool TryGuardMove(Vector3 dir)
        {
            HuntMoveDef next = null;
            var input = buffered;
            if (input == HuntInput.Light || input == HuntInput.ForwardLight)
            {
                if (SincePerfectGuard <= Set.counterWindow && Set.Move(Set.counter) != null) next = Set.Move(Set.counter);
                else next = dir.sqrMagnitude > .25f ? Set.Move(Set.guardDash) : Set.Move(Set.guardAttack);
            }
            else if (input == HuntInput.LightHeavy) next = Set.Move(Set.dashAttack);
            else if (input == HuntInput.Heavy) { buffered = HuntInput.None; BeginCounter(); return true; }
            if (next == null) return false;
            buffered = HuntInput.None;
            Guarding = false;
            Execute(next, dir);
            return true;
        }

        void Execute(HuntMoveDef move, Vector3 dir)
        {
            // Aim: the stick if pushed, else the locked target, else the nearest monster ahead.
            Vector3 aim = dir;
            if (aim.sqrMagnitude < .05f)
            {
                // Ranged moves look 30 m ahead, a charging move (dash attack, guard dash) 10 m, a swing 3.2 m.
                float range = move.shot != null ? 30 : move.travel > 0 ? 10 : 3.2f;
                var target = H.LockTarget && H.LockTarget.Alive ? H.LockTarget : S.NearestMonster(H.transform.position, range, H.transform.forward, move.shot != null ? 40 : move.travel > 0 ? 60 : 100);
                if (target) { aim = target.transform.position - H.transform.position; aim.y = 0; }
            }
            if (Aiming) aim = Flat(S.CameraRig.Camera.transform.forward);
            if (aim.sqrMagnitude > .01f) H.transform.rotation = Quaternion.LookRotation(aim);
            if (Spirit != null) move = SpiritMove(move);
            if (H.StartMove(move)) { moveSerial++; LastMove = move.name ?? move.id; LastEvent = LastMove + (spiritShort ? " (gauge short)" : ""); }
        }

        /// <summary>Katana: a spirit move spends gauge (short of it, the move is weaker and a roundslash does not level
        /// up); every move hits harder with the spirit level.</summary>
        HuntMoveDef SpiritMove(HuntMoveDef move)
        {
            if (move == null) return null;
            float now = Time.time;
            spiritShort = move.spiritCost > 0 && !Spirit.Spend(now, move.spiritCost);
            float k = Spirit.DamageMultiplier * (spiritShort ? .6f : 1);
            if (k == 1 && !spiritShort) return move;
            var c = move.Clone();
            c.damageMultiplier *= k;
            if (spiritShort) c.spiritLevelUp = false;
            return c;
        }

        // ---------------- charge (sword and shield heavy) ----------------
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

        // ---------------- lance counter stance ----------------
        void BeginCounter()
        {
            if (Set.Move(Set.counterRelease) == null) return;
            if (Sheathed) Draw();
            CounterStance = true; Guarding = false; counterStart = Time.time;
            H.CancelActions();
            H.Anim.Play("Blocking", true, 1, true, .05f);
            WorldKit.Burst(H.transform.position + Vector3.up * 1.1f + H.transform.forward * .5f, new Color(.6f, .85f, 1f), 8);
            LastEvent = "counter stance";
        }

        void UpdateCounter(Vector3 dir, float now)
        {
            if (dir.sqrMagnitude > .05f) H.transform.rotation = Quaternion.Slerp(H.transform.rotation, Quaternion.LookRotation(dir), 1 - Mathf.Exp(-Time.deltaTime * 3));
            if (!InputRouter.Held(GameAction.HeavyAttack) || now - counterStart > Set.counterStanceTime)
            {
                CounterStance = false;
                Execute(Set.Move(Set.counterRelease), dir);
            }
        }

        // ---------------- katana ----------------
        /// <summary>RB tapped: the Spirit Blade route. RB with B: Foresight Slash. RB with A: the iai stance.</summary>
        void HandleKatana(Vector3 dir, float now)
        {
            if (IaiStance) return;    // the stance reads its own buttons
            bool rb = InputRouter.Held(GameAction.Guard);
            bool rbDown = InputRouter.Down(GameAction.Guard);
            if (rbDown && pendingHeavy >= 0) { pendingHeavy = pendingSpirit = -1; TryForesight(now); return; }
            if (rbDown && pendingSpirit < 0) pendingSpirit = now;
            bool withRb = rb || pendingSpirit >= 0;
            if (withRb && InputRouter.Down(GameAction.Dodge)) { pendingSpirit = -1; skipDodge = true; TryIai(now); return; }
            if (withRb && InputRouter.Down(GameAction.HeavyAttack)) { pendingSpirit = -1; skipHeavy = true; TryForesight(now); return; }
            if (pendingSpirit >= 0 && now - pendingSpirit >= SimultaneousWindow) { pendingSpirit = -1; Queue(HuntInput.Spirit); }
        }

        /// <summary>Foresight Slash: a quick step back, invulnerable; a blow that passes through it is answered with
        /// the Foresight Counter and fills the spirit gauge, otherwise it is a plain slash.</summary>
        void TryForesight(float now)
        {
            if (H.Hurt || H.Dodging || Foresight || !HuntRoutes.CanRollCancel(Current, H.Attack.Normalized) || Set.Move(Set.foresightSlash) == null) return;
            if (Sheathed) Draw();
            if (KeepFacing) { var t = H.LockTarget.transform.position - H.transform.position; t.y = 0; if (t.sqrMagnitude > .01f) H.transform.rotation = Quaternion.LookRotation(t); }
            if (!H.Roll(-H.transform.forward, true, 1.9f, .34f, 10, .34f, 1.7f, "Dodge_Backward")) return;
            foresightAt = now; dodgeEndAt = now + .34f;
            buffered = HuntInput.None; LastEvade = EvadeKind.Backstep;
            WorldKit.Burst(H.transform.position + Vector3.up * 1.1f, new Color(.75f, .9f, 1f), 8);
            LastEvent = "foresight step";
        }

        /// <summary>Iai stance (Monster Hunter's special sheathe): the blade held low at the hip, up to a few seconds.</summary>
        void TryIai(float now)
        {
            if (H.Hurt || H.Dodging || Foresight || !HuntRoutes.CanRollCancel(Current, H.Attack.Normalized) || Set.Move(Set.iaiSlash) == null) return;
            if (Sheathed) Draw();
            H.CancelActions();
            IaiStance = true; iaiStart = now; buffered = HuntInput.None; pendingLight = pendingHeavy = -1;
            H.Anim.Play("Katana_Iai", true, 1, true, .08f);
            LastEvent = "iai stance";
        }

        void UpdateIai(Vector3 dir, float now)
        {
            if (now <= iaiStart) return;     // the press that opened the stance is not also its follow-up
            if (dir.sqrMagnitude > .05f) H.transform.rotation = Quaternion.Slerp(H.transform.rotation, Quaternion.LookRotation(dir), 1 - Mathf.Exp(-Time.deltaTime * 3));
            if (InputRouter.Down(GameAction.Attack)) { IaiStance = false; Execute(Set.Move(Set.iaiSlash), dir); return; }
            if (InputRouter.Down(GameAction.Guard)) { IaiStance = false; Execute(Set.Move(Set.iaiSpiritSlash), dir); return; }
            if (now - iaiStart > Set.iaiStanceTime) { IaiStance = false; H.Anim.Play("Idle", true, 1, false, .15f); LastEvent = "iai stance released"; }
        }

        static Color SpiritColor(int level) => level >= 3 ? new Color(1f, .22f, .12f) : level == 2 ? new Color(1f, .82f, .25f) : new Color(.92f, .95f, 1f);

        /// <summary>The katana's edge glows in the spirit level's colour (white, yellow, red); the bow's nocked arrow
        /// shows while the bow is out and drawn, and leaves the string for a moment after each shot.</summary>
        void UpdateWeaponVisuals(float now)
        {
            if (glowRenderer)
            {
                int want = Class != WeaponClass.Katana ? -2 : Sheathed ? -1 : Spirit.Level;
                if (want != shownGlow)
                {
                    shownGlow = want;
                    glowRenderer.enabled = want >= 1;
                    if (want >= 1) glowRenderer.sharedMaterial = WorldKit.Glow(SpiritColor(want), 2.5f);
                }
            }
            if (nocked && Class == WeaponClass.Bow)
            {
                var r = nocked.GetComponent<Renderer>();
                if (r) r.enabled = !Sheathed && now - lastShotAt > .3f && !(Current != null && Current.shot != null && Current.shot.arc);
            }
        }

        // ---------------- bow ----------------
        void HandleBow(Vector3 dir, float now)
        {
            // RB / Left Ctrl: aim over the shoulder (draws the bow if sheathed).
            bool wantAim = InputRouter.Held(GameAction.Guard) && !H.Hurt && !Locked;
            if (wantAim && Sheathed && !H.Attack.Running) Draw();
            SetAim(wantAim && !Sheathed);
            if (Aiming && !Drawing && !H.Attack.Running && !H.Dodging && !H.Anim.Current.EndsWith("Bow_Aim_Walk")) H.Anim.Play("Bow_Draw", false, 1.6f, false, .1f);
            // Standing still while drawing or aiming: the held draw, not the aiming walk running in place.
            if ((Aiming || Drawing) && H.Velocity.sqrMagnitude < .01f && H.Anim.Current.EndsWith("Bow_Aim_Walk")) H.Anim.Play("Bow_Draw", false, 2.5f, true, .1f);
            if (Aiming) LastAimPoint = AimPoint(out _);
        }

        void SetAim(bool on)
        {
            if (on == Aiming) return;
            Aiming = on;
            if (S && S.CameraRig) S.CameraRig.Aiming = on;
            if (!on && !Drawing && !H.Attack.Running && !H.Dodging) H.Anim.Play("Idle", true, 1, false, .12f);
        }

        /// <summary>Bow routes: Y draws (released: a shot at the drawn level; quick follow-ups are a level higher), B right
        /// after a shot is the power shot and otherwise the arc shot, Y+B the Dragon Piercer.</summary>
        bool TryBowMove(Vector3 dir)
        {
            var cur = Current;
            if (cur != null && !HuntRoutes.CanChain(cur, H.Attack.Normalized)) return true;    // stays buffered
            var input = buffered;
            float now = Time.time;
            if (Sheathed && input != HuntInput.LightHeavy) { buffered = HuntInput.None; Draw(); nextShotLevel = 1; nextIsPower = false; Execute(Set.Move(Set.drawAttack) ?? Set.Move(Set.shot), dir); return true; }
            if (Sheathed) Draw();
            switch (input)
            {
                case HuntInput.Light: case HuntInput.ForwardLight:
                    buffered = HuntInput.None;
                    if (Drawing) return true;
                    if (!InputRouter.Held(GameAction.Attack)) { FireShot(Set.Move(Set.shot), bow.Ready(now), false, dir); return true; }
                    H.CancelActions();
                    bow.BeginDraw(now);
                    H.Anim.Play("Bow_Draw", false, H.Anim.Length("Bow_Draw") / Mathf.Max(.2f, Set.chargeTimes.Count > 0 ? Set.chargeTimes[Set.chargeTimes.Count - 1] : 1), true, .08f);
                    LastEvent = "drawing";
                    return true;
                case HuntInput.Heavy:
                    buffered = HuntInput.None;
                    bool afterShot = now - lastShotAt <= Set.chainShotWindow && Set.Move(Set.powerShot) != null;
                    if (afterShot) { bow.CancelDraw(); FireShot(Set.Move(Set.powerShot), bow.Ready(now), true, dir); }
                    else { bow.CancelDraw(); nextShotLevel = 1; nextIsPower = false; Execute(Set.Move(Set.arcShot), dir); }
                    return true;
                case HuntInput.LightHeavy:
                    buffered = HuntInput.None;
                    if (Drawing) return true;
                    nextShotLevel = 1; nextIsPower = false;
                    Execute(Set.Move(Set.dragonPiercer), dir);
                    return true;
            }
            return false;
        }

        void UpdateDraw(Vector3 dir, float now)
        {
            int level = bow.Held(now);
            if (level > ChargeLevel) { ChargeLevel = level; Pip(level); }
            if (!Aiming && dir.sqrMagnitude > .05f && !KeepFacing) H.transform.rotation = Quaternion.Slerp(H.transform.rotation, Quaternion.LookRotation(dir), 1 - Mathf.Exp(-Time.deltaTime * 6));
            H.Stamina.Drain(Set.chargeStaminaPerSecond, Time.deltaTime);
            bool release = !InputRouter.Held(GameAction.Attack) || H.Stamina.Current <= 1;
            if (release && !H.Dodging) { int l = bow.Held(now); bow.CancelDraw(); ChargeLevel = 0; FireShot(Set.Move(Set.shot), l, false, dir); }
        }

        void FireShot(HuntMoveDef move, int level, bool power, Vector3 dir)
        {
            if (move == null) return;
            nextShotLevel = Mathf.Clamp(level, 1, Set.MaxLevel); nextIsPower = power;
            Execute(move, dir);
        }

        void Pip(int level)
        {
            Vector3 at = H.Hand ? H.Hand.position : H.transform.position + Vector3.up;
            WorldKit.Burst(at, level >= 3 ? new Color(1f, .45f, .15f) : level == 2 ? new Color(1f, .8f, .35f) : new Color(.9f, .9f, .8f), 6 + level * 4);
            WorldKit.Sound(H.transform.position, "chime", .12f + .08f * level);
            GamePad.Rumble(.1f * level, .2f * level, .08f);
        }

        /// <summary>Where the reticle points: the camera's centre ray against monsters and the world (60 m).</summary>
        Vector3 AimPoint(out MonsterActor monster)
        {
            monster = null;
            var cam = S.CameraRig.Camera.transform;
            var ray = new Ray(cam.position, cam.forward);
            float best = 60; Vector3 point = ray.origin + ray.direction * 60;
            if (Physics.Raycast(ray, out var w, 60, Layers.WorldMask, QueryTriggerInteraction.Ignore)) { best = w.distance; point = w.point; }
            if (Physics.SphereCast(ray, .2f, out var hb, best, Layers.HurtboxMask, QueryTriggerInteraction.Collide))
            {
                var box = hb.collider.GetComponent<Hurtbox>();
                if (box && box.Owner && box.Owner.Alive) { monster = box.Owner; point = hb.point; }
            }
            return point;
        }

        /// <summary>The shot's target point: the reticle when aiming, else the locked target's (or the nearest monster
        /// ahead's) aim point, else straight ahead.</summary>
        Vector3 ShotTarget(Vector3 origin, out MonsterActor target)
        {
            if (Aiming) return AimPoint(out target);
            target = H.LockTarget && H.LockTarget.Alive ? H.LockTarget : S.NearestMonster(H.transform.position, 30, H.transform.forward, 40);
            if (target) return target.AimPoint ? target.AimPoint.position : target.transform.position + Vector3.up;
            return origin + H.transform.forward * 30;
        }

        void OnShotLoosed(HuntMoveDef move)
        {
            if (!enabled || move.shot == null) return;
            var shot = move.shot;
            float now = Time.time;
            // Loosed from the bow hand (the left), where the nocked arrow rests.
            var bowHand = pieces.Length > 0 && pieces[0] != null && pieces[0].hand ? pieces[0].hand : H.Hand;
            Vector3 origin = (bowHand ? bowHand.position : H.transform.position + Vector3.up * 1.2f) + H.transform.forward * .3f;
            Vector3 aimAt = ShotTarget(origin, out var target);
            LastAimPoint = aimAt;
            if (shot.arc)
            {
                // Arc shot: arrows rise, then rain around the target (or 12 m ahead) after a delay.
                Vector3 center = target ? target.transform.position : Aiming ? aimAt : H.transform.position + H.transform.forward * 12;
                center.y = S.GroundHeight(center);
                for (int i = 0; i < 3; i++)
                {
                    var up = Quaternion.AngleAxis(-70 + i * 4, H.transform.right) * H.transform.forward;
                    ArrowShot.Fire(S, H, arrowTemplate, new ShotDef { speed = 30, range = 10 }, Hit(move, 0, 0, 0), origin, up);   // the volley rising (no damage)
                }
                foreach (var (x, z) in ShotPattern.Rain(shot.arrows, shot.arcRadius))
                {
                    var land = center + new Vector3(x, 0, z);
                    ArrowShot.Fire(S, H, arrowTemplate, shot, Hit(move, move.damageMultiplier, move.poiseMultiplier, move.koDamage), land + Vector3.up * 14, Vector3.down, shot.arcDelay);
                }
                LastShotLevel = 0; LastShotArrows = shot.arrows;
                bow?.Reset();
                return;
            }
            int level = nextShotLevel;
            int arrows = shot.arrows; float spread = shot.spread, mv = move.damageMultiplier, poise = move.poiseMultiplier;
            if (shot.usesChargeLevel)
            {
                var l = Set.Level(level);
                arrows = l.arrows + shot.bonusArrows; spread = (l.spread > 0 ? l.spread : 4) * shot.spreadScale; mv *= l.motionValue; poise *= l.poise;
            }
            Vector3 dir = (aimAt - origin).normalized;
            foreach (float yaw in ShotPattern.Fan(arrows, spread))
                ArrowShot.Fire(S, H, arrowTemplate, shot, Hit(move, mv, poise, move.koDamage), origin, Quaternion.AngleAxis(yaw, Vector3.up) * dir);
            LastShotLevel = shot.usesChargeLevel ? level : 0; LastShotArrows = arrows;
            if (shot.pierce) { bow?.Reset(); GamePad.Rumble(.6f, .8f, .3f); S.CameraRig.Shake(move.shake); }
            else if (nextIsPower) bow.PowerShot(level, now);
            else bow?.Fired(level, now);
            lastShotAt = now;
            nextIsPower = false;
            WorldKit.Sound(origin, "swish", .2f);
        }

        /// <summary>The per-arrow hit: the move's timing and stun with this arrow's motion value and poise.</summary>
        static HuntMoveDef Hit(HuntMoveDef move, float mv, float poise, float ko)
        {
            var c = move.Clone(); c.damageMultiplier = mv; c.poiseMultiplier = poise; c.koDamage = ko; return c;
        }

        // ---------------- guard ----------------
        /// <summary>Called before damage: a raised shield (or a guard-point move's windup) facing the blow stops it.
        /// The lance's counter stance stops it and answers with a Retribution Thrust.</summary>
        public bool TryGuard(MonsterAttackDef attack, Vector3 from)
        {
            var cur = Current;
            bool guardPoint = cur != null && cur.guardPoint && (H.Attack.Phase == AttackPhase.Windup || cur.guardPointActive && H.Attack.Phase == AttackPhase.Active);
            if (!Guarding && !guardPoint && !CounterStance) return false;
            var to = from - H.transform.position; to.y = 0;
            if (guardPoint && cur.spiritCounter)
            {
                // Iai Spirit Slash: a blow caught in the windup is parried; the slash lands and raises the spirit level.
                if (Vector3.Angle(H.transform.forward, to) > 100) return false;
                SpiritCounters++;
                Spirit?.LevelUp(Time.time); Spirit?.Fill(Time.time);
                levelledSwing = moveSerial;
                Vector3 at = H.transform.position + Vector3.up * 1.1f + H.transform.forward * .5f;
                WorldKit.Burst(at, new Color(.75f, .9f, 1f), 24);
                WorldKit.Sound(at, "chime", .45f);
                GamePad.Rumble(.5f, .9f, .15f);
                S.Hud?.Flash("Iai Spirit counter! Spirit level " + SpiritLevel);
                H.HitstopUntil = Time.time + .08f;
                LastEvent = "iai spirit counter";
                return true;
            }
            float angle = Vector3.Angle(H.transform.forward, to);
            Vector3 spark = H.transform.position + Vector3.up * 1.0f + H.transform.forward * .4f;
            if (CounterStance)
            {
                if (angle > Set.guardAngle) return false;
                CounterStance = false;
                Retributions++;
                WorldKit.Burst(spark, new Color(.6f, .9f, 1f), 22);
                WorldKit.Sound(spark, "chime", .4f);
                GamePad.Rumble(.6f, .9f, .15f);
                S.Hud?.Flash("Counter! Retribution Thrust");
                H.HitstopUntil = Time.time + .08f;
                var dir = to.sqrMagnitude > .01f ? to.normalized : H.transform.forward;
                Execute(Set.Move(Set.retribution), dir);
                LastEvent = "retribution";
                return true;
            }
            float since = guardPoint ? 0 : Time.time - guardRaisedAt;
            var r = GuardMath.Resolve(Set, attack.damage, angle, since, H.Stamina.Current);
            if (!r.Blocked) return false;
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
            LastGuardDamage = r.Damage;
            return true;
        }

        public float LastGuardDamage { get; private set; }

        // ---------------- reactions ----------------
        public void React(HitReaction reaction, Vector3 away)
        {
            LastReaction = reaction;
            Charging = false; Guarding = false; CounterStance = false; IaiStance = false; foresightAt = -1; buffered = HuntInput.None;
            bow?.CancelDraw(); SetAim(false);
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
            if (H.ReportingRangedHit) { monster.HitstopUntil = Time.time + .02f; GamePad.Rumble(.08f, .15f, .06f); return; }   // arrows: no freeze on the archer
            var move = Current;
            if (Spirit != null && move != null)
            {
                float now = Time.time;
                if (move.spiritCost <= 0) Spirit.Hit(now, Set.spiritPerHit * (move.rehit > 0 ? .5f : 1));
                if (move.spiritLevelUp && levelledSwing != moveSerial)
                {
                    levelledSwing = moveSerial; Spirit.LevelUp(now);
                    S.Hud?.Flash("Spirit level " + Spirit.Level);
                    WorldKit.Burst(H.BladeTip, SpiritColor(Spirit.Level), 24);
                }
                if (move.iaiBuff > 0 && buffedSwing != moveSerial) { buffedSwing = moveSerial; Spirit.Iai(now, move.iaiBuff); S.Hud?.Flash("Iai: the spirit gauge fills"); }
            }
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
            bool locked = Locked || H.Attack.Running || H.Dodging || Guarding || Drawing;
            if (InputRouter.Down(GameAction.UseItem) && !locked) H.UseHotbar(SelectedItem);
            for (int i = 0; i < 4; i++) if (InputRouter.Down(GameAction.Hotbar1 + i) && !locked) H.UseHotbar(i);
        }

        static int Next(List<string> bar, int from, int step)
        {
            for (int k = 1; k <= bar.Count; k++)
            {
                int i = ((from + step * k) % bar.Count + bar.Count) % bar.Count;
                if (!string.IsNullOrEmpty(bar[i])) return i;
            }
            return from;
        }

        void ShowItem(List<string> bar)
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
            foreach (var p in pieces)
            {
                if (p == null || !p.t || p.onBackAlways || !p.hand || !chest) continue;
                if (on) { p.t.SetParent(chest, false); p.t.localPosition = p.backPos; p.t.localRotation = p.backRot; }
                else { p.t.SetParent(p.hand, false); p.t.localPosition = p.handPos; p.t.localRotation = p.handRot; }
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        /// <summary>Works out once where a long weapon sits on the back, relative to the chest bone. Sword: blade down
        /// behind the right shoulder (tilt -28). Lance: point up across the back (tilt 152 from down), the grip low so
        /// the long shaft clears the ground. Bow: across the middle of the back, over the quiver (gripped by the left
        /// hand). Katana: hilt over the right shoulder, the blade slanting down across the back.</summary>
        Piece Long(Transform w, float tilt, float back, float gripUp, float gripRight, Transform grip = null)
        {
            var p = new Piece { t = w };
            if (!w || !chest) return p;
            p.hand = w.parent; p.handPos = w.localPosition; p.handRot = w.localRotation;
            var sm = w.GetComponent<MeshFilter>() ? w.GetComponent<MeshFilter>().sharedMesh : null;
            if (!sm) return p;
            Vector3 fwd = H.transform.forward, right = H.transform.right, up = Vector3.up;
            var b = sm.bounds;
            Vector3 axis = b.size.x > b.size.y && b.size.x > b.size.z ? Vector3.right * b.extents.x : b.size.y > b.size.z ? Vector3.up * b.extents.y : Vector3.forward * b.extents.z;
            if (!grip) grip = H.Hand;
            Vector3 gripLocal = grip ? w.InverseTransformPoint(grip.position) : b.center - axis;
            Vector3 tipLocal = Vector3.Distance(b.center + axis, gripLocal) > Vector3.Distance(b.center - axis, gripLocal) ? b.center + axis : b.center - axis;
            Vector3 tipDir = (w.TransformPoint(tipLocal) - w.TransformPoint(gripLocal)).normalized;
            Vector3 want = Quaternion.AngleAxis(tilt, fwd) * -up;
            Quaternion rot = Quaternion.FromToRotation(tipDir, want) * w.rotation;
            Vector3 gripTarget = chest.position + fwd * back + up * gripUp + right * gripRight;
            Vector3 pos = gripTarget - rot * Vector3.Scale(gripLocal, w.lossyScale);
            p.backRot = Quaternion.Inverse(chest.rotation) * rot;
            p.backPos = chest.InverseTransformPoint(pos);
            return p;
        }

        /// <summary>A shield lies flat on the back, outer face out.</summary>
        Piece Flat(Transform shield)
        {
            var p = new Piece { t = shield };
            if (!shield || !chest) return p;
            p.hand = shield.parent; p.handPos = shield.localPosition; p.handRot = shield.localRotation;
            var shm = shield.GetComponent<MeshFilter>() ? shield.GetComponent<MeshFilter>().sharedMesh : null;
            if (!shm) return p;
            Vector3 fwd = H.transform.forward, up = Vector3.up;
            var b = shm.bounds;
            Vector3 normalLocal = b.size.x < b.size.y && b.size.x < b.size.z ? Vector3.right : b.size.y < b.size.z ? Vector3.up : Vector3.forward;
            Vector3 n = shield.rotation * normalLocal;
            if (Vector3.Dot(n, shield.TransformPoint(b.center) - chest.position) < 0) n = -n;   // outer face
            Quaternion rot = Quaternion.FromToRotation(n, -fwd) * shield.rotation;
            Vector3 centerTarget = chest.position - fwd * .24f + up * .02f;
            Vector3 pos = centerTarget - rot * Vector3.Scale(b.center, shield.lossyScale);
            p.backRot = Quaternion.Inverse(chest.rotation) * rot;
            p.backPos = chest.InverseTransformPoint(pos);
            return p;
        }
    }
}
