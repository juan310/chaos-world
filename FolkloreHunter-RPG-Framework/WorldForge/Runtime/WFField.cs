using System.Collections.Generic;
using UnityEngine;

namespace FH.WorldForge
{
    /// <summary>The world as one signed field over space: below 0 is solid (rock, earth), above 0 is air; the surface the
    /// mesher draws is where it crosses 0. Built from the recipe: the ground's height, then the masses and walls added
    /// (smooth union), then the paths levelled and craters shaped, then the cavities carved (smooth subtraction). Real 3D:
    /// caves, tunnels, overhangs and arches come out as they are, not as a height map.
    /// Each feature is evaluated only inside its bounding box (its effect outside it is nil). Thread-safe.</summary>
    public sealed class WFField
    {
        public readonly WFRecipe R;
        public readonly WFNoise N;
        public readonly List<Feat> Adds = new List<Feat>(), Shapes = new List<Feat>(), Carves = new List<Feat>();
        public IEnumerable<Feat> All { get { foreach (var f in Adds) yield return f; foreach (var f in Shapes) yield return f; foreach (var f in Carves) yield return f; } }

        public WFField(WFRecipe r)
        {
            R = r; N = new WFNoise(r.seed);
            foreach (var f in r.features)
            {
                var noise = new WFNoise(r.seed * 31 + f.seed * 7 + f.name.GetHashCode());
                switch ((f.type ?? "").ToLowerInvariant())
                {
                    case "hills": Adds.Add(new Mass(f, noise, false)); break;
                    case "mountain": Adds.Add(new Mass(f, noise, true)); break;
                    case "cliff": case "rockwall": case "ledge": Adds.Add(new Wall(f, noise)); break;
                    case "arch": Adds.Add(new Arch(f, noise)); break;
                    case "ruin": Adds.Add(new Ruin(f, noise)); break;
                    case "boulders": Adds.Add(new Boulders(f, noise)); break;
                    case "path": Shapes.Add(new PathF(f, noise)); break;
                    case "crater": Shapes.Add(new Crater(f, noise)); break;
                    case "tunnel": case "cave": Carves.Add(new Tube(f, noise)); break;
                    case "chamber": Carves.Add(new Chamber(f, noise)); break;
                    case "canyon": Carves.Add(new Channel(f, noise, 6f)); break;
                    case "river": Carves.Add(new Channel(f, noise, 2f)); break;
                    case "ravine": Carves.Add(new Channel(f, noise, 1.3f)); break;
                    case "crevice": Carves.Add(new Channel(f, noise, 1f)); break;
                    case "hole": Carves.Add(new Hole(f, noise)); break;
                    default: Debug.LogWarning("WorldForge: unknown feature type '" + f.type + "' (" + f.name + ")"); break;
                }
            }
        }

        public float Ground(float x, float z)
        {
            var g = R.ground;
            return g.height + g.hills * N.Fbm2(x / g.hillScale, z / g.hillScale, 4) + g.detail * N.Fbm2(x * .3f, z * .3f, 3);
        }

        public float Eval(Vector3 p)
        {
            float f = R.ground.enabled ? p.y - Ground(p.x, p.z) : 1e4f;
            for (int i = 0; i < Adds.Count; i++) { var a = Adds[i]; if (a.Box.Contains(p)) f = SMin(f, a.Sdf(p), a.F.blend); }
            for (int i = 0; i < Shapes.Count; i++) { var s = Shapes[i]; if (s.Box.Contains(p)) f = s.Apply(f, p); }
            for (int i = 0; i < Carves.Count; i++) { var c = Carves[i]; if (c.Box.Contains(p)) f = SMax(f, -c.Sdf(p), c.F.blend * .5f); }
            return f;
        }

        /// <summary>The direction the field grows fastest (out of the solid, into the air).</summary>
        public Vector3 Gradient(Vector3 p, float h = .05f)
        {
            return new Vector3(Eval(p + new Vector3(h, 0, 0)) - Eval(p - new Vector3(h, 0, 0)),
                               Eval(p + new Vector3(0, h, 0)) - Eval(p - new Vector3(0, h, 0)),
                               Eval(p + new Vector3(0, 0, h)) - Eval(p - new Vector3(0, 0, h))) / (2 * h);
        }

