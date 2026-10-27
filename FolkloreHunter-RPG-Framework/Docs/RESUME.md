# Where work paused (2026-10-27)

Everything below is committed on `claude/new-session-xoe3g1`. On the PC, run `Tools\sync.ps1` first.

## Done today
- SightForge 0.17 / 0.18 (installed on the PC, with backups in `ClaudeEyesStudio\backups`): originality, weaponfit, smear, hits,
  problem (F9), perf, springs, terrain (maps + `--mesh`), footslide (+ `build_slim --footlock`), motionbricks, worldforge.
  See `Tools/sightforge/README.md`.
- Models rebuilt (b12): `--smooth 4 --footlock`, legs-only flank repair for hunters and skeletons (`SourceArt/Repaired`),
  new Forge sword (V2) staged. The Forge V2 bow was put back to the crafted bow (it broke the live string and the aimed shot);
  V2 bow kept in `C:\AW\briarhound_work\weapons_forge_v2` until it gets a fitting pass.
- Proving check fixes (springs history, grounded-foot calibration) and hunt-move reach/radius tuned by the hit viewer's
  measured shortfalls (content.json). Core tests 120/120. These still need the next full PC build + Proving run.
- WorldForge (`WorldForge/` Unity package, sandbox `Documents\WorldForge-Sandbox`): builds the cave sample in about 2 s
  (222k triangles, 155 chunks, 10 fire lights, 10 puddles). Last fixes (walker sizing, tester folder) are committed but the
  automated test has not completed a full run yet.

## Next
1. PC: `Tools\sync.ps1`, then `C:\AW\briarhound_work\wf_run.ps1` (WorldForge build + test + video + report). Look at
   `Documents\WorldForge-Sandbox\Captures\cave_sample` (shots, `walkthrough.mp4`) and fix what the report finds.
2. Full game chain (`C:\AW\briarhound_work\build.ps1`): confirm the reach fixes (Shield Bash, Sheath Draw, Ember Cut,
   Thorn Rush), the spring and foot checks, the bow back to passing.
3. The user's requests not started yet:
   - WorldForge inside the Studio app UI (a WorldForge page: pick a recipe, adjust shapes/lights/weather, Build, Test,
     Play the preview, see the report and video), not only the command line.
   - Fingers: finger bones for the slim bodies (they have none: 21 bones, rigid hands), a grip solver per weapon, a
     SightForge `hands` check (3D finger-through-grip / gap, 2D hand-pose on screenshots).
4. Later: V2 bow fitting; `Running_Strafe_Right` remade (MotionBricks); crypt survey findings (walk-through pillars,
   floating props); hitch after weapon equips (perf report).
