# Motion capture onto the slim skeleton: the Mesh2Motion human animation library (CC0, realistic proportions:
# human-base / human-addon / human-mocap .glb, kept outside the project at C:\AW\m2m) played by the slim hunters and
# skeletons. Each frame is turned into a pose for choreo's solver: the torso, clavicles and feet take the capture's
# turns from rest; the hips and ankles its movement, scaled by leg length; the wrists its reach from the shoulder,
# scaled by arm length, elbows bending as captured; the hands its grip (the thumb side as the blade, the knuckles as
# the edge, from the finger bones), so a weapon in the hand slot sits as the captured hand would hold it.
import bpy
from mathutils import Vector, Quaternion
import choreo

M2M_FILES = ['human-base-animations.glb', 'human-addon-animations.glb', 'human-mocap-animations.glb']


class Source:
    """Captured motion on another human rig. The bone names are the Mesh2Motion ones; `names` maps them to another
    rig's (MotionBricks' SOMA skeleton, see Tools/motionbricks/mb_bridge.py)."""
    def __init__(self, folder=None, names=None):
        self.clips = {}
        self.names = names or {}
        bpy.context.scene.render.fps = choreo.FPS
        for f in (M2M_FILES if folder else []):
            p = folder + '/' + f
            before_o, before_a = set(bpy.data.objects), set(bpy.data.actions)
            bpy.ops.import_scene.gltf(filepath=p)
            new = [o for o in bpy.data.objects if o not in before_o]
            arm = next(o for o in new if o.type == 'ARMATURE')
            for o in new:
                if o.type != 'ARMATURE' and o.type != 'EMPTY': bpy.data.objects.remove(o)
            for a in [a for a in bpy.data.actions if a not in before_a]:
                nm = a.name
                a.name = 'm2m:' + nm
                self.clips.setdefault(nm, (arm, a))
            self.prepare(arm)
        print('M2M_CLIPS', len(self.clips), flush=True)

    def prepare(self, arm):
        if arm.animation_data: arm.animation_data.action = None
        bpy.context.view_layer.update()
        M = arm.matrix_world
        rest = {b.name: M @ b.matrix_local for b in arm.data.bones}
        # facing: the toes point forward; the slim skeleton faces -Y
        N = lambda x: self.names.get(x, x)
        f = rest[N('ball_l')].translation - rest[N('foot_l')].translation
        flip = Quaternion((0, 0, 1), 3.14159265) if f.y > 0 else Quaternion()
        L = lambda a, b: (rest[N(b)].translation - rest[N(a)].translation).length
        arm['m2m_leg'] = L('thigh_l', 'calf_l') + L('calf_l', 'foot_l')
        arm['m2m_arm'] = L('upperarm_l', 'lowerarm_l') + L('lowerarm_l', 'hand_l')
        self.__dict__.setdefault('rest', {})[arm.name] = rest
        self.__dict__.setdefault('flip', {})[arm.name] = flip

    def sample(self, arm, act, frame):
        ad = arm.animation_data or arm.animation_data_create()
        if ad.action != act:
            ad.action = act
            try:
                if act.slots: ad.action_slot = act.slots[0]
            except AttributeError: pass
        bpy.context.scene.frame_set(int(frame), subframe=frame - int(frame))
        M = arm.matrix_world
        return {b.name: M @ b.matrix for b in arm.pose.bones}

    def pose(self, rig, arm, Wp, pin=False):
        rest, flip = self.rest[arm.name], self.flip[arm.name]
        fi = flip.inverted()
        N = lambda x: self.names.get(x, x)
        def dq(n): return flip @ (Wp[N(n)].to_quaternion() @ rest[N(n)].to_quaternion().inverted()) @ fi
        def pos(n): return flip @ Wp[N(n)].translation
        def rpos(n): return flip @ rest[N(n)].translation
        sl = rig.leg_len / arm['m2m_leg']; sa = rig.arm_len / arm['m2m_arm']
        # pinned: the capture's travel over the ground is taken out (the game moves the hunter)
        drift = Vector((*((pos('pelvis') - rpos('pelvis')).xy), 0)) * sl if pin else Vector()
        P = {'qhips': dq('pelvis'), 'hipsAt': rig.h0['hips'] + (pos('pelvis') - rpos('pelvis')) * sl - drift,
             'qspine': dq('spine_01'), 'qchest': dq('spine_03'), 'qneck': dq('neck_01'), 'qhead': dq('head'),
             'qshL': dq('clavicle_l'), 'qshR': dq('clavicle_r')}
        for s, S in (('l', 'L'), ('r', 'R')):
            sh, el, wr = pos('upperarm_' + s), pos('lowerarm_' + s), pos('hand_' + s)
            fingers = pos('middle_01_' + s) - wr
            thumbside = pos('index_01_' + s) - pos('pinky_01_' + s)
            P.update({'hand' + S: tuple((wr - sh) * sa), 'rel' + S: True, 'atWrist' + S: True, 'world' + S: True,
                      'blade' + S: tuple(thumbside.normalized()), 'edge' + S: tuple(fingers.normalized()),
                      'elbow' + S: tuple(el - (sh + wr) / 2)})
            hip, kn, an = pos('thigh_' + s), pos('calf_' + s), pos('foot_' + s)
            P.update({'ankle' + S: tuple(rig.h0['foot.' + s] + (an - rpos('foot_' + s)) * sl - drift),
                      'qfoot' + S: dq('foot_' + s), 'qtoes' + S: dq('ball_' + s), 'knee' + S: tuple(kn - (hip + an) / 2)})
        return P

    def clip(self, rig, name, src, loop=False, start=0.0, end=None, speed=1.0, then=None, pin=False):
        """A game clip from one capture (or two, `then` played after it: an attack and its recovery)."""
        parts = [src] + ([then] if then else [])
        segs = []
        for s in parts:
            if s not in self.clips: raise KeyError('no capture ' + s)
            arm, act = self.clips[s]
            f0, f1 = act.frame_range
            a = f0 + start * choreo.FPS if s == src else f0
            b = (f0 + end * choreo.FPS) if (end is not None and s == src) else f1
            segs.append((arm, act, a, b))
        total = sum((b - a) for _, _, a, b in segs) / choreo.FPS / speed

        def fn(t):
            fr = t * choreo.FPS * speed
            for arm, act, a, b in segs:
                if fr <= (b - a) or (arm, act, a, b) == segs[-1]:
                    return self.pose(rig, arm, self.sample(arm, act, a + min(fr, b - a)), pin)
                fr -= (b - a)
        act = choreo.bake_fn(rig, name, fn, total, loop=loop)
        rig.arm.animation_data.action = None
        return act

    def remove(self):
        """The captures and their rigs go before the export (only the slim clips ship)."""
        arms = set(arm for arm, _ in self.clips.values())
        for _, a in self.clips.values():
            if a.name in bpy.data.actions: bpy.data.actions.remove(a)
        for a in list(bpy.data.actions):
            if a.name.startswith('m2m:'): bpy.data.actions.remove(a)
        for o in list(bpy.data.objects):
            if o in arms or (o.type == 'EMPTY'): bpy.data.objects.remove(o)


