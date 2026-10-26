# Blender (headless): builds the Cu-sith hound companion/mount from scratch (original geometry, no downloads) in the
# KayKit chunky low-poly style: moss-green shaggy fur, glowing eyes, leather saddle on a red blanket. Rigid parts on
# an armature (like KayKit props on bones), keyframed clips, a palette atlas texture, FBX export with the same
# settings as the characters (FBX_SCALE_ALL), and preview renders of every clip for review.
#   blender -b --python Tools/build_cusith.py
import bpy, bmesh, json, math
from pathlib import Path
from mathutils import Vector, Matrix, Euler

r = Path.home() / 'Documents/FolkloreHunter-RPG-Framework'
dest = r / 'Assets/Resources/Models'
preview = r / 'Captures/CuSith'
preview.mkdir(parents=True, exist_ok=True)
FPS = 24

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = FPS

# ---------------- palette atlas: one swatch per material, faces UV'd to swatch centres ----------------
PALETTE = [('fur', (0.20, 0.33, 0.24)), ('fur_light', (0.38, 0.52, 0.35)), ('fur_dark', (0.11, 0.18, 0.14)),
           ('nose', (0.07, 0.07, 0.08)), ('eye', (0.55, 1.00, 0.72)), ('leather', (0.45, 0.28, 0.14)),
           ('strap', (0.22, 0.14, 0.08)), ('blanket', (0.60, 0.15, 0.12)), ('gold', (0.86, 0.69, 0.26)), ('claw', (0.92, 0.88, 0.78))]
SW = 16
img = bpy.data.images.new('CuSith_texture', width=SW * len(PALETTE), height=SW)
px = []
for y in range(SW):
    for i, (_, c) in enumerate(PALETTE):
        for x in range(SW):
            px += [c[0], c[1], c[2], 1.0]
img.pixels = px
img.filepath_raw = str(dest / 'CuSith_texture.png'); img.file_format = 'PNG'; img.save()
mat = bpy.data.materials.new('CuSith')
mat.use_nodes = True
bsdf = mat.node_tree.nodes.get('Principled BSDF')
tex = mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image = img; tex.interpolation = 'Closest'
mat.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
bsdf.inputs['Roughness'].default_value = .85
SWATCH = {n: (i + .5) / len(PALETTE) for i, (n, _) in enumerate(PALETTE)}

parts = []   # (object, bone)


def finish(obj, color, bone):
    """Assigns the atlas material, UVs every face to the swatch of `color`, smooth-free low-poly shading."""
    me = obj.data
    me.materials.append(mat)
    if not me.uv_layers:
        me.uv_layers.new(name='UV')
    uv = me.uv_layers.active.data
    for loop in uv:
        loop.uv = (SWATCH[color], .5)
    for p in me.polygons:
        p.use_smooth = False
    obj.name = obj.name if obj.name.startswith('CuSith') else 'CuSith_' + obj.name
    parts.append((obj, bone))
    return obj


def prim(kind, name, loc, scale=(1, 1, 1), rot=(0, 0, 0), color='fur', bone='chest', **kw):
    if kind == 'cube':
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    elif kind == 'sphere':
        bpy.ops.mesh.primitive_uv_sphere_add(segments=kw.get('segments', 10), ring_count=kw.get('rings', 7), radius=.5, location=loc)
    elif kind == 'ico':
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=kw.get('subdiv', 1), radius=.5, location=loc)
    elif kind == 'cyl':
        bpy.ops.mesh.primitive_cylinder_add(vertices=kw.get('verts', 8), radius=.5, depth=1, location=loc)
    elif kind == 'cone':
        bpy.ops.mesh.primitive_cone_add(vertices=kw.get('verts', 6), radius1=.5, radius2=kw.get('tip', 0), depth=1, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    o.rotation_euler = Euler(rot)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if kw.get('bevel'):
        mod = o.modifiers.new('Bevel', 'BEVEL'); mod.width = kw['bevel']; mod.segments = 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish(o, color, bone)


def limb(name, a, b, r1, r2, color, bone, verts=8):
    """Tapered cylinder from a to b."""
    a, b = Vector(a), Vector(b)
    d = b - a
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=d.length, location=(a + b) / 2)
    o = bpy.context.active_object; o.name = name
    o.rotation_euler = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_euler()
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return finish(o, color, bone)


