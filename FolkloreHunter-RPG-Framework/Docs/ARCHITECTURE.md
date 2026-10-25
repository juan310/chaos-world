# Folklore Hunter: Rise of the Estate — framework architecture

Project: `C:\Users\juana\Documents\FolkloreHunter-RPG-Framework` (isolated copy of the approved 3D sample).
Source of truth for code and data: git branch `claude/new-session-xoe3g1` of `juan310/chaos-world`,
folder `FolkloreHunter-RPG-Framework/`. `Tools/sync.ps1` mirrors it into the Unity project.

## Layers

```
Assets/Resources/Data/content.json   authored definitions (ids, numbers, text). Never written at runtime.
Assets/Framework/Core   (FH.Core.asmdef, noEngineReferences)  rules + save state, pure C#
Assets/Framework/Game   Unity runtime: world, actors, combat bodies, session glue
Assets/Framework/UI     uGUI screens and HUD built from UiKit components
Assets/Framework/Editor scene generation, Windows build, import settings
Assets/Framework/Tests  NUnit tests of Core (EditMode in Unity, or `dotnet test` in Tools/CoreTests)
```

Core never references UnityEngine (enforced by the asmdef), so every rule can be tested headless.

## Core modules (Assets/Framework/Core)

| File | Responsibility |
|---|---|
| `Definitions.cs` | Authored data types: items, weapons (attack timelines), armor, spells, statuses, monsters (attacks, parts, loot), contracts, quests, recipes, estate tiers, NPCs, factions, ranks, relationship stages, skills, attributes, vendors, dialogue, areas, spawns, balance. |
| `GameDatabase.cs` | Loads/merges content packs, indexes by stable id, validates every cross-reference and invariant (unique equipment, readable telegraphs ≥ 0.25 s, contiguous estate tiers...). |
| `GameState.cs` | Mutable save data only: player, inventory stacks (uid for unique items), equipment refs, gold, recipes, contracts, quests, reputation, relationships, estate (tier, storage, income day), world (clock, flags, pickups, spawns, reward-once keys, loot bags), vendor sales. |
| `GameEvents.cs` | Plain C# events between systems and presentation; `Result` (ok + player-facing reason); `IRandom`. |
| `Inventory.cs` | Slot-limited `Container` (stack merge, all-or-nothing adds, never-negative removes), `InventorySystem` (equip/unequip, upgrade-in-place, consumables). |
| `Stats.cs` | `StatCalculator` (base + attributes + skills + equipment), `ProgressionSystem` (XP curve, points, skill prerequisites, hunter reputation → rank). |
| `Rules.cs` | `Wallet`, data-driven `Conditions` with player-language explanations, `RewardService` (reward-once keys recorded in the save), `LootRoller`. |
| `Contracts.cs` | Shared objective engine; `ContractSystem` (gates, accept/abandon, progress, claim, repeat cooldown); `QuestSystem` (story stages). |
| `Economy.cs` | `CraftingSystem` (reasons for every missing requirement, upgrades keep equipped), `VendorSystem` (prices, daily stock, sell rules), `EstateSystem` (tiers, materials, rank gates, game-day income with cap, storage). |
| `Social.cs` | `RelationshipSystem` (affinity, daily gifts/talks, automatic and dialogue-only stages, one romance), `EffectRunner`, `DialogueRunner` (entry selection, disabled choices with reasons). |
| `Combat.cs` | `AttackTimeline` (windup/active/recovery, never skips the active window), `SwingHits` (once per swing), `StaminaPool`, `PoiseMeter`, `StatusTracker`, `DamageMath`, `MonsterBrain` (idle/patrol/alert/chase/telegraph/attack/recover/stagger/return/dead with grace and leash), `PartTracker` (breakables, gated weak points). |
| `World.cs` | `WorldClock` (only advances in play or sleep), `SpawnRules` (respawn hours, night-only). |
| `Save.cs` | `SaveCodec` (schema v3, step migrations v1→v2→v3 on the raw JSON tree, sanitising), `SaveStore` (slots, temp + `File.Replace` atomic write, `.bak` fallback, damaged files quarantined). |
| `GameCore.cs` | Composition root wiring systems and event → objective routing; `NewGameState`. Holds no rules. |

## Unity runtime (Assets/Framework/Game)

- `GameSession` — the bridge: new/continue/load/save, applies state to the world (estate meshes, NPC homes, pickups, loot bags, spawns), kill/part-break/loot flow, death recovery, sleep, area travel, time-of-day lighting, movement constraints.
- `HunterController` — reads `InputRouter`, runs the weapon's `AttackTimeline`; the blade's real mesh endpoints under `handslot.r` are swept with `OverlapCapsule` during the active window only; dodge invulnerability from stats; spells; hotbar consumables; hurt/death.
- `MonsterActor` — feeds senses (distance, sight cone, line of sight, home distance, safe areas) to `MonsterBrain` and executes it: movement, telegraph ring sized to the real hit volume, eye glow, bone hurtboxes, helm break (baked skinned mesh detached as a physics piece), death.
- `AnimationDriver` — legacy `Animation` with KayKit clips; measures each clip's strike time (peak hand speed) and locomotion stride so the gameplay timeline drives playback speed, never the reverse.
- `CameraRig` (orbit, lock-on framing, collision), `Projectile`, `WorldBuilder` (geography ported from `FH3World`, plus Thornbury, estate tier pieces, crypt instance), interactables (one MonoBehaviour per file).
- `InputRouter` — all device input; menus set `GameplayBlocked`; closing a menu suppresses input for the next frame; automation injects presses through the same path.

## UI (Assets/Framework/UI)

`UiKit` components (panels, buttons with disabled-reason lines, bars, scroll lists), `UiManager` (screen stack, Escape/back, focus for keyboard/controller via the EventSystem, pauses the world and blocks gameplay input while any window is open, fade), `Hud`, and screens: title, slots, settings, pause, controls, confirm/message, journal (pack/equipment, hunts & story, hunter stats/attributes/skills, map, estate, bonds), dialogue, shop (buy/sell), gift, crafting, storage, loot, hunt board.

## Data flow example — the starter hunt

1. Talking to Mira runs `d_mira`; the "I'll take the contract" choice's `AcceptContract` effect calls `ContractSystem.Accept`.
2. A blade sweep hits a `Hurtbox`; `PartTracker.Resolve` picks helm or skull; `DamageMath` computes the hit; `MonsterBrain.TakeHit` may stagger or kill.
3. `GameSession.OnMonsterKilled` grants gold/XP, rolls the loot table into a `LootBag`, raises `MonsterKilled` → kill objectives advance.
4. Taking the loot adds bones with `ItemSource.Loot` → collect objectives advance (bought items never count).
5. Mira's claim choice calls `ContractSystem.Claim` → `RewardService.Grant` records `contract:c_gravebound_cull:0` before granting, so the reward can never be paid twice.
6. Forge, shop, estate and save are the same pattern: UI → system method returning `Result` → events → HUD/world update.

## Save schema

`schemaVersion` 3. Saves contain ids and numbers only. Migration functions operate on the parsed tree (`MigrateV1toV2` imports the sample's flat prototype fields; `MigrateV2toV3` moves estate/world fields). Newer versions are refused, never mangled. Test saves use a temporary directory; the player accepts `--save-dir <path>` to isolate automated runs.