# game clip <- capture (loop, options)
MOCAP = [
    ('Idle', 'Idle_A', True, {}), ('Idle_Combat', 'Idle_Sword', True, {}), ('Idle_B', 'Idle_Sword', True, {}),
    ('Gait_Walk', 'Walk', True, {}), ('Walking_A', 'Walk', True, {}),
    ('Gait_Jog', 'Jog', True, {}), ('Running_A', 'Jog', True, {}), ('Hunt_Jog', 'Jog', True, {}),
    ('Gait_Sprint', 'Sprint', True, {}), ('Hunt_Sprint', 'Sprint', True, {}),
    ('Walking_Backwards', 'Walk_Backwards', True, {}), ('Running_Strafe_Left', 'Strafe_left', True, {}), ('Running_Strafe_Right', 'Strafe_right', True, {}),
    ('Dodge_Forward', 'Roll', False, {'pin': True}), ('Dodge_Backward', 'Dodge_back', False, {'pin': True}),
    ('Hit_A', 'Hit_Chest', False, {'pin': True}), ('Hit_B', 'Hit_Head', False, {'pin': True}), ('Death_A', 'Death_A', False, {}),
    ('Lie_StandUp', 'LayToIdle', False, {}), ('Use_Item', 'Consume', False, {}), ('Interact', 'Interact', False, {}),
    ('Spellcast_Shoot', 'Spell_Simple_Shoot', False, {}), ('Sit_Chair_Idle', 'Sitting_Idle', True, {}),
    # sword and shield: the basic cuts, the leaping spin, the guard and the bash
    ('1H_Melee_Attack_Chop', 'Sword_Regular_B', False, {'then': 'Sword_Regular_B_Rec', 'pin': True}),
    ('1H_Melee_Attack_Slice_Diagonal', 'Sword_Regular_A', False, {'then': 'Sword_Regular_A_Rec', 'pin': True}),
    ('1H_Melee_Attack_Slice_Horizontal', 'Sword_Attack', False, {'pin': True}),
    ('SnS_Rush_3', 'Sword_Regular_C', False, {'pin': True}),
    ('Blocking', 'Idle_Shield', True, {}), ('Block', 'Idle_Shield', True, {}), ('Block_Attack', 'Shield_OneShot', False, {'pin': True}),
    # bow: the draw (held at its end) and the release
    ('Bow_Draw', 'Bow Pull Hold', True, {}), ('Bow_Shoot', 'Bow Release', False, {}),
]

# townspeople: the everyday moves only
VILLAGER = ['Idle', 'Walking_A', 'Interact', 'Use_Item', 'Sit_Chair_Idle']

# the skeletons: the same, but an undead shamble for their walk
MONSTER_MOCAP = [t for t in MOCAP if t[0] not in ('Walking_A',)] + [('Walking_A', 'Zombie_Walk', True, {})]


def apply(rig, src, table=MOCAP, only=None):
    """Bakes the table's clips (only those named, if given; a name not in the table is taken as a capture's own
    name, for review)."""
    made = []
    if only:
        known = set(t[0] for t in table)
        table = list(table) + [(n.replace(' ', '_'), n, False, {}) for n in only if n not in known]
        only = set(only) | set(n.replace(' ', '_') for n in only)
    for name, cap, loop, opt in table:
        if only and name not in only: continue
        if cap not in src.clips: print('M2M_MISSING', cap, flush=True); continue
        old = bpy.data.actions.get(name)
        if old: bpy.data.actions.remove(old)
        made.append(src.clip(rig, name, cap, loop=loop, **opt))
        print('M2M_CLIP', name, '<-', cap, flush=True)
    return made
