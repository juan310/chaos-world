"""The 3D mesh system (Studio 0.16): what the 2D mesh system (sprite/mesh.py) does for a drawing tied to a 2D rig, for a
rigged 3D model. Every clip is played and the skinned body measured frame by frame against its rest shape, in Blender
(mesh3d_blender.py):

- **stretch**: skin that tears or pinches at a joint (edges grown past 1.7x or shrunk under 0.45x their rest length),
  by the bone that carries it;
- **clipping**: the pieces riding on the skeleton (weapons, shields, props) passing through the body;
- **ground**: the body sinking through the floor (outside clips that lie, sit or ride on purpose).

Each finding keeps to the five kinds (findings.py): sinking under the ground is a measured fact (confirmed); torn skin
and a weapon inside the body usually show (likely); small amounts are the art owner's call (uncertain). The worst frame
of each clip is the evidence picture: the offending skin red, the intruding points yellow. Thresholds were set on
calm clips of slim realistic characters (idle and walk stretch 0.4-0.8 % of the vertices with their cloth).
"""
import glob
import json
import os
import subprocess

from .findings import finding

HERE = os.path.dirname(os.path.abspath(__file__))
STRETCH_LIKELY, STRETCH_UNCERTAIN = 2.5, 1.5        # % of the body's vertices
CLIP_POINTS, CLIP_DEPTH_CM = 10, 3.0
GROUND_CM = 5.0


def blender_path():
    """Blender: CLAUDE_EYES_BLENDER, else Studio's settings ("blender"), else the newest under Program Files."""
    p = os.environ.get("CLAUDE_EYES_BLENDER")
    if p and os.path.isfile(p):
        return p
    try:
        home = os.environ.get("CLAUDE_EYES_STUDIO_HOME") or os.path.join(os.environ.get("LOCALAPPDATA", ""), "ClaudeEyesStudio")
        s = json.load(open(os.path.join(home, "settings.json"), encoding="utf-8"))
        if s.get("blender") and os.path.isfile(s["blender"]):
            return s["blender"]
    except (OSError, ValueError):
        pass
    found = sorted(glob.glob(r"C:\Program Files\Blender Foundation\Blender *\blender.exe"),
                   key=lambda x: [int(n) for n in os.path.basename(os.path.dirname(x)).split()[-1].split(".") if n.isdigit()])
    if found:
        return found[-1]
    raise FileNotFoundError("Blender was not found (set CLAUDE_EYES_BLENDER or Studio's \"blender\" setting)")


def run(model, out_dir, clips=None, config=None, timeout=3600):
    """measures every clip of the model (or those named). -> the measurements (mesh3d.json)"""
    os.makedirs(out_dir, exist_ok=True)
    cmd = [blender_path(), "-b", "--python", os.path.join(HERE, "mesh3d_blender.py"), "--", os.path.abspath(model), os.path.abspath(out_dir)]
    if clips:
        cmd += ["--clips", ",".join(clips)]
    if config:
        cfg_path = os.path.join(out_dir, "mesh3d_config.json")
        with open(cfg_path, "w", encoding="utf-8") as fh:
            json.dump(config, fh, indent=1)
        cmd += ["--config", cfg_path]
    log = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
    path = os.path.join(out_dir, "mesh3d.json")
    if not os.path.isfile(path):
        tail = "\n".join((log.stdout + log.stderr).splitlines()[-12:])
        raise RuntimeError("the 3D mesh check did not finish:\n" + tail)
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def findings(rep):
    out = []
    subject = os.path.splitext(os.path.basename(rep.get("model", "model")))[0]
    for clip, c in rep["clips"].items():
        ev = [c["picture"]] if c.get("picture") else None
        where = ", ".join("%s (%d)" % kv for kv in c.get("where", {}).items())
        frame = [c["worstFrame"]] if c.get("worstFrame") is not None else None
        if c["ground"] >= GROUND_CM:
            out.append(finding("confirmed", "sinks under the ground", "%s %s" % (subject, clip),
                               "the body goes %.0f cm under the soles' rest height" % c["ground"], frames=frame,
                               measured=c["ground"], threshold=GROUND_CM, evidence=ev,
                               fix="lift the pose where feet, toes or knees would pass the floor (a ground clamp when baking)"))
        if c["stretch"] >= STRETCH_LIKELY:
            out.append(finding("likely", "skin tears at a joint", "%s %s" % (subject, clip),
                               "%.1f %% of the body's vertices stretched past %.1fx or pinched under %.2fx; most on %s"
                               % (c["stretch"], rep["thresholds"]["stretch_max"], rep["thresholds"]["stretch_min"], where or "-"),
                               frames=frame, measured=c["stretch"], threshold=STRETCH_LIKELY, evidence=ev,
                               fix="soften the skin weights there, or limit how far the clip swings that joint"))
        elif c["stretch"] >= STRETCH_UNCERTAIN:
            out.append(finding("uncertain", "skin stretches at a joint", "%s %s" % (subject, clip),
                               "%.1f %% of the body's vertices; most on %s" % (c["stretch"], where or "-"),
                               frames=frame, measured=c["stretch"], threshold=STRETCH_UNCERTAIN, evidence=ev))
        if c["clipping"] >= CLIP_POINTS and c.get("clipDepthCm", 0) >= CLIP_DEPTH_CM:
            out.append(finding("likely", "a held piece passes through the body", "%s %s" % (subject, clip),
                               "%d points inside, up to %.0f cm deep%s" % (c["clipping"], c["clipDepthCm"], (": " + ", ".join("%s %.0f cm" % kv for kv in c["meets"].items())) if c.get("meets") else ""), frames=frame,
                               measured=c["clipping"], threshold=CLIP_POINTS, evidence=ev,
                               fix="move the piece's grip or the arm in that pose"))
        elif c["clipping"] > 0:
            out.append(finding("uncertain", "a held piece touches into the body", "%s %s" % (subject, clip),
                               "%d points inside, up to %.0f cm deep" % (c["clipping"], c.get("clipDepthCm", 0)), frames=frame,
                               measured=c["clipping"], threshold=CLIP_POINTS, evidence=ev))
    return out


def table(rep):
    lines = ["%-34s %8s %8s %8s %8s %8s  %s" % ("clip", "stretch%", "folds%", "points", "deep cm", "under cm", "where")]
    for clip, c in rep["clips"].items():
        lines.append("%-34s %8.2f %8.2f %8d %8.1f %8.1f  %s" % (clip[:34], c["stretch"], c.get("folds", 0), c["clipping"], c.get("clipDepthCm", 0), c["ground"],
                                                         ", ".join(c.get("where", {}))))
    return "\n".join(lines)
