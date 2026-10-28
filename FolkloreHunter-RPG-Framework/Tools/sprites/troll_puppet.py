"""The troll sprite as cut-out animation: the owner's own side view (Docs/Concepts/Troll/troll-turnaround.png) cut into
parts, each part pinned to a 2D skeleton (shoulder, elbow, hip, knee, ankle) and moved with math: a sine walk cycle with
the lower foot held on the ground, breathing idle with hair sway, an eased overhead hammer-fist with a dust burst.
The art stays the sheet's own; hidden surface a moving part uncovers is filled from its surroundings.

  python Tools/sprites/troll_puppet.py <out folder> [sprite height px, default 256]
  python Tools/sprites/troll_puppet.py game Assets/Resources/Sprites/Troll      the game's frames and sprite.json

Writes per animation: a sheet (PNG, transparent), a GIF on a dark backdrop, and the frames."""
import math
import random
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SHEET = Path(__file__).resolve().parents[2] / 'Docs' / 'Concepts' / 'Troll' / 'troll-turnaround.png'
SIDE = (560, 0, 990, 1024)                      # the side view on the sheet (the troll faces left)
GROUND = 992                                    # its soles, in the side view's pixels
OX, OY, CW, CH = 260, 260, 760, 1290           # the frame: room round the figure for the overhead swing

# joints, in the side view's pixels
SHOULDER, ELBOW, PELVIS = (262, 200), (262, 438), (232, 470)
HIP, KNEE, ANKLE, BELT = (247, 495), (255, 690), (276, 888), (215, 452)
FAR = (14, -8)                                  # the far leg sits a little behind and above the near one

OFFSETS = [(0, 95), (0, 120), (25, 95), (-25, 100), (0, -95), (0, 150), (40, 140), (-40, 140), (0, 60), (0, -60)]
POLY = {
    'upperarm': [(200, 160), (240, 140), (300, 145), (325, 175), (320, 240), (305, 300), (305, 380), (300, 432), (285, 455),
                 (222, 455), (200, 420), (198, 300), (195, 240), (185, 200)],
    'forearm': [(198, 420), (302, 425), (291, 470), (283, 520), (273, 560), (251, 600), (245, 650), (240, 698), (200, 717),
                (140, 710), (110, 672), (106, 600), (128, 560), (158, 530), (168, 480), (188, 444)],
    'cloth': [(40, 445), (110, 424), (200, 434), (300, 424), (366, 440), (392, 520), (394, 640), (390, 785), (330, 790),
              (310, 722), (300, 692), (286, 662), (250, 652), (205, 657), (186, 702), (182, 802), (108, 803), (68, 788),
              (36, 700), (34, 560), (31, 470)],
    'shin': [(186, 640), (302, 640), (320, 700), (332, 760), (322, 820), (327, 868), (337, 900), (250, 908), (194, 898),
             (200, 820), (190, 740)],
    'foot': [(190, 872), (332, 868), (347, 935), (344, 994), (240, 1000), (118, 997), (56, 978), (58, 955), (98, 934),
             (160, 924), (190, 904)],
    'thigh': [(188, 462), (306, 462), (312, 560), (304, 662), (196, 662), (184, 560)],
}


