# Blender (headless) export of additional KayKit Adventurers bodies for NPCs, mirroring the sample's
# export_3d_characters.py. FBX_SCALE_ALL is required: reverting it caused a 100x bind/animation mismatch.
import bpy, json
from pathlib import Path
r = Path.home() / 'Documents/FolkloreHunter-RPG-Framework'
source = r / 'SourceArt'
dest = r / 'Assets/Resources/Models'
wanted = ['Idle', 'Walking_A', 'Running_A', 'Interact', 'Hit_A', 'Death_A', 'Spellcast_Shoot', 'Blocking', '1H_Melee_Attack_Chop']
result = []
for srcname, outname, prefix in [('Barbarian.glb', 'Smith', 'Barbarian_'), ('Rogue_Hooded.glb', 'Noble', 'Rogue_')]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    src = next(source.rglob(srcname))
    bpy.ops.import_scene.gltf(filepath=str(src))
    for action in list(bpy.data.actions):
        if not any(action.name == n or action.name.startswith(n + '_') or action.name.startswith(n + '.') for n in wanted):
            bpy.data.actions.remove(action)
    for obj in list(bpy.data.objects):
        if obj.type == 'MESH' and not obj.name.startswith(prefix):
            bpy.data.objects.remove(obj, do_unlink=True)   # NPCs carry no weapons
    for obj in bpy.data.objects:
        obj.hide_render = False; obj.hide_viewport = False
    for im in bpy.data.images:
        if im.name in ['Render Result', 'Viewer Node']:
            continue
        try:
            im.filepath_raw = str(dest / (outname + '_texture.png')); im.file_format = 'PNG'; im.save()
        except Exception:
            pass
    bpy.ops.export_scene.fbx(filepath=str(dest / (outname + '.fbx')), use_selection=False, object_types={'ARMATURE', 'MESH', 'EMPTY'},
                             add_leaf_bones=False, apply_scale_options='FBX_SCALE_ALL', bake_anim=True, bake_anim_use_nla_strips=False,
                             bake_anim_use_all_actions=True, bake_anim_simplify_factor=0, axis_forward='-Z', axis_up='Y', path_mode='COPY', embed_textures=False)
    bpy.ops.wm.save_as_mainfile(filepath=str(source / (outname + '.blend')))
    result.append({'source': str(src), 'output': outname + '.fbx', 'license': 'CC0 (KayKit Character Pack: Adventures)',
                   'actions': [a.name for a in bpy.data.actions], 'meshes': [o.name for o in bpy.data.objects if o.type == 'MESH']})
    print('EXPORTED', json.dumps(result[-1]), flush=True)
(r / 'Docs/Assets/npc-character-export.json').write_text(json.dumps(result, indent=2))
