# Blender (headless), step 3 of the Briarhound build: rigs and animates the studio mesh (normalized by
# briarhound_prep.py, texture from briarhound_texture.py) and exports Assets/Resources/Models/Briarhound.fbx.
#  - The concept's head looks at the viewer (about 70 degrees to its left); it is turned to face forward first, which
#    also puts the crescent blade crosswise in the jaws the way a dog carries a stick.
#  - Joints are measured from the mesh (paws, leg centre lines, belly, seat, ears, tail), using the Cu-sith's bone names
#    so the companion code is unchanged, plus a `spine` bone under the saddle and the non-deforming `saddle` seat bone.
#  - Skin: distance to bones with anatomical gates (a leg never pulls the belly or the tail, the blade stays rigid with
#    the head), seam-unified surface diffusion, 8 influences (the same method as the reference monsters).
#  - Clips use IK on all four paws, so planted paws stay planted and the body (not the ground line) moves:
#    Idle, Walk (trot, 0.7 s), Gallop (rotary, 0.47 s, low bob, level head for the rider), Bite (blade slash),
#    Sit, Howl (one shot), Hit. Baked to plain bone keys for Unity's legacy Animation.
#   blender -b --python Tools/rig_briarhound.py [-- --turn 70]
import bpy, sys, json, math
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
TURN = float(argv[argv.index('--turn') + 1]) if '--turn' in argv else 70.0
r = Path.home() / 'Documents/FolkloreHunter-RPG-Framework'
work = r / 'SourceArt/Briarhound'
dest = r / 'Assets/Resources/Models'
preview = r / 'Captures/Briarhound'; preview.mkdir(parents=True, exist_ok=True)
FPS = 30

bpy.ops.wm.open_mainfile(filepath=str(work / 'briarhound_norm.blend'))
scene = bpy.context.scene; scene.render.fps = FPS
body = bpy.data.objects['BriarhoundBody']
prep = json.loads((work / 'prep.json').read_text())
img = bpy.data.images.load(str(r / 'Assets/Resources/Monsters/Briarhound_basecolor.png'), check_existing=True)
for m in body.data.materials:
    if m and m.use_nodes:
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE': n.image = img
me = body.data
V = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get('co', V); V = V.reshape(-1, 3)


