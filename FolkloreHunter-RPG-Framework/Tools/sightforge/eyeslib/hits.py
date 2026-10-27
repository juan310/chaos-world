"""Hit viewer (Studio 0.17): every hit test a game recorded (the Folklore Hunter game's HitTrace: hits.json, written on F9
and at the end of a verification run), read per move and per monster attack.

  the hunter's moves   each swing's blade sweeps (a capsule from the guard to the tip, radius r, each frame of its active
                       window), whether it landed, and for a swing that hit nothing the nearest monster's hurtboxes and
                       the gap it missed them by
  arrows and spells    the same, one capsule per frame of flight
  monster attacks      each attack's ball against the hunter (centre, radius, the hunter's body), landed or not, and
                       whether the hunter was invulnerable (an evasion)

Findings: a move that never connects (with how far short it falls and the bone it comes nearest), a move that lands
rarely, a monster attack whose ball never reaches the hunter. Pictures: for each move with misses, its sweeps from above
and from the side over the nearest monster's hurtboxes; for each monster attack, its balls around the hunter."""
import json
import math
import os
from collections import defaultdict

from .findings import finding
from . import devdraw as D


def load(path):
    if os.path.isdir(path):
        path = os.path.join(path, "hits.json")
    with open(path, encoding="utf-8") as fh:
        return json.load(fh), path


def _flat(a, b):
    return math.hypot(a[0] - b[0], a[2] - b[2])


def analyse(data):
    swings = defaultdict(lambda: {"sweeps": [], "landed": []})
    strikes = defaultdict(list)
    for e in data.get("entries", []):
        k = e.get("kind", "")
        if k in ("hunter", "arrow", "spell"):
            swings[(k, e.get("move", ""), e.get("swing", 0))]["sweeps"].append(e)
        elif k.endswith("landed"):
            src = "hunter" if k == "landed" else k[:-7]
            swings[(src, e.get("move", ""), e.get("swing", 0))]["landed"].append(e)
        elif k == "monster":
            strikes[(e.get("monster", ""), e.get("move", ""))].append(e)
    moves = defaultdict(lambda: {"swings": 0, "landed": 0, "missGaps": [], "nearBones": defaultdict(int), "examples": [], "damage": 0.0, "kind": ""})
    for (kind, move, swing), s in swings.items():
        m = moves[(kind, move)]; m["kind"] = kind
        m["swings"] += 1
        if s["landed"]:
            m["landed"] += 1; m["damage"] += sum(x.get("damage", 0) for x in s["landed"])
            continue
        best = None
        for sw in s["sweeps"]:
            for b in (sw.get("nearest") or {}).get("boxes", []):
                if best is None or b["gap"] < best[0]:
                    best = (b["gap"], b["bone"], sw)
        if best:
            m["missGaps"].append(best[0]); m["nearBones"][best[1]] += 1
            if len(m["examples"]) < 3: m["examples"].append(s["sweeps"])
    attacks = {}
    for (mon, atk), es in strikes.items():
        # contiguous frames are one attack
        tries, cur = [], []
        for e in sorted(es, key=lambda x: x["frame"]):
            if cur and e["frame"] - cur[-1]["frame"] > 3:
                tries.append(cur); cur = []
            cur.append(e)
        if cur: tries.append(cur)
        rec = {"name": es[0].get("name", mon), "tries": len(tries), "landed": 0, "evaded": 0, "gaps": [], "examples": []}
        for t in tries:
            if any(e.get("landed") for e in t):
                rec["landed"] += 1
                if any(e.get("invulnerable") for e in t): rec["evaded"] += 1
            else:
                g = min(_flat(e["a"], e["hunter"]) - e["r"] - e.get("hunterRadius", .35) for e in t)
                rec["gaps"].append(g)
                if len(rec["examples"]) < 2: rec["examples"].append(t)
        attacks[(mon, atk)] = rec
    return moves, attacks


