"""The two-camera frame: compose renders into it, split video frames out of it, map pixels to metres and back.

Mirrors blender/common.py. Front view (left, 420 px) looks along +Y: u grows with X. Side view (right, 860 px) looks
along +X: u grows with -Y, so the head (-Y) is on the right. In both, v grows downward with -Z and the ground (z = 0)
is row 630.
"""
import json, os, pathlib

import numpy as np

H, W_FRONT, W_SIDE = 720, 420, 860
M_PER_PX = 1.9 / H
GROUND_V = 630
# MOCAP_CREATURE=<assets dir> with a creature.json "layout" overrides the wolf's framing (see blender/common.py)
_cfg = pathlib.Path(os.environ.get("MOCAP_CREATURE", "")) / "creature.json"
if os.environ.get("MOCAP_CREATURE") and _cfg.exists():
    _l = json.load(open(_cfg)).get("layout", {})
    W_FRONT, W_SIDE = _l.get("W_FRONT", W_FRONT), _l.get("W_SIDE", W_SIDE)
    M_PER_PX, GROUND_V = _l.get("M_PER_PX", M_PER_PX), _l.get("GROUND_V", GROUND_V)
W = W_FRONT + W_SIDE
Z_CENTER = (GROUND_V - H / 2) * M_PER_PX
BG, FLOOR, DIVIDER = (128, 130, 134), (152, 154, 158), (34, 34, 38)


def compose(front_rgba, side_rgba, bg=BG, floor=FLOOR, divider=DIVIDER):
    """Alpha-composite the two RGBA renders over the studio backdrop (grey wall, lighter floor band, divider)."""
    out = np.zeros((H, W, 3), np.float32)
    out[:] = bg
    out[GROUND_V:] = floor
    for img, x0 in ((front_rgba, 0), (side_rgba, W_FRONT)):
        img = img.astype(np.float32)
        a = img[..., 3:4] / 255.0
        h, w = img.shape[:2]
        out[:h, x0:x0 + w] = img[..., :3] * a + out[:h, x0:x0 + w] * (1 - a)
    out[:, W_FRONT - 2:W_FRONT + 2] = divider
    return out.clip(0, 255).astype(np.uint8)


def split(frame):
    """A 1280 x 720 frame (resize first if needed) into its front and side views."""
    return frame[:, :W_FRONT], frame[:, W_FRONT:]


def project(p):
    """World points (..., 3) to pixel coordinates in each view: front (u, v) and side (u, v)."""
    p = np.asarray(p, np.float64)
    v = H / 2 - (p[..., 2] - Z_CENTER) / M_PER_PX
    front = np.stack([W_FRONT / 2 + p[..., 0] / M_PER_PX, v], -1)
    side = np.stack([W_SIDE / 2 - p[..., 1] / M_PER_PX, v], -1)
    return front, side


def unproject_front(uv):
    """Front-view pixels to (x, z) in metres."""
    uv = np.asarray(uv, np.float64)
    return np.stack([(uv[..., 0] - W_FRONT / 2) * M_PER_PX, Z_CENTER + (H / 2 - uv[..., 1]) * M_PER_PX], -1)


def unproject_side(uv):
    """Side-view pixels to (y, z) in metres."""
    uv = np.asarray(uv, np.float64)
    return np.stack([-(uv[..., 0] - W_SIDE / 2) * M_PER_PX, Z_CENTER + (H / 2 - uv[..., 1]) * M_PER_PX], -1)
