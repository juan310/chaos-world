# The hunter's clips, authored on the slim skeleton (see choreo.py for the space and units). Positions are metres for
# the 1.83 m male: hand targets around his rest chest (chest head at z 1.446, shoulders at x +-0.175, z 1.60), feet as
# offsets of the ball of the foot from where it stands at rest (lift = height above the ground). The character faces
# -Y; its right hand is at -X. A weapon's direction from the grip is its "blade", the side leading a cut its "edge"
# (shield: the face's outward normal and its top; bow: the upper limb and the side toward the target).
import math
from mathutils import Vector, Quaternion
import choreo


def n(*v): return tuple(Vector(v).normalized())


def cut(blade, axis):
    """A blade direction and its leading edge for a swing turning about `axis` (right-hand rule)."""
    b = Vector(blade).normalized(); e = Vector(axis).cross(b)
    return tuple(b), tuple(e.normalized() if e.length > 1e-4 else b.orthogonal())


def arc(b0, b1, t):
    """The blade t of the way from b0 to b1 (turning the short way) and its edge along that turn."""
    a, b = Vector(b0).normalized(), Vector(b1).normalized()
    axis = a.cross(b)
    if axis.length < 1e-4: axis = a.orthogonal()
    q = a.rotation_difference(b)
    ang = q.angle * t
    d = Quaternion(axis.normalized(), ang) @ a
    return cut(d, axis)


def sword(hand, blade, edge=None, axis=None, elbow=(-1, .5, -.4)):
    if edge is None: blade, edge = cut(blade, axis or (1, 0, 0))
    return {'handR': hand, 'bladeR': n(*blade), 'edgeR': n(*edge), 'elbowR': elbow}


def shield(hand=(.15, -.3, 1.38), face=(0, -1, 0), up=(0, 0, 1), elbow=(1, .2, -.6)):
    return {'handL': hand, 'bladeL': n(*face), 'edgeL': n(*up), 'elbowL': elbow}


def merge(*ds):
    out = {}
    for d in ds: out.update(d)
    return out


# ---- standing -----------------------------------------------------------------------------------------------
RELAX = {
    'hips': (0, 0, -.015), 'hipsR': (0, 0, 0), 'spine': (2, 0, 0), 'chest': (1, 0, 0), 'neck': (0, 0, 0), 'head': (-3, 0, 0),
    'shL': (0, 0, 0), 'shR': (0, 0, 0), 'feetHips': False, 'gripL': -.12,
    'handL': (.215, -.02, 1.15), 'elbowL': (.4, .6, -1), 'bladeL': None, 'edgeL': (0, 0, 1), 'wristL': (-6, 0, 0), 'worldL': False,
    'handR': (-.215, -.02, 1.15), 'elbowR': (-.4, .6, -1), 'bladeR': None, 'edgeR': (0, 0, 1), 'wristR': (-6, 0, 0), 'worldR': False,
    'footL': (0, 0, 0, 0, 4), 'footR': (0, 0, 0, 0, -4), 'kneeL': (.15, -1, 0), 'kneeR': (-.15, -1, 0),
}


def stance(front=.16, back=.2, wide=.04, drop=.07, turn=0):
    """Feet planted for a fight: left foot forward, right back; hips dropped."""
    return {'hips': (0, back * .2, -drop), 'hipsR': (0, 0, turn),
            'footL': (wide, -front, 0, 0, 10), 'footR': (-wide, back, 0, 8, -25),
            'kneeL': (.3, -1, 0), 'kneeR': (-.4, -1, 0)}


GUARD = shield()
SNS_READY = merge(RELAX, stance(), {'spine': (6, 0, 6), 'chest': (3, 0, 4), 'head': (-6, 0, -8)},
                  sword((-.27, -.24, 1.13), (0, -.55, .85), (0, -.85, -.55)), GUARD)
KATANA_READY = merge(RELAX, stance(.2, .22, .05, .1), {'spine': (6, 0, 0), 'chest': (2, 0, 0), 'head': (-6, 0, 0)},
                     sword((-.06, -.36, 1.2), (.05, -.75, .66), (0, -.66, -.75), elbow=(-1, .3, -.6)),
                     {'handL': 'twohand', 'gripL': -.13, 'elbowL': (1, .3, -.6)})
LANCE_READY = merge(RELAX, stance(.18, .26, .06, .1), {'spine': (8, 0, 4), 'chest': (2, 0, 2), 'head': (-8, 0, -4)},
                    sword((-.22, -.12, 1.2), (.04, -1, .08), (0, 0, 1), elbow=(-1, .8, 0)), shield((.14, -.3, 1.3), elbow=(1, .2, -.7)))
BOW_READY = merge(RELAX, stance(.1, .1, .14, .05), {'spine': (4, 0, 0), 'chest': (2, 0, 0)},
                  {'handL': (.2, -.12, 1.15), 'bladeL': n(0, -.35, .94), 'edgeL': n(0, -.94, -.35), 'elbowL': (1, .4, -.6)})


def loop2(rig, name, base, a, period):
    return choreo.bake(rig, name, [(0, {}), (period / 2, a), (period, {})], base, loop=True, length=period)


def idle(rig, name='Idle'):
    return loop2(rig, name, RELAX, {'hips': (0, 0, -.022), 'chest': (2.5, 0, 0), 'handL': (.218, -.025, 1.145), 'handR': (-.218, -.025, 1.145)}, 3.2)


def idle_combat(rig, name='Idle_Combat'):
    return loop2(rig, name, SNS_READY, {'hips': (0, .045, -.085), 'chest': (4, 0, 4), 'handL': (.15, -.3, 1.36)}, 1.8)


def katana_idle(rig, name='2H_Melee_Idle'):
    return loop2(rig, name, KATANA_READY, {'hips': (0, .045, -.115), 'chest': (3, 0, 0)}, 1.8)


def blocking(rig, name='Blocking'):
    up = merge(SNS_READY, stance(.2, .26, .06, .12), {'spine': (10, 0, 10), 'chest': (4, 0, 6), 'head': (-12, 0, -10)},
               shield((.06, -.36, 1.42), face=n(-.15, -1, .05)), sword((-.3, -.12, 1.15), (0, -.45, .9), (0, -.9, -.45)))
    return loop2(rig, name, up, {'hips': (0, .27 * .2 + .01, -.13), 'chest': (5, 0, 6)}, 1.2)


# ---- gait cycles ----------------------------------------------------------------------------------------------
def smooth(x): x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)


GAITS = {}      # every gait cycle baked: its period, duty, stride and direction (MotionBricks' locomotion needs them)


