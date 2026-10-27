# Renders an exported game model (Models/<name>.fbx) as the game will get it, for review by eye:
#   blender -b --python Tools/slim/render_fbx.py -- <model .fbx> <out dir> <clip> [<clip>...]
# Front three-quarter and side views, eight moments of each clip, the model's textures on (Models/<piece>_texture.png).
import bpy, sys, math
from pathlib import Path
argv = sys.argv[sys.argv.index('--') + 1:]
fbx, out, clips = Path(argv[0]), Path(argv[1]), argv[2:]
out.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(fbx))
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
models = fbx.parent
for o in bpy.data.objects:
    if o.type != 'MESH': continue
    tex = models / (o.name + '_texture.png')
    if not tex.exists() and o.name.endswith('_Body'): tex = models / (fbx.stem + '_texture.png')
    if not tex.exists() and not o.name.endswith('_Body'): tex = models / 'Hunter_texture.png'
    if not tex.exists(): continue
    m = bpy.data.materials.new(o.name); m.use_nodes = True
    t = m.node_tree.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(str(tex))
    m.node_tree.links.new(t.outputs['Color'], m.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    o.data.materials.clear(); o.data.materials.append(m)
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x = 320; scene.render.resolution_y = 420; scene.render.film_transparent = True
h = max(o.dimensions.z for o in bpy.data.objects if o.type == 'MESH')
cd = bpy.data.cameras.new('Cam'); cd.type = 'ORTHO'; cd.ortho_scale = 2.6 * max(1, h / 1.9)
cam = bpy.data.objects.new('Cam', cd); scene.collection.objects.link(cam); scene.camera = cam
z = 1.0 * max(1, h / 1.9)
views = {'front': ((3.2, -5.5, z), (math.radians(90), 0, math.radians(30))), 'side': ((-6, 0, z), (math.radians(90), 0, math.radians(-90)))}
names = [a.name for a in bpy.data.actions]
for want in clips:
    act = next((a for a in bpy.data.actions if a.name.endswith('|' + want) or a.name == want), None)
    if not act: print('RENDER_MISSING', want, names[:5]); continue
    arm.animation_data_create(); arm.animation_data.action = act
    try:
        if act.slots: arm.animation_data.action_slot = act.slots[0]
    except AttributeError: pass
    f0, f1 = act.frame_range
    for view, (loc, r) in views.items():
        cam.location = loc; cam.rotation_euler = r
        for i in range(8):
            scene.frame_set(int(round(f0 + (f1 - f0) * i / 8)))
            scene.render.filepath = str(out / ('%s_%s_%d.png' % (want, view, i)))
            bpy.ops.render.render(write_still=True)
    print('RENDERED', want, flush=True)
