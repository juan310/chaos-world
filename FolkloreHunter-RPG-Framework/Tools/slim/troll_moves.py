# The Barrow Troll's own moves, authored on its own skeleton (the owner's troll sheet -> the local Forge's rigged model):
# a hunched, heavy body whose long arms hang to its knees, so nothing here comes from the hunter's moves. Poses are
# written in the troll's own metres (choreo works in the 1.83 m hunter's; T() converts) and mostly as offsets from the
# troll's own rest pose (its hands where they hang, its feet where they stand). The walk is a cycle function (moves.gait:
# feet planted and swung by sine and smoothstep, the body bobbing and turning with the stepping leg); the attacks are key
# poses interpolated through (Catmull-Rom), the blow accelerating into its contact frame.
#   build_slim.py ... --moves troll_moves
import math
from mathutils import Vector
import choreo
import moves


def T(rig, v):
    """troll metres -> the hunter metres choreo scales by the body's size"""
    return tuple(Vector(v) / rig.k)


def hand0(rig, s):
    """where the troll's hand hangs at rest (its wrist), in choreo's units"""
    return Vector(rig.h0['hand.' + s]) / rig.k


def off(rig, s, d):
    """the hand `d` troll metres from where it hangs at rest"""
    return tuple(hand0(rig, s) + Vector(d) / rig.k)


def head_top(rig):
    return Vector(rig.h0['head']) / rig.k + Vector((0, 0, .22 / rig.k))


def relax(rig):
    """standing: knees soft, the back hunched, the head pushed forward and lifted to look ahead, the arms hanging"""
    return moves.merge(moves.RELAX, {
        'hips': T(rig, (0, .02, -.04)), 'spine': (10, 0, 0), 'chest': (5, 0, 0), 'neck': (4, 0, 0), 'head': (-14, 0, 0),
        'handL': off(rig, 'l', (.02, -.06, .05)), 'elbowL': (.5, .5, -1), 'wristL': (-4, 0, 0),
        'handR': off(rig, 'r', (-.02, -.06, .05)), 'elbowR': (-.5, .5, -1), 'wristR': (-4, 0, 0),
        'footL': (.02, 0, 0, 0, 8), 'footR': (-.02, 0, 0, 0, -8), 'kneeL': (.3, -1, 0), 'kneeR': (-.3, -1, 0)})


def idle(rig, name='Idle'):
    """slow heavy breathing: the chest heaves, the shoulders rise, the head sways a little"""
    base = relax(rig)
    up = {'hips': T(rig, (0, .02, -.03)), 'chest': (2, 0, 0), 'shL': (0, 4, 0), 'shR': (0, -4, 0), 'head': (-17, 0, 3),
          'handL': off(rig, 'l', (.03, -.05, .08)), 'handR': off(rig, 'r', (-.03, -.05, .08))}
    return choreo.bake(rig, name, [(0, {}), (1.6, up), (3.2, {})], base, loop=True, length=3.2)


def idle_combat(rig, name='Idle_Combat'):
    """roused: lower, wider, arms out from the body, fists half raised, swaying from foot to foot"""
    base = moves.merge(relax(rig), {
        'hips': T(rig, (0, .04, -.12)), 'spine': (16, 0, 0), 'chest': (6, 0, 0), 'head': (-22, 0, 0),
        'footL': (.1, -.06, 0, 0, 12), 'footR': (-.1, .06, 0, 0, -12), 'kneeL': (.6, -1, 0), 'kneeR': (-.6, -1, 0),
        'handL': off(rig, 'l', (.14, -.22, .32)), 'elbowL': (1, .2, -.6), 'handR': off(rig, 'r', (-.14, -.22, .32)), 'elbowR': (-1, .2, -.6)})
    left = {'hips': T(rig, (.04, .04, -.13)), 'hipsR': (0, 3, 4), 'chest': (8, 0, -4)}
    right = {'hips': T(rig, (-.04, .04, -.13)), 'hipsR': (0, -3, -4), 'chest': (8, 0, 4)}
    return choreo.bake(rig, name, [(0, left), (1.1, right), (2.2, left)], base, loop=True, length=2.2)


