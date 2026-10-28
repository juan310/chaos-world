# Two-view capture, last step (Blender, inside build_slim): a captured motion (anymove's solve.py -> motion.json) made
# into a clip on the game skeleton, baked like the hand-made moves so it goes through the same body gate and the
# Proving Grounds.
#
# The capture's joints (every bone's head, the leaves' tails) are mapped onto the build's rest skeleton by the
# similarity that best lays the capture's rest joints on it (the capture was set up on the built game model, the
# build starts from the source model: the same bones, perhaps in another frame). Then every frame:
# - the hips turn so their up (to the spine) and their side (hip to hip) match the capture's, at the capture's pelvis;
# - every other bone rides on its parent and turns, by the smallest turn, to point at its captured end joint.
# Twist about a bone's own length is not captured (two views show where a bone points, hardly how it is turned).
#
#   import to_clip; to_clip.clip(rig, 'Troll_Roar', 'Tools/capture2v/creatures/troll/clips/roar.json')
import json
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector

import choreo

HERE = Path(__file__).resolve().parent


def similarity(a, b):
    """scale s, rotation R, translation t with s R a + t ~ b (Umeyama)"""
    ma, mb = a.mean(0), b.mean(0)
    A, B = a - ma, b - mb
    U, S, Vt = np.linalg.svd(B.T @ A / len(a))
    D = np.eye(3); D[2, 2] = np.sign(np.linalg.det(U @ Vt))
    R = U @ D @ Vt
    s = (S * np.diag(D)).sum() / (A ** 2).sum() * len(a)
    return s, R, mb - s * R @ ma


def frame_of(up, side):
    """an orthonormal frame (columns: side, forward, up) from an up and a side direction"""
    u = up.normalized(); f = u.cross(side).normalized(); s = f.cross(u)
    return Matrix((s, f, u)).transposed()


def clip(rig, name, path, loop=False, rig_info=None):
    m = json.load(open(path))
    info = json.load(open(rig_info or Path(path).with_name('rig_info.json')))
    names = m['joint_names']
    J = np.array(m['joints'], float)                       # frames x joints x 3, the capture's world
    bones = info['bones']
    kp = {k['name']: k for k in info['keypoints']}
    # the build's rest positions of the capture's joints
    def build_rest(j):
        b = bones[kp[j]['bone']]['name']
        return rig.h0[b] if kp[j]['end'] == 'head' else rig.h0[b] + rig.dir0[b] * rig.len[b]
    common = [j for j in names if j in kp and bones[kp[j]['bone']]['name'] in rig.h0]
    a = np.array([kp[j]['rest'] for j in common]); b = np.array([list(build_rest(j)) for j in common])
    s, R, t = similarity(a, b)
    J = s * J @ R.T + t
    idx = {j: i for i, j in enumerate(names)}
    head_of = {bn['name']: bn['head_joint'] for bn in bones}
    tail_of = {bn['name']: bn['tail_joint'] for bn in bones}
    root = bones[0]['name']
    order = [n for n in rig.order if n in head_of]
    # the hips' rest frame from the same joints the capture gives
    def hip_frame(P):
        return frame_of(P('spine') - P('pelvis'), P('hip_L') - P('hip_R'))
    rest_P = lambda j: Vector(build_rest(j))
    F0 = hip_frame(rest_P)
    fps = m.get('fps', 24)
    T = len(J)
    length = (T - 1) / fps
    frames = max(2, int(round(length * choreo.FPS)) + 1)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    ad = rig.arm.animation_data or rig.arm.animation_data_create(); ad.action = act
    for i in range(frames):
        u = min(T - 1.0, i / choreo.FPS * fps); i0 = int(u); i1 = min(T - 1, i0 + 1); w = u - i0
        Jt = J[i0] * (1 - w) + J[i1] * w
        P = lambda j: Vector(Jt[idx[j]])
        W = {}
        Rh = (hip_frame(P) @ F0.transposed()).to_quaternion()
        rig.put(W, root, Rh @ rig.R0[root], P(head_of[root]))
        for n in order:
            if n == root: continue
            p = rig.parent[n]
            qp = (rig.posed(W, p).to_quaternion() @ rig.R0[p].inverted()) if p else Rh
            d0 = qp @ rig.dir0[n]
            d = P(tail_of[n]) - P(head_of[n]) if tail_of[n] in idx and head_of[n] in idx else d0
            q = d0.rotation_difference(d) @ qp if d.length > 1e-6 else qp
            rig.put(W, n, q @ rig.R0[n])
        rig.key(choreo.grounded(rig, W, name), 1 + i)
    print('CAPTURE_CLIP %s: %d frames from %s (rest fit: scale %.3f)' % (name, frames, Path(path).name, s), flush=True)
    return act


def clips(rig, creature):
    """every captured clip a creature has (creatures/<name>/clips/<move>.json, named in its clips.json)"""
    d = HERE / 'creatures' / creature / 'clips'
    table = json.load(open(d / 'clips.json')) if (d / 'clips.json').exists() else {}
    return [clip(rig, game, d / (move + '.json'), loop=False, rig_info=d / 'rig_info.json') for move, game in table.items()]
