using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Follow-through on top of the animation: a chain of bones (a tail, ears, a cape) whose tips trail behind
    /// when the body turns, starts or stops, then spring back to the animated pose. Each bone's tip is a damped particle
    /// pulled toward where the clip puts it; the bone is turned toward the particle, never more than MaxAngle off the
    /// clip. Frame-rate independent; a teleport resets it. SightForge's springs check reads the lag DevCapture records
    /// (jitter, never settling, pinned at the limit).</summary>
    public class SpringBone : MonoBehaviour
    {
        public Transform[] Chain = new Transform[0];
        [Range(0, 1)] public float Stiffness = .18f;    // share of the way back to the animated tip per 1/60 s
        [Range(0, 1)] public float Damping = .25f;      // share of the particle's speed lost per 1/60 s
        public float MaxAngle = 30;
        public float Gravity = 0;
        public string Label = "";

        public static readonly List<SpringBone> All = new List<SpringBone>();

        /// <summary>Every spring's record, kept after its character is gone (a verification run ends on the title).</summary>
        public class Record { public string Label; public float MaxAngle, MaxLag; public int Frames, Pinned; public bool Broke; }
        public static readonly Dictionary<string, Record> History = new Dictionary<string, Record>();
        /// <summary>The largest angle (degrees) between a chain bone and its animated direction this frame.</summary>
        public float Lag { get; private set; }
        /// <summary>Over the bone's life: the largest lag, frames run, frames held at the limit, and whether it ever went bad.</summary>
        public float MaxLagSeen { get; private set; }
        public int Frames { get; private set; }
        public int PinnedFrames { get; private set; }
        public bool Broke { get; private set; }

        Vector3[] tipLocal, pos, prev;
        float[] length;
        Vector3 lastRoot;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Start() { Init(); }

        void Init()
        {
            Chain = Chain.Where(b => b).ToArray();
            int n = Chain.Length;
            tipLocal = new Vector3[n]; pos = new Vector3[n]; prev = new Vector3[n]; length = new float[n];
            for (int i = 0; i < n; i++)
            {
                var b = Chain[i];
                // the tip: the next bone in the chain, else the bone's first child, else the parent's offset continued
                Vector3 local = i + 1 < n ? b.InverseTransformPoint(Chain[i + 1].position)
                              : b.childCount > 0 ? b.GetChild(0).localPosition
                              : (b.parent ? b.parent.InverseTransformVector(b.position - b.parent.position) : Vector3.up * .1f);
                if (local.sqrMagnitude < 1e-6f) local = Vector3.up * .05f;
                tipLocal[i] = local;
                pos[i] = prev[i] = b.TransformPoint(local);
                length[i] = Vector3.Distance(b.position, pos[i]);
            }
            lastRoot = n > 0 ? Chain[0].position : transform.position;
        }

        void LateUpdate()
        {
            int n = Chain.Length;
            if (n == 0 || tipLocal == null || tipLocal.Length != n) { if (n > 0) Init(); return; }
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            bool reset = Vector3.Distance(Chain[0].position, lastRoot) > 2f;
            lastRoot = Chain[0].position;
            float k = 1 - Mathf.Pow(1 - Stiffness, dt * 60), keep = Mathf.Pow(1 - Damping, dt * 60);
            float lag = 0;
            for (int i = 0; i < n; i++)
            {
                var b = Chain[i];
                Vector3 root = b.position, target = b.TransformPoint(tipLocal[i]);
                if (reset) { pos[i] = prev[i] = target; continue; }
                Vector3 v = (pos[i] - prev[i]) * keep;
                prev[i] = pos[i];
                pos[i] += v + Vector3.down * Gravity * dt * dt;
                pos[i] += (target - pos[i]) * k;
                // keep the bone's length, and keep it within MaxAngle of the clip
                Vector3 want = target - root, got = pos[i] - root;
                if (got.sqrMagnitude < 1e-8f) got = want;
                float ang = Vector3.Angle(want, got);
                if (ang > MaxAngle) { got = Vector3.Slerp(want, got, MaxAngle / ang); ang = MaxAngle; }
                pos[i] = root + got.normalized * length[i];
                b.rotation = Quaternion.FromToRotation(want, got) * b.rotation;
                lag = Mathf.Max(lag, ang);
            }
            Lag = lag;
            Frames++; MaxLagSeen = Mathf.Max(MaxLagSeen, lag);
            if (lag >= MaxAngle - .01f) PinnedFrames++;
            if (float.IsNaN(lag) || pos.Any(p => float.IsNaN(p.x))) { Broke = true; Init(); }
            if (!History.TryGetValue(Label, out var rec)) History[Label] = rec = new Record { Label = Label, MaxAngle = MaxAngle };
            rec.Frames++; rec.MaxLag = Mathf.Max(rec.MaxLag, float.IsNaN(lag) ? 0 : lag); if (lag >= MaxAngle - .01f) rec.Pinned++; rec.Broke |= Broke;
        }

        /// <summary>Springs on a character's loose parts, found by bone name: a tail chain (tail_1, tail_2 ...), ears, and any
        /// cape, cloak, tassel, ponytail, skirt or banner chain. Returns how many chains were added.</summary>
        public static int Rig(Transform visual, string who)
        {
            if (!visual) return 0;
            int made = 0;
            var bones = visual.GetComponentsInChildren<Transform>(true);
            bool Loose(Transform t) { var n = t.name.ToLowerInvariant(); return n.StartsWith("tail") || n.Contains("cape") || n.Contains("cloak") || n.Contains("tassel") || n.Contains("pony") || n.Contains("skirt") || n.Contains("banner"); }
            foreach (var top in bones.Where(t => Loose(t) && !(t.parent && Loose(t.parent))))
            {
                if (top.GetComponent<Renderer>()) continue;   // a mesh named like a bone is not a bone
                var chain = new List<Transform> { top };
                for (var c = top; ; )
                {
                    var next = c.Cast<Transform>().FirstOrDefault(t => Loose(t) && !t.GetComponent<Renderer>());
                    if (!next) break;
                    chain.Add(next); c = next;
                }
                bool tail = top.name.ToLowerInvariant().StartsWith("tail");
                Add(top.gameObject, who + " " + top.name, chain.ToArray(), tail ? .16f : .1f, tail ? .22f : .3f, tail ? 28 : 40, tail ? 0 : 2);
                made++;
            }
            foreach (var ear in bones.Where(t => t.name == "ear.l" || t.name == "ear.r"))
            {
                Add(ear.gameObject, who + " " + ear.name, new[] { ear }, .3f, .3f, 18, 0);
                made++;
            }
            if (made > 0) Debug.Log("FH_SPRING " + who + ": " + made + " spring chain(s)");
            return made;
        }

        static void Add(GameObject on, string label, Transform[] chain, float stiff, float damp, float max, float gravity)
        {
            var s = on.GetComponent<SpringBone>();
            if (!s) s = on.AddComponent<SpringBone>();
            s.Chain = chain; s.Stiffness = stiff; s.Damping = damp; s.MaxAngle = max; s.Gravity = gravity; s.Label = label;
        }
    }
}
