using System.Collections.Generic;
using UnityEngine;

namespace FH.WorldForge
{
    /// <summary>A path drawn through control points: a Catmull-Rom curve through them (8 steps a span), optionally
    /// winding sideways (curvature, by noise along its length), kept as a polyline with distances along it.</summary>
    public sealed class WFSpline
    {
        public readonly List<Vector3> P = new List<Vector3>();
        public readonly List<float> S = new List<float>();     // distance along the path at each point
        public float Length => S.Count > 0 ? S[S.Count - 1] : 0;
        public Bounds Box;

        public WFSpline(IList<Vector3> pts, float curvature = 0, WFNoise noise = null, int steps = 8)
        {
            if (pts.Count == 1) { P.Add(pts[0]); S.Add(0); Box = new Bounds(pts[0], Vector3.zero); return; }
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(pts.Count - 1, i + 2)];
                for (int k = 0; k < steps; k++)
                {
                    float t = k / (float)steps, t2 = t * t, t3 = t2 * t;
                    P.Add(.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3));
                }
            }
            P.Add(pts[pts.Count - 1]);
            float s = 0; S.Add(0);
            for (int i = 1; i < P.Count; i++) { s += Vector3.Distance(P[i], P[i - 1]); S.Add(s); }
            if (curvature > 0 && noise != null)
            {
                // wind sideways (horizontally, across the path), ends held in place
                var moved = new Vector3[P.Count];
                for (int i = 0; i < P.Count; i++)
                {
                    Vector3 t = Tangent(i); var side = Vector3.Cross(Vector3.up, t); side.y = 0;
                    float fade = Mathf.Clamp01(Mathf.Min(S[i], Length - S[i]) / Mathf.Max(1, curvature * 2));
                    moved[i] = P[i] + side.normalized * noise.Noise(S[i] * .07f, 3.3f, 7.7f) * curvature * 1.6f * fade;
                }
                P.Clear(); P.AddRange(moved);
                s = 0; S.Clear(); S.Add(0);
                for (int i = 1; i < P.Count; i++) { s += Vector3.Distance(P[i], P[i - 1]); S.Add(s); }
            }
            Box = new Bounds(P[0], Vector3.zero);
            foreach (var q in P) Box.Encapsulate(q);
        }

        public Vector3 Tangent(int i)
        {
            if (P.Count < 2) return Vector3.forward;
            int a = Mathf.Max(0, i - 1), b = Mathf.Min(P.Count - 1, i + 1);
            var t = P[b] - P[a];
            return t.sqrMagnitude > 1e-8f ? t.normalized : Vector3.forward;
        }

        /// <summary>The nearest point of the path: q, the distance along it s, its direction t; along &gt; 0 past the
        /// ends (how far beyond). horizontal: nearest in plan view (the height ignored).</summary>
        public void Closest(Vector3 p, bool horizontal, out Vector3 q, out float s, out Vector3 t, out float beyond)
        {
            float best = float.MaxValue; q = P[0]; s = 0; t = Tangent(0); beyond = 0;
            if (P.Count == 1) { q = P[0]; beyond = 0; return; }
            for (int i = 0; i < P.Count - 1; i++)
            {
                Vector3 a = P[i], b = P[i + 1], ab = b - a, ap = p - a;
                if (horizontal) { ab.y = 0; ap.y = 0; }
                float l2 = ab.sqrMagnitude;
                float u = l2 > 1e-9f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / l2) : 0;
                Vector3 c = a + (b - a) * u;
                Vector3 d = p - c; if (horizontal) d.y = 0;
                float dd = d.sqrMagnitude;
                if (dd < best) { best = dd; q = c; s = S[i] + (S[i + 1] - S[i]) * u; t = (b - a).normalized; }
            }
            // past an end: how far along the end direction
            if (s <= 1e-4f) { var d = p - P[0]; if (horizontal) d.y = 0; beyond = Mathf.Max(0, -Vector3.Dot(d, Tangent(0))); }
            else if (s >= Length - 1e-4f) { var d = p - P[P.Count - 1]; if (horizontal) d.y = 0; beyond = Mathf.Max(0, Vector3.Dot(d, Tangent(P.Count - 1))); }
        }

        public Vector3 At(float s)
        {
            if (P.Count == 1) return P[0];
            s = Mathf.Clamp(s, 0, Length);
            for (int i = 0; i < P.Count - 1; i++)
                if (s <= S[i + 1]) return Vector3.Lerp(P[i], P[i + 1], (s - S[i]) / Mathf.Max(1e-6f, S[i + 1] - S[i]));
            return P[P.Count - 1];
        }

        public Vector3 DirAt(float s)
        {
            for (int i = 0; i < P.Count - 1; i++) if (s <= S[i + 1]) return Tangent(i);
            return Tangent(P.Count - 1);
        }
    }
}
