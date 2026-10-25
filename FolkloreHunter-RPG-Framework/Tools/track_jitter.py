"""Frames where an actor leaves its own path while moving steadily (the SightForge jitter measure), with context:
python Tools/track_jitter.py <track.jsonl> [actor] [px threshold]"""
import json, math, sys
rows = []
for l in open(sys.argv[1], encoding='utf-8'):
    try: rows.append(json.loads(l))
    except ValueError: pass
who = sys.argv[2] if len(sys.argv) > 2 else 'hunter'
thr = float(sys.argv[3]) if len(sys.argv) > 3 else 3
ppu = 1080 / (2 * 3.46)
ss = [(r, a) for r in rows for a in r['actors'] if a['id'] == who]
for k in range(1, len(ss) - 1):
    (r0, a0), (r1, a1), (r2, a2) = ss[k - 1], ss[k], ss[k + 1]
    dt0, dt1 = r1['t'] - r0['t'], r2['t'] - r1['t']
    if dt0 <= 0 or dt1 <= 0: continue
    w = dt0 / (dt0 + dt1)
    ex, ey = a0['x'] + (a2['x'] - a0['x']) * w, a0['y'] + (a2['y'] - a0['y']) * w
    v0 = math.hypot(a1['x'] - a0['x'], a1['y'] - a0['y']) / dt0; v1 = math.hypot(a2['x'] - a1['x'], a2['y'] - a1['y']) / dt1
    steady = v0 > .3 and v1 > .3 and abs(v1 - v0) < .25 * max(v0, v1)
    off = math.hypot(a1['x'] - ex, a1['y'] - ey) * ppu
    if steady and off > thr:
        print('f%5d t%7.3f dt %.4f/%.4f v %.2f/%.2f off %.1f px  %s  ui=%s' % (r1['frame'], r1['t'], dt0, dt1, v0, v1, off, a1['sprite'], r1['ui']))
