# Blender (headless): a new hunter body from a SightForge Forge model job (local TripoSG, picture -> rigged mesh) on the
# game's KayKit hunter skeleton, so every clip the game plays (sword, lance, bow, katana, evasions, riding: all of
# Hunter.blend's actions) and every weapon socket works unchanged.
#   blender -b SourceArt/Hunter.blend --python Tools/build_hunter_variant.py -- <forge job dir> <Name>
# Steps: the Forge body is scaled to the KayKit hunter's height (weapons keep their size in the game); each Forge bone
# is turned to point the way its KayKit bone points at rest (A-pose -> the KayKit T-pose) and that pose is applied to
# the mesh; the KayKit bones are moved onto the body's joints, keeping their directions and rolls (so the clips' bone
# rotations mean the same); the hips' and IK bones' movement in the clips is scaled to the longer legs; the Forge
# weights are renamed (and merged) onto the KayKit bones. Writes Assets/Resources/Models/Hunter_<Name>.fbx, its
# texture, preview renders and a JSON report. Never touches Hunter.fbx or Hunter.blend (the file is not saved).
import bpy, json, math, sys
from pathlib import Path
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index('--') + 1:]
JOB, NAME = Path(argv[0]), argv[1]
r = Path.home() / 'Documents/FolkloreHunter-RPG-Framework'
dest = r / 'Assets/Resources/Models'
preview = r / 'Captures/HunterVariants'
preview.mkdir(parents=True, exist_ok=True)
report = {'name': NAME, 'job': str(JOB)}

rig = bpy.data.objects['Rig']
kb = {b.name: b for b in rig.data.bones}
old_meshes = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('Knight_')]
weapons = [o for o in bpy.data.objects if o.type == 'MESH' and not o.name.startswith('Knight_')]


def world_bounds(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    zs = []
    for o in objs:
        ev = o.evaluated_get(dg)
        zs += [(ev.matrix_world @ v.co).z for v in ev.to_mesh().vertices]
        ev.to_mesh_clear()
    return min(zs), max(zs)


k_lo, k_hi = world_bounds([o for o in old_meshes if o.name != 'Knight_Cape'])
report['kaykitHeight'] = k_hi - k_lo

# ---- the Forge model
before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=str(JOB / 'rigged.glb'))
new = [o for o in bpy.data.objects if o not in before]
F = next(o for o in new if o.type == 'ARMATURE')
body = max((o for o in new if o.type == 'MESH' and o.vertex_groups), key=lambda o: len(o.data.vertices))
for o in new:
    if o not in (F, body):
        bpy.data.objects.remove(o, do_unlink=True)
f_lo, f_hi = world_bounds([body])
s = (k_hi - k_lo) / (f_hi - f_lo)
F.scale = (s, s, s)
F.location.z -= f_lo * s
bpy.context.view_layer.update()
report['forgeHeight'] = f_hi - f_lo; report['scale'] = s

# Forge bone -> the KayKit bone whose rest direction it takes.
DIRECTION = {'Hips': 'hips', 'Spine': 'spine', 'Chest': 'chest', 'Neck': 'head', 'Head': 'head',
             'LeftUpperArm': 'upperarm.l', 'LeftLowerArm': 'lowerarm.l', 'LeftHand': 'hand.l',
             'RightUpperArm': 'upperarm.r', 'RightLowerArm': 'lowerarm.r', 'RightHand': 'hand.r',
             'LeftUpperLeg': 'upperleg.l', 'LeftLowerLeg': 'lowerleg.l', 'LeftFoot': 'foot.l', 'LeftToes': 'toes.l',
             'RightUpperLeg': 'upperleg.r', 'RightLowerLeg': 'lowerleg.r', 'RightFoot': 'foot.r', 'RightToes': 'toes.r'}


def kdir(name):
    b = kb[name]
    return (b.tail_local - b.head_local).normalized()


bpy.context.view_layer.objects.active = F
bpy.ops.object.mode_set(mode='POSE')
order = []
def walk(b):
    order.append(b.name)
    for c in b.children:
        walk(c)
for b in F.data.bones:
    if not b.parent:
        walk(b)
