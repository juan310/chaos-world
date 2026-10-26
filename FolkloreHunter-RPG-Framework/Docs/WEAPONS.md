# Build 0.8.0 — Katana, evasions, Monster Hunter bow animations, Training Range

## Training Range (new test area)
Title menu → **Training Range (weapons and evasions)**, or from either ledger → *Go to the Training Range*. Like
the Proving Grounds it is never saved and gives no rewards. A walled yard 56 × 80 m:
- **Archery lane** (west): a shooting line and straw targets at 10, 20, 30 and 40 m. Each target counts its hits and
  its gold (inner third) on its sign; the overlay lists them too.
- **Dummy pen** (east): a Thornback and a Gravebound, passive and unkillable (a blow that would fell one heals it
  first), for practising combos and spirit levels.
- **Hazard yard** (middle): a slow slam pad (3.4 s), a quick slam pad (2.2 s) and a sweeping log. Each shows a red
  telegraph, then strikes like a monster blow: roll or slide through it (i-frames), guard it, or counter it with the
  katana's Foresight Slash or Iai Spirit Slash. The overlay counts evaded / struck. Toggle at the ledger.
- **Dodge lane**: a strip marked every metre, to read how far each evasion travels.
- **Ledger** at the entrance: weapons, monsters, options (hazards on/off), travel to the Proving Grounds, leave.

## Evasions (all weapons)
| Input | Evasion | Distance / time / invulnerable / stamina |
|---|---|---|
| A, sheathed or stick forward | **Roll** | 3.0 m / 0.42 s / dodge stat / 25 |
| A, weapon out, stick to the side | **Side slide** (keeps facing) | 2.3 m / 0.30 s / 0.20 s / 18 |
| A, weapon out, stick back | **Back-step** (keeps facing) | 1.8 m / 0.28 s / 0.18 s / 15 |
| A while sprinting away from a monster within 12 m | **Dive** (then a short landing) | 4.6 m / 0.55 s / 0.60 s / 30 |
| A, lance drawn | Hop (unchanged) | |
| A, bow drawn | Charging sidestep (now with its own step clips) | |

