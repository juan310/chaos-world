# Dumps a body's hand vertices in each hand's own frame (x across the knuckles, y along the hand, z through the palm,
# metres), with their mesh island and edges, for studying finger detection outside Blender.
#   blender -b --python Tools/slim/hands_dump.py -- <body .fbx> <out.json>
import bpy, sys, json
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import choreo, fingers

argv = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=argv[0])
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
choreo.rename_bones(arm); choreo.add_hand_slots(arm)
body = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
out = {}
for side in ('l', 'r'):
    hv = fingers.hand_vertices(body, side)
    W = body.matrix_world; AM = arm.matrix_world
    hb = arm.data.bones['hand.' + side]
    M = (AM @ hb.matrix_local).inverted()
    P = {i: M @ (W @ body.data.vertices[i].co) for i in hv}
    adj = fingers._adjacency(body, hv)
    isl = {}; k = 0
    for i in hv:
        if i in isl: continue
        st = [i]; isl[i] = k
        while st:
            a = st.pop()
            for b in adj[a]:
                if b not in isl: isl[b] = k; st.append(b)
        k += 1
    n = fingers.palm_normal(arm, side)
    out[side] = {'verts': {str(i): [round(P[i].x, 5), round(P[i].y, 5), round(P[i].z, 5), isl[i], round(hv[i], 3)] for i in hv},
                 'edges': [[a, b] for a in adj for b in adj[a] if a < b], 'islands': k,
                 'palmNormalLocal': list((M.to_3x3() @ n).normalized()), 'boneLength': hb.length * AM.to_scale().y}
Path(argv[1]).write_text(json.dumps(out))
print('HANDS_DUMP', {s: (len(o['verts']), o['islands']) for s, o in out.items()}, flush=True)
