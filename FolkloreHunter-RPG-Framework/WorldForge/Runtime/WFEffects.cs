using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FH.WorldForge
{
    /// <summary>Lights, fire, water and air, made in code with Unity lights, particle systems and the WorldForge shaders:
    /// torches, braziers, campfires (flickering light, flame, sparks, smoke), magic lights, glowing eyes, puddles and
    /// pools and rivers, mist, dust, drips with splashes, rain, and light shafts. Their amounts follow the quality preset
    /// (WFQuality: particle counts, torch shadows).</summary>
    public static class WFEffects
    {
        public static WFTextures.Set Tex;
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        public static Material Mat(string key, string shader, Texture tex = null, Color? color = null)
        {
            if (mats.TryGetValue(key, out var m) && m) return m;
            var sh = Shader.Find(shader);
            if (!sh) { Debug.LogError("WorldForge: shader missing " + shader); sh = Shader.Find("Hidden/InternalErrorShader"); }
            m = new Material(sh) { name = "WorldForge " + key };
            if (tex) m.mainTexture = tex;
            if (color.HasValue && m.HasProperty("_Color")) m.color = color.Value;
            mats[key] = m;
            return m;
        }

        public static void Kill(Object o) { if (!o) return; if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }

        static GameObject Part(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Quaternion rot, Material m)
        {
            var g = GameObject.CreatePrimitive(t);
            Kill(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localRotation = rot; g.transform.localScale = scale;
            var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = m;
            return g;
        }

        static Material Prop(string key, Color c, float smooth = .2f, float metal = 0, Color? glow = null)
        {
            var m = Mat("prop_" + key, "WorldForge/Prop", Texture2D.whiteTexture, c);
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal); m.SetColor("_Glow", glow ?? Color.black);
            return m;
        }

        // ------------------------------------------------------------------------------------------------- particles
        static ParticleSystem PS(string name, Transform parent, Vector3 pos, Material mat, int max)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            var main = ps.main; main.loop = true; main.playOnAwake = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(4, Mathf.RoundToInt(max * WFQuality.Particles));
            return ps;
        }

        static void Fade(ParticleSystem ps, Color a, Color b, float peak = .15f)
        {
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, peak), new GradientAlphaKey(.6f, .6f), new GradientAlphaKey(0, 1) });
            col.color = g;
        }

        static void Grow(ParticleSystem ps, float from, float to)
        {
            var s = ps.sizeOverLifetime; s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, from, 1, to));
        }

        public static void Fire(Transform at, float size = 1f, float smoke = 1f)
        {
            float q = WFQuality.Particles;
            var flame = PS("Flame", at, Vector3.zero, Mat("flame", "WorldForge/ParticleAdd", Tex.Flame), 70);
            var m = flame.main; m.startLifetime = new ParticleSystem.MinMaxCurve(.35f, .6f); m.startSpeed = new ParticleSystem.MinMaxCurve(.35f * size, .8f * size);
            m.startSize = new ParticleSystem.MinMaxCurve(.17f * size, .32f * size); m.startColor = new ParticleSystem.MinMaxGradient(new Color(1, .78f, .42f), new Color(1, .5f, .18f));
            m.gravityModifier = -.05f; m.startRotation = new ParticleSystem.MinMaxCurve(-.3f, .3f);
            var em = flame.emission; em.rateOverTime = 30 * q * size;
            var sh = flame.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8; sh.radius = .05f * size;
            Fade(flame, new Color(1, .9f, .6f), new Color(1, .25f, .05f), .1f); Grow(flame, 1, .25f);
            var nz = flame.noise; nz.enabled = true; nz.strength = .25f * size; nz.frequency = 2.2f; nz.scrollSpeed = 1.2f;
            flame.Play();

            var sparks = PS("Sparks", at, Vector3.up * .1f * size, Mat("spark", "WorldForge/ParticleAdd", Tex.Spark), 40);
            m = sparks.main; m.startLifetime = new ParticleSystem.MinMaxCurve(.6f, 1.3f); m.startSpeed = new ParticleSystem.MinMaxCurve(.6f * size, 1.7f * size);
            m.startSize = new ParticleSystem.MinMaxCurve(.018f, .035f); m.startColor = new Color(1, .75f, .3f); m.gravityModifier = .12f;
            em = sparks.emission; em.rateOverTime = 3.5f * q * size;
            em.SetBursts(new[] { new ParticleSystem.Burst(.8f, 3, 6, 4, 1.7f) });
            sh = sparks.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 22; sh.radius = .04f;
            nz = sparks.noise; nz.enabled = true; nz.strength = .6f; nz.frequency = 1.5f;
            Fade(sparks, new Color(1, .9f, .5f), new Color(1, .3f, .05f), .05f);
            var sr = sparks.GetComponent<ParticleSystemRenderer>(); sr.renderMode = ParticleSystemRenderMode.Stretch; sr.lengthScale = 1.5f; sr.velocityScale = .06f;
            sparks.Play();

            if (smoke > 0)
            {
                var sm = PS("Smoke", at, Vector3.up * .3f * size, Mat("smoke", "WorldForge/ParticleAlpha", Tex.Smoke), 40);
                m = sm.main; m.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.6f); m.startSpeed = new ParticleSystem.MinMaxCurve(.25f, .55f);
                m.startSize = new ParticleSystem.MinMaxCurve(.25f * size, .45f * size); m.startColor = new Color(.16f, .15f, .14f, .32f * smoke);
                m.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f);
                em = sm.emission; em.rateOverTime = 5 * q * size * smoke;
                sh = sm.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 10; sh.radius = .06f;
                Fade(sm, new Color(.3f, .28f, .26f), new Color(.2f, .2f, .2f), .2f); Grow(sm, 1, 3.2f);
                nz = sm.noise; nz.enabled = true; nz.strength = .3f; nz.frequency = .6f;
                sm.Play();
            }
        }

        static Light PointLight(Transform at, Vector3 pos, WFLight spec, float intensityK = 1, float rangeK = 1, bool flicker = true, bool shadows = false)
        {
            var go = new GameObject("Light"); go.transform.SetParent(at, false); go.transform.localPosition = pos;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = spec.color;
            l.intensity = spec.intensity * intensityK; l.range = spec.range * rangeK;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None; l.shadowBias = .03f; l.shadowNormalBias = .4f;
            l.renderMode = LightRenderMode.ForcePixel;
            if (flicker) { var f = go.AddComponent<WFFlicker>(); f.Intensity = l.intensity; f.Range = l.range; f.Amount = spec.flicker; }
            return l;
        }

        // ------------------------------------------------------------------------------------------------- lights
        /// <summary>A torch on a wall: bracket, a stick leaning out, a pitch-soaked head, fire and a flickering light.
        /// at: the wall point; normal: out of the wall.</summary>
        public static GameObject Torch(Transform parent, Vector3 at, Vector3 normal, WFLight spec, bool shadows)
        {
            var t = new GameObject("Torch").transform; t.SetParent(parent, true);
            normal.y = 0; if (normal.sqrMagnitude < 1e-4f) normal = Vector3.forward;
            t.position = at; t.rotation = Quaternion.LookRotation(normal.normalized, Vector3.up);
            var iron = Prop("iron", new Color(.16f, .15f, .14f), .45f, .7f); var wood = Prop("wood", new Color(.32f, .2f, .11f), .15f);
            var cloth = Prop("cloth", new Color(.12f, .09f, .07f), .1f, 0, new Color(.25f, .07f, 0));
            Part(PrimitiveType.Cube, t, new Vector3(0, -.02f, .04f), new Vector3(.12f, .2f, .08f), Quaternion.identity, iron);
            Part(PrimitiveType.Cylinder, t, new Vector3(0, .02f, .1f), new Vector3(.09f, .03f, .09f), Quaternion.identity, iron);
            var lean = Quaternion.Euler(28, 0, 0);
            Part(PrimitiveType.Cylinder, t, new Vector3(0, .1f, .15f), new Vector3(.045f, .26f, .045f), lean, wood);
            var head = lean * new Vector3(0, .34f, 0) + new Vector3(0, .1f, .15f);
            Part(PrimitiveType.Cylinder, t, head, new Vector3(.085f, .07f, .085f), lean, cloth);
            var fire = new GameObject("Fire").transform; fire.SetParent(t, false); fire.localPosition = head + Vector3.up * .08f;
            Fire(fire, .85f, .8f);
            PointLight(t, fire.localPosition + Vector3.up * .15f + Vector3.forward * .15f, spec, 1, 1, true, shadows);
            return t.gameObject;
        }

        /// <summary>A brazier on the floor: three legs, an iron bowl of glowing coals, a big fire.</summary>
        public static GameObject Brazier(Transform parent, Vector3 floor, WFLight spec, bool shadows)
        {
            var t = new GameObject("Brazier").transform; t.SetParent(parent, true); t.position = floor;
            var iron = Prop("iron", new Color(.16f, .15f, .14f), .45f, .7f);
            for (int i = 0; i < 3; i++)
            {
                var d = Quaternion.Euler(0, i * 120, 0) * Vector3.forward * .22f;
                Part(PrimitiveType.Cylinder, t, d + Vector3.up * .45f, new Vector3(.04f, .45f, .04f), Quaternion.FromToRotation(Vector3.up, (Vector3.up * .9f - d * .6f).normalized), iron);
            }
            Part(PrimitiveType.Cylinder, t, Vector3.up * .95f, new Vector3(.62f, .09f, .62f), Quaternion.identity, iron);
            Part(PrimitiveType.Sphere, t, Vector3.up * 1.0f, new Vector3(.5f, .12f, .5f), Quaternion.identity, Prop("coals", new Color(.1f, .05f, .03f), .1f, 0, new Color(1.6f, .45f, .1f)));
            var fire = new GameObject("Fire").transform; fire.SetParent(t, false); fire.localPosition = Vector3.up * 1.05f;
            Fire(fire, 1.8f, 1.2f);
            PointLight(t, Vector3.up * 1.6f, spec, 1.3f, 1.4f, true, shadows);
            var col = t.gameObject.AddComponent<CapsuleCollider>(); col.center = Vector3.up * .55f; col.height = 1.1f; col.radius = .35f;
            return t.gameObject;
        }

        /// <summary>A campfire: a ring of stones, crossed logs, fire and smoke.</summary>
        public static GameObject Campfire(Transform parent, Vector3 floor, WFLight spec, bool shadows)
        {
            var t = new GameObject("Campfire").transform; t.SetParent(parent, true); t.position = floor;
            var stone = Prop("stone", new Color(.38f, .36f, .33f), .15f); var wood = Prop("charwood", new Color(.12f, .08f, .05f), .1f, 0, new Color(.5f, .12f, .02f));
            for (int i = 0; i < 9; i++)
            {
                var d = Quaternion.Euler(0, i * 40 + WFNoise.Hash(i) * 10, 0) * Vector3.forward * .55f;
                Part(PrimitiveType.Sphere, t, d + Vector3.up * .06f, new Vector3(.24f, .16f, .2f) * (.8f + .4f * WFNoise.Hash(i, 3)), Quaternion.Euler(0, i * 37, 0), stone);
            }
            for (int i = 0; i < 3; i++) Part(PrimitiveType.Cylinder, t, Vector3.up * .12f, new Vector3(.08f, .35f, .08f), Quaternion.Euler(75, i * 60, 0), wood);
            var fire = new GameObject("Fire").transform; fire.SetParent(t, false); fire.localPosition = Vector3.up * .15f;
            Fire(fire, 1.4f, 1.5f);
            PointLight(t, Vector3.up * .7f, spec, 1.2f, 1.3f, true, shadows);
            return t.gameObject;
        }

        /// <summary>A floating magic light: a glowing orb, motes round it, bobbing and pulsing.</summary>
        public static GameObject Magic(Transform parent, Vector3 at, WFLight spec)
        {
            var t = new GameObject("Magic light").transform; t.SetParent(parent, true); t.position = at;
            var orb = Part(PrimitiveType.Sphere, t, Vector3.zero, Vector3.one * .18f, Quaternion.identity, Prop("magic" + ColorUtility.ToHtmlStringRGB(spec.color), spec.color, .5f, 0, spec.color * 2.5f));
            var l = PointLight(t, Vector3.zero, spec, 1, 1, false);
            var motes = PS("Motes", t, Vector3.zero, Mat("glow", "WorldForge/ParticleAdd", Tex.Glow), 30);
            var m = motes.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f); m.startSpeed = .1f; m.startSize = new ParticleSystem.MinMaxCurve(.03f, .07f);
            m.startColor = spec.color; m.simulationSpace = ParticleSystemSimulationSpace.Local;
            var em = motes.emission; em.rateOverTime = 10 * WFQuality.Particles;
            var sh = motes.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = .35f;
            Fade(motes, spec.color, spec.color, .3f);
            motes.Play();
            var p = t.gameObject.AddComponent<WFPulse>(); p.Intensity = l.intensity; p.Glows = new[] { orb.transform };
            return t.gameObject;
        }

        /// <summary>A pair of glowing eyes in the dark, blinking now and then (a creature, or the boss to come).</summary>
        public static GameObject Eyes(Transform parent, Vector3 at, Vector3 facing, WFLight spec)
        {
            var t = new GameObject("Glowing eyes").transform; t.SetParent(parent, true); t.position = at;
            facing.y = 0; t.rotation = Quaternion.LookRotation(facing.sqrMagnitude > 1e-4f ? facing : Vector3.back);
            var glow = Prop("eyes" + ColorUtility.ToHtmlStringRGB(spec.color), Color.black, .8f, 0, spec.color * 4f);
            var a = Part(PrimitiveType.Sphere, t, new Vector3(-.11f, 0, 0), new Vector3(.08f, .05f, .03f), Quaternion.identity, glow);
            var b = Part(PrimitiveType.Sphere, t, new Vector3(.11f, 0, 0), new Vector3(.08f, .05f, .03f), Quaternion.identity, glow);
            var l = PointLight(t, Vector3.forward * .3f, spec, 1, 1, false);
            var p = t.gameObject.AddComponent<WFPulse>(); p.Blink = true; p.Intensity = l.intensity; p.Glows = new[] { a.transform, b.transform };
            return t.gameObject;
        }

        // ------------------------------------------------------------------------------------------------- water
        static Material Water(string key, WFWater spec, float rain)
        {
            var m = Mat("water_" + key, "WorldForge/Water", null, spec.color);
            m.SetTexture("_Nrm", Tex.WaterN); m.SetFloat("_Ripples", spec.ripples); m.SetFloat("_Rain", rain);
            return m;
        }

        /// <summary>A flat water disc at the given height; the ground around rises through it, so its outline is the
        /// ground's own low spot.</summary>
        public static GameObject Puddle(Transform parent, Vector3 at, float radius, WFWater spec, float rain)
        {
            var go = new GameObject("Puddle"); go.transform.SetParent(parent, true); go.transform.position = at;
            const int seg = 28, rings = 3;
            var v = new List<Vector3> { Vector3.zero }; var tri = new List<int>();
            for (int r = 1; r <= rings; r++)
                for (int s = 0; s < seg; s++) { float a = s * Mathf.PI * 2 / seg; v.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius * r / rings); }
            for (int s = 0; s < seg; s++) { tri.Add(0); tri.Add(1 + (s + 1) % seg); tri.Add(1 + s); }
            for (int r = 1; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = 1 + (r - 1) * seg + s, b = 1 + (r - 1) * seg + (s + 1) % seg, c = 1 + r * seg + s, d = 1 + r * seg + (s + 1) % seg;
                    tri.Add(a); tri.Add(b); tri.Add(d); tri.Add(a); tri.Add(d); tri.Add(c);
                }
            var mesh = new Mesh { name = "Puddle" }; mesh.SetVertices(v); mesh.SetTriangles(tri, 0);
            var n = new Vector3[v.Count]; var t = new Vector4[v.Count]; for (int i = 0; i < v.Count; i++) { n[i] = Vector3.up; t[i] = new Vector4(1, 0, 0, 1); }
            mesh.normals = n; mesh.tangents = t; mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Water(rain > .25f ? "rain" : "still", spec, rain); mr.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A river's surface along its channel, width wide at the given height, flowing along the path.</summary>
        public static GameObject River(Transform parent, WFSpline sp, float width, float level, WFWater spec, float rain)
        {
            var go = new GameObject("River water"); go.transform.SetParent(parent, true);
            var v = new List<Vector3>(); var tri = new List<int>(); var n = new List<Vector3>(); var tg = new List<Vector4>();
            for (int i = 0; i < sp.P.Count; i++)
            {
                var side = Vector3.Cross(Vector3.up, sp.Tangent(i)).normalized * width * .5f;
                var c = sp.P[i]; c.y = level;
                v.Add(c - side); v.Add(c + side); n.Add(Vector3.up); n.Add(Vector3.up); tg.Add(new Vector4(1, 0, 0, 1)); tg.Add(new Vector4(1, 0, 0, 1));
                if (i > 0) { int a = 2 * (i - 1); tri.AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2 }); }
            }
            var mesh = new Mesh { name = "River" }; mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTangents(tg); mesh.SetTriangles(tri, 0); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mat = new Material(Water("river", spec, rain)); var dir = sp.DirAt(sp.Length * .5f); mat.SetVector("_Flow", new Vector4(dir.x, 0, dir.z, 0) * .6f);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        // ------------------------------------------------------------------------------------------------- air
        /// <summary>Low drifting mist filling a box (a cave floor, a hollow).</summary>
        public static void Mist(Transform parent, Bounds box, float amount, Color color)
        {
            if (amount <= 0) return;
            var ps = PS("Mist", parent, box.center, Mat("mist", "WorldForge/ParticleAlpha", Tex.Smoke), 50);
            var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(9, 14); m.startSpeed = new ParticleSystem.MinMaxCurve(.03f, .12f);
            m.startSize = new ParticleSystem.MinMaxCurve(2.5f, 5f); m.startColor = new Color(color.r, color.g, color.b, .075f * amount); m.prewarm = true;
            m.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f);
            var em = ps.emission; em.rateOverTime = 4.5f * amount * WFQuality.Particles;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box.size;
            Fade(ps, color, color, .3f);
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial.SetFloat("_Soft", 1.2f);
            ps.Play();
        }

        /// <summary>Dust motes drifting in a volume (lit by a shaft or a torch).</summary>
        public static void Dust(Transform parent, Bounds box, float amount, Color color)
        {
            if (amount <= 0) return;
            var ps = PS("Dust", parent, box.center, Mat("dust", "WorldForge/ParticleAdd", Tex.Glow), 120);
            var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(5, 9); m.startSpeed = new ParticleSystem.MinMaxCurve(.01f, .05f);
            m.startSize = new ParticleSystem.MinMaxCurve(.012f, .03f); m.startColor = new Color(color.r, color.g, color.b, .5f); m.prewarm = true;
            var em = ps.emission; em.rateOverTime = 14 * amount * WFQuality.Particles;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box.size;
            var nz = ps.noise; nz.enabled = true; nz.strength = .05f; nz.frequency = .3f;
            Fade(ps, color, color, .3f);
            ps.Play();
        }

        /// <summary>Water dripping from a ceiling point and splashing where it lands.</summary>
        public static void Drip(Transform parent, Vector3 ceiling, float rate)
        {
            var ps = PS("Drip", parent, ceiling, Mat("streak", "WorldForge/ParticleAlpha", Tex.Streak), 8);
            var m = ps.main; m.startLifetime = 2.5f; m.startSpeed = 0; m.startSize = new ParticleSystem.MinMaxCurve(.02f, .03f);
            m.startColor = new Color(.7f, .8f, .85f, .7f); m.gravityModifier = 1;
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.enabled = false;
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = .025f; r.lengthScale = 1;
            var col = ps.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World; col.mode = ParticleSystemCollisionMode.Collision3D;
            col.quality = ParticleSystemCollisionQuality.Medium; col.bounce = 0; col.lifetimeLoss = 1; col.radiusScale = .5f;
            var splash = PS("Splash", ps.transform, Vector3.zero, Mat("glow_splash", "WorldForge/ParticleAdd", Tex.Glow), 30);
            var sm = splash.main; sm.loop = false; sm.playOnAwake = false; sm.startLifetime = new ParticleSystem.MinMaxCurve(.15f, .3f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(.4f, 1.1f); sm.startSize = new ParticleSystem.MinMaxCurve(.01f, .02f); sm.gravityModifier = 1.2f;
            sm.startColor = new Color(.5f, .55f, .6f, .6f);
            var sem = splash.emission; sem.rateOverTime = 0; sem.SetBursts(new[] { new ParticleSystem.Burst(0, 4, 7) });
            var ssh = splash.shape; ssh.shapeType = ParticleSystemShapeType.Hemisphere; ssh.radius = .02f;
            var sub = ps.subEmitters; sub.enabled = true; sub.AddSubEmitter(splash, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing);
            ps.Play();
        }

        /// <summary>Rain over a box that follows a target (the camera): streaks that stop on any surface (so none fall
        /// inside caves).</summary>
        public static GameObject Rain(Transform follow, float amount)
        {
            var ps = PS("Rain", follow, Vector3.up * 14, Mat("rain", "WorldForge/ParticleAlpha", Tex.Streak), 2500);
            var m = ps.main; m.startLifetime = 2.2f; m.startSpeed = new ParticleSystem.MinMaxCurve(10, 13); m.startSize = new ParticleSystem.MinMaxCurve(.015f, .025f);
            m.startColor = new Color(.75f, .8f, .88f, .38f);
            var em = ps.emission; em.rateOverTime = 1400 * amount * WFQuality.Particles;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(34, .5f, 34); sh.rotation = new Vector3(90, 0, 0);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(.4f); vel.y = new ParticleSystem.MinMaxCurve(-11f); vel.z = new ParticleSystem.MinMaxCurve(.2f);
            m.startSpeed = 0;
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = .04f; r.lengthScale = 1;
            var col = ps.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World; col.mode = ParticleSystemCollisionMode.Collision3D;
            col.quality = ParticleSystemCollisionQuality.Low; col.lifetimeLoss = 1; col.bounce = 0;
            ps.Play();
            return ps.gameObject;
        }

        /// <summary>A beam of light falling from an opening: a widening cone (Shaft shader) and a spot light along it.</summary>
        public static GameObject Shaft(Transform parent, Vector3 from, Vector3 dir, float length, float r0, float r1, Color color, float intensity, bool shadows)
        {
            var go = new GameObject("Light shaft"); go.transform.SetParent(parent, true); go.transform.position = from;
            go.transform.rotation = Quaternion.LookRotation(dir.normalized, Mathf.Abs(dir.normalized.y) > .9f ? Vector3.forward : Vector3.up);
            const int seg = 20; var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int s = 0; s <= seg; s++)
            {
                float a = s * Mathf.PI * 2 / seg; var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                v.Add(d * r0); v.Add(d * r1 + Vector3.forward * length); n.Add(d); n.Add(d); uv.Add(new Vector2(s / (float)seg, 0)); uv.Add(new Vector2(s / (float)seg, 1));
                if (s > 0) { int i = 2 * (s - 1); tri.AddRange(new[] { i, i + 1, i + 3, i, i + 3, i + 2 }); }
            }
            var mesh = new Mesh { name = "Shaft" }; mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mat = new Material(Mat("shaft", "WorldForge/Shaft")); mat.SetColor("_Color", new Color(color.r, color.g, color.b, .16f * intensity));
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var lgo = new GameObject("Shaft light"); lgo.transform.SetParent(go.transform, false);
            var l = lgo.AddComponent<Light>(); l.type = LightType.Spot; l.color = color; l.intensity = 1.6f * intensity; l.range = length * 1.4f;
            l.spotAngle = Mathf.Clamp(2 * Mathf.Atan2(r1, length) * Mathf.Rad2Deg * 1.3f, 20, 110); l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            Dust(go.transform, new Bounds(Vector3.forward * length * .5f, new Vector3(r1, r1, length * .8f)), 1, color);
            return go;
        }
    }

    /// <summary>Quality presets for testing performance: lights per pixel, shadows, particle amounts, reflections.</summary>
    public static class WFQuality
    {
        public static string Current = "Medium";
        public static float Particles = 1f;
        public static bool TorchShadows, Probes = true;

        public static void Apply(string q)
        {
            Current = q;
            switch (q)
            {
                case "Low":
                    QualitySettings.pixelLightCount = 3; QualitySettings.shadows = ShadowQuality.HardOnly; QualitySettings.shadowDistance = 45;
                    QualitySettings.shadowCascades = 2; QualitySettings.antiAliasing = 0; Particles = .4f; TorchShadows = false; Probes = false; break;
                case "High":
                    QualitySettings.pixelLightCount = 12; QualitySettings.shadows = ShadowQuality.All; QualitySettings.shadowDistance = 110;
                    QualitySettings.shadowCascades = 4; QualitySettings.antiAliasing = 4; Particles = 1.6f; TorchShadows = true; Probes = true; break;
                default:
                    Current = "Medium";
                    QualitySettings.pixelLightCount = 7; QualitySettings.shadows = ShadowQuality.All; QualitySettings.shadowDistance = 75;
                    QualitySettings.shadowCascades = 2; QualitySettings.antiAliasing = 2; Particles = 1f; TorchShadows = false; Probes = true; break;
            }
            QualitySettings.shadowResolution = q == "Low" ? ShadowResolution.Medium : ShadowResolution.High;
            QualitySettings.softParticles = true;
        }
    }
}
