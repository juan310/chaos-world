# Implementation checklist — Folklore Hunter RPG framework

Results for build 0.4.0: Core tests 53/53 (Unity Test Runner and dotnet); in-player play-through 127/127; relaunch/continue 16/16 (four consecutive runs of the final build); real Windows key input exercised (`Docs/real-input-session.txt`).

Legend: **Done** = implemented and verified in the running Windows player (see `Captures/RPG/verification*.txt`) and/or by the Core unit tests (`Docs/unity-test-results.xml`, `Tools/CoreTests`).
**Partial** = works, with the stated gap. **Placeholder/hook** = data and code path exist, content or art does not. **Missing** = not built.

## 1. Runtime foundation and data
| Item | Status | Evidence / notes |
|---|---|---|
| Stable ids + data-driven definitions (items, weapons, armor, spells, statuses, monsters, contracts, quests, recipes, estate tiers, NPCs, factions, ranks, skills, attributes, vendors, dialogue, areas, spawns) | Done | `Assets/Resources/Data/content.json`; validated on load, test `ShippedContentValidatesWithoutErrors` |
| Authored definitions separate from mutable save data | Done | `Definitions.cs` vs `GameState.cs`; saves hold ids/numbers only |
| One inventory / wallet / stats / progression state | Done | `GameCore` composition root; no global manager |
| Events / interfaces where useful | Done | `GameEvents`, `Result`, `IRandom`, `IConsumer` |
| Content validation (refs, invariants) | Done | `GameDatabase.Validate`, test `ValidatorCatchesBrokenReferences...` |

## 2. Player and hunting combat
| Item | Status | Evidence / notes |
|---|---|---|
| Movement, run with stamina, camera orbit/zoom | Done | verifier movement + run checks |
| Locomotion playback matched to stride (no foot sliding) | Done | planted foot 0.01 m/s vs 1.60 m/s travel (was 1.23 before the fix); trade-off: KayKit strides cap honest speed at 1.6 walk / 2.4 run |
| Attack windup / active / recovery from data, deterministic timeline | Done | `AttackTimeline`; verifier: damage lands at 0.267 s inside 0.26–0.40 active window |
| Three-hit combo with buffered input in recovery | Done | verifier "press during recovery chains the combo" |
| Real blade hit volume (sword mesh endpoints under `handslot.r`, swept capsule) | Done | `HunterController.SweepBlade` |
| Once-per-swing damage | Done | `SwingHits` unit test + per-swing hit log in verifier |
| Stamina costs, regen delay, exhaustion | Done | unit tests + HUD |
| Dodge with invulnerability window (stat-driven) | Done | verifier "dodging through a telegraphed strike takes no damage … evaded by invulnerability" |
| Health, magic, potions/hotbar, spells (Ember Bolt with burn, Hallowed Lance via skill) | Done | |
| Lock-on with reticle + target HUD | Done | verifier lock-on checks |
| Hurt / death reactions, stagger / poise, resistances, statuses | Done | unit tests; Hit_A / Death_A clips |
| Death and recovery (carried home, 10% gold lost) | Done | verifier |
| Damage independent of animation asset | Done | timeline drives clips via measured strike time |
| Feet / hips / grip visual validation | Done (with stated limits) | in-engine foot/travel measurement, sword on `handslot.r`, pose frames reviewed in SightForge (2D inspector: no 3D certification) — see `visual-review.md` |

## 3. Monster framework
| Item | Status | Evidence / notes |
|---|---|---|
| Idle / patrol / perception (cone, hearing, line of sight) / chase / telegraph / attack / recovery / stagger / death | Done | `MonsterBrain` unit tests; verifier |
| Readable telegraph (raised weapon, red eyes, ring sized to the hit volume) | Done | verifier capture `06-telegraph.png` |
| Leash / reset; no unavoidable attack after spawn, load, pause | Done | unit tests + verifier leash and post-load checks |
| Weak point + breakable part on real geometry (Gravebound helm → exposed skull) | Done | helm mesh detached as physics piece; grave iron drop; remembered per spawn |
| Loot tables | Done | |
| Werewolf | **Placeholder** | Definition + locked contract only. No 3D mesh/rig/animations exist; 2D sprite frames kept in `Assets/ArtReference/Werewolf` (not shipped). Not a completed encounter. |

