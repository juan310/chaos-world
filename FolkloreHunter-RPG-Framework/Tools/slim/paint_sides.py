# Repairs the flanks of a Codex body's texture. Those models were reconstructed from front and back pictures, so the
# surfaces that face sideways (the outsides of the legs and boots, the flanks) came out smeared. Two steps, with the local
# Forge's edit tool in between (free, on this PC):
#   blender -b --python Tools/slim/paint_sides.py -- render <body .fbx> <texture .png> <front.png>
#       the body in the game's relaxed standing pose (moves.RELAX), seen from the front, on a plain dark background: the
#       Forge input, so the side profiles it draws show this model's own armour, cloth and colours
#   (Forge edit, twice: "turn the character 90 degrees to show the left / right side ..." -> left.png, right.png)
#   blender -b --python Tools/slim/paint_sides.py -- paint <body .fbx> <texture .png> <left.png> <right.png> <out .png>
#       the same pose; each profile is fitted to the body's silhouette seen from that side, and only surface that faces
#       that side and is seen from it (a ray to the camera hits nothing) takes the profile's colour, blended in from
#       grazing angles; the front and back stay as they were. Baked into a copy of the texture (<out>); the original
#       texture is not changed. build_slim.py --texture <out> then uses it.
import bpy, sys, math
from pathlib import Path
from mathutils import Vector
sys.path.insert(0, str(Path(__file__).resolve().parent))
import choreo, moves

argv = sys.argv[sys.argv.index('--') + 1:]
mode, fbx, tex = argv[0], argv[1], argv[2]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
choreo.rename_bones(arm); choreo.add_hand_slots(arm)
body = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
for o in [o for o in bpy.data.objects if o.type == 'MESH' and o is not body]: o.hide_render = True
rig = choreo.Rig(arm)
rig.key(rig.solve(dict(moves.RELAX)), 1)
bpy.context.scene.frame_set(1)
bpy.context.view_layer.update()

img = bpy.data.images.load(tex)
mat = bpy.data.materials.new('body'); mat.use_nodes = True
nt = mat.node_tree; tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = img
nt.links.new(tn.outputs[0], nt.nodes['Principled BSDF'].inputs['Base Color'])
nt.nodes['Principled BSDF'].inputs['Roughness'].default_value = .7
body.data.materials.clear(); body.data.materials.append(mat)

dg = bpy.context.evaluated_depsgraph_get()
ev = body.evaluated_get(dg); em = ev.to_mesh()
mw = body.matrix_world; n3 = mw.to_3x3()
P = [mw @ v.co for v in em.vertices]
N = [(n3 @ v.normal).normalized() for v in em.vertices]
polys = [list(pg.vertices) for pg in em.polygons]
xs = [p.x for p in P]; ys = [p.y for p in P]; zs = [p.z for p in P]
lo = Vector((min(xs), min(ys), min(zs))); hi = Vector((max(xs), max(ys), max(zs)))
sc = bpy.context.scene

if mode == 'render':
    out = argv[3]
    sc.render.engine = 'CYCLES'; sc.cycles.samples = 64
    sc.render.resolution_x = 832; sc.render.resolution_y = 1216
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
    w.node_tree.nodes['Background'].inputs[0].default_value = (.06, .06, .065, 1)
    for ang, e in ((-60, 3.0), (60, 2.0), (180, 2.5)):
        L = bpy.data.objects.new('l', bpy.data.lights.new('l', 'SUN')); L.data.energy = e; sc.collection.objects.link(L)
        L.rotation_euler = (math.radians(55), 0, math.radians(ang))
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    c = (lo + hi) / 2; H = hi.z - lo.z
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = H * 1.1
    cam.location = c + Vector((0, -6, 0)); cam.rotation_euler = (math.radians(90), 0, 0)      # the body faces -Y
    sc.render.filepath = out; bpy.ops.render.render(write_still=True)
    print('SIDES_RENDER', out, flush=True)
    sys.exit(0)

