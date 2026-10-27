# Builds a game model on the slim skeleton: the Codex body (Models/Codex/<body>.fbx, unchanged there), its bones given
# the game's names, hand slots, the hunter's weapons gripped in them (the hunt pieces from SourceArt/Hunter.blend,
# re-seated and sized for a 1.8 m hunter), and every clip from moves.py; exported as Models/<name>.fbx with its texture.
#   blender -b --python Tools/slim/build_slim.py -- <Codex body .fbx> <name> [--pieces a,b] [--size piece=metres] [--helmet <fbx>]
#       [--monster] [--m2m <folder of the Mesh2Motion human .glb files>] [--mb <mb_out.json> --soma <soma.json>]
#       [--villager --cloth <hue 0-1>,<saturation scale>,<value scale>] [--texture <png>] [--smooth <passes>] [--footlock]
#       [--fingers] [--out <folder>] [--keep-clips "<source clip>=<Game_Clip>+<Game_Clip>,..."]
# --keep-clips keeps clips that came in the source file (a Tripo export's walk, run, idle...): each is sampled at 30
# frames a second, held in place (the game moves the body; the hips' travel over the clip is taken out) and stored under
# the game's clip names, replacing the keyposed clips of those names. A source clip is matched by the start of its name
# (after "Armature|", without ".001").
# --height <m> rescales a body made at another size (Tripo exports 1 m tall) to real height before anything else, so
# the game's weapon placement, hand slots and gait speeds read it as they read the other bodies; its kept clips'
# movements are scaled with it. --own-hair: the body has modelled hair (the game then adds no hairstyle), written
# beside the model as <name>_info.json.
# --lib-clips "<pack>:<clip>=<Game_Clip>+...,..." puts clips from SightForge's motion library (motionlib add) on this body
# through the retarget pose solver (it fits any proportions and rest pose; travel over the ground taken out).
# --fingers finds the fingers in the mesh, gives each three bones, and solves how they close round each held piece
# (Tools/slim/fingers.py); the curl angles go beside the model as <name>_grips.json for the game's HandGrip.
# --out writes the model somewhere else than the game's Models folder (trials).
# --footlock holds the planted feet of walks, jogs and runs still on the ground (Tools/sightforge/eyeslib/footslide_blender.py).
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
dest = Path(opt('--out')) if opt('--out') else root / 'Assets/Resources/Models'
dest.mkdir(parents=True, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(fbx))
# the source's own clips, kept aside when asked for (--keep-clips); the game's clips are made at 30 frames a second
src_fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
KEEP = {}
for _a in list(bpy.data.actions):
    if opt('--keep-clips'):
        _a.use_fake_user = True; KEEP['KEEP__' + _a.name.split('|')[-1].split('.')[0]] = _a; _a.name = 'KEEP__' + _a.name.split('|')[-1].split('.')[0]
bpy.context.scene.render.fps = 30; bpy.context.scene.render.fps_base = 1
if opt('--height'):
    _top = [o for o in bpy.data.objects if o.parent is None]
    _mesh = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
    bpy.context.view_layer.update()
    _zs = [(_mesh.matrix_world @ v.co).z for v in _mesh.data.vertices]
    _f = float(opt('--height')) / max(1e-4, max(_zs) - min(_zs))
    if abs(_f - 1) > .02:
        _arm0 = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
        for o in _top: o.scale = o.scale * _f
        bpy.context.view_layer.update()
        _s = _arm0.scale.x                                  # what the bones' rest positions (and clip moves) are multiplied by
        bpy.ops.object.select_all(action='DESELECT')
        for o in bpy.data.objects: o.select_set(True)
        bpy.context.view_layer.objects.active = _arm0
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        def _curves(a):
            try: return list(a.fcurves)
            except AttributeError: return [fc for l in a.layers for st in l.strips for cb in getattr(st, 'channelbags', []) for fc in cb.fcurves]
        for a in KEEP.values():
            for fc in _curves(a):
                if fc.data_path.endswith('.location'):
                    for kp in fc.keyframe_points:
                        kp.co[1] *= _s; kp.handle_left[1] *= _s; kp.handle_right[1] *= _s
        print('SLIM_HEIGHT %.3f m -> %s m (x%.3f)' % (max(_zs) - min(_zs), opt('--height'), _f), flush=True)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE'); arm.name = 'Rig'
