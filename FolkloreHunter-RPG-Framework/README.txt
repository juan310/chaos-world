FOLKLORE HUNTER: RISE OF THE ESTATE - RPG FRAMEWORK BUILD 0.4.0
Isolated Unity 6 (6000.6.7f1) project built from the approved 3D sample. The sample, its two review
builds, the 2D project and Astralwilds are untouched.

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
Tools\unity.ps1 tests tests              run the 52 Core tests in Unity (Docs\unity-test-results.xml)
Tools\verify.ps1        two-phase in-player verification (Captures\RPG\verification*.txt)
Tools\record_tour.ps1   gameplay video (Folklore-Hunter-RPG-Framework-Gameplay.mp4)
Tools\review_sightforge.py   SightForge / Claude Eyes review of pose captures

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