# ---------------- geometry (facing -Y; ground z = 0; withers ~1.2 m) ----------------
# torso: chunky front, leaner rear
prim('sphere', 'ChestBody', (0, -.42, .98), (.72, .9, .74), color='fur', bone='chest', segments=12, rings=8)
prim('sphere', 'RearBody', (0, .38, .95), (.62, .82, .64), color='fur', bone='hips', segments=12, rings=8)
prim('sphere', 'Belly', (0, -.05, .78), (.5, 1.05, .34), color='fur_light', bone='hips', segments=10, rings=6)
prim('cube', 'BackStripe', (0, 0, 1.3), (.14, 1.2, .06), color='fur_dark', bone='chest', bevel=.02)
# shaggy ruff around the neck and tufts down the back
for i in range(10):
    a = i / 10 * math.pi * 2
    off = Vector((math.cos(a) * .36, -.82, 1.06 + math.sin(a) * .34))
    prim('cone', 'Ruff%d' % i, off, (.22, .22, .32), rot=(math.radians(-70), 0, a + math.pi / 2), color='fur_light' if i % 2 else 'fur', bone='chest', verts=5)
for i, y in enumerate((-.25, .1, .42)):
    prim('cone', 'Tuft%d' % i, (0, y, 1.33), (.16, .26, .22), rot=(math.radians(60), 0, 0), color='fur_dark', bone='chest' if y < 0 else 'hips', verts=5)
# neck and head
limb('Neck', (0, -.72, 1.08), (0, -1.0, 1.42), .27, .21, 'fur', 'neck', verts=10)
prim('sphere', 'Skull', (0, -1.08, 1.5), (.52, .56, .48), color='fur', bone='head', segments=10, rings=7)
prim('cube', 'Snout', (0, -1.38, 1.42), (.24, .36, .2), color='fur_light', bone='head', bevel=.04)
prim('sphere', 'Nose', (0, -1.57, 1.47), (.11, .08, .08), color='nose', bone='head')
prim('cube', 'Jaw', (0, -1.34, 1.3), (.2, .32, .08), color='fur_light', bone='jaw', bevel=.02)
for sx, side in ((1, 'l'), (-1, 'r')):
    prim('sphere', 'Eye_' + side, (sx * .14, -1.27, 1.57), (.08, .06, .06), color='eye', bone='head')
    prim('cube', 'Brow_' + side, (sx * .15, -1.24, 1.64), (.12, .08, .04), rot=(0, 0, sx * .3), color='fur_dark', bone='head')
    prim('cone', 'Ear_' + side, (sx * .17, -1.0, 1.82), (.16, .1, .3), rot=(0, sx * .3, 0), color='fur_dark', bone='ear.' + side, verts=4)
    prim('cone', 'Fang_' + side, (sx * .08, -1.5, 1.33), (.03, .03, .07), rot=(math.pi, 0, 0), color='claw', bone='head', verts=4)
# legs: thick shaggy upper, slimmer lower, chunky paw with claws
for sx, side in ((1, 'l'), (-1, 'r')):
    x = sx * .23
    limb('ForeUpper_' + side, (x, -.5, .9), (x, -.5, .48), .15, .11, 'fur', 'fore_upper.' + side)
    prim('sphere', 'ForeElbow_' + side, (x, -.5, .48), (.2, .2, .2), color='fur', bone='fore_lower.' + side)
    limb('ForeLower_' + side, (x, -.5, .48), (x, -.52, .12), .1, .08, 'fur_dark', 'fore_lower.' + side)
    prim('cube', 'ForePaw_' + side, (x, -.58, .07), (.17, .24, .12), color='fur_dark', bone='fore_paw.' + side, bevel=.03)
    limb('HindUpper_' + side, (x, .55, .9), (x, .45, .52), .17, .12, 'fur', 'hind_upper.' + side)
    prim('sphere', 'HindHock_' + side, (x, .45, .52), (.19, .19, .19), color='fur', bone='hind_lower.' + side)
    limb('HindLower_' + side, (x, .45, .52), (x, .56, .12), .1, .08, 'fur_dark', 'hind_lower.' + side)
    prim('cube', 'HindPaw_' + side, (x, .5, .07), (.17, .24, .12), color='fur_dark', bone='hind_paw.' + side, bevel=.03)
    for k in (-1, 0, 1):
        prim('cone', 'ForeClaw_%s%d' % (side, k), (x + k * .05, -.71, .05), (.03, .03, .06), rot=(math.radians(-90), 0, 0), color='claw', bone='fore_paw.' + side, verts=4)
        prim('cone', 'HindClaw_%s%d' % (side, k), (x + k * .05, .37, .05), (.03, .03, .06), rot=(math.radians(-90), 0, 0), color='claw', bone='hind_paw.' + side, verts=4)
