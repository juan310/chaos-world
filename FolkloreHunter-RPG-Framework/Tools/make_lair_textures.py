"""Textures for the Grave Warden's hallway and lair (0.9), from the supplied concept art where it shows the thing flat
on (the great door and its inscription) and drawn here where it does not (tiling floor and wall stone, the lair's rune
circle). Deterministic: run again to get the same files.

  python3 Tools/make_lair_textures.py"""
from pathlib import Path
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

root = Path(__file__).resolve().parents[1]
art = root / 'Docs/Concepts/Characters-0.9'
out = root / 'Assets/Resources/Environment/WardenLair'
out.mkdir(parents=True, exist_ok=True)
rng = np.random.default_rng(9)

# ---- the great door: the hallway picture's door, whose lower right a guard hides; the door is symmetric, so its left
# half (frame, leaf, half the horned skull) is mirrored for the right half.
hall = Image.open(art / 'warden-hallway.png').convert('RGB')
left = hall.crop((712, 132, 866, 482))                      # frame edge to the centre seam
door = Image.new('RGB', (left.width * 2, left.height))
door.paste(left, (0, 0)); door.paste(left.transpose(Image.FLIP_LEFT_RIGHT), (left.width, 0))
door.resize((768, 872), Image.LANCZOS).save(out / 'great_door.png')
hall.crop((656, 76, 1080, 116)).resize((1272, 120), Image.LANCZOS).save(out / 'inscription.png')


