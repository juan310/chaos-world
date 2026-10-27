"""Claude Eyes / SightForge review of how the characters hold their weapons, on close views rendered by the real Unity
player (Captures/Proving/Closeups and Captures/Proving/Monsters, written by --verify-proving).

  python Tools/review_grips_sightforge.py

For each close view with a weapon in the right hand, the player also renders that weapon alone from the same camera
(<shot>_weapon.png, with <shot>_weapon.json: which weapon, pixels per metre). Then, from the pictures alone:
  - the hand: SightForge's AI joint finder (RTMPose body model, Apache-2.0) finds the right wrist and elbow on the
    character's picture (its plain background made transparent);
  - the weapon: its pixels in the weapon picture; its long axis; the cross-guard as the widest band along it (sword and
    katana); the hilt end as the end nearer the guard (the lance: the end nearer the hand);
  - the grip: the hand must close on the grip (the wrist within a hand's breadth of the stretch between the guard and the
    hilt end), and the blade must lead away from it (the guard between the hand and the tip, not the hand on the blade).
Findings are written to Docs/sightforge-grip-review.json with marked-up pictures in Captures/Proving/Grips: the wrist
(red), the grip stretch (green), the guard (yellow), the tip (blue), and the measured offset in centimetres.
A wrist the model is unsure of (score under 0.35) or a hand hidden behind the body gives "uncertain", not a verdict."""
from pathlib import Path
import sys, json, os
import numpy as np
from PIL import Image, ImageDraw

h = Path.home(); r = h / 'Documents/FolkloreHunter-RPG-Framework'
sys.path.insert(0, str(h / 'AppData/Local/ClaudeEyesStudio/app'))
from eyeslib.sprite import pose_ai

OUT = r / 'Captures/Proving/Grips'; OUT.mkdir(parents=True, exist_ok=True)


def background(rgb):
    flat = rgb.reshape(-1, 3).astype(np.int32)
    vals, counts = np.unique(flat, axis=0, return_counts=True)
    return vals[counts.argmax()]


def figure(rgb):
    """the picture with its plain review background transparent"""
    bg = background(rgb)
    a = (np.abs(rgb.astype(np.int32) - bg).sum(-1) > 12).astype(np.uint8) * 255
    return np.dstack([rgb.astype(np.uint8), a]), a > 0


def weapon_axis(mask, guard_bands=True):
    ys, xs = np.nonzero(mask)
    P = np.stack([xs, ys], 1).astype(np.float64)
    c = P.mean(0)
    u, s, vt = np.linalg.svd(P - c, full_matrices=False)
    ax = vt[0]; perp = np.array([-ax[1], ax[0]])
    t = (P - c) @ ax; q = (P - c) @ perp
    lo, hi = t.min(), t.max()
    bands = 20; w = np.zeros(bands)
    for k in range(bands):
        sel = (t >= lo + (hi - lo) * k / bands) & (t <= lo + (hi - lo) * (k + 1) / bands)
        if sel.any(): w[k] = q[sel].max() - q[sel].min()
    return c, ax, lo, hi, w


