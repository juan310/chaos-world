using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>A dodge-practice hazard in the Training Range. A slam pad: a red ring fills, then the hammer drops on
    /// the circle. A sweeping log: the log rises at one end of its lane, then sweeps along it. Either strikes the hunter
    /// as a monster blow does, so rolls and slides evade it (i-frames), shields guard it, and the katana's Foresight and
    /// Iai Spirit Slash counter it. Counts blows, evasions and hits taken.</summary>
    public class RangeHazard : MonoBehaviour
    {
        public enum Shape { Circle, Strip }
        public Shape Kind;
        public float Radius = 2.2f;                 // circle
        public Vector2 StripSize = new Vector2(10, 1.6f);   // strip: length along local X, width along local Z
        public float Period = 3.4f, Telegraph = 1f, Offset;
        public float Damage = 8;
        public Transform Striker, Warning;
        public static bool Enabled = true;
        public static readonly System.Collections.Generic.List<RangeHazard> All = new System.Collections.Generic.List<RangeHazard>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        public int Blows { get; private set; }
        public int Evaded { get; private set; }
        public int Struck { get; private set; }
        /// <summary>Seconds until the next blow lands (for the verifier and the overlay).</summary>
        public float UntilBlow => Period - Clock;
        public float Clock { get; private set; }

        GameSession S;
        Vector3 strikerRest;
        readonly MonsterAttackDef blow = new MonsterAttackDef { id = "range_hazard", damage = 8, reaction = "Flinch", weight = 1 };

        void Start()
        {
            if (Striker) strikerRest = Striker.localPosition;
            Clock = Offset % Period;
        }

        void Update()
        {
            if (!S) S = FindAnyObjectByType<GameSession>();
            bool live = S && S.Proving && S.InRange && Enabled && S.Simulating;
            if (Warning) Warning.gameObject.SetActive(live && Clock > Period - Telegraph);
            if (!live) return;
            float before = Clock;
            Clock += Time.deltaTime;
            float t = Mathf.Clamp01((Clock - (Period - Telegraph)) / Telegraph);
            if (Warning)
            {
                if (Kind == Shape.Circle) Warning.localScale = new Vector3(Radius * 2 * t, .02f, Radius * 2 * t);
                else Warning.localScale = new Vector3(StripSize.x * t, .02f, StripSize.y);
            }
            AnimateStriker(t);
            if (Clock >= Period) { Clock -= Period; Strike(); }
        }

        void AnimateStriker(float t)
        {
            if (!Striker) return;
            if (Kind == Shape.Circle) Striker.localPosition = strikerRest + Vector3.up * (2.2f * t);          // hammer rises, then drops
            else Striker.localPosition = strikerRest + new Vector3(-StripSize.x * .5f, .9f * t, 0);          // log rises at the near end
        }

        void Strike()
        {
            Blows++;
            var h = S.Hunter;
            if (Striker && Kind == Shape.Strip) StartCoroutine(Sweep());
            else if (Striker) Striker.localPosition = strikerRest;
            Vector3 c = transform.position;
            WorldKit.Burst(c + Vector3.up * .2f, new Color(.8f, .6f, .4f), 12);
            WorldKit.Sound(c, "hit", .3f);
            if (!h || h.Dead || !Covers(h.transform.position)) return;
            blow.damage = Damage;
            // The blow comes from the hammer's post (behind the pad), or for the log from the side it sweeps in from.
            Vector3 from = Kind == Shape.Circle ? c - transform.forward * (Radius + .8f) : h.transform.position - transform.right * 1.5f;
            bool evaded = h.Invulnerable;
            h.ReceiveHit(blow, from);
            if (evaded) Evaded++; else Struck++;
        }

        System.Collections.IEnumerator Sweep()
        {
            float t = 0;
            while (t < .25f && Striker)
            {
                t += Time.deltaTime;
                Striker.localPosition = strikerRest + new Vector3(Mathf.Lerp(-StripSize.x * .5f, StripSize.x * .5f, t / .25f), .3f, 0);
                yield return null;
            }
            if (Striker) Striker.localPosition = strikerRest;
        }

        /// <summary>Whether a point is inside the area the blow covers.</summary>
        public bool Covers(Vector3 p)
        {
            var local = transform.InverseTransformPoint(p);
            if (Kind == Shape.Circle) return new Vector2(local.x, local.z).magnitude <= Radius + .3f;
            return Mathf.Abs(local.x) <= StripSize.x * .5f && Mathf.Abs(local.z) <= StripSize.y * .5f + .3f;
        }

        public void ResetCount() { Blows = Evaded = Struck = 0; }
    }
}
