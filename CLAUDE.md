# Folklore Hunter: Rise of the Estate — standing rules

## Originality (the owner's rule: "we don't want to get sued by Monster Hunter")
The hunt combat is inspired by Monster Hunter's style of play, but everything a player sees must be our own.
- Never use Monster Hunter (or any other game's) names in player-facing text: move names (`name` in
  `content.json`), HUD and flash messages, notices, menus, controls help, ledger text, item and monster names.
  Use this game's own names instead (Hunter's Sight, Rend, Resolve, Unravel, Thorn Rush, Standoff, Seeker Thorn,
  Ember Cut / Ember Wheel / Shadowstep / Sheath Draw / Ember Draw, Fan Volley, Rain Volley, Skewer Shot, Shield Rush,
  Riposte Thrust, Thundering Charge, drawing step).
- No "Monster Hunter" or other trademarks in the game itself (UI, store text, build names, window titles).
- No copied assets, logos, icons, UI layouts, sounds, music or monster designs; all art is our own or CC0.
- Mechanics can be inspired by genre conventions, but give each one this game's own twist (e.g. Resolve stacks,
  Unravel on every third torn wound).
- Internal design docs may name the inspiration and cite sources; internal code ids may stay as they are.

## One body design: no chibi (the owner's rule)
- The player, villagers and humanoid monsters use the slim, tall realistic bodies (Codex's models, Models/Codex/). The
  chibi KayKit characters must not be used for them in any way: not as a visible model, not as a hidden rig driving
  them, and not as the source of their animations. Every animation they play is authored on the slim skeleton itself.

## Development tools (the owner's permission)
- Claude may build its own tools when they help develop this game now or later, including new modules for SightForge /
  Claude Eyes Studio on the owner's PC (the 3D mesh system, Studio 0.16, is the first). Back up the app first the way
  Studio's own upgrades do (backups/app_<old>_before_<new>_<date>), keep the source and installer in git
  (`FolkloreHunter-RPG-Framework/Tools/sightforge/`), and tell the owner what was added. Local and free only (see below).

## Other standing constraints
- No paid generation, purchases, subscriptions, asset-store imports, external publishing or account changes without
  explicit authorization. The local Forge on the owner's PC is free and allowed.
- Never overwrite unrelated work; keep older models and art on the side (the game falls back to them).
- Keep `FBX_SCALE_ALL` in Blender FBX exports. MonoBehaviours live in files with matching names.
- Exercise the actual player controls when verifying; don't claim a 2D inspector certified 3D combat, foot planting
  or collision.
- Check constantly with SightForge (the owner's rule: "constantly check this to avoid fixes later"): every built body
  passes the `bodycheck` gate before it goes into the game (`C:\AW\briarhound_work\slimall.ps1` builds into a staging
  folder and installs only what passes), every game build runs the Proving Grounds, and Claude looks at the pictures
  (close-ups, worst frames) itself before calling anything done. A failure is reported to the owner when it is found,
  not after moving on to the next task.
