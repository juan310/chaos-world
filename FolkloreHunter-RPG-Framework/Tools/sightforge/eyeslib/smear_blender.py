"""Smear finder, the Blender side (run by eyeslib/smear.py).

  blender -b --python smear_blender.py -- <model .fbx|.glb> <out dir> [--texture <png>]

For every face of the model's body: the texture's detail where the face samples it (the strength of the picture's edges,
averaged over 9 points of the face) and the texel density (texels per metre of surface). A face is smeared when its
detail is under a third of the body's median detail; blurred when it gets fewer than a quarter of the median texels per
metre. Areas are counted by the bone that carries the face and by the way it faces (front, back, left, right, up, down;
the model faces -Y). Four views of the body are rendered with smeared faces red and blurred faces blue.
Writes <out>/smear.json and the pictures."""
import bpy, sys, json, math
from pathlib import Path
from mathutils import Vector
import numpy as np

argv = sys.argv[sys.argv.index('--') + 1:]
model, out = Path(argv[0]), Path(argv[1]); out.mkdir(parents=True, exist_ok=True)
tex = argv[argv.index('--texture') + 1] if '--texture' in argv else None
bpy.ops.wm.read_factory_settings(use_empty=True)
if model.suffix.lower() in ('.glb', '.gltf'): bpy.ops.import_scene.gltf(filepath=str(model))
else: bpy.ops.import_scene.fbx(filepath=str(model))
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
body = max(meshes, key=lambda o: len(o.data.vertices))
arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
if arm and arm.animation_data: arm.animation_data.action = None
img = None
if tex: img = bpy.data.images.load(tex)
else:
    cand = model.with_name(model.stem + '_texture.png')
    if cand.exists(): img = bpy.data.images.load(str(cand))
    else:
        for m in body.data.materials:
            if m and m.use_nodes:
                for n in m.node_tree.nodes:
                    if n.type == 'TEX_IMAGE' and n.image: img = n.image; break
            if img: break
