# Visual review — framework build 0.4.0

Sources: native Unity captures from the Windows player (`Captures/RPG/*.png`, in-engine `ScreenCapture`, UI included),
isolated pose renders (`Captures/RPG/Poses`, 16 frames per clip, orthographic, transparent background), and the
installed SightForge / Claude Eyes inspector (`C:\Users\juana\AppData\Local\ClaudeEyesStudio\app`, run by
`Tools/review_sightforge.py`; reports in `Docs/ClaudeEyes/Inspections/rpg-*`, summary `Docs/sightforge-review.json`).

## What the inspector can and cannot certify
The installed inspector is sprite-oriented: it compares 2D silhouettes frame to frame (motion, holes, scale jumps,
ground line, frozen frames, loop closure). It **does not** certify 3D foot planting, collisions, hit volumes or
combat. Those are covered by the in-player verifier (`Captures/RPG/verification.txt`): planted-foot speed while
travelling, actual controller travel, skinned-mesh deformation, blade sweep timing and once-per-swing hits.
The inspector's own front-view note says it does not measure foot sliding for front/back views.

## SightForge results on the hunter pose renders
| Clip | Confirmed | Likely | Reading of the flags (frames inspected by eye) |
|---|---|---|---|
| Idle | 0 | 0 | clean |
| Walk | 0 | 0 | clean (foot sliding "not completed": front view) |
| Run | 0 | 0 | clean |
| Slash (attack) | 0 | 7 | Ground-line, scale-jump and "part missing" flags on frames 09–12 come from the sword arc widening the silhouette and the body twisting through the swing. Frames 08/09 compared side by side: body, shield and sword are all present; nothing is missing. Same 7 flags as the sample's baseline review of this authored KayKit clip. |
| Chop | 0 | 1 | Frame 10 is 93% of the median size: crouch at the bottom of the chop. Expected. |
| Dodge | 0 | 1 | "Loop does not close": the roll is a one-shot action, not a loop. Expected. |
| Negative control (one frozen walk frame ×16) | — | flagged | The inspector correctly detects a frozen walk. |

## 3D checks measured in the running player (not by the inspector)
- Planted foot speed while walking: 0.01 m/s against 1.60 m/s body travel (before the fix: 1.23 m/s, visible
  ice-skating). Travel speed is now derived from the authored stride measured from the foot bones.
- Walk clip deforms the skinned mesh (max vertex displacement 0.36 m between two frames).
- Sword is a child of the real `handslot.r` bone; monster swords are attached to their own `handslot.r`.
- Blade damage registered at 0.267 s into a swing whose active window is 0.26–0.40 s.
- 14 registered blade hits across the fights, 0 duplicates within a swing.

## Defects found by looking at captures, and fixed
| Found | Fix |
|---|---|
| 07:00 start lighting dim and muddy | daylight curve with full light 08:00–18:00; start at 08:00 |
| Large boulder spawned at the cottage door | prop clearance around the cottage widened |
| Repaired fence crossed the path to the porch | garden-gate gap |
| Camera ended up in front of the hunter after travel (boss off-screen in the crypt) | camera yaw convention corrected for teleports and doors |
| Crypt nearly black | crypt ambient and torch range raised |
| Buttons pale grey with pale text (colour tint not applied while paused) | explicit button colouring component |
| Focus highlight on the first tab looked like the active tab | focus starts on the active tab / selected row |
| Title backdrop showed tier-1 estate pieces before any game existed | title applies the starting estate |
| Interact chose Mira's mortar instead of Mira | mortar made set dressing; people preferred on ties |
| Helm break had no visible effect | rigid bone-attached meshes supported (hidden + physical piece) |
| Two loot sacks overlapping at one kill site | drops merge into one sack per site |
| Part-break loot could land behind the monster inside a wall (Warden in the crypt) | all drops land at the monster's feet |

## Known visual limitations (not fixed)
- KayKit chibi strides are short, so honest (non-sliding) travel is 1.6 m/s walking and 2.4 m/s running.
  Faster travel needs longer-stride run animations (art task).
- The upgraded blade is shown by a material tint on the same sword mesh; there is no unique Gravebane model.
- Armor pieces change stats only; the hunter mesh does not change with equipped armor.
- Damage numbers and loot sacks are simple primitives; no particle-system VFX library.
