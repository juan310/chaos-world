# Fingers for the slim bodies (Blender): found in the mesh itself, rigged, skinned, and closed round each weapon's grip.
#
#   detect   the hand's vertices (those the hand bone carries), walked over the mesh from the wrist (geodesic distance);
#            fingertips are the farthest points that stand apart; each finger is the piece of the hand that stays
#            separate from the others above the webbing (cut the hand at a rising distance: pieces part at the webs).
#            The thumb is the tip that parts lowest. Each finger's centre line runs through the middle of its piece.
#            A hand where five fingers are not found is reported, and gets no finger bones (no guessed mitten).
#   rig      three bones per finger (thumb_1..3, index_1..3, middle_, ring_, pinky_ .l/.r), children of the hand
#   skin     the hand's share of each finger vertex handed to the finger bones by where it lies along the finger,
#            blended over a few millimetres at each joint and at the knuckle (the rest of a vertex's weights untouched)
#   grips    for each weapon piece held in a hand slot: each finger curled joint by joint until it meets the handle
#            (a cylinder along the slot's Y axis through the slot, its radius measured from the piece's mesh), then
#            the next joint; a relaxed pose and the bow string hook are set values. Written as curl angles about an
#            axis the game rebuilds from the bones (finger direction x palm direction), so no axis conventions travel.
import bpy, heapq, json, math
from mathutils import Vector, Matrix, Quaternion

NAMES = ('thumb', 'index', 'middle', 'ring', 'pinky')
SEGS = {'thumb': (.40, .33, .27), 'index': (.46, .30, .24), 'middle': (.46, .30, .24), 'ring': (.46, .30, .24), 'pinky': (.46, .30, .24)}
LIMITS = {'thumb': (55, 65, 75), 'index': (90, 105, 75), 'middle': (90, 105, 75), 'ring': (90, 105, 75), 'pinky': (90, 105, 75)}
RELAXED = {'thumb': (8, 10, 6), 'index': (10, 16, 8), 'middle': (13, 20, 10), 'ring': (16, 24, 12), 'pinky': (19, 28, 14)}
HOOK = {'thumb': (20, 18, 10), 'index': (12, 75, 45), 'middle': (12, 75, 45), 'ring': (14, 75, 45), 'pinky': (30, 70, 40)}
THUMB_TILT = .7          # the thumb closes across the palm: its curl leans toward the little finger by this much


def bone_name(f, k, side):
    return '%s_%d.%s' % (f, k, side)


# ------------------------------------------------------------------------------------------------ detection
def hand_vertices(body, side, thr=.3):
    gi = {g.index: g.name for g in body.vertex_groups}
    out = {}
    for v in body.data.vertices:
        w = {gi[g.group]: g.weight for g in v.groups if g.group in gi and g.weight > 0}
        if w and max(w, key=w.get) == 'hand.' + side and w['hand.' + side] >= thr:
            out[v.index] = w['hand.' + side]
    return out


def _adjacency(body, verts):
    """mesh edges among the vertices, plus the seams welded: vertices at the same place (split along texture seams, so
    the surface comes in many islands) are joined, which gives back the one surface the eye sees"""
    adj = {i: [] for i in verts}
    for e in body.data.edges:
        a, b = e.vertices
        if a in adj and b in adj:
            adj[a].append(b); adj[b].append(a)
    same = {}
    for i in verts:
        c = body.data.vertices[i].co
        same.setdefault((round(c.x * 1e4), round(c.y * 1e4), round(c.z * 1e4)), []).append(i)
    for grp in same.values():
        for j in grp[1:]:
            adj[grp[0]].append(j); adj[j].append(grp[0])
    return adj


def _dijkstra(P, adj, sources):
    g = {i: math.inf for i in adj}; pred = {}
    h = []
    for s in sources: g[s] = 0; heapq.heappush(h, (0, s))
    while h:
        d, i = heapq.heappop(h)
        if d > g[i]: continue
        for j in adj[i]:
            nd = d + (P[i] - P[j]).length
            if nd < g[j]: g[j] = nd; pred[j] = i; heapq.heappush(h, (nd, j))
    return g, pred


class _UF:
    def __init__(self): self.p = {}
    def f(self, a):
        self.p.setdefault(a, a)
        while self.p[a] != a: self.p[a] = self.p[self.p[a]]; a = self.p[a]
        return a
    def u(self, a, b): self.p[self.f(a)] = self.f(b)


