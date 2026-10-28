# The MotionBricks lab, measuring side: scores every variant mb_lab.py generated against our own keyposed source, clip
# by clip, and draws them side by side, so each setting's effect is known by numbers and seen in pictures.
#
# All measured on the SOMA skeleton itself (forward kinematics of the frames, metres, Y up), before any retargeting:
#   smooth  the 95th percentile of the hands', feet's, hips' and head's acceleration (m/s^2): pops and jitter push it up
#   pops    frames where a joint's acceleration jumps more than POP m/s^2 from the frame before (a visible hitch)
#   keys    how far the joints are from our key poses at the key times (cm, mean): does the motion hit what we posed?
#   free    how far the joints are from our own in-betweens elsewhere (cm, mean): how much MotionBricks re-imagined
#   slide   how far a planted foot moves along the ground (cm per second) while MotionBricks says it is in contact
#           (and, for the source, while it is low and near-still, the same test for every variant: 'slide_low')
#   floor   the deepest any toe or heel goes under the floor (cm)
#   squash  the most a gap squeezed the generated motion (generated frames / frames it had to fill)
# The contact channels (four per frame) are matched to the heels and toes by which joint is low when the channel is on.
#
# Run from the MotionBricks checkout:  uv run python <this file> soma.json lab_out.json <out folder>
import itertools
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

POP = 60.0                      # m/s^2 change between neighbouring frames that reads as a hitch
JOINTS = ['Hips', 'Head', 'LeftHand', 'RightHand', 'LeftFoot', 'RightFoot', 'LeftToeBase', 'RightToeBase']
FEET = ['LeftFoot', 'LeftToeBase', 'RightFoot', 'RightToeBase']
BONES = [('Hips', 'Spine1'), ('Spine1', 'Chest'), ('Chest', 'Neck1'), ('Neck1', 'Head'), ('Head', 'HeadEnd')]
for S in ('Left', 'Right'):
    BONES += [('Chest', S + 'Shoulder'), (S + 'Shoulder', S + 'Arm'), (S + 'Arm', S + 'ForeArm'), (S + 'ForeArm', S + 'Hand'),
              (S + 'Hand', S + 'HandMiddle1'), ('Hips', S + 'Leg'), (S + 'Leg', S + 'Shin'), (S + 'Shin', S + 'Foot'),
              (S + 'Foot', S + 'ToeBase'), (S + 'ToeBase', S + 'ToeEnd')]


def qmul(a, b):
    """(x, y, z, w) quaternions, broadcast"""
    ax, ay, az, aw = np.moveaxis(a, -1, 0); bx, by, bz, bw = np.moveaxis(b, -1, 0)
    return np.stack([aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
                     aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz], -1)


def qrot(q, v):
    u = q[..., :3]; w = q[..., 3:]
    t = 2 * np.cross(u, v)
    return v + w * t + np.cross(u, t)


def fk(sk, frames):
    """[frames, joints, 7] local (position, quaternion xyzw) -> [frames, joints, 3] world positions"""
    f = np.asarray(frames, dtype=np.float64)
    n = len(sk['names'])
    gp = np.zeros((len(f), n, 3)); gr = np.zeros((len(f), n, 4))
    for i, j in enumerate(sk['parents']):
        p, q = f[:, i, :3], f[:, i, 3:] / np.linalg.norm(f[:, i, 3:], axis=-1, keepdims=True)
        if j < 0: gp[:, i], gr[:, i] = p, q
        else: gp[:, i], gr[:, i] = gp[:, j] + qrot(gr[:, j], p), qmul(gr[:, j], q)
    return gp


