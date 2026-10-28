using System.Collections.Generic;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>A monster drawn as a 2D sprite standing in the 3D world (model "Sprite_&lt;name&gt;": the frames and
    /// Resources/Sprites/&lt;name&gt;/sprite.json written by Tools/sprites/troll_puppet.py from the owner's troll sheet).
    /// A card that always faces the camera shows the playing clip's frame, turned left or right with the monster's
    /// heading (or its front or back when it comes at the camera or walks away: Tools/sprites/troll_views.py); under it, invisible bones (hips, chest, head, hand, feet) follow the drawn body frame by frame. The clips
    /// are ordinary legacy AnimationClips moving those bones, so the game's AnimationDriver plays, times (the strike lands
    /// when the drawn fist is fastest) and freezes them like any model's, and the hurtboxes ride on the bones.</summary>
    public class SpriteActor : MonoBehaviour
    {
        class Clip { public float fps; public bool loop; }
        class View { public Mesh mesh; public readonly Dictionary<string, Texture2D[]> frames = new Dictionary<string, Texture2D[]>(); }

        readonly Dictionary<string, Clip> clips = new Dictionary<string, Clip>();
        readonly Dictionary<string, View> views = new Dictionary<string, View>();
        Animation anim;
        Transform card;
        MeshFilter cardMesh;
        Renderer cardRenderer;
        MaterialPropertyBlock block;
        Texture shown;
        float flash, shownFlash = -1, faceSign = 1;

        public const string Prefix = "Sprite_";

        public static bool Exists(string resource) =>
            resource != null && resource.StartsWith(Prefix) && Resources.Load<TextAsset>("Sprites/" + resource.Substring(Prefix.Length) + "/sprite");

        public static Transform Create(string resource, Transform parent, float targetHeight)
        {
            string sprite = resource.Substring(Prefix.Length);
            var meta = (Dictionary<string, object>)Json.Parse(Resources.Load<TextAsset>("Sprites/" + sprite + "/sprite").text);
            var root = new GameObject(resource + " visual");
            root.transform.SetParent(parent, false);
            root.AddComponent<SpriteActor>().Build(sprite, meta, targetHeight);
            return root.transform;
        }

        /// <summary>A hit lights the card up for a moment.</summary>
        public void Flash() { flash = .12f; Flashes++; }
        public int Flashes { get; private set; }
        /// <summary>The frame on the card now ("&lt;clip&gt;/NN") and the view it is drawn from (side, front, back), for the checks.</summary>
        public string Frame { get; private set; } = "";
        public string ViewName { get; private set; } = "side";
        public Renderer Card => cardRenderer;

        static float F(object o) => (float)(double)o;
        static Vector2 V(object o) { var l = (List<object>)o; return new Vector2(F(l[0]), F(l[1])); }

        /// <summary>sprite.json: "views" (side, front, back; each its frame size, pivot, standing height, folder and
        /// clips), or one view at the top level (the side, as first exported). The side view's clips carry the joints.</summary>
        void Build(string sprite, Dictionary<string, object> meta, float targetHeight)
        {
            var viewsMeta = meta.ContainsKey("views") ? (Dictionary<string, object>)meta["views"] : new Dictionary<string, object> { { "side", meta } };
            var side = (Dictionary<string, object>)(viewsMeta.ContainsKey("side") ? viewsMeta["side"] : viewsMeta.Values.First());
            var pivot = V(side["pivot"]);
            float m = targetHeight / F(side["standingHeight"]);            // metres per frame pixel
            // frame pixels -> the monster's own space: the side art faces left, so the picture's left is the monster's forward
            Vector3 Local(Vector2 p) => new Vector3(0, (pivot.y - p.y) * m, (pivot.x - p.x) * m);

            anim = gameObject.AddComponent<Animation>();
            anim.cullingType = AnimationCullingType.AlwaysAnimate;
            var bones = new Dictionary<string, Transform>();
            var clipsMeta = (Dictionary<string, object>)side["clips"];
            var firstJoints = (Dictionary<string, object>)((Dictionary<string, object>)clipsMeta.Values.First())["joints"];
            foreach (var j in firstJoints.Keys)
            {
                var b = new GameObject(j).transform; b.SetParent(transform, false);
                b.localPosition = Local(V(((List<object>)firstJoints[j])[0]));
                bones[j] = b;
            }
            if (bones.TryGetValue("hand.r", out var hand)) new GameObject("handslot.r").transform.SetParent(hand, false);

            foreach (var kv in clipsMeta)
            {
                var c = (Dictionary<string, object>)kv.Value;
                var clip = new Clip { fps = F(c["fps"]), loop = (bool)c["loop"] };
                clips[kv.Key] = clip;
                var ac = new AnimationClip { legacy = true, name = kv.Key, frameRate = clip.fps, wrapMode = clip.loop ? WrapMode.Loop : WrapMode.ClampForever };
                foreach (var jv in (Dictionary<string, object>)c["joints"])
                {
                    var pts = ((List<object>)jv.Value).Select(V).ToList();
                    var cy = new AnimationCurve(); var cz = new AnimationCurve(); var cx = new AnimationCurve();
                    for (int i = 0; i <= pts.Count; i++)                     // one key a frame; a loop returns to its first
                    {
                        if (i == pts.Count && !clip.loop) break;
                        var p = Local(pts[i % pts.Count]); float t = i / clip.fps;
                        cx.AddKey(t, 0); cy.AddKey(t, p.y); cz.AddKey(t, p.z);
                    }
                    ac.SetCurve(jv.Key, typeof(Transform), "localPosition.x", cx);
                    ac.SetCurve(jv.Key, typeof(Transform), "localPosition.y", cy);
                    ac.SetCurve(jv.Key, typeof(Transform), "localPosition.z", cz);
                }
                anim.AddClip(ac, kv.Key);
            }
            // the alert stance is the idle; its own name lets the brain ask for it as it does of any model
            if (clips.TryGetValue("Idle", out var idle) && !clips.ContainsKey("Idle_Combat"))
            {
                clips["Idle_Combat"] = idle;
                var copy = Object.Instantiate(anim.GetClip("Idle")); copy.name = "Idle_Combat"; copy.legacy = true;
                anim.AddClip(copy, "Idle_Combat");
            }

            // each view: its frames, and a card mesh in its proportions with the ground under the hips at the origin
            foreach (var vk in viewsMeta)
            {
                var vm = (Dictionary<string, object>)vk.Value;
                string folder = vm.ContainsKey("folder") ? (string)vm["folder"] : "";
                var view = new View();
                foreach (var name in ((Dictionary<string, object>)vm["clips"]).Keys)
                {
                    var frames = Resources.LoadAll<Texture2D>("Sprites/" + sprite + "/" + folder + name).OrderBy(t => t.name).ToArray();
                    if (frames.Length > 0) view.frames[name] = frames;
                }
                if (view.frames.TryGetValue("Idle", out var idleFrames)) view.frames["Idle_Combat"] = idleFrames;
                if (view.frames.Count == 0) continue;
                var frame = V(vm["frame"]); var pv = V(vm["pivot"]);
                float k = targetHeight / F(vm["standingHeight"]);
                view.mesh = new Mesh { name = sprite + " card (" + vk.Key + ")" };
                float l = -pv.x * k, r = (frame.x - pv.x) * k, top = pv.y * k, bottom = (pv.y - frame.y) * k;
                view.mesh.vertices = new[] { new Vector3(l, top), new Vector3(r, top), new Vector3(r, bottom), new Vector3(l, bottom) };
                view.mesh.uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
                view.mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                var n = new Vector3(0, .55f, -1).normalized;                   // lit as if turned a little to the sky, not edge-on to the sun
                view.mesh.normals = new[] { n, n, n, n };
                view.mesh.RecalculateBounds();
                views[vk.Key] = view;
            }
            if (!views.ContainsKey("side")) ViewName = views.Keys.First();

            card = new GameObject("Sprite card").transform; card.SetParent(transform, false);
            cardMesh = card.gameObject.AddComponent<MeshFilter>();
            cardMesh.sharedMesh = views[ViewName].mesh;
            cardRenderer = card.gameObject.AddComponent<MeshRenderer>();
            var shader = Shader.Find("Folklore/SpriteCard");
            cardRenderer.sharedMaterial = new Material(shader ? shader : Shader.Find("Standard")) { name = sprite + " sprite" };
            cardRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            block = new MaterialPropertyBlock();
            ShowFrame(views[ViewName].frames.Values.First()[0]);
        }

        void ShowFrame(Texture2D tex)
        {
            float f = Mathf.Clamp01(flash / .12f) * .7f;
            if (tex == shown && f == shownFlash) return;
            shown = tex; shownFlash = f;
            cardRenderer.GetPropertyBlock(block);
            block.SetTexture("_MainTex", tex);
            block.SetFloat("_Flash", f);
            cardRenderer.SetPropertyBlock(block);
        }

        /// <summary>The frame of the clip that weighs most right now (a cross-fade switches at its middle), in the view shown.</summary>
        Texture2D CurrentFrame()
        {
            AnimationState best = null;
            foreach (AnimationState s in anim) if (s.enabled && (best == null || s.weight > best.weight)) best = s;
            if (best == null || !clips.TryGetValue(best.name, out var c)) return null;
            if (!views[ViewName].frames.TryGetValue(best.name, out var frames) && !views["side"].frames.TryGetValue(best.name, out frames)) return null;
            int i = Mathf.FloorToInt(best.time * c.fps + 1e-3f);
            i = c.loop ? ((i % frames.Length) + frames.Length) % frames.Length : Mathf.Clamp(i, 0, frames.Length - 1);
            Frame = best.name + "/" + frames[i].name;
            return frames[i];
        }

        void LateUpdate()
        {
            if (flash > 0) flash -= Time.deltaTime;
            FaceNow(Camera.main);
        }

        /// <summary>Shows the playing frame, turned to <paramref name="cam"/> (the game's camera; a review camera renders
        /// it as it would be seen from there), drawn from the view that matches the heading: its front when it comes at the
        /// camera, its back when it walks away, its side (mirrored to the way it heads) in between.</summary>
        public void FaceNow(Camera cam)
        {
            if (cam)
            {
                var toCam = cam.transform.position - transform.position; toCam.y = 0;
                var fwd = transform.forward; fwd.y = 0;
                if (toCam.sqrMagnitude > 1e-4f && fwd.sqrMagnitude > 1e-4f)
                {
                    float a = Vector3.Angle(fwd, toCam);
                    string want = a < (ViewName == "front" ? 58 : 50) ? "front" : a > (ViewName == "back" ? 122 : 130) ? "back" : "side";   // a margin so it does not flicker on the edge
                    if (!views.ContainsKey(want)) want = views.ContainsKey("side") ? "side" : ViewName;
                    if (want != ViewName) { ViewName = want; cardMesh.sharedMesh = views[want].mesh; shown = null; }
                }
            }
            var tex = CurrentFrame();
            if (tex) ShowFrame(tex);
            if (!cam) return;
            // face the camera, upright; the side view shows the drawing facing the way the monster heads across the screen
            var look = card.position - cam.transform.position; look.y = 0;
            if (look.sqrMagnitude < 1e-4f) return;
            card.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            float side = Vector3.Dot(transform.forward, cam.transform.right);
            if (Mathf.Abs(side) > .15f) faceSign = side > 0 ? -1 : 1;     // the side art faces left; heading right shows it mirrored
            card.localScale = new Vector3(ViewName == "side" ? faceSign : 1, 1, 1);
        }
    }
}