def gait(rig, name, period, duty, stride, lift, kick, drop, bob, lean, hip_yaw, arm, elbow_up, direction=(0, 1), upper=None,
         hands_low=0.0, hands_fwd=0.0, knees_out=0.1, hips_back=0.0, hands_at=None):
    """A walk/jog/sprint cycle in place: each foot is planted for `duty` of the cycle, sliding `stride` metres the way
    the ground passes under the body (direction (0, 1) is walking forward: the feet slide back, +Y), then swings
    back to the front with its heel lifted `lift` and kicked back `kick`. Arms swing against the legs; the hips bob
    (`bob`, twice a cycle: + highest at mid-stance as in a walk, - lowest as in a run), turn with the stepping leg and
    the chest turns back against them. `upper` replaces the arms and chest (a weapon held still). A crouched gait
    (sneaking) lowers the hands with the body (`hands_low`), carries them forward (`hands_fwd`) and the knees wider, and
    sits the hips back behind the feet (`hips_back`) so the forward-tipped trunk balances over them. `hands_at`
    (forward, height) places the hands in the body's own space instead of the chest's: a trunk tipped far forward
    would otherwise swing them back behind the hips."""
    dx, dy = direction
    GAITS[name] = {'period': period, 'duty': duty, 'stride': stride, 'direction': direction}

    def foot(phi):
        if phi < duty:
            u = phi / duty
            d = -stride / 2 + stride * u
            pitch = -14 * (1 - smooth(u / .18)) + 26 * smooth((u - .7) / .3)
            return d, 0, pitch
        u = (phi - duty) / (1 - duty)
        d = stride / 2 - stride * smooth(u)
        z = lift * math.sin(math.pi * min(1, u * 1.1)) + kick * math.sin(math.pi * min(1, u * 1.6)) * (u < .62)
        pitch = 26 * (1 - smooth(u / .4)) - 14 * smooth((u - .7) / .3)
        return d, z, pitch

    def pose(t):
        phi = t / period
        P = dict(RELAX)
        dl, zl, pl = foot(phi % 1)
        dr, zr, pr = foot((phi + .5) % 1)
        side = abs(dx) > abs(dy)
        if side: pl, pr = pl * .3, pr * .3      # sideways: little heel-toe roll
        P['footL'] = (.01 + dl * dx, dl * dy, zl, pl * (1 if dy >= 0 else -.5), 3)
        P['footR'] = (-.01 + dr * dx, dr * dy, zr, pr * (1 if dy >= 0 else -.5), -3)
        P['kneeL'] = (knees_out, -1, .2); P['kneeR'] = (-knees_out, -1, .2)
        c = math.cos(4 * math.pi * (phi - duty * .5))
        P['hips'] = (0, hips_back, -drop + bob * c)
        sway = math.sin(2 * math.pi * (phi - duty * .5))
        cy = math.cos(2 * math.pi * phi)
        P['hipsR'] = (lean * .3 * dy, 2.5 * sway * (1 if bob > 0 else .5), hip_yaw * cy * (1 if not side else .3))
        P['spine'] = (lean * .5 * dy, 0, -hip_yaw * .6 * cy)
        P['chest'] = (lean * .3 * dy, 0, -hip_yaw * .6 * cy)
        P['head'] = (-lean * .9 * dy, 0, hip_yaw * .2 * cy)
        for s_, sign in (('L', 1), ('R', -1)):
            a = cy if s_ == 'L' else -cy             # + : this arm swings back
            if hands_at:
                P['hand' + s_] = (sign * .21, -hands_at[0] + arm * a, hands_at[1] + .05 * max(0, -a))
                P['world' + s_] = True; P['elbow' + s_] = (sign * .4, .5, -.6)
            else:
                P['hand' + s_] = (sign * (.2 - elbow_up * .03), -.02 - hands_fwd + arm * a, 1.15 - hands_low + elbow_up * (.16 + .08 * max(0, -a)))
                P['elbow' + s_] = (sign * .35, 1, -.6)
            P['wrist' + s_] = (-10 - 30 * elbow_up, 0, 0)
        if upper:
            u = upper(t) if callable(upper) else upper
            P.update(u)
        return P
    return choreo.bake_fn(rig, name, pose, period)


def walk(rig, name='Gait_Walk'):
    return gait(rig, name, period=1.0, duty=.6, stride=.85, lift=.06, kick=.06, drop=.03, bob=.018, lean=3, hip_yaw=6, arm=.16, elbow_up=.1)


def jog(rig, name='Gait_Jog'):
    return gait(rig, name, period=.72, duty=.38, stride=.99, lift=.17, kick=.22, drop=.07, bob=-.035, lean=9, hip_yaw=8, arm=.2, elbow_up=1)


def sprint(rig, name='Gait_Sprint'):
    return gait(rig, name, period=.62, duty=.3, stride=.97, lift=.26, kick=.36, drop=.09, bob=-.045, lean=16, hip_yaw=10, arm=.28, elbow_up=1.2)


def walk_back(rig, name='Walking_Backwards'):
    return gait(rig, name, period=1.0, duty=.62, stride=.6, lift=.07, kick=0, drop=.05, bob=.012, lean=-3, hip_yaw=4, arm=.08, elbow_up=.2, direction=(0, -1))


def sneak(rig, name='Sneak_Walk'):
    """creeping: half crouched with the hips sat back and the trunk tipped in over the knees (about 25 degrees: the lean is shared by hips, spine and chest), long slow steps rolled heel
    to toe, hands carried low and forward"""
    return gait(rig, name, period=1.3, duty=.68, stride=.62, lift=.09, kick=.03, drop=.2, bob=.012, lean=44, hip_yaw=4,
                arm=.07, elbow_up=.45, knees_out=.14, hips_back=.09, hands_at=(.2, .74))


def crouch_walk(rig, name='Crouch_Walk'):
    """low to the ground: deep knees, short steps, the hips sat back and the trunk tipped well forward over the knees (about 37 degrees)"""
    return gait(rig, name, period=1.15, duty=.66, stride=.46, lift=.08, kick=.02, drop=.34, bob=.01, lean=60, hip_yaw=3,
                arm=.05, elbow_up=.6, knees_out=.18, hips_back=.14, hands_at=(.3, .6))


CROUCH = merge(RELAX, {'hips': (0, .14, -.34), 'hipsR': (18, 0, 0), 'spine': (30, 0, 0), 'chest': (18, 0, 0), 'head': (-54, 0, 0),
                       'footL': (.06, -.04, 0, 0, 10), 'footR': (-.06, .08, 0, 0, -10), 'kneeL': (.2, -1, 0), 'kneeR': (-.2, -1, 0),
                       'handL': (.21, -.3, .6), 'elbowL': (.4, .5, -.6), 'handR': (-.21, -.3, .6), 'elbowR': (-.4, .5, -.6),
                       'worldL': True, 'worldR': True})      # hands in the body's space, ahead of the knees
SNEAK = merge(CROUCH, {'hips': (0, .09, -.2), 'hipsR': (13, 0, 0), 'spine': (22, 0, 0), 'chest': (13, 0, 0), 'head': (-40, 0, 0),
                       'handL': (.21, -.2, .74), 'handR': (-.21, -.2, .74), 'kneeL': (.14, -1, 0), 'kneeR': (-.14, -1, 0)})


def crouch_idle(rig, name='Crouch_Idle'):
    return loop2(rig, name, CROUCH, {'hips': (0, .14, -.35), 'chest': (20, 0, 0), 'head': (-56, 0, 4)}, 2.6)


def sneak_idle(rig, name='Sneak_Idle'):
    return loop2(rig, name, SNEAK, {'hips': (0, .09, -.21), 'chest': (15, 0, 0), 'head': (-38, 0, -6)}, 2.4)


def strafe(rig, side, name):
    s = 1 if side == 'L' else -1      # moving to the character's left: the planted feet slide right (-X)
    return gait(rig, name, period=.7, duty=.42, stride=.7, lift=.14, kick=.04, drop=.08, bob=-.025, lean=4, hip_yaw=3, arm=.06, elbow_up=.8, direction=(-s, 0))


def bow_aim_walk(rig, name='Bow_Aim_Walk'):
    up = bow_pose(1.0)
    return gait(rig, name, period=1.1, duty=.62, stride=.6, lift=.07, kick=.03, drop=.09, bob=.01, lean=2, hip_yaw=3, arm=0, elbow_up=0,
                upper={k: v for k, v in up.items() if k not in ('hips', 'hipsR', 'footL', 'footR', 'kneeL', 'kneeR')})


def lance_charge(rig, name='Lance_Charge'):
    up = merge(sword((-.2, -.18, 1.22), (.03, -1, -.03), (0, 0, 1), elbow=(-1, .9, 0)), shield((.12, -.32, 1.32)), {'spine': (12, 0, 4), 'chest': (4, 0, 2), 'head': (-14, 0, -4)})
    return gait(rig, name, period=.66, duty=.36, stride=1.0, lift=.18, kick=.2, drop=.1, bob=-.035, lean=12, hip_yaw=4, arm=0, elbow_up=0, upper=up)


# ---- sword and shield -------------------------------------------------------------------------------------------
END = (1.0, {})


def attack(rig, name, keys, base=None, tail=.32):
    base = base or SNS_READY
    last = keys[-1][0]
    return choreo.bake(rig, name, keys + [(last + tail, dict(base))], base)


def chop(rig, name='1H_Melee_Attack_Chop'):
    """Overhead cut: the sword up behind the head, the weight onto the front foot, the blade down the centre line."""
    return attack(rig, name, [
        (0, {}),
        (.22, merge(sword((-.2, .05, 1.78), (0, .45, .9)), {'elbowR': (-1, .2, .6), 'spine': (-4, 0, 14), 'chest': (-4, 0, 8), 'hips': (0, .06, -.06), 'head': (-2, 0, -14)})),
        (.34, merge(sword((-.12, -.32, 1.62), (0, -.5, .87)), {'elbowR': (-1, .4, 0), 'spine': (8, 0, 0), 'chest': (6, 0, -2), 'hips': (0, -.02, -.09), 'head': (-8, 0, -4)})),
        (.42, merge(sword((-.08, -.5, 1.24), (0, -.93, -.37)), {'elbowR': (-1, .6, -.5), 'spine': (18, 0, -6), 'chest': (8, 0, -6), 'hips': (0, -.08, -.14), 'head': (-14, 0, 4),
                                                              'footL': (.04, -.2, 0, 0, 10), 'footR': (-.04, .26, 0, 22, -25)})),
        (.62, merge(sword((-.1, -.46, 1.18), (0, -.9, -.44)), {'spine': (16, 0, -6), 'hips': (0, -.06, -.13)})),
    ])


