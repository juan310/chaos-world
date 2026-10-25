using System;
using System.Collections.Generic;
using System.Linq;

namespace FH.Core
{
    /// <summary>Slot-limited container of stackable and unique items. All changes are all-or-nothing.</summary>
    public class Container
    {
        readonly GameDatabase db;
        readonly GameState state;
        public readonly List<ItemStack> Stacks;
        readonly Func<int> capacity;

        public Container(GameDatabase db, GameState state, List<ItemStack> stacks, Func<int> capacity)
        {
            this.db = db; this.state = state; Stacks = stacks; this.capacity = capacity;
        }

        public int Capacity => capacity();
        public int UsedSlots => Stacks.Count;
        public int FreeSlots => Math.Max(0, Capacity - UsedSlots);
        public int Count(string itemId) => Stacks.Where(s => s.itemId == itemId).Sum(s => s.count);
        public ItemStack ByUid(string uid) => string.IsNullOrEmpty(uid) ? null : Stacks.FirstOrDefault(s => s.uid == uid);

        /// <summary>Slots needed to add these amounts, accounting for room in existing stacks.</summary>
        public int SlotsNeeded(IEnumerable<ItemAmount> amounts)
        {
            int slots = 0;
            foreach (var g in amounts.GroupBy(a => a.itemId))
            {
                var def = db.Item(g.Key);
                if (def == null) continue;
                int n = g.Sum(a => a.count);
                if (def.IsUnique) { slots += n; continue; }
                int room = Stacks.Where(s => s.itemId == def.id).Sum(s => def.maxStack - s.count);
                int overflow = Math.Max(0, n - room);
                slots += (overflow + def.maxStack - 1) / def.maxStack;
            }
            return slots;
        }

        public Result CanAdd(IEnumerable<ItemAmount> amounts)
        {
            var list = amounts.ToList();
            foreach (var a in list)
            {
                if (db.Item(a.itemId) == null) return Result.Fail("Unknown item.");
                if (a.count <= 0) return Result.Fail("Nothing to add.");
            }
            int need = SlotsNeeded(list);
            if (need > FreeSlots) return Result.Fail("Not enough room: needs " + need + " free slot" + (need == 1 ? "" : "s") + ", " + FreeSlots + " available.");
            return Result.Success;
        }

        /// <summary>Adds the item(s). Returns the stack created for a unique item (first one) or the merged stack.</summary>
        public Result Add(string itemId, int count, out ItemStack stack)
        {
            stack = null;
            var check = CanAdd(new[] { new ItemAmount { itemId = itemId, count = count } });
            if (!check.Ok) return check;
            var def = db.Item(itemId);
            if (def.IsUnique)
            {
                for (int i = 0; i < count; i++)
                {
                    var s = new ItemStack { uid = state.NewUid(), itemId = itemId, count = 1 };
                    Stacks.Add(s);
                    if (stack == null) stack = s;
                }
                return Result.Success;
            }
            int left = count;
            foreach (var s in Stacks.Where(s => s.itemId == itemId && s.count < def.maxStack))
            {
                int take = Math.Min(left, def.maxStack - s.count);
                s.count += take; left -= take; stack = s;
                if (left == 0) break;
            }
            while (left > 0)
            {
                int take = Math.Min(left, def.maxStack);
                stack = new ItemStack { uid = "", itemId = itemId, count = take };
                Stacks.Add(stack);
                left -= take;
            }
            return Result.Success;
        }

        public Result Remove(string itemId, int count)
        {
            if (count <= 0) return Result.Fail("Nothing to remove.");
            int have = Count(itemId);
            if (have < count) return Result.Fail("Needs " + count + "× " + db.ItemName(itemId) + " (have " + have + ").");
            int left = count;
            for (int i = Stacks.Count - 1; i >= 0 && left > 0; i--)
            {
                var s = Stacks[i];
                if (s.itemId != itemId) continue;
                int take = Math.Min(left, s.count);
                s.count -= take; left -= take;
                if (s.count == 0) Stacks.RemoveAt(i);
            }
            return Result.Success;
        }

        public Result RemoveStack(ItemStack stack)
        {
            if (stack == null || !Stacks.Contains(stack)) return Result.Fail("Item not found.");
            Stacks.Remove(stack);
            return Result.Success;
        }
    }

    public class InventorySystem
    {
        readonly GameDatabase db;
        readonly GameState state;
        readonly GameEvents events;
        public readonly Container Bag;

        public InventorySystem(GameDatabase db, GameState state, GameEvents events, Func<int> capacity)
        {
            this.db = db; this.state = state; this.events = events;
            Bag = new Container(db, state, state.inventory, capacity);
        }

