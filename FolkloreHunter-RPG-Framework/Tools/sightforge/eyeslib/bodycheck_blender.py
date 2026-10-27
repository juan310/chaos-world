# bodycheck, Blender side: a built body at rest (no clip on it): where each bone-held piece's pivot sits against its
# bone (a piece seated on a posed hand ends up off its slot in the game), and the body's height.
#   blender -b --python bodycheck_blender.py -- <model.fbx> <out.json>
import bpy, sys, json
from mathutils import Matrix

argv = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=argv[0])
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
if arm.animation_data: arm.animation_data.action = None
for pb in arm.pose.bones: pb.matrix_basis = Matrix()
bpy.context.view_layer.update()
pieces = {}
for o in bpy.data.objects:
    if o.type == 'MESH' and o.parent == arm and o.parent_type == 'BONE':
        head = arm.matrix_world @ arm.data.bones[o.parent_bone].head_local
        pieces[o.name] = {'bone': o.parent_bone, 'offset': round((o.matrix_world.translation - head).length, 4)}
body = max((o for o in bpy.data.objects if o.type == 'MESH' and o.parent_type != 'BONE'), key=lambda o: len(o.data.vertices))
zs = [(body.matrix_world @ v.co).z for v in body.data.vertices]
json.dump({'pieces': pieces, 'height': round(max(zs) - min(zs), 3), 'clips': len(bpy.data.actions), 'bones': len(arm.data.bones)}, open(argv[1], 'w'), indent=1)
print('BODYCHECK_REST', len(pieces), flush=True)
