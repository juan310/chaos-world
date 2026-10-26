# Blender (headless): how the game's KayKit sword and shield sit in the hands (Hunter.blend, rest pose), written in
# each hand slot's own frame, so new weapons can be held the same way (build_hunter_variant.py --prop).
#   blender -b SourceArt/Hunter.blend --python Tools/dump_hand_props.py -- <out.json>
import bpy, json, sys
from mathutils import Matrix, Vector
argv = sys.argv[sys.argv.index('--') + 1:]
rig = bpy.data.objects['Rig']
rig.animation_data_create(); rig.animation_data.action = None
for pb in rig.pose.bones:
    pb.matrix_basis = Matrix.Identity(4)
bpy.context.view_layer.update()
out = {}
for name, bone in (('1H_Sword', 'handslot.r'), ('Round_Shield', 'handslot.l')):
    o = bpy.data.objects[name]
    slot = rig.matrix_world @ rig.data.bones[bone].matrix_local
    inv = slot.inverted()
    pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
    # the object's own axes in the world, longest first
    axes = sorted(((o.matrix_world.to_3x3() @ Vector(v)).normalized() * 1 for v in ((1, 0, 0), (0, 1, 0), (0, 0, 1))),
                  key=lambda a: -max(abs((p - pts[0]).dot(a)) for p in pts))
    ext = [max((p - pts[0]).dot(a) for p in pts) - min((p - pts[0]).dot(a) for p in pts) for a in axes]
    centre = sum(pts, Vector()) / 8
    long_axis, mid_axis, thin_axis = axes
    head = slot.translation
    # the sword's grip is the end of its long axis nearest the hand slot
    ends = [centre + long_axis * ext[0] / 2, centre - long_axis * ext[0] / 2]
    grip = min(ends, key=lambda e: (e - head).length)
    tip_dir = (centre - grip).normalized()
    to_local = lambda v: list(inv.to_3x3() @ v)
    out[name] = {'bone': bone, 'gripLocal': list(inv @ grip), 'centreLocal': list(inv @ centre), 'tipDirLocal': to_local(tip_dir),
                 'widthDirLocal': to_local(mid_axis), 'normalDirLocal': to_local(thin_axis), 'extent': ext}
open(argv[0], 'w').write(json.dumps(out, indent=1))
print('HAND_PROPS', json.dumps({k: v['extent'] for k, v in out.items()}), flush=True)