def match_contacts(P, idx, contacts):
    """which joint each contact channel belongs to: the foot joint that is lowest (relative to its own range) when it is on"""
    c = np.asarray(contacts, dtype=np.float64)
    h = {k: P[:, idx[k], 1] for k in FEET}
    score = np.zeros((c.shape[1], len(FEET)))
    for ch in range(c.shape[1]):
        on = c[:, ch] > .5
        if on.sum() < 2 or (~on).sum() < 2: return [None] * c.shape[1]      # a channel never changes: nothing to tell
        score[ch] = [(h[k][~on].mean() - h[k][on].mean()) / (np.ptp(h[k]) + 1e-6) for k in FEET]
    best = max(itertools.permutations(range(len(FEET))), key=lambda p: sum(score[ch, j] for ch, j in enumerate(p)))
    return [FEET[j] for j in best]


def measure(sk, idx, clip, frames, contacts=None, order=None, floor=None):
    fps = clip['fps']
    P = fk(sk, frames)
    J = [idx[j] for j in JOINTS]
    A = np.linalg.norm(np.diff(P[:, J], 2, axis=0), axis=-1) * fps * fps        # [frames-2, joints]
    pops = int((np.abs(np.diff(A, axis=0)) > POP).any(axis=1).sum()) if len(A) > 1 else 0
    S = fk(sk, clip['source'])
    m = min(len(S), len(P))
    keys = [min(m - 1, int(round(t * fps))) for t in clip['keys']]
    err = np.linalg.norm(P[:m, J] - S[:m, J], axis=-1).mean(axis=1) * 100     # cm per frame
    mask = np.ones(m, bool); mask[keys] = False
    out = {'smooth': round(float(np.percentile(A, 95)), 1) if len(A) else 0.0, 'pops': pops,
           'keys': round(float(err[keys].mean()), 2), 'free': round(float(err[mask].mean()), 2) if mask.any() else 0.0,
           'floor': round(float(max(0.0, max(floor[k] - P[:, idx[k], 1].min() for k in FEET)) * 100), 1)}
    # foot slide: the horizontal speed of a foot joint over frames it is planted
    V = np.linalg.norm(np.diff(P[:, :, [0, 2]], axis=0), axis=-1) * fps * 100   # cm/s, [frames-1, joints]
    low = []
    for k in FEET:
        y = P[:, idx[k], 1]
        planted = (y[1:] < floor[k] + .04) & (y[:-1] < floor[k] + .04) & (V[:, idx[k]] < 40)
        low += list(V[planted, idx[k]])
    out['slide_low'] = round(float(np.mean(low)), 1) if low else 0.0
    if contacts is not None and order is not None:
        c = np.asarray(contacts)[:len(P)]
        sl = []
        for ch, k in enumerate(order):
            if k is None: continue
            on = (c[1:, ch] > .5) & (c[:-1, ch] > .5)
            sl += list(V[on, idx[k]])
        out['slide'] = round(float(np.mean(sl)), 1) if sl else 0.0
    return out, P


