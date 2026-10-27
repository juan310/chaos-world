"""Installs this folder's SightForge / Claude Eyes Studio modules (0.16 -> 0.17), the way Studio's own upgrades go: the
whole app is copied to backups/app_<old>_before_<new>_<date> first, then
  eyeslib/*.py, eyeslib/motionbricks/*.py   the engines (copied from this folder)
  eyes_inspect.py                           one hook that adds every development tool's command (each module's
                                            register(sp, ctx)), so later tools need no further edits
  studio.py                                 VERSION 0.17
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
NEW = "0.17"

# the development tools, each an eyeslib module with register(sp, ctx) and cmd(a, ctx)
DEVTOOLS = ("originality", "weaponfit", "smear", "hits", "problem", "perf", "springs", "terrain", "footslide", "motionbricks")

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
    if ver != NEW:
        s = open(studio, encoding="utf-8").read()
        open(studio, "w", encoding="utf-8").write(s.replace('VERSION = "%s"' % ver, 'VERSION = "%s"' % NEW, 1))
        print("SIGHTFORGE_INSTALL studio VERSION", NEW)
    print("SIGHTFORGE_INSTALL done (Studio %s)" % NEW)


if __name__ == "__main__":
    main()
