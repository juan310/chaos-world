using System;
using System.Collections.Generic;
using System.Linq;

namespace FH.Core
{
    public class Wallet
    {
        readonly GameState state;
        readonly GameEvents events;
        public Wallet(GameState state, GameEvents events) { this.state = state; this.events = events; }
        public int Gold => state.gold;

        public Result CanSpend(int amount)
        {
            if (amount < 0) return Result.Fail("Invalid price.");
            if (state.gold < amount) return Result.Fail("Needs " + amount + " gold (have " + state.gold + ").");
            return Result.Success;
        }

        public Result Spend(int amount)
        {
            var r = CanSpend(amount);
            if (!r.Ok) return r;
            int before = state.gold;
            state.gold -= amount;
            events.RaiseGold(before, state.gold);
            return Result.Success;
        }

        public void Earn(int amount)
        {
            if (amount <= 0) return;
            int before = state.gold;
            state.gold = (int)Math.Min(int.MaxValue, (long)state.gold + amount);
            events.RaiseGold(before, state.gold);
        }

        /// <summary>Loses a fraction of gold (death penalty). Never goes negative.</summary>
        public int LoseFraction(float fraction)
        {
            int loss = (int)Math.Floor(state.gold * Math.Max(0, Math.Min(1, fraction)));
            if (loss <= 0) return 0;
            int before = state.gold;
            state.gold -= loss;
            events.RaiseGold(before, state.gold);
            return loss;
        }
    }

    /// <summary>Evaluates data-driven conditions and explains failures in player language.</summary>
    public class Conditions
    {
        readonly GameDatabase db;
        readonly GameState state;
        public Func<string> CurrentArea = () => "";
        public Conditions(GameDatabase db, GameState state) { this.db = db; this.state = state; }

        public int StageOrder(string stageId) => db.RelationshipStages.FirstOrDefault(s => s.id == stageId)?.order ?? 0;

        public bool Check(Condition c)
        {
            switch (c.type)
            {
                case ConditionType.None: return true;
                case ConditionType.Level: return state.player.level >= c.value;
                case ConditionType.Rank: return db.RankFor(state.player.hunterReputation).rank >= c.value;
                case ConditionType.Reputation: return state.Faction(c.id) >= c.value;
                case ConditionType.Gold: return state.gold >= c.value;
                case ConditionType.HasItem: return state.inventory.Where(s => s.itemId == c.id).Sum(s => s.count) >= Math.Max(1, c.value);
                case ConditionType.ContractStatus:
                    {
                        var cs = state.contracts.FirstOrDefault(x => x.id == c.id);
                        var st = cs?.status ?? ContractStatus.Locked;
                        if (c.state == "claimedOnce") return cs != null && cs.timesClaimed > 0;
                        return string.Equals(st.ToString(), c.state, StringComparison.OrdinalIgnoreCase);
                    }
                case ConditionType.QuestStage:
                    {
                        var q = state.quests.FirstOrDefault(x => x.id == c.id);
                        if (c.state == "completed") return q != null && q.status == QuestStatus.Completed;
                        return q != null && (q.status == QuestStatus.Completed || (q.status == QuestStatus.Active && q.stage >= c.value));
                    }
                case ConditionType.Affinity: return (state.relationships.FirstOrDefault(r => r.npcId == c.id)?.affinity ?? 0) >= c.value;
                case ConditionType.RelationshipStage:
                    {
                        var r = state.relationships.FirstOrDefault(x => x.npcId == c.id);
                        return StageOrder(r?.stage ?? "stranger") >= StageOrder(c.state);
                    }
                case ConditionType.EstateTier: return state.estate.tier >= c.value;
                case ConditionType.Flag: return state.HasFlag(c.id);
                case ConditionType.NotFlag: return !state.HasFlag(c.id);
                case ConditionType.Skill: return state.player.skills.Contains(c.id);
                case ConditionType.TimeOfDay:
                    {
                        // value = from hour, state = to hour (wraps midnight)
                        double h = state.world.HourOfDay, from = c.value, to = double.Parse(string.IsNullOrEmpty(c.state) ? "24" : c.state, System.Globalization.CultureInfo.InvariantCulture);
                        return from <= to ? h >= from && h < to : h >= from || h < to;
                    }
                case ConditionType.Area: return CurrentArea() == c.id;
                default: return false;
            }
        }

        public bool All(IEnumerable<Condition> list) => list == null || list.All(Check);

        public Result Explain(IEnumerable<Condition> list)
        {
            if (list == null) return Result.Success;
            foreach (var c in list) if (!Check(c)) return Result.Fail(Describe(c));
            return Result.Success;
        }

