# Build 0.6.0 — Proving Grounds, hunt combat, Xbox controller, the Briarhound

Everything here is a **test area first**: the Proving Grounds are a separate walled ring reached from the title menu.
Nothing in it is saved, it gives no rewards, and the valley/story game is unchanged until the combat, movement and
mount are signed off. Player: `Build-Proving\FolkloreHunterProving.exe`, shortcut
"Folklore Hunter RPG - Proving Grounds Build".

## What is in the ring
- **Hunt-style combat (sword and shield)**, data-driven (`content.json` → `huntMovesets`): light chain
  (slash → slash → chop, lunge), heavy chop with a hold-to-charge (three levels, the third becomes a spin),
  rising slash, Y+B shield bash, draw attack out of a sprint, roll slash, guard with a perfect-guard window and
  counter. Motion values, hitstop, camera shake and rumble per move; chain and roll-cancel windows; a 0.5 s input
  buffer; attacks cost no stamina (rolling, guarding and sprinting do).
- **Getting hit:** flinch, knockback and knockdown with a get-up (or a wake-up roll).
- **Monsters under hunt rules:** flinch build-up, topple (downed, with its own get-up), enrage below half health
  (faster, shorter cooldowns, glowing eyes), soft and hard hitzones shown by number colour.
- **Sheathe/draw:** weapons on the back, sprinting sheathes, X sheathes, attacking draws.
- **The ledger** (south wall): spawn Gravebound, Grave Warden, Thornback or Mirejaw; clear; passive monsters;
  infinite stamina; invulnerable; hit volumes; frame-data overlay (move, phase, windows, hitstop, hitzone,
  build-ups, live pad input); reset; controls; leave.
- **The Briarhound**, a rideable wolf-hound companion (below).

## Controls
| Action | Xbox | Keyboard + mouse (ring) |
|---|---|---|
| Move / camera | left stick / right stick | WASD / mouse (cursor locked) |
| Light / heavy (hold to charge) | Y / B | left mouse / right mouse |
| Shield bash | Y + B | both buttons |
| Roll | A | Space |
| Guard | RB | Ctrl |
| Sprint (sheathes) | RT | Shift |
| Interact, sheathe, mount / dismount | X | E (interact), X (sheathe) |
| Lock on | R3 | Tab / middle mouse |
| Use item / change item | LB / D-pad left-right | 1-4 |
| Whistle the Briarhound | D-pad up | G |
| Spell / change spell | LT / D-pad down | Q / F |
| Pause / map | Menu / View | Esc / M |
Riding: left stick steers, RT gallops, X dismounts. A monster blow while riding throws the rider.
Menus work on the pad (D-pad, A, B). XInput is read directly (no package), with rumble; the prompts switch to
Xbox labels when the pad was used last.

## The Briarhound (mount and companion)
From the concept art the user supplied (`Docs/Concepts/Briarhound-concept.png`, turnaround
`Briarhound-turnaround.png`), built locally, no paid generation:
1. **Studio (ClaudeEyesStudio Forge, local TripoSG on the RTX 5080):** `static` job from the concept → 40k-triangle
   mesh, 2k texture (`%LOCALAPPDATA%\ClaudeEyesStudio\Forge\static_Briarhound_20261025_194800`). Gates: shape,
   outline (IoU 0.835) and closed surface pass; the texture gate failed because only the concept's side was seen
   (30% from the picture, 70% guessed).
2. `Tools/briarhound_prep.py` (Blender): faces -Y, paws on the ground, 1.9 m tall; finds the four paws.
3. `Tools/briarhound_texture.py`: the far flank takes the mirrored concept-side texture (71% of it; the turnaround's
   rear 3/4 confirms both flanks carry a saddle bag, stirrup and bedroll); the neck fur that the art hid behind the
   blade is refilled from the fur around it.
4. `Tools/rig_briarhound.py` (Blender): the concept's head looks at the viewer (~70°); it is turned to face forward,
   which also puts the crescent blade crosswise in the jaws. 25 bones measured from the mesh (Cu-sith names, plus a
   `spine` under the saddle and the `saddle` seat bone); distance-to-bone skin with anatomical gates, the blade rigid
   with the head, 8 influences. Clips with IK on all four paws: Idle, Walk (trot), Gallop (rotary), Bite (blade slash),
   Sit, Howl, Hit. Measured in Blender: paws never sink, no seam opens, saddle bob 1.8 cm walking and 8.5 cm galloping.
   Previews: `Captures/Briarhound`. Export: `Assets/Resources/Models/Briarhound.fbx`, material from
   `Assets/Resources/Monsters/Briarhound_basecolor.png` (made at build time by `ReferenceMonsterAssets.PrepareCompanion`).
The older procedural Cu-sith (`Tools/build_cusith.py`) stays as the fallback when the Briarhound model is absent.

Gait speeds come from the clips themselves: `AnimationDriver.NaturalSpeed` measures how fast a planted paw sweeps
back, so the play rate keeps the paws planted at the speed actually travelled.

## Verification
`Tools\verify_proving.ps1` drives the real player with injected Xbox pad states. Results:
`Captures\Proving\verification.txt`; pose frames for Claude Eyes in `Captures\Proving\HoundPoses`;
`python Tools\review_hound_sightforge.py` → `Docs\sightforge-hound-review.json`.
RESULTS_PLACEHOLDER

## Limits (honest)
- The texture's far side is mirrored concept art, not painted for that side; the turned neck is refilled fur, softer
  than the hand-painted areas. A painted far side (or a second studio pass from the rear view) would improve it.
- The hound's collision is an upright capsule (CharacterController); its long body and tail do not block the hunter.
- Claude Eyes is a 2D/sprite inspector: it checks the rendered frames and the motion track; 3D paw planting and
  the rider's seat are measured in-engine, not certified by it.
- One moveset (sword and shield). Other weapon types need their own move data and animations.
