# The Barrow Troll built from math (the owner, 2026-10-28: "use math to make it, it's looking bad right now").
#
#   blender -b --python Tools/slim/troll_math.py -- <out folder>      -> Troll_math.fbx, Troll_math_texture.png, previews
#
# Nothing here comes from a generated mesh. The body is an implicit surface: a field summed from simple shapes
# (balls, capsules and ellipsoids, Blender's metaballs) laid along the troll's own skeleton, and the skin is where the
# field reaches its threshold. Each shape belongs to a bone (or is shared between two at a joint), so the skin weights
# come from the same fields that made the surface: a vertex follows the bones whose shapes made it, in proportion.
# The arms are their own shapes, held clear of the body at rest, so they never share skin with the flanks or thighs
# (the generated troll's arms were grown into its sides). Its proportions follow the owner's troll sheet
# (Docs/Concepts/Troll/troll-turnaround.png): hunched, a heavy brow and bulbous nose, tusks, pointed ears, a belly, long
# arms with big hands, thick legs and broad feet, a ragged hide loincloth on a rope belt, wrist wraps, long dark hair.
# The colours are painted by math too (mottled grey-green skin, pink nose and ears, dark nails, patched leather) and
# baked into one texture. The troll faces -Y with its left at +X, like every body the game's moves are written for.
import bpy, bmesh, math, sys
from pathlib import Path
import numpy as np
from mathutils import Vector, Quaternion, Matrix

OUT = Path(sys.argv[sys.argv.index('--') + 1]); OUT.mkdir(parents=True, exist_ok=True)
STIFF = 3.0
VIS = (1 - (0.6 / STIFF) ** (1 / 3)) ** .5        # where an element's field meets the threshold, as a share of its radius
rng = np.random.default_rng(7)

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene

# ---- the skeleton (metres; the game's slim bone names) -------------------------------------------------------------
J = {
    'hips': (0, .02, 1.10), 'spine': (0, .03, 1.30), 'chest': (0, .02, 1.56), 'neck': (0, -.08, 1.98), 'head': (0, -.16, 2.10),
    'head_top': (0, -.20, 2.40),
}
for s, x in (('l', 1), ('r', -1)):
    J.update({
        'shoulder.' + s: (x * .14, .0, 1.86), 'upperarm.' + s: (x * .55, .02, 1.88), 'lowerarm.' + s: (x * .74, .06, 1.36),
        'hand.' + s: (x * .88, .0, .90), 'handtip.' + s: (x * .93, -.04, .64),
        'upperleg.' + s: (x * .24, .03, 1.06), 'lowerleg.' + s: (x * .30, -.03, .62), 'foot.' + s: (x * .32, .04, .13),
        'toes.' + s: (x * .34, -.20, .05), 'toetip.' + s: (x * .35, -.34, .04),
    })
BONES = [  # name, head joint, tail joint, parent
    ('hips', 'hips', 'spine', None), ('spine', 'spine', 'chest', 'hips'), ('chest', 'chest', 'neck', 'spine'),
    ('neck', 'neck', 'head', 'chest'), ('head', 'head', 'head_top', 'neck')]
for s in 'lr':
    BONES += [('shoulder.' + s, 'shoulder.' + s, 'upperarm.' + s, 'chest'), ('upperarm.' + s, 'upperarm.' + s, 'lowerarm.' + s, 'shoulder.' + s),
              ('lowerarm.' + s, 'lowerarm.' + s, 'hand.' + s, 'upperarm.' + s), ('hand.' + s, 'hand.' + s, 'handtip.' + s, 'lowerarm.' + s),
              ('upperleg.' + s, 'upperleg.' + s, 'lowerleg.' + s, 'hips'), ('lowerleg.' + s, 'lowerleg.' + s, 'foot.' + s, 'upperleg.' + s),
              ('foot.' + s, 'foot.' + s, 'toes.' + s, 'lowerleg.' + s), ('toes.' + s, 'toes.' + s, 'toetip.' + s, 'foot.' + s)]
V = lambda k: Vector(J[k])

