# The MotionBricks side of the bridge (see mb_bridge.py). For each clip in export.json: the four poses leading into its
# first key pose are the context; each next key pose (four poses from that moment) is the target; MotionBricks generates
# the motion between them on the SOMA skeleton, and the result is re-timed so the key pose lands at its own time (the
# game times strikes by them). Each generation's last four played poses are the next one's context.
# Run from the MotionBricks checkout:  uv run python <this file> export.json mb_out.json
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


def main(src, dst):
    data = json.load(open(src))
    wp.init(); wp.set_device("cuda:0")
    sk = load_skeleton_from_usd(get_asset_file("human", "soma_base_skel_minimal_no_root.usd"), SOMA_SKELETON_PRIM_PATH)
    result = []
    with MotionBricksSOMAEngine(SOMA_N1_FP32_ONNX_PATH) as engine, wp.ScopedStream(engine.stream):
        context = engine.create_input(sk); target = engine.create_input(sk); output = engine.create_output(sk)
        for clip in data['clips']:
            fps = clip['fps']
            frames = np.array(clip['frames'], dtype=np.float32)
            keys = [int(round(t * fps)) for t in clip['keys']]
            out = [f for f in frames[:keys[0] + 1]]
            ctx = window(frames, keys[0] - (MODEL_POSES_PER_TOKEN - 1))
            for k0, k1 in zip(keys, keys[1:]):
                context.update_from_poses(wp.array(ctx, dtype=wp.transform), stream=engine.stream)
                target.update_from_poses(wp.array(window(frames, k1), dtype=wp.transform), stream=engine.stream)
                engine.generate_animation(context, target, output)
                engine.synchronize()
                gen = output.poses[:output.num_frames].numpy().reshape(output.num_frames, -1, 7)
                seg = retime(gen, k1 - k0)
                out += seg
                ctx = np.array(out[-MODEL_POSES_PER_TOKEN:], dtype=np.float32)
                print('MB_GEN %s %d->%d: %d generated frames into %d' % (clip['name'], k0, k1, len(gen), k1 - k0), flush=True)
            out += [f for f in frames[keys[-1] + 1:]]
            result.append({'name': clip['name'], 'fps': fps, 'frames': np.array(out).round(5).tolist()})
    json.dump({'clips': result}, open(dst, 'w'))
    print('MB_DONE', len(result), 'clips', flush=True)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
