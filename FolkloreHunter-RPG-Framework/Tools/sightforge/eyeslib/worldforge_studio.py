"""WorldForge's page in Studio (0.19): the recipes of the open project (its WorldForge/Recipes folder) edited as values or
as JSON, built and tested in the isolated sandbox (the same `worldforge test` command line, run in the background), the
playable preview started on a recipe, and the latest test's summary, findings, viewpoints, walk video and map.

Studio's routes (added to studio.py by Tools/sightforge/install.py):
  GET  /api/wf/state                    recipes, sandbox, preview build, the running job, finished tests
  GET  /api/wf/recipe?name=             one recipe (JSON)
  GET  /api/wf/result?name=             the latest test of a recipe: summary, findings, pictures, video
  GET  /wf/<recipe>/<file>              a test's pictures and video (shots/*.jpg, walkthrough.mp4) from the sandbox
  POST /api/wf/save {name, recipe}      write a recipe (the old one kept in Recipes/.history first)
  POST /api/wf/copy {name, to}          a new recipe from an existing one
  POST /api/wf/run {name, build}        build (when asked, or no preview yet) and test, in the background
  POST /api/wf/play {name}              the playable preview on that recipe (edit, save, press F5 in it to rebuild)
  POST /api/wf/cancel                   stop the running job and any preview it started
Only the project's WorldForge folder, the sandbox and the project's reports are touched."""
import datetime
import glob
import json
import os
import re
import subprocess
import sys
import threading
import time

from . import worldforge as WF

NAME_RE = re.compile(r"^[A-Za-z0-9_-]{1,64}$")
LOCK = threading.Lock()
JOB = {}                        # the one WorldForge job at a time: {name, step, started, t0, status, log, proc}
APP = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def _recipes_dir(prj):
    return os.path.join(prj.root, "WorldForge", "Recipes")


def _name(n):
    n = str(n or "")
    if not NAME_RE.match(n):
        raise ValueError("a recipe name is letters, digits, _ and - (up to 64)")
    return n


def _recipe_path(prj, name):
    return os.path.join(_recipes_dir(prj), _name(name) + ".json")


def _capture(name):
    return os.path.join(WF.SANDBOX, "Captures", _name(name))


def _exe():
    return os.path.join(WF.SANDBOX, "Build", "WorldForgePreview.exe")


def _when(t):
    return datetime.datetime.fromtimestamp(t).strftime("%Y-%m-%d %H:%M") if t else None


def _job_view():
    with LOCK:
        if not JOB: return None
        j = {k: v for k, v in JOB.items() if k != "proc"}
    j["seconds"] = round((j.get("t1") or time.time()) - j["t0"])
    log = j.get("logFile")
    if log and os.path.isfile(log):
        lines = open(log, encoding="utf-8", errors="replace").read().splitlines()
        j["log"] = lines[-14:]
    # the test player's own progress (its log.txt in the capture folder)
    tl = os.path.join(_capture(j["name"]), "log.txt")
    if os.path.isfile(tl) and os.path.getmtime(tl) >= j["t0"] - 5:
        j["progress"] = open(tl, encoding="utf-8", errors="replace").read().splitlines()[-6:]
    return j


def state(prj):
    rd = _recipes_dir(prj)
    if not os.path.isfile(os.path.join(prj.root, "WorldForge", "package.json")):
        return {"available": False, "why": "this project has no WorldForge package (a WorldForge folder with package.json)"}
    recipes = []
    for p in sorted(glob.glob(os.path.join(rd, "*.json"))):
        n = os.path.basename(p)[:-5]
        if not NAME_RE.match(n): continue
        cap = _capture(n)
        rep = os.path.join(prj.inspections, "worldforge_" + n, "report.json")
        tested = os.path.getmtime(os.path.join(cap, "done.txt")) if os.path.isfile(os.path.join(cap, "done.txt")) else None
        totals = None
        if os.path.isfile(rep):
            try: totals = json.load(open(rep, encoding="utf-8")).get("summary")
            except ValueError: pass
        recipes.append({"name": n, "saved": _when(os.path.getmtime(p)), "tested": _when(tested), "totals": totals,
                        "report": ("Inspections/worldforge_%s/index.html" % n) if os.path.isfile(rep) else None})
    exe = _exe()
    return {"available": True, "recipes": recipes, "sandbox": WF.SANDBOX, "sandboxReady": os.path.isdir(os.path.join(WF.SANDBOX, "Assets")),
            "preview": _when(os.path.getmtime(exe)) if os.path.isfile(exe) else None, "unity": os.path.isfile(WF.UNITY), "job": _job_view()}