def _median(v):
    v = sorted(v)
    return v[len(v) // 2] if v else None


def findings(moves, attacks, subject=""):
    out = []
    for (kind, move), m in sorted(moves.items()):
        what = "%s %s" % (kind, move) if kind != "hunter" else move
        gap = _median(m["missGaps"]); bone = max(m["nearBones"], key=m["nearBones"].get) if m["nearBones"] else None
        near = (" Missed swings fall short by a median %.2f m (nearest: %s)." % (gap, bone)) if gap is not None else ""
        if m["swings"] >= 2 and m["landed"] == 0:
            out.append(finding("likely" if gap is not None and gap < 1.5 else "uncertain", "a move that never connects", what,
                               "%d swings, none landed.%s" % (m["swings"], near), measured=gap, threshold=0,
                               fix="lengthen the move's reach or radius by about %.2f m, or step it in closer" % max(.05, gap or 0) if gap is not None else "check where the move is aimed"))
        elif m["swings"] >= 4 and m["landed"] / m["swings"] < .35 and gap is not None and gap < .4:
            out.append(finding("uncertain", "a move that lands rarely", what,
                               "%d of %d swings landed.%s" % (m["landed"], m["swings"], near), measured=m["landed"] / m["swings"], threshold=.35))
    for (mon, atk), a in sorted(attacks.items()):
        gap = _median(a["gaps"])
        if a["tries"] >= 2 and a["landed"] == 0 and gap is not None:
            out.append(finding("likely" if gap < .6 else "uncertain", "a monster attack that never reaches the hunter", "%s %s" % (a["name"], atk),
                               "%d tries, none landed; its ball falls short by a median %.2f m" % (a["tries"], gap), measured=gap, threshold=0,
                               fix="the attack's radius or range (or the hunter stood out of reach on purpose)"))
    return out


def pictures(data, moves, attacks, out_dir, limit=12):
    os.makedirs(out_dir, exist_ok=True)
    pics = []
    ranked = sorted(moves.items(), key=lambda kv: (kv[1]["landed"] > 0, -len(kv[1]["missGaps"])))
    for (kind, move), m in ranked[:limit]:
        if not m["examples"]: continue
        sweeps = m["examples"][0]
        last = sweeps[-1]; boxes = (last.get("nearest") or {}).get("boxes", [])
        pts = [s["a"] for s in sweeps] + [s["b"] for s in sweeps] + [b["a"] for b in boxes] + [b["b"] for b in boxes]
        mid = sweeps[len(sweeps) // 2]
        along = (mid["b"][0] - mid["a"][0], mid["b"][2] - mid["a"][2])
        if abs(along[0]) + abs(along[1]) < 1e-3: along = (1, 0)
        for plane in ("top", "side"):
            v = D.View(pts, plane, along=along)
            for b in boxes:
                v.capsule(b["a"], b["b"], b["r"], (230, 80, 80))
            near = min(boxes, key=lambda b: b["gap"]) if boxes else None
            if near: v.label(near["b"], "%s  gap %.2f m" % (near["bone"], near["gap"]), (255, 140, 140))
            n = len(sweeps)
            for i, s in enumerate(sweeps):
                c = (int(90 + 160 * i / max(1, n - 1)), int(170 - 40 * i / max(1, n - 1)), 255)
                v.capsule(s["a"], s["b"], s["r"], c, width=1)
            v.title("%s: a missed swing (%d frames), blue to violet over time; red: %s's hurtboxes" % (move, n, (last.get("nearest") or {}).get("name", "monster")))
            v.title("%d swings, %d landed" % (m["swings"], m["landed"]), 1)
            pics.append(v.save(os.path.join(out_dir, "move_%s_%s_%s.png" % (kind, _safe(move), plane))))
    for (mon, atk), a in list(attacks.items())[:limit]:
        if not a["examples"]: continue
        t = a["examples"][0]
        pts = [e["a"] for e in t] + [e["hunter"] for e in t]
        v = D.View(pts, "top")
        for e in t:
            v.capsule(e["a"], e["a"], e["r"], (255, 120, 60), width=1)
            v.capsule(e["hunter"], e["hunter"], e.get("hunterRadius", .35), (90, 200, 255), width=1)
        v.title("%s %s: the attack's ball (orange) and the hunter (blue) over its frames" % (a["name"], atk))
        v.title("%d tries, %d landed" % (a["tries"], a["landed"]), 1)
        pics.append(v.save(os.path.join(out_dir, "attack_%s_%s.png" % (_safe(mon), _safe(atk)))))
    return pics


def table(moves, attacks):
    lines = ["%-34s %7s %7s %10s %-14s %8s" % ("move", "swings", "landed", "miss gap", "nearest bone", "damage")]
    for (kind, move), m in sorted(moves.items(), key=lambda kv: kv[0]):
        gap = _median(m["missGaps"]); bone = max(m["nearBones"], key=m["nearBones"].get) if m["nearBones"] else "-"
        lines.append("%-34s %7d %7d %10s %-14s %8.0f" % ((move if kind == "hunter" else kind + " " + move)[:34], m["swings"], m["landed"], "%.2f m" % gap if gap is not None else "-", bone, m["damage"]))
    lines.append("")
    lines.append("%-34s %7s %7s %7s %10s" % ("monster attack", "tries", "landed", "evaded", "miss gap"))
    for (mon, atk), a in sorted(attacks.items()):
        gap = _median(a["gaps"])
        lines.append("%-34s %7d %7d %7d %10s" % (("%s %s" % (a["name"], atk))[:34], a["tries"], a["landed"], a["evaded"], "%.2f m" % gap if gap is not None else "-"))
    return "\n".join(lines)


def _safe(s):
    return "".join(c if c.isalnum() else "_" for c in s)[:40]


def register(sp, ctx):
    p = sp.add_parser("hits", help="every hit test a game recorded: moves that miss and by how much, monster attacks")
    p.add_argument("capture", help="a hits.json, or a folder with one (a problem capture, Captures/Proving/DevCapture)")
    p.add_argument("--run")
    p.set_defaults(fn=lambda a: cmd(a, ctx))


def cmd(a, ctx):
    prj = ctx["project"](a)
    data, path = load(a.capture)
    out = ctx["run_dir"](a.run, "hits", prj)
    moves, attacks = analyse(data)
    res = findings(moves, attacks)
    pics = pictures(data, moves, attacks, os.path.join(out, "evidence"))
    n = sum(m["swings"] for m in moves.values())
    page, rep = ctx["report"].write(out, "Hits: %d swings and shots, %d monster attacks" % (n, sum(x["tries"] for x in attacks.values())), res,
                                    sections=[("Per move and per monster attack", [], table(moves, attacks)),
                                              ("Missed swings and attacks, drawn", pics, None)],
                                    meta={"source": os.path.abspath(path), "entries": len(data.get("entries", []))})
    return page, rep