        public int Count(string itemId) => Bag.Count(itemId);
        public Result CanAdd(IEnumerable<ItemAmount> amounts) => Bag.CanAdd(amounts);

        public Result Add(string itemId, int count, ItemSource source) => Add(itemId, count, source, out _);

        public Result Add(string itemId, int count, ItemSource source, out ItemStack stack)
        {
            var r = Bag.Add(itemId, count, out stack);
            if (r.Ok) events.RaiseItemAdded(itemId, count, source);
            return r;
        }

        public Result AddAll(IList<ItemAmount> amounts, ItemSource source)
        {
            var r = Bag.CanAdd(amounts);
            if (!r.Ok) return r;
            foreach (var a in amounts) Add(a.itemId, a.count, source);
            return Result.Success;
        }

        /// <summary>Removes stackable quantities, never touching equipped unique items.</summary>
        public Result Remove(string itemId, int count)
        {
            var def = db.Item(itemId);
            if (def == null) return Result.Fail("Unknown item.");
            if (def.IsUnique)
            {
                var free = Bag.Stacks.Where(s => s.itemId == itemId && !IsEquipped(s.uid)).Take(count).ToList();
                if (free.Count < count) return Result.Fail("Needs " + count + "× " + def.name + " that is not equipped.");
                foreach (var s in free) Bag.RemoveStack(s);
                events.RaiseItemRemoved(itemId, count);
                return Result.Success;
            }
            var r = Bag.Remove(itemId, count);
            if (r.Ok) events.RaiseItemRemoved(itemId, count);
            return r;
        }

        public bool IsEquipped(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            var e = state.equipment;
            return e.weapon == uid || e.head == uid || e.body == uid || e.charm == uid;
        }

        public ItemStack Equipped(EquipSlot slot) => Bag.ByUid(SlotRef(slot));

        string SlotRef(EquipSlot slot)
        {
            switch (slot)
            {
                case EquipSlot.Weapon: return state.equipment.weapon;
                case EquipSlot.Head: return state.equipment.head;
                case EquipSlot.Body: return state.equipment.body;
                case EquipSlot.Charm: return state.equipment.charm;
                default: return "";
            }
        }

        void SetSlot(EquipSlot slot, string uid)
        {
            switch (slot)
            {
                case EquipSlot.Weapon: state.equipment.weapon = uid; break;
                case EquipSlot.Head: state.equipment.head = uid; break;
                case EquipSlot.Body: state.equipment.body = uid; break;
                case EquipSlot.Charm: state.equipment.charm = uid; break;
            }
        }

        public Result Equip(string uid)
        {
            var stack = Bag.ByUid(uid);
            if (stack == null) return Result.Fail("That item is not in your pack.");
            var def = db.Item(stack.itemId);
            if (def == null || def.slot == EquipSlot.None) return Result.Fail(def?.name + " cannot be equipped.");
            if (SlotRef(def.slot) == uid) return Result.Fail(def.name + " is already equipped.");
            SetSlot(def.slot, uid);
            events.RaiseEquipment();
            events.RaiseNotice("Equipped " + def.name + ".");
            return Result.Success;
        }

        public Result Unequip(EquipSlot slot)
        {
            if (slot == EquipSlot.Weapon) return Result.Fail("A hunter never goes unarmed. Equip another weapon instead.");
            if (string.IsNullOrEmpty(SlotRef(slot))) return Result.Fail("Nothing equipped there.");
            SetSlot(slot, "");
            events.RaiseEquipment();
            return Result.Success;
        }

        /// <summary>Replaces a unique stack in place (used by upgrades), keeping it equipped if it was.</summary>
        public ItemStack Replace(ItemStack old, string newItemId)
        {
            var def = db.Item(old.itemId);
            bool equipped = IsEquipped(old.uid);
            int index = Bag.Stacks.IndexOf(old);
            var fresh = new ItemStack { uid = state.NewUid(), itemId = newItemId, count = 1 };
            Bag.Stacks[index] = fresh;
            if (equipped) SetSlot(def.slot, fresh.uid);
            events.RaiseItemRemoved(old.itemId, 1);
            events.RaiseItemAdded(newItemId, 1, ItemSource.Craft);
            if (equipped) events.RaiseEquipment();
            return fresh;
        }

        public Result Use(string itemId, IConsumer consumer)
        {
            var def = db.Item(itemId);
            if (def == null || def.kind != ItemKind.Consumable) return Result.Fail("That cannot be used.");
            if (Count(itemId) <= 0) return Result.Fail("No " + def.name + " left.");
            var ok = consumer.CanConsume(def);
            if (!ok.Ok) return ok;
            Remove(itemId, 1);
            consumer.Consume(def);
            return Result.Success;
        }
    }

    public interface IConsumer { Result CanConsume(ItemDef item); void Consume(ItemDef item); }
}
