# Keypose animation on the slim skeleton (the Codex hunters' and skeletons' rig, bones renamed to the game's names).
# Every clip the game plays on the player and the humanoid monsters is authored here, on that skeleton itself: poses
# are given as body angles, hand and foot targets and weapon directions; two-bone IK places elbows and knees; the
# poses are interpolated smoothly and keyed every frame. Nothing comes from the old chibi rig.
#
# Space: Blender armature space. The character faces -Y, its left is +X, up is +Z. Lengths are metres for a 1.83 m
# body (the Codex male); Rig.k scales them to another body. Angles are degrees:
#   pitch > 0 leans forward, roll > 0 leans to the character's left, yaw > 0 turns to the left.
import bpy, math
from mathutils import Vector, Matrix, Quaternion

# Codex bone names -> the game's (the code finds hips, chest, head, hand slots and feet by these names)
NAMES = {
    'Hips': 'hips', 'Spine': 'spine', 'Chest': 'chest', 'Neck': 'neck', 'Head': 'head',
    'LeftShoulder': 'shoulder.l', 'LeftUpperArm': 'upperarm.l', 'LeftLowerArm': 'lowerarm.l', 'LeftHand': 'hand.l',
    'RightShoulder': 'shoulder.r', 'RightUpperArm': 'upperarm.r', 'RightLowerArm': 'lowerarm.r', 'RightHand': 'hand.r',
    'LeftUpperLeg': 'upperleg.l', 'LeftLowerLeg': 'lowerleg.l', 'LeftFoot': 'foot.l', 'LeftToes': 'toes.l',
    'RightUpperLeg': 'upperleg.r', 'RightLowerLeg': 'lowerleg.r', 'RightFoot': 'foot.r', 'RightToes': 'toes.r',
}
MALE_HEIGHT = 1.83
FPS = 30


def rot(pitch=0.0, roll=0.0, yaw=0.0):
    """Body rotation: yaw (about up), then pitch (forward lean), then roll (side lean)."""
    return (Quaternion((0, 0, 1), math.radians(yaw)) @ Quaternion((1, 0, 0), math.radians(pitch))
            @ Quaternion((0, 1, 0), math.radians(roll)))


def V(x, y, z): return Vector((x, y, z))


def frame(y_axis, z_hint):
    """Rotation whose Y axis is y_axis and whose Z axis is as close to z_hint as possible."""
    y = Vector(y_axis).normalized()
    z = Vector(z_hint) - y * Vector(z_hint).dot(y)
    if z.length < 1e-5: z = y.orthogonal()
    z.normalize(); x = y.cross(z)
    return Matrix((x, y, z)).transposed().to_quaternion()


def swing(a, b):
    return Vector(a).normalized().rotation_difference(Vector(b).normalized())


def rename_bones(arm):
    for b in arm.data.bones:
        if b.name in NAMES: b.name = NAMES[b.name]
    for o in bpy.data.objects:
        for g in getattr(o, 'vertex_groups', []):
            if g.name in NAMES: g.name = NAMES[g.name]


def add_hand_slots(arm):
    """handslot.l / handslot.r: the grip points, children of the hands, in the palm. Their Y axis is the weapon's
    direction from the grip (the blade), their Z axis its edge (the side that leads a cut). At rest the blade points
    forward (-Y) and the edge up (+Z)."""
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    for s in ('l', 'r'):
        hand = eb['hand.' + s]
        d = (hand.tail - hand.head)
        grip = hand.head + d * .38 + Vector((0, -.012, -.02))      # in the palm, a little below the bone line
        if 'handslot.' + s in eb: eb.remove(eb['handslot.' + s])
        sl = eb.new('handslot.' + s)
        sl.head = grip; sl.tail = grip + Vector((0, -.1, 0)); sl.align_roll(Vector((0, 0, 1)))
        sl.parent = hand; sl.use_connect = False; sl.use_deform = False
    bpy.ops.object.mode_set(mode='OBJECT')


