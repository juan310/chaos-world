"""MotionBricks in SightForge, the Blender side (run by eyeslib/motionbricks.py). Any humanoid rig (bone roles from
rigmap.py), NVIDIA MotionBricks run locally (code Apache-2.0, model under the NVIDIA Open Model Agreement; its sample
clips are never used: only the user's own poses go in).

  blender -b --python motionbricks_blender.py -- export <model> <soma.json> <export.json> --clips a,b [--keys auto|a=0,0.4,0.9;b=...]
  blender -b --python motionbricks_blender.py -- transition <model> <soma.json> <export.json> --from clip@sec --to clip@sec --seconds s --name N
  blender -b --python motionbricks_blender.py -- load <model> <soma.json> <export.json> <mb_out.json> <out.fbx> <poses.json>

export      each clip, frame by frame, as SOMA local transforms, with the times of its key poses: the keyframes it was
            made from, or (a clip baked on every frame) the moments the body holds still, plus its ends
transition  a new clip: the four poses up to a moment of one clip, the four from a moment of another, the time between
            left for MotionBricks to fill
load        the generated motion put back on the rig (torso, head, hands and feet turned as SOMA's; arm and leg bones
            aimed along SOMA's limbs; the hips moved by SOMA's, scaled by leg length; every other bone keeps its own
            animation), the clip replaced (or added), seamless loops kept seamless, the model written with every clip;
            the role joints' positions before and after go to poses.json for the stick-figure strips and the smoothness
            numbers."""
import bpy, sys, json, math
from pathlib import Path
from mathutils import Vector, Quaternion, Matrix
sys.path.insert(0, str(Path(__file__).resolve().parent))
import rigmap

MIN_GAP = .25
# SOMA joint -> (rig role, the role it aims at or None)
SOMA = {"Hips": ("hips", None), "Spine1": ("spine", None), "Chest": ("chest", None), "Neck1": ("neck", None), "Neck2": ("neck", None), "Head": ("head", None)}
for s, S in (("l", "Left"), ("r", "Right")):
    SOMA.update({S + "Shoulder": ("shoulder." + s, None), S + "Arm": ("upperarm." + s, "lowerarm." + s), S + "ForeArm": ("lowerarm." + s, "hand." + s),
                 S + "Hand": ("hand." + s, None), S + "Leg": ("upperleg." + s, "lowerleg." + s), S + "Shin": ("lowerleg." + s, "foot." + s),
                 S + "Foot": ("foot." + s, None), S + "ToeBase": ("toes." + s, None)})
SOMA_CHILD = {"LeftArm": "LeftForeArm", "LeftForeArm": "LeftHand", "LeftLeg": "LeftShin", "LeftShin": "LeftFoot",
              "RightArm": "RightForeArm", "RightForeArm": "RightHand", "RightLeg": "RightShin", "RightShin": "RightFoot"}


def q7(q): return [q.x, q.y, q.z, q.w]
def from7(a): return Vector(a[:3]), Quaternion((a[6], a[3], a[4], a[5]))


class Soma:
    def __init__(self, path):
        d = json.load(open(path))
        self.names, self.parents = d["names"], d["parents"]
        self.idx = {n: i for i, n in enumerate(self.names)}
        self.rest = [from7(a) for a in d["rest"]]
        C = Quaternion((1, 0, 0), math.radians(90))
        gp, gr = self.fk(self.rest)
        toe = C @ (gp[self.idx["LeftToeEnd" if "LeftToeEnd" in self.idx else "LeftToeBase"]] - gp[self.idx["LeftFoot"]])
        if toe.y > 0: C = Quaternion((0, 0, 1), math.pi) @ C
        self.C = C
        self.gp = [C @ p for p in gp]; self.gr = [C @ r for r in gr]
        L = lambda a, b: (self.gp[self.idx[b]] - self.gp[self.idx[a]]).length
        self.leg = L("LeftLeg", "LeftShin") + L("LeftShin", "LeftFoot")

    def fk(self, frame):
        gp, gr = [None] * len(self.names), [None] * len(self.names)
        for i, (p, r) in enumerate(frame):
            j = self.parents[i]
            if j < 0: gp[i], gr[i] = p.copy(), r.copy()
            else: gp[i], gr[i] = gp[j] + gr[j] @ p, gr[j] @ r
        return gp, gr