# a source that comes with clips (Tripo) is left wearing one: back to the rest pose before anything is measured or
# seated on it (weapons seated on a posed hand were exported 0.3 to 0.7 m off their hand slots)
if arm.animation_data: arm.animation_data.action = None
for _pb in arm.pose.bones: _pb.matrix_basis = Matrix()
bpy.context.view_layer.update()
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
    order = np.argsort(-W, axis=1); top4 = np.zeros_like(W, bool); np.put_along_axis(top4, order[:, :4], True, axis=1)
    W = np.where(top4, W, 0); tot = W.sum(1, keepdims=True); tot[tot == 0] = 1; W = W / tot
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
    if arm.animation_data and arm.animation_data.action: arm.animation_data.action = None
    for pb in arm.pose.bones: pb.matrix_basis = Matrix()
    bpy.context.view_layer.update()
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
    for a in list(bpy.data.actions):
        if not a.name.startswith('KEEP__'): bpy.data.actions.remove(a)

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

if '--fingers' in argv:
    # fingers found in the mesh, rigged and skinned, and closed round each held piece (fingers.py)
    import fingers, json
    held = [o for o in bpy.data.objects if o.type == 'MESH' and o.parent == arm and o.parent_type == 'BONE' and o.parent_bone.startswith('handslot.')]
    fd = fingers.run(body, arm, held, log=lambda m: print('SLIM_' + m, flush=True))
    if fd:
        fd['model'] = name
        (dest / (name + '_grips.json')).write_text(json.dumps(fd, indent=1))
        print('SLIM_GRIPS', dest / (name + '_grips.json'), flush=True)
    rig = choreo.Rig(arm)             # the bone lists again, fingers included
    if '--fingers-only' in argv: sys.exit(0)        # trials of the finger detection: no clips, no export

made = [] if villager else [fn(rig) for fn in (moves.MONSTER if '--monster' in argv else moves.CLIPS)]
# the shield arm: motion capture and MotionBricks make the body and sword arm move naturally, but their left hand holds
# nothing, so a shield on it lay flat or swung into the body (SightForge mesh3d: 7-11 cm inside in every sword-and-shield
# clip). The keyposed clips place the shield on purpose: their left arm is kept aside here and put back into whatever
# replaces them (below, after the captures, MotionBricks and kept clips).
SHIELD_CLIPS = ['Idle_Combat', '1H_Melee_Attack_Chop', '1H_Melee_Attack_Slice_Diagonal', '1H_Melee_Attack_Slice_Horizontal', '1H_Melee_Attack_Stab',
                'SnS_Rush_1', 'SnS_Rush_2', 'SnS_Rush_3', 'SnS_Leap_Slash', 'SnS_Rend', 'SnS_Shield_Crash', 'Block_Hit', 'Block_Attack', 'Blocking', 'Block']
AUTH = {}
if any(o.name in ('Round_Shield', 'Hunt_TowerShield') for o in bpy.data.objects if o.type == 'MESH'):
    for _n in SHIELD_CLIPS:
        _a = bpy.data.actions.get(_n)
        if _a: _c = _a.copy(); _c.name = 'AUTH__' + _n; _c.use_fake_user = True; AUTH[_n] = _c
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
    made = [a for a in bpy.data.actions if not a.name.startswith(('KEEP__', 'AUTH__'))]     # the keyposed clips a capture replaced are gone already
    rig.arm.animation_data.action = None
LOOPED = {'Idle', 'Idle_Combat', 'Idle_B', 'Walking_A', 'Gait_Walk', 'Gait_Jog', 'Gait_Sprint', 'Running_A', 'Hunt_Jog', 'Hunt_Sprint',
          'Walking_Backwards', 'Running_Strafe_Left', 'Running_Strafe_Right', 'Blocking', '2H_Melee_Idle', 'Run_Upstairs'}
