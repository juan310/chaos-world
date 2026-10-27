"""Terrain mesh check, the Blender side (run by eyeslib/terrain.py).

  blender -b --python terrain_blender.py -- <model .fbx|.glb|.obj> <out dir>

Per mesh object, in world space: open edges (a face on one side only) and how many separate openings they make, edges
shared by more than two faces, faces upside down among upward-facing neighbours (on ground, a face whose normal points
down while most of its neighbours point up), faces lying within 1 mm of another (same centre, parallel), sliver faces.
A top-down picture marks open edges red, upside-down faces orange, stacked faces violet.
Writes <out>/terrain_mesh.json and the picture."""
import bpy, bmesh, sys, json
from pathlib import Path
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
model, out = Path(argv[0]), Path(argv[1]); out.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
ext = model.suffix.lower()
if ext in ('.glb', '.gltf'): bpy.ops.import_scene.gltf(filepath=str(model))
elif ext == '.obj': bpy.ops.wm.obj_import(filepath=str(model))
else: bpy.ops.import_scene.fbx(filepath=str(model))
objs = [o for o in bpy.data.objects if o.type == 'MESH']
rep = {'model': str(model), 'objects': []}
marks = {'open': [], 'flipped': [], 'stacked': []}
for o in objs:
    bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world)
    bm.normal_update(); bm.edges.ensure_lookup_table(); bm.faces.ensure_lookup_table()
    open_e = [e for e in bm.edges if len(e.link_faces) == 1]
    nonman = [e for e in bm.edges if len(e.link_faces) > 2]
    # openings: connected groups of open edges
    left = set(open_e); openings = 0
    while left:
        e = left.pop(); openings += 1; stack = [e]
        while stack:
            x = stack.pop()
            for v in x.verts:
                for y in v.link_edges:
                    if y in left: left.remove(y); stack.append(y)
    flipped = []
    for f in bm.faces:
        if f.normal.z < -.5:
            nb = [g for e in f.edges for g in e.link_faces if g is not f]
            if nb and sum(1 for g in nb if g.normal.z > .5) >= max(2, len(nb) * .66): flipped.append(f)
    # faces on top of each other: same rounded centre and parallel normals
    seen = {}; stacked = []
    for f in bm.faces:
        c = f.calc_center_median(); key = (round(c.x, 3), round(c.y, 3), round(c.z, 3))
        if key in seen and abs(seen[key].normal.dot(f.normal)) > .98: stacked.append(f)
        else: seen[key] = f
    slivers = sum(1 for f in bm.faces if f.calc_area() > 0 and f.calc_area() / max(1e-9, max(e.calc_length() for e in f.edges)) < .001)
    rep['objects'].append({'name': o.name, 'faces': len(bm.faces), 'open': len(open_e), 'openLength': round(sum(e.calc_length() for e in open_e), 3),
                           'openings': openings, 'nonManifold': len(nonman), 'flipped': len(flipped), 'flippedArea': round(sum(f.calc_area() for f in flipped), 3),
                           'overlapping': len(stacked), 'slivers': slivers})
    marks['open'] += [(e.verts[0].co.copy(), e.verts[1].co.copy()) for e in open_e[:20000]]
    marks['flipped'] += [[v.co.copy() for v in f.verts] for f in flipped[:20000]]
    marks['stacked'] += [[v.co.copy() for v in f.verts] for f in stacked[:20000]]
    bm.free()
# top-down picture with PIL-free drawing: Blender's own image buffer
allv = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
if allv:
    lo = Vector([min(v[i] for v in allv) for i in range(3)]); hi = Vector([max(v[i] for v in allv) for i in range(3)])
    W = 1024; span = max(hi.x - lo.x, hi.y - lo.y, 1e-3); s = (W - 40) / span
    H = int((hi.y - lo.y) * s) + 40
    import numpy as np
    img = np.zeros((H, W, 4), np.float32); img[..., 3] = 1; img[..., :3] = .1
    P = lambda v: (int(20 + (v.x - lo.x) * s), int(20 + (v.y - lo.y) * s))
    def put(x, y, c, r=1):
        for a in range(-r, r + 1):
            for b in range(-r, r + 1):
                if 0 <= x + a < W and 0 <= y + b < H: img[y + b, x + a, :3] = c
    for o in objs:   # faces shaded by height (vertices only, cheap)
        for v in o.data.vertices:
            w = o.matrix_world @ v.co; t = (w.z - lo.z) / max(1e-6, hi.z - lo.z); x, y = P(w); put(x, y, (.25 + .5 * t,) * 3, 0)
    for poly in marks['flipped']:
        c = sum(poly, Vector()) / len(poly); put(*P(c), (1, .55, .1), 2)
    for poly in marks['stacked']:
        c = sum(poly, Vector()) / len(poly); put(*P(c), (.75, .35, 1), 2)
    for a, b in marks['open']:
        n = max(2, int((b - a).length * s))
        for k in range(n + 1): put(*P(a.lerp(b, k / n)), (1, .15, .1), 1)
    im = bpy.data.images.new('terrain_check', W, H, alpha=True); im.pixels.foreach_set(img.ravel())
    pic = out / (model.stem + '_terrain_top.png'); im.filepath_raw = str(pic); im.file_format = 'PNG'; im.save()
    rep['pictures'] = [str(pic)]
(out / 'terrain_mesh.json').write_text(json.dumps(rep, indent=1))
print('TERRAIN_MESH_DONE', len(objs), 'objects', flush=True)
