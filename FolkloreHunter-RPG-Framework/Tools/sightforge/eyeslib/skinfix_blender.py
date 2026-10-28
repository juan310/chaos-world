# SightForge skin relaxer (Studio 0.19), Blender side: a skinned body's bone weights smoothed only where its clips tear the
# skin, kept only if the tearing drops. Auto-skinned bodies (Tripo's, Meshy's) give a flap or a crease vertices weighted
# to one bone while their neighbours follow another, so the edges between them stretch or crush as the joint turns.
#
#   import skinfix_blender; skinfix_blender.run(body, arm, actions, log=print)
#   skinfix_blender.skirt(body, arm, log=print)     cloth hanging from the hips weighted as cloth (below)
#   skinfix_blender.untangle(body, arm, log=print)  hands that touched the thighs when skinned let go of them (below)
#
# Measures as mesh3d does (an edge longer than 1.7 or shorter than 0.45 times its rest length tears its two vertices),
# over a few frames of every clip. The vertices torn in any measured frame, and two rings round them, take their
# neighbours' weights in steps (each step half its own weights, half its neighbours' mean); after each round of steps
# the clips are measured again, and the last round that lowered the tearing is the one kept.
import bmesh
import bpy
import numpy as np

SMAX, SMIN = 1.7, .45


def _co(body):
    dg = bpy.context.evaluated_depsgraph_get(); ev = body.evaluated_get(dg); me = ev.to_mesh()
    a = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', a); ev.to_mesh_clear()
    return a.reshape(-1, 3)


def _frames(act, n):
    f0, f1 = act.frame_range
    return sorted(set(int(round(f0 + (f1 - f0) * i / max(1, n - 1))) for i in range(n)))


def measure(body, arm, actions, E, rest_len, samples=6):
    """-> (the mean share of torn vertices over every measured frame, per-vertex count of frames torn)"""
    sc = bpy.context.scene; ad = arm.animation_data or arm.animation_data_create()
    ok = rest_len > 1e-6; count = np.zeros(len(body.data.vertices), np.int32); shares = []
    for act in actions:
        ad.action = act
        for f in _frames(act, samples):
            sc.frame_set(f); c = _co(body)
            r = np.linalg.norm(c[E[:, 0]] - c[E[:, 1]], axis=1) / np.where(ok, rest_len, 1)
            bad = np.unique(E[ok & ((r > SMAX) | (r < SMIN))])
            count[bad] += 1; shares.append(len(bad) / len(count))
    ad.action = None
    for pb in arm.pose.bones: pb.matrix_basis.identity()
    bpy.context.view_layer.update()
    return (float(np.mean(shares)) * 100 if shares else 0.0), count


def _weights(body, G):
    W = np.zeros((len(body.data.vertices), G), np.float32)
    for v in body.data.vertices:
        for g in v.groups:
            if g.group < G: W[v.index, g.group] = g.weight
    return W


def _write(body, W, idx, deform):
    vg = body.vertex_groups
    for vi in idx:
        row = W[vi] * deform
        keep = np.argsort(row)[::-1][:4]; keep = keep[row[keep] > .01]
        tot = row[keep].sum() / max(.05, 1 - float((W[vi] * (1 - deform)).sum()))   # beside what kept groups hold
        # every deforming group the vertex is in now and is not keeping goes (a weight set to nothing included)
        for g in [x.group for x in body.data.vertices[int(vi)].groups]:
            if g < len(deform) and deform[g] > 0 and g not in keep: vg[int(g)].remove([int(vi)])
        for g in keep:
            vg[int(g)].add([int(vi)], float(row[g] / tot), 'REPLACE')


