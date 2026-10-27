using UnityEngine;

namespace FH.WorldForge
{
    /// <summary>A fire's light: intensity and range breathing on two noise rates, the light itself wobbling a few
    /// centimetres (moving shadows), never below 70 % or above 115 % of its set intensity.</summary>
    public class WFFlicker : MonoBehaviour
    {
        public float Intensity = 1.7f, Range = 8f, Amount = 1f;
        Light lt; Vector3 home; float seed;

        void Start() { lt = GetComponent<Light>(); home = transform.localPosition; seed = Random.value * 100; }

        void Update()
        {
            if (!lt) return;
            float t = Time.time + seed;
            float n = Mathf.PerlinNoise(t * 7.3f, seed) * .65f + Mathf.PerlinNoise(t * 19.1f, seed + 4) * .35f;
            lt.intensity = Intensity * (1 + Amount * (n - .5f) * .45f);
            lt.range = Range * (1 + Amount * (n - .5f) * .08f);
            transform.localPosition = home + Amount * .035f * new Vector3(Mathf.PerlinNoise(t * 5, seed + 9) - .5f, Mathf.PerlinNoise(t * 4.4f, seed + 2) - .5f, Mathf.PerlinNoise(t * 5.6f, seed + 7) - .5f);
        }
    }
}
