using System;
using System.Collections.Generic;
using System.Linq;

namespace FH.Core
{
    public class CraftCheck
    {
        public bool CanCraft => Reasons.Count == 0;
        public readonly List<string> Reasons = new List<string>();
        public string Summary => CanCraft ? "Ready to craft." : string.Join("\n", Reasons);
    }

    public class CraftingSystem
    {
        readonly GameDatabase db;
        readonly GameState state;
        readonly GameEvents events;
        readonly InventorySystem inventory;
        readonly Wallet wallet;
        readonly Conditions conditions;

        public CraftingSystem(GameDatabase db, GameState state, GameEvents events, InventorySystem inventory, Wallet wallet, Conditions conditions)
        {
            this.db = db; this.state = state; this.events = events; this.inventory = inventory; this.wallet = wallet; this.conditions = conditions;
        }

        public bool Known(string id) => state.knownRecipes.Contains(id) || (db.Recipes.TryGetValue(id, out var r) && r.unlockedByDefault);
        public IEnumerable<RecipeDef> ForStation(Station s) => db.Recipes.Values.Where(r => r.station == s);

        ItemStack BaseItem(RecipeDef r)
        {
            if (string.IsNullOrEmpty(r.upgradeFrom)) return null;
            // Prefer the equipped copy so the upgrade stays in the player's hands.
            var all = state.inventory.Where(s => s.itemId == r.upgradeFrom).ToList();
            return all.FirstOrDefault(s => inventory.IsEquipped(s.uid)) ?? all.FirstOrDefault();
        }

        public CraftCheck Check(string recipeId, Station atStation)
        {
            var c = new CraftCheck();
            if (!db.Recipes.TryGetValue(recipeId, out var r)) { c.Reasons.Add("Unknown recipe."); return c; }
            if (!Known(recipeId)) c.Reasons.Add("Recipe not learned yet.");
            if (r.station != atStation) c.Reasons.Add("Must be crafted at a " + r.station + ".");
            var gate = conditions.Explain(r.requirements);
            if (!gate.Ok) c.Reasons.Add(gate.Reason);
            if (!string.IsNullOrEmpty(r.upgradeFrom) && BaseItem(r) == null) c.Reasons.Add("Requires " + db.ItemName(r.upgradeFrom) + " to upgrade.");
            foreach (var input in r.inputs)
            {
                int have = inventory.Count(input.itemId);
                if (have < input.count) c.Reasons.Add("Needs " + input.count + "× " + db.ItemName(input.itemId) + " (have " + have + ").");
            }
            if (r.goldCost > 0 && wallet.Gold < r.goldCost) c.Reasons.Add("Needs " + r.goldCost + " gold (have " + wallet.Gold + ").");
            if (string.IsNullOrEmpty(r.upgradeFrom))
            {
                // Consumed stacks may free room; only count slots that remain occupied.
                var fit = inventory.CanAdd(new[] { r.output });
                if (!fit.Ok && !InputsFreeSlot(r)) c.Reasons.Add("Pack is full.");
            }
            return c;
        }

        bool InputsFreeSlot(RecipeDef r) => r.inputs.Any(i => inventory.Count(i.itemId) == i.count && db.Item(i.itemId) != null);

        public Result Craft(string recipeId, Station atStation)
        {
            var check = Check(recipeId, atStation);
            if (!check.CanCraft) return Result.Fail(check.Reasons[0]);
            var r = db.Recipes[recipeId];
            foreach (var input in r.inputs) inventory.Remove(input.itemId, input.count);
            if (r.goldCost > 0) wallet.Spend(r.goldCost);
            string resultName = db.ItemName(r.output.itemId);
            if (!string.IsNullOrEmpty(r.upgradeFrom))
            {
                inventory.Replace(BaseItem(r), r.output.itemId);
            }
            else
            {
                var added = inventory.Add(r.output.itemId, r.output.count, ItemSource.Craft);
                if (!added.Ok) throw new InvalidOperationException("Craft output did not fit after validation");
            }
            events.RaiseCrafted(r.output.itemId);
            events.RaiseNotice("Crafted " + (r.output.count > 1 ? r.output.count + "× " : "") + resultName + ".", NoticeKind.Reward);
            return Result.Success;
        }
    }

    public class VendorSystem
    {
        readonly GameDatabase db;
        readonly GameState state;
        readonly GameEvents events;
        readonly InventorySystem inventory;
        readonly Wallet wallet;
        readonly Conditions conditions;

