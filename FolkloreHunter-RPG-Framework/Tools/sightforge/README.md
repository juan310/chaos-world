# SightForge development tools (Claude Eyes Studio 0.17)

Claude's own tools for building this game, added to SightForge / Claude Eyes Studio on the owner's PC with the owner's
permission (CLAUDE.md, "Development tools"). Everything is local and free. The source lives here; `install.py` copies it
into `%LOCALAPPDATA%\ClaudeEyesStudio\app`. Before it changes anything it backs up the whole app to
`backups\app_<old>_before_<new>_<date>`. Running it again only refreshes the modules.

    python Tools/sightforge/install.py
    cd %LOCALAPPDATA%\ClaudeEyesStudio\app
    python eyes_inspect.py --project-root <game> <command> ...

Each command writes a normal Claude Eyes report (`Docs/ClaudeEyes/Inspections/<run>/index.html` plus `report.json`).
Findings come in the usual kinds: confirmed, likely, uncertain, not completed.

| command | what it does |
|---|---|
| `mesh3d <model>` | (0.16) plays every clip and measures the skin: torn or pinched joints, held pieces inside the body, the body under the floor |
| `originality` | scans player-facing text (data display fields and on-screen strings in code) for other games' names. Extra terms and allowed texts go in `Tools/sightforge/originality.json` |
| `weaponfit <models> --kind blade\|bow\|pole\|shield` | reads a weapon model's long axis and cross-sections, finds its guard, hilt, tip and grip point, and checks hilt share, grip thickness and guard width; writes `weaponfit.json` |
| `smear <model> [--texture png]` | marks where a model's texture is smeared or stretched, by bone and facing (red: smeared, blue: too few texels), and suggests the side-profile repair for flanks |
| `hits <capture>` | every hit test the game recorded: each move's swings, landed hits, and for a miss how far short it fell and the nearest bone. Also each monster attack's ball against the hunter. Draws the misses from above and from the side |
| `problem <folder>\|latest` | an F9 capture: the screenshot with the engine's skeletons and hurtboxes drawn over it, the body-pose detector checked against the engine's joints, the state (clip, move, the ground under each foot, the log), and the hits, frames and springs reports for the seconds before |
| `perf <capture>` | frame times (median, p95, p99), hitches and the events that usually come just before them, garbage made each frame, draw calls, batches and set-pass calls, with charts |
| `springs <capture>` | spring-bone lag (tails, ears, capes): never trailing, pinned at its limit, jitter, never settling |
| `terrain <surveys>` / `terrain --mesh <model>` | maps: ground the hunter can never reach, holes, open ledges, scenery you walk through, floating props, invisible walls, overlapping surfaces, on a top-down map of each area. Mesh mode: open edges, non-manifold edges, upside-down faces, stacked faces, slivers |
| `footslide <model> [--fix out.fbx]` | in-place walks and runs: travel speed, how long each foot is down, slide. The fix holds the ball of each planted foot still on the moving ground (two-bone IK). Works on any humanoid rig |
| `motionbricks setup\|inbetween\|transition` | NVIDIA MotionBricks run on this PC (RTX 5080) for any humanoid rig. `inbetween` makes the motion between a clip's key poses again; `transition` makes a new clip from a moment of one move into a moment of another. Reports show before/after stick-figure strips and smoothness, and `--check` runs mesh3d on the result |

The game's side:

- **F9** (or Back + Start on the pad) saves the moment to `Captures/Problems/problem_<time>/`: `screen.png`, `state.json`, `hits.json` and `perf.json` (`DevCapture.cs`, `HitTrace.cs`). Then run `problem latest`.
- **F10** surveys the area the hunter is in (`TerrainSurvey.cs`). `--survey-terrain <folder> [--survey-terrain-quit]` surveys every area at start-up.
- Verification runs write `Captures/Proving/DevCapture/` and `Captures/RPG/DevCapture/` at their end, for `hits`, `perf` and `springs`.
- `SpringBone.cs` gives follow-through to tails, ears, capes, cloaks, tassels and banners, found by bone name. The Proving Grounds checks that the springs move, stay sane, and are not held at their limit.
- `Tools/slim/build_slim.py --footlock` runs the foot-slide fixer on every walk, jog and run before export. `slimall.ps1` passes it, together with `--smooth 4`.

Helpers: `devdraw.py` (Pillow views and charts), `rigmap.py` (humanoid bone roles for the slim rig, Mixamo, Unreal,
Rigify and SOMA), and `mb_env/` (the scripts that run inside the MotionBricks environment).
