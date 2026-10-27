# Renders the slim-skeleton clips for review by eye, without writing any game file:
#   blender -b --python Tools/slim/preview_slim.py -- <Codex body .fbx> <out dir> [clip function names...]
# Front three-quarter and side views, eight moments of each clip; a bar stands for each weapon (blade along the
# hand slot's Y axis, edge along Z) so the grips can be judged.
import bpy, sys, math
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import choreo, moves
argv = sys.argv[sys.argv.index('--') + 1:]
fbx, out = argv[0], Path(argv[1]); out.mkdir(parents=True, exist_ok=True)
m2m = argv[argv.index('--m2m') + 1] if '--m2m' in argv else None
names = [a for i, a in enumerate(argv[2:], 2) if a != '--m2m' and (i < 1 or argv[i - 1] != '--m2m')]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE'); arm.name = 'Rig'
choreo.rename_bones(arm); choreo.add_hand_slots(arm)
body = next(o for o in bpy.data.objects if o.type == 'MESH')
tex = Path(fbx).with_name(Path(fbx).stem + '_texture.png')
if tex.exists():
    m = bpy.data.materials.new('Body'); m.use_nodes = True
    t = m.node_tree.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(str(tex))
    m.node_tree.links.new(t.outputs['Color'], m.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    body.data.materials.clear(); body.data.materials.append(m)


def bar(name, bone, length, width, color):
    bpy.ops.mesh.primitive_cube_add(size=1)
    o = bpy.context.object; o.name = name
    o.scale = (width, length, width * .35)
    bpy.ops.object.transform_apply(scale=True)
    for v in o.data.vertices: v.co.y += length / 2          # from the grip along +Y
    o.parent = arm; o.parent_type = 'BONE'; o.parent_bone = bone
    o.matrix_parent_inverse.identity()
    # bone-parented objects sit at the bone's tail: bring the grip back to the head
    o.location = (0, -arm.data.bones[bone].length, 0)
    mat = bpy.data.materials.new(name); mat.diffuse_color = color; o.data.materials.append(mat)
    return o


bar('Blade', 'handslot.r', .95, .05, (.8, .8, .9, 1))
bar('Edge', 'handslot.r', .02, .02, (1, .1, .1, 1)).location = (0, -arm.data.bones['handslot.r'].length, .04)
sh = bar('Shield', 'handslot.l', .05, .6, (.5, .25, .1, 1))

rig = choreo.Rig(arm)
clips = []
if m2m:
    import retarget
    src = retarget.Source(m2m)
    clips = retarget.apply(rig, src, only=names or None)
else:
    for fn in moves.TEST:
        nm = getattr(fn, '__name__', '')
        if names and nm not in names and not any(nm.startswith(x) for x in names): continue
        clips.append(fn(rig))

scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x = 320; scene.render.resolution_y = 420; scene.render.film_transparent = True
cd = bpy.data.cameras.new('Cam'); cd.type = 'ORTHO'; cd.ortho_scale = 2.6
cam = bpy.data.objects.new('Cam', cd); scene.collection.objects.link(cam); scene.camera = cam
views = {'front': ((3.2, -5.5, 1.0), (math.radians(90), 0, math.radians(30))), 'side': ((-6, 0, 1.0), (math.radians(90), 0, math.radians(-90)))}
for act in clips:
    arm.animation_data.action = act
    try:
        if act.slots: arm.animation_data.action_slot = act.slots[0]
    except AttributeError: pass
    f0, f1 = act.frame_range
    for view, (loc, r) in views.items():
        cam.location = loc; cam.rotation_euler = r
        for i in range(8):
            scene.frame_set(int(round(f0 + (f1 - f0) * i / 8)))
            scene.render.filepath = str(out / ('%s_%s_%d.png' % (act.name, view, i)))
            bpy.ops.render.render(write_still=True)
    print('SLIM_PREVIEW', act.name, int(f1 - f0), 'frames', flush=True)
