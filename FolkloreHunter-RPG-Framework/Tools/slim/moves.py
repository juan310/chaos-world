# The hunter's clips, authored on the slim skeleton (see choreo.py for the space and units). Positions are metres for
# the 1.83 m male: hand targets around his rest chest (chest head at z 1.446, shoulders at x +-0.175, z 1.60), feet as
# offsets of the ball of the foot from where it stands at rest. The character faces -Y; its right hand is at -X.
import math
from mathutils import Vector
import choreo
from choreo import V


def n(*v): return tuple(Vector(v).normalized())


def cut(blade, axis):
    """A blade direction and its leading edge for a swing turning about `axis` (right-hand rule)."""
    b = Vector(blade).normalized(); e = Vector(axis).cross(b)
    return tuple(b), tuple(e.normalized() if e.length > 1e-4 else b.orthogonal())


# ---- standing -----------------------------------------------------------------------------------------------
RELAX = {
    'hips': (0, 0, -.015), 'hipsR': (0, 0, 0), 'spine': (2, 0, 0), 'chest': (1, 0, 0), 'neck': (0, 0, 0), 'head': (-3, 0, 0),
    'shL': (0, 0, 0), 'shR': (0, 0, 0),
    'handL': (.215, -.02, 1.15), 'elbowL': (.4, .6, -1), 'bladeL': None, 'wristL': (-6, 0, 0),
    'handR': (-.215, -.02, 1.15), 'elbowR': (-.4, .6, -1), 'bladeR': None, 'wristR': (-6, 0, 0),
    'footL': (.0, 0, 0, 0, 4), 'footR': (.0, 0, 0, 0, -4), 'kneeL': (.15, -1, 0), 'kneeR': (-.15, -1, 0),
}


def stance(front=.16, back=.2, wide=.04, drop=.07, turn=0):
    """Feet planted for a fight: left foot forward, right back; hips dropped."""
    return {'hips': (0, back * .2, -drop), 'hipsR': (0, 0, turn),
            'footL': (wide, -front, 0, 0, 10), 'footR': (-wide, back, 0, 8, -25),
            'kneeL': (.3, -1, 0), 'kneeR': (-.4, -1, 0)}


# sword and shield: the sword hand low and forward, blade up and forward; the shield held up in front of the chest
SNS_READY = dict(stance(), **{
    'spine': (6, 0, 6), 'chest': (3, 0, 4), 'head': (-6, 0, -8),
    'handR': (-.27, -.24, 1.13), 'bladeR': n(0, -.55, .85), 'edgeR': n(0, -.85, -.55), 'elbowR': (-1, .5, -.4),
    'handL': (.16, -.3, 1.3), 'bladeL': (0, -1, 0), 'edgeL': (0, 0, 1), 'elbowL': (1, .2, -.6),
})


def idle_clip(rig, name='Idle'):
    keys = [(0, {}), (1.6, {'hips': (0, 0, -.022), 'chest': (2, 0, 0), 'handL': (.218, -.025, 1.145), 'handR': (-.218, -.025, 1.145)}), (3.2, {})]
    return choreo.bake(rig, name, keys, RELAX, loop=True, length=3.2)


def combat_idle_clip(rig, name='Idle_Combat'):
    keys = [(0, {}), (.9, {'hips': (0, .04, -.085), 'chest': (4, 0, 4)}), (1.8, {})]
    return choreo.bake(rig, name, keys, SNS_READY, loop=True, length=1.8)


# ---- gait cycles ----------------------------------------------------------------------------------------------
def smooth(x): x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)


def gait(rig, name, period, duty, stride, lift, kick, drop, bob, lean, hip_yaw, arm, elbow_up):
    """A walk/jog/sprint cycle in place: each foot is planted for `duty` of the cycle, sliding back `stride` metres
    (the ground passing under the body), then swings forward with its heel lifted `lift` and kicked back `kick`.
    Arms swing against the legs; the hips bob (`bob`, twice a cycle), turn with the stepping leg and the chest turns
    back against them."""
    def foot(phi):
        # phi 0: heel strike, front of the stride
        if phi < duty:
            u = phi / duty
            y = -stride / 2 + stride * u                  # moves back (+Y) while planted
            z = 0
            pitch = -14 * (1 - smooth(u / .18)) + 26 * smooth((u - .7) / .3)   # toe up at strike, heel up at push-off
            return (y, z, pitch)
        u = (phi - duty) / (1 - duty)
        y = stride / 2 - stride * smooth(u)
        z = lift * math.sin(math.pi * min(1, u * 1.1)) + kick * math.sin(math.pi * min(1, u * 1.6)) * (u < .62)
        pitch = 26 * (1 - smooth(u / .4)) - 14 * smooth((u - .7) / .3)
        return (y, z, pitch)

    def pose(t):
        phi = t / period
        P = dict(RELAX)
        yl, zl, pl = foot(phi % 1)
        yr, zr, pr = foot((phi + .5) % 1)
        P['footL'] = (.01, yl, zl, pl, 3); P['footR'] = (-.01, yr, zr, pr, -3)
        P['kneeL'] = (.1, -1, .2); P['kneeR'] = (-.1, -1, .2)
        c = math.cos(4 * math.pi * (phi - duty * .5))      # high at mid-stance (walk) / low (run: bob < 0)
        P['hips'] = (0, 0, -drop + bob * c)
        side = math.sin(2 * math.pi * (phi - duty * .5))   # + while the left foot carries the body
        P['hipsR'] = (lean * .3, 2.5 * side * (1 if bob > 0 else .5), hip_yaw * math.cos(2 * math.pi * phi))
        P['spine'] = (lean * .5, 0, -hip_yaw * .6 * math.cos(2 * math.pi * phi))
        P['chest'] = (lean * .3, 0, -hip_yaw * .6 * math.cos(2 * math.pi * phi))
        P['head'] = (-lean * .9, 0, hip_yaw * .2 * math.cos(2 * math.pi * phi))
        s = math.cos(2 * math.pi * phi)                   # left leg forward at phi 0: right arm forward
        for side_, sign in (('L', 1), ('R', -1)):
            a = s if side_ == 'L' else -s                 # + : this arm swings back
            P['hand' + side_] = (sign * (.2 - elbow_up * .03), -.02 + arm * a, 1.15 + elbow_up * (.16 + .08 * max(0, -a)))
            P['elbow' + side_] = (sign * .35, 1, -.6)
            P['wrist' + side_] = (-10 - 30 * elbow_up, 0, 0)
        return P
    return choreo.bake_fn(rig, name, pose, period)