def slice_diagonal(rig, name='1H_Melee_Attack_Slice_Diagonal'):
    """High right to low left, the hips and chest turning through the cut."""
    b0, b1 = n(-.5, .3, .8), n(.55, -.6, -.55)
    return attack(rig, name, [
        (0, {}),
        (.2, merge({'handR': (-.42, -.02, 1.72), 'elbowR': (-1, .3, .4)}, dict(zip(('bladeR', 'edgeR'), arc(b0, b1, 0))),
                   {'spine': (-2, 0, 22), 'chest': (-2, 0, 12), 'head': (-4, 0, -20), 'hips': (0, .06, -.07)})),
        (.33, merge({'handR': (-.2, -.42, 1.38), 'elbowR': (-1, .5, 0)}, dict(zip(('bladeR', 'edgeR'), arc(b0, b1, .5))),
                    {'spine': (10, 0, 0), 'chest': (4, 0, -4), 'hips': (0, -.03, -.11)})),
        (.42, merge({'handR': (.18, -.4, 1.0), 'elbowR': (-.4, 1, -.5)}, dict(zip(('bladeR', 'edgeR'), arc(b0, b1, 1))),
                    {'spine': (18, 0, -20), 'chest': (6, 0, -12), 'head': (-12, 0, 18), 'hips': (0, -.07, -.14),
                     'footL': (.06, -.22, 0, 0, 14), 'footR': (-.06, .24, 0, 20, -30)})),
        (.6, merge({'handR': (.2, -.34, .98)}, {'spine': (14, 0, -18)})),
    ])


def slice_horizontal(rig, name='1H_Melee_Attack_Slice_Horizontal'):
    """A flat cut from the right across the front, the body unwinding behind it."""
    b0, b1 = n(-.9, .3, .25), n(.75, -.6, .1)
    return attack(rig, name, [
        (0, {}),
        (.2, merge({'handR': (-.42, .02, 1.34), 'elbowR': (-.6, .8, -.2)}, dict(zip(('bladeR', 'edgeR'), arc(b0, b1, 0))),
                   {'hipsR': (0, 0, -14), 'spine': (2, 0, -22), 'chest': (0, 0, -14), 'head': (-4, 0, 30), 'hips': (0, .06, -.08)})),
        (.32, merge({'handR': (-.2, -.5, 1.32), 'elbowR': (-1, .5, -.3)}, dict(zip(('bladeR', 'edgeR'), arc(b0, b1, .5))),
                    {'hipsR': (0, 0, 4), 'spine': (8, 0, 6), 'chest': (2, 0, 6), 'head': (-6, 0, 0), 'hips': (0, -.02, -.1)})),
        (.42, merge({'handR': (.32, -.34, 1.3), 'elbowR': (-.2, 1, -.4)}, dict(zip(('bladeR', 'edgeR'), arc(b0, b1, 1))),
                    {'hipsR': (0, 0, 18), 'spine': (10, 0, 22), 'chest': (2, 0, 12), 'head': (-6, 0, -30), 'hips': (0, -.05, -.12),
                     'footL': (.08, -.2, 0, 0, 24), 'footR': (-.06, .22, 0, 22, -10)})),
        (.6, merge({'handR': (.3, -.3, 1.26)}, {'hipsR': (0, 0, 16)})),
    ])


def stab(rig, name='1H_Melee_Attack_Stab'):
    """Sword drawn back at the hip, then a straight thrust with a lunge onto the front foot."""
    return attack(rig, name, [
        (0, {}),
        (.2, merge(sword((-.3, .12, 1.22), (.05, -1, .04), (0, 0, 1), elbow=(-1, 1, 0)), {'hipsR': (0, 0, -10), 'spine': (2, 0, -8), 'hips': (0, .08, -.1)})),
        (.34, merge(sword((-.1, -.66, 1.34), (.02, -1, .05), (0, 0, 1), elbow=(-1, .5, -.3)),
                    {'hipsR': (0, 0, 8), 'spine': (16, 0, 6), 'chest': (4, 0, 4), 'hips': (0, -.12, -.15), 'head': (-12, 0, 0),
                     'footL': (.04, -.34, 0, 0, 10), 'footR': (-.04, .3, 0, 26, -25)})),
        (.52, merge(sword((-.12, -.6, 1.3), (.02, -1, .05), (0, 0, 1)), {'hips': (0, -.1, -.14)})),
    ])


def rush1(rig, name='SnS_Rush_1'):
    """Thorn Rush I: a rising diagonal cut from low right to high left, the rear leg driving through."""
    b0, b1 = n(-.45, .45, -.75), n(.4, -.5, .75)
    return attack(rig, name, [
        (0, {}),
        (.14, merge({'handR': (-.36, .08, .96), 'elbowR': (-1, .5, -.2)}, dict(zip(('bladeR', 'edgeR'), arc(b0, b1, 0))),
                    {'spine': (14, 0, 16), 'chest': (4, 0, 8), 'hips': (0, .08, -.15), 'head': (-14, 0, -12)})),
        (.25, merge({'handR': (-.12, -.44, 1.28)}, dict(zip(('bladeR', 'edgeR'), arc(b0, b1, .5))),
                    {'spine': (4, 0, -2), 'chest': (0, 0, -4), 'hips': (0, -.04, -.1)})),
        (.34, merge({'handR': (.14, -.3, 1.74), 'elbowR': (-.5, .6, .6)}, dict(zip(('bladeR', 'edgeR'), arc(b0, b1, 1))),
                    {'spine': (-6, 0, -18), 'chest': (-4, 0, -10), 'hips': (0, -.08, -.06), 'head': (-2, 0, 14),
                     'footL': (.05, -.24, 0, 0, 14), 'footR': (-.05, .22, 0, 30, -25)})),
        (.5, {'handR': (.1, -.3, 1.66)}),
    ], tail=.3)


def rush2(rig, name='SnS_Rush_2'):
    """Thorn Rush II: a full spin on the front foot with the sword held out level, cutting twice across the front."""
    keys = [(0, {})]
    for i, (t, yaw) in enumerate(((.1, -30), (.22, 40), (.34, 140), (.46, 250), (.58, 380), (.7, 420))):
        b = Quaternion((0, 0, 1), math.radians(yaw)) @ Vector((-.95, -.25, .12))
        keys.append((t, merge({'hipsR': (6, 0, yaw * .85), 'spine': (8, 0, yaw * .1), 'chest': (2, 0, yaw * .05), 'hips': (0, 0, -.12),
                               'handR': (-.5, -.22, 1.3), 'elbowR': (-1, .3, -.2)}, dict(zip(('bladeR', 'edgeR'), cut(tuple(b), (0, 0, 1)))),
                              shield((.1, -.12, 1.3), face=n(0, -1, 0)),
                              {'footL': (.02, -.12, 0, 0, yaw * .85), 'footR': (-.03, .12, .06 * (0 < i < 5), 10, -20 + yaw * .7)})))
    keys.append((.84, {'hipsR': (0, 0, 360), 'spine': (6, 0, 360 * 0), 'footL': (.04, -.16, 0, 0, 370), 'footR': (-.04, .2, 0, 8, 335)}))
    act = attack(rig, name, keys, tail=.2)
    return act


def rush3(rig, name='SnS_Rush_3'):
    """Thorn Rush III: a leaping spin, the sword wide, landing low with the blade out to the side."""
    keys = [(0, {}), (.12, {'hips': (0, .04, -.2), 'spine': (16, 0, -20), 'hipsR': (0, 0, -30)})]
    for t, yaw, lift in ((.24, 60, .22), (.36, 200, .3), (.48, 330, .18)):
        b = Quaternion((0, 0, 1), math.radians(yaw)) @ Vector((-.95, -.2, .1))
        keys.append((t, merge({'hipsR': (4, 0, yaw), 'hips': (0, 0, lift), 'spine': (4, 0, 0), 'handR': (-.55, -.18, 1.32), 'elbowR': (-1, .3, -.1)},
                              dict(zip(('bladeR', 'edgeR'), cut(tuple(b), (0, 0, 1)))), shield((.1, -.12, 1.32)),
                              {'feetHips': True, 'footL': (.03, -.16, .3, -20, 0), 'footR': (-.05, .1, .22, 30, 0), 'kneeL': (0, -1, .3), 'kneeR': (0, -1, .3)})))
    keys.append((.58, merge({'hipsR': (6, 0, 360), 'hips': (0, .02, -.24), 'feetHips': False, 'spine': (18, 0, 0),
                             'footL': (.08, -.22, 0, 0, 370), 'footR': (-.08, .24, 0, 20, 335), 'handR': (-.62, -.3, 1.0)},
                            dict(zip(('bladeR', 'edgeR'), cut(n(-.9, -.35, -.2), (0, 0, 1)))))))
    keys.append((.78, {'hips': (0, .02, -.18), 'hipsR': (2, 0, 360)}))
    return attack(rig, name, keys, tail=.25)


