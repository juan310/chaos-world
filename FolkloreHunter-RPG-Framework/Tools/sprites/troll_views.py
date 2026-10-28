"""The troll's front and back views (the owner's sheet), animated with the same moves and timing as its side view
(troll_puppet.py), so the game can show the view that matches its heading: coming at the camera (front), walking away
(back) or across (side), and switch views in the middle of a move without a jump.

Each view is cut into parts (body above the belt with head and hair, two arms, loincloth, two legs) and moved with math in
the picture's own plane: a leg stepping toward or away from the camera lifts and foreshortens while the body shifts its
weight over the planted foot; an arm raised overhead turns about the shoulder; a blow driven at the camera foreshortens
the arm and brings the fist closer.

  python Tools/sprites/troll_views.py <Assets/Resources/Sprites/Troll>      the game's front/ and back/ frames, merged into
                                                                           sprite.json beside the side view's
  python Tools/sprites/troll_views.py preview <folder>                     contact sheets to look at"""
import json
import math
import random
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, str(Path(__file__).resolve().parent))
import troll_puppet as TP                                   # noqa: E402  (shared: cutting, filling, the side's clip list)

SHEET = TP.SHEET


def mirror(poly, axis): return [(axis - x, y) for x, y in poly]


ARM_F = [(95, 180), (150, 195), (162, 260), (150, 330), (142, 400), (132, 470), (122, 540), (124, 600), (120, 692), (70, 690),
         (20, 668), (10, 600), (13, 540), (8, 470), (20, 400), (36, 320), (50, 250), (66, 205)]
LEG_F = [(118, 612), (242, 612), (252, 700), (242, 780), (227, 860), (217, 900), (222, 940), (218, 990), (50, 990), (50, 950),
         (88, 920), (118, 900), (113, 800), (103, 720), (108, 660)]
ARM_B = [(90, 180), (150, 195), (152, 260), (142, 330), (132, 400), (127, 470), (117, 540), (117, 600), (117, 684), (70, 684),
         (18, 662), (6, 600), (6, 530), (12, 470), (22, 400), (38, 320), (53, 250), (68, 200)]
LEG_B = [(108, 632), (237, 632), (242, 720), (234, 800), (227, 880), (217, 960), (214, 990), (55, 990), (55, 940), (98, 900),
         (118, 880), (113, 800), (103, 720)]
VIEWS = {
    'front': dict(box=(0, 0, 560, 1024), ground=984, top=22, axis=560, belt=478, pelvis=(280, 520),
                  arm_l=ARM_F, arm_r=mirror(ARM_F, 560), sh_l=(118, 236), sh_r=(442, 236),
                  leg_l=LEG_F, leg_r=mirror(LEG_F, 560), hip_l=(182, 560), hip_r=(378, 560),
                  cloth=[(112, 418), (280, 424), (448, 418), (452, 500), (452, 600), (442, 702), (400, 692), (332, 702), (312, 822),
                         (268, 822), (248, 702), (168, 702), (118, 702), (108, 600), (110, 500)],
                  smash='r', swipe='l', head=(280, 120), chest=(280, 300)),
    'back': dict(box=(985, 0, 1536, 1024), ground=984, top=22, axis=550, belt=470, pelvis=(275, 520),
                 arm_l=ARM_B, arm_r=mirror(ARM_B, 550), sh_l=(115, 236), sh_r=(435, 236),
                 leg_l=LEG_B, leg_r=mirror(LEG_B, 550), hip_l=(175, 560), hip_r=(375, 560),
                 cloth=[(112, 408), (275, 398), (440, 408), (452, 500), (456, 640), (446, 702), (400, 702), (332, 722), (302, 832),
                        (268, 832), (248, 722), (160, 702), (108, 702), (103, 600), (108, 500)],
                 smash='l', swipe='r', head=(275, 110), chest=(275, 300)),
}
PAD = (220, 230)                                             # room round the figure (sides, top) for raised arms


