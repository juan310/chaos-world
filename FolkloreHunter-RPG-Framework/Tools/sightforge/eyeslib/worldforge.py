"""WorldForge in Studio (0.18): procedural environments for a Unity game, made from an editable recipe (JSON) by the
WorldForge Unity package (the game's WorldForge/ folder: a signed field meshed by surface nets into chunks with collision,
procedural materials, water, lights and effects), built and tested in an isolated sandbox Unity project, and checked here.

  worldforge sandbox [--sandbox DIR]               make the isolated Unity project (once): the package linked from the
                                                   game folder, the test hunter copied in
  worldforge set <recipe> key=value ...            change a recipe: seed=12, voxel=.4, atmosphere.time=night,
                                                   features.tunnel.width=5, features.chamber.points.0=0,.5,80, ...
  worldforge build <recipe> [--sandbox DIR]        the playable preview (Unity batch build): DIR/Build/WorldForgePreview.exe
  worldforge test <recipe> [--sandbox DIR]         the preview run with its automated test (WFTester), the video made
                                                   (ffmpeg), then the report
  worldforge report <capture folder>               the report of a test run

What each check measures (from the test run inside the real player, not from pictures alone):
  walk       the hunter's CharacterController (the game's limits) jogging the route; stuck points, distance reached
  navigation navigation meshes built from the colliders for hunter, skeleton and boss sizes; complete paths or not;
             the share of the arena floor the boss can stand on
  collision  rays at 2000 points of the rendered terrain must meet a collider within 15 cm; props without colliders;
             wall torches' gap to the wall
  mesh       open edges of the whole surface inside the generation box (holes, seams between chunks)
  camera     the share of walk frames the camera was pulled in by walls, had its view of the hunter blocked, sat inside rock
  feet       the lower ball of the foot against the ground under it along the walk, against where it sits standing
             still: sinking (deepest), floating (the planted foot of each step; a run's flight phase is not floating)
  lights     lights reaching each chunk against the per-pixel light limit of each quality preset (more pop on and off)
  lighting   brightness of the viewpoint shots and of the walk frames (too dark to read, blown out)
  perf       frame times per viewpoint at Low, Medium and High
  terrain    the survey in SightForge's terrain format: unreachable ground, holes, open ledges, floating props"""
import glob
import json
import os
import re
import shutil
import subprocess
import time

from .findings import finding
from . import devdraw as D

UNITY = r"C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe"
SANDBOX = os.path.join(os.path.expanduser("~"), "Documents", "WorldForge-Sandbox")


def _sandbox(a):
    return os.path.abspath(getattr(a, "sandbox", None) or SANDBOX)


def _package(prj):
    p = os.path.join(prj.root, "WorldForge")
    if not os.path.isfile(os.path.join(p, "package.json")):
        raise FileNotFoundError("the WorldForge package is not in " + p)
    return p


def make_sandbox(prj, sb):
    log = []
    if not os.path.isdir(os.path.join(sb, "Assets")):
        r = subprocess.run([UNITY, "-batchmode", "-nographics", "-createProject", sb, "-quit", "-logFile", os.path.join(os.path.dirname(sb), "worldforge-create.log")], capture_output=True, text=True, timeout=1800)
        log.append("created the sandbox Unity project (exit %d)" % r.returncode)
    man = os.path.join(sb, "Packages", "manifest.json")
    m = json.load(open(man, encoding="utf-8"))
    rel = os.path.relpath(_package(prj), os.path.join(sb, "Packages")).replace("\\", "/")
    if m["dependencies"].get("com.folklorehunter.worldforge") != "file:" + rel:
        m["dependencies"]["com.folklorehunter.worldforge"] = "file:" + rel
        json.dump(m, open(man, "w", encoding="utf-8"), indent=2)
        log.append("linked the package: file:" + rel)
    dst = os.path.join(sb, "Assets", "Resources", "WorldForgeTest"); os.makedirs(dst, exist_ok=True)
    src = os.path.join(prj.root, "Assets", "Resources", "Models")
    for a, b in (("Hunter_Male_Slim.fbx", "Hunter.fbx"), ("Hunter_Male_Slim_texture.png", "Hunter_texture.png")):
        if os.path.isfile(os.path.join(src, a)):
            if not os.path.isfile(os.path.join(dst, b)) or os.path.getmtime(os.path.join(src, a)) > os.path.getmtime(os.path.join(dst, b)):
                shutil.copyfile(os.path.join(src, a), os.path.join(dst, b)); log.append("copied the test hunter: " + a)
    return log


