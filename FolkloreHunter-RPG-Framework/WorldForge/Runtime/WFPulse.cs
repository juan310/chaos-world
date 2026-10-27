using UnityEngine;

namespace FH.WorldForge
{
    /// <summary>Magic lights bob and pulse; glowing eyes blink now and then. Drives the light's intensity and the glow
    /// objects' scale.</summary>
    public class WFPulse : MonoBehaviour
    {
        public bool Blink;
        public float Intensity = 1f, Bob = .15f, Rate = 1.3f;
        public Transform[] Glows = new Transform[0];
        Light lt; Vector3 home, scale0; float seed, nextBlink, blinkUntil;

        void Start()
        {
            lt = GetComponentInChildren<Light>(); home = transform.localPosition; seed = Random.value * 10;
            scale0 = Glows.Length > 0 && Glows[0] ? Glows[0].localScale : Vector3.one;
            nextBlink = Time.time + 2 + Random.value * 3;
        }

        void Update()
        {
            float k = 1;
            if (Blink)
            {
                if (Time.time > nextBlink) { blinkUntil = Time.time + .14f; nextBlink = Time.time + 2.5f + Random.value * 4; }
                k = Time.time < blinkUntil ? .05f : 1;
            }
            else
            {
                k = .8f + .2f * Mathf.Sin((Time.time + seed) * Rate * 2 * Mathf.PI);
                transform.localPosition = home + Vector3.up * Bob * Mathf.Sin((Time.time + seed) * Rate);
            }
            if (lt) lt.intensity = Intensity * k;
            foreach (var g in Glows) if (g) g.localScale = new Vector3(scale0.x, scale0.y * (Blink ? k : 1), scale0.z) * (Blink ? 1 : (.9f + .1f * k));
        }
    }
}
