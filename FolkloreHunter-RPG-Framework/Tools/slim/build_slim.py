# Builds a game model on the slim skeleton: the Codex body (Models/Codex/<body>.fbx, unchanged there), its bones given
# the game's names, hand slots, the hunter's weapons gripped in them (the hunt pieces from SourceArt/Hunter.blend,
# re-seated and sized for a 1.8 m hunter), and every clip from moves.py; exported as Models/<name>.fbx with its texture.
#   blender -b --python Tools/slim/build_slim.py -- <Codex body .fbx> <name> [--pieces a,b] [--size piece=metres] [--helmet <fbx>]
#       [--monster] [--m2m <folder of the Mesh2Motion human .glb files>] [--mb <mb_out.json> --soma <soma.json>]
#       [--villager --cloth <hue 0-1>,<saturation scale>,<value scale>] [--texture <png>] [--smooth <passes>]
# --texture uses another picture as the body's texture (the flank-repaired copy from paint_sides.py).
# --pieces keeps only those weapons (a monster's own), --size changes a weapon's length, --helmet puts a separate helmet
# mesh (named "Helmet", so the helm part breaks it off) on the head. --villager makes a townsperson: no weapons, only the
# everyday captured moves, and --cloth recolours the outfit's red cloth (its sash, cape and trim) to its own colour.
import bpy, sys, shutil, math
from pathlib import Path
from mathutils import Vector, Matrix
sys.path.insert(0, str(Path(__file__).resolve().parent))
import choreo, moves

argv = sys.argv[sys.argv.index('--') + 1:]
fbx, name = Path(argv[0]), argv[1]
def opt(flag, default=None):
    return argv[argv.index(flag) + 1] if flag in argv else default


keep = opt('--pieces'); keep = set(keep.split(',')) if keep else None
sizes = dict(x.split('=') for x in opt('--size', '').split(',') if x)
helmet = opt('--helmet')
villager = '--villager' in argv
weapons = keep != set(['none']) and not villager
root = Path.home() / 'Documents/FolkloreHunter-RPG-Framework'
dest = root / 'Assets/Resources/Models'

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(fbx))
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE'); arm.name = 'Rig'
choreo.rename_bones(arm); choreo.add_hand_slots(arm)
body = next(o for o in bpy.data.objects if o.type == 'MESH'); body.name = name + '_Body'
smooth = int(opt('--smooth', '0'))
if smooth:
    # the auto-rig's skin weights hand over too sharply at the shoulders, hips and spine: big swings tear the skin there
    # (the 3D mesh check measured it). Those bones' weights are averaged with the neighbouring vertices', then every
    # vertex keeps its 4 strongest bones, normalised. (numpy: Blender's smoothing operator will not run in the background)
    import numpy as np
    me = body.data; V = len(me.vertices)
    E = np.empty(len(me.edges) * 2, np.int64); me.edges.foreach_get('vertices', E); E = E.reshape(-1, 2)
    deform = [g for g in body.vertex_groups if g.name in arm.data.bones]
    col = {g.index: i for i, g in enumerate(deform)}
    W = np.zeros((V, len(deform)))
    for v in me.vertices:
        for ge in v.groups:
            if ge.group in col: W[v.index, col[ge.group]] = ge.weight
    deg = np.bincount(E.ravel(), minlength=V).astype(float); deg[deg == 0] = 1
    soft = [col[g.index] for g in deform if g.name in ('upperarm.l', 'upperarm.r', 'shoulder.l', 'shoulder.r', 'upperleg.l', 'upperleg.r', 'hips', 'spine', 'chest')]
    for _ in range(smooth):
        S = W[:, soft]
        acc = np.zeros_like(S); np.add.at(acc, E[:, 0], S[E[:, 1]]); np.add.at(acc, E[:, 1], S[E[:, 0]])
        W[:, soft] = S + .5 * (acc / deg[:, None] - S)
    # 4 strongest, normalised
    order = np.argsort(-W, axis=1); keep = np.zeros_like(W, bool); np.put_along_axis(keep, order[:, :4], True, axis=1)
    W = np.where(keep, W, 0); tot = W.sum(1, keepdims=True); tot[tot == 0] = 1; W = W / tot
    for g in deform:
        j = col[g.index]
        g.remove(list(range(V)))
        nz = np.nonzero(W[:, j] > 1e-4)[0]
        for i in nz: g.add([int(i)], float(W[i, j]), 'REPLACE')
    print('SLIM_SMOOTHED weights x%d over %d bones' % (smooth, len(soft)), flush=True)