# tail: three shaggy segments curling up
limb('Tail1', (0, .78, 1.05), (0, .98, 1.22), .12, .1, 'fur', 'tail_1')
limb('Tail2', (0, .98, 1.22), (0, 1.12, 1.45), .11, .1, 'fur', 'tail_2')
prim('cone', 'TailTip', (0, 1.17, 1.62), (.22, .22, .34), color='fur_light', bone='tail_3', verts=6)
# saddle: blanket, seat with pommel and cantle, girth strap, gold trim
prim('cube', 'Blanket', (0, -.08, 1.29), (.78, .62, .05), color='blanket', bone='chest', bevel=.015)
prim('cube', 'BlanketTrim', (0, -.08, 1.27), (.82, .66, .03), color='gold', bone='chest')
prim('cube', 'Seat', (0, -.08, 1.36), (.46, .48, .1), color='leather', bone='chest', bevel=.03)
prim('cube', 'Pommel', (0, -.34, 1.44), (.24, .1, .14), color='leather', bone='chest', bevel=.03)
prim('cube', 'Cantle', (0, .17, 1.43), (.34, .08, .12), color='leather', bone='chest', bevel=.03)
bpy.ops.mesh.primitive_torus_add(major_radius=.47, minor_radius=.035, major_segments=16, minor_segments=4, location=(0, -.12, .98), rotation=(0, math.pi / 2, 0))
girth = bpy.context.active_object; girth.name = 'Girth'; girth.scale = (1, 1, 1.12)
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
finish(girth, 'strap', 'chest')
for sx in (1, -1):
    prim('cube', 'Stirrup_' + ('l' if sx > 0 else 'r'), (sx * .42, -.08, .98), (.04, .14, .1), color='gold', bone='chest')

# ---------------- armature ----------------
bones = {
    'root': ((0, 0, 0), (0, 0, .3), None),
    'hips': ((0, .55, .95), (0, 0, .98), 'root'),
    'chest': ((0, 0, .98), (0, -.62, 1.02), 'hips'),
    'neck': ((0, -.68, 1.06), (0, -.98, 1.4), 'chest'),
    'head': ((0, -.98, 1.4), (0, -1.4, 1.48), 'neck'),
    'jaw': ((0, -1.16, 1.33), (0, -1.48, 1.3), 'head'),
    'ear.l': ((.15, -1.0, 1.68), (.19, -1.0, 1.9), 'head'), 'ear.r': ((-.15, -1.0, 1.68), (-.19, -1.0, 1.9), 'head'),
    'tail_1': ((0, .76, 1.04), (0, .98, 1.22), 'hips'), 'tail_2': ((0, .98, 1.22), (0, 1.12, 1.45), 'tail_1'), 'tail_3': ((0, 1.12, 1.45), (0, 1.2, 1.7), 'tail_2'),
    'saddle': ((0, -.08, 1.42), (0, -.08, 1.62), 'chest'),
}
for sx, side in ((1, 'l'), (-1, 'r')):
    x = sx * .23
    bones['fore_upper.' + side] = ((x, -.5, .9), (x, -.5, .48), 'chest')
    bones['fore_lower.' + side] = ((x, -.5, .48), (x, -.52, .12), 'fore_upper.' + side)
    bones['fore_paw.' + side] = ((x, -.52, .12), (x, -.66, .05), 'fore_lower.' + side)
    bones['hind_upper.' + side] = ((x, .55, .9), (x, .45, .52), 'hips')
    bones['hind_lower.' + side] = ((x, .45, .52), (x, .56, .12), 'hind_upper.' + side)
    bones['hind_paw.' + side] = ((x, .56, .12), (x, .42, .05), 'hind_lower.' + side)

arm_data = bpy.data.armatures.new('CuSithRig')
arm = bpy.data.objects.new('Rig', arm_data)
scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
for name, (h, t, parent) in bones.items():
    b = arm_data.edit_bones.new(name); b.head = h; b.tail = t; b.roll = 0
for name, (h, t, parent) in bones.items():
    if parent:
        arm_data.edit_bones[name].parent = arm_data.edit_bones[parent]
        arm_data.edit_bones[name].use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')
bpy.context.view_layer.update()
for obj, bone in parts:
    mw = obj.matrix_world.copy()
    obj.parent = arm; obj.parent_type = 'BONE'; obj.parent_bone = bone
    bpy.context.view_layer.update()
    obj.matrix_world = mw

# ---------------- animation ----------------
pb = arm.pose.bones
for p in pb:
    p.rotation_mode = 'XYZ'
arm.animation_data_create()