class Rig:
    def __init__(self, arm):
        self.arm = arm
        self.bones = arm.data.bones
        self.rest = {b.name: b.matrix_local.copy() for b in self.bones}
        self.R0 = {n: m.to_quaternion() for n, m in self.rest.items()}
        self.h0 = {n: m.translation.copy() for n, m in self.rest.items()}
        self.dir0 = {b.name: (b.tail_local - b.head_local).normalized() for b in self.bones}
        self.len = {b.name: (b.tail_local - b.head_local).length for b in self.bones}
        self.parent = {b.name: b.parent.name if b.parent else None for b in self.bones}
        order, todo = [], [b for b in self.bones if not b.parent]
        while todo:
            b = todo.pop(0); order.append(b.name); todo += list(b.children)
        self.order = order
        self.height = self.h0['head'].z + self.len['head']
        self.k = self.height / MALE_HEIGHT
        self.arm_len = self.len['upperarm.l'] + self.len['lowerarm.l']
        self.leg_len = self.len['upperleg.l'] + self.len['lowerleg.l']

    # ---- matrices -------------------------------------------------------------------------------------------
    def carried(self, W, n):
        """Head of bone n when its parent is posed as W[parent] (it rides on the parent as at rest)."""
        p = self.parent[n]
        if not p: return self.h0[n].copy()
        return W[p] @ (self.rest[p].inverted() @ self.h0[n])

    def put(self, W, n, R, head=None):
        h = self.carried(W, n) if head is None else head
        m = R.to_matrix().to_4x4(); m.translation = h
        W[n] = m

    def chest_frame(self, W):
        """Maps a point given in the rest chest frame (armature coords around the rest chest head) to the posed body."""
        Q = W['chest'].to_quaternion() @ self.R0['chest'].inverted()
        h = W['chest'].translation; h0 = self.h0['chest']
        return lambda p: h + Q @ (Vector(p) - h0), Q

    # ---- solving one pose -----------------------------------------------------------------------------------
    def solve(self, P):
        k = self.k
        W = {}
        Hq = rot(*P.get('hipsR', (0, 0, 0)))
        hips = self.h0['hips'] + Vector(P.get('hips', (0, 0, 0))) * k
        self.put(W, 'hips', Hq @ self.R0['hips'], hips)
        Sq = Hq @ rot(*P.get('spine', (0, 0, 0)))
        self.put(W, 'spine', Sq @ self.R0['spine'])
        Cq = Sq @ rot(*P.get('chest', (0, 0, 0)))
        self.put(W, 'chest', Cq @ self.R0['chest'])
        Nq = Cq @ rot(*P.get('neck', (0, 0, 0)))
        self.put(W, 'neck', Nq @ self.R0['neck'])
        self.put(W, 'head', Nq @ rot(*P.get('head', (0, 0, 0))) @ self.R0['head'])
        to_body, Bq = self.chest_frame(W)
        for s in ('r', 'l'):
            self.put(W, 'shoulder.' + s, Cq @ rot(*P.get('sh' + s.upper(), (0, 0, 0))) @ self.R0['shoulder.' + s])
            self.arm_ik(W, s, P, to_body, Bq)
        for s in ('l', 'r'):
            self.leg_ik(W, s, P, Hq, W['hips'].translation)
        return W

    def arm_ik(self, W, s, P, to_body, Bq):
        k = self.k; S_ = s.upper()
        up, lo, hd, sl = 'upperarm.' + s, 'lowerarm.' + s, 'hand.' + s, 'handslot.' + s
        world = P.get('world' + S_, False)
        conv = (lambda p: Vector(p)) if world else to_body
        cq = Quaternion() if world else Bq
        self.put(W, up, self.R0[up])          # head position only, for S
        S = W[up].translation
        blade = P.get('blade' + S_)
        if s == 'l' and P.get('handL') == 'twohand':
            # the left hand on the right hand's weapon, `gripL` metres along it (negative: toward the pommel)
            R = W['handslot.r']; ry = R.to_quaternion() @ Vector((0, 1, 0)); rz = R.to_quaternion() @ Vector((0, 0, 1))
            tgt = R.translation + ry * P.get('gripL', -.12) * k
            blade, edge, cq = tuple(ry), tuple(rz), Quaternion()
            P = dict(P); P['edgeL'] = edge
        else:
            tgt = conv(self.scale_pt(P['hand' + S_], 'chest' if not world else None))
        if blade is not None:
            edge = P.get('edge' + S_, (0, 0, 1))
            Rslot = cq @ frame(blade, edge)
            Rhand = Rslot @ self.R0[sl].inverted()       # the hand's turn from rest (the slot rides on the hand)
            off = self.h0[sl] - self.h0[hd]
            wrist = tgt - (Rhand @ off)
            Rh = Rhand @ self.R0[hd]
        else:
            wrist = tgt; Rh = None
        pole = conv_dir(cq, P.get('elbow' + S_, (.3 if s == 'l' else -.3, .5, -1)))
        E, Wp = self.two_bone(S, wrist, self.len[up], self.len[lo], pole)
        Ru = swing(self.dir0[up], E - S) @ self.R0[up]
        self.put(W, up, Ru, S)
        Rl = swing(self.dir0[lo], Wp - E) @ self.R0[lo]
        if Rh is not None:
            Rl = self.half_twist(Rl, Rh @ self.R0[hd].inverted() @ self.R0[lo], Wp - E)
        self.put(W, lo, Rl, E)
        if Rh is None:
            Rh = Rl @ self.R0[lo].inverted() @ self.R0[hd]       # a straight wrist
            bend = P.get('wrist' + S_)
            if bend: Rh = conv_rot(cq, rot(*bend)) @ Rh
        self.put(W, hd, Rh, Wp)
        self.put(W, sl, Rh @ self.R0[hd].inverted() @ self.R0[sl])

    @staticmethod
    def half_twist(Rl, Rfollow, d):
        """Turns the forearm about its own axis halfway toward the hand's twist (no candy-wrapper wrist). Rfollow is
        the forearm's rotation if it turned fully with the hand."""
        axis = Vector(d).normalized()
        a = Rl @ Vector((1, 0, 0)); b = Rfollow @ Vector((1, 0, 0))
        a = (a - axis * a.dot(axis)); b = (b - axis * b.dot(axis))
        if a.length < 1e-4 or b.length < 1e-4: return Rl
        ang = a.normalized().angle(b.normalized())
        if a.cross(b).dot(axis) < 0: ang = -ang
        return Quaternion(axis, ang * .5) @ Rl

    def leg_ik(self, W, s, P, Hq, hips):
        k = self.k; S_ = s.upper()
        up, lo, ft, to = 'upperleg.' + s, 'lowerleg.' + s, 'foot.' + s, 'toes.' + s
        self.put(W, up, self.R0[up])
        S = W[up].translation
        x, y, lift, pitch, yaw = (list(P.get('foot' + S_, ())) + [0, 0, 0, 0, 0])[:5]
        ball0 = self.h0[to]
        if P.get('feetHips'):
            # tucked or lying: the ankle given relative to the hips' rest place, turning with the hips
            Rf = Hq @ Quaternion((0, 0, 1), math.radians(yaw)) @ Quaternion((1, 0, 0), math.radians(pitch))
            ankle = hips + Hq @ ((self.h0[ft] - self.h0['hips']) + Vector((x, y, lift)) * k)
        else:
            Rf = Quaternion((0, 0, 1), math.radians(yaw)) @ Quaternion((1, 0, 0), math.radians(pitch))
            ball = Vector((ball0.x + x * k, ball0.y + y * k, ball0.z + lift * k))
            ankle = ball + Rf @ (self.h0[ft] - ball0)
        pole = Vector(P.get('knee' + S_, (0, -1, 0)))
        pole = Hq @ pole if P.get('kneeHips', True) else pole
        E, Wp = self.two_bone(S, ankle, self.len[up], self.len[lo], pole)
        self.put(W, up, swing(self.dir0[up], E - S) @ self.R0[up], S)
        self.put(W, lo, swing(self.dir0[lo], Wp - E) @ self.R0[lo], E)
        self.put(W, ft, Rf @ self.R0[ft], Wp)
        tb = P.get('toes' + S_, max(0.0, pitch) if not P.get('feetHips') else 0)   # toes stay on the ground when the heel lifts
        self.put(W, to, Rf @ Quaternion((1, 0, 0), math.radians(-tb)) @ self.R0[to])

    @staticmethod
    def two_bone(S, T, L1, L2, pole):
        T = Vector(T); d = (T - S).length
        d = max(1e-4, min(d, (L1 + L2) * .999))
        axis = (T - S).normalized()
        a = (L1 * L1 - L2 * L2 + d * d) / (2 * d); h = math.sqrt(max(0.0, L1 * L1 - a * a))
        n = Vector(pole) - axis * Vector(pole).dot(axis)
        n = n.normalized() if n.length > 1e-6 else axis.orthogonal().normalized()
        E = S + axis * a + n * h
        return E, S + axis * d

    def scale_pt(self, p, about):
        """Hand targets are written for the male body around its rest chest head: scale them about it."""
        p = Vector(p)
        if about is None: return p * self.k
        c = self.h0[about]
        male_c = c / self.k
        return c + (p - male_c) * self.k

    # ---- keying ---------------------------------------------------------------------------------------------
    def key(self, W, f):
        pbs = self.arm.pose.bones
        for n in self.order:
            if n not in W: continue
            p = self.parent[n]
            if p:
                basis = (self.rest[p].inverted() @ self.rest[n]).inverted() @ W[p].inverted() @ W[n]
            else:
                basis = self.rest[n].inverted() @ W[n]
            pb = pbs[n]; pb.rotation_mode = 'QUATERNION'
            loc, q, _ = basis.decompose()
            pb.location = loc
            pb.rotation_quaternion = q
            pb.keyframe_insert('rotation_quaternion', frame=f)
            pb.keyframe_insert('location', frame=f)


