# The Blender side of the MotionBricks bridge (NVIDIA MotionBricks, run locally on the RTX 5080; code Apache-2.0, model
# under the NVIDIA Open Model Agreement; its sample clips are evaluation-only and are never used here).
#
# MotionBricks generates natural human motion between poses on its own SOMA skeleton. We give it only our own poses:
#   export  the slim hunter's keyposed clips (Tools/slim/moves.py) are retargeted onto SOMA, frame by frame, with the
#           times of their key poses  ->  export.json
#   (mb_generate.py, in the MotionBricks environment, fills in the motion between those key poses and re-times it so
#    every key pose lands at its own time  ->  mb_out.json)
#   load    (build_slim.py --mb mb_out.json) the generated SOMA motion is put on a SOMA armature and retargeted back
#           onto the slim skeleton by Tools/slim/retarget.py, replacing the keyposed clip of the same name.
import bpy, json, math
from mathutils import Vector, Quaternion, Matrix
import choreo, retarget

# Mesh2Motion names (what retarget.Source reads) -> SOMA joints
SOMA_NAMES = {'pelvis': 'Hips', 'spine_01': 'Spine1', 'spine_03': 'Chest', 'neck_01': 'Neck1', 'head': 'Head'}
for s, S in (('l', 'Left'), ('r', 'Right')):
    SOMA_NAMES.update({'clavicle_' + s: S + 'Shoulder', 'upperarm_' + s: S + 'Arm', 'lowerarm_' + s: S + 'ForeArm', 'hand_' + s: S + 'Hand',
                       'middle_01_' + s: S + 'HandMiddle1', 'index_01_' + s: S + 'HandIndex1', 'pinky_01_' + s: S + 'HandPinky1',
                       'thigh_' + s: S + 'Leg', 'calf_' + s: S + 'Shin', 'foot_' + s: S + 'Foot', 'ball_' + s: S + 'ToeBase'})

# the keyposed clips that go through MotionBricks (the loops and the captured clips do not)
MB_CLIPS = ['SnS_Rush_1', 'SnS_Rush_2', 'SnS_Leap_Slash', 'SnS_Shield_Crash', 'SnS_Rend', '1H_Melee_Attack_Stab', 'Block_Hit',
            'Hunt_JumpDash_L', 'Hunt_JumpDash_R', 'Dodge_Left', 'Dodge_Right', 'Jump_Full_Short', 'Lie_Down',
            '2H_Melee_Attack_Chop', '2H_Melee_Attack_Slice', '2H_Melee_Attack_Stab', '2H_Melee_Attack_Spin', '2H_Melee_Attack_Spinning', 'Katana_Rend',
            'Lance_High_Thrust', 'Lance_Rend', 'Bow_Power_Shoot', 'Bow_Arc_Shoot', 'Bow_Piercer', 'Bow_Tracer',
            'Katana_Iai', 'Hunt_Clash', 'Bow_Step_L', 'Bow_Step_R', '2H_Melee_Idle', 'Lance_Charge', 'Bow_Aim_Walk', 'Ride_Idle',
            'Bow_Draw', 'Bow_Shoot']
LOOPS = {'2H_Melee_Idle', 'Lance_Charge', 'Bow_Aim_Walk', 'Ride_Idle', 'Bow_Draw'}   # their last key pose is their first: they stay seamless
MIN_GAP = .25                                   # key poses closer than this are merged into one target


def q7(q):           # mathutils (w, x, y, z) -> warp (x, y, z, w)
    return [q.x, q.y, q.z, q.w]


def from7(a):
    return Vector(a[:3]), Quaternion((a[6], a[3], a[4], a[5]))


