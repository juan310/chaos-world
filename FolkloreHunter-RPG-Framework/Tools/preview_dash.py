# Preview of the jump dash (Hunt_JumpDash_L/R) without touching any game file: opens Hunter.blend, builds the battle
# clips with hunter_weapons.battle_clips, renders eight front-view frames of each dash and quits without saving.
#   blender -b SourceArt/Hunter.blend --python Tools/preview_dash.py
import bpy, math, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import hunter_weapons

r = Path.home() / 'Documents/FolkloreHunter-RPG-Framework'
out = r / 'Captures/DashPreview'; out.mkdir(parents=True, exist_ok=True)
rig = bpy.data.objects['Rig']
for n in ('Hunt_JumpDash_L', 'Hunt_JumpDash_R'):
    old = bpy.data.actions.get(n)
    if old: bpy.data.actions.remove(old)
clips = [c for c in hunter_weapons.battle_clips(rig) if c.name.startswith('Hunt_JumpDash_')]
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x = 300; scene.render.resolution_y = 360; scene.render.film_transparent = True
cd = bpy.data.cameras.new('DashCam'); cd.type = 'ORTHO'; cd.ortho_scale = 3.4
cam = bpy.data.objects.new('DashCam', cd); scene.collection.objects.link(cam); scene.camera = cam
cam.location = (0, -6, 1.1); cam.rotation_euler = (math.radians(90), 0, 0)        # in front of the hunter (it faces -Y)
for o in bpy.data.objects:
    if o.name.startswith('Hunt_'): o.hide_render = True
paths = []
for act in clips:
    rig.animation_data_create(); rig.animation_data.action = act
    try:
        if act.slots: rig.animation_data.action_slot = act.slots[0]
    except AttributeError:
        pass
    f0, f1 = act.frame_range
    for i in range(8):
        scene.frame_set(int(round(f0 + (f1 - f0) * i / 7)))
        p = out / ('%s_%d.png' % (act.name, i)); scene.render.filepath = str(p)
        bpy.ops.render.render(write_still=True); paths.append(str(p))
print('DASH_PREVIEW', len(paths), flush=True)