def _step(node, key):
    """into a dict by key, or a list by index or by an item's name (features.tunnel, lights.torch_row)"""
    if isinstance(node, list):
        if key.isdigit(): return node[int(key)]
        for x in node:
            if isinstance(x, dict) and key in (x.get("name"), x.get("feature"), x.get("type")): return x
        raise KeyError("no item named " + key)
    return node[key]


def _value(val):
    if "," in val and all(re.match(r"^-?[\d.]+$", x) for x in val.split(",")):
        n = [float(x) for x in val.split(",")]
        return dict(zip("xyz", n)) if len(n) == 3 else dict(zip("rgba", n))
    if re.match(r"^-?\d+$", val): return int(val)
    if re.match(r"^-?[\d.]+$", val): return float(val)
    if val in ("true", "false"): return val == "true"
    return val


def set_values(path, pairs):
    r = json.load(open(path, encoding="utf-8"))
    for kv in pairs:
        key, val = kv.split("=", 1)
        parts = key.split(".")
        node = r
        for k in parts[:-1]: node = _step(node, k)
        last = parts[-1]
        if isinstance(node, list): node[int(last)] = _value(val)
        else: node[last] = _value(val)
    json.dump(r, open(path, "w", encoding="utf-8"), indent=1)
    return r


def build(prj, sb, recipe):
    log_file = os.path.join(sb, "Logs", "worldforge-build.log"); os.makedirs(os.path.dirname(log_file), exist_ok=True)
    r = subprocess.run([UNITY, "-batchmode", "-projectPath", sb, "-executeMethod", "FH.WorldForge.WFBuild.BuildPreview", "-wfRecipe", os.path.abspath(recipe),
                        "-wfOut", os.path.join(sb, "Build"), "-logFile", log_file, "-quit"], capture_output=True, text=True, timeout=3600)
    lines = [l for l in open(log_file, encoding="utf-8", errors="replace").read().splitlines() if "WF_BUILD" in l or "error CS" in l or "Shader error" in l or "Exception" in l][:40]
    return r.returncode, lines, os.path.join(sb, "Build", "WorldForgePreview.exe")


def run_test(sb, recipe, out, timeout=900):
    exe = os.path.join(sb, "Build", "WorldForgePreview.exe")
    if not os.path.isfile(exe): raise FileNotFoundError("no preview build: run worldforge build first")
    # a preview left running (a test that failed half way) holds its files: close it first
    if os.name == "nt":
        subprocess.run(["taskkill", "/IM", "WorldForgePreview.exe", "/F"], capture_output=True)
        time.sleep(1)
    if os.path.isdir(out): shutil.rmtree(out, ignore_errors=True)
    os.makedirs(out, exist_ok=True)
    t0 = time.time()
    p = subprocess.Popen([exe, "--wf-test", "--wf-quit", "--wf-recipe", os.path.abspath(recipe), "--wf-out", out, "-screen-width", "1280", "-screen-height", "720",
                          "-screen-fullscreen", "0", "-logFile", os.path.join(out, "player.log")])
    try:
        p.wait(timeout=timeout)
    except subprocess.TimeoutExpired:
        p.kill()
    return time.time() - t0


def make_video(folder):
    frames = os.path.join(folder, "frames")
    if not os.path.isdir(frames) or not os.listdir(frames): return None
    ff = shutil.which("ffmpeg") or next(iter(glob.glob(os.path.expandvars(r"%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg*\*\bin\ffmpeg.exe"))), None)
    if not ff: return None
    out = os.path.join(folder, "walkthrough.mp4")
    subprocess.run([ff, "-y", "-framerate", "30", "-i", os.path.join(frames, "f%05d.jpg"), "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20", "-movflags", "+faststart", out],
                   capture_output=True, timeout=600)
    return out if os.path.isfile(out) else None


# ------------------------------------------------------------------------------------------------------ the report
def _j(folder, name):
    p = os.path.join(folder, name)
    return json.load(open(p, encoding="utf-8")) if os.path.isfile(p) else None


def _lum(path, size=(160, 90)):
    from PIL import Image
    im = Image.open(path).convert("L").resize(size)
    px = list(im.getdata())
    px.sort()
    return sum(px) / len(px) / 255, px[int(len(px) * .95)] / 255