def result(prj, name):
    _name(name)
    rep = os.path.join(prj.inspections, "worldforge_" + name, "report.json")
    cap = _capture(name)
    out = {"name": name, "report": None, "summary": "", "findings": [], "shots": [], "video": None, "map": None, "sheet": None, "charts": []}
    if os.path.isfile(rep):
        d = json.load(open(rep, encoding="utf-8"))
        out["report"] = "Inspections/worldforge_%s/index.html" % name
        out["totals"] = d.get("summary")
        out["created"] = d.get("created")
        out["findings"] = [{k: f.get(k) for k in ("kind", "check", "subject", "detail", "measured", "threshold", "suggestedFix")} for f in d.get("findings", [])][:120]
        for s in d.get("sections") or []:
            if s.get("heading") == "Summary": out["summary"] = (s.get("text") or "")[:8000]
        ev = os.path.join(prj.inspections, "worldforge_" + name, "evidence")
        rel = lambda f: "Inspections/worldforge_%s/evidence/%s" % (name, f)
        if os.path.isfile(os.path.join(ev, "map_%s.png" % name)): out["map"] = rel("map_%s.png" % name)
        if os.path.isfile(os.path.join(ev, "walk_sheet.jpg")): out["sheet"] = rel("walk_sheet.jpg")
        out["charts"] = [rel(f) for f in ("feet.png", "camera.png", "brightness.png") if os.path.isfile(os.path.join(ev, f))]
    out["shots"] = [os.path.basename(p) for p in sorted(glob.glob(os.path.join(cap, "shots", "*.jpg")))][:40]
    if os.path.isfile(os.path.join(cap, "walkthrough.mp4")): out["video"] = "walkthrough.mp4"
    return out


def get(prj, op, q):
    try:
        return _get(prj, op, q)
    except OSError as e:            # a file that went missing or is locked: a plain error for the page, not a dropped connection
        raise ValueError(str(e))


def _get(prj, op, q):
    if op == "state": return state(prj)
    if op == "recipe":
        p = _recipe_path(prj, q.get("name"))
        if not os.path.isfile(p): raise ValueError("no such recipe")
        return {"name": q["name"], "recipe": json.load(open(p, encoding="utf-8"))}
    if op == "result": return result(prj, q.get("name"))
    raise ValueError("unknown WorldForge request")


def file_path(name, rel):
    """a test's pictures and video only: shots/<file>.jpg or walkthrough.mp4 of a capture in the sandbox"""
    if not NAME_RE.match(name or ""): return None
    if not (re.match(r"^shots/[A-Za-z0-9_.-]+\.jpg$", rel) or rel == "walkthrough.mp4"): return None
    base = os.path.realpath(_capture(name))
    p = os.path.realpath(os.path.join(base, rel))
    return p if p.startswith(base + os.sep) and os.path.isfile(p) else None


def _check_recipe(r, name):
    if not isinstance(r, dict): raise ValueError("a recipe is a JSON object")
    for k in ("features",):
        if not isinstance(r.get(k), list): raise ValueError("a recipe needs a features list")
    for k in ("min", "max"):
        v = r.get(k)
        if not (isinstance(v, dict) and all(isinstance(v.get(a), (int, float)) for a in "xyz")): raise ValueError("a recipe needs %s with x, y, z" % k)
    vox = r.get("voxel", .45)
    if not isinstance(vox, (int, float)) or not .15 <= vox <= 2: raise ValueError("voxel must be between 0.15 and 2 m")
    cells = 1
    for a in "xyz": cells *= max(1, (r["max"][a] - r["min"][a]) / vox)
    if cells > 40e6: raise ValueError("the generation box holds %.0f million cells at this voxel size (40 million at most): a smaller box or a bigger voxel" % (cells / 1e6))
    r["name"] = name
    return r


def save(prj, name, recipe):
    p = _recipe_path(prj, name)
    r = _check_recipe(recipe, name)
    if os.path.isfile(p):
        hist = os.path.join(_recipes_dir(prj), ".history"); os.makedirs(hist, exist_ok=True)
        with open(p, "rb") as f, open(os.path.join(hist, "%s_%s.json" % (name, time.strftime("%Y%m%d_%H%M%S"))), "wb") as g:
            g.write(f.read())
    tmp = p + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(r, f, indent=1)
    os.replace(tmp, p)
    return {"ok": True, "name": name}


