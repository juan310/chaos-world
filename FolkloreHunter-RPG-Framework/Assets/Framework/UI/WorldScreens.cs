using System.Collections.Generic;
using System.Linq;
using FH.Core;
using FH.Game;
using UnityEngine;
using UnityEngine.UI;

namespace FH.UI
{
    public class DialogueScreen : UiScreen
    {
        readonly NpcActor npc;
        DialogueRunner runner;
        public DialogueRunner Runner => runner;
        public DialogueScreen(NpcActor n) { npc = n; }
        public override string Title => C.Db.Npcs[npc.NpcId].name;
        public override Vector2 Size => new Vector2(1320, 520);
        public override bool Dim => false;

        public override void Build()
        {
            Root.GetComponentInChildren<Image>().rectTransform.Box(new Vector2(.5f, 0), new Vector2(0, 40), Size);
            if (runner == null)
            {
                var def = C.Db.Npcs[npc.NpcId];
                runner = new DialogueRunner(C, def.dialogueId, npc.NpcId);
                C.AfterStateChange();
            }
            if (runner.Finished) { Ui.Close(this); return; }
            var col = UiKit.Column("Dialogue", Body, 10);
            col.Stretch();
            var n = C.Db.Npcs[npc.NpcId];
            var rel = C.Relationships.Get(npc.NpcId);
            UiKit.Paragraph(col, "<color=#a9a59a>" + n.title + "  ·  " + C.Relationships.Stage(npc.NpcId).name + "  ·  bond " + rel.affinity + "</color>", 17);
            UiKit.Paragraph(col, runner.Text, 24);
            UiKit.Paragraph(col, " ", 6);
            var choices = runner.Choices();
            for (int i = 0; i < choices.Count; i++)
            {
                int idx = i;
                var v = choices[i];
                var b = UiKit.Button(col, (i + 1) + ".  " + v.Choice.text, () => Choose(idx), 44, 21);
                b.Text.alignment = TextAnchor.MiddleLeft; b.Text.rectTransform.Stretch(16);
                b.Enabled(v.Available, v.Reason);
            }
        }

        public void Choose(int index)
        {
            var r = runner.Choose(index);
            if (!r.Ok) { S.Hud.Flash(r.Reason); return; }
            var ui = runner.PendingUi; runner.PendingUi = UiRequest.None;
            var def = C.Db.Npcs[npc.NpcId];
            if (runner.Finished) Ui.Close(this); else Refresh();
            switch (ui)
            {
                case UiRequest.Shop: if (!string.IsNullOrEmpty(def.vendorId)) Ui.Push(new ShopScreen(def.vendorId)); break;
                case UiRequest.Crafting: Ui.Push(new CraftScreen(def.role == "smith" ? Station.Forge : Station.Alchemy, def.role == "smith" ? "Bram's forge" : "Mira's mortar and pestle")); break;
                case UiRequest.Gift: Ui.Push(new GiftScreen(npc.NpcId)); break;
                case UiRequest.Storage: Ui.Push(new StorageScreen()); break;
            }
        }

        public override void Tick()
        {
            // Number keys pick a choice quickly.
            for (int i = 0; i < 9; i++) if (Input.GetKeyDown(KeyCode.Alpha1 + i) && Ui.Top == this) { if (i < runner.Choices().Count) Choose(i); break; }
        }
    }

    public class ShopScreen : UiScreen
    {
        readonly string vendorId;
        bool selling;
        string selected;
        int qty = 1;
        RectTransform list, detail;
        public ShopScreen(string v) { vendorId = v; }
        VendorDef V => C.Db.Vendors[vendorId];
        public override string Title => V.name + "   <size=22><color=#e9e4d4>" + C.Wallet.Gold + " gold</color></size>";
        public override Vector2 Size => new Vector2(1500, 840);

