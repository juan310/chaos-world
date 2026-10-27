"""Claude Eyes / SightForge review of how the hunters handle the bow, on live shots from the real Unity player
(Captures/Proving/Bow, written by --verify-proving while the hunter really draws and releases at the archery range:
time held still for the shot, front, side and three-quarter views, each with the bow's and arrow's own points).

  python Tools/review_bow_sightforge.py

The hands and the face are found in the picture by SightForge's AI joint finder (RTMPose body model, Apache-2.0); the
palm is taken a little past the wrist along the forearm. Then, per picture:
  bow hand      the left palm on the bow's grip (not on a limb)
  upright       seen from the side, the bow stands within 35 degrees of upright
  string inward the string is on the archer's side of the bow (nearer the chest than the bow's grip is)
  arrow on      the nocked arrow's nock at the string, its tip out past the bow, across the bow (60-120 degrees)
  draw hand     drawing: the right palm on the string at the nock
  anchor        at full draw: the right palm at the face (within 25 cm of the nose or an eye)
Findings go to Docs/sightforge-bow-review.json with marked-up pictures in Captures/Proving/Bow/Marked: palms (red),
face (white), bow tips (blue), grip (yellow), string (green), arrow (orange). A joint the model is unsure of (score
under 0.35) makes its checks "uncertain"; a view where the bow is edge-on skips the checks that need its outline."""
from pathlib import Path
import sys, json, math
import numpy as np
from PIL import Image, ImageDraw

h = Path.home(); r = h / 'Documents/FolkloreHunter-RPG-Framework'
sys.path.insert(0, str(h / 'AppData/Local/ClaudeEyesStudio/app'))
from eyeslib.sprite import pose_ai

SRC = r / 'Captures/Proving/Bow'; OUT = SRC / 'Marked'; OUT.mkdir(parents=True, exist_ok=True)


def figure(rgb):
    flat = rgb.reshape(-1, 3).astype(np.int32)
    vals, counts = np.unique(flat, axis=0, return_counts=True); bg = vals[counts.argmax()]
    a = (np.abs(rgb.astype(np.int32) - bg).sum(-1) > 12).astype(np.uint8) * 255
    return np.dstack([rgb.astype(np.uint8), a])


def P(info, k): return np.array(info[k], float) if k in info else None
def dist(a, b): return float(np.hypot(*(a - b)))
def angle(u, v):
    c = abs(float(u @ v)) / max(1e-6, np.linalg.norm(u) * np.linalg.norm(v)); return math.degrees(math.acos(min(1, c)))


