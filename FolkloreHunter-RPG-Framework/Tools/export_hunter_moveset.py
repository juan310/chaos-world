# Blender (headless) re-export of the hunter (KayKit Knight, CC0) with the full melee moveset the Monster Hunter-style
# combat needs, using the same export settings as the sample's character export (FBX_SCALE_ALL is required: reverting
# it caused a 100x bind/animation mismatch). Same meshes as before: Knight body/helmet/cape, 1H_Sword, Round_Shield.
# Adds Ride_Idle: Sit_Chair_Idle with the thighs spread to straddle a mount. Renders preview frames of new poses.
#   blender -b --python Tools/export_hunter_moveset.py -- [legSpread] [legDrop]
import bpy, json, math, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import hunter_weapons
from mathutils import Quaternion, Vector

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
SPREAD = float(argv[0]) if len(argv) > 0 else 32.0     # degrees each thigh opens outward
DROP = float(argv[1]) if len(argv) > 1 else 28.0       # degrees the thighs drop from the chair's horizontal
r = Path.home() / 'Documents/FolkloreHunter-RPG-Framework'
source = r / 'SourceArt'
dest = r / 'Assets/Resources/Models'
preview = r / 'Captures/HunterExport'
preview.mkdir(parents=True, exist_ok=True)

wanted = [
    # the original twelve
    'Idle', 'Walking_A', 'Running_A', 'Interact', 'Hit_A', 'Death_A', 'Death_A_Pose', 'Spellcast_Shoot', 'Blocking',
    '1H_Melee_Attack_Chop', '1H_Melee_Attack_Slice_Diagonal', 'Dodge_Forward',
    # sword and shield moveset
    '1H_Melee_Attack_Slice_Horizontal', '1H_Melee_Attack_Stab', '2H_Melee_Attack_Chop', '2H_Melee_Attack_Slice',
    '2H_Melee_Attack_Spin', '2H_Melee_Idle', 'Block', 'Block_Attack', 'Block_Hit',
    'Dodge_Backward', 'Dodge_Left', 'Dodge_Right', 'Hit_B', 'Death_B', 'Lie_Down', 'Lie_Idle', 'Lie_StandUp',
    # locomotion while locked on, sprinting, items, riding base poses
    'Running_B', 'Running_Strafe_Left', 'Running_Strafe_Right', 'Walking_Backwards', 'Walking_B',
    'Use_Item', 'PickUp', 'Cheer', 'Throw', 'Spellcast_Raise', 'Jump_Start', 'Jump_Idle', 'Jump_Land', 'Jump_Full_Short',
    'Sit_Chair_Idle', 'Sit_Floor_Idle',
    # lance and bow (0.7): thrusts, guard thrust, aiming and shooting
    '1H_Ranged_Aiming', '1H_Ranged_Shoot', '1H_Ranged_Shooting', '1H_Ranged_Reload', '2H_Ranged_Aiming', '2H_Ranged_Shoot',
    '2H_Melee_Attack_Stab', '2H_Melee_Attack_Spinning', 'Spellcast_Long']
keep_meshes = ['Knight_ArmLeft', 'Knight_ArmRight', 'Knight_Body', 'Knight_Cape', 'Knight_Head', 'Knight_Helmet',
               'Knight_LegLeft', 'Knight_LegRight', '1H_Sword', 'Round_Shield', 'Rectangle_Shield']


def fcurves(action):
    """F-curves of an action in both the legacy and the layered (slotted) action APIs."""
    try:
        if action.fcurves is not None:
            return list(action.fcurves)
    except AttributeError:
        pass
    out = []
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                out += list(bag.fcurves)
    return out


def bone_rotation_curves(action, bone):
    path = 'pose.bones["%s"].rotation_quaternion' % bone
    found = {c.array_index: c for c in fcurves(action) if c.data_path == path}
    return [found.get(i) for i in range(4)]


bpy.ops.wm.read_factory_settings(use_empty=True)
src = next(source.rglob('Knight.glb'))
bpy.ops.import_scene.gltf(filepath=str(src))
for action in list(bpy.data.actions):
    if action.name not in wanted:
        bpy.data.actions.remove(action)
