# Weapons for the hunter's Lance and Bow movesets, built in Blender for Tools/export_hunter_moveset.py (original
# geometry in the chunky KayKit style; colours are picked from the knight's own texture atlas so Unity's single
# hunter material shows them). Also derives clips the new weapons need from the CC0 KayKit animations.
#   Hunt_Bow           right hand, along the sword's axis (vertical while aiming; the limbs open toward the archer)
#   Hunt_Lance         right hand, along the sword's axis (forward in a thrust), with a vamplate guarding the hand
#   Hunt_TowerShield   left hand: the Knight's Rectangle_Shield enlarged
#   Hunt_Quiver        on the back (chest bone), arrows showing
#   Hunt_Arrow         template for flying arrows (origin at the nock, head at +Z); hidden in game, cloned when fired
import bpy, bmesh, math
import numpy as np
from mathutils import Vector, Matrix, Quaternion


# ---------------- palette from the atlas ----------------
class Palette:
    def __init__(self, image):
        w, h = image.size
        px = np.array(image.pixels[:], dtype=np.float32).reshape(h, w, 4)[..., :3]
        # Flat regions only: a swatch must not sit on an edge between two colours.
        pad = np.pad(px, ((2, 2), (2, 2), (0, 0)), mode='edge')
        var = np.zeros((h, w), np.float32)
        for dy in range(-2, 3):
            for dx in range(-2, 3):
                var += np.abs(pad[2 + dy:2 + dy + h, 2 + dx:2 + dx + w] - px).sum(2)
        self.px, self.flat, self.w, self.h = px, var < .08, w, h

    def uv(self, rgb):
        d = np.abs(self.px - np.array(rgb, np.float32)).sum(2) + (~self.flat) * 10
        y, x = np.unravel_index(np.argmin(d), d.shape)
        return ((x + .5) / self.w, (y + .5) / self.h)    # Blender image rows run bottom-up, as UV v does


COLORS = {'wood': (.42, .25, .13), 'wood_dark': (.26, .15, .08), 'leather': (.36, .2, .1), 'metal': (.72, .74, .78),
          'metal_dark': (.33, .34, .38), 'gold': (.86, .64, .22), 'string': (.92, .88, .76), 'red': (.68, .14, .12)}