def key(action, frame, pose):
    """pose: {bone: (rx, ry, rz)} degrees in bone space, plus optional ('root_loc', (x,y,z))."""
    for p in pb:
        p.rotation_euler = (0, 0, 0); p.location = (0, 0, 0)
    for bone, v in pose.items():
        if bone == 'root_loc':
            pb['root'].location = v
        else:
            pb[bone].rotation_euler = tuple(math.radians(a) for a in v)
    for p in pb:
        p.keyframe_insert('rotation_euler', frame=frame, group=p.name)
    pb['root'].keyframe_insert('location', frame=frame, group='root')


def clip(name, length, pose_at, step=2):
    act = bpy.data.actions.new(name)
    arm.animation_data.action = act
    for f in range(0, length + 1, step):
        key(act, f, pose_at(f / length))
    act.use_fake_user = True
    return act


def s(t, phase=0):
    return math.sin((t + phase) * math.pi * 2)


def legs(t, swing, lift, phases, hind_bias=0):
    """Leg swing about the bone X axis; the lower leg folds while the leg travels forward (lifted)."""
    out = {}
    for limbname, ph in phases.items():
        a = s(t, ph)
        lifted = max(0, math.cos((t + ph) * math.pi * 2))           # forward travel phase
        hind = limbname.startswith('hind')
        out[limbname.replace('X', 'upper')] = (a * swing + (hind_bias if hind else 0), 0, 0)
        out[limbname.replace('X', 'lower')] = ((-1 if hind else 1) * lifted * lift, 0, 0)
        out[limbname.replace('X', 'paw')] = ((1 if hind else -1) * lifted * lift * .5, 0, 0)
    return out


WALK = {'fore_X.l': 0, 'hind_X.r': 0, 'fore_X.r': .5, 'hind_X.l': .5}            # diagonal pairs
GALLOP = {'fore_X.l': 0, 'fore_X.r': .08, 'hind_X.l': .5, 'hind_X.r': .58}       # bounding gallop


def idle(t):
    p = {'chest': (s(t) * 1.5, 0, 0), 'neck': (-4 + s(t, .2) * 2, 0, s(t * .5) * 6), 'head': (s(t, .4) * 2, 0, 0),
         'tail_1': (10, 0, s(t * 2) * 18), 'tail_2': (8, 0, s(t * 2, .1) * 14), 'tail_3': (6, 0, s(t * 2, .2) * 10),
         'ear.l': (0, 0, max(0, s(t * 2, .6)) * -12), 'ear.r': (0, 0, 0), 'root_loc': (0, s(t) * .006, 0)}
    return p


def walk(t):
    p = legs(t, 26, 38, WALK)
    p.update({'chest': (s(t * 2) * 2, 0, s(t) * 3), 'hips': (0, 0, -s(t) * 3), 'neck': (-2 + s(t * 2) * 3, 0, 0),
              'tail_1': (14, 0, s(t) * 14), 'tail_2': (8, 0, s(t, .1) * 10), 'root_loc': (0, abs(s(t * 2)) * .03, 0)})
    return p


def gallop(t):
    p = legs(t, 46, 60, GALLOP, hind_bias=-6)
    flex = s(t)
    p.update({'hips': (flex * 9, 0, 0), 'chest': (-flex * 12, 0, 0), 'neck': (flex * 10 - 8, 0, 0), 'head': (-flex * 6, 0, 0),
              'ear.l': (-30, 0, 0), 'ear.r': (-30, 0, 0), 'tail_1': (-10 + flex * 10, 0, 0), 'tail_2': (-8, 0, 0),
              'root_loc': (0, max(0, s(t, .25)) * .12, 0)})
    return p


def bite(t):
    lunge = math.sin(min(1, t / .55) * math.pi) if t < .55 else 0
    crouch = math.sin(min(1, t / .3) * math.pi) * (1 if t < .3 else 0)
    jaw = 34 * math.sin(min(1, max(0, (t - .2) / .4)) * math.pi)
    return {'chest': (8 * crouch - 10 * lunge, 0, 0), 'neck': (-22 * lunge + 10 * crouch, 0, 0), 'head': (-12 * lunge, 0, 0), 'jaw': (jaw, 0, 0),
            'fore_upper.l': (-14 * lunge, 0, 0), 'fore_upper.r': (-14 * lunge, 0, 0), 'hind_upper.l': (16 * crouch, 0, 0), 'hind_upper.r': (16 * crouch, 0, 0),
            'ear.l': (-35 * lunge, 0, 0), 'ear.r': (-35 * lunge, 0, 0), 'root_loc': (0, -.06 * crouch, -.25 * lunge)}