ad = bpy.data.armatures.new('Rig'); arm = bpy.data.objects.new('Rig', ad); sc.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
for n, h, t, p in BONES:
    b = ad.edit_bones.new(n); b.head = V(h); b.tail = V(t)
    # roll: the bone's Z axis toward the troll's front (-Y) for the body and legs, up for the arms
    b.align_roll(Vector((0, 0, 1)) if n.split('.')[0] in ('shoulder', 'upperarm', 'lowerarm', 'hand') else Vector((0, -1, 0)))
    if p: b.parent = ad.edit_bones[p]; b.use_connect = (Vector(J[h]) - ad.edit_bones[p].tail).length < 1e-4
bpy.ops.object.mode_set(mode='OBJECT')

# ---- the body: shapes along the bones -------------------------------------------------------------------------------
mb = bpy.data.metaballs.new('TrollField'); mb.resolution = .022; mb.render_resolution = .022; mb.threshold = .6
field_obj = bpy.data.objects.new('TrollField', mb); sc.collection.objects.link(field_obj)
ELEMS = []        # (kind, centre, rotation, radius, size, bones {name: share}, paint)


def _el(kind, c, q, r, size, bones, paint, neg=False):
    e = mb.elements.new(type=kind); e.co = c; e.rotation = q; e.radius = r; e.stiffness = STIFF; e.use_negative = neg
    if kind in ('ELLIPSOID', 'CAPSULE'): e.size_x, e.size_y, e.size_z = size
    if not neg: ELEMS.append((kind, Vector(c), q.copy(), r, tuple(size), bones, paint))


def ball(c, vis, bones, paint='skin', neg=False):
    _el('BALL', c, Quaternion(), vis / VIS, (1, 1, 1), bones, paint, neg)


def blob(c, semi, bones, paint='skin', rot=Quaternion(), neg=False):
    """an ellipsoid with these visible semi-axes"""
    r = max(semi) / VIS
    _el('ELLIPSOID', c, rot, r, tuple(a / VIS / r for a in semi), bones, paint, neg)


def limb(a, b, ra, rb, bones, paint='skin', n=4):
    """a tapering limb from a to b: capsules whose radius goes from ra to rb"""
    a, b = Vector(a), Vector(b)
    for i in range(n):
        p, q = a.lerp(b, i / n), a.lerp(b, (i + 1) / n)
        r = ra + (rb - ra) * (i + .5) / n
        d = q - p; rot = Vector((1, 0, 0)).rotation_difference(d.normalized())
        _el('CAPSULE', (p + q) / 2, rot, r / VIS, (d.length / 2, 1, 1), bones, paint)


def mid(a, b, t=.5): return V(a).lerp(V(b), t)


# torso: a deep barrel chest, a hunched back, a belly hanging forward, heavy shoulders and traps
blob(V('chest') + Vector((0, .02, .10)), (.40, .30, .30), {'chest': 1})
blob(V('chest') + Vector((0, .14, .26)), (.36, .22, .22), {'chest': .7, 'neck': .3})               # the hump over the shoulders
for s, x in (('l', 1), ('r', -1)):
    blob(V('chest') + Vector((x * .17, -.17, .14)), (.17, .10, .13), {'chest': 1})                # pectorals
    blob(V('upperarm.' + s) + Vector((-x * .05, .01, .0)), (.17, .16, .17), {'upperarm.' + s: .6, 'shoulder.' + s: .4})   # deltoid
    blob(mid('shoulder.' + s, 'upperarm.' + s, .45) + Vector((0, .06, .08)), (.18, .12, .09), {'shoulder.' + s: .6, 'chest': .4})  # trapezius
    blob(V('chest') + Vector((x * .28, .10, -.04)), (.12, .16, .26), {'chest': 1})                # lats
