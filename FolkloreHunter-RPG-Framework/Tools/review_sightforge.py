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
summary['scope'] = 'Sprite-oriented 2D review of 3D pose renders. Does not certify 3D foot planting, collision or combat.'
(r / 'Docs/sightforge-review.json').write_text(json.dumps(summary, indent=2))
print('SIGHTFORGE_NEGATIVE_DETECTED', summary['negativeControlDetected'], flush=True)
print('SIGHTFORGE_REVIEW_DONE', flush=True)
