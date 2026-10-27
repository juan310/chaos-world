# The hands check, Blender side: a built model with finger bones and its grips file (<model>_grips.json). Each grip is
# put on the hand the way the game does it (each finger joint curled about finger direction x palm direction), then
# measured on the skinned mesh against the held piece's own mesh: finger skin inside the piece (deeper than 2 mm), each
# finger's gap to the piece (a finger meant to close on the handle that stays clear of it), and pictures of each grip
# from three sides; the relaxed hand and the bow-string hook are pictured too.
#   blender -b --python hands_blender.py -- <model.fbx> <grips.json> <out folder>
import bpy, sys, json, math
from pathlib import Path
from mathutils import Vector, Quaternion
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:]
fbx, gpath, out = Path(argv[0]), Path(argv[1]), Path(argv[2]); out.mkdir(parents=True, exist_ok=True)
G = json.loads(gpath.read_text())
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(fbx))
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
body = next((o for o in meshes if o.name.endswith('_Body')), max(meshes, key=lambda o: len(o.data.vertices)))
pieces = {o.name: o for o in meshes if o is not body and o.parent == arm and o.parent_type == 'BONE'}
AM = arm.matrix_world
FINGERS = ('thumb', 'index', 'middle', 'ring', 'pinky')
tilt = G.get('thumbTilt', .7)
rep = {'model': fbx.name, 'grips': {}, 'missing': []}


def bone(n): return arm.data.bones.get(n)


def palm_normal(side):
    hb, sl = bone('hand.' + side), bone('handslot.' + side)
    h0, h1 = AM @ hb.head_local, AM @ hb.tail_local; hd = (h1 - h0).normalized()
    v = AM @ sl.head_local - h0
    return (v - hd * v.dot(hd)).normalized()


def pose_hand(side, pose):
    """the same curl the game's HandGrip applies: about (finger direction x palm direction) at rest, in each bone's frame"""
    n = palm_normal(side)
    ib, pb_ = bone('index_1.' + side), bone('pinky_1.' + side)
    tp = ((AM @ pb_.head_local) - (AM @ ib.head_local)).normalized()
    for f in FINGERS:
        ang = pose.get(f, (0, 0, 0))
        for k in range(3):
            b = bone('%s_%d.%s' % (f, k + 1, side))
            if not b: continue
            d = (AM.to_3x3() @ (b.tail_local - b.head_local)).normalized()
            nn = (n + tp * tilt).normalized() if f == 'thumb' else n
            ax_w = d.cross(nn).normalized()
            ax_b = (AM.to_3x3() @ b.matrix_local.to_3x3()).inverted() @ ax_w
            p = arm.pose.bones[b.name]; p.rotation_mode = 'QUATERNION'
            p.rotation_quaternion = Quaternion(ax_b.normalized(), math.radians(ang[k]))


def reset():
    for p in arm.pose.bones: p.rotation_mode = 'QUATERNION'; p.rotation_quaternion = Quaternion(); p.location = (0, 0, 0)


def finger_verts(side):
    gi = {g.index: g.name for g in body.vertex_groups}
    out = {f: [] for f in FINGERS}
    for v in body.data.vertices:
        best = None
        for g in v.groups:
            nm = gi.get(g.group, '')
            if nm.endswith('.' + side) and nm.split('_')[0] in out and g.weight > .4:
                if best is None or g.weight > best[1]: best = (nm.split('_')[0], g.weight)
        if best: out[best[0]].append(v.index)
    return out


def evaluated():
    dg = bpy.context.evaluated_depsgraph_get(); e = body.evaluated_get(dg)
    m = e.to_mesh(); W = body.matrix_world
    pts = [W @ v.co for v in m.vertices]; e.to_mesh_clear()
    return pts


