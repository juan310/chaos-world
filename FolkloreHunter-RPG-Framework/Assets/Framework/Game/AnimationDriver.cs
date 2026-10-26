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

        readonly Dictionary<AnimationState, float> frozenSpeeds = new Dictionary<AnimationState, float>();
        public bool Frozen { get; private set; }

        /// <summary>Hitstop: stops every playing clip where it is, and resumes each at its own speed.</summary>
        public void Freeze(bool on)
        {
            if (on == Frozen) return;
            Frozen = on;
            if (on) { frozenSpeeds.Clear(); foreach (AnimationState s in Anim) if (s.enabled) { frozenSpeeds[s] = s.speed; s.speed = 0; } }
            else { foreach (var kv in frozenSpeeds) if (kv.Key) kv.Key.speed = kv.Value; frozenSpeeds.Clear(); }
        }

        /// <summary>Plays a one-shot backwards from its end (a fall played in reverse is a get-up).</summary>
        public void PlayReverse(string name, float speed = 1, float fade = .1f)
        {
            var st = State(name);
            if (st == null) return;
            st.wrapMode = WrapMode.ClampForever; st.speed = -Mathf.Abs(speed); st.time = st.length;
            Anim.CrossFade(st.name, fade);
            Current = st.name;
        }

        /// <summary>Shows one frame of a clip and keeps it there (a charge-up holds the raised sword).</summary>
        public void Hold(string name, float normalized, float fade = .1f)
        {
            var st = State(name);
            if (st == null) return;
            st.wrapMode = WrapMode.ClampForever; st.speed = 0; st.time = st.length * normalized;
            if (Current != st.name) Anim.CrossFade(st.name, fade);
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
            const int N = 48;
            var z = new float[2, N + 1]; var y = new float[2, N + 1];
            st.enabled = true; st.weight = 1;
            for (int i = 0; i <= N; i++)
            {
                st.time = st.length * i / N;
                Anim.Sample();
                for (int f = 0; f < 2; f++)
                {
                    var p = space.InverseTransformPoint((f == 0 ? footL : footR).position);
                    z[f, i] = p.z * space.lossyScale.z; y[f, i] = p.y * space.lossyScale.y;
                }
            }
            st.enabled = false;
            RestorePose(saved);
            // Planted feet (within 2 cm of their lowest point) sweep backwards at the ground speed the clip was authored
            // for; that is exact for IK-planted clips whatever share of the cycle a foot is down (a gallop's is short).
            float sum = 0; int n = 0, dt = 0;
            float stride = 0;
            for (int f = 0; f < 2; f++)
            {
                float low = 1e9f, min = 1e9f, max = -1e9f;
                for (int i = 0; i <= N; i++) { low = Mathf.Min(low, y[f, i]); min = Mathf.Min(min, z[f, i]); max = Mathf.Max(max, z[f, i]); }
                stride += (max - min) * .5f;
                for (int i = 0; i < N; i++)
                    if (y[f, i] < low + .02f && y[f, i + 1] < low + .02f && z[f, i + 1] < z[f, i]) { sum += z[f, i] - z[f, i + 1]; dt++; n++; }
            }
            // Fallback for clips without a clean planted phase: each foot sweeps its stride over about half the cycle.
            float speed = n >= 6 ? sum / (dt * st.length / N) : stride / (st.length * .5f);
            strideCache[name] = speed;
            return speed;
        }

        // Sampling must isolate one clip: any other enabled state would blend in and distort the measurement.
        List<(AnimationState, bool, float, float)> savedStates;
        List<(Transform, Vector3, Quaternion)> SavePose()
        {
            savedStates = Anim.Cast<AnimationState>().Select(s => (s, s.enabled, s.weight, s.time)).ToList();
            foreach (var (s, _, _, _) in savedStates) { s.enabled = false; s.weight = 0; }
            return Anim.GetComponentsInChildren<Transform>().Select(t => (t, t.localPosition, t.localRotation)).ToList();
        }
        void RestorePose(List<(Transform, Vector3, Quaternion)> pose)
        {
            foreach (var (t, p, r) in pose) { t.localPosition = p; t.localRotation = r; }
            foreach (var (s, en, w, time) in savedStates) { s.enabled = en; s.weight = w; s.time = time; }
        }
    }
}