blob(V('spine') + Vector((0, -.08, .06)), (.37, .33, .27), {'spine': 1})                           # belly
blob(V('spine') + Vector((0, -.15, -.06)), (.30, .24, .20), {'spine': .7, 'hips': .3})
blob(V('hips') + Vector((0, .04, .0)), (.36, .27, .20), {'hips': 1})                               # pelvis
blob(V('hips') + Vector((0, .16, -.06)), (.30, .14, .17), {'hips': 1})                             # seat
# neck and head: thick neck pushed forward, heavy brow, jaw, bulbous nose, pointed ears
limb(V('neck') + Vector((0, .06, -.12)), V('head') + Vector((0, .02, .02)), .21, .16, {'neck': 1})
H = V('head')
blob(H + Vector((0, .0, .13)), (.16, .17, .17), {'head': 1})                                       # cranium
blob(H + Vector((0, -.08, .17)), (.15, .12, .10), {'head': 1})                                     # forehead
blob(H + Vector((0, -.20, .13)), (.17, .07, .065), {'head': 1})                                    # brow ridge, jutting
blob(H + Vector((0, -.10, -.01)), (.17, .15, .11), {'head': 1})                                    # jaw, wide
blob(H + Vector((0, -.21, -.04)), (.14, .07, .07), {'head': 1})                                    # underbite: the lower lip pushed out
blob(H + Vector((0, -.15, -.10)), (.10, .08, .06), {'head': 1})                                    # chin under it
blob(H + Vector((0, -.26, .06)), (.065, .07, .075), {'head': 1}, 'nose')                           # nose: long, heavy
blob(H + Vector((0, -.30, .015)), (.075, .055, .05), {'head': 1}, 'nose')                          # its bulb, drooping
for s, x in (('l', 1), ('r', -1)):
    blob(H + Vector((x * .045, -.30, .005)), (.03, .03, .025), {'head': 1}, 'nose')                # nostril wings
    blob(H + Vector((x * .075, -.215, .085)), (.034, .03, .03), {'head': 1}, neg=True)             # eye socket under the brow
    blob(H + Vector((x * .12, -.16, .03)), (.07, .06, .055), {'head': 1})                          # cheek
    ear = Quaternion((0, 0, 1), x * math.radians(-20)) @ Quaternion((0, 1, 0), x * math.radians(-18))
    blob(H + Vector((x * .20, -.02, .13)), (.11, .025, .06), {'head': 1}, 'ear', ear)              # ear, out to the side
    blob(H + Vector((x * .30, .02, .17)), (.06, .018, .035), {'head': 1}, 'ear', ear)              # its long point
# arms: thick, long, a heavy forearm, a big hand with thick fingers
for s, x in (('l', 1), ('r', -1)):
    ua, la, hd, tip = 'upperarm.' + s, 'lowerarm.' + s, 'hand.' + s, 'handtip.' + s
    limb(V(ua), V(la), .15, .12, {ua: 1})
    blob(mid(ua, la, .4) + Vector((0, -.05, 0)), (.10, .09, .17), {ua: 1})                          # biceps
    ball(V(la), .12, {ua: .5, la: .5})                                                              # elbow
    limb(V(la), V(hd), .14, .10, {la: 1})
    blob(mid(la, hd, .3) + Vector((x * .02, .0, 0)), (.13, .12, .17), {la: 1})                      # forearm bulk
    ball(V(hd), .09, {la: .4, hd: .6})                                                              # wrist
    palm = V(hd).lerp(V(tip), .35)
    blob(palm, (.08, .13, .15), {hd: 1})                                                            # palm and back of the hand
    d = (V(tip) - V(hd)).normalized()
    for k, off in enumerate((-.075, -.025, .025, .075)):                                           # four fingers, curled
        base = palm + Vector((0, off, 0)) + d * .10
        mid1 = base + d * .07 + Vector((-x * .02, 0, 0))
        end = mid1 + d * .03 + Vector((-x * .05, 0, .0))
        ball(base, .042, {hd: 1})                                                                   # knuckle
        limb(base, mid1, .04, .036, {hd: 1}, n=1); limb(mid1, end, .036, .03, {hd: 1}, n=1)
        ball(end + Vector((-x * .016, 0, -.004)), .022, {hd: 1}, 'nail')
    thumb0 = V(hd).lerp(palm, .6) + Vector((0, -.08, 0))
    limb(thumb0, thumb0 + Vector((-x * .03, -.06, -.07)), .035, .028, {hd: 1}, n=2)
# legs: thick thighs and calves, big knees, broad feet with stubby toes and dark nails
for s, x in (('l', 1), ('r', -1)):
    ul, ll, ft, to, tt = 'upperleg.' + s, 'lowerleg.' + s, 'foot.' + s, 'toes.' + s, 'toetip.' + s
    limb(V(ul), V(ll), .22, .15, {ul: 1})
    blob(mid(ul, ll, .35) + Vector((0, -.06, 0)), (.15, .14, .22), {ul: 1})                         # quadriceps
    ball(V(ll) + Vector((0, -.03, 0)), .14, {ul: .5, ll: .5})                                       # knee
    limb(V(ll), V(ft), .15, .10, {ll: 1})
    blob(mid(ll, ft, .3) + Vector((0, .06, 0)), (.12, .11, .17), {ll: 1})                           # calf
    ball(V(ft), .10, {ll: .4, ft: .6})                                                              # ankle
    blob(mid(ft, to, .45) + Vector((0, 0, -.04)), (.16, .21, .08), {ft: 1})                         # the foot, broad
    blob(V(ft) + Vector((0, .08, -.06)), (.08, .07, .06), {ft: 1})                                  # heel
    for k, off in enumerate((-.10, -.035, .03, .095)):
        c = V(to) + Vector((x * off, -.08, -.005))
        ball(c, .048 - .005 * abs(k - 1.5), {to: 1})
        ball(c + Vector((0, -.04, .016)), .025, {to: 1}, 'nail')