def analyse(folder):
    d = {k: _j(folder, k + ".json") for k in ("info", "mesh", "collision", "nav", "walk", "lights")}
    d["perf"] = {q: _j(folder, "perf_%s.json" % q) for q in ("Low", "Medium", "High")}
    d["shots"] = {}
    for p in sorted(glob.glob(os.path.join(folder, "shots", "*.jpg"))):
        d["shots"][os.path.basename(p)[:-4]] = _lum(p)
    frames = sorted(glob.glob(os.path.join(folder, "frames", "*.jpg")))
    d["frames"] = [(i / 30, *_lum(frames[i])) for i in range(0, len(frames), 10)]
    d["framePaths"] = frames
    return d


def findings(d):
    out = []
    w = d.get("walk")
    if w:
        share = w["reached"] / max(.01, w["routeLength"])
        if share < .97 or w["nudges"]:
            where = "; ".join("(%.1f, %.1f, %.1f) at %.1f m" % (*s["at"], s["s"]) for s in w["stuck"][:6])
            out.append(finding("likely" if w["nudges"] else "uncertain", "walk: the route is not walkable throughout", "walk",
                               "the hunter reached %.1f of %.1f m (%.0f %%); stuck %d times (moved on 1.5 m each time): %s" % (w["reached"], w["routeLength"], share * 100, w["nudges"], where or "-"),
                               measured=share, threshold=.97, fix="widen or lower the passage there, or ease the step or slope"))
        cam = w["camera"]
        if cam["lineBlocked"] > .1:
            out.append(finding("likely", "camera: view of the hunter blocked", "walk", "%.0f %% of frames the line from the camera to the hunter's head passed through rock" % (cam["lineBlocked"] * 100),
                               measured=cam["lineBlocked"], threshold=.1, fix="widen tight spots, or a closer camera in tunnels"))
        if cam["inside"] > .01:
            out.append(finding("likely", "camera: inside rock", "walk", "%.1f %% of frames the camera itself was inside the terrain" % (cam["inside"] * 100), measured=cam["inside"], threshold=.01))
        if cam["pulledIn"] > .3:
            out.append(finding("uncertain", "camera: pulled in by walls", "walk", "%.0f %% of frames walls pushed the camera more than 30 cm closer (expected in tunnels)" % (cam["pulledIn"] * 100), measured=cam["pulledIn"]))
        f = w["feet"]
        rest = f.get("rest") or 0          # the ball of the foot standing still on flat ground (bone inside the foot, skin width)
        if f.get("deepest") is not None and f["deepest"] - rest < -.06:
            out.append(finding("likely", "feet: sinking into the ground", "walk", "the lower ball of the foot went %.0f cm deeper than it sits standing still (it rests %.0f cm up)" % (-(f["deepest"] - rest) * 100, rest * 100),
                               measured=f["deepest"] - rest, threshold=-.06, fix="foot placement (IK) on slopes and bumps; or a smoother floor"))
        if f.get("planted") is not None and f["planted"] < 9 and f["planted"] - rest > .06:
            out.append(finding("uncertain", "feet: floating", "walk", "at each step the planted foot stays %.0f cm higher than it sits standing still" % ((f["planted"] - rest) * 100),
                               measured=f["planted"] - rest, threshold=.06, fix="foot placement (IK), or the clip's contact height"))
    n = d.get("nav")
    if n:
        for a in n["agents"]:
            for leg, key in (("start to arena", "toArena"), ("arena to start", "back")):
                st = a[key].get("status")
                if st != "PathComplete":
                    kind = "uncertain" if a["agent"] == "boss" else "likely"
                    out.append(finding(kind, "navigation: no complete path", "%s (r %.2f m)" % (a["agent"], a["radius"]), "%s: %s%s" % (leg, st, (" (the boss is meant to stay in its arena)" if a["agent"] == "boss" else "")),
                                       fix="widen the passage beyond twice the agent radius, ease steps and slopes"))
            if a["agent"] == "boss" and a.get("arenaFloor") is not None and a["arenaFloor"] < .6:
                out.append(finding("likely", "arena: room for the boss", "boss (r %.2f m)" % a["radius"], "the boss can stand on %.0f %% of the arena floor" % (a["arenaFloor"] * 100), measured=a["arenaFloor"], threshold=.6))
    c = d.get("collision")
    if c:
        if c["tested"] and c["missed"] / c["tested"] > .005:
            out.append(finding("likely", "collision: rendered surface without collision", "terrain", "%d of %d sampled surface points had no collider within 15 cm" % (c["missed"], c["tested"]), measured=c["missed"] / c["tested"], threshold=.005))
        for p in c["propsWithoutCollider"]:
            out.append(finding("likely", "collision: prop without collider", p, "the hunter walks through it"))
        if c["worstMountGap"] > .1:
            out.append(finding("likely", "props: torch off the wall", "torches", "a wall torch's bracket is %.0f cm from the wall" % (c["worstMountGap"] * 100), measured=c["worstMountGap"], threshold=.1))
    m = d.get("mesh")
    if m:
        if m["openInside"]:
            out.append(finding("likely", "mesh: holes or seams", "terrain", "%d open edges (%.1f m) inside the generation box" % (m["openInside"], m["openLength"]), measured=m["openInside"], threshold=0))
        if m["overShared"]:
            out.append(finding("uncertain", "mesh: edges shared by 3+ triangles", "terrain", "%d edges (thin walls touching)" % m["overShared"], measured=m["overShared"]))
    L = d.get("lights")
    if L and L["maxPerChunk"] > L["limits"]["Medium"]:
        out.append(finding("likely", "lighting: more lights than the per-pixel limit", "terrain chunks", "a chunk is reached by %d lights; Medium lights %d per pixel, the rest fall back to rougher lighting and can pop" % (L["maxPerChunk"], L["limits"]["Medium"]),
                           measured=L["maxPerChunk"], threshold=L["limits"]["Medium"], fix="fewer or shorter-range lights there, or smaller chunks"))
    for name, (mean, p95) in d.get("shots", {}).items():
        if name.endswith("_Medium") and mean < .07:
            out.append(finding("likely", "lighting: too dark to read", name, "mean brightness %.2f (95th percentile %.2f)" % (mean, p95), measured=mean, threshold=.07, fix="another light, brighter ambient, or a lighter rock colour"))
        if name.endswith("_Medium") and mean > .8:
            out.append(finding("uncertain", "lighting: washed out", name, "mean brightness %.2f" % mean, measured=mean, threshold=.8))
    pm = (d.get("perf") or {}).get("Medium")
    if pm:
        for v in pm["views"]:
            if v["msP95"] > 16.7:
                out.append(finding("likely", "performance: below 60 fps", "%s (Medium)" % v["view"], "95 %% of frames within %.1f ms (median %.1f)" % (v["msP95"], v["msMedian"]), measured=v["msP95"], threshold=16.7))
    return out


