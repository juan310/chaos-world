using System;
using System.Collections.Generic;
using System.Linq;

namespace FH.Core
{
    // Mutable, saved game state. Contains only ids and numbers; definitions live in GameDatabase.

    [Serializable]
    public class ItemStack
    {
        public string uid;       // unique items only; empty for stackables
        public string itemId;
        public int count;
    }

    [Serializable] public class AttributeValue { public string id; public int points; }

    [Serializable]
    public class PlayerState
    {
        public string name = "Hunter";
        /// <summary>The look chosen at a new game (Appearance): "male", "female" or "classic"; empty in saves from before 0.9 (classic).</summary>
        public string body = "";
        public int hair = 1;
        public int level = 1, xp, attributePoints, skillPoints;
        public List<AttributeValue> attributes = new List<AttributeValue>();
        public List<string> skills = new List<string>();
        public float health = -1, stamina = -1, magic = -1;   // -1 = full on load
        public string areaId = "home";
        public float x = -9, y, z = -21, yaw;
        public int hunterReputation;
        public List<string> hotbar = new List<string> { "potion_mooncap", "", "", "" };
        public int deaths;
    }

    [Serializable]
    public class EquipmentState { public string weapon = "", head = "", body = "", charm = ""; }

    public enum ContractStatus { Locked, Available, Active, Completed, Claimed }

    [Serializable]
    public class ContractState
    {
        public string id;
        public ContractStatus status;
        public List<int> progress = new List<int>();
        public int timesClaimed, lastClaimDay = -999;
    }

    public enum QuestStatus { NotStarted, Active, Completed }

    [Serializable]
    public class QuestState
    {
        public string id;
        public QuestStatus status;
        public int stage;
        public List<int> progress = new List<int>();
    }

    [Serializable] public class FactionStanding { public string factionId; public int value; }

    [Serializable]
    public class RelationshipState
    {
        public string npcId;
        public int affinity;
        public string stage = "stranger";
        public int lastGiftDay = -999, lastTalkDay = -999, stageSinceDay;
    }

    [Serializable]
    public class EstateState
    {
        public int tier;
        public int lastIncomeDay;
        public List<ItemStack> storage = new List<ItemStack>();
        public int totalIncome;
    }

    [Serializable]
    public class LootBagState { public string id, areaId, label; public float x, y, z; public List<ItemAmount> items = new List<ItemAmount>(); }

    [Serializable] public class VendorSale { public string vendorId, itemId; public int count, day; }

    [Serializable] public class SpawnState { public string spawnId; public double deadUntilHour = -1; public List<string> brokenParts = new List<string>(); }

    [Serializable]
    public class WorldState
    {
        public double hours;                 // absolute in-game hours since day 0 at midnight
        public List<string> flags = new List<string>();
        public List<string> usedPickups = new List<string>();
        public List<SpawnState> spawns = new List<SpawnState>();
        public List<string> claimedRewards = new List<string>(); // reward-once guard keys
        public List<string> discoveredAreas = new List<string>();
        public List<LootBagState> lootBags = new List<LootBagState>();   // dropped loot not yet picked up
        public int Day => (int)Math.Floor(hours / 24.0);
        public double HourOfDay => hours - Day * 24.0;
    }

    [Serializable]
    public class GameState
    {
        public const int CurrentSchema = 3;
        public int schemaVersion = CurrentSchema;
        public string saveId = "";
        public string contentVersion = "";
        public double playSeconds;
        public string savedAtUtc = "";
        public PlayerState player = new PlayerState();
        public List<ItemStack> inventory = new List<ItemStack>();
        public EquipmentState equipment = new EquipmentState();
        public int gold;
        public List<string> knownRecipes = new List<string>();
        public List<ContractState> contracts = new List<ContractState>();
        public List<QuestState> quests = new List<QuestState>();
        public List<FactionStanding> reputation = new List<FactionStanding>();
        public List<RelationshipState> relationships = new List<RelationshipState>();
        public EstateState estate = new EstateState();
        public WorldState world = new WorldState();
        public List<VendorSale> vendorSales = new List<VendorSale>();
        public int nextUid = 1;

        public ContractState Contract(string id)
        {
            var c = contracts.FirstOrDefault(x => x.id == id);
            if (c == null) { c = new ContractState { id = id, status = ContractStatus.Locked }; contracts.Add(c); }
            return c;
        }

        public QuestState Quest(string id)
        {
            var q = quests.FirstOrDefault(x => x.id == id);
            if (q == null) { q = new QuestState { id = id }; quests.Add(q); }
            return q;
        }

        public RelationshipState Relationship(string npcId)
        {
            var r = relationships.FirstOrDefault(x => x.npcId == npcId);
            if (r == null) { r = new RelationshipState { npcId = npcId }; relationships.Add(r); }
            return r;
        }

        public SpawnState Spawn(string id)
        {
            var s = world.spawns.FirstOrDefault(x => x.spawnId == id);
            if (s == null) { s = new SpawnState { spawnId = id }; world.spawns.Add(s); }
            return s;
        }

        public int Faction(string id) => reputation.FirstOrDefault(x => x.factionId == id)?.value ?? 0;

        public void AddFaction(string id, int amount)
        {
            var f = reputation.FirstOrDefault(x => x.factionId == id);
            if (f == null) { f = new FactionStanding { factionId = id }; reputation.Add(f); }
            f.value += amount;
        }

        public bool HasFlag(string flag) => world.flags.Contains(flag);
        public void SetFlag(string flag) { if (!world.flags.Contains(flag)) world.flags.Add(flag); }
        public int Attribute(string id) => player.attributes.FirstOrDefault(a => a.id == id)?.points ?? 0;
        public string NewUid() => "u" + (nextUid++);
    }

    [Serializable]
    public class SettingsState
    {
        public int schemaVersion = 1;
        public float masterVolume = .8f, mouseSensitivity = 1, uiScale = 1;
        public bool invertY, fullscreen;
        public bool showDamageNumbers = true;
        /// <summary>The look last chosen or played (Appearance), used by the Proving Grounds and the Training Range.</summary>
        public string lookBody = "";
        public int lookHair = 1;
    }
}
