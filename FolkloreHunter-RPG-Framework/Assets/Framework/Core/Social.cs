using System;
using System.Collections.Generic;
using System.Linq;

namespace FH.Core
{
    public class RelationshipSystem
    {
        readonly GameDatabase db;
        readonly GameState state;
        readonly GameEvents events;
        readonly InventorySystem inventory;
        readonly Conditions conditions;

        public RelationshipSystem(GameDatabase db, GameState state, GameEvents events, InventorySystem inventory, Conditions conditions)
        {
            this.db = db; this.state = state; this.events = events; this.inventory = inventory; this.conditions = conditions;
        }

        public RelationshipState Get(string npcId) => state.Relationship(npcId);
        public RelationshipStageDef Stage(string npcId) => db.RelationshipStages.FirstOrDefault(s => s.id == Get(npcId).stage) ?? db.RelationshipStages.FirstOrDefault();
        public RelationshipStageDef NextStage(string npcId)
        {
            var cur = Stage(npcId);
            return db.RelationshipStages.FirstOrDefault(s => s.order == (cur?.order ?? 0) + 1);
        }

        public bool IsSpouse(string npcId) => Get(npcId).stage == "spouse";

        public void ChangeAffinity(string npcId, int amount)
        {
            if (amount == 0) return;
            var r = Get(npcId);
            r.affinity = Math.Max(-100, Math.Min(100, r.affinity + amount));
            events.RaiseRelationship(npcId);
            AutoAdvance(npcId);
        }