def conv_dir(q, v): return q @ Vector(v)


def conv_rot(q, r): return q @ r @ q.inverted()


# ---- interpolation ----------------------------------------------------------------------------------------------
def _lerp(a, b, t):
    if isinstance(a, bool) or isinstance(b, bool) or a is None or b is None: return a if t < .5 else b
    if isinstance(a, (int, float)): return a + (b - a) * t
    return type(a)(x + (y - x) * t for x, y in zip(a, b))


def _cr(p0, p1, p2, p3, t):
    """Catmull-Rom between p1 and p2 (smooth through the keys)."""
    if any(isinstance(v, (bool, str)) or v is None for v in (p0, p1, p2, p3)): return p1 if t < .5 else p2
    if not isinstance(p1, (int, float)) and any(isinstance(v, (int, float)) or len(v) != len(p1) for v in (p0, p2, p3)): return p1 if t < .5 else p2
    def f(a, b, c, d): return .5 * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t)
    if isinstance(p1, (int, float)): return f(p0, p1, p2, p3)
    return tuple(f(a, b, c, d) for a, b, c, d in zip(p0, p1, p2, p3))


def fill(keys, base):
    """Each key's missing entries come from the key before (the first from base)."""
    out, cur = [], dict(base)
    for t, pose in keys:
        cur = dict(cur); cur.update(pose); out.append((t, cur))
    return out


