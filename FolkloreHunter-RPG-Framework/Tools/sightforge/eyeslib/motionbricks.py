"""MotionBricks in SightForge (Studio 0.17): NVIDIA MotionBricks' natural human motion for any humanoid model, run on this
PC (free and local: the MotionBricks checkout and its uv environment; code Apache-2.0, model under the NVIDIA Open Model
Agreement; its sample clips are never used, only your own poses go in).

  motionbricks setup [--home <MotionBricks checkout>]       find MotionBricks, its environment and the GPU; save the
                                                            SOMA skeleton (once)
  motionbricks inbetween <model> --clips a,b [--keys auto|a=0,.4,.9] --out <model.fbx>
        each clip's motion between its key poses made again by MotionBricks (key poses kept at their times, so strikes
        stay timed); the model written with the new clips
  motionbricks transition <model> --from clipA@sec --to clipB@sec [--seconds .6] [--name N] --out <model.fbx>
        a new clip that moves naturally from a moment of one clip into a moment of another
Both reports show each clip before and after as stick-figure strips (side and front), and how smooth it is (the role
joints' mean acceleration: lower is smoother), and run the 3D mesh system on the result when --check is given.

The game's own pipeline (Tools/motionbricks/mb_bridge.py, --mb in build_slim.py) does the same for the Folklore Hunter
slim rig with its hand-slot detail; this is the general tool for any rig."""
import json
import os
import shutil
import subprocess

from .findings import finding
from . import devdraw as D

HERE = os.path.dirname(os.path.abspath(__file__))
STORE = os.path.join(os.environ.get("CLAUDE_EYES_STUDIO_HOME") or os.path.join(os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "ClaudeEyesStudio"), "MotionBricks")
CONFIG = os.path.join(STORE, "config.json")
GUESSES = [r"C:\AW\motionbricks", os.path.expanduser("~/motionbricks"), os.path.expanduser("~/MotionBricks")]
LINKS = [("hips", "spine"), ("spine", "chest"), ("chest", "neck"), ("neck", "head")]
for _s in ("l", "r"):
    LINKS += [("chest", "upperarm." + _s), ("upperarm." + _s, "lowerarm." + _s), ("lowerarm." + _s, "hand." + _s),
              ("hips", "upperleg." + _s), ("upperleg." + _s, "lowerleg." + _s), ("lowerleg." + _s, "foot." + _s), ("foot." + _s, "toes." + _s)]


def config():
    try:
        return json.load(open(CONFIG, encoding="utf-8"))
    except (OSError, ValueError):
        return {}


def uv_path():
    p = shutil.which("uv")
    if p: return p
    for c in (os.path.expanduser(r"~\AppData\Roaming\Python\Python312\Scripts\uv.exe"), os.path.expanduser("~/.local/bin/uv"), os.path.expanduser("~/.cargo/bin/uv")):
        if os.path.isfile(c): return c
    return None


def home(given=None):
    for h in [given, config().get("home")] + GUESSES:
        if h and os.path.isfile(os.path.join(h, "pyproject.toml")) and os.path.isdir(os.path.join(h, "motionbricks")):
            return h
    return None


def mb_python(script, args, mb_home, timeout=3600):
    uv = uv_path()
    if not uv: raise RuntimeError("uv (the MotionBricks environment's runner) was not found")
    log = subprocess.run([uv, "run", "python", script] + args, cwd=mb_home, capture_output=True, text=True, timeout=timeout)
    return log.returncode, log.stdout + log.stderr


def blender(args, timeout=3600):
    from .mesh3d import blender_path
    log = subprocess.run([blender_path(), "-b", "--python", os.path.join(HERE, "motionbricks_blender.py"), "--"] + args, capture_output=True, text=True, timeout=timeout)
    return log.returncode, log.stdout + log.stderr


def tail(text, n=14):
    return "\n".join(l for l in text.splitlines() if l.strip())[-3000:].splitlines()[-n:]


