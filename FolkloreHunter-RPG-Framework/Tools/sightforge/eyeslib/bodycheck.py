"""Body gate (Studio 0.19): run on every built body before it goes into the game, so problems are caught at build time,
not seen first in play.

  bodycheck <model.fbx> [--clips a,b] [--baseline <report folder or report.json>]

Checks, each with a plain PASS or FAIL line (exit status 1 on any FAIL, so a script can refuse to install the body):
  pieces at their slots     every bone-held piece's pivot on its bone at rest (over 1 cm: FAIL; a piece seated on a
                            posed hand hangs off the hand in the game)
  height                    the body between 1.4 and 2.4 m tall (another scale breaks weapon placement and gait speeds)
  held pieces in the body   SightForge mesh3d over the clips: a held piece more than 4 cm inside the body in any clip FAILS
  skin                      a clip tearing over 6 % of the body's vertices FAILS; 3 to 6 % is listed to watch
  against a baseline        with --baseline (a bodycheck report of the body installed now): any clip worse than it was by
                            more than 2 cm of depth or 1.5 points of tearing FAILS (a regression); problems the baseline
                            already had are listed as KNOWN instead of failing, so they stay in view without blocking
The report holds every clip's numbers and the worst frame of each, pictured."""
import json
import os
import subprocess
import sys

from .findings import finding

HERE = os.path.dirname(os.path.abspath(__file__))
SLOT_CM, DEPTH_CM, TEAR_FAIL, TEAR_WATCH = 1.0, 4.0, 6.0, 3.0


def rest(model, out_json):
    from .mesh3d import blender_path
    subprocess.run([blender_path(), "-b", "--python", os.path.join(HERE, "bodycheck_blender.py"), "--", os.path.abspath(model), out_json],
                   capture_output=True, text=True, timeout=900)
    return json.load(open(out_json, encoding="utf-8")) if os.path.isfile(out_json) else None


def baseline_clips(path):
    if not path: return {}
    p = path if path.endswith(".json") else os.path.join(path, "report.json")
    d = json.load(open(p, encoding="utf-8"))
    return (d.get("meta") or {}).get("clips") or {}


def gate(r, rep, base):
    """[(PASS|FAIL|WATCH, what, detail)]"""
    out = []
    if not r:
        return [("FAIL", "rest pose", "the model could not be read")]
    off = {k: v for k, v in r["pieces"].items() if v["offset"] * 100 > SLOT_CM and not v["bone"].startswith(("chest", "spine", "hips"))}
    out.append(("FAIL" if off else "PASS", "pieces at their slots",
                ", ".join("%s %.0f cm off %s" % (k, v["offset"] * 100, v["bone"]) for k, v in off.items()) or "%d pieces on their bones" % len(r["pieces"])))
    out.append(("PASS" if 1.4 <= r["height"] <= 2.4 else "FAIL", "height", "%.2f m" % r["height"]))
    deep = {c: v for c, v in rep["clips"].items() if v.get("clipping", 0) > 0 and v.get("clipDepthCm", 0) > DEPTH_CM}
    hard = "FAIL" if not base else "KNOWN"          # with a baseline, standing problems are listed; only regressions fail
    out.append((hard if deep else "PASS", "held pieces in the body",
                "; ".join("%s %.0f cm%s" % (c, v["clipDepthCm"], (" (" + ", ".join(v["meets"]) + ")") if v.get("meets") else "") for c, v in sorted(deep.items(), key=lambda kv: -kv[1]["clipDepthCm"])) or "none deeper than %.0f cm" % DEPTH_CM))
    tear = {c: v["stretch"] for c, v in rep["clips"].items() if v["stretch"] >= TEAR_FAIL}
    watch = {c: v["stretch"] for c, v in rep["clips"].items() if TEAR_WATCH <= v["stretch"] < TEAR_FAIL}
    out.append((hard if tear else "PASS", "skin", "; ".join("%s %.1f %%" % kv for kv in tear.items()) or "no clip over %.0f %%" % TEAR_FAIL))
    if watch: out.append(("WATCH", "skin", "; ".join("%s %.1f %%" % kv for kv in sorted(watch.items(), key=lambda kv: -kv[1]))))
    if base:
        worse = []
        for c, v in rep["clips"].items():
            b = base.get(c)
            if not b: continue
            if v.get("clipDepthCm", 0) > b.get("clipDepthCm", 0) + 2 and v.get("clipDepthCm", 0) > DEPTH_CM / 2: worse.append("%s pieces %.0f -> %.0f cm" % (c, b.get("clipDepthCm", 0), v["clipDepthCm"]))
            if v["stretch"] > b.get("stretch", 0) + 1.5: worse.append("%s skin %.1f -> %.1f %%" % (c, b.get("stretch", 0), v["stretch"]))
        out.append(("FAIL" if worse else "PASS", "against the baseline", "; ".join(worse) or "no clip worse"))
    return out


def register(sp, ctx):
    p = sp.add_parser("bodycheck", help="the gate for a built body: pieces at their slots, nothing held inside the body, skin, against a baseline")
    p.add_argument("model"); p.add_argument("--clips"); p.add_argument("--baseline"); p.add_argument("--config"); p.add_argument("--run")
    p.set_defaults(fn=lambda a: cmd(a, ctx))


def cmd(a, ctx):
    from . import mesh3d
    prj = ctx["project"](a)
    stem = os.path.splitext(os.path.basename(a.model))[0]
    out = ctx["run_dir"](a.run, "bodycheck_" + stem, prj)
    ev = os.path.join(out, "evidence"); os.makedirs(ev, exist_ok=True)
    r = rest(a.model, os.path.join(ev, "rest.json"))
    cfg = json.load(open(a.config, encoding="utf-8")) if a.config else None
    rep = mesh3d.run(a.model, ev, clips=[c for c in (a.clips or "").split(",") if c] or None, config=cfg)
    rows = gate(r, rep, baseline_clips(a.baseline))
    for st, what, detail in rows: print("BODYCHECK %-5s %-26s %s" % (st, what, detail), flush=True)
    res = [finding("likely" if st == "FAIL" else "uncertain", "body gate: " + what, stem, detail) for st, what, detail in rows if st != "PASS"]
    res += mesh3d.findings(rep)
    pics = [c["picture"] for c in rep["clips"].values() if c.get("picture")]
    clips = {c: {"stretch": v["stretch"], "clipping": v.get("clipping", 0), "clipDepthCm": v.get("clipDepthCm", 0)} for c, v in rep["clips"].items()}
    page = ctx["report"].write(out, "Body gate: %s (%s)" % (stem, "FAIL" if any(s == "FAIL" for s, _, _ in rows) else "PASS"), res,
                               sections=[("Gate", [], "\n".join("%-5s %-26s %s" % row for row in rows)), ("Clips", [], mesh3d.table(rep)),
                                         ("Worst frame of each clip", pics[:60], None)],
                               meta={"model": os.path.abspath(a.model), "rest": r, "clips": clips})
    if any(s == "FAIL" for s, _, _ in rows):
        print("BODYCHECK FAIL", stem, flush=True)
        sys.stdout.flush()
        os._exit(1)
    print("BODYCHECK PASS", stem, flush=True)
    return page
