# The 3D mesh check (the SightForge mesh system's idea, for the game's 3D models): every clip of an exported model is
# played and its skinned body measured frame by frame against its rest shape:
#   stretch   triangles whose edges grow past 1.7x or shrink under 0.45x of their rest length (a torn shoulder, a
#             candy-wrapper wrist, a pinched knee), by the bone that carries them;
#   clipping  a held weapon passing through the body: the weapon's vertices that lie inside the body's surface (the
#             nearest body surface faces away from them), with the depth;
#   ground    the body sinking under the ground (the lowest point more than 4 cm below the soles' rest height), outside
#             the clips that lie down on purpose.
# The worst frame of each clip with a finding is rendered with the offending triangles in red and intruding weapon
# points in yellow (<out>/<model>_<clip>.png); the numbers go to <out>/<model>_mesh_check.json.
#   blender -b --python Tools/slim/mesh_check.py -- <model .fbx> <out dir> [clip ...]
import bpy, bmesh, sys, json, math
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:]
fbx, out = Path(argv[0]), Path(argv[1]); only = set(argv[2:])
out.mkdir(parents=True, exist_ok=True)
name = fbx.stem
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(fbx))
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
body = max(meshes, key=lambda o: len(o.data.vertices))
pieces = {o.name: o for o in meshes if o is not body and not o.name.endswith('Helmet')}
sc = bpy.context.scene

# which held pieces each clip family shows in the game
def held(clip):
    if clip.startswith(('Bow_',)): return ['Hunt_Bow', 'Hunt_NockedArrow']
    if clip.startswith(('2H_', 'Katana_')): return ['Hunt_Katana']
    if clip.startswith('Lance_'): return ['Hunt_Lance', 'Hunt_TowerShield']
    if clip.startswith(('1H_', 'SnS_', 'Block', 'Blocking', 'Idle_Combat', 'Idle_B', 'Hunt_Clash')): return ['1H_Sword', 'Round_Shield']
    return []
LYING = ('Lie_', 'Death', 'Dodge_Forward', 'Jump_Full_Short', 'Sit_')

import numpy as np
M = np.array(body.matrix_world)
def world_co(me):
    a = np.empty(len(me.vertices) * 3, np.float64); me.vertices.foreach_get('co', a); a = a.reshape(-1, 3)
    return a @ M[:3, :3].T + M[:3, 3]
# rest shape: edge lengths and the soles' height
rest_me = body.data
rest_co = world_co(rest_me)
E = np.empty(len(rest_me.edges) * 2, np.int64); rest_me.edges.foreach_get('vertices', E); E = E.reshape(-1, 2)
rest_len = np.linalg.norm(rest_co[E[:, 0]] - rest_co[E[:, 1]], axis=1)
okE = rest_len > 1e-5
sole = rest_co[:, 2].min()
faces = [tuple(p.vertices) for p in rest_me.polygons]
# the bone carrying each vertex most (for "where")
groups = {g.index: g.name for g in body.vertex_groups}
def main_bone(vi):
    gs = rest_me.vertices[vi].groups
    return groups.get(max(gs, key=lambda g: g.weight).group, '?') if len(gs) else '?'

