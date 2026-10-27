using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace FH.Game
{
    /// <summary>"Something looks off": F9 (or Back + Start on the pad) saves the moment for SightForge
    /// (`eyes_inspect.py problem <folder>`) in Captures/Problems/problem_&lt;time&gt;/:
    ///   screen.png   what the player saw
    ///   state.json   the area, camera, the hunter (clip and its time, move, weapon, health, every bone, the ground under
    ///                each foot) and the monsters near it (mode, attack, bones), the springs, the latest log lines
    ///   hits.json    the latest hit tests (HitTrace) and every monster's hurtboxes now
    ///   perf.json    the latest frames: frame time, garbage, draw calls, batches, set-pass calls, triangles, spring lag,
    ///                and the events beside them (clip changes, moves, hits taken, area changes)
    /// The same recorders run through a verification run (--verify-*) with room for all of it; the verifier writes them
    /// at its end for SightForge's hits, perf and springs reports.</summary>
    public class DevCapture : MonoBehaviour
    {
        public static DevCapture I { get; private set; }
        public int FrameCapacity = 1800, EventCapacity = 600, LogCapacity = 200;
        public string LastFolder { get; private set; }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly Queue<string> frames = new Queue<string>(), events = new Queue<string>(), log = new Queue<string>();
        ProfilerRecorder gc, draws, batches, setpass, tris;
        string lastClip = "", lastEvent = "", lastArea = "";
        float lastHealth = -1;
        bool busy;

        void Awake()
        {
            I = this;
            if (GameSession.I && GameSession.I.Automated) { FrameCapacity = 80000; EventCapacity = 20000; LogCapacity = 1000; HitTrace.Capacity = 80000; }
            Application.logMessageReceived += OnLog;
        }

        void OnEnable()
        {
            gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        }

        void OnDisable() { gc.Dispose(); draws.Dispose(); batches.Dispose(); setpass.Dispose(); tris.Dispose(); }
        void OnDestroy() { Application.logMessageReceived -= OnLog; if (I == this) I = null; }

        void OnLog(string text, string stack, LogType type)
        {
            log.Enqueue(Time.time.ToString("0.00", Inv) + " " + type + " " + text.Split('\n')[0]);
            while (log.Count > LogCapacity) log.Dequeue();
        }

        void Event(string kind, string text)
        {
            events.Enqueue("{\"t\":" + F(Time.time) + ",\"frame\":" + Time.frameCount + ",\"kind\":\"" + kind + "\",\"text\":" + Q(text) + "}");
            while (events.Count > EventCapacity) events.Dequeue();
        }

        void Update()
        {
            if (!busy && (Input.GetKeyDown(KeyCode.F9) || (GamePad.Held(PadButton.Back) && GamePad.Down(PadButton.Start)) || (GamePad.Held(PadButton.Start) && GamePad.Down(PadButton.Back))))
                StartCoroutine(Capture(null, true));
            if (!busy && Input.GetKeyDown(KeyCode.F10) && GameSession.I && GameSession.I.Mode == SessionMode.Playing)
            {
                var area = TerrainSurvey.Around(GameSession.I.Hunter.transform.position);
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Captures", "Problems", "terrain_" + area.Id + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")));
                TerrainSurvey.Write(area, Path.Combine(dir, "terrain_" + area.Id + ".json"));
                GameSession.I.Hud?.Flash("Ground surveyed for review: " + area.Id);
            }
        }

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--survey-terrain");
            if (i < 0 || i + 1 >= args.Length) yield break;
            while (!GameSession.I || !GameSession.I.World) yield return null;
            for (int f = 0; f < 10; f++) yield return null;     // the world settled (estate tiers, doors)
            TerrainSurvey.WriteAll(Path.GetFullPath(args[i + 1]));
            if (args.Contains("--survey-terrain-quit")) Application.Quit();
        }

        void LateUpdate()
        {
            var s = GameSession.I;
            if (!s || s.Mode != SessionMode.Playing) return;
            var h = s.Hunter;
            string clip = h.Anim?.Current ?? "";
            if (clip != lastClip) { Event("clip", clip); lastClip = clip; }
            string ev = h.Hunt ? h.Hunt.LastEvent : "";
            if (ev != lastEvent) { Event("move", ev); lastEvent = ev; }
            if (s.CurrentArea != lastArea) { Event("area", s.CurrentArea); lastArea = s.CurrentArea; }
            if (lastHealth >= 0 && h.Health < lastHealth - .01f) Event("hurt", (lastHealth - h.Health).ToString("0.#", Inv) + " damage");
            lastHealth = h.Health;
            var sb = new StringBuilder(160);
            sb.Append("{\"t\":").Append(F(Time.time)).Append(",\"frame\":").Append(Time.frameCount).Append(",\"dt\":").Append(F(Time.unscaledDeltaTime * 1000))
              .Append(",\"gc\":").Append(Val(gc)).Append(",\"draws\":").Append(Val(draws)).Append(",\"batches\":").Append(Val(batches))
              .Append(",\"setpass\":").Append(Val(setpass)).Append(",\"tris\":").Append(Val(tris)).Append(",\"monsters\":").Append(s.Monsters.Count(m => m && m.isActiveAndEnabled));
            if (SpringBone.All.Count > 0)
            {
                sb.Append(",\"springs\":[");
                for (int i = 0; i < SpringBone.All.Count; i++) { if (i > 0) sb.Append(','); sb.Append(F(SpringBone.All[i].Lag)); }
                sb.Append(']');
            }
            frames.Enqueue(sb.Append('}').ToString());
            while (frames.Count > FrameCapacity) frames.Dequeue();
        }

        static string Val(ProfilerRecorder r) => r.Valid ? r.LastValue.ToString(Inv) : "null";

        /// <summary>Writes the recorders to a folder (default Captures/Problems/problem_&lt;time&gt;); with a screenshot when asked.</summary>
        public IEnumerator Capture(string folder, bool screenshot, string note = null)
        {
            busy = true;
            folder = folder ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Captures", "Problems", "problem_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")));
            Directory.CreateDirectory(folder);
            string state = State(note);           // the moment the key went down, before the screenshot's frame passes
            string hits = HitTrace.Json(), perf = Perf();
            if (screenshot)
            {
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(folder, "screen.png"), tex.EncodeToPNG());
                Destroy(tex);
            }
            File.WriteAllText(Path.Combine(folder, "state.json"), state, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "hits.json"), hits, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "perf.json"), perf, new UTF8Encoding(false));
            LastFolder = folder;
            Debug.Log("FH_CAPTURE " + folder);
            if (screenshot) GameSession.I?.Hud?.Flash("Moment saved for review: " + Path.GetFileName(folder));
            busy = false;
        }

        /// <summary>The same, at once and without a screenshot (the verifier's end of run).</summary>
        public void Write(string folder, string note = null)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "state.json"), State(note), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "hits.json"), HitTrace.Json(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "perf.json"), Perf(), new UTF8Encoding(false));
            LastFolder = folder;
            Debug.Log("FH_CAPTURE " + folder);
        }

        string Perf()
        {
            var sb = new StringBuilder(frames.Count * 120 + events.Count * 80 + 1024);
            sb.Append("{\"time\":").Append(F(Time.time)).Append(",\"targetFps\":").Append(Application.targetFrameRate)
              .Append(",\"quality\":").Append(Q(QualitySettings.names.Length > 0 ? QualitySettings.names[QualitySettings.GetQualityLevel()] : ""))
              .Append(",\"screen\":[").Append(Screen.width).Append(',').Append(Screen.height).Append(']')
              .Append(",\"gpu\":").Append(Q(SystemInfo.graphicsDeviceName)).Append(",\"springs\":[").Append(string.Join(",", SpringBone.All.Select(x => "{\"label\":" + Q(x.Label) + ",\"max\":" + F(x.MaxAngle) + "}"))).Append(']')
              .Append(",\"frames\":[\n").Append(string.Join(",\n", frames)).Append("\n],\"events\":[\n").Append(string.Join(",\n", events)).Append("\n]}");
            return sb.ToString();
        }

        string State(string note)
        {
            var s = GameSession.I;
            var o = new Dictionary<string, object> { ["time"] = Time.time, ["frame"] = Time.frameCount, ["note"] = note ?? "", ["build"] = Application.version, ["unity"] = Application.unityVersion };
            if (!s) return FH.Core.Json.Write(o);
            o["area"] = s.CurrentArea; o["mode"] = s.Mode.ToString(); o["menu"] = s.Ui && s.Ui.Top != null ? s.Ui.Top.GetType().Name : "";
            var cam = s.CameraRig ? s.CameraRig.Camera : Camera.main;
            if (cam) o["camera"] = new Dictionary<string, object> { ["position"] = Vec(cam.transform.position), ["forward"] = Vec(cam.transform.forward), ["up"] = Vec(cam.transform.up), ["fov"] = cam.fieldOfView, ["aspect"] = cam.aspect };
            var h = s.Hunter;
            if (h)
            {
                var hd = Actor(h.transform, h.Visual, h.Anim);
                hd["health"] = h.Health; hd["weapon"] = h.CurrentWeapon != null ? h.CurrentWeapon.id : "";
                hd["move"] = h.Hunt ? h.Hunt.LastMove : ""; hd["event"] = h.Hunt ? h.Hunt.LastEvent : "";
                hd["dodging"] = h.Dodging; hd["invulnerable"] = h.Invulnerable;
                o["hunter"] = hd;
            }
            var near = new List<object>();
            foreach (var m in s.Monsters.Where(m => m && m.isActiveAndEnabled && h && Vector3.Distance(m.transform.position, h.transform.position) < 30).OrderBy(m => Vector3.Distance(m.transform.position, h.transform.position)).Take(4))
            {
                var md = Actor(m.transform, m.Visual, m.Anim);
                md["key"] = m.InstanceKey; md["id"] = m.MonsterId; md["name"] = m.DisplayName; md["alive"] = m.Alive;
                if (m.Brain != null) { md["brainMode"] = m.Brain.Mode.ToString(); md["attack"] = m.Brain.CurrentAttack != null ? m.Brain.CurrentAttack.id : ""; }
                near.Add(md);
            }
            o["monsters"] = near;
            if (s.Hound) { var hd = Actor(s.Hound.transform, s.Hound.Visual, s.Hound.Anim); o["hound"] = hd; }
            o["springs"] = SpringBone.All.Select(x => (object)new Dictionary<string, object> { ["label"] = x.Label, ["lag"] = x.Lag, ["max"] = x.MaxAngle }).ToList();
            o["log"] = log.Cast<object>().ToList();
            o["fps"] = frames.Count > 0 ? 1000f / Mathf.Max(.1f, frames.Skip(Mathf.Max(0, frames.Count - 60)).Select(f => float.Parse(f.Split(new[] { "\"dt\":" }, StringSplitOptions.None)[1].Split(',')[0], Inv)).Average()) : 0f;
            return FH.Core.Json.Write(o);
        }

        /// <summary>A body: where it is, what it plays, every bone's world position, the ground under its feet.</summary>
        static Dictionary<string, object> Actor(Transform body, Transform visual, AnimationDriver anim)
        {
            var d = new Dictionary<string, object> { ["position"] = Vec(body.position), ["forward"] = Vec(body.forward) };
            string clip = anim?.Current ?? "";
            var st = anim?.State(clip);
            d["clip"] = clip; d["clipTime"] = st != null ? st.time : 0f; d["clipLength"] = st != null ? st.length : 0f; d["clipSpeed"] = st != null ? st.speed : 0f;
            if (!visual) return d;
            var bones = new Dictionary<string, object>();
            foreach (var t in visual.GetComponentsInChildren<Transform>())
                if (!t.GetComponent<Renderer>() && t != visual && !bones.ContainsKey(t.name)) bones[t.name] = Vec(t.position);
            d["bones"] = bones;
            var ground = new Dictionary<string, object>();
            foreach (var foot in new[] { "foot.l", "foot.r", "toes.l", "toes.r", "fore_paw.l", "fore_paw.r", "hind_paw.l", "hind_paw.r" })
            {
                var f = WorldKit.Find(visual, foot);
                if (f && Physics.Raycast(f.position + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, Layers.WorldMask, QueryTriggerInteraction.Ignore))
                    ground[foot] = new Dictionary<string, object> { ["foot"] = f.position.y, ["ground"] = hit.point.y, ["above"] = f.position.y - hit.point.y, ["surface"] = hit.collider.name };
            }
            d["ground"] = ground;
            var renderers = visual.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList();
            if (renderers.Count > 0)
            {
                var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
                d["bounds"] = new Dictionary<string, object> { ["min"] = Vec(b.min), ["max"] = Vec(b.max) };
            }
            return d;
        }

        static List<object> Vec(Vector3 v) => new List<object> { v.x, v.y, v.z };
        static string F(float v) => v.ToString("0.###", Inv);
        static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\"";
    }
}
