"""Motion library (Studio 0.19): animation clips kept for later, to put on any humanoid body.

  motionlib add <file.fbx|.glb> --name <pack> [--origin "Tripo"] [--license "..."] [--note "..."]
                                   store a character's clips (the file is copied in; its textures too) and catalogue
                                   them: length, frame rate, loop or not, travel speed in leg lengths a second, pictures
  motionlib list                   every pack and clip, with pictures (a report)
  motionlib show <pack>            one pack's report

The library lives in Studio's own folder (%LOCALAPPDATA%\\ClaudeEyesStudio\\motionlib\\<pack>\\: the source file, its
textures, manifest.json, previews). To put clips on a body, the game's model build takes them by name:

  build_slim.py ... --lib-clips "<pack>:<clip>=<Game_Clip>+<Game_Clip>,..."

which plays each clip onto that body through the retarget pose solver (motionlib_blender.source): any humanoid rig
whose hips, spine, arms, legs and first finger bones can be named (Tripo, Mixamo, Unreal-style, the game's own) can
give clips, and any slim body can take them, whatever its proportions or rest pose."""
import datetime
import glob
import json
import os
import shutil
import subprocess

from .findings import finding

HERE = os.path.dirname(os.path.abspath(__file__))
HOME = os.environ.get("CLAUDE_EYES_STUDIO_HOME") or os.path.join(os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "ClaudeEyesStudio")
LIB = os.path.join(HOME, "motionlib")


def packs():
    out = []
    for m in sorted(glob.glob(os.path.join(LIB, "*", "manifest.json"))):
        if "_before_" in os.path.basename(os.path.dirname(m)): continue          # earlier copies of a pack added again
        try: out.append(json.load(open(m, encoding="utf-8")))
        except ValueError: pass
    return out


def add(path, name, origin="", license_="", note=""):
    from .mesh3d import blender_path
    import re
    if not re.match(r"^[A-Za-z0-9_-]{1,48}$", name): raise ValueError("a pack name is letters, digits, _ and - (up to 48)")
    d = os.path.join(LIB, name)
    if os.path.isdir(d):
        old = d + "_before_" + datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
        shutil.move(d, old)                         # a pack added again keeps its earlier copy beside it
    os.makedirs(d)
    fn = os.path.basename(path); shutil.copyfile(path, os.path.join(d, fn))
    fbm = os.path.splitext(path)[0] + ".fbm"
    if os.path.isdir(fbm): shutil.copytree(fbm, os.path.join(d, os.path.splitext(fn)[0] + ".fbm"))
    log = subprocess.run([blender_path(), "-b", "--python", os.path.join(HERE, "motionlib_blender.py"), "--", "catalog", os.path.join(d, fn), d],
                         capture_output=True, text=True, timeout=1800)
    cj = os.path.join(d, "clips.json")
    if not os.path.isfile(cj): raise RuntimeError("the catalogue did not finish:\n" + "\n".join((log.stdout + log.stderr).splitlines()[-12:]))
    c = json.load(open(cj, encoding="utf-8"))
    man = {"name": name, "file": fn, "added": datetime.datetime.now().isoformat(timespec="seconds"), "from": os.path.abspath(path),
           "origin": origin, "license": license_, "note": note, "rig": c["rig"], "fps": c["fps"], "clips": c["clips"]}
    json.dump(man, open(os.path.join(d, "manifest.json"), "w", encoding="utf-8"), indent=1)
    strips(d, man)
    return man


def strips(d, man):
    """one picture per clip: its four moments side by side"""
    from PIL import Image
    for c in man["clips"]:
        ims = [Image.open(os.path.join(d, "previews", p)).convert("RGB") for p in c["previews"] if os.path.isfile(os.path.join(d, "previews", p))]
        if not ims: continue
        w, h = ims[0].size
        s = Image.new("RGB", (w * len(ims), h), (0, 0, 0))
        for i, im in enumerate(ims): s.paste(im, (i * w, 0))
        c["strip"] = "previews/%s_strip.png" % c["previews"][0].rsplit("_", 1)[0]
        s.save(os.path.join(d, c["strip"]))
    json.dump(man, open(os.path.join(d, "manifest.json"), "w", encoding="utf-8"), indent=1)


