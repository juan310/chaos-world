using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Every hit test the game makes, kept for SightForge's hit viewer (`eyes_inspect.py hits`): the hunter's blade
    /// sweeps, arrows and spells (a capsule from a to b with a radius), the monsters' strikes (a ball against the hunter),
    /// the hits that landed, and for a sweep that hit nothing the nearest monster's hurtboxes and how far it missed them by.
    /// A ring of the latest entries (DevCapture writes it on F9 and at the end of a verification run).</summary>
    public static class HitTrace
    {
        public static int Capacity = 6000;
        static readonly Queue<string> entries = new Queue<string>();
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static int Count => entries.Count;

        public static void Clear() => entries.Clear();

        /// <summary>A move's shown name (hunt moves have one), else its id.</summary>
        public static string MoveName(AttackDef d) => d == null ? "" : d is HuntMoveDef h && !string.IsNullOrEmpty(h.name) ? h.name : d.id;

        /// <summary>A sweep by the hunter's blade, an arrow or a spell. found: the hurtbox colliders it overlapped.</summary>
        public static void Sweep(string kind, string move, int swing, Vector3 a, Vector3 b, float r, IEnumerable<Collider> found)
        {
            var sb = Begin(kind, move, swing);
            sb.Append(",\"a\":"); V(sb, a); sb.Append(",\"b\":"); V(sb, b); sb.Append(",\"r\":").Append(F(r));
            var boxes = found?.Select(c => c ? c.GetComponent<Hurtbox>() : null).Where(h => h && h.Owner).Distinct().ToList() ?? new List<Hurtbox>();
            sb.Append(",\"touched\":[");
            for (int i = 0; i < boxes.Count; i++) { if (i > 0) sb.Append(','); sb.Append("{\"monster\":\"").Append(boxes[i].Owner.InstanceKey).Append("\",\"bone\":\"").Append(boxes[i].Bone).Append("\"}"); }
            sb.Append(']');
            if (boxes.Count == 0) Nearest(sb, a, b, r);
            Push(sb);
        }

        /// <summary>A hit that landed (after the closest-box and once-per-swing rules).</summary>
        public static void Landed(string kind, string move, int swing, MonsterActor m, Hurtbox box, Vector3 point, float damage)
        {
            var sb = Begin(kind == "hunter" ? "landed" : kind + "_landed", move, swing);
            sb.Append(",\"monster\":\"").Append(m ? m.InstanceKey : "").Append("\",\"bone\":\"").Append(box ? box.Bone : "").Append("\",\"point\":"); V(sb, point);
            sb.Append(",\"damage\":").Append(F(damage));
            Push(sb);
        }

        /// <summary>A monster's strike tested against the hunter this frame (a ball at the attack's centre).</summary>
        public static void MonsterStrike(MonsterActor m, string attack, Vector3 center, float radius, HunterController h, bool landed)
        {
            var sb = Begin("monster", attack, 0);
            sb.Append(",\"monster\":\"").Append(m.InstanceKey).Append("\",\"name\":\"").Append(m.DisplayName.Replace("\"", "'")).Append('"');
            sb.Append(",\"a\":"); V(sb, center); sb.Append(",\"b\":"); V(sb, center); sb.Append(",\"r\":").Append(F(radius));
            sb.Append(",\"hunter\":"); V(sb, h.transform.position); sb.Append(",\"hunterRadius\":").Append(F(h.Body ? h.Body.radius : .35f));
            sb.Append(",\"invulnerable\":").Append(h.Invulnerable ? "true" : "false").Append(",\"landed\":").Append(landed ? "true" : "false");
            Push(sb);
        }

        /// <summary>The nearest monster's hurtboxes (their capsules now) and the gap between the sweep and each.</summary>
        static void Nearest(StringBuilder sb, Vector3 a, Vector3 b, float r)
        {
            var s = GameSession.I; if (!s) return;
            MonsterActor best = null; float bd = 1e9f;
            var mid = (a + b) * .5f;
            foreach (var m in s.Monsters)
            {
                if (!m || !m.Alive) continue;
                float d = Vector3.Distance(m.transform.position, mid);
                if (d < bd) { bd = d; best = m; }
            }
            if (!best || bd > Vector3.Distance(a, b) + 6) return;
            sb.Append(",\"nearest\":{\"monster\":\"").Append(best.InstanceKey).Append("\",\"name\":\"").Append(best.DisplayName.Replace("\"", "'")).Append("\",\"boxes\":[");
            bool first = true;
            foreach (var hb in best.Hurtboxes)
            {
                if (!hb) continue;
                Shape(hb, out var p, out var q, out float br);
                float gap = SegmentDistance(a, b, p, q) - r - br;
                if (!first) sb.Append(','); first = false;
                sb.Append("{\"bone\":\"").Append(hb.Bone).Append("\",\"a\":"); V(sb, p); sb.Append(",\"b\":"); V(sb, q);
                sb.Append(",\"r\":").Append(F(br)).Append(",\"gap\":").Append(F(gap)).Append('}');
            }
            sb.Append("]}");
        }

        /// <summary>A hurtbox as a world capsule (a = b for a ball).</summary>
        public static void Shape(Hurtbox hb, out Vector3 a, out Vector3 b, out float r)
        {
            var t = hb.transform;
            float s = Mathf.Max(t.lossyScale.x, t.lossyScale.y, t.lossyScale.z);
            if (hb.GetComponent<CapsuleCollider>() is CapsuleCollider c && c)
            {
                r = c.radius * s;
                var axis = c.direction == 0 ? Vector3.right : c.direction == 1 ? Vector3.up : Vector3.forward;
                float half = Mathf.Max(0, c.height * .5f - c.radius);
                a = t.TransformPoint(c.center - axis * half); b = t.TransformPoint(c.center + axis * half);
            }
            else if (hb.GetComponent<SphereCollider>() is SphereCollider sc && sc)
            {
                r = sc.radius * s; a = b = t.TransformPoint(sc.center);
            }
            else { r = .3f; a = b = t.position; }
        }

        /// <summary>Every live monster's hurtboxes now (for the viewer's picture of the moment of a capture).</summary>
        public static string Monsters()
        {
            var sb = new StringBuilder(4096); sb.Append('[');
            var s = GameSession.I; bool first = true;
            if (s)
                foreach (var m in s.Monsters)
                {
                    if (!m || !m.Alive) continue;
                    if (!first) sb.Append(','); first = false;
                    sb.Append("{\"monster\":\"").Append(m.InstanceKey).Append("\",\"name\":\"").Append(m.DisplayName.Replace("\"", "'")).Append("\",\"position\":"); V(sb, m.transform.position);
                    sb.Append(",\"boxes\":[");
                    bool f2 = true;
                    foreach (var hb in m.Hurtboxes)
                    {
                        if (!hb) continue;
                        Shape(hb, out var p, out var q, out float r);
                        if (!f2) sb.Append(','); f2 = false;
                        sb.Append("{\"bone\":\"").Append(hb.Bone).Append("\",\"parts\":\"").Append(string.Join("+", hb.Parts)).Append("\",\"a\":"); V(sb, p); sb.Append(",\"b\":"); V(sb, q); sb.Append(",\"r\":").Append(F(r)).Append('}');
                    }
                    sb.Append("]}");
                }
            return sb.Append(']').ToString();
        }

        public static string Json()
        {
            var sb = new StringBuilder(entries.Count * 160 + 4096);
            sb.Append("{\"time\":").Append(F(Time.time)).Append(",\"monsters\":").Append(Monsters()).Append(",\"entries\":[\n");
            bool first = true;
            foreach (var e in entries) { if (!first) sb.Append(",\n"); first = false; sb.Append(e); }
            return sb.Append("\n]}").ToString();
        }

        static StringBuilder Begin(string kind, string move, int swing)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"t\":").Append(F(Time.time)).Append(",\"frame\":").Append(Time.frameCount).Append(",\"kind\":\"").Append(kind)
              .Append("\",\"move\":\"").Append((move ?? "").Replace("\"", "'")).Append("\",\"swing\":").Append(swing);
            return sb;
        }

        static void Push(StringBuilder sb)
        {
            entries.Enqueue(sb.Append('}').ToString());
            while (entries.Count > Capacity) entries.Dequeue();
        }

        static string F(float v) => v.ToString("0.###", Inv);
        static void V(StringBuilder sb, Vector3 v) => sb.Append('[').Append(F(v.x)).Append(',').Append(F(v.y)).Append(',').Append(F(v.z)).Append(']');

        /// <summary>Shortest distance between segments pq and uv.</summary>
        public static float SegmentDistance(Vector3 p, Vector3 q, Vector3 u, Vector3 v)
        {
            Vector3 d1 = q - p, d2 = v - u, r = p - u;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r), s, t;
            if (a < 1e-8f && e < 1e-8f) return r.magnitude;
            if (a < 1e-8f) { s = 0; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e < 1e-8f) { t = 0; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), den = a * e - b * b;
                    s = den > 1e-8f ? Mathf.Clamp01((b * f - c * e) / den) : 0;
                    t = (b * s + f) / e;
                    if (t < 0) { t = 0; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1) { t = 1; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return Vector3.Distance(p + d1 * s, u + d2 * t);
        }
    }
}