# ---- tiling cobblestone: irregular rounded stones (jittered grid Voronoi), dark mortar, wet highlights
def cobbles(size=1024, cells=10, base=(78, 70, 62), seed=1):
    r = np.random.default_rng(seed)
    cell = size / cells
    jit = r.uniform(-.35, .35, (cells, cells, 2))           # one stone per grid cell, jittered; the grid wraps (tiles)
    yy, xx = np.mgrid[0:size, 0:size]
    ci, cj = (xx / cell).astype(int), (yy / cell).astype(int)
    d1 = np.full((size, size), 1e9); d2 = np.full((size, size), 1e9); idx = np.zeros((size, size), int)
    for di in (-1, 0, 1):
        for dj in (-1, 0, 1):
            ni, nj = ci + di, cj + dj
            wi, wj = ni % cells, nj % cells
            px = (ni + .5 + jit[wi, wj, 0]) * cell; py = (nj + .5 + jit[wi, wj, 1]) * cell
            d = np.hypot(xx - px, yy - py)
            k = wi * cells + wj
            closer = d < d1
            d2 = np.where(closer, d1, np.minimum(d2, d)); idx = np.where(closer, k, idx); d1 = np.where(closer, d, d1)
    pts = np.zeros((cells * cells, 2))
    edge = d2 - d1                                          # distance to the mortar line
    tone = r.uniform(.72, 1.18, cells * cells)[idx]
    shade = np.clip(edge / 10, 0, 1) ** .5                  # rounded stone tops, dark bevel at the joints
    low = np.asarray(Image.fromarray((r.random((16, 16)) * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC)) / 255
    fine = np.asarray(Image.fromarray((r.random((size // 2, size // 2)) * 255).astype(np.uint8)).resize((size, size), Image.BILINEAR)) / 255
    img = np.zeros((size, size, 3))
    for c in range(3):
        img[..., c] = base[c] * tone * (.4 + .6 * shade) * (.8 + .35 * low) * (.93 + .14 * fine)
    img[edge < 2.4] *= .3                                   # mortar
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(.7))


cobbles().save(out / 'cobble_floor.png')


# ---- tiling wall stone: courses of cut blocks with chipped edges
def wall(size=1024, rows=8, base=(66, 60, 56)):
    img = Image.new('RGB', (size, size)); d = ImageDraw.Draw(img)
    h = size // rows
    for row in range(rows):
        off = (row % 2) * size // 6
        x = -off
        while x < size:
            w = int(rng.uniform(.22, .36) * size)
            t = rng.uniform(.75, 1.15)
            col = tuple(int(c * t) for c in base)
            for dx in (0, size, -size):
                d.rectangle((x + dx + 3, row * h + 3, x + dx + w - 3, row * h + h - 3), fill=col)
            x += w
    a = np.asarray(img).astype(float)
    noise = np.asarray(Image.fromarray((rng.random((size // 4, size // 4)) * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC)) / 255
    grain = np.asarray(Image.fromarray((rng.random((size, size)) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2))) / 255
    a *= (.7 + .45 * noise)[..., None] * (.85 + .3 * grain)[..., None]
    chip = np.asarray(Image.fromarray((rng.random((size // 24, size // 24)) * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC)).astype(float)
    chip = np.asarray(Image.fromarray(((chip > 225) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(3))) / 255
    a *= (1 - .45 * chip)[..., None]
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(.8))


wall().save(out / 'wall_stone.png')

# ---- the lair's floor circle: stone rings with engraved grooves, an eight-pointed star and rune marks, as in the lair
# picture (seen from above). Alpha fades its rim into the cobbles.
S = 2048
c = S / 2
yy, xx = np.mgrid[0:S, 0:S]
rad = np.hypot(xx - c, yy - c) / S                          # 0 at the centre, .5 at the rim
ang = (np.arctan2(yy - c, xx - c) + math.pi) / (2 * math.pi)
ring_w = .028
ring = (rad / ring_w).astype(int)
segs = np.maximum(6, (2 * math.pi * (ring + .5) * ring_w * 22).astype(int))   # blocks about as long as they are deep
seg = (ang * segs + (ring % 2) * .5).astype(int)
fr = rad / ring_w - ring; fs = ang * segs + (ring % 2) * .5 - seg
joint = np.minimum(np.minimum(fr, 1 - fr) * ring_w * S, np.minimum(fs, 1 - fs) / np.maximum(segs, 1) * 2 * math.pi * rad * S)
rs = np.random.default_rng(4)
tone = rs.uniform(.72, 1.15, 4000)[(ring * 97 + seg) % 4000]
low = np.asarray(Image.fromarray((rs.random((24, 24)) * 255).astype(np.uint8)).resize((S, S), Image.BICUBIC)) / 255
base = np.array([80, 72, 64])
img = base[None, None, :] * (tone * (.45 + .55 * np.clip(joint / 8, 0, 1) ** .5) * (.8 + .35 * low))[..., None]
img[joint < 2.5] *= .3
disc = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).convert('RGBA')
d = ImageDraw.Draw(disc)
groove = (24, 19, 16, 255); gold = (170, 120, 60, 255)
for rr_, wdt in ((.47, 16), (.42, 10), (.35, 18), (.31, 8), (.22, 16), (.19, 8), (.10, 12)):
    R = rr_ * S
    d.ellipse((c - R, c - R, c + R, c + R), outline=groove, width=wdt)
for k in range(8):
    a_ = k * math.pi / 4
    d.line((c + math.cos(a_) * .10 * S, c + math.sin(a_) * .10 * S, c + math.cos(a_) * .42 * S, c + math.sin(a_) * .42 * S), fill=groove, width=12)
for rot in (0, math.pi / 4):
    pts = [(c + math.cos(rot + k * math.pi / 2) * .31 * S, c + math.sin(rot + k * math.pi / 2) * .31 * S) for k in range(4)]
    d.polygon(pts, outline=groove, width=14)
for k in range(40):
    a_ = k * 2 * math.pi / 40
    x, y = c + math.cos(a_) * .385 * S, c + math.sin(a_) * .385 * S
    s_ = 20
    shape = k % 4
    if shape == 0: d.line((x - s_, y - s_, x + s_, y + s_), fill=gold, width=7); d.line((x - s_, y + s_, x + s_, y - s_), fill=gold, width=7)
    elif shape == 1: d.line((x, y - s_, x, y + s_), fill=gold, width=7); d.line((x - s_, y, x + s_, y - s_), fill=gold, width=7)
    elif shape == 2: d.ellipse((x - s_ * .7, y - s_ * .7, x + s_ * .7, y + s_ * .7), outline=gold, width=6)
    else: d.polygon([(x, y - s_), (x + s_, y + s_), (x - s_, y + s_)], outline=gold, width=6)
alpha = np.zeros((S, S), np.uint8)
alpha[:] = (np.clip((.5 - rad) / .03, 0, 1) * 255).astype(np.uint8)
disc.putalpha(Image.fromarray(alpha))
disc.save(out / 'rune_circle.png')
print('LAIR_TEXTURES', sorted(p.name for p in out.glob('*.png')))

# ---- small effect textures: a soft fog puff, a flame tongue, a chain link strip, a tattered banner
n = 256
yy, xx = np.mgrid[0:n, 0:n] / (n - 1) * 2 - 1
r2 = np.hypot(xx, yy)
puff = np.clip(1 - r2, 0, 1) ** 2
Image.fromarray(np.dstack([np.full((n, n), 255)] * 3 + [(puff * 255)]).astype(np.uint8), 'RGBA').save(out / 'fog_puff.png')
flame_a = np.clip(1 - np.hypot(xx * 1.6, (yy + .2) * .9), 0, 1) ** 1.5
core = np.clip(1 - np.hypot(xx * 3, (yy + .35) * 1.6), 0, 1)
fr = np.full((n, n), 255.); fg = 120 + 135 * core; fb = 30 + 170 * core ** 2
Image.fromarray(np.dstack([fr, fg, fb, flame_a * 255]).astype(np.uint8), 'RGBA').save(out / 'flame.png')
# chain: links along the strip (u along the chain), alternating face-on rings and edge-on bars
cw, ch = 256, 64
chain = Image.new('RGBA', (cw, ch), (0, 0, 0, 0)); d = ImageDraw.Draw(chain)
for i in range(4):
    x0 = i * 64
    if i % 2 == 0:
        d.ellipse((x0 + 2, 10, x0 + 78, 54), outline=(92, 84, 78, 255), width=11)
    else:
        d.rounded_rectangle((x0 - 6, 27, x0 + 70, 37), radius=5, fill=(78, 70, 64, 255))
chain.save(out / 'chain.png')
# banner: crimson cloth, gold cross with flared arms, darker folds, ragged bottom edge (alpha)
bw, bh = 512, 1024
ban = np.zeros((bh, bw, 4), np.uint8)
yy2, xx2 = np.mgrid[0:bh, 0:bw]
fold = .82 + .18 * np.sin(xx2 / bw * math.pi * 5) ** 2
cloth = np.asarray(Image.fromarray((rng.random((bh // 8, bw // 8)) * 255).astype(np.uint8)).resize((bw, bh), Image.BICUBIC)) / 255
for k, cval in enumerate((120, 18, 24)):
    ban[..., k] = np.clip(cval * fold * (.8 + .35 * cloth), 0, 255)
rag = (bh * .86 + 60 * np.sin(xx2[0] / 23.0) + rng.uniform(-40, 40, bw)).astype(int)
ban[..., 3] = 255
for x in range(bw):
    ban[rag[x]:, x, 3] = 0
holes = np.asarray(Image.fromarray((rng.random((bh // 16, bw // 16)) * 255).astype(np.uint8)).resize((bw, bh), Image.BICUBIC)) > 232
ban[holes & (yy2 > bh * .55), 3] = 0
bimg = Image.fromarray(ban, 'RGBA'); d = ImageDraw.Draw(bimg)
gold_c = (190, 140, 62, 255); cx, cy = bw // 2, int(bh * .38)
d.rectangle((cx - 16, cy - 230, cx + 16, cy + 250), fill=gold_c); d.rectangle((cx - 150, cy - 16, cx + 150, cy + 16), fill=gold_c)
for sx, sy in ((0, -1), (0, 1), (-1, 0), (1, 0)):          # flared arm ends
    ex, ey = cx + sx * (150 if sx else 0), cy + sy * (240 if sy else 0)
    if sx: d.polygon([(ex, ey), (ex + sx * 40, ey - 40), (ex + sx * 40, ey + 40)], fill=gold_c)
    else: d.polygon([(ex, ey), (ex - 40, ey + sy * 40), (ex + 40, ey + sy * 40)], fill=gold_c)
d.ellipse((cx - 34, cy - 34, cx + 34, cy + 34), outline=gold_c, width=10)
bimg.save(out / 'banner.png')
print('LAIR_EFFECTS', sorted(p.name for p in out.glob('*.png')))