def sit_pose(amount, head_up=0, jaw=0):
    return {'hips': (-38 * amount, 0, 0), 'chest': (-8 * amount, 0, 0), 'hind_upper.l': (-70 * amount, 0, 0), 'hind_upper.r': (-70 * amount, 0, 0),
            'hind_lower.l': (110 * amount, 0, 0), 'hind_lower.r': (110 * amount, 0, 0), 'hind_paw.l': (-40 * amount, 0, 0), 'hind_paw.r': (-40 * amount, 0, 0),
            'fore_upper.l': (12 * amount, 0, 0), 'fore_upper.r': (12 * amount, 0, 0), 'neck': (-14 * amount - head_up, 0, 0), 'head': (-head_up * .6, 0, 0),
            'jaw': (jaw, 0, 0), 'tail_1': (40 * amount, 0, 0), 'root_loc': (0, -.28 * amount, .1 * amount)}


def sit(t):
    p = sit_pose(1); p['chest'] = (-8 + s(t) * 1.5, 0, 0); return p


def howl(t):
    a = min(1, t / .25); up = math.sin(min(1, max(0, (t - .15) / .7)) * math.pi)
    return sit_pose(a, head_up=42 * up, jaw=26 * up)


def hit(t):
    k = math.sin(min(1, t / .5) * math.pi)
    return {'chest': (10 * k, 0, 0), 'neck': (18 * k, 0, 0), 'head': (12 * k, 0, 0), 'ear.l': (-40 * k, 0, 0), 'ear.r': (-40 * k, 0, 0), 'root_loc': (0, 0, .12 * k)}


clips = {'Idle': (48, idle), 'Walk': (24, walk), 'Gallop': (12, gallop), 'Bite': (22, bite), 'Sit': (24, sit), 'Howl': (40, howl), 'Hit': (12, hit)}
for name, (length, fn) in clips.items():
    clip(name, length, fn, step=1 if length <= 24 else 2)

# ---------------- preview renders ----------------
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x = 420; scene.render.resolution_y = 340; scene.render.film_transparent = True
cam_data = bpy.data.cameras.new('PreviewCam'); cam_data.type = 'ORTHO'; cam_data.ortho_scale = 3.2
cam = bpy.data.objects.new('PreviewCam', cam_data); scene.collection.objects.link(cam); scene.camera = cam
shots = []
views = {'side': ((5, -.3, 1.0), (math.radians(90), 0, math.radians(90))), 'front': ((0, -6, 1.0), (math.radians(90), 0, 0)),
         'threequarter': ((3.6, -3.6, 2.6), (math.radians(65), 0, math.radians(45)))}
for name, (length, fn) in clips.items():
    act = bpy.data.actions[name]
    arm.animation_data.action = act
    for frac in ((0, .25, .5, .75) if name in ('Walk', 'Gallop') else (.5,)):
        scene.frame_set(int(length * frac))
        for vname in (('side',) if name in ('Walk', 'Gallop') else ('side', 'threequarter')):
            loc, rot = views[vname]
            cam.location = loc; cam.rotation_euler = rot
            out = preview / ('%s_%s_%02d.png' % (name, vname, int(frac * 100)))
            scene.render.filepath = str(out); bpy.ops.render.render(write_still=True); shots.append(out.name)
arm.animation_data.action = bpy.data.actions['Idle']
scene.frame_set(0)
bpy.data.objects.remove(cam, do_unlink=True)

bpy.ops.export_scene.fbx(filepath=str(dest / 'CuSith.fbx'), use_selection=False, object_types={'ARMATURE', 'MESH', 'EMPTY'},
                         add_leaf_bones=False, apply_scale_options='FBX_SCALE_ALL', bake_anim=True, bake_anim_use_nla_strips=False,
                         bake_anim_use_all_actions=True, bake_anim_simplify_factor=0, axis_forward='-Z', axis_up='Y', path_mode='COPY', embed_textures=False)
bpy.ops.wm.save_as_mainfile(filepath=str(r / 'SourceArt' / 'CuSith.blend'))
info = {'output': 'CuSith.fbx', 'license': 'original geometry made for this project (no third-party assets)', 'parts': len(parts),
        'bones': list(bones), 'clips': {k: v[0] / FPS for k, v in clips.items()}, 'previews': shots}
(r / 'Docs/Assets/cusith-export.json').write_text(json.dumps(info, indent=2))
print('CUSITH', len(parts), 'parts', len(bones), 'bones', list(clips), flush=True)
