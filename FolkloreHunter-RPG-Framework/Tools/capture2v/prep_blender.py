# Two-view capture, step 1 (Blender): a game model made ready for anymove's local parts (Tools/capture2v/anymove).
#
#   set MOCAP_CREATURE=Tools/capture2v/creatures/<name>
#   blender -b --python Tools/capture2v/prep_blender.py -- <model.fbx> <run folder>
#
# The rig is the model's own skeleton (the game's bone names), so a captured motion lands on it directly:
# - <run>/rig_info.json: the bones the capture moves, root first, each with its parent among them. Their joint limits
#   come from the model's own clips, with a margin. Every bone end is a joint the reader learns.
# - <run>/rigged.blend: objects named 'rig' and 'body' as anymove's scripts expect. Transforms are applied, the model
#   faces -Y, its limbs are painted with their colours, and the studio is set up.
# - <run>/ref/ref_front.png, ref_side.png: the standing frame of its idle, from both layout cameras. film.py puts them
#   side by side as the video model's first frame.
import bpy, json, math, os, sys
from pathlib import Path
import numpy as np
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / 'anymove' / 'blender'))
import common  # noqa: E402  (reads MOCAP_CREATURE for the layout and the limb paint)

fbx, run = sys.argv[sys.argv.index('--') + 1:][:2]
run = Path(run); (run / 'ref').mkdir(parents=True, exist_ok=True)
creature = json.load(open(Path(os.environ['MOCAP_CREATURE']) / 'creature.json'))

# the bones the capture moves (the game's slim skeleton names), root first; the rest of the skeleton follows them
CHAIN = ['hips', 'spine', 'chest', 'neck', 'head']
SIDE = ['shoulder.%s', 'upperarm.%s', 'lowerarm.%s', 'hand.%s', 'upperleg.%s', 'lowerleg.%s', 'foot.%s', 'toes.%s']
# a joint per bone head, named for the body; the leaves' tails too
HEAD_JOINT = {'hips': 'pelvis', 'spine': 'spine', 'chest': 'chest', 'neck': 'neck', 'head': 'head',
              'shoulder': 'clavicle', 'upperarm': 'shoulder', 'lowerarm': 'elbow', 'hand': 'wrist',
              'upperleg': 'hip', 'lowerleg': 'knee', 'foot': 'ankle', 'toes': 'ball'}
TAIL_JOINT = {'head': 'head_top', 'hand': 'handtip', 'toes': 'toetip'}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
# the texture lies beside the model (the game's Models folder, <model>_texture.png); the FBX names the path it was
# built from, which is elsewhere
here = Path(fbx).parent
beside = sorted(here.glob(Path(fbx).stem + '*.png'))
for im in bpy.data.images:
    if im.source == 'FILE' and not Path(bpy.path.abspath(im.filepath)).is_file():
        found = here / Path(im.filepath).name if (here / Path(im.filepath).name).is_file() else (beside[0] if beside else None)
        if found:
            im.filepath = str(found); im.reload()
            print('CAPTURE texture', im.name, '->', found, flush=True)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
body = max(meshes, key=lambda o: len(o.data.vertices))
for o in meshes:
    if o is not body: bpy.data.objects.remove(o)
arm.name, body.name = 'rig', 'body'

# transforms applied, so the rig object can be placed and turned freely (render_dataset sets its location and yaw)
bpy.ops.object.select_all(action='DESELECT')
for o in (arm, body): o.select_set(True)
bpy.context.view_layer.objects.active = arm
if arm.animation_data: arm.animation_data.action = None
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.context.view_layer.update()
B = arm.data.bones
# facing -Y (anymove's front camera looks along +Y at the face): the toes' skin lies in front of the ankles (the toe
# bones' own direction is not to be trusted after an FBX round trip)
gi = {g.name: g.index for g in body.vertex_groups}
toe = [body.matrix_world @ v.co for v in body.data.vertices
       if any(g.group in (gi.get('toes.l'), gi.get('toes.r')) and g.weight > .5 for g in v.groups)]
ankles = (B['foot.l'].head_local + B['foot.r'].head_local) / 2
fwd = (sum(toe, Vector()) / len(toe) - ankles) if toe else (B['toes.l'].tail_local - B['foot.l'].head_local)
print('CAPTURE toes lie %.2f m along y from the ankles' % fwd.y, flush=True)
if fwd.y > 0:
    print('CAPTURE turning the model to face -Y', flush=True)
    arm.rotation_euler = (0, 0, math.pi); bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
