using System.Threading.Tasks;
using UnityEngine;

namespace FH.WorldForge
{
    /// <summary>Seamless surface textures made from noise (nothing downloaded or bought): rock (layered, cracked), dirt
    /// (with pebbles), moss, ruin stone (bricks), each with a height in alpha and a normal map; water ripple normals; and
    /// the particle sprites (soft glow, flame, smoke puff, spark, rain streak). Colours from the recipe's palette.</summary>
    public static class WFTextures
    {
        public sealed class Set
        {
            public Texture2D Rock, RockN, Dirt, DirtN, Moss, MossN, Ruin, RuinN, WaterN, Glow, Flame, Smoke, Spark, Streak;
        }

        public static Set Make(WFPalette pal, int seed, int size = 512)
        {
            var n = new WFNoise(seed * 13 + 5);
            var s = new Set();
            s.Rock = Surface(size, n, out s.RockN, 3.2f, (u, v) =>
            {
                float layers = n.PeriodicFbm(u, v * .35f, 2, 3, 3.1f);
                float big = n.PeriodicFbm(u, v, 4, 5, 1.7f), crack = n.PeriodicFbm(u, v, 3, 4, 9.1f, true);
                float h = .55f + .3f * big + .12f * layers - .35f * Mathf.Pow(Mathf.Clamp01(crack), 6);
                float tone = .85f + .3f * big + .12f * Mathf.Sin((v + layers * .3f) * Mathf.PI * 8);
                var c = pal.rock * tone; c = Color.Lerp(c, c * .55f, Mathf.Pow(Mathf.Clamp01(crack), 6));
                return new Vector4(c.r, c.g, c.b, Mathf.Clamp01(h));
            });
            s.Dirt = Surface(size, n, out s.DirtN, 2.2f, (u, v) =>
            {
                float base_ = n.PeriodicFbm(u, v, 6, 5, 4.4f);
                float peb = Pebbles(u, v, 24, seed);
                float h = .45f + .2f * base_ + .45f * peb;
                var c = pal.dirt * (.9f + .25f * base_);
                c = Color.Lerp(c, pal.rock * 1.05f, peb * .8f);
                return new Vector4(c.r, c.g, c.b, Mathf.Clamp01(h));
            });
            s.Moss = Surface(size, n, out s.MossN, 1.6f, (u, v) =>
            {
                float a = n.PeriodicFbm(u, v, 8, 5, 6.6f), b = n.PeriodicFbm(u, v, 32, 3, 2.2f);
                float h = .5f + .3f * a + .2f * b;
                var c = pal.moss * (.8f + .35f * a + .2f * b);
                c = Color.Lerp(c, new Color(c.r * 1.4f, c.g * 1.25f, c.b * .7f), Mathf.Clamp01(b * 1.5f));
                return new Vector4(c.r, c.g, c.b, Mathf.Clamp01(h));
            });
            s.Ruin = Surface(size, n, out s.RuinN, 3.5f, (u, v) =>
            {
                const int rows = 4; float y = v * rows; int row = Mathf.FloorToInt(y);
                float x = u * 2 + (row % 2) * .5f; int col = Mathf.FloorToInt(x);
                float fx = x - col, fy = y - row;
                float mortar = Mathf.Min(Mathf.Min(fx, 1 - fx) * 2, Mathf.Min(fy, 1 - fy)) ;
                float edge = Mathf.SmoothStep(0, .06f, mortar + .02f * n.PeriodicFbm(u, v, 16, 3, 1.3f));
                float var = WFNoise.Hash(col % 2, row) * .25f + WFNoise.Hash(col + 9, row + 3) * .2f;
                float grain = n.PeriodicFbm(u, v, 12, 4, 8.8f);
                float h = edge * (.6f + .25f * grain);
                var c = pal.ruin * (.8f + var + .2f * grain) * Mathf.Lerp(.55f, 1, edge);
                return new Vector4(c.r, c.g, c.b, Mathf.Clamp01(h));
            });
            s.WaterN = NormalOnly(256, n, 1.2f, (u, v) => .5f + .5f * n.PeriodicFbm(u, v, 6, 4, 2.9f));
            s.Glow = Sprite(64, (x, y) => { float r = Mathf.Sqrt(x * x + y * y); float a = Mathf.Clamp01(1 - r); return new Color(1, 1, 1, a * a); });
            s.Flame = Sprite(64, (x, y) =>
            {
                float yy = (y + 1) * .5f; float w = Mathf.Lerp(.75f, .05f, yy * yy);
                float a = Mathf.Clamp01(1 - Mathf.Abs(x) / Mathf.Max(.05f, w)) * Mathf.Clamp01((1 - yy) * 3) * Mathf.Clamp01(yy * 6);
                a *= .75f + .25f * n.Noise(x * 4, y * 4, 1.5f);
                return new Color(1, 1, 1, a);
            });
            s.Smoke = Sprite(64, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y); float a = Mathf.Clamp01(1 - r) * (.6f + .4f * n.Fbm(new Vector3(x * 2.5f, y * 2.5f, 3.3f), 4));
                return new Color(1, 1, 1, Mathf.Clamp01(a * 1.2f));
            });
            s.Spark = Sprite(32, (x, y) => { float r = Mathf.Sqrt(x * x * 1.0f + y * y); float a = Mathf.Clamp01(1 - r * 1.2f); return new Color(1, 1, 1, Mathf.Pow(a, 3)); });
            s.Streak = Sprite(32, (x, y) => { float a = Mathf.Clamp01(1 - Mathf.Abs(x) * 3.5f) * Mathf.Clamp01(1 - Mathf.Abs(y)); return new Color(1, 1, 1, a * a); });
            return s;
        }

        delegate Vector4 Sampler(float u, float v);

        /// <summary>Colour (rgb) and height (a) from f, and a normal map from the height (strength: bumpiness).</summary>
        static Texture2D Surface(int size, WFNoise n, out Texture2D normal, float strength, Sampler f)
        {
            var px = new Vector4[size * size];
            Parallel.For(0, size, y => { for (int x = 0; x < size; x++) px[x + y * size] = f((x + .5f) / size, (y + .5f) / size); });
            var col = new Color[size * size]; var nrm = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = px[x + y * size]; col[x + y * size] = new Color(p.x, p.y, p.z, p.w);
                    float hl = px[((x - 1 + size) % size) + y * size].w, hr = px[((x + 1) % size) + y * size].w;
                    float hd = px[x + ((y - 1 + size) % size) * size].w, hu = px[x + ((y + 1) % size) * size].w;
                    var nv = new Vector3((hl - hr) * strength, (hd - hu) * strength, 1).normalized;
                    nrm[x + y * size] = new Color(nv.x * .5f + .5f, nv.y * .5f + .5f, nv.z * .5f + .5f, 1);
                }
            normal = Tex(size, nrm, true);
            return Tex(size, col, false);
        }

        static Texture2D NormalOnly(int size, WFNoise n, float strength, System.Func<float, float, float> h)
        {
            var hh = new float[size * size];
            Parallel.For(0, size, y => { for (int x = 0; x < size; x++) hh[x + y * size] = h((x + .5f) / size, (y + .5f) / size); });
            var nrm = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float hl = hh[((x - 1 + size) % size) + y * size], hr = hh[((x + 1) % size) + y * size];
                    float hd = hh[x + ((y - 1 + size) % size) * size], hu = hh[x + ((y + 1) % size) * size];
                    var nv = new Vector3((hl - hr) * strength * size / 64f, (hd - hu) * strength * size / 64f, 1).normalized;
                    nrm[x + y * size] = new Color(nv.x * .5f + .5f, nv.y * .5f + .5f, nv.z * .5f + .5f, 1);
                }
            return Tex(size, nrm, true);
        }

        static Texture2D Sprite(int size, System.Func<float, float, Color> f)
        {
            var c = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) c[x + y * size] = f((x + .5f) / size * 2 - 1, (y + .5f) / size * 2 - 1);
            var t = Tex(size, c, true); t.wrapMode = TextureWrapMode.Clamp; return t;
        }

        static Texture2D Tex(int size, Color[] c, bool linear)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, linear) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.SetPixels(c); t.Apply(true, false);
            return t;
        }

        /// <summary>Round pebbles on a seamless tile: about count per row; 0 between them, up to 1 on top of one.</summary>
        static float Pebbles(float u, float v, int count, int seed)
        {
            float x = u * count, y = v * count; int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y); float best = 0;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int gx = ((cx + i) % count + count) % count, gy = ((cy + j) % count + count) % count;
                    if (WFNoise.Hash(gx * 7 + seed, gy) > .45f) continue;
                    float px = cx + i + WFNoise.Hash(gx, gy + seed), py = cy + j + WFNoise.Hash(gy + 31, gx);
                    float r = .25f + .25f * WFNoise.Hash(gx + 5, gy + 9);
                    float d = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py)) / r;
                    best = Mathf.Max(best, Mathf.Clamp01(1 - d * d));
                }
            return Mathf.Sqrt(best);
        }
    }
}