def leap_slash(rig, name='SnS_Leap_Slash'):
    """A crouch, a leap with the sword raised overhead, the cut coming down as the hunter lands."""
    return attack(rig, name, [
        (0, {}),
        (.1, {'hips': (0, .06, -.24), 'spine': (22, 0, 0), 'footR': (-.04, .22, 0, 30, -25)}),
        (.22, merge(sword((-.16, .08, 1.82), (0, .4, .92)), {'hips': (0, -.02, .26), 'spine': (-6, 0, 8), 'chest': (-6, 0, 4),
                    'feetHips': True, 'footL': (.04, -.32, .36, -25, 0), 'footR': (-.06, .22, .2, 40, 0), 'kneeL': (0, -1, .4), 'kneeR': (0, -1, 0), 'elbowR': (-1, .2, .6)})),
        (.32, merge(sword((-.14, -.36, 1.66), (0, -.5, .87)), {'hips': (0, -.04, .2), 'spine': (8, 0, 0)})),
        (.4, merge(sword((-.08, -.52, 1.24), (0, -.95, -.3)), {'hips': (0, -.08, -.26), 'feetHips': False, 'spine': (26, 0, -4), 'chest': (8, 0, -4), 'head': (-18, 0, 0),
                   'footL': (.06, -.24, 0, 0, 10), 'footR': (-.06, .26, 0, 30, -25), 'kneeL': (.3, -1, 0), 'kneeR': (-.4, -1, 0)})),
        (.62, {'hips': (0, -.05, -.2), 'spine': (18, 0, -4)}),
    ], tail=.36)


def shield_crash(rig, name='SnS_Shield_Crash'):
    """A jump with the shield raised high, slammed down on the landing."""
    return attack(rig, name, [
        (0, {}),
        (.12, merge(shield((.12, -.18, 1.6), face=n(0, -.6, .8), up=n(0, .8, .6)), {'hips': (0, .05, -.22), 'spine': (16, 0, 0)})),
        (.28, merge(shield((.08, -.08, 2.0), face=n(0, -.2, .98), up=n(0, 1, .2), elbow=(1, .3, .3)),
                    {'hips': (0, 0, .28), 'spine': (-10, 0, 0), 'chest': (-8, 0, 0), 'feetHips': True,
                     'footL': (.06, -.2, .3, -20, 0), 'footR': (-.06, .2, .22, 30, 0), 'kneeL': (.2, -1, .4), 'kneeR': (-.2, -1, 0)})),
        (.38, merge(shield((.08, -.42, 1.3), face=n(0, -.6, -.8), up=n(0, -.8, .6)), {'hips': (0, -.04, .1), 'spine': (14, 0, 0)})),
        (.45, merge(shield((.06, -.56, .78), face=n(0, -.1, -1), up=n(0, -1, .1)),
                    {'hips': (0, -.06, -.34), 'feetHips': False, 'spine': (34, 0, 0), 'chest': (10, 0, 0), 'head': (-24, 0, 0),
                     'footL': (.1, -.24, 0, 0, 14), 'footR': (-.1, .24, 0, 30, -20), 'kneeL': (.3, -1, 0), 'kneeR': (-.4, -1, 0)})),
        (.7, {'hips': (0, -.05, -.3), 'spine': (30, 0, 0)}),
    ], tail=.4)


def sns_rend(rig, name='SnS_Rend'):
    """Rending Leap: a low lunge onto the wound, a diagonal cut, then a back-hand cut through it."""
    b0, b1 = n(-.5, .3, .8), n(.55, -.6, -.55)
    c0, c1 = n(.8, -.3, .3), n(-.85, -.4, .2)
    return attack(rig, name, [
        (0, {}),
        (.1, {'hips': (0, .06, -.22), 'spine': (22, 0, 10)}),
        (.2, merge({'handR': (-.4, -.08, 1.6)}, dict(zip(('bladeR', 'edgeR'), arc(b0, b1, 0))),
                   {'hips': (0, -.12, -.2), 'spine': (14, 0, 18), 'footL': (.04, -.36, 0, 0, 10), 'footR': (-.04, .32, .02, 34, -25)})),
        (.32, merge({'handR': (.16, -.42, 1.0)}, dict(zip(('bladeR', 'edgeR'), arc(b0, b1, 1))), {'spine': (22, 0, -18), 'chest': (6, 0, -10)})),
        (.44, merge({'handR': (.32, -.3, 1.28), 'elbowR': (-.3, 1, -.3)}, dict(zip(('bladeR', 'edgeR'), arc(c0, c1, 0))), {'spine': (12, 0, -24)})),
        (.56, merge({'handR': (-.4, -.36, 1.24), 'elbowR': (-1, .5, -.3)}, dict(zip(('bladeR', 'edgeR'), arc(c0, c1, 1))), {'spine': (12, 0, 20), 'chest': (4, 0, 10), 'hipsR': (0, 0, -10)})),
        (.74, {'handR': (-.42, -.3, 1.2)}),
    ], tail=.36)


def clash(rig, name='Hunt_Clash'):
    """Standoff: braced hard behind the shield, leaning in, the rear leg pushing (looped, straining)."""
    b = merge(SNS_READY, stance(.22, .4, .07, .16), {'spine': (20, 0, 8), 'chest': (6, 0, 6), 'head': (-20, 0, -8)},
              shield((.05, -.4, 1.36), face=n(-.1, -1, 0), elbow=(1, .1, -.6)), sword((-.28, -.18, 1.12), (0, -.6, .8), (0, -.8, -.6)))
    keys = [(0, {}), (.1, {'spine': (22, 1, 9), 'handL': (.05, -.41, 1.37)}), (.2, {'spine': (19, -1, 7)}), (.3, {'spine': (21, 1, 8), 'handL': (.05, -.39, 1.35)}), (.4, {})]
    return choreo.bake(rig, name, keys, b, loop=True, length=.4)


def block_hit(rig, name='Block_Hit'):
    up = merge(SNS_READY, shield((.06, -.36, 1.42), face=n(-.15, -1, .05)))
    return attack(rig, name, [(0, {}), (.08, merge(shield((.08, -.18, 1.46), face=n(-.1, -.9, .3)), {'hips': (0, .1, -.1), 'spine': (-6, 0, 6), 'head': (-2, 0, -6),
                                                    'footR': (-.04, .28, 0, 10, -25)})), (.28, {'hips': (0, .06, -.1), 'spine': (4, 0, 6)})], base=up, tail=.2)


def shield_bash(rig, name='Block_Attack'):
    """The shield driven forward into the monster from the guard, the shoulder behind it."""
    return attack(rig, name, [
        (0, {}),
        (.16, merge(shield((.14, -.16, 1.36)), {'hipsR': (0, 0, 16), 'spine': (4, 0, 14), 'hips': (0, .08, -.12)})),
        (.3, merge(shield((.04, -.62, 1.34), elbow=(1, .4, -.3)), {'hipsR': (0, 0, -10), 'spine': (18, 0, -12), 'chest': (4, 0, -6), 'hips': (0, -.12, -.14),
                   'footL': (.04, -.32, 0, 0, 10), 'footR': (-.04, .3, 0, 28, -25)})),
        (.48, merge(shield((.06, -.52, 1.34)), {'hips': (0, -.1, -.13)})),
    ], tail=.32)


# ---- katana (two hands) ---------------------------------------------------------------------------------------
def katana_attack(rig, name, keys, tail=.34):
    return attack(rig, name, keys, base=KATANA_READY, tail=tail)


