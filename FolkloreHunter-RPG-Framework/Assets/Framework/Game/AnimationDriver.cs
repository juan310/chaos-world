using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Plays the KayKit authored clips through the legacy Animation component (as the sample did)
    /// and measures clip properties so gameplay timing can drive playback speed, not the other way round.</summary>
    public class AnimationDriver
    {
        public readonly Animation Anim;
        public string Current = "";
        readonly Dictionary<string, float> peakCache = new Dictionary<string, float>();
        readonly Dictionary<string, float> strideCache = new Dictionary<string, float>();

        public AnimationDriver(Transform visual, string resource)
        {
            Anim = visual.GetComponentInChildren<Animation>();
            if (!Anim) Anim = visual.gameObject.AddComponent<Animation>();
            foreach (var clip in Resources.LoadAll<AnimationClip>("Models/" + resource))
            {
                if (clip.name.StartsWith("__preview")) continue;
                clip.legacy = true;
                if (Anim.GetClip(clip.name) == null) Anim.AddClip(clip, clip.name);
            }
            Anim.cullingType = AnimationCullingType.AlwaysAnimate;
        }

        public AnimationState State(string needle)
        {
            if (string.IsNullOrEmpty(needle)) return null;
            return Anim.Cast<AnimationState>().Where(s => s.name.EndsWith(needle)).OrderBy(s => s.name.Length).FirstOrDefault()
                ?? Anim.Cast<AnimationState>().FirstOrDefault(s => s.name.Contains(needle));
        }

        public bool Has(string name) => State(name) != null;
        public float Length(string name) => State(name)?.length ?? .7f;

        public void Play(string name, bool loop = true, float speed = 1, bool restart = false, float fade = .12f)
        {
            var st = State(name);
            if (st == null) return;
            st.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
            st.speed = speed;
            if (Current == st.name && !restart) return;
            if (restart) st.time = 0;
            Anim.CrossFade(st.name, fade);
            Current = st.name;
        }

        /// <summary>Plays a one-shot so that its fastest motion (the strike) lands at <paramref name="impactAt"/> seconds.</summary>
        public void PlayStrike(string name, float impactAt, Transform tracked, float fade = .08f)
        {
            var st = State(name);
            if (st == null) return;
            float peak = PeakTime(name, tracked);
            float speed = Mathf.Clamp(peak * st.length / Mathf.Max(.05f, impactAt), .45f, 2.6f);
            Play(name, false, speed, true, fade);
        }

        /// <summary>Normalized time of the tracked bone's fastest movement in a clip (the swing's impact).</summary>
        public float PeakTime(string name, Transform tracked)
        {
            if (peakCache.TryGetValue(name, out var cached)) return cached;
            var st = State(name);
            if (st == null || tracked == null) return .45f;
            var root = Anim.transform;
            var saved = SavePose();
            float best = 0, bestT = .45f;
            Vector3 prev = Vector3.zero;
            const int steps = 40;
            st.enabled = true; st.weight = 1;
            for (int i = 0; i <= steps; i++)
            {
                st.time = st.length * i / steps;
                Anim.Sample();
                var p = root.InverseTransformPoint(tracked.position);
                if (i > 0)
                {
                    float v = (p - prev).magnitude;
                    float t = (i - .5f) / steps;
                    if (v > best && t > .1f && t < .9f) { best = v; bestT = t; }
                }
                prev = p;
            }
            st.enabled = false;
            RestorePose(saved);
            peakCache[name] = bestT;
            return bestT;
        }

        /// <summary>Ground speed implied by a locomotion clip, measured from foot travel in character space.</summary>
        public float NaturalSpeed(string name, Transform footL, Transform footR, Transform space)
        {
            if (strideCache.TryGetValue(name, out var cached)) return cached;
            var st = State(name);
            if (st == null || footL == null || footR == null) return 0;
            var saved = SavePose();
            float minL = 1e9f, maxL = -1e9f, minR = 1e9f, maxR = -1e9f;
            st.enabled = true; st.weight = 1;
            for (int i = 0; i <= 32; i++)
            {
                st.time = st.length * i / 32f;
                Anim.Sample();
                float zl = space.InverseTransformPoint(footL.position).z * space.lossyScale.z;
                float zr = space.InverseTransformPoint(footR.position).z * space.lossyScale.z;
                minL = Mathf.Min(minL, zl); maxL = Mathf.Max(maxL, zl);
                minR = Mathf.Min(minR, zr); maxR = Mathf.Max(maxR, zr);
            }
            st.enabled = false;
            RestorePose(saved);
            // Each foot sweeps its stride backwards while planted, about half the cycle.
            float stride = ((maxL - minL) + (maxR - minR)) * .5f;
            float speed = stride / (st.length * .5f);
            strideCache[name] = speed;
            return speed;
        }

        List<(Transform, Vector3, Quaternion)> SavePose() => Anim.GetComponentsInChildren<Transform>().Select(t => (t, t.localPosition, t.localRotation)).ToList();
        void RestorePose(List<(Transform, Vector3, Quaternion)> pose) { foreach (var (t, p, r) in pose) { t.localPosition = p; t.localRotation = r; } }
    }
}
