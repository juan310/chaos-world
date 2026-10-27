using UnityEngine;

namespace FH.Game
{
    /// <summary>A bow's string drawn live: from one limb tip to the other, straight at rest, and back to the nocked arrow's
    /// nock while the hunter draws (the rigid bow mesh cannot bend its string). The string's own part is taken out of the
    /// bow's mesh (its last submesh) and its two ends kept, in the bow's space.</summary>
    public class BowString : MonoBehaviour
    {
        public Vector3 EndA, EndB;            // the string's ends on the limb tips, in the bow's space
        public float Width = .006f;
        public Transform Nock;                // the nocked arrow (its end nearer the string hand)
        public Transform StringHand;
        LineRenderer line;
        Renderer nockR;

        /// <summary>Takes the string out of the bow mesh's last submesh and draws it instead.</summary>
        public static void Attach(Transform bow, MeshFilter mf)
        {
            var mesh = mf.sharedMesh; int last = mesh.subMeshCount - 1;
            var tris = mesh.GetTriangles(last); var v = mesh.vertices;
            if (tris.Length == 0) return;
            // the string's ends: the extremes of its own vertices along its length
            var used = new System.Collections.Generic.HashSet<int>(tris);
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            foreach (var i in used) { lo = Vector3.Min(lo, v[i]); hi = Vector3.Max(hi, v[i]); }
            var size = hi - lo; int ax = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            Vector3 a = Vector3.zero, b = Vector3.zero; float amin = float.MaxValue, amax = float.MinValue;
            foreach (var i in used) { if (v[i][ax] < amin) { amin = v[i][ax]; a = v[i]; } if (v[i][ax] > amax) { amax = v[i][ax]; b = v[i]; } }
            var cut = Object.Instantiate(mesh); cut.name = mesh.name + " (string drawn live)";
            cut.SetTriangles(new int[0], last); cut.subMeshCount = last;
            mf.sharedMesh = cut;
            var s = bow.gameObject.GetComponent<BowString>() ?? bow.gameObject.AddComponent<BowString>();
            s.EndA = a; s.EndB = b; s.Width = Mathf.Max(.003f, Mathf.Min(size[(ax + 1) % 3], size[(ax + 2) % 3]) * bow.lossyScale.x);
        }

        /// <summary>A bow modelled without a string: the string runs between its limb tips (the two ends of its length,
        /// on the side away from the wood's mass, a little in from the very tips).</summary>
        public static void AttachAtTips(Transform bow, MeshFilter mf)
        {
            var mesh = mf.sharedMesh; var v = mesh.vertices; var b = mesh.bounds; var size = b.size;
            int L = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            // the tip vertices: the last 3 % of the length at each end; the string sits on their inner (string) side
            Vector3 SideOf(float lo, float hi)
            {
                Vector3 sum = Vector3.zero; int n = 0;
                foreach (var p in v) if (p[L] >= lo && p[L] <= hi) { sum += p; n++; }
                return n > 0 ? sum / n : b.center;
            }
            float len = size[L];
            var a = SideOf(b.min[L], b.min[L] + len * .03f); var c = SideOf(b.max[L] - len * .03f, b.max[L]);
            var s = bow.gameObject.GetComponent<BowString>() ?? bow.gameObject.AddComponent<BowString>();
            s.EndA = a; s.EndB = c; s.Width = Mathf.Max(.003f, .004f * bow.lossyScale.x / Mathf.Max(1e-4f, bow.lossyScale.x));
        }

        void Start()
        {
            var visual = transform.root;
            if (!Nock) Nock = WorldKit.Find(visual, "Hunt_NockedArrow");
            if (!StringHand) StringHand = WorldKit.Find(visual, "handslot.r");
            nockR = Nock ? Nock.GetComponent<Renderer>() : null;
            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true; line.widthMultiplier = Width; line.numCapVertices = 2;
            line.sharedMaterial = WorldKit.Mat("Bow string", new Color(.86f, .83f, .72f));
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        /// <summary>Where the string is pulled to this frame (the nock), or null when it rests straight.</summary>
        public Vector3? PulledTo { get; private set; }

        void LateUpdate()
        {
            if (!line) return;
            var bowR = GetComponent<Renderer>();
            line.enabled = bowR && bowR.enabled;
            Vector3 a = transform.TransformPoint(EndA), b = transform.TransformPoint(EndB);
            PulledTo = null;
            if (nockR && nockR.enabled && StringHand && transform.parent && transform.parent.name.Contains(".l"))
            {
                // the nock is the arrow's end nearer the string hand
                var mfN = Nock.GetComponent<MeshFilter>();
                Vector3 nock = StringHand.position;
                if (mfN && mfN.sharedMesh)
                {
                    var bb = mfN.sharedMesh.bounds; var sz = bb.size; int ax = sz.x >= sz.y && sz.x >= sz.z ? 0 : sz.y >= sz.z ? 1 : 2;
                    Vector3 lo = bb.center, hi = bb.center; lo[ax] = bb.min[ax]; hi[ax] = bb.max[ax];
                    Vector3 wl = Nock.TransformPoint(lo), wh = Nock.TransformPoint(hi);
                    nock = Vector3.Distance(wl, StringHand.position) < Vector3.Distance(wh, StringHand.position) ? wl : wh;
                }
                var mid = (a + b) * .5f;
                if (Vector3.Distance(nock, mid) > .03f && Vector3.Distance(nock, mid) < Vector3.Distance(a, b)) PulledTo = nock;
            }
            if (PulledTo.HasValue) { line.positionCount = 3; line.SetPosition(0, a); line.SetPosition(1, PulledTo.Value); line.SetPosition(2, b); }
            else { line.positionCount = 2; line.SetPosition(0, a); line.SetPosition(1, b); }
        }
    }
}
