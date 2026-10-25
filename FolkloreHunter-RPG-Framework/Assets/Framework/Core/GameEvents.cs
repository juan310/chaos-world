using System;

namespace FH.Core
{
    public enum NoticeKind { Info, Reward, Warning, Progress, Story }
    public enum ItemSource { Loot, Gather, Reward, Purchase, Craft, Storage, Gift, Start, System }

    /// <summary>Plain C# events connecting systems and presentation. Systems raise; quests, UI and world listen.</summary>
    public class GameEvents
    {
        public event Action<string, int, ItemSource> ItemAdded;
        public event Action<string, int> ItemRemoved;
        public event Action<int, int> GoldChanged;
        public event Action<string, string> MonsterKilled;      // monsterId, spawnId
        public event Action<string, string> PartBroken;         // monsterId, partId
        public event Action<string> ContractChanged;
        public event Action<string> QuestChanged;
        public event Action<int> LevelUp;
        public event Action<string, NoticeKind> Notice;
        public event Action<int> EstateTierChanged;
        public event Action<string> RelationshipChanged;
        public event Action<string> Talked;
        public event Action<string> AreaEntered;
        public event Action<string> Crafted;
        public event Action EquipmentChanged;
        public event Action StatsChanged;
        public event Action<int> DayChanged;
        public event Action<string> FlagSet;

        public void RaiseItemAdded(string id, int n, ItemSource s) => ItemAdded?.Invoke(id, n, s);
        public void RaiseItemRemoved(string id, int n) => ItemRemoved?.Invoke(id, n);
        public void RaiseGold(int before, int after) => GoldChanged?.Invoke(before, after);
        public void RaiseKilled(string monsterId, string spawnId) => MonsterKilled?.Invoke(monsterId, spawnId);
        public void RaisePartBroken(string monsterId, string partId) => PartBroken?.Invoke(monsterId, partId);
        public void RaiseContract(string id) => ContractChanged?.Invoke(id);
        public void RaiseQuest(string id) => QuestChanged?.Invoke(id);
        public void RaiseLevelUp(int level) => LevelUp?.Invoke(level);
        public void RaiseNotice(string text, NoticeKind kind = NoticeKind.Info) => Notice?.Invoke(text, kind);
        public void RaiseEstate(int tier) => EstateTierChanged?.Invoke(tier);
        public void RaiseRelationship(string npc) => RelationshipChanged?.Invoke(npc);
        public void RaiseTalked(string npc) => Talked?.Invoke(npc);
        public void RaiseArea(string area) => AreaEntered?.Invoke(area);
        public void RaiseCrafted(string item) => Crafted?.Invoke(item);
        public void RaiseEquipment() { EquipmentChanged?.Invoke(); StatsChanged?.Invoke(); }
        public void RaiseStats() => StatsChanged?.Invoke();
        public void RaiseDay(int day) => DayChanged?.Invoke(day);
        public void RaiseFlag(string flag) => FlagSet?.Invoke(flag);
    }

    /// <summary>Outcome of a player action. Reason is player-facing text explaining a refusal.</summary>
    public struct Result
    {
        public bool Ok;
        public string Reason;
        public static Result Success => new Result { Ok = true, Reason = "" };
        public static Result Fail(string reason) => new Result { Ok = false, Reason = reason };
        public override string ToString() => Ok ? "OK" : Reason;
    }

    public interface IRandom { float Value(); int Range(int minInclusive, int maxInclusive); }

    public class SeededRandom : IRandom
    {
        readonly Random r;
        public SeededRandom(int seed) { r = new Random(seed); }
        public float Value() => (float)r.NextDouble();
        public int Range(int min, int max) => r.Next(min, max + 1);
    }
}