## 4. Contracts and quests
| Item | Status | Notes |
|---|---|---|
| Accept / track (max 3) / complete / claim, prerequisites, rank gates | Done | |
| Reward-once guard (keyed, persisted) | Done | unit + reload checks |
| Repeatable contract with cooldown | Done | `c_bone_harvest` |
| Starter hunt connected to dialogue and drops | Done | |
| Story quest with stages driven by events | Done | `q_rise` |
| Collect objectives ignore bought items | Done | unit test |

## 5. Inventory, gear, economy
Stacks, unique items, slot capacity, equip/unequip with stat preview, rarity, tooltips (detail panes), vendor buy/sell with daily stock and rank gates, loot window with Take all, negative/duplicate/bypass guards — **Done** (unit tests + verifier).

## 6. Crafting and forging
Recipes from first-hunt drops (Gravebane upgrade, Bonewoven Vest, tonics), upgrade keeps item equipped, unlocks via contract rewards, rank-gated helm recipe, every refusal explained — **Done**.

## 7. Progression
XP/levels, attribute points, 8-skill tree in 3 branches with prerequisites and live stat preview, hunter reputation → rank gating contracts, crypt, vendor stock, estate and court — **Done**.

## 8. Estate
| Item | Status | Notes |
|---|---|---|
| Data-driven tiers, costs, requirements, benefits, persistent ownership | Done | |
| Tier 1 "Restored Cottage" changes real meshes (rubble removed; porch, shed, storage chest, workbench, planters, fence added) | Done | before/after captures |
| Storage, workbench station, pack bonus, daily income capped, no offline accrual | Done | |
| Tiers 2–3 (Lodge, Manor) | **Hook only** | data present, refused with "not drawn up yet"; no art |

## 9. NPCs, factions, relationships
| Item | Status | Notes |
|---|---|---|
| Dialogue conditions/effects, named NPC state, shops | Done | Mira, Bram, Lady Elowen |
| Affinity, daily talk/gift, stages stranger→married, one romance at a time | Done | Mira chain; spouse moves to the cottage |
| Factions + court access hook | Done | Elowen grants court access at Silver rank + estate |
| Full romance stories, cinematics, court content | **Not in scope** | framework only, as requested |

## 10. World flow
Home / settlement / hunt area / dungeon instance with entry gate and safe return, spawning by rules (respawn hours, night-only), interaction prompts, map from a real top-down render with markers, time-of-day lighting hooks — **Done**. Additional regions (woodland/wasteland expansions, towns beyond Thornbury) — **Missing (content)**.

## 11. UI
Main menu, new/continue/load (3 slots), pause, settings (persisted), controls, HUD (vitals, gold/rank/clock, tracker, hotbar, spell cooldown, prompts, notifications, target/boss bar with part states, lock-on reticle), journal (pack/equipment, hunts & story, hunter, map, estate, bonds), dialogue, shop, gift, crafting, storage, loot, hunt board, confirmations — **Done**. Keyboard/controller navigation via EventSystem; menus pause the world and block gameplay input.
Gamepad bindings are mapped for the main actions through the legacy Input Manager; a full rebinding screen is **Missing**.

## 12. Persistence
Schema v3 with step migrations, atomic writes + backup, damaged-file quarantine and graceful refusal, isolated test saves (`--save-dir`), verified across a real exit and relaunch — **Done**.

## Verification and delivery (Phase D)
| Item | Status | Evidence |
|---|---|---|
| Native Unity captures reviewed | Done | `Captures/RPG/*.png`; defects found and fixed are listed in `visual-review.md` |
| SightForge / Claude Eyes on actual captures | Done | pose clips (2D silhouette scope), actor tracks recorded from real 3D play (temporal checks; 0 findings on the final recording), comparison with the sample's renders; both negative controls detected. `Docs/sightforge-review.json`, `visual-review.md` |
| Real player input (not only scripted) | Done | window-targeted Windows key messages read by Unity `Input`; audit log |
| Windows build | Done | `Build-RPG/FolkloreHunterRPG.exe`, `Docs/build-result.txt` |
| Gameplay recording | Done | `Folklore-Hunter-RPG-Framework-Gameplay.mp4` (95 s; two kills shown as labelled 4x time-lapse; timber/nails bought off-screen) |
| Desktop shortcut and build opened on the Acer | Done | `Folklore Hunter RPG - Framework Build` |
