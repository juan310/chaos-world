using System;
using System.Collections.Generic;
using System.Linq;

namespace FH.Core
{
    public class StatSheet
    {
        readonly float[] values = new float[Enum.GetValues(typeof(Stat)).Length];
        public float this[Stat s] { get => values[(int)s]; set => values[(int)s] = value; }
        public void Add(IEnumerable<StatMod> mods, float scale = 1) { foreach (var m in mods) values[(int)m.stat] += m.value * scale; }
        public StatSheet Clone() { var c = new StatSheet(); Array.Copy(values, c.values, values.Length); return c; }
    }

    /// <summary>Derives the player's combat stats from base balance, attributes, skills and equipment.</summary>
    public class StatCalculator
    {
        readonly GameDatabase db;
        readonly GameState state;
        public StatCalculator(GameDatabase db, GameState state) { this.db = db; this.state = state; }

        public StatSheet Compute(bool includeEquipment = true)
        {
            var b = db.Balance;
            var s = new StatSheet();
            s[Stat.MaxHealth] = b.baseHealth;
            s[Stat.MaxStamina] = b.baseStamina;
            s[Stat.MaxMagic] = b.baseMagic;
            s[Stat.Attack] = b.baseAttack;
            s[Stat.StaminaRegen] = b.baseStaminaRegen;
            s[Stat.MoveSpeed] = 1;
            s[Stat.DodgeWindow] = .32f;
            foreach (var a in state.player.attributes)
                if (db.Attributes.TryGetValue(a.id, out var def)) s.Add(def.perPoint, a.points);
            foreach (var id in state.player.skills)
                if (db.Skills.TryGetValue(id, out var sk)) s.Add(sk.stats);
            if (includeEquipment)
            {
                foreach (var uid in new[] { state.equipment.weapon, state.equipment.head, state.equipment.body, state.equipment.charm })
                {
                    var stack = state.inventory.FirstOrDefault(x => !string.IsNullOrEmpty(uid) && x.uid == uid);
                    var item = stack == null ? null : db.Item(stack.itemId);
                    if (item == null) continue;
                    s.Add(item.stats);
                    if (db.Armors.TryGetValue(item.id, out var armor)) s[Stat.Defense] += armor.defense;
                }
            }
            return s;
        }

        /// <summary>Preview of how equipping an item changes stats (positive = gain).</summary>
        public StatSheet PreviewDelta(string uid)
        {
            var stack = state.inventory.FirstOrDefault(x => x.uid == uid);
            var item = stack == null ? null : db.Item(stack.itemId);
            var before = Compute();
            if (item == null || item.slot == EquipSlot.None) return new StatSheet();
            var e = state.equipment;
            var saved = new EquipmentState { weapon = e.weapon, head = e.head, body = e.body, charm = e.charm };
            switch (item.slot)
            {
                case EquipSlot.Weapon: e.weapon = uid; break;
                case EquipSlot.Head: e.head = uid; break;
                case EquipSlot.Body: e.body = uid; break;
                case EquipSlot.Charm: e.charm = uid; break;
            }
            var after = Compute();
            state.equipment = saved;
            var d = new StatSheet();
            foreach (Stat st in Enum.GetValues(typeof(Stat))) d[st] = after[st] - before[st];
            return d;
        }

        public WeaponDef EquippedWeapon()
        {
            var stack = state.inventory.FirstOrDefault(x => x.uid == state.equipment.weapon && !string.IsNullOrEmpty(x.uid));
            return stack != null && db.Weapons.TryGetValue(stack.itemId, out var w) ? w : null;
        }

        public List<Resistance> PlayerResistances()
        {
            var list = new List<Resistance>();
            foreach (var uid in new[] { state.equipment.head, state.equipment.body, state.equipment.charm })
            {
                var stack = state.inventory.FirstOrDefault(x => !string.IsNullOrEmpty(uid) && x.uid == uid);
                if (stack != null && db.Armors.TryGetValue(stack.itemId, out var a)) list.AddRange(a.resistances);
            }
            return list;
        }

