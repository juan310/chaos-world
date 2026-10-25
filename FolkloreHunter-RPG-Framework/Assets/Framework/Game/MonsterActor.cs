using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Unity body of a monster: senses and movement for its MonsterBrain, hurtboxes, hit volume, visuals.</summary>
    public class MonsterActor : MonoBehaviour
    {
        public string MonsterId, SpawnId, AreaId;
        public float PatrolRadius = 3;
        public MonsterDef Def { get; private set; }
        public MonsterBrain Brain { get; private set; }
        public PartTracker Parts { get; private set; }
        public Transform Visual;
        public AnimationDriver Anim { get; private set; }
        public bool Ready { get; private set; }
        public Vector3 Home;
        public Vector3 AttackCenter => transform.position + transform.forward * (Brain.CurrentAttack == null ? 1 : Brain.CurrentAttack.range * .55f) + Vector3.up * .1f;

        CharacterController cc;
        Transform hand, chest, head;
        Renderer eyes;
        Material eyesBase;
        GameObject ring;
        Vector3 patrolTarget;
        float walkNatural = 1;
        float vertical, deathTime = -1, hurtFlash;
        readonly List<Hurtbox> hurtboxes = new List<Hurtbox>();
        GameSession session;

        static int serial;
        public string InstanceKey { get; private set; }
        public string DisplayName => Def?.name ?? name;
        public bool Alive => Ready && Brain.Alive;
        public Transform AimPoint => chest ? chest : transform;
        public Vector3 HeadPosition => head ? head.position : transform.position + Vector3.up * Def.height;

        public void Initialize(GameSession s, MonsterDef def, IEnumerable<string> brokenParts)
        {
            session = s; Def = def; MonsterId = def.id; InstanceKey = "m" + (++serial);
            Brain = new MonsterBrain(def);
            Parts = new PartTracker(def, brokenParts);
            cc = GetComponent<CharacterController>();
            Anim = new AnimationDriver(Visual, def.model);
            hand = WorldKit.Find(Visual, "handslot.r");
            chest = WorldKit.Find(Visual, "chest");
            head = WorldKit.Find(Visual, "head");
            eyes = Visual.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name.Contains("Eyes"));
            if (eyes) eyesBase = eyes.sharedMaterial;
            Home = transform.position;
            patrolTarget = Home;
            AttachWeapon();
            BuildHurtboxes();
            walkNatural = Mathf.Max(.3f, Anim.NaturalSpeed("Walking_A", WorldKit.Find(Visual, "foot.l"), WorldKit.Find(Visual, "foot.r"), transform));
            foreach (var p in Parts.Broken) HidePartMesh(Def.parts.FirstOrDefault(x => x.id == p), false);
            ring = WorldKit.Cylinder("Attack warning", transform.position, .5f, .02f, WorldKit.Transparent("Telegraph ring", new Color(.95f, .18f, .08f, .35f)));
            ring.transform.SetParent(transform, true);
            ring.SetActive(false);
            Brain.ModeChanged += OnMode;
            Anim.Play(Anim.Has("Idle_Combat") ? "Idle" : "Idle");
            Ready = true;
        }

        void AttachWeapon()
        {
            if (string.IsNullOrEmpty(Def.weaponVisual) || !hand || WorldKit.Find(hand, Def.weaponVisual + " (held)")) return;
            var template = Resources.Load<GameObject>("Models/Hunter");
            var src = template ? WorldKit.Find(template.transform, Def.weaponVisual) : null;
            if (!src) return;
            // The sample's working approach: the Knight's sword is authored under handslot.r, so its local
            // transform is already relative to a hand slot; copy it under this monster's real hand slot bone.
            var sword = Instantiate(src.gameObject, hand, false);
            sword.name = Def.weaponVisual + " (held)";
            sword.transform.localPosition = src.localPosition;
            sword.transform.localRotation = src.localRotation;
            sword.transform.localScale = src.localScale;
            var mat = CharacterFactory.Atlas("Hunter");
            foreach (var r in sword.GetComponentsInChildren<Renderer>()) r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
        }

        void BuildHurtboxes()
        {
            float scale = Def.height / 1.8f;
            foreach (var group in Def.parts.GroupBy(p => p.bone))
            {
                var bone = WorldKit.Find(Visual, group.Key);
                if (!bone) { Debug.LogWarning("Monster " + Def.id + " missing bone " + group.Key); continue; }
                var go = new GameObject("Hurtbox " + string.Join("+", group.Select(p => p.id)));
                go.layer = Layers.Hurtbox;
                go.transform.SetParent(bone, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                var ls = bone.lossyScale;
                go.transform.localScale = new Vector3(1 / Mathf.Max(1e-4f, ls.x), 1 / Mathf.Max(1e-4f, ls.y), 1 / Mathf.Max(1e-4f, ls.z));
                var col = go.AddComponent<SphereCollider>();
                col.isTrigger = true;
                col.radius = group.Max(p => p.radius) * scale;
                if (group.Any(p => p.height > 0)) col.center = Vector3.zero;
                var hb = go.AddComponent<Hurtbox>();
                hb.Owner = this; hb.Bone = group.Key; hb.Parts = group.Select(p => p.id).ToList();
                hurtboxes.Add(hb);
            }
        }

        public IEnumerable<Hurtbox> Hurtboxes => hurtboxes;

        /// <summary>Called after load, unpause, a menu closing, or the player respawning: no attack can start immediately.</summary>
        public void Calm(float seconds) { if (Ready) Brain.AddGrace(seconds); }

        void OnMode(MonsterMode m)
        {
            ring.SetActive(m == MonsterMode.Telegraph || m == MonsterMode.Attack);
            if (eyes) eyes.sharedMaterial = m == MonsterMode.Telegraph || m == MonsterMode.Attack ? WorldKit.Glow(new Color(1f, .15f, .05f), 4) : eyesBase;
            switch (m)
            {
                case MonsterMode.Idle: Anim.Play("Idle"); break;
                case MonsterMode.Patrol: patrolTarget = Home + Quaternion.Euler(0, Random.Range(0, 360), 0) * Vector3.forward * Random.Range(.5f, PatrolRadius); break;
                case MonsterMode.Alert: Anim.Play(Anim.Has("Idle_Combat") ? "Idle_Combat" : "Idle", true, 1, false, .08f); WorldKit.Sound(transform.position, "step", .25f); break;
                case MonsterMode.Telegraph:
                    {
                        var atk = Brain.CurrentAttack;
                        // The strike frame of the clip lands exactly when the active window opens.
                        Anim.PlayStrike(atk.anim, atk.telegraph + atk.active * .5f, hand);
                        break;
                    }
                case MonsterMode.Stagger: Anim.Play("Hit_A", false, .8f, true); break;
                case MonsterMode.Return: returnBest = float.MaxValue; returnStall = 0; detourFor = 0; Anim.Play("Walking_A", true, Gait(Def.chaseSpeed)); break;
                case MonsterMode.Dead: Die(); break;
            }
        }

        void Update()
        {
            if (!Ready || session == null || !session.Simulating) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            if (Brain.Mode == MonsterMode.Dead)
            {
                if (deathTime > 0 && Time.time - deathTime > 12) gameObject.SetActive(false);
                return;
            }
            var hero = session.Hunter;
            Vector3 to = hero.transform.position - transform.position; to.y = 0;
            float dist = to.magnitude;
            Vector3 fromHome = transform.position - Home; fromHome.y = 0;
            bool los = !Physics.Linecast(transform.position + Vector3.up * 1.4f, hero.transform.position + Vector3.up * 1.2f, Layers.WorldMask, QueryTriggerInteraction.Ignore);
            var senses = new MonsterBrain.Senses
            {
                distanceToPlayer = dist,
                distanceFromHome = fromHome.magnitude,
                angleToPlayer = dist > .01f ? Vector3.Angle(transform.forward, to) : 0,
                lineOfSight = los,
                playerAlive = !hero.Dead,
                playerHidden = session.PlayerInSafeArea(Def)
            };
            float burn = Brain.Status.Tick(dt);
            if (burn > 0 && Brain.TakeStatusDamage(burn)) { OnKilled(); return; }
            Brain.Tick(dt, senses);
            if (!Brain.Alive) { OnKilled(); return; }

            Vector3 move = Vector3.zero;
            float speed = 0;
            float slow = Brain.Status.SpeedMultiplier;
            switch (Brain.Mode)
            {
                case MonsterMode.Patrol:
                    {
                        var d = patrolTarget - transform.position; d.y = 0;
                        if (d.magnitude > .4f) { Face(d, dt); move = Flat(transform.forward); speed = Def.walkSpeed; Anim.Play("Walking_A", true, Gait(Def.walkSpeed)); }   // curves toward the spot along its facing (SightForge: patrols walked sideways)
                        else Anim.Play("Idle");
                        break;
                    }
                case MonsterMode.Alert: Face(to, dt * 2); break;
                case MonsterMode.Chase:
                    Face(to, dt);
                    float stop = Def.attacks.Min(a => a.range) * .8f;
                    if (dist > stop) { move = to.normalized; speed = Def.chaseSpeed; Anim.Play("Walking_A", true, Gait(Def.chaseSpeed)); }
                    else Anim.Play(Anim.Has("Idle_Combat") ? "Idle_Combat" : "Idle");
                    break;
                case MonsterMode.Telegraph:
                    // Tracks the player during the wind-up, then commits: the swing direction locks before it lands.
                    if (Brain.ModeTime < Brain.CurrentAttack.telegraph * .6f) Face(to, dt * .7f);
                    UpdateRing();
                    break;
                case MonsterMode.Attack:
                    UpdateRing();
                    if (!Brain.HitDelivered && hero.Overlaps(AttackCenter, Brain.CurrentAttack.radius))
                    {
                        Brain.HitDelivered = true;
                        hero.ReceiveHit(Brain.CurrentAttack, transform.position);
                    }
                    break;
                case MonsterMode.Return:
                    {
                        var d = Home - transform.position; d.y = 0;
                        float left = d.magnitude;
                        if (left < returnBest - .25f) { returnBest = left; returnStall = 0; } else returnStall += dt;
                        if (returnStall > 4f && dist > 12f) { WarpHome(); break; }   // blocked with nobody watching: reset home
                        if (left > .3f)
                        {
                            var dir = d / left;
                            if (returnStall > 1f && detourFor <= 0) { detour = Vector3.Cross(Vector3.up, dir) * (Random.value < .5f ? 1 : -1); detourFor = 1.2f; }
                            if (detourFor > 0) { detourFor -= dt; dir = (dir * .3f + detour).normalized; }   // step around whatever blocks the way
                            Face(dir, dt); move = Flat(transform.forward); speed = Def.chaseSpeed;
                        }
                        break;
                    }
            }
            vertical = cc.isGrounded ? -2 : Mathf.Max(-20, vertical - dt * 24);
            cc.Move((move * speed * slow + Vector3.up * vertical) * dt);
            if (hurtFlash > 0) hurtFlash -= dt;
        }

        float returnBest, returnStall, detourFor;
        Vector3 detour;

        void WarpHome()
        {
            cc.enabled = false;
            transform.position = Home;
            cc.enabled = true;
            returnStall = 0;
        }

        /// <summary>Walk playback rate that matches ground speed to the clip's stride (no foot sliding).</summary>
        float Gait(float speed) => Mathf.Clamp(speed * Brain.Status.SpeedMultiplier / walkNatural, .5f, 2.5f);

        void UpdateRing()
        {
            var atk = Brain.CurrentAttack;
            float grow = Brain.Mode == MonsterMode.Attack ? 1 : Mathf.Clamp01(Brain.ModeTime / atk.telegraph);
            ring.transform.position = new Vector3(AttackCenter.x, session.GroundHeight(AttackCenter) + .03f, AttackCenter.z);
            float r = atk.radius * 2 * Mathf.Lerp(.35f, 1, grow);
            ring.transform.localScale = new Vector3(r / transform.lossyScale.x, .01f, r / transform.lossyScale.z);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.normalized; }

        void Face(Vector3 dir, float dt)
        {
            if (dir.sqrMagnitude < .01f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 1 - Mathf.Exp(-dt * Def.turnSpeed));
        }

        /// <summary>Receives a resolved player hit on one of this monster's bones.</summary>
        public void ReceiveHit(Hurtbox box, Vector3 point, System.Func<MonsterPartDef, bool, HitInfo> computeHit, string statusId = null, float statusBuildup = 0)
        {
            if (!Alive) return;
            var part = box ? Parts.Resolve(box.Parts) : null;
            bool partBroken = part != null && Parts.Broken.Contains(part.id);
            var hit = computeHit(part, partBroken);
            if (part != null && Parts.Damage(part.id, hit.Damage)) BreakPart(part);
            if (!string.IsNullOrEmpty(statusId) && session.Core.Db.Statuses.TryGetValue(statusId, out var st)) Brain.Status.Add(st, statusBuildup);
            bool killed = Brain.TakeHit(hit);
            hurtFlash = .12f;
            WorldKit.Burst(point, hit.WeakPoint ? new Color(1f, .8f, .3f) : new Color(.85f, .7f, .45f), hit.WeakPoint ? 14 : 8);
            WorldKit.Sound(point, "hit", hit.WeakPoint ? .4f : .25f);
            WorldKit.DamageNumber(point + Vector3.up * .3f, hit.Damage, hit.WeakPoint, hit.Critical);
            if (Brain.Mode != MonsterMode.Stagger && !killed && Brain.Mode != MonsterMode.Telegraph && Brain.Mode != MonsterMode.Attack)
                Anim.Play("Hit_A", false, 1.4f, true, .05f);
            session.OnMonsterHit(this, hit, part);
            if (killed) OnKilled();
        }

        void BreakPart(MonsterPartDef part)
        {
            HidePartMesh(part, true);
            session.OnPartBroken(this, part);
            Brain.ForceStagger();
        }

        /// <summary>Hides the part's mesh (skinned or rigidly bone-attached) and drops a physical copy of it.</summary>
        void HidePartMesh(MonsterPartDef part, bool detach)
        {
            if (part == null || string.IsNullOrEmpty(part.breakMesh)) return;
            var renderer = Visual.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == part.breakMesh || r.name.EndsWith(part.breakMesh));
            if (!renderer || !renderer.enabled) return;
            if (detach)
            {
                Mesh mesh;
                Vector3 scale = Vector3.one;
                if (renderer is SkinnedMeshRenderer smr) { mesh = new Mesh { name = part.breakMesh + " (broken)" }; smr.BakeMesh(mesh, true); }
                else { mesh = renderer.GetComponent<MeshFilter>().sharedMesh; scale = renderer.transform.lossyScale; }
                var piece = new GameObject(part.name + " - broken piece");
                piece.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                piece.transform.localScale = scale;
                piece.AddComponent<MeshFilter>().sharedMesh = mesh;
                piece.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                var col = piece.AddComponent<BoxCollider>();
                col.center = mesh.bounds.center; col.size = Vector3.Max(mesh.bounds.size, Vector3.one * .02f);
                piece.layer = 2;   // debris: never mistaken for ground by drop/placement rays
                var rb = piece.AddComponent<Rigidbody>();
                rb.mass = 2;
                rb.AddForce((Vector3.up * 2.5f + Random.insideUnitSphere) * 2f, ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * 3, ForceMode.Impulse);
                Destroy(piece, 30);
            }
            renderer.enabled = false;
        }

        public Renderer PartRenderer(string partId)
        {
            var part = Def.parts.FirstOrDefault(p => p.id == partId);
            if (part == null || string.IsNullOrEmpty(part.breakMesh)) return null;
            return Visual.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == part.breakMesh || r.name.EndsWith(part.breakMesh));
        }

        bool killHandled;
        void OnKilled()
        {
            if (killHandled) return;
            killHandled = true;
            session.OnMonsterKilled(this);
        }

        void Die()
        {
            ring.SetActive(false);
            if (eyes) eyes.sharedMaterial = eyesBase;
            Anim.Play("Death_A", false, 1, true, .06f);
            if (cc) cc.enabled = false;
            foreach (var h in hurtboxes) h.gameObject.SetActive(false);
            deathTime = Time.time;
        }

        public void ResetToHome()
        {
            if (!Ready) return;
            cc.enabled = false; transform.position = Home; cc.enabled = true;
            Brain.Health = Def.maxHealth;
            Calm(2);
        }
    }
}
