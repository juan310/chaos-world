using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FH.WorldForge
{
    /// <summary>The playable preview: reads a recipe (--wf-recipe &lt;file&gt;, else StreamingAssets/WorldForge/recipe.json),
    /// builds the world, puts the test hunter at the start of the route with the follow camera, and lets you walk it.
    /// Keys: WASD / arrows move (Shift sprint, Ctrl walk), mouse turns the camera (wheel: distance), F1 quality
    /// (Low / Medium / High), F2 time of day (day / dusk / night), F3 weather (clear / fog / rain), F5 reload the recipe
    /// file and rebuild (edit the JSON, press F5), Tab hides this help, Esc quits. With --wf-test the automated test
    /// (WFTester) runs instead and quits when done (--wf-quit).</summary>
    public class WFPreview : MonoBehaviour
    {
        public WFWorld World; public WFWalker Walker; public WFCamera Cam;
        public string RecipePath;
        string status = "Generating...", weather = "clear"; bool help = true, building;
        float fps = 60;

        void Awake()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--wf-recipe");
            RecipePath = i >= 0 && i + 1 < args.Length ? Path.GetFullPath(args[i + 1]) : Path.Combine(Application.streamingAssetsPath, "WorldForge", "recipe.json");
            Application.targetFrameRate = args.Contains("--wf-test") ? -1 : 144;
            QualitySettings.vSyncCount = args.Contains("--wf-test") ? 0 : 1;
        }

        IEnumerator Start()
        {
            var camGo = Camera.main ? Camera.main.gameObject : new GameObject("Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            Cam = camGo.GetComponent<WFCamera>(); if (!Cam) Cam = camGo.AddComponent<WFCamera>();
            yield return Rebuild(true);
            if (Environment.GetCommandLineArgs().Contains("--wf-test"))
            {
                help = false;
                var t = gameObject.AddComponent<WFTester>(); t.World = World; t.Walker = Walker; t.Cam = Cam;
                StartCoroutine(Watchdog(900));
                int o = Array.IndexOf(Environment.GetCommandLineArgs(), "--wf-out");
                if (o >= 0) t.Out = Path.GetFullPath(Environment.GetCommandLineArgs()[o + 1]);
            }
        }

        /// <summary>A test that stops part way (an error in a check) still ends the player, so nothing is left running.</summary>
        IEnumerator Watchdog(float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds) yield return null;
            Debug.LogError("WF_TEST watchdog: the test did not finish in " + seconds + " s");
            Application.Quit(3);
        }

        IEnumerator Rebuild(bool spawn)
        {
            building = true; status = "Generating " + Path.GetFileName(RecipePath) + "...";
            WFRecipe r;
            try { r = WFRecipe.FromJson(File.ReadAllText(RecipePath)); }
            catch (Exception e) { status = "Recipe unreadable: " + e.Message; building = false; yield break; }
            if (!World) World = new GameObject("WorldForge").AddComponent<WFWorld>();
            yield return null;
            yield return World.Build(r);
            if (!World.Ready) { status = "Generation failed (see the log)"; building = false; yield break; }
            if (spawn || !Walker)
            {
                Vector3 at = Vector3.zero; float yaw = 0;
                var first = r.test.route.Count > 0 ? r.Feature(r.test.route[0]) : null;
                if (first != null)
                {
                    at = first.points[0];
                    if (first.points.Count > 1) { var d = first.points[1] - first.points[0]; yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }
                }
                if (Physics.Raycast(at + Vector3.up * 3, Vector3.down, out var h, 10)) at = h.point;
                if (Walker) Destroy(Walker.gameObject);
                Walker = WFWalker.Spawn(at, yaw);
            }
            Walker.CameraT = Cam.transform; Cam.Target = Walker.transform; Cam.Yaw = Walker.transform.eulerAngles.y;
            World.AttachRain(Cam.transform);
            status = $"{r.name}: {World.Result.Triangles:N0} triangles in {World.Result.Meshes.Count} chunks, built in {World.BuildSeconds:0.0} s";
            building = false;
        }

        void Update()
        {
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(1e-4f, Time.unscaledDeltaTime), .05f);
            if (building || !World || !World.Ready || GetComponent<WFTester>()) return;
            if (Input.GetKeyDown(KeyCode.Escape)) Application.Quit();
            if (Input.GetKeyDown(KeyCode.Tab)) help = !help;
            if (Input.GetKeyDown(KeyCode.F1)) World.SetQuality(WFQuality.Current == "Low" ? "Medium" : WFQuality.Current == "Medium" ? "High" : "Low");
            if (Input.GetKeyDown(KeyCode.F2))
            {
                var t = World.Recipe.atmosphere.time; t = t == "day" ? "dusk" : t == "dusk" ? "night" : "day";
                var a = World.Recipe.atmosphere; World.SetWeather(t, a.rain, a.fog, a.mist, Cam.transform);
            }
            if (Input.GetKeyDown(KeyCode.F3))
            {
                weather = weather == "clear" ? "fog" : weather == "fog" ? "rain" : "clear";
                var a = World.Recipe.atmosphere;
                World.SetWeather(a.time, weather == "rain" ? .8f : 0, weather == "fog" ? .045f : .012f, weather == "fog" ? 1f : .35f, Cam.transform);
            }
            if (Input.GetKeyDown(KeyCode.F5)) StartCoroutine(Rebuild(false));
        }

        void OnGUI()
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 15 }; st.normal.textColor = Color.white;
            GUI.Label(new Rect(14, 10, 1400, 24), status + $"   |   {fps:0} fps   |   quality {WFQuality.Current}, {(World && World.Recipe != null ? World.Recipe.atmosphere.time : "")}, {weather}", st);
            if (!help) return;
            GUI.Label(new Rect(14, 34, 1400, 24), "WASD move (Shift sprint, Ctrl walk)  ·  mouse: camera  ·  F1 quality  ·  F2 time of day  ·  F3 weather  ·  F5 reload the recipe and rebuild  ·  Tab hide  ·  Esc quit", st);
        }
    }
}