def k_chop(rig, name='2H_Melee_Attack_Chop'):
    return katana_attack(rig, name, [
        (0, {}),
        (.24, merge(sword((-.04, .02, 1.84), (0, .35, .94), elbow=(-1, .3, .5)), {'elbowL': (1, .3, .5), 'spine': (-6, 0, 0), 'chest': (-4, 0, 0), 'hips': (0, .06, -.08), 'head': (-2, 0, 0)})),
        (.36, merge(sword((-.02, -.34, 1.64), (0, -.55, .84), elbow=(-1, .4, 0)), {'spine': (8, 0, 0), 'hips': (0, -.03, -.12)})),
        (.44, merge(sword((-.02, -.52, 1.24), (0, -.94, -.34), elbow=(-1, .5, -.5)), {'spine': (24, 0, 0), 'chest': (8, 0, 0), 'hips': (0, -.1, -.18), 'head': (-16, 0, 0),
                    'footL': (.05, -.28, 0, 0, 8), 'footR': (-.05, .3, 0, 28, -25)})),
        (.66, merge(sword((-.04, -.48, 1.16), (0, -.9, -.44)), {'spine': (22, 0, 0), 'hips': (0, -.08, -.17)})),
    ])


def k_slice(rig, name='2H_Melee_Attack_Slice'):
    b0, b1 = n(-.55, .25, .8), n(.6, -.55, -.58)
    return katana_attack(rig, name, [
        (0, {}),
        (.22, merge({'handR': (-.32, .0, 1.72), 'elbowR': (-1, .3, .4)}, dict(zip(('bladeR', 'edgeR'), arc(b0, b1, 0))), {'spine': (-2, 0, 24), 'chest': (0, 0, 10), 'hips': (0, .05, -.1)})),
        (.34, merge({'handR': (-.08, -.46, 1.34)}, dict(zip(('bladeR', 'edgeR'), arc(b0, b1, .5))), {'spine': (12, 0, 0), 'hips': (0, -.04, -.14)})),
        (.43, merge({'handR': (.2, -.42, 1.0), 'elbowR': (-.5, 1, -.4)}, dict(zip(('bladeR', 'edgeR'), arc(b0, b1, 1))),
                    {'spine': (22, 0, -24), 'chest': (6, 0, -10), 'hips': (0, -.08, -.17), 'footL': (.06, -.26, 0, 0, 16), 'footR': (-.06, .28, 0, 24, -30)})),
        (.62, {'handR': (.2, -.38, .98)}),
    ])


def k_stab(rig, name='2H_Melee_Attack_Stab'):
    return katana_attack(rig, name, [
        (0, {}),
        (.2, merge(sword((-.12, .02, 1.3), (.02, -1, .1), (0, 0, 1), elbow=(-1, 1, 0)), {'elbowL': (1, 1, 0), 'spine': (2, 0, -6), 'hips': (0, .08, -.12)})),
        (.33, merge(sword((-.04, -.66, 1.38), (.02, -1, .06), (0, 0, 1), elbow=(-1, .4, -.3)), {'elbowL': (1, .4, -.3), 'spine': (18, 0, 4), 'hips': (0, -.12, -.18),
                    'footL': (.04, -.36, 0, 0, 8), 'footR': (-.04, .3, 0, 28, -25)})),
        (.5, {'hips': (0, -.1, -.17)}),
    ])


def k_spin(rig, name='2H_Melee_Attack_Spin'):
    """Ember Wheel: a full turn with the blade held out level, then settled."""
    keys = [(0, {}), (.12, merge({'hipsR': (0, 0, -40), 'spine': (8, 0, -20), 'hips': (0, .04, -.16)}, sword((-.42, .1, 1.3), n(-.9, .4, .1), axis=(0, 0, 1))))]
    for t, yaw in ((.24, 40), (.36, 160), (.48, 280), (.6, 380)):
        b = Quaternion((0, 0, 1), math.radians(yaw)) @ Vector((-.95, -.3, .05))
        keys.append((t, merge({'hipsR': (6, 0, yaw * .9), 'spine': (10, 0, yaw * .1), 'hips': (0, 0, -.18), 'handR': (-.42, -.34, 1.26), 'elbowR': (-1, .3, -.2)},
                              dict(zip(('bladeR', 'edgeR'), cut(tuple(b), (0, 0, 1)))),
                              {'footL': (.02, -.12, 0, 0, yaw * .9), 'footR': (-.04, .14, .05, 12, -20 + yaw * .8)})))
    keys.append((.76, {'hipsR': (2, 0, 360), 'footL': (.05, -.2, 0, 0, 368), 'footR': (-.05, .22, 0, 10, 335), 'spine': (10, 0, 0)}))
    return katana_attack(rig, name, keys, tail=.3)


def k_spinning(rig, name='2H_Melee_Attack_Spinning'):
    """Spirit cut III: two cuts while turning, the second wide and low."""
    keys = [(0, {})]
    for t, yaw, h in ((.12, -30, 1.6), (.26, 90, 1.4), (.4, 210, 1.2), (.54, 330, 1.05), (.66, 400, 1.0)):
        b = Quaternion((0, 0, 1), math.radians(yaw)) @ Vector((-.9, -.3, .2 - (1.6 - h)))
        keys.append((t, merge({'hipsR': (8, 0, yaw * .85), 'spine': (12, 0, 0), 'hips': (0, 0, -.12 - (1.6 - h) * .2), 'handR': (-.4, -.3, h), 'elbowR': (-1, .3, -.2)},
                              dict(zip(('bladeR', 'edgeR'), cut(tuple(Vector(b).normalized()), (0, 0, 1)))),
                              {'footL': (.02, -.12, 0, 0, yaw * .85), 'footR': (-.04, .14, .05, 12, -20 + yaw * .75)})))
    keys.append((.82, {'hipsR': (2, 0, 360), 'footL': (.05, -.2, 0, 0, 368), 'footR': (-.05, .22, 0, 10, 335)}))
    return katana_attack(rig, name, keys, tail=.3)


IAI = merge(RELAX, stance(.22, .26, .08, .16), {'hipsR': (0, 0, 20), 'spine': (14, 0, 6), 'chest': (4, 0, 6), 'head': (-14, 0, -22)},
            sword((.06, -.06, 1.08), n(-.3, .85, .1), (0, 0, 1), elbow=(-1, .4, -.5)),
            {'handL': (.14, -.04, 1.06), 'bladeL': None, 'elbowL': (1, .3, -.6)})


def katana_iai(rig, name='Katana_Iai'):
    return loop2(rig, name, IAI, {'hips': (0, .26 * .2 + .01, -.18), 'spine': (15, 0, 6)}, 1.6)


def katana_rend(rig, name='Katana_Rend'):
    """Ember Rend: a low dash with the blade drawn across the body, then a cut down through the wound."""
    return katana_attack(rig, name, [
        (0, {}),
        (.12, merge(sword((.12, -.12, 1.12), n(.9, .2, .3), axis=(0, 0, -1)), {'hips': (0, .04, -.22), 'spine': (22, 0, 18), 'hipsR': (0, 0, 16)})),
        (.24, merge(sword((-.4, -.4, 1.26), n(-.9, -.4, .1), axis=(0, 0, -1)), {'hips': (0, -.1, -.2), 'spine': (14, 0, -16), 'hipsR': (0, 0, -10),
                    'footL': (.04, -.34, 0, 0, 8), 'footR': (-.04, .32, .02, 32, -25)})),
        (.36, merge(sword((-.06, .02, 1.84), (0, .3, .95), elbow=(-1, .3, .5)), {'hips': (0, -.06, -.1), 'spine': (-4, 0, 0), 'hipsR': (0, 0, 0)})),
        (.46, merge(sword((-.02, -.54, 1.24), (0, -.94, -.34), elbow=(-1, .5, -.5)), {'hips': (0, -.12, -.24), 'spine': (26, 0, 0), 'chest': (8, 0, 0)})),
        (.66, {'hips': (0, -.1, -.22)}),
    ], tail=.36)


# ---- lance and tower shield -------------------------------------------------------------------------------------
def lance_attack(rig, name, keys, tail=.32):
    return attack(rig, name, keys, base=LANCE_READY, tail=tail)


