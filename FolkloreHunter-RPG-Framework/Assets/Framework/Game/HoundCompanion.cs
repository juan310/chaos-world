using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    public enum HoundState { Follow, Assist, Called, Ridden, Sitting }

    /// <summary>The Briarhound: a great shaggy hunting hound that follows the hunter, slashes the monster being hunted
    /// with the crescent blade it carries in its jaws, comes when whistled, and can be ridden at a gallop to cross ground
    /// quickly (the Palamute's role). Gait speeds come from the clip's paw stride so the feet do not slide.</summary>
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
        float vertical, biteReadyAt, biteAt = -1, idleFor, stuckFor;
        MonsterActor biteTarget;
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
            State = HoundState.Called; LastEvent = "called";
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

        void RideTick(float dt)
        {
            var h = S.Hunter;
            if (Time.frameCount > mountedFrame + 1 && (InputRouter.Down(GameAction.Interact) || InputRouter.Down(GameAction.Sheathe))) { Dismount(); return; }
            if (InputRouter.Down(GameAction.Whistle)) { Anim.Play("Howl", false, 1.4f, true); LastEvent = "howl"; }
            var mv = InputRouter.Move();
            var cam = S.CameraRig.Camera.transform;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized, right = Vector3.Cross(Vector3.up, fwd);
            Vector3 dir = Vector3.ClampMagnitude(fwd * mv.y + right * mv.x, 1);
            bool gallop = InputRouter.Held(GameAction.Run);
            if (dir.sqrMagnitude > .01f)
            {
                float speed = (gallop ? GallopSpeed : WalkSpeed) * dir.magnitude;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 1 - Mathf.Exp(-dt * (gallop ? 5 : 8)));
                // Travel along the hound's facing: it curves into turns instead of sliding sideways.
                Vector3 step = transform.forward * speed * dt;
                Vector3 to = S.ConstrainMove(transform.position, transform.position + step);
                cc.Move(to - transform.position);
                PlayGait(gallop, speed);
            }
            else if (!Anim.Current.EndsWith("Howl") || !Anim.Anim.isPlaying) Anim.Play("Idle");
        }

        void AiTick(float dt)
        {
            var h = S.Hunter.transform;
            Vector3 toHunter = h.position - transform.position; toHunter.y = 0;
            float dist = toHunter.magnitude;
            if (dist > 45) { Warp(S.SafeGround(h.position - h.forward * 4 + h.right * 2), h.eulerAngles.y); return; }

            // Assist: bite the monster the hunter is fighting.
            var target = S.Hunter.LockTarget && S.Hunter.LockTarget.Alive ? S.Hunter.LockTarget
                : S.Monsters.Where(m => m && m.Alive && !m.Passive && m.Brain.Engaged && Vector3.Distance(m.transform.position, h.position) < 12)
                            .OrderBy(m => Vector3.Distance(m.transform.position, transform.position)).FirstOrDefault();
            if (State == HoundState.Called && (dist < 3.5f || Time.time - biteReadyAt > 8)) State = HoundState.Follow;
            if (target && State != HoundState.Called) State = HoundState.Assist;
            else if (State == HoundState.Assist) State = HoundState.Follow;

            if (biteAt > 0 && Time.time >= biteAt) LandBite();
            if (Anim.Current.EndsWith("Bite") && Anim.Anim.isPlaying) return;
            if (Anim.Current.EndsWith("Howl") && Anim.Anim.isPlaying) return;

            Vector3 goal; float stopAt;
            if (State == HoundState.Assist)
            {
                var side = transform.position - target.transform.position; side.y = 0;
                goal = target.transform.position + (side.sqrMagnitude > .01f ? side.normalized : -target.transform.forward) * (target.Def.bodyRadius + 1.3f * Size);
                stopAt = .5f;
                float reach = Vector3.Distance(Flat(transform.position), Flat(target.transform.position));
                if (reach < target.Def.bodyRadius + 1.9f * Size && Time.time >= biteReadyAt) { StartBite(target); return; }
            }
            else
            {
                // Heel: behind and to the right of the hunter.
                goal = h.position - (h.forward * 2.6f - h.right * 1.8f) * Size;
                stopAt = 1.2f;
            }
            Vector3 to = goal - transform.position; to.y = 0;
            // Dead zone: stop inside stopAt, start again only past stopAt + 0.6 m (no idle/walk flicker at heel).
            moving = moving ? to.magnitude > stopAt : to.magnitude > stopAt + .6f;
            if (moving)
            {
                bool gallop = to.magnitude > 7 || State == HoundState.Called;
                float speed = gallop ? GallopSpeed : WalkSpeed;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 1 - Mathf.Exp(-dt * 7));
                Vector3 step = transform.forward * Mathf.Min(speed * dt, to.magnitude);
                Vector3 before = transform.position;
                cc.Move(step);
                float moved = Vector3.Distance(Flat(before), Flat(transform.position));
                stuckFor = moved < speed * dt * .2f ? stuckFor + dt : 0;
                if (stuckFor > 1.5f) { Warp(S.SafeGround(goal), transform.eulerAngles.y); stuckFor = 0; }
                PlayGait(gallop, speed);
                idleFor = 0;
            }
            else
            {
                var face = State == HoundState.Assist ? target.transform.position - transform.position : h.forward;
                face.y = 0;
                if (face.sqrMagnitude > .01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(face), 1 - Mathf.Exp(-dt * 5));
                idleFor += dt;
                Anim.Play(idleFor > 6 && State == HoundState.Follow ? "Sit" : "Idle");
            }
        }

        void PlayGait(bool gallop, float speed)
        {
            // Playback rate keeps the paws planted: authored stride at the speed actually travelled.
            float natural = gallop ? GallopSpeed : WalkSpeed;
            Anim.Play(gallop ? "Gallop" : "Walk", true, Mathf.Clamp(speed / Mathf.Max(.1f, natural), .4f, 2f));
        }

        void StartBite(MonsterActor target)
        {
            biteTarget = target;
            var face = target.transform.position - transform.position; face.y = 0;
            if (face.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(face);
            Anim.Play("Bite", false, 1.1f, true, .05f);
            biteAt = Time.time + .38f;
            biteReadyAt = Time.time + 3.2f;
            LastEvent = "bite";
        }

        void LandBite()
        {
            biteAt = -1;
            var m = biteTarget;
            if (!m || !m.Alive) return;
            var box = m.Hurtboxes.OrderBy(b => Vector3.Distance(b.transform.position, transform.position + (transform.forward * 1.2f + Vector3.up) * Size)).FirstOrDefault();
            if (!box || Vector3.Distance(Flat(box.transform.position), Flat(transform.position)) > m.Def.bodyRadius + 2.4f * Size) { LastEvent = "bite missed"; return; }
            if (m.HuntRules) m.PendingStun = 3;
            m.ReceiveHit(box, box.transform.position, (part, broken) => new HitInfo { Damage = 9, Poise = 6 });
            Bites++;
            LastEvent = "bit " + m.DisplayName;
        }

        /// <summary>A monster blow aimed at the hunter while riding: the hound shies and throws the rider.</summary>
        public void RiderHit() { Anim.Play("Hit", false, 1.2f, true, .05f); Dismount(true); }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        void OnDestroy() { if (Ridden && S && S.Hunter) S.Hunter.EndRide(S.SafeGround(transform.position + transform.right * 1.3f * Size), transform.eulerAngles.y); }
    }
}