def summary(d):
    i = d.get("info") or {}; w = d.get("walk") or {}; c = d.get("collision") or {}; m = d.get("mesh") or {}; L = d.get("lights") or {}
    lines = ["%s: %s triangles in %s chunks (voxel %s m), built in %.1f s; %s fire lights, %s puddles; GPU %s, %s" % (i.get("recipe"), format(i.get("triangles", 0), ","), i.get("chunks"), i.get("voxel"),
             i.get("buildSeconds", 0), i.get("fireLights"), i.get("puddles"), i.get("gpu"), i.get("screen"))]
    if w: lines.append("walk: %.1f of %.1f m in %.1f s, %d stuck; camera pulled in %.0f %%, view blocked %.0f %%, inside rock %.1f %% of frames; planted foot %s m (rests %s m), deepest %s m" %
                       (w["reached"], w["routeLength"], w["seconds"], w["nudges"], w["camera"]["pulledIn"] * 100, w["camera"]["lineBlocked"] * 100, w["camera"]["inside"] * 100, w["feet"].get("planted"), w["feet"].get("rest"), w["feet"].get("deepest")))
    for a in (d.get("nav") or {}).get("agents", []):
        lines.append("navigation %-8s r %.2f: to the arena %s (%.1f m), back %s, arena floor %s" % (a["agent"], a["radius"], a["toArena"].get("status"), a["toArena"].get("length") or 0, a["back"].get("status"),
                                                                                            "-" if a.get("arenaFloor") is None else "%.0f %%" % (a["arenaFloor"] * 100)))
    if c: lines.append("collision: %d of %d surface points without a collider; props without colliders %d; wall torches' gap up to %.3f m" % (c["missed"], c["tested"], len(c["propsWithoutCollider"]), c["worstMountGap"]))
    if m: lines.append("mesh: %d open edges inside the box (%.2f m), %d on the box's sides (expected), %d shared by 3+" % (m["openInside"], m["openLength"], m["openOnBox"], m["overShared"]))
    if L: lines.append("lights: %d; at most %d reach one chunk (limits Low %d, Medium %d, High %d)" % (L["lights"], L["maxPerChunk"], L["limits"]["Low"], L["limits"]["Medium"], L["limits"]["High"]))
    for q, p in (d.get("perf") or {}).items():
        if p: lines.append("perf %-6s " % q + "; ".join("%s %.1f ms median, %.1f p95%s" % (v["view"], v["msMedian"], v["msP95"], (", %s draws" % v["draws"]) if v.get("draws") else "") for v in p["views"]))
    lines.append("brightness of the shots (mean, 95th pct): " + "; ".join("%s %.2f/%.2f" % (k, a, b) for k, (a, b) in d.get("shots", {}).items()))
    return "\n".join(lines)


