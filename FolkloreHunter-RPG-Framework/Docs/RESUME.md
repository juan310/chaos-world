# Where work stands (2026-10-27, afternoon)

Everything below is committed on `claude/new-session-xoe3g1`. On the PC, run `Tools\sync.ps1` first.


## The Barrow Troll built from math (2026-10-28, evening)
- `Tools/slim/troll_math.py` (Blender) makes the troll with no generated mesh. The body is an implicit surface:
  Blender metaballs, 142 balls, capsules and ellipsoids laid along its own skeleton. It is rebuilt as an even 2 cm
  skin (voxel remesh: the field's own grid slivers read as torn skin) and skinned by the same fields that made it.
  The loincloth, rope belt, wrist wraps, tusks, eyes and hair are made by math too. The colours are painted by noise
  (mottled grey-green skin, pink nose and ears, dark nails, patched leather) and baked to one texture.
- It is built into `SourceArt\Troll\math` and goes through `slimall` (job `Monster_Troll`, `--moves troll_moves`)
  like any body. On the PC: `C:\AW\briarhound_work\math_troll.ps1 [game]`.
- Gate: PASS, judged without a baseline (`slimall ... nobaseline`, a new model replacing a different one). Worst
  moves: Troll_Smash 5.6 % (watch), Troll_Swipe 4.7, Death_A 4.4; folds 1.3 % or less. Proving: all 6 troll checks
  pass (225 checks, 10 failures, all the known hunter ones). Close-ups reviewed: it faces its way, walks forward, bends
  forward into the pound, no holes.
- The Forge troll it replaces had its skeleton in backwards (toe bones in its heels, left bones on its right).
  SightForge now checks the facing of every build (`skinfix_blender.facing`, logged as SLIM_FACING; `--face-fix`
  turns the body round). It is kept as job `Monster_TrollForge` and in
  `C:\AW\briarhound_work\models_before_math_troll_20261028`.
- Body gate: "skin folded inside out" is reported as a watch item. The weight writer now removes cleared weights.
  `--untangle` frees hands grown into thighs.
- To do: the face and fingers are softened by the 2 cm skin (a finer skin or sharper features); a small light patch
  on the loincloth in the backhand.

## Two-view capture (option A, local and free) and the MotionBricks lab
- `Tools/capture2v/`: anymove's local parts are vendored (Apache-2.0, see its README). The tool covers prep, film
  (Wan 2.2 on this PC, the VAE untiled), dataset, train, read and solve (`capture.ps1`), and `to_clip.py` turns a
  capture into a clip on our skeleton. Not run end to end yet: the first film crashed in the VAE tiling, now fixed and
  not yet re-run. Next: prep on the math troll, film a roar, check that the two views stay in step.
- MotionBricks lab, rounds 1-3: `Docs/MOTIONBRICKS_PLAYBOOK.md`. The pipeline's default is now `adaptive`, which takes
  effect at the next `mbrun` regeneration of the hunters (not run yet).

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

## The Barrow Troll, a drawn monster (2026-10-28)
- The owner's troll sheet animated by cut-out animation (`Tools/sprites/troll_puppet.py`: the side view cut into parts on a
  2D skeleton, moved by math; `game` exports `Assets/Resources/Sprites/Troll`: 78 frames and sprite.json with the joints).
- In the game as `barrow_troll` (model `Sprite_Troll`): `SpriteActor` shows a camera-facing card (shader
  `Folklore/SpriteCard`, cut-out, lit, casts a shadow, mirrored with its heading) over bones whose legacy clips the
  AnimationDriver plays like a model's, so the brain, strike timing and hurtboxes work unchanged. Guards the Old Bridge
  (`sp_troll_bridge`), in the Proving Grounds roster. Proving: 7 troll checks pass (226 checks, the same 10 older failures);
  the fight is recorded to `Captures/Proving/TrollFight`. To do: send the hound away while recording; the hit flash is strong.
- Three views (2026-10-28): front and back from the sheet (`Tools/sprites/troll_views.py`, same moves and timing as the
  side); `SpriteActor` shows the front when the troll heads within 50 deg of the camera, the back beyond 130, the side
  between (margins against flicker). Proving: 8 troll checks pass (227 checks, 8 failures). The frames (~55 MB) are not in
  git: `sync.ps1` runs `Tools/sprites/make_sprites.py`, which makes them in the project when the scripts or sheet change.
- `sync.ps1` used to copy `Assets/Resources/Sprites` (now made on the PC instead); `build.ps1` stops when the scripts do not compile (a Unity build would
  otherwise run the old scripts); `build_quick.ps1` = sync, compile, Unity build, Proving.

## Next
1. Fingers: solve against the skin, not a centre-line cylinder (fingers stop at the piece's surface); check the index and
   ring order on the right hand; then build all bodies with `--fingers`, run `hands`, and check the grips in the game
   (Proving Grounds, each weapon, close camera).
2. Full game chain (`C:\AW\briarhound_work\build.ps1`): confirm the reach fixes, spring and foot checks, the bow passing.
3. Later: V2 bow fitting; `Running_Strafe_Right`; crypt survey findings; hitch after weapon equips.