# the surface
dg = bpy.context.evaluated_depsgraph_get()
me = bpy.data.meshes.new_from_object(field_obj.evaluated_get(dg))
body = bpy.data.objects.new('Troll_Body', me); sc.collection.objects.link(body)
bpy.data.objects.remove(field_obj)
bm = bmesh.new(); bm.from_mesh(me)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
bm.to_mesh(me); bm.free()
n0 = len(me.vertices)
if n0 > 32000:                                   # the field's even grid is denser than a game body needs
    dec = body.modifiers.new('fewer', 'DECIMATE'); dec.ratio = 32000 / n0
    bpy.context.view_layer.objects.active = body; bpy.ops.object.modifier_apply(modifier=dec.name)
print('TROLL_MATH body %d vertices (field surface %d) from %d shapes' % (len(me.vertices), n0, len(ELEMS)), flush=True)


def fields(P):
    """each shape's field at points P (n x 3), as Blender sums them (stiffness x (1 - d^2/r^2)^3 inside the radius)"""
    F = np.zeros((len(P), len(ELEMS)))
    for i, (kind, c, q, r, size, _, _) in enumerate(ELEMS):
        Rm = np.array(q.to_matrix())
        d = (P - np.array(c)) @ Rm                                  # into the shape's own frame
        if kind == 'CAPSULE': d[:, 0] -= np.clip(d[:, 0], -size[0], size[0])
        if kind == 'ELLIPSOID': d = d / np.array(size)
        q2 = 1 - (d ** 2).sum(1) / r ** 2
        F[:, i] = np.where(q2 > 0, STIFF * q2 ** 3, 0)
    return F


# ---- clothing and the rest, also from math ---------------------------------------------------------------------------
parts = []     # (object, {bone: weight} function of vertex position or None, paint)


def new_obj(name, verts, faces):
    m = bpy.data.meshes.new(name); m.from_pydata([tuple(v) for v in verts], [], faces); m.update()
    o = bpy.data.objects.new(name, m); sc.collection.objects.link(o); return o


def body_radius(z, ang, top=True):
    """how far out the body surface is at height z in direction ang (from the hips' axis), by marching the field"""
    d = np.array([math.sin(ang), -math.cos(ang), 0.0])
    o = np.array([0, .04, z])
    ts = np.linspace(0, .8, 161)
    f = fields(o + ts[:, None] * d).sum(1)
    out = ts[np.argmax(f < .6)] if (f < .6).any() else .8
    return out


# loincloth: a ragged hide skirt hanging from a rope belt, longer flaps front and back, a jagged hem
NA, NZ = 72, 14
belt_z, belt_r = 1.17, None
ring = [body_radius(belt_z, 2 * math.pi * i / NA) + .025 for i in range(NA)]
verts, faces = [], []
for j in range(NZ + 1):
    for i in range(NA):
        a = 2 * math.pi * i / NA
        front = math.cos(a)                        # +1 at the front (-Y), -1 at the back
        flap = max(0.0, abs(front) - .55) / .45     # the front and back flaps hang lower than the sides
        length = .30 + .24 * flap + .05 * rng.random()
        t = j / NZ
        z = belt_z - t * length
        flare = 1 + .28 * t * (1 - .5 * flap)
        r = ring[i] * flare
        # tatters: the hem cut into teeth, the bottom rows pulled to points
        if j == NZ: z -= (.03 + .10 * rng.random()) * (i % 3 != 1)
        verts.append((math.sin(a) * r, .04 - math.cos(a) * r, z))
for j in range(NZ):
    for i in range(NA):
        a, b = j * NA + i, j * NA + (i + 1) % NA
        faces.append((a, b, b + NA, a + NA))
