"""Smear finder (Studio 0.17): where a 3D model's texture is smeared or blurred, marked on the model.

Models reconstructed from front and back pictures smear what those pictures never saw (the outsides of legs and boots,
flanks, undersides); a texture stretched over a large surface blurs. smear_blender.py measures every face: the texture's
own detail where the face samples it, and the texels per metre it gets. Regions (the bone carrying the faces x the way
they face) with much smeared or blurred surface become findings; side-facing smear suggests the side-profile repair
(a Forge edit of the model's own render turned to each side, projected onto the flanks)."""
import json
import os
import subprocess

from .findings import finding

HERE = os.path.dirname(os.path.abspath(__file__))


def run(model, out_dir, texture=None, timeout=1800):
    from .mesh3d import blender_path
    os.makedirs(out_dir, exist_ok=True)
    cmd = [blender_path(), "-b", "--python", os.path.join(HERE, "smear_blender.py"), "--", os.path.abspath(model), os.path.abspath(out_dir)]
    if texture: cmd += ["--texture", os.path.abspath(texture)]
    log = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
    path = os.path.join(out_dir, "smear.json")
    if not os.path.isfile(path):
        raise RuntimeError("the smear finder did not finish:\n" + "\n".join((log.stdout + log.stderr).splitlines()[-12:]))
    return json.load(open(path, encoding="utf-8"))


def findings(rep):
    out = []; subject = os.path.splitext(os.path.basename(rep["model"]))[0]
    for region, r in sorted(rep["regions"].items(), key=lambda kv: -kv[1]["smeared"] * kv[1]["areaShare"]):
        side = region.split()[-1]
        if r["smeared"] >= .35 and r["areaShare"] >= .01:
            fix = ("repair the flanks from side profiles of the model's own render (paint_sides)" if side in ("left", "right")
                   else "repaint or re-project this area")
            out.append(finding("likely", "smeared texture", "%s %s" % (subject, region),
                               "%.0f %% of this region's surface has under a third of the texture's usual detail (%.1f %% of the body)" % (r["smeared"] * 100, r["areaShare"] * 100),
                               measured=r["smeared"], threshold=.35, evidence=rep.get("pictures"), fix=fix))
        elif r["smeared"] >= .2 and r["areaShare"] >= .01:
            out.append(finding("uncertain", "soft texture", "%s %s" % (subject, region), "%.0f %% of this region's surface is low on detail" % (r["smeared"] * 100),
                               measured=r["smeared"], threshold=.2))
        if r["blurred"] >= .4 and r["areaShare"] >= .01:
            out.append(finding("likely", "blurred texture (too few texels)", "%s %s" % (subject, region),
                               "%.0f %% of this region gets under a quarter of the body's usual texels per metre" % (r["blurred"] * 100),
                               measured=r["blurred"], threshold=.4, fix="give this area more room in the texture layout, or a bigger texture"))
    return out


def register(sp, ctx):
    p = sp.add_parser("smear", help="where a 3D model's texture is smeared or blurred")
    p.add_argument("model"); p.add_argument("--texture"); p.add_argument("--run")
    p.set_defaults(fn=lambda a: cmd(a, ctx))


def cmd(a, ctx):
    prj = ctx["project"](a)
    stem = os.path.splitext(os.path.basename(a.model))[0]
    out = ctx["run_dir"](a.run, "smear_" + stem, prj)
    rep = run(a.model, os.path.join(out, "evidence"), a.texture)
    res = findings(rep)
    lines = ["%-28s %6s %8s %8s" % ("region (bone, facing)", "area%", "smeared", "blurred")]
    for k, r in sorted(rep["regions"].items(), key=lambda kv: -kv[1]["smeared"]):
        lines.append("%-28s %6.1f %7.0f%% %7.0f%%" % (k, r["areaShare"] * 100, r["smeared"] * 100, r["blurred"] * 100))
    page, data = ctx["report"].write(out, "Smear: %s (%.0f %% smeared, %.0f %% blurred)" % (stem, rep["smearedShare"] * 100, rep["blurredShare"] * 100), res,
                                     sections=[("The model from four sides (red: smeared; blue: too few texels)", rep.get("pictures", []), None),
                                               ("Regions", [], "\n".join(lines))], meta={"texture": rep["texture"], "size": rep["size"]})
    return page, data
