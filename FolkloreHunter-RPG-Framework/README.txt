FOLKLORE HUNTER: RISE OF THE ESTATE - RPG FRAMEWORK BUILD 0.8.0 (KATANA, EVASIONS, TRAINING RANGE, PALAMUTE-STYLE HOUND)
Isolated Unity 6 (6000.6.7f1) project built from the approved 3D sample. The sample, its two review
builds, the 2D project and Astralwilds are untouched.

NEW IN 0.8.0 - details: Docs\WEAPONS.md (top sections)
 - Training Range (title menu, or the ledger's travel button): archery targets at 10/20/30/40 m, unkillable training
   dummies, dodge hazards (two slam pads, a sweeping log) and a dodge lane marked every metre. Never saved.
 - Katana (two-handed, Monster Hunter's long sword): spirit gauge, Spirit Blade combo to the Spirit Roundslash
   (levels white/yellow/red, the edge glows), Foresight Slash (RB+B), iai stance (RB+A: Iai Slash, Iai Spirit Slash).
 - Evasions: roll, side slide and back-step (keep facing), dive when sprinting away; a perfect evade (dodged at the
   last moment) slows time briefly and gives back 20 stamina.
 - Bow animated like Monster Hunter's archer: held in the left hand, drawn to the cheek, nocked arrow, canted power
   shot, upward arc shot, Dragon Piercer, step clips.
 - The Briarhound fights and rides like the Palamute and thinks: bite/slash/spin combo, pounce, flanks across the
   monster, hops clear of wind-ups, punishes openings, cuts soft parts, steers round obstacles, keeps out of your
   attacks; ridden: lope, RT dash, RB drift, Y attacks, A sidestep, runs to a locked target, sidesteps wind-ups.
 - Movement at Monster Hunter pace in the hunt combat (jog 3.6 m/s, sprint 5.2 m/s, was 1.6 / 2.4), attacks on the
   press, quicker turning, tighter camera. Claude Eyes now reviews the hunter's clips as well as the hound's.

NEW IN 0.7.0 - details: Docs\WEAPONS.md
Two more Monster Hunter-style weapons in the Proving Grounds, picked at the ledger (Weapon):
 - Lance: mid/high thrusts in strings of three, wide sweep, tower-shield guard, guard thrust and guard dash,
   counter stance (a blocked blow becomes a Retribution Thrust), dash attack with finishing thrust, hops.
 - Bow: hold to draw through three charge levels, quick follow-up shots rise a level, charging sidestep,
   power shot, arc shot, Dragon Piercer, over-the-shoulder aim with a crosshair.

NEW IN 0.6.0 (test area first, not yet in the valley) - details: Docs\PROVING-GROUNDS.md
Title menu -> "Proving Grounds (combat test)": a walled ring that is never saved and gives no rewards.
 - Hunt-style sword and shield: light chain, hold-to-charge heavy, Y+B shield bash, roll cancels, guard with
   perfect guard and counter, hitstop/shake/rumble, flinch/knockdown, monster flinch/topple/enrage.
 - Xbox controller (XInput, rumble, pad menus, Xbox prompts). Keyboard + mouse still works.
 - The Briarhound: a rideable wolf-hound companion built in the studio from the concept art (heels, comes to
   the whistle, bites what you are locked on to; ride with X, gallop with RT).
 - The ledger by the south wall spawns test monsters and toggles training options and the frame-data overlay.
Player: Build-Proving\FolkloreHunterProving.exe   Shortcut: "Folklore Hunter RPG - Proving Grounds Build"
Check: Tools\verify_proving.ps1, then python Tools\review_hound_sightforge.py (Claude Eyes on the hound)

PLAY
Desktop shortcut: "Folklore Hunter RPG - Framework Build"
Player: Build-RPG\FolkloreHunterRPG.exe   (saves: %USERPROFILE%\AppData\LocalLow\Folklore Hunter\Folklore Hunter - Rise of the Estate\Saves)

WASD move   Shift run   Left mouse attack (press in rhythm for a 3-hit combo)   Space dodge
Q spell   F change spell   Tab / middle mouse lock-on   E interact   1-4 hotbar (R = slot 1)
I pack   J hunts & story   K hunter (stats, skills)   M map   Esc menu / back
Right mouse drag: orbit camera   Wheel: zoom.  Menus also work with arrow keys + Enter.

THE STARTER LOOP
Cottage -> Thornbury Crossing (south-west) -> talk to Mira, accept "Bones by the Old Bridge"
-> cross the bridge, fight the Gravebound (watch for the raised blade, red eyes and red ring; dodge, then punish)
-> crack a helm to expose the skull weak point -> collect bones from the remains -> report to Mira
-> forge the Gravebane Shortsword at Bram's forge -> buy timber and nails from Bram
-> restore the cottage at the estate ledger -> sleep at the cottage door (saves) -> quit -> Continue.
Further: the Warden's Crypt beneath the ruined tower (Bronze rank), Silver rank and Lady Elowen's court,
Bram's repeatable Bone Harvest, estate storage/workbench/rent, Mira's friendship and courtship.

VERIFY / BUILD (PowerShell)
Tools\sync.ps1          pull the framework source from git into this project
Tools\unity.ps1 FHBuild.Build build      regenerate the scene and build Build-RPG
Tools\unity.ps1 tests tests              run the 53 Core tests in Unity (Docs\unity-test-results.xml)
Tools\verify.ps1        two-phase in-player verification (Captures\RPG\verification*.txt)
Tools\record_tour.ps1   gameplay video (Folklore-Hunter-RPG-Framework-Gameplay.mp4)
Tools\review_sightforge.py   SightForge / Claude Eyes: pose clips, actor tracks from real play, sample comparison
                              (any run can record a track: FolkloreHunterRPG.exe --eyes-track <file.jsonl>)

DOCUMENTS
Docs\HANDOFF.md        status, results, limitations, next steps
Docs\CHECKLIST.md      implemented / partial / placeholder / missing, per module
Docs\ARCHITECTURE.md   module map and data flow
Docs\visual-review.md  captures, SightForge findings and what they mean
Docs\Provenance\       the sample's original docs, scripts and baseline checks

ASSETS
Cottage: custom Blender geometry (SourceArt\Cottage-Modular.blend). KayKit Adventurers and Skeletons and
the Kenney Nature Kit are CC0 (licenses in Docs\Assets). Smith and Noble NPC bodies were exported from the
KayKit Barbarian and Rogue_Hooded sources (SourceArt\Smith.blend, Noble.blend) with the same FBX_SCALE_ALL
settings as the sample. The werewolf exists only as 2D comparison frames (Assets\ArtReference\Werewolf) and a
locked data placeholder: there is no 3D werewolf.