skirt = new_obj('Troll_Loincloth', verts, faces)
sol = skirt.modifiers.new('thick', 'SOLIDIFY'); sol.thickness = .012; sol.offset = 1
parts.append((skirt, 'cloth'))
# belt and wrist wraps: rope rings (tori) following the body
def torus(name, centre, axis, R, r, n=48, m=8, paint='leather'):
    q = Vector((0, 0, 1)).rotation_difference(Vector(axis).normalized())
    vs, fs = [], []
    for i in range(n):
        a = 2 * math.pi * i / n
        for k in range(m):
            b = 2 * math.pi * k / m
            p = Vector(((R + r * math.cos(b)) * math.cos(a), (R + r * math.cos(b)) * math.sin(a), r * math.sin(b)))
            vs.append(Vector(centre) + q @ p)
    for i in range(n):
        for k in range(m):
            fs.append((i * m + k, i * m + (k + 1) % m, ((i + 1) % n) * m + (k + 1) % m, ((i + 1) % n) * m + k))
    o = new_obj(name, vs, fs); parts.append((o, paint)); return o
for k in range(3):
    zz = belt_z + .02 - k * .028
    rr = np.mean([body_radius(zz, 2 * math.pi * i / NA) for i in range(0, NA, 6)]) + .035
    o = torus('Troll_Belt%d' % k, (0, .04, zz), (0, 0, 1), rr, .017, paint='rope')
    o.scale = (1.0, .86, 1.0)
for s in 'lr':
    for k in range(3):
        c = mid('lowerarm.' + s, 'hand.' + s, .58 + k * .09)
        torus('Troll_Wrap_%s%d' % (s, k), c, V('hand.' + s) - V('lowerarm.' + s), .125 - k * .008, .016, paint='leather')
# tusks: curved cones from the lower jaw
for s, x in (('l', 1), ('r', -1)):
    base = H + Vector((x * .085, -.265, -.035))
    vs, fs = [], []
    n = 10; m = 8
    for i in range(n + 1):
        t = i / n
        c = base + Vector((x * .012 * t, -.03 * t * t, .11 * t))
        r = .027 * (1 - t) + .003
        for k in range(m):
            b = 2 * math.pi * k / m
            vs.append(c + Vector((r * math.cos(b), r * math.sin(b), 0)))
    for i in range(n):
        for k in range(m):
            fs.append((i * m + k, i * m + (k + 1) % m, (i + 1) * m + (k + 1) % m, (i + 1) * m + k))
    parts.append((new_obj('Troll_Tusk_' + s, vs, fs), 'tusk'))
# eyes: small dark balls in the sockets
for s, x in (('l', 1), ('r', -1)):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=.024, location=H + Vector((x * .075, -.225, .08)), segments=12, ring_count=8)
    o = bpy.context.object; o.name = 'Troll_Eye_' + s; parts.append((o, 'eye'))
# hair: long dark strands from the back and top of the head, down over the shoulders and back
for i in range(110):
    a = rng.uniform(-2.6, 2.6)                     # around the head (0 = straight back, +-2.6 = beside the face)
    up = rng.uniform(.06, .22)
    root = H + Vector((.155 * math.sin(a), .01 + .15 * math.cos(a), up))
    L = rng.uniform(.40, .85) * (.75 if abs(a) > 2.0 else 1)
    vs, fs = [], []
    n, m = 12, 5
    pts = []
    p = root.copy(); d = Vector((math.sin(a) * .45, .35 + .35 * math.cos(a), .1)).normalized()
    for k in range(n + 1):
        pts.append(p.copy())
        d = (d + Vector((0, .06, -.35))).normalized()
        p = p + d * L / n
        # kept outside the body: pushed out along the field's falloff
        for _ in range(4):
            f = fields(np.array([list(p)])).sum()
            if f < .45: break
            p = p + Vector((math.sin(a) * .5, .7 + .3 * math.cos(a), 0)).normalized() * .02
    for k, c in enumerate(pts):
        r = .022 * (1 - k / n) + .006
        for j in range(m):
            b = 2 * math.pi * j / m
            vs.append(c + Vector((r * math.cos(b), r * math.sin(b) * .6, 0)))
    for k in range(n):
        for j in range(m):
            fs.append((k * m + j, k * m + (j + 1) % m, (k + 1) * m + (j + 1) % m, (k + 1) * m + j))
    parts.append((new_obj('Troll_Hair%d' % i, vs, fs), 'hair'))

