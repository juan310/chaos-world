"""Map and terrain mesh system (Studio 0.17): a game's areas as the player meets them, and terrain models before they go in.

Survey mode (the Folklore Hunter game's TerrainSurvey: terrain_<area>.json, written with --survey-terrain or F10):
  ground the hunter can never reach   standable ground (slope and head room fine) cut off from the spawn by the
                                      hunter's own limits (a step of .4 m, slope 48 degrees, a jump down of 2.5 m)
  holes                               spots inside the area with nothing to stand on, beside reachable ground
  open ledges                         reachable ground ending in a fall of over 2.5 m (or into nothing) with no wall
  walk-through scenery                a drawn piece with no collider in it (the hunter passes through it)
  floating props                      scenery hanging above the ground under it
  invisible walls                     colliders with nothing drawn on them (often meant; the owner decides)
  overlapping surfaces                two surfaces at the same height in the same spot (they flicker)
Each area gets a top-down map: height shading, reached ground green-tinted, unreachable ground violet, holes red, open
ledges yellow, props and invisible walls outlined.

Mesh mode (terrain --mesh <model>, through Blender): holes in the surface (open edges), edges shared by more than two
faces, faces turned upside down among upward neighbours, faces lying on top of each other, slivers; a top-down picture
with each marked."""
import json
import math
import os
import subprocess
from collections import deque

from PIL import Image, ImageDraw

from .findings import finding
from . import devdraw as D

HERE = os.path.dirname(os.path.abspath(__file__))
SOFT = ("grass", "flower", "fern", "reed", "smoke", "water", "river", "leaf", "leaves", "light", "glow", "flame", "fire", "spark", "fog",
        "mist", "cloud", "sky", "particle", "dust", "moss", "petal", "vine", "decal", "shadow", "ring", "marker", "beam", "aura", "telegraph")


def load(path):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def _soft(name):
    n = name.lower()
    return any(s in n for s in SOFT)


