using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.AI;

namespace FH.WorldForge
{
    /// <summary>The automated test of a generated world, run inside the real player (--wf-test). Each part measures one
    /// thing with physics, navigation or the rendered frames, and writes it for SightForge (`worldforge` report):
    ///   survey.json     terrain_&lt;name&gt;.json: what the hunter's capsule can stand on and reach (WFSurvey)
    ///   mesh.json       open edges of the whole terrain surface away from the generation box's sides (holes, seams)
    ///   collision.json  2000 points on rendered terrain triangles: a ray from 10 cm out must hit a collider within 15 cm;
    ///                   props: drawn ones that have no collider; wall torches: the gap between bracket and wall
    ///   nav.json        navigation meshes built from the colliders for a hunter (r .35 h 1.8), a skeleton (r .5 h 2) and
    ///                   the boss (recipe size): whether each finds a complete path from the route's start to the arena
    ///                   and back, its length, and how much of the arena floor the boss can stand on
    ///   walk.json       the hunter walking the route (jogging, steering ahead along it) through the CharacterController:
    ///                   where it got stuck (no progress for 3 s; then moved on 1.5 m and counted), how far it got, the
    ///                   camera pulled in by walls, the line to its head blocked, the camera inside rock, both balls of
    ///                   the feet against the ground under them, every frame (30 per second, fixed)
    ///   frames/         the walk as JPEG frames (the video), shots/ the viewpoints at each quality
    ///   lights.json     per terrain chunk, how many lights reach it, against the pixel-light limit of each preset
    ///   perf_*.json     frame times (and draw counts where the player reports them) at each viewpoint per preset</summary>
    public class WFTester : MonoBehaviour
    {
        public WFWorld World; public WFWalker Walker; public WFCamera Cam;
        public string Out;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const int Mask = Physics.DefaultRaycastLayers;
        readonly List<Vector3> route = new List<Vector3>(); readonly List<float> routeS = new List<float>();

        IEnumerator Start()
        {
            while (!World || !World.Ready || !Walker || !Cam) yield return null;
            Out = Out ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Captures", "WorldForge", World.Recipe.name));
            foreach (var sub in new[] { "frames", "shots" })
            {
                var d = Path.Combine(Out, sub);
                if (Directory.Exists(d)) Directory.Delete(d, true);     // only what this test writes (the player log may be open here)
                Directory.CreateDirectory(d);
            }
            Log("start " + World.Recipe.name);
            BuildRoute();
            File.WriteAllText(Path.Combine(Out, "info.json"), "{\"recipe\":" + Q(World.Recipe.name) + ",\"buildSeconds\":" + F(World.BuildSeconds) + ",\"chunks\":" + World.Result.Meshes.Count
                + ",\"triangles\":" + World.Result.Triangles + ",\"vertices\":" + World.Result.Vertices + ",\"voxel\":" + F(World.Recipe.voxel) + ",\"routeLength\":" + F(routeS.LastOrDefault())
                + ",\"fireLights\":" + World.FireLights.Count + ",\"puddles\":" + World.PuddleSpots.Count + ",\"notes\":[" + string.Join(",", World.Notes.Select(Q)) + "],\"unity\":" + Q(Application.unityVersion)
                + ",\"gpu\":" + Q(SystemInfo.graphicsDeviceName) + ",\"screen\":[" + Screen.width + "," + Screen.height + "]}");
            yield return null;
            Survey(); yield return null;
            MeshAudit(); yield return null;
            Collision(); yield return null;
            Nav(); yield return null;
            LightBudget(); yield return null;
            yield return Walk();
            yield return Perf();
            Log("done");
            File.WriteAllText(Path.Combine(Out, "done.txt"), DateTime.Now.ToString("u"));
            yield return null;
            if (Environment.GetCommandLineArgs().Contains("--wf-quit")) Application.Quit(0);
        }

        void Log(string s) { Debug.Log("WF_TEST " + s); File.AppendAllText(Path.Combine(Out, "log.txt"), DateTime.Now.ToString("HH:mm:ss ") + s + "\n"); }