def load(box):
    im = Image.open(SHEET).convert('RGB').crop(box)
    a = np.asarray(im).astype(np.float32)
    bg = np.median(np.concatenate([a[:6].reshape(-1, 3), a[:, :6].reshape(-1, 3), a[:, -6:].reshape(-1, 3)]), axis=0)
    alpha = np.clip((np.abs(a - bg).sum(2) - 22) / 26, 0, 1)
    alpha = np.asarray(Image.fromarray((alpha * 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(3))).astype(np.float32) / 255
    soft = (alpha > 0) & (alpha < 1)
    a[soft] = np.clip((a[soft] - (1 - alpha[soft, None]) * bg) / np.maximum(alpha[soft, None], .35), 0, 255)
    alpha = np.asarray(Image.fromarray((alpha * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(.7))).astype(np.float32) / 255
    return a, alpha


def layers(v):
    rgb, alpha = load(v['box'])
    size = (rgb.shape[1], rgb.shape[0])
    m = {k: TP.mask(v[k], size=size) for k in ('arm_l', 'arm_r', 'leg_l', 'leg_r', 'cloth')}
    arms = np.clip(m['arm_l'] + m['arm_r'], 0, 1)
    rows = np.arange(rgb.shape[0])[:, None]
    def rgba(col, a): return Image.fromarray(np.dstack([np.clip(col, 0, 255), np.clip(a, 0, 1) * 255]).astype(np.uint8), 'RGBA')
    top = np.asarray(Image.fromarray(((rows < v['belt']) * np.ones_like(alpha) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(3))).astype(np.float32) / 255
    body_rgb = TP.fill(rgb, (arms < .5) & (alpha > .5))
    L = {'body': rgba(body_rgb, alpha * top * np.where(rows > 300, 1 - arms, 1))}   # the shoulder caps stay with the body
    cloth_rgb = TP.fill(rgb, (arms < .5) & (alpha > .5) & (m['cloth'] > .5))
    L['cloth'] = rgba(cloth_rgb, alpha * m['cloth'] * (1 - arms) + m['cloth'] * arms * (alpha > .1))
    for s in 'lr':
        L['arm_' + s] = rgba(rgb, alpha * m['arm_' + s])
        L['leg_' + s] = rgba(rgb, alpha * m['leg_' + s] * (1 - m['cloth'] * (rows < 700)))
    return L


def R(deg, about): return TP.R(deg, about)
T = TP.T


def Sxy(kx, ky, about):
    x, y = about
    return np.array([[kx, 0, x - kx * x], [0, ky, y - ky * y], [0, 0, 1.0]])


REST = dict(dx=0.0, dy=0.0, tilt=0.0, breathe=0.0, crouch=0.0, sway=0.0, hair=0.0,
            lift_l=0.0, lift_r=0.0, kneel=0.0,
            arm_l=0.0, arm_r=0.0, fore_l=0.0, fore_r=0.0)


def pose(v, L, p, dust=(), shake=0.0):
    ox, oy = PAD
    W, H = v['box'][2] - v['box'][0] + 2 * ox, v['box'][3] - v['box'][1] + oy + 20
    canvas = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    root = T(p['dx'] + shake, p['dy'])
    pel = v['pelvis']
    torso = root @ R(p['tilt'], pel) @ Sxy(1, (1 - .1 * p['crouch'] - .12 * p['kneel']) * (1 + .006 * p['breathe']), (pel[0], v['ground'] - 300))
    def place(layer, M):
        full = T(ox, oy) @ M
        inv = np.linalg.inv(full)
        canvas.alpha_composite(layer.transform((W, H), Image.AFFINE, data=tuple(inv[:2].ravel()), resample=Image.BICUBIC))
    legs = {}
    for s in 'lr':
        lift = p['lift_' + s]; hip = v['hip_' + s]
        k = (1 - .24 * lift) * (1 - .45 * p['kneel']) * (1 - .1 * p['crouch'])      # a leg toward or away from the camera, or bent, looks shorter
        legs[s] = root @ T(0, -LIFT * lift) @ Sxy(1 + .07 * lift, k, hip)
    arms = {}
    for s in 'lr':
        sh = v['sh_' + s]
        fore = p['fore_' + s]                                           # a blow driven at the camera: shorter arm, closer fist
        arms[s] = torso @ R(p['arm_' + s], sh) @ Sxy(1 + .18 * fore, 1 - .42 * fore, sh)
    cloth = root @ T(0, 0) @ Sxy(1, 1 - .08 * p['crouch'] - .15 * p['kneel'], (pel[0], v['belt'] - 60))
    shear = np.array([[1, p['sway'] / 300.0, -p['sway'] / 300.0 * v['belt']], [0, 1, 0], [0, 0, 1.0]])
    # back to front: legs, body, cloth, arms (front view: the arms hang beside the body and over the cloth's edges)
    for s in 'lr': place(L['leg_' + s], legs[s])
    place(L['body'], torso)
    place(L['cloth'], cloth @ shear)
    for s in 'lr': place(L['arm_' + s], arms[s])
    if dust:
        d = ImageDraw.Draw(canvas)
        for (x, y), col, r in dust: d.ellipse((x + ox - r, y + oy - r, x + ox + r, y + oy + r), fill=col)
    return canvas, dict(root=root, torso=torso, arm_l=arms['l'], arm_r=arms['r'], leg_l=legs['l'], leg_r=legs['r'])


FOOT_L_PTS = [(60, 980), (140, 990), (215, 988)]
LIFT = 70                                                   # how high a stepping foot comes off the ground (picture pixels)


def ground(v, p):
    """the lower foot on the ground"""
    low = -1e9
    for s in 'lr':
        lift = p['lift_' + s]; hip = v['hip_' + s]
        k = (1 - .24 * lift) * (1 - .45 * p['kneel']) * (1 - .1 * p['crouch'])
        M = T(p['dx'], 0) @ T(0, -LIFT * lift) @ Sxy(1 + .07 * lift, k, hip)
        pts = FOOT_L_PTS if s == 'l' else mirror(FOOT_L_PTS, v['axis'])
        low = max(low, max(TP.apply(M, q)[1] for q in pts))
    p['dy'] = v['ground'] - low
    return p


def keyed(v, L, keys, n, fast=None, extra=None):
    out = []
    for i in range(n):
        t = i / (n - 1)
        k = max(j for j in range(len(keys) - 1) if keys[j][0] <= t)
        (t0, a), (t1, b) = keys[k], keys[k + 1]
        s = (t - t0) / (t1 - t0)
        s = s ** 2.2 if k == fast else TP.ease(s)
        A, B = dict(REST, **a), dict(REST, **b)
        p = ground(v, {q: A[q] + (B[q] - A[q]) * s for q in REST})
        dust, shake = (extra(p, t, i) if extra else ((), 0.0))
        out.append(pose(v, L, p, dust, shake))
    return out


def idle(v, L, n=10):
    out = []
    for i in range(n):
        ph = 2 * math.pi * i / n; b = math.sin(ph)
        p = ground(v, dict(REST, breathe=b, tilt=.4 * b, arm_l=-1.2 * math.sin(ph - .8), arm_r=1.2 * math.sin(ph - .8), sway=1.5 * math.sin(ph - .5)))
        out.append(pose(v, L, p))
    return out


def walk(v, L, n=14):
    """toward or away from the camera: each leg lifts and foreshortens in turn, the weight moves over the planted foot"""
    out = []
    for i in range(n):
        ph = 2 * math.pi * i / n; s = math.sin(ph)
        p = dict(REST, lift_l=max(0.0, s) ** 1.1, lift_r=max(0.0, -s) ** 1.1,
                 dx=15 * s, tilt=-3.2 * s, arm_l=-6 * s, arm_r=-6 * s, fore_l=.3 * max(0.0, -s), fore_r=.3 * max(0.0, s), sway=-10 * s,
                 breathe=-1.5 * abs(s))
        out.append(pose(v, L, ground(v, p)))
    return out


def smash(v, L, n=16):
    a = 'arm_' + v['smash']; f = 'fore_' + v['smash']; sign = 1 if v['smash'] == 'r' else -1
    keys = [(0, dict()), (.32, {a: 160 * sign, 'breathe': 1.5, 'crouch': .1, 'tilt': -3 * sign}), (.44, {a: 172 * sign, 'breathe': 2, 'crouch': .12, 'tilt': -4 * sign}),
            (.58, {a: 14 * sign, f: 1.0, 'crouch': 1.0, 'tilt': 4 * sign, 'sway': 6}), (.76, {a: 12 * sign, f: .9, 'crouch': .9, 'tilt': 3 * sign, 'sway': 4}), (1.0, dict())]
    def extra(p, t, i):
        if not .56 <= t <= .9: return (), 0.0
        rnd = random.Random(i); burst = (t - .56) / .34; dust = []
        cx = v['axis'] / 2 + 120 * sign
        for _ in range(140):
            ang = rnd.uniform(math.pi, 2 * math.pi); r = 10 + burst * rnd.uniform(30, 230)
            dust.append(((cx + math.cos(ang) * r, v['ground'] - 4 + math.sin(ang) * r * .35),
                         rnd.choice([(150, 138, 112, 230), (122, 110, 88, 220), (186, 176, 150, 200)]), rnd.uniform(3, 10) * (1.3 - burst * .6)))
        return dust, (6 if i % 2 else -6) if t < .66 else 0.0
    return keyed(v, L, keys, n, fast=2, extra=extra)


def swipe(v, L, n=12):
    a = 'arm_' + v['swipe']; f = 'fore_' + v['swipe']; sign = 1 if v['swipe'] == 'r' else -1
    return keyed(v, L, [(0, dict()), (.38, {a: 70 * sign, 'tilt': 3 * sign}), (.62, {a: -38 * sign, f: .45, 'tilt': -4 * sign, 'sway': -4 * sign}), (1.0, dict())], n, fast=1)


def hit(v, L, n=8):
    return keyed(v, L, [(0, dict()), (.3, dict(arm_l=-18, arm_r=18, breathe=-2, tilt=3, dy=0, sway=4)), (1.0, dict())], n)


def death(v, L, n=18):
    return keyed(v, L, [(0, dict()), (.22, dict(arm_l=-16, arm_r=16, tilt=4, breathe=-2)), (.7, dict(kneel=1.0, crouch=.6, arm_l=6, arm_r=-6, tilt=-2)),
                        (.82, dict(kneel=1.05, crouch=.8, arm_l=4, arm_r=-4, tilt=-3)), (1.0, dict(kneel=1.05, crouch=.85, arm_l=3, arm_r=-3, tilt=-3))], n, fast=1)


CLIPS = {'Idle': idle, 'Walking_A': walk, 'Troll_Smash': smash, 'Troll_Swipe': swipe, 'Hit_A': hit, 'Death_A': death}


def render_view(name):
    v = VIEWS[name]; L = layers(v)
    return {nm: CLIPS[nm](v, L) for nm, _, _, _ in TP.GAME_CLIPS}


def export(out, height_px=512):
    out = Path(out)
    meta = json.loads((out / 'sprite.json').read_text())
    if 'views' not in meta:                                  # the side view's export: moved under side/ and views.side
        import shutil
        for nm in meta['clips']:
            if (out / nm).is_dir():
                (out / 'side').mkdir(exist_ok=True); shutil.move(str(out / nm), str(out / 'side' / nm))
        side = {k: meta[k] for k in ('frame', 'pivot', 'standingHeight', 'facing', 'clips')}
        meta = {'views': {'side': dict(side, folder='side/')}}
    side_clips = meta['views']['side']['clips']
    for name in ('front', 'back'):
        v = VIEWS[name]; clips = render_view(name)
        bb = None
        for frames in clips.values():
            for f, _ in frames:
                b = f.getbbox(); bb = b if bb is None else (min(bb[0], b[0]), min(bb[1], b[1]), max(bb[2], b[2]), max(bb[3], b[3]))
        box = (bb[0] - 8, bb[1] - 8, bb[2] + 8, PAD[1] + v['ground'] + 10)
        k = height_px / float(v['ground'] - v['top'])
        w, h = round((box[2] - box[0]) * k), round((box[3] - box[1]) * k)
        vm = {'frame': [w, h], 'pivot': [round((v['axis'] / 2 + PAD[0] - box[0]) * k, 1), round((v['ground'] + PAD[1] - box[1]) * k, 1)],
              'standingHeight': height_px, 'facing': name, 'folder': name + '/', 'clips': {}}
        for nm, frames in clips.items():
            d = out / name / nm; d.mkdir(parents=True, exist_ok=True)
            for i, (f, _) in enumerate(frames): f.crop(box).resize((w, h), Image.LANCZOS).save(d / ('%02d.png' % i))
            sc = side_clips[nm]
            assert len(frames) == sc['frames'], (name, nm, len(frames), sc['frames'])
            vm['clips'][nm] = {'fps': sc['fps'], 'frames': len(frames), 'loop': sc['loop']}
            print('TROLL_VIEW', name, nm, len(frames), flush=True)
        meta['views'][name] = vm
    (out / 'sprite.json').write_text(json.dumps(meta, indent=1))


def preview(out):
    out = Path(out); out.mkdir(parents=True, exist_ok=True)
    for name in ('front', 'back'):
        clips = render_view(name)
        for nm, frames in clips.items():
            pick = frames[::max(1, len(frames) // 6)][:7]
            w, h = pick[0][0].size
            row = Image.new('RGBA', (w * len(pick), h), (52, 56, 62, 255))
            for i, (f, _) in enumerate(pick): row.alpha_composite(f, (i * w, 0))
            row.convert('RGB').resize((row.width // 3, row.height // 3)).save(out / ('%s_%s.png' % (name, nm)))
        print('TROLL_VIEW_PREVIEW', name, flush=True)


if __name__ == '__main__':
    if sys.argv[1] == 'preview': preview(sys.argv[2])
    else: export(sys.argv[1])
