using UnityEngine;

namespace FH.Game
{
    /// <summary>Brief slow motion (the perfect evade). The menus still pause the world (time scale 0); when they close,
    /// time resumes at the warp's scale while it lasts. The last 0.15 s eases back to normal speed.</summary>
    public static class TimeWarp
    {
        static float until = -1, scale = 1;
        public static bool Active => until > 0;
        public static int Count { get; private set; }
        /// <summary>The time scale the world should run at when nothing pauses it.</summary>
        public static float Scale
        {
            get
            {
                if (!Active) return 1;
                float left = until - Time.unscaledTime;
                return left < .15f ? Mathf.Lerp(1, scale, Mathf.Clamp01(left / .15f)) : scale;
            }
        }

        public static void Slow(float timeScale, float realSeconds)
        {
            scale = timeScale; until = Time.unscaledTime + realSeconds; Count++;
            if (Time.timeScale > 0) Time.timeScale = Scale;
        }

        /// <summary>Called every frame: follows the ease and ends the warp on time.</summary>
        public static void Tick()
        {
            if (!Active) return;
            if (Time.unscaledTime >= until) until = -1;
            if (Time.timeScale > 0) Time.timeScale = Scale;
        }

        public static void Clear() { until = -1; if (Time.timeScale > 0) Time.timeScale = 1; }
    }
}