def walk(rig, name='Gait_Walk'):
    return gait(rig, name, period=1.1, duty=.6, stride=.78, lift=.06, kick=.06, drop=.03, bob=.018, lean=3, hip_yaw=6, arm=.16, elbow_up=.1)


def jog(rig, name='Gait_Jog'):
    return gait(rig, name, period=.72, duty=.38, stride=.95, lift=.17, kick=.22, drop=.07, bob=-.035, lean=9, hip_yaw=8, arm=.2, elbow_up=1)


def sprint(rig, name='Gait_Sprint'):
    return gait(rig, name, period=.62, duty=.3, stride=1.05, lift=.26, kick=.36, drop=.09, bob=-.045, lean=16, hip_yaw=10, arm=.28, elbow_up=1.2)


# ---- sword and shield -----------------------------------------------------------------------------------------
def chop(rig, name='1H_Melee_Attack_Chop'):
    """Overhead cut: the sword goes up behind the head, the weight shifts onto the front foot, the blade comes down
    through the centre line and stops low in front."""
    up_b, up_e = cut((0, .45, .9), (1, 0, 0))
    hi_b, hi_e = cut((0, -.5, .87), (1, 0, 0))
    dn_b, dn_e = cut((0, -.6, -.8), (1, 0, 0))
    keys = [
        (0, {}),
        (.22, {'handR': (-.2, .05, 1.78), 'bladeR': up_b, 'edgeR': up_e, 'elbowR': (-1, .2, .6),
               'spine': (-4, 0, 14), 'chest': (-4, 0, 8), 'hips': (0, .06, -.06), 'head': (-2, 0, -14)}),
        (.34, {'handR': (-.12, -.3, 1.62), 'bladeR': hi_b, 'edgeR': hi_e, 'elbowR': (-1, .4, 0),
               'spine': (8, 0, 0), 'chest': (6, 0, -2), 'hips': (0, -.02, -.09), 'head': (-8, 0, -4)}),
        (.42, {'handR': (-.08, -.48, 1.1), 'bladeR': dn_b, 'edgeR': dn_e, 'elbowR': (-1, .6, -.5),
               'spine': (18, 0, -6), 'chest': (8, 0, -6), 'hips': (0, -.08, -.14), 'head': (-14, 0, 4),
               'footL': (.04, -.2, 0, 0, 10), 'footR': (-.04, .26, 0, 22, -25)}),
        (.62, {'handR': (-.1, -.42, .98), 'spine': (16, 0, -6), 'hips': (0, -.06, -.13)}),
        (.95, dict(SNS_READY)),
    ]
    return choreo.bake(rig, name, keys, SNS_READY)


def jump_dash(rig, side, name=None):
    """The drawn evade to the side: crouch, spring sideways off the trailing leg, the leading knee tucked high and the
    trailing leg flung out, the body leaning into the dash; a low landing on the leading foot."""
    s = 1 if side == 'L' else -1            # +X is the character's left
    lead, trail = ('L', 'R') if s > 0 else ('R', 'L')
    keys = [
        (0, {}),
        (.08, {'hips': (0, .02, -.16), 'spine': (14, -6 * s, 0), 'foot' + lead: (s * .05, -.1, 0, 0, 10 * s), 'foot' + trail: (-s * .06, .12, 0, 16, -10 * s)}),
        (.18, {'hips': (0, 0, .2), 'spine': (6, 14 * s, 0), 'chest': (2, 8 * s, 0), 'head': (-4, -12 * s, 0),
               'foot' + lead: (s * .22, -.08, .42, -10, 15 * s), 'knee' + lead: (s * .5, -1, .4),
               'foot' + trail: (-s * .3, .1, .16, 30, -20 * s), 'knee' + trail: (-s * .6, -1, 0)}),
        (.28, {'hips': (0, 0, .12), 'spine': (8, 10 * s, 0),
               'foot' + lead: (s * .16, -.06, .22, -4, 12 * s), 'foot' + trail: (-s * .24, .08, .2, 20, -15 * s)}),
        (.36, {'hips': (0, .03, -.2), 'spine': (18, 4 * s, 0), 'chest': (6, 2 * s, 0), 'head': (-10, -4 * s, 0),
               'foot' + lead: (s * .12, -.1, 0, 0, 12 * s), 'foot' + trail: (-s * .16, .2, 0, 18, -20 * s)}),
        (.52, dict(SNS_READY)),
    ]
    return choreo.bake(rig, name or 'Hunt_JumpDash_' + side, keys, SNS_READY)


TEST = [idle_clip, combat_idle_clip, walk, jog, sprint, chop, lambda r: jump_dash(r, 'L')]