def setup(given=None):
    h = home(given)
    rep = {"home": h, "uv": uv_path()}
    try:
        g = subprocess.run(["nvidia-smi", "--query-gpu=name,memory.total", "--format=csv,noheader"], capture_output=True, text=True, timeout=30)
        rep["gpu"] = g.stdout.strip() or None
    except (OSError, subprocess.SubprocessError):
        rep["gpu"] = None
    if h and rep["uv"]:
        os.makedirs(STORE, exist_ok=True)
        soma = os.path.join(STORE, "soma.json")
        if not os.path.isfile(soma):
            code, out = mb_python(os.path.join(HERE, "mb_env", "mb_skeleton.py"), [soma], h, 900)
            rep["skeletonLog"] = tail(out)
        rep["soma"] = soma if os.path.isfile(soma) else None
        json.dump({"home": h}, open(CONFIG, "w", encoding="utf-8"))
    return rep


def soma_path():
    p = os.path.join(STORE, "soma.json")
    if not os.path.isfile(p):
        r = setup()
        if not r.get("soma"): raise RuntimeError("MotionBricks is not set up: run 'motionbricks setup --home <the MotionBricks checkout>' (found: %s)" % r)
    return p


def generate(export_json, out_json):
    h = home()
    if not h: raise RuntimeError("MotionBricks checkout not found (motionbricks setup --home <folder>)")
    code, log = mb_python(os.path.join(HERE, "mb_env", "mb_generate.py"), [export_json, out_json], h)
    if not os.path.isfile(out_json): raise RuntimeError("MotionBricks did not generate:\n" + "\n".join(tail(log)))
    return [l for l in log.splitlines() if l.startswith("MB_")]


def smoothness(frames, fps):
    """mean acceleration of the role joints (m/s^2): lower is smoother"""
    if not frames or len(frames) < 3: return None
    tot, n = 0.0, 0
    for a, b, c in zip(frames, frames[1:], frames[2:]):
        for r in b:
            if r in a and r in c:
                tot += sum((a[r][i] - 2 * b[r][i] + c[r][i]) ** 2 for i in range(3)) ** .5 * fps * fps; n += 1
    return tot / max(1, n)


def strip(frames, path, title, count=8):
    """stick figures across the clip, side view (along X) above front view"""
    if not frames: return None
    from PIL import Image
    idx = [round(i * (len(frames) - 1) / max(1, count - 1)) for i in range(count)]
    cells = []
    frames = [{r: (p[0], p[2], p[1]) for r, p in f.items()} for f in frames]     # Blender is Z up; the views draw Y up
    allp = [p for f in frames for p in f.values()]
    for plane, along in (("side", (0.0, 1.0)), ("side", (1.0, 0.0))):
        row = []
        for i in idx:
            f = frames[i]
            v = D.View(allp, plane, size=(170, 230), margin=14, along=along)
            for a, b in LINKS:
                if a in f and b in f:
                    side = b.endswith(".l")
                    v.line(f[a], f[b], (110, 200, 255) if side else (255, 170, 90) if b.endswith(".r") else (230, 230, 230), 3)
            v.d.text((6, 4), "%d" % i, fill=(200, 200, 200), font=v.f)
            row.append(v.img)
        cells.append(row)
    W = sum(c.width for c in cells[0]); H = sum(r[0].height for r in cells) + 26
    im = Image.new("RGB", (W, H), D.BG)
    from PIL import ImageDraw
    ImageDraw.Draw(im).text((8, 5), title + "   (top: from the side; bottom: from the front; blue left, orange right)", fill=(230, 230, 230), font=D.font(13))
    y = 26
    for row in cells:
        x = 0
        for c in row: im.paste(c, (x, y)); x += c.width
        y += row[0].height
    im.save(path); return path


def register(sp, ctx):
    p = sp.add_parser("motionbricks", help="NVIDIA MotionBricks (local): natural motion between a clip's key poses, or a new transition, for any humanoid model")
    sub = p.add_subparsers(dest="mb_cmd", required=True)
    s = sub.add_parser("setup"); s.add_argument("--home"); s.add_argument("--run")
    s.set_defaults(fn=lambda a: cmd_setup(a, ctx))
    s = sub.add_parser("inbetween"); s.add_argument("model"); s.add_argument("--clips", required=True); s.add_argument("--keys", default="auto")
    s.add_argument("--out", required=True); s.add_argument("--check", action="store_true"); s.add_argument("--run")
    s.set_defaults(fn=lambda a: cmd_make(a, ctx, "inbetween"))
    s = sub.add_parser("transition"); s.add_argument("model"); s.add_argument("--from", dest="src", required=True); s.add_argument("--to", dest="dst", required=True)
    s.add_argument("--seconds", type=float, default=.6); s.add_argument("--name"); s.add_argument("--out", required=True); s.add_argument("--check", action="store_true"); s.add_argument("--run")
    s.set_defaults(fn=lambda a: cmd_make(a, ctx, "transition"))