turned = {}
for name in order:
    if name not in DIRECTION:
        continue
    pb = F.pose.bones[name]
    cur = (pb.tail - pb.head).normalized()
    want = kdir(DIRECTION[name])
    q = cur.rotation_difference(want)
    turned[name] = round(math.degrees(q.angle), 1)
    h = pb.matrix.translation.copy()
    pb.matrix = Matrix.Translation(h) @ q.to_matrix().to_4x4() @ Matrix.Translation(-h) @ pb.matrix
    bpy.context.view_layer.update()
report['turnedDegrees'] = turned
joint = {b.name: (F.matrix_world @ F.pose.bones[b.name].head, F.matrix_world @ F.pose.bones[b.name].tail) for b in F.data.bones}
bpy.ops.object.mode_set(mode='OBJECT')

# Apply the pose to the mesh (it is now in the KayKit rest pose, at the KayKit height), then drop the Forge skeleton.
mod = next(m for m in body.modifiers if m.type == 'ARMATURE')
with bpy.context.temp_override(object=body, active_object=body, selected_objects=[body], selected_editable_objects=[body]):
    bpy.ops.object.modifier_apply(modifier=mod.name)
mw = body.matrix_world.copy()
body.parent = None
body.data.transform(mw)
body.matrix_world = Matrix.Identity(4)
bpy.data.objects.remove(F, do_unlink=True)

# ---- move the KayKit bones onto the body's joints (directions and rolls kept)
old = {n: (b.head_local.copy(), b.tail_local.copy()) for n, b in kb.items()}
# Weapons hang from the hand slots (and the quiver from the chest): remember where each sits against its bone at rest.
WEAPON_SCALE = float(argv[2]) if len(argv) > 2 else .8   # realistic hands: the chibi's weapons scaled down a little
rig.animation_data_create(); rig.animation_data.action = None
for pb in rig.pose.bones:
    pb.matrix_basis = Matrix.Identity(4)
bpy.context.view_layer.update()
held = {o.name: (o.parent_bone, kb[o.parent_bone].matrix_local.copy(), o.matrix_world.copy()) for o in weapons if o.parent_bone in kb}
sword = bpy.data.objects.get('1H_Sword')
if sword:
    pts = [sword.matrix_world @ Vector(c) for c in sword.bound_box]
    report['swordLengthBefore'] = max((p - q).length for p in pts for q in pts)
leg_old = (old['upperleg.l'][0] - old['foot.l'][0]).length
leg_new = (joint['LeftUpperLeg'][0] - joint['LeftFoot'][0]).length
arm_old = (old['upperarm.l'][0] - old['hand.l'][1]).length
arm_new = (joint['LeftUpperArm'][0] - joint['LeftHand'][1]).length
report['legRatio'] = leg_new / leg_old; report['armRatio'] = arm_new / arm_old

bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
eb = rig.data.edit_bones
placed = {}


def place(name, head, length):
    b = eb[name]
    d = (b.tail - b.head).normalized()
    roll = b.roll
    b.head = head; b.tail = head + d * length; b.roll = roll
    placed[name] = head.copy()


def from_forge(k, f):
    h, t = joint[f]
    place(k, h, (t - h).length)


for side, S in (('l', 'Left'), ('r', 'Right')):
    from_forge('upperleg.' + side, S + 'UpperLeg'); from_forge('lowerleg.' + side, S + 'LowerLeg')
    from_forge('foot.' + side, S + 'Foot'); from_forge('toes.' + side, S + 'Toes')
    from_forge('upperarm.' + side, S + 'UpperArm'); from_forge('lowerarm.' + side, S + 'LowerArm')
    hh, ht = joint[S + 'Hand']; hl = (ht - hh).length
    place('wrist.' + side, hh, hl * .35)
    place('hand.' + side, eb['wrist.' + side].tail.copy(), hl * .65)
    k_hand = (old['hand.' + side][1] - old['wrist.' + side][0]).length
    off = (old['handslot.' + side][0] - old['wrist.' + side][0]) * (hl / k_hand)
    place('handslot.' + side, hh + off, (old['handslot.' + side][1] - old['handslot.' + side][0]).length * hl / k_hand)
from_forge('hips', 'Hips'); from_forge('spine', 'Spine'); from_forge('chest', 'Chest')
place('head', joint['Neck'][0], (joint['Head'][1] - joint['Neck'][0]).length)

