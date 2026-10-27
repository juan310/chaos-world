"""Installs the 3D mesh system into SightForge / Claude Eyes Studio (0.15 -> 0.16), the way Studio's own upgrades go: the
whole app is copied to backups/app_0_15_before_0_16_<date> first, then
  eyeslib/mesh3d.py, eyeslib/mesh3d_blender.py      the engine and its Blender side (copied from this folder)
  eyes_inspect.py                                    a "mesh3d" command beside "animation", "track", "gameplay"
  studio.py                                          VERSION 0.16
Running it again changes nothing that is already in place.

  python Tools/sightforge/install_mesh3d.py

Then:  python eyes_inspect.py --project-root <game> mesh3d <model.fbx|.glb> [--clips a,b] [--config cfg.json] [--run name]
"""
import datetime
import os
import re
import shutil

HERE = os.path.dirname(os.path.abspath(__file__))
HOME = os.environ.get("CLAUDE_EYES_STUDIO_HOME") or os.path.join(os.environ["LOCALAPPDATA"], "ClaudeEyesStudio")
APP = os.path.join(HOME, "app")

CMD = '''

def cmd_mesh3d(a):
    """the 3D mesh system: every clip of a rigged 3D model played and its skin measured against its rest shape (torn or
    pinched skin at the joints, held pieces passing through the body, the body sinking through the floor)"""
    from eyeslib import mesh3d
    prj = getattr(a, "project", None) or P
    model = os.path.abspath(a.model)
    stem = os.path.splitext(os.path.basename(model))[0]
    out = run_dir(a.run, "mesh3d_" + stem, prj)
    cfg = None
    if a.config:
        with open(a.config, encoding="utf-8") as fh:
            cfg = json.load(fh)
    rep = mesh3d.run(model, os.path.join(out, "evidence"), clips=[c for c in (a.clips or "").split(",") if c] or None, config=cfg)
    res = mesh3d.findings(rep)
    pics = [c["picture"] for c in rep["clips"].values() if c.get("picture")]
    sections = [("Clips: stretched skin (% of vertices), points of held pieces inside the body, deepest, under the floor", [], mesh3d.table(rep)),
                ("Worst frame of each clip (red: torn or pinched skin; yellow: a held piece inside the body)", pics[:40], None),
                ("Measurements", [], json.dumps(rep, indent=1))]
    page, data = report.write(out, "3D mesh: %s (%d clips)" % (stem, len(rep["clips"])), res, sections=sections,
                              meta={"model": model, "body": rep.get("body"), "pieces": rep.get("pieces"), "thresholds": rep.get("thresholds")})
    return page, data

'''

PARSER = ('    p = sp.add_parser("mesh3d"); p.add_argument("model"); p.add_argument("--clips"); p.add_argument("--config"); p.add_argument("--run")\n'
          '    p.set_defaults(fn=cmd_mesh3d)\n')


def main():
    studio = os.path.join(APP, "studio.py"); inspect = os.path.join(APP, "eyes_inspect.py")
    ver = re.search(r'^VERSION = "([^"]+)"', open(studio, encoding="utf-8").read(), re.M).group(1)
    if ver == "0.15":
        dst = os.path.join(HOME, "backups", "app_0_15_before_0_16_" + datetime.date.today().strftime("%Y%m%d"))
        if not os.path.exists(dst):
            shutil.copytree(APP, dst, ignore=shutil.ignore_patterns("__pycache__"))
            print("MESH3D_INSTALL backup", dst)
    for f in ("mesh3d.py", "mesh3d_blender.py"):
        shutil.copyfile(os.path.join(HERE, "eyeslib", f), os.path.join(APP, "eyeslib", f))
        print("MESH3D_INSTALL copied eyeslib/" + f)
    src = open(inspect, encoding="utf-8").read()
    if "def cmd_mesh3d(" not in src:
        src = src.replace("\ndef print_summary(", CMD + "\ndef print_summary(", 1)
        print("MESH3D_INSTALL eyes_inspect: cmd_mesh3d")
    if 'add_parser("mesh3d")' not in src:
        src = src.replace("    a = ap.parse_args(argv)\n", PARSER + "    a = ap.parse_args(argv)\n", 1)
        print("MESH3D_INSTALL eyes_inspect: mesh3d command")
    open(inspect, "w", encoding="utf-8").write(src)
    s = open(studio, encoding="utf-8").read()
    if ver == "0.15":
        open(studio, "w", encoding="utf-8").write(s.replace('VERSION = "0.15"', 'VERSION = "0.16"', 1))
        print("MESH3D_INSTALL studio VERSION 0.16")
    print("MESH3D_INSTALL done (Studio %s)" % ("0.16" if ver == "0.15" else ver))


if __name__ == "__main__":
    main()
