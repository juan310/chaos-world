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
                if (kind == Kind.Bow) { if (mf.sharedMesh.subMeshCount > 1) BowString.Attach(t, mf); else BowString.AttachAtTips(t, mf); }
                var tex = Resources.Load<Texture2D>("Models/Weapons/" + model + "_texture");
                var r = t.GetComponent<Renderer>();
                if (r && tex)
                {
                    var m = new Material(Shader.Find("Standard")) { name = model, mainTexture = tex };
                    m.SetFloat("_Glossiness", kind == Kind.Blade || kind == Kind.Pole ? .45f : .2f);
                    m.SetFloat("_Metallic", kind == Kind.Blade ? .35f : .05f);
                    r.sharedMaterials = Enumerable.Repeat(m, mf.sharedMesh.subMeshCount).ToArray();
                }
                if (record) Applied.Add(piece);
            }
        }

        /// <summary>The new model as one mesh in its own root space.</summary>
        static Mesh Source(string model)
        {
            if (combined.TryGetValue(model, out var cached)) return cached;
            var made = Combined(model);
            // The Forge sword came with a hilt 40 % of its length, its grip twice a real one's thickness and a very wide
            // guard: the hilt is brought to a one-handed sword's proportions (the blade keeps its shape).
            if (made && model == "Weapon_Sword") made = ReshapeHilt(made, HiltShare, GripThickness, GuardWidth);
            combined[model] = made;
            return made;
        }

        /// <summary>Grip and pommel together as a share of the blade's length, the grip's and pommel's thickness kept, and
        /// the cross-guard's width kept, when the sword's hilt is reshaped.</summary>
        public const float HiltShare = .24f, GripThickness = .5f, GuardWidth = .72f;

        static Mesh Combined(string model)
        {
            var prefab = Resources.Load<GameObject>("Models/Weapons/" + model);
            if (!prefab) return null;
            var parts = prefab.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh).ToArray();
            if (parts.Length == 0) return null;
            CombineInstance Part(MeshFilter f) => new CombineInstance { mesh = f.sharedMesh, transform = prefab.transform.worldToLocalMatrix * f.transform.localToWorldMatrix };
            var mesh = new Mesh { name = model + " (combined)", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var strings = parts.Where(f => f.name.EndsWith("String")).ToArray();
            if (strings.Length == 0) { mesh.CombineMeshes(parts.Select(Part).ToArray(), true, true); return mesh; }
            // a bow's string kept apart (the last submesh): the game draws it back to the nocked arrow (BowString)
            var body = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; body.CombineMeshes(parts.Except(strings).Select(Part).ToArray(), true, true);
            var str = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; str.CombineMeshes(strings.Select(Part).ToArray(), true, true);
            mesh.CombineMeshes(new[] { new CombineInstance { mesh = body, transform = Matrix4x4.identity }, new CombineInstance { mesh = str, transform = Matrix4x4.identity } }, false, true);
            return mesh;
        }

        /// <summary>The hilt (everything on the pommel side of the cross-guard, the widest band) shortened to
        /// <paramref name="share"/> of the blade's length and thinned to <paramref name="thick"/> about the sword's centre
        /// line; the guard band narrowed to <paramref name="guard"/>. The blade is untouched.</summary>
        static Mesh ReshapeHilt(Mesh src, float share, float thick, float guard)
        {
            var v = src.vertices; var b = src.bounds; int ax = AxesBySize(b.size)[0];
            const int bands = 20; int best = 0; float widest = -1;
            for (int k = 0; k < bands; k++) { float w = Width(v, b, ax, k / (float)bands, (k + 1) / (float)bands); if (w > widest) { widest = w; best = k; } }
            // the guard band grows while its neighbours are nearly as wide (a guard can span two bands)
            int g0 = best, g1 = best;
            while (g0 > 0 && Width(v, b, ax, (g0 - 1) / (float)bands, g0 / (float)bands) > widest * .75f) g0--;
            while (g1 < bands - 1 && Width(v, b, ax, (g1 + 1) / (float)bands, (g1 + 2) / (float)bands) > widest * .75f) g1++;
            float lo = b.min[ax], len = b.size[ax];
            float gLo = lo + len * g0 / bands, gHi = lo + len * (g1 + 1) / bands;
            bool hiltAtMin = best < bands / 2;
            float blade = hiltAtMin ? b.max[ax] - gHi : gLo - lo, hilt = hiltAtMin ? gLo - lo : b.max[ax] - gHi;
            float s = Mathf.Min(1, share * blade / Mathf.Max(1e-5f, hilt));
            // the centre line: the middle of the guard band's cross-section
            Vector3 c = Vector3.zero; int n = 0;
            foreach (var p in v) if (p[ax] >= gLo && p[ax] <= gHi) { c += p; n++; }
            c = n > 0 ? c / n : b.center;
            var o = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i]; float t = p[ax];
                bool inHilt = hiltAtMin ? t < gLo : t > gHi, inGuard = t >= gLo && t <= gHi;
                if (inHilt || inGuard)
                {
                    float f = inHilt ? thick : guard;
                    for (int a = 0; a < 3; a++) if (a != ax) p[a] = c[a] + (p[a] - c[a]) * f;
                    if (inHilt) p[ax] = hiltAtMin ? gLo - (gLo - t) * s : gHi + (t - gHi) * s;
                }
                o[i] = p;
            }
            var m = Object.Instantiate(src); m.name = src.name + " (hilt reshaped)";
            m.vertices = o; m.RecalculateBounds();
            Debug.Log("FH_WEAPON " + src.name + ": hilt " + (hilt / len * 100).ToString("0") + " % of the length -> " + (hilt * s / (len - hilt + hilt * s) * 100).ToString("0") + " %, guard bands " + g0 + "-" + g1);
            return m;
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

        /// <summary>+1 when the cross-guard (the widest of twenty bands along `axis`) lies in the max half, -1 in the min
        /// half. (The two ends' widths are no guide: a long grip can be narrower than the blade.)</summary>
        static float GuardEnd(Vector3[] v, Bounds b, int axis)
        {
            int best = 0; float widest = -1;
            for (int k = 0; k < 20; k++) { float w = Width(v, b, axis, k / 20f, (k + 1) / 20f); if (w > widest) { widest = w; best = k; } }
            return best >= 10 ? 1 : -1;
        }

        /// <summary>For checks: how far the blade's cross-guard sits from the hand, as a share of the blade's length (a
        /// sword held the right way round has it at the hand: well under a half).</summary>
        public static float GuardFromHand(Transform piece, Transform hand)
        {
            var mf = piece ? piece.GetComponent<MeshFilter>() : null;
            if (!mf || !mf.sharedMesh || !hand) return -1;
            var v = mf.sharedMesh.vertices; var b = mf.sharedMesh.bounds; int axis = AxesBySize(b.size)[0];
            int best = 0; float widest = -1;
            for (int k = 0; k < 20; k++) { float w = Width(v, b, axis, k / 20f, (k + 1) / 20f); if (w > widest) { widest = w; best = k; } }
            var guard = b.center; guard[axis] = b.min[axis] + b.size[axis] * (best + .5f) / 20f;
            float length = (piece.TransformVector(Unit(axis, b.size[axis]))).magnitude;
            return Vector3.Distance(piece.TransformPoint(guard), hand.position) / Mathf.Max(1e-4f, length);
        }

        /// <summary>For review pictures: a held weapon's cross-guard (the widest band along its length; for a pole, where it
        /// is held), tip and butt end, in world space.</summary>
        public static bool WeaponPoints(Transform piece, Transform hand, out Vector3 guard, out Vector3 tip, out Vector3 butt)
        {
            guard = tip = butt = Vector3.zero;
            var mf = piece ? piece.GetComponent<MeshFilter>() : null;
            if (!mf || !mf.sharedMesh) return false;
            var v = mf.sharedMesh.vertices; var b = mf.sharedMesh.bounds; int axis = AxesBySize(b.size)[0];
            Vector3 lo = b.center, hi = b.center; lo[axis] = b.min[axis]; hi[axis] = b.max[axis];
            bool pole = piece.name.Contains("Lance");
            int best = 0; float widest = -1;
            for (int k = 0; k < 20; k++) { float w = Width(v, b, axis, k / 20f, (k + 1) / 20f); if (w > widest) { widest = w; best = k; } }
            bool hiltAtMin = pole ? (hand && Vector3.Distance(piece.TransformPoint(lo), hand.position) < Vector3.Distance(piece.TransformPoint(hi), hand.position)) : best < 10;
            var g = b.center; g[axis] = b.min[axis] + b.size[axis] * (best + .5f) / 20f;
            butt = piece.TransformPoint(hiltAtMin ? lo : hi); tip = piece.TransformPoint(hiltAtMin ? hi : lo);
            guard = pole ? (hand ? hand.position : butt) : piece.TransformPoint(g);
            return true;
        }

        /// <summary>For review pictures: a bow's two tips, the middle of its grip and the middle of its string (the string
        /// lies on the side of the bow's depth away from the wood's mass), in world space.</summary>
        public static bool BowPoints(Transform bow, out Vector3 tipA, out Vector3 tipB, out Vector3 grip, out Vector3 stringMid)
        {
            tipA = tipB = grip = stringMid = Vector3.zero;
            var mf = bow ? bow.GetComponent<MeshFilter>() : null;
            if (!mf || !mf.sharedMesh) return false;
            var v = mf.sharedMesh.vertices; var b = mf.sharedMesh.bounds; var ax = AxesBySize(b.size);
            int L = ax[0], D = ax[1];
            Vector3 a = b.center, c = b.center; a[L] = b.min[L]; c[L] = b.max[L];
            float mean = v.Average(p => p[D]) - b.center[D];
            var g = b.center; var s = b.center; s[D] = mean > 0 ? b.min[D] : b.max[D];      // the string: away from the wood
            g[D] = b.center[D] + mean;
            tipA = bow.TransformPoint(a); tipB = bow.TransformPoint(c); grip = bow.TransformPoint(g); stringMid = bow.TransformPoint(s);
            return true;
        }

        /// <summary>For review pictures: an arrow's nock (the end nearer <paramref name="hand"/>) and tip, in world space.</summary>
        public static bool ArrowPoints(Transform arrow, Transform hand, out Vector3 nock, out Vector3 tip)
        {
            nock = tip = Vector3.zero;
            var mf = arrow ? arrow.GetComponent<MeshFilter>() : null;
            if (!mf || !mf.sharedMesh) return false;
            var b = mf.sharedMesh.bounds; int L = AxesBySize(b.size)[0];
            Vector3 a = b.center, c = b.center; a[L] = b.min[L]; c[L] = b.max[L];
            Vector3 wa = arrow.TransformPoint(a), wc = arrow.TransformPoint(c);
            bool aNear = !hand || Vector3.Distance(wa, hand.position) < Vector3.Distance(wc, hand.position);
            nock = aNear ? wa : wc; tip = aNear ? wc : wa;
            return true;
        }

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
                case Kind.Blade:       // hilt toward the hand: the hilt is the end nearer the cross-guard (the widest band)
                    nLong *= GuardEnd(nv, nb, na[0]);
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
            if (kind == Kind.Blade && grip)
            {
                // along the blade, the hand closes on the grip just below the cross-guard (however long the new grip is)
                int ax = na[0]; int best = 0; float widest = -1;
                for (int kk = 0; kk < 20; kk++) { float w = Width(nv, nb, ax, kk / 20f, (kk + 1) / 20f); if (w > widest) { widest = w; best = kk; } }
                var guardN = nb.center; guardN[ax] = nb.min[ax] + nb.size[ax] * (best + .5f) / 20f;
                Vector3 guardO = rot * ((guardN - nb.center) * k);
                int ol = oa[0]; float along = oLong[ol];                       // +1/-1: the hilt's direction on the old long axis
                float lengthO = ob.size[ol];
                shift[ol] = hand[ol] - along * .07f * lengthO - guardO[ol];     // the guard a hand's breadth from the hand, bladeward
            }
            for (int i = 0; i < outV.Length; i++) outV[i] += shift;
            var n = src.normals; var outN = new Vector3[n.Length];
            for (int i = 0; i < n.Length; i++) outN[i] = rot * n[i];
            var mesh = new Mesh { name = old.name + " (" + src.name + ")", indexFormat = src.indexFormat };
            mesh.vertices = outV; mesh.normals = outN; mesh.uv = src.uv;
            mesh.subMeshCount = src.subMeshCount;
            for (int sm = 0; sm < src.subMeshCount; sm++) mesh.SetTriangles(src.GetTriangles(sm), sm);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
