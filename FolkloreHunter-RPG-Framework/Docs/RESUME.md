# Where work stands (2026-10-27, afternoon)

Everything below is committed on `claude/new-session-xoe3g1`. On the PC, run `Tools\sync.ps1` first.

## Done
- SightForge / Studio **0.19** (installed; backup `ClaudeEyesStudio\backups\app_0_18_before_0_19_20261027`):
  - **WorldForge page** under Tools: recipes edited as values (with a line of help per value) or as JSON, Save (old version
    kept in `WorldForge\Recipes\.history`), Build & test in the background, Play the preview, Last test (summary, findings,
    walk video, viewpoints, map). Driven end to end in a real browser by `Tools/sightforge/studio/wf_page_test.mjs`
    (all checks pass); Studio's self-test passes with four new WorldForge checks.
  - The game folder was added to Studio's project list (`projects.json`, old copy `projects.json.before_worldforge_20261027`).
  - `hands` check (new): a model's grips put on the skinned hand and measured against each held piece, pictures of each grip.
- WorldForge cave sample: full automated test done (walk 86.3 of 87.1 m, 0 stuck; collision 0 of 1999 misses; boss path
  partial by design; frame times 1.6 to 4.1 ms). Survey fixes: rock-filled columns are no longer holes or ledges; the box's
  own sides are one finding; feet judged at the planted foot (9.9 cm, resting 5.0 cm). Report: 4 likely, 6 worth a look.
- `Tools\sync.ps1` keeps WorldForge recipes edited on the PC (it no longer copies git's version over them).
- Fingers (trial, **not in the game yet**): `build_slim --fingers` finds five fingers per hand in the mesh (seams welded,
  tips by persistence), adds 3 bones per finger, skins them, solves a grip per held piece and writes
  `<model>_grips.json`; `HandGrip.cs` applies it in the game after the clip, but only for a model that has a grips file
  (none in `Assets\Resources\Models` yet). Trial model: `C:\AW\briarhound_work\finger_try`; report
  `Docs\ClaudeEyes\Inspections\hands_trial`. Relaxed hand, bow-string hook, sword and katana grips look right; lance, bow
  and shields close to a fist that sinks 1 to 4 cm into their thick handles (22 "likely" findings).

## Tripo hunters (2026-10-27, later)
- The owner's Tripo downloads replace both slim hunters: `SourceArt\Tripo\MaleHunter_Tripo_Animated.fbx` and
  `FemaleHunter_Tripo_Animated.fbx` on the PC (not in git), built by `C:\AW\briarhound_work\slimall.ps1` with
  `--height 1.83/1.72 --own-hair --fingers --keep-clips ...` (the female's Idle comes from the male pack: `--lib-clips`).
  The Codex bodies they replaced are kept in `C:\AW\briarhound_work\models_before_tripo_20261027`.
- Tripo's own clips play as Idle (male), Walking_A/Gait_Walk, Running_A/Gait_Jog/Hunt_Jog, Dodge_Forward, Idle_Combat,
  plus Dig and Run_Upstairs; every other move stays the game's own (attacks, bow, blocks...).
- SightForge motion library (`motionlib add/list/show`, `%LOCALAPPDATA%\ClaudeEyesStudio\motionlib`): packs
  `tripo_male` (7 clips) and `tripo_female` (6). Any slim body takes them with `build_slim --lib-clips`.
- First game run with the Tripo male (1 m tall build): 219 checks, 7 failures; weapons floated off the hands (fixed by
  building at real height), a hairstyle sat on the modelled hair (fixed: `_info.json` ownHair). Next run verifies both.

## Next
1. Fingers: solve against the skin, not a centre-line cylinder (fingers stop at the piece's surface); check the index and
   ring order on the right hand; then build all bodies with `--fingers`, run `hands`, and check the grips in the game
   (Proving Grounds, each weapon, close camera).
2. Full game chain (`C:\AW\briarhound_work\build.ps1`): confirm the reach fixes, spring and foot checks, the bow passing.
3. Later: V2 bow fitting; `Running_Strafe_Right`; crypt survey findings; hitch after weapon equips.
