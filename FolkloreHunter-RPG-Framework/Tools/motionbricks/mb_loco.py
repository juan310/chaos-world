# MotionBricks locomotion: our gait cycles (walk, jog, sprint, walking back, strafes, sneak, crouch walk) made natural by
# MotionBricks the way its own docs drive locomotion: the body travels, the targets are poses placed ahead along the
# way, all four target poses given so it matches the speed at each, and generation is chained (each one's last four
# played poses are the next one's context). Only our own poses go in.
#
# mb_bridge.export hands over each gait as LOCO_CYCLES cycles carried forward at the speed its planted foot slides
# back. Here, from the context ending one cycle in, MotionBricks generates to our pose one cycle on, then to the next:
# the first generation settles it into the stride, and the second becomes the clip. That one starts on our cycle's
# pose and ends on the same pose one cycle further on (MotionBricks ends exactly on its target), so with the travel
# taken off it loops without a seam. For the game the travel is taken off (the game moves the body); for the lab the
# clip stays travelling, so foot slide can be measured where the feet should be still.
#
# Settings (VARIANTS): poses = target poses used (4: speed and momentum, as its docs advise for locomotion);
# tokens = 'fit' (the cycle's own length) or 'auto' (MotionBricks picks its cadence, re-timed to ours).
import numpy as np
import warp as wp

from mb_generate import window, retime
from motionbricks.model.config import MotionBricksInferenceConfig
from motionbricks.model.contract import MODEL_POSES_PER_TOKEN

VARIANTS = {
    'loco4': dict(poses=4, tokens='fit'),
    'loco2': dict(poses=2, tokens='fit'),
    'loco_auto': dict(poses=4, tokens='auto'),
    'loco4_arms': dict(poses=4, tokens='fit', upper='arms'),      # the bow drawn while walking, the lance held level
    'loco2_arms': dict(poses=2, tokens='fit', upper='arms'),
    'loco2_torso': dict(poses=2, tokens='fit', upper='torso'),    # our hunch kept (the sneak, the crouch walk)
    'loco4_raw': dict(poses=4, tokens='fit', correct=False),       # without its spreading of the end correction
    'loco4_ground': dict(poses=4, tokens='fit', ground=True),      # every frame lifted back onto the floor
}
DEFAULT = 'loco2'
# per gait, what the lab films chose (Docs/MOTIONBRICKS_PLAYBOOK.md, locomotion): two target poses keep the planted feet
# the steadiest after the foot lock; the sprint holds its long flight stride better with all four, kept on the floor
# (a weapon held still while walking keeps our arms over MotionBricks' legs)
PICK = {'Gait_Sprint': 'loco4_ground', 'Bow_Aim_Walk': 'loco2_arms', 'Lance_Charge': 'loco2_arms'}


def setting(name):
    return PICK.get(name, DEFAULT)


def fit_tokens(frames):
    return int(np.clip(round((frames + 6) / 4), 6, 16))


def run(engine, sk, clip, v, inplace=True):
    """-> {'frames': one cycle (N + 1 frames, the last the first again), 'contacts', 'source': our cycle the same way,
    'gaps'}; travelling unless inplace"""
    F = np.array(clip['frames'], dtype=np.float32)
    N = clip['loco']['cycle']
    d = (F[N, 0, :3] - F[0, 0, :3]) / N                    # the travel per frame (the root's position, model space)
    context = engine.create_input(sk); target = engine.create_input(sk); output = engine.create_output(sk)
    ctx = window(F, N - (MODEL_POSES_PER_TOKEN - 1))
    out, cts, gaps = [], [], []
    for k in (2, 3):
        kw = {} if v.get('correct', True) else {'correct_generated_diff': False}
        cfg = MotionBricksInferenceConfig(allowed_token_counts=(fit_tokens(N),), **kw) if v['tokens'] == 'fit' else MotionBricksInferenceConfig(**kw)
        context.update_from_poses(wp.array(ctx, dtype=wp.transform), stream=engine.stream)
        target.update_from_poses(wp.array(window(F, k * N)[:v['poses']], dtype=wp.transform), stream=engine.stream)
        engine.generate_animation(context, target, output, config=cfg)
        engine.synchronize()
        m = output.num_frames
        gen = output.poses[:m].numpy().reshape(m, -1, 7)
        fc = output.foot_contacts.numpy()[:m]
        seg = retime(gen, N)
        cs = [fc[min(m - 1, int(round(j / N * (m - 1))))].round(3).tolist() for j in range(1, N + 1)]
        gaps.append({'from': (k - 1) * N, 'to': k * N, 'generated': int(m), 'squash': round(m / N, 2), 'poses': v['poses']})
        start = out[-1] if out else F[(k - 1) * N]
        if k == 3:                                        # the clip: from our pose at 2N (where the warm-up landed) to 3N
            out = [np.array(start)] + seg; cts = [cs[0]] + cs
        else:
            out = seg
        ctx = np.array(out[-MODEL_POSES_PER_TOKEN:], dtype=np.float32)
    clip_frames = np.array(out)
    src = F[2 * N: 3 * N + 1].copy()
    if v.get('upper'):
        from mb_lab import keep_upper, upper_joints
        clip_frames = keep_upper(clip_frames, src, upper_joints(list(sk.joint_names), v['upper']))
    if v.get('ground'):
        clip_frames = grounded(clip_frames, src, sk)
    # the clip and our cycle both start where our cycle does (frame 0); in place, the travel is taken off as well
    for arr in (clip_frames, src):
        for i in range(len(arr)):
            arr[i, 0, :3] -= d * (2 * N + (i if inplace else 0))
    return {'frames': clip_frames.round(5).tolist(), 'contacts': cts, 'gaps': gaps, 'source': src.round(5).tolist(),
            'loop': True}


def grounded(frames, src, sk):
    """each frame lifted (never lowered) so its lowest heel or toe is no lower than ours ever goes"""
    from mb_lab_eval import fk
    par = {'parents': list(sk.parent_indices.numpy())}
    names = list(sk.joint_names)
    feet = [names.index(n) for n in ('LeftFoot', 'LeftToeBase', 'RightFoot', 'RightToeBase')]
    floor = fk(par, src)[:, feet, 1].min()
    P = fk(par, frames)
    low = P[:, feet, 1].min(1)
    out = np.array(frames)
    out[:, 0, 1] += np.maximum(0, floor - low)
    return out
