# MotionBricks playbook (internal)

How this game uses NVIDIA MotionBricks (local, RTX 5080, `C:\AW\motionbricks`, run with `uv run`), what each setting
does to our clips, measured, and the rules that follow. Code is Apache-2.0; the model is under the NVIDIA Open Model
Agreement. **Its sample motions (`assets/motions`) are evaluation-only and are never used: only our own poses go in.**

## What MotionBricks is (from its own docs: `docs/runtime_api.md`, `docs/soma_model.md`)
- A transformer that generates human motion **between poses** on its 30-joint SOMA skeleton (Y up, 30 Hz), trained on
  ~313k studio mocap clips (Bones RigPlay 1). It knows human motion at human pace; it has no idea of our game's timing.
- One generation: **4 context poses** (the last 0.1 s played) and **1-4 target poses**. It returns 18-58 frames
  (0.6-1.9 s): 6-16 tokens of four poses, minus three boundary frames at each end. It also predicts the root path and the
  duration, and four foot contacts per frame.
- Settings:
  | Setting | Where | Effect |
  |---|---|---|
  | `allowed_token_counts` | `MotionBricksInferenceConfig` (or per target) | lengths it may choose; default 8-14 tokens (26-50 frames). One count = an exact length (`4n - 6` frames) |
  | `num_poses_to_use` | per target | 1 = arrive and stop in that pose; 2 = also its velocity; 3-4 = also acceleration and momentum |
  | `transition_early_by_frames` | per target (players) | the target as a guide: move on before arriving |
  | `ik_pin_mask` | per target (players) | hands/feet eased onto the target by two-bone IK near the end |
  | `correct_generated_diff` | config, default on | spreads small corrections so it starts exactly at the context and ends exactly at the target |
  | `heading_alignment` | config | the facing it turns the last context pose to before inference |
- Foot contacts: `output.foot_contacts[frames, 4]`, 0-1, columns **left heel (LeftFoot), left toe (LeftToeBase), right
  heel (RightFoot), right toe (RightToeBase)**.
- The raw ONNX interface (docs `onnx_inference.md`) also takes per-slot masks for the pose, the root position/heading and
  the root velocity/height separately, so a target could give only where to stand, or only the body pose. The engine API
  does not expose that; it is the next lever to try if we need it.
- Limits it states: works best for target poses inside its training distribution (stylised or non-human poses come out
  worse), human proportions only (we retarget to SOMA), kinematic only.

## Our bridge
`Tools/motionbricks/`:
- `mb_skeleton.py` (uv): SOMA skeleton to `soma.json`.
- `mb_export.py` (Blender): bakes our keyposed clips (`Tools/slim/moves.py`) on a body, retargets them to SOMA frame by
  frame and records their key times. An optional clip list exports just those.
- `mb_generate.py` (uv): the game pipeline's generator.
- `build_slim.py --mb mb_out.json`: back onto the slim skeleton.

