"""Humanoid bone roles from any rig's names and hierarchy (Studio 0.17; used inside Blender by footslide_blender.py and
motionbricks_blender.py, and by plain Python). Knows the Folklore Hunter slim rig (upperleg.l), Mixamo (LeftUpLeg),
Unreal (thigh_l), Rigify-like (thigh.L), SOMA / MotionBricks (LeftLeg is the thigh there) and most mixtures: the feet,
hands and head are found by name, the limbs above them by walking up the hierarchy, so a name that means a thigh in
one convention and a shin in another cannot confuse it.

roles(names, parent) -> {"hips": n, "spine": n, "chest": n, "neck": n, "head": n,
                          "shoulder.l": n, "upperarm.l": n, "lowerarm.l": n, "hand.l": n,
                          "upperleg.l": n, "lowerleg.l": n, "foot.l": n, "toes.l": n, (and .r)}"""
import re

SKIP = re.compile(r"(ik|pole|target|twist|roll|helper|ctrl|control|mch|org|def_?ik|end$|_end|nub|tip)", re.I)


def side_of(name):
    n = re.split(r"[:|]", name)[-1]              # mixamorig:LeftFoot, Armature|foot.l
    for s, word in (("l", "left"), ("r", "right")):
        c = s.upper()
        if re.match(r"(%s|%s)(?=[A-Z0-9_.\- ]|$)" % (word.capitalize(), c), n) or n.lower().startswith(word) or n.startswith((c + "_", s + "_")):
            return s
        if re.search(r"(\.|_|-| )(%s|%s|%s)$" % (s, c, word), n, re.I) or re.search(r"(?<=[a-z0-9])%s(?=[A-Z0-9_.]|$)" % word.capitalize(), n):
            return s
    return None


def _find(names, word, side=None):
    c = [n for n in names if re.search(word, n, re.I) and not SKIP.search(re.sub(word, "", n, flags=re.I)) and (side is None or side_of(n) == side)]
    # the plainest name wins (fewest extra characters)
    return sorted(c, key=len)[0] if c else None


def roles(names, parent):
    names = list(names)
    kids = {}
    for n in names:
        kids.setdefault(parent.get(n), []).append(n)
    R = {}
    up = lambda n: parent.get(n) if n else None
    for s in ("l", "r"):
        foot = _find([n for n in names if not re.search("toe|ball", n, re.I)], "foot|ankle", s)
        if foot:
            R["foot." + s] = foot
            shin = up(foot); thigh = up(shin)
            if shin: R["lowerleg." + s] = shin
            if thigh: R["upperleg." + s] = thigh
            toes = [k for k in kids.get(foot, []) if not SKIP.search(k)]
            if toes: R["toes." + s] = sorted(toes, key=lambda k: (not re.search("toe|ball", k, re.I), len(k)))[0]
        hand = _find(names, "hand|wrist", s)
        if hand:
            R["hand." + s] = hand
            fore = up(hand); upper = up(fore); clav = up(upper)
            if fore: R["lowerarm." + s] = fore
            if upper: R["upperarm." + s] = upper
            if clav and re.search("shoulder|clav|collar", clav, re.I): R["shoulder." + s] = clav
    head = _find([n for n in names if side_of(n) is None], "head")
    if head:
        R["head"] = head
        neck = up(head)
        if neck: R["neck"] = neck
    # the hips: the common ancestor of the thighs; the chest: the common ancestor of the arms
    def chain(n):
        out = []
        while n: out.append(n); n = parent.get(n)
        return out
    def common(a, b):
        if not a or not b: return None
        ca = chain(a); cb = set(chain(b))
        return next((x for x in ca if x in cb), None)
    hips = common(R.get("upperleg.l"), R.get("upperleg.r")) or _find(names, "hips|pelvis")
    chest = common(R.get("upperarm.l"), R.get("upperarm.r"))
    if hips: R["hips"] = hips
    if chest: R["chest"] = chest
    if hips and chest and chest != hips:
        between = []
        n = parent.get(chest)
        while n and n != hips: between.append(n); n = parent.get(n)
        if between: R["spine"] = between[-1]
    return R


def blender_roles(arm):
    """roles for a Blender armature object"""
    bones = arm.data.bones
    return roles([b.name for b in bones], {b.name: (b.parent.name if b.parent else None) for b in bones})
