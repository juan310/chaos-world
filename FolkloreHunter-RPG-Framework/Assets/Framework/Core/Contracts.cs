using System;
using System.Collections.Generic;
using System.Linq;

namespace FH.Core
{
    /// <summary>A gameplay event that may advance objectives. Contracts and quests consume the same stream.</summary>
    public struct ObjectiveEvent
    {
        public ObjectiveType type;
        public string targetId;
        public int amount;
        public ObjectiveEvent(ObjectiveType t, string id, int n = 1) { type = t; targetId = id; amount = n; }
    }

    static class ObjectiveMath
    {
        public static bool Apply(List<ObjectiveDef> defs, List<int> progress, ObjectiveEvent e)
        {
            while (progress.Count < defs.Count) progress.Add(0);
            bool changed = false;
            for (int i = 0; i < defs.Count; i++)
            {
                var o = defs[i];
                if (o.type != e.type || o.targetId != e.targetId || progress[i] >= o.count) continue;
                progress[i] = Math.Min(o.count, progress[i] + Math.Max(1, e.amount));
                changed = true;
            }
            return changed;
        }

        public static bool Done(List<ObjectiveDef> defs, List<int> progress)
        {
            for (int i = 0; i < defs.Count; i++) if (i >= progress.Count || progress[i] < defs[i].count) return false;
            return true;
        }
    }

    public class ContractSystem
    {
        public const int MaxActive = 3;
        readonly GameDatabase db;
        readonly GameState state;
        readonly GameEvents events;
        readonly Conditions conditions;
        readonly RewardService rewards;

        public ContractSystem(GameDatabase db, GameState state, GameEvents events, Conditions conditions, RewardService rewards)
        {
            this.db = db; this.state = state; this.events = events; this.conditions = conditions; this.rewards = rewards;
        }

        public ContractState Get(string id) => state.Contract(id);
        public IEnumerable<ContractDef> All => db.Contracts.Values;
        public IEnumerable<ContractDef> Active => db.Contracts.Values.Where(c => Get(c.id).status == ContractStatus.Active);

        Result Gate(ContractDef def)
        {
            if (db.RankFor(state.player.hunterReputation).rank < def.rankRequired)
            {
                var rank = db.Ranks.FirstOrDefault(r => r.rank >= def.rankRequired);
                return Result.Fail("Requires Hunter Rank " + def.rankRequired + (rank != null ? " (" + rank.name + ")" : "") + ".");
            }
            return conditions.Explain(def.prerequisites);
        }

        /// <summary>Unlocks contracts whose gates are met and reopens repeatable ones after their cooldown.</summary>
        public void Refresh()
        {
            int day = state.world.Day;
            foreach (var def in db.Contracts.Values)
            {
                var cs = Get(def.id);
                if (cs.status == ContractStatus.Locked && Gate(def).Ok) { cs.status = ContractStatus.Available; events.RaiseContract(def.id); }
                else if (cs.status == ContractStatus.Claimed && def.repeatable && day >= cs.lastClaimDay + def.cooldownDays && Gate(def).Ok)
                {
                    cs.status = ContractStatus.Available;
                    cs.progress.Clear();
                    events.RaiseContract(def.id);
                }
            }
        }

        public Result CanAccept(string id)
        {
            if (!db.Contracts.TryGetValue(id, out var def)) return Result.Fail("Unknown contract.");
            var cs = Get(id);
            switch (cs.status)
            {
                case ContractStatus.Active: return Result.Fail("Already accepted.");
                case ContractStatus.Completed: return Result.Fail("Already complete. Claim your reward.");
                case ContractStatus.Claimed:
                    if (!def.repeatable) return Result.Fail("This contract is finished.");
                    int wait = cs.lastClaimDay + def.cooldownDays - state.world.Day;
                    if (wait > 0) return Result.Fail("New sightings in " + wait + " day" + (wait == 1 ? "" : "s") + ".");
                    break;
            }
            var gate = Gate(def);
            if (!gate.Ok) return gate;
            if (Active.Count() >= MaxActive) return Result.Fail("You can track at most " + MaxActive + " contracts. Finish or abandon one.");
            return Result.Success;
        }

        public Result Accept(string id)
        {
            var r = CanAccept(id);
            if (!r.Ok) return r;
            var def = db.Contracts[id];
            var cs = Get(id);
            cs.status = ContractStatus.Active;
            cs.progress = def.objectives.Select(_ => 0).ToList();
            events.RaiseContract(id);
            events.RaiseNotice("Contract accepted: " + def.name, NoticeKind.Story);
            return Result.Success;
        }

        public Result Abandon(string id)
        {
            var cs = Get(id);
            if (cs.status != ContractStatus.Active) return Result.Fail("That contract is not active.");
            cs.status = ContractStatus.Available;
            cs.progress.Clear();
            events.RaiseContract(id);
            return Result.Success;
        }

        public void Handle(ObjectiveEvent e)
        {
            foreach (var def in db.Contracts.Values)
            {
                var cs = Get(def.id);
                if (cs.status != ContractStatus.Active) continue;
                if (!ObjectiveMath.Apply(def.objectives, cs.progress, e)) continue;
                if (ObjectiveMath.Done(def.objectives, cs.progress))
                {
                    cs.status = ContractStatus.Completed;
                    var giver = db.Npcs.TryGetValue(def.giverNpcId ?? "", out var n) ? n.name : "the contract board";
                    events.RaiseNotice("Contract complete: " + def.name + ". Return to " + giver + ".", NoticeKind.Progress);
                }
                else
                {
                    for (int i = 0; i < def.objectives.Count; i++)
                        if (def.objectives[i].type == e.type && def.objectives[i].targetId == e.targetId)
                            events.RaiseNotice(def.objectives[i].description + " " + cs.progress[i] + "/" + def.objectives[i].count, NoticeKind.Progress);
                }
                events.RaiseContract(def.id);
            }
        }

