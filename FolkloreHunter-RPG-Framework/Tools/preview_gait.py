# Preview of the gait cycles (Hunt_Walk, Hunt_Jog, Hunt_Sprint) without touching any game file: opens Hunter.blend,
# builds the cycles with hunter_weapons.gait_clip / locomotion_clips, renders eight side-view frames of each and a strip,
# and quits without saving.   blender -b SourceArt/Hunter.blend --python Tools/preview_gait.py
import bpy, math, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import hunter_weapons

r = Path.home() / 'Documents/FolkloreHunter-RPG-Framework'
out = r / 'Captures/GaitPreview'; out.mkdir(parents=True, exist_ok=True)
rig = bpy.data.objects['Rig']
for n in ('Hunt_Walk', 'Hunt_Jog', 'Hunt_Sprint'):
    old = bpy.data.actions.get(n)
    if old: bpy.data.actions.remove(old)
clips = hunter_weapons.locomotion_clips(rig)
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x = 300; scene.render.resolution_y = 360; scene.render.film_transparent = True
cd = bpy.data.cameras.new('GaitCam'); cd.type = 'ORTHO'; cd.ortho_scale = 3.4
cam = bpy.data.objects.new('GaitCam', cd); scene.collection.objects.link(cam); scene.camera = cam
cam.location = (6, 0, 1.1); cam.rotation_euler = (math.radians(90), 0, math.radians(90))
for o in bpy.data.objects:
    if o.name.startswith('Hunt_') or o.name in ('Round_Shield',): o.hide_render = True
paths = []
for act in clips:
    rig.animation_data_create(); rig.animation_data.action = act
    try:
        if act.slots: rig.animation_data.action_slot = act.slots[0]
    except AttributeError:
        pass
    f0, f1 = act.frame_range
    for i in range(8):
        scene.frame_set(int(round(f0 + (f1 - f0) * i / 8)))
        p = out / ('%s_%d.png' % (act.name, i)); scene.render.filepath = str(p)
        bpy.ops.render.render(write_still=True); paths.append(str(p))
print('GAIT_PREVIEW', len(paths), flush=True)