        void AutoAdvance(string npcId)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                var next = NextStage(npcId);
                if (next == null || !next.automatic || !CanAdvance(npcId, next.id).Ok) return;
                Advance(npcId, next.id);
            }
        }

        public Result CanAdvance(string npcId, string stageId)
        {
            if (!db.Npcs.TryGetValue(npcId, out var npc)) return Result.Fail("Unknown person.");
            var target = db.RelationshipStages.FirstOrDefault(s => s.id == stageId);
            if (target == null) return Result.Fail("Unknown relationship.");
            var next = NextStage(npcId);
            if (next == null || next.id != stageId) return Result.Fail("That would be too sudden.");
            bool romantic = target.order > (db.RelationshipStages.FirstOrDefault(s => s.id == "friend")?.order ?? int.MaxValue);
            if (romantic && !npc.romanceable) return Result.Fail(npc.name + " values your friendship.");
            if (romantic && state.relationships.Any(r => r.npcId != npcId && conditions.StageOrder(r.stage) > conditions.StageOrder("friend")))
                return Result.Fail("Your heart is already promised to someone.");
            var r2 = Get(npcId);
            if (r2.affinity < target.affinityRequired) return Result.Fail(npc.name + " needs to know you better (bond " + r2.affinity + "/" + target.affinityRequired + ").");
            return conditions.Explain(target.requirements);
        }

        public Result Advance(string npcId, string stageId)
        {
            var ok = CanAdvance(npcId, stageId);
            if (!ok.Ok) return ok;
            var r = Get(npcId);
            r.stage = stageId;
            r.stageSinceDay = state.world.Day;
            var stage = db.RelationshipStages.First(s => s.id == stageId);
            state.SetFlag("rel_" + npcId + "_" + stageId);
            events.RaiseRelationship(npcId);
            events.RaiseNotice(db.Npcs[npcId].name + ": " + stage.name, NoticeKind.Story);
            return Result.Success;
        }

        public Result CanGift(string npcId, string itemId)
        {
            if (!db.Npcs.ContainsKey(npcId)) return Result.Fail("Unknown person.");
            var item = db.Item(itemId);
            if (item == null || inventory.Count(itemId) <= 0) return Result.Fail("You do not have that.");
            if (item.kind == ItemKind.Key || item.slot != EquipSlot.None) return Result.Fail("That is not a suitable gift.");
            if (Get(npcId).lastGiftDay == state.world.Day) return Result.Fail("You already gave a gift today.");
            return Result.Success;
        }

        public Result Gift(string npcId, string itemId)
        {
            var r = CanGift(npcId, itemId);
            if (!r.Ok) return r;
            var npc = db.Npcs[npcId];
            inventory.Remove(itemId, 1);
            Get(npcId).lastGiftDay = state.world.Day;
            int amount = npc.gifts.FirstOrDefault(g => g.itemId == itemId)?.affinity ?? npc.defaultGiftAffinity;
            ChangeAffinity(npcId, amount);
            events.RaiseNotice(npc.name + (amount >= 8 ? " loves the gift!" : amount > 0 ? " appreciates the gift." : " does not care for that."), NoticeKind.Info);
            return Result.Success;
        }

        /// <summary>Daily conversation builds a little affinity once per in-game day.</summary>
        public void Talk(string npcId)
        {
            var r = Get(npcId);
            events.RaiseTalked(npcId);
            if (r.lastTalkDay == state.world.Day) return;
            r.lastTalkDay = state.world.Day;
            ChangeAffinity(npcId, 1);
        }
    }

    public enum UiRequest { None, Shop, Crafting, Storage, Gift, ContractBoard, Close }

    public class ChoiceView
    {
        public DialogueChoice Choice;
        public bool Available;
        public string Reason;
    }

    /// <summary>Executes data-driven effects from dialogue and scripted events.</summary>
    public class EffectRunner
    {
        readonly GameCore core;
        public EffectRunner(GameCore core) { this.core = core; }

        public Result Validate(IEnumerable<Effect> effects)
        {
            foreach (var e in effects)
            {
                switch (e.type)
                {
                    case EffectType.TakeItem:
                        if (core.Inventory.Count(e.id) < Math.Max(1, (int)e.value)) return Result.Fail("Requires " + Math.Max(1, (int)e.value) + "× " + core.Db.ItemName(e.id) + ".");
                        break;
                    case EffectType.Gold:
                        if (e.value < 0 && core.Wallet.Gold < -e.value) return Result.Fail("Requires " + (-e.value) + " gold.");
                        break;
                    case EffectType.GiveItem:
                        var fit = core.Inventory.CanAdd(new[] { new ItemAmount { itemId = e.id, count = Math.Max(1, (int)e.value) } });
                        if (!fit.Ok) return fit;
                        break;
                    case EffectType.AcceptContract: { var r = core.Contracts.CanAccept(e.id); if (!r.Ok) return r; break; }
                    case EffectType.ClaimContract: { var r = core.Contracts.CanClaim(e.id); if (!r.Ok) return r; break; }
                    case EffectType.SetRelationship: { var r = core.Relationships.CanAdvance(e.id, e.state); if (!r.Ok) return r; break; }
                }
            }
            return Result.Success;
        }

        public UiRequest Apply(IEnumerable<Effect> effects)
        {
            var ui = UiRequest.None;
            foreach (var e in effects)
            {
                switch (e.type)
                {
                    case EffectType.SetFlag: core.State.SetFlag(e.id); core.Events.RaiseFlag(e.id); break;
                    case EffectType.ClearFlag: core.State.world.flags.Remove(e.id); break;
                    case EffectType.Affinity: core.Relationships.ChangeAffinity(e.id, (int)e.value); break;
                    case EffectType.GiveItem: core.Inventory.Add(e.id, Math.Max(1, (int)e.value), ItemSource.Gift); break;
                    case EffectType.TakeItem: core.Inventory.Remove(e.id, Math.Max(1, (int)e.value)); break;
                    case EffectType.Gold: if (e.value > 0) core.Wallet.Earn((int)e.value); else core.Wallet.Spend((int)-e.value); break;
                    case EffectType.Reputation: core.State.AddFaction(e.id, (int)e.value); break;
                    case EffectType.AcceptContract: core.Contracts.Accept(e.id); break;
                    case EffectType.ClaimContract: core.Contracts.Claim(e.id); break;
                    case EffectType.StartQuest: core.Quests.Start(e.id); break;
                    case EffectType.SetRelationship: core.Relationships.Advance(e.id, e.state); break;
                    case EffectType.Xp: core.Progression.AddXp((int)e.value); break;
                    case EffectType.OpenShop: ui = UiRequest.Shop; break;
                    case EffectType.OpenCrafting: ui = UiRequest.Crafting; break;
                    case EffectType.OpenStorage: ui = UiRequest.Storage; break;
                    case EffectType.OpenGift: ui = UiRequest.Gift; break;
                    case EffectType.CloseDialogue: ui = UiRequest.Close; break;
                }
            }
            core.AfterStateChange();
            return ui;
        }
    }

    public class DialogueRunner
    {
        readonly GameCore core;
        readonly DialogueDef def;
        public readonly string NpcId;
        public DialogueNode Node { get; private set; }
        public bool Finished => Node == null;
        public UiRequest PendingUi;
        public string LastRefusal = "";

        public DialogueRunner(GameCore core, string dialogueId, string npcId)
        {
            this.core = core; NpcId = npcId;
            if (!core.Db.Dialogues.TryGetValue(dialogueId ?? "", out def)) throw new ArgumentException("Unknown dialogue " + dialogueId);
            core.Relationships.Talk(npcId);
            foreach (var entry in def.entries)
            {
                var n = def.nodes.First(x => x.id == entry);
                if (core.Conditions.All(n.conditions)) { Enter(n); break; }
            }
        }

        void Enter(DialogueNode n)
        {
            Node = n;
            if (n != null && n.onEnter.Count > 0)
            {
                var ui = core.Effects.Apply(n.onEnter);
                if (ui != UiRequest.None) PendingUi = ui;
            }
        }

        public string Text => Node == null ? "" : Node.text.Replace("{player}", core.State.player.name);

        public List<ChoiceView> Choices()
        {
            var list = new List<ChoiceView>();
            if (Node == null) return list;
            foreach (var c in Node.choices)
            {
                // Unmet conditions may hide a choice; effects that cannot run yet show it disabled with the reason.
                var gate = core.Conditions.Explain(c.conditions);
                if (!gate.Ok && c.hideWhenUnavailable) continue;
                if (gate.Ok) gate = core.Effects.Validate(c.effects);
                list.Add(new ChoiceView { Choice = c, Available = gate.Ok, Reason = gate.Reason });
            }
            return list;
        }

        public Result Choose(int index)
        {
            var options = Choices();
            if (index < 0 || index >= options.Count) return Result.Fail("No such choice.");
            var v = options[index];
            if (!v.Available) { LastRefusal = v.Reason; return Result.Fail(v.Reason); }
            var ui = core.Effects.Apply(v.Choice.effects);
            if (ui != UiRequest.None) PendingUi = ui;
            if (string.IsNullOrEmpty(v.Choice.next) || ui == UiRequest.Close) Node = null;
            else Enter(def.nodes.First(n => n.id == v.Choice.next));
            return Result.Success;
        }
    }
}
