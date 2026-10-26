# Blender (headless): writes an armature's rest bones (head, tail, roll axis, parent) and its meshes to JSON.
#   blender -b <file.blend> --python Tools/dump_rig.py -- <out.json>
#   blender -b --python Tools/dump_rig.py -- <out.json> <model.glb>
import bpy, json, sys
argv = sys.argv[sys.argv.index('--') + 1:]
if len(argv) > 1:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=argv[1])
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
out = {'object': rig.name, 'matrix_world': [list(r) for r in rig.matrix_world], 'bones': {}, 'meshes': {}, 'actions': len(bpy.data.actions)}
for b in rig.data.bones:
    m = b.matrix_local
    out['bones'][b.name] = {'head': list(b.head_local), 'tail': list(b.tail_local), 'parent': b.parent.name if b.parent else None,
                            'x': list(m.col[0][:3]), 'z': list(m.col[2][:3])}
for o in bpy.data.objects:
    if o.type == 'MESH':
        out['meshes'][o.name] = {'parent': o.parent.name if o.parent else None, 'parent_bone': o.parent_bone, 'verts': len(o.data.vertices),
                                 'groups': len(o.vertex_groups), 'dims': list(o.dimensions)}
open(argv[0], 'w').write(json.dumps(out, indent=1))
print('DUMPED', len(out['bones']), 'bones', len(out['meshes']), 'meshes', flush=True)
