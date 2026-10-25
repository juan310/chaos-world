"""Runs the installed SightForge / Claude Eyes inspector on actual Unity pose captures of the hunter.
The inspector is sprite-oriented: it reviews silhouettes frame to frame (motion, holes, frozen frames). It does not
certify 3D foot planting, collision or combat; those are covered by the in-player verifier."""
from pathlib import Path
import sys, json, shutil
from types import SimpleNamespace
h = Path.home(); r = h / 'Documents/FolkloreHunter-RPG-Framework'; app = h / 'AppData/Local/ClaudeEyesStudio/app'
sys.path.insert(0, str(app))
import eyes_inspect as E
from eyeslib import checks as C
poses = r / 'Captures/RPG/Poses'
p = E.Project(str(r), 'Captures/RPG/Poses', pid='folklore-rpg', name='Folklore Hunter - RPG framework')
for k in ['walk', 'idle', 'run', 'attack', 'chop', 'dodge']:
    C.EXPECTED_FRAMES[k] = 16
summary = {}
for state in ['idle', 'walk', 'run', 'attack', 'chop', 'dodge']:
    files = sorted(poses.glob('hunter_' + state + '_S_*.png'))
    if len(files) != 16:
        summary[state] = {'error': 'expected 16 frames, found %d' % len(files)}; continue
    page, data = E.cmd_animation(SimpleNamespace(project=p, run='rpg-' + state, subject='Hunter (Unity 3D)', state=state, dir='S', files=list(map(str, files))))
    summary[state] = {'summary': data['summary'], 'findings': data['findings'], 'page': page}
    print('SIGHTFORGE', state, json.dumps(data['summary']), flush=True)
# Negative control: a frozen walk built from one frame must be flagged by the same inspector.
neg = r / 'Docs/ClaudeEyes/Proposals/rpg-frozen-control'; neg.mkdir(parents=True, exist_ok=True)
files = []
for i in range(16):
    d = neg / ('frozen_walk_S_%02d.png' % i); shutil.copy2(poses / 'hunter_walk_S_00.png', d); files.append(str(d))
page, data = E.cmd_animation(SimpleNamespace(project=p, run='rpg-frozen-negative-control', subject='INTENTIONAL frozen control', state='walk', dir='S', files=files))
summary['negativeControl'] = {'summary': data['summary'], 'findings': data['findings']}
summary['negativeControlDetected'] = data['summary'].get('likely', 0) + data['summary'].get('confirmed', 0) > 0

# Temporal review of real play: the game writes the inspector's track format (MotionTrack.cs) during the gameplay
# recording and the real-input session. Facing against travel, walk-cycle stride, jitter, pops, pose flicker and
# "moving while the picture does not change" are checked on every recorded frame of the actual 3D game.
def track_review(path, run):
    page, data = E.cmd_track(SimpleNamespace(project=p, run=run, track=str(path)))
    rows = [json.loads(l) for l in open(path, encoding='utf-8') if l.strip()]
    met = data.get('meta', {}).get('metrics', {})
    out = {'summary': data['summary'], 'findings': data['findings'], 'page': page, 'frames': len(rows),
           'track': met.get('track'), 'hunter': met.get('actors', {}).get('hunter'),
           'expectations': [{k: e[k] for k in ('id', 'rule', 'subject', 'holds', 'framesChecked')} for e in met.get('expectations', [])]}
    print('SIGHTFORGE_TRACK', run, json.dumps(data['summary']), flush=True)
    return out, rows

tracks = {'tourTrack': r / 'Captures/Tour/tour_track.jsonl', 'realInputTrack': r / 'Captures/realinput_track.jsonl',
          'verifierTrack': r / 'Captures/RPG/verify_track.jsonl'}   # the verifier teleports a lot: jumps there are scripted
for key, path in tracks.items():
    if path.is_file():
        summary[key], rows = track_review(path, 'rpg-' + key)
        if key == 'tourTrack':
            # Negative control: the same real track with the hunter's walk frozen on one frame and drawn facing
            # reversed for every walking frame. The inspector must report both.
            flip = {'N': 'S', 'S': 'N', 'E': 'W', 'W': 'E'}
            ctl = neg.parent / 'rpg-track-negative-control.jsonl'
            with open(ctl, 'w', encoding='utf-8') as fh:
                for row in rows:
                    for a in row['actors']:
                        sp = a.get('sprite', '')
                        if a['id'] == 'hunter' and '_walk_' in sp:
                            d = sp.split('_walk_')[1][0]
                            a['sprite'] = 'hunter_walk_' + flip[d] + '_00'
                    fh.write(json.dumps(row) + '\n')
            page, data = E.cmd_track(SimpleNamespace(project=p, run='rpg-track-negative-control', track=str(ctl)))
            checks = sorted({f['check'] for f in data['findings'] if f['kind'] in ('likely', 'confirmed')})
            contra = [e['id'] for e in data.get('meta', {}).get('metrics', {}).get('expectations', []) if not e['holds'] and e['subject'] == 'hunter']
            summary['trackNegativeControl'] = {'summary': data['summary'], 'checksFlagged': checks, 'contradictedExpectations': contra}
            summary['trackNegativeControlDetected'] = 'facing against travel' in checks
            print('SIGHTFORGE_TRACK_NEGATIVE', checks, contra, flush=True)
    else:
        summary[key] = {'error': 'no track at ' + str(path)}
summary['scope'] = ('Sprite-oriented 2D review of 3D pose renders, plus temporal review of tracks recorded from the running 3D game '
                    '(x = world X, y = world Z, facing = rendered model forward quantised to S/E/N/W). '
                    'Does not certify 3D foot planting, collision or combat.')
(r / 'Docs/sightforge-review.json').write_text(json.dumps(summary, indent=2))
print('SIGHTFORGE_NEGATIVE_DETECTED', summary['negativeControlDetected'], flush=True)
print('SIGHTFORGE_REVIEW_DONE', flush=True)