        // ------------------------------------------------------------------------------------------------- the route
        void BuildRoute()
        {
            var R = World.Recipe;
            foreach (var n in R.test.route)
            {
                var f = R.Feature(n); if (f == null) { Log("route: no feature " + n); continue; }
                if (f.type == "chamber" || f.points.Count == 1) { route.Add(Ground(f.points[0])); continue; }
                var sp = new WFSpline(f.points, f.curvature, new WFNoise(R.seed * 31 + f.seed * 7 + f.name.GetHashCode()));
                foreach (var p in sp.P) route.Add(Ground(p));
            }
            float s = 0; routeS.Add(0);
            for (int i = 1; i < route.Count; i++) { s += Vector3.Distance(route[i], route[i - 1]); routeS.Add(s); }
        }

        Vector3 Ground(Vector3 p) => Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var h, 4, Mask, QueryTriggerInteraction.Ignore) ? h.point : p;

        float Progress(Vector3 p, out Vector3 ahead, float look = 1.8f)
        {
            float best = 1e9f, bs = 0;
            for (int i = 0; i < route.Count - 1; i++)
            {
                var a = route[i]; var b = route[i + 1]; var ab = b - a; ab.y = 0; var ap = p - a; ap.y = 0;
                float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
                float d = (ap - ab * t).sqrMagnitude;
                if (d < best) { best = d; bs = routeS[i] + (routeS[i + 1] - routeS[i]) * t; }
            }
            ahead = At(bs + look);
            return bs;
        }

        Vector3 At(float s)
        {
            if (route.Count == 0) return Vector3.zero;
            s = Mathf.Clamp(s, 0, routeS.Last());
            for (int i = 0; i < route.Count - 1; i++) if (s <= routeS[i + 1]) return Vector3.Lerp(route[i], route[i + 1], (s - routeS[i]) / Mathf.Max(1e-5f, routeS[i + 1] - routeS[i]));
            return route.Last();
        }

        // ------------------------------------------------------------------------------------------------- checks
        void Survey()
        {
            var p = WFSurvey.Write(World, route.Count > 0 ? route[0] : Walker.transform.position, Path.Combine(Out, "terrain_" + World.Recipe.name + ".json"));
            Log("survey " + p);
        }

        void MeshAudit()
        {
            // the whole surface's edges (chunks share vertex positions exactly), each counted by the triangles using it
            var edges = new Dictionary<(Vector3Int, Vector3Int), int>();
            Vector3Int K(Vector3 v) => new Vector3Int(Mathf.RoundToInt(v.x * 1000), Mathf.RoundToInt(v.y * 1000), Mathf.RoundToInt(v.z * 1000));
            long tris = 0;
            foreach (var m in World.Result.Meshes)
            {
                var v = m.vertices; var t = m.triangles; tris += t.Length / 3;
                for (int i = 0; i < t.Length; i += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        var a = K(v[t[i + e]]); var b = K(v[t[i + (e + 1) % 3]]);
                        var key = (a.x < b.x || (a.x == b.x && (a.y < b.y || (a.y == b.y && a.z < b.z)))) ? (a, b) : (b, a);
                        edges.TryGetValue(key, out int c); edges[key] = c + 1;
                    }
            }
            var R = World.Recipe; float margin = R.voxel * 1.6f;
            bool OnBox(Vector3 p) => p.x < R.min.x + margin || p.x > R.max.x - margin || p.y < R.min.y + margin || p.y > R.max.y - margin || p.z < R.min.z + margin || p.z > R.max.z - margin;
            int open = 0, boxEdge = 0, over = 0; float openLen = 0; var at = new List<string>();
            foreach (var kv in edges)
            {
                if (kv.Value > 2) over++;
                if (kv.Value != 1) continue;
                Vector3 a = (Vector3)kv.Key.Item1 / 1000f, b = (Vector3)kv.Key.Item2 / 1000f;
                if (OnBox(a) || OnBox(b)) { boxEdge++; continue; }
                open++; openLen += Vector3.Distance(a, b);
                if (at.Count < 100) at.Add(V((a + b) * .5f));
            }
            File.WriteAllText(Path.Combine(Out, "mesh.json"), "{\"triangles\":" + tris + ",\"edges\":" + edges.Count + ",\"openInside\":" + open + ",\"openLength\":" + F(openLen)
                + ",\"openOnBox\":" + boxEdge + ",\"overShared\":" + over + ",\"openAt\":[" + string.Join(",", at) + "]}");
            Log($"mesh: {open} open edges inside the box ({openLen:0.00} m), {boxEdge} on the box sides, {over} shared by 3+");
        }

