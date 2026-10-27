# Looks at a body's hands before fingers are rigged: the vertices the hand bones carry, measured in each hand's own
# frame (along the hand, across the knuckles, through the palm), cross-sections out toward the fingertips (how many
# separate pieces each one cuts: separate fingers, or one mitten), and close-up pictures of each hand from three sides.
#   blender -b --python Tools/slim/hands_probe.py -- <body .fbx> <out folder>
import bpy, sys, json, math
from pathlib import Path
from mathutils import Vector, Matrix
sys.path.insert(0, str(Path(__file__).resolve().parent))
import choreo

argv = sys.argv[sys.argv.index('--') + 1:]
fbx, out = Path(argv[0]), Path(argv[1]); out.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(fbx))
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
choreo.rename_bones(arm)
body = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
me = body.data
gi = {g.index: g.name for g in body.vertex_groups}
rep = {'model': fbx.name, 'vertices': len(me.vertices), 'bones': [b.name for b in arm.data.bones], 'hands': {}}


def hand_frame(side):
    b = arm.data.bones['hand.' + side]
    M = arm.matrix_world @ b.matrix_local          # bone space: Y along the hand, X and Z across
    return b, M, M.inverted()


for side in ('l', 'r'):
    b, M, Mi = hand_frame(side)
    W = body.matrix_world
    pts = []
    for v in me.vertices:
        w = {gi[g.group]: g.weight for g in v.groups if g.group in gi}
        if not w: continue
        top = max(w, key=w.get)
        if top == 'hand.' + side and w[top] > .5:
            pts.append(Mi @ (W @ v.co))
    if not pts:
        rep['hands'][side] = {'vertices': 0}; continue
    ys = [p.y for p in pts]; y0, y1 = min(ys), max(ys)
    span = lambda a: (min(getattr(p, a) for p in pts), max(getattr(p, a) for p in pts))
    # cross-sections from the palm out: the pieces along each slice (gaps over 3 mm between neighbouring points across
    # the knuckles X, then across the palm Z), how many pieces a slice cuts
    slices = []
    for t in [.35 + .05 * i for i in range(14)]:
        y = y0 + (y1 - y0) * t; band = [p for p in pts if abs(p.y - y) < (y1 - y0) * .025]
        if len(band) < 3: slices.append({'t': round(t, 2), 'points': len(band), 'pieces': 0}); continue
        xs = sorted(p.x for p in band); pieces = 1 + sum(1 for a, c in zip(xs, xs[1:]) if c - a > .003)
        zs = sorted(p.z for p in band); zp = 1 + sum(1 for a, c in zip(zs, zs[1:]) if c - a > .003)
        slices.append({'t': round(t, 2), 'points': len(band), 'pieces': pieces, 'piecesZ': zp, 'width': round(xs[-1] - xs[0], 4)})
    rep['hands'][side] = {'vertices': len(pts), 'boneLength': round(b.length * arm.matrix_world.to_scale().y, 4),
                          'along': [round(y0, 4), round(y1, 4)], 'across': [round(x, 4) for x in span('x')], 'through': [round(z, 4) for z in span('z')],
                          'slices': slices}

# close-ups: workbench, each hand from the back of the hand, the palm and the side
sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'; sc.render.resolution_x = sc.render.resolution_y = 640
sc.display.shading.light = 'STUDIO'; sc.display.shading.show_cavity = True; sc.display.shading.color_type = 'TEXTURE'
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.lens = 85
for side in ('l', 'r'):
    b, M, Mi = hand_frame(side)
    c = M @ Vector((0, b.length * .9, 0))
    for nm, d in (('back', Vector((0, 0, 1))), ('palm', Vector((0, 0, -1))), ('side', Vector((1, 0, 0)))):
        dirw = (M.to_3x3() @ d).normalized()
        cam.location = c + dirw * .55
        cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = str(out / ('hand_%s_%s.png' % (side, nm)))
        bpy.ops.render.render(write_still=True)
(out / 'hands.json').write_text(json.dumps(rep, indent=1))
print('HANDS_PROBE', json.dumps({s: {k: v for k, v in h.items() if k != 'slices'} for s, h in rep['hands'].items()}), flush=True)
for s, h in rep['hands'].items():
    print('HANDS_SLICES', s, ' '.join('%s:%s/%s' % (x['t'], x['pieces'], x.get('piecesZ', 0)) for x in h.get('slices', [])), flush=True)