rig = choreo.Rig(arm)
k = rig.k

# Each piece: (name, parent bone, piece axis along the slot's Y (blade / outward face / upper limb), piece axis along
# the slot's Z (edge / top / toward the target), real length of the piece's longest side in metres, grip point mode)
PIECES = [
    ('1H_Sword', 'handslot.r', (0, 0, 1), (1, 0, 0), .95),
    ('Round_Shield', 'handslot.l', (0, -1, 0), (0, 0, 1), .62),
    ('Hunt_TowerShield', 'handslot.l', (0, -1, 0), (0, 0, 1), 1.15),
    ('Hunt_Lance', 'handslot.r', (0, 0, 1), (1, 0, 0), 2.5),
    ('Hunt_Katana', 'handslot.r', (0, 0, 1), (1, 0, 0), 1.25),        # the game shows it at 80 %
    ('Hunt_KatanaGlow', 'handslot.r', (0, 0, 1), (1, 0, 0), None),     # as the katana
    ('Hunt_Bow', 'handslot.l', (0, 0, 1), (1, 0, 0), 1.35),
    ('Hunt_NockedArrow', 'handslot.r', (0, 0, 1), (1, 0, 0), .78),
    ('Hunt_Arrow', 'handslot.r', (0, 0, 1), (1, 0, 0), .78),
]


def axes_matrix(ya, za):
    """Piece coordinates -> slot coordinates: piece axis ya onto slot Y, za onto slot Z."""
    y = Vector(ya); z = Vector(za); x = y.cross(z)
    P = Matrix((x, y, z))                   # rows: piece-space directions of slot X, Y, Z
    return P.to_4x4()


def seat(src, bone, ya, za, length, scale=None, grip=None):
    """Copies the piece's mesh into slot space (grip at the slot, axes as given, scaled) and parents it to the bone."""
    me = src.data.copy()
    # the old slot's origin in the piece's coordinates is its grip
    g = grip if grip is not None else Vector((0, 0, 0))
    size = max(max(v.co[i] for v in me.vertices) - min(v.co[i] for v in me.vertices) for i in range(3))
    s = scale if scale is not None else (length * k) / size
    M = axes_matrix(ya, za) @ Matrix.Scale(s, 4) @ Matrix.Translation(-g)
    me.transform(M)
    o = bpy.data.objects.new(src.name.replace('__src', ''), me)
    bpy.context.scene.collection.objects.link(o)
    o.parent = arm; o.parent_type = 'BONE'; o.parent_bone = bone
    b = arm.data.bones[bone]
    o.matrix_world = arm.matrix_world @ b.matrix_local          # at the bone's head, in its frame
    return o, s


