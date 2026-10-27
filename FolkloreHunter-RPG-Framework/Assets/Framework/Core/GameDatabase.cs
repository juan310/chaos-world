using System;
using System.Collections.Generic;
using System.Linq;

namespace FH.Core
{
    /// <summary>Indexed, validated view over one or more ContentPacks.</summary>
    public class GameDatabase
    {
        public BalanceDef Balance = new BalanceDef();
        public readonly Dictionary<string, ItemDef> Items = new Dictionary<string, ItemDef>();
        public readonly Dictionary<string, WeaponDef> Weapons = new Dictionary<string, WeaponDef>();
        public readonly Dictionary<string, ArmorDef> Armors = new Dictionary<string, ArmorDef>();
        public readonly Dictionary<string, StatusDef> Statuses = new Dictionary<string, StatusDef>();
        public readonly Dictionary<string, SpellDef> Spells = new Dictionary<string, SpellDef>();
        public readonly Dictionary<string, MonsterDef> Monsters = new Dictionary<string, MonsterDef>();
        public readonly Dictionary<string, LootTableDef> LootTables = new Dictionary<string, LootTableDef>();
        public readonly Dictionary<string, ContractDef> Contracts = new Dictionary<string, ContractDef>();
        public readonly Dictionary<string, QuestDef> Quests = new Dictionary<string, QuestDef>();
        public readonly Dictionary<string, RecipeDef> Recipes = new Dictionary<string, RecipeDef>();
        public readonly List<EstateTierDef> EstateTiers = new List<EstateTierDef>();
        public readonly Dictionary<string, NpcDef> Npcs = new Dictionary<string, NpcDef>();
        public readonly Dictionary<string, FactionDef> Factions = new Dictionary<string, FactionDef>();
        public readonly List<RankDef> Ranks = new List<RankDef>();
        public readonly List<RelationshipStageDef> RelationshipStages = new List<RelationshipStageDef>();
        public readonly Dictionary<string, SkillDef> Skills = new Dictionary<string, SkillDef>();
        public readonly Dictionary<string, AttributeDef> Attributes = new Dictionary<string, AttributeDef>();
        public readonly Dictionary<string, VendorDef> Vendors = new Dictionary<string, VendorDef>();
        public readonly Dictionary<string, DialogueDef> Dialogues = new Dictionary<string, DialogueDef>();

        /// <summary>Some dialogue has a choice that claims this contract's reward (otherwise the board claims it).</summary>
        public bool ClaimedInDialogue(string contractId) => Dialogues.Values.Any(d => d.nodes.Any(n => n.choices.Any(c => c.effects.Any(e => e.type == EffectType.ClaimContract && e.id == contractId))));

        /// <summary>The dialogue node ids (of one dialogue) where this contract can be claimed.</summary>
        public IEnumerable<string> ClaimNodes(string dialogueId, string contractId) => Dialogues.TryGetValue(dialogueId, out var d)
            ? d.nodes.Where(n => n.choices.Any(c => c.effects.Any(e => e.type == EffectType.ClaimContract && e.id == contractId))).Select(n => n.id)
            : Enumerable.Empty<string>();
        public readonly Dictionary<string, AreaDef> Areas = new Dictionary<string, AreaDef>();
        public readonly Dictionary<string, SpawnDef> Spawns = new Dictionary<string, SpawnDef>();
        public readonly Dictionary<string, HuntMovesetDef> HuntMovesets = new Dictionary<string, HuntMovesetDef>();
        public readonly List<string> Errors = new List<string>();

        public static GameDatabase FromJson(params string[] jsonFiles)
        {
            var db = new GameDatabase();
            foreach (var text in jsonFiles) db.Add(JsonMapper.To<ContentPack>(Json.Parse(text)));
            db.Validate();
            return db;
        }

        void Put<T>(Dictionary<string, T> map, IEnumerable<T> list, Func<T, string> id, string kind)
        {
            foreach (var item in list)
            {
                string key = id(item);
                if (string.IsNullOrEmpty(key)) { Errors.Add(kind + " without id"); continue; }
                if (map.ContainsKey(key)) Errors.Add("Duplicate " + kind + " id '" + key + "'");
                map[key] = item;
            }
        }