def run(body, arm, actions, log=print, rounds=4, steps=3, keep=None):
    """smooths the torn regions' weights; returns (tearing before, after) as the mean share of vertices, in %"""
    me = body.data
    E = np.empty(len(me.edges) * 2, np.int64); me.edges.foreach_get('vertices', E); E = E.reshape(-1, 2)
    if arm.animation_data: arm.animation_data.action = None
    for pb in arm.pose.bones: pb.matrix_basis.identity()
    bpy.context.view_layer.update()
    rest = _co(body); rest_len = np.linalg.norm(rest[E[:, 0]] - rest[E[:, 1]], axis=1)
    before, count = measure(body, arm, actions, E, rest_len)
    G = len(body.vertex_groups)
    deform = np.array([1.0 if (vg.name in arm.data.bones and arm.data.bones[vg.name].use_deform) else 0.0 for vg in body.vertex_groups], np.float32)
    if keep:                                               # groups left exactly as they are (e.g. finger bones made by the build)
        for i, vg in enumerate(body.vertex_groups):
            if any(k in vg.name for k in keep): deform[i] = 0.0
    W0 = _weights(body, G)
    sel = count > 0
    nb = [[] for _ in range(len(rest))]
    for a, b in E: nb[a].append(b); nb[b].append(a)
    for _ in range(2):                                     # two rings round the torn vertices
        grow = sel.copy()
        for a, b in E[sel[E[:, 0]] | sel[E[:, 1]]]: grow[a] = grow[b] = True
        sel = grow
    if keep:                                               # vertices mostly on kept groups are left alone
        kept = (W0 * (1 - deform) * (W0.sum(1, keepdims=True) > 0)).sum(1) > .5 * W0.sum(1)
        sel &= ~kept
    idx = np.nonzero(sel)[0]
    log('SKINFIX tearing %.2f %% of the body (mean over %d clips), %d vertices to relax' % (before, len(actions), len(idx)))
    if not len(idx): return before, before
    W = W0.copy(); best = (before, W0.copy())
    for rnd in range(rounds):
        for _ in range(steps):
            M = np.array([W[n].mean(0) if n else W[i] for i, n in ((i, nb[i]) for i in idx)])
            W[idx] = .5 * W[idx] + .5 * M
        W[:, deform == 0] = W0[:, deform == 0]            # kept groups and non-deforming ones stay as they were
        _write(body, W, idx, deform)
        now, _ = measure(body, arm, actions, E, rest_len)
        log('SKINFIX round %d: tearing %.2f %%' % (rnd + 1, now))
        if now < best[0] - .01: best = (now, W.copy())
        else: break
    _write(body, best[1], idx, deform)
    log('SKINFIX kept: tearing %.2f %% -> %.2f %%' % (before, best[0]))
    return before, best[0]