class Soma:
    """The SOMA skeleton from mb_skeleton.py's JSON. Model space is Y up; Blender's is Z up, the character facing -Y."""
    def __init__(self, path):
        d = json.load(open(path))
        self.names, self.parents = d['names'], d['parents']
        self.idx = {n: i for i, n in enumerate(self.names)}
        self.rest = [from7(a) for a in d['rest']]
        # model -> Blender: Y up becomes Z up; turned to face -Y
        C = Quaternion((1, 0, 0), math.radians(90))
        gp, gr = self.fk_rest()
        toe = C @ (gp[self.idx['LeftToeEnd']] - gp[self.idx['LeftFoot']])
        if toe.y > 0: C = Quaternion((0, 0, 1), math.pi) @ C
        self.C = C
        self.gp = [C @ p for p in gp]                        # rest, Blender space
        self.gr = [C @ r for r in gr]
        L = lambda a, b: (self.gp[self.idx[b]] - self.gp[self.idx[a]]).length
        self.leg = L('LeftLeg', 'LeftShin') + L('LeftShin', 'LeftFoot')

    def fk_rest(self):
        gp, gr = [None] * len(self.names), [None] * len(self.names)
        for i, (p, r) in enumerate(self.rest):
            j = self.parents[i]
            if j < 0: gp[i], gr[i] = p.copy(), r.copy()
            else: gp[i], gr[i] = gp[j] + gr[j] @ p, gr[j] @ r
        return gp, gr

    # ---- slim pose -> SOMA local transforms -------------------------------------------------------------------
    def from_slim(self, rig, W):
        """W: the slim rig's armature-space matrices for one frame."""
        I = self.idx
        R = {}                                              # SOMA world rotations (Blender space) that are driven
        def delta(b): return W[b].to_quaternion() @ rig.R0[b].inverted()
        torso = {'Hips': 'hips', 'Spine1': 'spine', 'Chest': 'chest', 'Neck1': 'neck', 'Neck2': 'neck', 'Head': 'head'}
        for j, b in torso.items(): R[j] = delta(b) @ self.gr[I[j]]
        R['Spine2'] = delta('spine').slerp(delta('chest'), .5) @ self.gr[I['Spine2']]
        for s, S in (('l', 'Left'), ('r', 'Right')):
            R[S + 'Shoulder'] = delta('shoulder.' + s) @ self.gr[I[S + 'Shoulder']]
            for j, c, b, bc in ((S + 'Arm', S + 'ForeArm', 'upperarm.' + s, 'lowerarm.' + s), (S + 'ForeArm', S + 'Hand', 'lowerarm.' + s, 'hand.' + s),
                                (S + 'Leg', S + 'Shin', 'upperleg.' + s, 'lowerleg.' + s), (S + 'Shin', S + 'Foot', 'lowerleg.' + s, 'foot.' + s)):
                q = delta(b)
                d0 = q @ (self.gp[I[c]] - self.gp[I[j]])
                d1 = W[bc].translation - W[b].translation
                R[j] = d0.rotation_difference(d1) @ q @ self.gr[I[j]]
            # the hand by its anatomy: the thumb side (the slot's blade axis) and the fingers (the hand bone)
            fingers = W['hand.' + s].to_quaternion() @ Vector((0, 1, 0))
            thumb = W['handslot.' + s].to_quaternion() @ Vector((0, 1, 0))
            f0 = self.gp[I[S + 'HandMiddle1']] - self.gp[I[S + 'Hand']]
            r0 = self.gp[I[S + 'HandIndex1']] - self.gp[I[S + 'HandPinky1']]
            R[S + 'Hand'] = choreo.frame(thumb, fingers) @ choreo.frame(r0, f0).inverted() @ self.gr[I[S + 'Hand']]
            R[S + 'Foot'] = delta('foot.' + s) @ self.gr[I[S + 'Foot']]
            R[S + 'ToeBase'] = delta('toes.' + s) @ self.gr[I[S + 'ToeBase']]
        hips = self.gp[I['Hips']] + (W['hips'].translation - rig.h0['hips']) * (self.leg / rig.leg_len)
        Ci = self.C.inverted()
        gm = [None] * len(self.names)                       # world rotations, model space
        out = []
        for i, n in enumerate(self.names):
            j = self.parents[i]
            if n in R: gm[i] = Ci @ R[n]
            else: gm[i] = (gm[j] if j >= 0 else Quaternion()) @ self.rest[i][1]
            if j < 0: out.append(list(Ci @ hips) + q7(gm[i]))
            else: out.append(list(self.rest[i][0]) + q7(gm[j].inverted() @ gm[i]))
        return out

    # ---- SOMA local transforms -> an action on a SOMA armature in Blender -------------------------------------
    def armature(self):
        if 'SOMA' in bpy.data.objects: return bpy.data.objects['SOMA']
        ad = bpy.data.armatures.new('SOMA'); arm = bpy.data.objects.new('SOMA', ad)
        bpy.context.scene.collection.objects.link(arm)
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.mode_set(mode='EDIT')
        eb = []
        for i, n in enumerate(self.names):
            b = ad.edit_bones.new(n); b.head = self.gp[i]
            kids = [k for k, p in enumerate(self.parents) if p == i]
            tail = self.gp[kids[0]] if kids and (self.gp[kids[0]] - self.gp[i]).length > 1e-3 else None
            if tail is None:
                j = self.parents[i]
                d = (self.gp[i] - self.gp[j]).normalized() if j >= 0 else Vector((0, 0, 1))
                tail = self.gp[i] + d * .04
            b.tail = tail
            eb.append(b)
        for i, b in enumerate(eb):
            if self.parents[i] >= 0: b.parent = eb[self.parents[i]]
        bpy.ops.object.mode_set(mode='OBJECT')
        return arm

    def action(self, arm, name, frames):
        act = bpy.data.actions.new('mb:' + name)
        ad = arm.animation_data or arm.animation_data_create(); ad.action = act
        bones = arm.data.bones
        rest = {b.name: b.matrix_local.copy() for b in bones}
        for f, fr in enumerate(frames):
            gp, gr = [None] * len(self.names), [None] * len(self.names)
            for i, a in enumerate(fr):
                p, r = from7(a); j = self.parents[i]
                if j < 0: gp[i], gr[i] = p, r
                else: gp[i], gr[i] = gp[j] + gr[j] @ p, gr[j] @ r
            M = {}
            for i, n in enumerate(self.names):
                rb = self.C @ gr[i] @ (self.C @ self.fk_rest_rot(i)).inverted() @ rest[n].to_quaternion()
                m = rb.to_matrix().to_4x4(); m.translation = self.C @ gp[i]
                M[n] = m
            for i, n in enumerate(self.names):
                j = self.parents[i]; pb = arm.pose.bones[n]; pb.rotation_mode = 'QUATERNION'
                if j < 0: basis = rest[n].inverted() @ M[n]
                else:
                    pn = self.names[j]
                    basis = (rest[pn].inverted() @ rest[n]).inverted() @ M[pn].inverted() @ M[n]
                loc, q, _ = basis.decompose()
                pb.location = loc; pb.rotation_quaternion = q
                pb.keyframe_insert('location', frame=1 + f); pb.keyframe_insert('rotation_quaternion', frame=1 + f)
        ad.action = None
        return act

    def fk_rest_rot(self, i):
        if not hasattr(self, '_grm'): self._grm = self.fk_rest()[1]
        return self._grm[i]


