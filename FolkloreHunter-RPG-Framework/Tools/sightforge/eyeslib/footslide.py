"""Foot-slide fixer (Studio 0.17): in-place walks, jogs and runs of a rigged model measured for foot slide, and fixed by
locking the planted feet (footslide_blender.py: the planted feet's own speed is the clip's travel speed; a planted foot
doing anything else slides; the fix holds each plant on the travelling ground with two-bone IK).

  footslide <model> [--clips a,b] [--fix <out.fbx>]

Findings: a clip whose planted feet slide more than 15 % of its travel speed (likely) or 6 % (uncertain); a clip with too
little ground contact to carry its speed. The report gives each clip's travel speed (what a game should move the body
at for its feet to stay planted) and, with --fix, the slide before and after, with each foot's path over the ground
(a planted foot should draw a still point; a sliding one draws a streak)."""
import json
import os
import subprocess

from .findings import finding
from . import devdraw as D

HERE = os.path.dirname(os.path.abspath(__file__))


def run(model, out_dir, clips=None, fix=None, timeout=3600):
    from .mesh3d import blender_path
    os.makedirs(out_dir, exist_ok=True)
    cmd = [blender_path(), "-b", "--python", os.path.join(HERE, "footslide_blender.py"), "--", os.path.abspath(model), os.path.abspath(out_dir)]
    if clips: cmd += ["--clips", ",".join(clips)]
    if fix: cmd += ["--fix", os.path.abspath(fix)]
    log = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
    p = os.path.join(out_dir, "footslide.json")
    if not os.path.isfile(p):
        raise RuntimeError("the foot-slide fixer did not finish:\n" + "\n".join((log.stdout + log.stderr).splitlines()[-12:]))
    return json.load(open(p, encoding="utf-8"))


def findings(rep):
    out = []; sub = os.path.splitext(os.path.basename(rep["model"]))[0]
    for name, c in rep["clips"].items():
        b = c["before"]; a = c.get("after")
        if b.get("speed", 0) <= .3: continue
        share = (a or b)["residualShare"]
        state = "after the fix" if a else "as made"
        if share > .15:
            out.append(finding("likely", "foot slide", "%s %s" % (sub, name), "planted feet slide %.0f %% of the %.2f m/s travel speed %s" % (share * 100, b["speed"], state),
                               measured=share, threshold=.15, fix=None if a else "run with --fix (or the game build's --footlock)"))
        elif share > .06:
            out.append(finding("uncertain", "foot slide", "%s %s" % (sub, name), "planted feet slide %.0f %% of the %.2f m/s travel speed %s" % (share * 100, b["speed"], state),
                               measured=share, threshold=.06))
        if b.get("contact", 1) < .25:
            out.append(finding("uncertain", "little ground contact", "%s %s" % (sub, name), "each foot is down %.0f %% of the cycle" % (b["contact"] * 100), measured=b["contact"], threshold=.25))
    return out


def pictures(rep, out_dir):
    os.makedirs(out_dir, exist_ok=True); pics = []
    for name, c in rep["clips"].items():
        for when in ("tracksBefore", "tracksAfter"):
            tr = c.get(when)
            if not tr: continue
            pts = [(p[1], p[3], p[2]) for t in tr.values() for p in t]
            v = D.View(pts, "top", size=(420, 420))
            for k, (s, t) in enumerate(sorted(tr.items())):
                col = D.PALETTE[k]
                for p, q in zip(t, t[1:]):
                    v.line((p[1], 0, p[2]), (q[1], 0, q[2]), col, 2)
                lo = min(p[3] for p in t)
                for p in t:
                    if p[3] <= lo + .035: v.dot((p[1], 0, p[2]), (255, 255, 255), 2)
            r = c["after" if when == "tracksAfter" else "before"]
            v.title("%s %s: feet over the ground (white: planted)" % (name, "after the fix" if when == "tracksAfter" else "as made"))
            v.title("travel %.2f m/s, slide %.0f %%" % (c["before"]["speed"], r["residualShare"] * 100), 1)
            pics.append(v.save(os.path.join(out_dir, "%s_%s.png" % ("".join(ch if ch.isalnum() else "_" for ch in name), when))))
    return pics


def table(rep):
    lines = ["%-28s %7s %8s %8s %8s %7s %7s" % ("clip", "m/s", "heading", "contact", "slide", "after", "plants")]
    for name, c in rep["clips"].items():
        b = c["before"]; a = c.get("after")
        lines.append("%-28s %7.2f %7.0f° %7.0f%% %7s %7s %7s" % (name[:28], b.get("speed", 0), b.get("direction", 0), b.get("contact", 0) * 100,
                                                            "%.0f%%" % (b["residualShare"] * 100) if "residualShare" in b else "-",
                                                            "%.0f%%" % (a["residualShare"] * 100) if a else "-", c.get("locked", "-")))
    return "\n".join(lines)


def register(sp, ctx):
    p = sp.add_parser("footslide", help="in-place walks and runs: foot slide measured, and fixed by locking planted feet")
    p.add_argument("model"); p.add_argument("--clips"); p.add_argument("--fix", help="write the fixed model here")
    p.add_argument("--run")
    p.set_defaults(fn=lambda a: cmd(a, ctx))


def cmd(a, ctx):
    prj = ctx["project"](a)
    stem = os.path.splitext(os.path.basename(a.model))[0]
    out = ctx["run_dir"](a.run, "footslide_" + stem, prj)
    rep = run(a.model, os.path.join(out, "evidence"), [c for c in (a.clips or "").split(",") if c] or None, a.fix)
    return ctx["report"].write(out, "Foot slide: %s (%d clips)%s" % (stem, len(rep["clips"]), ", fixed" if a.fix else ""), findings(rep),
                               sections=[("Clips", [], table(rep)), ("Feet over the ground", pictures(rep, os.path.join(out, "evidence")), None)],
                               meta={"model": os.path.abspath(a.model), "fixed": rep.get("fixed"), "legs": rep.get("legs")})
