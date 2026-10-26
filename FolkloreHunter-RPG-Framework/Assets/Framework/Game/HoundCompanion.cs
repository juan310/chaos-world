using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    public enum HoundState { Follow, Assist, Called, Ridden, Sitting }

    /// <summary>The Briarhound: a great shaggy hunting hound in Monster Hunter Rise's Palamute role. It heels (and runs
    /// beside a sprinting hunter), comes when whistled, and fights the hunted monster with the crescent blade in its
    /// jaws: bite, return slash and spinning slash in a combo, a pounce to close in, a flanking position across the
    /// monster from the hunter, and a hop clear of the monster's wind-ups. Ridden it walks, lopes, dashes (RT), drifts
    /// through sharp turns (RB while dashing), attacks on Y (chain up to three) and sidesteps on A, without costing the
    /// rider stamina. Gait speeds come from the clip's paw stride so the feet do not slide.</summary>
    public class HoundCompanion : MonoBehaviour
    {
        public const string DisplayName = "Briarhound";
        /// <summary>The generated Briarhound model when present, else the earlier hand-built hound.</summary>
        public static string Model => Resources.Load<GameObject>("Models/Briarhound") ? "Briarhound" : "CuSith";
        /// <summary>Overall height to the ear tips. The Briarhound is a big mount: withers about the hunter's height.</summary>
        public static float Height => Model == "Briarhound" ? 2.5f : 1.9f;
        /// <summary>Size relative to the original 1.9 m hound (collision, reach and offsets scale with it).</summary>
        public static float Size => Height / 1.9f;
        bool moving;
        public HoundState State { get; private set; } = HoundState.Follow;
        public AnimationDriver Anim { get; private set; }
        public Transform Visual { get; private set; }
        public Transform Saddle { get; private set; }
        public float WalkSpeed { get; private set; } = 2.4f;
        public float GallopSpeed { get; private set; } = 7.5f;
        public float Speed { get; private set; }
        public int Bites { get; private set; }
        public string LastEvent { get; private set; } = "";
        public bool Ridden => State == HoundState.Ridden;

        GameSession S;
        CharacterController cc;
        Transform pawL, pawR;
        float vertical, calledAt, idleFor, stuckFor;
        int mountedFrame = -10;
        Vector3 lastPos;
        HoundMount mount;

        public static HoundCompanion Spawn(GameSession s, Vector3 at, float yaw)
        {
            var go = CharacterFactory.Actor(DisplayName, s.SafeGround(at) + Vector3.up * .05f, 1.25f * Size, .42f * Size);
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var h = go.AddComponent<HoundCompanion>();
            h.Initialize(s);
            return h;
        }

        void Initialize(GameSession s)
        {
            S = s;
            cc = GetComponent<CharacterController>();
            if (s.Hunter && s.Hunter.Body) Physics.IgnoreCollision(cc, s.Hunter.Body);   // a companion never body-blocks its hunter
            if (s.Hunter) { s.Hunter.BladeHit -= OnHunterHit; s.Hunter.BladeHit += OnHunterHit; }
            Visual = CharacterFactory.Create(Model, transform, Height);
            foreach (var r in Visual.GetComponentsInChildren<Renderer>(true).Where(r => r.name.Contains("Eye_")))
                r.sharedMaterial = WorldKit.Glow(new Color(.55f, 1f, .72f), 3);
            Anim = new AnimationDriver(Visual, Model);
            Saddle = WorldKit.Find(Visual, "saddle");
            pawL = WorldKit.Find(Visual, "fore_paw.l"); pawR = WorldKit.Find(Visual, "fore_paw.r");
            float walkNatural = Anim.NaturalSpeed("Walk", pawL, pawR, transform, true);
            float gallopNatural = Anim.NaturalSpeed("Gallop", pawL, pawR, transform, true);
            if (walkNatural > .3f) WalkSpeed = Mathf.Clamp(walkNatural, 1.6f, 3.2f);
            if (gallopNatural > 1f) GallopSpeed = Mathf.Clamp(gallopNatural, 5.5f, 9f);
            Debug.Log("FH_HOUND walk " + WalkSpeed.ToString("0.00") + " m/s (natural " + walkNatural.ToString("0.00") + "), gallop " + GallopSpeed.ToString("0.00") + " m/s (natural " + gallopNatural.ToString("0.00") + ")");
            var m = new GameObject("Hound mount point").AddComponent<HoundMount>();
            m.transform.SetParent(transform, false); m.transform.localPosition = Vector3.up * .6f * Size; m.Hound = this; m.Range = 2.4f * Size;
            mount = m;
            Anim.Play("Idle");
            lastPos = transform.position;
        }

        // ---------------- commands ----------------
        /// <summary>Whistle: comes to the hunter; if it is far away it reappears close by, out of the way.</summary>
        public void Call()
        {
            if (Ridden) return;
            var h = S.Hunter.transform;
            if (Vector3.Distance(h.position, transform.position) > 30)
            {
                var p = h.position - h.forward * 4 + h.right * 2;
                Warp(S.SafeGround(p), h.eulerAngles.y);
                LastEvent = "appeared";
            }
            State = HoundState.Called; LastEvent = "called"; calledAt = Time.time;
            Anim.Play("Howl", false, 1.2f, true);
            WorldKit.Sound(transform.position, "chime", .3f);
        }

        public bool CanMount => !Ridden && !S.Hunter.Dead && !S.Hunter.Attack.Running && !S.Hunter.Dodging && !(S.Hunter.Hunt && S.Hunter.Hunt.Locked);

        public void Mount()
        {
            if (!CanMount) return;
            var h = S.Hunter;
            h.Hunt?.SetSheathed(true);
            h.LockTarget = null;
            h.BeginRide(this);
            mountedFrame = Time.frameCount;   // the press that mounted must not also dismount
            State = HoundState.Ridden;
            LastEvent = "mounted";
        }

        /// <summary>Gets off beside the hound. <paramref name="thrown"/>: knocked off by a blow.</summary>
        public void Dismount(bool thrown = false)
        {
            if (!Ridden) return;
            var side = transform.position + transform.right * 1.3f * Size;
            S.Hunter.EndRide(S.SafeGround(side), transform.eulerAngles.y);
            State = HoundState.Follow;
            LastEvent = thrown ? "rider thrown" : "dismounted";
        }

        /// <summary>Puts the hound somewhere at once (the Training Range, the verifier).</summary>
        public void PlaceAt(Vector3 p, float yaw) { Warp(p, yaw); lastPos = p; }

        void Warp(Vector3 p, float yaw)
        {
            cc.enabled = false; transform.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0)); cc.enabled = true;
        }

        // ---------------- per frame ----------------
        void Update()
        {
            if (!S || !S.Simulating) { if (Anim != null && (Anim.Current.EndsWith("Walk") || Anim.Current.EndsWith("Gallop"))) Anim.Play("Idle"); return; }
            float dt = Time.deltaTime;
            // Unity forgets an ignored pair whenever either collider is switched off and on (a teleport, mounting): re-assert it.
            if (S.Hunter && S.Hunter.Body && S.Hunter.Body.enabled && cc.enabled) Physics.IgnoreCollision(cc, S.Hunter.Body);
            if (Ridden) RideTick(dt);
            else AiTick(dt);
            vertical = cc.isGrounded ? -2 : Mathf.Max(-20, vertical - dt * 24);
            cc.Move(Vector3.up * vertical * dt);
            var moved = transform.position - lastPos; moved.y = 0;
            Speed = dt > 0 ? moved.magnitude / dt : 0;
            lastPos = transform.position;
        }

        // ---------------- actions: strikes, pounce, hops (shared by the companion and the ridden hound) ----------------
        /// <summary>A strike, pounce or hop is playing out (no steering until it ends).</summary>
        public bool Acting => Time.time < actionEnd;
        public HoundAction Action { get; private set; } = HoundAction.Follow;
        public int Pounces { get; private set; }
        public int Evades { get; private set; }
        public int RideAttacks { get; private set; }
        public int RideHops { get; private set; }
        public bool Dashing { get; private set; }
        public bool Drifting { get; private set; }
        /// <summary>Clips of the strikes started, in order (the verifier reads the combo from it).</summary>
        public readonly List<string> Sequence = new List<string>();
        /// <summary>What each strike did: the part it cut, or why it missed (distance and angle to the nearest part).</summary>
        public readonly List<string> StrikeLog = new List<string>();
        float actionEnd, strikeAt = -1, travelFrom, travelUntil, travelSpeed, comboReadyAt, pounceReadyAt, evadeReadyAt, rideHopReadyAt, bank, dustClock;
        Vector3 travelDir; bool travelKeepsFacing, rideQueued;
        int comboStep = -1;
        float lastStrikeAt = -99;
        public bool PounceReady => Time.time >= pounceReadyAt;
        HoundStrike strike;
        MonsterActor strikeTarget;
        Quaternion visualBase = Quaternion.identity; bool visualBaseSet;

        void StartStrike(HoundStrike st, int step, MonsterActor target)
        {
            strike = st; comboStep = step; strikeTarget = target;
            if (target)
            {
                var aimBox = st.AllAround ? null : BestBox(target, transform.position, st.Reach * Size + 1, true, out _);
                var face = (aimBox ? aimBox.transform.position : target.transform.position) - transform.position; face.y = 0;
                if (face.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(face);
            }
            Anim.Play(st.Clip, false, Mathf.Max(.3f, Anim.Length(st.Clip) / st.Length), true, .05f);
            strikeAt = Time.time + st.HitAt; actionEnd = Time.time + st.Length;
            if (target && st.Clip != "Pounce")
            {
                // A step in with the strike when the monster has drifted to the edge of reach (the combo connects).
                float gap = Vector3.Distance(Flat(target.transform.position), Flat(transform.position)) - target.Def.bodyRadius;
                float stepIn = Mathf.Clamp(gap - st.Reach * Size * .6f, 0, .9f * Size);
                if (stepIn > .05f) Travel(Flat(target.transform.position - transform.position), stepIn, 0, st.HitAt * .9f, true);
                else travelUntil = 0;
            }
            Sequence.Add(st.Clip); if (Sequence.Count > 4000) Sequence.RemoveRange(0, 1000);
            Trace("strike " + st.Clip);
            LastEvent = st.Clip == "Bite" ? "bite" : st.Clip == "Slash_R" ? "slash" : st.Clip == "Spin" ? "spin" : "pounce";
        }

        /// <summary>Moves the hound along <paramref name="dir"/> between two times of the current action (a pounce's leap, a hop).</summary>
        void Travel(Vector3 dir, float distance, float from, float until, bool keepFacing)
        {
            travelDir = dir.normalized; travelFrom = Time.time + from; travelUntil = Time.time + until;
            travelSpeed = distance / Mathf.Max(.05f, until - from); travelKeepsFacing = keepFacing;
        }

        void UpdateAction(float dt)
        {
            float now = Time.time;
            if (now >= travelFrom && now < travelUntil)
            {
                var to = S.ConstrainMove(transform.position, transform.position + travelDir * travelSpeed * dt);
                cc.Move(to - transform.position);
                if (!travelKeepsFacing && travelDir.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(travelDir);
            }
            if (strikeAt > 0 && now >= strikeAt) { strikeAt = -1; lastStrikeAt = now; LandStrike(); }
        }

        /// <summary>The strike lands: the spin catches every monster around; the others the nearest body part in front.</summary>
        void LandStrike()
        {
            bool any = false;
            foreach (var m in S.Monsters.Where(x => x && x.Alive).ToList())
            {
                if (strikeTarget && m != strikeTarget && !strike.AllAround) continue;
                // The softest part within reach (in front for a slash, all around for the spin).
                float reachNow = (strike.AllAround ? strike.Radius : strike.Reach) * Size;
                var box = BestBox(m, transform.position, reachNow, strike.AllAround, out _);
                if (!box) continue;
                var part = m.Parts.Resolve(box.Parts);
                LastPartStruck = part != null ? part.name ?? part.id : box.name;
                var nearest = m.Hurtboxes.Where(b => b).OrderBy(b => Vector3.Distance(b.transform.position, transform.position + (transform.forward * 1.2f + Vector3.up) * Size)).First();
                WorthChosen += PartWorth(m, box); WorthNearest += PartWorth(m, nearest);
                if (m.HuntRules) m.PendingStun = strike.Stun;
                float dmg = strike.Damage;
                m.ReceiveHit(box, box.transform.position, (part, broken) => new HitInfo { Damage = dmg, Poise = dmg * .6f });
                Bites++; any = true;
                LastEvent = (strike.Clip == "Spin" ? "spin hit " : strike.Clip == "Pounce" ? "pounced on " : strike.Clip == "Slash_R" ? "slashed " : "bit ") + m.DisplayName;
            }
            if (!any)
            {
                LastEvent = LastEvent + " (missed)";
                var t = strikeTarget;
                var nb = t ? t.Hurtboxes.Where(b => b).OrderBy(b => Vector3.Distance(Flat(b.transform.position), Flat(transform.position))).FirstOrDefault() : null;
                StrikeLog.Add(strike.Clip + " missed" + (nb ? " (nearest part " + Vector3.Distance(Flat(nb.transform.position), Flat(transform.position)).ToString("0.0") + " m, "
                    + Vector3.Angle(transform.forward, Flat(nb.transform.position - (transform.position - transform.forward * 1.2f * Size))).ToString("0") + " deg, reach " + (t.Def.bodyRadius + strike.Reach * Size).ToString("0.0") + ")" : " (no target)"));
            }
            else StrikeLog.Add(strike.Clip + " hit " + LastPartStruck);
            if (StrikeLog.Count > 60) StrikeLog.RemoveAt(0);
            WorldKit.Sound(transform.position, any ? "hit" : "swish", any ? .25f : .14f);
        }

        /// <summary>A monster is winding up (or swinging) and the hound stands inside its reach.</summary>
        bool Danger(MonsterActor m)
        {
            if (!m || !m.Alive || m.Passive) return false;
            if (m.Brain.Mode != MonsterMode.Telegraph && m.Brain.Mode != MonsterMode.Attack) return false;
            var atk = m.Brain.CurrentAttack;
            if (atk == null) return false;
            float d = Vector3.Distance(Flat(m.transform.position), Flat(transform.position));
            switch (atk.Shape)
            {
                case AoeShape.None: return d < m.Def.bodyRadius + atk.range + atk.radius + HoundTactics.DangerMargin * Size;
                case AoeShape.Burst: case AoeShape.Leap:
                    // Aimed at the hunter: dangerous when the hound stands near them.
                    return Vector3.Distance(Flat(S.Hunter.transform.position), Flat(transform.position)) < atk.aoeRadius + atk.aoeSpread + HoundTactics.DangerMargin * Size;
                default: return d < HuntBoss.ThreatRadius(atk) + HoundTactics.DangerMargin * Size;
            }
        }

        void Hop(Vector3 dir, float distance, float time, bool keepFacing)
        {
            Trace("hop");
            Anim.Play("Hop", false, Mathf.Max(.3f, Anim.Length("Hop") / time), true, .04f);
            Travel(dir, distance, .05f, time * .85f, keepFacing);
            actionEnd = Time.time + time; strikeAt = -1; comboStep = -1;
        }

        // ---------------- ridden: lope, dash, drift, attacks, sidestep (Monster Hunter Rise's Palamute) ----------------
        void RideTick(float dt)
        {
            var h = S.Hunter;
            if (Time.frameCount > mountedFrame + 1 && (InputRouter.Down(GameAction.Interact) || InputRouter.Down(GameAction.Sheathe))) { Dismount(); return; }
            if (InputRouter.Down(GameAction.Whistle) && !Acting) { Anim.Play("Howl", false, 1.4f, true); LastEvent = "howl"; }
            var mv = InputRouter.Move();
            var cam = S.CameraRig.Camera.transform;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized, right = Vector3.Cross(Vector3.up, fwd);
            Vector3 dir = Vector3.ClampMagnitude(fwd * mv.y + right * mv.x, 1);
            float stick = dir.magnitude, now = Time.time;
            // Y: the hound attacks (bite, slash, spin; press again during a strike to chain). A: a quick sidestep.
            if (InputRouter.Down(GameAction.Attack))
            {
                if (!Acting) { RideAttacks++; StartStrike(HoundTactics.Combo[0], 0, RideTarget()); }
                else if (comboStep >= 0) rideQueued = true;
            }
            if (InputRouter.Down(GameAction.Dodge) && now >= rideHopReadyAt && !(Acting && comboStep >= 0 && strikeAt > 0))
            {
                var side = stick > .1f ? dir : transform.right;
                Hop(side, HoundTactics.RideHopDistance * Size, HoundTactics.RideHopTime, true);
                h.InvulnerableFor(HoundTactics.RideHopInvulnerable);
                rideHopReadyAt = now + HoundTactics.RideHopCooldown; RideHops++;
                LastEvent = "ridden sidestep";
            }
            // R3: lock on from the saddle. A wind-up aimed at the hound: it sidesteps on its own (unless the rider is already steering away).
            if (InputRouter.Down(GameAction.LockOn)) h.ToggleLock();
            if (h.LockTarget && (!h.LockTarget.Alive || Vector3.Distance(h.LockTarget.transform.position, transform.position) > 30)) h.LockTarget = null;
            var threat = S.Monsters.Where(m => m && m.Alive && Danger(m)).OrderBy(m => Vector3.Distance(m.transform.position, transform.position)).FirstOrDefault();
            if (threat)
            {
                var away = Flat(transform.position - threat.transform.position).normalized;
                bool steeringAway = stick > .5f && Vector3.Dot(dir.normalized, away) > .5f;
                if (HoundTactics.AutoEvadeRidden(true, now >= rideHopReadyAt, Acting, steeringAway))
                {
                    var side = Vector3.Cross(Vector3.up, away); if (Vector3.Dot(side, transform.right) < 0) side = -side;
                    Hop(away + side * .6f, HoundTactics.RideHopDistance * Size, HoundTactics.RideHopTime, true);
                    h.InvulnerableFor(HoundTactics.RideHopInvulnerable + .1f);
                    rideHopReadyAt = now + HoundTactics.RideHopCooldown; AutoEvades++;
                    LastEvent = "sidestepped " + threat.DisplayName + "'s wind-up";
                }
            }
            UpdateAction(dt);
            if (Acting)
            {
                if (comboStep >= 0 && now >= actionEnd - .05f && rideQueued) ChainRideStrike();
                Bank(0, dt);
                return;
            }
            if (comboStep >= 0) { if (rideQueued) { ChainRideStrike(); return; } comboStep = -1; }
            Dashing = InputRouter.Held(GameAction.Run) && stick > .1f;
            Drifting = Dashing && InputRouter.Held(GameAction.Guard) && stick > .3f;
            // Locked on with the stick let go: the hound runs the rider to the target and stops within striking range.
            AutoRunning = false;
            if (stick <= .05f && h.LockTarget)
            {
                var m = h.LockTarget;
                var toT = Flat(m.transform.position - transform.position);
                float gapT = toT.magnitude - m.Def.bodyRadius;
                if (gapT > HoundTactics.AutoRunStandOff * Size)
                {
                    AutoRunning = true;
                    var heading = Steered(toT);
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(heading), 1 - Mathf.Exp(-dt * HoundTactics.TurnLope));
                    float speed = gapT > 12 ? GallopSpeed : GallopSpeed * HoundTactics.LopeFraction;
                    speed = Mathf.Min(speed, (gapT - HoundTactics.AutoRunStandOff * Size) / Mathf.Max(dt, 1e-3f));
                    Vector3 to2 = S.ConstrainMove(transform.position, transform.position + transform.forward * speed * dt);
                    cc.Move(to2 - transform.position);
                    PlayGait(speed > WalkSpeed * 1.05f, speed);
                    LastEvent = "running to " + m.DisplayName;
                    Bank(0, dt);
                    return;
                }
                if (toT.sqrMagnitude > .01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toT), 1 - Mathf.Exp(-dt * 6));
            }
            if (stick > .05f)
            {
                float speed = HoundTactics.RideSpeed(stick, Dashing, Drifting, WalkSpeed, GallopSpeed);
                float yawBefore = transform.eulerAngles.y;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 1 - Mathf.Exp(-dt * HoundTactics.TurnRate(stick, Dashing, Drifting)));
                float yawRate = Mathf.DeltaAngle(yawBefore, transform.eulerAngles.y) / Mathf.Max(1e-4f, dt);
                // Travel along the hound's facing: it curves into turns instead of sliding sideways.
                Vector3 to = S.ConstrainMove(transform.position, transform.position + transform.forward * speed * dt);
                if (Blocked(transform.forward) && !Drifting) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(Steered(transform.forward)), 1 - Mathf.Exp(-dt * 4));   // ease round a pillar
                cc.Move(to - transform.position);
                PlayGait(speed > WalkSpeed * 1.05f, speed);
                // A drift leans into the turn and kicks up dirt; a dashing turn leans a little.
                Bank(Drifting ? Mathf.Clamp(-yawRate * .12f, -22, 22) : Dashing ? Mathf.Clamp(-yawRate * .04f, -8, 8) : 0, dt);
                if (Drifting && (dustClock -= dt) <= 0) { dustClock = .08f; WorldKit.Burst(transform.position - transform.forward * .8f * Size + Vector3.up * .1f, new Color(.6f, .52f, .4f), 4); }
                if (Drifting) LastEvent = "drift";
            }
            else { Bank(0, dt); if (!Busy("Howl")) Anim.Play("Idle"); }
        }

        void ChainRideStrike()
        {
            rideQueued = false;
            int next = HoundTactics.NextStrike(comboStep);
            if (next < 0) { comboStep = -1; return; }
            RideAttacks++;
            StartStrike(HoundTactics.Combo[next], next, RideTarget());
        }

        /// <summary>The monster in front of the ridden hound within reach (or none: the strike swings at the air).</summary>
        MonsterActor RideTarget() => S.NearestMonster(transform.position, 4.5f * Size, transform.forward, 70);

        void Bank(float degrees, float dt)
        {
            if (!Visual) return;
            if (!visualBaseSet) { visualBase = Visual.localRotation; visualBaseSet = true; }
            bank = Mathf.Lerp(bank, degrees, 1 - Mathf.Exp(-dt * 8));
            Visual.localRotation = Quaternion.AngleAxis(bank, Vector3.forward) * visualBase;
        }

        // ---------------- companion ----------------
        void AiTick(float dt)
        {
            var h = S.Hunter.transform;
            Vector3 toHunter = h.position - transform.position; toHunter.y = 0;
            float dist = toHunter.magnitude;
            if (dist > 45) { Warps++; Warp(S.SafeGround(h.position - h.forward * 4 + h.right * 2), h.eulerAngles.y); return; }
            Bank(0, dt);

            // Assist: fight the monster that matters (see PickTarget).
            var target = PickTarget();
            Target = target;
            if (State == HoundState.Called && (dist < 3.5f || Time.time - calledAt > 8)) State = HoundState.Follow;
            var was = State;
            if (target && State != HoundState.Called) State = HoundState.Assist;
            else if (State == HoundState.Assist) State = HoundState.Follow;
            if (State != was) Trace("state " + State + (target ? " (" + target.DisplayName + ")" : ""));

            UpdateAction(dt);
            // A wind-up aimed at it cancels whatever it is doing (except the instant a strike lands): hop clear first.
            if (Acting && InHuntersLane() && !Anim.Current.EndsWith("Hop")) { actionEnd = 0; strikeAt = -1; comboStep = -1; travelUntil = 0; }
            if (Acting && comboStep >= -1 && target && State == HoundState.Assist && Danger(target) && Time.time >= evadeReadyAt && !(strikeAt > 0 && strikeAt - Time.time < .06f) && !Anim.Current.EndsWith("Hop"))
            {
                actionEnd = 0; strikeAt = -1; comboStep = -1; travelUntil = 0;
                comboReadyAt = Mathf.Max(comboReadyAt, Time.time + .8f);
            }
            if (Acting)
            {
                // The combo carries on while the monster stays in reach and is not winding up at the hound.
                if (comboStep >= 0 && Time.time >= actionEnd - .04f && State == HoundState.Assist) ContinueCombo(target);
                return;
            }
            if (comboStep >= 0) { comboStep = -1; comboReadyAt = Time.time + HoundTactics.CooldownAfterCombo(target ? target.Brain.Mode : MonsterMode.Idle, S.Hunter.Hunt && S.Hunter.Hunt.Downed); }
            if (Busy("Howl")) return;

            if (State == HoundState.Assist) { AssistTick(target, dt); return; }
            Action = HoundAction.Follow;
            if (ClearOfHuntersLane(dt)) return;
            // Heel behind and to the right; beside the hunter when they sprint (not when an attack carries them), matching their pace.
            bool hunterRunning = S.Hunter.Hunt && S.Hunter.Hunt.Sprinting && !S.Hunter.Attack.Running && !S.Hunter.Dodging;
            Vector3 goal = hunterRunning ? h.position + (h.right * 2.2f - h.forward * .4f) * Size : h.position - (h.forward * 2.6f - h.right * 1.8f) * Size;
            MoveToward(goal, hunterRunning ? .9f : 1.2f, State == HoundState.Called || hunterRunning, h.forward, dt, hunterRunning ? S.Hunter.Velocity.magnitude * 1.15f : 0);
        }

        void AssistTick(MonsterActor target, float dt)
        {
            float now = Time.time, body = target.Def.bodyRadius, reach = 1.9f * Size;
            Vector3 toM = Flat(target.transform.position - transform.position);
            float gap = toM.magnitude - body;
            var h = S.Hunter.transform.position;
            // An opening (recovering, staggered, toppled) or the hunter down: commit now, not after the usual pause.
            // (A flinch the hound caused itself is not an opening: otherwise a passive target that flinches at every bite keeps it attacking forever.)
            bool open = (HoundTactics.IsOpening(target.Brain.Mode) && !(target.Brain.Mode == MonsterMode.Stagger && now - lastStrikeAt < 1.5f)) || (S.Hunter.Hunt && S.Hunter.Hunt.Downed);
            if (open && comboReadyAt > now + .25f) comboReadyAt = now + .25f;
            // Flank across from the hunter, on whichever side puts the softest part nearer.
            var soft = BestBox(target, target.transform.position, body + 4, true, out _);
            Vector3 flank = default; float flankScore = float.MaxValue;
            foreach (float spread in new[] { 35f, -35f })
            {
                var (fx, fz) = HoundTactics.FlankPoint(target.transform.position.x, target.transform.position.z, h.x, h.z, body + 1.3f * Size, spread);
                var p = new Vector3(fx, transform.position.y, fz);
                float sc = soft ? Vector3.Distance(Flat(p), Flat(soft.transform.position)) : Vector3.Distance(Flat(p), Flat(transform.position));
                if (sc < flankScore) { flankScore = sc; flank = p; }
            }
            bool atFlank = Vector3.Distance(Flat(flank), Flat(transform.position)) < .9f * Size;   // close enough to really be across
            bool danger = Danger(target);
            PincerAngle = HoundTactics.PincerAngle(target.transform.position.x, target.transform.position.z, h.x, h.z, transform.position.x, transform.position.z);
            Action = HoundTactics.Choose(gap, open ? reach + 1.2f * Size : reach, danger, now >= evadeReadyAt, now >= comboReadyAt, now >= pounceReadyAt, atFlank);
            string why = Action + " gap " + gap.ToString("0.0") + (now >= comboReadyAt ? " ready" : " wait " + (comboReadyAt - now).ToString("0.0")) + (open ? " OPEN" : "") + (atFlank ? " flank" : "") + " " + target.Brain.Mode;
            if (DecisionLog.Count == 0 || !DecisionLog[DecisionLog.Count - 1].EndsWith(why)) { DecisionLog.Add(now.ToString("0.00") + " " + why); if (DecisionLog.Count > 40) DecisionLog.RemoveAt(0); }
            if (Action != HoundAction.Evade && ClearOfHuntersLane(dt)) return;
            switch (Action)
            {
                case HoundAction.Evade:
                {
                    // Hop clear of the swing: away from the monster and a little to the side.
                    var away = toM.sqrMagnitude > .01f ? -toM.normalized : -transform.forward;
                    var side = Vector3.Cross(Vector3.up, away) * (Random.value < .5f ? .5f : -.5f);
                    Hop(away + side, HoundTactics.EvadeDistance * Size, HoundTactics.EvadeTime, true);
                    evadeReadyAt = now + HoundTactics.EvadeCooldown; Evades++;
                    LastEvent = "hopped clear of " + target.DisplayName;
                    return;
                }
                case HoundAction.Attack:
                    if (open) { Punishes++; LastEvent = "punishing the opening"; }
                    StartStrike(HoundTactics.Combo[0], 0, target);
                    return;
                case HoundAction.Pounce:
                {
                    StartStrike(HoundTactics.PounceStrike, -1, target);
                    // Leap to striking distance (the strike lands as the forepaws come down, at 0.55 of the clip).
                    Travel(toM, Mathf.Max(0, gap - reach * .7f), HoundTactics.PounceStrike.Length * .28f, HoundTactics.PounceStrike.Length * .58f, true);
                    pounceReadyAt = now + HoundTactics.PounceCooldown; comboReadyAt = now + HoundTactics.PounceStrike.Length + .1f;
                    Pounces++;
                    return;
                }
                case HoundAction.Reposition when danger:
                {
                    // No hop ready: trot out of reach.
                    var atk = target.Brain.CurrentAttack;
                    var away = toM.sqrMagnitude > .01f ? -toM.normalized : -transform.forward;
                    MoveToward(target.transform.position + away * (body + (atk != null ? atk.range + atk.radius : 2) + HoundTactics.DangerMargin * Size + .5f), .3f, true, toM, dt, 0);
                    return;
                }
                default:
                {
                    // Approach or circle to the flank (across the monster from the hunter). When the straight way there
                    // runs through the monster's body, go round it: a point on a circle about it, 60 degrees on toward the flank.
                    Vector3 goal = flank;
                    var mc = Flat(target.transform.position); var me = Flat(transform.position); var fl = Flat(flank);
                    float ring = body + 1.5f * Size;
                    var seg = fl - me; float tSeg = seg.sqrMagnitude > .01f ? Mathf.Clamp01(Vector3.Dot(mc - me, seg) / seg.sqrMagnitude) : 0;
                    if (Vector3.Distance(me + seg * tSeg, mc) < body + 1.0f * Size && Vector3.Distance(me, fl) > 1.5f * Size)
                    {
                        float aMe = Mathf.Atan2(me.x - mc.x, me.z - mc.z) * Mathf.Rad2Deg, aFl = Mathf.Atan2(fl.x - mc.x, fl.z - mc.z) * Mathf.Rad2Deg;
                        float step = Mathf.Clamp(Mathf.DeltaAngle(aMe, aFl), -60, 60);
                        float a = (aMe + step) * Mathf.Deg2Rad;
                        goal = new Vector3(mc.x + Mathf.Sin(a) * ring, transform.position.y, mc.z + Mathf.Cos(a) * ring);
                        if (LastEvent != "circling to the flank") Circles++;
                        LastEvent = "circling to the flank";
                    }
                    MoveToward(goal, .5f, gap > 7, toM, dt, 0);
                    return;
                }
            }
        }

        public float PincerAngle { get; private set; }
        /// <summary>The companion's recent decisions (time, action, gap, readiness, opening, monster mode) for diagnosis.</summary>
        public readonly List<string> DecisionLog = new List<string>();
        void Trace(string what) { DecisionLog.Add(Time.time.ToString("0.00") + " " + what); if (DecisionLog.Count > 60) DecisionLog.RemoveAt(0); }
        public int LaneClears { get; private set; }
        public int Circles { get; private set; }
        bool inLane; float laneAt;

        /// <summary>The hunter is attacking (a charge, a thrust, a shot) and the hound stands in front of them, in the
        /// way: it steps aside, to whichever side it is already on. True while it does.</summary>
        /// <summary>The hound stands in the path of the hunter's attack (a charge's whole lane, a swing's reach).</summary>
        bool InHuntersLane() => LanePosition(out _, out _);

        bool LanePosition(out float across, out Vector3 hf)
        {
            var hunter = S.Hunter;
            hf = Flat(hunter.transform.forward).normalized; across = 0;
            if (!hunter.Attack.Running) return false;
            var rel = Flat(transform.position - hunter.transform.position);
            float along = Vector3.Dot(rel, hf); across = Vector3.Dot(rel, Vector3.Cross(Vector3.up, hf));
            float lane = 1.4f * Size, length = (hunter.Attack.Def is HuntMoveDef hm && hm.travel > 0 ? 9 : 4) * Size;
            return along >= 0 && along <= length && Mathf.Abs(across) <= lane;
        }

        bool ClearOfHuntersLane(float dt)
        {
            if (inLane && Time.time - laneAt > .3f) inLane = false;
            if (!LanePosition(out float across, out var hf)) return false;
            float lane = 1.4f * Size;
            var side = Vector3.Cross(Vector3.up, hf) * (across >= 0 ? 1 : -1);
            MoveToward(transform.position + side * (lane + 1.2f * Size - Mathf.Abs(across) + .6f), .2f, true, -side, dt, GallopSpeed);
            if (!inLane) { LaneClears++; Trace("lane clear"); }
            inLane = true; laneAt = Time.time;
            LastEvent = "out of the hunter's way";
            return true;
        }
        // ---------------- judgement ----------------
        public MonsterActor Target { get; private set; }
        public string TargetReason { get; private set; } = "";
        public int Punishes { get; private set; }
        public int Detours { get; private set; }
        public int Warps { get; private set; }
        public int AutoEvades { get; private set; }
        public bool AutoRunning { get; private set; }
        public string LastPartStruck { get; private set; } = "";
        /// <summary>Summed worth (PartScore) of the parts struck, and of the nearest part each time (what a plain bite would hit).</summary>
        public float WorthChosen { get; private set; }
        public float WorthNearest { get; private set; }
        MonsterActor huntersMark; float huntersMarkAt = -99; int sideBias = 1; float sideBiasUntil;

        /// <summary>The monster to fight: the hunter's lock; one about to hit the hunter (urgent when the hunter is down);
        /// the one the hunter has been hitting; any engaged one close to the hunter.</summary>
        MonsterActor PickTarget()
        {
            var hunter = S.Hunter; float now = Time.time;
            bool down = hunter.Hunt && hunter.Hunt.Downed;
            MonsterActor best = null; float bestScore = float.MinValue; string reason = "";
            foreach (var m in S.Monsters)
            {
                if (!m || !m.Alive) continue;
                float dh = Vector3.Distance(Flat(m.transform.position), Flat(hunter.transform.position));
                bool locked = hunter.LockTarget == m;
                var atk = m.Brain.CurrentAttack;
                bool threat = !m.Passive && atk != null && (m.Brain.Mode == MonsterMode.Telegraph || m.Brain.Mode == MonsterMode.Attack) && dh < m.Def.bodyRadius + atk.range + atk.radius + 1.5f;
                bool marked = huntersMark == m && now - huntersMarkAt < 6 && dh < 14;   // only while the hunter is still near it
                if (!locked && !threat && !marked && !(!m.Passive && m.Brain.Engaged && dh < 14)) continue;
                float score = HoundTactics.TargetScore(locked, threat, marked, dh, down);
                if (score > bestScore) { bestScore = score; best = m; reason = locked ? "your lock" : threat ? (down ? "protecting you (down)" : "about to hit you") : marked ? "you are hitting it" : "engaged nearby"; }
            }
            if (best != Target) LastEvent = best ? "now fighting " + best.DisplayName + " (" + reason + ")" : LastEvent;
            TargetReason = reason;
            return best;
        }

        /// <summary>The body part worth cutting within reach of <paramref name="from"/> (and in front, unless all around).</summary>
        Hurtbox BestBox(MonsterActor m, Vector3 from, float reach, bool allAround, out float score)
        {
            Hurtbox best = null; score = float.MinValue;
            foreach (var b in m.Hurtboxes)
            {
                if (!b) continue;
                var to = Flat(b.transform.position - from);
                if (to.magnitude > m.Def.bodyRadius + reach) continue;
                // "In front" is judged from behind the hound's shoulders: a big hound pressed against the monster still has it ahead.
                var ahead = Flat(b.transform.position - (transform.position - transform.forward * 1.2f * Size));
                if (!allAround && ahead.sqrMagnitude > .01f && Vector3.Angle(transform.forward, ahead) > 70) continue;
                float sc = PartWorth(m, b) - to.magnitude * .02f;
                if (sc > score) { score = sc; best = b; }
            }
            return best;
        }

        static float PartWorth(MonsterActor m, Hurtbox b)
        {
            var part = m.Parts.Resolve(b.Parts);
            if (part == null) return 1;
            bool broken = m.Parts.Broken.Contains(part.id);
            float mult = broken && part.brokenDamageMultiplier > 0 ? part.brokenDamageMultiplier : part.damageMultiplier;
            return HoundTactics.PartScore(mult, part.weakPoint, part.breakHealth > 0 && !broken);
        }

        /// <summary>Is the way ahead blocked by something solid (a wall, a pillar)? Actors are not obstacles.</summary>
        bool Blocked(Vector3 dir)
        {
            var o = transform.position + Vector3.up * .8f * Size;
            return Physics.SphereCast(o, .38f * Size, dir, out _, 1.7f * Size, Layers.WorldMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Heading toward <paramref name="want"/>, turned round obstacles (keeps to one side for a moment).</summary>
        Vector3 Steered(Vector3 want)
        {
            want.y = 0; if (want.sqrMagnitude < 1e-4f) return want;
            want.Normalize();
            if (Time.time > sideBiasUntil) sideBias = 1;
            float? a = HoundTactics.Steer(deg => Blocked(Quaternion.Euler(0, deg, 0) * want), sideBias);
            if (a == null || a.Value == 0) return want;
            if (Time.time > sideBiasUntil) Detours++;
            sideBias = a.Value > 0 ? 1 : -1; sideBiasUntil = Time.time + .8f;
            return Quaternion.Euler(0, a.Value, 0) * want;
        }

        /// <summary>After each strike: the next one while the monster is in reach and not winding up.</summary>
        void ContinueCombo(MonsterActor target)
        {
            int next = HoundTactics.NextStrike(comboStep);
            if (next < 0 || !target || !target.Alive || Danger(target)) { comboStep = -1; comboReadyAt = Time.time + HoundTactics.CooldownAfterCombo(target ? target.Brain.Mode : MonsterMode.Idle, S.Hunter.Hunt && S.Hunter.Hunt.Downed); return; }
            float gap = Vector3.Distance(Flat(target.transform.position), Flat(transform.position)) - target.Def.bodyRadius;
            if (gap > 1.9f * Size + .8f) { comboStep = -1; comboReadyAt = Time.time + HoundTactics.ComboCooldown * .5f; return; }
            StartStrike(HoundTactics.Combo[next], next, target);
        }

        /// <summary>Walks or gallops to <paramref name="goal"/> (stops within <paramref name="stopAt"/>), then faces
        /// <paramref name="face"/>. <paramref name="pace"/> &gt; 0 sets the speed (matching a running hunter).</summary>
        void MoveToward(Vector3 goal, float stopAt, bool gallop, Vector3 face, float dt, float pace)
        {
            Vector3 to = goal - transform.position; to.y = 0;
            // Dead zone: stop inside stopAt, start again only past stopAt + 0.6 m (no idle/walk flicker at heel).
            moving = moving ? to.magnitude > stopAt : to.magnitude > stopAt + .6f;
            if (moving)
            {
                gallop = gallop || to.magnitude > 7;
                float speed = pace > 0 ? Mathf.Clamp(pace, WalkSpeed, GallopSpeed) : gallop ? GallopSpeed : WalkSpeed;
                var heading = Steered(to);    // round pillars and walls instead of into them
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(heading), 1 - Mathf.Exp(-dt * 7));
                Vector3 step = transform.forward * Mathf.Min(speed * dt, to.magnitude);
                Vector3 before = transform.position;
                cc.Move(step);
                float moved = Vector3.Distance(Flat(before), Flat(transform.position));
                stuckFor = moved < speed * dt * .2f ? stuckFor + dt : 0;
                if (stuckFor > 2.5f) { Warps++; Warp(S.SafeGround(goal), transform.eulerAngles.y); stuckFor = 0; }   // last resort
                PlayGait(speed > WalkSpeed * 1.3f, speed);
                idleFor = 0;
            }
            else
            {
                face.y = 0;
                if (face.sqrMagnitude > .01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(face), 1 - Mathf.Exp(-dt * 5));
                idleFor += dt;
                Anim.Play(idleFor > 6 && State == HoundState.Follow ? "Sit" : "Idle");
            }
        }

        bool Busy(string clip) => Anim.Current.EndsWith(clip) && !Anim.Finished(clip);

        void PlayGait(bool gallop, float speed)
        {
            // Playback rate keeps the paws planted: authored stride at the speed actually travelled.
            float natural = gallop ? GallopSpeed : WalkSpeed;
            Anim.Play(gallop ? "Gallop" : "Walk", true, Mathf.Clamp(speed / Mathf.Max(.1f, natural), .4f, 2f));
        }

        public int BlowsTaken { get; private set; }

        /// <summary>A boss's area attack caught the hound (a hop's leap clears it): knocked back a step, then back to work.</summary>
        public void TakeBlow(MonsterAttackDef atk, Vector3 from)
        {
            if (Anim.Current.EndsWith("Hop") && Acting) { LastEvent = "hopped over " + atk.id; return; }
            BlowsTaken++;
            Anim.Play("Hit", false, 1.2f, true, .05f);
            var away = Flat(transform.position - from); if (away.sqrMagnitude < .01f) away = -transform.forward;
            Travel(away, 1.4f * Size, 0, .3f, true);
            actionEnd = Time.time + .55f; strikeAt = -1; comboStep = -1;
            LastEvent = "hit by " + atk.id;
        }

        /// <summary>A roar: the hound flattens its ears and cowers for a moment.</summary>
        public void Cower(float seconds)
        {
            if (Ridden) return;
            Anim.Play("Hit", false, .7f, true, .05f);
            actionEnd = Time.time + seconds * .8f; strikeAt = -1; comboStep = -1; travelUntil = 0;
            LastEvent = "cowering (roar)";
        }

        /// <summary>A monster blow aimed at the hunter while riding: the hound shies and throws the rider.</summary>
        public void RiderHit() { Anim.Play("Hit", false, 1.2f, true, .05f); Dismount(true); }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        void OnHunterHit(MonsterActor m, HitInfo hit) { huntersMark = m; huntersMarkAt = Time.time; }

        void OnDestroy() { if (S && S.Hunter) S.Hunter.BladeHit -= OnHunterHit; if (Ridden && S && S.Hunter) S.Hunter.EndRide(S.SafeGround(transform.position + transform.right * 1.3f * Size), transform.eulerAngles.y); }
    }
}