def _box_edge(found, sv, pics):
    """holes and open ledges on the generation box's own sides are where the made world stops, not gaps in it: those
    are told as one finding (a game level needs walls, rock or more terrain there)"""
    ox, oz = sv["origin"]; nx, nz = sv["size"]; st = sv["step"]
    x1, z1 = ox + nx * st, oz + nz * st
    keep, edge = [], []
    for f in found:
        m = re.search(r"at \((-?[\d.]+), (-?[\d.]+)\)", f.get("subject") or "")
        if f.get("check") in ("hole in the ground", "open ledge") and m:
            x, z = float(m.group(1)), float(m.group(2))
            if min(x - ox, x1 - x, z - oz, z1 - z) < 1.0: edge.append(f); continue
        keep.append(f)
    if edge:
        keep.append(finding("uncertain", "the made area ends", sv["area"], "%d holes or open ledges lie on the generation box's sides (x %.0f to %.0f, z %.0f to %.0f): the terrain stops there"
                            % (len(edge), ox, x1, oz, z1), measured=len(edge), evidence=pics, fix="in a game level: rock walls or more terrain at the edge, or a larger box"))
    return keep


def contact_sheet(frames, path, every=60, width=1280):
    from PIL import Image
    pick = frames[::every][:24]
    if not pick: return None
    tw = width // 4; th = tw * 9 // 16
    sheet = Image.new("RGB", (tw * 4, th * ((len(pick) + 3) // 4)), (0, 0, 0))
    for i, p in enumerate(pick):
        sheet.paste(Image.open(p).convert("RGB").resize((tw, th)), ((i % 4) * tw, (i // 4) * th))
    sheet.save(path, quality=88)
    return path


def report(ctx, prj, folder, run=None):
    from . import terrain as T
    d = analyse(folder)
    out = ctx["run_dir"](run, "worldforge_" + os.path.basename(folder.rstrip("/\\")), prj)
    ev = os.path.join(out, "evidence"); os.makedirs(ev, exist_ok=True)
    res = findings(d)
    sections = [("Summary", [], summary(d))]
    vid = os.path.join(folder, "walkthrough.mp4")
    if os.path.isfile(vid):
        shutil.copyfile(vid, os.path.join(ev, "walkthrough.mp4"))
        sections.append(("Video of the walk", [], "evidence/walkthrough.mp4 (%d frames at 30 per second, as the player saw them)" % len(d["framePaths"])))
    shots = sorted(glob.glob(os.path.join(folder, "shots", "*_Medium.jpg"))) + sorted(glob.glob(os.path.join(folder, "shots", "*_High.jpg")))
    sections.append(("Viewpoints (Medium, then High)", shots, None))
    sheet = contact_sheet(d["framePaths"], os.path.join(ev, "walk_sheet.jpg"))
    if sheet: sections.append(("The walk, every 2 s", [sheet], None))
    w = d.get("walk")
    csv = os.path.join(folder, "walk.csv")
    if w and os.path.isfile(csv):
        rows = [l.split(",") for l in open(csv, encoding="utf-8").read().splitlines()[1:] if l]
        t = [float(r[0]) for r in rows]
        cam = D.chart(os.path.join(ev, "camera.png"), [("pulled in (m)", list(zip(t, [float(r[6]) for r in rows])), (255, 170, 60)),
                                                        ("view blocked", list(zip(t, [float(r[7]) for r in rows])), (255, 90, 90))], (), "Camera along the walk")
        feet = D.chart(os.path.join(ev, "feet.png"), [("left ball (m)", list(zip(t, [max(0, min(.4, float(r[9]))) for r in rows])), (90, 170, 255)),
                                                       ("right ball (m)", list(zip(t, [max(0, min(.4, float(r[10]))) for r in rows])), (120, 220, 120))], (), "Balls of the feet above the ground")
        bright = D.chart(os.path.join(ev, "brightness.png"), [("mean", [(a, b) for a, b, _ in d["frames"]], (250, 230, 90))], (), "Frame brightness along the walk")
        sections.append(("Camera, feet and brightness along the walk", [cam, feet, bright], None))
    surveys = glob.glob(os.path.join(folder, "terrain_*.json"))
    for s in surveys:
        sv = T.load(s); r = T.analyse(sv)
        pic = T.picture(sv, r, os.path.join(ev, "map_%s.png" % sv["area"]))
        res += _box_edge(T.findings(sv, r, [pic]), sv, [pic])
        sections.append(("Map: what the hunter can reach (survey)", [pic], None))
    if d.get("nav"): sections.append(("Navigation (raw)", [], json.dumps(d["nav"], indent=1)))
    return ctx["report"].write(out, "WorldForge test: %s" % ((d.get("info") or {}).get("recipe") or os.path.basename(folder)), res, sections=sections, meta={"capture": folder})


# ------------------------------------------------------------------------------------------------------ commands
def register(sp, ctx):
    p = sp.add_parser("worldforge", help="WorldForge: procedural environments (recipe -> Unity), built and tested in a sandbox")
    sub = p.add_subparsers(dest="wf_cmd", required=True)
    s = sub.add_parser("sandbox"); s.add_argument("--sandbox"); s.add_argument("--run"); s.set_defaults(fn=lambda a: cmd(a, ctx))
    s = sub.add_parser("set"); s.add_argument("recipe"); s.add_argument("pairs", nargs="+"); s.add_argument("--run"); s.set_defaults(fn=lambda a: cmd(a, ctx))
    s = sub.add_parser("build"); s.add_argument("recipe"); s.add_argument("--sandbox"); s.add_argument("--run"); s.set_defaults(fn=lambda a: cmd(a, ctx))
    s = sub.add_parser("test"); s.add_argument("recipe"); s.add_argument("--sandbox"); s.add_argument("--build", action="store_true", help="build first"); s.add_argument("--run"); s.set_defaults(fn=lambda a: cmd(a, ctx))
    s = sub.add_parser("report"); s.add_argument("folder"); s.add_argument("--run"); s.set_defaults(fn=lambda a: cmd(a, ctx))


def cmd(a, ctx):
    prj = ctx["project"](a)
    c = a.wf_cmd
    if c == "sandbox":
        sb = _sandbox(a); log = make_sandbox(prj, sb)
        print("WorldForge sandbox:", sb); print("\n".join(log) or "up to date"); return None
    if c == "set":
        r = set_values(a.recipe, a.pairs); print("WorldForge: %s updated (%s)" % (a.recipe, ", ".join(a.pairs))); return None
    if c == "build":
        sb = _sandbox(a); make_sandbox(prj, sb)
        code, lines, exe = build(prj, sb, a.recipe)
        print("\n".join(lines)); print("WorldForge build:", "ok" if code == 0 and os.path.isfile(exe) else "FAILED", exe); return None
    if c == "test":
        sb = _sandbox(a)
        if a.build:
            make_sandbox(prj, sb); code, lines, exe = build(prj, sb, a.recipe)
            if code != 0: raise RuntimeError("build failed:\n" + "\n".join(lines))
        name = json.load(open(a.recipe, encoding="utf-8"))["name"]
        out = os.path.join(sb, "Captures", name)
        secs = run_test(sb, a.recipe, out)
        print("WorldForge test ran %.0f s; done: %s" % (secs, os.path.isfile(os.path.join(out, "done.txt"))))
        v = make_video(out); print("video:", v)
        return report(ctx, prj, out, a.run)
    if c == "report":
        return report(ctx, prj, os.path.abspath(a.folder), a.run)