        public static string Label(Stat s)
        {
            switch (s)
            {
                case Stat.MaxHealth: return "Health";
                case Stat.MaxStamina: return "Stamina";
                case Stat.MaxMagic: return "Magic";
                case Stat.Attack: return "Attack";
                case Stat.Defense: return "Defense";
                case Stat.SpellPower: return "Spell power";
                case Stat.StaminaRegen: return "Stamina recovery";
                case Stat.PoiseDamage: return "Stagger power";
                case Stat.CritChance: return "Critical chance";
                case Stat.MoveSpeed: return "Move speed";
                case Stat.DodgeWindow: return "Dodge window";
                default: return s.ToString();
            }
        }
    }

    public class ProgressionSystem
    {
        readonly GameDatabase db;
        readonly GameState state;
        readonly GameEvents events;
        public ProgressionSystem(GameDatabase db, GameState state, GameEvents events) { this.db = db; this.state = state; this.events = events; }

        public int XpToNext(int level) => (int)Math.Round(db.Balance.xpBase * Math.Pow(db.Balance.xpGrowth, level - 1));

        public int AddXp(int amount)
        {
            if (amount <= 0) return 0;
            var p = state.player;
            int gained = 0;
            p.xp += amount;
            while (p.level < db.Balance.maxLevel && p.xp >= XpToNext(p.level))
            {
                p.xp -= XpToNext(p.level);
                p.level++;
                gained++;
                p.attributePoints += db.Balance.attributePointsPerLevel;
                p.skillPoints += db.Balance.skillPointsPerLevel;
                events.RaiseLevelUp(p.level);
                events.RaiseNotice("Level " + p.level + "! +" + db.Balance.attributePointsPerLevel + " attribute points, +" + db.Balance.skillPointsPerLevel + " skill point.", NoticeKind.Reward);
            }
            if (p.level >= db.Balance.maxLevel) p.xp = 0;
            if (gained > 0) events.RaiseStats();
            return gained;
        }

        public Result SpendAttribute(string id)
        {
            if (!db.Attributes.ContainsKey(id)) return Result.Fail("Unknown attribute.");
            if (state.player.attributePoints <= 0) return Result.Fail("No attribute points. Gain a level to earn more.");
            state.player.attributePoints--;
            var a = state.player.attributes.FirstOrDefault(x => x.id == id);
            if (a == null) state.player.attributes.Add(a = new AttributeValue { id = id });
            a.points++;
            events.RaiseStats();
            return Result.Success;
        }

        public Result CanUnlock(string skillId)
        {
            if (!db.Skills.TryGetValue(skillId, out var s)) return Result.Fail("Unknown skill.");
            if (state.player.skills.Contains(skillId)) return Result.Fail("Already learned.");
            if (state.player.level < s.levelRequired) return Result.Fail("Requires level " + s.levelRequired + ".");
            foreach (var r in s.requires)
                if (!state.player.skills.Contains(r)) return Result.Fail("Learn " + (db.Skills.TryGetValue(r, out var rd) ? rd.name : r) + " first.");
            if (state.player.skillPoints < s.cost) return Result.Fail("Needs " + s.cost + " skill point" + (s.cost == 1 ? "" : "s") + " (have " + state.player.skillPoints + ").");
            return Result.Success;
        }

        public Result Unlock(string skillId)
        {
            var r = CanUnlock(skillId);
            if (!r.Ok) return r;
            var s = db.Skills[skillId];
            state.player.skillPoints -= s.cost;
            state.player.skills.Add(skillId);
            events.RaiseStats();
            events.RaiseNotice("Learned " + s.name + ".", NoticeKind.Reward);
            return Result.Success;
        }

        public RankDef Rank => db.RankFor(state.player.hunterReputation);

        public void AddHunterReputation(int amount)
        {
            if (amount <= 0) return;
            var before = Rank;
            state.player.hunterReputation += amount;
            var after = Rank;
            if (after.rank > before.rank) events.RaiseNotice("Hunter rank raised: " + after.name + ". New contracts are available.", NoticeKind.Reward);
        }
    }
}
