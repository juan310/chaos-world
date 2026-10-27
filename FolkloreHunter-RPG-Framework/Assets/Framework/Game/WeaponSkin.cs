using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FH.Game
{
    /// <summary>The hunter's starting weapon set (Docs/Concepts/Weapons-0.9/starting-weapon-set.png), made into models
    /// on this PC (Models/Weapons/Weapon_*: Forge static jobs; the bow and the katana modelled in code by
    /// Tools/slim/craft_weapons.py), worn in place of the KayKit pieces: each new mesh is
    /// fitted onto the old one's box (same length, so grips, sheathing and the blade sweep keep working) and turned by
    /// its shape: the sword's hilt to the hand, the spear's head away from it, the bow's grip to the hand with the string
    /// toward the archer, the shields' bosses outward (the kite's point down), the quiver's fletchings up.
    /// A piece whose model is not in the build stays as it was.</summary>
    public static class WeaponSkin
    {
        enum Kind { Blade, Pole, Bow, Shield, Quiver }
        static readonly (string piece, string model, Kind kind)[] Set =
        {
            ("1H_Sword", "Weapon_Sword", Kind.Blade), ("Round_Shield", "Weapon_RoundShield", Kind.Shield),
            ("Hunt_Bow", "Weapon_Bow", Kind.Bow), ("Hunt_Quiver", "Weapon_Quiver", Kind.Quiver),
            ("Hunt_Lance", "Weapon_Spear", Kind.Pole), ("Hunt_TowerShield", "Weapon_KiteShield", Kind.Shield),
            ("Hunt_Katana", "Weapon_Katana", Kind.Blade),
        };
        static readonly Dictionary<string, Mesh> combined = new Dictionary<string, Mesh>();
        public static readonly List<string> Applied = new List<string>();

        /// <summary>Swaps every piece that has a new model, on a freshly made hunter visual (before the weapon layer works
        /// out its grips and back positions), or on a slim skeleton's own sword, shield or lance (<paramref name="record"/>
        /// false: the hunter's list of swapped pieces is left alone). <paramref name="hand"/> is the right hand slot.</summary>
        public static void Apply(Transform visual, Transform hand, bool record = true)
        {
            if (record) Applied.Clear();
            foreach (var (piece, model, kind) in Set)
            {
                var t = WorldKit.Find(visual, piece);
                var mf = t ? t.GetComponent<MeshFilter>() : null;
                if (!mf || !mf.sharedMesh) continue;
                var src = Source(model);
                if (!src) continue;
                var grip = kind == Kind.Bow ? WorldKit.Find(visual, "handslot.l") ?? hand : kind == Kind.Shield ? WorldKit.Find(visual, "handslot.l") ?? hand : hand;
                mf.sharedMesh = Fit(src, mf.sharedMesh, t, grip, kind);
                var tex = Resources.Load<Texture2D>("Models/Weapons/" + model + "_texture");
                var r = t.GetComponent<Renderer>();
                if (r && tex)
                {
                    var m = new Material(Shader.Find("Standard")) { name = model, mainTexture = tex };
                    m.SetFloat("_Glossiness", kind == Kind.Blade || kind == Kind.Pole ? .45f : .2f);
                    m.SetFloat("_Metallic", kind == Kind.Blade ? .35f : .05f);
                    r.sharedMaterials = Enumerable.Repeat(m, 1).ToArray();
                }
                if (record) Applied.Add(piece);
            }
        }

        /// <summary>The new model as one mesh in its own root space.</summary>
        static Mesh Source(string model)
        {
            if (combined.TryGetValue(model, out var cached)) return cached;
            var prefab = Resources.Load<GameObject>("Models/Weapons/" + model);
            if (!prefab) { combined[model] = null; return null; }
            var parts = prefab.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh).ToArray();
            if (parts.Length == 0) { combined[model] = null; return null; }
            var ci = parts.Select(f => new CombineInstance { mesh = f.sharedMesh, transform = prefab.transform.worldToLocalMatrix * f.transform.localToWorldMatrix }).ToArray();
            var mesh = new Mesh { name = model + " (combined)", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(ci, true, true);
            combined[model] = mesh;
            return mesh;
        }

        static int[] AxesBySize(Vector3 size)
        {
            var a = new[] { 0, 1, 2 };
            return a.OrderByDescending(i => size[i]).ToArray();      // long, middle, thin
        }

        static Vector3 Unit(int axis, float sign) { var v = Vector3.zero; v[axis] = sign; return v; }

        /// <summary>Cross-section extent of the vertices whose coordinate along `axis` lies in [from, to] (0 = min end).</summary>
        static float Width(Vector3[] v, Bounds b, int axis, float from, float to)
        {
            float lo = b.min[axis] + b.size[axis] * from, hi = b.min[axis] + b.size[axis] * to, w = 0;
            int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
            float min1 = float.MaxValue, max1 = float.MinValue, min2 = float.MaxValue, max2 = float.MinValue;
            foreach (var p in v)
            {
                if (p[axis] < lo || p[axis] > hi) continue;
                min1 = Mathf.Min(min1, p[a1]); max1 = Mathf.Max(max1, p[a1]); min2 = Mathf.Min(min2, p[a2]); max2 = Mathf.Max(max2, p[a2]);
            }
            if (min1 <= max1) w = Mathf.Max(max1 - min1, max2 - min2);
            return w;
        }

        /// <summary>+1 when the end of `axis` at its max is the one asked for, -1 when it is the min end.</summary>
        static float WiderEnd(Vector3[] v, Bounds b, int axis, float from, float to)
            => Width(v, b, axis, 1 - to, 1 - from) >= Width(v, b, axis, from, to) ? 1 : -1;

        static Mesh Fit(Mesh src, Mesh old, Transform piece, Transform grip, Kind kind)
        {
            var nv = src.vertices; var nb = src.bounds; var ob = old.bounds; var ov = old.vertices;
            int[] na = AxesBySize(nb.size), oa = AxesBySize(ob.size);
            Vector3 hand = grip ? piece.InverseTransformPoint(grip.position) : ob.center;
            Vector3 upOld = piece.InverseTransformDirection(Vector3.up);
            // Decide, for two of the new mesh's axes, which old axis (and sign) each goes to.
            Vector3 nLong = Unit(na[0], 1), nMid = Unit(na[1], 1);
            Vector3 oLong = Unit(oa[0], 1), oMid = Unit(oa[1], 1);
            switch (kind)
            {
                case Kind.Blade:       // hilt (the cross-guard's band) toward the hand
                    nLong *= WiderEnd(nv, nb, na[0], .08f, .32f);
                    oLong *= Mathf.Sign(hand[oa[0]] - ob.center[oa[0]] + 1e-6f);
                    break;
                case Kind.Pole:        // the head (the wider end) away from the hand
                    nLong *= WiderEnd(nv, nb, na[0], 0, .25f);
                    oLong *= -Mathf.Sign(hand[oa[0]] - ob.center[oa[0]] + 1e-6f);
                    break;
                case Kind.Quiver:      // fletchings (the wider end) up
                    nLong *= WiderEnd(nv, nb, na[0], 0, .2f);
                    oLong *= Mathf.Sign(upOld[oa[0]] + 1e-6f);
                    break;
                case Kind.Bow:         // grip side (where the wood is, not the string) toward the hand
                {
                    float mean = nv.Average(p => p[na[1]]) - nb.center[na[1]];
                    nMid *= Mathf.Sign(mean + 1e-6f);
                    oMid *= Mathf.Sign(hand[oa[1]] - ob.center[oa[1]] + 1e-6f);
                    break;
                }
                case Kind.Shield:      // the face: the thinnest axis; the boss (the centre's furthest point) away from the forearm
                {
                    int nt = na[2], ot = oa[2];
                    float c1 = nb.center[(nt + 1) % 3], c2 = nb.center[(nt + 2) % 3], rr = Mathf.Min(nb.size[(nt + 1) % 3], nb.size[(nt + 2) % 3]) * .2f;
                    float far = 0;
                    foreach (var p in nv)
                        if (Mathf.Abs(p[(nt + 1) % 3] - c1) < rr && Mathf.Abs(p[(nt + 2) % 3] - c2) < rr && Mathf.Abs(p[nt] - nb.center[nt]) > Mathf.Abs(far)) far = p[nt] - nb.center[nt];
                    nMid = Unit(nt, Mathf.Sign(far + 1e-6f));                                   // outer face of the new shield
                    oMid = Unit(ot, -Mathf.Sign(hand[ot] - ob.center[ot] + 1e-6f));              // away from the forearm
                    // and its tall axis: a kite's point (the narrower end) down
                    nLong *= -WiderEnd(nv, nb, na[0], 0, .2f);                                   // the narrow end
                    oLong *= -Mathf.Sign(upOld[oa[0]] + 1e-6f);                                  // the lower end
                    break;
                }
            }
            // Rotation taking (nLong, nMid) onto (oLong, oMid), right-handed (no mirroring).
            var rot = Quaternion.LookRotation(oLong, oMid) * Quaternion.Inverse(Quaternion.LookRotation(nLong, nMid));
            float k = kind == Kind.Shield ? Mathf.Max(ob.size[oa[0]], ob.size[oa[1]]) / Mathf.Max(1e-4f, Mathf.Max(nb.size[na[0]], nb.size[na[1]]))
                                          : ob.size[oa[0]] / Mathf.Max(1e-4f, nb.size[na[0]]);
            var outV = new Vector3[nv.Length];
            for (int i = 0; i < nv.Length; i++) outV[i] = rot * ((nv[i] - nb.center) * k);
            var fitted = new Bounds(outV[0], Vector3.zero); foreach (var p in outV) fitted.Encapsulate(p);
            Vector3 shift = ob.center - fitted.center;
            if (kind == Kind.Shield)
            {
                // keep the inner face where the old one was, so the forearm does not go through the new shield
                int ot = oa[2]; float inner = -oMid[ot];
                shift[ot] = (inner > 0 ? ob.max[ot] - fitted.max[ot] : ob.min[ot] - fitted.min[ot]);
            }
            for (int i = 0; i < outV.Length; i++) outV[i] += shift;
            var n = src.normals; var outN = new Vector3[n.Length];
            for (int i = 0; i < n.Length; i++) outN[i] = rot * n[i];
            var mesh = new Mesh { name = old.name + " (" + src.name + ")", indexFormat = src.indexFormat };
            mesh.vertices = outV; mesh.normals = outN; mesh.uv = src.uv; mesh.triangles = src.triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
