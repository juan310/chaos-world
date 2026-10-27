"""Foot-slide fixer, the Blender side (run by eyeslib/footslide.py, and imported by a game's own model build: the Folklore
Hunter game's Tools/slim/build_slim.py --footlock).

  blender -b --python footslide_blender.py -- <model .fbx|.glb> <out dir> [--clips a,b] [--fix <out.fbx>]

A walk, jog or run made to play in place has its planted foot sweep back under the body at the speed the body is meant
to travel. Measured per clip: each foot is planted while it is within 3.5 cm of its lowest point; the planted feet's
own velocity (the ground's, as the clip sees it) gives the clip's travel speed and direction; what is left over (a
planted foot moving at any other speed, sideways, or bobbing) is the slide a player sees, as a share of the speed.
The fix (lock): each planted spell's foot is held on one point of the moving ground (the average of where the clip put
it), eased in and out over two frames, with an analytic two-bone IK on the thigh and shin keeping the knee in its own
plane and the foot's own angle; seamless loops stay seamless. Writes <out>/footslide.json (and the fixed model)."""
import bpy, sys, json, math, re
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion
sys.path.insert(0, str(Path(__file__).resolve().parent))
import rigmap

LOCOMOTION = re.compile(r"walk|jog|run|sprint|gait|trot|gallop|strafe|locomot", re.I)
NOT_LOCOMOTION = re.compile(r"^(Ride_|Sit_)|attack|slash|shoot|idle", re.I)
PLANT = .035
BLEND = 2


def legs(arm):
    R = rigmap.blender_roles(arm)
    return [(s, R["upperleg." + s], R["lowerleg." + s], R["foot." + s]) for s in "lr" if all(R.get(k + "." + s) for k in ("upperleg", "lowerleg", "foot"))]


def use(arm, act):
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    try:
        if act.slots: ad.action_slot = act.slots[0]
    except AttributeError:
        pass


def span(act):
    return int(round(act.frame_range[0])), int(round(act.frame_range[1]))


def sample(arm, act, lg):
    use(arm, act); sc = bpy.context.scene; f0, f1 = span(act); mw = arm.matrix_world
    tr = {s: [] for s, *_ in lg}
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        for s, _, _, ft in lg:
            tr[s].append((mw @ arm.pose.bones[ft].head).copy())
    return f0, f1, tr


def planted(track):
    lo = min(p.z for p in track)
    return [p.z <= lo + PLANT for p in track]


