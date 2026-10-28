# Vendored from anymove (Apache-2.0)

Upstream: https://github.com/Patch-gg/anymove (Patch). See `UPSTREAM_COMMIT` and `LICENSE` (Apache-2.0).
The two-camera method is credited upstream to @CanItCode. The Patch name and marks are not covered by the licence and
are not used by this game.

Only the parts that run locally and free are copied:
- `mocap/`: `layout`, `model`, `detect`, `train` and `solve`.
- `blender/`: `common`, `render_dataset` and `apply_motion`.

The fal client and every paid step are left out. Our own scripts in `Tools/capture2v/` replace the parts that are
specific to the creature and to video generation:
- the clips come from Wan 2.2 TI2V-5B (Apache-2.0), run locally;
- the rig is the game model's own skeleton.

Changes to these files are listed below, file by file, as the Apache-2.0 licence asks.

## Changes
(none yet)
