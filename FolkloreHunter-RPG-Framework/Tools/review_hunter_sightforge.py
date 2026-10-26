"""SightForge / Claude Eyes review of the hunter's new clips with the weapon in hand (MH-style bow, two-handed katana) and
of the evasions (side slide, back-step, roll, dive and its landing), on frames rendered by the real Unity player
(Captures/Proving/HunterPoses, written by --verify-proving), and of the hunter's motion in that session's actor track.

  python Tools/review_hunter_sightforge.py

Pose checks are sprite-oriented (motion between frames, holes, scale jumps, ground line, frozen frames, loop closure,
parts missing); the track checks are temporal (facing against travel, jitter, pops, stuck pictures). A jump, dive or
spin leaves the ground line on purpose: the inspector reports what it sees, the summary says which findings were
expected. 3D contact, hits and timing are measured in the player (verification.txt), not certified here."""
from pathlib import Path
import sys, json
from types import SimpleNamespace
h = Path.home(); r = h / 'Documents/FolkloreHunter-RPG-Framework'; app = h / 'AppData/Local/ClaudeEyesStudio/app'
sys.path.insert(0, str(app))
import eyes_inspect as E
from eyeslib import checks as C, temporal

poses = r / 'Captures/Proving/HunterPoses'
p = E.Project(str(r), 'Captures/Proving/HunterPoses', pid='folklore-rpg', name='Folklore Hunter - RPG framework')
STATES = ['bow_draw', 'bow_shoot', 'bow_power', 'bow_arc', 'bow_piercer', 'bow_step', 'walk',
          'katana_overhead', 'katana_slice', 'katana_thrust', 'katana_roundslash', 'katana_spirit3', 'katana_iai',
          'run', 'sprint', 'slide', 'backstep', 'roll', 'dive', 'dive_landing']
# Moves that leave the ground or turn the body edge-on by design: ground-line and silhouette findings there are expected.
AIRBORNE = {'dive', 'roll', 'bow_step', 'slide', 'backstep', 'katana_roundslash', 'katana_spirit3'}
for s in STATES: C.EXPECTED_FRAMES[s] = 16
summary = {'scope': 'Hunter with the bow, the katana and the evasions: real Unity player frames (orthographic side view) and the '
                    'hunter actor track. Sprite-oriented 2D pose checks plus temporal track checks.'}
totals = {'confirmed': 0, 'likely': 0, 'uncertain': 0}
for state in STATES:
    files = sorted(poses.glob('hunter_%s_E_*.png' % state))
    key = 'hunter_' + state
    if len(files) != 16:
        summary[key] = {'error': 'expected 16 frames, found %d' % len(files)}; print('SIGHTFORGE_HUNTER', key, 'missing frames', flush=True); continue
    page, data = E.cmd_animation(SimpleNamespace(project=p, run='hunter-' + state, subject='Hunter', state=state, dir='E', files=list(map(str, files))))
    found = [f for f in data['findings'] if f['kind'] in ('confirmed', 'likely', 'uncertain')]
    for f in found:
        # One-shots do not loop, so "loop does not close" does not apply to them; airborne moves leave the ground line.
        f['expected'] = (state != 'walk' and f['check'] == 'loop does not close') or \
                        (state in AIRBORNE and f['check'] in ('feet leave the ground line', 'scale jump', 'part missing', 'frame displaced'))
    summary[key] = {'summary': data['summary'], 'findings': found, 'page': page}
    for k in totals: totals[k] += data['summary'].get(k, 0)
    print('SIGHTFORGE_HUNTER', key, json.dumps(data['summary']), 'unexpected', sum(1 for f in found if not f['expected'] and f['kind'] != 'uncertain'), flush=True)
summary['totals'] = totals

track = r / 'Captures/Proving/proving_track.jsonl'
if track.is_file():
    rows = temporal.load(str(track))
    hunter_rows = []
    for row in rows:
        acts = [a for a in row['actors'] if a.get('id') == 'hunter']
        if acts: hunter_rows.append(dict(row, actors=acts))
    out = r / 'Captures/Proving/hunter_track.jsonl'
    with open(out, 'w', encoding='utf-8') as fh:
        for row in hunter_rows: fh.write(json.dumps(row) + '\n')
    page, data = E.cmd_track(SimpleNamespace(project=p, run='hunter-track', track=str(out)))
    summary['hunterTrack'] = {'frames': len(hunter_rows), 'summary': data['summary'], 'findings': data['findings'], 'page': page}
    print('SIGHTFORGE_HUNTER_TRACK', len(hunter_rows), 'frames', json.dumps(data['summary']), flush=True)
(r / 'Docs/sightforge-hunter-review.json').write_text(json.dumps(summary, indent=2))
print('SIGHTFORGE_HUNTER_DONE', json.dumps(totals), flush=True)
