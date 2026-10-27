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

## Other standing constraints
- No paid generation, purchases, subscriptions, asset-store imports, external publishing or account changes without
  explicit authorization. The local Forge on the owner's PC is free and allowed.
- Never overwrite unrelated work; keep older models and art on the side (the game falls back to them).
- Keep `FBX_SCALE_ALL` in Blender FBX exports. MonoBehaviours live in files with matching names.
- Exercise the actual player controls when verifying; don't claim a 2D inspector certified 3D combat, foot planting
  or collision.