def skirt(body, arm, log=print):
    """cloth hanging from the hips (a loincloth, sash or skirt) weighted as cloth: auto-skinning ties it to whichever
    thigh is nearest, so where the left thigh's cloth meets the right thigh's, every stride tears it apart. Cloth here is
    surface between the knees and the hip joints that stands clear of both thighs (beyond 1.25 times the thigh's own
    radius) and is not held by the arms or the spine; it follows the hips, and each thigh by up to half, fading to nothing
    at 2.2 radii from it, so the weights change smoothly round the body and between the legs.
    -> the number of cloth vertices reweighted"""
    B = arm.data.bones; AW = arm.matrix_world
    if not all(n in B for n in ('hips', 'upperleg.l', 'upperleg.r', 'lowerleg.l', 'lowerleg.r')):
        log('SKIRT the rig has no hips, thighs and shins to weight cloth with'); return 0
    me = body.data; G = len(body.vertex_groups); gi = {g.name: g.index for g in body.vertex_groups}
    if not all(n in gi for n in ('hips', 'upperleg.l', 'upperleg.r')):
        log('SKIRT the body has no hips and thigh weights'); return 0
    Mw = np.array(body.matrix_world)
    co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3) @ Mw[:3, :3].T + Mw[:3, 3]
    hip = {s: np.array(AW @ B['upperleg.' + s].head_local) for s in 'lr'}
    knee = {s: np.array(AW @ B['lowerleg.' + s].head_local) for s in 'lr'}
    hz = (hip['l'][2] + hip['r'][2]) / 2; kz = (knee['l'][2] + knee['r'][2]) / 2; L = hz - kz
    def dist(s):
        a, b = hip[s], knee[s]; ab = b - a; t = np.clip(((co - a) @ ab) / (ab @ ab), 0, 1)
        return np.linalg.norm(co - (a + t[:, None] * ab), axis=1)
    d = {s: dist(s) for s in 'lr'}
    W = _weights(body, G)
    names = [g.name for g in body.vertex_groups]
    held = np.zeros(len(co), np.float32); spine = np.zeros(len(co), np.float32)
    for i, n in enumerate(names):
        root = n.split('.')[0].split('_')[0].lower()
        if root in ('hand', 'lowerarm', 'upperarm', 'shoulder', 'handslot', 'thumb', 'index', 'middle', 'ring', 'pinky') or 'Hand' in n or 'Arm' in n:
            held += W[:, i]
        if root in ('spine', 'chest'): spine += W[:, i]
    band = (co[:, 2] < hz + .05 * L) & (co[:, 2] > kz - .1 * L)
    r = {}
    for s in 'lr':                                          # the thigh's own radius: the skin of the thigh's middle
        mid = band & (co[:, 2] < hz - .2 * L) & (co[:, 2] > kz + .2 * L) & (W[:, gi['upperleg.' + s]] > .5) & (d[s] < .6 * L)
        r[s] = float(np.percentile(d[s][mid], 40)) if mid.sum() > 20 else .12 * L
    cloth = band & (d['l'] > 1.25 * r['l']) & (d['r'] > 1.25 * r['r']) & (held < .05) & (spine < .3)
    idx = np.nonzero(cloth)[0]
    if not len(idx): log('SKIRT no cloth found clear of the thighs'); return 0
    Wn = W.copy()
    for g in [gi[n] for n in names if n.split('.')[0] in ('upperleg', 'lowerleg', 'foot', 'toes', 'hips', 'spine')]: Wn[idx, g] = 0
    wl = .5 * np.clip((2.2 * r['l'] - d['l'][idx]) / (.95 * r['l']), 0, 1)
    wr = .5 * np.clip((2.2 * r['r'] - d['r'][idx]) / (.95 * r['r']), 0, 1)
    Wn[idx, gi['upperleg.l']] = wl; Wn[idx, gi['upperleg.r']] = wr; Wn[idx, gi['hips']] = 1 - wl - wr
    deform = np.ones(G, np.float32)
    _write(body, Wn, idx, deform)
    log('SKIRT %d cloth vertices weighted as cloth (thigh radius %.3f / %.3f m)' % (len(idx), r['l'], r['r']))
    return len(idx)


def _seg_dist(co, pts):
    """each point's distance to a polyline through `pts`"""
    d = np.full(len(co), np.inf)
    for a, b in zip(pts, pts[1:]):
        ab = b - a; t = np.clip(((co - a) @ ab) / max(ab @ ab, 1e-9), 0, 1)
        d = np.minimum(d, np.linalg.norm(co - (a + t[:, None] * ab), axis=1))
    return d