def cmd_setup(a, ctx):
    prj = ctx["project"](a)
    r = setup(a.home)
    out = ctx["run_dir"](a.run, "motionbricks_setup", prj)
    res = []
    if not r["home"]: res.append(finding("not_completed", "MotionBricks checkout", "setup", "not found in %s (pass --home)" % ", ".join(GUESSES)))
    if not r["uv"]: res.append(finding("not_completed", "uv", "setup", "the uv runner was not found"))
    if not r.get("gpu"): res.append(finding("uncertain", "GPU", "setup", "nvidia-smi found no NVIDIA GPU (MotionBricks runs on CUDA)"))
    if r["home"] and r["uv"] and not r.get("soma"): res.append(finding("not_completed", "SOMA skeleton", "setup", "\n".join(r.get("skeletonLog") or [])))
    return ctx["report"].write(out, "MotionBricks setup: %s" % ("ready" if r.get("soma") else "not ready"), res, sections=[("Found", [], json.dumps(r, indent=1))], meta=r)


def cmd_make(a, ctx, mode):
    prj = ctx["project"](a)
    stem = os.path.splitext(os.path.basename(a.model))[0]
    out = ctx["run_dir"](a.run, "motionbricks_%s_%s" % (mode, stem), prj)
    ev = os.path.join(out, "evidence"); os.makedirs(ev, exist_ok=True)
    soma = soma_path()
    exp, gen, poses = os.path.join(ev, "export.json"), os.path.join(ev, "mb_out.json"), os.path.join(ev, "poses.json")
    model = os.path.abspath(a.model)
    if mode == "inbetween":
        code, log = blender(["export", model, soma, exp, "--clips", a.clips, "--keys", a.keys])
    else:
        code, log = blender(["transition", model, soma, exp, "--from", a.src, "--to", a.dst, "--seconds", str(a.seconds)] + (["--name", a.name] if a.name else []))
    lines = [l for l in log.splitlines() if l.startswith("MB_")]
    if not os.path.isfile(exp): raise RuntimeError("the export did not finish:\n" + "\n".join(tail(log)))
    lines += generate(exp, gen)
    code, log = blender(["load", model, soma, exp, gen, os.path.abspath(a.out), poses])
    lines += [l for l in log.splitlines() if l.startswith("MB_")]
    if not os.path.isfile(poses): raise RuntimeError("putting the motion back did not finish:\n" + "\n".join(tail(log)))
    P = json.load(open(poses, encoding="utf-8"))
    res, pics, table = [], [], ["%-30s %7s %12s %12s" % ("clip", "frames", "accel before", "accel after")]
    for name, c in P.items():
        fps = c.get("fps", 30)
        sb, sa = smoothness(c.get("before"), fps), smoothness(c["after"], fps)
        table.append("%-30s %7d %12s %12s" % (name[:30], len(c["after"]), "%.1f" % sb if sb is not None else "-", "%.1f" % sa if sa is not None else "-"))
        safe = "".join(ch if ch.isalnum() else "_" for ch in name)
        if c.get("before"): pics.append(strip(c["before"], os.path.join(ev, safe + "_before.png"), name + " before"))
        pics.append(strip(c["after"], os.path.join(ev, safe + "_after.png"), name + " with MotionBricks"))
        if sb and sa and sa > sb * 1.5:
            res.append(finding("uncertain", "rougher than before", name, "mean joint acceleration %.1f -> %.1f m/s2" % (sb, sa), measured=sa, threshold=sb * 1.5))
    sections = [("Clips", [], "\n".join(table)), ("Before and after", [p for p in pics if p], None), ("Log", [], "\n".join(lines[-60:]))]
    if a.check:
        from . import mesh3d
        rep = mesh3d.run(os.path.abspath(a.out), os.path.join(ev, "mesh3d"), clips=list(P))
        res += mesh3d.findings(rep)
        sections.append(("3D mesh check of the result", [c["picture"] for c in rep["clips"].values() if c.get("picture")], mesh3d.table(rep)))
    return ctx["report"].write(out, "MotionBricks %s: %s (%d clips)" % (mode, stem, len(P)), res, sections=sections, meta={"model": model, "out": os.path.abspath(a.out)})
