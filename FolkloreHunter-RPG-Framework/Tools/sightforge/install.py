"""Installs this folder's SightForge / Claude Eyes Studio modules (0.16 -> 0.17 -> 0.18 -> 0.19), the way Studio's own upgrades go: the
whole app is copied to backups/app_<old>_before_<new>_<date> first, then
  eyeslib/*.py, eyeslib/motionbricks/*.py   the engines (copied from this folder)
  eyes_inspect.py                           one hook that adds every development tool's command (each module's
                                            register(sp, ctx)), so later tools need no further edits
  studio.py                                 VERSION 0.19 (0.18: WorldForge; 0.19: its page in Studio, the /api/wf/ and /wf/
                                            routes and their self-test checks)
  studio_page.html                          the WorldForge page (studio/worldforge_page.js) and its place under Tools
Running it again only refreshes the copied modules and the list of tools.

  python Tools/sightforge/install.py

Then:  python eyes_inspect.py --project-root <game> <command> ...   (commands: see DEVTOOLS below and README.md)
"""
import datetime
import os
import re
import shutil

HERE = os.path.dirname(os.path.abspath(__file__))
HOME = os.environ.get("CLAUDE_EYES_STUDIO_HOME") or os.path.join(os.environ.get("LOCALAPPDATA", ""), "ClaudeEyesStudio")
APP = os.path.join(HOME, "app")
NEW = "0.19"

# the development tools, each an eyeslib module with register(sp, ctx) and cmd(a, ctx)
DEVTOOLS = ("originality", "weaponfit", "smear", "hits", "problem", "perf", "springs", "terrain", "footslide", "motionbricks", "worldforge", "hands")

HOOK = '''

DEVTOOLS = ()


def _devtools(sp):
    """the development tools' commands (installed by the game's Tools/sightforge/install.py): each eyeslib module in
    DEVTOOLS adds its own sub-command through register(sp, ctx)"""
    import importlib
    ctx = {"project": lambda a: getattr(a, "project", None) or P, "run_dir": run_dir, "report": report, "safe": safe,
           "finding": finding, "app": os.path.dirname(os.path.abspath(__file__))}
    for m in DEVTOOLS:
        try:
            importlib.import_module("eyeslib." + m).register(sp, ctx)
        except Exception as e:  # one broken tool must not take the others down
            print("Claude Eyes: the %s tool did not load (%s)" % (m, e), file=sys.stderr)

'''


# ---- the WorldForge page in Studio (0.19): each patch is found by a marker, so running again changes nothing twice
GET_ROUTES = '''            if u.path.startswith("/api/wf/"):
                from eyeslib import worldforge_studio as WFS      # WorldForge (0.19)
                return self._send(200, WFS.get(prj, u.path[len("/api/wf/"):], q))
            m = re.match(r"^/wf/([A-Za-z0-9_-]+)/(.+)$", u.path)
            if m:
                from eyeslib import worldforge_studio as WFS
                p = WFS.file_path(m.group(1), unquote(m.group(2)))
                if not p:
                    return self._send(404, {"error": "not found"})
                if not load_settings()["showArt"]:
                    return self._send(403, {"error": "showing images is switched off in Privacy & access"})
                return self._file(p)
'''
GET_AT = '''            if u.path.startswith("/api/forge/"):
                from eyeslib import forge as FG          # 3D Model'''
POST_LIMIT = '''            if path == "/api/wf/save":
                limit = 4 * 1024 * 1024            # a WorldForge recipe
'''
POST_LIMIT_AT = '''            n = int(self.headers.get("Content-Length") or 0)
            if n > limit:'''
POST_ROUTE = '''            if path.startswith("/api/wf/"):
                if not (STATE["allow_run"] and load_settings()["allowRun"]):
                    return self._send(403, {"error": "making changes is switched off (Privacy & access)"})
                from eyeslib import worldforge_studio as WFS      # WorldForge (0.19)
                return self._send(200, WFS.post(self._prj(body), path[len("/api/wf/"):], body))
'''
POST_ROUTE_AT = '''            if path.startswith("/api/forge/"):
                if not (STATE["allow_run"] and load_settings()["allowRun"]):'''
SELFTEST = '''    check("WorldForge files: path escape refused", get("/wf/cave_sample/..%2f..%2fsettings.json")[0] == 404)
    check("WorldForge files: only test pictures and the video", get("/wf/cave_sample/player.log")[0] == 404)
    check("WorldForge recipe: bad name refused", get("/api/wf/recipe?name=..%5c..%5csettings&project=" + pid)[0] == 400)
    check("WorldForge save refused without the key", get("/api/wf/save", {}, "POST", b"{}")[0] == 403)
'''
SELFTEST_AT = '''    check("Forge start refused without the key",'''
NAV_OLD = '''["prop3d", "3D Prop & Building", "\u25a3"], '''
NAV_NEW = '''["prop3d", "3D Prop & Building", "\u25a3"], ["worldforge", "WorldForge (environments)", "\u26f0"], '''
PAGE_AT = "// ---------------------------------------------------------------- Home\nR.home"