def detect(body, arm, side, log=print):
    """the five fingers of one hand: tips, the vertices of each finger above its web, centre lines, joints"""
    W = body.matrix_world
    hv = hand_vertices(body, side)
    if len(hv) < 60: return {'ok': False, 'why': 'only %d vertices on hand.%s' % (len(hv), side)}
    P = {i: W @ body.data.vertices[i].co for i in hv}
    hb = arm.data.bones['hand.' + side]; AM = arm.matrix_world
    h0, h1 = AM @ hb.head_local, AM @ hb.tail_local; hd = (h1 - h0).normalized()
    along = {i: (P[i] - h0).dot(hd) for i in hv}
    lo, hi = min(along.values()), max(along.values())
    adj = _adjacency(body, hv)
    wrist = [i for i in hv if along[i] < lo + .12 * (hi - lo)]
    g, pred = _dijkstra(P, adj, wrist)
    reach = [i for i in hv if g[i] < math.inf]
    gmax = max(g[i] for i in reach)
    # tips: the peaks of the walk from the wrist that stand highest above where they join the rest of the hand
    # (persistence: going down from the top, two pieces meeting leave the lower peak behind at that level; bumps on a
    # finger stand a few millimetres, a finger stands centimetres)
    order = sorted(reach, key=lambda i: -g[i])
    uf = _UF(); on = set(); peak = {}; prom = {}
    for i in order:
        on.add(i); uf.f(i); peak[uf.f(i)] = i
        for j in adj[i]:
            if j not in on: continue
            a, b = uf.f(i), uf.f(j)
            if a == b: continue
            pa, pb = peak[a], peak[b]
            lo_, hi_ = (pa, pb) if g[pa] < g[pb] else (pb, pa)
            if lo_ != i: prom[lo_] = g[lo_] - g[i]
            uf.u(a, b); peak[uf.f(i)] = hi_
    top = max(reach, key=lambda i: g[i]); prom[top] = math.inf
    tips = sorted(prom, key=lambda i: -prom[i])[:5]
    log('FINGERS hand.%s peaks: %s' % (side, ', '.join('%.3f/%.3f' % (g[t], prom[t] if prom[t] < 9 else -1) for t in sorted(prom, key=lambda i: -prom[i])[:8])))
    if len(tips) < 5 or prom[tips[4]] < .08 * gmax: return {'ok': False, 'why': 'found %d fingertips standing clear on hand.%s, not 5' % (sum(1 for t in tips if prom[t] >= .08 * gmax), side)}
    # the level where each tip's piece first joins another tip's piece (going down from the tips): its web
    order = sorted(reach, key=lambda i: -g[i])
    uf = _UF(); on = set(); web = {}
    tipset = set(tips)
    for i in order:
        on.add(i); uf.f(i)
        for j in adj[i]:
            if j in on: uf.u(i, j)
        roots = {}
        for t in tips:
            if t in on: roots.setdefault(uf.f(t), []).append(t)
        for grp in roots.values():
            if len(grp) > 1:
                for t in grp:
                    if t not in web: web[t] = g[i]
        if len(web) == 5: break
    # a tip on an island of its own (a finger modelled as a separate piece) never joins: its web is the island's lowest point
    for t in tips:
        if t not in web:
            seen = {t}; st = [t]
            while st:
                a = st.pop()
                for b in adj[a]:
                    if b not in seen and g[b] < math.inf: seen.add(b); st.append(b)
            web[t] = min(g[i] for i in seen)
            log('FINGERS hand.%s: a tip on its own island (%d vertices)' % (side, len(seen)))
    log('FINGERS hand.%s tips: %s' % (side, ', '.join('%d g %.3f web %.3f' % (t, g[t], web[t]) for t in tips)))
    # each finger's own vertices: the piece holding its tip, just above its web
    pieces = {}
    for t in tips:
        L = web[t] + 1e-6
        seen = {t}; st = [t]
        while st:
            a = st.pop()
            for b in adj[a]:
                if b not in seen and g[b] >= L: seen.add(b); st.append(b)
        pieces[t] = seen
    # thumb: the tip whose piece parts lowest (the deepest web); the others ordered across the hand from the thumb
    thumb = min(tips, key=lambda t: web[t])
    rest = [t for t in tips if t != thumb]
    rest.sort(key=lambda t: (P[t] - P[thumb]).length)
    names = dict(zip([thumb] + rest, NAMES))
    fingers = {}
    for t in tips:
        nm = names[t]; vs = pieces[t]
        # centre line: centroids of the piece's vertices in bands of the walk distance, web to tip
        g0, g1 = web[t], g[t]; nb = 6; cl = []
        for k in range(nb):
            a, b = g0 + (g1 - g0) * k / nb, g0 + (g1 - g0) * (k + 1) / nb
            band = [P[i] for i in vs if a <= g[i] <= b]
            if band: cl.append(sum(band, Vector()) / len(band))
        if len(cl) < 2: return {'ok': False, 'why': 'the %s finger of hand.%s is too small to follow' % (nm, side)}
        d = (cl[-1] - cl[0]).normalized()
        visible = (P[t] - cl[0]).dot(d)
        # the knuckle lies below the web: a finger shows about three quarters of its length above it, a thumb about half
        full = visible / (.52 if nm == 'thumb' else .74)
        base = cl[0] - d * (full - visible)
        end = P[t] - d * .004
        radius = sorted(((P[i] - cl[0]) - d * (P[i] - cl[0]).dot(d)).length for i in vs)[len(vs) // 2]
        fingers[nm] = {'tip': t, 'base': base, 'end': end, 'dir': (end - base).normalized(), 'length': (end - base).length, 'radius': radius,
                       'web': web[t], 'vertices': len(vs), 'centre': cl}
    log('FINGERS hand.%s: %s' % (side, ', '.join('%s %.1f cm (r %.1f mm, %d verts)' % (n, f['length'] * 100, f['radius'] * 1000, f['vertices']) for n, f in fingers.items())))
    return {'ok': True, 'side': side, 'fingers': fingers, 'g': g, 'hand': hv, 'P': P, 'names': names}


# ------------------------------------------------------------------------------------------------ rig and skin
def joints(f, nm):
    s = SEGS[nm]; acc = 0; pts = [f['base']]
    for x in s:
        acc += x; pts.append(f['base'] + (f['end'] - f['base']) * acc)
    return pts


def palm_normal(arm, side):
    """toward the palm: from the hand bone's line to the grip slot (the grip sits in the palm)"""
    AM = arm.matrix_world; hb = arm.data.bones['hand.' + side]; sl = arm.data.bones['handslot.' + side]
    h0, h1 = AM @ hb.head_local, AM @ hb.tail_local; hd = (h1 - h0).normalized()
    v = AM @ sl.head_local - h0
    n = v - hd * v.dot(hd)
    return n.normalized()


def curl_axis(d, n, nm, toward_pinky):
    nn = (n + toward_pinky * THUMB_TILT).normalized() if nm == 'thumb' else n
    a = d.cross(nn)
    return a.normalized() if a.length > 1e-6 else Vector((1, 0, 0))


def add_bones(arm, found):
    """three bones per finger, children of the hand (bones' Z toward the palm: a curl turns about their X)"""
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones; AMi = arm.matrix_world.inverted()
    for side, h in found.items():
        if not h.get('ok'): continue
        n = palm_normal(arm, side)
        for nm, f in h['fingers'].items():
            pts = joints(f, nm); parent = eb['hand.' + side]
            for k in range(3):
                bn = bone_name(nm, k + 1, side)
                if bn in eb: eb.remove(eb[bn])
                b = eb.new(bn); b.head = AMi @ pts[k]; b.tail = AMi @ pts[k + 1]
                b.align_roll(AMi.to_3x3() @ n)
                b.parent = parent; b.use_connect = k > 0; b.use_deform = True
                parent = b
    bpy.ops.object.mode_set(mode='OBJECT')


def skin(body, found, blend=.005):
    """the hand's share of each finger vertex passed to the finger bones along the finger"""
    vg = {g.name: g for g in body.vertex_groups}
    for side, h in found.items():
        if not h.get('ok'): continue
        hand = vg['hand.' + side]
        for nm in h['fingers']:
            for k in range(3):
                bn = bone_name(nm, k + 1, side)
                if bn not in vg: vg[bn] = body.vertex_groups.new(name=bn)
        P = h['P']; F = h['fingers']
        moved = 0
        for i, wh in h['hand'].items():
            # the nearest finger centre line (by distance to its segment base..end), within its reach
            best = None
            for nm, f in F.items():
                d = f['dir']; t = (P[i] - f['base']).dot(d)
                if t < -blend or t > f['length'] + .01: continue
                r = ((P[i] - f['base']) - d * t).length
                lim = f['radius'] * 2.2
                if r > lim: continue
                if best is None or r < best[0]: best = (r, nm, t)
            if not best: continue
            _, nm, t = best; f = F[nm]
            a = max(0.0, min(1.0, (t + blend) / (2 * blend)))         # 0 below the knuckle, 1 past it
            if a <= 0: continue
            cuts = [f['length'] * x for x in (SEGS[nm][0], SEGS[nm][0] + SEGS[nm][1])]
            w = [0.0, 0.0, 0.0]
            # bone 1 up to the first joint, 2 to the second, 3 beyond; linear blends of +-blend at each joint
            u1 = max(0.0, min(1.0, (t - cuts[0] + blend) / (2 * blend)))
            u2 = max(0.0, min(1.0, (t - cuts[1] + blend) / (2 * blend)))
            w[0] = 1 - u1; w[1] = u1 - u2; w[2] = u2
            hand.add([i], wh * (1 - a), 'REPLACE')
            for k in range(3):
                if w[k] > 1e-4: vg[bone_name(nm, k + 1, side)].add([i], wh * a * w[k], 'REPLACE')
            moved += 1
        print('FINGERS skin hand.%s: %d vertices handed to finger bones' % (side, moved), flush=True)


# ------------------------------------------------------------------------------------------------ grips
def _seg_dist_to_line(p, q, o, u):
    """closest distance between segment p-q and the infinite line o + s u"""
    best = math.inf
    for k in range(9):
        x = p + (q - p) * (k / 8)
        r = (x - o) - u * (x - o).dot(u)
        best = min(best, r.length)
    return best


def handle(piece, slot_head, slot_y):
    """the handle's radius: the piece's vertices within 3 cm of the grip along the slot's Y axis, their distance to it"""
    M = piece.matrix_world; rs = []
    for v in piece.data.vertices:
        x = M @ v.co; s = (x - slot_head).dot(slot_y)
        if abs(s) < .03: rs.append(((x - slot_head) - slot_y * s).length)
    if len(rs) < 4: return None
    rs.sort()
    return rs[len(rs) // 4]


def fk(f, nm, ang, n, tp):
    """the finger's joint positions with curl angles ang (degrees) at its three joints"""
    pts = joints(f, nm); out = [pts[0]]; Q = Quaternion()
    for k in range(3):
        d = (pts[k + 1] - pts[k]); ax = curl_axis(d.normalized(), n, nm, tp)
        Q = Q @ Quaternion(ax, math.radians(ang[k]))
        out.append(out[-1] + Q @ d)
    return out


def solve(f, nm, n, tp, o, u, r):
    """curl each joint in turn until the finger meets the handle (radius r, line o + s u)"""
    lim = LIMITS[nm]; ang = [0.0, 0.0, 0.0]; fr = f['radius']
    touch = lambda pts, k0: min(_seg_dist_to_line(pts[k], pts[k + 1], o, u) for k in range(k0, 3)) <= r + fr
    if touch(fk(f, nm, ang, n, tp), 0): return ang, 'through'
    state = 'free'
    for k in range(3):
        a = 0.0
        while a < lim[k]:
            ang[k] = a + 1
            if touch(fk(f, nm, ang, n, tp), k): state = 'contact'; ang[k] = a; break
            a += 1
        else:
            ang[k] = lim[k]
    return [round(x, 1) for x in ang], state


def grips(arm, found, pieces, log=print):
    """curl angles per pose and hand: relaxed, hook (bow string), and one per weapon piece in a hand slot"""
    AM = arm.matrix_world
    out = {'curlAxis': 'finger direction x palm direction (thumb: palm + %.2f x toward the little finger)' % THUMB_TILT, 'thumbTilt': THUMB_TILT,
           'segments': SEGS, 'poses': {'relaxed': {}, 'hook': {}}, 'pieces': {}, 'contacts': {}}
    for side, h in found.items():
        if not h.get('ok'): continue
        out['poses']['relaxed'][side] = {k: list(v) for k, v in RELAXED.items()}
        out['poses']['hook'][side] = {k: list(v) for k, v in HOOK.items()}
    for p in pieces:
        if p.parent_type != 'BONE' or not p.parent_bone.startswith('handslot.'): continue
        side = p.parent_bone[-1]; h = found.get(side)
        if not h or not h.get('ok'): continue
        sl = arm.data.bones[p.parent_bone]
        o = AM @ sl.head_local; u = (AM.to_3x3() @ (sl.tail_local - sl.head_local)).normalized()
        r = handle(p, o, u)
        if r is None: log('FINGERS %s: no handle found near the grip' % p.name); continue
        n = palm_normal(arm, side)
        F = h['fingers']; tp = (F['pinky']['base'] - F['index']['base']).normalized()
        pose = {}; con = {}
        for nm, f in F.items():
            pose[nm], con[nm] = solve(f, nm, n, tp, o, u, r)
        out['poses'][p.name] = {side: pose}; out['pieces'][p.name] = side; out['contacts'][p.name] = {'radius': round(r, 4), 'fingers': con}
        log('FINGERS grip %s (hand.%s, handle r %.1f mm): %s' % (p.name, side, r * 1000, ', '.join('%s %s %s' % (k, v, con[k]) for k, v in pose.items())))
    return out


def run(body, arm, pieces, log=print):
    found = {s: detect(body, arm, s, log) for s in ('l', 'r')}
    for s, h in found.items():
        if not h.get('ok'): log('FINGERS hand.%s: no finger bones (%s)' % (s, h.get('why')))
    if not any(h.get('ok') for h in found.values()): return None
    add_bones(arm, found)
    skin(body, found)
    data = grips(arm, found, pieces, log)
    data['hands'] = {s: ({'fingers': {n: {'length': round(f['length'], 4), 'radius': round(f['radius'], 4), 'vertices': f['vertices']} for n, f in h['fingers'].items()}}
                         if h.get('ok') else {'why': h.get('why')}) for s, h in found.items()}
    return data