def untangle(body, arm, log=print):
    """hands that hung against the thighs when the body was skinned: auto-skinning shares the skin where they touch, so
    the thigh's skin carries finger and hand weights (and the hand's, thigh weights), and raising the arms drags the
    thigh's skin up with them (a long-armed troll's ground pound: 13 % of its skin torn). Each vertex weighted to both
    an arm (forearm, hand, fingers) and a leg (hips, thigh, shin, foot) goes to the one it is nearer, measured in each
    part's own thickness (its distance to the bones' line over the part's own skin radius); the other's weights go.
    -> the number of vertices untangled"""
    B = arm.data.bones; AW = arm.matrix_world
    me = body.data; G = len(body.vertex_groups); names = [g.name for g in body.vertex_groups]
    Mw = np.array(body.matrix_world)
    co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3) @ Mw[:3, :3].T + Mw[:3, 3]
    P = lambda n, tail=False: np.array(AW @ (B[n].tail_local if tail else B[n].head_local))
    W = _weights(body, G)

    def side(n):
        if n.endswith('.l') or n.endswith('_l') or n.startswith('Left'): return 'l'
        if n.endswith('.r') or n.endswith('_r') or n.startswith('Right'): return 'r'
        return None

    def is_arm(n):
        root = n.split('.')[0].split('_')[0].lower()
        return root in ('hand', 'lowerarm', 'handslot', 'thumb', 'index', 'middle', 'ring', 'pinky') or 'Hand' in n or 'ForeArm' in n

    def is_leg(n): return n.split('.')[0] in ('hips', 'upperleg', 'lowerleg', 'foot', 'toes')
    leg = np.array([is_leg(n) for n in names])
    need = ['hips', 'upperleg.l', 'upperleg.r', 'lowerleg.l', 'lowerleg.r', 'foot.l', 'foot.r', 'lowerarm.l', 'lowerarm.r', 'hand.l', 'hand.r']
    if not all(n in B for n in need): log('UNTANGLE the rig lacks %s' % [n for n in need if n not in B]); return 0
    dl = np.minimum(_seg_dist(co, [P('upperleg.l'), P('lowerleg.l'), P('foot.l')]), _seg_dist(co, [P('upperleg.r'), P('lowerleg.r'), P('foot.r')]))
    legw = W[:, leg].sum(1)
    core = (legw > .9) & np.isin(np.argmax(W, 1), [names.index(n) for n in ('upperleg.l', 'upperleg.r') if n in names])
    r_leg = float(np.percentile(dl[core], 40)) if core.sum() > 20 else .12
    done = 0
    for s in 'lr':
        armg = np.array([is_arm(n) and side(n) == s for n in names])
        hand_len = np.linalg.norm(P('hand.' + s, True) - P('hand.' + s))
        tip = P('hand.' + s) + (P('hand.' + s, True) - P('hand.' + s)) * max(1.0, .18 / max(hand_len, 1e-3))
        da = _seg_dist(co, [P('lowerarm.' + s), P('hand.' + s), tip])
        armw = W[:, armg].sum(1)
        acore = armw > .9
        r_arm = float(np.percentile(da[acore], 40)) if acore.sum() > 20 else .05
        both = (armw > .02) & (legw > .02)
        idx = np.nonzero(both)[0]
        if not len(idx): continue
        to_arm = da[idx] / r_arm < dl[idx] / r_leg
        Wn = W.copy()
        a_idx, l_idx = idx[to_arm], idx[~to_arm]
        Wn[np.ix_(a_idx, np.nonzero(leg)[0])] = 0
        Wn[np.ix_(l_idx, np.nonzero(armg)[0])] = 0
        _write(body, Wn, idx, np.ones(G, np.float32))
        W = _weights(body, G)
        done += len(idx)
        log('UNTANGLE %s: %d vertices shared by the hand and the legs: %d stay the hand\'s, %d the legs\' (radius hand %.3f, thigh %.3f m)'
            % ({'l': 'left', 'r': 'right'}[s], len(idx), len(a_idx), len(l_idx), r_arm, r_leg))
    # where the model was made with the hand grown into the thigh, the two surfaces are one: faces run from the hand's
    # skin to the thigh's, and however they are weighted they stretch as the hand leaves. Those faces go, and each side's
    # opening is closed with a cap of its own.
    armw = W[:, np.array([is_arm(n) for n in names])].sum(1); legw = W[:, leg].sum(1)
    own = np.where(armw > legw, 1, 2) * ((armw > .5) | (legw > .5))
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    bridge = [f for f in bm.faces if {int(own[v.index]) for v in f.verts} >= {1, 2}]
    if bridge:
        near = {v.index for f in bridge for v in f.verts}
        bmesh.ops.delete(bm, geom=bridge, context='FACES_ONLY')
        rim = [e for e in bm.edges if e.is_boundary and e.verts[0].index in near and e.verts[1].index in near]
        capped = bmesh.ops.holes_fill(bm, edges=rim, sides=0)['faces']
        bm.to_mesh(me); me.update()
        log('UNTANGLE cut %d faces joining the hands to the legs, %d cap faces close the openings' % (len(bridge), len(capped)))
    bm.free()
    return done


