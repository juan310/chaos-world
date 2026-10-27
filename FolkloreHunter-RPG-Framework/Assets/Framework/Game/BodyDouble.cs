using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FH.Game
{
    /// <summary>The characters made in the Codex combat prototype (Models/Codex/: the male and female hunters, the
    /// Boneguard, Iron Revenant, Grave Hexer and Grave Warden, copied unchanged from that project), worn the way that
    /// project wears them: the KayKit rig with all the game's clips keeps playing, hidden, and every frame this copies its
    /// pose onto the new body bone by bone (each bone's direction, its twist, the hips' travel, the soles on the ground).
    /// The source rig's chest and hand slots are then moved onto the new body, so the weapons in hand or on the back, the
    /// blade sweeps and everything else that follows those bones go with what the player sees.</summary>
    [DefaultExecutionOrder(-50)]
    public class BodyDouble : MonoBehaviour
    {
        public const string Folder = "Models/Codex/";

        /// <summary>Body bone, its child (for the bone's direction), source bone, source child.</summary>
        static readonly string[][] Map =
        {
            new[] { "Hips", "Spine", "hips", "spine" }, new[] { "Spine", "Chest", "spine", "chest" }, new[] { "Chest", "Neck", "chest", "head" },
            new[] { "Neck", "Head", "head", "" }, new[] { "Head", "", "head", "" },
            new[] { "LeftUpperArm", "LeftLowerArm", "upperarm.l", "lowerarm.l" }, new[] { "LeftLowerArm", "LeftHand", "lowerarm.l", "wrist.l" }, new[] { "LeftHand", "", "wrist.l", "handslot.l" },
            new[] { "RightUpperArm", "RightLowerArm", "upperarm.r", "lowerarm.r" }, new[] { "RightLowerArm", "RightHand", "lowerarm.r", "wrist.r" }, new[] { "RightHand", "", "wrist.r", "handslot.r" },
            new[] { "LeftUpperLeg", "LeftLowerLeg", "upperleg.l", "lowerleg.l" }, new[] { "LeftLowerLeg", "LeftFoot", "lowerleg.l", "foot.l" }, new[] { "LeftFoot", "LeftToes", "foot.l", "toes.l" }, new[] { "LeftToes", "", "toes.l", "" },
            new[] { "RightUpperLeg", "RightLowerLeg", "upperleg.r", "lowerleg.r" }, new[] { "RightLowerLeg", "RightFoot", "lowerleg.r", "foot.r" }, new[] { "RightFoot", "RightToes", "foot.r", "toes.r" }, new[] { "RightToes", "", "toes.r", "" },
        };
        /// <summary>Source (KayKit) bone names that have a body bone of their own, for hurtboxes and aim points.</summary>
        static readonly Dictionary<string, string> Named = new Dictionary<string, string>
        {
            { "hips", "Hips" }, { "spine", "Spine" }, { "chest", "Chest" }, { "neck", "Neck" }, { "head", "Head" },
            { "upperarm.l", "LeftUpperArm" }, { "lowerarm.l", "LeftLowerArm" }, { "wrist.l", "LeftHand" }, { "hand.l", "LeftHand" },
            { "upperarm.r", "RightUpperArm" }, { "lowerarm.r", "RightLowerArm" }, { "wrist.r", "RightHand" }, { "hand.r", "RightHand" },
            { "upperleg.l", "LeftUpperLeg" }, { "lowerleg.l", "LeftLowerLeg" }, { "foot.l", "LeftFoot" }, { "toes.l", "LeftToes" },
            { "upperleg.r", "RightUpperLeg" }, { "lowerleg.r", "RightLowerLeg" }, { "foot.r", "RightFoot" }, { "toes.r", "RightToes" },
        };

        class Link { public Transform target, source, end; public Vector3 restLocal, restDirection, sourceDirection; public Quaternion restRotation, sourceRotation; }
        /// <summary>A source bone moved onto the body after the pose copy. Put back next frame unless the clip wrote it.</summary>
        class Nudge { public Transform t; public Vector3 before, set; public bool on; }

        Transform root, model, hips, toeL, toeR, handL, handR, srcHips, srcToeL, srcToeR;
        Link[] links;
        Vector3 restHips, srcRestHips, offset;
        float srcSole, soleL, soleR;
        AnimationDriver anim;
        Nudge chestNudge, slotR, slotL;
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        GameObject hair;

        public string Body { get; private set; }
        public Transform Model => model;
        public Transform Head { get; private set; }
        public Transform Chest { get; private set; }
        public Renderer[] Renderers { get; private set; }
        /// <summary>The source rig's renderers this body hides (its own body; weapons stay).</summary>
        public Renderer[] Hidden { get; private set; }

        public static bool Exists(string body) => !string.IsNullOrEmpty(body) && Resources.Load<GameObject>(Folder + body) != null;

        /// <summary>Puts <paramref name="body"/> (a path under Models/Codex/) on the rig in <paramref name="visual"/>, which
        /// must still be in its bind pose; <paramref name="root"/> is the transform that walks and turns. Null when the
        /// body is not in the build or lacks the bones.</summary>
        public static BodyDouble Wear(Transform visual, Transform root, string body, float height, AnimationDriver anim)
        {
            var prefab = Exists(body) ? Resources.Load<GameObject>(Folder + body) : null;
            if (!prefab || !visual) return null;
            var d = visual.gameObject.AddComponent<BodyDouble>();
            if (d.Build(visual, root, prefab, body, height, anim)) return d;
            if (d.model) Destroy(d.model.gameObject);
            DestroyImmediate(d);
            return null;
        }

        bool Build(Transform visual, Transform r, GameObject prefab, string body, float height, AnimationDriver driver)
        {
            root = r; anim = driver; Body = body;
            Transform S(string n) => WorldKit.Find(visual, n);
            srcHips = S("hips"); srcToeL = S("toes.l"); srcToeR = S("toes.r");
            var srcChest = S("chest"); var srcSlotR = S("handslot.r"); var srcSlotL = S("handslot.l");
            var source = visual.GetComponentsInChildren<Renderer>(true);
            model = Instantiate(prefab, visual).transform;
            model.name = "Body " + body;
            model.localPosition = Vector3.zero; model.rotation = root.rotation;
            foreach (var a in model.GetComponentsInChildren<Animator>(true)) DestroyImmediate(a);
            foreach (var a in model.GetComponentsInChildren<Animation>(true)) DestroyImmediate(a);
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            hips = B("Hips"); Head = B("Head"); Chest = B("Chest"); toeL = B("LeftToes"); toeR = B("RightToes"); handL = B("LeftHand"); handR = B("RightHand");
            if (!hips || !Head || !Chest || !toeL || !toeR || !handL || !handR || !srcHips || !srcToeL || !srcToeR)
            {
                Debug.LogWarning("FH_BODY " + body + " not worn: missing bones");
                return false;
            }
            Renderers = model.GetComponentsInChildren<Renderer>(true);
            var b = Bounds(Renderers);
            model.localScale *= height / Mathf.Max(.01f, b.size.y);
            b = Bounds(Renderers);
            model.position += Vector3.up * (root.position.y - b.min.y);
            offset = model.localPosition;

            string name = body.Substring(body.LastIndexOf('/') + 1);
            var mat = new Material(Shader.Find("Standard")) { name = name + " atlas", color = new Color(.9f, .9f, .9f) };
            mat.mainTexture = Resources.Load<Texture2D>(Folder + body + "_texture");
            mat.SetFloat("_Glossiness", .13f);
            foreach (var rr in Renderers)
            {
                rr.sharedMaterials = Enumerable.Repeat(mat, rr.sharedMaterials.Length).ToArray();
                if (rr is SkinnedMeshRenderer sk) sk.updateWhenOffscreen = true;
            }

            // The source's own body goes; its weapons (on a hand slot, or the hunter's Hunt_* pieces) stay.
            Hidden = source.Where(x => x is SkinnedMeshRenderer || !(Under(x.transform, srcSlotR) || Under(x.transform, srcSlotL) || IsWeapon(x.name))).ToArray();
            foreach (var x in Hidden) { x.enabled = false; x.forceRenderingOff = true; }

            restHips = root.InverseTransformPoint(hips.position);
            srcRestHips = root.InverseTransformPoint(srcHips.position);
            srcSole = Mathf.Min(srcToeL.position.y, srcToeR.position.y) - root.position.y;
            soleL = toeL.position.y - root.position.y; soleR = toeR.position.y - root.position.y;
            var list = new List<Link>();
            foreach (var m in Map)
            {
                var t = B(m[0]); var s = S(m[2]);
                if (!t || !s) continue;
                var child = m[1] == "" ? null : B(m[1]);
                var end = m[3] == "" ? null : S(m[3]);
                list.Add(new Link
                {
                    target = t, source = s, end = end, restLocal = t.localPosition,
                    restDirection = root.InverseTransformDirection(child ? (child.position - t.position).normalized : t.up),
                    sourceDirection = root.InverseTransformDirection(end ? (end.position - s.position).normalized : s.up),
                    restRotation = Quaternion.Inverse(root.rotation) * t.rotation, sourceRotation = Quaternion.Inverse(root.rotation) * s.rotation,
                });
            }
            links = list.OrderBy(l => Depth(l.target)).ToArray();
            if (srcChest) chestNudge = new Nudge { t = srcChest };
            if (srcSlotR) slotR = new Nudge { t = srcSlotR };
            if (srcSlotL) slotL = new Nudge { t = srcSlotL };
            Debug.Log("FH_BODY " + body + " on " + visual.name + ": " + links.Length + " bones, height " + height.ToString("0.00") + ", hid " + Hidden.Length + " source renderers");
            Sync();
            return true;
        }

        static bool IsWeapon(string n) => n.StartsWith("Hunt_") || n.StartsWith("1H_") || n.StartsWith("2H_") || n.Contains("Shield") || n.Contains("Sword") || n.Contains("held");
        static bool Under(Transform t, Transform parent) { for (; parent && t; t = t.parent) if (t == parent) return true; return false; }
        static int Depth(Transform t) { int n = 0; while (t.parent) { n++; t = t.parent; } return n; }
        Transform B(string n) => bones.TryGetValue(n, out var t) ? t : null;

        /// <summary>The body bone standing for a source (KayKit) bone name, or null.</summary>
        public Transform Bone(string sourceName) => sourceName != null && Named.TryGetValue(sourceName, out var n) ? B(n) : null;

        static Bounds Bounds(Renderer[] rr)
        {
            var b = rr[0].bounds;
            foreach (var x in rr.Skip(1)) b.Encapsulate(x.bounds);
            return b;
        }

        void LateUpdate() => Sync();

        void Sync()
        {
            if (!model) return;
            Undo(slotL); Undo(slotR); Undo(chestNudge);
            model.localPosition = offset;
            var inv = Quaternion.Inverse(root.rotation);
            foreach (var l in links)
            {
                l.target.localPosition = l.restLocal;
                Vector3 direction = inv * (l.end ? (l.end.position - l.source.position).normalized : l.source.up);
                var delta = inv * l.source.rotation * Quaternion.Inverse(l.sourceRotation);
                var swing = Quaternion.FromToRotation(l.sourceDirection, direction);
                var raw = delta * Quaternion.Inverse(swing);
                var p = Vector3.Project(new Vector3(raw.x, raw.y, raw.z), direction);
                var twist = new Quaternion(p.x, p.y, p.z, raw.w).normalized;
                l.target.rotation = root.rotation * twist * Quaternion.FromToRotation(l.restDirection, direction) * l.restRotation;
            }
            hips.position = root.TransformPoint(restHips + root.InverseTransformPoint(srcHips.position) - srcRestHips);
            float floor = Mathf.Min(toeL.position.y - soleL, toeR.position.y - soleR);
            float clearance = Mathf.Min(srcToeL.position.y, srcToeR.position.y) - root.position.y - srcSole;
            if (anim != null && (anim.Current.Contains("Death") || anim.Current.Contains("Lie_")))
            {
                // lying down: the lowest of head, chest and hips rests on the floor, not the soles
                floor = Mathf.Min(floor, Mathf.Min(Head.position.y - .1f, Mathf.Min(Chest.position.y - .14f, hips.position.y - .14f)));
                clearance = 0;
            }
            model.position += Vector3.up * (root.position.y + clearance - floor);
            Push(chestNudge, Chest.position);
            Push(slotR, handR.position + handR.up * .06f);
            Push(slotL, handL.position + handL.up * .06f);
        }

        static void Push(Nudge n, Vector3 world)
        {
            if (n == null || !n.t) return;
            n.before = n.t.localPosition; n.t.position = world; n.set = n.t.localPosition; n.on = true;
        }

        static void Undo(Nudge n)
        {
            if (n == null || !n.on || !n.t) return;
            if (n.t.localPosition == n.set) n.t.localPosition = n.before;      // not written by a clip since: back where the rig had it
            n.on = false;
        }

        /// <summary>One of the ten hairstyles (index 0-9, the creator's order) on this body's head (Codex's hair builder).</summary>
        public void Hair(bool female, int style)
        {
            if (hair) { Destroy(hair.GetComponent<MeshFilter>().sharedMesh); Destroy(hair.transform.parent ? hair.transform.parent.gameObject : hair); }
            var hb = HeadBounds();
            var center = hb.center; center.y = hb.max.y - .112f * HeightScale();
            var size = new Vector3(Mathf.Clamp(hb.extents.x, .07f, .13f) + .004f, .125f, Mathf.Clamp(hb.extents.z, .07f, .12f) + .005f) * HeightScale();
            var anchor = new GameObject("Hair anchor").transform;
            anchor.SetParent(Head, false); anchor.position = center; anchor.rotation = root.rotation;
            anchor.localScale = Vector3.one / Head.lossyScale.x;
            hair = HunterHair.Create(female, ((style % 10) + 10) % 10, size, anchor);
            Renderers = Renderers.Where(x => x).Concat(hair.GetComponentsInChildren<Renderer>()).ToArray();
        }

        /// <summary>Codex sized hair for 1.8 m hunters; a taller or shorter body scales it.</summary>
        float HeightScale() => Mathf.Max(.5f, Bounds(model.GetComponentsInChildren<SkinnedMeshRenderer>().Cast<Renderer>().ToArray()).size.y / 1.8f);

        /// <summary>The head's vertices (those weighted mostly to the head bone, near the top of the body).</summary>
        Bounds HeadBounds()
        {
            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = skin.sharedMesh;
            var vertices = mesh.vertices; var weights = mesh.boneWeights;
            var matrices = skin.bones.Select((bone, i) => bone.localToWorldMatrix * mesh.bindposes[i]).ToArray();
            int head = System.Array.IndexOf(skin.bones, Head);
            float top = Bounds(new Renderer[] { skin }).max.y, scale = HeightScale();
            bool any = false; var result = new Bounds();
            for (int i = 0; i < vertices.Length && weights.Length == vertices.Length; i++)
            {
                var w = weights[i];
                float value = (w.boneIndex0 == head ? w.weight0 : 0) + (w.boneIndex1 == head ? w.weight1 : 0) + (w.boneIndex2 == head ? w.weight2 : 0) + (w.boneIndex3 == head ? w.weight3 : 0);
                if (value < .45f) continue;
                var p = matrices[w.boneIndex0].MultiplyPoint3x4(vertices[i]) * w.weight0;
                if (w.weight1 > 0) p += matrices[w.boneIndex1].MultiplyPoint3x4(vertices[i]) * w.weight1;
                if (w.weight2 > 0) p += matrices[w.boneIndex2].MultiplyPoint3x4(vertices[i]) * w.weight2;
                if (w.weight3 > 0) p += matrices[w.boneIndex3].MultiplyPoint3x4(vertices[i]) * w.weight3;
                if (p.y < top - .22f * scale) continue;
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; } else result.Encapsulate(p);
            }
            if (!any) result = new Bounds(Head.position + Vector3.up * .1f * scale, new Vector3(.19f, .22f, .18f) * scale);
            return result;
        }

        /// <summary>The Grave Warden's separate horned helmet (Models/Codex/Skeletons/GraveWardenHelmet), on the head, named
        /// "Helmet" so the helm part can be broken off.</summary>
        public GameObject Helmet(string resource)
        {
            var prefab = Resources.Load<GameObject>(Folder + resource);
            if (!prefab) return null;
            var obj = Instantiate(prefab, Head);
            obj.name = "Helmet";
            obj.transform.rotation = root.rotation * Quaternion.Euler(-90, 0, 0);
            obj.transform.localScale = Vector3.one / Head.lossyScale.x;
            var rr = obj.GetComponentsInChildren<Renderer>(true);
            if (rr.Length == 0) { Destroy(obj); return null; }
            float k = HeightScale() * 1.8f / 2.4f;          // Codex fitted it to a 2.4 m Warden
            var b = Bounds(rr);
            obj.transform.localScale *= .43f * k / b.size.x;
            b = Bounds(rr);
            obj.transform.position += Head.position + root.up * .18f * k - b.center;
            var mat = new Material(Shader.Find("Standard")) { name = "Grave Warden helmet atlas", color = new Color(.82f, .82f, .82f) };
            mat.mainTexture = Resources.Load<Texture2D>(Folder + resource + "_texture");
            mat.SetFloat("_Metallic", .12f); mat.SetFloat("_Glossiness", .2f);
            foreach (var x in rr) { x.sharedMaterials = Enumerable.Repeat(mat, x.sharedMaterials.Length).ToArray(); x.gameObject.name = "Helmet"; }
            Renderers = Renderers.Concat(rr).ToArray();
            return obj;
        }
    }
}
