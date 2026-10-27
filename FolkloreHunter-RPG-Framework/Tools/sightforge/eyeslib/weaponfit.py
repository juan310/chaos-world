"""Weapon fitter (Studio 0.17): a weapon model read before it goes into a hand. Its long axis, the cross-section along it
(twenty bands), the guard (the widest band, for blades), the hilt end (the end nearer the guard), the tip, the share of
the length the hilt takes, the thickness of the grip; for a bow its limb tips and whether a string is modelled; for a
shield its face and boss. Each is checked against what the kind of weapon should look like in a hand:

  blade   hilt (grip and pommel) 12-30 % of the length; grip no thicker than 6 % of the length; guard narrower than 35 %
  bow     a grip in the middle (its narrowest-but-solid middle band); string modelled or not (the game can draw one live)
  pole    a head (the widest end band) at one end
  shield  thin (thickness under 25 % of its height)

The answers (grip point, guard, tip, axes, in the model's own space) go to weaponfit.json for the game's fitting code
(the Folklore Hunter game's WeaponSkin uses the same rules); proportions outside the ranges are findings. Works on the
mesh alone (OBJ/GLB/FBX through Blender, or a numpy array of vertices)."""
import json
import os
import subprocess

import numpy as np

from .findings import finding

HERE = os.path.dirname(os.path.abspath(__file__))


def analyze(v, kind):
    v = np.asarray(v, float)
    lo, hi = v.min(0), v.max(0); size = hi - lo
    axes = list(np.argsort(-size)); L, M, T = axes
    length = float(size[L])
    t = (v[:, L] - lo[L]) / max(1e-9, length)
    widths = []
    for k in range(20):
        sel = (t >= k / 20) & (t <= (k + 1) / 20)
        widths.append(float(max(np.ptp(v[sel, M]), np.ptp(v[sel, T]))) if sel.sum() > 2 else 0.0)
    widths = np.array(widths)
    res = {"kind": kind, "length": length, "size": size.tolist(), "axes": {"long": int(L), "mid": int(M), "thin": int(T)},
           "bands": [round(w / length, 4) for w in widths]}
    if kind == "blade":
        g = int(np.argmax(widths)); hilt_at_min = g < 10
        g0, g1 = g, g
        while g0 > 0 and widths[g0 - 1] > widths[g] * .75: g0 -= 1
        while g1 < 19 and widths[g1 + 1] > widths[g] * .75: g1 += 1
        hilt = (g0 / 20) if hilt_at_min else (1 - (g1 + 1) / 20)
        grip_bands = range(0, g0) if hilt_at_min else range(g1 + 1, 20)
        grip_w = float(np.median([widths[k] for k in grip_bands])) if len(grip_bands) else 0.0
        res.update({"guardBand": [g0, g1], "hiltAtMin": bool(hilt_at_min), "hiltShare": round(hilt, 3),
                    "gripThickness": round(grip_w / length, 3), "guardWidth": round(widths[g] / length, 3)})
        gp = (lo + hi) / 2; gp[L] = lo[L] + length * ((g0 if hilt_at_min else g1 + 1) / 20)
        res["guardPoint"] = gp.tolist()
        tip = (lo + hi) / 2; tip[L] = hi[L] if hilt_at_min else lo[L]; res["tipPoint"] = tip.tolist()
        grip = gp.copy(); grip[L] += (-1 if hilt_at_min else 1) * length * .06; res["gripPoint"] = grip.tolist()
    elif kind == "bow":
        mid = widths[7:13]; res["gripBand"] = int(7 + np.argmax(mid))
        ends = [(lo + hi) / 2, (lo + hi) / 2]; ends[0][L] = lo[L]; ends[1][L] = hi[L]
        res["limbTips"] = [e.tolist() for e in ends]
        # a modelled string: thin vertices along the long axis on one side, far from the wood's mean
        mean = float(v[:, M].mean())
        side = np.where(np.abs(v[:, M] - (lo[M] if mean > (lo[M] + hi[M]) / 2 else hi[M])) < size[M] * .06)[0]
        res["stringModelled"] = bool(len(side) > 8 and np.ptp(v[side, L]) > length * .8)
    elif kind == "pole":
        head_min = widths[:4].max() > widths[-4:].max()
        res["headAtMin"] = bool(head_min); res["headWidth"] = round(float(max(widths[:4].max(), widths[-4:].max())) / length, 3)
    elif kind == "shield":
        res["thickness"] = round(float(size[T]) / max(1e-9, float(size[L])), 3)
    return res


