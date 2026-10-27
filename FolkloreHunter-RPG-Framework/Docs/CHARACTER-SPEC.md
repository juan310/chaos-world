# Character models: what the game needs (for an outside artist or another AI)

The 0.9 characters made on this PC were rejected on 2026-10-27, and the review by eye agreed with the owner. Here is what went wrong:
- Forge (TripoSG) bodies: the textures are smeared and the faces melted. The hands are mittens. Shoulders and elbows crush when they bend.
- The KayKit rig was built for chibi proportions. The retargeted clips put arms through the torso, spread arms in the idles and give the running poses broken legs.
- The low-poly KayKit weapons don't match a realistic body.

Any replacement must keep the rules in `CLAUDE.md`: our own designs only. Nothing copied from another game, and no store assets unless the owner authorizes them.

## How a model gets into the game

Put each character in `Assets/Resources/Models/<Name>.fbx`. Textures go next to it in the same folder.
- Player: `Hunter_Male`, `Hunter_Female`. Hair variants are `Hunter_Male_NN` and `Hunter_Female_NN`.
- Villagers: `Hunter_NpcMira`, `Hunter_NpcBram`, `Hunter_NpcElowen`.
- Monsters: `Gravebound_Warrior`, `Gravebound_Knight`, `Gravebound_Mage`, `GraveWarden`.

Keep the old files on the side, because the game falls back to them.

The game plays animations through Unity's legacy `Animation` component, by clip name (`Game/AnimationDriver.cs`). The clips must therefore be inside the FBX, as takes on the model's own skeleton. No Animator controller and no Humanoid avatar are involved.

The size is set from the model's import bounds (`CharacterFactory`). These bounds cover every clip, so no clip may reach higher than a normal jump. The game makes the hunter about 1.8 m tall and stands them on their feet.

### Required
- **Format:** one FBX per character, Y up, facing +Z in Unity. The skin is weighted to one armature, with at most four bone influences per vertex.
- **Export:** from Blender, use `apply_scale_options='FBX_SCALE_ALL'`, `add_leaf_bones=False` and `bake_anim_use_all_actions=True`.
- **Bones the code looks up by name:**
  - `hips`, `chest`, `head`, `foot.l`, `foot.r`;
  - `handslot.r` (weapon grip, right hand) and `handslot.l` (shield or bow, left hand).

  The handslots are child bones of the hands, placed where the palm closes on a grip.
- **Weapons:** separate meshes parented to the handslots, named `1H_Sword`, `Round_Shield`, `Hunt_Bow`, `Hunt_Quiver`, `Hunt_Lance`, `Hunt_TowerShield`, `Hunt_Katana` (with a `Hunt_KatanaGlow` overlay) and `Hunt_NockedArrow`. `Hunt_Arrow` is optional. The game shows only the ones for the equipped weapon. Villagers don't need any.
- **Size:** a realistic adult, about 1.75–1.85 m, in a T- or A-pose at rest. Use one material, or a few, with 2K textures.

### Clip names
A missing clip falls back to a nearby one or to the idle, so the list below runs from most important to least.

- **Hunter, movement:**
  - `Idle`, `Idle_Combat`;
  - `Gait_Walk`, `Gait_Jog`, `Gait_Sprint` (looped, in place, and the feet must not slide);
  - `Dodge_Forward`, `Hit_A`, `Hit_B`, `Death_A`, `Use_Item`, `Interact`, `Ride_Idle`, `Lie_Down`, `Lie_StandUp`.
- **Sword and shield:**
  - `1H_Melee_Attack_Chop`, `1H_Melee_Attack_Slice_Diagonal`, `1H_Melee_Attack_Slice_Horizontal`, `1H_Melee_Attack_Stab`;
  - `Blocking`, `Block_Hit`, `Block_Attack`;
  - `SnS_Rush_1`, `SnS_Rush_2`, `SnS_Rush_3`, `SnS_Leap_Slash`, `SnS_Shield_Crash`, `SnS_Rend`, `Hunt_Clash`.
- **Katana:**
  - `2H_Melee_Idle`, `2H_Melee_Attack_Chop`, `2H_Melee_Attack_Slice`, `2H_Melee_Attack_Spin`, `2H_Melee_Attack_Spinning`, `2H_Melee_Attack_Stab`;
  - `Katana_Iai`, `Katana_Rend`.
- **Lance:** `Lance_High_Thrust`, `Lance_Charge`, `Lance_Rend`.
- **Bow:** `Bow_Draw`, `Bow_Shoot`, `Bow_Power_Shoot`, `Bow_Arc_Shoot`, `Bow_Piercer`, `Bow_Tracer`, `Bow_Aim_Walk`, `Bow_Step_L`.
- **Spell:** `Spellcast_Shoot`.
- **Skeletons and the Warden:**
  - `Idle_B`, `Walking_A`, `Running_A`;
  - `1H_Melee_Attack_Chop`, `1H_Melee_Attack_Slice_Diagonal`, `Blocking`, `Hit_A`, `Death_A`, `Spellcast_Shoot` (mage).

Root motion is not used. The game moves the character itself, and it measures stride from the feet to set the playback speed.

## How a delivered model gets checked
1. Copy the FBX into `Assets/Resources/Models/` on the PC and run `C:\AW\briarhound_work\build.ps1`. It builds, then runs the proving checks: height, the battle checks, and Claude Eyes on 16-frame in-game sequences.
2. Play it with the real controls in the Proving Grounds: walk, jog, sprint, dodge, every weapon, and the skeleton fights.
