# SightForge skin relaxer (Studio 0.19), Blender side: a skinned body's bone weights smoothed only where its clips tear the
# skin, kept only if the tearing drops. Auto-skinned bodies (Tripo's, Meshy's) give a flap or a crease vertices weighted
# to one bone while their neighbours follow another, so the edges between them stretch or crush as the joint turns.
#
#   import skinfix_blender; skinfix_blender.run(body, arm, actions, log=print)
#
# Measures as mesh3d does (an edge longer than 1.7 or shorter than 0.45 times its rest length tears its two vertices),
# over a few frames of every clip. The vertices torn in any measured frame, and two rings round them, take their
# neighbours' weights in steps (each step half its own weights, half its neighbours' mean); after each round of steps
# the clips are measured again, and the last round that lowered the tearing is the one kept.
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
        for g in np.nonzero(W[vi] * deform)[0]:
            if g not in keep: vg[int(g)].remove([int(vi)])
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