        public string RewardKey(string id) => "contract:" + id + ":" + Get(id).timesClaimed;

        public Result CanClaim(string id)
        {
            if (!db.Contracts.TryGetValue(id, out var def)) return Result.Fail("Unknown contract.");
            var cs = Get(id);
            if (cs.status == ContractStatus.Claimed) return Result.Fail("This reward has already been claimed.");
            if (cs.status != ContractStatus.Completed) return Result.Fail("Complete the contract first.");
            return rewards.CanGrant(def.reward, RewardKey(id));
        }

        public Result Claim(string id)
        {
            var r = CanClaim(id);
            if (!r.Ok) return r;
            var def = db.Contracts[id];
            var cs = Get(id);
            var granted = rewards.Grant(def.reward, RewardKey(id));
            if (!granted.Ok) return granted;
            cs.status = ContractStatus.Claimed;
            cs.timesClaimed++;
            cs.lastClaimDay = state.world.Day;
            events.RaiseContract(id);
            events.RaiseNotice("Reward claimed: " + RewardService.Describe(db, def.reward), NoticeKind.Reward);
            Refresh();
            return Result.Success;
        }

        public string ProgressText(ContractDef def)
        {
            var cs = Get(def.id);
            var lines = new List<string>();
            for (int i = 0; i < def.objectives.Count; i++)
            {
                int p = i < cs.progress.Count ? cs.progress[i] : 0;
                lines.Add(def.objectives[i].description + "  " + p + "/" + def.objectives[i].count);
            }
            return string.Join("\n", lines);
        }
    }

    public class QuestSystem
    {
        readonly GameDatabase db;
        readonly GameState state;
        readonly GameEvents events;
        readonly Conditions conditions;
        readonly RewardService rewards;

        public QuestSystem(GameDatabase db, GameState state, GameEvents events, Conditions conditions, RewardService rewards)
        {
            this.db = db; this.state = state; this.events = events; this.conditions = conditions; this.rewards = rewards;
        }

        public void Refresh()
        {
            foreach (var q in db.Quests.Values)
            {
                var qs = state.Quest(q.id);
                if (qs.status == QuestStatus.NotStarted && q.autoStart && conditions.All(q.prerequisites)) Start(q.id);
            }
            // State-based objectives (estate tier) are evaluated continuously.
            Handle(new ObjectiveEvent(ObjectiveType.EstateTier, "estate", 0));
        }

        public Result Start(string id)
        {
            if (!db.Quests.TryGetValue(id, out var q)) return Result.Fail("Unknown quest.");
            var qs = state.Quest(id);
            if (qs.status != QuestStatus.NotStarted) return Result.Fail("Already started.");
            var gate = conditions.Explain(q.prerequisites);
            if (!gate.Ok) return gate;
            qs.status = QuestStatus.Active; qs.stage = 0; qs.progress = new List<int>();
            events.RaiseQuest(id);
            events.RaiseNotice("New story: " + q.name, NoticeKind.Story);
            return Result.Success;
        }

        public void Handle(ObjectiveEvent e)
        {
            foreach (var q in db.Quests.Values)
            {
                var qs = state.Quest(q.id);
                int guard = 0;
                while (qs.status == QuestStatus.Active && qs.stage < q.stages.Count && guard++ < 16)
                {
                    var stage = q.stages[qs.stage];
                    bool changed;
                    if (e.type == ObjectiveType.EstateTier)
                    {
                        while (qs.progress.Count < stage.objectives.Count) qs.progress.Add(0);
                        changed = false;
                        for (int i = 0; i < stage.objectives.Count; i++)
                            if (stage.objectives[i].type == ObjectiveType.EstateTier && state.estate.tier >= stage.objectives[i].count && qs.progress[i] < stage.objectives[i].count)
                            { qs.progress[i] = stage.objectives[i].count; changed = true; }
                    }
                    else changed = ObjectiveMath.Apply(stage.objectives, qs.progress, e);
                    if (!ObjectiveMath.Done(stage.objectives, qs.progress)) { if (changed) events.RaiseQuest(q.id); break; }
                    var granted = rewards.Grant(stage.reward, "quest:" + q.id + ":" + stage.id);
                    if (!granted.Ok && !rewards.AlreadyGranted("quest:" + q.id + ":" + stage.id))
                    {
                        events.RaiseNotice("Story reward waiting: " + granted.Reason, NoticeKind.Warning);
                        break;
                    }
                    qs.stage++;
                    qs.progress = new List<int>();
                    if (qs.stage >= q.stages.Count)
                    {
                        qs.status = QuestStatus.Completed;
                        events.RaiseNotice("Story complete: " + q.name, NoticeKind.Story);
                    }
                    else events.RaiseNotice(q.name + ": " + q.stages[qs.stage].description, NoticeKind.Story);
                    events.RaiseQuest(q.id);
                    if (e.type != ObjectiveType.EstateTier) e = new ObjectiveEvent(ObjectiveType.EstateTier, "estate", 0);
                }
            }
        }

        public string CurrentStageText(QuestDef q)
        {
            var qs = state.Quest(q.id);
            if (qs.status == QuestStatus.Completed) return "Complete";
            if (qs.status != QuestStatus.Active || qs.stage >= q.stages.Count) return "";
            var st = q.stages[qs.stage];
            var lines = new List<string> { st.description };
            for (int i = 0; i < st.objectives.Count; i++)
                lines.Add("  " + st.objectives[i].description + "  " + (i < qs.progress.Count ? qs.progress[i] : 0) + "/" + st.objectives[i].count);
            return string.Join("\n", lines);
        }
    }
}
