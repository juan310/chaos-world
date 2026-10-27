"""Performance capture (Studio 0.17): a game's frame record (the Folklore Hunter game's DevCapture: perf.json, written on
F9 and at the end of a verification run) read for what a player feels: frame times, hitches and what happened just
before each, garbage made every frame, draw calls, batches, set-pass calls and triangles.

Findings: a slow frame rate against the target (95th percentile frame time), hitches (a frame over twice the median and
over 25 ms) that keep following the same kind of event (a clip, a move, an area change), steady garbage, heavy draw
counts. Charts: frame time over the run with the events marked, and the draw counts."""
import json
import os
from collections import Counter

from .findings import finding
from . import devdraw as D


def load(path):
    if os.path.isdir(path):
        path = os.path.join(path, "perf.json")
    with open(path, encoding="utf-8") as fh:
        return json.load(fh), path


def pct(v, q):
    v = sorted(v)
    return v[min(len(v) - 1, int(q * len(v)))] if v else 0


def analyse(data):
    fr = [f for f in data.get("frames", []) if f.get("dt") is not None]
    ev = data.get("events", [])
    if not fr:
        return {"frames": 0}
    dts = [f["dt"] for f in fr]
    med = pct(dts, .5)
    hitches = [f for f in fr if f["dt"] > max(25.0, 2 * med)]
    # each kind of event: how many hitches came within 0.35 s after it, and how often it happened at all
    causes = Counter(); seen = Counter((e["kind"] + ": " + e["text"]) for e in ev)
    near = []
    for h in hitches:
        before = [e for e in ev if h["t"] - .35 <= e["t"] <= h["t"] + 0.02]
        for key in set(e["kind"] + ": " + e["text"] for e in before):
            causes[key] += 1
        if not before: causes["(nothing recorded)"] += 1
        near.append((h, before[-1] if before else None))
    rates = {k: n / max(1, seen.get(k, n)) for k, n in causes.items()}
    gcs = [f["gc"] for f in fr if f.get("gc") is not None]
    def stat(k):
        v = [f[k] for f in fr if f.get(k) is not None]
        return {"median": pct(v, .5), "p95": pct(v, .95), "max": max(v)} if v else None
    return {"frames": len(fr), "seconds": fr[-1]["t"] - fr[0]["t"], "median": med, "p95": pct(dts, .95), "p99": pct(dts, .99), "worst": max(dts),
            "fps": 1000 / (sum(dts) / len(dts)), "hitches": len(hitches), "causes": [(k, n, rates[k]) for k, n in causes.most_common(12)], "near": near,
            "gcFrames": (sum(1 for g in gcs if g > 0) / len(gcs)) if gcs else None, "gcMean": (sum(gcs) / len(gcs)) if gcs else None,
            "draws": stat("draws"), "batches": stat("batches"), "setpass": stat("setpass"), "tris": stat("tris"),
            "target": data.get("targetFps") or 60, "gpu": data.get("gpu"), "screen": data.get("screen"), "quality": data.get("quality")}


def findings(r):
    out = []
    if not r.get("frames"):
        return [finding("not_completed", "frame record", "perf", "no frames recorded")]
    budget = 1000.0 / max(1, r["target"] if r["target"] > 0 else 60)
    if r["p95"] > budget * 1.5:
        out.append(finding("likely", "frame rate", "whole run", "95 %% of frames take up to %.1f ms (%.0f fps); the target is %.1f ms (%d fps)" % (r["p95"], 1000 / r["p95"], budget, r["target"]),
                           measured=r["p95"], threshold=budget * 1.5))
    for cause, n, rate in r["causes"]:
        if n >= 3 and rate >= .5 and cause != "(nothing recorded)":
            out.append(finding("likely", "hitch after an event", cause, "%d hitches (over 25 ms and twice the median) came within 0.35 s after this; %.0f %% of the times it happened" % (n, rate * 100),
                               measured=n, threshold=3, fix="load or build what this needs ahead of time (warm the clip, pool the effect, preload the area)"))
    if r["hitches"] >= 10 and r["hitches"] / max(1, r["frames"]) > .01:
        out.append(finding("uncertain", "hitches", "whole run", "%d hitches in %.0f s" % (r["hitches"], r["seconds"]), measured=r["hitches"]))
    if r.get("gcFrames") is not None and r["gcFrames"] > .5 and (r.get("gcMean") or 0) > 1024:
        out.append(finding("uncertain", "garbage every frame", "whole run", "%.0f %% of frames make garbage, %.1f KB a frame on average (the collector will pause the game)" % (r["gcFrames"] * 100, r["gcMean"] / 1024),
                           measured=r["gcMean"], threshold=1024, fix="reuse lists and strings in per-frame code (the recorder's own JSON is made only on capture)"))
    if r.get("draws") and r["draws"]["p95"] > 2500:
        out.append(finding("uncertain", "draw calls", "whole run", "95th percentile %d draw calls a frame" % r["draws"]["p95"], measured=r["draws"]["p95"], threshold=2500,
                           fix="combine static scenery, share materials, GPU instancing"))
    return out


