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

## Shield arm and the body gate (2026-10-27, evening)
- Every body is now built into `C:\AW\briarhound_work\staging` and installed only if SightForge's `bodycheck` passes
  (`slimall.ps1`; `gateonly` re-checks the staged bodies without rebuilding; slimall refreshes Studio's SightForge
  modules from git first, so the gate runs the code in git). The installed body is the baseline: only a clip worse than
  it fails; standing problems are listed as KNOWN.
- Shield arm in the sword-and-shield clips: a two-bone solve to the keyposed wrist (relative to the hips), the elbow nearest
  the keyposed elbow, each arm bone keeping the keyposes' roll. Blender's IK first tried wrung the wrist, then the elbow
  (the gate failed it twice); the solve keeps both. Shield and sword much less inside the body (male Idle_Combat 9.9 -> 0 cm,
  Slice_Diagonal 11 -> 1 cm, Rush_1 11 -> 1 cm). Both Tripo hunters passed and are installed.
- Still KNOWN on the male: tower shield into the legs in Lance_High_Thrust (10 cm), katana idle into the thigh (9 cm),
  nocked arrow into the shoulders in the bow shots (5-8 cm), SnS_Rush_2/3 and Shield_Crash (7-8 cm), skin in SnS_Rush_3 6.2 %.
- The female's skin tears far more (13-24 % of the body in attacks and bow clips, 8.6 % in her run): the Tripo download
  tears the same in its own clips (run 8.9 %), so it is Tripo's auto-skinning (waist sash, upper arms and thighs).
  `--skin-relax` (SightForge `skinfix_blender.py`) only took the mean from 5.15 to 4.86 %; the sash needs real
  reweighting (next).

## Troll (2026-10-28)
- The owner's troll sheet (`Docs/Concepts/Troll`): front and back crops through the local Forge (job
  `model_Troll_20261027_212910`, 4 min) -> textured, rigged mesh (`SourceArt/Troll/Troll_rigged.*` on the PC); flanks
  repainted from the sheet's side view and its mirror (`SourceArt/Repaired/Troll_sides_texture.png`).
- Built as `Monster_Troll` (slimall job: `--monster --pieces none --height 2.4 --skirt --skin-relax`), staged in
  `C:\AW\briarhound_work\staging\Monster_Troll`, **not installed**: the gate fails on skin. The loincloth tied to the thighs
  tore 7-29 % of the body; `--skirt` (new, SightForge) plus the relaxer brought the mean from 12.1 to 4.4 % (idle 3.4,
  walk 3.0, jog 5.5), but the hunter's attacks, dodges and bow moves still tear 6-25 % and look wrong on its short heavy
  legs (lunges stretch them). It needs its own moveset (heavy swings, slams, a club or bare fists), then the gate again.

## Next
1. Fingers: solve against the skin, not a centre-line cylinder (fingers stop at the piece's surface); check the index and
   ring order on the right hand; then build all bodies with `--fingers`, run `hands`, and check the grips in the game
   (Proving Grounds, each weapon, close camera).
2. Full game chain (`C:\AW\briarhound_work\build.ps1`): confirm the reach fixes, spring and foot checks, the bow passing.
3. Later: V2 bow fitting; `Running_Strafe_Right`; crypt survey findings; hitch after weapon equips.