        /// <summary>The surface's make-up at a point: r rock, g moss, b dirt, a wet; ruin stone separately.</summary>
        public Color Material(Vector3 p, Vector3 n, out float ruin)
        {
            float up = n.y;
            float rock = .15f + Mathf.Clamp01((.72f - up) * 3f), moss = Mathf.Clamp01((up - .62f) * 3f) * .9f, dirt = 0, wet = R.atmosphere.wetness * .35f;
            ruin = 0;
            foreach (var f in All)
            {
                if (!f.Box.Contains(p)) continue;
                f.Paint(p, n, ref rock, ref moss, ref dirt, ref wet, ref ruin);
            }
            float sum = rock + moss + dirt;
            if (sum < 1e-4f) { rock = 1; sum = 1; }
            return new Color(rock / sum, moss / sum, dirt / sum, Mathf.Clamp01(wet));
        }

        public static float SMin(float a, float b, float k)
        {
            if (k <= 1e-4f) return Mathf.Min(a, b);
            float h = Mathf.Clamp01(.5f + .5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1 - h);
        }

        public static float SMax(float a, float b, float k) => -SMin(-a, -b, k);

        // ------------------------------------------------------------------------------------------------- features
        public abstract class Feat
        {
            public readonly WFFeature F;
            public readonly WFNoise N;
            public Bounds Box;
            public WFSpline Sp;
            protected Feat(WFFeature f, WFNoise n) { F = f; N = n; }
            protected float Margin => F.roughness + F.strata + F.blend + 1f;
            public abstract float Sdf(Vector3 p);
            public virtual float Apply(float f, Vector3 p) => f;
            public virtual void Paint(Vector3 p, Vector3 n, ref float rock, ref float moss, ref float dirt, ref float wet, ref float ruin)
            {
                float near = Mathf.Clamp01(1 - Mathf.Abs(Sdf(p)) / 1.2f);
                rock += near; moss *= 1 - near * .5f;
            }
            protected float Rough(Vector3 p) => F.roughness * N.Fbm(p / Mathf.Max(.1f, F.noiseScale), 4);
            protected float Strata(Vector3 p) => F.strata <= 0 ? 0 : F.strata * Mathf.Sin(p.y * 4.1f + 2.4f * N.Noise(p.x * .09f, p.y * .05f, p.z * .09f));
            protected static Vector3 Side(Vector3 t) { var s = Vector3.Cross(Vector3.up, t); s.y = 0; return s.sqrMagnitude > 1e-6f ? s.normalized : Vector3.right; }
        }

        /// <summary>hills, mountain: a noisy ellipsoid (radius across, height up, length along Z); a mountain's surface is
        /// ridged and layered (rock faces, overhangs).</summary>
        sealed class Mass : Feat
        {
            readonly Vector3 c, r; readonly bool ridged;
            public Mass(WFFeature f, WFNoise n, bool ridged) : base(f, n)
            {
                this.ridged = ridged; c = f.points.Count > 0 ? f.points[0] : Vector3.zero;
                r = new Vector3(f.radius, f.height, f.length > 0 ? f.length : f.radius);
                Box = new Bounds(c, 2 * r + Vector3.one * 2 * Margin);
            }
            public override float Sdf(Vector3 p)
            {
                var q = p - c; var e = new Vector3(q.x / r.x, q.y / r.y, q.z / r.z);
                float d = (e.magnitude - 1) * Mathf.Min(r.x, Mathf.Min(r.y, r.z));
                if (ridged) d -= F.roughness * (.6f * N.Ridged(p / Mathf.Max(.1f, F.noiseScale), 4) + .7f * N.Fbm(p / (F.noiseScale * 5), 2));
                else d -= Rough(p);
                return d - Strata(p);
            }
        }