def facing(body, arm):
    """(skeleton forward, body forward) along y, each +1 or -1. The skeleton's forward is where its toe bones point
    from the ankles; the body's is where its feet reach furthest from the ankles (toes are longer than heels). An
    auto-rig that put the skeleton in backwards has the two opposite: every move then plays reversed against the face
    (the troll's ground pound bent it backwards)."""
    B = arm.data.bones; AW = arm.matrix_world
    if not all(n in B for n in ('foot.l', 'foot.r', 'toes.l', 'toes.r')): return None, None
    ank = {s: AW @ B['foot.' + s].head_local for s in 'lr'}
    tip = {s: AW @ B['toes.' + s].tail_local for s in 'lr'}
    sk = 1 if sum((tip[s] - ank[s]).y for s in 'lr') > 0 else -1
    me = body.data; gi = {g.name: g.index for g in body.vertex_groups}
    Mw = np.array(body.matrix_world)
    co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3) @ Mw[:3, :3].T + Mw[:3, 3]
    front = back = 0.0
    for s in 'lr':
        ids = {gi.get('foot.' + s), gi.get('toes.' + s)} - {None}
        on = np.array([any(g.group in ids and g.weight > .3 for g in v.groups) for v in me.vertices])
        low = on & (co[:, 2] < ank[s].z + .03)
        if low.sum() < 10: continue
        dy = co[low, 1] - ank[s].y
        front += dy.max(); back += -dy.min()
    return sk, (1 if front > back else -1)


def turn_around(body, arm, log=print):
    """a body skinned to a skeleton that faces the other way is turned to face the skeleton's way: the mesh is turned
    half round about the hips, its left and right groups swap (they now lie on the other side), and each foot's
    weights are shared out again between the ankle and the toes along the toe bone (the heel had been the toes').
    -> True if it was turned"""
    sk, bd = facing(body, arm)
    if sk is None or sk == bd:
        log('FACING the body faces its skeleton\'s way' if sk else 'FACING no feet to judge by'); return False
    from mathutils import Matrix, Vector
    B = arm.data.bones; AW = arm.matrix_world
    piv = AW @ B['hips'].head_local
    T = Matrix.Translation(Vector((piv.x, piv.y, 0)))
    turn = body.matrix_world.inverted() @ T @ Matrix.Rotation(np.pi, 4, 'Z') @ T.inverted() @ body.matrix_world
    body.data.transform(turn); body.data.update()
    # left and right swap places
    def other(n):
        for a, b in (('.l', '.r'), ('_l', '_r'), ('.L', '.R'), ('_L', '_R')):
            if n.endswith(a): return n[:-len(a)] + b
            if n.endswith(b): return n[:-len(b)] + a
        for a, b in (('Left', 'Right'), ('left', 'right')):
            if n.startswith(a): return b + n[len(a):]
            if n.startswith(b): return a + n[len(b):]
        return None
    pairs = [(g, other(g.name)) for g in body.vertex_groups if other(g.name)]
    for g, _ in pairs: g.name = '__swap__' + g.name
    for g, o in pairs: g.name = o
    # each foot: the ankle's share and the toes' share along the toe bone
    me = body.data; gi = {g.name: g.index for g in body.vertex_groups}
    Mw = body.matrix_world
    for s in 'lr':
        F, Tg = gi.get('foot.' + s), gi.get('toes.' + s)
        if F is None or Tg is None: continue
        h = AW @ B['toes.' + s].head_local; d = (AW @ B['toes.' + s].tail_local - h).normalized()
        fl, tl = body.vertex_groups[F], body.vertex_groups[Tg]
        n = 0
        for v in me.vertices:
            w = {g.group: g.weight for g in v.groups}
            tot = w.get(F, 0) + w.get(Tg, 0)
            if tot <= 1e-4: continue
            u = (Mw @ v.co - h).dot(d)
            share = min(1.0, max(0.0, (u + .04) / .06))
            fl.add([v.index], tot * (1 - share), 'REPLACE'); tl.add([v.index], tot * share, 'REPLACE'); n += 1
        log('FACING %s foot: %d vertices shared between ankle and toes again' % ({'l': 'left', 'r': 'right'}[s], n))
    log('FACING the body faced the other way from its skeleton (toes bones %+d, feet %+d along y): turned to match, %d left/right groups swapped'
        % (sk, bd, len(pairs) // 2))
    return True
