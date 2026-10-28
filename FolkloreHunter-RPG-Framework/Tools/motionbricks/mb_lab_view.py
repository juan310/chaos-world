# The MotionBricks lab, seen on the game's body: each clip's settings (and our own) put on the Tripo hunter through the
# same retargeter the build uses, gaits taken back to on-the-spot cycles and foot-locked as the build does
# (SightForge's foot-slide fixer, --footlock), then filmed side by side walking over a floor grid at their own speed,
# from the side, so a slipping foot shows against the grid.
#
#   blender -b --python mb_lab_view.py -- <body.fbx> <soma.json> <lab_out.json> <out folder> <clip,clip> [variant,variant]
# -> <out>/<clip>/f_NNN.png (one row of hunters per frame: ours first, then each setting, locked) and view.json
import bpy, json, math, sys
from pathlib import Path
import numpy as np
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE)); sys.path.insert(0, str(HERE.parent / 'slim')); sys.path.insert(0, str(HERE.parent / 'sightforge' / 'eyeslib'))
import choreo, mb_bridge, retarget, footslide_blender  # noqa: E402

a = sys.argv[sys.argv.index('--') + 1:]
body_fbx, soma_json, lab_json, out = a[0], a[1], a[2], Path(a[3])
want = a[4].split(',')
variants = a[5].split(',') if len(a) > 5 else None
lab = json.load(open(lab_json))

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene; sc.render.fps = choreo.FPS
bpy.ops.import_scene.fbx(filepath=body_fbx)
for x in list(bpy.data.actions): bpy.data.actions.remove(x)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
choreo.rename_bones(arm); choreo.add_hand_slots(arm)
for pb in arm.pose.bones: pb.matrix_basis.identity()
rig = choreo.Rig(arm)
body = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
for o in [o for o in bpy.data.objects if o.type == 'MESH' and o is not body]: bpy.data.objects.remove(o)
soma = mb_bridge.Soma(soma_json)
sarm = soma.armature()
src = retarget.Source(names=mb_bridge.SOMA_NAMES)
rows = []
for c in lab['clips']:
    if c['name'] not in want: continue
    vs = ['source'] + [v for v in c['variants'] if not variants or v in variants]
    for v in vs:
        fr = np.array(c['source'] if v == 'source' else c['variants'][v]['frames'], float)
        speed = 0.0
        if c.get('loco'):                                  # back on the spot, as the game plays it
            d = (fr[-1, 0, :3] - fr[0, 0, :3]) / (len(fr) - 1)
            speed = float(np.linalg.norm(d[[0, 2]])) * c['fps']
            fr[:, 0, :3] -= d[None, :] * np.arange(len(fr))[:, None]
        key = '%s__%s' % (c['name'], v)
        src.clips[key] = (sarm, soma.action(sarm, key, fr.tolist()))
        rows.append((c['name'], v, key, speed, bool(c.get('loco'))))
src.prepare(sarm)
made = {}
for name, v, key, speed, loco in rows:
    made[key] = src.clip(rig, key, key, loop=loco)
locked = footslide_blender.run(arm, names=[k for n, v, k, s, l in rows if l], fix=True, fps=choreo.FPS,
                               log=lambda m: print('VIEW_' + m, flush=True))
bpy.data.objects.remove(sarm)

# the stage: the hunter walking over a grid, filmed from its side, one setting after another (joined into columns after)
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'MATERIAL'
bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, 0))
floor = bpy.context.object
m = bpy.data.materials.new('grid'); m.use_nodes = True
nt = m.node_tree; chk = nt.nodes.new('ShaderNodeTexChecker'); chk.inputs['Scale'].default_value = 400
nt.links.new(chk.outputs['Color'], nt.nodes['Principled BSDF'].inputs['Base Color'])
m.diffuse_color = (.5, .5, .5, 1); floor.data.materials.append(m)
sc.display.shading.color_type = 'TEXTURE'
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'
report = {}
sc.render.resolution_x, sc.render.resolution_y = 520, 620
cam.data.ortho_scale = 2.6
for name in want:
    rs = [r for r in rows if r[0] == name]
    if not rs: continue
    d = out / name; d.mkdir(parents=True, exist_ok=True)
    for col, (n, v, key, speed, loco) in enumerate(rs):
        act = made[key]
        arm.animation_data_create(); arm.animation_data.action = act
        try:
            if act.slots: arm.animation_data.action_slot = act.slots[0]
        except AttributeError: pass
        f0, f1 = map(int, act.frame_range)
        i = 0
        for k in range(2 if loco else 1):                 # a gait twice round, walking over the grid
            for f in range(f0, f1 + (0 if loco else 1)):
                sc.frame_set(f)
                t = (k * (f1 - f0) + f - f0) / choreo.FPS
                arm.location = (0, -speed * t, 0)
                cam.location = (6.0, -speed * t - .15, 1.0); cam.rotation_euler = (math.radians(87), 0, math.radians(90))
                sc.render.filepath = str(d / ('c%d_f%03d.png' % (col, i))); bpy.ops.render.render(write_still=True); i += 1
        arm.location = (0, 0, 0)
    report[name] = {'columns': [v for _, v, _, _, _ in rs], 'frames': i,
                    'footlock': {v: locked.get(k, {}).get('after', locked.get(k, {}).get('before')) for (nn, v, k, s, l) in rs}}
    print('VIEW_CLIP', name, i, 'frames', [v for _, v, _, _, _ in rs], flush=True)
(out / 'view.json').write_text(json.dumps(report, indent=1, default=str))
print('VIEW_DONE', flush=True)
