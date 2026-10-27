# blender -b --python Tools/motionbricks/mb_export.py -- <Codex body .fbx> <soma.json> <export.json>
# Bakes the slim hunter's keyposed clips (recording their key times) and writes the MotionBricks ones for mb_generate.py.
import bpy, sys
from pathlib import Path
here = Path(__file__).resolve().parent
sys.path.insert(0, str(here)); sys.path.insert(0, str(here.parent / 'slim'))
import choreo, mb_bridge
mb_bridge.record_keys()
import moves

fbx, soma_json, out = sys.argv[sys.argv.index('--') + 1:][:3]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.render.fps = choreo.FPS
bpy.ops.import_scene.fbx(filepath=fbx)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE'); arm.name = 'Rig'
choreo.rename_bones(arm); choreo.add_hand_slots(arm)
rig = choreo.Rig(arm)
for fn in moves.CLIPS: fn(rig)
mb_bridge.export(rig, mb_bridge.Soma(soma_json), mb_bridge.MB_CLIPS, out)
print('MB_EXPORTED', out, flush=True)
