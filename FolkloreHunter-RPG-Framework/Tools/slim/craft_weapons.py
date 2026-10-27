# Two weapons modelled in code (our own designs, realistic proportions), for the ones the Forge could not make from
# the starting weapon set picture: a wooden recurve bow (carved limbs, leather grip, string) and a katana (curved steel
# blade with a pale edge, round guard, collar, wrapped grip, pommel cap). Written as Models/Weapons/Weapon_Bow and
# Weapon_Katana (.fbx + _texture.png), where WeaponSkin picks them up like the Forge weapons.
#   blender -b --python Tools/slim/craft_weapons.py
import bpy, bmesh, math
from pathlib import Path
from mathutils import Vector, Matrix

out = Path.home() / 'Documents/FolkloreHunter-RPG-Framework/Assets/Resources/Models/Weapons'
out.mkdir(parents=True, exist_ok=True)

# a small palette texture: each part's UVs sit inside one swatch
SW = {'wood': (.47, .29, .15), 'wood_dark': (.33, .19, .09), 'leather': (.24, .13, .07), 'string': (.86, .83, .72),
      'steel': (.70, .72, .75), 'edge': (.90, .92, .95), 'gold': (.72, .55, .22), 'wrap': (.10, .09, .10), 'ray': (.85, .82, .74)}
NAMES = list(SW)
N = 4                                   # swatches per row


def palette(path):
    size = 64 * N
    img = bpy.data.images.new('palette', size, size)
    px = [0.0] * (size * size * 4)
    for i, nm in enumerate(NAMES):
        cx, cy = i % N, i // N
        r, g, b = SW[nm]
        for y in range(cy * 64, cy * 64 + 64):
            for x in range(cx * 64, cx * 64 + 64):
                k = (y * size + x) * 4
                n = ((x * 7 + y * 13) % 11) / 11 * .06 - .03          # a little grain
                px[k:k + 4] = [max(0, min(1, r + n)), max(0, min(1, g + n)), max(0, min(1, b + n)), 1]
    img.pixels = px
    img.filepath_raw = str(path); img.file_format = 'PNG'; img.save()


def uv_of(nm):
    i = NAMES.index(nm); cx, cy = i % N, i // N
    return ((cx + .5) / N, (cy + .5) / N)


def tube(bm, pts, radii, swatch, sides=10, flat=1.0, twist_up=Vector((0, 1, 0))):
    """A tube through pts with the given radii (flat < 1 squashes it across `twist_up`)."""
    uv = bm.loops.layers.uv.verify()
    rings = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        a = twist_up - t * twist_up.dot(t); a = a.normalized() if a.length > 1e-5 else t.orthogonal().normalized()
        b = t.cross(a)
        ring = []
        for k in range(sides):
            ang = 2 * math.pi * k / sides
            ring.append(bm.verts.new(p + (a * math.cos(ang) * flat + b * math.sin(ang)) * radii[i]))
        rings.append(ring)
    for i in range(len(rings) - 1):
        for k in range(sides):
            f = bm.faces.new((rings[i][k], rings[i][(k + 1) % sides], rings[i + 1][(k + 1) % sides], rings[i + 1][k]))
            for l in f.loops: l[uv].uv = uv_of(swatch)
    for ring in (rings[0], rings[-1]):
        f = bm.faces.new(ring if ring is rings[-1] else list(reversed(ring)))
        for l in f.loops: l[uv].uv = uv_of(swatch)


def save(bm, name):
    me = bpy.data.meshes.new(name); bm.normal_update(); bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = True
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    mat = bpy.data.materials.new(name)
    me.materials.append(mat)
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.export_scene.fbx(filepath=str(out / (name + '.fbx')), use_selection=True, object_types={'MESH'},
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_anim=False)
    palette(out / (name + '_texture.png'))
    print('CRAFTED', name, len(me.vertices), 'verts', flush=True)
    bpy.data.objects.remove(o)


def bow():
    """1.35 m recurve: the grip at the origin, limbs along Z, the belly (string side) toward -X."""
    bm = bmesh.new()
    L = .66
    def limb(sign):
        pts, rad = [], []
        for i in range(25):
            u = i / 24
            z = sign * (.08 + (L - .08) * u)
            back = .10 * math.sin(math.pi * min(1, u * 1.05)) * (1 - u * .4)        # bends away from the archer
            recurve = -.06 * max(0, (u - .78) / .22) ** 2                           # the tips curl forward again
            pts.append(Vector((back + recurve, 0, z)))
            rad.append(.026 * (1 - .65 * u) + .006)
        return pts, rad
    for sign in (1, -1):
        p, r = limb(sign)
        tube(bm, p, r, 'wood', sides=8, flat=.55, twist_up=Vector((1, 0, 0)))
    tube(bm, [Vector((0, 0, -.11)), Vector((0, 0, .11))], [.034, .034], 'leather', sides=10)
    for z in (-.12, .12):
        tube(bm, [Vector((0, 0, z - .012)), Vector((0, 0, z + .012))], [.038, .038], 'wood_dark', sides=10)
    t1, t2 = Vector((-.055, 0, L - .005)), Vector((-.055, 0, -L + .005))
    tube(bm, [t2, t1], [.0035, .0035], 'string', sides=6)
    save(bm, 'Weapon_Bow')


def katana():
    """1.0 m: the grip from -0.27 to 0, the blade along +Z curving toward its back (+X), the edge toward -X."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.verify()
    n = 30; Lb = .70
    spine, edge = [], []
    for i in range(n + 1):
        u = i / n
        z = .035 + Lb * u
        sori = .035 * (u ** 2)                                     # the curve toward the back
        w = .032 * (1 - .25 * u) if u < .93 else .032 * .75 * (1 - (u - .93) / .07)
        spine.append(Vector((sori + w * .45, 0, z)))
        edge.append(Vector((sori - w * .55, 0, z + (.0 if u < .93 else (u - .93) * .02))))
    th = .0035
    def strip(a, b, sw, ta, tb):
        for i in range(n):
            q = [a[i] + Vector((0, ta, 0)), a[i + 1] + Vector((0, ta, 0)), b[i + 1] + Vector((0, tb, 0)), b[i] + Vector((0, tb, 0))]
            f = bm.faces.new([bm.verts.new(v) for v in q])
            for l in f.loops: l[uv].uv = uv_of(sw)
    mid = [s.lerp(e, .55) for s, e in zip(spine, edge)]
    for side in (1, -1):
        strip(spine, mid, 'steel', side * th, side * th * .8)
        strip(mid, edge, 'edge', side * th * .8, 0)
    strip(spine, spine, 'steel', th, -th)
    tube(bm, [Vector((0, 0, .018)), Vector((0, 0, .036))], [.021, .021], 'gold', sides=12)                  # collar
    tube(bm, [Vector((0, 0, .002)), Vector((0, 0, .012))], [.042, .042], 'gold', sides=20, flat=.8)          # guard
    tube(bm, [Vector((0, 0, -.27)), Vector((0, 0, 0))], [.017, .017], 'wrap', sides=10, flat=.8)             # grip
    for i in range(9):
        z = -.25 + i * .028
        tube(bm, [Vector((0, 0, z)), Vector((0, 0, z + .008))], [.0185, .0185], 'ray', sides=10, flat=.8)   # the wrap's diamonds
    tube(bm, [Vector((0, 0, -.285)), Vector((0, 0, -.268))], [.019, .019], 'gold', sides=12, flat=.8)        # pommel cap
    save(bm, 'Weapon_Katana')


bpy.ops.wm.read_factory_settings(use_empty=True)
bow()
katana()
