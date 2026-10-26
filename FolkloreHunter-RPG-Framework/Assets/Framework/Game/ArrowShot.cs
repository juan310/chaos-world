using System.Collections.Generic;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>One flying arrow from the hunt bow, drawn with the hunter's own arrow mesh. A normal arrow stops at the
    /// first body part it strikes; a piercing one (Dragon Piercer) passes through and strikes each part again every
    /// pierce interval up to a limit; arc arrows wait, then fall from above onto their landing point.</summary>
    public class ArrowShot : MonoBehaviour
    {
        public static int Fired { get; private set; }
        public static int Landed { get; private set; }

        GameSession S;
        HunterController H;
        AttackDef hit;
        ShotDef shot;
        Vector3 velocity;
        float life, waitUntil, gravity;
        readonly Dictionary<Hurtbox, float> struck = new Dictionary<Hurtbox, float>();
        readonly Dictionary<MonsterActor, int> hitsOn = new Dictionary<MonsterActor, int>();
        readonly Collider[] found = new Collider[12];

        /// <summary>Looses one arrow. <paramref name="hitDef"/> carries this arrow's motion value, poise and stun.</summary>
        public static ArrowShot Fire(GameSession s, HunterController h, Transform template, ShotDef shot, AttackDef hitDef, Vector3 from, Vector3 dir, float delay = 0)
        {
            var go = new GameObject(shot.pierce ? "Dragon Piercer bolt" : shot.arc ? "Arc arrow" : "Arrow");
            go.transform.position = from;
            if (template)
            {
                var vis = Instantiate(template.gameObject, go.transform);
                vis.name = "Arrow mesh";
                foreach (var r in vis.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                vis.transform.localScale = template.lossyScale * (shot.pierce ? 2.2f : 1f);
                // The template's head points along its mesh's long axis away from the nock (its origin): turn that onto +Z.
                var mesh = template.GetComponent<MeshFilter>()?.sharedMesh;
                Vector3 head = mesh ? mesh.bounds.center : Vector3.forward;
                var b = mesh ? mesh.bounds : new Bounds(Vector3.forward * .5f, Vector3.one);
                Vector3 axis = b.size.x > b.size.y && b.size.x > b.size.z ? Vector3.right : b.size.y > b.size.z ? Vector3.up : Vector3.forward;
                axis *= Mathf.Sign(Vector3.Dot(head, axis) == 0 ? 1 : Vector3.Dot(head, axis));
                vis.transform.localRotation = Quaternion.FromToRotation(axis, Vector3.forward);
                vis.transform.localPosition = -(vis.transform.localRotation * Vector3.Scale(b.center, vis.transform.localScale));
            }
            if (shot.pierce)
            {
                var glow = WorldKit.Orb("Piercer glow", from, .22f, WorldKit.Glow(new Color(1f, .75f, .35f), 3), go.transform);
                Destroy(glow.GetComponent<Collider>());
                var light = go.AddComponent<Light>(); light.color = new Color(1f, .7f, .3f); light.range = 5; light.intensity = 2;
            }
            var a = go.AddComponent<ArrowShot>();
            a.S = s; a.H = h; a.shot = shot; a.hit = hitDef; a.velocity = dir.normalized * shot.speed;
            a.gravity = shot.pierce || shot.arc ? 0 : 3.5f;
            a.waitUntil = Time.time + delay;
            go.transform.rotation = Quaternion.LookRotation(a.velocity);
            go.SetActive(true);
            if (delay > 0) foreach (var r in go.GetComponentsInChildren<Renderer>()) r.enabled = false;
            Fired++;
            return a;
        }

        void Update()
        {
            if (S == null || !S.Simulating) return;
            if (Time.time < waitUntil) return;
            if (waitUntil > 0) { waitUntil = 0; foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = true; }
            float dt = Time.deltaTime;
            Vector3 from = transform.position;
            velocity += Vector3.down * gravity * dt;
            transform.position += velocity * dt;
            if (velocity.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(velocity);
            life += dt;
            float radius = shot.pierce ? .45f : shot.arc ? .4f : .16f;
            int n = hit.damageMultiplier <= 0 ? 0 : Physics.OverlapCapsuleNonAlloc(from, transform.position, radius, found, Layers.HurtboxMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var box = found[i].GetComponent<Hurtbox>();
                if (!box || !box.Owner || !box.Owner.Alive) continue;
                var m = box.Owner;
                if (shot.pierce)
                {
                    if (struck.TryGetValue(box, out var at) && Time.time - at < shot.pierceInterval) continue;
                    hitsOn.TryGetValue(m, out int count);
                    if (count >= shot.pierceHits) continue;
                    struck[box] = Time.time; hitsOn[m] = count + 1;
                    Strike(m, box);
                    continue;
                }
                Strike(m, box);
                Destroy(gameObject);
                return;
            }
            RaycastHit hitInfo = default;
            if (!shot.pierce && Physics.Linecast(from, transform.position, out hitInfo, Layers.WorldMask, QueryTriggerInteraction.Ignore))
            {
                transform.position = hitInfo.point;
                if (shot.arc) WorldKit.Burst(hitInfo.point + Vector3.up * .1f, new Color(.85f, .75f, .55f), 4);
                enabled = false; Destroy(gameObject, 1.5f);       // it sticks where it landed for a moment
                return;
            }
            if (life * shot.speed > shot.range + (shot.arc ? 20 : 0)) Destroy(gameObject);
        }

        void Strike(MonsterActor m, Hurtbox box)
        {
            HitInfo dealt = default;
            var w = H.CurrentWeapon;
            bool crit = Random.value * 100 < H.Stats[Stat.CritChance];
            if (m.HuntRules && hit is HuntMoveDef hm) m.PendingStun = hm.koDamage;
            m.ReceiveHit(box, transform.position, (part, broken) => dealt = DamageMath.PlayerHit(w, hit, H.Stats, m.Def, part, broken, crit));
            H.ReportRangedHit(m, dealt);
            Landed++;
            WorldKit.Burst(transform.position, shot.pierce ? new Color(1f, .7f, .3f) : new Color(1f, .9f, .6f), shot.pierce ? 8 : 5);
            WorldKit.Sound(transform.position, "hit", shot.pierce ? .22f : .14f);
        }
    }
}
