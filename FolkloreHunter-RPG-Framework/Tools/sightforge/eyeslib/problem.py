"""Problem capture (Studio 0.17): one moment a player flagged with "something looks off" (F9 in the Folklore Hunter game;
Captures/Problems/problem_<time>/ with screen.png, state.json, hits.json, perf.json) turned into one report.

  the picture   the screenshot with the engine's own view of the moment drawn over it: the hunter's and the near
                monsters' bones and the monsters' hurtboxes, projected through the game's camera (where a drawn body and
                its skeleton or hit volumes part company, it shows); the body pose detector run on the hunter, measured
                against the engine's joints
  the state     the area, the hunter's clip and its time, the move, the weapon, the ground under each foot, the monsters
                near it and what they were doing, the last log lines
  the record    the hit viewer, performance and spring reports of the seconds before (hits.py, perf.py, springs.py)

Findings: a foot under the floor or a standing body floating, an error in the log just before, the picture and the
skeleton disagreeing, plus each sub-report's own."""
import json
import math
import os

from PIL import Image, ImageDraw

from .findings import finding
from . import devdraw as D, hits as HITS, perf as PERF, springs as SPRINGS

LINKS = [("hips", "spine"), ("spine", "chest"), ("chest", "neck"), ("neck", "head")]
for s in ("l", "r"):
    LINKS += [("chest", "shoulder." + s), ("shoulder." + s, "upperarm." + s), ("upperarm." + s, "lowerarm." + s), ("lowerarm." + s, "hand." + s),
              ("hips", "upperleg." + s), ("upperleg." + s, "lowerleg." + s), ("lowerleg." + s, "foot." + s), ("foot." + s, "toes." + s)]
# body pose detector joints -> the slim skeleton's bones
POSE = {"left_shoulder": "upperarm.l", "right_shoulder": "upperarm.r", "left_elbow": "lowerarm.l", "right_elbow": "lowerarm.r",
        "left_wrist": "hand.l", "right_wrist": "hand.r", "left_hip": "upperleg.l", "right_hip": "upperleg.r",
        "left_knee": "lowerleg.l", "right_knee": "lowerleg.r", "left_ankle": "foot.l", "right_ankle": "foot.r"}
STANDING = ("Idle", "Gait_", "Walk", "Jog", "Sprint", "Run")


class Camera:
    def __init__(self, cam, size):
        self.c, self.f, self.u = cam["position"], cam["forward"], cam["up"]
        f, u = self.f, self.u
        self.r = (u[1] * f[2] - u[2] * f[1], u[2] * f[0] - u[0] * f[2], u[0] * f[1] - u[1] * f[0])   # Unity: right = up x forward
        self.t = math.tan(math.radians(cam.get("fov", 60)) / 2); self.a = cam.get("aspect", size[0] / size[1]); self.W, self.H = size

    def px(self, p):
        d = [p[i] - self.c[i] for i in range(3)]
        z = sum(d[i] * self.f[i] for i in range(3))
        if z < .05: return None
        x = sum(d[i] * self.r[i] for i in range(3)); y = sum(d[i] * self.u[i] for i in range(3))
        return ((x / (z * self.t * self.a) + 1) / 2 * self.W, (1 - y / (z * self.t)) / 2 * self.H)


def overlay(folder, state, hitdata, out):
    shot = os.path.join(folder, "screen.png")
    if not os.path.isfile(shot) or "camera" not in state: return None, {}
    im = Image.open(shot).convert("RGB"); d = ImageDraw.Draw(im); cam = Camera(state["camera"], im.size)
    f = D.font(14)
    def skeleton(bones, color):
        pts = {n: cam.px(p) for n, p in bones.items()}
        for a, b in LINKS:
            if pts.get(a) and pts.get(b): d.line([pts[a], pts[b]], fill=color, width=3)
        for n in set(x for l in LINKS for x in l):
            if pts.get(n): d.ellipse([pts[n][0] - 3, pts[n][1] - 3, pts[n][0] + 3, pts[n][1] + 3], fill=color)
        return pts
    hp = skeleton(state.get("hunter", {}).get("bones", {}), (90, 220, 255))
    for m in state.get("monsters", []):
        skeleton(m.get("bones", {}), (255, 200, 80))
    for m in (hitdata or {}).get("monsters", []):
        for b in m.get("boxes", []):
            pa, pb = cam.px(b["a"]), cam.px(b["b"])
            if not pa or not pb: continue
            # the hurtbox's screen radius at its own depth
            off = [b["a"][i] + (cam.u[i] * b["r"]) for i in range(3)]; po = cam.px(off)
            rr = max(2, math.hypot(po[0] - pa[0], po[1] - pa[1])) if po else 4
            for p in (pa, pb): d.ellipse([p[0] - rr, p[1] - rr, p[0] + rr, p[1] + rr], outline=(255, 70, 70), width=2)
            if math.hypot(pa[0] - pb[0], pa[1] - pb[1]) > 1: d.line([pa, pb], fill=(255, 70, 70), width=1)
    d.text((10, 10), "blue: the hunter's skeleton as the engine has it; amber: monsters' skeletons; red: monster hurtboxes", fill=(255, 255, 255), font=f)
    path = os.path.join(out, "screen_overlay.png"); im.save(path)
    return path, {k: v for k, v in hp.items() if v}