missing = [w for w in wanted if w not in bpy.data.actions]
for obj in list(bpy.data.objects):
    if obj.type == 'MESH' and obj.name not in keep_meshes:
        bpy.data.objects.remove(obj, do_unlink=True)
for obj in bpy.data.objects:
    obj.hide_render = False; obj.hide_viewport = False
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
rig.name = 'Rig'

# Ride_Idle: the chair pose's thighs point straight ahead; open them outward and let them fall to straddle a back.
base = bpy.data.actions['Sit_Chair_Idle']
ride = base.copy(); ride.name = 'Ride_Idle'
rest = {b.name: b for b in rig.data.bones}
for side, sign in (('l', 1), ('r', -1)):
    curves = bone_rotation_curves(ride, 'upperleg.' + side)
    if None in curves:
        raise SystemExit('Ride_Idle: missing rotation curves for upperleg.' + side)
    # Offsets are applied in armature space (about the body's up and side axes), converted into the bone's frame.
    bone = rest['upperleg.' + side]
    to_bone = bone.matrix_local.to_quaternion().inverted()
    open_out = Quaternion(Vector((0, 0, 1)), math.radians(SPREAD) * sign)      # around the body's up axis (Blender Z)
    drop = Quaternion(Vector((1, 0, 0)), math.radians(-DROP))                    # around the body's side axis
    world_offset = open_out @ drop
    for k in range(len(curves[0].keyframe_points)):
        q = Quaternion([curves[i].keyframe_points[k].co[1] for i in range(4)])
        q2 = to_bone @ world_offset @ bone.matrix_local.to_quaternion() @ q
        for i in range(4):
            curves[i].keyframe_points[k].co[1] = q2[i]
            curves[i].keyframe_points[k].handle_left[1] = q2[i]
            curves[i].keyframe_points[k].handle_right[1] = q2[i]

# Lance and bow (0.7): new weapon meshes on the hand/back slots, and the clips derived for them.
atlas = next(im for im in bpy.data.images if im.size[0] > 0 and im.name not in ('Render Result', 'Viewer Node'))
weapons = hunter_weapons.build_weapons(rig, bpy.data.objects['1H_Sword'], bpy.data.objects['Rectangle_Shield'], atlas)
bpy.data.objects.remove(bpy.data.objects['Rectangle_Shield'], do_unlink=True)   # only its enlarged copy ships
weapons.update(hunter_weapons.build_katana(rig, bpy.data.objects['1H_Sword'], atlas))
derived, arm_bones = hunter_weapons.derive_clips(rig)
derived += hunter_weapons.bow_clips(rig)
hunter_weapons.place_bow(rig, weapons['Hunt_Bow'], weapons['Hunt_NockedArrow'])

for im in bpy.data.images:
    if im.name in ['Render Result', 'Viewer Node']:
        continue
    try:
        im.filepath_raw = str(dest / 'Hunter_texture.png'); im.file_format = 'PNG'; im.save()
    except Exception:
        pass

# Preview renders of new poses (front and side), so the riding pose can be checked by eye before Unity.
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x = 360; scene.render.resolution_y = 420
scene.render.film_transparent = True
cam_data = bpy.data.cameras.new('PreviewCam'); cam_data.type = 'ORTHO'; cam_data.ortho_scale = 3.2
cam = bpy.data.objects.new('PreviewCam', cam_data); scene.collection.objects.link(cam); scene.camera = cam
rig.animation_data_create()
shots = []
props = [o for o in bpy.data.objects if o.name in ('1H_Sword', 'Round_Shield', 'Knight_Cape')]
hunt_props = [o for o in bpy.data.objects if o.name.startswith('Hunt_')]
LOADOUT = {'sns': ('1H_Sword', 'Round_Shield'), 'lance': ('Hunt_Lance', 'Hunt_TowerShield'), 'bow': ('Hunt_Bow', 'Hunt_Quiver', 'Hunt_NockedArrow'),
           'katana': ('Hunt_Katana', 'Hunt_KatanaGlow')}