class Rig:
    """a humanoid armature seen in one frame "B": world space turned so the body faces -Y"""
    def __init__(self, arm):
        self.arm = arm; self.R = rigmap.blender_roles(arm)
        need = ("hips", "chest", "head", "upperleg.l", "lowerleg.l", "foot.l", "upperarm.l", "lowerarm.l", "hand.l")
        miss = [r for r in need if r not in self.R]
        if miss: raise RuntimeError("not a humanoid rig I can read (missing %s)" % ", ".join(miss))
        mw = arm.matrix_world
        b = arm.data.bones
        fwd = (mw @ b[self.R["toes.l"]].head_local - mw @ b[self.R["foot.l"]].head_local) if "toes.l" in self.R else Vector((0, -1, 0))
        fwd.z = 0
        yaw = math.atan2(fwd.x, -fwd.y) if fwd.length > 1e-4 else 0.0
        self.F = Matrix.Rotation(yaw, 4, "Z")                # turns the rig's facing onto -Y
        self.BA = self.F @ mw; self.AB = self.BA.inverted()  # armature space <-> B
        self.rest = {x.name: x.matrix_local.copy() for x in b}
        self.R0 = {r: (self.BA @ self.rest[n]).to_quaternion() for r, n in self.R.items()}
        self.h0 = {r: (self.BA @ self.rest[n]).translation for r, n in self.R.items()}
        L = lambda a, c: (self.h0[c] - self.h0[a]).length
        self.leg = L("upperleg.l", "lowerleg.l") + L("lowerleg.l", "foot.l")
        self.order = [x.name for x in sorted(b, key=lambda x: len(x.parent_recursive))]

    def pose_B(self):
        """this frame: role -> matrix in B"""
        pbs = self.arm.pose.bones
        return {r: self.BA @ pbs[n].matrix for r, n in self.R.items()}


def to_soma(rig, soma, W):
    I = soma.idx; R = {}
    def delta(r): return W[r].to_quaternion() @ rig.R0[r].inverted()
    for j, (r, aim) in SOMA.items():
        if j not in I: continue
        if r not in W:
            if r == "spine": q = delta("hips").slerp(delta("chest"), .5)
            elif r == "neck": q = delta("chest").slerp(delta("head"), .5)
            elif r.startswith("shoulder"): q = delta("chest")
            elif r.startswith("toes"): q = delta("foot" + r[4:])
            else: continue
            R[j] = q @ soma.gr[I[j]]; continue
        q = delta(r)
        if aim and aim in W and SOMA_CHILD.get(j) in I:
            d0 = q @ (soma.gp[I[SOMA_CHILD[j]]] - soma.gp[I[j]])
            d1 = W[aim].translation - W[r].translation
            R[j] = d0.rotation_difference(d1) @ q @ soma.gr[I[j]]
        else:
            R[j] = q @ soma.gr[I[j]]
    if "Spine2" in I and "Spine1" in R and "Chest" in R:
        R["Spine2"] = (R["Spine1"] @ soma.gr[I["Spine1"]].inverted()).slerp(R["Chest"] @ soma.gr[I["Chest"]].inverted(), .5) @ soma.gr[I["Spine2"]]
    hips = soma.gp[I["Hips"]] + (W["hips"].translation - rig.h0["hips"]) * (soma.leg / rig.leg)
    Ci = soma.C.inverted(); gm = [None] * len(soma.names); out = []
    for i, n in enumerate(soma.names):
        j = soma.parents[i]
        gm[i] = Ci @ R[n] if n in R else (gm[j] if j >= 0 else Quaternion()) @ soma.rest[i][1]
        if j < 0: out.append(list(Ci @ hips) + q7(gm[i]))
        else: out.append(list(soma.rest[i][0]) + q7(gm[j].inverted() @ gm[i]))
    return out


def use(arm, act):
    ad = arm.animation_data or arm.animation_data_create(); ad.action = act
    try:
        if act.slots: ad.action_slot = act.slots[0]
    except AttributeError:
        pass


def span(act): return int(round(act.frame_range[0])), int(round(act.frame_range[1]))


def frames_of(rig, soma, act):
    use(rig.arm, act); f0, f1 = span(act); sc = bpy.context.scene; out = []; pos = []
    for f in range(f0, f1 + 1):
        sc.frame_set(f); W = rig.pose_B()
        out.append(to_soma(rig, soma, W)); pos.append({r: list(m.translation) for r, m in W.items()})
    return out, pos