**Perfect evade** (any evasion above, and the katana's Foresight step): when a blow meets the evasion's
invulnerability within **0.12 s** of pressing A (dodged at the last moment), the hunter gets **+20 stamina** (enough
to end exhaustion) and time slows to **30 % for 0.6 s**, easing back to normal; "Perfect evade!" flashes and the pad
gives a light buzz. A dodge that started earlier still evades through its i-frames, without the bonus. The pause
menu still freezes time and the slow motion resumes when it closes. (Monster Hunter Wilds rewards a well-timed
dodge with stamina, its Discerning Dodge; the slow motion is this game's own addition.) The overlay counts perfect
evades and shows SLOW MOTION while it runs.

## Katana (two-handed) — Monster Hunter's Long Sword
| Input (Xbox / keyboard+mouse) | Move | Notes |
|---|---|---|
| Y / LMB | Overhead Slash → Thrust → Rising Slash | normal hits fill the **spirit gauge** (9 per hit) |
| B / RMB | Thrust, Rising Slash | |
| Y+B | Fade Slash (slides back) | stick to the side: **Lateral Fade Slash** (about 2.6 m sideways) |
| RB / Ctrl | **Spirit Blade I → II → III → Spirit Roundslash** | each spends gauge (12/14/18/15); short of gauge the move is weaker (×0.6) |
| (Roundslash lands) | **Spirit level up** | white → yellow → red: ×1.05 / ×1.1 / ×1.2 damage; each level fades after 45 s; the blade's edge glows in the level's colour |
| RB + B | **Foresight Slash** | a quick invulnerable step back; a blow that passes through it is answered with the Foresight Counter (MV 1.8) and fills the gauge; otherwise a plain slash |
| RB + A | **Iai stance** (special sheathe, up to 3 s) | Y: **Iai Slash** (multi-hit; the gauge then refills itself for 12 s). RB: **Iai Spirit Slash**: a blow caught in its windup is parried, it lands, level up and a full gauge |
The gauge drains slowly after 4 s without hitting. Spirit gauge bar under the magic bar; the overlay shows the gauge,
level, stance and counters. Damage: the katana's own weapon (Slash, base 30).

## Bow — now animated like Monster Hunter's archer
From gameplay footage and guides: the MH bow is held **in the left hand**, the string drawn to the cheek by the right,
an arrow nocked on the bow while it is out; the power shot fires with the bow **canted** (turned on its side); the arc
shot is loosed **upward**; the Dragon Piercer is a long, leaning draw with the bow canted; the charging sidestep is a
quick side hop with the bow kept up.
New hunter clips (authored in Blender on the KayKit rig, `Tools/hunter_weapons.py`): `Bow_Draw` (drawn to full over
the charge time), `Bow_Shoot`, `Bow_Power_Shoot` (canted 80°), `Bow_Arc_Shoot` (pitched up 55°), `Bow_Piercer` (1.45 s,
canted, leaning), `Bow_Step_L/R`, `Bow_Aim_Walk`; plus `Katana_Iai`. The bow now sits in the left hand slot; the
nocked arrow (`Hunt_NockedArrow`) shows while the bow is out and leaves the string for a moment at each shot.
Arrows leave from the bow hand.

## The Briarhound fights and rides like a Palamute
Reference: Monster Hunter Rise's Palamute, a melee companion that fights beside the hunter (its "Pincer" tactic
works the far side of the monster) and a mount that runs, dashes and drifts without costing the rider stamina,
with a three-hit attack on the rider's command ([Fextralife: Palamutes](https://monsterhunterrise.wiki.fextralife.com/Palamutes),
[GamerGuides](https://gamerguides.com/monster-hunter-rise/guide/how-to-play/new-features/palamutes),
[HighGround Gaming](https://www.highgroundgaming.com/mhr-palamute-guide/)).
- **Companion:** against the monster the hunter fights (locked on, or engaged nearby) it runs a **combo**: blade
  bite (right to left), return slash (left to right), spinning slash that strikes all around (9 / 8 / 14 damage, with
  stun). Between combos (2.4 s) it circles to the **flank across the monster from the hunter**. From 4.5 to 9 m it
  **pounces**: crouch, leap in and strike. When the monster **winds up** with the hound inside its reach, it **hops
  clear** (or trots out if the hop is not ready), then comes back in. It runs **beside** a sprinting hunter at their pace,
  and heels behind and to the right otherwise.
- **Ridden:** the stick walks (light push) or **lopes** (full push); **RT dashes**; **RB while dashing drifts**: a
  much tighter turn, the hound leaning into it and throwing up dirt. **Y**: the hound attacks (press again during a
  strike to chain bite, slash, spin). **A**: a quick sidestep, the rider protected for 0.3 s. Riding costs no stamina.
- **Judgement (smarter hound):**
  - *Which monster:* your lock first; otherwise one winding up to hit you (top priority while you are knocked
    down: it comes to the rescue), then the one you have been hitting (even a passive training target), then any
    engaged monster near you. The overlay and `TargetReason` say why.
  - *When:* a monster recovering from a swing, staggered or toppled is an opening: the hound attacks at once instead
    of waiting out its 2.4 s pause (0.6 s; 0.4 s while you are down).
  - *Where to cut:* each strike goes to the softest part in reach (hitzone, weak points ×1.5, unbroken breakable
    parts +0.3), and it flanks on the side nearer that part. Strikes step in up to 0.9 m so the combo connects.
  - *Getting around:* it steers round pillars and walls (tries 35°, 70°, 110° to the side it went last) and only
    teleports after 2.5 s truly stuck. It steps out of your attack's path (a lance charge's whole lane) and only
    runs beside you when you sprint, not when an attack carries you.
  - *Ridden:* lock on (R3) and let go of the stick: it runs you to the monster and stops at striking range. A
    wind-up aimed at you makes it sidestep on its own (you stay on), unless you are already steering away.
- **New clips** on the hound's rig (`Tools/rig_briarhound.py`, IK paws as before): `Slash_R`, `Spin` (a full turn with
  a hop, paws carried round), `Pounce` (crouch, leap, slash on landing), `Hop`. The game moves the hound during the
  leap and hops. Backup of the previous hound: `SourceArt/Backups/briarhound-before-palamute-20261026`.
- Rules in `Assets/Framework/Core/HoundTactics.cs` (HoundTacticsTests).

## Boss monsters (hunt rules: Proving Grounds and Training Range)
Monster Hunter's large monsters are long fights against a few big, readable moves: wide sweeps and slams, roars that
pin you, charges, area attacks with a clear wind-up, and an enraged state that makes them faster and nastier. Here,
under the hunt rules (the valley's monsters are unchanged):
- **Tougher:** Grave Warden ×2.5 health (750) and ×1.5 poise; Thornback, Mirejaw and the Gravebound ×2 health,
  ×1.3–1.4 poise; damage ×1.1.
- **Area attacks with ground telegraphs** (a red area grows on the ground during the wind-up; roars show amber):
  - *Grave Warden:* **roar** (9 m: ears covered, rooted a moment, no damage); **ground slam**: a shockwave ring that
    travels out to 8 m (only its front hits: roll through it); **grave burst**: three eruptions, one under you and two
    around, after a short delay (move); **leap smash** (enraged): jumps onto where you stood; **cataclysm** (desperate):
    three shockwaves in a row.
  - *Thornback:* roar; **stomp** all around it; its **charge** becomes a **double charge** when enraged; **quake**
    (desperate): two shockwaves.
  - *Mirejaw:* roar; **tail spin** all around (it turns a full circle); bite into tail spin combos; **venom pools**
    (enraged): four eruptions around you; **venom breath** (desperate): a cone.
  - *Gravebound:* lunging chop; **frenzy** combo when enraged.
- **Phases:** below half health it enrages (the old rule) and **roars**, opening its enraged moves; below 30 % it turns
  **desperate**, roars again and its most dangerous moves come out (the overlay shows the phase).
- **Smarter choice of attack** (weighted, `HuntBoss.Choose`): a hunter at its back meets the tail (×3), a hunter out of
  reach gets charged or leapt on (×3), **drinking a potion invites a gap closer or eruptions (×5)**, a new phase shows off
  its new moves (×2), and it rarely repeats a move. Combos chain without a pause.
- **Against it:** roll i-frames evade every area (a last-moment roll is a perfect evade), a raised shield guards slams,
  waves and roars, the lance's counter and the katana's Foresight and Iai Spirit Slash catch them; a flinch or topple
  during the wind-up cancels the attack. The Briarhound hops clear of areas it is standing in (or is knocked back a
  step) and cowers at a roar.
- Rules and data: `Assets/Framework/Core/HuntBoss.cs` (HuntBossTests), `content.json` → `huntAttacks`, `huntHealth`;
  runtime `MonsterAoe.cs`. The moves reuse the monsters' existing clips at boss timing plus motion (leap arcs, spins);
  bespoke roar / slam animations for the quadrupeds are a next step.

## Movement feel (why it was sluggish, and Monster Hunter Rise's pace)
Measured in the player before the change: in the hunt combat the hunter **walked at 1.6 m/s with full stick and
"ran" at 2.4 m/s on RT**, and only 1.4 m/s with a weapon out. The speeds were derived from the stock KayKit clips so the
feet would not slide, but the chibi knight's run moves each foot only about 0.2 m per stride, so the honest speed was
a brisk walk. On top of that every attack waited 70 ms (to see whether Y+B was coming) and the camera smoothed twice
(about 0.2 s behind). In Rise full stick is a run, R a clearly faster dash with stamina, the weapon drawn a slower run,
and attacks come out on the press ([Nintendo's Rise basics](https://www.nintendo.co.uk/News/2021/March/MONSTER-HUNTER-RISE-Guide-The-Basics-1945194.html)).
Now, in the Proving Grounds and the Training Range (the valley's classic movement and its foot-planting checks are unchanged):
- **Light stick walks; full stick jogs at 3.6 m/s; RT sprints at 5.2 m/s** (stamina). Weapon drawn: the jog at the
  weapon's pace (sword ×0.85 = 3.1 m/s, katana ×0.8, bow ×0.75, lance ×0.6).
- **New stride clips** `Hunt_Jog` and `Hunt_Sprint` (`Tools/hunter_weapons.py`, `stride_clip`): KayKit's run with each
  leg and arm bone's swing enlarged about its own average pose (×1.8 / ×2.1), a forward lean (10° / 17°), and the hips
  kept on the planted foot every frame. Played at the rate the planted foot's measured sweep needs (capped at 3.6×).
  Honest limit: the chibi knight's legs are very short, so at a hunter's pace the planted foot still slides a little
  (checked in the player: under half the body speed).
- **Attacks start on the press.** If the other button follows within 70 ms while the move is still winding up, it
  becomes the Y+B move instead (the bow keeps its window: tap or hold decides a quick shot or a draw).
- Turning is quicker (22/s vs 15/s) and the camera follows tightly in the hunt combat.
- The Briarhound's ridden gallop stays well above the hunter's sprint (7.4 vs 5.2 m/s).

## Claude Eyes: what it caught, what was fixed
The inspector now reviews the hunter too (`Tools/review_hunter_sightforge.py`): 20 clips with the weapon in hand
(bow, katana, jog and sprint, evasions), rendered by the real player, plus the hunter's and the hound's movement tracks.
- **Fixed:** the katana's overhead drove the tip about 0.4 m into the ground. At 80 % scale it was still 0.27 m under
  the soles, so a drawn katana is now tilted up about the grip after the pose just enough that its tip rests on the
  floor (`HuntFighter.KeepBladeOffGround`; the player measures the tip at 0.00 m over the ground through the chop); the katana's spirit glow showed for a frame at level 0; the dive's get-up started from frames
  lying sunk into the floor (the landing now starts past them); the review frames cut off the katana's reach.
- **False positives explained:** "picture stuck while moving" on the hound (16 recorded frames per gallop stride each
  covered more ground than the check's threshold: the track now records 32) and on the hunter (riding was labelled
  as standing idle: now its own state); "loop does not close" on one-shot moves; ground-line and silhouette changes
  on jumps, spins, rolls and getting up, which leave the ground or turn edge-on by design.

## Results (0.8.0)
Final runs on the Windows build (commits 4d8eaf1 and 43ccbdc):
- **Proving Grounds, in the player with the real controls:** 159 checks, 0 failures (43ccbdc). The run before it
  (4d8eaf1) had 158 checks with 1 failure: the lance charge struck the Gravebound once instead of twice. See Limits.
- **Classic regressions:** phase 1 128 checks, 0 failures; phase 2 16 checks, 0 failures.
- **Core rules (NUnit, in the container):** 100 / 100, including HuntBossTests (6) and HoundTacticsTests (7).
- **Boss checks (all pass):** the Warden is a boss (750 vs 300 health and 5 area attacks); the roar telegraphs and
  makes the hunter cover their ears; the ground slam's shockwave strikes the hunter 5 m away; a roll through the wave
  front evades it (0 hits, 1 evaded); three eruptions, one under the hunter, are avoided by moving; the leap smash
  jumps 8.2 m and lands 0.8 m from where the hunter stood; it enrages (phase 2) and turns desperate (phase 3), roaring
  each time; the cataclysm's three waves all strike; a potion drunk in front of the Thornback draws its charge;
  Mirejaw's tail spin strikes a hunter behind it; the Briarhound hops clear of a slam.
- **Claude Eyes:** hunter 0 confirmed findings in 20 clips (25 "likely", most expected on jumps, spins, rolls and
  one-shots); hound 0 confirmed, and its negative control (a deliberately broken clip) is still detected. Open
  "likely" findings, not fixed: the jog and sprint bob about 0.1 m against the ground line in the side view; single
  frames in the bow's arc shot, the katana slice and the sprint differ in size from their neighbours.

## Limits (0.8.0, honest)
- The katana uses KayKit's two-handed sword clips (chop, slice, stab, spin, spinning) for its moves; only the iai stance
  pose is new. No Spirit Helm Breaker, Focus Strike or Spirit Charge yet; the Red level does not lengthen the blade.
- The dive has no separate animation (the KayKit jump with a quick stand-up). The perfect evade works in the hunt
  combat (Proving Grounds and Training Range); the classic valley combat is unchanged.
- The bow's string is a static mesh line; it does not bend as the hand draws.
- Foot slide at hunt pace: the planted foot moves at about 70 % of body speed while jogging or sprinting (the chibi
  legs are short). The check fails if it gets worse; a real fix needs an authored run cycle.
- The bosses' moves reuse each monster's existing clips at boss timing, plus motion (leap arcs, spins); there are no
  bespoke roar or slam animations yet.
- The lance charge against a man-sized monster (the Gravebound): when the charge glances off its side, the lance
  points past it and the pass strikes once rather than repeatedly. This happened in 1 of the last 5 runs. A toppled
  monster is struck lying down (checked).
- The Training Range hazards are fixed timers, not monsters; they test i-frames, guards and counters, not reading
  a monster's tells.

Sources: Monster Hunter Wilds Long Sword guides — [Icy Veins](https://icy-veins.com/monster-hunter-wilds/long-sword-guide),
[Mobalytics](https://mobalytics.gg/mhw/long-sword-guide), [GameWith](https://gamewith.net/monster-hunter-wilds/49136),
[PCGamesN](https://www.pcgamesn.com/monster-hunter-wilds/long-sword); Bow guides —
[Icy Veins](https://icy-veins.com/monster-hunter-wilds/bow-guide), [Mobalytics](https://mobalytics.gg/mhw/bow-guide),
[PC Gamer](https://pcgamer.com/games/action/monster-hunter-wilds-bow-builds), [PCGamesN](https://www.pcgamesn.com/monster-hunter-wilds/bow).

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
Final 0.7.0 build, real Windows player, injected Xbox pad input (`Tools\verify_proving.ps1`):
- **Proving Grounds verifier: 87/87** (the 52 earlier checks plus 35 for the lance, the bow and the weapon switch).
  Lance: Y, B, Y gives mid_1 > high_2 > mid_3; thrusts land from 2.3 m; Y+B wide sweep; hops 1.64, 1.64, 2.59 m then
  a fourth refused, facing kept (0 degrees turned); the tower shield took a Warden blow for 0.8 health; guard thrust;
  guard dash 2.5 m; the counter stance caught a real Warden blow and answered with the Retribution Thrust; quick
  RB+B is the counter thrust; dash attack 7.3 m in 1.3 s for 16 stamina, two hits on one pass, Y finishing thrust.
  Bow: draw shot hits at 10 m; quick follow-ups L2 x2 then L3 x3; back to L1 after a pause; held draw reaches L3 in a
  second and costs stamina; charging sidestep 2.47 m to level 2, facing kept; power shot x5 at level 3; arc shot on
  the locked target (hits and stun 0 -> 5); Dragon Piercer 3 hits from one bolt; aiming brings the camera to 2.9 m
  over the shoulder and the aimed arrow hits the monster under the crosshair.
- **No regressions:** the original game's verifier on the same build: play-through 127/127, relaunch/continue 16/16.
  Core tests 79/79 (10 new for the weapons).
- **Found and fixed by the verification:** a dodge without lock-on turned the hunter toward the dodge, so the bow's
  sidestep spun the archer and the lance's back-hop would have turned the lancer around (hops and sidesteps now keep
  facing); the Briarhound could body-block the hunter's charge (a companion no longer collides with its hunter); the
  over-the-shoulder camera sat behind the hunter's big helmet (moved 1 m to the right); a dash with nothing in reach
  ignored a monster ahead (charging moves now look 10 m ahead).
Screenshots: `Captures/Proving/p11-lance-drawn` ... `p18-bow-aim`.

## Limits (honest)
- No coatings, tracer arrows, focus strikes, sharpness or weapon wear yet; one lance and one bow (no upgrade trees).
- (0.7.0; fixed in 0.8.0) The bow was held in the right hand with no string-pull animation.
- The lance's counter stance blocks any frontal blow during its window (no separate power-guard stance).
- No aim assist: free aim needs the crosshair on the monster; without aiming, shots go to the locked or nearest target.