def smooth(v, a, b):
    t = np.clip((v - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)


# ---------------- 1. turn the head to face forward ----------------
hsel = (V[:, 2] > 1.5) & (V[:, 1] > -.95) & (V[:, 1] < -.4) & (V[:, 0] > -.3) & (V[:, 0] < .35)
HC = V[hsel].mean(0)
blade_rear = (V[:, 0] > .36) & (V[:, 1] > -.5) & (V[:, 1] < -.1) & (V[:, 2] > 1.28) & (V[:, 2] < 1.56)
w = smooth(V[:, 2], .95, 1.4) * (1 - smooth(V[:, 1], -.6, 0))     # wide falloff: the mane twists gently
w[blade_rear] = 1
a = -math.radians(TURN) * w
rel = V[:, :2] - HC[:2]
V[:, 0] = HC[0] + rel[:, 0] * np.cos(a) - rel[:, 1] * np.sin(a) - HC[0] * w
V[:, 1] = HC[1] + rel[:, 0] * np.sin(a) + rel[:, 1] * np.cos(a)
me.vertices.foreach_set('co', V.ravel()); me.update()
H = 1.9

# ---------------- 2. joints measured from the mesh ----------------
def slab(z, dz=.015): return V[np.abs(V[:, 2] - z) < dz]


def trace(c0, sign, levels):
    c = np.array(c0, float); out = {}
    for z in levels:
        s = slab(z); s = s[sign * s[:, 0] > .06]
        s = s[np.linalg.norm(s[:, :2] - c, axis=1) < .17]
        if len(s) > 8: c = .5 * c + .5 * s[:, :2].mean(0)
        out[round(z, 2)] = c.copy()
    return out


levels = [round(.04 + .02 * i, 2) for i in range(30)]
bones = {}
def bone(n, h, t, parent, deform=True): bones[n] = (tuple(map(float, h)), tuple(map(float, t)), parent, deform)

legs = {}
for key, paw in prep['paws'].items():
    region, side = key.split('.'); sign = 1 if side == 'l' else -1
    t = trace(paw['center'], sign, levels)
    toe = (paw['center'][0], paw['toe'] + .05, .04)
    if region == 'fore':
        wrist = (t[.18][0], t[.18][1], .18); e = t[.58]; elbow = (e[0], e[1] + .04, .58); top = (e[0] * .8, e[1] - .04, .98)
        names = ('fore_upper.', 'fore_lower.', 'fore_paw.')
    else:
        hk = t[.3]; wrist = (hk[0], hk[1] + .02, .30); s6 = t[.6]; elbow = (s6[0], s6[1] - .08, .62); top = (s6[0] * .8, s6[1] + .14, 1.0)
        names = ('hind_upper.', 'hind_lower.', 'hind_paw.')
    legs[key] = dict(region=region, side=side, sign=sign, top=top, mid=elbow, low=wrist, toe=toe, names=[n + side for n in names])

fy = np.mean([legs[k]['top'][1] for k in ('fore.l', 'fore.r')]); hy = np.mean([legs[k]['top'][1] for k in ('hind.l', 'hind.r')])
def belly(y):
    s = V[(np.abs(V[:, 1] - y) < .05) & (np.abs(V[:, 0]) < .12)]; return float(s[:, 2].min()) if len(s) else .62
zc = belly(fy) + .38; zh = belly(hy) + .38
# Seat: the lowest point of the saddle's top surface between pommel and cantle.
best = None
for y0 in np.arange(-.25, .35, .03):
    s = V[(np.abs(V[:, 1] - y0) < .02) & (np.abs(V[:, 0]) < .1)]
    if len(s):
        top = float(s[:, 2].max())
        if best is None or top < best[1]: best = (float(y0), top)
seat_y, seat_z = best
# Head after the turn: skull centre, nose tip, mouth line (the blade's height in the jaws).
hsel = (V[:, 2] > 1.45) & (V[:, 1] > -1.05) & (V[:, 1] < -.35) & (np.abs(V[:, 0]) < .3)
HC2 = V[hsel].mean(0)
snout = V[(np.abs(V[:, 0] - HC2[0]) < .1) & (V[:, 2] > 1.3) & (V[:, 2] < 1.62)]
nose_y = float(snout[:, 1].min())
bl = V[(np.abs(V[:, 0]) > .4) & (V[:, 2] > 1.2) & (V[:, 2] < 1.7) & (V[:, 1] < -.6)]
mouth_z = float(np.median(bl[:, 2])) if len(bl) else 1.42
ears = {}
for side, sign in (('l', 1), ('r', -1)):
    e = V[(V[:, 2] > 1.7) & (sign * (V[:, 0] - HC2[0]) > .02) & (V[:, 1] < -.3)]
    if len(e) < 5: e = V[(V[:, 2] > 1.7) & (V[:, 1] < -.3)]
    tip = e[np.argmax(e[:, 2])]; ears[side] = (float(np.median(e[:, 0])), float(np.median(e[:, 1])), float(tip[2]))
# Tail centre line: the bushy mass behind the hind legs, from the rump down to the tip.
tail = [(0, hy + .3, zh + .05)]
for z, ymin in ((.85, .95), (.6, .88), (.38, .88), (.18, .9)):
    s = V[(np.abs(V[:, 2] - z) < .06) & (V[:, 1] > ymin) & (np.abs(V[:, 0]) < .3)]
    if len(s) > 10: tail.append((float(s[:, 0].mean()), float(s[:, 1].mean()), z))

bone('root', (0, 0, 0), (0, 0, .3), None, False)
bone('hips', (0, hy + .25, zh), (0, .2, (zh + zc) / 2), 'root')
bone('spine', (0, .2, (zh + zc) / 2), (0, -.2, zc), 'hips')
bone('chest', (0, -.2, zc), (0, fy - .2, zc + .08), 'spine')
neck0 = (0, fy - .2, zc + .1); atlas = (HC2[0] * .5, HC2[1] + .2, HC2[2] - .1)
bone('neck', neck0, atlas, 'chest')
bone('head', atlas, (HC2[0], nose_y, mouth_z + .04), 'neck')
bone('jaw', (HC2[0], HC2[1] - .02, mouth_z - .06), (HC2[0], nose_y + .1, mouth_z - .1), 'head')
for side in ('l', 'r'):
    ex, ey, ez = ears[side]; bone('ear.' + side, (ex, ey, ez - .2), (ex, ey, ez), 'head')
for i in range(min(3, len(tail) - 1)):
    bone('tail_%d' % (i + 1), tail[i], tail[i + 1] if i < 2 else tail[-1], 'hips' if i == 0 else 'tail_%d' % i)
for L in legs.values():
    up, lo, paw = L['names']
    bone(up, L['top'], L['mid'], 'chest' if L['region'] == 'fore' else 'hips')
    bone(lo, L['mid'], L['low'], up); bone(paw, L['low'], L['toe'], lo)
bone('saddle', (0, seat_y, seat_z), (0, seat_y, seat_z + .2), 'spine', False)

ad = bpy.data.armatures.new('BriarhoundRig'); arm = bpy.data.objects.new('Rig', ad); scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
for n, (h, t, p, d) in bones.items():
    eb = ad.edit_bones.new(n); eb.head = h; eb.tail = t; eb.roll = 0; eb.use_deform = d
for n, (h, t, p, d) in bones.items():
    if p: ad.edit_bones[n].parent = ad.edit_bones[p]
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------- 3. skin ----------------
names = [n for n, b in bones.items() if b[3]]
D = []
for n in names:
    h = np.array(bones[n][0]); t = np.array(bones[n][1]); ab = t - h
    u = np.clip(((V - h) @ ab) / max(1e-9, ab @ ab), 0, 1)
    d = np.linalg.norm(V - (h + u[:, None] * ab), axis=1)
    if n.endswith('.l'): d += np.maximum(0, -V[:, 0]) * 2
    if n.endswith('.r'): d += np.maximum(0, V[:, 0]) * 2
    D.append(d)
D = np.array(D).T
Wf = np.exp(-(D - D.min(1, keepdims=True)) / .09)
col = {n: i for i, n in enumerate(names)}
def gate(n, g):
    Wf[:, col[n]] *= g
tailzone = smooth(V[:, 1], hy + .22, hy + .32) * (1 - smooth(np.abs(V[:, 0]), .2, .3))
for L in legs.values():
    for n in L['names']:
        g = smooth(L['sign'] * V[:, 0], .05, .16) * (1 - smooth(V[:, 2], .72, 1.0)) * (1 - tailzone)
        yc = L['top'][1]; g *= 1 - smooth(np.abs(V[:, 1] - yc), .32, .45)
        gate(n, g)
headzone = smooth(V[:, 2], mouth_z - .25, mouth_z - .1) * (1 - smooth(V[:, 1], HC2[1] + .15, HC2[1] + .35))
gate('jaw', (V[:, 2] < mouth_z - .02) * (V[:, 1] < HC2[1] - .05) * (np.abs(V[:, 0] - HC2[0]) < .2))
for side in ('l', 'r'): gate('ear.' + side, smooth(V[:, 2], 1.62, 1.7) * (V[:, 1] < HC2[1] + .2))
for n in names:
    if n.startswith('tail_'): gate(n, smooth(V[:, 1], hy + .1, hy + .3))
gate('head', headzone)
Wf /= np.maximum(1e-9, Wf.sum(1, keepdims=True))
# Seam-unified surface diffusion (vertices split at UV seams share one weight set).
edges = np.zeros(len(me.edges) * 2, int); me.edges.foreach_get('vertices', edges); edges = edges.reshape(-1, 2)
owner = np.concatenate([edges[:, 0], edges[:, 1]]); nbr = np.concatenate([edges[:, 1], edges[:, 0]])
deg = np.maximum(1, np.bincount(owner, minlength=len(V)))
_, same = np.unique(np.round(V / 1e-5).astype(np.int64), axis=0, return_inverse=True); same = same.ravel(); cnt = np.bincount(same)
def unify(w):
    acc = np.zeros((len(cnt), w.shape[1])); np.add.at(acc, same, w); return acc[same] / cnt[same, None]
def relax(w, n, keep):
    for _ in range(n):
        avg = np.zeros_like(w); np.add.at(avg, owner, w[nbr]); w = unify(keep * w + (1 - keep) * avg / deg[:, None])
    return w
Wf = relax(unify(Wf), 20, .4)
# Rigid pieces: the blade (both halves) moves with the head only; paws below the ankle with their paw bone.
blade = ((np.abs(V[:, 0]) > .32) & (V[:, 2] > mouth_z - .15) & (V[:, 2] < mouth_z + .15) & (V[:, 1] < HC2[1] + .15) & (V[:, 1] > HC2[1] - .45))
Wf[blade] = 0; Wf[blade, col['head']] = 1
paws = [col[L['names'][2]] for L in legs.values()]
nearest_paw = np.argmin(D[:, paws], 1); pg = (1 - smooth(V[:, 2], .07, .13)) * (1 - tailzone)
Wf *= 1 - pg[:, None]; Wf[np.arange(len(V)), np.array(paws)[nearest_paw]] += pg
Wf = relax(Wf, 6, .6)
Wf[blade] = 0; Wf[blade, col['head']] = 1
top = np.argsort(-Wf, 1)[:, :8]; Wt = np.take_along_axis(Wf, top, 1); Wt /= Wt.sum(1, keepdims=True)
body.vertex_groups.clear()
for n in names: body.vertex_groups.new(name=n)
for j, n in enumerate(names):
    rows = np.nonzero((top == j).any(1))[0]
    vg = body.vertex_groups[n]
    for i in rows:
        wv = float(Wt[i][top[i] == j][0])
        if wv > .002: vg.add([int(i)], wv, 'REPLACE')
body.parent = arm
mod = body.modifiers.new('Skin', 'ARMATURE'); mod.object = arm

# ---------------- 4. IK paws and authored clips ----------------
pb = arm.pose.bones
for p in pb: p.rotation_mode = 'QUATERNION'
targets = {}
for key, L in legs.items():
    tg = bpy.data.objects.new('IK_' + key, None); scene.collection.objects.link(tg); tg.location = L['low']
    c = pb[L['names'][1]].constraints.new('IK'); c.target = tg; c.chain_count = 2; c.use_stretch = False
    c2 = pb[L['names'][2]].constraints.new('COPY_ROTATION'); c2.target = tg; c2.owner_space = 'WORLD'; c2.target_space = 'WORLD'
    tg.rotation_mode = 'XYZ'; tg.rotation_euler = arm.data.bones[L['names'][2]].matrix_local.to_euler()
    targets[key] = (tg, Vector(L['low']), tg.rotation_euler.copy())
rest_rot = {b.name: b.matrix_local.to_3x3() for b in arm.data.bones}


def reset():
    for p in pb: p.rotation_quaternion = (1, 0, 0, 0); p.location = (0, 0, 0)
    for tg, at, rot in targets.values(): tg.location = at; tg.rotation_euler = rot


def rot(n, axis, deg):
    """Rotates a bone about a world axis (X pitch: negative lifts the nose; Z yaw: positive turns to its left)."""
    B = rest_rot[n]; ax = (B.inverted() @ Vector(axis)).normalized()
    pb[n].rotation_quaternion = Quaternion(ax, math.radians(deg)) @ pb[n].rotation_quaternion


def move_root(v):
    pb['root'].location = rest_rot['root'].inverted() @ Vector(v)


def paw(key, dy=0, dz=0, pitch=0):
    tg, at, r0 = targets[key]; tg.location = at + Vector((0, dy, dz))
    tg.rotation_euler = (Matrix.Rotation(math.radians(pitch), 3, 'X') @ r0.to_matrix()).to_euler()


def s(t, ph=0): return math.sin((t + ph) * 2 * math.pi)


def step(phase, duty, stride, lift):
    """Paw path for one cycle: planted and sweeping back for `duty` of it, then lifted and carried forward."""
    phase %= 1
    if phase < duty: return (-.5 + phase / duty) * stride, 0, 0
    u = (phase - duty) / (1 - duty)
    return (.5 - u) * stride, math.sin(math.pi * u) * lift, 30 * math.sin(math.pi * u)


def gait(t, offsets, duty, stride, lift):
    for key, ph in offsets.items():
        dy, dz, pitch = step(t + ph, duty, stride[key[:4]] if isinstance(stride, dict) else stride, lift)
        paw(key, dy, dz, pitch)


def tail_sway(t, amp, ph=0, lift=0):
    for i, n in enumerate(('tail_1', 'tail_2', 'tail_3')):
        if n in pb: rot(n, (0, 0, 1), amp * s(t, ph + i * .08)); rot(n, (1, 0, 0), lift)


def idle(t):
    move_root((0, 0, .006 * s(t)))
    rot('chest', (1, 0, 0), -1.2 * s(t)); rot('neck', (0, 0, 1), 3 * s(t * .5)); rot('head', (1, 0, 0), 1.5 * s(t, .3))
    rot('ear.l', (1, 0, 0), 10 * max(0, s(t * 2, .6)) ** 8)
    tail_sway(t, 6)


WALK = {'fore.l': 0, 'hind.r': 0, 'fore.r': .5, 'hind.l': .5}                 # trot: diagonal pairs
GALLOP = {'hind.l': 0, 'hind.r': .1, 'fore.l': .5, 'fore.r': .6}               # rotary gallop, short suspensions


def walk(t):
    gait(t, WALK, .55, .72, .11)
    move_root((0, 0, .018 * abs(s(t * 2, .12))))
    rot('chest', (0, 0, 1), 2.5 * s(t)); rot('hips', (0, 0, 1), -2.5 * s(t))
    rot('neck', (1, 0, 0), 1.5 * s(t * 2)); rot('head', (1, 0, 0), -1.5 * s(t * 2))      # head stays level
    tail_sway(t, 9, lift=-4)


def gallop(t):
    gait(t, GALLOP, .36, .94, .2)
    flex = s(t, .1)
    move_root((0, 0, .018 * s(t * 2, .2)))
    rot('hips', (1, 0, 0), 2.2 * flex); rot('spine', (1, 0, 0), -1.2 * flex); rot('chest', (1, 0, 0), -2 * flex)
    rot('neck', (1, 0, 0), 4 * flex - 4); rot('head', (1, 0, 0), -3 * flex + 4)           # counter-pitch: steady head
    for e in ('ear.l', 'ear.r'): rot(e, (1, 0, 0), 25)
    tail_sway(t, 3, lift=-8 + 4 * flex)


def sm(t, a, b):
    u = min(1, max(0, (t - a) / (b - a))); return u * u * (3 - 2 * u)


def bite(t):
    # Blade slash: crouch and wind the head to the right, then a fast sweep across to the left with a lunge, hold, recover.
    wind = sm(t, 0, .34) * (1 - sm(t, .36, .5))
    sweep = sm(t, .36, .5) * (1 - sm(t, .62, 1))
    lunge = math.sin(math.pi * min(1, max(0, (t - .3) / .45)))
    yaw = -28 * wind + 34 * sweep
    move_root((0, -.16 * lunge, -.06 * wind))
    rot('chest', (1, 0, 0), 6 * wind - 5 * lunge); rot('chest', (0, 0, 1), yaw * .25)
    rot('neck', (0, 0, 1), yaw * .35); rot('head', (0, 0, 1), yaw * .4); rot('head', (1, 0, 0), 4 * wind)
    rot('jaw', (1, 0, 0), 6 * lunge)
    for e in ('ear.l', 'ear.r'): rot(e, (1, 0, 0), 35 * max(wind, lunge))
    tail_sway(t, 0, lift=12 * lunge)


def sit_pose(a):
    # The whole body pitches nose-up about the shoulders (they stay put, so the straight front legs keep their paws
    # planted) and the rump drops; the hind legs fold into a crouch, hocks low and paws planted (laying the paws fully
    # flat curls the paw mesh into a hook).
    th = 17 * a
    shoulder = Vector((0, fy, zc))
    R = Matrix.Rotation(math.radians(-th), 3, 'X')
    rot('root', (1, 0, 0), -th); move_root(shoulder - R @ shoulder)
    rot('chest', (1, 0, 0), 5 * a); rot('neck', (1, 0, 0), 8 * a)
    for k in ('hind.l', 'hind.r'): paw(k, -.08 * a, -.12 * a, -25 * a)
    if 'tail_1' in pb: rot('tail_1', (1, 0, 0), 28 * a)


def sit(t):
    sit_pose(1); rot('chest', (1, 0, 0), -1 * s(t)); rot('head', (0, 0, 1), 4 * s(t * .5)); tail_sway(t, 4)


def howl(t):
    a = sm(t, 0, .22); up = math.sin(math.pi * min(1, max(0, (t - .15) / .75)))
    sit_pose(a); rot('neck', (1, 0, 0), -30 * up); rot('head', (1, 0, 0), -22 * up); rot('jaw', (1, 0, 0), 10 * up)
    for e in ('ear.l', 'ear.r'): rot(e, (1, 0, 0), 20 * up)


def hit(t):
    k = math.sin(math.pi * min(1, t / .55))
    move_root((0, .08 * k, -.04 * k))
    rot('chest', (1, 0, 0), 7 * k); rot('neck', (1, 0, 0), 14 * k); rot('head', (1, 0, 0), 8 * k); rot('head', (0, 0, 1), -10 * k)
    for e in ('ear.l', 'ear.r'): rot(e, (1, 0, 0), 40 * k)
    tail_sway(t, 0, lift=20 * k)


clips = {'Idle': (2.4, idle), 'Walk': (.7, walk), 'Gallop': (.4667, gallop), 'Bite': (.9, bite), 'Sit': (2.0, sit), 'Howl': (1.6, howl), 'Hit': (.5, hit)}
arm.animation_data_create()
actions = []
deform = [p for p in pb]
for name, (length, fn) in clips.items():
    count = max(2, round(length * FPS))
    reset(); arm.animation_data.action = None
    for tg, _, _ in targets.values(): tg.animation_data_clear()
    ctl = bpy.data.actions.new(name + '_controls'); arm.animation_data.action = ctl
    for f in range(count + 1):
        reset(); fn(f / count)
        for p in pb: p.keyframe_insert('rotation_quaternion', frame=f + 1); p.keyframe_insert('location', frame=f + 1)
        for tg, _, _ in targets.values(): tg.keyframe_insert('location', frame=f + 1); tg.keyframe_insert('rotation_euler', frame=f + 1)
    scene.frame_start = 1; scene.frame_end = count + 1
    bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE'); bpy.ops.pose.select_all(action='SELECT')
    bpy.ops.nla.bake(frame_start=1, frame_end=count + 1, step=1, only_selected=True, visual_keying=True, clear_constraints=False,
                     use_current_action=False, bake_types={'POSE'})
    bpy.ops.object.mode_set(mode='OBJECT')
    baked = arm.animation_data.action; baked.name = name; baked.use_fake_user = True; actions.append(baked)
for p in pb:
    for c in list(p.constraints): p.constraints.remove(c)
for tg, _, _ in targets.values(): bpy.data.objects.remove(tg, do_unlink=True)
for a in list(bpy.data.actions):
    if a not in actions: bpy.data.actions.remove(a)

# ---------------- 5. measurements: planted paws, seat motion, deformation ----------------
def paw_world(n):
    return arm.matrix_world @ arm.pose.bones[n].tail


report = {'turnDeg': TURN, 'bones': list(bones), 'seat': [seat_y, seat_z], 'mouthZ': mouth_z, 'clips': {}}
rest = V.copy()
for a in actions:
    arm.animation_data.action = a
    n = int(a.frame_range[1]); info = {'frames': n, 'seconds': (n - 1) / FPS}
    lows = []; seat = []; worst = 0
    for f in range(1, n + 1):
        scene.frame_set(f)
        lows.append([paw_world(L['names'][2]).z for L in legs.values()])
        seat.append(tuple(arm.matrix_world @ arm.pose.bones['saddle'].head))
    lows = np.array(lows); seat = np.array(seat)
    info['pawLowest'] = float(lows.min()); info['seatBob'] = float(np.ptp(seat[:, 2])); info['seatSurge'] = float(np.ptp(seat[:, 1]))
    dg = bpy.context.evaluated_depsgraph_get(); scene.frame_set(max(1, n // 2))
    ev = body.evaluated_get(dg); m2 = ev.to_mesh(); P = np.zeros(len(m2.vertices) * 3); m2.vertices.foreach_get('co', P); ev.to_mesh_clear()
    P = P.reshape(-1, 3)
    # Seams must not open: split copies of one vertex have to stay together.
    acc = np.zeros((len(cnt), 3)); np.add.at(acc, same, P); spread = np.linalg.norm(P - (acc / cnt[:, None])[same], axis=1)
    info['seamGap'] = float(spread.max()); info['maxMove'] = float(np.linalg.norm(P - rest, axis=1).max())
    report['clips'][a.name] = info
arm.animation_data.action = bpy.data.actions['Idle']; scene.frame_set(1)

# ---------------- 6. preview renders ----------------
scene.render.engine = 'BLENDER_WORKBENCH'; scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x = 640; scene.render.resolution_y = 480; scene.render.film_transparent = True
cd = bpy.data.cameras.new('PreviewCam'); cd.type = 'ORTHO'; cd.ortho_scale = 3.6
cam = bpy.data.objects.new('PreviewCam', cd); scene.collection.objects.link(cam); scene.camera = cam
views = {'side': ((6, 0, 1.0), (math.radians(90), 0, math.radians(90))), 'front': ((0, -6, 1.0), (math.radians(90), 0, 0)),
         'threequarter': ((4.2, -4.2, 2.7), (math.radians(64), 0, math.radians(45))), 'top': ((0, 0, 7), (0, 0, 0))}
shots = []
for a in actions:
    arm.animation_data.action = a; n = int(a.frame_range[1])
    fracs = (0, .25, .5, .75) if a.name in ('Walk', 'Gallop', 'Bite', 'Howl') else (.5,)
    for fr in fracs:
        scene.frame_set(1 + int((n - 1) * fr))
        for v in (('side', 'threequarter') if fr in (0, .5) else ('side',)) if a.name != 'Idle' else ('side', 'front', 'threequarter', 'top'):
            cam.location, cam.rotation_euler = views[v]
            out = preview / ('%s_%s_%02d.png' % (a.name, v, int(fr * 100)))
            scene.render.filepath = str(out); bpy.ops.render.render(write_still=True); shots.append(out.name)
arm.animation_data.action = bpy.data.actions['Idle']; scene.frame_set(1)
bpy.data.objects.remove(cam, do_unlink=True)

# ---------------- 7. export ----------------
bpy.ops.wm.save_as_mainfile(filepath=str(work / 'Briarhound_rigged.blend'))
bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); body.select_set(True); bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=str(dest / 'Briarhound.fbx'), use_selection=True, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                         apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_anim=True, bake_anim_use_all_actions=True,
                         bake_anim_use_nla_strips=False, bake_anim_simplify_factor=0, path_mode='STRIP', embed_textures=False)
report.update({'output': 'Assets/Resources/Models/Briarhound.fbx', 'texture': 'Assets/Resources/Monsters/Briarhound_basecolor.png',
               'source': 'ClaudeEyesStudio Forge static job (local TripoSG) from the Briarhound concept art', 'vertices': len(V),
               'triangles': len(me.polygons), 'previews': shots})
(r / 'Docs/Assets').mkdir(parents=True, exist_ok=True)
(r / 'Docs/Assets/briarhound-export.json').write_text(json.dumps(report, indent=2))
print('BRIARHOUND_RIG', json.dumps({k: v for k, v in report.items() if k != 'previews'}), flush=True)