if weapons:
    lib = root / 'SourceArt/Hunter.blend'
    want = [p[0] for p in PIECES] + ['Hunt_Quiver']
    with bpy.data.libraries.load(str(lib), link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n in want]
    loaded = {o.name: o for o in dst.objects if o}
    for o in loaded.values(): o.name = o.name + '__src'        # the seated copies take the plain names the game looks for
    # grips (the old hand slots' origins in each piece's coordinates, measured in Hunter.blend)
    GRIPS = {'1H_Sword': (0, 0, -.033), 'Round_Shield': (0, .156, -.017), 'Hunt_TowerShield': (0, .156, -.017), 'Hunt_Lance': (0, 0, -.033),
             'Hunt_Katana': (0, 0, -.033), 'Hunt_KatanaGlow': (0, 0, -.033), 'Hunt_Bow': (0, 0, 0), 'Hunt_NockedArrow': (0, 0, 0), 'Hunt_Arrow': (0, 0, -.033)}
    scales = {}
    for nm, bone, ya, za, length in PIECES:
        if keep and nm not in keep and not (nm == 'Hunt_KatanaGlow' and 'Hunt_Katana' in keep): continue
        if nm in sizes: length = float(sizes[nm])
        src = loaded.get(nm)
        if not src: print('SLIM_MISSING', nm); continue
        o, s = seat(src, bone, ya, za, length, scales.get('Hunt_Katana') if nm == 'Hunt_KatanaGlow' else None, Vector(GRIPS[nm]))
        scales[nm] = s
        print('SLIM_PIECE', o.name, bone, 'scale %.3f' % s, flush=True)
    q = loaded.get('Hunt_Quiver')
    if q and (not keep or 'Hunt_Quiver' in keep):
        # on the back, behind the right shoulder, fletchings up and tilted out
        me = q.data.copy(); size = max(max(v.co[i] for v in me.vertices) - min(v.co[i] for v in me.vertices) for i in range(3))
        s = .7 * k / size
        up = Vector((-.3, .2, 1)).normalized(); fwd = Vector((0, -1, 0)); x = fwd.cross(up).normalized(); fwd = up.cross(x)
        R = Matrix((x, fwd, up)).transposed().to_4x4()
        me.transform(Matrix.Scale(s, 4))
        o = bpy.data.objects.new('Hunt_Quiver', me); bpy.context.scene.collection.objects.link(o)
        o.parent = arm; o.parent_type = 'BONE'; o.parent_bone = 'chest'
        c = rig.h0['chest'] + Vector((-.1, .17, .02)) * k
        o.matrix_world = arm.matrix_world @ (Matrix.Translation(c) @ R)
        print('SLIM_PIECE Hunt_Quiver chest scale %.3f' % s, flush=True)
    # only the meshes were wanted: the old file's rig and every clip that came with it go (no chibi motion here)
    for o in list(bpy.data.objects):
        if o.name.endswith('__src') or (o.type == 'ARMATURE' and o != arm): bpy.data.objects.remove(o)
    for a in list(bpy.data.actions): bpy.data.actions.remove(a)

if helmet:
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=helmet)
    parts = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
    for o in [o for o in bpy.data.objects if o not in before and o.type != 'MESH']: bpy.data.objects.remove(o)
    if parts:
        h = parts[0]
        if len(parts) > 1:
            bpy.ops.object.select_all(action='DESELECT')
            for o in parts: o.select_set(True)
            bpy.context.view_layer.objects.active = h; bpy.ops.object.join()
        h.parent = None; bpy.context.view_layer.update()
        h.data.transform(h.matrix_world); h.matrix_world = Matrix()
        lo = Vector([min(v.co[i] for v in h.data.vertices) for i in range(3)]); hi = Vector([max(v.co[i] for v in h.data.vertices) for i in range(3)])
        print('SLIM_HELMET_SIZE', tuple(round(v, 3) for v in hi - lo), flush=True)
        if (hi.y - lo.y) > 1.3 * (hi.z - lo.z):     # lying on its back in the file: stand it up
            h.data.transform(Matrix.Rotation(math.radians(90), 4, 'X'))
            lo = Vector([min(v.co[i] for v in h.data.vertices) for i in range(3)]); hi = Vector([max(v.co[i] for v in h.data.vertices) for i in range(3)])
        c = (lo + hi) / 2
        s = (.43 * rig.height / 2.4) / max(1e-4, hi.x - lo.x)          # Codex fitted it 0.43 m wide on its 2.4 m Warden
        # seated on the skull itself (the vertices the head bone carries): its crown a little above the skull's top,
        # centred over the skull, so the head fills the helmet's bowl
        g = body.vertex_groups.get('head')
        skull = [body.matrix_world @ v.co for v in body.data.vertices if g and any(e.group == g.index and e.weight > .5 for e in v.groups)]
        if skull:
            top = max(p.z for p in skull)
            mid = sum(skull, Vector()) / len(skull)
            at = Vector((mid.x, mid.y, top + .04 * rig.height / 2.4 - (hi.z - c.z) * s))
            print('SLIM_SKULL top %.3f centre %.3f %.3f, %d verts' % (top, mid.x, mid.y, len(skull)), flush=True)
        else:
            at = rig.h0['head'] + Vector((0, 0, .18 * rig.height / 2.4))
        h.data.transform(Matrix.Translation(at) @ Matrix.Scale(s, 4) @ Matrix.Translation(-c))
        h.name = name + '_Helmet'; h.data.name = name + '_Helmet'      # the game breaks off the mesh whose name ends "Helmet"
        ht = Path(helmet).with_name(Path(helmet).stem + '_texture.png')
        if ht.exists(): shutil.copyfile(ht, dest / (name + '_Helmet_texture.png'))
        mw = h.matrix_world.copy()
        h.parent = arm; h.parent_type = 'BONE'; h.parent_bone = 'head'; h.matrix_world = mw
        print('SLIM_HELMET scale %.3f' % s, flush=True)