        public void Add(ContentPack p)
        {
            if (p.balance != null) Balance = p.balance;
            Put(Items, p.items, x => x.id, "item");
            Put(Weapons, p.weapons, x => x.id, "weapon");
            Put(Armors, p.armors, x => x.id, "armor");
            Put(Statuses, p.statuses, x => x.id, "status");
            Put(Spells, p.spells, x => x.id, "spell");
            Put(Monsters, p.monsters, x => x.id, "monster");
            Put(LootTables, p.lootTables, x => x.id, "loot table");
            Put(Contracts, p.contracts, x => x.id, "contract");
            Put(Quests, p.quests, x => x.id, "quest");
            Put(Recipes, p.recipes, x => x.id, "recipe");
            Put(Npcs, p.npcs, x => x.id, "npc");
            Put(Factions, p.factions, x => x.id, "faction");
            Put(Skills, p.skills, x => x.id, "skill");
            Put(Attributes, p.attributes, x => x.id, "attribute");
            Put(Vendors, p.vendors, x => x.id, "vendor");
            Put(HuntMovesets, p.huntMovesets, x => x.id, "hunt moveset");
            Put(Dialogues, p.dialogues, x => x.id, "dialogue");
            Put(Areas, p.areas, x => x.id, "area");
            Put(Spawns, p.spawns, x => x.id, "spawn");
            EstateTiers.AddRange(p.estateTiers);
            EstateTiers.Sort((a, b) => a.tier.CompareTo(b.tier));
            Ranks.AddRange(p.ranks);
            Ranks.Sort((a, b) => a.rank.CompareTo(b.rank));
            RelationshipStages.AddRange(p.relationshipStages);
            RelationshipStages.Sort((a, b) => a.order.CompareTo(b.order));
        }

        void Ref(string kind, string owner, string id, System.Collections.IDictionary map)
        {
            if (!string.IsNullOrEmpty(id) && !map.Contains(id)) Errors.Add(owner + " references missing " + kind + " '" + id + "'");
        }

        void Amounts(string owner, IEnumerable<ItemAmount> list)
        {
            foreach (var a in list)
            {
                Ref("item", owner, a.itemId, Items);
                if (a.count <= 0) Errors.Add(owner + " has non-positive amount for " + a.itemId);
            }
        }

        void Reward(string owner, RewardDef r)
        {
            if (r == null) return;
            Amounts(owner, r.items);
            foreach (var id in r.unlockRecipes) Ref("recipe", owner, id, Recipes);
            foreach (var f in r.reputation) Ref("faction", owner, f.factionId, Factions);
            if (r.gold < 0 || r.xp < 0) Errors.Add(owner + " has a negative reward");
        }

        void Objectives(string owner, IEnumerable<ObjectiveDef> list)
        {
            foreach (var o in list)
            {
                if (o.count <= 0) Errors.Add(owner + " objective " + o.id + " has no count");
                if (o.type == ObjectiveType.Kill) Ref("monster", owner, o.targetId, Monsters);
                if (o.type == ObjectiveType.Collect || o.type == ObjectiveType.Craft) Ref("item", owner, o.targetId, Items);
                if (o.type == ObjectiveType.Talk) Ref("npc", owner, o.targetId, Npcs);
                if (o.type == ObjectiveType.EnterArea) Ref("area", owner, o.targetId, Areas);
            }
        }