def analyse(sv):
    nx, nz = sv["size"]; cells = sv["cells"]          # cells[k][i] = [[y, slope, collider, reached], ...]
    step = sv["step"]; ox, oz = sv["origin"]; lim = sv["limits"]
    circle = sv.get("circle")
    def inside(i, k):
        if not circle: return True
        x, z = ox + (i + .5) * step, oz + (k + .5) * step
        return math.hypot(x - circle[0], z - circle[1]) < circle[2] - .6
    reached = [[any(s[3] for s in cells[k][i]) for i in range(nx)] for k in range(nz)]
    # unreachable standable ground: components of cells whose surfaces are all unreached and gentle
    seen = set(); islands = []
    for k in range(nz):
        for i in range(nx):
            if (i, k) in seen or reached[k][i] or not inside(i, k): continue
            if not any(s[1] <= lim["slope"] for s in cells[k][i]): continue
            comp = []; q = deque([(i, k)]); seen.add((i, k))
            while q:
                a, b = q.popleft(); comp.append((a, b))
                for da, db in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    c, d = a + da, b + db
                    if 0 <= c < nx and 0 <= d < nz and (c, d) not in seen and not reached[d][c] and inside(c, d) and any(s[1] <= lim["slope"] for s in cells[d][c]):
                        seen.add((c, d)); q.append((c, d))
            if len(comp) * step * step >= 2:
                ys = [max(s[0] for s in cells[b][a]) for a, b in comp]
                cx = sum(a for a, _ in comp) / len(comp); cz = sum(b for _, b in comp) / len(comp)
                names = {}
                for a, b in comp[:400]:
                    for s in cells[b][a]: names[sv["colliders"][s[2]]] = names.get(sv["colliders"][s[2]], 0) + 1
                islands.append({"cells": comp, "area": len(comp) * step * step, "at": (ox + (cx + .5) * step, oz + (cz + .5) * step),
                                "height": (min(ys), max(ys)), "what": max(names, key=names.get) if names else "?"})
    # holes beside reached ground
    holes = set()
    for k in range(nz):
        for i in range(nx):
            if cells[k][i] or not inside(i, k): continue
            if any(0 <= i + a < nx and 0 <= k + b < nz and reached[k + b][i + a] for a, b in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                holes.add((i, k))
    hole_groups = _groups(holes)
    ledges = sv.get("ledges", [])
    ledge_groups = _cluster([((l[0] + l[2]) / 2, (l[1] + l[3]) / 2, l[4]) for l in ledges], 3.0)
    props = [p for p in sv.get("props", []) if not _soft(p["what"])]
    walk = [p for p in props if p.get("walkThrough")]
    floating = [p for p in props if not p.get("walkThrough") and p.get("above", -1) > .08]
    return {"islands": islands, "holes": hole_groups, "ledges": ledge_groups, "walk": walk, "floating": floating,
            "invisible": sv.get("invisible", []), "overlaps": sv.get("overlaps", 0), "overlapAt": sv.get("overlapAt", []),
            "reached": sum(1 for row in reached for r in row if r) * step * step}


def _groups(cells):
    left = set(cells); out = []
    while left:
        c = left.pop(); comp = [c]; q = deque([c])
        while q:
            a, b = q.popleft()
            for da in (-1, 0, 1):
                for db in (-1, 0, 1):
                    n = (a + da, b + db)
                    if n in left: left.remove(n); comp.append(n); q.append(n)
        out.append(comp)
    return sorted(out, key=len, reverse=True)


def _cluster(points, r):
    out = []
    for x, z, drop in points:
        for c in out:
            if math.hypot(c["x"] - x, c["z"] - z) < r:
                c["n"] += 1; c["drop"] = min(c["drop"], drop); c["pts"].append((x, z)); break
        else:
            out.append({"x": x, "z": z, "n": 1, "drop": drop, "pts": [(x, z)]})
    return sorted(out, key=lambda c: -c["n"])


def findings(sv, r, evidence=None):
    out = []; area = sv["area"]; step = sv["step"]; ox, oz = sv["origin"]
    spawn_y = sv["spawn"][1]
    for isl in sorted(r["islands"], key=lambda x: -x["area"])[:25]:
        high = isl["height"][0] > spawn_y + 3
        out.append(finding("uncertain" if high or isl["area"] < 4 else "likely", "ground the hunter can never reach", "%s at (%.0f, %.0f)" % (area, *isl["at"]),
                           "%.0f m2 of standable ground (%s, %.1f to %.1f m) cut off from the spawn by steps, slopes or walls%s" %
                           (isl["area"], isl["what"], isl["height"][0], isl["height"][1], " (high up: a roof or ledge?)" if high else ""),
                           measured=isl["area"], evidence=evidence, fix="a ramp, steps or a lower step, or block it off so it does not look walkable"))
    for g in r["holes"][:20]:
        a = len(g) * step * step
        cx = ox + (sum(c[0] for c in g) / len(g) + .5) * step; cz = oz + (sum(c[1] for c in g) / len(g) + .5) * step
        out.append(finding("likely" if a >= .5 else "uncertain", "hole in the ground", "%s at (%.1f, %.1f)" % (area, cx, cz),
                           "%.2f m2 with nothing to stand on beside reachable ground" % a, measured=a, evidence=evidence, fix="close the gap between the ground pieces"))
    for c in r["ledges"][:20]:
        drop = "into nothing" if c["drop"] <= -98 else "%.1f m down" % -c["drop"]
        out.append(finding("likely" if c["n"] >= 3 else "uncertain", "open ledge", "%s at (%.1f, %.1f)" % (area, c["x"], c["z"]),
                           "reachable ground ends %s with no wall or rail (%d spots along it)" % (drop, c["n"]), measured=c["n"], evidence=evidence,
                           fix="a wall, rail, rocks, or an invisible blocker at the edge"))
    for p in r["walk"][:40]:
        h = p["max"][1] - p["min"][1]
        out.append(finding("likely" if h > 1.2 else "uncertain", "walk-through scenery", "%s: %s" % (area, p["what"]),
                           "%.1f m tall, drawn but with no collider in it: the hunter walks through it" % h, measured=h, fix="give it a collider"))
    for p in sorted(r["floating"], key=lambda p: -p["above"])[:40]:
        out.append(finding("likely" if p["above"] < .6 else "uncertain", "floating prop", "%s: %s" % (area, p["what"]),
                           "its bottom is %.0f cm above %s" % (p["above"] * 100, p.get("under") or "the ground"), measured=p["above"], threshold=.08,
                           fix="lower it onto the ground" + ("" if p["above"] < .6 else " (or it hangs on purpose)")))
    if r["invisible"]:
        out.append(finding("uncertain", "invisible walls", area, "%d colliders with nothing drawn on them: %s" % (len(r["invisible"]), ", ".join(sorted(set(p["what"] for p in r["invisible"]))[:12])),
                           measured=len(r["invisible"])))
    if r["overlaps"]:
        out.append(finding("uncertain", "overlapping surfaces", area, "%d spots where two surfaces lie within 2 cm of each other (they may flicker): %s" %
                           (r["overlaps"], "; ".join(sorted(set(o[2] for o in r["overlapAt"]))[:6])), measured=r["overlaps"]))
    return out


def picture(sv, r, path, scale=None):
    nx, nz = sv["size"]; cells = sv["cells"]; step = sv["step"]; ox, oz = sv["origin"]
    s = scale or max(2, min(10, int(900 / max(nx, nz))))
    im = Image.new("RGB", (nx * s, nz * s), D.BG); px = im.load()
    ys = [c[0][0] for row in cells for c in row if c]
    lo, hi = (min(ys), max(ys)) if ys else (0, 1)
    island = set((a, b) for isl in r["islands"] for a, b in isl["cells"])
    holes = set(c for g in r["holes"] for c in g)
    for k in range(nz):
        for i in range(nx):
            c = cells[k][i]
            if (i, k) in holes: col = (230, 50, 50)
            elif not c: col = (16, 16, 18)
            else:
                t = (c[0][0] - lo) / max(1e-6, hi - lo); g = int(60 + 150 * t)
                if (i, k) in island: col = (int(g * .8) + 50, int(g * .4), g + 40)
                elif any(x[3] for x in c): col = (int(g * .75), g, int(g * .7))
                elif c[0][1] > sv["limits"]["slope"]: col = (g, int(g * .65), int(g * .4))
                else: col = (g, g, g)
            for a in range(s):
                for b in range(s):
                    px[i * s + a, (nz - 1 - k) * s + b] = col
    d = ImageDraw.Draw(im)
    X = lambda x: (x - ox) / step * s
    Z = lambda z: (nz - (z - oz) / step) * s
    for l in sv.get("ledges", []):
        d.line([(X(l[0]), Z(l[1])), (X(l[2]), Z(l[3]))], fill=(255, 230, 60), width=max(1, s // 2))
    for p in r["walk"]:
        d.rectangle([X(p["min"][0]), Z(p["max"][2]), X(p["max"][0]), Z(p["min"][2])], outline=(255, 120, 255), width=2)
    for p in r["floating"]:
        d.rectangle([X(p["min"][0]), Z(p["max"][2]), X(p["max"][0]), Z(p["min"][2])], outline=(80, 220, 255), width=2)
    for p in r["invisible"]:
        d.rectangle([X(p["min"][0]), Z(p["max"][2]), X(p["max"][0]), Z(p["min"][2])], outline=(255, 255, 255), width=1)
    sx, sz = X(sv["spawn"][0]), Z(sv["spawn"][2])
    d.ellipse([sx - 6, sz - 6, sx + 6, sz + 6], outline=(255, 255, 255), width=3)
    f = D.font(13)
    legend = [("reached ground (green tint)", (120, 170, 110)), ("never reachable", (170, 90, 200)), ("hole", (230, 50, 50)), ("open ledge", (255, 230, 60)),
              ("too steep", (200, 130, 80)), ("walk-through", (255, 120, 255)), ("floating", (80, 220, 255)), ("invisible wall", (255, 255, 255))]
    y = 6
    d.rectangle([4, 2, 196, 8 + 16 * len(legend) + 18], fill=(0, 0, 0))
    d.text((10, y), "%s (spawn: white ring)" % sv["area"], fill=(255, 255, 255), font=f); y += 18
    for t, c in legend:
        d.rectangle([10, y + 3, 20, y + 13], fill=c); d.text((26, y), t, fill=(230, 230, 230), font=f); y += 16
    im.save(path)
    return path


# ---------------- mesh mode ----------------
def mesh_run(model, out_dir, timeout=1800):
    from .mesh3d import blender_path
    os.makedirs(out_dir, exist_ok=True)
    log = subprocess.run([blender_path(), "-b", "--python", os.path.join(HERE, "terrain_blender.py"), "--", os.path.abspath(model), os.path.abspath(out_dir)],
                         capture_output=True, text=True, timeout=timeout)
    p = os.path.join(out_dir, "terrain_mesh.json")
    if not os.path.isfile(p):
        raise RuntimeError("the terrain mesh check did not finish:\n" + "\n".join((log.stdout + log.stderr).splitlines()[-12:]))
    return load(p)


def mesh_findings(rep):
    out = []; sub = os.path.basename(rep["model"])
    pics = rep.get("pictures")
    for o in rep["objects"]:
        nm = "%s %s" % (sub, o["name"])
        if o["open"] > 0:
            out.append(finding("likely" if o["openLength"] > .3 else "uncertain", "holes in the surface", nm, "%d open edges, %.1f m of edge with a face on one side only (%d separate openings)" % (o["open"], o["openLength"], o["openings"]),
                               measured=o["openLength"], evidence=pics, fix="fill the holes or weld the seams (merge by distance)"))
        if o["nonManifold"] > 0:
            out.append(finding("likely", "edges shared by more than two faces", nm, "%d edges" % o["nonManifold"], measured=o["nonManifold"], evidence=pics))
        if o["flipped"] > 0:
            out.append(finding("likely", "faces upside down", nm, "%d faces (%.1f m2) face down among upward-facing neighbours" % (o["flipped"], o["flippedArea"]), measured=o["flipped"], evidence=pics,
                               fix="recalculate normals outside"))
        if o["overlapping"] > 0:
            out.append(finding("likely", "faces on top of each other", nm, "%d faces lie within 1 mm of another face (they flicker)" % o["overlapping"], measured=o["overlapping"], evidence=pics,
                               fix="delete the duplicates (merge by distance, delete loose)"))
        if o["slivers"] > 0:
            out.append(finding("uncertain", "sliver faces", nm, "%d faces thinner than 1 mm" % o["slivers"], measured=o["slivers"]))
    return out


def register(sp, ctx):
    p = sp.add_parser("terrain", help="maps and terrain: unreachable ground, holes, open ledges, walk-through or floating scenery; or a terrain model's mesh")
    p.add_argument("survey", nargs="*", help="terrain_<area>.json files, or folders with them (the game's --survey-terrain / F10)")
    p.add_argument("--mesh", help="a terrain or level model (.fbx/.glb/.obj) to check instead")
    p.add_argument("--run")
    p.set_defaults(fn=lambda a: cmd(a, ctx))


def cmd(a, ctx):
    prj = ctx["project"](a)
    if a.mesh:
        stem = os.path.splitext(os.path.basename(a.mesh))[0]
        out = ctx["run_dir"](a.run, "terrain_mesh_" + stem, prj)
        rep = mesh_run(a.mesh, os.path.join(out, "evidence"))
        lines = ["%-30s %7s %6s %9s %8s %7s %8s %6s" % ("object", "faces", "open", "openings", "nonmani", "flipped", "overlap", "sliver")]
        for o in rep["objects"]:
            lines.append("%-30s %7d %6d %9d %8d %7d %8d %6d" % (o["name"][:30], o["faces"], o["open"], o["openings"], o["nonManifold"], o["flipped"], o["overlapping"], o["slivers"]))
        return ctx["report"].write(out, "Terrain mesh: %s" % stem, mesh_findings(rep), sections=[("Objects", [], "\n".join(lines)), ("From above (red: open edges, orange: upside down, violet: on top of each other)", rep.get("pictures", []), None)],
                                   meta={"model": os.path.abspath(a.mesh)})
    files = []
    for s in a.survey or []:
        if os.path.isdir(s): files += sorted(os.path.join(s, f) for f in os.listdir(s) if f.startswith("terrain_") and f.endswith(".json"))
        else: files.append(s)
    if not files: raise FileNotFoundError("no terrain_<area>.json given (run the game with --survey-terrain <folder>, or press F10)")
    out = ctx["run_dir"](a.run, "terrain", prj)
    res, sections, summary = [], [], []
    for f in files:
        sv = load(f); r = analyse(sv)
        pic = picture(sv, r, os.path.join(out, "evidence", "map_%s.png" % sv["area"]))
        res += findings(sv, r, [pic])
        summary.append("%-10s %4.0f m2 reached, %d unreachable patches (%.0f m2), %d holes, %d open ledges, %d walk-through, %d floating, %d invisible walls, %d overlaps (%.1f s survey)" %
                       (sv["area"], r["reached"], len(r["islands"]), sum(i["area"] for i in r["islands"]), len(r["holes"]), len(r["ledges"]), len(r["walk"]), len(r["floating"]),
                        len(r["invisible"]), r["overlaps"], sv.get("seconds", 0)))
        sections.append(("%s from above" % sv["area"], [pic], None))
    return ctx["report"].write(out, "Terrain: %d areas" % len(files), res, sections=[("Areas", [], "\n".join(summary))] + sections, meta={"surveys": [os.path.abspath(f) for f in files]})