def lance_high(rig, name='Lance_High_Thrust'):
    """Over the shield: the lance raised to the shoulder and driven forward and down."""
    return lance_attack(rig, name, [
        (0, {}),
        (.2, merge(sword((-.24, .16, 1.66), n(.02, -1, -.12), (0, 0, 1), elbow=(-1, .4, .8)), {'spine': (2, 0, -6), 'hips': (0, .08, -.1)})),
        (.34, merge(sword((-.16, -.5, 1.6), n(.02, -1, -.18), (0, 0, 1), elbow=(-1, .2, .4)), {'spine': (16, 0, 8), 'hips': (0, -.08, -.14),
                    'footL': (.06, -.3, 0, 0, 10), 'footR': (-.06, .3, 0, 26, -25)})),
        (.5, {'hips': (0, -.06, -.13)}),
    ])


def lance_rend(rig, name='Lance_Rend'):
    """Piercing Rend: braced behind the tower shield, three driving thrusts into the wound."""
    keys = [(0, {})]
    for i, t0 in enumerate((.12, .34, .56)):
        keys += [(t0, merge(sword((-.24, .1, 1.24), n(.03, -1, .02), (0, 0, 1), elbow=(-1, .9, 0)), {'hips': (0, .08, -.14), 'spine': (10, 0, 4)})),
                 (t0 + .1, merge(sword((-.16, -.56, 1.26), n(.03, -1, -.04), (0, 0, 1), elbow=(-1, .4, -.2)),
                                 {'hips': (0, -.08 - .02 * i, -.17), 'spine': (20 + 2 * i, 0, 8), 'footR': (-.06, .34, 0, 28, -25)}))]
    keys.append((.86, {'hips': (0, -.06, -.15)}))
    return lance_attack(rig, name, keys, tail=.3)


# ---- bow --------------------------------------------------------------------------------------------------------
def bow_pose(depth=1.0, cant=0.0, pitch=0.0, release=0.0, lean=0.0):
    """Full draw (depth 1): the body turned side-on, the bow arm out toward the target (straight ahead, -Y), the
    string hand drawn back to the cheek. cant tilts the bow, pitch aims up, release flings the string hand back."""
    aim = Quaternion((1, 0, 0), math.radians(-pitch)) @ Vector((0, -1, 0))     # pitch > 0 aims up
    shoulder_l = Vector((.12, -.06, 1.58))
    bow_hand = shoulder_l + aim * (.62 - .04 * (1 - depth))
    up = Quaternion(aim, math.radians(-cant)) @ (Quaternion((1, 0, 0), math.radians(-pitch)) @ Vector((0, 0, 1)))
    draw = (.16 + .5 * depth) + .25 * release
    str_hand = bow_hand - aim * draw + Vector((-.06, 0, .04)) - Vector((.0, 0, .08)) * release
    return {'hipsR': (0, 0, 55), 'spine': (2 + lean, 0, -20), 'chest': (0, 0, -18), 'head': (-pitch * .6, 0, -60), 'hips': (0, 0, -.06),
            'worldL': True, 'handL': tuple(bow_hand), 'bladeL': tuple(up), 'edgeL': tuple(aim), 'elbowL': (.3, .8, -1),
            'worldR': True, 'handR': tuple(str_hand), 'bladeR': tuple(aim), 'edgeR': tuple(up), 'elbowR': (-1, .8, .6),
            'footL': (.14, -.04, 0, 0, 50), 'footR': (-.16, .12, 0, 0, 40), 'kneeL': (.5, -1, 0), 'kneeR': (-.2, -1, 0)}


def bow_clip(rig, name, keys, tail=.25):
    return choreo.bake(rig, name, keys + [(keys[-1][0] + tail, {})], merge(RELAX, bow_pose(.6)))


def bow_draw(rig, name='Bow_Draw'):
    return choreo.bake(rig, name, [(0, bow_pose(1.0)), (.6, bow_pose(1.04, lean=1)), (1.2, bow_pose(1.0))], merge(RELAX, bow_pose(1.0)), loop=True, length=1.2)


def bow_shoot(rig, name='Bow_Shoot'):
    return bow_clip(rig, name, [(0, bow_pose(1.0)), (.06, bow_pose(.95, release=1)), (.2, bow_pose(.6, release=.6)), (.45, bow_pose(.75))])


def bow_power(rig, name='Bow_Power_Shoot'):
    return bow_clip(rig, name, [(0, bow_pose(1.0, cant=75)), (.07, bow_pose(.95, cant=75, release=1)), (.25, bow_pose(.6, cant=65, release=.5)), (.5, bow_pose(.75, cant=20))])


def bow_arc(rig, name='Bow_Arc_Shoot'):
    return bow_clip(rig, name, [(0, bow_pose(.6, pitch=15)), (.25, bow_pose(1.0, pitch=55)), (.32, bow_pose(.95, pitch=57, release=1)), (.7, bow_pose(.7, pitch=20))])


def bow_piercer(rig, name='Bow_Piercer'):
    return bow_clip(rig, name, [(0, bow_pose(.8, cant=40, lean=6)), (.7, bow_pose(1.15, cant=45, lean=12)), (.78, bow_pose(1.0, cant=45, lean=4, release=1)), (1.45, bow_pose(.7, cant=20))])


def bow_tracer(rig, name='Bow_Tracer'):
    deep = lambda d, **k: merge(bow_pose(d, **k), {'hips': (0, 0, -.14), 'footL': (.2, -.06, 0, 0, 50), 'footR': (-.22, .14, 0, 0, 40)})
    return bow_clip(rig, name, [(0, deep(.9, cant=25, lean=6)), (.3, deep(1.2, cant=25, lean=10)), (.36, deep(1.0, cant=25, lean=6, release=1)),
                                (.55, deep(.7, cant=20, release=.5)), (.8, deep(.8))])


def bow_step(rig, side, name=None):
    s = 1 if side == 'L' else -1
    base = bow_pose(.5)
    return choreo.bake(rig, name or 'Bow_Step_' + side, [
        (0, {}), (.1, {'hips': (0, 0, -.14)}),
        (.24, {'hips': (0, 0, .08), 'feetHips': True, 'footL': (s * .08, 0, .14, 0, 0), 'footR': (s * .02, 0, .1, 0, 0), 'kneeL': (0, -1, .3), 'kneeR': (0, -1, .3)}),
        (.38, merge(base, {'hips': (0, 0, -.12), 'feetHips': False})), (.6, merge(base, {}))], merge(RELAX, base))


# ---- evasions, falls, hits ----------------------------------------------------------------------------------------
def jump_dash(rig, side, name=None):
    """The drawn evade to the side: crouch, spring sideways off the trailing leg, the leading knee tucked high and the
    trailing leg flung out, the body leaning into the dash; a low landing on the leading foot."""
    s = 1 if side == 'L' else -1
    lead, trail = ('L', 'R') if s > 0 else ('R', 'L')
    return choreo.bake(rig, name or 'Hunt_JumpDash_' + side, [
        (0, {}),
        (.08, {'hips': (0, .02, -.18), 'spine': (14, -6 * s, 0), 'foot' + lead: (s * .05, -.1, 0, 0, 10 * s), 'foot' + trail: (-s * .06, .12, 0, 16, -10 * s)}),
        (.18, {'hips': (0, 0, .26), 'spine': (6, 16 * s, 0), 'chest': (2, 8 * s, 0), 'head': (-4, -14 * s, 0),
               'foot' + lead: (s * .24, -.08, .5, -10, 15 * s), 'knee' + lead: (s * .5, -1, .4),
               'foot' + trail: (-s * .32, .1, .2, 30, -20 * s), 'knee' + trail: (-s * .6, -1, 0)}),
        (.28, {'hips': (0, 0, .14), 'spine': (8, 12 * s, 0), 'foot' + lead: (s * .16, -.06, .24, -4, 12 * s), 'foot' + trail: (-s * .24, .08, .22, 20, -15 * s)}),
        (.36, {'hips': (0, .03, -.22), 'spine': (20, 4 * s, 0), 'chest': (6, 2 * s, 0), 'head': (-10, -4 * s, 0),
               'foot' + lead: (s * .12, -.1, 0, 0, 12 * s), 'foot' + trail: (-s * .16, .2, 0, 18, -20 * s)}),
        (.52, dict(SNS_READY)),
    ], SNS_READY)