def review(png, info):
    rgb = np.asarray(Image.open(png).convert('RGB'))
    k = pose_ai.keypoints_tta(figure(rgb), 'body')
    ppm = float(info['pixelsPerMetre']); cm = lambda px: px / ppm * 100
    def palm(side):
        w = np.array(k[side + '_wrist'][:2]); e = np.array(k[side + '_elbow'][:2]); s = k[side + '_wrist'][2]
        return (w + (w - e) * .3 if k[side + '_elbow'][2] >= .2 else w), s
    # the picture's left/right can be swapped by the model: the palm nearer the bow grip is the bow hand
    pa, sa = palm('left'); pb, sb = palm('right')
    grip, tipA, tipB, smid = P(info, 'bowGrip'), P(info, 'bowTipA'), P(info, 'bowTipB'), P(info, 'stringMid')
    nock, atip, chest = P(info, 'arrowNock'), P(info, 'arrowTip'), P(info, 'chest')
    if grip is None: return {'checks': {}, 'note': 'no bow in view'}
    if dist(pb, grip) < dist(pa, grip): (pa, sa), (pb, sb) = (pb, sb), (pa, sa)
    bowL, bowLs, drawP, drawS = pa, sa, pb, sb
    blen = max(1.0, dist(tipA, tipB))
    face = [np.array(k[n][:2]) for n in ('nose', 'left_eye', 'right_eye') if k[n][2] >= .3]
    checks = {}
    def put(name, ok, why, unsure=False):
        checks[name] = {'verdict': 'uncertain' if unsure else ('ok' if ok else 'wrong'), 'why': why}
    edge_on = blen < .35 * ppm                                  # the bow seen end-on: its outline says little
    d = dist(bowL, grip)
    put('bow hand', d < .14 * blen + 6, 'left palm %.0f cm from the bow grip' % cm(d), bowLs < .35)
    if info['view'] == 'side' and not edge_on:
        up = angle(tipB - tipA, np.array([0.0, 1.0]))
        put('upright', up < 35, 'the bow leans %.0f degrees from upright' % up)
    if chest is not None and not edge_on:
        put('string inward', dist(smid, chest) < dist(grip, chest) + 2,
            'string %.0f cm from the chest, grip %.0f cm' % (cm(dist(smid, chest)), cm(dist(grip, chest))))
    if nock is not None:
        dn = dist(nock, smid)
        out = chest is None or dist(atip, chest) > dist(grip, chest)
        cross = angle(atip - nock, tipB - tipA)
        put('arrow on', dn < .16 * blen + 6 and out and (edge_on or 60 <= cross <= 120),
            'nock %.0f cm from the string, tip %s past the bow, %.0f degrees to the bow' % (cm(dn), 'out' if out else 'NOT', cross))
        if info['moment'].startswith('draw'):
            dd = dist(drawP, nock)
            put('draw hand', dd < .16 * blen + 8, 'right palm %.0f cm from the nock' % cm(dd), drawS < .35)
    if info['moment'] == 'draw_full':
        if face:
            da = min(dist(drawP, f) for f in face)
            put('anchor', da < .25 * ppm, 'right palm %.0f cm from the face' % cm(da), drawS < .35)
        else:
            put('anchor', False, 'the face is not clear in this view', True)
    # marked-up picture
    im = Image.fromarray(rgb.copy()); dr = ImageDraw.Draw(im)
    def dot(p, col, rr=6):
        if p is not None: dr.ellipse([p[0] - rr, p[1] - rr, p[0] + rr, p[1] + rr], outline=col, width=3)
    dr.line([tuple(tipA), tuple(tipB)], fill=(80, 140, 255), width=2); dot(tipA, (80, 140, 255)); dot(tipB, (80, 140, 255))
    dot(grip, (255, 220, 40)); dot(smid, (60, 230, 90))
    if nock is not None: dr.line([tuple(nock), tuple(atip)], fill=(255, 150, 40), width=3)
    dot(bowL, (255, 50, 50), 8); dot(drawP, (255, 50, 50), 8)
    for f in face: dot(f, (255, 255, 255), 4)
    dr.text((8, 8), '%s  %s  %s' % (Path(png).stem, info.get('clip', ''), 'ALL OK' if all(c['verdict'] == 'ok' for c in checks.values()) else ''), fill=(255, 255, 255))
    for i, (n, c) in enumerate(checks.items()):
        col = (140, 255, 140) if c['verdict'] == 'ok' else (255, 200, 120) if c['verdict'] == 'uncertain' else (255, 120, 120)
        dr.text((8, 24 + 14 * i), '%s: %s - %s' % (n, c['verdict'].upper(), c['why']), fill=col)
    im.save(OUT / (Path(png).stem + '_bow.png'))
    return {'checks': checks, 'clip': info.get('clip'), 'picture': str(OUT / (Path(png).stem + '_bow.png'))}


summary = {'scope': 'The hunters drawing and releasing the bow at the range (live player shots): hands, bow, string, arrow '
                    'and face judged from the pictures with the bow\'s own points. 2D pictures: a hand hidden behind the body '
                    'or a bow seen edge-on gives "uncertain" or skips a check.', 'shots': {}}
totals = {'ok': 0, 'wrong': 0, 'uncertain': 0}
for js in sorted(SRC.glob('*.json')):
    png = js.with_suffix('.png')
    if not png.is_file(): continue
    try:
        res = review(png, json.loads(js.read_text()))
    except Exception as e:                                   # noqa: BLE001
        res = {'checks': {'review': {'verdict': 'uncertain', 'why': 'review failed: %s' % e}}}
    summary['shots'][png.stem] = res
    for n, c in res['checks'].items():
        totals[c['verdict']] += 1
        if c['verdict'] != 'ok': print('SIGHTFORGE_BOW', png.stem, n, c['verdict'].upper(), c['why'], flush=True)
    print('SIGHTFORGE_BOW_SHOT', png.stem, ' '.join('%s=%s' % (n, c['verdict']) for n, c in res['checks'].items()), flush=True)
summary['totals'] = totals
(r / 'Docs/sightforge-bow-review.json').write_text(json.dumps(summary, indent=2))
print('SIGHTFORGE_BOW_DONE', json.dumps(totals), flush=True)
