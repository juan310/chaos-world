# The motion library, Blender side (Studio 0.19).
#
# As a script: a pack's source file catalogued: every clip's length, frame rate, whether it loops, how fast it travels
# (in leg lengths a second, so it reads the same on any body), the bones it moves, and four moments of it pictured.
#   blender -b --python motionlib_blender.py -- catalog <source .fbx/.glb> <out folder>
#
# As a module (Tools/slim/build_slim.py --lib-clips): a pack loaded as a retarget source. Its clips are played onto
# another body by the same pose solver that brings motion capture onto the slim skeleton (Tools/slim/retarget.py): the
# torso takes the clip's turns, the hands its reach from the shoulder scaled by arm length, the feet their steps scaled
# by leg length, elbows and knees bending as in the clip. So a clip made on one body plays on another of different
# proportions or rest pose (T or A pose), and travel over the ground can be taken out for the game to move the body.
import bpy, json, math, os, re, sys
from pathlib import Path
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path: sys.path.insert(0, HERE)
import rigmap


def m2m_names(arm):
    """the retarget solver's bone names (Mesh2Motion's) -> this rig's, from the bone roles plus the first finger bones"""
    R = rigmap.blender_roles(arm)
    m = {'pelvis': 'hips', 'spine_01': 'spine', 'spine_03': 'chest', 'neck_01': 'neck', 'head': 'head'}
    out = {k: R[v] for k, v in m.items() if v in R}
    for s in ('l', 'r'):
        for a, b in (('clavicle', 'shoulder'), ('upperarm', 'upperarm'), ('lowerarm', 'lowerarm'), ('hand', 'hand'),
                     ('thigh', 'upperleg'), ('calf', 'lowerleg'), ('foot', 'foot'), ('ball', 'toes')):
            if b + '.' + s in R: out[a + '_' + s] = R[b + '.' + s]
        hand = R.get('hand.' + s)
        if hand:
            kids = [c.name for c in arm.data.bones[hand].children]
            for f in ('index', 'middle', 'pinky'):
                k = [c for c in kids if re.search(f if f != 'pinky' else 'pinky|little', c, re.I)]
                if k: out['%s_01_%s' % (f, s)] = sorted(k, key=len)[0]
        if 'clavicle_' + s not in out and 'upperarm_' + s in out:
            out['clavicle_' + s] = out['upperarm_' + s]
    if 'spine_01' not in out: out['spine_01'] = out.get('pelvis')
    if 'neck_01' not in out: out['neck_01'] = out.get('head')
    return out


def clean(name):
    return re.sub(r'\.\d+$', '', name.split('|')[-1]).strip()


def _import(path):
    before_o, before_a = set(bpy.data.objects), set(bpy.data.actions)
    if str(path).lower().endswith(('.glb', '.gltf')): bpy.ops.import_scene.gltf(filepath=str(path))
    else: bpy.ops.import_scene.fbx(filepath=str(path))
    new_o = [o for o in bpy.data.objects if o not in before_o]
    arm = next(o for o in new_o if o.type == 'ARMATURE')
    fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
    return arm, new_o, [a for a in bpy.data.actions if a not in before_a], fps


def source(pack_dir):
    """a pack as a retarget.Source (Tools/slim/retarget.py must be importable): .clips[name] = (rig, action), .fps,
    .manifest; play with src.clip(rig, game_name, clip, loop=..., pin=..., speed=src.fps / 30)"""
    import retarget, choreo
    pack_dir = Path(pack_dir)
    man = json.loads((pack_dir / 'manifest.json').read_text())
    src = retarget.Source(None, {})
    arm, new_o, acts, fps = _import(pack_dir / man['file'])
    bpy.context.scene.render.fps = choreo.FPS; bpy.context.scene.render.fps_base = 1
    for o in new_o:
        if o.type not in ('ARMATURE',): bpy.data.objects.remove(o)
    src.names = m2m_names(arm)
    for a in acts:
        nm = clean(a.name); a.name = 'm2m:lib:%s:%s' % (man['name'], nm)
        src.clips[nm] = (arm, a)
    src.prepare(arm)
    src.fps = fps; src.manifest = man
    return src