def table(ps):
    lines = ["%-16s %-28s %7s %5s %6s %10s  %s" % ("pack", "clip", "seconds", "loops", "fps", "legs/s", "travel")]
    for p in ps:
        for c in p["clips"]:
            lines.append("%-16s %-28s %7.2f %5s %6g %10.2f  %s" % (p["name"][:16], c["name"][:28], c["seconds"], "yes" if c["loops"] else "no", c["fps"], c["speedLegs"],
                                                               "%.2f m along %s" % (c["travel"], c["direction"]) if c["travel"] > .05 else "in place"))
    return "\n".join(lines)


def findings(ps):
    out = []
    for p in ps:
        if not p["rig"].get("fingers"):
            out.append(finding("uncertain", "motion library: no finger bones", p["name"], "the hands' turn is guessed from the wrist alone when these clips are put on a body"))
        for k in ("pelvis", "thigh_l", "foot_l", "upperarm_l", "hand_l", "head"):
            if k not in p["rig"]["names"]:
                out.append(finding("likely", "motion library: a body part not found", p["name"], "no bone for " + k + ": clips from this pack cannot be put on another body"))
    return out


def report(ctx, prj, ps, title, run=None):
    out = ctx["run_dir"](run, "motionlib", prj)
    ev = os.path.join(out, "evidence"); os.makedirs(ev, exist_ok=True)
    sections = [("Clips", [], table(ps))]
    for p in ps:
        pics = []
        for c in p["clips"]:
            if c.get("strip"):
                dst = os.path.join(ev, "%s__%s" % (p["name"], os.path.basename(c["strip"])))
                shutil.copyfile(os.path.join(LIB, p["name"], c["strip"]), dst); pics.append(dst)
        sections.append(("%s (%s%s): %d clips" % (p["name"], p.get("origin") or "?", (", " + p["license"]) if p.get("license") else "", len(p["clips"])), pics, p.get("note") or None))
    sections.append(("How to use", [], "build_slim.py ... --lib-clips \"<pack>:<clip>=<Game_Clip>+<Game_Clip>,...\"\n"
                     "e.g. --lib-clips \"tripo_male:standing_relax=Idle,tripo_male:walk=Walking_A\"\n"
                     "Library folder: " + LIB))
    return ctx["report"].write(out, title, findings(ps), sections=sections, meta={"library": LIB, "packs": [p["name"] for p in ps]})


def register(sp, ctx):
    p = sp.add_parser("motionlib", help="motion library: keep clips from any character file, to put on other bodies later")
    sub = p.add_subparsers(dest="ml_cmd", required=True)
    s = sub.add_parser("add"); s.add_argument("file"); s.add_argument("--name", required=True); s.add_argument("--origin", default="")
    s.add_argument("--license", default=""); s.add_argument("--note", default=""); s.add_argument("--run"); s.set_defaults(fn=lambda a: cmd(a, ctx))
    s = sub.add_parser("list"); s.add_argument("--run"); s.set_defaults(fn=lambda a: cmd(a, ctx))
    s = sub.add_parser("show"); s.add_argument("pack"); s.add_argument("--run"); s.set_defaults(fn=lambda a: cmd(a, ctx))


def cmd(a, ctx):
    prj = ctx["project"](a)
    if a.ml_cmd == "add":
        man = add(a.file, a.name, a.origin, a.license, a.note)
        print("Motion library: %s, %d clips -> %s" % (man["name"], len(man["clips"]), os.path.join(LIB, man["name"])))
        return report(ctx, prj, [man], "Motion library: %s (%d clips)" % (man["name"], len(man["clips"])), a.run)
    ps = packs() if a.ml_cmd == "list" else [p for p in packs() if p["name"] == a.pack]
    if not ps: raise ValueError("nothing in the motion library" + ("" if a.ml_cmd == "list" else " called " + a.pack))
    return report(ctx, prj, ps, "Motion library: %d packs, %d clips" % (len(ps), sum(len(p["clips"]) for p in ps)), a.run)