def draw(sk, idx, rows, path, every=3, w=150, h=190):
    """one row per variant (the source first), every `every`-th frame drawn as a stick figure, seen from the side the
    source travels across (a dodge to the left is drawn from the front, a lunge forward from the side)"""
    labels = list(rows)
    P0 = rows[labels[0]][:, idx['Hips']]
    ax = 0 if np.ptp(P0[:, 0]) > np.ptp(P0[:, 2]) else 2
    n = max(len(P) for P in rows.values())
    cols = list(range(0, n, every))
    allp = np.concatenate([P for P in rows.values()])
    lo, hi = allp[:, :, 1].min(), allp[:, :, 1].max()
    scale = (h - 30) / max(hi - lo, 1e-3)
    img = Image.new('RGB', (90 + w * len(cols), h * len(labels)), (30, 32, 38))
    d = ImageDraw.Draw(img)
    for r, lab in enumerate(labels):
        P = rows[lab]
        d.text((6, r * h + h // 2), lab, fill=(230, 230, 230))
        for c, f in enumerate(cols):
            if f >= len(P): continue
            ox = 90 + c * w + w // 2; oy = r * h + h - 12
            hz = P[f, idx['Hips'], ax]
            pt = lambda j: (ox + (P[f, idx[j], ax] - hz) * scale, oy - (P[f, idx[j], 1] - lo) * scale)
            d.line([(90 + c * w, oy), (90 + (c + 1) * w - 4, oy)], fill=(70, 70, 80))
            for a, b in BONES:
                if a in idx and b in idx:
                    left = 'Left' in a or 'Left' in b
                    d.line([pt(a), pt(b)], fill=(120, 190, 255) if left else (255, 170, 90) if 'Right' in a or 'Right' in b else (235, 235, 235), width=3)
            if r == 0: d.text((90 + c * w + 4, 2), str(f), fill=(150, 150, 150))
    img.save(path)


def main(soma_json, lab_json, out_dir):
    sk = json.load(open(soma_json))
    idx = {n: i for i, n in enumerate(sk['names'])}
    data = json.load(open(lab_json))
    out = Path(out_dir); out.mkdir(parents=True, exist_ok=True)
    rest = fk(sk, [[list(t) for t in sk['rest']]])[0]
    floor = {k: float(rest[idx[k], 1]) for k in FEET}           # each heel's and toe's height standing at rest is its floor
    report = {'variants': data['variants'], 'clips': [], 'contact_channels': None}
    for clip in data['clips']:
        src, P0 = measure(sk, idx, clip, clip['source'], floor=floor)
        row = {'name': clip['name'], 'frames': len(clip['source']), 'source': src, 'variants': {}}
        pics = {'source': P0}
        for v, r in clip['variants'].items():
            P = fk(sk, r['frames'])
            order = match_contacts(P, idx, r['contacts'])
            if report['contact_channels'] is None and all(order): report['contact_channels'] = order
            m, P = measure(sk, idx, clip, r['frames'], r['contacts'], report['contact_channels'] or order, floor)
            gens = [g for g in r['gaps'] if not g.get('kept')]
            m['squash'] = max([g['squash'] for g in gens], default=1.0)
            m['kept'] = sum(1 for g in r['gaps'] if g.get('kept'))
            row['variants'][v] = m
            pics[v] = P
        draw(sk, idx, pics, out / ('%s.png' % clip['name']))
        report['clips'].append(row)
        print('MB_EVAL', clip['name'], json.dumps({'source': src, **row['variants']}), flush=True)
    # each variant's average over the clips, and how often it was the best on each measure
    names = list(data['variants'])
    summary = {}
    for v in names:
        rows = [c['variants'][v] for c in report['clips'] if v in c['variants']]
        summary[v] = {k: round(float(np.mean([r[k] for r in rows])), 2) for k in ('smooth', 'pops', 'keys', 'free', 'slide', 'slide_low', 'floor', 'squash')}
    summary['source'] = {k: round(float(np.mean([c['source'][k] for c in report['clips']])), 2) for k in ('smooth', 'pops', 'keys', 'free', 'slide_low', 'floor')}
    report['summary'] = summary
    json.dump(report, open(out / 'lab_report.json', 'w'), indent=1)
    lines = ['| variant | smooth m/s2 | pops | keys cm | free cm | slide cm/s | slide (low) cm/s | floor cm | squash |', '|---|---|---|---|---|---|---|---|---|']
    for v, s in summary.items():
        lines.append('| %s | %s | %s | %s | %s | %s | %s | %s | %s |' % (v, s['smooth'], s['pops'], s['keys'], s['free'], s.get('slide', '-'),
                                                                       s['slide_low'], s['floor'], s.get('squash', '-')))
    (out / 'lab_summary.md').write_text('\n'.join(lines) + '\n\ncontact channels: %s\n' % report['contact_channels'], encoding='utf-8')
    print('\n'.join(lines))
    print('MB_EVAL_DONE contact channels', report['contact_channels'], flush=True)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2], sys.argv[3])
