using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FH.Game
{
    public static class Layers
    {
        public const int Actor = 8, Review = 9, Hurtbox = 11, Map = 12;
        public const int ActorMask = 1 << Actor, HurtboxMask = 1 << Hurtbox;
        public const int WorldMask = ~((1 << Actor) | (1 << Hurtbox) | (1 << Review) | (1 << 2));
    }

    /// <summary>Geometry, material and effect helpers shared by the world builder and runtime (adapted from the 3D sample).</summary>
    public static class WorldKit
    {
        public static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        public static Material Mat(string name, Color color, float metal = 0)
        {
            if (Materials.TryGetValue(name, out var m) && m) return m;
            m = new Material(Shader.Find("Standard")) { name = name, color = color };
            m.SetFloat("_Glossiness", .18f);
            m.SetFloat("_Metallic", metal);
            Materials[name] = m;
            return m;
        }

        public static Material Glow(Color color, float intensity = 2)
        {
            var m = Mat("Glow " + ColorUtility.ToHtmlStringRGB(color) + intensity, color);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * intensity);
            return m;
        }

        public static Material Transparent(string name, Color color)
        {
            if (Materials.TryGetValue(name, out var m) && m) return m;
            m = new Material(Shader.Find("Standard")) { name = name, color = color };
            m.SetFloat("_Mode", 3);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = 3000;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(color.r, color.g, color.b) * .8f);
            Materials[name] = m;
            return m;
        }

        public static void RemoveCollider(GameObject o)
        {
            foreach (var c in o.GetComponents<Collider>()) { if (Application.isPlaying) Object.Destroy(c); else Object.DestroyImmediate(c); }
        }

        public static GameObject Box(string name, Vector3 pos, Vector3 size, Material mat, Transform parent = null, bool solid = false)
        {
            var o = GameObject.CreatePrimitive(PrimitiveType.Cube);
            o.name = name;
            o.transform.SetParent(parent, false);
            o.transform.position = pos;
            o.transform.localScale = size;
            o.GetComponent<Renderer>().sharedMaterial = mat;
            if (!solid) RemoveCollider(o);
            return o;
        }

        public static GameObject Cylinder(string name, Vector3 pos, float radius, float height, Material mat, Transform parent = null, bool solid = false)
        {
            var o = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            o.name = name;
            o.transform.SetParent(parent, false);
            o.transform.position = pos;
            o.transform.localScale = new Vector3(radius * 2, height * .5f, radius * 2);
            o.GetComponent<Renderer>().sharedMaterial = mat;
            if (!solid) RemoveCollider(o);
            return o;
        }

        public static GameObject Orb(string name, Vector3 pos, float radius, Material mat, Transform parent = null)
        {
            var o = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            o.name = name;
            o.transform.SetParent(parent, false);
            o.transform.position = pos;
            o.transform.localScale = Vector3.one * radius * 2;
            o.GetComponent<Renderer>().sharedMaterial = mat;
            RemoveCollider(o);
            return o;
        }

        public static Transform Find(Transform root, string name)
        {
            if (!root) return null;
            var all = root.GetComponentsInChildren<Transform>(true);
            return all.FirstOrDefault(t => t.name == name) ?? all.FirstOrDefault(t => t.name.EndsWith(name));
        }

        public static TextMesh Label(string text, Transform parent, Vector3 localPos, float size, Color color)
        {
            var t = new GameObject("Lettering").AddComponent<TextMesh>();
            t.transform.SetParent(parent, false);
            t.transform.localPosition = localPos;
            t.text = text; t.fontSize = 48; t.characterSize = size; t.color = color; t.anchor = TextAnchor.MiddleCenter;
            return t;
        }

        // ---- runtime effects ----
        static AudioClip hitClip, stepClip, chimeClip;

        public static void Burst(Vector3 p, Color color, int count = 9)
        {
            var mat = Glow(color);
            for (int i = 0; i < count; i++)
            {
                var s = Orb("Spark", p, Random.Range(.035f, .065f), mat);
                var fx = s.AddComponent<Spark>();
                fx.velocity = Random.onUnitSphere * Random.Range(.5f, 2.2f);
            }
        }

        public static void Sound(Vector3 p, string kind, float volume)
        {
            if (!Application.isPlaying) return;
            if (!hitClip)
            {
                const int rate = 22050;
                var rng = new System.Random(91);
                float[] a = new float[4400], b = new float[1800], c = new float[9000];
                for (int i = 0; i < a.Length; i++) { float t = i / (float)rate; a[i] = (float)((rng.NextDouble() * 2 - 1) * .5 + Mathf.Sin(t * 850)) * Mathf.Exp(-t * 28); }
                for (int i = 0; i < b.Length; i++) b[i] = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-i / 260f) * .5f;
                for (int i = 0; i < c.Length; i++) { float t = i / (float)rate; c[i] = (Mathf.Sin(t * 2 * Mathf.PI * 660) * .5f + Mathf.Sin(t * 2 * Mathf.PI * 990) * .3f) * Mathf.Exp(-t * 6); }
                hitClip = AudioClip.Create("Swish", a.Length, 1, rate, false); hitClip.SetData(a, 0);
                stepClip = AudioClip.Create("Step", b.Length, 1, rate, false); stepClip.SetData(b, 0);
                chimeClip = AudioClip.Create("Chime", c.Length, 1, rate, false); chimeClip.SetData(c, 0);
            }
            var o = new GameObject("Sound");
            o.transform.position = p;
            var src = o.AddComponent<AudioSource>();
            src.spatialBlend = .35f; src.volume = volume * AudioListener.volume;
            src.clip = kind == "swish" ? hitClip : kind == "chime" ? chimeClip : stepClip;
            src.pitch = kind == "hit" ? .7f : 1;
            src.Play();
            Object.Destroy(o, 1.5f);
        }

        public static void DamageNumber(Vector3 p, float amount, bool weak, bool crit)
        {
            if (!GameSession.ShowDamageNumbers) return;
            var go = new GameObject("Damage number");
            go.transform.position = p;
            var t = go.AddComponent<TextMesh>();
            t.text = Mathf.RoundToInt(amount) + (weak ? "!" : "");
            t.fontSize = 64; t.characterSize = weak || crit ? .05f : .038f; t.anchor = TextAnchor.MiddleCenter;
            t.color = weak ? new Color(1f, .78f, .25f) : crit ? new Color(1f, .45f, .3f) : new Color(.95f, .93f, .86f);
            go.AddComponent<FloatingText>();
        }
    }

}
