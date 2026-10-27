"""Claude Eyes 3D mesh system, the Blender side (run by eyeslib/mesh3d.py; any rigged 3D model).

  blender -b --python mesh3d_blender.py -- <model .fbx|.glb> <out dir> [--clips a,b] [--config config.json]

Every clip (action) of the model is played and the skinned body measured at evenly spaced frames against its rest shape:
  stretch   vertices on edges grown past `stretch_max` or shrunk under `stretch_min` times their rest length (skin that
            tears or pinches at a joint), counted per bone that carries them;
  clipping  points of the held pieces (other meshes riding on the skeleton) that lie inside the body: a ray along four
            directions crosses the body's surface an odd number of times in at least three of them;
  ground    the body's lowest point under the soles' rest height (the model sinking through the floor), outside the
            clips that lie, sit or ride on purpose.
The worst frame of each clip that shows something is rendered: the offending skin red, the intruding points yellow.
Writes <out>/mesh3d.json (and the pictures). The config (JSON, all optional):
  {"held": {"<clip name prefix>": ["<piece>", ...]}, "ungrounded": ["<clip name prefix>", ...],
   "stretch_max": 1.7, "stretch_min": 0.45, "samples": 12, "skip": ["<piece name part>", ...]}
"held" says which pieces a clip shows (a game may carry several weapons and show one); without it every piece counts.
"""
import bpy, sys, json
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
import numpy as np

argv = sys.argv[sys.argv.index('--') + 1:]
model, out = Path(argv[0]), Path(argv[1])
def opt(flag, default=None): return argv[argv.index(flag) + 1] if flag in argv else default
only = set(x for x in (opt('--clips') or '').split(',') if x)
cfg = json.loads(Path(opt('--config')).read_text()) if opt('--config') else {}
SMAX, SMIN = float(cfg.get('stretch_max', 1.7)), float(cfg.get('stretch_min', .45))
SAMPLES = int(cfg.get('samples', 12))
UNGROUNDED = tuple(cfg.get('ungrounded', ['Lie', 'Death', 'Sit', 'Ride', 'Dodge_Forward', 'Roll']))
SKIP = cfg.get('skip', [])
out.mkdir(parents=True, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
if model.suffix.lower() in ('.glb', '.gltf'): bpy.ops.import_scene.gltf(filepath=str(model))
else: bpy.ops.import_scene.fbx(filepath=str(model))
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
skinned = [o for o in meshes if any(m.type == 'ARMATURE' for m in o.modifiers)]
body = max(skinned or meshes, key=lambda o: len(o.data.vertices))
pieces = {o.name: o for o in meshes if o is not body and not any(s in o.name for s in SKIP)}
sc = bpy.context.scene

def clean(n): return n.split('|')[-1]
def held(clip):
    m = cfg.get('held')
    if not m: return list(pieces)
    for prefix, names in m.items():
        if clip.startswith(prefix): return [n for n in names if n in pieces]
    return []

M = np.array(body.matrix_world)
def world_co(me):
    a = np.empty(len(me.vertices) * 3, np.float64); me.vertices.foreach_get('co', a); a = a.reshape(-1, 3)
    return a @ M[:3, :3].T + M[:3, 3]
if arm.animation_data: arm.animation_data.action = None
for pb in arm.pose.bones: pb.matrix_basis.identity()
bpy.context.view_layer.update()
rest_me = body.data
rest_co = world_co(rest_me)
E = np.empty(len(rest_me.edges) * 2, np.int64); rest_me.edges.foreach_get('vertices', E); E = E.reshape(-1, 2)
rest_len = np.linalg.norm(rest_co[E[:, 0]] - rest_co[E[:, 1]], axis=1); okE = rest_len > 1e-5
sole = rest_co[:, 2].min()
faces = [tuple(p.vertices) for p in rest_me.polygons]
groups = {g.index: g.name for g in body.vertex_groups}
def main_bone(vi):
    gs = rest_me.vertices[vi].groups
    return groups.get(max(gs, key=lambda g: g.weight).group, '?') if len(gs) else '?'
face_bone = [main_bone(f[0]) for f in faces]
# a held piece meets the hand and forearm that hold it by design (a fist round a grip, inside a shield's boss): only the
# rest of the body counts. The holding side is the side of the bone the piece rides on (handslot.l: the left hand).
def holder(pn):
    o = pieces[pn]
    b = o.parent_bone if o.parent_type == 'BONE' else ''
    side = b[-1] if b[-2:-1] == '.' else None
    if not side: return set()
    return {n for n in groups.values() if n.endswith('.' + side) and (n.split('.')[0] in ('hand', 'lowerarm', 'handslot') or n.split('_')[0] in ('thumb', 'index', 'middle', 'ring', 'pinky'))}

def crossings(bvh, p, d):
    n, q = 0, p.copy()
    for _ in range(24):
        hit = bvh.ray_cast(q, d, 10.0)
        if hit[0] is None: break
        n += 1; q = hit[0] + d * 1e-4
    return n
def inside(bvh, p):
    return sum(crossings(bvh, p, d) % 2 for d in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0)))) >= 3

report = {'model': str(model), 'body': body.name, 'pieces': list(pieces), 'vertices': len(rest_co),
          'thresholds': {'stretch_max': SMAX, 'stretch_min': SMIN, 'samples': SAMPLES}, 'clips': {}}