if img is None: raise SystemExit('SMEAR no texture found (pass --texture)')
W, H = img.size
px = np.empty(W * H * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(H, W, 4)
lum = px[..., :3] @ np.array([.299, .587, .114], np.float32)
g = np.zeros_like(lum)
g[1:-1, 1:-1] = np.abs(lum[1:-1, 2:] - lum[1:-1, :-2]) + np.abs(lum[2:, 1:-1] - lum[:-2, 1:-1])
k = max(2, W // 512)              # detail over a small neighbourhood (box filter by cumulative sums)
c = np.pad(g, ((1, 0), (1, 0))).cumsum(0).cumsum(1)
def box(y0, x0, y1, x1): return (c[y1, x1] - c[y0, x1] - c[y1, x0] + c[y0, x0]) / max(1, (y1 - y0) * (x1 - x0))
me = body.data; uv = me.uv_layers.active.data
mw = body.matrix_world; n3 = mw.to_3x3()
groups = {gr.index: gr.name for gr in body.vertex_groups}
def bone_of(poly):
    tally = {}
    for vi in poly.vertices:
        for ge in me.vertices[vi].groups:
            nm = groups.get(ge.group)
            if nm: tally[nm] = tally.get(nm, 0) + ge.weight
    return max(tally, key=tally.get) if tally else '-'
def facing(n):
    a = max(range(3), key=lambda i: abs(n[i])); s = n[a] > 0
    return [('left', 'right'), ('back', 'front'), ('up', 'down')][a][0 if s else 1] if a != 2 else ('up' if s else 'down')
faces = []
for p in me.polygons:
    loops = [uv[i].uv for i in p.loop_indices]
    cen = sum(loops, Vector((0, 0))) / len(loops)
    pts = [cen] + [l.lerp(cen, .5) for l in loops[:8]]
    det = []
    for q in pts:
        x = min(W - k - 1, max(k, int(q.x * W))); y = min(H - k - 1, max(k, int(q.y * H)))
        det.append(box(y - k, x - k, y + k, x + k))
    # texel density: UV area in texels against the face's world area
    uva = 0.0
    for i in range(1, len(loops) - 1):
        a, b, cc = loops[0], loops[i], loops[i + 1]
        uva += abs((b.x - a.x) * (cc.y - a.y) - (cc.x - a.x) * (b.y - a.y)) / 2
    wa = p.area * (mw.to_scale().x ** 2)
    dens = math.sqrt(uva * W * H / max(1e-9, wa))
    nrm = (n3 @ p.normal).normalized()
    # the model faces -Y: a normal toward -Y is its front, +X its left side
    side = 'front' if nrm.y < -.6 else 'back' if nrm.y > .6 else ('left' if nrm.x > 0 else 'right') if abs(nrm.x) > .6 else ('up' if nrm.z > 0 else 'down')
    faces.append((p.index, float(np.mean(det)), dens, wa, bone_of(p), side))
det_m = float(np.median([f[1] for f in faces])); den_m = float(np.median([f[2] for f in faces]))
total = sum(f[3] for f in faces) or 1
smear, blur = set(), set()
regions = {}
for i, d, dn, a, bone, side in faces:
    s = d < det_m / 3; b = dn < den_m / 4
    if s: smear.add(i)
    if b: blur.add(i)
    r = regions.setdefault(bone + ' ' + side, {'area': 0.0, 'smeared': 0.0, 'blurred': 0.0})
    r['area'] += a; r['smeared'] += a if s else 0; r['blurred'] += a if b else 0
rep = {'model': str(model), 'texture': img.filepath, 'size': [W, H], 'medianDetail': det_m, 'medianTexelsPerMetre': den_m,
       'smearedShare': sum(f[3] for f in faces if f[0] in smear) / total, 'blurredShare': sum(f[3] for f in faces if f[0] in blur) / total,
       'regions': {k: {'areaShare': round(v['area'] / total, 4), 'smeared': round(v['smeared'] / max(1e-9, v['area']), 3),
                       'blurred': round(v['blurred'] / max(1e-9, v['area']), 3)} for k, v in regions.items() if v['area'] / total > .004}}
# pictures: smeared red, blurred blue, the rest the texture greyed
col = me.color_attributes.new('smear', 'BYTE_COLOR', 'CORNER')
for p in me.polygons:
    cl = (1, .15, .1, 1) if p.index in smear else (.2, .4, 1, 1) if p.index in blur else (0, 0, 0, 0)
    for li in p.loop_indices: col.data[li].color = cl
mat = bpy.data.materials.new('smear'); mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear()
ti = nt.nodes.new('ShaderNodeTexImage'); ti.image = img
at = nt.nodes.new('ShaderNodeAttribute'); at.attribute_name = 'smear'
mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'
nt.links.new(at.outputs['Alpha'], mix.inputs['Factor']); nt.links.new(ti.outputs['Color'], mix.inputs[6]); nt.links.new(at.outputs['Color'], mix.inputs[7])
em = nt.nodes.new('ShaderNodeEmission'); nt.links.new(mix.outputs[2], em.inputs['Color'])
ou = nt.nodes.new('ShaderNodeOutputMaterial'); nt.links.new(em.outputs[0], ou.inputs['Surface'])
body.data.materials.clear(); body.data.materials.append(mat)
for o in meshes: o.hide_render = o is not body
sc = bpy.context.scene
engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in engines else 'BLENDER_EEVEE'
sc.render.resolution_x = 520; sc.render.resolution_y = 900
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (.12, .12, .13, 1)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.type = 'ORTHO'
vs = [mw @ v.co for v in me.vertices]
lo = Vector([min(v[i] for v in vs) for i in range(3)]); hi = Vector([max(v[i] for v in vs) for i in range(3)]); cc = (lo + hi) / 2
cam.data.ortho_scale = (hi.z - lo.z) * 1.1
pics = []
for name, d in (('front', Vector((0, -1, 0))), ('right', Vector((-1, 0, 0))), ('back', Vector((0, 1, 0))), ('left', Vector((1, 0, 0)))):
    cam.location = cc + d * 6; cam.rotation_euler = (cc - cam.location).to_track_quat('-Z', 'Y').to_euler()
    f = out / ('%s_smear_%s.png' % (model.stem, name)); sc.render.filepath = str(f); bpy.ops.render.render(write_still=True); pics.append(str(f))
rep['pictures'] = pics
(out / 'smear.json').write_text(json.dumps(rep, indent=1))
print('SMEAR_DONE', round(rep['smearedShare'] * 100, 1), '% smeared,', round(rep['blurredShare'] * 100, 1), '% blurred', flush=True)