def charts(data, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    fr = [f for f in data.get("frames", []) if f.get("dt") is not None]
    ev = [(e["t"], e["kind"]) for e in data.get("events", []) if e.get("kind") in ("area", "move")]
    target = data.get("targetFps") or 60
    pics = [D.chart(os.path.join(out_dir, "frame_time.png"), [("frame ms", [(f["t"], f["dt"]) for f in fr], (90, 170, 255))], ev,
                    "Frame time (ms); ticks: moves and area changes", hlines=[(1000.0 / target, "target %d fps" % target)])]
    ser = [(k, [(f["t"], f[k]) for f in fr if f.get(k) is not None], c) for k, c in (("draws", (255, 170, 60)), ("batches", (120, 220, 120)), ("setpass", (240, 90, 200)))]
    if any(s for _, s, _ in ser):
        pics.append(D.chart(os.path.join(out_dir, "draws.png"), [s for s in ser if s[1]], ev, "Draw calls, batches, set-pass calls a frame"))
    gc = [(f["t"], f["gc"] / 1024) for f in fr if f.get("gc") is not None]
    if gc:
        pics.append(D.chart(os.path.join(out_dir, "garbage.png"), [("KB a frame", gc, (250, 230, 90))], ev, "Garbage made each frame (KB)"))
    return pics


def text(r):
    if not r.get("frames"): return "no frames"
    lines = ["%d frames over %.0f s; %.0f fps average; frame time median %.1f, p95 %.1f, p99 %.1f, worst %.1f ms; %d hitches" %
             (r["frames"], r["seconds"], r["fps"], r["median"], r["p95"], r["p99"], r["worst"], r["hitches"]),
             "GPU %s, screen %s, quality %s" % (r.get("gpu"), r.get("screen"), r.get("quality"))]
    for k in ("draws", "batches", "setpass", "tris"):
        if r.get(k): lines.append("%-8s median %d, p95 %d, max %d" % (k, r[k]["median"], r[k]["p95"], r[k]["max"]))
    if r.get("gcMean") is not None: lines.append("garbage: %.0f %% of frames, %.1f KB a frame on average" % (r["gcFrames"] * 100, r["gcMean"] / 1024))
    lines.append("hitches by what came just before:")
    lines += ["  %3d  (%3.0f %% of its times)  %s" % (n, rate * 100, c) for c, n, rate in r["causes"]]
    lines.append("the worst frames:")
    for h, e in sorted(r["near"], key=lambda x: -x[0]["dt"])[:10]:
        lines.append("  %6.1f ms at %.2f s%s" % (h["dt"], h["t"], ("  after %s: %s" % (e["kind"], e["text"])) if e else ""))
    return "\n".join(lines)


def register(sp, ctx):
    p = sp.add_parser("perf", help="a game's frame record: frame times, hitches and their causes, garbage, draw calls")
    p.add_argument("capture", help="a perf.json, or a folder with one")
    p.add_argument("--run")
    p.set_defaults(fn=lambda a: cmd(a, ctx))


def cmd(a, ctx):
    prj = ctx["project"](a)
    data, path = load(a.capture)
    out = ctx["run_dir"](a.run, "perf", prj)
    r = analyse(data)
    pics = charts(data, os.path.join(out, "evidence"))
    title = "Performance: %.0f fps average, p95 %.1f ms, %d hitches" % (r.get("fps", 0), r.get("p95", 0), r.get("hitches", 0)) if r.get("frames") else "Performance: no frames"
    page, rep = ctx["report"].write(out, title, findings(r), sections=[("Summary", [], text(r)), ("Charts", pics, None)],
                                    meta={"source": os.path.abspath(path), "summary": {k: v for k, v in r.items() if k != "near"}})
    return page, rep
