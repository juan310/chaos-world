using System;
using System.Collections.Generic;

namespace FH.Core
{
    // Authored, read-only game content. Loaded from JSON under Resources/Data and never written by gameplay.
    // Every definition is addressed by a stable string id; saves refer to these ids only.

    public enum ItemKind { Material, Consumable, Weapon, Armor, Charm, Gift, Key }
    public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }
    public enum DamageType { Slash, Blunt, Pierce, Fire, Holy, Poison }
    public enum EquipSlot { None, Weapon, Head, Body, Charm }
    public enum Stat { MaxHealth, MaxStamina, MaxMagic, Attack, Defense, SpellPower, StaminaRegen, PoiseDamage, CritChance, MoveSpeed, DodgeWindow }

    [Serializable] public class StatMod { public Stat stat; public float value; }
    [Serializable] public class ItemAmount { public string itemId; public int count = 1; }
    [Serializable] public class Resistance { public DamageType type; public float multiplier = 1; }

    [Serializable]
    public class ItemDef
    {
        public string id, name, description;
        public ItemKind kind;
        public Rarity rarity;
        public int maxStack = 99;        // 1 = unique item instance
        public int buyPrice;             // 0 = cannot be bought
        public int sellPrice;            // 0 = cannot be sold
        public EquipSlot slot;
        public List<StatMod> stats = new List<StatMod>();
        public float heal, restoreMagic, restoreStamina; // consumable effects
        public bool curePoison;
        public string icon;              // short glyph shown on slots
        public bool IsUnique => maxStack <= 1;
    }

    [Serializable]
    public class AttackDef
    {
        public string id, anim;
        public float windup = .2f, active = .15f, recovery = .35f;
        public float staminaCost = 18, damageMultiplier = 1, poiseMultiplier = 1;
        public float reach = 1.0f, radius = .25f, forwardStep;
        public float comboWindow = .25f;  // input buffered during the last part of recovery chains to next
        public float Total => windup + active + recovery;
    }

    [Serializable]
    public class WeaponDef
    {
        public string id;               // same id as the ItemDef
        public DamageType damageType;
        public float baseDamage = 20, poiseDamage = 10;
        public string grip = "handslot.r";
        public List<AttackDef> combo = new List<AttackDef>();
    }

    [Serializable]
    public class ArmorDef
    {
        public string id;               // same id as the ItemDef
        public float defense;
        public List<Resistance> resistances = new List<Resistance>();
        public string visual;           // name of the mesh piece that represents it, empty if unmodeled
    }

    [Serializable]
    public class StatusDef
    {
        public string id, name;
        public float buildup = 100, duration = 4, tickInterval = .5f, tickDamage, slowMultiplier = 1;
    }

    [Serializable]
    public class SpellDef
    {
        public string id, name, description, anim;
        public float magicCost = 20, staminaCost, cooldown = 2.5f, castTime = .35f;
        public float damage = 30, poiseDamage = 8, speed = 12, lifetime = 1.8f;
        public DamageType damageType = DamageType.Fire;
        public string statusId; public float statusBuildup;
        public string requiresSkill;    // empty = known from start
    }

    [Serializable]
    public class MonsterAttackDef
    {
        public string id, anim;
        public string hitBone;
        public float minRange, minAngle, maxAngle = 180, lungeSpeed;
        public string reaction;          // Flinch, Knockback or Knockdown; empty = by damage
        public float telegraph = .55f, active = .2f, recovery = .7f;
        public float damage = 12, range = 2.2f, radius = .9f, cooldown = 1.6f, weight = 1, animSpeed = 1;
        public DamageType damageType = DamageType.Slash;
        public float Total => telegraph + active + recovery;

        // Boss attacks (hunt rules, HuntBoss): an area instead of a hit volume, the phase that unlocks it, combos.
        public string aoe;                  // Circle, Ring (shockwave), Cone, Line, Roar, Burst (eruptions at the hunter), Leap
        public float aoeRadius, aoeWidth = 1, aoeAngle = 60, aoeLength, aoeSpeed = 10, aoeDelay, aoeSpread = 2.5f;
        public int aoeCount = 1, pulses = 1;
        public float pulseGap = .5f, roarTime = 1.1f;
        public int phase = 1;               // 1 always, 2 once enraged, 3 when desperate (low health); weight < 0: only as a follow-up
        public string follow;               // combo: the attack that may follow this one at once
        public float followChance;
        public bool gapCloser;              // charges / leaps: preferred when the hunter is far or healing
        public bool spin;                   // the monster turns a full circle during the active window (tail spin)
        public AoeShape Shape => Enum.TryParse(aoe, true, out AoeShape s) ? s : AoeShape.None;
        public MonsterAttackDef Clone() => (MonsterAttackDef)MemberwiseClone();
    }

    [Serializable]
    public class MonsterPartDef
    {
        public string id, name, bone;
        public float radius = .3f, height;           // height>0 = capsule
        public float damageMultiplier = 1;
        public bool weakPoint;
        public float breakHealth;                      // 0 = cannot break
        public string breakMesh;                       // renderer name hidden and detached when broken
        public string breakLootTable;
        public float brokenDamageMultiplier;           // multiplier after break (0 = unchanged)
        public string requiresBrokenPart;              // weak point only exposed once this part is broken
    }

    [Serializable]
    public class MonsterDef
    {
        public string id, name, model, family;
        public float bodyRadius;
        public float flinchThreshold, stunThreshold;   // hunt combat: 0 = derived from max health
        public float height = 1.8f, maxHealth = 90, poise = 40, poiseRegen = 8, staggerTime = 1.1f;
        public float walkSpeed = 1.6f, chaseSpeed = 2.2f, turnSpeed = 7;
        public float perceptionRadius = 11, fieldOfView = 140, hearingRadius = 4, leashRadius = 18, loseInterestTime = 5;
        public float spawnGrace = 1.5f;
        public List<Resistance> resistances = new List<Resistance>();
        public List<string> statusImmunities = new List<string>();
        public List<MonsterAttackDef> attacks = new List<MonsterAttackDef>();
        public List<MonsterPartDef> parts = new List<MonsterPartDef>();
        public string lootTable, weaponVisual;
        public int xp, gold;
        public bool boss;
        public string assetStatus = "ready";           // "ready" or a description of the missing dependency
        // Hunt rules (Proving Grounds, Training Range): extra boss attacks and tougher stats; the valley is unchanged.
        public List<MonsterAttackDef> huntAttacks = new List<MonsterAttackDef>();
        public float huntHealth = 1, huntPoise = 1, huntDamage = 1;
        public string roarAttack;                      // roared when it enrages and when it turns desperate
        public MonsterDef CloneShallow() => (MonsterDef)MemberwiseClone();
    }

    [Serializable] public class LootEntry { public string itemId; public int min = 1, max = 1; public float chance = 1; }
    [Serializable] public class LootTableDef { public string id; public List<LootEntry> entries = new List<LootEntry>(); }

    public enum ConditionType { None, Level, Rank, Reputation, Gold, HasItem, ContractStatus, QuestStage, Affinity, RelationshipStage, EstateTier, Flag, NotFlag, Skill, TimeOfDay, Area }

    [Serializable]
    public class Condition
    {
        public ConditionType type;
        public string id;            // item/contract/npc/faction/flag id
        public float value;          // threshold / count
        public string state;         // contract status or relationship stage
        public string label;         // optional player-facing explanation override
    }

    public enum EffectType { None, SetFlag, ClearFlag, Affinity, GiveItem, TakeItem, Gold, Reputation, AcceptContract, ClaimContract, StartQuest, SetRelationship, OpenShop, OpenCrafting, OpenStorage, OpenGift, Xp, CloseDialogue }

    [Serializable]
    public class Effect { public EffectType type; public string id; public float value; public string state; }

    public enum ObjectiveType { Kill, Collect, Break, Talk, EnterArea, Craft, EstateTier }

    [Serializable]
    public class ObjectiveDef
    {
        public string id, description, targetId;
        public ObjectiveType type;
        public int count = 1;
    }

    [Serializable]
    public class FactionAmount { public string factionId; public int amount; }

    [Serializable]
    public class RewardDef
    {
        public int gold, xp, hunterReputation;
        public List<ItemAmount> items = new List<ItemAmount>();
        public List<FactionAmount> reputation = new List<FactionAmount>();
        public List<string> unlockRecipes = new List<string>();
        public List<string> setFlags = new List<string>();
    }

    [Serializable]
    public class ContractDef
    {
        public string id, name, description, giverNpcId, areaId, targetMonsterId;
        public int rankRequired;
        public List<Condition> prerequisites = new List<Condition>();
        public List<ObjectiveDef> objectives = new List<ObjectiveDef>();
        public RewardDef reward = new RewardDef();
        public bool repeatable;
        public int cooldownDays = 1;
    }

    [Serializable]
    public class QuestStageDef
    {
        public string id, description;
        public List<ObjectiveDef> objectives = new List<ObjectiveDef>();
        public RewardDef reward = new RewardDef();
    }

    [Serializable]
    public class QuestDef
    {
        public string id, name, description;
        public bool autoStart;
        public List<Condition> prerequisites = new List<Condition>();
        public List<QuestStageDef> stages = new List<QuestStageDef>();
    }

    public enum Station { None, Forge, Workbench, Alchemy }

    [Serializable]
    public class RecipeDef
    {
        public string id, name, description;
        public Station station;
        public List<ItemAmount> inputs = new List<ItemAmount>();
        public int goldCost;
        public string upgradeFrom;   // unique item consumed as the base of an upgrade
        public ItemAmount output = new ItemAmount();
        public bool unlockedByDefault;
        public List<Condition> requirements = new List<Condition>();
    }

    [Serializable]
    public class EstateTierDef
    {
        public string id, name, description;
        public int tier, goldCost, incomePerDay, storageSlots, inventoryBonus, maxIncomeDays = 3;
        public float restHealBonus;
        public List<ItemAmount> materials = new List<ItemAmount>();
        public List<Condition> requirements = new List<Condition>();
        public List<Station> stations = new List<Station>();
        public List<string> visualGroups = new List<string>();
        public bool modeled;          // false = data hook only, no art yet
    }

    [Serializable] public class GiftPreference { public string itemId; public int affinity; }

    [Serializable]
    public class NpcDef
    {
        public string id, name, title, role, factionId, model, vendorId, dialogueId, homeArea;
        public bool romanceable;
        public List<GiftPreference> gifts = new List<GiftPreference>();
        public int defaultGiftAffinity = 1;
    }

    [Serializable] public class FactionDef { public string id, name, description; }
    [Serializable] public class RankDef { public int rank; public string name; public int reputationRequired; }

    [Serializable]
    public class RelationshipStageDef
    {
        public string id, name;
        public int order, affinityRequired;
        public List<Condition> requirements = new List<Condition>(); // extra gates (items, estate tier, flags)
        public bool automatic = true;  // false = only advanced by a dialogue effect
    }

    [Serializable]
    public class SkillDef
    {
        public string id, name, description, branch;
        public int cost = 1, levelRequired = 1;
        public List<string> requires = new List<string>();
        public List<StatMod> stats = new List<StatMod>();
        public string unlockSpell;
    }

    [Serializable] public class AttributeDef { public string id, name, description; public List<StatMod> perPoint = new List<StatMod>(); }

    [Serializable]
    public class VendorStock { public string itemId; public int price; public int stock = -1; public List<Condition> requirements = new List<Condition>(); }

    [Serializable]
    public class VendorDef
    {
        public string id, name, npcId;
        public float sellRate = 1;   // multiplier applied to ItemDef.sellPrice
        public List<VendorStock> stock = new List<VendorStock>();
        public List<ItemKind> buysKinds = new List<ItemKind>();
    }

    [Serializable]
    public class DialogueChoice
    {
        public string text, next;
        public List<Condition> conditions = new List<Condition>();
        public List<Effect> effects = new List<Effect>();
        public bool hideWhenUnavailable;
    }

    [Serializable]
    public class DialogueNode
    {
        public string id, speaker, text;
        public List<Condition> conditions = new List<Condition>(); // used by entry selection
        public List<Effect> onEnter = new List<Effect>();
        public List<DialogueChoice> choices = new List<DialogueChoice>();
    }

    [Serializable]
    public class DialogueDef
    {
        public string id;
        public List<string> entries = new List<string>(); // node ids tried in order; first whose conditions pass starts
        public List<DialogueNode> nodes = new List<DialogueNode>();
    }

    public enum AreaKind { Home, Settlement, HuntArea, Dungeon }

    [Serializable]
    public class AreaDef
    {
        public string id, name;
        public AreaKind kind;
        public bool safe;
        public float x, z, radius;     // overworld footprint (instances use their own origin)
        public string returnArea;      // dungeon exit target
        public List<Condition> entryRequirements = new List<Condition>();
    }

    [Serializable]
    public class SpawnDef
    {
        public string id, areaId, monsterId;
        public float x, z, patrolRadius = 3;
        public float respawnHours = 24;     // in-game hours; <0 never respawns
        public List<Condition> requirements = new List<Condition>();
        public bool nightOnly;
    }

    [Serializable]
    public class BalanceDef
    {
        public float baseHealth = 120, baseStamina = 100, baseMagic = 60, baseAttack = 0, baseStaminaRegen = 22;
        public float staminaRegenDelay = .6f, exhaustedThreshold = 15;
        public int baseInventorySlots = 24, startGold = 75, attributePointsPerLevel = 2, skillPointsPerLevel = 1;
        public int xpBase = 60; public float xpGrowth = 1.45f; public int maxLevel = 30;
        public float deathGoldLossFraction = .1f;
        public float hoursPerRealMinute = 1, dayStartHour = 7;
        public string startArea = "home", startWeapon, startArmor;
        public List<ItemAmount> startItems = new List<ItemAmount>();
        public List<string> startRecipes = new List<string>();
    }

    /// <summary>All authored content. One instance per data file; merged by GameDatabase.</summary>
    [Serializable]
    public class ContentPack
    {
        public BalanceDef balance;
        public List<ItemDef> items = new List<ItemDef>();
        public List<WeaponDef> weapons = new List<WeaponDef>();
        public List<ArmorDef> armors = new List<ArmorDef>();
        public List<StatusDef> statuses = new List<StatusDef>();
        public List<SpellDef> spells = new List<SpellDef>();
        public List<MonsterDef> monsters = new List<MonsterDef>();
        public List<LootTableDef> lootTables = new List<LootTableDef>();
        public List<ContractDef> contracts = new List<ContractDef>();
        public List<QuestDef> quests = new List<QuestDef>();
        public List<RecipeDef> recipes = new List<RecipeDef>();
        public List<EstateTierDef> estateTiers = new List<EstateTierDef>();
        public List<NpcDef> npcs = new List<NpcDef>();
        public List<FactionDef> factions = new List<FactionDef>();
        public List<RankDef> ranks = new List<RankDef>();
        public List<RelationshipStageDef> relationshipStages = new List<RelationshipStageDef>();
        public List<SkillDef> skills = new List<SkillDef>();
        public List<AttributeDef> attributes = new List<AttributeDef>();
        public List<VendorDef> vendors = new List<VendorDef>();
        public List<DialogueDef> dialogues = new List<DialogueDef>();
        public List<AreaDef> areas = new List<AreaDef>();
        public List<SpawnDef> spawns = new List<SpawnDef>();
        public List<HuntMovesetDef> huntMovesets = new List<HuntMovesetDef>();
    }
}
