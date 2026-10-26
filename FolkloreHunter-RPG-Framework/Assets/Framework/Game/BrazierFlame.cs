using UnityEngine;

namespace FH.Game
{
    /// <summary>A brazier's light flickers like the fire under it.</summary>
    public class BrazierFlame : MonoBehaviour
    {
        public Light Light;
        float baseIntensity, seed;

        void Start() { if (Light) baseIntensity = Light.intensity; seed = Random.value * 100; }

        void Update()
        {
            if (!Light) return;
            float n = Mathf.PerlinNoise(seed, Time.time * 6f);
            Light.intensity = baseIntensity * (.78f + .4f * n);
        }
    }
}
