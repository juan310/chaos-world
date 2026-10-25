using System;
using System.Linq;

namespace FH.Core
{
    /// <summary>Composition root for one play session: owns the state and wires the systems together.
    /// Holds no gameplay rules itself; each rule lives in its system.</summary>
    public class GameCore
    {
        public readonly GameDatabase Db;
        public readonly GameState State;
        public readonly GameEvents Events = new GameEvents();
        public readonly Wallet Wallet;
        public readonly InventorySystem Inventory;
        public readonly StatCalculator Stats;
        public readonly ProgressionSystem Progression;
        public readonly Conditions Conditions;
        public readonly RewardService Rewards;
        public readonly ContractSystem Contracts;
        public readonly QuestSystem Quests;
        public readonly CraftingSystem Crafting;
        public readonly VendorSystem Vendors;
        public readonly EstateSystem Estate;
        public readonly RelationshipSystem Relationships;
        public readonly EffectRunner Effects;
        public readonly WorldClock Clock;
        public readonly SpawnRules Spawns;
        public readonly LootRoller Loot;

        public GameCore(GameDatabase db, GameState state)
        {
            Db = db; State = state;
            Wallet = new Wallet(state, Events);
            Inventory = new InventorySystem(db, state, Events, InventoryCapacity);
            Stats = new StatCalculator(db, state);
            Progression = new ProgressionSystem(db, state, Events);
            Conditions = new Conditions(db, state);
            Rewards = new RewardService(db, state, Events, Inventory, Wallet, Progression);
            Contracts = new ContractSystem(db, state, Events, Conditions, Rewards);
            Quests = new QuestSystem(db, state, Events, Conditions, Rewards);
            Crafting = new CraftingSystem(db, state, Events, Inventory, Wallet, Conditions);
            Vendors = new VendorSystem(db, state, Events, Inventory, Wallet, Conditions);
            Estate = new EstateSystem(db, state, Events, Inventory, Wallet, Conditions);
            Relationships = new RelationshipSystem(db, state, Events, Inventory, Conditions);
            Effects = new EffectRunner(this);
            Clock = new WorldClock(db, state, Events);
            Spawns = new SpawnRules(db, state, Conditions, Clock);
            Loot = new LootRoller(db);

            Events.MonsterKilled += (monster, spawn) => Objective(ObjectiveType.Kill, monster);
            Events.PartBroken += (monster, part) => Objective(ObjectiveType.Break, monster + "." + part);
            Events.ItemAdded += (item, n, source) => { if (source == ItemSource.Loot || source == ItemSource.Gather) Objective(ObjectiveType.Collect, item, n); };
            Events.Crafted += item => Objective(ObjectiveType.Craft, item);
            Events.Talked += npc => Objective(ObjectiveType.Talk, npc);
            Events.AreaEntered += area => { if (!State.world.discoveredAreas.Contains(area)) State.world.discoveredAreas.Add(area); Objective(ObjectiveType.EnterArea, area); };
            Events.EstateTierChanged += _ => AfterStateChange();
            Events.LevelUp += _ => AfterStateChange();
            Events.DayChanged += _ => { Spawns.ResetRespawned(); AfterStateChange(); };
        }

        int InventoryCapacity()
        {
            int bonus = Db.EstateTiers.Where(t => t.tier <= State.estate.tier).Sum(t => t.inventoryBonus);
            return Db.Balance.baseInventorySlots + bonus;
        }

        public void Objective(ObjectiveType type, string id, int amount = 1)
        {
            var e = new ObjectiveEvent(type, id, amount);
            Contracts.Handle(e);
            Quests.Handle(e);
            AfterStateChange();
        }

        bool refreshing;
        /// <summary>Re-evaluates gated content (contract unlocks, auto-start quests) after any meaningful change.</summary>
        public void AfterStateChange()
        {
            if (refreshing) return;
            refreshing = true;
            try { Contracts.Refresh(); Quests.Refresh(); }
            finally { refreshing = false; }
        }

        public static GameState NewGameState(GameDatabase db, string saveId)
        {
            var b = db.Balance;
            var s = new GameState { saveId = saveId };
            s.gold = b.startGold;
            s.world.hours = b.dayStartHour;
            s.player.areaId = b.startArea;
            s.knownRecipes.AddRange(b.startRecipes);
            foreach (var r in db.Recipes.Values.Where(r => r.unlockedByDefault)) if (!s.knownRecipes.Contains(r.id)) s.knownRecipes.Add(r.id);
            var core = new GameCore(db, s);
            if (!string.IsNullOrEmpty(b.startWeapon))
            {
                core.Inventory.Add(b.startWeapon, 1, ItemSource.Start, out var w);
                s.equipment.weapon = w.uid;
            }
            if (!string.IsNullOrEmpty(b.startArmor))
            {
                core.Inventory.Add(b.startArmor, 1, ItemSource.Start, out var a);
                var slot = db.Item(b.startArmor).slot;
                if (slot == EquipSlot.Body) s.equipment.body = a.uid; else if (slot == EquipSlot.Head) s.equipment.head = a.uid;
            }
            foreach (var it in b.startItems) core.Inventory.Add(it.itemId, it.count, ItemSource.Start);
            foreach (var n in db.Npcs.Values) s.Relationship(n.id);
            core.AfterStateChange();
            return s;
        }
    }
}