views = (('front', (0, -6, 1.0), (math.radians(90), 0, 0)), ('side', (6, 0, 1.0), (math.radians(90), 0, math.radians(90))),
         ('right', (-6, 0, 1.0), (math.radians(90), 0, math.radians(-90))), ('threequarter', (4.2, -4.2, 2.6), (math.radians(68), 0, math.radians(45))))
CLIP_LOADOUT = {'Lance_High_Thrust': 'lance', 'Lance_Charge': 'lance', 'Blocking': 'lance',
                'Bow_Draw': 'bow', 'Bow_Shoot': 'bow', 'Bow_Power_Shoot': 'bow', 'Bow_Arc_Shoot': 'bow', 'Bow_Piercer': 'bow', 'Bow_Step_L': 'bow', 'Bow_Aim_Walk': 'bow',
                '2H_Melee_Idle': 'katana', '2H_Melee_Attack_Chop': 'katana', '2H_Melee_Attack_Spin': 'katana', 'Katana_Iai': 'katana'}
for name in ['Ride_Idle', 'Sit_Chair_Idle', 'Block_Attack', '2H_Melee_Attack_Spin', 'Lie_Down', 'Use_Item'] + list(CLIP_LOADOUT):
    act = bpy.data.actions.get(name)
    if not act:
        continue
    rig.animation_data.action = act
    try:
        if act.slots:
            rig.animation_data.action_slot = act.slots[0]
    except AttributeError:
        pass
    f0, f1 = act.frame_range
    scene.frame_set(int(f0 + (f1 - f0) * .5))
    bare = name in ('Ride_Idle', 'Sit_Chair_Idle')      # legs must be visible: hide sword, shield and cape for these
    show = LOADOUT[CLIP_LOADOUT.get(name, 'sns')]
    for o in props + hunt_props:
        o.hide_render = bare or (o.name in ('1H_Sword', 'Round_Shield') + tuple(n for k in LOADOUT.values() for n in k) and o.name not in show)
    if name in CLIP_LOADOUT and name not in ('Blocking',):
        views_now = views[:2] + views[3:]
        if name.startswith('Bow_') and name not in ('Bow_Draw',): views_now = views[1:2] + views[3:]
    else:
        views_now = views if bare else views[:2]
    for view, loc, rot in views_now:
        cam.location = loc; cam.rotation_euler = rot
        out = preview / ('%s_%s.png' % (name, view))
        scene.render.filepath = str(out)
        bpy.ops.render.render(write_still=True)
        shots.append(str(out))
rig.animation_data.action = None
for o in props + hunt_props:
    o.hide_render = False

bpy.data.objects.remove(cam, do_unlink=True)
bpy.ops.export_scene.fbx(filepath=str(dest / 'Hunter.fbx'), use_selection=False, object_types={'ARMATURE', 'MESH', 'EMPTY'},
                         add_leaf_bones=False, apply_scale_options='FBX_SCALE_ALL', bake_anim=True, bake_anim_use_nla_strips=False,
                         bake_anim_use_all_actions=True, bake_anim_simplify_factor=0, axis_forward='-Z', axis_up='Y', path_mode='COPY', embed_textures=False)
bpy.ops.wm.save_as_mainfile(filepath=str(source / 'Hunter.blend'))
result = {'source': str(src), 'output': 'Hunter.fbx', 'license': 'CC0 (KayKit Character Pack: Adventures); Hunt_* weapons are original geometry made for this project',
          'spread': SPREAD, 'drop': DROP, 'weapons': sorted(weapons), 'derivedClips': [a.name for a in derived], 'armBones': arm_bones,
          'actions': sorted(a.name for a in bpy.data.actions), 'missing': missing,
          'meshes': sorted(o.name for o in bpy.data.objects if o.type == 'MESH'), 'previews': shots}
(r / 'Docs/Assets/hunter-moveset-export.json').write_text(json.dumps(result, indent=2))
print('EXPORTED', json.dumps({k: result[k] for k in ('missing', 'meshes')}), len(result['actions']), 'actions', flush=True)
