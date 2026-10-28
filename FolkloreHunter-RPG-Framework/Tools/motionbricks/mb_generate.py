# The MotionBricks side of the bridge (see mb_bridge.py). For each clip in export.json: the four poses leading into its
# first key pose are the context; each next key pose (four poses from that moment) is the target; MotionBricks generates
# the motion between them on the SOMA skeleton, and the result is re-timed so the key pose lands at its own time (the
# game times strikes by them). Each generation's last four played poses are the next one's context. How each gap is
# generated is one of the lab's settings (mb_lab.py; measured in Docs/MOTIONBRICKS_PLAYBOOK.md).
# Run from the MotionBricks checkout:  uv run python <this file> export.json mb_out.json [setting, default adaptive]
import json, sys
import numpy as np
import warp as wp
from motionbricks.io.usd import load_skeleton_from_usd
from motionbricks.io.utils import SOMA_SKELETON_PRIM_PATH, get_asset_file
from motionbricks.model.contract import MODEL_POSES_PER_TOKEN
from motionbricks.model.soma.contract import SOMA_N1_FP32_ONNX_PATH
from motionbricks.model.soma.engine import MotionBricksSOMAEngine


def window(frames, start):
    idx = [min(max(0, start + k), len(frames) - 1) for k in range(MODEL_POSES_PER_TOKEN)]
    return frames[idx]


def slerp(a, b, t):
    """Quaternion (x, y, z, w) arrays, per joint."""
    d = np.sum(a * b, axis=-1, keepdims=True)
    b = np.where(d < 0, -b, b); d = np.abs(d)
    q = a * (1 - t) + b * t                      # nlerp is close enough between neighbouring frames
    return q / np.linalg.norm(q, axis=-1, keepdims=True)


def retime(gen, count):
    """`count` frames spread over the generated ones (the first after the context, the last on the target)."""
    n = len(gen)
    out = []
    for m in range(1, count + 1):
        u = m / count * (n - 1)
        i = min(int(u), n - 2); t = u - i
        a, b = gen[i], gen[i + 1]
        out.append(np.concatenate([a[:, :3] * (1 - t) + b[:, :3] * t, slerp(a[:, 3:], b[:, 3:], t)], axis=1))
    return out


# clips the lab films showed better under another setting (Docs/MOTIONBRICKS_PLAYBOOK.md, round 5): the dive arcs
# instead of flying as a plank, the standing jump keeps our arm swing over its calmer body
PICK = {'Jump_Full_Short': 'settle', 'Jump_Up': 'settle_arms'}
# Attacks the body gate held back under adaptive (2026-10-28): with their short gaps kept as our in-betweens, the sword
# hilt swings into the thigh (SnS_Rush_1 1 -> 9 cm, SnS_Leap_Slash 5 -> 8, Katana_Iai 3 -> 7) and the skin folds more
# (SnS_Rush_2, SnS_Shield_Crash). They keep the generation that passed until their in-betweens are re-keyed clear of the legs.
PICK.update({n: 'base' for n in ('SnS_Rush_1', 'SnS_Rush_2', 'SnS_Leap_Slash', 'SnS_Shield_Crash', 'Katana_Iai')})


def main(src, dst, policy='adaptive'):
    """each clip under one of the lab's settings (mb_lab.VARIANTS; Docs/MOTIONBRICKS_PLAYBOOK.md says why 'adaptive':
    MotionBricks only where a gap between our key poses gives it room, the length fitted to the gap, two target poses
    through the middle keys and one on the last, and gentle clips kept as ours). 'base' is the old way (every gap, four
    target poses, the model choosing the length), which the lab measured squeezing its motion 4.5 times."""
    import mb_lab
    data = json.load(open(src))
    wp.init(); wp.set_device("cuda:0")
    sk = load_skeleton_from_usd(get_asset_file("human", "soma_base_skel_minimal_no_root.usd"), SOMA_SKELETON_PRIM_PATH)
    result = []
    with MotionBricksSOMAEngine(SOMA_N1_FP32_ONNX_PATH) as engine, wp.ScopedStream(engine.stream):
        for clip in data['clips']:
            if 'loco' in clip:                      # a gait: chained locomotion, one seamless cycle on the spot (mb_loco.py)
                import mb_loco
                pick = mb_loco.setting(clip['name'])
                r = mb_loco.run(engine, sk, clip, mb_loco.VARIANTS[pick], inplace=True)
                print('MB_GEN %s: one cycle of %d frames by locomotion (%s)' % (clip['name'], len(r['frames']) - 1, pick), flush=True)
                result.append({'name': clip['name'], 'fps': clip['fps'], 'frames': r['frames'], 'contacts': r['contacts'], 'loop': True})
                continue
            pick = PICK.get(clip['name'], policy) if policy == 'adaptive' else policy
            r = mb_lab.run(engine, sk, clip, mb_lab.VARIANTS[pick])
            if pick != policy: print('MB_GEN %s: %s (the lab\'s pick)' % (clip['name'], pick), flush=True)
            for g in r['gaps']:
                print('MB_GEN %s %d->%d: %s' % (clip['name'], g['from'], g['to'], 'kept ours' + (' (gentle clip)' if g.get('still') else '')
                      if g.get('kept') else '%d generated frames into %d (%s target poses)' % (g['generated'], g['to'] - g['from'], g['poses'])), flush=True)
            result.append({'name': clip['name'], 'fps': clip['fps'], 'frames': r['frames'], 'contacts': r['contacts']})
    json.dump({'clips': result, 'policy': policy}, open(dst, 'w'))
    print('MB_DONE', len(result), 'clips', policy, flush=True)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2], *sys.argv[3:4])