report = {'model': name, 'clips': {}}
def clean(n): return n.split('|')[-1]
acts = [a for a in bpy.data.actions if not only or clean(a.name) in only]
for act in acts:
    cname = clean(act.name)
    arm.animation_data_create(); arm.animation_data.action = act
    try:
        if act.slots: arm.animation_data.action_slot = act.slots[0]
    except AttributeError: pass
    f0, f1 = act.frame_range
    frames = sorted(set(int(round(f0 + (f1 - f0) * i / 11)) for i in range(12)))
    worst = None; clip = {'stretch': 0, 'clipping': 0, 'ground': 0.0, 'where': {}}
    for f in frames:
        sc.frame_set(f)
        dg = bpy.context.evaluated_depsgraph_get()
        ev = body.evaluated_get(dg); me = ev.to_mesh()
        cn = world_co(me)
        ratio = np.linalg.norm(cn[E[:, 0]] - cn[E[:, 1]], axis=1) / np.where(okE, rest_len, 1)
        badE = okE & ((ratio > 1.7) | (ratio < .45))
        bad = set(np.unique(E[badE]).tolist())
        co = [Vector(p) for p in cn]
        bvh = BVHTree.FromPolygons(co, faces)
        intr = []
        for pn in held(cname):
            o = pieces.get(pn)
            if not o: continue
            eo = o.evaluated_get(dg)
            for v in eo.data.vertices[::3]:
                p = eo.matrix_world @ v.co
                hit = bvh.find_nearest(p)
                if hit[0] is None: continue
                loc, nrm, idx, d = hit
                if (p - loc).dot(nrm) < -.012 and d > .012:      # behind the surface: inside the body
                    intr.append((p.copy(), d))
        low = cn[:, 2].min() - sole
        ground = -low if low < -.04 and not cname.startswith(LYING) else 0.0
        score = len(bad) / max(1, len(co)) * 100 + len(intr) * .5 + float(ground) * 100
        for vi in list(bad)[:4000]:
            bn = main_bone(vi); clip['where'][bn] = clip['where'].get(bn, 0) + 1
        clip['stretch'] = max(clip['stretch'], round(len(bad) / max(1, len(co)) * 100, 2))
        clip['clipping'] = max(clip['clipping'], len(intr))
        if intr: clip['clipDepthCm'] = max(clip.get('clipDepthCm', 0), round(max(d for _, d in intr) * 100, 1))
        clip['ground'] = max(clip['ground'], round(float(ground) * 100, 1))
        if worst is None or score > worst[0]: worst = (score, f, bad, intr)
        ev.to_mesh_clear()
    clip['where'] = dict(sorted(clip['where'].items(), key=lambda kv: -kv[1])[:4])
    found = clip['stretch'] > .5 or clip['clipping'] > 3 or clip['ground'] > 0
    clip['verdict'] = 'check' if found else 'ok'
    report['clips'][cname] = clip
    print('MESH_CHECK', name, cname, clip['verdict'].upper(), json.dumps({k: v for k, v in clip.items() if k != 'verdict'}), flush=True)
    if found and worst:
        # the worst frame: offending triangles red, intruding weapon points yellow
        sc.frame_set(worst[1])
        dg = bpy.context.evaluated_depsgraph_get()
        col = body.data.color_attributes.get('check') or body.data.color_attributes.new('check', 'BYTE_COLOR', 'POINT')
        for i in range(len(body.data.vertices)): col.data[i].color = (1, 0, 0, 1) if i in worst[2] else (.75, .75, .75, 1)
        mat = bpy.data.materials.get('checkmat') or bpy.data.materials.new('checkmat'); mat.use_nodes = True
        nt = mat.node_tree; nt.nodes.clear()
        at = nt.nodes.new('ShaderNodeAttribute'); at.attribute_name = 'check'
        em = nt.nodes.new('ShaderNodeEmission'); nt.links.new(at.outputs['Color'], em.inputs['Color'])
        ou = nt.nodes.new('ShaderNodeOutputMaterial'); nt.links.new(em.outputs[0], ou.inputs['Surface'])
        body.data.materials.clear(); body.data.materials.append(mat)
        for o in meshes: o.hide_render = o is not body and o.name not in held(cname)
        marks = []
        for p, _ in worst[3][:200]:
            bpy.ops.mesh.primitive_uv_sphere_add(radius=.012, location=p, segments=8, ring_count=4)
            s = bpy.context.active_object; marks.append(s)
            ym = bpy.data.materials.get('yel') or bpy.data.materials.new('yel'); ym.use_nodes = True
            ym.node_tree.nodes['Principled BSDF'].inputs['Emission Color'].default_value = (1, .9, 0, 1)
            ym.node_tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value = 3
            s.data.materials.append(ym)
        cam = sc.camera
        if not cam:
            cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
            cam.data.type = 'ORTHO'
            engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
            sc.render.resolution_x = 900; sc.render.resolution_y = 600
            sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in engines else 'BLENDER_EEVEE' 
            w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (.1, .1, .11, 1)
        ev = body.evaluated_get(dg); pts = world_co(ev.to_mesh()); ev.to_mesh_clear()
        lo = Vector(pts.min(0)); hi = Vector(pts.max(0))
        c = (lo + hi) / 2; size = max(hi - lo) * 1.15
        cam.data.ortho_scale = size
        # two views side by side would need two renders: front three-quarter (the body faces -Y)
        cam.location = c + Vector((4, -5, 1.5)); cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = str(out / ('%s_%s.png' % (name, cname)))
        bpy.ops.render.render(write_still=True)
        for s in marks: bpy.data.objects.remove(s)
        for o in meshes: o.hide_render = False
(out / ('%s_mesh_check.json' % name)).write_text(json.dumps(report, indent=1))
flagged = [k for k, v in report['clips'].items() if v['verdict'] != 'ok']
print('MESH_CHECK_DONE', name, len(report['clips']), 'clips,', len(flagged), 'to look at:', ' '.join(flagged), flush=True)