def key_times(act, pos, fps):
    f0, f1 = span(act); n = f1 - f0 + 1
    keyed = sorted(set(int(round(k.co.x)) for fc in getattr(act, "fcurves", []) for k in fc.keyframe_points))
    try:   # Blender 4.4+ layered actions
        if not keyed:
            for layer in act.layers:
                for strip in layer.strips:
                    for bag in strip.channelbags:
                        for fc in bag.fcurves: keyed += [int(round(k.co.x)) for k in fc.keyframe_points]
            keyed = sorted(set(keyed))
    except AttributeError:
        pass
    if keyed and len(keyed) < .6 * n:
        ts = [(f - f0) / fps for f in keyed]
    else:
        # baked on every frame: the moments the body holds still (speed minima), and the ends
        sp = [sum((Vector(pos[i + 1][r]) - Vector(pos[i][r])).length for r in pos[i]) for i in range(n - 1)]
        ts = [0.0] + [i / fps for i in range(1, n - 2) if sp[i] <= sp[i - 1] and sp[i] <= sp[i + 1]] + [(n - 1) / fps]
    out = []
    for t in ts:
        if not out or t - out[-1] >= MIN_GAP: out.append(t)
    L = (n - 1) / fps
    if out and L - out[-1] > 1e-3:
        if L - out[-1] < MIN_GAP and len(out) > 1: out[-1] = L
        else: out.append(L)
    return out


def apply(rig, soma, act, frames, base_act=None):
    """generated SOMA frames onto the rig as `act` (keys replaced); bones without a role keep base_act's animation"""
    arm = rig.arm; pbs = arm.pose.bones; I = soma.idx; sc = bpy.context.scene
    names = {n: r for r, n in rig.R.items()}
    for i, fr in enumerate(frames):
        f = 1 + i
        base = {}
        if base_act:
            use(arm, base_act); bf0, bf1 = span(base_act); sc.frame_set(min(bf1, bf0 + i))
            base = {pb.name: pb.matrix_basis.copy() for pb in pbs}
        use(arm, act)
        gp, gr = soma.fk([from7(a) for a in fr])
        P = [soma.C @ p for p in gp]; Q = [soma.C @ q for q in gr]
        delta = {j: Q[I[j]] @ soma.gr[I[j]].inverted() for j in SOMA if j in I}
        RB = {}
        for j, (r, aim) in SOMA.items():
            if j not in I or r not in rig.R or r in RB and j in ("Neck2",): continue
            q = delta[j]
            if aim and aim in rig.R and SOMA_CHILD.get(j) in I:
                d0 = q @ (rig.h0[aim] - rig.h0[r]); d1 = P[I[SOMA_CHILD[j]]] - P[I[j]]
                RB[r] = d0.rotation_difference(d1) @ q @ rig.R0[r]
            else:
                RB[r] = q @ rig.R0[r]
        hipB = rig.h0["hips"] + (P[I["Hips"]] - soma.gp[I["Hips"]]) * (rig.leg / soma.leg)
        M = {}
        for n in rig.order:
            b = arm.data.bones[n]; par = b.parent.name if b.parent else None
            local = (rig.rest[par].inverted() @ rig.rest[n]) if par else rig.rest[n]
            Mp = M.get(par, Matrix()) if par else Matrix()
            if n in names and names[n] in RB:
                head = (rig.AB @ Matrix.Translation(hipB)).translation if names[n] == "hips" else (Mp @ local).translation
                rot = (rig.AB.to_quaternion() @ RB[names[n]]).to_matrix().to_4x4()
                M[n] = Matrix.Translation(head) @ rot
            else:
                M[n] = Mp @ local @ base.get(n, Matrix())
            basis = (local.inverted() @ Mp.inverted() @ M[n]) if par else rig.rest[n].inverted() @ M[n]
            pb = pbs[n]; loc, q, _ = basis.decompose()
            pb.rotation_mode = "QUATERNION"; pb.rotation_quaternion = q; pb.location = loc
            pb.keyframe_insert("rotation_quaternion", frame=f); pb.keyframe_insert("location", frame=f)


def positions(rig, act):
    use(rig.arm, act); f0, f1 = span(act); sc = bpy.context.scene; out = []
    for f in range(f0, f1 + 1):
        sc.frame_set(f); out.append({r: [round(x, 4) for x in m.translation] for r, m in rig.pose_B().items()})
    return out


def open_model(p):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if p.lower().endswith((".glb", ".gltf")): bpy.ops.import_scene.gltf(filepath=p)
    else: bpy.ops.import_scene.fbx(filepath=p)
    for a in bpy.data.actions: a.name = a.name.split("|")[-1]
    return next(o for o in bpy.data.objects if o.type == "ARMATURE")


