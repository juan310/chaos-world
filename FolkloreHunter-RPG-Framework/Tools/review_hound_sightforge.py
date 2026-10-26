"""SightForge / Claude Eyes review of the Briarhound companion and mount (or the older Cu-sith, whichever the player rendered), on frames rendered by the real Unity player
(Captures/Proving/HoundPoses, written by --verify-proving) and on the hound's motion in that session's actor track.

  python Tools/review_hound_sightforge.py

Pose checks (sprite-oriented): motion between frames, holes, scale jumps, ground line, frozen frames, loop closure,
parts missing. Track checks (temporal): facing against travel, stride per walk cycle, jitter, pops, pose flicker and
"moving while the picture does not change". A frozen gallop is fed in as a negative control.
3D foot planting and the rider's seat are measured in the player (verification.txt), not certified here."""
from pathlib import Path
import sys, json, shutil
from types import SimpleNamespace
h = Path.home(); r = h / 'Documents/FolkloreHunter-RPG-Framework'; app = h / 'AppData/Local/ClaudeEyesStudio/app'
sys.path.insert(0, str(app))
import eyes_inspect as E
from eyeslib import checks as C, temporal

poses = r / 'Captures/Proving/HoundPoses'
p = E.Project(str(r), 'Captures/Proving/HoundPoses', pid='folklore-rpg', name='Folklore Hunter - RPG framework')
hound = 'briarhound' if list(poses.glob('briarhound_idle_E_*.png')) else 'cusith'
NAME = {'briarhound': 'Briarhound', 'cusith': 'Cu-sith'}[hound]
clips = [(hound, s) for s in ('idle', 'walk', 'run', 'attack', 'sit', 'howl', 'hit')] + [('riding', 'run')]
for _, state in clips:
    C.EXPECTED_FRAMES[state] = 16
summary = {'scope': NAME + ' and the hunter riding it: real Unity player frames (orthographic side view) and the hound actor track. '
                    'Sprite-oriented 2D pose checks plus temporal track checks; 3D foot planting and seat measured in-engine.'}
for who, state in clips:
    files = sorted(poses.glob('%s_%s_E_*.png' % (who, state)))
    key = '%s_%s' % (who, state)
    if len(files) != 16:
        summary[key] = {'error': 'expected 16 frames, found %d' % len(files)}; print('SIGHTFORGE_HOUND', key, 'missing frames', flush=True); continue
    page, data = E.cmd_animation(SimpleNamespace(project=p, run='hound-' + key, subject=NAME if who == hound else 'Hunter riding the ' + NAME,
                                                 state=state, dir='E', files=list(map(str, files))))
    summary[key] = {'summary': data['summary'], 'findings': [f for f in data['findings'] if f['kind'] in ('confirmed', 'likely', 'uncertain')], 'page': page}
    print('SIGHTFORGE_HOUND', key, json.dumps(data['summary']), flush=True)

# Negative control: the gallop frozen on one frame must be flagged by the same inspector.
neg = r / 'Docs/ClaudeEyes/Proposals/hound-frozen-control'; neg.mkdir(parents=True, exist_ok=True)
src = poses / (hound + '_run_E_00.png')
if src.is_file():
    files = []
    for i in range(16):
        d = neg / ('frozen_run_E_%02d.png' % i); shutil.copy2(src, d); files.append(str(d))
    page, data = E.cmd_animation(SimpleNamespace(project=p, run='hound-frozen-negative-control', subject='INTENTIONAL frozen gallop', state='run', dir='E', files=files))
    summary['negativeControl'] = {'summary': data['summary']}
    summary['negativeControlDetected'] = data['summary'].get('likely', 0) + data['summary'].get('confirmed', 0) > 0
    print('SIGHTFORGE_HOUND_NEGATIVE', summary['negativeControlDetected'], flush=True)

# The hound's motion in the real session: keep only its actor so findings are about the companion.
track = r / 'Captures/Proving/proving_track.jsonl'
if track.is_file():
    rows = temporal.load(str(track))
    hound_rows = []
    for row in rows:
        acts = [a for a in row['actors'] if a.get('id') == 'hound']
        if acts:
            hound_rows.append(dict(row, actors=acts))
    out = r / 'Captures/Proving/hound_track.jsonl'
    with open(out, 'w', encoding='utf-8') as fh:
        for row in hound_rows:
            fh.write(json.dumps(row) + '\n')
    page, data = E.cmd_track(SimpleNamespace(project=p, run='hound-track', track=str(out)))
    met = data.get('meta', {}).get('metrics', {})
    summary['houndTrack'] = {'frames': len(hound_rows), 'summary': data['summary'], 'findings': data['findings'], 'page': page,
                             'hound': met.get('actors', {}).get(NAME) or met.get('actors', {}).get(hound) or met.get('actors', {}).get('hound'),
                             'expectations': [{k: e[k] for k in ('id', 'rule', 'subject', 'holds', 'framesChecked')} for e in met.get('expectations', [])]}
    print('SIGHTFORGE_HOUND_TRACK', len(hound_rows), 'frames', json.dumps(data['summary']), flush=True)
(r / 'Docs/sightforge-hound-review.json').write_text(json.dumps(summary, indent=2))
print('SIGHTFORGE_HOUND_DONE', flush=True)