        public VendorSystem(GameDatabase db, GameState state, GameEvents events, InventorySystem inventory, Wallet wallet, Conditions conditions)
        {
            this.db = db; this.state = state; this.events = events; this.inventory = inventory; this.wallet = wallet; this.conditions = conditions;
        }

        public int Price(VendorStock s) => s.price > 0 ? s.price : db.Item(s.itemId)?.buyPrice ?? 0;

        public int SoldToday(string vendorId, string itemId)
            => state.vendorSales.Where(v => v.vendorId == vendorId && v.itemId == itemId && v.day == state.world.Day).Sum(v => v.count);

        public int Remaining(VendorDef v, VendorStock s) => s.stock < 0 ? int.MaxValue : Math.Max(0, s.stock - SoldToday(v.id, s.itemId));

        public Result CanBuy(string vendorId, string itemId, int qty)
        {
            if (qty <= 0) return Result.Fail("Choose a quantity.");
            if (!db.Vendors.TryGetValue(vendorId, out var v)) return Result.Fail("Unknown shop.");
            var s = v.stock.FirstOrDefault(x => x.itemId == itemId);
            if (s == null) return Result.Fail("Not sold here.");
            var gate = conditions.Explain(s.requirements);
            if (!gate.Ok) return gate;
            if (Remaining(v, s) < qty) return Result.Fail("Sold out until tomorrow.");
            int price = Price(s);
            if (price <= 0) return Result.Fail("Not for sale.");
            long total = (long)price * qty;
            if (total > int.MaxValue) return Result.Fail("Too many at once.");
            var money = wallet.CanSpend((int)total);
            if (!money.Ok) return money;
            return inventory.CanAdd(new[] { new ItemAmount { itemId = itemId, count = qty } });
        }

        public Result Buy(string vendorId, string itemId, int qty)
        {
            var r = CanBuy(vendorId, itemId, qty);
            if (!r.Ok) return r;
            var s = db.Vendors[vendorId].stock.First(x => x.itemId == itemId);
            wallet.Spend(Price(s) * qty);
            inventory.Add(itemId, qty, ItemSource.Purchase);
            state.vendorSales.Add(new VendorSale { vendorId = vendorId, itemId = itemId, count = qty, day = state.world.Day });
            events.RaiseNotice("Bought " + qty + "× " + db.ItemName(itemId) + ".");
            return Result.Success;
        }

        public int SellPrice(VendorDef v, ItemDef item) => (int)Math.Floor(item.sellPrice * v.sellRate);

        public Result CanSell(string vendorId, ItemStack stack, int qty)
        {
            if (!db.Vendors.TryGetValue(vendorId, out var v)) return Result.Fail("Unknown shop.");
            if (stack == null || !state.inventory.Contains(stack)) return Result.Fail("Item not found.");
            var item = db.Item(stack.itemId);
            if (item == null) return Result.Fail("Unknown item.");
            if (qty <= 0 || qty > stack.count) return Result.Fail("Invalid quantity.");
            if (item.kind == ItemKind.Key) return Result.Fail("You cannot part with that.");
            if (inventory.IsEquipped(stack.uid)) return Result.Fail("Unequip it before selling.");
            if (item.sellPrice <= 0) return Result.Fail("Nobody will buy that.");
            if (v.buysKinds.Count > 0 && !v.buysKinds.Contains(item.kind)) return Result.Fail(v.name + " does not buy " + item.kind.ToString().ToLowerInvariant() + "s.");
            return Result.Success;
        }

        public Result Sell(string vendorId, ItemStack stack, int qty)
        {
            var r = CanSell(vendorId, stack, qty);
            if (!r.Ok) return r;
            var v = db.Vendors[vendorId];
            var item = db.Item(stack.itemId);
            if (item.IsUnique) inventory.Bag.RemoveStack(stack);
            else inventory.Bag.Remove(item.id, qty);
            events.RaiseItemRemoved(item.id, qty);
            wallet.Earn(SellPrice(v, item) * qty);
            events.RaiseNotice("Sold " + qty + "× " + item.name + " for " + SellPrice(v, item) * qty + " gold.");
            return Result.Success;
        }
    }

    public class EstateSystem
    {
        readonly GameDatabase db;
        readonly GameState state;
        readonly GameEvents events;
        readonly InventorySystem inventory;
        readonly Wallet wallet;
        readonly Conditions conditions;
        public readonly Container Storage;

        public EstateSystem(GameDatabase db, GameState state, GameEvents events, InventorySystem inventory, Wallet wallet, Conditions conditions)
        {
            this.db = db; this.state = state; this.events = events; this.inventory = inventory; this.wallet = wallet; this.conditions = conditions;
            Storage = new Container(db, state, state.estate.storage, () => Current?.storageSlots ?? 0);
        }