made = [] if villager else [fn(rig) for fn in (moves.MONSTER if '--monster' in argv else moves.CLIPS)]
mb = opt('--mb'); soma = opt('--soma')
if mb and soma and not villager:
    # the game's own moves with MotionBricks' natural motion between their key poses (Tools/motionbricks)
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'motionbricks'))
    import mb_bridge
    mb_bridge.load(rig, mb_bridge.Soma(soma), mb)
    rig.arm.animation_data.action = None
m2m = opt('--m2m')
if m2m:
    # the everyday moves from CC0 motion capture (Mesh2Motion), replacing the keyposed versions of the same name
    import retarget
    src = retarget.Source(m2m)
    retarget.apply(rig, src, retarget.MONSTER_MOCAP if '--monster' in argv else retarget.MOCAP, retarget.VILLAGER if villager else None)
    src.remove()
    made = list(bpy.data.actions)                 # the keyposed clips a capture replaced are gone already
    rig.arm.animation_data.action = None
assert all('.' not in a.name for a in made) and len(bpy.data.actions) == len(made), 'stray clips: ' + ', '.join(a.name for a in bpy.data.actions)
arm.animation_data.action = None
print('SLIM_CLIPS', len(made), ' '.join(a.name for a in made), flush=True)

for o in list(bpy.data.objects):
    if o.type not in ('ARMATURE', 'MESH'): bpy.data.objects.remove(o)
# the file's own pose is the rest pose (not the last frame a clip left behind): the game sizes the body from it
for pb in arm.pose.bones: pb.matrix_basis = Matrix()
bpy.context.view_layer.update()
out = dest / (name + '.fbx')
bpy.ops.export_scene.fbx(filepath=str(out), use_selection=False, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                         apply_scale_options='FBX_SCALE_ALL', bake_anim=True, bake_anim_use_nla_strips=False,
                         bake_anim_use_all_actions=True, bake_anim_simplify_factor=0, axis_forward='-Z', axis_up='Y',
                         path_mode='COPY', embed_textures=False)
tex = Path(opt('--texture')) if opt('--texture') else fbx.with_name(fbx.stem + '_texture.png')
cloth = opt('--cloth')
if tex.exists() and cloth:
    # the outfit's red cloth (strongly red, saturated texels) turned to the villager's colour; leather, steel and skin stay
    import numpy as np
    hue, sat, val = (float(x) for x in cloth.split(','))
    img = bpy.data.images.load(str(tex))
    px = np.empty(len(img.pixels), dtype=np.float32); img.pixels.foreach_get(px); px = px.reshape(-1, 4)
    r, g, b = px[:, 0], px[:, 1], px[:, 2]
    mx = px[:, :3].max(1); mn = px[:, :3].min(1); d = mx - mn
    s_ = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    red = (r == mx) & (s_ > .38) & (g - b < .25 * d) & (np.abs(g - b) < .5 * d + 1e-6)
    v2 = np.clip(mx * val, 0, 1); s2 = np.clip(s_ * sat, 0, 1)
    h6 = hue * 6; i = np.floor(h6) % 6; f = h6 - np.floor(h6)
    p_, q_, t_ = v2 * (1 - s2), v2 * (1 - s2 * f), v2 * (1 - s2 * (1 - f))
    rgb = {0: (v2, t_, p_), 1: (q_, v2, p_), 2: (p_, v2, t_), 3: (p_, q_, v2), 4: (t_, p_, v2), 5: (v2, p_, q_)}[int(i)]
    for c in range(3): px[red, c] = rgb[c][red]
    img.pixels.foreach_set(px.ravel())
    img.filepath_raw = str(dest / (name + '_texture.png')); img.file_format = 'PNG'; img.save()
    print('SLIM_CLOTH %d of %d texels recoloured' % (int(red.sum()), len(px)), flush=True)
elif tex.exists(): shutil.copyfile(tex, dest / (name + '_texture.png'))
print('SLIM_EXPORTED', out, out.stat().st_size, flush=True)