# Helpers (IK targets and foot-roll controls) keep their offsets from the joint they belong to, scaled with the limb.
ANCHOR = {'kneeIK': ('lowerleg', leg_new / leg_old), 'control-toe-roll': ('toes', leg_new / leg_old), 'control-heel-roll': ('foot', leg_new / leg_old),
          'control-foot-roll': ('foot', leg_new / leg_old), 'heelIK': ('foot', leg_new / leg_old), 'IK-foot': ('foot', leg_new / leg_old),
          'IK-toe': ('foot', leg_new / leg_old), 'elbowIK': ('lowerarm', arm_new / arm_old), 'handIK': ('wrist', arm_new / arm_old)}
for n, b in list(eb.items()):
    if n in placed or n == 'root':
        continue
    base, side = n.rsplit('.', 1) if '.' in n else (n, '')
    if base not in ANCHOR:
        continue
    anchor, k = ANCHOR[base]
    a = anchor + '.' + side
    oh, ot = old[n]
    head = placed[a] + (oh - old[a][0]) * k
    place(n, head, (ot - oh).length * k)
bpy.ops.object.mode_set(mode='OBJECT')
report['bonesPlaced'] = len(placed)
bpy.context.view_layer.update()
for name, (bone, old_m, old_w) in held.items():
    o = bpy.data.objects[name]
    new_m = rig.data.bones[bone].matrix_local
    o.matrix_world = rig.matrix_world @ new_m @ Matrix.Scale(WEAPON_SCALE, 4) @ old_m.inverted() @ rig.matrix_world.inverted() @ old_w
bpy.context.view_layer.update()
report['weaponScale'] = WEAPON_SCALE
if sword:
    pts = [sword.matrix_world @ Vector(c) for c in sword.bound_box]
    report['swordLengthAfter'] = max((p - q).length for p in pts for q in pts)

# ---- the clips: bone rotations carry over; translations (hips bob, IK targets) scale with the limbs
loc_scale = {}
for a in bpy.data.actions:
    try:
        curves = list(a.fcurves)
    except AttributeError:
        curves = [c for layer in a.layers for st in layer.strips for bag in st.channelbags for c in bag.fcurves]
    for c in curves:
        if not c.data_path.endswith('.location') or 'pose.bones' not in c.data_path:
            continue
        bone = c.data_path.split('"')[1]
        k = arm_new / arm_old if any(x in bone for x in ('elbow', 'hand', 'arm', 'wrist')) else leg_new / leg_old
        for p in c.keyframe_points:
            p.co[1] *= k; p.handle_left[1] *= k; p.handle_right[1] *= k
        loc_scale[bone] = round(k, 3)
report['locationScaled'] = loc_scale

# ---- weights: Forge groups renamed (and merged) onto the KayKit bones
SPLIT = {'Hips': {'hips': 1}, 'Spine': {'spine': 1}, 'Chest': {'chest': 1}, 'Neck': {'chest': .4, 'head': .6}, 'Head': {'head': 1}}
for S, side in (('Left', 'l'), ('Right', 'r')):
    SPLIT.update({S + 'Shoulder': {'chest': .5, 'upperarm.' + side: .5}, S + 'UpperArm': {'upperarm.' + side: 1},
                  S + 'LowerArm': {'lowerarm.' + side: 1}, S + 'Hand': {'hand.' + side: 1}, S + 'UpperLeg': {'upperleg.' + side: 1},
                  S + 'LowerLeg': {'lowerleg.' + side: 1}, S + 'Foot': {'foot.' + side: 1}, S + 'Toes': {'toes.' + side: 1}})
for g in body.vertex_groups:
    if g.name not in SPLIT and not g.name.startswith(('Left', 'Right')):
        SPLIT.setdefault(g.name, {'chest': 1})
idx = {g.index: g.name for g in body.vertex_groups}
acc = [dict() for _ in body.data.vertices]
for v in body.data.vertices:
    for ge in v.groups:
        src = idx[ge.group]
        dst = SPLIT.get(src) or ({'hand.l': 1} if src.startswith('Left') else {'hand.r': 1} if src.startswith('Right') else {'chest': 1})
        for k, w in dst.items():
            acc[v.index][k] = acc[v.index].get(k, 0) + ge.weight * w