        public override void Build()
        {
            Root.GetComponentsInChildren<Text>().First(t => t.name == "Title").text = Title;
            var tabs = UiKit.Row("Tabs", Body, 8, 48);
            tabs.Place(new Vector2(0, 1), new Vector2(.5f, 1), new Vector2(0, -48), Vector2.zero);
            UiKit.Button(tabs, selling ? "Buy" : "<b>Buy</b>", () => { selling = false; selected = null; qty = 1; Refresh(); });
            UiKit.Button(tabs, selling ? "<b>Sell</b>" : "Sell", () => { selling = true; selected = null; qty = 1; Refresh(); });
            var content = UiKit.Rect("Content", Body).Place(Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -60));
            Layout.Split(content, .48f, out list, out detail);
            if (!selling)
            {
                foreach (var st in V.stock)
                {
                    var def = C.Db.Item(st.itemId);
                    int left = C.Vendors.Remaining(V, st);
                    var gate = C.Conditions.Explain(st.requirements);
                    var ss = st;
                    string label = "<color=" + UiKit.RarityColor(def.rarity) + ">" + def.name + "</color>   <color=#f5d68c>" + C.Vendors.Price(st) + "g</color>" + (left != int.MaxValue ? "   <color=#a9a59a>" + left + " left today</color>" : "") + (gate.Ok ? "" : "   <color=#ffb36b>locked</color>");
                    Layout.Row(list, label, () => { selected = ss.itemId; qty = 1; Refresh(); }, null, selected == st.itemId);
                }
                var sel = V.stock.FirstOrDefault(s => s.itemId == selected) ?? V.stock.FirstOrDefault();
                if (sel != null) BuyDetail(sel);
            }
            else
            {
                foreach (var st in C.State.inventory.ToList())
                {
                    var def = C.Db.Item(st.itemId);
                    string key = string.IsNullOrEmpty(st.uid) ? st.itemId : st.uid;
                    var can = C.Vendors.CanSell(vendorId, st, 1);
                    var ss = st;
                    Layout.Row(list, def.name + (def.IsUnique ? "" : " ×" + st.count) + "   " + (can.Ok ? "<color=#f5d68c>" + C.Vendors.SellPrice(V, def) + "g each</color>" : "<color=#a9a59a>" + can.Reason + "</color>"), () => { selected = key; qty = 1; Refresh(); }, null, selected == key);
                }
                var cur = C.State.inventory.FirstOrDefault(s => (string.IsNullOrEmpty(s.uid) ? s.itemId : s.uid) == selected);
                if (cur != null) SellDetail(cur);
                else UiKit.Paragraph(detail, "Choose something to sell. " + V.name + " buys " + string.Join(", ", V.buysKinds.Select(k => k.ToString().ToLowerInvariant() + "s")) + ".", 20, UiKit.Muted);
            }
        }

        void Quantity(int max)
        {
            var row = UiKit.Row("Quantity", detail, 8, 46);
            UiKit.Button(row, "-", () => { qty = Mathf.Max(1, qty - 1); Refresh(); });
            UiKit.Label("Qty", row, "×" + qty, 22, UiKit.Gold, TextAnchor.MiddleCenter);
            UiKit.Button(row, "+", () => { qty = Mathf.Min(Mathf.Max(1, max), qty + 1); Refresh(); });
        }

        void BuyDetail(VendorStock st)
        {
            var def = C.Db.Item(st.itemId);
            UiKit.Paragraph(detail, JournalScreen.ItemText(C, def), 20);
            UiKit.Paragraph(detail, "You have " + C.Inventory.Count(def.id) + ".", 18, UiKit.Muted);
            if (!def.IsUnique) Quantity(10);
            var can = C.Vendors.CanBuy(vendorId, st.itemId, qty);
            UiKit.Button(detail, "Buy " + qty + " for " + C.Vendors.Price(st) * qty + " gold", () => { var r = C.Vendors.Buy(vendorId, st.itemId, qty); if (!r.Ok) S.Hud.Flash(r.Reason); Refresh(); }).Enabled(can.Ok, can.Reason);
        }