# ---- export: record the key times while the keyposed clips are baked ------------------------------------------
_keys = {}
_bake = choreo.bake


def _recording_bake(rig, name, keys, base, loop=False, length=None, post=None):
    _keys[name] = [t for t, _ in keys]
    return _bake(rig, name, keys, base, loop, length, post)


def record_keys():
    choreo.bake = _recording_bake


def key_times(name, length):
    ts = sorted(set(t for t in _keys.get(name, []) if 0 <= t <= length))
    out = []
    for t in ts:
        if not out or t - out[-1] >= MIN_GAP: out.append(t)
    if not out: return []                       # not made from key poses (a sampled function): nothing to generate between
    if length - out[-1] > 1e-3:
        if length - out[-1] < MIN_GAP and len(out) > 1: out[-1] = length
        else: out.append(length)
    return out


def export(rig, soma, names, path):
    clips = []
    for nm in names:
        act = bpy.data.actions.get(nm)
        if not act: print('MB_SKIP', nm); continue
        rig.arm.animation_data.action = act
        try:
            if act.slots: rig.arm.animation_data.action_slot = act.slots[0]
        except AttributeError: pass
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        frames = []
        for f in range(f0, f1 + 1):
            bpy.context.scene.frame_set(f)
            W = {b.name: b.matrix.copy() for b in rig.arm.pose.bones}
            frames.append(soma.from_slim(rig, W))
        length = (f1 - f0) / choreo.FPS
        keys = key_times(nm, length)
        if len(keys) < 2: print('MB_SKIP', nm, 'has no key poses'); continue
        clips.append({'name': nm, 'fps': choreo.FPS, 'keys': keys, 'frames': frames})
        print('MB_EXPORT', nm, len(frames), 'frames, keys', clips[-1]['keys'], flush=True)
    rig.arm.animation_data.action = None
    json.dump({'clips': clips}, open(path, 'w'))


def load(rig, soma, path, pins=()):
    """The generated clips (mb_out.json) onto the slim rig, replacing the clips of the same name."""
    data = json.load(open(path))
    arm = soma.armature()
    src = retarget.Source(names=SOMA_NAMES)
    for c in data['clips']:
        src.clips[c['name']] = (arm, soma.action(arm, c['name'], c['frames']))
    src.prepare(arm)
    made = []
    for c in data['clips']:
        old = bpy.data.actions.get(c['name'])
        if old: bpy.data.actions.remove(old)
        made.append(src.clip(rig, c['name'], c['name'], loop=c['name'] in LOOPS, pin=c['name'] in pins))
        print('MB_CLIP', c['name'], flush=True)
    for a in [a for a in bpy.data.actions if a.name.startswith('mb:')]: bpy.data.actions.remove(a)
    bpy.data.objects.remove(arm)
    return made