def _busy():
    with LOCK:
        return bool(JOB) and JOB.get("status") == "running"


def run(prj, name, build=False):
    p = _recipe_path(prj, name)
    if not os.path.isfile(p): raise ValueError("no such recipe")
    if _busy(): raise ValueError("a WorldForge job is already running (%s)" % JOB.get("name"))
    if not os.path.isfile(WF.UNITY) and (build or not os.path.isfile(_exe())):
        raise ValueError("Unity 6000.6.7f1 is not installed where WorldForge looks (%s)" % WF.UNITY)
    # the preview is built again when asked, when there is none, or when the package changed after it was built
    exe = _exe()
    pkg = os.path.join(prj.root, "WorldForge")
    newest = max((os.path.getmtime(f) for f in glob.glob(os.path.join(pkg, "**", "*.cs"), recursive=True) + glob.glob(os.path.join(pkg, "**", "*.shader"), recursive=True)), default=0)
    build = bool(build) or not os.path.isfile(exe) or newest > os.path.getmtime(exe)
    logs = os.path.join(WF.SANDBOX, "Logs"); os.makedirs(logs, exist_ok=True)
    log = os.path.join(logs, "studio-worldforge.log")
    cmd = [sys.executable, os.path.join(APP, "eyes_inspect.py"), "--project-root", prj.root, "worldforge", "test", p] + (["--build"] if build else [])
    with LOCK:
        JOB.clear(); JOB.update({"name": name, "step": "building the preview, then testing" if build else "testing", "started": time.strftime("%H:%M:%S"), "t0": time.time(),
                                 "status": "running", "logFile": log, "build": build})

    def work():
        env = dict(os.environ, PYTHONIOENCODING="utf-8")
        with open(log, "w", encoding="utf-8") as lf:
            lf.write("%s %s\n" % (time.strftime("%H:%M:%S"), " ".join('"%s"' % c if " " in c else c for c in cmd))); lf.flush()
            proc = subprocess.Popen(cmd, stdout=lf, stderr=subprocess.STDOUT, cwd=prj.root, env=env, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
            with LOCK: JOB["proc"] = proc
            try:
                code = proc.wait(timeout=5400)
            except subprocess.TimeoutExpired:
                proc.kill(); code = -9
        with LOCK:
            if JOB.get("status") == "running":
                JOB["status"] = "done" if code == 0 else "failed (exit %d)" % code
            JOB["t1"] = time.time(); JOB.pop("proc", None)
    threading.Thread(target=work, daemon=True).start()
    return _job_view()


def cancel():
    with LOCK:
        proc = JOB.get("proc")
        if JOB.get("status") == "running": JOB["status"] = "cancelled"
    if proc:
        if os.name == "nt": subprocess.run(["taskkill", "/PID", str(proc.pid), "/T", "/F"], capture_output=True)
        else: proc.kill()
    if os.name == "nt": subprocess.run(["taskkill", "/IM", "WorldForgePreview.exe", "/F"], capture_output=True)
    return {"ok": True}


def play(prj, name):
    p = _recipe_path(prj, name)
    if not os.path.isfile(p): raise ValueError("no such recipe")
    if _busy(): raise ValueError("wait for the running WorldForge job to finish (it uses the preview)")
    exe = _exe()
    if not os.path.isfile(exe): raise ValueError("there is no preview yet: run Build & test once")
    subprocess.Popen([exe, "--wf-recipe", p, "-screen-width", "1600", "-screen-height", "900", "-screen-fullscreen", "0"], cwd=os.path.dirname(exe))
    return {"ok": True}


def post(prj, op, body):
    try:
        return _post(prj, op, body)
    except OSError as e:
        raise ValueError(str(e))


def _post(prj, op, body):
    if op == "save": return save(prj, _name(body.get("name")), body.get("recipe"))
    if op == "copy":
        src, to = _recipe_path(prj, body.get("name")), _name(body.get("to"))
        if not os.path.isfile(src): raise ValueError("no such recipe")
        if os.path.isfile(_recipe_path(prj, to)): raise ValueError("a recipe called %s already exists" % to)
        return save(prj, to, json.load(open(src, encoding="utf-8")))
    if op == "run": return run(prj, _name(body.get("name")), bool(body.get("build")))
    if op == "play": return play(prj, _name(body.get("name")))
    if op == "cancel": return cancel()
    raise ValueError("unknown WorldForge request")