        void Collision()
        {
            var rnd = new System.Random(World.Recipe.seed); int tested = 0, missed = 0; var missAt = new List<string>();
            var meshes = World.Result.Meshes;
            for (int n = 0; n < 2000; n++)
            {
                var m = meshes[rnd.Next(meshes.Count)]; var t = m.triangles; if (t.Length == 0) continue;
                var v = m.vertices; int i = rnd.Next(t.Length / 3) * 3;
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                var nrm = Vector3.Cross(b - a, c - a); if (nrm.sqrMagnitude < 1e-10f) continue; nrm.Normalize();
                var p = (a + b + c) / 3f; tested++;
                if (!Physics.Raycast(p + nrm * .1f, -nrm, out var h, .25f, Mask, QueryTriggerInteraction.Ignore) || h.distance > .15f + .1f)
                { missed++; if (missAt.Count < 50) missAt.Add(V(p)); }
            }
            var props = new List<string>();
            if (World.Props)
                foreach (Transform p in World.Props)
                {
                    if (World.Mounted.Contains(p.gameObject) || p.name == "Magic light" || p.name == "Glowing eyes") continue;
                    if (!p.GetComponentInChildren<Collider>()) props.Add(Q(p.name + " at " + V(p.position)));
                }
            var mounts = new List<string>(); float worst = 0;
            foreach (var t in World.Mounted)
            {
                if (!t) continue;
                var o = t.transform.position + t.transform.forward * .3f + Vector3.up * .05f;
                float gap = Physics.Raycast(o, -t.transform.forward, out var h, 1, Mask, QueryTriggerInteraction.Ignore) ? h.distance - .3f : 1;
                worst = Mathf.Max(worst, gap); mounts.Add("{\"at\":" + V(t.transform.position) + ",\"gap\":" + F(gap) + "}");
            }
            File.WriteAllText(Path.Combine(Out, "collision.json"), "{\"tested\":" + tested + ",\"missed\":" + missed + ",\"missAt\":[" + string.Join(",", missAt) + "],\"propsWithoutCollider\":[" + string.Join(",", props)
                + "],\"mounts\":[" + string.Join(",", mounts) + "],\"worstMountGap\":" + F(worst) + "}");
            Log($"collision: {missed} of {tested} surface points without a collider; {props.Count} props without one; torch gaps up to {worst:0.000} m");
        }