        public void Validate()
        {
            foreach (var i in Items.Values)
            {
                if (i.maxStack < 1) Errors.Add("Item " + i.id + " maxStack < 1");
                if ((i.kind == ItemKind.Weapon || i.kind == ItemKind.Armor || i.kind == ItemKind.Charm) && !i.IsUnique) Errors.Add("Equipment " + i.id + " must be unique (maxStack 1)");
                if (i.kind == ItemKind.Weapon && !Weapons.ContainsKey(i.id)) Errors.Add("Weapon item " + i.id + " has no weapon definition");
                if (i.sellPrice > i.buyPrice && i.buyPrice > 0) Errors.Add("Item " + i.id + " sells for more than it costs");
            }
            foreach (var w in Weapons.Values)
            {
                Ref("item", "Weapon " + w.id, w.id, Items);
                if (w.combo.Count == 0) Errors.Add("Weapon " + w.id + " has no attacks");
                foreach (var a in w.combo) if (a.windup < 0 || a.active <= 0 || a.recovery < 0) Errors.Add("Weapon " + w.id + " attack " + a.id + " has invalid timing");
            }
            foreach (var set in HuntMovesets.Values)
            {
                var ids = new HashSet<string>(set.moves.Select(x => x.id));
                foreach (var opener in new[] { set.light, set.heavy, set.lightHeavy, set.forwardLight, set.drawAttack, set.rollAttack, set.counter,
                                               set.guardAttack, set.guardDash, set.counterRelease, set.retribution, set.dashAttack, set.dashFinisher,
                                               set.shot, set.chargedShot, set.powerShot, set.arcShot, set.dragonPiercer, set.spirit,
                                               set.foresightCounter, set.foresightSlash, set.iaiSlash, set.iaiSpiritSlash, set.lateralFade,
                                               set.backstepAttack, set.rend, set.clashFinisher })
                    if (!string.IsNullOrEmpty(opener) && !ids.Contains(opener)) Errors.Add("Hunt moveset " + set.id + " opener '" + opener + "' is not one of its moves");
                foreach (var mv in set.moves)
                {
                    if (string.IsNullOrEmpty(mv.anim)) Errors.Add("Hunt move " + set.id + "." + mv.id + " has no animation");
                    if (mv.windup < 0 || mv.active <= 0 || mv.recovery < 0) Errors.Add("Hunt move " + set.id + "." + mv.id + " has invalid timing");
                    foreach (var next in new[] { mv.onLight, mv.onHeavy, mv.onLightHeavy, mv.onSpirit })
                        if (!string.IsNullOrEmpty(next) && !ids.Contains(next)) Errors.Add("Hunt move " + set.id + "." + mv.id + " routes to unknown move '" + next + "'");
                }
                if (set.chargeMultipliers.Count < 3) Errors.Add("Hunt moveset " + set.id + " needs three charge multipliers");
                if (!Enum.TryParse(set.weaponClass, true, out WeaponClass wc)) Errors.Add("Hunt moveset " + set.id + " has unknown weapon class '" + set.weaponClass + "'");
                if (!Enum.TryParse(set.dodge, true, out DodgeStyle _)) Errors.Add("Hunt moveset " + set.id + " has unknown dodge '" + set.dodge + "'");
                if (wc == WeaponClass.Bow)
                {
                    if (set.shotLevels.Count < 2) Errors.Add("Hunt moveset " + set.id + " (bow) needs at least two charge levels");
                    if (set.chargeTimes.Count < set.shotLevels.Count - 1 || set.chargeTimes.Zip(set.chargeTimes.Skip(1), (x, y) => y <= x).Any(b => b))
                        Errors.Add("Hunt moveset " + set.id + " (bow) charge times must rise, one per level after the first");
                    foreach (var id in new[] { set.shot, set.powerShot, set.arcShot, set.dragonPiercer })
                        if (set.Move(id) != null && set.Move(id).shot == null) Errors.Add("Hunt move " + set.id + "." + id + " is a bow shot without a shot definition");
                }
                if (wc == WeaponClass.Katana && (set.Move(set.spirit) == null || set.Move(set.foresightCounter) == null || set.Move(set.iaiSpiritSlash) == null || !set.moves.Any(m => m.spiritLevelUp)))
                    Errors.Add("Hunt moveset " + set.id + " (katana) needs a spirit opener, foresight counter, iai spirit slash and a level-up move");
                if (!string.IsNullOrEmpty(set.rend) && set.Move(set.rend) != null && !set.Move(set.rend).rend) Errors.Add("Hunt moveset " + set.id + " rend move '" + set.rend + "' does not tear wounds (rend: true)");
                if (set.clash && set.Move(set.clashFinisher) == null) Errors.Add("Hunt moveset " + set.id + " has the power clash but no clash finisher");
                foreach (var mv in set.moves)
                    if (mv.perfectBonus > 0 && !(mv.perfectFrom >= 0 && mv.perfectTo > mv.perfectFrom && mv.perfectTo <= 1)) Errors.Add("Hunt move " + set.id + "." + mv.id + " has an invalid perfect-link window");
                if (set.weapon != null && set.weapon.baseDamage <= 0) Errors.Add("Hunt moveset " + set.id + " weapon needs base damage");
                foreach (var mv in set.moves)
                    if (mv.shot != null && (mv.shot.arrows < 1 || mv.shot.speed <= 0)) Errors.Add("Hunt move " + set.id + "." + mv.id + " has an invalid shot");
            }
            foreach (var m in Monsters.Values)
            {
                Ref("loot table", "Monster " + m.id, m.lootTable, LootTables);
                if (m.attacks.Count == 0) Errors.Add("Monster " + m.id + " has no attacks");
                foreach (var a in m.attacks) if (a.telegraph < .25f) Errors.Add("Monster " + m.id + " attack " + a.id + " telegraph shorter than 0.25s is unreadable");
                foreach (var part in m.parts)
                {
                    Ref("loot table", "Monster part " + m.id + "." + part.id, part.breakLootTable, LootTables);
                    if (!string.IsNullOrEmpty(part.requiresBrokenPart) && !m.parts.Any(x => x.id == part.requiresBrokenPart)) Errors.Add("Monster part " + part.id + " requires unknown part " + part.requiresBrokenPart);
                }
            }
            foreach (var t in LootTables.Values) foreach (var e in t.entries) { Ref("item", "Loot " + t.id, e.itemId, Items); if (e.min > e.max || e.min < 0) Errors.Add("Loot " + t.id + " bad range"); }
            foreach (var c in Contracts.Values) { Reward("Contract " + c.id, c.reward); Objectives("Contract " + c.id, c.objectives); Ref("npc", "Contract " + c.id, c.giverNpcId, Npcs); if (c.objectives.Count == 0) Errors.Add("Contract " + c.id + " has no objectives"); }
            foreach (var q in Quests.Values) foreach (var s in q.stages) { Reward("Quest " + q.id, s.reward); Objectives("Quest " + q.id, s.objectives); }
            foreach (var r in Recipes.Values)
            {
                Amounts("Recipe " + r.id, r.inputs);
                Ref("item", "Recipe " + r.id, r.output.itemId, Items);
                Ref("item", "Recipe " + r.id, r.upgradeFrom, Items);
                if (r.output.count <= 0) Errors.Add("Recipe " + r.id + " produces nothing");
            }
            for (int i = 0; i < EstateTiers.Count; i++)
            {
                if (EstateTiers[i].tier != i) Errors.Add("Estate tiers must be contiguous from 0");
                Amounts("Estate tier " + EstateTiers[i].id, EstateTiers[i].materials);
            }
            foreach (var n in Npcs.Values)
            {
                Ref("vendor", "NPC " + n.id, n.vendorId, Vendors);
                Ref("dialogue", "NPC " + n.id, n.dialogueId, Dialogues);
                Ref("faction", "NPC " + n.id, n.factionId, Factions);
            }
            foreach (var s in Skills.Values) { foreach (var r in s.requires) Ref("skill", "Skill " + s.id, r, Skills); Ref("spell", "Skill " + s.id, s.unlockSpell, Spells); }
            foreach (var v in Vendors.Values) foreach (var st in v.stock) Ref("item", "Vendor " + v.id, st.itemId, Items);
            foreach (var d in Dialogues.Values)
            {
                var ids = new HashSet<string>(d.nodes.Select(n => n.id));
                foreach (var e in d.entries) if (!ids.Contains(e)) Errors.Add("Dialogue " + d.id + " entry " + e + " missing");
                foreach (var n in d.nodes) foreach (var c in n.choices) if (!string.IsNullOrEmpty(c.next) && !ids.Contains(c.next)) Errors.Add("Dialogue " + d.id + " choice leads to missing node " + c.next);
            }
            foreach (var s in Spawns.Values) { Ref("monster", "Spawn " + s.id, s.monsterId, Monsters); Ref("area", "Spawn " + s.id, s.areaId, Areas); }
            Ref("weapon", "Balance", Balance.startWeapon, Weapons);
            Amounts("Balance start items", Balance.startItems);
            foreach (var r in Balance.startRecipes) Ref("recipe", "Balance", r, Recipes);
        }

        public ItemDef Item(string id) => id != null && Items.TryGetValue(id, out var d) ? d : null;
        public string ItemName(string id) => Item(id)?.name ?? id;
        public RankDef RankFor(int reputation) => Ranks.LastOrDefault(r => reputation >= r.reputationRequired) ?? new RankDef { rank = 0, name = "Unranked" };
        public EstateTierDef Tier(int tier) => tier >= 0 && tier < EstateTiers.Count ? EstateTiers[tier] : null;
    }
}