class Builder:
    """Accumulates low-poly parts, each face UV'd to one palette swatch."""
    def __init__(self, palette):
        self.bm = bmesh.new(); self.uvl = self.bm.loops.layers.uv.new('UV'); self.pal = palette

    def _paint(self, faces, color):
        uv = self.pal.uv(COLORS[color])
        for f in faces:
            for l in f.loops: l[self.uvl].uv = uv

    def tube(self, points, radii, color, sides=6, caps=True):
        """A tapered tube through `points` (local coordinates) with one radius per point."""
        rings = []
        for i, p in enumerate(points):
            p = Vector(p)
            d = (Vector(points[min(i + 1, len(points) - 1)]) - Vector(points[max(i - 1, 0)])).normalized()
            a = d.orthogonal().normalized(); b = d.cross(a)
            rings.append([self.bm.verts.new(p + (a * math.cos(t) + b * math.sin(t)) * radii[i]) for t in (2 * math.pi * k / sides for k in range(sides))])
        faces = []
        for r0, r1 in zip(rings, rings[1:]):
            for k in range(sides):
                faces.append(self.bm.faces.new((r0[k], r0[(k + 1) % sides], r1[(k + 1) % sides], r1[k])))
        if caps:
            for ring, flip in ((rings[0], True), (rings[-1], False)):
                if min(radii[0], radii[-1]) >= 0:
                    try: faces.append(self.bm.faces.new(list(reversed(ring)) if flip else ring))
                    except ValueError: pass
        self._paint(faces, color)

    def box(self, center, size, color, rot=None):
        c = Vector(center); s = Vector(size) / 2; R = rot or Matrix.Identity(3)
        vs = [self.bm.verts.new(c + R @ Vector((sx * s.x, sy * s.y, sz * s.z))) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
        idx = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        self._paint([self.bm.faces.new([vs[i] for i in q]) for q in idx], color)

    def cone(self, base, tip, r, color, sides=6):
        base, tip = Vector(base), Vector(tip)
        d = (tip - base).normalized(); a = d.orthogonal().normalized(); b = d.cross(a)
        ring = [self.bm.verts.new(base + (a * math.cos(t) + b * math.sin(t)) * r) for t in (2 * math.pi * k / sides for k in range(sides))]
        apex = self.bm.verts.new(tip)
        faces = [self.bm.faces.new((ring[k], ring[(k + 1) % sides], apex)) for k in range(sides)]
        faces.append(self.bm.faces.new(list(reversed(ring))))
        self._paint(faces, color)

    def finish(self, name, material):
        me = bpy.data.meshes.new(name); self.bm.normal_update(); self.bm.to_mesh(me); self.bm.free()
        me.materials.append(material)
        for p in me.polygons: p.use_smooth = False
        return me


def _attach(name, mesh, like):
    """New object with the same bone parent and local transform as `like` (the sword or a shield)."""
    o = bpy.data.objects.new(name, mesh); bpy.context.scene.collection.objects.link(o)
    o.parent = like.parent; o.parent_type = like.parent_type; o.parent_bone = like.parent_bone
    o.matrix_parent_inverse = like.matrix_parent_inverse.copy(); o.matrix_basis = like.matrix_basis.copy()
    return o


def build_weapons(rig, sword, shield_rect, image):
    pal = Palette(image); mat = sword.active_material
    made = {}
    # Bow: grip at the hand, limbs along local Z (vertical while aiming); tips bend toward the archer (+X), short
    # recurves flick forward, string straight between the tips.
    b = Builder(pal)
    L = .82
    ys = [-L, -.7, -.5, -.25, 0, .25, .5, .7, L]
    pts = [(.26 * (abs(z) / L) ** 1.8 - (.05 if abs(z) > .75 else 0), 0, z) for z in ys]
    b.tube(pts, [.025, .035, .045, .055, .07, .055, .045, .035, .025], 'wood', sides=5)
    b.tube([(-.02, 0, -.13), (-.02, 0, .13)], [.075, .075], 'leather', sides=6)             # leather grip wrap
    for z in (-.15, .15): b.tube([(-.02, 0, z - .015), (-.02, 0, z + .015)], [.085, .085], 'gold', sides=6)
    tip = .26 - .05
    b.tube([(tip, 0, -L + .02), (tip, 0, L - .02)], [.008, .008], 'string', sides=4, caps=False)
    made['Hunt_Bow'] = _attach('Hunt_Bow', b.finish('Hunt_Bow', mat), sword)

    # Lance: long shaft along local +Z from behind the hand to a steel point, vamplate cone over the hand, gold rings.
    b = Builder(pal)
    b.tube([(0, 0, -.95), (0, 0, -.1), (0, 0, .25)], [.075, .065, .06], 'wood_dark', sides=6)
    b.tube([(0, 0, .25), (0, 0, 2.35)], [.07, .05], 'metal_dark', sides=6)
    b.cone((0, 0, 2.3), (0, 0, 3.15), .13, 'metal', sides=6)
    b.tube([(0, 0, .22), (0, 0, .78)], [.36, .08], 'metal', sides=8)                          # vamplate (hand guard)
    for z in (-.9, .8, 1.6, 2.28): b.tube([(0, 0, z - .03), (0, 0, z + .03)], [.09, .09], 'gold', sides=6)
    b.box((0, 0, 2.55), (.02, .3, .22), 'red')                                                 # pennant
    made['Hunt_Lance'] = _attach('Hunt_Lance', b.finish('Hunt_Lance', mat), sword)

    # Tower shield: the Knight's own rectangular shield, larger, for the lance.
    ts = shield_rect.copy(); ts.data = shield_rect.data.copy(); ts.name = 'Hunt_TowerShield'; ts.data.name = 'Hunt_TowerShield'
    bpy.context.scene.collection.objects.link(ts)
    ts.matrix_parent_inverse = shield_rect.matrix_parent_inverse.copy(); ts.matrix_basis = shield_rect.matrix_basis.copy()
    ts.data.transform(Matrix.Diagonal((1.25, 1.1, 1.45, 1)))
    made['Hunt_TowerShield'] = ts

    # Arrow template: origin at the nock, head toward +Z.
    b = Builder(pal)
    b.tube([(0, 0, 0), (0, 0, .78)], [.018, .018], 'wood', sides=4)
    b.cone((0, 0, .76), (0, 0, .95), .05, 'metal', sides=4)
    for a in (0, 120, 240):
        R = Matrix.Rotation(math.radians(a), 3, 'Z')
        b.box(R @ Vector((.035, 0, .1)), (.06, .006, .16), 'red', R)
    made['Hunt_Arrow'] = _attach('Hunt_Arrow', b.finish('Hunt_Arrow', mat), sword)

    # Quiver on the back: a leather tube with fletchings showing, slung over the right shoulder.
    b = Builder(pal)
    b.tube([(0, 0, -.42), (0, 0, .38)], [.15, .17], 'leather', sides=8)
    for z in (-.36, .32): b.tube([(0, 0, z - .025), (0, 0, z + .025)], [.18, .18], 'gold', sides=8)
    for k, (x, y) in enumerate(((.05, .04), (-.06, .02), (.0, -.07), (.08, -.05))):
        b.tube([(x, y, .3), (x, y, .62 + .04 * k)], [.015, .015], 'wood', sides=4)
        b.box((x, y, .58 + .04 * k), (.012, .09, .12), 'red' if k % 2 else 'string')
    q = bpy.data.objects.new('Hunt_Quiver', b.finish('Hunt_Quiver', mat)); bpy.context.scene.collection.objects.link(q)
    rig.data.pose_position = 'REST'; bpy.context.view_layer.update()
    chest = rig.pose.bones['chest']
    c = rig.matrix_world @ chest.head
    # The knight faces -Y: its back is +Y, its right side -X.
    want = Matrix.Translation(c + Vector((-.22, .42, .18))) @ (Matrix.Rotation(math.radians(-28), 4, 'Y') @ Matrix.Rotation(math.radians(8), 4, 'X'))
    q.parent = rig; q.parent_type = 'BONE'; q.parent_bone = 'chest'
    bpy.context.view_layer.update()
    q.matrix_world = want
    rig.data.pose_position = 'POSE'; bpy.context.view_layer.update()
    made['Hunt_Quiver'] = q
    return made


# ---------------- derived clips ----------------
def _curves(action):
    try:
        if action.fcurves is not None: return list(action.fcurves)
    except AttributeError: pass
    out = []
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags: out += list(bag.fcurves)
    return out


def _rot_curves(action, bone):
    path = 'pose.bones["%s"].rotation_quaternion' % bone
    found = {c.array_index: c for c in _curves(action) if c.data_path == path}
    return [found.get(i) for i in range(4)]


def _set_constant(action, bone, q):
    curves = _rot_curves(action, bone)
    if None in curves: return False
    for i in range(4):
        for kp in curves[i].keyframe_points:
            kp.co[1] = q[i]; kp.handle_left[1] = q[i]; kp.handle_right[1] = q[i]
    return True


def _pose_at(action, bone, frac):
    curves = _rot_curves(action, bone)
    if None in curves: return None
    f0, f1 = action.frame_range
    f = f0 + (f1 - f0) * frac
    return Quaternion([curves[i].evaluate(f) for i in range(4)]).normalized()


def overlay(base, pose, bones, frac, name):
    """`base` with `bones` held at `pose`'s rotation at normalized time `frac` (an upper-body layer, baked)."""
    a = bpy.data.actions[base].copy(); a.name = name
    for bone in bones:
        q = _pose_at(bpy.data.actions[pose], bone, frac)
        if q is not None: _set_constant(a, bone, q)
    return a


def offset(base, name, bone, rest, axis, degrees):
    """`base` with an extra rotation on one bone, about an armature-space axis (e.g. raise the thrusting arm)."""
    a = bpy.data.actions[base].copy(); a.name = name
    curves = _rot_curves(a, bone)
    if None in curves: return a
    to_bone = rest.matrix_local.to_quaternion().inverted()
    world = Quaternion(Vector(axis), math.radians(degrees))
    for k in range(len(curves[0].keyframe_points)):
        q = Quaternion([curves[i].keyframe_points[k].co[1] for i in range(4)])
        q2 = to_bone @ world @ rest.matrix_local.to_quaternion() @ q
        for i in range(4):
            curves[i].keyframe_points[k].co[1] = q2[i]; curves[i].keyframe_points[k].handle_left[1] = q2[i]; curves[i].keyframe_points[k].handle_right[1] = q2[i]
    return a


def derive_clips(rig):
    names = [b.name for b in rig.data.bones]
    right = [n for n in names if n.endswith('.r') and any(k in n for k in ('upperarm', 'lowerarm', 'wrist', 'hand'))]
    left = [n for n in names if n.endswith('.l') and any(k in n for k in ('upperarm', 'lowerarm', 'wrist', 'hand'))]
    torso = [n for n in names if n in ('spine', 'chest', 'neck', 'head', 'Spine', 'Chest', 'upperbody', 'torso')]
    made = []
    # High thrust: the stab with the thrusting arm raised, the point driven upward over a monster's back.
    arm = next((n for n in right if 'upperarm' in n), None)
    if arm: made.append(offset('1H_Melee_Attack_Stab', 'Lance_High_Thrust', arm, rig.data.bones[arm], (1, 0, 0), -38))
    # Lance charge: running with the lance couched forward (stab arm part-way out) and the shield up (blocking arm).
    a = overlay('Running_A', '1H_Melee_Attack_Stab', right, .38, 'Lance_Charge')
    for bone in left:
        q = _pose_at(bpy.data.actions['Blocking'], bone, .5)
        if q is not None: _set_constant(a, bone, q)
    made.append(a)
    # Bow: walking with the bow held out and the torso turned to aim (the aiming pose over the walk's legs).
    made.append(overlay('Walking_A', '1H_Ranged_Aiming', right + left + torso, .5, 'Bow_Aim_Walk'))
    return made, {'right': right, 'left': left, 'torso': torso}
