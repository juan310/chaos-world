# Looks at a character file from outside (Tripo, a store, a friend) before it goes near the game: its meshes, rig,
# bones, clips (frames, which bones move, how far the body travels), its size and proportions, and pictures: front and
# side at rest, and four moments of every clip from the front-left.
#   blender -b --python Tools/slim/inspect_import.py -- <model .fbx/.glb> <out folder>
import bpy, sys, json, math
from pathlib import Path
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
src, out = Path(argv[0]), Path(argv[1]); out.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
if src.suffix.lower() in ('.glb', '.gltf'): bpy.ops.import_scene.gltf(filepath=str(src))
else: bpy.ops.import_scene.fbx(filepath=str(src))
arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
rep = {'file': src.name, 'meshes': [], 'armatures': [], 'actions': []}
for m in meshes:
    rep['meshes'].append({'name': m.name, 'vertices': len(m.data.vertices), 'faces': len(m.data.polygons), 'groups': len(m.vertex_groups),
                          'materials': [s.material.name for s in m.material_slots if s.material], 'parent': m.parent.name if m.parent else None})
for a in arms:
    rep['armatures'].append({'name': a.name, 'bones': [(b.name, b.parent.name if b.parent else None) for b in a.data.bones], 'scale': list(a.matrix_world.to_scale())})


def bounds():
    dg = bpy.context.evaluated_depsgraph_get(); lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
    for m in meshes:
        e = m.evaluated_get(dg); me = e.to_mesh()
        for v in me.vertices:
            p = m.matrix_world @ v.co
            lo = Vector(map(min, lo, p)); hi = Vector(map(max, hi, p))
        e.to_mesh_clear()
    return lo, hi


arm = arms[0] if arms else None
acts = list(bpy.data.actions)
if arm and arm.animation_data: arm.animation_data.action = None
bpy.context.view_layer.update()
lo, hi = bounds(); rep['restSize'] = [round(x, 3) for x in hi - lo]; rep['restMin'] = [round(x, 3) for x in lo]
# pictures
sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'; sc.render.resolution_x, sc.render.resolution_y = 480, 640
sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'; sc.display.shading.show_cavity = True
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'


def shoot(name, dirv, lo, hi):
    c = (lo + hi) / 2; size = max(hi.z - lo.z, hi.x - lo.x, hi.y - lo.y)
    cam.data.ortho_scale = size * 1.15
    cam.location = c + dirv.normalized() * size * 3
    cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = str(out / (name + '.png')); bpy.ops.render.render(write_still=True)


def curves(act):
    """an action's curves (Blender 5 keeps them in layers, strips and channel bags)"""
    try: return list(act.fcurves)
    except AttributeError:
        out = []
        for l in act.layers:
            for st in l.strips:
                for cb in getattr(st, 'channelbags', []): out += list(cb.fcurves)
        return out


shoot('rest_front', Vector((0, -1, 0)), lo, hi); shoot('rest_side', Vector((1, 0, 0)), lo, hi); shoot('rest_back', Vector((0, 1, 0)), lo, hi)
for act in acts:
    f0, f1 = act.frame_range
    moved = sorted({fc.data_path.split('"')[1] for fc in curves(act) if fc.data_path.startswith('pose.bones') and len({round(k.co[1], 4) for k in fc.keyframe_points}) > 1})
    info = {'name': act.name, 'frames': [f0, f1], 'curves': len(curves(act)), 'bonesMoving': len(moved), 'pictures': []}
    if arm:
        arm.animation_data_create(); arm.animation_data.action = act
        roots = []
        for k in range(4):
            f = f0 + (f1 - f0) * k / 3; sc.frame_set(int(round(f)))
            l2, h2 = bounds(); roots.append(((l2 + h2) / 2))
            nm = 'clip_%s_%d' % (''.join(ch if ch.isalnum() else '_' for ch in act.name)[:40], k)
            shoot(nm, Vector((-.6, -1, .15)), l2, h2); info['pictures'].append(nm + '.png')
        info['travel'] = [round(x, 3) for x in roots[-1] - roots[0]]
        info['height'] = [round(x, 3) for x in (h2 - l2)]
    rep['actions'].append(info)
(out / 'inspect.json').write_text(json.dumps(rep, indent=1))
print('INSPECT', json.dumps({'meshes': [(m['name'], m['vertices']) for m in rep['meshes']], 'bones': [len(a['bones']) for a in rep['armatures']],
                             'size': rep['restSize'], 'actions': [(a['name'], a['frames'], a['bonesMoving'], a.get('travel')) for a in rep['actions']]}), flush=True)