def problems(r):
    out = []
    if r["kind"] == "blade":
        if not .12 <= r["hiltShare"] <= .30:
            out.append(("likely", "hilt proportion", "the hilt is %.0f %% of the length (a one-handed sword's is 12-30 %%)" % (r["hiltShare"] * 100),
                        "shorten the grip and pommel, or make the weapon again from a picture with a normal hilt"))
        if r["gripThickness"] > .06:
            out.append(("likely", "grip thickness", "the grip is %.1f %% of the length thick (a hand closes on under 6 %%)" % (r["gripThickness"] * 100),
                        "thin the grip"))
        if r["guardWidth"] > .35:
            out.append(("uncertain", "guard width", "the guard is %.0f %% of the length wide" % (r["guardWidth"] * 100), None))
    if r["kind"] == "bow" and not r.get("stringModelled"):
        out.append(("uncertain", "bow string", "no string modelled: the game must draw one (a live string from the limb tips)", None))
    if r["kind"] == "shield" and r["thickness"] > .25:
        out.append(("likely", "shield thickness", "the shield is %.0f %% as thick as it is tall" % (r["thickness"] * 100), None))
    return out


def vertices(model):
    """the model's vertices (all its meshes, in its own space), read through Blender"""
    from .mesh3d import blender_path
    out = model + ".verts.npy"
    script = ("import bpy,sys,numpy as np\nbpy.ops.wm.read_factory_settings(use_empty=True)\np=sys.argv[-2]\n"
              "(bpy.ops.import_scene.gltf if p.lower().endswith(('.glb','.gltf')) else bpy.ops.wm.obj_import if p.lower().endswith('.obj') else bpy.ops.import_scene.fbx)(filepath=p)\n"
              "vs=[list(o.matrix_world@v.co) for o in bpy.data.objects if o.type=='MESH' for v in o.data.vertices]\nnp.save(sys.argv[-1],np.array(vs))\n")
    sp = os.path.join(os.path.dirname(os.path.abspath(out)), "_weaponfit_read.py")
    open(sp, "w").write(script)
    subprocess.run([blender_path(), "-b", "--python", sp, "--", os.path.abspath(model), os.path.abspath(out)], capture_output=True, timeout=600)
    os.remove(sp)
    v = np.load(out); os.remove(out)
    return v


def register(sp, ctx):
    p = sp.add_parser("weaponfit", help="a weapon model's grip, guard, tip and proportions before it goes into a hand")
    p.add_argument("models", nargs="+"); p.add_argument("--kind", choices=["blade", "bow", "pole", "shield"], required=True)
    p.add_argument("--run")
    p.set_defaults(fn=lambda a: cmd(a, ctx))


def cmd(a, ctx):
    prj = ctx["project"](a)
    out = ctx["run_dir"](a.run, "weaponfit_" + a.kind, prj)
    res, results = [], {}
    for m in a.models:
        r = analyze(vertices(m), a.kind); results[os.path.basename(m)] = r
        for kind, check, detail, fix in problems(r):
            res.append(finding(kind, check, os.path.basename(m), detail, fix=fix))
    with open(os.path.join(out, "weaponfit.json"), "w", encoding="utf-8") as fh:
        json.dump(results, fh, indent=1)
    lines = []
    for n, r in results.items():
        bars = "".join(" .:-=+*#%@"[min(9, int(b * 40))] for b in r["bands"])
        lines.append("%-34s length %.2f  bands |%s|  %s" % (n, r["length"], bars, ", ".join("%s %s" % (k, r[k]) for k in ("hiltShare", "gripThickness", "guardWidth", "stringModelled", "headAtMin", "thickness") if k in r)))
    page, data = ctx["report"].write(out, "Weapon fit: %d %s model(s)" % (len(results), a.kind), res,
                                     sections=[("Cross-section along the length (wider = denser mark) and proportions", [], "\n".join(lines))],
                                     meta={"results": results})
    return page, data
