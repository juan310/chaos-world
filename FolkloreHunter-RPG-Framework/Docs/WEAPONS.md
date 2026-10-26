# Build 0.7.0 — Lance and Bow (Monster Hunter-style weapons)

Both weapons live in the Proving Grounds next to the sword and shield: choose them at the **ledger** (south wall),
under **Weapon**. Nothing in the ring is saved; the valley is unchanged until the weapons are signed off.

## What the research said (and what was taken from it)
Monster Hunter weapons are built from committed moves with **motion values** (each move's damage multiplier),
**hitzones** (part multipliers per damage type), hitstop, and per-weapon defence and mobility. Two weapons asked for:
- **Lance**: the defensive weapon. Its strength is the shield: it guards most hits with little knockback, attacks
  from behind the guard (guard thrust, guard dash), counters a blocked blow (Counter/Retribution thrusts; Wilds'
  Power Guard and Charge Counter are variations of this), pokes with mid and high thrusts in strings of three that can
  be cancelled into guard, closes distance with a dash attack ending in a finishing thrust, and hops instead of rolling.
- **Bow**: the mobile ranged weapon. Three **charge levels** shown beside the reticle; holding the draw raises them;
  a **charging sidestep** (dodging while drawn) raises the level by one and is the core of the rhythm (dodge, shoot at
  max level); a **power shot** after a shot fires a spread at the current level without raising it; the **arc shot**
  rains arrows from above (and builds stun); the **Dragon Piercer** is a long-windup piercing bolt that hits along its
  whole path; holding the draw costs stamina.
Sources: Monster Hunter Wilds bow and lance guides (Icy Veins, Mobalytics, PCGamesN, PC Gamer, GameWith,
Deltia's Gaming), Kiranico's damage guide, the Fextralife sharpness/guard pages. Coatings, tracer arrows, focus
strikes and sharpness are **not** implemented (see Limits).

## Lance (`content.json` → `huntMovesets` → `lance`)
| Input (Xbox / keyboard+mouse) | Move | Notes |
|---|---|---|
| Y / LMB | Mid Thrust I → II → III | MV .85 / .85 / 1.05; mix freely with high thrusts, three per string |
| B / RMB | High Thrust I → II → III | MV .95 / .95 / 1.15, point raised over a monster's back |
| Y+B / both | Wide Sweep | MV .9, wide arc, high stagger |
| RB / Ctrl | Guard (tower shield) | 85° arc, 4% chip (sword and shield: 70°, 12%), short pushback, perfect window 0.2 s |
| RB + Y | Guard Thrust | the shield stays up through the thrust |
| RB + stick + Y | Guard Dash | 5.5 m/s shield charge, guarded the whole way |
| RB + B (hold) | Counter stance | up to 1.1 s; a blocked blow → **Retribution Thrust** (MV 2.3, stun 12); released → Counter Thrust |
| perfect guard, then Y | Payback Thrust | MV 1.6 |
| RB + Y + B | Dash Attack | 6 m/s charge, steerable, strikes every 0.3 s, 14 stamina/s; Y → **Finishing Thrust** (MV 1.9) |
| A / Space (drawn) | Hop | 1.6 m, 0.18 s invulnerable, three in a row (the third is a 2.6 m long hop), keeps facing |
| Y from sheathed | Draw Thrust | |
Damage: the lance's own weapon (Pierce, base 26) — its hits use the target part's Pierce hitzone.

## Bow (`content.json` → `huntMovesets` → `bow`)
| Input | Move | Notes |
|---|---|---|
| Y tap / LMB tap | Shot | at the ready level: L1 one arrow (MV .75), L2 two (MV .55 each), L3 three (MV .5 each) |
| Y hold → release | Charged shot | level rises at 0.45 s and 0.9 s; drawing costs 10 stamina/s; you can walk slowly while drawing |
| Y again quickly | Next shot | each quick follow-up shot is one level higher (MH's shot chain), back to L1 after 0.8 s |
| A / Space (drawn) | Charging Sidestep | 2.4 m, invulnerable 0.2 s, **+1 level**; while drawing, the draw continues from the new level |
| B right after a shot | Power Shot | the current level's arrows + 2, in a fan 1.8x as wide; the level is kept, not raised |
| B (not after a shot) | Arc Shot | 9 arrows rain (1.8 m around) on the locked target / reticle / 12 m ahead after 0.85 s; each adds stun (KO) |
| Y+B | Dragon Piercer | 0.75 s windup, one bolt that pierces and strikes each part up to 8 times |
| RB / Ctrl (hold) | Aim | camera over the right shoulder, crosshair with level pips; arrows go to the crosshair |
| Y from sheathed | Draw Shot | |
Without aiming, a shot flies at the locked target, else the nearest monster in front (within 40° and 30 m), else
straight ahead. Arrows hit the part they strike (each part's Pierce hitzone), with a short monster-only hitstop.

## Art and animation
- `Tools/hunter_weapons.py` (run by `Tools/export_hunter_moveset.py`): the bow, lance, quiver and arrow are original
  low-poly geometry in the KayKit style, coloured from the knight's own atlas (one hunter material). The tower
  shield is the Knight's `Rectangle_Shield` (CC0), enlarged. Weapons sit on the same hand slots as the sword.
- New KayKit clips exported: `1H_Ranged_Aiming/Shoot/Shooting/Reload`, `2H_Ranged_Aiming/Shoot`, `2H_Melee_Attack_Stab`,
  `2H_Melee_Attack_Spinning`, `Spellcast_Long`. Derived clips: `Lance_High_Thrust` (the stab with the arm raised 38°),
  `Lance_Charge` (running with the lance couched and the shield up), `Bow_Aim_Walk` (walking legs, aiming upper body).
- Previews: `Captures/HunterExport` (`1H_Ranged_Aiming_*`, `Lance_*`, `Bow_Aim_Walk_*`, `Blocking_*`).
- Backup of the previous hunter: `SourceArt\Backups\hunter-before-weapons-20261026`.

## Verification
VERIFY_PLACEHOLDER

## Limits (honest)
- No coatings, tracer arrows, focus strikes, sharpness or weapon wear yet; one lance and one bow (no upgrade trees).
- The bow is held in the right hand (the KayKit aiming clip's hand), like a mirrored archer; there is no string-pull
  animation, only the aim, release and walk poses.
- The lance's counter stance blocks any frontal blow during its window (no separate power-guard stance).
- No aim assist: free aim needs the crosshair on the monster; without aiming, shots go to the locked or nearest target.
