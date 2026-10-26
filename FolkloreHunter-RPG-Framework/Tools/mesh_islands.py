# Blender (headless): lists the loose parts of a Forge rigged model (size, centre, the bones that weight them).
#   blender -b --python Tools/mesh_islands.py -- <rigged.glb> <out.json>
import bpy, bmesh, json, sys
from collections import defaultdict
argv = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=argv[0])
body = max((o for o in bpy.data.objects if o.type == 'MESH' and o.vertex_groups), key=lambda o: len(o.data.vertices))
bm = bmesh.new(); bm.from_mesh(body.data)
# glTF splits vertices along UV seams: weld them (in this copy only) so pieces of one surface are one island.
orig = {v: v.index for v in bm.verts}
welded = bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-4)
bm.verts.ensure_lookup_table()
seen = set(); islands = []
for v in bm.verts:
    if v.index in seen:
        continue
    stack = [v]; part = []
    seen.add(v.index)
    while stack:
        a = stack.pop(); part.append(a.index)
        for e in a.link_edges:
            b = e.other_vert(a)
            if b.index not in seen:
                seen.add(b.index); stack.append(b)
    islands.append(part)
names = {g.index: g.name for g in body.vertex_groups}
out = []
for part in sorted(islands, key=len, reverse=True)[:40]:
    co = [body.matrix_world @ bm.verts[i].co for i in part]
    lo = [min(c[k] for c in co) for k in range(3)]; hi = [max(c[k] for c in co) for k in range(3)]
    w = defaultdict(float)
    layer = bm.verts.layers.deform.active
    for i in part:
        if layer:
            for gi, wt in bm.verts[i][layer].items():
                w[names[gi]] += wt
    tot = sum(w.values()) or 1
    out.append({'verts': len(part), 'lo': [round(x, 3) for x in lo], 'hi': [round(x, 3) for x in hi],
                'bones': {k: round(v / tot, 2) for k, v in sorted(w.items(), key=lambda kv: -kv[1])[:4]}})
open(argv[1], 'w').write(json.dumps({'islands': len(islands), 'top': out}, indent=1))
print('ISLANDS', len(islands), flush=True)
