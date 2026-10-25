# Handoff — Folklore Hunter: Rise of the Estate, RPG framework milestone

Build **0.4.0-framework** · Unity 6000.6.7f1 · Windows x64 (D3D11) · prepared 2026-10-25

## Where things are
| What | Path |
|---|---|
| Isolated Unity project (work happens here) | `C:\Users\juana\Documents\FolkloreHunter-RPG-Framework` |
| Windows player | `...\FolkloreHunter-RPG-Framework\Build-RPG\FolkloreHunterRPG.exe` |
| Desktop shortcut | `Folklore Hunter RPG - Framework Build` (OneDrive Desktop) |
| Gameplay video (74 s, 30 fps, in-engine capture with UI) | `...\Folklore-Hunter-RPG-Framework-Gameplay.mp4` |
| Verification results + captures | `...\Captures\RPG\verification.txt`, `verification-continue.txt`, `*.png`, `Poses\` |
| Real-input audit log | `...\Docs\real-input-session.txt` |
| Unity test results (52 tests) | `...\Docs\unity-test-results.xml` |
| SightForge review | `...\Docs\sightforge-review.json`, `...\Docs\ClaudeEyes\Inspections\rpg-*` |
| Source of truth for code/data/docs | git `juan310/chaos-world`, branch `claude/new-session-xoe3g1`, folder `FolkloreHunter-RPG-Framework/` (mirrored by `Tools\sync.ps1`; local clone `...\FolkloreHunter-RPG-Framework-src`) |
| Native art sources | `...\FolkloreHunter-RPG-Framework\SourceArt` (Cottage-Modular.blend, Hunter/Gravebound/Herbalist.blend from the sample; new Smith.blend, Noble.blend; KayKit and Kenney originals + zips) |
| Sample provenance (original docs, scripts, baseline checks) | `...\Docs\Provenance\Sample`, `...\Docs\Provenance\SampleScripts` |

Untouched: `FolkloreHunter-3D-Sample-20261025` (its `Build` and `Build-Werewolf`), the 2D project, Astralwilds.
The copy excluded Library, Temp, Logs, Builds and captures. Sample scripts (`FH3*.cs`) were moved out of `Assets`
into provenance because the framework replaces them; geography, palette, cottage handling and import settings were
ported (with the FBX_SCALE_ALL characters, the scale-one cottage wrapper and the door hinge axis preserved).

## Results
- **Core unit tests:** 52/52 pass in Unity's Test Runner (EditMode) and with `dotnet test` (Tools/CoreTests).
  Covers inventory/costs, vendor guards, crafting reasons and upgrades, equipment stats, progression and skills,
  estate transactions/income/storage, contract reward-once (including across save/load and tampering), quests,
  relationships/dialogue, combat timing (hit windows that never skip on long frames, once-per-swing, stamina,
  poise, statuses, weak points behind breakable parts, telegraph and grace, leash), spawns/clock, save round-trip,
  atomic backup recovery, corruption handling, schema refusal and v1→v3 migration, and the whole starter loop.
- **In-player verification (real Windows build, isolated save folder):** phase 1 play-through 126/126,
  phase 2 separate launch continuing the save 16/16. Movement, combat and menus are driven through the game's input
  router (the same path the keyboard uses) and real uGUI click events; the one labelled shortcut is Mira's bond
  being set to stand in for several days of gifts before the courtship steps.
- **Real input:** Windows key messages posted to the game window only (never to other windows on the shared
  desktop) were received by Unity's `Input`: menu navigation with arrows/Enter, walking, dodging, interacting,
  Escape/back. See `Docs/real-input-session.txt`.
- **Regression of the sample's 20 checks:** carried into the verifier (independent cottage parts, roof/walls/glass/
  door, no billboards, 350+ separate objects, terrain collider, rigged animated monsters, clips, sword on the real
  hand slot, skin deformation, controller travel, combat damage, loot/gathering/chest, rest, estate geometry).

## Honest status
See `CHECKLIST.md` for every module. In short: the connected starter loop and all twelve framework areas work
in the running game. Not done or not modeled:
- **No 3D werewolf.** Only the original 2D Sprite Studio frames exist (kept in `Assets/ArtReference/Werewolf`,
  not shipped). The werewolf is a data placeholder with a locked contract. A real encounter needs a modeled,
  rigged and animated 3D werewolf (separate asset dependency).
- **Estate tiers 2–3** (Hunter's Lodge, Ashgrove Manor) are data hooks only; the game refuses them with an
  in-world explanation. Only tier 1 has modeled pieces.
- Equipped armor changes stats but not the hunter's mesh; the Gravebane blade is a tint of the same sword mesh.
- KayKit locomotion strides are short. To keep planted feet still, travel is 1.6 m/s walking and 2.4 m/s running.
  Faster, Monster Hunter-like movement needs longer-stride run cycles.
- Romance, court and story content are framework-level only (one conversation chain, flags, factions).
- Gamepad works through the legacy Input Manager mappings for the main actions; no rebinding screen.
- The SightForge inspector is 2D/sprite-oriented; it did not and cannot certify 3D foot planting, collision or
  combat (those are measured in-engine). See `visual-review.md`.
- Additional regions (more woodland/wasteland, more towns, more dungeons) were intentionally not built yet.

## Next steps (suggested order)
1. Art: longer-stride run cycle and a heavier attack set; armor pieces as visible meshes; Gravebane blade mesh.
2. Werewolf: model, rig and animate a low-poly werewolf matching the KayKit style, then reuse the existing
   monster definition, parts (head weak point, breakable foreclaw) and the locked contract.
3. Estate tier 2 pieces in Blender (export like the cottage), set `modeled: true` in `content.json`.
4. Second region and dungeon using the same AreaDef/SpawnDef/AreaDoor pieces; more contracts in data.
5. Input System package (when approved) for rebinding and better gamepad support; the InputRouter is the seam.
6. Content: Mira's courtship scenes, court contracts, vendor rotations.

## Rebuild and re-verify
```
powershell -ExecutionPolicy Bypass -File Tools\sync.ps1
powershell -ExecutionPolicy Bypass -File Tools\unity.ps1 tests tests
powershell -ExecutionPolicy Bypass -File Tools\unity.ps1 FHBuild.Build build
powershell -ExecutionPolicy Bypass -File Tools\verify.ps1
python Tools\review_sightforge.py
powershell -ExecutionPolicy Bypass -File Tools\record_tour.ps1
```
No purchases, paid generation, asset-store imports, publishing or account changes were made. uGUI and the Test
Framework are built-in packages bundled with the installed editor (no download).