body.vertex_groups.clear()
groups = {}
unweighted = 0
for v, ws in zip(body.data.vertices, acc):
    tot = sum(ws.values())
    if tot <= 0:
        unweighted += 1; ws = {'hips': 1}; tot = 1
    for k, w in ws.items():
        g = groups.get(k) or body.vertex_groups.new(name=k)
        groups[k] = g
        g.add([v.index], w / tot, 'REPLACE')
report['unweighted'] = unweighted
body.name = 'Hunter_' + NAME + '_Body'
body.parent = rig
arm = body.modifiers.new('Armature', 'ARMATURE'); arm.object = rig

for o in old_meshes:
    bpy.data.objects.remove(o, do_unlink=True)

# The body's texture (Forge paints one picture for the whole mesh).
img = next((n.image for m in body.data.materials if m and m.use_nodes for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image), None)
if img:
    img.filepath_raw = str(dest / ('Hunter_' + NAME + '_texture.png')); img.file_format = 'PNG'; img.save()
report['texture'] = bool(img)

# ---- previews (front and side, a few clips) to check by eye before Unity
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x = 360; scene.render.resolution_y = 420; scene.render.film_transparent = True
cam_data = bpy.data.cameras.new('PreviewCam'); cam_data.type = 'ORTHO'; cam_data.ortho_scale = 3.2
cam = bpy.data.objects.new('PreviewCam', cam_data); scene.collection.objects.link(cam); scene.camera = cam
rig.animation_data_create()
shots = []
SHOW = {'Idle': ('1H_Sword', 'Round_Shield'), '1H_Melee_Attack_Chop': ('1H_Sword', 'Round_Shield'), 'Hunt_Jog': ('1H_Sword', 'Round_Shield'),
        'Dodge_Forward': ('1H_Sword', 'Round_Shield'), '2H_Melee_Attack_Chop': ('Hunt_Katana',), 'Bow_Draw': ('Hunt_Bow', 'Hunt_NockedArrow', 'Hunt_Quiver'),
        'Lance_Charge': ('Hunt_Lance', 'Hunt_TowerShield'), 'Ride_Idle': ()}
for clip, show in SHOW.items():
    act = bpy.data.actions.get(clip)
    if not act:
        continue
    rig.animation_data.action = act
    try:
        if act.slots: rig.animation_data.action_slot = act.slots[0]
    except AttributeError:
        pass
    f0, f1 = act.frame_range
    scene.frame_set(int(f0 + (f1 - f0) * (.0 if clip == 'Idle' else .5)))
    for o in weapons:
        o.hide_render = o.name not in show
    for view, loc, rot in (('front', (0, -6, 1.0), (math.radians(90), 0, 0)), ('side', (6, 0, 1.0), (math.radians(90), 0, math.radians(90)))):
        cam.location = loc; cam.rotation_euler = rot
        out = preview / ('%s_%s_%s.png' % (NAME, clip, view))
        scene.render.filepath = str(out); bpy.ops.render.render(write_still=True); shots.append(out.name)
rig.animation_data.action = None
for o in weapons:
    o.hide_render = False
bpy.data.objects.remove(cam, do_unlink=True)
report['previews'] = shots

bpy.ops.export_scene.fbx(filepath=str(dest / ('Hunter_' + NAME + '.fbx')), use_selection=False, object_types={'ARMATURE', 'MESH', 'EMPTY'},
                         add_leaf_bones=False, apply_scale_options='FBX_SCALE_ALL', bake_anim=True, bake_anim_use_nla_strips=False,
                         bake_anim_use_all_actions=True, bake_anim_simplify_factor=0, axis_forward='-Z', axis_up='Y', path_mode='COPY', embed_textures=False)
report['actions'] = len(bpy.data.actions)
(r / 'Docs/Assets').mkdir(parents=True, exist_ok=True)
(r / 'Docs/Assets' / ('hunter-variant-%s.json' % NAME)).write_text(json.dumps(report, indent=2))
print('HUNTER_VARIANT', json.dumps({k: report.get(k) for k in ('name', 'scale', 'legRatio', 'armRatio', 'unweighted', 'bonesPlaced', 'actions', 'kaykitHeight', 'swordLengthBefore', 'swordLengthAfter')}), flush=True)
