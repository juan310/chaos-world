using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FH.Game
{
    /// <summary>The map and terrain mesh system's game side: each area surveyed the way the hunter meets it, for SightForge's
    /// `eyes_inspect.py terrain <survey.json>` report.
    ///   ground   a probe every half metre straight down through everything: each surface the hunter could stand on
    ///            (upward facing, with head room), its height, slope and what it is; several per spot (a bridge over the
    ///            river, a floor under a roof)
    ///   walking  from the area's spawn point, with the hunter's own controller limits (step .4 m, slope 48 deg, a drop
    ///            of up to 2.5 m is a jump down): which ground the hunter reaches, which it never can, and the edges
    ///            where a reachable spot ends in a big fall with no wall or rail
    ///   scenery  every piece of scenery: one with no collider the hunter walks through, a collider with nothing drawn
    ///            (an invisible wall), a prop hanging above the ground under it, two surfaces in the same place
    /// Run for every area at start with --survey-terrain &lt;folder&gt; (and --survey-terrain-quit), or with F10 for the
    /// area the hunter is in (Captures/Problems/terrain_&lt;area&gt;_&lt;time&gt;/).</summary>
    public static class TerrainSurvey
    {
        public const float Step = .5f, StepUp = .4f, SlopeLimit = 48, Headroom = 1.7f, SafeDrop = 2.5f, BodyRadius = .3f;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public struct Area { public string Id; public Vector3 Spawn; public Rect Rect; public Vector3 Circle; public float Radius; }

        /// <summary>The game's areas: the overworld, the crypt, the Proving Grounds and the Training Range.</summary>
        public static List<Area> Areas()
        {
            var s = GameSession.I; var w = s ? s.World : null;
            var list = new List<Area>();
            if (!w) return list;
            Vector3 A(string id, Vector3 fallback) { var a = w.Anchors.FirstOrDefault(x => x && x.Id == id); return a ? a.transform.position : fallback; }
            list.Add(new Area { Id = "overworld", Spawn = A("spawn_home", Vector3.zero), Rect = w.MapBounds });
            var c = w.CryptOrigin; var ch = w.CryptHalfSize;
            list.Add(new Area { Id = "crypt", Spawn = A("crypt_inside", c), Rect = new Rect(c.x - ch.x, c.z - ch.y - 4, ch.x * 2, ch.y * 2 + 8) });
            var p = w.ProvingOrigin; float r = w.ProvingRadius;
            list.Add(new Area { Id = "proving", Spawn = A("proving_spawn", p), Rect = new Rect(p.x - r - 4, p.z - r - 4, r * 2 + 8, r * 2 + 8), Circle = p, Radius = r });
            var g = w.RangeOrigin; var gh = w.RangeHalfSize;
            list.Add(new Area { Id = "range", Spawn = A("range_spawn", g), Rect = new Rect(g.x - gh.x - 2, g.z - gh.y - 2, gh.x * 2 + 4, gh.y * 2 + 4) });
            return list;
        }

        public static Area Around(Vector3 p)
        {
            var all = Areas();
            foreach (var a in all) if (a.Rect.Contains(new Vector2(p.x, p.z))) return a;
            return new Area { Id = "here", Spawn = p, Rect = new Rect(p.x - 40, p.z - 40, 80, 80) };
        }

        public static string WriteAll(string folder)
        {
            Directory.CreateDirectory(folder);
            foreach (var a in Areas()) Write(a, Path.Combine(folder, "terrain_" + a.Id + ".json"));
            return folder;
        }

        struct Surface { public float y, slope; public string what; public int col; }

        public static void Write(Area area, string path)
        {
            Physics.SyncTransforms();
            float t0 = Time.realtimeSinceStartup;
            int nx = Mathf.CeilToInt(area.Rect.width / Step), nz = Mathf.CeilToInt(area.Rect.height / Step);
            float top = area.Spawn.y + 60;
            var cells = new List<Surface>[nx, nz];
            var colliderIds = new Dictionary<Collider, int>(); var colliderNames = new List<string>();
            int Id(Collider c) { if (!colliderIds.TryGetValue(c, out int i)) { i = colliderNames.Count; colliderIds[c] = i; colliderNames.Add(Name(c.transform)); } return i; }
            int overlaps = 0; var overlapAt = new List<string>();
            float cosLimit = Mathf.Cos(SlopeLimit * Mathf.Deg2Rad);
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    float x = area.Rect.xMin + (i + .5f) * Step, z = area.Rect.yMin + (k + .5f) * Step;
                    var hits = Physics.RaycastAll(new Vector3(x, top, z), Vector3.down, 120, Layers.WorldMask, QueryTriggerInteraction.Ignore).OrderByDescending(h => h.point.y).ToList();
                    var list = new List<Surface>();
                    for (int j = 0; j < hits.Count; j++)
                    {
                        var h = hits[j];
                        if (h.normal.y < .3f) continue;
                        // two different surfaces within 2 cm, both facing up: drawn in the same place (flicker)
                        if (j + 1 < hits.Count && hits[j + 1].collider != h.collider && Mathf.Abs(hits[j + 1].point.y - h.point.y) < .02f && hits[j + 1].normal.y > .9f && h.normal.y > .9f)
                        { overlaps++; if (overlapAt.Count < 400) overlapAt.Add("[" + F(x) + "," + F(z) + "," + Q(Name(h.collider.transform) + " / " + Name(hits[j + 1].collider.transform)) + "]"); }
                        float above = j > 0 ? hits[j - 1].point.y - h.point.y : 99;
                        // head room: the next surface above, then the hunter's capsule standing here
                        if (above < Headroom) continue;
                        var foot = h.point + Vector3.up * (BodyRadius + .45f);
                        if (Physics.CheckCapsule(foot, h.point + Vector3.up * (Headroom - BodyRadius), BodyRadius * .8f, Layers.WorldMask, QueryTriggerInteraction.Ignore)) continue;
                        list.Add(new Surface { y = h.point.y, slope = Mathf.Acos(Mathf.Clamp01(h.normal.y)) * Mathf.Rad2Deg, what = null, col = Id(h.collider) });
                        if (list.Count >= 3) break;
                    }
                    cells[i, k] = list;
                }
            // walking from the spawn: (cell, surface) nodes; a step up to StepUp, any slope under the limit, a drop to SafeDrop
            var reach = new HashSet<(int, int, int)>();
            int si = Mathf.Clamp(Mathf.FloorToInt((area.Spawn.x - area.Rect.xMin) / Step), 0, nx - 1), sk = Mathf.Clamp(Mathf.FloorToInt((area.Spawn.z - area.Rect.yMin) / Step), 0, nz - 1);
            var q = new Queue<(int, int, int)>();
            int sl = Nearest(cells[si, sk], area.Spawn.y);
            if (sl >= 0) { q.Enqueue((si, sk, sl)); reach.Add((si, sk, sl)); }
            var ledges = new List<string>();
            while (q.Count > 0)
            {
                var (i, k, l) = q.Dequeue();
                var here = cells[i, k][l];
                foreach (var (di, dk) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int a = i + di, b = k + dk;
                    if (a < 0 || b < 0 || a >= nx || b >= nz) continue;
                    var there = cells[a, b];
                    Vector3 p0 = Center(area, i, k, here.y), p1 = Center(area, a, b, here.y);
                    bool walled = Physics.Linecast(p0 + Vector3.up * .9f, p1 + Vector3.up * .9f, Layers.WorldMask, QueryTriggerInteraction.Ignore);
                    bool any = false;
                    for (int m = 0; m < there.Count; m++)
                    {
                        float dh = there[m].y - here.y;
                        if (dh > StepUp || dh < -SafeDrop || there[m].slope > SlopeLimit) continue;
                        if (walled && dh > -1) continue;
                        any = true;
                        if (reach.Add((a, b, m))) q.Enqueue((a, b, m));
                    }
                    if (!any && !walled && InArea(area, p1) && (there.Count == 0 || there.All(s => s.y - here.y < -SafeDrop)) && ledges.Count < 2000)
                        ledges.Add("[" + F(p0.x) + "," + F(p0.z) + "," + F(p1.x) + "," + F(p1.z) + "," + F(there.Count == 0 ? -99 : there.Max(s => s.y) - here.y) + "]");
                }
            }
            // scenery
            var props = new List<string>();
            foreach (var r in Object.FindObjectsByType<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                if (r.GetComponentInParent<HunterController>() || r.GetComponentInParent<MonsterActor>() || r.GetComponentInParent<NpcActor>() || r.GetComponentInParent<HoundCompanion>() || r.GetComponentInParent<LootBag>()) continue;
                if (r.gameObject.layer == Layers.Actor || r.gameObject.layer == Layers.Hurtbox || r.gameObject.layer == Layers.Review || r.gameObject.layer == Layers.Map) continue;
                var bd = r.bounds;
                if (!area.Rect.Contains(new Vector2(bd.center.x, bd.center.z))) continue;
                string name = Name(r.transform);
                bool solid = Physics.CheckBox(bd.center, Vector3.Max(bd.extents * .45f, Vector3.one * .02f), Quaternion.identity, Layers.WorldMask, QueryTriggerInteraction.Ignore);
                float below = 99; string under = "";
                var down = Physics.RaycastAll(new Vector3(bd.center.x, bd.min.y + .02f, bd.center.z), Vector3.down, 6, Layers.WorldMask, QueryTriggerInteraction.Ignore).Where(h => !h.collider.transform.IsChildOf(r.transform) && h.collider.transform != r.transform).OrderBy(h => h.distance).FirstOrDefault();
                if (down.collider) { below = down.distance - .02f; under = Name(down.collider.transform); }
                bool walkThrough = !solid && bd.size.y > .6f && Mathf.Min(bd.size.x, bd.size.z) > .25f;
                bool floating = below > .08f && below < 6 && bd.size.y < 6 && !(r.GetComponentInParent<Light>());
                if (!walkThrough && !floating) continue;
                if (props.Count >= 1500) break;
                props.Add("{\"what\":" + Q(name) + ",\"min\":[" + F(bd.min.x) + "," + F(bd.min.y) + "," + F(bd.min.z) + "],\"max\":[" + F(bd.max.x) + "," + F(bd.max.y) + "," + F(bd.max.z) + "]"
                          + ",\"walkThrough\":" + (walkThrough ? "true" : "false") + ",\"above\":" + F(below < 99 ? below : -1) + ",\"under\":" + Q(under) + "}");
            }
            var invisible = new List<string>();
            foreach (var c in Object.FindObjectsByType<Collider>())
            {
                if (c.isTrigger || !c.enabled || !c.gameObject.activeInHierarchy || ((1 << c.gameObject.layer) & Layers.WorldMask) == 0) continue;
                if (c is TerrainCollider || c.GetComponentInParent<HunterController>() || c.GetComponentInParent<MonsterActor>() || c.GetComponentInParent<NpcActor>() || c.GetComponentInParent<HoundCompanion>()) continue;
                var bd = c.bounds;
                if (!area.Rect.Contains(new Vector2(bd.center.x, bd.center.z)) || bd.size.y < .5f) continue;
                bool drawn = c.GetComponentsInChildren<Renderer>().Any(r => r.enabled && r.bounds.Intersects(bd)) || (c.transform.parent && c.transform.parent.GetComponentsInChildren<Renderer>().Any(r => r.enabled && r.bounds.Intersects(bd)));
                if (drawn || invisible.Count >= 600) continue;
                invisible.Add("{\"what\":" + Q(Name(c.transform)) + ",\"min\":[" + F(bd.min.x) + "," + F(bd.min.y) + "," + F(bd.min.z) + "],\"max\":[" + F(bd.max.x) + "," + F(bd.max.y) + "," + F(bd.max.z) + "]}");
            }
            // the grid: per cell its surfaces as [height, slope, collider, reached 0/1]
            var sb = new StringBuilder(nx * nz * 24 + 65536);
            sb.Append("{\"area\":").Append(Q(area.Id)).Append(",\"step\":").Append(F(Step)).Append(",\"origin\":[").Append(F(area.Rect.xMin)).Append(',').Append(F(area.Rect.yMin)).Append(']')
              .Append(",\"size\":[").Append(nx).Append(',').Append(nz).Append("],\"spawn\":[").Append(F(area.Spawn.x)).Append(',').Append(F(area.Spawn.y)).Append(',').Append(F(area.Spawn.z)).Append(']')
              .Append(",\"circle\":").Append(area.Radius > 0 ? "[" + F(area.Circle.x) + "," + F(area.Circle.z) + "," + F(area.Radius) + "]" : "null")
              .Append(",\"limits\":{\"stepUp\":").Append(F(StepUp)).Append(",\"slope\":").Append(F(SlopeLimit)).Append(",\"headroom\":").Append(F(Headroom)).Append(",\"safeDrop\":").Append(F(SafeDrop)).Append('}')
              .Append(",\"colliders\":[").Append(string.Join(",", colliderNames.Select(Q))).Append("],\"cells\":[");
            for (int k = 0; k < nz; k++)
            {
                if (k > 0) sb.Append(',');
                sb.Append("\n[");
                for (int i = 0; i < nx; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('[');
                    var list = cells[i, k];
                    for (int m = 0; m < list.Count; m++)
                    {
                        if (m > 0) sb.Append(',');
                        sb.Append('[').Append(F(list[m].y)).Append(',').Append(Mathf.RoundToInt(list[m].slope)).Append(',').Append(list[m].col).Append(',').Append(reach.Contains((i, k, m)) ? 1 : 0).Append(']');
                    }
                    sb.Append(']');
                }
                sb.Append(']');
            }
            sb.Append("],\n\"ledges\":[").Append(string.Join(",", ledges)).Append("],\n\"overlaps\":").Append(overlaps).Append(",\"overlapAt\":[").Append(string.Join(",", overlapAt))
              .Append("],\n\"props\":[").Append(string.Join(",\n", props)).Append("],\n\"invisible\":[").Append(string.Join(",\n", invisible))
              .Append("],\"seconds\":").Append(F(Time.realtimeSinceStartup - t0)).Append('}');
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("FH_TERRAIN " + area.Id + ": " + nx + "x" + nz + " cells, " + reach.Count + " reached, " + ledges.Count + " open ledges, " + props.Count + " props, " + invisible.Count + " invisible colliders, " + overlaps + " overlaps -> " + path);
        }

        static bool InArea(Area a, Vector3 p) => a.Radius > 0 ? Vector2.Distance(new Vector2(p.x, p.z), new Vector2(a.Circle.x, a.Circle.z)) < a.Radius : a.Rect.Contains(new Vector2(p.x, p.z));
        static Vector3 Center(Area a, int i, int k, float y) => new Vector3(a.Rect.xMin + (i + .5f) * Step, y, a.Rect.yMin + (k + .5f) * Step);

        static int Nearest(List<Surface> list, float y)
        {
            int best = -1; float bd = 1e9f;
            for (int m = 0; m < list.Count; m++) { float d = Mathf.Abs(list[m].y - y); if (d < bd) { bd = d; best = m; } }
            return bd < 2 ? best : -1;
        }

        static string Name(Transform t) => t.parent ? t.parent.name + "/" + t.name : t.name;
        static string F(float v) => v.ToString("0.###", Inv);
        static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
