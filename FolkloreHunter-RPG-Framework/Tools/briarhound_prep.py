# Blender (headless), step 1 of the Briarhound build: takes the studio's Forge job (TripoSG mesh + texture made from
# the concept art), turns it to face -Y with its paws on z = 0 and scales it to a 1.9 m overall height (the same frame
# as the Cu-sith), finds the four paws, and writes the normalized mesh plus arrays for the texture and rig steps.
#   blender -b --python Tools/briarhound_prep.py -- <forge job folder>
import bpy, sys, json, math
import numpy as np
from pathlib import Path
from mathutils import Matrix

job = Path(sys.argv[sys.argv.index('--') + 1])
r = Path.home() / 'Documents/FolkloreHunter-RPG-Framework'
work = r / 'SourceArt/Briarhound'; work.mkdir(parents=True, exist_ok=True)
HEIGHT = 1.9

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(job / 'model.glb'))
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
body = bpy.context.view_layer.objects.active; body.name = 'BriarhoundBody'
for o in list(bpy.context.scene.objects):
    if o is not body and o.type != 'MESH': bpy.data.objects.remove(o, do_unlink=True)
body.parent = None
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
V = np.array([v.co[:] for v in body.data.vertices])

# Body runs along the longer horizontal axis; the head end is the one whose front quarter stands taller (ears).
ax = 0 if np.ptp(V[:, 0]) > np.ptp(V[:, 1]) else 1
lo, hi = V[:, ax].min(), V[:, ax].max(); q = (hi - lo) * .3
top_lo = V[V[:, ax] < lo + q, 2].max(); top_hi = V[V[:, ax] > hi - q, 2].max()
head_dir = np.zeros(3); head_dir[ax] = -1 if top_lo > top_hi else 1
yaw = math.atan2(head_dir[1], head_dir[0])           # direction the head points now
turn = -math.pi / 2 - yaw                              # rotate it onto -Y
R = Matrix.Rotation(turn, 4, 'Z')
body.data.transform(R); body.data.update()
V = np.array([v.co[:] for v in body.data.vertices])
k = HEIGHT / np.ptp(V[:, 2])
V[:, 2] -= V[:, 2].min(); V *= k

# Paws: the lowest 4.5% of the height, four clusters (front/hind x left/right).
low = V[V[:, 2] < HEIGHT * .045][:, :2]
ymid = np.median(low[:, 1]); xmid = np.median(low[:, 0])
centers = np.array([[xmid + s * .2, ymid + f * .5] for f in (-1, 1) for s in (1, -1)])
for _ in range(30):
    lab = np.argmin(((low[:, None, :] - centers[None]) ** 2).sum(2), 1)
    centers = np.array([low[lab == i].mean(0) if (lab == i).any() else centers[i] for i in range(4)])
order = np.argsort(centers[:, 1]); front, hind = centers[order[:2]], centers[order[2:]]
xc = (centers[:, 0].mean()); yc = (front[:, 1].mean() + hind[:, 1].mean()) / 2
V[:, 0] -= xc; V[:, 1] -= yc; centers -= [xc, yc]
for i, v in enumerate(body.data.vertices): v.co = V[i]
body.data.update()
paws = {}
for i, c in enumerate(centers):
    region = 'fore' if c[1] < 0 else 'hind'; side = 'l' if c[0] > 0 else 'r'   # facing -Y, the animal's left is +X
    pts = low[lab == i] - [xc, yc]
    paws[region + '.' + side] = {'center': [float(c[0]), float(c[1])], 'toe': float(pts[:, 1].min()), 'heel': float(pts[:, 1].max()),
                                 'width': float(np.ptp(pts[:, 0])), 'points': int(len(pts))}

F = np.array([p.vertices[:] for p in body.data.polygons])
uvl = body.data.uv_layers.active.data
UV = np.array([[uvl[li].uv[:] for li in p.loop_indices] for p in body.data.polygons])
img = next((n.image for m in body.data.materials if m and m.use_nodes for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image), None)
tex_path = work / 'forge_texture.png'
if img:
    img.filepath_raw = str(tex_path); img.file_format = 'PNG'; img.save()
np.savez(work / 'briarhound_arrays.npz', V=V, F=F, UV=UV)
bpy.ops.wm.save_as_mainfile(filepath=str(work / 'briarhound_norm.blend'))
info = {'job': str(job), 'longAxis': 'xy'[ax], 'headWas': head_dir.tolist(), 'turnDeg': math.degrees(turn), 'scale': float(k),
        'size': np.ptp(V, 0).round(3).tolist(), 'vertices': len(V), 'triangles': len(F), 'paws': paws, 'texture': str(tex_path) if img else None}
(work / 'prep.json').write_text(json.dumps(info, indent=2))
print('BRIARHOUND_PREP', json.dumps(info), flush=True)