        /// <summary>cliff, rockwall, ledge: a wall along the points: width thick, height above the points, depth below.</summary>
        sealed class Wall : Feat
        {
            public Wall(WFFeature f, WFNoise n) : base(f, n)
            {
                Sp = new WFSpline(f.points, f.curvature, n);
                Box = Sp.Box; Box.Expand(new Vector3(f.width + 2 * Margin, 2 * (f.height + f.depth) + 2 * Margin, f.width + 2 * Margin));
            }
            public override float Sdf(Vector3 p)
            {
                Sp.Closest(p, true, out var q, out _, out _, out float beyond);
                var o = p - q; o.y = 0;
                float d = Mathf.Max(Mathf.Max(o.magnitude - F.width * .5f, beyond), Mathf.Max(p.y - (q.y + F.height), (q.y - F.depth) - p.y));
                return d - Rough(p) - Strata(p);
            }
        }

        /// <summary>arch: from points[0] to points[1] (its feet), height tall, width thick.</summary>
        sealed class Arch : Feat
        {
            readonly Vector3 mid, u, w; readonly float R, H;
            public Arch(WFFeature f, WFNoise n) : base(f, n)
            {
                Vector3 a = f.points[0], b = f.points.Count > 1 ? f.points[1] : f.points[0] + Vector3.right * 6;
                mid = (a + b) * .5f; var ab = b - a; ab.y = 0; R = ab.magnitude * .5f; u = ab.normalized; w = Side(u); H = f.height > 0 ? f.height : R;
                Box = new Bounds(mid + Vector3.up * H * .5f, new Vector3(2 * R, H, 2 * R) + Vector3.one * (f.width + 2 * Margin));
            }
            public override float Sdf(Vector3 p)
            {
                var q = p - mid; float x = Vector3.Dot(q, u), y = q.y, z = Vector3.Dot(q, w);
                float rr = Mathf.Sqrt(x * x + y * R / H * (y * R / H));
                float tor = new Vector2(rr - R, z).magnitude - F.width * .5f;
                return Mathf.Max(tor, -y - .4f) - Rough(p);
            }
        }

        /// <summary>ruin: broken columns every spacing m along the points (radius thick, up to height tall), low walls
        /// between some of them.</summary>
        sealed class Ruin : Feat
        {
            readonly List<Vector3> cols = new List<Vector3>(); readonly List<float> hs = new List<float>();
            public Ruin(WFFeature f, WFNoise n) : base(f, n)
            {
                Sp = new WFSpline(f.points, f.curvature, n);
                int k = Mathf.Max(1, Mathf.FloorToInt(Sp.Length / Mathf.Max(.5f, f.spacing)) + 1);
                for (int i = 0; i < k; i++)
                {
                    float s = i * f.spacing; var c = Sp.At(s) + Side(Sp.DirAt(s)) * (WFNoise.Hash(i, f.seed) - .5f) * .6f;
                    cols.Add(c); hs.Add(f.height * (.3f + .7f * WFNoise.Hash(i + 91, f.seed)));
                }
                Box = Sp.Box; Box.Expand(new Vector3(f.radius * 4 + 2 * Margin, 2 * f.height + 2 * Margin, f.radius * 4 + 2 * Margin));
            }
            public override float Sdf(Vector3 p)
            {
                float d = 1e4f;
                for (int i = 0; i < cols.Count; i++)
                {
                    var c = cols[i]; var o = p - c; float h = hs[i] + .4f * N.Noise(p.x * 1.3f, i, p.z * 1.3f);
                    float col = Mathf.Max(new Vector2(o.x, o.z).magnitude - F.radius, Mathf.Max(p.y - (c.y + h), c.y - .6f - p.y));
                    d = Mathf.Min(d, col);
                    if (i + 1 < cols.Count && WFNoise.Hash(i + 7, F.seed) > .4f)
                    {
                        var a = cols[i]; var b = cols[i + 1]; var ab = b - a; ab.y = 0; var ap = p - a; ap.y = 0;
                        float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                        float wallH = Mathf.Min(hs[i], hs[i + 1]) * (.35f + .3f * WFNoise.Hash(i + 3, F.seed)) + .3f * N.Noise(p.x * 2, p.z * 2, i);
                        float y0 = Mathf.Lerp(a.y, b.y, t);
                        float wall = Mathf.Max((ap - ab * t).magnitude - .28f, Mathf.Max(p.y - (y0 + wallH), y0 - .6f - p.y));
                        d = Mathf.Min(d, wall);
                    }
                }
                return d - .06f * N.Fbm(p * 3, 2);
            }
            public override void Paint(Vector3 p, Vector3 n, ref float rock, ref float moss, ref float dirt, ref float wet, ref float ruin)
            {
                ruin = Mathf.Max(ruin, Mathf.Clamp01(1 - Sdf(p) / .25f));
            }
        }