def opt(argv, flag, default=None):
    return argv[argv.index(flag) + 1] if flag in argv else default


def main(argv):
    mode = argv[0]
    arm = open_model(argv[1]); soma = Soma(argv[2]); rig = Rig(arm)
    fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
    if mode == "export":
        names = opt(argv, "--clips").split(",")
        given = {}
        for part in (opt(argv, "--keys", "auto") or "auto").split(";"):
            if "=" in part:
                k, v = part.split("="); given[k] = [float(x) for x in v.split(",")]
        clips = []
        for nm in names:
            act = bpy.data.actions.get(nm)
            if not act: print("MB_SKIP", nm, "not in the model", flush=True); continue
            fr, pos = frames_of(rig, soma, act)
            keys = given.get(nm) or key_times(act, pos, fps)
            if len(keys) < 2: print("MB_SKIP", nm, "has no key poses", flush=True); continue
            loop = all((Vector(pos[0][r]) - Vector(pos[-1][r])).length < .003 for r in pos[0])
            clips.append({"name": nm, "fps": fps, "keys": keys, "frames": fr, "loop": loop, "base": nm})
            print("MB_EXPORT", nm, len(fr), "frames, keys", [round(k, 2) for k in keys], "loop" if loop else "", flush=True)
        json.dump({"clips": clips}, open(argv[3], "w"))
    elif mode == "transition":
        a, ta = opt(argv, "--from").split("@"); b, tb = opt(argv, "--to").split("@")
        secs = float(opt(argv, "--seconds", ".6")); name = opt(argv, "--name", "%s_to_%s" % (a, b))
        A, _ = frames_of(rig, soma, bpy.data.actions[a]); B, _ = frames_of(rig, soma, bpy.data.actions[b])
        ia = min(len(A) - 1, int(round(float(ta) * fps))); ib = min(len(B) - 1, int(round(float(tb) * fps)))
        ctx = [A[max(0, ia - 3 + k)] for k in range(4)]
        gap = max(2, int(round(secs * fps)))
        tgt = [B[min(len(B) - 1, ib + k)] for k in range(4)]
        frames = ctx + [ctx[-1]] * (gap - 1) + tgt
        json.dump({"clips": [{"name": name, "fps": fps, "keys": [3 / fps, (3 + gap) / fps], "frames": frames, "loop": False, "base": a, "trim": [3, 3 + gap + 1]}]}, open(argv[3], "w"))
        print("MB_EXPORT transition", name, "%s@%s -> %s@%s over %d frames" % (a, ta, b, tb, gap), flush=True)
    elif mode == "load":
        meta = {c["name"]: c for c in json.load(open(argv[3]))["clips"]}
        data = json.load(open(argv[4]))
        poses = {}
        for c in data["clips"]:
            m = meta.get(c["name"], {})
            frames = c["frames"]
            if m.get("trim"): frames = frames[m["trim"][0]:m["trim"][1]]
            if m.get("loop"): frames = frames[:-1] + [frames[0]]
            old = bpy.data.actions.get(c["name"])
            before = positions(rig, old) if old else None
            base = bpy.data.actions.get(m.get("base") or c["name"])
            keep = None
            if old:
                keep = old.copy(); keep.name = c["name"] + "__base"
                if base is old: base = keep
                bpy.data.actions.remove(old)
            act = bpy.data.actions.new(c["name"]); act.use_fake_user = True
            apply(rig, soma, act, frames, base)
            if keep: bpy.data.actions.remove(keep)
            poses[c["name"]] = {"before": before, "after": positions(rig, act), "fps": c.get("fps", fps)}
            print("MB_CLIP", c["name"], len(frames), "frames", flush=True)
        arm.animation_data.action = None
        for pb in arm.pose.bones: pb.matrix_basis = Matrix()
        bpy.ops.export_scene.fbx(filepath=argv[5], use_selection=False, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                                 apply_scale_options="FBX_SCALE_ALL", bake_anim=True, bake_anim_use_nla_strips=False,
                                 bake_anim_use_all_actions=True, bake_anim_simplify_factor=0, axis_forward="-Z", axis_up="Y",
                                 path_mode="COPY", embed_textures=False)
        json.dump(poses, open(argv[6], "w"))
        print("MB_LOADED", argv[5], flush=True)


if __name__ == "__main__" and "--" in sys.argv:
    main(sys.argv[sys.argv.index("--") + 1:])