# ---- paint (vertex colours, by math) --------------------------------------------------------------------------------
def srgb(r, g, b): return np.array([(c / 255) ** 2.2 for c in (r, g, b)])
SKIN, SKIN_DARK, SKIN_LIGHT = srgb(150, 154, 126), srgb(104, 118, 98), srgb(170, 168, 140)
PINK, NAIL = srgb(176, 128, 118), srgb(58, 56, 60)
LEATHER, LEATHER_DARK, LEATHER_TAN = srgb(112, 72, 44), srgb(66, 46, 34), srgb(150, 108, 66)
ROPE, TUSK, EYE, HAIR, HAIR_LIGHT = srgb(98, 66, 42), srgb(226, 214, 180), srgb(30, 24, 20), srgb(44, 37, 32), srgb(78, 64, 52)


def noise3(P, scale, seed):
    """smooth value noise (trilinear between hashed lattice values), 0..1"""
    g = P / scale
    i = np.floor(g).astype(np.int64); f = g - i; f = f * f * (3 - 2 * f)
    def h(ix, iy, iz):
        v = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791) ^ (seed * 2654435761)
        v = (v ^ (v >> 13)) * 1274126177
        return ((v ^ (v >> 16)) & 0xffff) / 65535.0
    out = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                out = out + w * h(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz)
    return out


def coords(o):
    m = o.data; a = np.empty(len(m.vertices) * 3); m.vertices.foreach_get('co', a); a = a.reshape(-1, 3)
    M = np.array(o.matrix_world); return a @ M[:3, :3].T + M[:3, 3]


def paint(o, cols):
    m = o.data
    attr = m.color_attributes.new('paint', 'FLOAT_COLOR', 'POINT')
    attr.data.foreach_set('color', np.hstack([cols, np.ones((len(cols), 1))]).ravel())


P = coords(body); Fb = fields(P)
own = np.argmax(Fb, 1)
kinds = np.array([e[6] for e in ELEMS])
blot = noise3(P, .16, 1); fine = noise3(P, .045, 2)
mott = np.clip((blot - .48) * 4, 0, 1)
col = SKIN * (1 - mott[:, None]) + SKIN_DARK * mott[:, None]
col = col * (.9 + .2 * fine[:, None])
belly = np.exp(-((P[:, 2] - 1.32) / .22) ** 2) * (P[:, 1] < -.15)
col = col * (1 - .4 * belly[:, None]) + SKIN_LIGHT * .4 * belly[:, None]
# creases darker: where the field rises fastest inward (between shapes), from the count of shapes sharing the point
crowd = (Fb > .05).sum(1)
col = col * (1 - .05 * np.clip(crowd - 2, 0, 4))[:, None]
share = Fb / np.maximum(Fb.sum(1, keepdims=True), 1e-9)
for tag, c in (('nose', PINK), ('ear', PINK), ('nail', NAIL)):
    w = share[:, kinds == tag].sum(1)
    w = np.clip((w - .25) / .5, 0, 1)
    col = col * (1 - w[:, None]) + c * w[:, None]
paint(body, col)
for o, tag in parts:
    Q = coords(o)
    if tag == 'cloth':
        n1, n2 = noise3(Q, .12, 3), noise3(Q, .03, 4)
        c = np.where((n1 > .62)[:, None], LEATHER_DARK, np.where((n1 < .3)[:, None], LEATHER_TAN, LEATHER))
        c = c * (.8 + .35 * n2[:, None])
    elif tag == 'hair':
        c = np.tile(HAIR, (len(Q), 1)) * 1.0
        c = np.where((noise3(Q, .05, 5) > .7)[:, None], HAIR_LIGHT, c)
    else:
        base = {'leather': LEATHER_DARK, 'rope': ROPE, 'tusk': TUSK, 'eye': EYE}[tag]
        c = np.tile(base, (len(Q), 1)) * (.85 + .3 * noise3(Q, .02, 6)[:, None])
    paint(o, c)

# ---- one mesh, skinned by the fields --------------------------------------------------------------------------------
bones = [b[0] for b in BONES]