def walk(rig, name='Walking_A'):
    """a lumbering walk: long planted steps, the body rolling over each one, the long arms swinging low"""
    hl, hr = hand0(rig, 'l'), hand0(rig, 'r')

    def arms(t):
        a = math.cos(2 * math.pi * t / 1.3)                      # + : the left arm swings back
        return {'handL': tuple(hl + Vector((.02, -.04 + .2 * a, .05 + .06 * max(0, -a))) / rig.k),
                'handR': tuple(hr + Vector((-.02, -.04 - .2 * a, .05 + .06 * max(0, a))) / rig.k),
                'elbowL': (.5, .6, -1), 'elbowR': (-.5, .6, -1), 'wristL': (-8, 0, 0), 'wristR': (-8, 0, 0),
                'neck': (6, 0, 0), 'head': (-18, 0, 0)}
    return moves.gait(rig, name, period=1.3, duty=.64, stride=.62, lift=.08, kick=.03, drop=.06, bob=.03, lean=14,
                      hip_yaw=9, arm=0, elbow_up=0, upper=arms)


def smash(rig, name='Troll_Smash'):
    """the ground pound: both fists raised together before the face, the body gathering on bent knees, then everything
    thrown down: the fists hammer the ground in front, the troll dropping into a deep squat; a moment's hold; it rises"""
    base = idle_combat_pose(rig)
    top = head_top(rig)
    # the fists raised together in front of the face, elbows bent, the back still hunched. The model was made with its
    # long arms hanging along its sides, so it has no armpit skin to stretch: fists over the head shredded its flanks
    # (the Proving close-ups, 2026-10-28), and arching the back or pushing the hips back tore the thighs' skin (12 %)
    rear = {'hips': T(rig, (0, .02, -.1)), 'spine': (8, 0, 0), 'chest': (0, 0, 0), 'neck': (-2, 0, 0), 'head': (-10, 0, 0),
            'handL': tuple(top + Vector((.13, -.42, -.22)) / rig.k), 'elbowL': (1, -.2, -.6),
            'handR': tuple(top + Vector((-.13, -.42, -.22)) / rig.k), 'elbowR': (-1, -.2, -.6),
            'footL': (.12, -.08, 0, 0, 14), 'footR': (-.12, .1, 0, 0, -14)}
    peak = moves.merge(rear, {'handL': tuple(top + Vector((.11, -.36, -.12)) / rig.k), 'handR': tuple(top + Vector((-.11, -.36, -.12)) / rig.k),
                              'spine': (6, 0, 0), 'hips': T(rig, (0, .02, -.11))})
    # the reach to the ground comes mostly from the back bending, with the feet wide and the knees out: a deep squat
    # folded the thighs onto the loincloth and crushed it (the body check measured 12 % of the skin torn there)
    # (with the body turned to face its skeleton's way, 2026-10-28, the bend really is forward: 58 degrees of back
    # pinched the belly, 6.1 %; more of the reach now comes from the hips hinging back)
    hit = {'hips': T(rig, (0, .06, -.4)), 'spine': (44, 0, 0), 'chest': (14, 0, 0), 'neck': (6, 0, 0), 'head': (-30, 0, 0),
           'handL': T(rig, (.2, -.92, .3)), 'elbowL': (1, .3, -.2), 'wristL': (20, 0, 0),
           'handR': T(rig, (-.2, -.92, .3)), 'elbowR': (-1, .3, -.2), 'wristR': (20, 0, 0),
           'footL': (.24, -.1, 0, 0, 22), 'footR': (-.24, .12, 0, 0, -22), 'kneeL': (1.3, -1, 0), 'kneeR': (-1.3, -1, 0)}
    hold = moves.merge(hit, {'hips': T(rig, (0, .05, -.37)), 'spine': (40, 0, 0)})
    # the blow itself runs straight and fast from the peak to the ground ('_ease' carries forward, so it is reset after)
    keys = [(0, {}), (.36, rear), (.5, moves.merge(peak, {'_ease': 'linear'})), (.62, moves.merge(hit, {'_ease': 'smooth'})), (.86, hold), (1.3, {})]
    return choreo.bake(rig, name, keys, base)


def idle_combat_pose(rig):
    return moves.merge(relax(rig), {
        'hips': T(rig, (0, .04, -.12)), 'spine': (16, 0, 0), 'chest': (6, 0, 0), 'head': (-22, 0, 0),
        'footL': (.1, -.06, 0, 0, 12), 'footR': (-.1, .06, 0, 0, -12), 'kneeL': (.6, -1, 0), 'kneeR': (-.6, -1, 0),
        'handL': off(rig, 'l', (.14, -.22, .32)), 'elbowL': (1, .2, -.6), 'handR': off(rig, 'r', (-.14, -.22, .32)), 'elbowR': (-1, .2, -.6)})