        /// <summary>boulders: count rocks (radius, varied) scattered beside the points (width apart).</summary>
        sealed class Boulders : Feat
        {
            readonly List<Vector4> rocks = new List<Vector4>();
            public Boulders(WFFeature f, WFNoise n) : base(f, n)
            {
                Sp = new WFSpline(f.points, f.curvature, n);
                int k = f.count > 0 ? f.count : Mathf.Max(1, Mathf.FloorToInt(Sp.Length / Mathf.Max(.5f, f.spacing)));
                for (int i = 0; i < k; i++)
                {
                    float s = WFNoise.Hash(i, f.seed) * Sp.Length, side = WFNoise.Hash(i + 50, f.seed) < .5f ? -1 : 1;
                    float r = f.radius * (.5f + WFNoise.Hash(i + 99, f.seed));
                    var c = Sp.At(s) + Side(Sp.DirAt(s)) * side * (f.width * .5f + r * .6f + WFNoise.Hash(i + 7, f.seed) * 1.5f) + Vector3.up * r * .15f;
                    rocks.Add(new Vector4(c.x, c.y, c.z, r));
                }
                Box = Sp.Box; Box.Expand(new Vector3(f.width + 4 * f.radius + 4, 4 * f.radius + 2, f.width + 4 * f.radius + 4));
            }
            public override float Sdf(Vector3 p)
            {
                float d = 1e4f;
                foreach (var r in rocks)
                {
                    var o = p - (Vector3)r; float m = o.magnitude;
                    if (m - r.w > 1.5f) { d = Mathf.Min(d, m - r.w); continue; }
                    d = Mathf.Min(d, new Vector3(o.x, o.y * 1.35f, o.z).magnitude - r.w - r.w * .35f * N.Fbm(p * (1.2f / Mathf.Max(.3f, r.w)), 3));
                }
                return d;
            }
        }

        /// <summary>path: the ground levelled to the points' height, width wide (cut through, filled over), with height m of
        /// head room cut above; its floor a little uneven; dirt.</summary>
        sealed class PathF : Feat
        {
            public PathF(WFFeature f, WFNoise n) : base(f, n)
            {
                Sp = new WFSpline(f.points, f.curvature, n);
                Box = Sp.Box; Box.Expand(new Vector3(f.width + 2 * f.blend + 4, 2 * Mathf.Max(f.height, 4) + 12, f.width + 2 * f.blend + 4));
            }
            float Edge(Vector3 p, out Vector3 q)
            {
                Sp.Closest(p, true, out q, out _, out _, out float beyond);
                var o = p - q; o.y = 0;
                return Mathf.Max(o.magnitude - F.width * .5f, beyond);
            }
            public override float Sdf(Vector3 p) { float e = Edge(p, out var q); return Mathf.Max(e, p.y - q.y); }
            public override float Apply(float f, Vector3 p)
            {
                float e = Edge(p, out var q);
                float y = q.y + F.floor * N.Fbm(new Vector3(p.x * .8f, 0, p.z * .8f), 3);
                float slab = Mathf.Max(e, p.y - y);
                float room = Mathf.Max(e, Mathf.Max(y - p.y, p.y - (y + Mathf.Max(2.2f, F.height))));
                f = SMin(f, slab, F.blend);
                return SMax(f, -room, F.blend);
            }
            public override void Paint(Vector3 p, Vector3 n, ref float rock, ref float moss, ref float dirt, ref float wet, ref float ruin)
            {
                float e = Edge(p, out _);
                float on = Mathf.Clamp01(1 - (e + F.width * .5f) / (F.width * .5f + .9f)) * Mathf.Clamp01(n.y * 1.5f);
                dirt += on * 1.6f; moss *= 1 - on; rock *= 1 - on * .6f;
            }
        }

