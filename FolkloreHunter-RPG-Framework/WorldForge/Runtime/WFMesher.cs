using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace FH.WorldForge
{
    /// <summary>The field to meshes: surface nets on a grid of voxel-sized cells (one vertex per cell the surface passes
    /// through, placed on the surface by two Newton steps along the field's gradient; one quad per grid edge the surface
    /// crosses), then cut into chunk-sized meshes. Normals come from the field's gradient, so chunk seams are invisible;
    /// every vertex carries its material (colour: rock, moss, dirt, wet), and in uv2 its ruin share and how much sky it
    /// sees (sky: 1 in the open, about 0 deep in a cave; the shader darkens ambient light by it).</summary>
    public static class WFMesher
    {
        public sealed class Result
        {
            public readonly List<Mesh> Meshes = new List<Mesh>();
            public readonly List<Bounds> ChunkBounds = new List<Bounds>();
            public int Vertices, Triangles, Samples;
            public float Seconds;
            public Bounds Domain;
        }

        public static Result Build(WFField field, Action<string> log = null)
        {
            var R = field.R; var t0 = DateTime.Now;
            float h = Mathf.Max(.15f, R.voxel);
            Vector3 mn = R.min, size = R.max - R.min;
            int nx = Mathf.CeilToInt(size.x / h), ny = Mathf.CeilToInt(size.y / h), nz = Mathf.CeilToInt(size.z / h);
            int sx = nx + 1, sy = ny + 1, sz = nz + 1;
            var val = new float[sx * sy * sz];
            Parallel.For(0, sz, k =>
            {
                for (int j = 0; j < sy; j++)
                    for (int i = 0; i < sx; i++)
                        val[i + sx * (j + sy * k)] = field.Eval(mn + new Vector3(i, j, k) * h);
            });
            log?.Invoke($"sampled {sx * sy * sz:N0} points ({nx}x{ny}x{nz} cells of {h:0.00} m) in {(DateTime.Now - t0).TotalSeconds:0.0} s");

            // vertices: one per cell the surface crosses
            int cells = nx * ny * nz;
            var cellPos = new Vector3[cells]; var cellNrm = new Vector3[cells]; var has = new bool[cells];
            int[,] edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
            Parallel.For(0, nz, k =>
            {
                var cv = new float[8]; var cp = new Vector3[8];
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        int neg = 0;
                        for (int c = 0; c < 8; c++)
                        {
                            int di = c & 1, dj = (c >> 1) & 1, dk = (c >> 2) & 1;
                            cv[c] = val[(i + di) + sx * ((j + dj) + sy * (k + dk))];
                            cp[c] = mn + new Vector3(i + di, j + dj, k + dk) * h;
                            if (cv[c] < 0) neg++;
                        }
                        if (neg == 0 || neg == 8) continue;
                        Vector3 sum = Vector3.zero; int n = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            float a = cv[edges[e, 0]], b = cv[edges[e, 1]];
                            if ((a < 0) == (b < 0)) continue;
                            sum += Vector3.Lerp(cp[edges[e, 0]], cp[edges[e, 1]], a / (a - b)); n++;
                        }
                        var p = sum / n; var lo = cp[0] - Vector3.one * h * .5f; var hi = cp[7] + Vector3.one * h * .5f;
                        Vector3 g = Vector3.up;
                        for (int it = 0; it < 2; it++)
                        {
                            float f = field.Eval(p); g = field.Gradient(p, h * .25f);
                            float gg = g.sqrMagnitude; if (gg < 1e-8f) break;
                            p -= g * (f / gg);
                            p = Vector3.Max(lo, Vector3.Min(hi, p));
                        }
                        int ci = i + nx * (j + ny * k);
                        cellPos[ci] = p; cellNrm[ci] = g.sqrMagnitude > 1e-8f ? g.normalized : Vector3.up; has[ci] = true;
                    }
            });
            var index = new int[cells]; var verts = new List<Vector3>(); var nrms = new List<Vector3>();
            for (int c = 0; c < cells; c++) { if (!has[c]) { index[c] = -1; continue; } index[c] = verts.Count; verts.Add(cellPos[c]); nrms.Add(cellNrm[c]); }

            // quads: one per grid edge with a sign change, joining the four cells round it
            var tris = new List<int>(verts.Count * 6);
            int Cell(int i, int j, int k) => (i < 0 || j < 0 || k < 0 || i >= nx || j >= ny || k >= nz) ? -1 : index[i + nx * (j + ny * k)];
            void Quad(int a, int b, int c, int d)
            {
                if (a < 0 || b < 0 || c < 0 || d < 0) return;
                // the shorter diagonal, each triangle facing the air (its normal along the field's gradient)
                if ((verts[a] - verts[c]).sqrMagnitude <= (verts[b] - verts[d]).sqrMagnitude) { Tri(a, b, c); Tri(a, c, d); }
                else { Tri(a, b, d); Tri(b, c, d); }
            }
            void Tri(int a, int b, int c)
            {
                var gn = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                if (gn.sqrMagnitude < 1e-12f) return;
                if (Vector3.Dot(gn, nrms[a] + nrms[b] + nrms[c]) >= 0) { tris.Add(a); tris.Add(b); tris.Add(c); }
                else { tris.Add(a); tris.Add(c); tris.Add(b); }
            }
            bool Cross(int i0, int j0, int k0, int i1, int j1, int k1) => (val[i0 + sx * (j0 + sy * k0)] < 0) != (val[i1 + sx * (j1 + sy * k1)] < 0);
            for (int k = 0; k < sz; k++)
                for (int j = 0; j < sy; j++)
                    for (int i = 0; i < sx; i++)
                    {
                        if (i < nx && Cross(i, j, k, i + 1, j, k)) Quad(Cell(i, j - 1, k - 1), Cell(i, j, k - 1), Cell(i, j, k), Cell(i, j - 1, k));
                        if (j < ny && Cross(i, j, k, i, j + 1, k)) Quad(Cell(i - 1, j, k - 1), Cell(i, j, k - 1), Cell(i, j, k), Cell(i - 1, j, k));
                        if (k < nz && Cross(i, j, k, i, j, k + 1)) Quad(Cell(i - 1, j - 1, k), Cell(i, j - 1, k), Cell(i, j, k), Cell(i - 1, j, k));
                    }
            log?.Invoke($"surface: {verts.Count:N0} vertices, {tris.Count / 3:N0} triangles");

            // materials and sky visibility per vertex
            var sky = SkyGrid(field, mn, size);
            var cols = new Color[verts.Count]; var uv2 = new Vector2[verts.Count];
            Parallel.For(0, verts.Count, v =>
            {
                cols[v] = field.Material(verts[v], nrms[v], out float ruin);
                uv2[v] = new Vector2(ruin, sky.Sample(verts[v] + nrms[v] * .5f));
            });

            // chunks
            var res = new Result { Samples = sx * sy * sz, Vertices = verts.Count, Triangles = tris.Count / 3, Domain = new Bounds(mn + size * .5f, size) };
            float ch = Mathf.Max(2, R.chunk);
            int cx = Mathf.CeilToInt(size.x / ch), cy = Mathf.CeilToInt(size.y / ch), cz = Mathf.CeilToInt(size.z / ch);
            var bins = new List<int>[cx * cy * cz];
            for (int t = 0; t < tris.Count; t += 3)
            {
                var c = (verts[tris[t]] + verts[tris[t + 1]] + verts[tris[t + 2]]) / 3f - mn;
                int bx = Mathf.Clamp((int)(c.x / ch), 0, cx - 1), by = Mathf.Clamp((int)(c.y / ch), 0, cy - 1), bz = Mathf.Clamp((int)(c.z / ch), 0, cz - 1);
                int b = bx + cx * (by + cy * bz);
                (bins[b] ??= new List<int>()).Add(t);
            }
            foreach (var bin in bins)
            {
                if (bin == null) continue;
                var map = new Dictionary<int, int>(); var mv = new List<Vector3>(); var mnrm = new List<Vector3>(); var mt = new List<Vector4>();
                var mc = new List<Color>(); var mu = new List<Vector2>(); var mu2 = new List<Vector2>(); var mtri = new List<int>(bin.Count * 3);
                foreach (int t in bin)
                    for (int e = 0; e < 3; e++)
                    {
                        int g = tris[t + e];
                        if (!map.TryGetValue(g, out int l))
                        {
                            l = mv.Count; map[g] = l;
                            var p = verts[g]; var n = nrms[g];
                            mv.Add(p); mnrm.Add(n); mc.Add(cols[g]); mu2.Add(uv2[g]); mu.Add(new Vector2(p.x, p.z) * .25f);
                            var tg = Vector3.Cross(n, Mathf.Abs(n.y) < .95f ? Vector3.up : Vector3.forward).normalized;
                            mt.Add(new Vector4(tg.x, tg.y, tg.z, 1));
                        }
                        mtri.Add(l);
                    }
                var m = new Mesh { name = "WorldForge chunk " + res.Meshes.Count };
                if (mv.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(mv); m.SetNormals(mnrm); m.SetTangents(mt); m.SetColors(mc); m.SetUVs(0, mu); m.SetUVs(1, mu2);
                m.SetTriangles(mtri, 0, true);
                res.Meshes.Add(m); res.ChunkBounds.Add(m.bounds);
            }
            res.Seconds = (float)(DateTime.Now - t0).TotalSeconds;
            log?.Invoke($"{res.Meshes.Count} chunks in {res.Seconds:0.0} s");
            return res;
        }

        /// <summary>How much of the sky each place sees: on a 2 m grid, 13 rays (straight up and two rings) marched
        /// through the field up to 30 m; the share that reach open air. Sampled trilinearly.</summary>
        public sealed class Sky
        {
            public float[] V; public Vector3 Min; public float Step; public int X, Y, Z;
            public float Sample(Vector3 p)
            {
                var g = (p - Min) / Step;
                int i = Mathf.Clamp(Mathf.FloorToInt(g.x), 0, X - 2), j = Mathf.Clamp(Mathf.FloorToInt(g.y), 0, Y - 2), k = Mathf.Clamp(Mathf.FloorToInt(g.z), 0, Z - 2);
                float u = Mathf.Clamp01(g.x - i), v = Mathf.Clamp01(g.y - j), w = Mathf.Clamp01(g.z - k);
                float At(int a, int b, int c) => V[(i + a) + X * ((j + b) + Y * (k + c))];
                float x00 = Mathf.Lerp(At(0, 0, 0), At(1, 0, 0), u), x10 = Mathf.Lerp(At(0, 1, 0), At(1, 1, 0), u);
                float x01 = Mathf.Lerp(At(0, 0, 1), At(1, 0, 1), u), x11 = Mathf.Lerp(At(0, 1, 1), At(1, 1, 1), u);
                return Mathf.Lerp(Mathf.Lerp(x00, x10, v), Mathf.Lerp(x01, x11, v), w);
            }
        }

        public static Sky SkyGrid(WFField field, Vector3 mn, Vector3 size, float step = 2f)
        {
            var s = new Sky { Min = mn, Step = step, X = Mathf.CeilToInt(size.x / step) + 1, Y = Mathf.CeilToInt(size.y / step) + 1, Z = Mathf.CeilToInt(size.z / step) + 1 };
            s.V = new float[s.X * s.Y * s.Z];
            var dirs = new List<Vector3> { Vector3.up };
            for (int a = 0; a < 6; a++) dirs.Add(Quaternion.Euler(0, a * 60, 0) * Quaternion.Euler(-60, 0, 0) * Vector3.forward);
            for (int a = 0; a < 6; a++) dirs.Add(Quaternion.Euler(0, a * 60 + 30, 0) * Quaternion.Euler(-28, 0, 0) * Vector3.forward);
            float top = mn.y + size.y;
            Parallel.For(0, s.Z, k =>
            {
                for (int j = 0; j < s.Y; j++)
                    for (int i = 0; i < s.X; i++)
                    {
                        var p0 = mn + new Vector3(i, j, k) * step;
                        if (field.Eval(p0) < -1.5f) { s.V[i + s.X * (j + s.Y * k)] = 0; continue; }
                        float open = 0;
                        foreach (var d in dirs)
                        {
                            float t = .6f; bool hit = false;
                            while (t < 30)
                            {
                                var p = p0 + d * t;
                                if (p.y > top) break;
                                float f = field.Eval(p);
                                if (f < 0) { hit = true; break; }
                                t += Mathf.Clamp(f * .8f, .35f, 2.5f);
                            }
                            if (!hit) open += d.y;     // weighted by how high the ray looks
                        }
                        float max = 0; foreach (var d in dirs) max += d.y;
                        s.V[i + s.X * (j + s.Y * k)] = open / max;
                    }
            });
            return s;
        }
    }
}