for act in [a for a in bpy.data.actions if not only or clean(a.name) in only]:
    cname = clean(act.name)
    arm.animation_data_create(); arm.animation_data.action = act
    try:
        if act.slots: arm.animation_data.action_slot = act.slots[0]
    except AttributeError: pass
    f0, f1 = act.frame_range
    frames = sorted(set(int(round(f0 + (f1 - f0) * i / max(1, SAMPLES - 1))) for i in range(SAMPLES)))
    clip = {'frames': frames, 'stretch': 0.0, 'clipping': 0, 'clipDepthCm': 0.0, 'ground': 0.0, 'where': {}}
    worst = None
    for f in frames:
        sc.frame_set(f)
        dg = bpy.context.evaluated_depsgraph_get()
        ev = body.evaluated_get(dg); me = ev.to_mesh()
        cn = world_co(me)
        ratio = np.linalg.norm(cn[E[:, 0]] - cn[E[:, 1]], axis=1) / np.where(okE, rest_len, 1)
        bad = set(np.unique(E[okE & ((ratio > SMAX) | (ratio < SMIN))]).tolist())
        bvh = BVHTree.FromPolygons([Vector(p) for p in cn], faces)
        intr = []; meets = {}
        for pn in held(cname):
            eo = pieces[pn].evaluated_get(dg); own = holder(pn)
            for v in list(eo.data.vertices)[::3]:
                p = eo.matrix_world @ v.co
                hit = bvh.find_nearest(p)
                if hit[0] is None or hit[3] < .012 or hit[3] > .3: continue
                part = face_bone[hit[2]] if hit[2] is not None and hit[2] < len(face_bone) else '?'
                if part in own: continue
                if inside(bvh, p):
                    intr.append((p.copy(), hit[3])); key = pn + ' into ' + part; meets[key] = max(meets.get(key, 0), hit[3])
        low = float(cn[:, 2].min() - sole)
        ground = -low if low < -.04 and not cname.startswith(UNGROUNDED) else 0.0
        share = len(bad) / max(1, len(cn)) * 100
        for vi in list(bad)[:4000]:
            bn = main_bone(vi); clip['where'][bn] = clip['where'].get(bn, 0) + 1
        clip['stretch'] = max(clip['stretch'], round(share, 2))
        if len(intr) > clip['clipping']: clip['meets'] = {k: round(v * 100, 1) for k, v in sorted(meets.items(), key=lambda kv: -kv[1])[:4]}
        clip['clipping'] = max(clip['clipping'], len(intr))
        if intr: clip['clipDepthCm'] = max(clip['clipDepthCm'], round(max(d for _, d in intr) * 100, 1))
        clip['ground'] = max(clip['ground'], round(ground * 100, 1))
        score = share + len(intr) * .5 + ground * 100
        if worst is None or score > worst[0]: worst = (score, f, bad, intr)
        ev.to_mesh_clear()
    clip['where'] = dict(sorted(clip['where'].items(), key=lambda kv: -kv[1])[:4])
    clip['worstFrame'] = worst[1] if worst else None
    report['clips'][cname] = clip
    print('MESH3D', cname, json.dumps(clip), flush=True)
    if worst and worst[0] > 1.0:
        sc.frame_set(worst[1]); dg = bpy.context.evaluated_depsgraph_get()
        col = body.data.color_attributes.get('mesh3d') or body.data.color_attributes.new('mesh3d', 'BYTE_COLOR', 'POINT')
        for i in range(len(body.data.vertices)): col.data[i].color = (1, .1, .05, 1) if i in worst[2] else (.75, .75, .75, 1)
        mat = bpy.data.materials.get('mesh3d') or bpy.data.materials.new('mesh3d'); mat.use_nodes = True
        nt = mat.node_tree; nt.nodes.clear()
        at = nt.nodes.new('ShaderNodeAttribute'); at.attribute_name = 'mesh3d'
        em = nt.nodes.new('ShaderNodeEmission'); nt.links.new(at.outputs['Color'], em.inputs['Color'])
        ou = nt.nodes.new('ShaderNodeOutputMaterial'); nt.links.new(em.outputs[0], ou.inputs['Surface'])
        body.data.materials.clear(); body.data.materials.append(mat)
        shown = set(held(cname))
        for o in meshes: o.hide_render = o is not body and o.name not in shown
        marks = []
        ym = bpy.data.materials.get('mesh3d_point') or bpy.data.materials.new('mesh3d_point'); ym.use_nodes = True
        ym.node_tree.nodes['Principled BSDF'].inputs['Emission Color'].default_value = (1, .9, 0, 1)
        ym.node_tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value = 3
        for p, _ in worst[3][:200]:
            bpy.ops.mesh.primitive_uv_sphere_add(radius=.012, location=p, segments=8, ring_count=4)
            s = bpy.context.active_object; s.data.materials.append(ym); marks.append(s)
        cam = sc.camera
        if not cam:
            cam = bpy.data.objects.new('mesh3d_cam', bpy.data.cameras.new('mesh3d_cam')); sc.collection.objects.link(cam); sc.camera = cam
            cam.data.type = 'ORTHO'
            engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
            sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in engines else 'BLENDER_EEVEE'
            sc.render.resolution_x = 640; sc.render.resolution_y = 900
            w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (.1, .1, .11, 1)
        ev = body.evaluated_get(dg); pts = world_co(ev.to_mesh()); ev.to_mesh_clear()
        lo = Vector(pts.min(0)); hi = Vector(pts.max(0)); c = (lo + hi) / 2
        cam.data.ortho_scale = max(hi - lo) * 1.25
        cam.location = c + Vector((4, -5, 1.0)) * 2; cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
        pic = out / ('%s_%s.png' % (model.stem, cname)); sc.render.filepath = str(pic)
        bpy.ops.render.render(write_still=True)
        clip['picture'] = str(pic)
        for s in marks: bpy.data.objects.remove(s)
        for o in meshes: o.hide_render = False
(out / 'mesh3d.json').write_text(json.dumps(report, indent=1))
print('MESH3D_DONE', len(report['clips']), flush=True)