if opt('--keep-clips'):
    # the source's clips under the game's names: sampled at 30 fps, every bone keyed, the hips' travel taken out
    sc = bpy.context.scene
    ad = arm.animation_data or arm.animation_data_create()
    for pair in opt('--keep-clips').split(','):
        src_name, targets = pair.split('=')
        src = next((a for k, a in KEEP.items() if k[6:].startswith(src_name.strip())), None)
        if not src: print('SLIM_KEEP missing source clip', src_name, '(the source has: %s)' % ', '.join(k[6:] for k in KEEP), flush=True); continue
        # its curves still name the source's bones (renaming bones only updates the clip the rig wears): the game's names
        def _curves(a):
            try: return list(a.fcurves)
            except AttributeError: return [fc for l in a.layers for st in l.strips for cb in getattr(st, 'channelbags', []) for fc in cb.fcurves]
        for fc in _curves(src):
            for o_, n_ in choreo.NAMES.items():
                if '"%s"' % o_ in fc.data_path: fc.data_path = fc.data_path.replace('"%s"' % o_, '"%s"' % n_)
        ad.action = src
        if getattr(src, 'slots', None) and len(src.slots) and hasattr(ad, 'action_slot'): ad.action_slot = src.slots[0]
        f0, f1 = src.frame_range
        n = max(2, int(round((f1 - f0) / src_fps * 30))) + 1
        samples = []
        for i in range(n):
            t = f0 + (f1 - f0) * i / (n - 1)
            sc.frame_set(int(t), subframe=t - int(t))
            samples.append({pb.name: pb.matrix_basis.decompose()[:2] for pb in arm.pose.bones})
        travel = samples[-1]['hips'][0] - samples[0]['hips'][0] if 'hips' in samples[0] else None
        # a clip the game loops whose last pose is not its first (Tripo's run ends 31 degrees off): its last quarter
        # eases into the first pose, so the loop has no jump
        loops_in_game = any(t.strip() in LOOPED for t in targets.split('+'))
        seam = max(math.degrees(samples[0][b][1].rotation_difference(samples[-1][b][1]).angle) for b in samples[0])
        if loops_in_game and seam > 5:
            k0 = int(n * .75)
            for i in range(k0, n):
                w = (i - k0) / max(1, n - 1 - k0); w = w * w * (3 - 2 * w)
                for b in samples[i]:
                    l0, q0 = samples[0][b]; li, qi = samples[i][b]
                    if b == 'hips' and travel is not None: l0 = l0 + travel * (i / (n - 1))     # the travel comes out below
                    samples[i][b] = (li.lerp(l0, w), qi.slerp(q0, w))
            print('SLIM_KEEP %s: loop seam of %.0f degrees eased over its last quarter' % (src.name[6:], seam), flush=True)
        for tgt in targets.split('+'):
            tgt = tgt.strip()
            old_a = bpy.data.actions.get(tgt)
            if old_a:
                if old_a in made: made.remove(old_a)
                bpy.data.actions.remove(old_a)
            act = bpy.data.actions.new(tgt); act.use_fake_user = True; ad.action = act
            for i, smp in enumerate(samples):
                for pb in arm.pose.bones:
                    loc, q = smp[pb.name]
                    if pb.name == 'hips' and travel is not None: loc = loc - travel * (i / (n - 1))
                    pb.rotation_mode = 'QUATERNION'; pb.location = loc; pb.rotation_quaternion = q
                    pb.keyframe_insert('location', frame=1 + i); pb.keyframe_insert('rotation_quaternion', frame=1 + i)
            made.append(act)
            print('SLIM_KEEP %s -> %s (%d frames, travel taken out %.2f)' % (src.name[6:], tgt, n, travel.length if travel is not None else 0), flush=True)
    for a in list(KEEP.values()): bpy.data.actions.remove(a)
    ad.action = None
    for pb in arm.pose.bones: pb.matrix_basis = Matrix()
if opt('--lib-clips'):
    # clips from SightForge's motion library (Tools/sightforge/eyeslib/motionlib.py), retargeted onto this body
    import os, retarget
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'sightforge' / 'eyeslib'))
    import motionlib_blender as MLB
    libdir = Path(os.environ.get('CLAUDE_EYES_STUDIO_HOME') or (Path(os.environ.get('LOCALAPPDATA', str(Path.home()))) / 'ClaudeEyesStudio')) / 'motionlib'
    lib_src = {}
    for item in opt('--lib-clips').split(','):
        left, targets = item.split('=')
        pack, clip = left.strip().split(':', 1)
        if pack not in lib_src: lib_src[pack] = MLB.source(libdir / pack)
        src = lib_src[pack]
        info = next((c for c in src.manifest['clips'] if c['name'] == clip), None)
        if not info: print('SLIM_LIB missing clip', pack, clip, flush=True); continue
        for tgt in targets.split('+'):
            tgt = tgt.strip()
            old_a = bpy.data.actions.get(tgt)
            if old_a:
                if old_a in made: made.remove(old_a)
                bpy.data.actions.remove(old_a)
            act = src.clip(rig, tgt, clip, loop=info['loops'], pin=True, speed=src.fps / choreo.FPS)
            made.append(act)
            print('SLIM_LIB %s:%s -> %s (%.1f s, %s)' % (pack, clip, tgt, info['seconds'], 'loop' if info['loops'] else 'once'), flush=True)
    for s_ in lib_src.values(): s_.remove()
    arm.animation_data.action = None
    for pb in arm.pose.bones: pb.matrix_basis = Matrix()
