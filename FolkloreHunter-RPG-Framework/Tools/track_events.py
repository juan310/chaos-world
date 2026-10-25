"""Lists the frames behind SightForge track findings for one actor: fast travel while facing elsewhere, and
movement while the picture stays on one idle pose (python Tools/track_events.py <track.jsonl> [actor])."""
import json, math, sys
rows = [json.loads(l) for l in open(sys.argv[1], encoding='utf-8') if l.strip()]
who = sys.argv[2] if len(sys.argv) > 2 else 'hunter'
ss = [(r, next((a for a in r['actors'] if a['id'] == who), None)) for r in rows]
ss = [(r, a) for r, a in ss if a]
def show(i):
    r, a = ss[i]
    print('  f%5d t%7.2f %-12s ui=%-14s x=%7.2f y=%7.2f %s' % (r['frame'], r['t'], r['map'], r['ui'], a['x'], a['y'], a['sprite']))
print('== fast moves (> 3 units/s, < 0.6 units per frame)')
for i in range(1, len(ss)):
    (r0, a0), (r1, a1) = ss[i - 1], ss[i]
    d = math.hypot(a1['x'] - a0['x'], a1['y'] - a0['y']); dt = r1['t'] - r0['t']
    if dt > 0 and d < .6 and d / dt > 3 and r0['map'] == r1['map']:
        print(' %.2f units/s' % (d / dt)); show(i - 1); show(i)
print('== same idle picture while moving > 0.15')
i = 0
while i < len(ss):
    j, acc = i, 0.0
    while j + 1 < len(ss) and ss[j + 1][1]['sprite'] == ss[i][1]['sprite'] and '_idle' in ss[i][1]['sprite']:
        s0, s1 = ss[j][1], ss[j + 1][1]
        st = math.hypot(s1['x'] - s0['x'], s1['y'] - s0['y'])
        if st < .6 and ss[j][0]['map'] == ss[j + 1][0]['map']: acc += st
        j += 1
    if acc > .15:
        print(' moved %.2f units in %d frames' % (acc, j - i)); [show(k) for k in range(max(0, i - 2), min(len(ss), j + 3)) if k < i + 3 or k > j - 3]
    i = j + 1