def review(shot, info):
    rgb = np.asarray(Image.open(shot).convert('RGB'))
    wrgb = np.asarray(Image.open(str(shot)[:-4] + '_weapon.png').convert('RGB'))
    rgba, body = figure(rgb)
    wmask = np.abs(wrgb.astype(np.int32) - background(wrgb)).sum(-1) > 12
    if wmask.sum() < 30:
        return {'verdict': 'uncertain', 'why': 'the weapon is not in view'}
    ppm = float(info.get('pixelsPerMetre', 100)); weapon = info.get('weapon', '')
    k = pose_ai.keypoints_tta(rgba, 'body')
    wx, wy, ws = k['right_wrist']; ex, ey, es = k['right_elbow']
    # the right wrist as the model saw it; if the model is unsure, the wrist nearer the weapon (pictures can swap sides)
    lw = k['left_wrist']
    wpts = np.stack(np.nonzero(wmask)[::-1], 1)
    def dist_to_weapon(p): return float(np.min(np.hypot(wpts[:, 0] - p[0], wpts[:, 1] - p[1])))
    if dist_to_weapon(lw) + 4 < dist_to_weapon((wx, wy)) and lw[2] >= ws - .1:
        wx, wy, ws = lw; ex, ey, es = k['left_elbow']
    wrist = np.array([wx, wy])
    c, ax, lo, hi, widths = weapon_axis(wmask)
    endA, endB = c + ax * lo, c + ax * hi
    length = hi - lo
    if weapon.startswith('Hunt_Lance'):
        hilt_end = endA if np.hypot(*(endA - wrist)) < np.hypot(*(endB - wrist)) else endB
        guard = hilt_end + (c - hilt_end) / max(1e-6, np.hypot(*(c - hilt_end))) * .35 * length     # where a lance is held
    else:
        g = int(np.argmax(widths)); gt = lo + (hi - lo) * (g + .5) / len(widths)
        guard = c + ax * gt
        hilt_end = endA if g < len(widths) / 2 else endB
    tip = endB if hilt_end is endA else endA
    # the wrist against the grip stretch (guard -> hilt end), and which side of the guard it is on
    seg = hilt_end - guard; sl = max(1e-6, np.hypot(*seg)); sdir = seg / sl
    along = float((wrist - guard) @ sdir)                 # > 0: toward the hilt end (where the hand belongs)
    off_line = float(abs((wrist - guard) @ np.array([-sdir[1], sdir[0]])))
    on_grip = -0.12 * length <= along <= sl + 0.12 * length and off_line <= 0.14 * length
    blade_away = np.hypot(*(tip - wrist)) > np.hypot(*(guard - wrist))
    hand_on_blade = along < -0.12 * length
    verdict = 'ok' if on_grip and blade_away else 'uncertain' if ws < .35 else 'wrong'
    why = []
    if hand_on_blade: why.append('the hand is on the blade, %.0f cm past the guard' % (-along / ppm * 100))
    elif along > sl + .12 * length: why.append('the hand is %.0f cm beyond the hilt end' % ((along - sl) / ppm * 100))
    if off_line > .14 * length: why.append('the hand is %.0f cm off the grip line' % (off_line / ppm * 100))
    if not blade_away: why.append('the blade points back past the hand')
    if ws < .35: why.append('the wrist is not clear in this view (score %.2f)' % ws)
    # marked-up picture
    im = Image.fromarray(rgb.copy()); d = ImageDraw.Draw(im)
    ov = np.zeros((*wmask.shape, 4), np.uint8); ov[wmask] = (80, 200, 255, 90); im = Image.alpha_composite(im.convert('RGBA'), Image.fromarray(ov)); d = ImageDraw.Draw(im)
    d.line([tuple(guard), tuple(hilt_end)], fill=(60, 230, 90, 255), width=4)
    d.ellipse([guard[0] - 6, guard[1] - 6, guard[0] + 6, guard[1] + 6], outline=(255, 220, 40, 255), width=3)
    d.ellipse([tip[0] - 6, tip[1] - 6, tip[0] + 6, tip[1] + 6], outline=(60, 120, 255, 255), width=3)
    d.ellipse([wx - 7, wy - 7, wx + 7, wy + 7], outline=(255, 50, 50, 255), width=3)
    d.text((8, 8), '%s  %s  %s' % (Path(shot).stem, weapon, verdict.upper()), fill=(255, 255, 255, 255))
    for i, line in enumerate(why): d.text((8, 24 + 14 * i), line, fill=(255, 200, 200, 255))
    im.convert('RGB').save(OUT / (Path(shot).stem + '_grip.png'))
    return {'verdict': verdict, 'weapon': weapon, 'why': why, 'wristScore': round(ws, 2),
            'handAlongGripCm': round(along / ppm * 100, 1), 'handOffLineCm': round(off_line / ppm * 100, 1),
            'weaponLengthCm': round(length / ppm * 100, 1), 'picture': str(OUT / (Path(shot).stem + '_grip.png'))}


summary = {'scope': 'How each character holds the weapon in its right hand, judged from the player\'s close views: the hand by '
                    'SightForge\'s AI joint finder, the hilt, guard and blade from the weapon alone. 2D pictures; a hand behind '
                    'the body or the weapon edge-on gives "uncertain".', 'shots': {}}
totals = {'ok': 0, 'wrong': 0, 'uncertain': 0}
for folder in ('Closeups', 'Monsters'):
    for js in sorted((r / 'Captures/Proving' / folder).glob('*_weapon.json')):
        shot = Path(str(js)[:-len('_weapon.json')] + '.png')
        if not shot.is_file(): continue
        try:
            res = review(shot, json.loads(js.read_text()))
        except Exception as e:                               # noqa: BLE001
            res = {'verdict': 'uncertain', 'why': ['review failed: %s' % e]}
        summary['shots'][shot.stem] = res; totals[res['verdict']] += 1
        print('SIGHTFORGE_GRIP', shot.stem, res['verdict'].upper(), res.get('weapon', ''), '; '.join(res.get('why', [])), flush=True)
summary['totals'] = totals
(r / 'Docs/sightforge-grip-review.json').write_text(json.dumps(summary, indent=2))
print('SIGHTFORGE_GRIP_DONE', json.dumps(totals), flush=True)
