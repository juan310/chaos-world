using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Fingers closed round whatever each hand holds, on top of the animation: a slim body built with finger
    /// bones (Tools/slim/fingers.py) carries its grips beside it (Models/&lt;model&gt;_grips.json: each finger's curl at its
    /// three joints for every held piece, a relaxed hand and the bow-string hook). Every frame after the clip, each hand
    /// takes the pose of the piece showing in its slot (the hook while an arrow is in the right hand, relaxed when the hand
    /// is empty) and eases to it. A curl turns each finger bone about (finger direction x palm direction), measured on
    /// this rig at the start, so it reads the same here as in Blender, where the grips were solved and checked.</summary>
    public class HandGrip : MonoBehaviour
    {
        static readonly string[] Names = { "thumb", "index", "middle", "ring", "pinky" };
        const float EaseTime = .08f;

        class Hand
        {
            public string Side; public Transform Slot;
            public Transform[,] Bones = new Transform[5, 3];
            public Quaternion[,] Rest = new Quaternion[5, 3];
            public Vector3[,] Axis = new Vector3[5, 3];
            public float[,] Now = new float[5, 3];
            public string Pose = "relaxed";
        }

        readonly List<Hand> hands = new List<Hand>();
        Dictionary<string, object> poses;
        Dictionary<string, object> pieces;
        float thumbTilt = .7f;

        /// <summary>The pose each hand shows now (for checks and captures).</summary>
        public string PoseOf(string side) => hands.FirstOrDefault(h => h.Side == side)?.Pose;
        public bool Ready => hands.Count > 0;

        /// <summary>Adds the grips to a model made by CharacterFactory when it has finger bones and a grips file.</summary>
        public static HandGrip Attach(GameObject visual, string model)
        {
            var text = Resources.Load<TextAsset>("Models/" + model + "_grips");
            if (!text) return null;
            var g = visual.AddComponent<HandGrip>();
            if (!g.Init(text.text)) { Destroy(g); return null; }
            return g;
        }

        bool Init(string json)
        {
            if (!(FH.Core.Json.Parse(json) is Dictionary<string, object> d)) return false;
            poses = d.TryGetValue("poses", out var p) ? p as Dictionary<string, object> : null;
            pieces = d.TryGetValue("pieces", out var q) ? q as Dictionary<string, object> : new Dictionary<string, object>();
            if (d.TryGetValue("thumbTilt", out var t) && t is double td) thumbTilt = (float)td;
            if (poses == null) return false;
            var all = GetComponentsInChildren<Transform>(true);
            Transform F(string n) => all.FirstOrDefault(x => x.name == n);
            foreach (var side in new[] { "l", "r" })
            {
                var hand = F("hand." + side); var slot = F("handslot." + side);
                if (!hand || !slot || !F("index_1." + side)) continue;
                var h = new Hand { Side = side, Slot = slot };
                // the palm's direction: from the hand bone's line to the grip slot (it sits in the palm); toward the little finger
                var hd = (F("middle_1." + side).position - hand.position).normalized;
                var v = slot.position - hand.position; var n = (v - hd * Vector3.Dot(v, hd)).normalized;
                var tp = (F("pinky_1." + side).position - F("index_1." + side).position).normalized;
                bool ok = true;
                for (int f = 0; f < 5 && ok; f++)
                    for (int k = 0; k < 3; k++)
                    {
                        var b = F(Names[f] + "_" + (k + 1) + "." + side); if (!b) { ok = false; break; }
                        h.Bones[f, k] = b; h.Rest[f, k] = b.localRotation;
                    }
                if (!ok) continue;
                for (int f = 0; f < 5; f++)
                {
                    // the finger lies straight at rest: its direction is its first bone's to its second
                    var d = (h.Bones[f, 1].position - h.Bones[f, 0].position).normalized;
                    var nn = f == 0 ? (n + tp * thumbTilt).normalized : n;
                    var axW = Vector3.Cross(d, nn).normalized;
                    for (int k = 0; k < 3; k++) h.Axis[f, k] = Quaternion.Inverse(h.Bones[f, k].rotation) * axW;
                }
                hands.Add(h);
            }
            return hands.Count > 0;
        }

        /// <summary>The pose for what shows in a hand's slot: the piece's own grip, the hook for an arrow, else relaxed.</summary>
        string Choose(Hand h)
        {
            foreach (var r in h.Slot.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var n = r.name.Replace(" (held)", "");
                if (n.Contains("Arrow")) return "hook";
                if (poses.ContainsKey(n) && Has(n, h.Side)) return n;
                // a re-skinned piece keeps its kind: the closest grip this hand has
                var kind = n.Contains("Shield") ? "Shield" : n.Contains("Bow") ? "Bow" : n.Contains("Lance") ? "Lance" : n.Contains("Katana") ? "Katana" : "Sword";
                var alt = pieces.Keys.FirstOrDefault(k => k.Contains(kind) && Has(k, h.Side)) ?? pieces.Keys.FirstOrDefault(k => Has(k, h.Side));
                if (alt != null) return alt;
            }
            return "relaxed";
        }

        bool Has(string pose, string side) => poses.TryGetValue(pose, out var p) && p is Dictionary<string, object> ps && ps.ContainsKey(side);

        float Target(string pose, string side, int f, int k)
        {
            if (!poses.TryGetValue(pose, out var p) || !(p is Dictionary<string, object> ps) || !ps.TryGetValue(side, out var s) || !(s is Dictionary<string, object> fs)) return 0;
            if (!fs.TryGetValue(Names[f], out var a) || !(a is List<object> l) || l.Count < 3) return 0;
            return System.Convert.ToSingle(l[k]);
        }

        void LateUpdate()
        {
            float t = 1 - Mathf.Exp(-Time.deltaTime / EaseTime);
            foreach (var h in hands)
            {
                h.Pose = Choose(h);
                for (int f = 0; f < 5; f++)
                    for (int k = 0; k < 3; k++)
                    {
                        h.Now[f, k] = Mathf.Lerp(h.Now[f, k], Target(h.Pose, h.Side, f, k), t);
                        h.Bones[f, k].localRotation = h.Rest[f, k] * Quaternion.AngleAxis(h.Now[f, k], h.Axis[f, k]);
                    }
            }
        }
    }
}