# ------------------------------------------------------------------------------------------------- cutting the art
def load():
    im = Image.open(SHEET).convert('RGB').crop(SIDE)
    a = np.asarray(im).astype(np.float32)
    bg = np.median(np.concatenate([a[:6].reshape(-1, 3), a[:, :6].reshape(-1, 3), a[:, -6:].reshape(-1, 3)]), axis=0)
    dist = np.abs(a - bg).sum(2)
    alpha = np.clip((dist - 22) / 26, 0, 1)                                   # the paper, and the faint floor shadow, go
    alpha = np.asarray(Image.fromarray((alpha * 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(3))).astype(np.float32) / 255
    # the edge: the paper's colour unmixed from half-covered pixels, and the edge pulled in a pixel (no pale fringe)
    soft = (alpha > 0) & (alpha < 1)
    a[soft] = np.clip((a[soft] - (1 - alpha[soft, None]) * bg) / np.maximum(alpha[soft, None], .35), 0, 255)
    alpha = np.asarray(Image.fromarray((alpha * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(.7))).astype(np.float32) / 255
    return a, alpha


def mask(poly, feather=1.6, size=(430, 1024)):
    m = Image.new('L', size, 0); ImageDraw.Draw(m).polygon(poly, fill=255)
    return np.asarray(m.filter(ImageFilter.GaussianBlur(feather))).astype(np.float32) / 255


def fill(rgb, known):
    """what a moving part uncovers: colour spread in from the surroundings (blurred at growing radii), then grain"""
    out = rgb.copy(); k = known.astype(np.float32)
    for r in (2, 4, 8, 16, 32, 64):
        num = np.stack([np.asarray(Image.fromarray(np.clip(out[..., c] * k, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r))).astype(np.float32) for c in range(3)], 2)
        den = np.asarray(Image.fromarray((k * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r))).astype(np.float32)[..., None] / 255
        est = num / np.maximum(den, 1e-3)
        new = (k < .5) & (den[..., 0] > .02)
        out[new] = est[new]; k = np.where(new, 1.0, k)
    def blur(x, r): return np.stack([np.asarray(Image.fromarray(np.clip(x[..., c], 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r))).astype(np.float32) for c in range(3)], 2)
    detail = np.clip(rgb - blur(rgb, 5), -60, 60)                         # the art's fine detail (wrinkles, spots, hide grain)
    hole = ~known; got = np.zeros(hole.shape, bool); add = np.zeros_like(out)
    for dy, dx in OFFSETS:                                                # borrowed from the same material beside the hole
        sh_known = np.roll(np.roll(known, dy, 0), dx, 1); sh_det = np.roll(np.roll(detail, dy, 0), dx, 1)
        take = hole & ~got & sh_known
        add[take] = sh_det[take]; got |= take
    out[hole] = np.clip(out[hole] + add[hole] * .9, 0, 255)
    return out


def layers():
    rgb, alpha = load()
    m = {k: mask(p) for k, p in POLY.items()}
    arm = np.clip(m['upperarm'] + m['forearm'], 0, 1)
    L = {}
    def rgba(col, a): return Image.fromarray(np.dstack([np.clip(col, 0, 255), np.clip(a, 0, 1) * 255]).astype(np.uint8), 'RGBA')
    # the body above the belt (head, hair, chest, back): the arm's place filled in, the bottom hidden under the belt
    top = np.zeros_like(alpha); top[:478] = 1; top = np.asarray(Image.fromarray((top * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(3))).astype(np.float32) / 255
    known = (arm < .5) & (alpha > .5)
    body_rgb = fill(rgb, known)
    L['body'] = rgba(body_rgb, alpha * top)
    # the loincloth, the fist and wrap's place in it filled with hide
    cloth_known = (arm < .5) & (alpha > .5) & (m['cloth'] > .5)
    cloth_rgb = fill(rgb, cloth_known | ((m['cloth'] < .5) & (alpha > .5) & (arm < .5) & (np.arange(1024)[:, None] > 420)))
    L['cloth'] = rgba(cloth_rgb, np.where(arm > .5, m['cloth'], alpha * m['cloth']))
    L['upperarm'] = rgba(rgb, alpha * m['upperarm'] * (1 - m['forearm'] * (np.arange(1024)[:, None] > 440)))
    L['forearm'] = rgba(rgb, alpha * m['forearm'])
    L['shin'] = rgba(rgb, alpha * m['shin'])
    L['foot'] = rgba(rgb, alpha * m['foot'])
    # the thigh under the loincloth is never seen in the sheet: skin spread up from the knee, seen only in a stride
    shin_known = (m['shin'] > .5) & (alpha > .5) & (np.arange(1024)[:, None] > 700)
    thigh_rgb = fill(rgb, shin_known)
    L['thigh'] = rgba(thigh_rgb * .96, m['thigh'])
    for k in ('thigh', 'shin', 'foot'):                                     # the far leg: the same, in the body's shade
        im = L[k].copy(); a_ = np.asarray(im).astype(np.float32)
        a_[..., :3] *= np.array([.72, .74, .76]); L[k + '_far'] = Image.fromarray(a_.astype(np.uint8), 'RGBA')
    return L


# --------------------------------------------------------------------------------------------- the 2D skeleton
def R(deg, about):
    """visual counter-clockwise turn by deg about a point (y points down): a hanging limb turned + swings back (+x)"""
    a = math.radians(deg); c, s = math.cos(a), math.sin(a); x, y = about
    return np.array([[c, s, x - c * x - s * y], [-s, c, y + s * x - c * y], [0, 0, 1]])


def T(dx, dy): return np.array([[1, 0, dx], [0, 1, dy], [0, 0, 1.0]])
def S(k, about): x, y = about; return np.array([[1, 0, 0], [0, k, y - k * y], [0, 0, 1.0]])
def apply(M, p): return (M[0, 0] * p[0] + M[0, 1] * p[1] + M[0, 2], M[1, 0] * p[0] + M[1, 1] * p[1] + M[1, 2])


def place(canvas, layer, M):
    full = T(OX, OY) @ M
    inv = np.linalg.inv(full)
    part = layer.transform((CW, CH), Image.AFFINE, data=tuple(inv[:2].ravel()), resample=Image.BICUBIC)
    canvas.alpha_composite(part)


def sway_hair(img, amp, phase):
    """the mane down the back and the strands in front swing, more toward their ends"""
    if abs(amp) < .05: return img
    a = np.asarray(img).copy(); out = a.copy()
    for y in range(120, 470):
        k = min(1.0, (y - 120) / 260.0)
        d = int(round(amp * k * math.sin(phase - y / 90.0)))
        if d == 0: continue
        for x0, x1 in ((300, 430), (80, 160)):
            row = a[y, x0:x1]
            if d > 0: out[y, x0 + d:x1] = row[:-d]
            else: out[y, x0:x1 + d] = row[-d:]
    return Image.fromarray(out, 'RGBA')


def pose_frame(L, p, phase=0.0, dust=()):
    """p: dy (body drop), lean (deg, + forward), breathe, sh, el (arm, deg), cloth (deg), hair, and per leg n/f: th (thigh,
    deg, + forward), kn (knee bend, deg), ft (foot, deg)"""
    canvas = Image.new('RGBA', (CW, CH), (0, 0, 0, 0))
    root = T(p.get('dx', 0), p.get('dy', 0)) @ R(p.get('fall', 0), HEEL)   # fall: the whole body tips over its heel
    torso = root @ R(p['lean'], PELVIS) @ S(1 + p.get('breathe', 0) * .006, PELVIS)
    def leg(side):
        off = T(*FAR) if side == 'f' else np.eye(3)
        th = root @ off @ R(-p[side + 'th'], HIP)                         # forward swing turns the leg clockwise
        sh = th @ R(p[side + 'kn'], KNEE)                                 # the knee bends back
        ft = sh @ R(p.get(side + 'ft', 0), ANKLE)
        return th, sh, ft
    f_th, f_sh, f_ft = leg('f')
    n_th, n_sh, n_ft = leg('n')
    for nm, M in (('thigh_far', f_th), ('foot_far', f_ft), ('shin_far', f_sh)):
        place(canvas, L[nm], M)
    place(canvas, sway_hair(L['body'], p.get('hair', 0), phase), torso)
    for nm, M in (('thigh', n_th), ('foot', n_ft), ('shin', n_sh)):
        place(canvas, L[nm], M)
    cloth = root @ R(p.get('cloth', 0) + p['lean'] * .35, BELT)
    place(canvas, L['cloth'], cloth)
    ua = torso @ R(-p['sh'], SHOULDER)                                     # + raises the arm forward
    fa = ua @ R(-p['el'], ELBOW)
    place(canvas, L['upperarm'], ua)
    place(canvas, L['forearm'], fa)
    if dust:
        d = ImageDraw.Draw(canvas)
        for (x, y), col, r in dust:
            X, Y = x + OX, y + OY
            d.ellipse((X - r, Y - r, X + r, Y + r), fill=col)
    return canvas, dict(root=root, torso=torso, n_ft=n_ft, f_ft=f_ft, fa=fa)


SOLE = [(60, 975), (150, 996), (250, 998), (340, 990)]
HEEL = (335, 992)
KNEECAP = [(205, 700), (230, 735), (260, 745)]


def ground(L, p, phase=0.0):
    """drop or lift the body so the lower sole stands on the ground"""
    root = T(p.get('dx', 0), 0)
    low = -1e9
    for side in ('n', 'f'):
        off = T(*FAR) if side == 'f' else np.eye(3)
        sh = root @ off @ R(-p[side + 'th'], HIP) @ R(p[side + 'kn'], KNEE)
        ft = sh @ R(p.get(side + 'ft', 0), ANKLE)
        low = max(low, max(apply(ft, s)[1] for s in SOLE), max(apply(sh, s)[1] for s in KNEECAP))   # a kneel rests on its knees
    p['dy'] = GROUND - low
    return p


REST = dict(fall=0.0, dx=0.0, lean=0.0, sh=0.0, el=0.0, cloth=0.0, hair=0.0, breathe=0.0, nth=0.0, nkn=0.0, nft=0.0, fth=0.0, fkn=0.0, fft=0.0)


def idle(L, n=10):
    out = []
    for i in range(n):
        ph = 2 * math.pi * i / n; b = math.sin(ph)
        p = dict(REST, breathe=b, lean=.8 * b, sh=1.5 * math.sin(ph - .8), el=1.2 * math.sin(ph - 1.2), cloth=.6 * math.sin(ph - .5),
                 hair=2.0, nkn=1.0 + .8 * b, fkn=1.0 + .8 * b, nth=.4 * b, fth=.4 * b)
        out.append(pose_frame(L, ground(L, p), ph * 1.0))
    return out


def walk(L, n=14):
    out = []
    for i in range(n):
        ph = 2 * math.pi * i / n
        def side(o):
            s, c = math.sin(ph + o), math.cos(ph + o)
            th = 17 * s                                                    # + forward
            kn = 6 + 34 * max(0.0, c) ** 1.4                               # the swinging leg bends as it comes through
            ft = th - kn + (12 * c if c > 0 else 0)                        # flat on the ground in stance, toe down in swing
            return th, kn, ft
        nth, nkn, nft = side(0); fth, fkn, fft = side(math.pi)
        p = dict(REST, nth=nth, nkn=nkn, nft=nft, fth=fth, fkn=fkn, fft=fft,
                 lean=3 + 1.5 * math.sin(2 * ph), sh=-11 * math.sin(ph), el=4 + 4 * math.sin(ph - .6), cloth=-2.5 * math.sin(ph),
                 hair=4.0)
        out.append(pose_frame(L, ground(L, p), ph * 2))
    return out


def ease(t): return t * t * (3 - 2 * t)


def smash(L, n=16):
    keys = [(0.0, dict()),
            (.32, dict(lean=-9, sh=150, el=55, nkn=8, fkn=6, nth=-3, fth=4, cloth=3, hair=5)),
            (.44, dict(lean=-11, sh=162, el=62, nkn=10, fkn=8, nth=-4, fth=5, cloth=4, hair=6)),
            (.58, dict(lean=65, sh=60, el=-20, nth=55, nkn=105, fth=50, fkn=100, cloth=-8, hair=9)),
            (.76, dict(lean=60, sh=56, el=-16, nth=50, nkn=96, fth=45, fkn=92, cloth=-5, hair=6)),
            (1.0, dict())]
    out = []
    for i in range(n):
        t = i / (n - 1)
        k = max(j for j in range(len(keys) - 1) if keys[j][0] <= t)
        (t0, a), (t1, b) = keys[k], keys[k + 1]
        s = (t - t0) / (t1 - t0)
        s = s ** 2.4 if k == 2 else ease(s)                               # the blow accelerates; the rest ease in and out
        A, B = dict(REST, **a), dict(REST, **b)
        p = {q: A[q] + (B[q] - A[q]) * s for q in REST}
        p['nft'] = p['nth'] - p['nkn']                                    # the feet stay flat
        p['fft'] = p['fth'] - p['fkn']
        p = ground(L, p)
        dust = []
        if .56 <= t <= .9:
            fa = pose_frame(L, p, 0)[1]['fa']
            fist = apply(fa, (170, 690))
            rnd = random.Random(i); burst = (t - .56) / .34
            for _ in range(140):                                              # grit thrown out low along the ground
                ang = rnd.uniform(math.pi, 2 * math.pi); r = 10 + burst * rnd.uniform(30, 230)
                dust.append(((fist[0] + math.cos(ang) * r, GROUND - 4 + math.sin(ang) * r * .35),
                             rnd.choice([(150, 138, 112, 230), (122, 110, 88, 220), (186, 176, 150, 200)]), rnd.uniform(3, 10) * (1.3 - burst * .6)))
            if t < .66: p['dx'] = (6 if i % 2 else -6)                    # the ground shakes with the blow
        out.append(pose_frame(L, p, 0, dust))
    return out


def keyed(L, keys, n, fast=None, grounded=True, extra=None):
    """frames between key poses: eased, or accelerating into the key after segment `fast`"""
    out = []
    for i in range(n):
        t = i / (n - 1)
        k = max(j for j in range(len(keys) - 1) if keys[j][0] <= t)
        (t0, a), (t1, b) = keys[k], keys[k + 1]
        s_ = (t - t0) / (t1 - t0)
        s_ = s_ ** 2.2 if k == fast else ease(s_)
        A, B = dict(REST, **a), dict(REST, **b)
        p = {q: A[q] + (B[q] - A[q]) * s_ for q in A}
        p['nft'] = p['nth'] - p['nkn']; p['fft'] = p['fth'] - p['fkn']
        if grounded: p = ground(L, p)
        else: p['dy'] = ground(L, dict(p, fall=0.0))['dy']
        if extra: extra(p, t, i)
        out.append(pose_frame(L, p, t * 6))
    return out


def swipe(L, n=12):
    """a backhand: the arm drawn back across the body, then swept forward at chest height"""
    return keyed(L, [(0, dict()), (.38, dict(lean=-6, sh=-32, el=42, nth=-4, fth=6, hair=3, cloth=2)),
                     (.62, dict(lean=14, sh=96, el=6, nth=10, nkn=14, fth=-6, fkn=6, hair=6, cloth=-4)),
                     (1.0, dict())], n, fast=1)


def hit(L, n=8):
    """struck: rocks back, the arm thrown up, then settles"""
    return keyed(L, [(0, dict()), (.3, dict(lean=-11, sh=28, el=30, dx=14, nkn=12, fkn=10, hair=6, cloth=3)), (1.0, dict())], n)


def death(L, n=18):
    """staggers back, drops to its knees and slumps forward over them"""
    return keyed(L, [(0, dict()), (.22, dict(lean=-12, sh=30, el=40, dx=10, nkn=20, fkn=14, hair=5)),
                     (.7, dict(lean=58, sh=40, el=10, nth=62, nkn=128, fth=58, fkn=124, hair=8, cloth=-6)),
                     (.82, dict(lean=70, sh=52, el=4, nth=64, nkn=132, fth=60, fkn=128, hair=4, cloth=-4)),
                     (1.0, dict(lean=74, sh=56, el=2, nth=64, nkn=132, fth=60, fkn=128, hair=1, cloth=-3))], n, fast=1)


# ------------------------------------------------------------------------------------------- the game's sprite
GAME_CLIPS = [('Idle', idle, 9, True), ('Walking_A', walk, 14, True), ('Troll_Smash', smash, 14, False),
              ('Troll_Swipe', swipe, 15, False), ('Hit_A', hit, 16, False), ('Death_A', death, 14, False)]
JOINTS = {'hips': ('root', PELVIS), 'chest': ('torso', (205, 300)), 'head': ('torso', (85, 120)), 'hand.r': ('fa', (170, 650)),
          'foot.l': ('n_ft', (200, 960)), 'foot.r': ('f_ft', (200, 960))}
STAND_TOP = 22                                   # the top of the head standing, in the side view's pixels


def export_game(L, out, height_px=512):
    """frames for the game (Assets/Resources/Sprites/<name>/<clip>/NN.png, all in one frame box so the feet stay put) and
    sprite.json: frame size, the pivot (the ground under the hips), the standing height, and each clip's joints per frame
    in frame pixels (the game's hit zones and strike timing ride on them)"""
    import json
    clips = [(nm, fn(L), fps, loop) for nm, fn, fps, loop in GAME_CLIPS]
    bb = None
    for _, frames, _, _ in clips:
        for f, _ in frames:
            b = f.getbbox()
            bb = b if bb is None else (min(bb[0], b[0]), min(bb[1], b[1]), max(bb[2], b[2]), max(bb[3], b[3]))
    box = (bb[0] - 8, bb[1] - 8, bb[2] + 8, OY + GROUND + 10)
    k = height_px / float(GROUND - STAND_TOP)
    w, h = round((box[2] - box[0]) * k), round((box[3] - box[1]) * k)
    def px(p): return [round((p[0] + OX - box[0]) * k, 1), round((p[1] + OY - box[1]) * k, 1)]
    meta = {'frame': [w, h], 'pivot': px((HIP[0], GROUND)), 'standingHeight': height_px, 'facing': 'left', 'clips': {}}
    for nm, frames, fps, loop in clips:
        d = out / nm; d.mkdir(parents=True, exist_ok=True)
        joints = {j: [] for j in JOINTS}
        for i, (f, mats) in enumerate(frames):
            f.crop(box).resize((w, h), Image.LANCZOS).save(d / ('%02d.png' % i))
            for j, (m, pt) in JOINTS.items(): joints[j].append(px(apply(mats[m], pt)))
        meta['clips'][nm] = {'fps': fps, 'frames': len(frames), 'loop': loop, 'joints': joints}
        print('TROLL_GAME', nm, len(frames), flush=True)
    (out / 'sprite.json').write_text(json.dumps(meta, indent=1))
    return meta


def save(frames, out, name, height, ms):
    frames = [f[0] if isinstance(f, tuple) else f for f in frames]
    bb = None
    for f in frames:
        b = f.getbbox()
        bb = b if bb is None else (min(bb[0], b[0]), min(bb[1], b[1]), max(bb[2], b[2]), max(bb[3], b[3]))
    pad = 12
    box = (max(0, bb[0] - pad), max(0, bb[1] - pad), min(CW, bb[2] + pad), OY + GROUND + 14)
    k = height / (OY + GROUND + 14 - (OY - 10))                          # the troll's own height sets the scale
    w, h = round((box[2] - box[0]) * k), round((box[3] - box[1]) * k)
    small = [f.crop(box).resize((w, h), Image.LANCZOS) for f in frames]
    sheet = Image.new('RGBA', (w * len(small), h))
    for i, f in enumerate(small): sheet.alpha_composite(f, (i * w, 0))
    sheet.save(out / ('troll_%s_sheet.png' % name))
    fr = out / ('frames_' + name); fr.mkdir(exist_ok=True)
    gif = []
    for i, f in enumerate(small):
        f.save(fr / ('%02d.png' % i))
        bg = Image.new('RGBA', f.size, (52, 56, 62, 255)); bg.alpha_composite(f); gif.append(bg.convert('RGB'))
    gif[0].save(out / ('troll_%s.gif' % name), save_all=True, append_images=gif[1:], duration=ms, loop=0)
    return small


def main():
    if len(sys.argv) > 1 and sys.argv[1] == 'game':                    # troll_puppet.py game <Assets/Resources/Sprites/Troll>
        out = Path(sys.argv[2]); out.mkdir(parents=True, exist_ok=True)
        export_game(layers(), out)
        return
    out = Path(sys.argv[1] if len(sys.argv) > 1 else 'troll_puppet'); out.mkdir(parents=True, exist_ok=True)
    height = int(sys.argv[2]) if len(sys.argv) > 2 else 256
    L = layers()
    for name, fn, ms in (('idle', idle, 110), ('walk', walk, 85), ('smash', smash, 70)):
        save(fn(L), out, name, height, ms)
        print('TROLL_PUPPET', name, flush=True)


if __name__ == '__main__':
    main()