def weights_from_fields(Q):
    F = fields(Q)
    W = np.zeros((len(Q), len(bones)))
    for i, e in enumerate(ELEMS):
        for b, sh in e[5].items(): W[:, bones.index(b)] += F[:, i] * sh
    empty = W.sum(1) < 1e-9
    if empty.any():                                 # beyond every shape (hair tips, the hem): the nearest shape's bones
        cs = np.array([list(e[1]) for e in ELEMS])
        near = np.argmin(((Q[empty, None, :] - cs[None]) ** 2).sum(-1), 1)
        for k, i in zip(np.nonzero(empty)[0], near):
            for b, sh in ELEMS[i][5].items(): W[k, bones.index(b)] += sh
    return W


for o, tag in parts:
    for mo in list(o.modifiers):
        bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier=mo.name)
all_objs = [body] + [o for o, _ in parts]
bpy.ops.object.select_all(action='DESELECT')
for o in all_objs: o.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
body.name = 'Troll_Body'
Q = coords(body)
W = weights_from_fields(Q)
# hair hangs from the head, its lower strands ride the chest; the loincloth is left to the build's cloth weighting
for b in bones: body.vertex_groups.new(name=b)
order = np.argsort(-W, 1)[:, :4]
for vi in range(len(Q)):
    top = order[vi]; w = W[vi, top]; w = w / max(w.sum(), 1e-9)
    for bi, wi in zip(top, w):
        if wi > .01: body.vertex_groups[int(bi)].add([vi], float(wi), 'REPLACE')
body.parent = arm
mod = body.modifiers.new('Armature', 'ARMATURE'); mod.object = arm
bpy.ops.object.shade_smooth()
print('TROLL_MATH skinned %d vertices to %d bones by their fields' % (len(Q), len(bones)), flush=True)

# ---- UVs and the baked texture -------------------------------------------------------------------------------------
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); bpy.context.view_layer.objects.active = body
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=.003)
bpy.ops.object.mode_set(mode='OBJECT')
img = bpy.data.images.new('Troll_math_texture', 2048, 2048)
mat = bpy.data.materials.new('Troll'); mat.use_nodes = True
nt = mat.node_tree; bsdf = nt.nodes['Principled BSDF']
attr = nt.nodes.new('ShaderNodeVertexColor'); attr.layer_name = 'paint'
nt.links.new(attr.outputs['Color'], bsdf.inputs['Base Color'])
bsdf.inputs['Roughness'].default_value = .8
tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = img; nt.nodes.active = tex
body.data.materials.clear(); body.data.materials.append(mat)
sc.render.engine = 'CYCLES'; sc.cycles.samples = 1
try:
    prefs = bpy.context.preferences.addons['cycles'].preferences; prefs.compute_device_type = 'CUDA'; prefs.get_devices()
    for d in prefs.devices: d.use = d.type == 'CUDA'
    sc.cycles.device = 'GPU'
except Exception: pass
sc.render.bake.use_pass_direct = False; sc.render.bake.use_pass_indirect = False; sc.render.bake.margin = 8
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'})
img.filepath_raw = str(OUT / 'Troll_math_texture.png'); img.file_format = 'PNG'; img.save()
nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
print('TROLL_MATH texture baked', OUT / 'Troll_math_texture.png', flush=True)

# ---- previews and the source FBX -----------------------------------------------------------------------------------
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.9
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun)
sun.rotation_euler = (math.radians(50), 0, math.radians(-30)); sun.data.energy = 3
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (.35, .36, .38, 1)
sc.render.resolution_x, sc.render.resolution_y = 700, 900; sc.cycles.samples = 48
for name, loc in (('front', (0, -8, 1.3)), ('side', (8, 0, 1.3)), ('three_quarter', (5.6, -5.6, 1.6)), ('back', (0, 8, 1.3))):
    cam.location = loc; cam.rotation_euler = (Vector((0, 0, 1.25)) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = str(OUT / ('preview_%s.png' % name)); bpy.ops.render.render(write_still=True)
bpy.data.objects.remove(cam); bpy.data.objects.remove(sun)
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); arm.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(OUT / 'Troll_math.fbx'), use_selection=True, apply_scale_options='FBX_SCALE_ALL',
                         add_leaf_bones=False, bake_anim=False, object_types={'ARMATURE', 'MESH'}, mesh_smooth_type='FACE')
print('TROLL_MATH_DONE', OUT / 'Troll_math.fbx', len(Q), 'vertices', flush=True)