        void Nav()
        {
            var R = World.Recipe; var sb = new StringBuilder("{\"agents\":[");
            var b = World.Result.Domain; b.Expand(2);
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(b, Mask, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            var arena = R.Feature(R.test.arena);
            Vector3 start = route.Count > 0 ? route[0] : Walker.transform.position, end = arena != null ? Ground(arena.points[0]) : (route.Count > 0 ? route.Last() : start);
            bool first = true;
            foreach (var (name, r, h, climb, slope) in new[] { ("hunter", .35f, 1.8f, .4f, 48f), ("skeleton", .5f, 2.0f, .4f, 45f), ("boss", R.test.bossRadius, R.test.bossHeight, .5f, 40f) })
            {
                var st = NavMesh.CreateSettings(); st.agentRadius = r; st.agentHeight = h; st.agentClimb = climb; st.agentSlope = slope;
                st.overrideVoxelSize = true; st.voxelSize = Mathf.Max(.08f, r / 3); st.minRegionArea = 1;
                var data = NavMeshBuilder.BuildNavMeshData(st, sources, b, Vector3.zero, Quaternion.identity);
                var inst = NavMesh.AddNavMeshData(data);
                var filter = new NavMeshQueryFilter { agentTypeID = st.agentTypeID, areaMask = NavMesh.AllAreas };
                string P(Vector3 x, Vector3 y)
                {
                    bool okA = NavMesh.SamplePosition(x, out var ha, 3, filter), okB = NavMesh.SamplePosition(y, out var hb, 3, filter);
                    if (!okA || !okB) return "{\"status\":\"no navmesh at " + (okA ? "end" : "start") + "\"}";
                    var path = new NavMeshPath(); NavMesh.CalculatePath(ha.position, hb.position, filter, path);
                    float len = 0; for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                    var endGap = path.corners.Length > 0 ? Vector3.Distance(path.corners.Last(), hb.position) : -1;
                    return "{\"status\":" + Q(path.status.ToString()) + ",\"length\":" + F(len) + ",\"corners\":" + path.corners.Length + ",\"endGap\":" + F(endGap) + "}";
                }
                string arenaFloor = "null";
                if (arena != null)
                {
                    int all = 0, ok = 0; var c = arena.points[0];
                    for (float x = -arena.radius; x <= arena.radius; x += .75f)
                        for (float z = -arena.radius; z <= arena.radius; z += .75f)
                        {
                            if (x * x + z * z > arena.radius * arena.radius * .8f) continue;
                            var g = Ground(c + new Vector3(x, 0, z)); all++;
                            if (NavMesh.SamplePosition(g, out var hh, .4f, filter)) ok++;
                        }
                    arenaFloor = F(all > 0 ? ok / (float)all : 0);
                }
                if (!first) sb.Append(','); first = false;
                sb.Append("{\"agent\":" + Q(name) + ",\"radius\":" + F(r) + ",\"height\":" + F(h) + ",\"toArena\":" + P(start, end) + ",\"back\":" + P(end, start) + ",\"arenaFloor\":" + arenaFloor + "}");
                NavMesh.RemoveNavMeshData(inst);
                Log("nav " + name + " done");
            }
            File.WriteAllText(Path.Combine(Out, "nav.json"), sb.Append("]}").ToString());
        }

        void LightBudget()
        {
            var lights = World.GetComponentsInChildren<Light>(true).Where(l => l.type != LightType.Directional && l.enabled).ToList();
            var per = new List<int>(); var where = new List<string>();
            for (int i = 0; i < World.Result.Meshes.Count; i++)
            {
                var b = World.Result.ChunkBounds[i];
                int n = lights.Count(l => b.SqrDistance(l.transform.position) < l.range * l.range);
                per.Add(n); if (n > 0) where.Add("{\"chunk\":" + i + ",\"lights\":" + n + ",\"center\":" + V(b.center) + "}");
            }
            File.WriteAllText(Path.Combine(Out, "lights.json"), "{\"lights\":" + lights.Count + ",\"limits\":{\"Low\":3,\"Medium\":7,\"High\":12},\"maxPerChunk\":" + (per.Count > 0 ? per.Max() : 0)
                + ",\"chunks\":[" + string.Join(",", where) + "]}");
            Log("lights: " + lights.Count + ", up to " + (per.Count > 0 ? per.Max() : 0) + " on one chunk");
        }

        // ------------------------------------------------------------------------------------------------- the walk
        IEnumerator Walk()
        {
            if (route.Count < 2) { Log("walk: no route"); yield break; }
            Walker.transform.position = route[0] + Vector3.up * .1f; Physics.SyncTransforms();
            Walker.Auto = true; Cam.Auto = true;
            var d0 = route[1] - route[0]; d0.y = 0; Walker.transform.rotation = Quaternion.LookRotation(d0); Cam.Yaw = Walker.transform.eulerAngles.y;
            yield return new WaitForSeconds(.5f);
            // standing still: how high the balls of the feet sit above flat ground in the idle pose (bone inside the foot, skin width)
            float rest = Mathf.Min(Gap(Walker.BallL), Gap(Walker.BallR));
            var lowerByFrame = new List<float>();
            Time.captureFramerate = 30;
            var rows = new StringBuilder(); var stuck = new List<string>();
            float last = 0, lastMoveT = 0, t = 0; int frame = 0, nudges = 0, pulled = 0, blocked = 0, inside = 0;
            float total = routeS.Last(), maxS = 0;
            var feetGap = new List<float>(); float sink = 0, floatMax = 0;
            while (t < total / WFWalker.JogSpeed * 2.5f + 10)
            {
                float s = Progress(Walker.transform.position, out var ahead);
                maxS = Mathf.Max(maxS, s);
                if (s >= total - .8f) break;
                var dir = ahead - Walker.transform.position; dir.y = 0;
                Walker.AutoDir = dir.normalized; Walker.Run = false; Walker.Slow = false;
                if (s > last + .3f) { last = s; lastMoveT = t; }
                else if (t - lastMoveT > 3)
                {
                    stuck.Add("{\"at\":" + V(Walker.transform.position) + ",\"s\":" + F(s) + ",\"t\":" + F(t) + "}");
                    nudges++; Walker.transform.position = At(s + 1.5f) + Vector3.up * .3f; Physics.SyncTransforms(); lastMoveT = t;
                }
                yield return new WaitForEndOfFrame();
                // the frame as the player saw it
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(Out, "frames", "f" + frame.ToString("00000") + ".jpg"), tex.EncodeToJPG(82));
                Destroy(tex);
                // the feet against the ground under them
                float gl = Gap(Walker.BallL), gr = Gap(Walker.BallR), lower = Mathf.Min(gl, gr);
                if (lower < 9) { feetGap.Add(lower); lowerByFrame.Add(lower); sink = Mathf.Min(sink, lower); floatMax = Mathf.Max(floatMax, lower); }
                if (Cam.PulledIn > .3f) pulled++; if (Cam.LineBlocked) blocked++; if (Cam.InsideRock) inside++;
                rows.Append(F(t)).Append(',').Append(F(s)).Append(',').Append(V(Walker.transform.position)).Append(',').Append(Walker.Grounded ? 1 : 0).Append(',')
                    .Append(F(Cam.PulledIn)).Append(',').Append(Cam.LineBlocked ? 1 : 0).Append(',').Append(Cam.InsideRock ? 1 : 0).Append(',').Append(F(gl)).Append(',').Append(F(gr)).Append('\n');
                frame++; t += 1 / 30f;
            }
            Time.captureFramerate = 0;
            Walker.AutoDir = Vector3.zero;
            float reached = Progress(Walker.transform.position, out _);
            feetGap.Sort();
            // the planted foot: the lowest the lower foot gets in each .4 s (a step at a jog), so a run's flight phase does not count as floating
            var contacts = new List<float>();
            for (int i = 0; i + 12 <= lowerByFrame.Count; i += 12) contacts.Add(lowerByFrame.GetRange(i, 12).Min());
            contacts.Sort();
            float planted = contacts.Count > 0 ? contacts[contacts.Count / 2] : 99;
            string Pct(float q) => feetGap.Count > 0 ? F(feetGap[Mathf.Clamp((int)(q * feetGap.Count), 0, feetGap.Count - 1)]) : "null";
            File.WriteAllText(Path.Combine(Out, "walk.csv"), "t,s,x,y,z,grounded,camPulledIn,lineBlocked,camInside,ballL,ballR\n" + rows);
            File.WriteAllText(Path.Combine(Out, "walk.json"), "{\"routeLength\":" + F(total) + ",\"reached\":" + F(Mathf.Max(reached, maxS)) + ",\"seconds\":" + F(t) + ",\"frames\":" + frame + ",\"nudges\":" + nudges
                + ",\"stuck\":[" + string.Join(",", stuck) + "],\"camera\":{\"pulledIn\":" + F(frame > 0 ? pulled / (float)frame : 0) + ",\"lineBlocked\":" + F(frame > 0 ? blocked / (float)frame : 0) + ",\"inside\":" + F(frame > 0 ? inside / (float)frame : 0) + "}"
                + ",\"feet\":{\"lowerBallP05\":" + Pct(.05f) + ",\"median\":" + Pct(.5f) + ",\"p95\":" + Pct(.95f) + ",\"deepest\":" + F(sink) + ",\"highest\":" + F(floatMax) + ",\"rest\":" + F(rest < 9 ? rest : 0) + ",\"planted\":" + F(planted) + "}}");
            Log($"walk: {Mathf.Max(reached, maxS):0.0} of {total:0.0} m in {t:0.0} s, {nudges} nudges, camera blocked {blocked} of {frame} frames");
        }