        /// <summary>crater: a bowl radius wide and depth deep at points[0] (its rim level), with a raised rim width thick.</summary>
        sealed class Crater : Feat
        {
            readonly Vector3 c;
            public Crater(WFFeature f, WFNoise n) : base(f, n)
            {
                c = f.points.Count > 0 ? f.points[0] : Vector3.zero;
                Box = new Bounds(c, new Vector3(2 * f.radius + 2 * f.width, 2 * f.depth + f.height, 2 * f.radius + 2 * f.width) + Vector3.one * 2 * Margin);
            }
            public override float Sdf(Vector3 p)
            {
                var q = p - c; float rxz = new Vector2(q.x, q.z).magnitude;
                return (Mathf.Sqrt(rxz * rxz / (F.radius * F.radius) + q.y * q.y / (F.depth * F.depth)) - 1) * Mathf.Min(F.radius, F.depth) - Rough(p);
            }
            public override float Apply(float f, Vector3 p)
            {
                var q = p - c; float rxz = new Vector2(q.x, q.z).magnitude;
                float rim = new Vector2((rxz - F.radius) * .8f, q.y / Mathf.Max(.3f, F.height > 0 ? F.height : 1f)).magnitude - F.width * .5f - Rough(p);
                f = SMin(f, rim, F.blend);
                return SMax(f, -Sdf(p), F.blend * .5f);
            }
        }

        /// <summary>tunnel, cave: a tube along the points (they are its floor line), width wide and height tall, varying
        /// along its length, its mouth flared, its walls rough and layered, its floor flat but uneven.</summary>
        sealed class Tube : Feat
        {
            public Tube(WFFeature f, WFNoise n) : base(f, n)
            {
                Sp = new WFSpline(f.points, f.curvature, n);
                float g = Mathf.Max(f.width, f.height) * (1 + f.flare) + 2 * Margin;
                Box = Sp.Box; Box.Expand(new Vector3(g * 1.4f, g * 1.4f, g * 1.4f));
            }
            public override float Sdf(Vector3 p)
            {
                Sp.Closest(p, false, out var q, out float s, out var t, out float beyond);
                var side = Side(t);
                float vary = 1 + .2f * N.Noise(s * .11f, 1.7f, 3.1f);
                float fl = Mathf.Max(1, F.width * 1.6f);
                float flare = s < fl ? 1 + F.flare * (1 - s / fl) * (1 - s / fl) : 1;
                float rw = F.width * .5f * vary * flare, rh = F.height * .5f * vary * Mathf.Lerp(1, flare, .7f);
                var o = p - q;
                float ox = Vector3.Dot(o, side), oy = p.y - (q.y + .55f * rh), oz = beyond;
                float e = Mathf.Sqrt(ox * ox / (rw * rw) + oy * oy / (rh * rh) + oz * oz / (rw * rw));
                float d = (e - 1) * Mathf.Min(rw, rh) + Rough(p) + Strata(p);
                float floorY = q.y + F.floor * N.Fbm(new Vector3(p.x * .7f, 5.5f, p.z * .7f), 3);
                return Mathf.Max(d, floorY - p.y);
            }
            public override void Paint(Vector3 p, Vector3 n, ref float rock, ref float moss, ref float dirt, ref float wet, ref float ruin)
            {
                float near = Mathf.Clamp01(1 - Mathf.Abs(Sdf(p)) / 1.2f);
                rock += near * 1.5f; moss *= 1 - near; wet += near * .25f;
                if (n.y > .6f) { dirt += near * .5f; wet += near * .15f; }
            }
        }