        void SellDetail(ItemStack st)
        {
            var def = C.Db.Item(st.itemId);
            UiKit.Paragraph(detail, JournalScreen.ItemText(C, def), 20);
            if (!def.IsUnique) Quantity(st.count);
            qty = Mathf.Min(qty, st.count);
            var can = C.Vendors.CanSell(vendorId, st, qty);
            UiKit.Button(detail, "Sell " + qty + " for " + C.Vendors.SellPrice(V, def) * qty + " gold", () => { var r = C.Vendors.Sell(vendorId, st, qty); if (!r.Ok) S.Hud.Flash(r.Reason); selected = null; Refresh(); }).Enabled(can.Ok, can.Reason);
        }
    }

    public class GiftScreen : UiScreen
    {
        readonly string npcId;
        public GiftScreen(string n) { npcId = n; }
        public override string Title => "Give a gift to " + C.Db.Npcs[npcId].name;
        public override Vector2 Size => new Vector2(1000, 760);

        public override void Build()
        {
            var col = UiKit.Column("Gifts", Body, 8);
            col.Stretch();
            var rel = C.Relationships.Get(npcId);
            UiKit.Paragraph(col, C.Relationships.Stage(npcId).name + "  ·  bond " + rel.affinity + (rel.lastGiftDay == C.State.world.Day ? "   <color=#ffb36b>One gift a day.</color>" : ""), 20, UiKit.Gold);
            var giftable = C.State.inventory.Where(s => { var d = C.Db.Item(s.itemId); return d.kind != ItemKind.Key && d.slot == EquipSlot.None; }).GroupBy(s => s.itemId).ToList();
            if (giftable.Count == 0) UiKit.Paragraph(col, "You have nothing suitable to give.", 20, UiKit.Muted);
            foreach (var g in giftable)
            {
                var id = g.Key;
                var can = C.Relationships.CanGift(npcId, id);
                var b = UiKit.Button(col, C.Db.ItemName(id) + "  ×" + g.Sum(x => x.count), () => { var r = C.Relationships.Gift(npcId, id); if (!r.Ok) S.Hud.Flash(r.Reason); Ui.Close(this); }, 46, 20);
                b.Enabled(can.Ok, can.Reason);
            }
            UiKit.Button(col, "Back", () => Ui.Close(this));
        }
    }

    public class CraftScreen : UiScreen
    {
        readonly Station station;
        readonly string label;
        string selected;
        RectTransform list, detail;
        public CraftScreen(Station s, string l) { station = s; label = l; }
        public override string Title => char.ToUpper(label[0]) + label.Substring(1);
        public override Vector2 Size => new Vector2(1500, 840);

        public override void Build()
        {
            Layout.Split(Body, .45f, out list, out detail);
            var recipes = C.Crafting.ForStation(station).ToList();
            if (recipes.Count == 0) UiKit.Paragraph(list, "Nothing can be made here yet.", 20, UiKit.Muted);
            foreach (var r in recipes)
            {
                var chk = C.Crafting.Check(r.id, station);
                bool known = C.Crafting.Known(r.id);
                var rr = r;
                string name = known ? C.Db.ItemName(r.output.itemId) : "Unknown recipe";
                Layout.Row(list, (chk.CanCraft ? "<color=#9be38a>●</color> " : "<color=#6f6c64>●</color> ") + name + (r.output.count > 1 ? " ×" + r.output.count : ""), () => { selected = rr.id; Refresh(); }, null, selected == r.id);
            }
            var sel = recipes.FirstOrDefault(r => r.id == selected) ?? recipes.FirstOrDefault();
            if (sel == null) return;
            var check = C.Crafting.Check(sel.id, station);
            if (!C.Crafting.Known(sel.id))
            {
                Layout.Heading(detail, "Unknown recipe");
                UiKit.Paragraph(detail, "You have not learned how to make this yet. Hunt contracts and progress teach new recipes.", 20, UiKit.Muted);
                UiKit.Button(detail, "Craft", null).Enabled(false, "Recipe not learned yet.");
                return;
            }
            var outDef = C.Db.Item(sel.output.itemId);
            UiKit.Paragraph(detail, JournalScreen.ItemText(C, outDef), 20);
            UiKit.Paragraph(detail, sel.description, 18, UiKit.Muted);
            var need = new List<string>();
            if (!string.IsNullOrEmpty(sel.upgradeFrom)) need.Add((C.Inventory.Count(sel.upgradeFrom) > 0 ? "<color=#9be38a>✓</color> " : "<color=#ff8a6a>✗</color> ") + "Base: " + C.Db.ItemName(sel.upgradeFrom) + " (upgraded in place, stays equipped)");
            foreach (var i in sel.inputs) { int have = C.Inventory.Count(i.itemId); need.Add((have >= i.count ? "<color=#9be38a>" : "<color=#ff8a6a>") + have + "/" + i.count + "</color> " + C.Db.ItemName(i.itemId)); }
            if (sel.goldCost > 0) need.Add((C.Wallet.Gold >= sel.goldCost ? "<color=#9be38a>" : "<color=#ff8a6a>") + sel.goldCost + " gold</color>");
            UiKit.Paragraph(detail, "<color=#f5d68c>Requires</color>\n" + string.Join("\n", need), 20);
            var b = UiKit.Button(detail, "Craft", () => { var r = C.Crafting.Craft(sel.id, station); if (!r.Ok) S.Hud.Flash(r.Reason); Refresh(); });
            b.Enabled(check.CanCraft, check.Summary);
        }
    }

    public class StorageScreen : UiScreen
    {
        public override string Title => "Estate storage";
        public override Vector2 Size => new Vector2(1500, 840);

        public override void Build()
        {
            var left = UiKit.Scroll("Pack", Body, out var pack); left.Place(Vector2.zero, new Vector2(.5f, 1), Vector2.zero, new Vector2(-10, 0));
            var right = UiKit.Scroll("Storage", Body, out var store); right.Place(new Vector2(.5f, 0), Vector2.one, new Vector2(10, 0), Vector2.zero);
            Layout.Heading(pack, "Pack  " + C.Inventory.Bag.UsedSlots + "/" + C.Inventory.Bag.Capacity + "   <size=17><color=#a9a59a>select to store</color></size>");
            foreach (var st in C.State.inventory.ToList())
            {
                var s2 = st;
                var def = C.Db.Item(st.itemId);
                var b = Layout.Row(pack, def.name + (def.IsUnique ? "" : " ×" + st.count), () => { var r = C.Estate.Deposit(s2, s2.count); if (!r.Ok) S.Hud.Flash(r.Reason); Refresh(); }, null);
                if (C.Inventory.IsEquipped(st.uid)) b.Enabled(false, "Equipped.");
            }
            Layout.Heading(store, "Storage  " + C.Estate.Storage.UsedSlots + "/" + C.Estate.Storage.Capacity + "   <size=17><color=#a9a59a>select to take</color></size>");
            foreach (var st in C.Estate.Storage.Stacks.ToList())
            {
                var s2 = st;
                Layout.Row(store, C.Db.ItemName(st.itemId) + (C.Db.Item(st.itemId).IsUnique ? "" : " ×" + st.count), () => { var r = C.Estate.Withdraw(s2, s2.count); if (!r.Ok) S.Hud.Flash(r.Reason); Refresh(); }, null);
            }
        }
    }

    public class LootScreen : UiScreen
    {
        readonly LootBag bag;
        public LootScreen(LootBag b) { bag = b; }
        public override string Title => bag.State.label;
        public override Vector2 Size => new Vector2(860, 620);

        public override void Build()
        {
            var col = UiKit.Column("Loot", Body, 8);
            col.Stretch();
            UiKit.Paragraph(col, "Pack " + C.Inventory.Bag.UsedSlots + "/" + C.Inventory.Bag.Capacity, 18, UiKit.Muted);
            foreach (var it in bag.State.items.ToList())
            {
                var i2 = it;
                var def = C.Db.Item(it.itemId);
                var can = C.Inventory.CanAdd(new[] { it });
                UiKit.Button(col, "<color=" + UiKit.RarityColor(def.rarity) + ">" + def.name + "</color>  ×" + it.count, () => Take(i2), 46, 21).Enabled(can.Ok, can.Reason);
            }
            var row = UiKit.Row("Buttons", col, 10, 52);
            UiKit.Button(row, "Take all", TakeAll);
            UiKit.Button(row, "Leave", () => Ui.Close(this));
        }

        void Take(ItemAmount it)
        {
            var r = C.Inventory.Add(it.itemId, it.count, ItemSource.Loot);
            if (!r.Ok) { S.Hud.Flash(r.Reason); return; }
            bag.State.items.Remove(it);
            S.Hud.Notify("+" + it.count + " " + C.Db.ItemName(it.itemId), NoticeKind.Reward);
            After();
        }

        public void TakeAll()
        {
            foreach (var it in bag.State.items.ToList())
            {
                if (!C.Inventory.Add(it.itemId, it.count, ItemSource.Loot).Ok) continue;
                bag.State.items.Remove(it);
                S.Hud.Notify("+" + it.count + " " + C.Db.ItemName(it.itemId), NoticeKind.Reward);
            }
            if (bag.State.items.Count > 0) S.Hud.Flash("Your pack is full. Some loot was left behind.");
            After();
        }

        void After()
        {
            if (bag.State.items.Count == 0) { S.RemoveBag(bag); Ui.Close(this); }
            else Refresh();
        }
    }

    public class ContractBoardScreen : UiScreen
    {
        string selected;
        RectTransform list, detail;
        public override string Title => "Thornbury hunt board";
        public override Vector2 Size => new Vector2(1500, 840);

        public override void Build()
        {
            Layout.Split(Body, .45f, out list, out detail);
            Layout.Heading(list, "Rank: " + C.Progression.Rank.name + "   ·   tracking " + C.Contracts.Active.Count() + "/" + ContractSystem.MaxActive);
            foreach (var def in C.Db.Contracts.Values)
            {
                var cs = C.Contracts.Get(def.id);
                var dd = def;
                string tag = cs.status == ContractStatus.Locked ? "<color=#ffb36b>" + (def.rankRequired > 0 ? C.Db.Ranks.First(r => r.rank == def.rankRequired).name : "locked") + "</color>" : "<color=#a9a59a>" + JournalScreen.StatusLabel(cs.status) + "</color>";
                Layout.Row(list, def.name + "   " + tag, () => { selected = dd.id; Refresh(); }, null, selected == def.id);
            }
            var sel = C.Db.Contracts.Values.FirstOrDefault(d => d.id == selected) ?? C.Db.Contracts.Values.First();
            JournalScreen.ContractDetail(C, detail, sel);
            var st = C.Contracts.Get(sel.id).status;
            if (st == ContractStatus.Active) UiKit.Button(detail, "Abandon", () => { C.Contracts.Abandon(sel.id); Refresh(); });
            else if (st == ContractStatus.Completed && string.IsNullOrEmpty(sel.giverNpcId))
            {
                var can = C.Contracts.CanClaim(sel.id);
                UiKit.Button(detail, "Claim reward", () => { var r = C.Contracts.Claim(sel.id); if (!r.Ok) S.Hud.Flash(r.Reason); Refresh(); }).Enabled(can.Ok, can.Reason);
            }
            else if (st != ContractStatus.Completed && st != ContractStatus.Claimed || C.Db.Contracts[sel.id].repeatable)
            {
                var can = C.Contracts.CanAccept(sel.id);
                UiKit.Button(detail, "Accept contract", () => { var r = C.Contracts.Accept(sel.id); if (!r.Ok) S.Hud.Flash(r.Reason); Refresh(); }).Enabled(can.Ok, can.Reason);
            }
        }
    }
}
