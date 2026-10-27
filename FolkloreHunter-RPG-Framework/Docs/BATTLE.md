# Build 0.9.0 — Hunter's Sight: wounds, Rend strikes, the Thorn Rush, the Standoff, the Seeker Thorn

Asked for: "look at Monster Hunter videos — combat with bows, shield and sword, lance etc. — and implement battle
animations in ours, but our own unique way". The research was done from guides to Monster Hunter Wilds, which describe
what its videos show (sources at the end). The game already had Monster Hunter's core (sword and shield, lance, bow,
long sword as the katana, perfect guards, counters, charge levels, spirit levels, topples). 0.9 adds the newest layer
of Wilds' combat and gives each piece this game's own twist. All of it is in the hunt combat (Proving Grounds and
Training Range), like the rest of the Monster Hunter-style combat; the valley's classic combat is unchanged.

## What Monster Hunter Wilds does, and what this game does
| Monster Hunter Wilds | Folklore Hunter 0.9 (own twist in bold) |
|---|---|
| Wounds: hitting one area again and again wounds it; wounds glow red in Focus Mode and take more damage | **Wounds** on the bone that keeps being struck: a red rune-scar glows there (faint normally); +25 % damage on it; at most three open; a wound left alone closes after 75 s; build-up fades after 12 s without a blow there |
| Focus Mode (hold L2): aims toward the screen, highlights wounds | **Hunter's Sight** (hold LT / Left Alt with the weapon out): the camera closes in, wounds pulse bright, the hunter faces the camera's direction and strafes; **a tap of LT is still the spell** |
| Focus Strike on a wound: big damage, the monster reacts; each weapon has its own | **Rend** (Y in Sight), one per weapon, aimed at the open wound nearest the centre of the view; it **leaps** up to 2.5-3 m onto it. Tearing it: ×2.2 on that blow, the monster flinches, the spot **scars** (no new wound there for 60 s) |
| — | **Resolve: each torn wound gives a stack (up to 3, 40 s): +6 % damage and +15 % stamina recovery per stack** |
| — | **Unravel: every third wound torn in a fight topples the monster (4 s)** |
| Sword and shield: Perfect Rush, timed presses when the hunter glows, from a back-step into a leaping slash; shield finishers stun | **Thorn Rush**: back-step, Y = **Leaping Slash**, then Y: **Thorn Rush I** (rising cut), **II** (two spinning cuts), **III** (leaping spin), **Shield Crash** (jump and slam the shield down: the hardest stun of the weapon). The blade **glints** late in each move; Y in the glint is a **perfect link** (+40-45 % damage and stun, quicker windup); earlier is a plain link |
| Power Clash: perfect guards build it; certain blows clash; mash to overpower; each clash needs more guards | **Standoff** (sword and shield, lance): three perfect guards fill the meter; the next blow the raised shield meets locks hunter and monster; **mash Y** (7 presses in 1.6 s, one more each clash) to throw the monster down (3 s topple) and land the **Standoff Bash / Standoff Thrust**; fail and the hunter is pushed back unhurt; each clash asks one more perfect guard (up to 6); the meter empties when you change weapons |
| Bow: Tracer Arrow — later shots home on it and it detonates after enough hits | **Seeker Thorn** (the bow's Rend): a heavy shot that tears a wound and **stays in the monster**; for 20 s your straight arrows bend toward it (up to 540°/s, within 70° of their flight); the eighth arrow to land **bursts** it (motion value 2.4) |
| Long sword focus strike | **Ember Rend** (katana): a low dash and two cuts; tearing a wound **fills the ember gauge by half** |
| Lance focus strike | **Piercing Rend**: braced behind the tower shield, three driving thrusts |
| Sword and shield focus strike | **Rending Leap**: a low lunge, a diagonal cut then a back-hand cut |

## Our own names (no Monster Hunter terms in the game)
Mechanics can follow the genre; names, text and art must be ours (the owner's rule, also in the repo's `CLAUDE.md`).
Every player-facing name that echoed Monster Hunter was replaced in 0.9 (internal code ids are unchanged):

| Was (Monster Hunter's term) | Now |
|---|---|
| Spirit gauge / spirit level | Ember gauge / ember level |
| Spirit Blade I, II, III / Spirit Roundslash | Ember Cut I, II, III / Ember Wheel |
| Foresight Slash / Foresight Counter | Shadowstep Slash / Shadowstep Counter |
| Iai stance / Iai Slash / Iai Spirit Slash | Sheath stance / Sheath Draw / Ember Draw |
| Fade Slash / Lateral Fade Slash | Retreating Cut / Sidelong Cut |
| Power Shot / Arc Shot / Dragon Piercer / charging sidestep | Fan Volley / Rain Volley / Skewer Shot / drawing step |
| Guard Dash / Counter Thrust / Dash Attack | Shield Rush / Riposte Thrust / Thundering Charge |
| Power Clash, Perfect Rush, Tracer Arrow, Focus Mode, Focus Strike | Standoff, Thorn Rush, Seeker Thorn, Hunter's Sight, Rend |
| "Monster Hunter-style" in the controls help | "hunt combat" |

## Controls (also in the pause menu's Controls)
- **Hunter's Sight**: hold LT (weapon out) or Left Alt. **Rend**: Y while in Sight. Tap LT: the spell.
- **Thorn Rush**: stick back + A (back-step), then Y; keep pressing Y when the blade glints.
- **Standoff**: perfect-guard three blows (RB just before they land), then guard the next one; mash Y.
- The HUD line under the vitals shows SIGHT, Resolve (stacks and seconds), the standoff meter (or STANDOFF READY) and the
  Seeker Thorn's count; a wound opening says so on screen.

## New animations (hunter, KayKit rig, `Tools/hunter_weapons.py` → `battle_clips`)
Built from the KayKit swings (their arm arcs) with jumps, crouches, leans, the shield arm and the legs posed on top;
the lowest foot is kept on the ground (or exactly at the jump's height). Preview strips (side and three-quarter views):
`Captures/HunterExport/battle_*.png`.
`SnS_Leap_Slash`, `SnS_Rush_1` (rising cut), `SnS_Rush_2` (two spinning cuts, shield held in), `SnS_Rush_3` (leaping
spin), `SnS_Shield_Crash` (jump, shield overhead, slam), `SnS_Rend`, `Lance_Rend` (three thrusts behind the shield),
`Hunt_Clash` (braced, leaning in, trembling: looped), `Katana_Rend`, `Bow_Tracer` (the Seeker Thorn: deep canted draw, hard release).
The new male and female hunters get them through `Tools/build_hunter_variant.py` like every other clip.

**Posed after the original pose study** (`Docs/Concepts/Poses-0.9/original-pose-study.png`, supplied by the owner):
the Blender poser now has leg IK (`Poser.leg`: the ankle on a ground target, the knee bent forward and out, the foot kept
flat) and a `stance` helper (hip drop, front foot, back foot, width as shares of the leg). The sheet's poses map to:
| Pose study | Used for |
|---|---|
| 6 shield block, loaded rear leg | Thorn Rush I wind-up, Ember Rend wind-up (`LOADED`); the Standoff and Piercing Rend brace (`BRACE`, the rear leg pushing) |
| 7 diagonal cut, hip rotation | the weight moving onto the front foot through Thorn Rush I, the Rending Leap's cuts, the Leaping Slash follow-through (`THROUGH`) |
| 8 recovery, feet under body | the landings of the Leaping Slash, Thorn Rush III and Shield Crash (`LAND`: low, both feet planted) |
| 9-10 bow draw and release | a wide, low archer's base under every bow shot clip (Bow_Draw, Bow_Shoot, Fan/Rain Volley, Skewer Shot, Seeker Thorn) |
| 11-12 lance guard and committed thrust | the Piercing Rend's thrusts (`LUNGE`: front knee bent, rear leg long) |

## Where it lives
- Rules (engine-free, tested in `HuntWoundTests`): `Assets/Framework/Core/HuntWounds.cs` — `WoundTracker`,
  `HunterResolve`, `RushTiming`, `ClashMeter`, `ClashBout`, `TracerMark`. Routes: `HuntRoutes.Opener` (back-step
  attack). Data: `content.json` → `huntMovesets` (`backstepAttack`, `rend`, `clash`, `clashFinisher`; moves
  `perfectFrom/perfectTo/perfectBonus`, `rend`, `rendLeap`, `tracer`). The database rejects a Rend that does not tear,
  a clash without a finisher and a bad glint window.
- Runtime: `MonsterWounds.cs` (wounds on the body, marks, tears, Unravel), `HuntFighter` (Sight, Rend aim and leap,
  glint and perfect links, Resolve, clash bout, tracer), `HunterController.SweepBlade` / `ArrowShot` (a Rend tears the
  open wound within a metre of its blade or 0.9 m of its arrow; arrows home on the tracer), `CameraRig.Sight`, HUD.
- Player checks: `ProvingVerifier.BattleChecks` (the actual pad buttons; fixtures are named in the results).

## Results (0.9.0)
RESULTS_BATTLE

## Limits (honest)
- The research is from written guides to Wilds (what its videos show), not from watching footage frame by frame.
- The new clips are built on the KayKit chibi knight's swings; on the new realistic hunters they play through the same
  retargeting as every other clip, so arm arcs were not authored for those bodies.
- Wounds sit on the hurtbox bones the monsters already have (head, chest, limbs...), so a monster with few hurtboxes
  has few wound spots.
- The Standoff is triggered by any blow the raised shield meets once the meter is full (Wilds picks certain attacks).

## Sources
- Wounds, Focus Mode and Focus Strikes: [Mobalytics: wounds explained](https://mobalytics.gg/mhw/guides/wounds-explained),
  [Mobalytics: focus mode](https://mobalytics.gg/mhw/guides/focus-mode-explained), [Icy Veins](https://www.icy-veins.com/monster-hunter-wilds/news/how-focus-mode-works-in-monster-hunter-wilds/),
  [PCGamesN](https://www.pcgamesn.com/monster-hunter-wilds/focus-mode)
- Sword and shield Perfect Rush: [Icy Veins SnS guide](https://icy-veins.com/monster-hunter-wilds/sword-and-shield-guide),
  [Mobalytics SnS guide](https://mobalytics.gg/mhw/sword-and-shield-guide)
- Power Clash and the lance: [Digital Trends](https://www.digitaltrends.com/gaming/monster-hunter-wilds-power-clash/),
  [Icy Veins lance guide](https://icy-veins.com/monster-hunter-wilds/lance-guide), [Mobalytics lance guide](https://mobalytics.gg/mhw/lance-guide)
- Bow tracer arrows and coatings: [Icy Veins bow guide](https://icy-veins.com/monster-hunter-wilds/bow-guide),
  [PC Gamer](https://pcgamer.com/games/action/monster-hunter-wilds-bow-builds), [Mobalytics bow guide](https://mobalytics.gg/mhw/bow-guide)
