using UnityEngine;

namespace FH.WorldForge
{
    /// <summary>Seeded gradient noise (improved Perlin) in 3D, with fractal sums: smooth (fbm) and ridged (sharp crests,
    /// for rock). Values about -1..1. Thread-safe (read-only after construction).</summary>
    public sealed class WFNoise
    {
        readonly int[] perm = new int[512];

        public WFNoise(int seed)
        {
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            var r = new System.Random(seed);
            for (int i = 255; i > 0; i--) { int j = r.Next(i + 1); (p[i], p[j]) = (p[j], p[i]); }
            for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
        }

        static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float Grad(int h, float x, float y, float z)
        {
            h &= 15;
            float u = h < 8 ? x : y, v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        public float Noise(float x, float y, float z)
        {
            int X = Mathf.FloorToInt(x), Y = Mathf.FloorToInt(y), Z = Mathf.FloorToInt(z);
            x -= X; y -= Y; z -= Z; X &= 255; Y &= 255; Z &= 255;
            float u = Fade(x), v = Fade(y), w = Fade(z);
            int A = perm[X] + Y, AA = perm[A] + Z, AB = perm[A + 1] + Z, B = perm[X + 1] + Y, BA = perm[B] + Z, BB = perm[B + 1] + Z;
            return Lerp(Lerp(Lerp(Grad(perm[AA], x, y, z), Grad(perm[BA], x - 1, y, z), u),
                             Lerp(Grad(perm[AB], x, y - 1, z), Grad(perm[BB], x - 1, y - 1, z), u), v),
                        Lerp(Lerp(Grad(perm[AA + 1], x, y, z - 1), Grad(perm[BA + 1], x - 1, y, z - 1), u),
                             Lerp(Grad(perm[AB + 1], x, y - 1, z - 1), Grad(perm[BB + 1], x - 1, y - 1, z - 1), u), v), w);
        }

        /// <summary>Noise that repeats every period units in x and y (for seamless textures).</summary>
        public float Periodic(float x, float y, float z, int period)
        {
            int X = Mathf.FloorToInt(x), Y = Mathf.FloorToInt(y), Z = Mathf.FloorToInt(z);
            x -= X; y -= Y; z -= Z;
            int X0 = ((X % period) + period) % period & 255, X1 = (((X + 1) % period) + period) % period & 255;
            int Y0 = ((Y % period) + period) % period & 255, Y1 = (((Y + 1) % period) + period) % period & 255;
            int Z0 = Z & 255, Z1 = (Z + 1) & 255;
            float u = Fade(x), v = Fade(y), w = Fade(z);
            int G(int a, int b, int c) => perm[perm[perm[a] + b] + c];
            return Lerp(Lerp(Lerp(Grad(G(X0, Y0, Z0), x, y, z), Grad(G(X1, Y0, Z0), x - 1, y, z), u),
                             Lerp(Grad(G(X0, Y1, Z0), x, y - 1, z), Grad(G(X1, Y1, Z0), x - 1, y - 1, z), u), v),
                        Lerp(Lerp(Grad(G(X0, Y0, Z1), x, y, z - 1), Grad(G(X1, Y0, Z1), x - 1, y, z - 1), u),
                             Lerp(Grad(G(X0, Y1, Z1), x, y - 1, z - 1), Grad(G(X1, Y1, Z1), x - 1, y - 1, z - 1), u), v), w);
        }

        /// <summary>Seamless fractal noise over a tile: u, v in 0..1, base cells per tile.</summary>
        public float PeriodicFbm(float u, float v, int cells, int octaves = 5, float z = 0, bool ridged = false)
        {
            float a = 1, s = 0, n = 0; int c = cells;
            for (int i = 0; i < octaves; i++)
            {
                float x = Periodic(u * c, v * c, z + i * 7.3f, c);
                s += a * (ridged ? 1 - 2 * Mathf.Abs(x) : x); n += a; a *= .5f; c *= 2;
            }
            return s / n;
        }

        public float Fbm(Vector3 p, int octaves = 4)
        {
            float a = 1, s = 0, n = 0;
            for (int i = 0; i < octaves; i++) { s += a * Noise(p.x, p.y, p.z); n += a; p = p * 2.03f + new Vector3(17.1f, 3.7f, 9.3f); a *= .5f; }
            return s / n;
        }

        /// <summary>Sharp crests and creases (rock faces); about -1..1.</summary>
        public float Ridged(Vector3 p, int octaves = 4)
        {
            float a = 1, s = 0, n = 0;
            for (int i = 0; i < octaves; i++) { s += a * (1 - 2 * Mathf.Abs(Noise(p.x, p.y, p.z))); n += a; p = p * 2.07f + new Vector3(5.3f, 11.9f, 2.1f); a *= .5f; }
            return s / n;
        }

        public float Fbm2(float x, float z, int octaves = 4) => Fbm(new Vector3(x, 37.17f, z), octaves);

        public static float Hash(int a, int b = 0)
        {
            unchecked
            {
                uint h = (uint)(a * 374761393 + b * 668265263);
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0x1000000;
            }
        }
    }
}