        public EstateTierDef Current => db.Tier(state.estate.tier);
        public EstateTierDef Next => db.Tier(state.estate.tier + 1);
        public bool HasStation(Station s) => db.EstateTiers.Where(t => t.tier <= state.estate.tier).Any(t => t.stations.Contains(s));

        public Result CanUpgrade()
        {
            var next = Next;
            if (next == null) return Result.Fail("Your estate is at its finest.");
            if (!next.modeled) return Result.Fail(next.name + " has not been drawn up by the builders yet.");
            var gate = conditions.Explain(next.requirements);
            if (!gate.Ok) return gate;
            foreach (var m in next.materials)
            {
                int have = inventory.Count(m.itemId);
                if (have < m.count) return Result.Fail("Needs " + m.count + "× " + db.ItemName(m.itemId) + " (have " + have + ").");
            }
            return wallet.CanSpend(next.goldCost);
        }

        public Result Upgrade()
        {
            var r = CanUpgrade();
            if (!r.Ok) return r;
            var next = Next;
            foreach (var m in next.materials) inventory.Remove(m.itemId, m.count);
            wallet.Spend(next.goldCost);
            state.estate.tier = next.tier;
            state.estate.lastIncomeDay = state.world.Day;   // income accrues from the day of the upgrade only
            events.RaiseEstate(next.tier);
            events.RaiseStats();
            events.RaiseNotice("Estate improved: " + next.name + ". " + next.description, NoticeKind.Reward);
            return Result.Success;
        }

        public int PendingIncome()
        {
            var t = Current;
            if (t == null || t.incomePerDay <= 0) return 0;
            int days = Math.Min(t.maxIncomeDays, state.world.Day - state.estate.lastIncomeDay);
            return Math.Max(0, days) * t.incomePerDay;
        }

        /// <summary>Income is measured in in-game days, which only pass while playing, so closing the game earns nothing.</summary>
        public Result CollectIncome()
        {
            int amount = PendingIncome();
            if (amount <= 0) return Result.Fail(Current != null && Current.incomePerDay > 0 ? "No rent is due yet. Come back tomorrow." : "Your estate earns no income yet.");
            state.estate.lastIncomeDay = state.world.Day;
            state.estate.totalIncome += amount;
            wallet.Earn(amount);
            events.RaiseNotice("Collected " + amount + " gold of estate income.", NoticeKind.Reward);
            return Result.Success;
        }

        public Result Deposit(ItemStack stack, int qty)
        {
            if (stack == null || !state.inventory.Contains(stack)) return Result.Fail("Item not found.");
            if (Storage.Capacity <= 0) return Result.Fail("Your estate has no storage yet.");
            if (inventory.IsEquipped(stack.uid)) return Result.Fail("Unequip it first.");
            var item = db.Item(stack.itemId);
            if (item.kind == ItemKind.Key) return Result.Fail("Keep that with you.");
            qty = Math.Min(qty, stack.count);
            if (item.IsUnique)
            {
                if (Storage.FreeSlots < 1) return Result.Fail("Storage is full.");
                inventory.Bag.RemoveStack(stack);
                Storage.Stacks.Add(stack);
                return Result.Success;
            }
            var fit = Storage.CanAdd(new[] { new ItemAmount { itemId = item.id, count = qty } });
            if (!fit.Ok) return Result.Fail("Storage is full.");
            inventory.Bag.Remove(item.id, qty);
            Storage.Add(item.id, qty, out _);
            return Result.Success;
        }

        public Result Withdraw(ItemStack stack, int qty)
        {
            if (stack == null || !Storage.Stacks.Contains(stack)) return Result.Fail("Item not found.");
            var item = db.Item(stack.itemId);
            qty = Math.Min(qty, stack.count);
            if (item.IsUnique)
            {
                if (inventory.Bag.FreeSlots < 1) return Result.Fail("Your pack is full.");
                Storage.RemoveStack(stack);
                state.inventory.Add(stack);
                events.RaiseItemAdded(item.id, 1, ItemSource.Storage);
                return Result.Success;
            }
            var fit = inventory.CanAdd(new[] { new ItemAmount { itemId = item.id, count = qty } });
            if (!fit.Ok) return fit;
            Storage.Remove(item.id, qty);
            inventory.Add(item.id, qty, ItemSource.Storage);
            return Result.Success;
        }
    }
}
