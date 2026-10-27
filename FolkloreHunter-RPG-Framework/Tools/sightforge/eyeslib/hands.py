"""The hands check (Studio 0.19): a built model's fingers on each weapon it holds, measured on the skinned mesh.

  hands <model.fbx> [--grips <model>_grips.json]

The model's grips file (written by the build's --fingers: each finger's curl at its three joints for every held piece,
the relaxed hand and the bow-string hook) is put on the hand exactly as the game's HandGrip does it, then measured
against the held piece's own mesh (hands_blender.py):
  finger inside the piece     finger skin more than 2 mm inside the handle (likely when 4+ vertices of one finger)
  finger clear of the piece   a finger the solver closed on the handle that still stays more than 8 mm from it
  finger stopped at its limit a finger that reached its joint limits without meeting the handle (a handle too thin
                              or too far from the palm for a closed hand)
  the handle through a finger  the handle already passed through the finger with the hand open (the grip point or
                              the finger's place is wrong)
Pictures of each grip from the back of the hand, the palm and the thumb side show what the numbers mean."""
import json
import os
import subprocess

from .findings import finding

HERE = os.path.dirname(os.path.abspath(__file__))


def run(model, grips, out_dir, timeout=1800):
    from .mesh3d import blender_path
    os.makedirs(out_dir, exist_ok=True)
    cmd = [blender_path(), "-b", "--python", os.path.join(HERE, "hands_blender.py"), "--", os.path.abspath(model), os.path.abspath(grips), os.path.abspath(out_dir)]
    log = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
    p = os.path.join(out_dir, "hands.json")
    if not os.path.isfile(p):
        raise RuntimeError("the hands check did not finish:\n" + "\n".join((log.stdout + log.stderr).splitlines()[-12:]))
    return json.load(open(p, encoding="utf-8"))


def findings(rep, grips):
    out = []; sub = os.path.splitext(rep["model"])[0]
    for m in rep.get("missing", []):
        out.append(finding("not_completed", "hands: not checked", sub, m if " " in m else "the piece %s is not in the model" % m))
    for side, h in (grips.get("hands") or {}).items():
        if "why" in h:
            out.append(finding("likely", "hands: no fingers", "%s hand.%s" % (sub, side), "no finger bones: " + h["why"]))
    for name, g in rep.get("grips", {}).items():
        if "fingers" not in g: continue
        for f, r in g["fingers"].items():
            where = "%s %s (hand.%s) %s" % (sub, name, g["side"], f)
            if r["inside"] >= 4:
                out.append(finding("likely", "hands: finger inside the piece", where, "%d of %d finger vertices more than 2 mm inside, deepest %.0f mm" % (r["inside"], r["vertices"], r["deepest"] * 1000),
                                   measured=r["deepest"], threshold=.002, fix="less curl on that joint (grips file), or a thinner handle"))
            elif r["inside"]:
                out.append(finding("uncertain", "hands: finger touching into the piece", where, "%d vertices up to %.0f mm inside" % (r["inside"], r["deepest"] * 1000), measured=r["deepest"]))
            if r.get("solver") == "contact" and r.get("gap") is not None and r["gap"] > .008:
                out.append(finding("likely", "hands: finger clear of the piece", where, "closed on the handle by the solver, but %.0f mm away on the skin" % (r["gap"] * 1000),
                                   measured=r["gap"], threshold=.008, fix="the finger's thickness or the handle's radius measured wrong"))
            if r.get("solver") == "free":
                out.append(finding("uncertain", "hands: finger stopped at its limit", where, "curled to its joint limits without meeting the handle (gap %s mm)" % (round(r["gap"] * 1000) if r.get("gap") is not None else "?"),
                                   measured=r.get("gap")))
            if r.get("solver") == "through":
                out.append(finding("likely", "hands: the handle through a finger", where, "the handle passes through the finger with the hand open", fix="move the grip point (hand slot) into the palm"))
    return out


def table(rep):
    lines = ["%-22s %-4s %-7s %7s %7s %8s %7s  %s" % ("grip", "hand", "finger", "verts", "inside", "deepest", "gap", "curl (solver)")]
    for name, g in rep.get("grips", {}).items():
        for f, r in (g.get("fingers") or {}).items():
            lines.append("%-22s %-4s %-7s %7d %7d %6.1fmm %5smm  %s (%s)" % (name[:22], g["side"], f, r["vertices"], r["inside"], r["deepest"] * 1000,
                                                                       "%.1f" % (r["gap"] * 1000) if r.get("gap") is not None else "-", r.get("curl"), r.get("solver")))
    return "\n".join(lines)


def register(sp, ctx):
    p = sp.add_parser("hands", help="fingers on each held weapon: skin inside the piece, fingers clear of it, pictures of every grip")
    p.add_argument("model"); p.add_argument("--grips", help="the grips file (default: <model>_grips.json beside the model)")
    p.add_argument("--run")
    p.set_defaults(fn=lambda a: cmd(a, ctx))


def cmd(a, ctx):
    prj = ctx["project"](a)
    stem = os.path.splitext(os.path.basename(a.model))[0]
    gp = a.grips or os.path.join(os.path.dirname(os.path.abspath(a.model)), stem + "_grips.json")
    if not os.path.isfile(gp): raise FileNotFoundError("no grips file: " + gp + " (build the model with --fingers)")
    grips = json.load(open(gp, encoding="utf-8"))
    out = ctx["run_dir"](a.run, "hands_" + stem, prj)
    ev = os.path.join(out, "evidence")
    rep = run(a.model, gp, ev)
    sections = [("Fingers on each grip", [], table(rep))]
    for name, g in rep.get("grips", {}).items():
        sections.append(("%s (hand.%s): back of the hand, palm, thumb side" % (name, g["side"]), [os.path.join(ev, p) for p in g["pictures"]], None))
    sections.append(("Measurements", [], json.dumps({"hands": grips.get("hands"), "contacts": grips.get("contacts")}, indent=1)))
    return ctx["report"].write(out, "Hands: %s (%d grips)" % (stem, len(rep.get("grips", {}))), findings(rep, grips), sections=sections,
                               meta={"model": os.path.abspath(a.model), "grips": gp})
