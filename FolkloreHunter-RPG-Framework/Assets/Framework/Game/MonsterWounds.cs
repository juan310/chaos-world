using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Hunt rules: the monster's wounds (<see cref="WoundTracker"/>) on its body. A wound shows where it opened
    /// as a red rune-scar on that bone: a faint glow normally, bright and pulsing in the hunter's Sight. A Rend strike
    /// that reaches it tears it (more damage, the monster flinches; every third tear topples it); a torn spot shows a
    /// pale scar until it can wound again.</summary>
    public class MonsterWounds : MonoBehaviour
    {
        /// <summary>A Rend strike on an open wound deals this much more (on top of the wound's own bonus).</summary>
        public const float TearMultiplier = 2.2f;
        public WoundTracker Tracker { get; private set; }
        public int Opened => Tracker.Opened;
        public int Torn => Tracker.Torn;

        class Mark { public string key; public Hurtbox box; public Vector3 local; public GameObject orb; public bool scar; }
        readonly Dictionary<string, Mark> marks = new Dictionary<string, Mark>();
        MonsterActor M;
        GameSession S;
        static Material open, bright, scarMat;

        public void Initialize(GameSession s, MonsterActor m)
        {
            S = s; M = m;
            Tracker = new WoundTracker(m.Def.maxHealth);
            if (!open) { open = WorldKit.Glow(new Color(1f, .16f, .08f), 2.2f); bright = WorldKit.Glow(new Color(1f, .3f, .12f), 5); scarMat = WorldKit.Glow(new Color(.75f, .65f, .6f), .8f); }
        }

        public static string Key(Hurtbox box) => box ? (!string.IsNullOrEmpty(box.Bone) ? box.Bone : box.name) : null;

        public bool IsOpen(Hurtbox box) => box && Tracker.IsOpen(Key(box), Time.time);

        /// <summary>A blow on <paramref name="box"/>: returns the damage scale (wound bonus, tear) and whether this blow
        /// tore the wound. A blow that is not a tear builds toward opening one there.</summary>
        public float Scale(Hurtbox box, bool rend, out bool tore)
        {
            tore = false;
            string key = Key(box);
            if (key == null) return 1;
            float now = Time.time;
            if (rend && Tracker.IsOpen(key, now)) { tore = true; return WoundTracker.DamageBonus * TearMultiplier; }
            return Tracker.Multiplier(key, now);
        }

        /// <summary>After the blow landed with <paramref name="damage"/>: tears or builds the wound and shows it.</summary>
        public void Landed(Hurtbox box, Vector3 point, float damage, bool tore)
        {
            string key = Key(box);
            if (key == null) return;
            float now = Time.time;
            if (tore)
            {
                Tracker.Tear(key, now);
                if (marks.TryGetValue(key, out var mk)) { mk.scar = true; Show(mk); }
                Vector3 at = mk != null && mk.orb ? mk.orb.transform.position : point;
                WorldKit.Burst(at, new Color(1f, .25f, .1f), 30);
                WorldKit.Burst(at, new Color(1f, .85f, .5f), 14);
                WorldKit.Sound(at, "hit", .55f);
                S.CameraRig.Shake(.35f);
                if (Tracker.Unravels)
                {
                    M.Brain.Topple(4f);
                    S.Hud?.Notify(M.DisplayName + " unravels! Third wound torn: it is down!", NoticeKind.Reward);
                }
                else M.Brain.Flinch(.85f);
                S.Hunter.Hunt?.WoundTorn(M, Tracker.Torn, Tracker.Unravels);
                return;
            }
            if (!Tracker.Hit(key, damage, now)) return;
            var m = new Mark { key = key, box = box, local = box.transform.InverseTransformPoint(point) };
            if (marks.TryGetValue(key, out var old) && old.orb) Destroy(old.orb);
            marks[key] = m;
            Show(m);
            WorldKit.Burst(point, new Color(1f, .2f, .08f), 16);
            S.Hud?.Flash("A wound opened on the " + M.DisplayName + "  (Sight: hold LT, then Y to Rend)");
        }

        void Show(Mark m)
        {
            if (!m.box) return;
            if (!m.orb)
            {
                m.orb = WorldKit.Orb("Wound " + m.key, m.box.transform.TransformPoint(m.local), .1f, open, m.box.transform);
                m.orb.layer = 2;
            }
            m.orb.GetComponent<Renderer>().sharedMaterial = m.scar ? scarMat : open;
        }

        /// <summary>World positions of the open wounds.</summary>
        public IEnumerable<(Hurtbox box, Vector3 at)> OpenWounds()
        {
            float now = Time.time;
            foreach (var m in marks.Values)
                if (!m.scar && m.box && m.orb && Tracker.IsOpen(m.key, now)) yield return (m.box, m.orb.transform.position);
        }

        /// <summary>The open wound within <paramref name="within"/> metres of the segment a..b (a Rend's blade, or the
        /// point an arrow struck), the nearest one; null when none is that close.</summary>
        public Hurtbox WoundNear(Vector3 a, Vector3 b, float within)
        {
            Hurtbox best = null; float bestD = within;
            foreach (var (box, at) in OpenWounds())
            {
                var ab = b - a;
                float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector3.Dot(at - a, ab) / ab.sqrMagnitude);
                float d = Vector3.Distance(at, a + ab * t);
                if (d < bestD) { bestD = d; best = box; }
            }
            return best;
        }

        void LateUpdate()
        {
            if (Tracker == null) return;
            float now = Time.time;
            bool sight = S && S.Hunter && S.Hunter.Hunt && S.Hunter.Hunt.enabled && S.Hunter.Hunt.Sight;
            foreach (var m in marks.Values.ToList())
            {
                if (!m.orb) continue;
                bool alive = m.scar ? Tracker.IsScarred(m.key, now) : Tracker.IsOpen(m.key, now);
                if (!alive || !M.Alive) { Destroy(m.orb); marks.Remove(m.key); continue; }
                // A constant size in the world whatever the bone's scale: faint 0.2 m normally, 0.34 m and pulsing in Sight.
                float r = m.scar ? .14f : sight ? .34f * (1 + .18f * Mathf.Sin(now * 9)) : .2f;
                var parent = m.orb.transform.parent;
                float k = parent ? Mathf.Max(1e-4f, parent.lossyScale.x) : 1;
                m.orb.transform.localScale = Vector3.one * r / k;
                if (!m.scar) m.orb.GetComponent<Renderer>().sharedMaterial = sight ? bright : open;
            }
        }

        /// <summary>The fight starts over (the monster sent home): no wounds, scars or tears.</summary>
        public void Clear()
        {
            foreach (var m in marks.Values) if (m.orb) Destroy(m.orb);
            marks.Clear(); Tracker?.Reset();
        }

        void OnDestroy() { foreach (var m in marks.Values) if (m.orb) Destroy(m.orb); }
    }
}