def pose_check(folder, hp, state):
    """the body pose detector on the hunter's part of the picture, against the engine's joints (pixels)"""
    try:
        import numpy as np
        from .sprite import pose_ai
    except Exception as e:
        return None, "pose detector not available (%s)" % e
    pts = [p for p in hp.values()]
    if len(pts) < 8: return None, "the hunter is not in view"
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    h = max(ys) - min(ys)
    if h < 80: return None, "the hunter is too small in the picture (%d px)" % h
    im = Image.open(os.path.join(folder, "screen.png")).convert("RGBA")
    m = .35 * h
    box = (int(max(0, min(xs) - m)), int(max(0, min(ys) - m)), int(min(im.width, max(xs) + m)), int(min(im.height, max(ys) + m)))
    crop = np.asarray(im.crop(box))
    k = pose_ai.keypoints_tta(crop, "body")
    offs = []
    for kp, bone in POSE.items():
        if kp in k and bone in hp and k[kp][2] >= .4:
            offs.append((kp, math.hypot(k[kp][0] + box[0] - hp[bone][0], k[kp][1] + box[1] - hp[bone][1]) / h))
    if len(offs) < 5: return None, "the detector found too few joints"
    offs.sort(key=lambda x: x[1])
    return {"median": offs[len(offs) // 2][1], "worst": offs[-1], "joints": offs, "height": h}, None


def analyse(folder):
    state = json.load(open(os.path.join(folder, "state.json"), encoding="utf-8"))
    hitdata = json.load(open(os.path.join(folder, "hits.json"), encoding="utf-8")) if os.path.isfile(os.path.join(folder, "hits.json")) else None
    perfdata = json.load(open(os.path.join(folder, "perf.json"), encoding="utf-8")) if os.path.isfile(os.path.join(folder, "perf.json")) else None
    return state, hitdata, perfdata


def findings(state, pose):
    out = []
    h = state.get("hunter", {}); clip = h.get("clip", "")
    for who, body in [("hunter", h)] + [(m.get("name", "monster"), m) for m in state.get("monsters", [])]:
        g = body.get("ground", {})
        for foot, v in g.items():
            if v["above"] < -.05:
                out.append(finding("likely", "foot under the floor", "%s %s" % (who, foot), "%.0f cm below %s in %s at %.2f s" % (-v["above"] * 100, v["surface"], body.get("clip"), body.get("clipTime", 0)),
                                   measured=v["above"], threshold=-.05))
        feet = [v["above"] for k, v in g.items() if k.startswith("foot") or k.startswith("hind_paw")]
        if feet and min(feet) > .14 and any(body.get("clip", "").split("|")[-1].startswith(s) for s in STANDING):
            out.append(finding("uncertain", "standing body floating", who, "both feet %.0f cm or more above the ground in %s" % (min(feet) * 100, body.get("clip")), measured=min(feet), threshold=.14))
    errs = [l for l in state.get("log", []) if " Error " in l or " Exception " in l or " Assert " in l]
    for l in errs[-5:]:
        out.append(finding("likely", "error in the log before the capture", "log", l[:300]))
    if state.get("fps") and state["fps"] < 30:
        out.append(finding("uncertain", "frame rate at the moment", "game", "%.0f fps over the last second" % state["fps"], measured=state["fps"], threshold=30))
    if pose and pose["median"] > .08:
        out.append(finding("uncertain", "the picture and the skeleton disagree", "hunter %s" % clip,
                           "the pose detector's joints sit a median %.0f %% of the body's height from the engine's (worst %s %.0f %%)" % (pose["median"] * 100, pose["worst"][0], pose["worst"][1] * 100),
                           measured=pose["median"], threshold=.08, fix="look at the overlay: skin weights, a hidden rig, or the detector misreading the picture"))
    return out


def text(state):
    h = state.get("hunter", {})
    lines = ["area %s, %s%s, %.0f fps; note: %s" % (state.get("area"), state.get("mode"), (", menu " + state["menu"]) if state.get("menu") else "", state.get("fps", 0), state.get("note") or "-"),
             "hunter at %s: clip %s at %.2f / %.2f s (x%.2f), move %s, event %s, weapon %s, health %.0f%s" %
             (_v(h.get("position")), h.get("clip"), h.get("clipTime", 0), h.get("clipLength", 0), h.get("clipSpeed", 0), h.get("move") or "-", h.get("event") or "-", h.get("weapon"), h.get("health", 0),
              ", dodging" if h.get("dodging") else "")]
    for foot, v in h.get("ground", {}).items():
        lines.append("  %-8s %+.3f m over %s" % (foot, v["above"], v["surface"]))
    for m in state.get("monsters", []):
        lines.append("%s (%s) at %s: %s, attack %s, clip %s at %.2f s" % (m.get("name"), m.get("key"), _v(m.get("position")), m.get("brainMode"), m.get("attack") or "-", m.get("clip"), m.get("clipTime", 0)))
    for s in state.get("springs", []):
        lines.append("spring %s: %.1f / %.0f deg" % (s["label"], s["lag"], s["max"]))
    lines.append("log (latest):")
    lines += ["  " + l for l in state.get("log", [])[-25:]]
    return "\n".join(lines)


def _v(p):
    return "(%.2f, %.2f, %.2f)" % tuple(p) if p else "-"


def register(sp, ctx):
    p = sp.add_parser("problem", help="a moment a player flagged (F9 capture): the picture with the engine's bones and hurtboxes, the state, the record before it")
    p.add_argument("folder", help="the capture folder (Captures/Problems/problem_<time>), or 'latest'")
    p.add_argument("--run")
    p.set_defaults(fn=lambda a: cmd(a, ctx))


def latest(prj):
    base = os.path.join(prj.root, "Captures", "Problems")
    if not os.path.isdir(base):
        base = os.path.join(os.path.dirname(prj.root), "Captures", "Problems")
    ds = [os.path.join(base, d) for d in os.listdir(base) if d.startswith("problem_")] if os.path.isdir(base) else []
    if not ds: raise FileNotFoundError("no captures in " + base)
    return max(ds, key=os.path.getmtime)


def cmd(a, ctx):
    prj = ctx["project"](a)
    folder = latest(prj) if a.folder == "latest" else os.path.abspath(a.folder)
    state, hitdata, perfdata = analyse(folder)
    out = ctx["run_dir"](a.run, "problem_" + os.path.basename(folder.rstrip("/\\")).replace("problem_", ""), prj)
    ev = os.path.join(out, "evidence")
    pic, hp = overlay(folder, state, hitdata, ev)
    pose, why = pose_check(folder, hp, state) if pic else (None, "no screenshot")
    res = findings(state, pose)
    sections = [("The moment (with the engine's skeletons and hurtboxes drawn over it)", [pic] if pic else [], None), ("State", [], text(state))]
    if pose:
        sections.append(("Pose detector against the engine's joints (share of body height)", [], "\n".join("%-16s %.0f %%" % (k, v * 100) for k, v in pose["joints"])))
    elif why:
        res.append(finding("not_completed", "pose detector check", "hunter", why))
    if hitdata:
        moves, attacks = HITS.analyse(hitdata)
        res += HITS.findings(moves, attacks)
        sections += [("Hits before the capture", [], HITS.table(moves, attacks)), ("Missed swings, drawn", HITS.pictures(hitdata, moves, attacks, ev, 6), None)]
    if perfdata:
        r = PERF.analyse(perfdata)
        res += PERF.findings(r)
        sections += [("Frames before the capture", [], PERF.text(r)), ("Frame charts", PERF.charts(perfdata, ev), None)]
        sr = SPRINGS.analyse(perfdata)
        if sr:
            res += SPRINGS.findings(sr)
            sections += [("Springs", [], SPRINGS.text(sr)), ("Spring lag", SPRINGS.charts(perfdata, sr, ev), None)]
    h = state.get("hunter", {})
    page, rep = ctx["report"].write(out, "Problem capture: %s, %s at %.2f s" % (state.get("area"), h.get("clip"), h.get("clipTime", 0)), res, sections=sections,
                                    meta={"capture": folder, "time": state.get("time")})
    return page, rep