        float Gap(Transform ball)
        {
            if (!ball) return 99;
            return Physics.Raycast(ball.position + Vector3.up * .5f, Vector3.down, out var h, 2, Mask, QueryTriggerInteraction.Ignore) ? ball.position.y - h.point.y : 99;
        }

        // ------------------------------------------------------------------------------------------------- performance
        IEnumerator Perf()
        {
            var vps = new List<(string, Vector3, Vector3)>();
            var R = World.Recipe; string q0 = R.quality;
            if (R.test.viewpoints.Count > 0) for (int i = 0; i < R.test.viewpoints.Count; i++) vps.Add(("view" + i, Ground(R.test.viewpoints[i]), Vector3.zero));
            else if (route.Count > 1)
                foreach (var (n, f) in new[] { ("start", .02f), ("mouth", .35f), ("tunnel", .6f), ("arena", .97f) })
                {
                    float s = routeS.Last() * f; vps.Add((n, At(s), At(s + 2) - At(s)));
                }
            Walker.Auto = true; Walker.AutoDir = Vector3.zero;
            using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            using var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            using var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            foreach (var q in new[] { "Low", "Medium", "High" })
            {
                World.SetQuality(q);
                var sb = new StringBuilder("{\"quality\":" + Q(q) + ",\"views\":[");
                for (int v = 0; v < vps.Count; v++)
                {
                    var (name, at, look) = vps[v];
                    Walker.transform.position = at + Vector3.up * .1f; look.y = 0;
                    if (look.sqrMagnitude > .01f) Walker.transform.rotation = Quaternion.LookRotation(look);
                    Cam.Yaw = Walker.transform.eulerAngles.y; Physics.SyncTransforms();
                    for (float w = 0; w < 1.5f; w += Time.unscaledDeltaTime) yield return null;
                    var dts = new List<float>(); long dc = 0, bc = 0, tc = 0; int n = 0;
                    for (float w = 0; w < 4f; w += Time.unscaledDeltaTime)
                    {
                        yield return null; dts.Add(Time.unscaledDeltaTime * 1000);
                        if (draws.Valid) { dc += draws.LastValue; bc += batches.LastValue; tc += tris.LastValue; n++; }
                    }
                    yield return new WaitForEndOfFrame();
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(Out, "shots", name + "_" + q + ".jpg"), tex.EncodeToJPG(90)); Destroy(tex);
                    dts.Sort();
                    if (v > 0) sb.Append(',');
                    sb.Append("{\"view\":" + Q(name) + ",\"at\":" + V(at) + ",\"frames\":" + dts.Count + ",\"msMedian\":" + F(dts[dts.Count / 2]) + ",\"msP95\":" + F(dts[(int)(dts.Count * .95f)]) + ",\"msMax\":" + F(dts.Last())
                        + ",\"draws\":" + (n > 0 ? (dc / n).ToString() : "null") + ",\"batches\":" + (n > 0 ? (bc / n).ToString() : "null") + ",\"triangles\":" + (n > 0 ? (tc / n).ToString() : "null") + "}");
                }
                File.WriteAllText(Path.Combine(Out, "perf_" + q + ".json"), sb.Append("]}").ToString());
                Log("perf " + q + " done");
            }
            World.SetQuality(q0);
        }

        static string F(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("0.####", Inv);
        static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
        static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
