# The MotionBricks lab, MotionBricks side: the same clips generated under several settings, so what each setting does can
# be measured and seen (mb_lab_eval.py, in Blender, measures and pictures them). Only our own poses go in (export.json
# from mb_export.py); the model's sample clips are evaluation-only and never used.
#
# For each clip, between each pair of its key poses (the first key's four poses before it are the context, as in
# mb_generate.py), a variant sets:
#   poses   how many of the target's four poses inform the model: 1 = arrive and settle there, 2 = arrive with its speed,
#           4 = arrive with its speed and momentum (MotionBricks' own description)
#   tokens  'auto': the model chooses the length (8-14 tokens, the default); 'fit': the one token count whose length is
#           nearest the gap between the key poses (each token is four poses, the valid frames 4n - 6, at least 18)
#   short   'mb': every gap goes to MotionBricks; 'keep': a gap shorter than MotionBricks' shortest motion (18 frames,
#           0.6 s) keeps our own keyposed frames there (MotionBricks would be squashed into it); 'merge': key poses
#           closer than that are dropped (the first, the last and the ones 18 frames apart stay), so MotionBricks fills
#           the longer gaps between the ones that stay at its own pace
#   mix     (optional) how much of MotionBricks goes into the result: 1 = all of it; less, our own in-betweens mixed in,
#           fading to ours entirely at every key pose (so the keys are exactly ours), the most MotionBricks mid-gap
# and the generated frames are then fitted to the gap (re-timed). Written per variant and clip: the frames, the foot
# contacts MotionBricks predicted (heel and toe, left and right, 0-1 per frame) and, per gap, what was generated.
#
# Run from the MotionBricks checkout:  uv run python <this file> export.json lab_out.json [variant,...]
import json
import sys
from pathlib import Path

import numpy as np
import warp as wp

sys.path.insert(0, str(Path(__file__).resolve().parent))
from mb_generate import window, retime, slerp                                    # noqa: E402
from motionbricks.io.usd import load_skeleton_from_usd                    # noqa: E402
from motionbricks.io.utils import SOMA_SKELETON_PRIM_PATH, get_asset_file  # noqa: E402
from motionbricks.model.config import MotionBricksInferenceConfig          # noqa: E402
from motionbricks.model.contract import MODEL_POSES_PER_TOKEN               # noqa: E402
from motionbricks.model.soma.contract import SOMA_N1_FP32_ONNX_PATH          # noqa: E402
from motionbricks.model.soma.engine import MotionBricksSOMAEngine            # noqa: E402

VARIANTS = {
    'base':   dict(poses=4, tokens='auto', short='mb'),     # what the game's pipeline does today
    'fit':    dict(poses=4, tokens='fit', short='mb'),
    'settle': dict(poses=1, tokens='fit', short='mb'),
    'speed':  dict(poses=2, tokens='fit', short='mb'),
    'hybrid': dict(poses=4, tokens='fit', short='keep'),
    'best':   dict(poses='end1', tokens='fit', short='keep'),  # momentum through the middle keys, settling on the last
    'merge':  dict(poses='end1', tokens='fit', short='merge'),
    'weave':  dict(poses='end1', tokens='fit', short='mb', mix=.5),
    'weave_m': dict(poses='end1', tokens='fit', short='merge', mix=.5),
}
MIN_FRAMES = 18                                             # MotionBricks' shortest motion (6 tokens: 4 x 6 - 6)


def merged(keys):
    """the key frames 18 or more apart (always the first and the last)"""
    out = [keys[0]]
    for k in keys[1:-1]:
        if k - out[-1] >= MIN_FRAMES and keys[-1] - k >= MIN_FRAMES: out.append(k)
    out.append(keys[-1])
    return out


def weave(gen, src, k0, mix):
    """MotionBricks' frames after k0 mixed with ours: ours at both keys, `mix` of MotionBricks mid-gap"""
    n = len(gen); out = []
    for j, g in enumerate(gen):
        w = mix * np.sin(np.pi * (j + 1) / n) ** 2 if n > 1 else 0.0
        a = src[k0 + 1 + j]
        out.append(np.concatenate([a[:, :3] * (1 - w) + g[:, :3] * w, slerp(a[:, 3:], g[:, 3:], w)], axis=1))
    return out