def roll(rig, name='Dodge_Forward'):
    """A forward roll: dive low over the leading shoulder, curl through, come up on the feet."""
    tuck = {'feetHips': True, 'footL': (.06, -.32, .5, -40, 0), 'footR': (-.06, -.28, .44, -40, 0), 'kneeL': (0, -1, .5), 'kneeR': (0, -1, .5),
            'handL': (.16, -.36, 1.3), 'handR': (-.16, -.36, 1.3), 'bladeL': None, 'bladeR': None, 'spine': (40, 0, 0), 'chest': (20, 0, 0), 'head': (-10, 0, 0)}
    return choreo.bake(rig, name, [
        (0, {}),
        (.08, {'hips': (0, -.1, -.3), 'spine': (30, 0, 0), 'footR': (-.04, .2, 0, 30, -10)}),
        (.16, merge(tuck, {'hips': (0, -.2, -.62), 'hipsR': (95, -12, 0)})),
        (.25, merge(tuck, {'hips': (0, -.1, -.78), 'hipsR': (190, -8, 0)})),
        (.33, merge(tuck, {'hips': (0, .0, -.62), 'hipsR': (280, 0, 0)})),
        (.42, {'hips': (0, .02, -.32), 'hipsR': (350, 0, 0), 'feetHips': False, 'spine': (24, 0, 0), 'chest': (8, 0, 0),
               'footL': (.06, -.1, 0, 0, 6), 'footR': (-.06, .16, 0, 30, -6), 'kneeL': (.2, -1, 0), 'kneeR': (-.2, -1, 0)}),
        (.6, merge(dict(RELAX), {'hipsR': (360, 0, 0), 'hips': (0, 0, -.06), 'spine': (8, 0, 0)})),
    ], RELAX)


def backstep(rig, name='Dodge_Backward'):
    return choreo.bake(rig, name, [
        (0, {}),
        (.07, {'hips': (0, -.02, -.14), 'spine': (12, 0, 0)}),
        (.16, {'hips': (0, .04, .12), 'spine': (-6, 0, 0), 'head': (4, 0, 0), 'feetHips': True,
               'footL': (.04, .08, .16, 10, 0), 'footR': (-.04, .14, .12, 10, 0), 'kneeL': (0, -1, .2), 'kneeR': (0, -1, .2)}),
        (.26, {'hips': (0, .06, -.16), 'spine': (14, 0, 0), 'feetHips': False, 'footL': (.04, -.08, 0, 0, 8), 'footR': (-.04, .22, 0, 20, -20)}),
        (.42, dict(SNS_READY)),
    ], SNS_READY)


LYING = {'feetHips': True, 'hips': (0, .55, -.92), 'hipsR': (-88, 0, 0), 'spine': (4, 0, 0), 'chest': (2, 0, 0), 'head': (8, 0, 0),
         'footL': (.08, 0, 0, -10, 0), 'footR': (-.1, 0, 0, -10, 0), 'kneeL': (.2, -1, .2), 'kneeR': (-.2, -1, .2),
         'handL': (.34, .1, 1.2), 'handR': (-.34, .1, 1.2), 'bladeL': None, 'bladeR': None, 'elbowL': (1, 0, 0), 'elbowR': (-1, 0, 0)}


def lie_down(rig, name='Lie_Down'):
    """Knocked flat: thrown back off the feet and onto the back."""
    return choreo.bake(rig, name, [
        (0, {}),
        (.12, {'hips': (0, .14, -.06), 'spine': (-16, 0, 0), 'head': (12, 0, 0), 'handL': (.34, -.1, 1.4), 'handR': (-.34, -.1, 1.4), 'bladeL': None, 'bladeR': None,
               'footL': (.06, -.18, .1, -20, 4)}),
        (.3, merge(LYING, {'hips': (0, .4, -.6), 'hipsR': (-60, 0, 0), 'footL': (.08, -.1, .2, -10, 0), 'footR': (-.1, -.05, .14, -10, 0)})),
        (.42, merge(LYING, {'hips': (0, .54, -.95)})),
        (.6, LYING),
    ], RELAX)


def lie_standup(rig, name='Lie_StandUp'):
    """From the back: sit up, a knee under the body, push up to standing."""
    return choreo.bake(rig, name, [
        (0, LYING),
        (.3, merge(LYING, {'hips': (0, .4, -.86), 'hipsR': (-40, 0, 0), 'spine': (30, 0, 0), 'chest': (16, 0, 0), 'head': (-10, 0, 0),
                           'footL': (.08, -.1, .2, -40, 0), 'footR': (-.1, -.2, .3, -50, 0), 'handL': (.4, .26, .9), 'handR': (-.4, .26, .9)})),
        (.6, {'feetHips': False, 'hips': (0, .1, -.62), 'hipsR': (10, 0, 0), 'spine': (30, 0, 0), 'chest': (10, 0, 0), 'head': (-14, 0, 0),
              'footL': (.06, -.24, 0, 0, 6), 'footR': (-.08, .28, 0, 60, -6), 'kneeL': (.2, -1, 0), 'kneeR': (-.2, -1, 0),
              'handL': (.3, -.26, .92), 'handR': (-.26, -.1, 1.0)}),
        (.95, {'hips': (0, 0, -.2), 'hipsR': (6, 0, 0), 'spine': (14, 0, 0), 'chest': (4, 0, 0), 'footR': (-.06, .12, 0, 20, -6)}),
        (1.25, merge(dict(RELAX), {})),
    ], RELAX)


def dive(rig, name='Jump_Full_Short'):
    """The sprinting dive: a long flat leap forward, arms reaching out, landing on the chest."""
    return choreo.bake(rig, name, [
        (0, {}),
        (.1, {'hips': (0, -.06, -.2), 'spine': (30, 0, 0), 'footR': (-.04, .24, 0, 40, -6)}),
        (.25, {'feetHips': True, 'hips': (0, -.3, .0), 'hipsR': (62, 0, 0), 'spine': (10, 0, 0), 'head': (-30, 0, 0),
               'handL': (.18, -.62, 1.62), 'handR': (-.18, -.62, 1.62), 'elbowL': (1, 0, 0), 'elbowR': (-1, 0, 0),
               'footL': (.06, .06, .0, 30, 0), 'footR': (-.06, .1, .04, 30, 0), 'kneeL': (0, 1, -.2), 'kneeR': (0, 1, -.2)}),
        (.42, {'hips': (0, -.5, -.55), 'hipsR': (82, 0, 0), 'spine': (4, 0, 0), 'head': (-40, 0, 0), 'handL': (.24, -.5, 1.3), 'handR': (-.24, -.5, 1.3)}),
        (.55, {'hips': (0, -.6, -.9), 'hipsR': (88, 0, 0), 'head': (-50, 0, 0), 'handL': (.3, -.2, 1.25), 'handR': (-.3, -.2, 1.25)}),
    ], RELAX)


def jump_up(rig, name='Jump_Up'):
    """a standing jump: down into the knees, arms swung back; thrown up, arms forward; knees tucked at the top; landing
    soft into the knees; standing again"""
    crouch = {'hips': (0, .08, -.34), 'spine': (26, 0, 0), 'chest': (8, 0, 0), 'head': (-20, 0, 0), 'kneeL': (.3, -1, 0), 'kneeR': (-.3, -1, 0),
              'handL': (.24, .26, .86), 'handR': (-.24, .26, .86), 'elbowL': (.4, 1, 0), 'elbowR': (-.4, 1, 0)}
    takeoff = {'hips': (0, -.02, .06), 'spine': (4, 0, 0), 'chest': (0, 0, 0), 'head': (-6, 0, 0),
               'footL': (.02, .02, .06, 34, 4), 'footR': (-.02, .02, .06, 34, -4),
               'handL': (.24, -.24, 1.5), 'handR': (-.24, -.24, 1.5), 'elbowL': (.6, .3, -1), 'elbowR': (-.6, .3, -1)}
    apex = {'feetHips': True, 'hips': (0, -.02, .42), 'spine': (10, 0, 0), 'chest': (4, 0, 0), 'head': (-10, 0, 0),
            'footL': (.06, -.1, .62, 10, 4), 'footR': (-.06, -.06, .66, 10, -4), 'kneeL': (.2, -1, .4), 'kneeR': (-.2, -1, .4),
            'handL': (.3, -.2, 1.62), 'handR': (-.3, -.2, 1.62)}
    land = merge(crouch, {'hips': (0, .06, -.3), 'spine': (20, 0, 0), 'handL': (.3, -.22, 1.0), 'handR': (-.3, -.22, 1.0),
                          'elbowL': (.6, .2, -1), 'elbowR': (-.6, .2, -1)})
    return choreo.bake(rig, name, [(0, {}), (.3, crouch), (.44, takeoff), (.62, apex), (.82, merge(takeoff, {'feetHips': False})), (.94, land), (1.4, {})], RELAX)