# ---------------------------------------------------------------------------------------------------- catalogue
def catalog(path, out):
    out = Path(out); (out / 'previews').mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    arm, new_o, acts, fps = _import(path)
    meshes = [o for o in new_o if o.type == 'MESH']
    names = m2m_names(arm)
    M = arm.matrix_world
    if arm.animation_data: arm.animation_data.action = None
    bpy.context.view_layer.update()
    P0 = {b.name: M @ b.head_local for b in arm.data.bones}
    leg = (P0[names['thigh_l']] - P0[names['calf_l']]).length + (P0[names['calf_l']] - P0[names['foot_l']]).length
    sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'; sc.render.resolution_x, sc.render.resolution_y = 300, 400
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'; sc.display.shading.show_cavity = True
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam; cam.data.type = 'ORTHO'
    height = max((M @ v.co).z for m in meshes for v in m.data.vertices) if meshes else leg * 2.2
    clips = []
    ad = arm.animation_data or arm.animation_data_create()
    for a in acts:
        ad.action = a
        try:
            if a.slots: ad.action_slot = a.slots[0]
        except AttributeError: pass
        f0, f1 = a.frame_range
        def pose_at(f):
            sc.frame_set(int(f), subframe=f - int(f))
            return {pb.name: (M @ pb.matrix) for pb in arm.pose.bones}
        A, B = pose_at(f0), pose_at(f1)
        hipsA, hipsB = A[names['pelvis']].translation, B[names['pelvis']].translation
        secs = (f1 - f0) / fps
        travel = Vector((hipsB.x - hipsA.x, hipsB.y - hipsA.y, 0))
        # loops when the last pose is the first again (once the travel is taken out): the largest bone turn between them
        turn = max(math.degrees((A[n].to_quaternion().rotation_difference(B[n].to_quaternion())).angle) for n in names.values() if n in A)
        moving = 0
        mid = pose_at((f0 + f1) / 2)
        for n in A:
            if math.degrees(A[n].to_quaternion().rotation_difference(mid[n].to_quaternion()).angle) > 2: moving += 1
        pics = []
        for k in range(4):
            f = f0 + (f1 - f0) * k / 3; sc.frame_set(int(f), subframe=f - int(f))
            c = M @ arm.pose.bones[names['pelvis']].head
            cam.data.ortho_scale = height * 1.5
            cam.location = Vector((c.x, c.y, height * .5)) + Vector((-.6, -1, .12)).normalized() * height * 4
            cam.rotation_euler = (Vector((c.x, c.y, height * .5)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
            fn = '%s_%d.png' % (re.sub(r'[^A-Za-z0-9]+', '_', clean(a.name)), k)
            sc.render.filepath = str(out / 'previews' / fn); bpy.ops.render.render(write_still=True); pics.append(fn)
        clips.append({'name': clean(a.name), 'source': a.name, 'frames': [f0, f1], 'fps': fps, 'seconds': round(secs, 3),
                      'loops': turn < 12, 'endTurn': round(turn, 1), 'bonesMoving': moving,
                      'travel': round(travel.length, 3), 'speedLegs': round(travel.length / leg / secs, 3) if secs > 0 else 0,
                      'direction': [round(x, 3) for x in (travel.normalized() if travel.length > 1e-4 else travel)], 'previews': pics})
    rep = {'rig': {'bones': len(arm.data.bones), 'names': names, 'legLength': round(leg, 4), 'height': round(height, 4),
                   'fingers': all(k in names for k in ('index_01_l', 'middle_01_l', 'pinky_01_l', 'index_01_r', 'middle_01_r', 'pinky_01_r'))},
           'fps': fps, 'clips': clips}
    (out / 'clips.json').write_text(json.dumps(rep, indent=1))
    print('MOTIONLIB_CATALOG', len(clips), 'clips', flush=True)


if __name__ == '__main__' and '--' in sys.argv:
    argv = sys.argv[sys.argv.index('--') + 1:]
    if argv[0] == 'catalog': catalog(argv[1], argv[2])