The lab (`mb_lab.py` generates under several settings, `mb_lab_eval.py` scores and draws): on the PC
`C:\AW\briarhound_work\mblab.ps1 [-clips a,b,c] [-variants x,y]` runs export, generate and score on the Tripo hunter. It
writes `C:\AW\briarhound_work\mb\lab\report\` (`lab_report.json`, `lab_summary.md`, one strip per clip).

Measures (on SOMA, before retargeting):

| Measure | Meaning |
|---|---|
| smooth | 95th-percentile acceleration of hips, head, hands, feet and toes, in m/s² |
| pops | frames with a hitch, where an acceleration jumps more than 60 m/s² in one frame |
| keys | distance from our key poses at the key times, in cm |
| free | distance from our own in-betweens elsewhere, in cm (how much it re-imagined) |
| slide | foot speed while MotionBricks says the foot is planted, in cm/s |
| slide (low) | the same while the foot is low and near-still, comparable with our source |
| floor | depth under the floor, in cm |
| squash | generated frames ÷ frames to fill |

## Results

### Rounds 1-2 (2026-10-28): 16 of our clips × 9 settings, Tripo hunter
Clips: SnS_Rush_1, SnS_Leap_Slash, Block_Hit, Dodge_Left, Dodge_Backward, Jump_Full_Short, Lie_Down, Lie_StandUp,
2H_Melee_Attack_Spin, Katana_Iai, Lance_High_Thrust, Bow_Power_Shoot, Death_A, Use_Item, Interact, Spellcast_Shoot.

| setting | smooth | pops | keys cm | free cm | slide | slide (low) | floor | squash |
|---|---|---|---|---|---|---|---|---|
| **base** (what the game did: 4 poses, model picks the length) | 231 | 7.4 | 0 | 13.5 | 67 | 21.3 | 4.7 | 4.5 |
| fit (4 poses, length fitted to the gap) | 78 | 3.3 | 0 | 9.4 | 51 | 14.9 | 2.5 | 1.9 |
| settle (1 pose, fitted) | 58 | 2.1 | 0 | 9.5 | 42 | 16.6 | 1.7 | 1.9 |
| speed (2 poses, fitted) | 71 | 2.4 | 0 | 8.7 | 45 | 15.3 | 2.3 | 1.9 |
| hybrid / best (gaps under 18 frames kept ours) | 86 | 3.0 | 0 | 0.6 | 51 | 13.2 | 1.5 | 1.0 |
| merge (short key gaps dropped) | 10 | 0.1 | **7.7** | 13.7 | 20 | 14.7 | 4.9 | 1.1 |
| weave (half MotionBricks, ours at the keys) | 96 | 4.3 | 0 | 3.3 | 49 | 14.7 | 2.1 | 1.9 |
| *our own source* | 86 | 3.1 | 0 | 0 | - | 12.6 | 1.5 | - |

What it showed:
1. **Never let it pick the length for our clips.** Our attacks put key poses 8-15 frames apart. Left to choose, it
   makes 26-50 frames and we squeeze them 4.5× on average (up to 7×). The result is 2.7× our own accelerations, twice
   the hitches and feet 5 cm (up to 23 cm) under the floor. That was the game pipeline until this lab.
2. **It always hits the key poses exactly** (keys 0 cm): `correct_generated_diff` ends every generation on the target.
   Our strike timing survives any setting that keeps the keys.
3. **One target pose ("settle") is its best pure setting**: the smoothest, fewest hitches, least under the floor. Two
   poses ("speed") is best for moves that carry momentum through a key (Dodge_Left smooth 83 against settle's 167, and
   it re-imagines half as much). Four poses is never the best for our clips.
4. **The squash decides whether it beats us.** Where a gap is squeezed at most ~1.4× (14+ frames between keys), it was
   smoother than our own in-betweens every time:

   | clip | ours | settle | squash |
   |---|---|---|---|
   | Block_Hit | 20.9 | 3.4 | 1.29 |
   | Dodge_Backward | 148 | 3.6 | 1.38 |
   | Death_A | 96 | 16 | 1.29 |

   At 1.8-2.6× it is mixed. It is better on Lance_High_Thrust (32 to 16), SnS_Rush_1 (47 to 30), Lie_StandUp (97 to 42)
   and Jump (306 to 121). It is worse on Bow_Power_Shoot, Use_Item and Interact.
5. **Gentle clips should stay ours.** A held stance, a drink or a draw-and-hold (our source under ~15 m/s²: Katana_Iai
   0.1, Use_Item 2.2, Bow_Power_Shoot 3.0, Interact 10) came out livelier from MotionBricks than they should be (15-106).
6. **Dropping keys to give it room (merge) is smooth but misses the strike** (7.7 cm mean, up to 21 cm from our keys,
   and up to 25 cm under the floor on getting up). Not for attacks.
7. **Mixing it half with ours (weave) adds hitches** on fast moves (2H spin 526, Dodge_Backward 85). The two disagree
   about timing, and a blend of two timings jerks. It is only good on slow clips, where we keep ours anyway.
8. **Its feet slide.** Even where it marks a foot planted, the foot moves 40-60 cm/s on average in our squeezed clips
   (our own: 13 cm/s when low and still). Its contacts are the right signal to lock feet on. They must be applied after
   retargeting (our foot-slide fixer), not trusted as is.

### Round 3: the adaptive setting (rules 4 and 5 put together)
`adaptive`:
- MotionBricks fills only the gaps of 13 frames or more, with the length fitted to the gap.
- Two target poses through the middle keys and one on the last.
- Gentle clips (95th-percentile acceleration under 15 m/s²) stay ours.

`adaptive1` is the same with one target pose everywhere.

| setting | smooth | pops | keys cm | free cm | slide | slide (low) | floor | squash |
|---|---|---|---|---|---|---|---|---|
| base (the old pipeline) | 231 | 7.4 | 0 | 13.5 | 67 | 21.3 | 4.7 | 4.5 |
| settle | 58 | 2.1 | 0 | 9.5 | 42 | 16.6 | 1.7 | 1.9 |
| **adaptive** | **70** | **2.4** | 0 | **1.7** | 43 | **14.3** | **1.5** | **1.1** |
| adaptive1 | 70 | 2.3 | 0 | 2.0 | 43 | 14.3 | 1.4 | 1.1 |
| *our own source* | 86 | 3.1 | 0 | 0 | - | 12.6 | 1.5 | - |

- It fills 5 of the 16 clips and keeps the other 11 as ours, so the measure only moves where it helps:

  | clip | ours | adaptive |
  |---|---|---|
  | Dodge_Backward | 148 | 3.6 |
  | Death_A | 96 | 23 (16 with one pose) |
  | Block_Hit | 21 | 3.4 |
  | Spellcast_Shoot | 25 | 17 |
  | Lance_High_Thrust | 32 | 30 |

- Across all 16 clips: 18% lower accelerations than ours and a quarter fewer hitches. It stays within 1.7 cm of our
  poses on average, never sinks deeper than ours, and its feet slide a third less than the old pipeline's.
- **`adaptive` is the game pipeline's setting from now on** (`mb_generate.py`, default). `base` stays available.
  It takes effect at the next regeneration, through the body gate and the Proving Grounds like any build.

## Rules (what the lab has shown so far)
1. Fit the length to the gap (`allowed_token_counts=(round((gap + 6) / 4),)`, 6-16). Never the model's default range.
2. Give it room. Under ~13 frames between our keys, our own in-betweens are better: keep them.
3. One target pose to arrive and settle, two to carry momentum through a key. Four only for locomotion targets (its docs).
4. Leave gentle clips alone (held stances, drinking, aiming holds).
5. It always lands exactly on the target pose (`correct_generated_diff`), so our strike timing is safe.
6. Its foot contacts (columns: left heel, left toe, right heel, right toe) are the signal for foot locking after
   retargeting. Its feet still slide.

## Next experiments
- Moves that aren't attacks, where the middle key matters less (getting up, lying down, the jump): MotionBricks
  between the first and last keys only, at its own pace, over 0.6-1.9 s. In round 1, "settle" made Lie_StandUp 97 to 42
  and the jump 306 to 121.
- Foot locking from its contacts after retargeting (`footslide_blender` driven by the contact channels).
- Transitions between clips (attack recovery into the combat idle, idle into an attack's first key): its intended use.
- The raw ONNX masks (root without pose, pose without root) for targets where only the position matters.
