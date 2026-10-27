using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FH.WorldForge
{
    /// <summary>The generated world surveyed the way the hunter meets it, written in SightForge's terrain format (the
    /// same as the game's TerrainSurvey, so `eyes_inspect.py terrain` reads it): a probe straight down every half metre,
    /// every surface with head room (1.7 m clear, the hunter's capsule fits), reachability from the spawn with the
    /// controller's own limits (step .4 m, slope 48 deg, a jump down of up to 2.5 m; no walking through walls), open
    /// ledges, overlapping surfaces, scenery without collision. Measured with physics against the real colliders.</summary>
    public static class WFSurvey
    {
        public const float Step = .5f, StepUp = .4f, SlopeLimit = 48, Headroom = 1.7f, SafeDrop = 2.5f, BodyRadius = .3f;
        const int Mask = Physics.DefaultRaycastLayers;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        struct Surface { public float y, slope; public int col; }

        public static string Write(WFWorld w, Vector3 spawn, string path)
        {
            Physics.SyncTransforms();
            var R = w.Recipe; var rect = new Rect(R.min.x, R.min.z, R.max.x - R.min.x, R.max.z - R.min.z);
            int nx = Mathf.CeilToInt(rect.width / Step), nz = Mathf.CeilToInt(rect.height / Step);
            float top = R.max.y + 5;
            var cells = new List<Surface>[nx, nz];
            var ids = new Dictionary<Collider, int>(); var names = new List<string>();
            int Id(Collider c) { if (!ids.TryGetValue(c, out int i)) { i = names.Count; ids[c] = i; names.Add(c.transform.parent ? c.transform.parent.name + "/" + c.name : c.name); } return i; }
            int overlaps = 0; var overlapAt = new List<string>();
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    float x = rect.xMin + (i + .5f) * Step, z = rect.yMin + (k + .5f) * Step;
                    var hits = Physics.RaycastAll(new Vector3(x, top, z), Vector3.down, top - R.min.y + 5, Mask, QueryTriggerInteraction.Ignore).OrderByDescending(h => h.point.y).ToList();
                    var list = new List<Surface>();
                    for (int j = 0; j < hits.Count; j++)
                    {
                        var h = hits[j];
                        if (h.normal.y < .3f) continue;
                        if (j + 1 < hits.Count && hits[j + 1].collider != h.collider && Mathf.Abs(hits[j + 1].point.y - h.point.y) < .02f && hits[j + 1].normal.y > .9f && h.normal.y > .9f)
                        { overlaps++; if (overlapAt.Count < 200) overlapAt.Add("[" + F(x) + "," + F(z) + "," + Q(h.collider.name + " / " + hits[j + 1].collider.name) + "]"); }
                        float above = j > 0 ? hits[j - 1].point.y - h.point.y : 99;
                        if (above < Headroom) continue;
                        if (Physics.CheckCapsule(h.point + Vector3.up * (BodyRadius + .45f), h.point + Vector3.up * (Headroom - BodyRadius), BodyRadius * .8f, Mask, QueryTriggerInteraction.Ignore)) continue;
                        list.Add(new Surface { y = h.point.y, slope = Mathf.Acos(Mathf.Clamp01(h.normal.y)) * Mathf.Rad2Deg, col = Id(h.collider) });
                        if (list.Count >= 3) break;
                    }
                    cells[i, k] = list;
                }
            var reach = new HashSet<(int, int, int)>(); var q = new Queue<(int, int, int)>(); var ledges = new List<string>();
            int si = Mathf.Clamp(Mathf.FloorToInt((spawn.x - rect.xMin) / Step), 0, nx - 1), sk = Mathf.Clamp(Mathf.FloorToInt((spawn.z - rect.yMin) / Step), 0, nz - 1);
            int sl = -1; float sd = 2;
            for (int m = 0; m < cells[si, sk].Count; m++) { float d = Mathf.Abs(cells[si, sk][m].y - spawn.y); if (d < sd) { sd = d; sl = m; } }
            if (sl >= 0) { q.Enqueue((si, sk, sl)); reach.Add((si, sk, sl)); }
            while (q.Count > 0)
            {
                var (i, k, l) = q.Dequeue(); var here = cells[i, k][l];
                foreach (var (di, dk) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int a = i + di, b = k + dk;
                    if (a < 0 || b < 0 || a >= nx || b >= nz) continue;
                    var there = cells[a, b];
                    Vector3 p0 = new Vector3(rect.xMin + (i + .5f) * Step, here.y, rect.yMin + (k + .5f) * Step), p1 = new Vector3(rect.xMin + (a + .5f) * Step, here.y, rect.yMin + (b + .5f) * Step);
                    bool walled = Physics.Linecast(p0 + Vector3.up * .9f, p1 + Vector3.up * .9f, Mask, QueryTriggerInteraction.Ignore);
                    bool any = false;
                    for (int m = 0; m < there.Count; m++)
                    {
                        float dh = there[m].y - here.y;
                        if (dh > StepUp || dh < -SafeDrop || there[m].slope > SlopeLimit) continue;
                        if (walled && dh > -1) continue;
                        any = true;
                        if (reach.Add((a, b, m))) q.Enqueue((a, b, m));
                    }
                    if (!any && !walled && (there.Count == 0 || there.All(s => s.y - here.y < -SafeDrop)) && ledges.Count < 2000)
                        ledges.Add("[" + F(p0.x) + "," + F(p0.z) + "," + F(p1.x) + "," + F(p1.z) + "," + F(there.Count == 0 ? -99 : there.Max(s => s.y) - here.y) + "]");
                }
            }
            // scenery: drawn props with no collider inside them (walk-through), props hanging above the ground under them
            var props = new List<string>();
            if (w.Props)
                foreach (Transform p in w.Props)
                {
                    if (w.Mounted.Contains(p.gameObject)) continue;      // wall torches: checked as mounts (WFTester)
                    var rs = p.GetComponentsInChildren<MeshRenderer>(); if (rs.Length == 0) continue;
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    bool solid = Physics.CheckBox(b.center, Vector3.Max(b.extents * .45f, Vector3.one * .02f), Quaternion.identity, Mask, QueryTriggerInteraction.Ignore);
                    float below = -1; string under = "";
                    var down = Physics.RaycastAll(new Vector3(b.center.x, b.min.y + .02f, b.center.z), Vector3.down, 6, Mask, QueryTriggerInteraction.Ignore).Where(h => !h.collider.transform.IsChildOf(p)).OrderBy(h => h.distance).FirstOrDefault();
                    if (down.collider) { below = down.distance - .02f; under = down.collider.name; }
                    bool walkThrough = !solid && b.size.y > .6f && Mathf.Min(b.size.x, b.size.z) > .25f;
                    bool floating = below > .08f && p.name != "Magic light" && p.name != "Glowing eyes";
                    if (!walkThrough && !floating) continue;
                    props.Add("{\"what\":" + Q("Props/" + p.name) + ",\"min\":[" + F(b.min.x) + "," + F(b.min.y) + "," + F(b.min.z) + "],\"max\":[" + F(b.max.x) + "," + F(b.max.y) + "," + F(b.max.z) + "],\"walkThrough\":" + (walkThrough ? "true" : "false") + ",\"above\":" + F(below) + ",\"under\":" + Q(under) + "}");
                }
            var sb = new StringBuilder(nx * nz * 24 + 65536);
            sb.Append("{\"area\":").Append(Q(R.name)).Append(",\"step\":").Append(F(Step)).Append(",\"origin\":[").Append(F(rect.xMin)).Append(',').Append(F(rect.yMin)).Append(']')
              .Append(",\"size\":[").Append(nx).Append(',').Append(nz).Append("],\"spawn\":[").Append(F(spawn.x)).Append(',').Append(F(spawn.y)).Append(',').Append(F(spawn.z)).Append("],\"circle\":null")
              .Append(",\"limits\":{\"stepUp\":").Append(F(StepUp)).Append(",\"slope\":").Append(F(SlopeLimit)).Append(",\"headroom\":").Append(F(Headroom)).Append(",\"safeDrop\":").Append(F(SafeDrop)).Append('}')
              .Append(",\"colliders\":[").Append(string.Join(",", names.Select(Q))).Append("],\"cells\":[");
            for (int k = 0; k < nz; k++)
            {
                if (k > 0) sb.Append(','); sb.Append("\n[");
                for (int i = 0; i < nx; i++)
                {
                    if (i > 0) sb.Append(','); sb.Append('[');
                    var list = cells[i, k];
                    for (int m = 0; m < list.Count; m++) { if (m > 0) sb.Append(','); sb.Append('[').Append(F(list[m].y)).Append(',').Append(Mathf.RoundToInt(list[m].slope)).Append(',').Append(list[m].col).Append(',').Append(reach.Contains((i, k, m)) ? 1 : 0).Append(']'); }
                    sb.Append(']');
                }
                sb.Append(']');
            }
            sb.Append("],\n\"ledges\":[").Append(string.Join(",", ledges)).Append("],\"overlaps\":").Append(overlaps).Append(",\"overlapAt\":[").Append(string.Join(",", overlapAt))
              .Append("],\"props\":[").Append(string.Join(",\n", props)).Append("],\"invisible\":[]}");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        static string F(float v) => v.ToString("0.###", Inv);
        static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
