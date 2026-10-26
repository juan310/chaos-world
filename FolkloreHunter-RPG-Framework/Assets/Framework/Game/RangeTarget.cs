using System.Collections.Generic;
using UnityEngine;

namespace FH.Game
{
    /// <summary>An archery target in the Training Range: counts arrows that strike its face and the ones inside the
    /// gold (the inner third), and shows them on its sign.</summary>
    public class RangeTarget : MonoBehaviour
    {
        public static readonly List<RangeTarget> All = new List<RangeTarget>();
        public float Distance;          // metres from the shooting line
        public float FaceRadius = 1f;
        public Transform Face;
        public TextMesh Sign;
        public int Hits { get; private set; }
        public int Golds { get; private set; }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        /// <summary>An arrow struck the target at <paramref name="point"/>.</summary>
        public void Struck(Vector3 point)
        {
            Hits++;
            var c = Face ? Face.position : transform.position;
            bool gold = Vector3.Distance(point, c) <= FaceRadius / 3f;
            if (gold) Golds++;
            WorldKit.Burst(point, gold ? new Color(1f, .85f, .3f) : new Color(.95f, .9f, .8f), gold ? 10 : 5);
            WorldKit.Sound(point, gold ? "chime" : "hit", gold ? .25f : .12f);
            Refresh();
        }

        public void ResetCount() { Hits = Golds = 0; Refresh(); }

        void Refresh() { if (Sign) Sign.text = Distance.ToString("0") + " m\nhits " + Hits + "  gold " + Golds; }
    }
}
