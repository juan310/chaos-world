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
        float actionEnd, strikeAt = -1, travelFrom, travelUntil, travelSpeed, comboReadyAt, pounceReadyAt, evadeReadyAt, rideHopReadyAt, bank, dustClock;
        Vector3 travelDir; bool travelKeepsFacing, rideQueued;
        int comboStep = -1;
        HoundStrike strike;
        MonsterActor strikeTarget;
        Quaternion visualBase = Quaternion.identity; bool visualBaseSet;

        void StartStrike(HoundStrike st, int step, MonsterActor target)
        {
            strike = st; comboStep = step; strikeTarget = target;
            if (target)
            {
                var face = target.transform.position - transform.position; face.y = 0;
                if (face.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(face);
            }
            Anim.Play(st.Clip, false, Mathf.Max(.3f, Anim.Length(st.Clip) / st.Length), true, .05f);
            strikeAt = Time.time + st.HitAt; actionEnd = Time.time + st.Length;
            Sequence.Add(st.Clip); if (Sequence.Count > 40) Sequence.RemoveAt(0);
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
            if (strikeAt > 0 && now >= strikeAt) { strikeAt = -1; LandStrike(); }
        }

        /// <summary>The strike lands: the spin catches every monster around; the others the nearest body part in front.</summary>
        void LandStrike()
        {
            Vector3 muzzle = transform.position + (transform.forward * 1.2f + Vector3.up) * Size;
            bool any = false;
            foreach (var m in S.Monsters.Where(x => x && x.Alive).ToList())
            {
                if (strikeTarget && m != strikeTarget && !strike.AllAround) continue;
                var box = m.Hurtboxes.OrderBy(b => Vector3.Distance(b.transform.position, strike.AllAround ? transform.position + Vector3.up * Size : muzzle)).FirstOrDefault();
                if (!box) continue;
                float d = Vector3.Distance(Flat(box.transform.position), Flat(transform.position));
                if (d > m.Def.bodyRadius + (strike.AllAround ? strike.Radius : strike.Reach) * Size) continue;
                if (!strike.AllAround)
                {
                    var to = Flat(box.transform.position - transform.position);
                    if (to.sqrMagnitude > .01f && Vector3.Angle(transform.forward, to) > 75) continue;    // a slash hits what is in front
                }
                if (m.HuntRules) m.PendingStun = strike.Stun;
                float dmg = strike.Damage;
                m.ReceiveHit(box, box.transform.position, (part, broken) => new HitInfo { Damage = dmg, Poise = dmg * .6f });
                Bites++; any = true;
                LastEvent = (strike.Clip == "Spin" ? "spin hit " : strike.Clip == "Pounce" ? "pounced on " : strike.Clip == "Slash_R" ? "slashed " : "bit ") + m.DisplayName;
            }
            if (!any) LastEvent = LastEvent + " (missed)";
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
            return d < m.Def.bodyRadius + atk.range + atk.radius + HoundTactics.DangerMargin * Size;
        }

        void Hop(Vector3 dir, float distance, float time, bool keepFacing)
        {
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
            if (stick > .05f)
            {
                float speed = HoundTactics.RideSpeed(stick, Dashing, Drifting, WalkSpeed, GallopSpeed);
                float yawBefore = transform.eulerAngles.y;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 1 - Mathf.Exp(-dt * HoundTactics.TurnRate(stick, Dashing, Drifting)));
                float yawRate = Mathf.DeltaAngle(yawBefore, transform.eulerAngles.y) / Mathf.Max(1e-4f, dt);
                // Travel along the hound's facing: it curves into turns instead of sliding sideways.
                Vector3 to = S.ConstrainMove(transform.position, transform.position + transform.forward * speed * dt);
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
            if (dist > 45) { Warp(S.SafeGround(h.position - h.forward * 4 + h.right * 2), h.eulerAngles.y); return; }
            Bank(0, dt);

            // Assist: fight the monster the hunter is fighting.
            var target = S.Hunter.LockTarget && S.Hunter.LockTarget.Alive ? S.Hunter.LockTarget
                : S.Monsters.Where(m => m && m.Alive && !m.Passive && m.Brain.Engaged && Vector3.Distance(m.transform.position, h.position) < 12)
                            .OrderBy(m => Vector3.Distance(m.transform.position, transform.position)).FirstOrDefault();
            if (State == HoundState.Called && (dist < 3.5f || Time.time - calledAt > 8)) State = HoundState.Follow;
            if (target && State != HoundState.Called) State = HoundState.Assist;
            else if (State == HoundState.Assist) State = HoundState.Follow;

            UpdateAction(dt);
            if (Acting)
            {
                // The combo carries on while the monster stays in reach and is not winding up at the hound.
                if (comboStep >= 0 && Time.time >= actionEnd - .04f && State == HoundState.Assist) ContinueCombo(target);
                return;
            }
            if (comboStep >= 0) { comboStep = -1; comboReadyAt = Time.time + HoundTactics.ComboCooldown; }
            if (Busy("Howl")) return;

            if (State == HoundState.Assist) { AssistTick(target, dt); return; }
            Action = HoundAction.Follow;
            // Heel behind and to the right; beside the hunter when they sprint, matching their pace.
            bool hunterRunning = S.Hunter.Velocity.magnitude > S.Hunter.WalkSpeed * 1.2f;
            Vector3 goal = hunterRunning ? h.position + (h.right * 2.2f - h.forward * .4f) * Size : h.position - (h.forward * 2.6f - h.right * 1.8f) * Size;
            MoveToward(goal, hunterRunning ? .9f : 1.2f, State == HoundState.Called || hunterRunning, h.forward, dt, hunterRunning ? S.Hunter.Velocity.magnitude * 1.15f : 0);
        }

        void AssistTick(MonsterActor target, float dt)
        {
            float now = Time.time, body = target.Def.bodyRadius, reach = 1.9f * Size;
            Vector3 toM = Flat(target.transform.position - transform.position);
            float gap = toM.magnitude - body;
            var h = S.Hunter.transform.position;
            var (fx, fz) = HoundTactics.FlankPoint(target.transform.position.x, target.transform.position.z, h.x, h.z, body + 1.3f * Size);
            var flank = new Vector3(fx, transform.position.y, fz);
            bool atFlank = Vector3.Distance(Flat(flank), Flat(transform.position)) < 1.4f * Size;
            bool danger = Danger(target);
            PincerAngle = HoundTactics.PincerAngle(target.transform.position.x, target.transform.position.z, h.x, h.z, transform.position.x, transform.position.z);
            Action = HoundTactics.Choose(gap, reach, danger, now >= evadeReadyAt, now >= comboReadyAt, now >= pounceReadyAt, atFlank);
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
                    StartStrike(HoundTactics.Combo[0], 0, target);
                    return;
                case HoundAction.Pounce:
                {
                    StartStrike(HoundTactics.PounceStrike, -1, target);
                    Travel(toM, Mathf.Max(0, gap - reach * .5f), HoundTactics.PounceStrike.Length * .3f, HoundTactics.PounceStrike.Length * .7f, true);
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
                    // Approach or circle to the flank (across the monster from the hunter).
                    MoveToward(flank, .5f, gap > 7, toM, dt, 0);
                    return;
            }
        }

        public float PincerAngle { get; private set; }

        /// <summary>After each strike: the next one while the monster is in reach and not winding up.</summary>
        void ContinueCombo(MonsterActor target)
        {
            int next = HoundTactics.NextStrike(comboStep);
            if (next < 0 || !target || !target.Alive || Danger(target)) { comboStep = -1; comboReadyAt = Time.time + HoundTactics.ComboCooldown; return; }
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
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 1 - Mathf.Exp(-dt * 7));
                Vector3 step = transform.forward * Mathf.Min(speed * dt, to.magnitude);
                Vector3 before = transform.position;
                cc.Move(step);
                float moved = Vector3.Distance(Flat(before), Flat(transform.position));
                stuckFor = moved < speed * dt * .2f ? stuckFor + dt : 0;
                if (stuckFor > 1.5f) { Warp(S.SafeGround(goal), transform.eulerAngles.y); stuckFor = 0; }
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

        /// <summary>A monster blow aimed at the hunter while riding: the hound shies and throws the rider.</summary>
        public void RiderHit() { Anim.Play("Hit", false, 1.2f, true, .05f); Dismount(true); }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        void OnDestroy() { if (Ridden && S && S.Hunter) S.Hunter.EndRide(S.SafeGround(transform.position + transform.right * 1.3f * Size), transform.eulerAngles.y); }
    }
}