def _insert(src, at, block, marker, what):
    if marker in src: return src
    if at not in src: raise RuntimeError("SIGHTFORGE_INSTALL could not place " + what + ": Studio's code is not what this installer knows")
    print("SIGHTFORGE_INSTALL " + what)
    return src.replace(at, block + at, 1)


def studio_page(studio, page):
    """the WorldForge page: routes in studio.py, the page and its menu entry in studio_page.html"""
    s = open(studio, encoding="utf-8").read()
    s = _insert(s, GET_AT, GET_ROUTES, "WFS.get(", "studio.py: WorldForge GET routes")
    s = _insert(s, POST_LIMIT_AT, POST_LIMIT, 'path == "/api/wf/save"', "studio.py: WorldForge recipe size limit")
    s = _insert(s, POST_ROUTE_AT, POST_ROUTE, "WFS.post(", "studio.py: WorldForge POST routes")
    s = _insert(s, SELFTEST_AT, SELFTEST, "WorldForge files: path escape refused", "studio.py: WorldForge self-test checks")
    open(studio, "w", encoding="utf-8").write(s)
    h = open(page, encoding="utf-8").read()
    js = open(os.path.join(HERE, "studio", "worldforge_page.js"), encoding="utf-8").read()
    block = "// WORLDFORGE_PAGE_BEGIN (Tools/sightforge/studio/worldforge_page.js; replaced by each install)\n" + js.rstrip() + "\n// WORLDFORGE_PAGE_END\n\n"
    if "// WORLDFORGE_PAGE_BEGIN" in h:
        h = re.sub(r"// WORLDFORGE_PAGE_BEGIN.*?// WORLDFORGE_PAGE_END\n\n", lambda _: block, h, count=1, flags=re.S)
        print("SIGHTFORGE_INSTALL studio_page.html: WorldForge page refreshed")
    else:
        h = _insert(h, PAGE_AT, block, "// WORLDFORGE_PAGE_BEGIN", "studio_page.html: WorldForge page")
    if '["worldforge", ' not in h:
        if NAV_OLD not in h: raise RuntimeError("SIGHTFORGE_INSTALL could not place the WorldForge menu entry")
        h = h.replace(NAV_OLD, NAV_NEW, 1); print("SIGHTFORGE_INSTALL studio_page.html: WorldForge under Tools")
    open(page, "w", encoding="utf-8").write(h)


def version(studio):
    return re.search(r'^VERSION = "([^"]+)"', open(studio, encoding="utf-8").read(), re.M).group(1)


def main():
    studio = os.path.join(APP, "studio.py"); inspect = os.path.join(APP, "eyes_inspect.py")
    ver = version(studio)
    if ver != NEW:
        dst = os.path.join(HOME, "backups", "app_%s_before_%s_%s" % (ver.replace(".", "_"), NEW.replace(".", "_"), datetime.date.today().strftime("%Y%m%d")))
        if not os.path.exists(dst):
            shutil.copytree(APP, dst, ignore=shutil.ignore_patterns("__pycache__"))
            print("SIGHTFORGE_INSTALL backup", dst)
    src_lib = os.path.join(HERE, "eyeslib")
    for root, _, files in os.walk(src_lib):
        if "__pycache__" in root: continue
        rel = os.path.relpath(root, src_lib)
        dst_dir = os.path.join(APP, "eyeslib", rel) if rel != "." else os.path.join(APP, "eyeslib")
        os.makedirs(dst_dir, exist_ok=True)
        for f in files:
            if f.endswith((".py", ".json")):
                shutil.copyfile(os.path.join(root, f), os.path.join(dst_dir, f))
                print("SIGHTFORGE_INSTALL copied eyeslib/" + (f if rel == "." else rel.replace("\\", "/") + "/" + f))
    src = open(inspect, encoding="utf-8").read()
    if "def _devtools(" not in src:
        src = src.replace("\ndef print_summary(", HOOK + "\ndef print_summary(", 1)
        src = src.replace("    a = ap.parse_args(argv)\n", "    _devtools(sp)\n    a = ap.parse_args(argv)\n", 1)
        print("SIGHTFORGE_INSTALL eyes_inspect: development tools hook")
    src = re.sub(r"^DEVTOOLS = \(.*?\)$", "DEVTOOLS = %r" % (DEVTOOLS,), src, count=1, flags=re.M)
    open(inspect, "w", encoding="utf-8").write(src)
    print("SIGHTFORGE_INSTALL tools:", ", ".join(DEVTOOLS))
    studio_page(studio, os.path.join(APP, "studio_page.html"))
    if ver != NEW:
        s = open(studio, encoding="utf-8").read()
        open(studio, "w", encoding="utf-8").write(s.replace('VERSION = "%s"' % ver, 'VERSION = "%s"' % NEW, 1))
        print("SIGHTFORGE_INSTALL studio VERSION", NEW)
    print("SIGHTFORGE_INSTALL done (Studio %s)" % NEW)


if __name__ == "__main__":
    main()