def piece_bvh(o):
    dg = bpy.context.evaluated_depsgraph_get(); e = o.evaluated_get(dg); m = e.to_mesh()
    W = o.matrix_world
    bvh = BVHTree.FromPolygons([W @ v.co for v in m.vertices], [tuple(p.vertices) for p in m.polygons])
    e.to_mesh_clear()
    return bvh


# pictures
sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'; sc.render.resolution_x = sc.render.resolution_y = 560
sc.display.shading.light = 'STUDIO'; sc.display.shading.show_cavity = True; sc.display.shading.color_type = 'MATERIAL'
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 70


def shoot(side, tag):
    pb = arm.pose.bones['hand.' + side]
    M = AM @ pb.matrix
    # in world units (an imported armature may be in centimetres): the hand's middle, a little past its knuckles
    h0, h1 = M @ Vector((0, 0, 0)), M @ Vector((0, pb.bone.length, 0))
    L = (h1 - h0).length
    tip = bone('middle_3.' + side)
    c = (h0 + (AM @ arm.pose.bones[tip.name].tail)) / 2 if tip else h0 + (h1 - h0) * .45
    pics = []
    print('HANDS_FRAME %s %s centre %s hand %.3f m, body %s..%s' % (tag, side, tuple(round(x, 3) for x in c), L,
          tuple(round(min((body.matrix_world @ Vector(b))[i] for b in body.bound_box), 2) for i in range(3)),
          tuple(round(max((body.matrix_world @ Vector(b))[i] for b in body.bound_box), 2) for i in range(3))), flush=True)
    for nm, d in (('back', Vector((0, 0, -1))), ('palm', Vector((0, 0, 1))), ('thumb side', Vector((1 if side == 'l' else -1, 0, 0)))):
        dw = (M.to_3x3() @ d).normalized()
        cam.location = c + dw * (L * 2.2 + .15)
        cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
        fn = out / ('%s_%s_%s.png' % (tag, side, nm.replace(' ', '_')))
        sc.render.filepath = str(fn); bpy.ops.render.render(write_still=True); pics.append(fn.name)
    return pics


if not bone('index_1.l') and not bone('index_1.r'):
    rep['missing'].append('no finger bones in the model (built without --fingers?)')
else:
    for pname, side in G.get('pieces', {}).items():
        o = pieces.get(pname)
        if not o: rep['missing'].append(pname); continue
        for q in pieces.values(): q.hide_render = q is not o
        reset(); pose_hand(side, G['poses'][pname][side]); bpy.context.view_layer.update()
        P = evaluated(); bvh = piece_bvh(o); fv = finger_verts(side)
        res = {}
        for f, idx in fv.items():
            if not idx: continue
            inside, deepest, gap = 0, 0.0, math.inf
            for i in idx:
                hit = bvh.find_nearest(P[i])
                if not hit[0]: continue
                loc, nor, _, dist = hit
                if (P[i] - loc).dot(nor) < 0:
                    if dist > .002: inside += 1
                    deepest = max(deepest, dist); gap = 0
                else:
                    gap = min(gap, dist)
            res[f] = {'vertices': len(idx), 'inside': inside, 'deepest': round(deepest, 4), 'gap': round(gap, 4) if gap < math.inf else None,
                      'curl': G['poses'][pname][side].get(f), 'solver': G.get('contacts', {}).get(pname, {}).get('fingers', {}).get(f)}
        rep['grips'][pname] = {'side': side, 'fingers': res, 'handleRadius': G.get('contacts', {}).get(pname, {}).get('radius'), 'pictures': shoot(side, pname)}
    for q in pieces.values(): q.hide_render = True
    for pose in ('relaxed', 'hook'):
        for side in G['poses'].get(pose, {}):
            reset(); pose_hand(side, G['poses'][pose][side]); bpy.context.view_layer.update()
            rep['grips'][pose + '.' + side] = {'side': side, 'pose': pose, 'pictures': shoot(side, pose)}
(out / 'hands.json').write_text(json.dumps(rep, indent=1))
print('HANDS_DONE', len(rep['grips']), flush=True)