def jump_forward(rig, name='Jump_Forward'):
    """a running leap: the push off the back foot, the front knee driven up, a long flight, landing on the front foot and
    running on (it starts and ends in the jog's stride)"""
    push = {'hips': (0, -.12, -.1), 'spine': (14, 0, 0), 'footL': (.03, -.3, 0, 0, 4), 'footR': (-.03, .36, .04, 40, -4),
            'kneeL': (.1, -1, .2), 'handL': (.24, .18, 1.1), 'handR': (-.24, -.3, 1.36)}
    fly = {'feetHips': True, 'hips': (0, -.5, .32), 'spine': (12, 0, 0), 'head': (-12, 0, 0),
           'footL': (.05, -.42, .5, 20, 4), 'footR': (-.05, .32, .36, 60, -4), 'kneeL': (.1, -1, .6), 'kneeR': (-.1, -1, -.2),
           'handL': (.32, -.3, 1.5), 'handR': (-.32, -.1, 1.45)}
    land = {'hips': (0, -.9, -.18), 'spine': (18, 0, 0), 'footL': (.04, -.98, 0, 0, 4), 'footR': (-.04, -.5, .08, 30, -4),
            'handL': (.28, -.9, 1.0), 'handR': (-.28, -.7, 1.1)}
    return choreo.bake(rig, name, [(0, {}), (.18, push), (.5, fly), (.78, land), (1.1, {'hips': (0, -1.0, -.06), 'footL': (.02, -1.0, 0, 0, 4), 'footR': (-.02, -1.0, 0, 0, -4)})], RELAX)


def hit(rig, name, side=0.0):
    """Struck: thrown back from the blow (side: + from the left), recovering into the guard."""
    return choreo.bake(rig, name, [
        (0, {}),
        (.08, {'hips': (side * .05, .08, -.08), 'spine': (-14, -side * 12, side * 10), 'chest': (-6, 0, 0), 'head': (12, -side * 10, side * 16),
               'handL': (.3, -.08, 1.3), 'handR': (-.3, -.06, 1.28), 'footR': (-.04, .14, 0, 10, -10)}),
        (.24, {'hips': (0, .06, -.1), 'spine': (2, 0, 0), 'chest': (2, 0, 0), 'head': (-2, 0, 0)}),
        (.5, dict(SNS_READY)),
    ], SNS_READY)


def death(rig, name='Death_A'):
    """Falls back, limp."""
    limp = merge(LYING, {'head': (14, 0, 30), 'handL': (.5, .16, 1.0), 'handR': (-.46, .24, 1.06)})
    return choreo.bake(rig, name, [
        (0, {}), (.2, {'hips': (0, .1, -.24), 'spine': (-8, 0, 0), 'head': (10, 0, 0), 'handL': (.24, -.1, 1.2), 'handR': (-.24, -.1, 1.2), 'bladeL': None, 'bladeR': None}),
        (.45, merge(LYING, {'hips': (0, .3, -.66), 'hipsR': (-50, 0, 0), 'feetHips': True})),
        (.7, limp), (1.2, limp)], RELAX)


def use_item(rig, name='Use_Item'):
    """Drinks a tonic: the flask to the mouth, the head tipped back."""
    sip = {'handR': (-.06, -.14, 1.66), 'elbowR': (-1, .1, -.3), 'wristR': (-50, 0, 20), 'head': (12, 0, 0), 'neck': (6, 0, 0), 'spine': (-2, 0, 0)}
    return choreo.bake(rig, name, [(0, {}), (.3, sip), (.8, merge(sip, {'head': (18, 0, 0)})), (1.1, {})], RELAX)


def interact(rig, name='Interact'):
    """Kneels to gather: down on one knee, the right hand reaching to the ground."""
    down = {'hips': (0, .12, -.48), 'spine': (34, 0, 0), 'chest': (10, 0, 0), 'head': (-14, 0, 0),
            'footL': (.06, -.26, 0, 0, 8), 'footR': (-.08, .3, 0, 60, -6), 'kneeL': (.2, -1, 0), 'kneeR': (-.1, -1, -.3),
            'handR': (-.14, -.5, .62), 'elbowR': (-1, .4, -.4), 'handL': (.2, -.3, 1.0)}
    return choreo.bake(rig, name, [(0, {}), (.35, down), (.7, merge(down, {'handR': (-.12, -.44, .66)})), (1.1, {})], RELAX)


def ride(rig, name='Ride_Idle'):
    """Seated astride the hound: thighs apart, knees bent, feet hanging; hands forward on the harness."""
    sit = {'hips': (0, 0, 0), 'spine': (8, 0, 0), 'chest': (2, 0, 0), 'head': (-6, 0, 0), 'feetHips': True,
           'footL': (.2, -.16, .38, -10, 10), 'footR': (-.2, -.16, .38, -10, -10), 'kneeL': (.6, -1, 0), 'kneeR': (-.6, -1, 0),
           'handL': (.14, -.32, 1.14), 'handR': (-.14, -.32, 1.14), 'elbowL': (1, .6, -.4), 'elbowR': (-1, .6, -.4)}
    return loop2(rig, name, merge(RELAX, sit), {'spine': (10, 0, 0), 'hips': (0, 0, -.01)}, 2.0)


def sit_chair(rig, name='Sit_Chair_Idle'):
    sit = {'hips': (0, .1, -.58), 'spine': (-4, 0, 0), 'head': (-2, 0, 0), 'feetHips': True,
           'footL': (.06, -.48, .46, 0, 0), 'footR': (-.06, -.48, .46, 0, 0), 'kneeL': (0, -1, .6), 'kneeR': (0, -1, .6),
           'handL': (.18, -.26, .98), 'handR': (-.18, -.26, .98)}
    return loop2(rig, name, merge(RELAX, sit), {'chest': (3, 0, 0)}, 3.0)


def spellcast(rig, name='Spellcast_Shoot'):
    """Ember Bolt: the left hand drawn back, the right palm thrust out toward the target."""
    return choreo.bake(rig, name, [
        (0, {}),
        (.25, {'handR': (-.3, .1, 1.4), 'wristR': (40, 0, 0), 'hipsR': (0, 0, -18), 'spine': (0, 0, -12), 'hips': (0, .06, -.08)}),
        (.4, {'handR': (-.08, -.6, 1.48), 'elbowR': (-1, .4, -.2), 'wristR': (60, 0, 0), 'hipsR': (0, 0, 10), 'spine': (12, 0, 8), 'hips': (0, -.08, -.1),
              'handL': (.26, .12, 1.2), 'footL': (.04, -.24, 0, 0, 8), 'footR': (-.04, .24, 0, 20, -20)}),
        (.7, {'handR': (-.12, -.5, 1.4)}),
        (1.0, {}),
    ], RELAX)


# ---- the whole set ---------------------------------------------------------------------------------------------
def same(fn, name): return lambda rig: fn(rig, name)


CLIPS = [
    idle, idle_combat, katana_idle, blocking, same(blocking, 'Block'),
    walk, jog, sprint, same(walk, 'Walking_A'), same(jog, 'Running_A'), same(jog, 'Hunt_Jog'), same(sprint, 'Hunt_Sprint'), walk_back,
    sneak, crouch_walk, crouch_idle, sneak_idle,
    lambda r: strafe(r, 'L', 'Running_Strafe_Left'), lambda r: strafe(r, 'R', 'Running_Strafe_Right'),
    roll, backstep, lambda r: jump_dash(r, 'L'), lambda r: jump_dash(r, 'R'), lambda r: jump_dash(r, 'L', 'Dodge_Left'), lambda r: jump_dash(r, 'R', 'Dodge_Right'),
    dive, jump_up, jump_forward, lie_down, lie_standup, same(hit, 'Hit_A'), lambda r: hit(r, 'Hit_B', 1.0), death, use_item, interact, ride, sit_chair, spellcast,
    chop, slice_diagonal, slice_horizontal, stab, rush1, rush2, rush3, leap_slash, shield_crash, sns_rend, clash, block_hit, shield_bash,
    k_chop, k_slice, k_stab, k_spin, k_spinning, katana_iai, katana_rend,
    lance_high, lance_rend, lance_charge,
    bow_draw, bow_shoot, bow_power, bow_arc, bow_piercer, bow_tracer, lambda r: bow_step(r, 'L'), lambda r: bow_step(r, 'R'), bow_aim_walk,
]
TEST = CLIPS

# The humanoid monsters (the Codex skeletons) play the same clips, and Idle_B as their guard.
MONSTER = CLIPS + [same(idle_combat, 'Idle_B')]