# paint
import numpy as np
from mathutils.bvhtree import BVHTree
left_png, right_png, out = argv[3], argv[4], argv[5]
bvh = BVHTree.FromPolygons(P, polys)
me = body.data
sides = {}
for side, png in ((1, left_png), (-1, right_png)):
    # camera on +X sees the character's left (facing -Y, it faces the picture's left edge); on -X, its right
    pim = bpy.data.images.load(png); w, h = pim.size
    px = np.array(pim.pixels[:]).reshape(h, w, 4)[::-1]
    bg = np.median(np.concatenate([px[:4, :, :3].reshape(-1, 3), px[-4:, :, :3].reshape(-1, 3), px[:, :4, :3].reshape(-1, 3)]), axis=0)
    fg = np.abs(px[..., :3] - bg).sum(2) > .12
    if px.shape[2] == 4 and (px[..., 3] < .5).any(): fg &= px[..., 3] > .5        # a cut-out picture: its own alpha too
    # the profile's colour counts only inside the figure (eroded 3 px, so its edge never brings in the background):
    # surface the profile does not cover keeps the original texture
    inner = fg.copy()
    for _ in range(3):
        inner[1:, :] &= inner[:-1, :]; inner[:-1, :] &= inner[1:, :]; inner[:, 1:] &= inner[:, :-1]; inner[:, :-1] &= inner[:, 1:]
    cut = px.copy(); cut[..., 3] = inner.astype(np.float32)
    pim = bpy.data.images.new(Path(png).stem + '_cut', w, h, alpha=True)
    pim.pixels.foreach_set(cut[::-1].astype(np.float32).ravel())
    rows, cols = np.where(fg)
    r0, r1, c0, c1 = rows.min(), rows.max(), cols.min(), cols.max()
    uvs, wts = [], []
    for p, n in zip(P, N):
        depth = (p.y - lo.y) / (hi.y - lo.y) if side > 0 else (hi.y - p.y) / (hi.y - lo.y)
        col = c0 + depth * (c1 - c0); row = r0 + (hi.z - p.z) / (hi.z - lo.z) * (r1 - r0)
        uvs.append((col / w, 1 - row / h))
        facing = n.x * side; wgt = 0.0
        if facing > .35:
            hit = bvh.ray_cast(p + Vector((side * .004, 0, 0)), Vector((side, 0, 0)), 10)
            if hit[0] is None: wgt = min(1.0, (facing - .35) / .3)
        wts.append(wgt)
    sides[side] = (pim, uvs, wts)
    print('SIDES_FIT', 'left' if side > 0 else 'right', 'silhouette', int(c0), int(r0), int(c1), int(r1), 'painted vertices', sum(1 for x in wts if x > .5), flush=True)
uv_base = me.uv_layers.active.name
attr = me.color_attributes.new('side_weights', 'FLOAT_COLOR', 'POINT')
for i, (wl, wr) in enumerate(zip(sides[1][2], sides[-1][2])): attr.data[i].color = (wl, wr, 0, 1)
for side, name in ((1, 'side_left_uv'), (-1, 'side_right_uv')):
    layer = me.uv_layers.new(name=name); uvs = sides[side][1]
    for loop in me.loops: layer.data[loop.index].uv = uvs[loop.vertex_index]
me.uv_layers.active = me.uv_layers[uv_base]
bm = bpy.data.materials.new('SideBake'); bm.use_nodes = True; bt = bm.node_tree; bt.nodes.clear()
uvn = bt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = uv_base
t0 = bt.nodes.new('ShaderNodeTexImage'); t0.image = img; bt.links.new(uvn.outputs[0], t0.inputs[0])
cur = t0.outputs['Color']
an = bt.nodes.new('ShaderNodeAttribute'); an.attribute_name = 'side_weights'
sep = bt.nodes.new('ShaderNodeSeparateColor'); bt.links.new(an.outputs['Color'], sep.inputs[0])
for side, name, ch in ((1, 'side_left_uv', 0), (-1, 'side_right_uv', 1)):
    u = bt.nodes.new('ShaderNodeUVMap'); u.uv_map = name
    t = bt.nodes.new('ShaderNodeTexImage'); t.image = sides[side][0]; t.extension = 'CLIP'; bt.links.new(u.outputs[0], t.inputs[0])
    fac = bt.nodes.new('ShaderNodeMath'); fac.operation = 'MULTIPLY'
    bt.links.new(sep.outputs[ch], fac.inputs[0]); bt.links.new(t.outputs['Alpha'], fac.inputs[1])       # weight x inside the figure
    mix = bt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'
    bt.links.new(fac.outputs[0], mix.inputs['Factor']); bt.links.new(cur, mix.inputs[6]); bt.links.new(t.outputs['Color'], mix.inputs[7])
    cur = mix.outputs[2]
emn = bt.nodes.new('ShaderNodeEmission'); bt.links.new(cur, emn.inputs['Color'])
on = bt.nodes.new('ShaderNodeOutputMaterial'); bt.links.new(emn.outputs[0], on.inputs['Surface'])
target = bpy.data.images.new('SideBaked', img.size[0], img.size[1])
tt = bt.nodes.new('ShaderNodeTexImage'); tt.image = target; bt.nodes.active = tt
me.materials.clear(); me.materials.append(bm)
sc.render.engine = 'CYCLES'; sc.cycles.samples = 1
for o in bpy.context.view_layer.objects: o.select_set(o == body)
bpy.context.view_layer.objects.active = body
bpy.ops.object.bake(type='EMIT', margin=4, use_clear=True)
target.filepath_raw = out; target.file_format = 'PNG'; target.save()
print('SIDES_PAINTED', out, flush=True)
