"""Single-frame position pops (over 10 units/s, 0.15-3 units) per actor in a SightForge track, and the hunter at frames 180-192."""
import json, math, sys, collections
rows = []
for l in open(sys.argv[1], encoding='utf-8'):
    try: rows.append(json.loads(l))
    except ValueError: pass   # a line cut short when the game was stopped
last, pops = {}, collections.defaultdict(list)
for r in rows:
    for a in r['actors']:
        p = last.get(a['id'])
        if p and p[0]['map'] == r['map']:
            d = math.hypot(a['x'] - p[1]['x'], a['y'] - p[1]['y']); dt = r['t'] - p[0]['t']
            if .15 < d < 3 and dt > 0 and d / dt > 10: pops[a['id']].append((r['frame'], round(d, 2), p[1]['sprite'], a['sprite']))
        last[a['id']] = (r, a)
for k, v in pops.items():
    print(k, len(v), collections.Counter(x[3].rsplit('_', 1)[0] for x in v).most_common(4), v[:3])
for r in rows:
    if 180 <= r['frame'] <= 192:
        a = next(a for a in r['actors'] if a['id'] == 'hunter'); print(r['frame'], round(r['t'], 3), r['ui'], a['x'], a['y'], a['sprite'])