def fit_tokens(frames):
    """the token count (6-16) whose valid length (4n - 6 frames) is nearest `frames`"""
    return int(np.clip(round((frames + 6) / 4), 6, 16))


def run(engine, sk, clip, v):
    fps = clip['fps']
    frames = np.array(clip['frames'], dtype=np.float32)
    keys = [int(round(t * fps)) for t in clip['keys']]
    if v['short'] == 'merge': keys = merged(keys)
    context = engine.create_input(sk); target = engine.create_input(sk); output = engine.create_output(sk)
    out = [f for f in frames[:keys[0] + 1]]
    contacts = [[1, 1, 1, 1]] * len(out)
    ctx = window(frames, keys[0] - (MODEL_POSES_PER_TOKEN - 1))
    gaps = []
    for gi, (k0, k1) in enumerate(zip(keys, keys[1:])):
        gap = k1 - k0
        if v['short'] == 'keep' and gap < 18:
            seg = [f for f in frames[k0 + 1:k1 + 1]]                   # our own keyposed frames for a quick gap
            out += seg; contacts += [contacts[-1]] * len(seg)
            ctx = np.array(out[-MODEL_POSES_PER_TOKEN:], dtype=np.float32)
            gaps.append({'from': k0, 'to': k1, 'kept': True})
            continue
        last = gi == len(keys) - 2
        n = v['poses'] if v['poses'] != 'end1' else (1 if last else 4)
        cfg = MotionBricksInferenceConfig(allowed_token_counts=(fit_tokens(gap),)) if v['tokens'] == 'fit' else MotionBricksInferenceConfig()
        context.update_from_poses(wp.array(ctx, dtype=wp.transform), stream=engine.stream)
        target.update_from_poses(wp.array(window(frames, k1)[:n], dtype=wp.transform), stream=engine.stream)
        engine.generate_animation(context, target, output, config=cfg)
        engine.synchronize()
        m = output.num_frames
        gen = output.poses[:m].numpy().reshape(m, -1, 7)
        fc = output.foot_contacts.numpy()[:m]
        seg = retime(gen, gap)
        if v.get('mix') is not None: seg = weave(seg, frames, k0, v['mix'])
        # the contacts follow the same re-timing (nearest generated frame)
        cs = [fc[min(m - 1, int(round(j / gap * (m - 1))))].round(3).tolist() for j in range(1, gap + 1)]
        out += seg; contacts += cs
        ctx = np.array(out[-MODEL_POSES_PER_TOKEN:], dtype=np.float32)
        gaps.append({'from': k0, 'to': k1, 'generated': int(m), 'squash': round(m / gap, 2), 'poses': n,
                     'tokens': fit_tokens(gap) if v['tokens'] == 'fit' else 'auto'})
    out += [f for f in frames[keys[-1] + 1:]]
    contacts += [contacts[-1]] * (len(out) - len(contacts))
    return {'frames': np.array(out).round(5).tolist(), 'contacts': contacts, 'gaps': gaps}


def main(src, dst, names=None):
    data = json.load(open(src))
    names = names or list(VARIANTS)
    wp.init(); wp.set_device("cuda:0")
    sk = load_skeleton_from_usd(get_asset_file("human", "soma_base_skel_minimal_no_root.usd"), SOMA_SKELETON_PRIM_PATH)
    result = {'variants': {n: VARIANTS[n] for n in names}, 'clips': []}
    with MotionBricksSOMAEngine(SOMA_N1_FP32_ONNX_PATH) as engine, wp.ScopedStream(engine.stream):
        for clip in data['clips']:
            row = {'name': clip['name'], 'fps': clip['fps'], 'keys': clip['keys'], 'source': clip['frames'], 'variants': {}}
            for n in names:
                row['variants'][n] = run(engine, sk, clip, VARIANTS[n])
                print('MB_LAB %s %s %s' % (clip['name'], n, json.dumps(row['variants'][n]['gaps'])), flush=True)
            result['clips'].append(row)
    json.dump(result, open(dst, 'w'))
    print('MB_LAB_DONE', len(result['clips']), 'clips x', len(names), 'variants', flush=True)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2], sys.argv[3].split(',') if len(sys.argv) > 3 else None)