# standing on z = 0
co = np.array([body.matrix_world @ v.co for v in body.data.vertices])
arm.location.z -= co[:, 2].min(); bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)

names = CHAIN + [p % s for s in 'lr' for p in SIDE]
missing = [n for n in names if n not in B]
if missing: sys.exit('CAPTURE_FAIL the model lacks bones %s' % missing)


def kind(n): return n.split('.')[0]
def side(n): return '_' + n.split('.')[1].upper() if '.' in n else ''
def joint(n, end='head'):
    return (HEAD_JOINT if end == 'head' else TAIL_JOINT)[kind(n)] + side(n)
def parent(n):
    p = B[n].parent
    while p and p.name not in names: p = p.parent
    return p.name if p else None


children = {n: [m for m in names if parent(m) == n] for n in names}
main_child = {'hips': 'spine', 'spine': 'chest', 'chest': 'neck', 'neck': 'head'}
bones, joints = [], {}
for n in names:
    joints[joint(n)] = list(B[n].head_local)
    if kind(n) in TAIL_JOINT:
        t = joint(n, 'tail'); joints[t] = list(B[n].tail_local)
    else:
        c = main_child.get(n) or (children[n][0] if children[n] else None)
        t = joint(c)
    bones.append([n, joint(n), t, parent(n)])

# joint limits from what the model's own clips do (Blender XYZ Euler of each pose bone), widened by a margin
margin, floor = creature.get('limit_margin_deg', 30), creature.get('limit_floor_deg', 25)
lo = {n: np.full(3, 0.0) for n in names}; hi = {n: np.full(3, 0.0) for n in names}
for act in bpy.data.actions:
    arm.animation_data_create(); arm.animation_data.action = act
    try:
        if act.slots: arm.animation_data.action_slot = act.slots[0]
    except AttributeError: pass
    f0, f1 = map(int, act.frame_range)
    for f in range(f0, f1 + 1, 2):
        bpy.context.scene.frame_set(f)
        for n in names:
            e = np.degrees(np.array(arm.pose.bones[n].matrix_basis.to_euler('XYZ')))
            lo[n] = np.minimum(lo[n], e); hi[n] = np.maximum(hi[n], e)
arm.animation_data.action = None
limits = {}
for n in names:
    a, b = lo[n] - margin, hi[n] + margin
    mid = (a + b) / 2
    a, b = np.minimum(a, mid - floor), np.maximum(b, mid + floor)
    limits[n] = [[round(float(max(-175, x)), 1), round(float(min(175, y)), 1)] for x, y in zip(a, b)]
for pb in arm.pose.bones: pb.matrix_basis = Matrix()
bpy.context.view_layer.update()
common.write_rig_info(arm, joints, bones, limits, run / 'rig_info.json')
print('CAPTURE_RIG %d bones, %d joints -> %s' % (len(bones), len(joints), run / 'rig_info.json'), flush=True)

# limb paint: the hand's own finger groups count as the hand
for key, v in list(common.LEG_BONES.items()):
    sidename = 'Left' if any(b.endswith('.l') for b in v) else 'Right'
    if any(kind(b) == 'hand' for b in v):
        common.LEG_BONES[key] = v + [g.name for g in body.vertex_groups if g.name.startswith(sidename + 'Hand')]
common.paint_legs(body)
common.studio(bpy.context.scene, samples=48)

# the reference frame: the idle's first frame (standing as it does in the game), from both cameras
idle = bpy.data.actions.get('Idle') or next((a for a in bpy.data.actions if a.name.split('|')[-1] == 'Idle'), None)
if idle:
    arm.animation_data_create(); arm.animation_data.action = idle
    try:
        if idle.slots: arm.animation_data.action_slot = idle.slots[0]
    except AttributeError: pass
    bpy.context.scene.frame_set(int(idle.frame_range[0]))
paths = common.render_views(run / 'ref', 'ref', scale=1.0)
if arm.animation_data: arm.animation_data.action = None
bpy.ops.wm.save_as_mainfile(filepath=str(run / 'rigged.blend'))
print('CAPTURE_PREP_DONE', paths, flush=True)
