using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace FH.WorldForge
{
    /// <summary>Builds a whole environment from a recipe, under this object: the terrain chunks (mesh, ground material,
    /// mesh collider each), the sun or moon, sky, ambient light and fog, water (puddles found in the floor's own low spots,
    /// pools, rivers), the lights (torches mounted on the walls they find, braziers, campfires, magic lights, eyes), the
    /// air (mist, drips, light shafts through openings, dust) and reflection probes. Build() runs the heavy work on worker
    /// threads; BuildNow() is the same, finished at once (the editor). Rebuilding replaces everything it made before.</summary>
    public class WFWorld : MonoBehaviour
    {
        public WFRecipe Recipe;
        [Tooltip("The recipe file this world is made from (Load / Save in the inspector)")] public string RecipeFile = "";
        public WFField Field;
        public WFMesher.Result Result;
        public WFMesher.Sky Sky;
        public Transform Terrain, Props, Fx;
        public Material Ground;
        public Light Sun;
        public bool Ready;
        public float BuildSeconds;
        public readonly List<string> Notes = new List<string>();
        public readonly List<Light> FireLights = new List<Light>();
        public readonly List<GameObject> Mounted = new List<GameObject>();      // wall torches (for the attachment check)
        public readonly List<Vector4> PuddleSpots = new List<Vector4>();       // xyz, radius
        public readonly List<ReflectionProbe> ProbesMade = new List<ReflectionProbe>();
        GameObject rain;

        void Note(string s) { lock (Notes) Notes.Add(s); Debug.Log("WorldForge: " + s); }

        public void Clear()
        {
            foreach (Transform c in transform.Cast<Transform>().ToList()) WFEffects.Kill(c.gameObject);
            Notes.Clear(); FireLights.Clear(); Mounted.Clear(); PuddleSpots.Clear(); ProbesMade.Clear(); Ready = false;
        }

        public void BuildNow(WFRecipe r)
        {
            var e = Build(r);
            while (e.MoveNext()) Thread.Sleep(5);
        }

        public IEnumerator Build(WFRecipe r)
        {
            Clear(); Recipe = r; float t0 = Time.realtimeSinceStartup;
            WFQuality.Apply(r.quality);
            Field = new WFField(r);
            WFMesher.Result res = null; Exception err = null;
            var task = Task.Run(() => { try { res = WFMesher.Build(Field, Note); } catch (Exception e) { err = e; } });
            WFEffects.Tex = WFTextures.Make(r.palette, r.seed);
            while (!task.IsCompleted) yield return null;
            if (err != null) { Debug.LogException(err); yield break; }
            Result = res; Sky = res.Sky; Result.MakeMeshes();
            MakeTerrain();
            Physics.SyncTransforms();
            Atmosphere(r.atmosphere.time);
            Waters();
            Physics.SyncTransforms();
            Lights();
            Air();
            yield return null;
            Probes();
            baseRates.Clear();
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true)) baseRates[ps] = ps.emission.rateOverTimeMultiplier / Mathf.Max(.01f, WFQuality.Particles);
            BuildSeconds = Time.realtimeSinceStartup - t0;
            Ready = true;
            Note($"built '{r.name}' in {BuildSeconds:0.0} s: {Result.Meshes.Count} chunks, {Result.Triangles:N0} triangles, {FireLights.Count} fire lights, {PuddleSpots.Count} puddles");
        }

        Transform Group(string n)
        {
            var g = new GameObject(n).transform; g.SetParent(transform, false);
            if (!Application.isPlaying) g.gameObject.hideFlags = HideFlags.DontSave;     // regenerated, never saved into the scene
            return g;
        }

        void MakeTerrain()
        {
            Terrain = Group("Terrain"); Props = Group("Props"); Fx = Group("Effects");
            var t = WFEffects.Tex;
            Ground = new Material(Shader.Find("WorldForge/Ground")) { name = "WorldForge ground" };
            Ground.SetTexture("_RockTex", t.Rock); Ground.SetTexture("_RockNrm", t.RockN); Ground.SetTexture("_DirtTex", t.Dirt); Ground.SetTexture("_DirtNrm", t.DirtN);
            Ground.SetTexture("_MossTex", t.Moss); Ground.SetTexture("_MossNrm", t.MossN); Ground.SetTexture("_RuinTex", t.Ruin); Ground.SetTexture("_RuinNrm", t.RuinN);
            Ground.SetFloat("_Tiling", Recipe.palette.tiling); Ground.SetFloat("_Wet", Recipe.atmosphere.rain * .5f);
            for (int i = 0; i < Result.Meshes.Count; i++)
            {
                var go = new GameObject("Chunk " + i); go.transform.SetParent(Terrain, false); go.isStatic = true;
                go.AddComponent<MeshFilter>().sharedMesh = Result.Meshes[i];
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Ground; mr.shadowCastingMode = ShadowCastingMode.TwoSided;
                go.AddComponent<MeshCollider>().sharedMesh = Result.Meshes[i];
            }
        }

        // ------------------------------------------------------------------------------------------------- sky, sun
        public void Atmosphere(string time)
        {
            var a = Recipe.atmosphere; a.time = time;
            if (!Sun)
            {
                var g = new GameObject(time == "night" ? "Moon" : "Sun"); g.transform.SetParent(transform, false);
                Sun = g.AddComponent<Light>(); Sun.type = LightType.Directional;
            }
            Sun.shadows = LightShadows.Soft; Sun.shadowStrength = 1; Sun.shadowBias = .04f; Sun.shadowNormalBias = .5f;
            var sky = new Material(Shader.Find("WorldForge/Sky")) { name = "WorldForge sky" };
            Color top, hor, gnd, sunC, amb, eq, low, fog; float inten, stars = 0; Vector3 ang = a.sunAngles;
            switch (time)
            {
                case "dusk":
                    ang = new Vector3(11, a.sunAngles.y + 40, 0); sunC = new Color(1f, .58f, .34f); inten = .95f;
                    top = new Color(.22f, .26f, .42f); hor = new Color(.95f, .55f, .36f); gnd = new Color(.16f, .12f, .11f);
                    amb = new Color(.36f, .32f, .38f); eq = new Color(.42f, .30f, .26f); low = new Color(.12f, .10f, .09f); fog = new Color(.52f, .40f, .38f); break;
                case "night":
                    ang = new Vector3(48, a.sunAngles.y - 30, 0); sunC = new Color(.55f, .66f, 1f); inten = .28f; stars = 1;
                    top = new Color(.01f, .015f, .04f); hor = new Color(.06f, .08f, .14f); gnd = new Color(.02f, .02f, .025f);
                    amb = new Color(.07f, .09f, .15f); eq = new Color(.05f, .06f, .09f); low = new Color(.02f, .02f, .03f); fog = new Color(.04f, .05f, .08f); break;
                default:
                    sunC = new Color(1f, .95f, .86f); inten = 1.25f;
                    top = new Color(.28f, .46f, .78f); hor = new Color(.74f, .80f, .87f); gnd = new Color(.26f, .25f, .23f);
                    amb = new Color(.50f, .58f, .70f); eq = new Color(.46f, .46f, .43f); low = new Color(.22f, .20f, .18f); fog = new Color(.64f, .70f, .77f); break;
            }
            Sun.transform.rotation = Quaternion.Euler(ang); Sun.color = sunC; Sun.intensity = inten; Sun.name = time == "night" ? "Moon" : "Sun";
            sky.SetColor("_Top", top); sky.SetColor("_Horizon", hor); sky.SetColor("_Ground", gnd); sky.SetColor("_SunColor", sunC * (time == "night" ? .6f : 1f));
            sky.SetVector("_SunDir", -Sun.transform.forward); sky.SetFloat("_Stars", stars); sky.SetFloat("_SunSize", time == "night" ? .012f : .02f);
            RenderSettings.skybox = sky; RenderSettings.sun = Sun;
            RenderSettings.ambientMode = AmbientMode.Trilight; RenderSettings.ambientSkyColor = amb; RenderSettings.ambientEquatorColor = eq; RenderSettings.ambientGroundColor = low;
            RenderSettings.fog = a.fog > 0; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = a.fog; RenderSettings.fogColor = fog;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            DynamicGI.UpdateEnvironment();
        }

        // ------------------------------------------------------------------------------------------------- water
        bool Floor(Vector3 above, out RaycastHit hit, float down = 4) => Physics.Raycast(above, Vector3.down, out hit, down, ~0, QueryTriggerInteraction.Ignore);

        void Waters()
        {
            var group = Group("Water");
            foreach (var w in Recipe.water)
            {
                var f = Recipe.Feature(w.feature);
                switch (w.type)
                {
                    case "puddles":
                        if (f == null) { Note("puddles: no feature '" + w.feature + "'"); continue; }
                        foreach (var spot in LowSpots(f, w.count, w.radius))
                        {
                            float outdoor = Sky.Sample(spot + Vector3.up);
                            float rain = outdoor > .3f ? Recipe.atmosphere.rain : Recipe.atmosphere.drips * .35f;
                            WFEffects.Puddle(group, spot, w.radius * (.8f + .5f * WFNoise.Hash(PuddleSpots.Count, Recipe.seed)), w, rain);
                            PuddleSpots.Add(new Vector4(spot.x, spot.y, spot.z, w.radius));
                        }
                        break;
                    case "pool":
                        if (Floor(w.position + Vector3.up * 3, out var ph, 8)) { var at = ph.point + Vector3.up * (w.level > 0 ? w.level : .25f); WFEffects.Puddle(group, at, w.radius, w, Recipe.atmosphere.rain); PuddleSpots.Add(new Vector4(at.x, at.y, at.z, w.radius)); }
                        break;
                    case "river":
                        if (f == null) { Note("river: no feature '" + w.feature + "'"); continue; }
                        var sp = new WFSpline(f.points, f.curvature, new WFNoise(Recipe.seed * 31 + f.seed * 7 + f.name.GetHashCode()));
                        float level = sp.P[0].y - f.depth + (w.level > 0 ? w.level : f.depth * .45f);
                        WFEffects.River(group, sp, f.width * .95f, level, w, Recipe.atmosphere.rain);
                        break;
                }
            }
            if (PuddleSpots.Count > 0) Wetten();
        }

        /// <summary>The lowest spots of a feature's floor (rays down along it and across it), at least 3 m apart; water
        /// filled 3.5 cm above each.</summary>
        List<Vector3> LowSpots(WFFeature f, int count, float radius)
        {
            var pts = new List<Vector3>();
            var noise = new WFNoise(Recipe.seed * 31 + f.seed * 7 + f.name.GetHashCode());
            if (f.type == "chamber")
            {
                var c = f.points[0];
                for (float x = -f.radius * .8f; x <= f.radius * .8f; x += .6f)
                    for (float z = -f.radius * .8f; z <= f.radius * .8f; z += .6f)
                        if (x * x + z * z < f.radius * f.radius * .6f && Floor(c + new Vector3(x, 2, z), out var h, 4)) pts.Add(h.point);
            }
            else
            {
                var sp = new WFSpline(f.points, f.curvature, noise);
                for (float s = 2; s < sp.Length - 2; s += .5f)
                {
                    var q = sp.At(s); var side = Vector3.Cross(Vector3.up, sp.DirAt(s)).normalized;
                    for (float o = -f.width * .3f; o <= f.width * .3f; o += .5f)
                        if (Floor(q + side * o + Vector3.up * 1.5f, out var h, 3)) pts.Add(h.point);
                }
            }
            // depth below the neighbourhood's average
            var scored = new List<(Vector3 p, float depth)>();
            foreach (var p in pts)
            {
                float sum = 0; int n = 0;
                foreach (var q in pts) if ((q - p).sqrMagnitude < 2.25f) { sum += q.y; n++; }
                if (n > 4) scored.Add((p, sum / n - p.y));
            }
            var chosen = new List<Vector3>();
            foreach (var s in scored.OrderByDescending(x => x.depth))
            {
                if (chosen.Count >= count || s.depth < .005f) break;
                if (chosen.Any(c => (c - s.p).sqrMagnitude < 9)) continue;
                chosen.Add(s.p + Vector3.up * .035f);
            }
            return chosen;
        }

        /// <summary>The ground round each puddle darkened and glossy (the mesh's wetness).</summary>
        void Wetten()
        {
            foreach (var m in Result.Meshes)
            {
                var b = m.bounds; bool any = false;
                foreach (var s in PuddleSpots) if (b.SqrDistance(s) < s.w * s.w * 4) { any = true; break; }
                if (!any) continue;
                var v = m.vertices; var c = m.colors;
                for (int i = 0; i < v.Length; i++)
                    foreach (var s in PuddleSpots)
                    {
                        float d = Vector3.Distance(v[i], s) / (s.w * 1.6f);
                        if (d < 1) c[i].a = Mathf.Max(c[i].a, 1 - d * d * .6f);
                    }
                m.colors = c;
            }
        }

        // ------------------------------------------------------------------------------------------------- lights
        bool Wall(Vector3 from, Vector3 dir, float max, out RaycastHit hit) => Physics.Raycast(from, dir, out hit, max, ~0, QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.normal.y) < .75f;

        void Lights()
        {
            bool sh = WFQuality.TorchShadows;
            foreach (var l in Recipe.lights)
            {
                var f = Recipe.Feature(l.feature);
                switch (l.type)
                {
                    case "torch":
                        {
                            RaycastHit best = default; float bd = 99;
                            for (int a = 0; a < 16; a++)
                                if (Wall(l.position, Quaternion.Euler(0, a * 22.5f, 0) * Vector3.forward, 6, out var h) && h.distance < bd) { bd = h.distance; best = h; }
                            if (bd < 99) Mount(best, l, sh); else Note("torch '" + l.name + "': no wall within 6 m");
                            break;
                        }
                    case "torch_row":
                        {
                            if (f == null) { Note("torch_row: no feature '" + l.feature + "'"); break; }
                            var sp = new WFSpline(f.points, f.curvature, new WFNoise(Recipe.seed * 31 + f.seed * 7 + f.name.GetHashCode()));
                            int k = 0;
                            for (float s = l.offset; s < sp.Length - 1.5f; s += l.spacing, k++)
                            {
                                var q = sp.At(s); var side = Vector3.Cross(Vector3.up, sp.DirAt(s)).normalized * (k % 2 == 0 ? 1 : -1);
                                if (Floor(q + Vector3.up * 1.2f, out var fl, 3)) q = fl.point;
                                if (Wall(q + Vector3.up * l.mount, side, f.width * (1 + f.flare) * 1.6f, out var h)) Mount(h, l, sh);
                                else Note($"torch_row {l.feature}: no wall at {s:0.0} m");
                            }
                            break;
                        }
                    case "torch_pair":
                        {
                            if (f == null) break;
                            var sp = new WFSpline(f.points, f.curvature, new WFNoise(Recipe.seed * 31 + f.seed * 7 + f.name.GetHashCode()));
                            var q = sp.At(Mathf.Min(l.offset, sp.Length)); var side = Vector3.Cross(Vector3.up, sp.DirAt(0)).normalized;
                            if (Floor(q + Vector3.up * 1.2f, out var fl, 3)) q = fl.point;
                            foreach (var d in new[] { side, -side })
                                if (Wall(q + Vector3.up * l.mount, d, f.width * 2, out var h)) Mount(h, l, sh); else Note("torch_pair: no wall beside the mouth");
                            break;
                        }
                    case "brazier_ring":
                        {
                            if (f == null) break;
                            var c = f.points[0];
                            for (int i = 0; i < l.count; i++)
                            {
                                var p = c + Quaternion.Euler(0, 45 + i * 360f / l.count, 0) * Vector3.forward * f.radius * Mathf.Clamp(l.spacing, .2f, .9f);
                                if (Floor(p + Vector3.up * 2, out var h, 5)) FireLights.Add(WFEffects.Brazier(Props, h.point, l, sh).GetComponentInChildren<Light>());
                            }
                            break;
                        }
                    case "campfire":
                        if (Floor(l.position + Vector3.up * 2, out var cf, 6)) FireLights.Add(WFEffects.Campfire(Props, cf.point, l, sh).GetComponentInChildren<Light>());
                        break;
                    case "magic":
                        WFEffects.Magic(Props, l.position, l);
                        break;
                    case "eyes":
                        {
                            var start = Recipe.test.route.Count > 0 && Recipe.Feature(Recipe.test.route[0]) != null ? Recipe.Feature(Recipe.test.route[0]).points[0] : Vector3.zero;
                            WFEffects.Eyes(Props, l.position, start - l.position, l);
                            break;
                        }
                }
            }
        }

        void Mount(RaycastHit h, WFLight l, bool sh)
        {
            var t = WFEffects.Torch(Props, h.point + h.normal * .02f, h.normal, l, sh);
            Mounted.Add(t); FireLights.Add(t.GetComponentInChildren<Light>());
        }

        // ------------------------------------------------------------------------------------------------- air
        void Air()
        {
            var a = Recipe.atmosphere;
            var shaftColor = Sun ? Sun.color : Color.white;
            float shaftK = a.time == "night" ? .35f : a.time == "dusk" ? .7f : 1f;
            foreach (var f in Recipe.features)
            {
                var noise = new WFNoise(Recipe.seed * 31 + f.seed * 7 + f.name.GetHashCode());
                if (f.type == "tunnel" || f.type == "cave" || f.type == "chamber")
                {
                    var b = Field.All.First(x => x.F == f).Box;
                    var mist = new Bounds(new Vector3(b.center.x, (f.type == "chamber" ? f.points[0].y : b.min.y + 1.5f) + .7f, b.center.z), new Vector3(b.size.x * .6f, 1.2f, b.size.z * .6f));
                    WFEffects.Mist(Fx, mist, a.mist, new Color(.62f, .66f, .7f));
                    if (f.type == "chamber") WFEffects.Dust(Fx, new Bounds(f.points[0] + Vector3.up * f.height * .4f, new Vector3(f.radius, f.height * .6f, f.radius)), a.dust * .5f, new Color(1, .8f, .6f));
                }
                if ((f.type == "tunnel" || f.type == "cave") && a.drips > 0)
                {
                    var sp = new WFSpline(f.points, f.curvature, noise);
                    int n = Mathf.RoundToInt(sp.Length / 7f * a.drips);
                    for (int i = 0; i < n; i++)
                    {
                        float s = sp.Length * (i + .5f) / n; var q = sp.At(s) + Vector3.Cross(Vector3.up, sp.DirAt(s)).normalized * (WFNoise.Hash(i, f.seed) - .5f) * f.width * .5f;
                        if (Physics.Raycast(q + Vector3.up * .5f, Vector3.up, out var h, f.height * 2, ~0, QueryTriggerInteraction.Ignore))
                            WFEffects.Drip(Fx, h.point - Vector3.up * .05f, .4f + .6f * WFNoise.Hash(i + 4, f.seed));
                    }
                }
                if (!a.lightShafts) continue;
                if (f.type == "tunnel" || f.type == "cave")
                {
                    var sp = new WFSpline(f.points, f.curvature, noise);
                    var t = sp.DirAt(0);
                    var from = sp.P[0] - t * 1.5f + Vector3.up * f.height * .85f;
                    WFEffects.Shaft(Fx, from, (t + Vector3.down * .32f).normalized, Mathf.Min(13, sp.Length * .5f), f.width * .3f, f.width * .55f, shaftColor, shaftK, WFQuality.TorchShadows);
                }
                if (f.type == "hole" && f.points.Count > 1)
                {
                    Vector3 top = f.points[0].y > f.points[1].y ? f.points[0] : f.points[1], bot = f.points[0].y > f.points[1].y ? f.points[1] : f.points[0];
                    var dir = (bot - top).normalized; float len = Vector3.Distance(top, bot);
                    if (Physics.Raycast(bot, dir, out var fl, 30, ~0, QueryTriggerInteraction.Ignore)) len += fl.distance;
                    WFEffects.Shaft(Fx, top, dir, len, f.radius * .8f, f.radius * 1.7f, shaftColor, shaftK * 1.2f, WFQuality.TorchShadows);
                }
            }
        }

        /// <summary>Rain round a moving target (the camera), when the recipe asks for it.</summary>
        public void AttachRain(Transform follow)
        {
            if (rain) WFEffects.Kill(rain);
            if (Recipe != null && Recipe.atmosphere.rain > 0 && follow) rain = WFEffects.Rain(follow, Recipe.atmosphere.rain);
        }

        void Probes()
        {
            if (!WFQuality.Probes) return;
            var spots = new List<(Vector3, Vector3)>();
            if (Recipe.test.route.Count > 0 && Recipe.Feature(Recipe.test.route[0]) is WFFeature first) spots.Add((first.points[0] + Vector3.up * 2.5f, new Vector3(40, 30, 40)));
            foreach (var f in Recipe.features)
            {
                if (f.type == "chamber") spots.Add((f.points[0] + Vector3.up * f.height * .4f, new Vector3(f.radius * 2, f.height, (f.length > 0 ? f.length : f.radius) * 2)));
                if (f.type == "tunnel" || f.type == "cave")
                {
                    var sp = new WFSpline(f.points, f.curvature, new WFNoise(1));
                    for (float s = 6; s < sp.Length; s += 14) spots.Add((sp.At(s) + Vector3.up * f.height * .5f, new Vector3(f.width * 1.5f, f.height * 1.3f, 16)));
                }
            }
            var g = Group("Reflection probes");
            foreach (var (p, size) in spots)
            {
                var go = new GameObject("Probe"); go.transform.SetParent(g, false); go.transform.position = p;
                var rp = go.AddComponent<ReflectionProbe>(); rp.mode = ReflectionProbeMode.Realtime; rp.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                rp.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing; rp.size = size; rp.boxProjection = true; rp.resolution = 128;
                rp.intensity = 1; rp.importance = 1;
                if (Application.isPlaying) rp.RenderProbe();
                ProbesMade.Add(rp);
            }
        }

        public void RefreshProbes() { foreach (var p in ProbesMade) if (p) p.RenderProbe(); }

        readonly Dictionary<ParticleSystem, float> baseRates = new Dictionary<ParticleSystem, float>();

        /// <summary>A quality preset applied to what is already built: lights per pixel, shadows (the sun's, the fires'),
        /// particle amounts, reflection probes.</summary>
        public void SetQuality(string q)
        {
            WFQuality.Apply(q); Recipe.quality = WFQuality.Current;
            foreach (var l in FireLights) if (l) l.shadows = WFQuality.TorchShadows ? LightShadows.Soft : LightShadows.None;
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                var em = ps.emission;
                if (!baseRates.ContainsKey(ps)) baseRates[ps] = em.rateOverTimeMultiplier / Mathf.Max(.01f, WFQuality.Particles);
                em.rateOverTimeMultiplier = baseRates[ps] * WFQuality.Particles;
            }
            foreach (var p in ProbesMade) if (p) p.enabled = WFQuality.Probes;
            if (WFQuality.Probes) RefreshProbes();
        }

        /// <summary>Time of day and weather changed on the built world (sky, sun, fog, mist, drips, shafts, rain, wetness).</summary>
        public void SetWeather(string time, float rain, float fog, float mist, Transform follow)
        {
            var a = Recipe.atmosphere; a.rain = rain; a.fog = fog; a.mist = mist;
            Atmosphere(time);
            if (Ground) Ground.SetFloat("_Wet", rain * .5f);
            if (Fx) { WFEffects.Kill(Fx.gameObject); Fx = Group("Effects"); }
            Air();
            AttachRain(follow);
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true)) if (!baseRates.ContainsKey(ps)) baseRates[ps] = ps.emission.rateOverTimeMultiplier / Mathf.Max(.01f, WFQuality.Particles);
            if (WFQuality.Probes) RefreshProbes();
        }
    }
}