def sample(keys, t, loop=False, length=None):
    times = [k[0] for k in keys]
    n = len(keys)
    if loop:
        L = length or times[-1]
        t = t % L
        ext = [(times[i] - L, keys[i][1]) for i in range(n)] + [(times[i], keys[i][1]) for i in range(n)] + [(times[i] + L, keys[i][1]) for i in range(n)]
        times = [e[0] for e in ext]; poses = [e[1] for e in ext]
    else:
        poses = [k[1] for k in keys]
        if t <= times[0]: return poses[0]
        if t >= times[-1]: return poses[-1]
    i = max(j for j in range(len(times) - 1) if times[j] <= t)
    u = (t - times[i]) / max(1e-6, times[i + 1] - times[i])
    p0 = poses[max(0, i - 1)]; p1 = poses[i]; p2 = poses[i + 1]; p3 = poses[min(len(poses) - 1, i + 2)]
    out = {}
    for key in p1:
        a, b = p1.get(key), p2.get(key, p1.get(key))
        c0, c3 = p0.get(key, a), p3.get(key, b)
        try:
            out[key] = _cr(c0, a, b, c3, u) if p1.get('_ease') != 'linear' else _lerp(a, b, u)
        except TypeError:
            out[key] = a if u < .5 else b
    return out


def bake(rig, name, keys, base, loop=False, length=None, post=None):
    """keys: [(seconds, pose dict)], each pose filled forward from base. post(pose, t) may adjust a sampled pose."""
    act = bpy.data.actions.new(name); act.use_fake_user = True
    ad = rig.arm.animation_data or rig.arm.animation_data_create()
    ad.action = act
    keys = fill(keys, base)
    L = length or keys[-1][0]
    frames = max(2, int(round(L * FPS)) + (0 if loop else 1))
    for i in range(frames):
        t = i / FPS
        P = sample(keys, t, loop, L)
        if post: P = post(P, t)
        rig.key(rig.solve(P), 1 + i)
    if loop:      # the first pose again one frame after the last: a seamless loop
        P = sample(keys, 0, loop, L)
        if post: P = post(P, 0)
        rig.key(rig.solve(P), 1 + frames)
    return act


def bake_fn(rig, name, fn, length, loop=True):
    """A clip from a function of time (gait cycles)."""
    act = bpy.data.actions.new(name); act.use_fake_user = True
    ad = rig.arm.animation_data or rig.arm.animation_data_create()
    ad.action = act
    frames = max(2, int(round(length * FPS)))
    for i in range(frames + 1):
        t = (i / frames) * length
        rig.key(rig.solve(fn(t % length if loop else t)), 1 + i)
    return act