def swipe(rig, name='Troll_Swipe'):
    """a backhand: the right arm drawn across the chest as the body winds to the left, then flung out wide to the right
    at chest height as the hips and shoulders unwind"""
    base = idle_combat_pose(rig)
    wind = {'hipsR': (0, 0, 22), 'spine': (14, 0, 18), 'chest': (6, 0, 10), 'head': (-20, 0, -14),
            'handR': T(rig, (.42, -.32, 1.55)), 'elbowR': (-1, -.2, .4), 'wristR': (0, 0, 30),
            'handL': off(rig, 'l', (.2, .1, .3)), 'footR': (-.1, .14, 0, 10, -20)}
    strike = {'hipsR': (0, 0, -26), 'spine': (18, 0, -20), 'chest': (6, 0, -12), 'head': (-22, 0, 10),
              'handR': T(rig, (-1.05, -.42, 1.4)), 'elbowR': (-1, .3, .2), 'wristR': (0, 0, -20),
              'handL': off(rig, 'l', (.1, .14, .2)), 'footL': (.14, -.16, 0, 0, 14)}
    keys = [(0, {}), (.36, moves.merge(wind, {'_ease': 'linear'})), (.5, moves.merge(strike, {'_ease': 'smooth'})), (.95, {})]
    return choreo.bake(rig, name, keys, base)


def hit(rig, name='Hit_A'):
    """struck: the head and chest knocked back, a step back to catch the weight, then the guard again"""
    base = idle_combat_pose(rig)
    knocked = {'hips': T(rig, (0, .12, -.14)), 'spine': (-10, 0, 6), 'chest': (-6, 0, 0), 'head': (14, 0, 10),
               'handL': off(rig, 'l', (.24, .04, .3)), 'handR': off(rig, 'r', (-.24, .04, .3)), 'footR': (-.12, .26, 0, 10, -16)}
    return choreo.bake(rig, name, [(0, {}), (.1, knocked), (.28, {'spine': (6, 0, 0), 'head': (-8, 0, 0)}), (.55, {})], base)


def death(rig, name='Death_A'):
    """the legs give: it drops to its knees, sways, and topples forward onto its face"""
    base = idle_combat_pose(rig)
    knees = {'feetHips': True, 'hips': T(rig, (0, .1, -.62)), 'spine': (20, 0, 0), 'chest': (6, 0, 0), 'head': (-10, 0, 0),
             'footL': (.08, .5, .2, 80, 6), 'footR': (-.08, .5, .2, 80, -6), 'kneeL': (.3, -1, -.2), 'kneeR': (-.3, -1, -.2),
             'handL': off(rig, 'l', (.1, -.1, -.2)), 'handR': off(rig, 'r', (-.1, -.1, -.2))}
    sway = moves.merge(knees, {'spine': (26, 4, 0), 'head': (-4, 0, 6)})
    # face down: the hips at the belly's depth above the ground (lower, the body sank 30 cm into it: the body check)
    down = moves.merge(knees, {'hips': T(rig, (0, -.1, -.62)), 'hipsR': (70, 0, 0), 'spine': (12, 0, 0), 'chest': (4, 0, 0), 'head': (-30, 0, 14),
                               'handL': T(rig, (.5, -.9, .25)), 'handR': T(rig, (-.45, -.7, .25))})
    return choreo.bake(rig, name, [(0, {}), (.3, knees), (.55, sway), (.9, down), (1.4, down)], base)


def _captured():
    """moves captured from video by the two-view capture (Tools/capture2v: Wan 2.2 on this PC, read and fitted locally),
    one clip each, named in creatures/troll/clips/clips.json; none until a capture passes its checks"""
    import json, sys
    from pathlib import Path
    d = Path(__file__).resolve().parent.parent / 'capture2v'
    table = d / 'creatures' / 'troll' / 'clips' / 'clips.json'
    if not table.exists(): return []
    sys.path.insert(0, str(d))
    import to_clip
    return [(lambda rig, move=move, game=game: to_clip.clip(rig, game, table.with_name(move + '.json'), rig_info=table.with_name('rig_info.json')))
            for move, game in json.load(open(table)).items()]


CLIPS = [idle, idle_combat, walk, smash, swipe, hit, death] + _captured()