if AUTH:
    # the shield where the keyposes hold it, relative to the hips (so the captured chest's twist through a lunge does
    # not tip it), reached by the replacing clip's own left arm: two-bone IK to the keyposed wrist, the elbow toward the
    # keyposed elbow, the hand turned as the keyposes turn it. Rigs without the bones: the arm's turns are copied instead.
    ad = arm.animation_data or arm.animation_data_create(); sc = bpy.context.scene
    PB = arm.pose.bones; AW = arm.matrix_world
    ik_ok = all(b in PB for b in ('hips', 'upperarm.l', 'lowerarm.l', 'hand.l'))
    tgt = bpy.data.objects.new('ShieldWrist', None); pol = bpy.data.objects.new('ShieldElbow', None)
    sc.collection.objects.link(tgt); sc.collection.objects.link(pol)
    def world(n): return AW @ PB[n].matrix
    for nm, A in AUTH.items():
        B = bpy.data.actions.get(nm)
        if not B: continue
        fa0, fa1 = A.frame_range; fb0, fb1 = B.frame_range
        n = int(round(fb1 - fb0)) + 1
        ad.action = A
        smp = []
        for i in range(n):
            f = fa0 + (fa1 - fa0) * (i / max(1, n - 1)); sc.frame_set(int(f), subframe=f - int(f))
            H = world('hips'); Hi = H.inverted()
            smp.append({'wrist': Hi @ (AW @ PB['hand.l'].head), 'elbow': Hi @ (AW @ PB['lowerarm.l'].head),
                        'hand': Hi @ world('hand.l'), 'local': {b: PB[b].matrix_basis.copy() for b in ('shoulder.l', 'upperarm.l', 'lowerarm.l', 'hand.l') if b in PB}})
        ad.action = B
        keys = []
        if ik_ok:
            c = PB['lowerarm.l'].constraints.new('IK'); c.target = tgt; c.pole_target = pol; c.chain_count = 2
            # the pole angle depends on how the rig's arm bones are rolled: the one that puts the elbow nearest the
            # keyposed elbow on this clip's first frame
            sc.frame_set(int(fb0)); H = world('hips')
            tgt.matrix_world = Matrix.Translation(H @ smp[0]['wrist']); pol.matrix_world = Matrix.Translation(H @ smp[0]['elbow'])
            best = None
            for ang in (0.0, 1.5708, -1.5708, 3.14159):
                c.pole_angle = ang; bpy.context.view_layer.update()
                e = ((AW @ PB['lowerarm.l'].head) - (H @ smp[0]['elbow'])).length
                if best is None or e < best[0]: best = (e, ang)
            c.pole_angle = best[1]
        for i, d in enumerate(smp):
            sc.frame_set(int(fb0 + i))
            if ik_ok:
                H = world('hips')
                tgt.matrix_world = Matrix.Translation(H @ d['wrist']); pol.matrix_world = Matrix.Translation(H @ d['elbow'])
                bpy.context.view_layer.update()
                k = {b: arm.convert_space(pose_bone=PB[b], matrix=PB[b].matrix, from_space='POSE', to_space='LOCAL') for b in ('upperarm.l', 'lowerarm.l')}
                want = AW.inverted() @ (H @ d['hand'])
                k['hand.l'] = arm.convert_space(pose_bone=PB['hand.l'], matrix=want, from_space='POSE', to_space='LOCAL')
            else:
                k = d['local']
            keys.append(k)
        if ik_ok: PB['lowerarm.l'].constraints.remove(c)
        for i, k in enumerate(keys):
            for b, M in k.items():
                loc, q, _ = M.decompose()
                pb = PB[b]; pb.rotation_mode = 'QUATERNION'; pb.location = loc; pb.rotation_quaternion = q
                pb.keyframe_insert('location', frame=fb0 + i); pb.keyframe_insert('rotation_quaternion', frame=fb0 + i)
        print('SLIM_SHIELD %s: the shield held as the keyposes hold it (%s, %d frames)' % (nm, 'reached by IK' if ik_ok else 'arm copied', n), flush=True)
    bpy.data.objects.remove(tgt); bpy.data.objects.remove(pol)
    for A in AUTH.values(): bpy.data.actions.remove(A)
    ad.action = None
    for pb in arm.pose.bones: pb.matrix_basis = Matrix()
if '--footlock' in argv:
    # walks, jogs and runs with their planted feet held on the travelling ground (SightForge's foot-slide fixer)
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'sightforge' / 'eyeslib'))
    import footslide_blender
    footslide_blender.run(arm, fix=True, fps=choreo.FPS, log=lambda m: print('SLIM_' + m, flush=True))
    arm.animation_data.action = None
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
if '--own-hair' in argv:
    import json as _json
    (dest / (name + '_info.json')).write_text(_json.dumps({'ownHair': True, 'source': fbx.name}, indent=1))
print('SLIM_EXPORTED', out, out.stat().st_size, flush=True)
