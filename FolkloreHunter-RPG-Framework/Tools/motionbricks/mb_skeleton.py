# Writes the MotionBricks SOMA skeleton (joint names, parents, rest local transforms; Y up) to JSON, for the Blender side
# of the bridge. Run from the MotionBricks checkout:  uv run python <this file> <out.json>
import json, sys
import numpy as np
import warp as wp
from motionbricks.io.usd import load_skeleton_from_usd
from motionbricks.io.utils import SOMA_SKELETON_PRIM_PATH, get_asset_file

wp.init()
sk = load_skeleton_from_usd(get_asset_file("human", "soma_base_skel_minimal_no_root.usd"), SOMA_SKELETON_PRIM_PATH)
rest = sk.reference_local_transforms.numpy().reshape(len(sk.joint_names), -1)
out = {"names": sk.joint_names, "parents": sk.parent_indices.numpy().tolist(), "rest": rest.tolist()}
json.dump(out, open(sys.argv[1], "w"), indent=1)
print("MB_SKELETON", len(sk.joint_names), "joints:", " ".join(sk.joint_names))