        public string Describe(Condition c)
        {
            if (!string.IsNullOrEmpty(c.label)) return c.label;
            switch (c.type)
            {
                case ConditionType.Level: return "Requires level " + c.value + ".";
                case ConditionType.Rank:
                    {
                        var rank = db.Ranks.FirstOrDefault(r => r.rank >= c.value);
                        return "Requires Hunter Rank " + c.value + (rank != null ? " (" + rank.name + ")" : "") + ".";
                    }
                case ConditionType.Reputation: return "Requires " + c.value + " standing with " + (db.Factions.TryGetValue(c.id, out var f) ? f.name : c.id) + ".";
                case ConditionType.Gold: return "Requires " + c.value + " gold.";
                case ConditionType.HasItem: return "Requires " + Math.Max(1, c.value) + "× " + db.ItemName(c.id) + ".";
                case ConditionType.ContractStatus: return "Requires the contract \"" + (db.Contracts.TryGetValue(c.id, out var k) ? k.name : c.id) + "\" first.";
                case ConditionType.QuestStage: return "Continue \"" + (db.Quests.TryGetValue(c.id, out var q) ? q.name : c.id) + "\" first.";
                case ConditionType.Affinity: return "Needs a closer bond with " + (db.Npcs.TryGetValue(c.id, out var n) ? n.name : c.id) + ".";
                case ConditionType.RelationshipStage:
                    {
                        var stage = db.RelationshipStages.FirstOrDefault(s => s.id == c.state);
                        return "Requires being " + (stage?.name ?? c.state).ToLowerInvariant() + " with " + (db.Npcs.TryGetValue(c.id, out var n2) ? n2.name : c.id) + ".";
                    }
                case ConditionType.EstateTier: return "Requires estate: " + (db.Tier((int)c.value)?.name ?? ("tier " + c.value)) + ".";
                case ConditionType.Skill: return "Requires the skill " + (db.Skills.TryGetValue(c.id, out var s) ? s.name : c.id) + ".";
                case ConditionType.TimeOfDay: return "Only available between " + c.value + ":00 and " + c.state + ":00.";
                case ConditionType.Area: return "Must be at " + (db.Areas.TryGetValue(c.id, out var a) ? a.name : c.id) + ".";
                case ConditionType.Flag: return "Not yet available.";
                case ConditionType.NotFlag: return "Already done.";
                default: return "Not available.";
            }
        }
    }

    /// <summary>Grants rewards exactly once per key. The key is recorded in the save before returning.</summary>
    public class RewardService
    {
        readonly GameDatabase db;
        readonly GameState state;
        readonly GameEvents events;
        readonly InventorySystem inventory;
        readonly Wallet wallet;
        readonly ProgressionSystem progression;

        public RewardService(GameDatabase db, GameState state, GameEvents events, InventorySystem inventory, Wallet wallet, ProgressionSystem progression)
        {
            this.db = db; this.state = state; this.events = events; this.inventory = inventory; this.wallet = wallet; this.progression = progression;
        }

        public bool AlreadyGranted(string key) => state.world.claimedRewards.Contains(key);

        public Result CanGrant(RewardDef r, string key)
        {
            if (AlreadyGranted(key)) return Result.Fail("This reward has already been claimed.");
            if (r.items.Count > 0)
            {
                var fit = inventory.CanAdd(r.items);
                if (!fit.Ok) return Result.Fail("Make room in your pack first. " + fit.Reason);
            }
            return Result.Success;
        }

        public Result Grant(RewardDef r, string key)
        {
            var check = CanGrant(r, key);
            if (!check.Ok) return check;
            state.world.claimedRewards.Add(key);
            if (r.items.Count > 0) inventory.AddAll(r.items, ItemSource.Reward);
            wallet.Earn(r.gold);
            progression.AddHunterReputation(r.hunterReputation);
            foreach (var f in r.reputation) state.AddFaction(f.factionId, f.amount);
            foreach (var recipe in r.unlockRecipes)
                if (!state.knownRecipes.Contains(recipe))
                {
                    state.knownRecipes.Add(recipe);
                    events.RaiseNotice("New recipe: " + (db.Recipes.TryGetValue(recipe, out var rd) ? rd.name : recipe), NoticeKind.Reward);
                }
            foreach (var flag in r.setFlags) { state.SetFlag(flag); events.RaiseFlag(flag); }
            progression.AddXp(r.xp);
            return Result.Success;
        }

        public static string Describe(GameDatabase db, RewardDef r)
        {
            var parts = new List<string>();
            if (r.gold > 0) parts.Add(r.gold + " gold");
            if (r.xp > 0) parts.Add(r.xp + " XP");
            if (r.hunterReputation > 0) parts.Add(r.hunterReputation + " hunter reputation");
            parts.AddRange(r.items.Select(i => i.count + "× " + db.ItemName(i.itemId)));
            parts.AddRange(r.reputation.Select(f => "+" + f.amount + " " + (db.Factions.TryGetValue(f.factionId, out var fd) ? fd.name : f.factionId)));
            parts.AddRange(r.unlockRecipes.Select(x => "Recipe: " + (db.Recipes.TryGetValue(x, out var rd) ? rd.name : x)));
            return string.Join(", ", parts);
        }
    }

    public class LootRoller
    {
        readonly GameDatabase db;
        public LootRoller(GameDatabase db) { this.db = db; }

        public List<ItemAmount> Roll(string tableId, IRandom rng)
        {
            var result = new List<ItemAmount>();
            if (string.IsNullOrEmpty(tableId) || !db.LootTables.TryGetValue(tableId, out var table)) return result;
            foreach (var e in table.entries)
            {
                if (e.chance < 1 && rng.Value() > e.chance) continue;
                int n = rng.Range(e.min, e.max);
                if (n <= 0) continue;
                var existing = result.FirstOrDefault(x => x.itemId == e.itemId);
                if (existing != null) existing.count += n;
                else result.Add(new ItemAmount { itemId = e.itemId, count = n });
            }
            return result;
        }
    }
}