        /// <summary>chamber: a domed room on a floor at points[0]: radius across, height up, length along (default radius).</summary>
        sealed class Chamber : Feat
        {
            readonly Vector3 c, r;
            public Chamber(WFFeature f, WFNoise n) : base(f, n)
            {
                c = f.points.Count > 0 ? f.points[0] : Vector3.zero;
                r = new Vector3(f.radius, f.height, f.length > 0 ? f.length : f.radius);
                Box = new Bounds(c + Vector3.up * f.height * .45f, 2 * r + Vector3.one * 2 * Margin);
            }
            public override float Sdf(Vector3 p)
            {
                var q = p - (c + Vector3.up * F.height * .45f);
                float d = (new Vector3(q.x / r.x, q.y / r.y, q.z / r.z).magnitude - 1) * Mathf.Min(r.x, Mathf.Min(r.y, r.z)) + Rough(p) + Strata(p);
                float floorY = c.y + F.floor * N.Fbm(new Vector3(p.x * .6f, 9.5f, p.z * .6f), 3);
                return Mathf.Max(d, floorY - p.y);
            }
            public override void Paint(Vector3 p, Vector3 n, ref float rock, ref float moss, ref float dirt, ref float wet, ref float ruin)
            {
                float near = Mathf.Clamp01(1 - Mathf.Abs(Sdf(p)) / 1.2f);
                rock += near * 1.5f; moss *= 1 - near; wet += near * .2f;
                if (n.y > .6f) dirt += near * .6f;
            }
        }

        /// <summary>canyon, river, ravine, crevice: a channel cut along the points (its rim line), width across the top and
        /// depth deep; canyons have steep walls and a flat floor, rivers a U, ravines and crevices a V.</summary>
        sealed class Channel : Feat
        {
            readonly float exp;
            public Channel(WFFeature f, WFNoise n, float exp) : base(f, n)
            {
                this.exp = exp; Sp = new WFSpline(f.points, f.curvature, n);
                Box = Sp.Box; Box.Expand(new Vector3(f.width + 2 * Margin + 2, 2 * f.depth + 2 * Margin + 20, f.width + 2 * Margin + 2));
            }
            public override float Sdf(Vector3 p)
            {
                Sp.Closest(p, true, out var q, out _, out _, out float beyond);
                var o = p - q; o.y = 0; float dh = o.magnitude, w = F.width * .5f;
                float bottom = q.y - F.depth * (1 - Mathf.Pow(Mathf.Clamp01(dh / w), exp));
                return Mathf.Max(Mathf.Max(dh - w, beyond), bottom - p.y) + Rough(p) + Strata(p);
            }
            public override void Paint(Vector3 p, Vector3 n, ref float rock, ref float moss, ref float dirt, ref float wet, ref float ruin)
            {
                float near = Mathf.Clamp01(1 - Mathf.Abs(Sdf(p)) / 1.5f);
                rock += near * (1 - n.y); dirt += near * n.y * .6f;
                if (F.type == "river") wet += near * .7f;
            }
        }

        /// <summary>hole: a shaft radius wide from points[0] to points[1] (or depth straight down).</summary>
        sealed class Hole : Feat
        {
            readonly Vector3 a, b;
            public Hole(WFFeature f, WFNoise n) : base(f, n)
            {
                a = f.points.Count > 0 ? f.points[0] : Vector3.zero; b = f.points.Count > 1 ? f.points[1] : a - Vector3.up * f.depth;
                Box = new Bounds((a + b) * .5f, new Vector3(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y), Mathf.Abs(a.z - b.z)) + Vector3.one * (2 * f.radius + 2 * Margin));
            }
            public override float Sdf(Vector3 p)
            {
                var ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                return Vector3.Distance(p, a + ab * t) - F.radius * (1 + .25f * N.Noise(t * 6, 2.2f, 4.4f)) + Rough(p);
            }
        }
    }
}