def _median(v):
    v = sorted(v)
    return v[len(v) // 2] if v else 0.0


def analyse(tr, fps):
    vels = []; contact = []
    for s, track in tr.items():
        pl = planted(track); contact.append(sum(pl) / len(pl))
        for i in range(len(track) - 1):
            if pl[i] and pl[i + 1]:
                d = (track[i + 1] - track[i]) * fps; d.z = 0
                vels.append(d)
    if len(vels) < 4:
        return {"speed": 0.0, "contact": sum(contact) / max(1, len(contact)), "plants": len(vels)}
    g = Vector((_median([v.x for v in vels]), _median([v.y for v in vels]), 0))
    sp = g.length
    cons = sum((v.normalized().dot(g.normalized()) if v.length > 1e-4 and sp > 1e-4 else 0) for v in vels) / len(vels)
    resid = _median([(v - g).length for v in vels])
    # the model faces -Y: travel is the opposite of the planted feet's motion
    ang = math.degrees(math.atan2(-g.x, g.y)) if sp > 1e-4 else 0.0     # 0 = straight ahead
    return {"speed": sp, "ground": [g.x, g.y], "direction": ang, "consistency": cons, "residual": resid, "residualShare": resid / max(1e-4, sp),
            "contact": sum(contact) / len(contact), "plants": len(vels)}


def world_tracks(tr, g, fps):
    """each foot's path on the ground the body travels over (in place + travel), for the charts"""
    T = -Vector((g[0], g[1], 0))
    return {s: [[round(i / fps, 4), round((p + T * (i / fps)).x, 4), round((p + T * (i / fps)).y, 4), round(p.z, 4)] for i, p in enumerate(track)] for s, track in tr.items()}


def _phases(pl, loop):
    n = len(pl)
    if all(pl): return [list(range(n))]
    start = next(i for i in range(n) if not pl[i]) if loop else 0
    order = [(start + k) % n for k in range(n)] if loop else list(range(n))
    out, cur = [], []
    for k, i in enumerate(order):
        if pl[i]: cur.append((k, i))
        elif cur: out.append(cur); cur = []
    if cur: out.append(cur)
    return [[(k, i) for k, i in ph] for ph in out if len(ph) >= 3]


def _basis(arm, name, M, Mparent):
    b = arm.data.bones[name]
    if b.parent:
        return (b.parent.matrix_local.inverted() @ b.matrix_local).inverted() @ Mparent.inverted() @ M
    return b.matrix_local.inverted() @ M


def solve(hip, knee, ank, T):
    a, b = (knee - hip).length, (ank - knee).length
    dv = T - hip; d = min(max(dv.length, abs(a - b) + 1e-4), a + b - 1e-4)
    u = dv.normalized() if dv.length > 1e-6 else (ank - hip).normalized()
    n = (knee - hip).cross(ank - hip)
    if n.length < 1e-7: n = (knee - hip).orthogonal()
    n.normalize()
    al = math.acos(max(-1, min(1, (a * a + d * d - b * b) / (2 * a * d))))
    c1 = hip + (Quaternion(n, al) @ u) * a; c2 = hip + (Quaternion(n, -al) @ u) * a
    k2 = c1 if (c1 - knee).length <= (c2 - knee).length else c2
    return k2, hip + u * d


def lock(arm, act, lg, g, fps):
    """hold each planted foot on the travelling ground; returns the number of plants locked"""
    f0, f1, tr = sample(arm, act, lg)
    n = f1 - f0 + 1
    loop = all((t[0] - t[-1]).length < .002 for t in tr.values())
    M = n - 1 if loop else n
    gv = Vector((g[0], g[1], 0))
    targets = {}
    locked = 0
    for s, th, sh, ft in lg:
        track = tr[s][:M]; pl = planted(track)
        for ph in _phases(pl, loop):
            t = [k / fps for k, _ in ph]
            base = sum(((track[i] - gv * tk) for (k, i), tk in zip(ph, t)), Vector()) / len(ph)
            L = len(ph)
            for j, ((k, i), tk) in enumerate(zip(ph, t)):
                w = min(1.0, (j + 1) / (BLEND + 1), (L - j) / (BLEND + 1))
                targets.setdefault(i, {})[s] = (base + gv * tk, w)
            locked += 1
    sc = bpy.context.scene; mwi = arm.matrix_world.inverted(); pbs = arm.pose.bones
    use(arm, act)
    for i in sorted(targets):
        sc.frame_set(f0 + i)
        for s, th, sh, ft in lg:
            if s not in targets[i]: continue
            tw, w = targets[i][s]
            Mth, Msh, Mft = pbs[th].matrix.copy(), pbs[sh].matrix.copy(), pbs[ft].matrix.copy()
            hip, knee, ank = Mth.translation.copy(), Msh.translation.copy(), Mft.translation.copy()
            T = ank.lerp(mwi @ tw, w)
            k2, T = solve(hip, knee, ank, T)
            R1 = (knee - hip).rotation_difference(k2 - hip)
            Mth2 = Matrix.Translation(hip) @ R1.to_matrix().to_4x4() @ Matrix.Translation(-hip) @ Mth
            R2 = (R1 @ (ank - knee)).rotation_difference(T - k2)
            Msh2 = Matrix.Translation(k2) @ (R2 @ R1).to_matrix().to_4x4() @ Matrix.Translation(-knee) @ Msh
            Mft2 = Matrix.Translation(T - ank) @ Mft
            par = pbs[th].parent.matrix.copy() if pbs[th].parent else Matrix()
            frames = [f0 + i] + ([f1] if loop and i == 0 else [])
            for f in frames:
                for name, Mx, Mp in ((th, Mth2, par), (sh, Msh2, Mth2), (ft, Mft2, Msh2)):
                    pb = pbs[name]
                    _key_at(pb, _basis(arm, name, Mx, Mp), f)
    return locked


def _key_at(pb, basis, f):
    loc, q, _ = basis.decompose()
    if pb.rotation_mode == "QUATERNION":
        if q.dot(pb.rotation_quaternion) < 0: q.negate()
        pb.rotation_quaternion = q; pb.keyframe_insert("rotation_quaternion", frame=f)
    elif pb.rotation_mode == "AXIS_ANGLE":
        ax, an = q.to_axis_angle(); pb.rotation_axis_angle = (an, ax.x, ax.y, ax.z); pb.keyframe_insert("rotation_axis_angle", frame=f)
    else:
        pb.rotation_euler = q.to_euler(pb.rotation_mode, pb.rotation_euler); pb.keyframe_insert("rotation_euler", frame=f)
    if not pb.bone.use_connect:
        pb.location = loc; pb.keyframe_insert("location", frame=f)


def candidates(names=None):
    acts = [a for a in bpy.data.actions if (a.name in names if names else (LOCOMOTION.search(a.name) and not NOT_LOCOMOTION.search(a.name)))]
    return acts


def run(arm, names=None, fix=False, fps=None, log=print):
    fps = fps or bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
    lg = legs(arm)
    if len(lg) < 2: raise RuntimeError("no legs found on " + arm.name)
    out = {}
    for act in candidates(names):
        f0, f1, tr = sample(arm, act, lg)
        r = analyse(tr, fps)
        rec = {"before": r}
        if r["speed"] > 0.3:
            rec["tracksBefore"] = world_tracks(tr, r["ground"], fps)
        if fix and r["speed"] > 0.3 and r.get("consistency", 0) > .6:
            rec["locked"] = lock(arm, act, lg, r["ground"], fps)
            f0, f1, tr = sample(arm, act, lg)
            rec["after"] = analyse(tr, fps)
            rec["tracksAfter"] = world_tracks(tr, r["ground"], fps)
            log("FOOTSLIDE %s: %.2f m/s, slide %.0f %% -> %.0f %% (%d plants locked)" % (act.name, r["speed"], r["residualShare"] * 100, rec["after"]["residualShare"] * 100, rec["locked"]))
        else:
            log("FOOTSLIDE %s: %.2f m/s, slide %s" % (act.name, r["speed"], ("%.0f %%" % (r["residualShare"] * 100)) if "residualShare" in r else "-"))
        out[act.name] = rec
    if arm.animation_data: arm.animation_data.action = None
    return out


if __name__ == "__main__" and "--" in sys.argv and Path(sys.argv[sys.argv.index("--") + 1]).suffix.lower() in (".fbx", ".glb", ".gltf"):
    argv = sys.argv[sys.argv.index("--") + 1:]
    model, outd = Path(argv[0]), Path(argv[1]); outd.mkdir(parents=True, exist_ok=True)
    clips = argv[argv.index("--clips") + 1].split(",") if "--clips" in argv else None
    fixto = argv[argv.index("--fix") + 1] if "--fix" in argv else None
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if model.suffix.lower() in (".glb", ".gltf"): bpy.ops.import_scene.gltf(filepath=str(model))
    else: bpy.ops.import_scene.fbx(filepath=str(model))
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    for a in bpy.data.actions: a.name = a.name.split("|")[-1]
    res = run(arm, clips, fix=bool(fixto))
    if fixto:
        for pb in arm.pose.bones: pb.matrix_basis = Matrix()
        bpy.ops.export_scene.fbx(filepath=fixto, use_selection=False, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                                 apply_scale_options="FBX_SCALE_ALL", bake_anim=True, bake_anim_use_nla_strips=False,
                                 bake_anim_use_all_actions=True, bake_anim_simplify_factor=0, axis_forward="-Z", axis_up="Y",
                                 path_mode="COPY", embed_textures=False)
    (outd / "footslide.json").write_text(json.dumps({"model": str(model), "fixed": fixto, "legs": legs(arm), "clips": res}, indent=1))
    print("FOOTSLIDE_DONE", len(res), "clips", flush=True)
