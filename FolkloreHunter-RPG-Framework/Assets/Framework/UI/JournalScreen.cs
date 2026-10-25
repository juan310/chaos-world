using System;
using System.Collections.Generic;
using System.Linq;
using FH.Core;
using FH.Game;
using UnityEngine;
using UnityEngine.UI;

namespace FH.UI
{
    public enum JournalTab { Pack, Hunts, Hunter, Map, Estate, Bonds }

    /// <summary>The hunter's journal: pack and equipment, contracts and story, stats and skills, map, estate, bonds.</summary>
    public class JournalScreen : UiScreen
    {
        public JournalTab Tab { get; private set; }
        string selected;
        RectTransform list, detail;
        static RenderTexture mapTexture;

        public JournalScreen(JournalTab tab) { Tab = tab; }
        public override string Title => "Journal";
        public override Vector2 Size => new Vector2(1640, 900);

        public void Show(JournalTab t) { Tab = t; selected = null; Refresh(); }

        public override void Build()
        {
            var tabs = UiKit.Row("Tabs", Body, 8, 50);
            tabs.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -50), Vector2.zero);
            foreach (JournalTab t in Enum.GetValues(typeof(JournalTab)))
            {
                var tt = t;
                string key = t == JournalTab.Pack ? "I" : t == JournalTab.Hunts ? "J" : t == JournalTab.Hunter ? "K" : t == JournalTab.Map ? "M" : "";
                var b = UiKit.Button(tabs, (t == JournalTab.Hunts ? "Hunts & Story" : t.ToString()) + (key != "" ? "  <size=15><color=#a9a59a>" + key + "</color></size>" : ""), () => Show(tt), 50, 21);
                if (t == Tab) b.Button.GetComponent<Image>().color = new Color(1.5f, 1.3f, .9f);
            }
            var content = UiKit.Rect("Tab content", Body).Place(Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -64));
            switch (Tab)
            {
                case JournalTab.Pack: Pack(content); break;
                case JournalTab.Hunts: Hunts(content); break;
                case JournalTab.Hunter: Hunter(content); break;
                case JournalTab.Map: Map(content); break;
                case JournalTab.Estate: Estate(content); break;
                case JournalTab.Bonds: Bonds(content); break;
            }
        }

        // ---------------- pack ----------------
        void Pack(RectTransform content)
        {
            Layout.Split(content, .44f, out list, out detail);
            var inv = C.Inventory;
            Layout.Heading(list, "Pack  " + inv.Bag.UsedSlots + " / " + inv.Bag.Capacity);
            foreach (var st in C.State.inventory.OrderBy(s => SortKey(s)).ToList())
            {
                var def = C.Db.Item(st.itemId);
                string key = string.IsNullOrEmpty(st.uid) ? st.itemId : st.uid;
                bool eq = inv.IsEquipped(st.uid);
                string label = "<color=" + UiKit.RarityColor(def.rarity) + ">" + def.name + "</color>" + (def.IsUnique ? "" : "  ×" + st.count) + (eq ? "   <color=#f5d68c>equipped</color>" : "");
                var s2 = st;
                Layout.Row(list, label, () => { selected = key; ShowItem(s2); }, () => ShowItem(s2), selected == key);
            }
            var cur = C.State.inventory.FirstOrDefault(s => (string.IsNullOrEmpty(s.uid) ? s.itemId : s.uid) == selected);
            if (cur != null) ShowItem(cur); else ShowEquipment();
        }

        int SortKey(ItemStack s)
        {
            var d = C.Db.Item(s.itemId);
            return (C.Inventory.IsEquipped(s.uid) ? 0 : 100) + (int)d.kind * 10 - (int)d.rarity;
        }

        void ShowEquipment()
        {
            Layout.Clear(detail);
            Layout.Heading(detail, "Equipment");
            foreach (EquipSlot slot in new[] { EquipSlot.Weapon, EquipSlot.Head, EquipSlot.Body, EquipSlot.Charm })
            {
                var st = C.Inventory.Equipped(slot);
                UiKit.Paragraph(detail, "<color=#a9a59a>" + slot + "</color>   " + (st == null ? "—" : C.Db.ItemName(st.itemId)), 21);
            }
            var s = S.Hunter.Stats;
            UiKit.Paragraph(detail, "\n<color=#f5d68c>Attack</color> " + Weapon() + "   <color=#f5d68c>Defense</color> " + s[Stat.Defense] + "   <color=#f5d68c>Health</color> " + s[Stat.MaxHealth] + "   <color=#f5d68c>Stamina</color> " + s[Stat.MaxStamina], 20);
            UiKit.Paragraph(detail, "\nSelect an item to see its details. Consumables can be placed on the hotbar (keys 1-4).", 18, UiKit.Muted);
        }

        string Weapon()
        {
            var w = C.Stats.EquippedWeapon();
            return w == null ? "0" : (w.baseDamage + S.Hunter.Stats[Stat.Attack]).ToString("0");
        }

        public static string ItemText(GameCore c, ItemDef def)
        {
            var lines = new List<string> { "<color=" + UiKit.RarityColor(def.rarity) + "><b>" + def.name + "</b></color>   <color=#a9a59a>" + def.rarity + " " + def.kind.ToString().ToLowerInvariant() + "</color>", def.description };
            if (c.Db.Weapons.TryGetValue(def.id, out var w)) lines.Add("Damage " + w.baseDamage + " " + w.damageType.ToString().ToLowerInvariant() + "   Stagger " + w.poiseDamage + "   Combo " + w.combo.Count + " strikes");
            if (c.Db.Armors.TryGetValue(def.id, out var a)) lines.Add("Defense " + a.defense + (a.resistances.Count > 0 ? "   Resists " + string.Join(", ", a.resistances.Select(r => r.type + " " + Mathf.RoundToInt((1 - r.multiplier) * 100) + "%")) : ""));
            foreach (var m in def.stats) lines.Add((m.value >= 0 ? "+" : "") + m.value + " " + StatCalculator.Label(m.stat));
            if (def.heal > 0) lines.Add("Restores " + def.heal + " health");
            if (def.restoreStamina > 0) lines.Add("Restores " + def.restoreStamina + " stamina");
            if (def.restoreMagic > 0) lines.Add("Restores " + def.restoreMagic + " magic");
            lines.Add("<color=#a9a59a>" + (def.sellPrice > 0 ? "Sells for " + def.sellPrice + " gold" : "Cannot be sold") + (def.IsUnique ? "" : "   ·   stacks to " + def.maxStack) + "</color>");
            return string.Join("\n", lines);
        }

        void ShowItem(ItemStack st)
        {
            Layout.Clear(detail);
            var def = C.Db.Item(st.itemId);
            UiKit.Paragraph(detail, ItemText(C, def), 21);
            if (def.slot != EquipSlot.None)
            {
                bool eq = C.Inventory.IsEquipped(st.uid);
                if (!eq)
                {
                    var delta = C.Stats.PreviewDelta(st.uid);
                    var changes = Enum.GetValues(typeof(Stat)).Cast<Stat>().Where(x => Mathf.Abs(delta[x]) > .001f).Select(x => (delta[x] > 0 ? "<color=#9be38a>+" : "<color=#ff8a6a>") + delta[x].ToString("0.##") + " " + StatCalculator.Label(x) + "</color>");
                    var wd = C.Db.Weapons.TryGetValue(def.id, out var nw) ? nw : null; var cw = C.Stats.EquippedWeapon();
                    var dmg = wd != null && cw != null ? new[] { (wd.baseDamage - cw.baseDamage >= 0 ? "<color=#9be38a>+" : "<color=#ff8a6a>") + (wd.baseDamage - cw.baseDamage) + " weapon damage</color>" } : new string[0];
                    UiKit.Paragraph(detail, "If equipped:  " + string.Join("   ", dmg.Concat(changes).DefaultIfEmpty("no change")), 19);
                }
                var b = UiKit.Button(detail, eq ? "Unequip" : "Equip", () => { var r = eq ? C.Inventory.Unequip(def.slot) : C.Inventory.Equip(st.uid); if (!r.Ok) S.Hud.Flash(r.Reason); Refresh(); });
                if (eq && def.slot == EquipSlot.Weapon) b.Enabled(false, "Equip another weapon to replace it.");
            }
            if (def.kind == ItemKind.Consumable)
            {
                var use = UiKit.Button(detail, "Use", () => { var r = C.Inventory.Use(def.id, S.Hunter); if (!r.Ok) S.Hud.Flash(r.Reason); Refresh(); });
                var can = S.Hunter.CanConsume(def);
                use.Enabled(can.Ok, can.Reason);
                var row = UiKit.Row("Hotbar", detail, 8, 46);
                for (int i = 0; i < 4; i++)
                {
                    int slot = i;
                    bool here = C.State.player.hotbar[i] == def.id;
                    UiKit.Button(row, here ? "Slot " + (i + 1) + " ✓" : "Slot " + (i + 1), () =>
                    {
                        var bar = C.State.player.hotbar;
                        for (int k = 0; k < bar.Count; k++) if (bar[k] == def.id) bar[k] = "";
                        bar[slot] = def.id; Refresh();
                    }, 46, 18);
                }
            }
        }

        // ---------------- hunts ----------------
        void Hunts(RectTransform content)
        {
            Layout.Split(content, .44f, out list, out detail);
            Layout.Heading(list, "Story");
            foreach (var q in C.Db.Quests.Values)
            {
                var qs = C.State.Quest(q.id);
                if (qs.status == QuestStatus.NotStarted) continue;
                var qq = q;
                Layout.Row(list, "<color=#c9d6ff>" + q.name + "</color>" + (qs.status == QuestStatus.Completed ? "   <color=#9be38a>complete</color>" : ""), () => { selected = "q:" + qq.id; ShowQuest(qq); }, () => ShowQuest(qq), selected == "q:" + q.id);
            }
            Layout.Heading(list, "Contracts  (" + C.Contracts.Active.Count() + "/" + ContractSystem.MaxActive + " tracked)");
            foreach (var def in C.Db.Contracts.Values.OrderBy(d => Order(C.Contracts.Get(d.id).status)))
            {
                var cs = C.Contracts.Get(def.id);
                var dd = def;
                Layout.Row(list, def.name + "   <color=#a9a59a>" + StatusLabel(cs.status) + "</color>", () => { selected = "c:" + dd.id; ShowContract(dd); }, () => ShowContract(dd), selected == "c:" + def.id);
            }
            if (selected != null && selected.StartsWith("c:") && C.Db.Contracts.TryGetValue(selected.Substring(2), out var sel)) ShowContract(sel);
            else if (selected != null && selected.StartsWith("q:") && C.Db.Quests.TryGetValue(selected.Substring(2), out var sq)) ShowQuest(sq);
            else { var first = C.Db.Quests.Values.FirstOrDefault(); if (first != null) ShowQuest(first); }
        }

        static int Order(ContractStatus s) => s == ContractStatus.Completed ? 0 : s == ContractStatus.Active ? 1 : s == ContractStatus.Available ? 2 : s == ContractStatus.Locked ? 3 : 4;
        public static string StatusLabel(ContractStatus s) => s == ContractStatus.Completed ? "ready to report" : s == ContractStatus.Active ? "in progress" : s == ContractStatus.Available ? "available" : s == ContractStatus.Locked ? "locked" : "finished";

        void ShowQuest(QuestDef q)
        {
            Layout.Clear(detail);
            Layout.Heading(detail, q.name);
            UiKit.Paragraph(detail, q.description, 20, UiKit.Muted);
            var qs = C.State.Quest(q.id);
            for (int i = 0; i < q.stages.Count; i++)
            {
                bool done = qs.status == QuestStatus.Completed || i < qs.stage;
                bool current = qs.status == QuestStatus.Active && i == qs.stage;
                if (!done && !current) { UiKit.Paragraph(detail, "<color=#6f6c64>· · ·</color>", 20); break; }
                UiKit.Paragraph(detail, (done ? "<color=#9be38a>✓</color> " : "<color=#f5d68c>▶</color> ") + q.stages[i].description, 21);
                if (current)
                    for (int k = 0; k < q.stages[i].objectives.Count; k++)
                        UiKit.Paragraph(detail, "      " + q.stages[i].objectives[k].description + "  " + (k < qs.progress.Count ? qs.progress[k] : 0) + "/" + q.stages[i].objectives[k].count, 19, UiKit.Muted);
            }
        }

        public static void ContractDetail(GameCore c, RectTransform detail, ContractDef def)
        {
            var cs = c.Contracts.Get(def.id);
            Layout.Heading(detail, def.name);
            UiKit.Paragraph(detail, def.description, 20);
            string giver = c.Db.Npcs.TryGetValue(def.giverNpcId ?? "", out var n) ? n.name : "the hunt board";
            string area = c.Db.Areas.TryGetValue(def.areaId ?? "", out var a) ? a.name : "unknown";
            UiKit.Paragraph(detail, "<color=#a9a59a>Client</color> " + giver + "    <color=#a9a59a>Hunting ground</color> " + area + "    <color=#a9a59a>Rank</color> " + (def.rankRequired == 0 ? "any" : c.Db.Ranks.FirstOrDefault(r => r.rank == def.rankRequired)?.name), 19);
            if (c.Db.Monsters.TryGetValue(def.targetMonsterId ?? "", out var m) && m.assetStatus != "ready")
                UiKit.Paragraph(detail, "<color=#a9a59a>This hunt is not yet possible in the world.</color>", 18);
            UiKit.Paragraph(detail, "<color=#f5d68c>Objectives</color>\n" + c.Contracts.ProgressText(def), 20);
            UiKit.Paragraph(detail, "<color=#f5d68c>Reward</color>  " + RewardService.Describe(c.Db, def.reward) + (def.repeatable ? "\n<color=#a9a59a>Repeatable every " + def.cooldownDays + " day(s). Claimed " + cs.timesClaimed + "×.</color>" : ""), 19);
            UiKit.Paragraph(detail, "<color=#a9a59a>Status</color> " + StatusLabel(cs.status), 19);
            if (cs.status == ContractStatus.Locked || cs.status == ContractStatus.Available)
            {
                var gate = c.Contracts.CanAccept(def.id);
                if (!gate.Ok) UiKit.Paragraph(detail, gate.Reason, 19, UiKit.Warn);
            }
            if (cs.status == ContractStatus.Completed) UiKit.Paragraph(detail, "Report to " + giver + " to claim the reward.", 19, UiKit.Good);
        }

        void ShowContract(ContractDef def)
        {
            Layout.Clear(detail);
            ContractDetail(C, detail, def);
            var cs = C.Contracts.Get(def.id);
            if (cs.status == ContractStatus.Active)
                UiKit.Button(detail, "Abandon contract", () => Ui.Confirm("Abandon " + def.name + "?", "Progress on this contract will be lost. You can accept it again later.", "Abandon", () => { C.Contracts.Abandon(def.id); Refresh(); }));
            if (cs.status == ContractStatus.Available) UiKit.Paragraph(detail, "Accept this contract from " + (C.Db.Npcs.TryGetValue(def.giverNpcId ?? "", out var n) ? n.name + " or " : "") + "the hunt board at Thornbury.", 18, UiKit.Muted);
        }

        // ---------------- hunter ----------------
        void Hunter(RectTransform content)
        {
            Layout.Split(content, .44f, out list, out detail);
            var p = C.State.player; var s = S.Hunter.Stats;
            Layout.Heading(list, p.name + "  ·  Level " + p.level);
            UiKit.Paragraph(list, "XP " + p.xp + " / " + C.Progression.XpToNext(p.level) + "      Rank: " + C.Progression.Rank.name + " (" + p.hunterReputation + " reputation)", 19);
            UiKit.Paragraph(list, "Health " + s[Stat.MaxHealth] + "   Stamina " + s[Stat.MaxStamina] + "   Magic " + s[Stat.MaxMagic] + "\nAttack " + Weapon() + "   Defense " + s[Stat.Defense] + "   Spell power " + s[Stat.SpellPower] + "\nStagger power " + s[Stat.PoiseDamage] + "   Critical " + s[Stat.CritChance] + "%   Dodge window " + s[Stat.DodgeWindow].ToString("0.00") + "s", 19);
            Layout.Heading(list, "Attributes  (" + p.attributePoints + " point" + (p.attributePoints == 1 ? "" : "s") + ")");
            foreach (var a in C.Db.Attributes.Values)
            {
                var aa = a;
                var b = Layout.Row(list, a.name + "  " + C.State.Attribute(a.id) + "    <color=#a9a59a>" + a.description + "</color>", () => { var r = C.Progression.SpendAttribute(aa.id); if (!r.Ok) S.Hud.Flash(r.Reason); Refresh(); }, null);
                b.Enabled(p.attributePoints > 0, null);
            }
            Layout.Heading(detail, "Skills  (" + p.skillPoints + " point" + (p.skillPoints == 1 ? "" : "s") + ")");
            foreach (var branch in C.Db.Skills.Values.GroupBy(k => k.branch))
            {
                UiKit.Paragraph(detail, "<color=#f5d68c>" + branch.Key + "</color>", 21);
                foreach (var sk in branch)
                {
                    var kk = sk;
                    bool owned = p.skills.Contains(sk.id);
                    var can = C.Progression.CanUnlock(sk.id);
                    string effect = sk.stats.Count > 0 ? string.Join(", ", sk.stats.Select(m => StatCalculator.Label(m.stat) + " " + s[m.stat].ToString("0.##") + " → " + (s[m.stat] + m.value).ToString("0.##"))) : sk.description;
                    var b = UiKit.Button(detail, (owned ? "✓ " : "") + sk.name + "  <size=16><color=#a9a59a>" + (owned ? sk.description : effect + "  ·  " + sk.cost + " pt") + "</color></size>", () => { var r = C.Progression.Unlock(kk.id); if (!r.Ok) S.Hud.Flash(r.Reason); Refresh(); }, 46, 19);
                    b.Text.alignment = TextAnchor.MiddleLeft; b.Text.rectTransform.Stretch(14);
                    if (owned) b.Enabled(false, null); else b.Enabled(can.Ok, can.Reason);
                }
            }
        }

        // ---------------- map ----------------
        void Map(RectTransform content)
        {
            var frame = UiKit.Panel("Map frame", content, UiKit.PanelLight, false).rectTransform;
            frame.Place(Vector2.zero, new Vector2(.68f, 1), Vector2.zero, Vector2.zero);
            const float side = 740;
            var mapRect = UiKit.Rect("Map", frame).Box(new Vector2(.5f, .5f), Vector2.zero, new Vector2(side, side) - Vector2.one * 20);
            var raw = mapRect.gameObject.AddComponent<RawImage>();
            raw.texture = RenderMap();
            var legend = UiKit.Column("Legend", content, 8);
            legend.Place(new Vector2(.7f, 0), Vector2.one, Vector2.zero, Vector2.zero);
            Layout.Heading(legend, "Valley of Thornbury");
            bool inCrypt = S.CurrentArea == "crypt";
            UiKit.Paragraph(legend, inCrypt ? "You are below ground in the Warden's Crypt. The way out is the stair behind you." : "You are in " + C.Db.Areas[S.CurrentArea].name + ".", 20);
            UiKit.Paragraph(legend, "<color=#ffffff>▲</color> You\n<color=#f5d68c>●</color> People\n<color=#9be38a>■</color> Your cottage\n<color=#c9d6ff>■</color> Thornbury Crossing\n<color=#ff7a55>●</color> Contract quarry\n<color=#b98cff>■</color> Crypt entrance\n<color=#ff7a55>◯</color> Active hunting ground", 20);
            UiKit.Paragraph(legend, C.Clock.Label + (C.Clock.IsNight ? "\nNight: more of the dead walk the graveyard." : ""), 19, UiKit.Muted);
            var w = S.World;
            Marker(mapRect, w.Anchor("map_home").position, "■", new Color(.6f, .9f, .55f), 26);
            Marker(mapRect, w.Anchor("map_thornbury").position, "■", new Color(.79f, .84f, 1f), 26);
            Marker(mapRect, w.Anchor("map_crypt").position, "■", new Color(.73f, .55f, 1f), 26);
            foreach (var n in w.Npcs) Marker(mapRect, n.transform.position, "●", UiKit.Gold, 18);
            var quarry = new HashSet<string>(C.Contracts.Active.Select(a => a.targetMonsterId));
            foreach (var def in C.Contracts.Active)
                if (C.Db.Areas.TryGetValue(def.areaId ?? "", out var area) && area.kind != AreaKind.Dungeon) Ring(mapRect, new Vector3(area.x, 0, area.z), area.radius);
            foreach (var m in S.Monsters.Where(m => m && m.Alive && quarry.Contains(m.MonsterId) && !w.IsInCrypt(m.transform.position))) Marker(mapRect, m.transform.position, "●", new Color(1, .48f, .33f), 18);
            var hp = inCrypt ? w.Anchor("crypt_outside").position : S.Hunter.transform.position;
            var you = Marker(mapRect, hp, "▲", Color.white, 30);
            you.rectTransform.localRotation = Quaternion.Euler(0, 0, -S.Hunter.transform.eulerAngles.y);
        }

        Text Marker(RectTransform map, Vector3 world, string glyph, Color color, int size)
        {
            var b = S.World.MapBounds;
            var uv = new Vector2((world.x - b.xMin) / b.width, (world.z - b.yMin) / b.height);
            var t = UiKit.Label("Marker", map, glyph, size, color, TextAnchor.MiddleCenter);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = uv; t.rectTransform.sizeDelta = new Vector2(40, 40); t.rectTransform.anchoredPosition = Vector2.zero;
            return t;
        }

        void Ring(RectTransform map, Vector3 world, float radius)
        {
            var b = S.World.MapBounds;
            var uv = new Vector2((world.x - b.xMin) / b.width, (world.z - b.yMin) / b.height);
            var r = UiKit.Rect("Hunting ground", map);
            r.anchorMin = r.anchorMax = uv; r.anchoredPosition = Vector2.zero;
            float px = radius / b.width * map.rect.width * 2;
            r.sizeDelta = new Vector2(px, px);
            var img = r.gameObject.AddComponent<Image>(); img.sprite = UiKit.Rounded; img.type = Image.Type.Sliced; img.color = new Color(1, .45f, .3f, .12f);
            var o = r.gameObject.AddComponent<Outline>(); o.effectColor = new Color(1, .45f, .3f, .8f); o.effectDistance = new Vector2(2, 2);
        }

        /// <summary>Renders the real valley from above (no painted map) into a texture.</summary>
        Texture RenderMap()
        {
            if (!mapTexture) mapTexture = new RenderTexture(1024, 1024, 24) { name = "Map render" };
            var go = new GameObject("Map camera");
            var cam = go.AddComponent<Camera>();
            var b = S.World.MapBounds;
            cam.orthographic = true; cam.orthographicSize = b.height / 2;
            cam.transform.SetPositionAndRotation(new Vector3(b.center.x, 90, b.center.y), Quaternion.Euler(90, 0, 0));
            cam.cullingMask = ~((1 << Layers.Hurtbox) | (1 << 5));
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.1f, .12f, .1f);
            cam.targetTexture = mapTexture; cam.enabled = false; cam.nearClipPlane = 1; cam.farClipPlane = 140;
            bool fog = RenderSettings.fog; RenderSettings.fog = false;
            cam.Render();
            RenderSettings.fog = fog;
            cam.targetTexture = null;
            UnityEngine.Object.Destroy(go);
            return mapTexture;
        }

        // ---------------- estate ----------------
        void Estate(RectTransform content)
        {
            Layout.Split(content, .5f, out list, out detail);
            var e = C.Estate; var cur = e.Current;
            Layout.Heading(list, cur.name);
            UiKit.Paragraph(list, cur.description, 20);
            UiKit.Paragraph(list, "<color=#f5d68c>Benefits</color>\nIncome " + cur.incomePerDay + " gold/day (holds up to " + cur.maxIncomeDays + " days)\nStorage " + e.Storage.UsedSlots + "/" + e.Storage.Capacity + " slots\nPack +" + C.Db.EstateTiers.Where(t => t.tier <= cur.tier).Sum(t => t.inventoryBonus) + " slots\nStations: " + (cur.stations.Count > 0 ? string.Join(", ", C.Db.EstateTiers.Where(t => t.tier <= cur.tier).SelectMany(t => t.stations).Distinct()) : "none"), 19);
            int pending = e.PendingIncome();
            var collect = UiKit.Button(list, pending > 0 ? "Collect " + pending + " gold of rent" : "Collect rent", () => { var r = e.CollectIncome(); if (!r.Ok) S.Hud.Flash(r.Reason); Refresh(); });
            collect.Enabled(pending > 0, pending > 0 ? null : cur.incomePerDay > 0 ? "No rent is due yet. Come back tomorrow." : "Your estate earns no income yet.");
            UiKit.Paragraph(list, "Rent is counted in days that pass in the valley; it does not build up while the game is closed.", 16, UiKit.Muted);

            var next = e.Next;
            if (next == null) { Layout.Heading(detail, "Fully improved"); return; }
            Layout.Heading(detail, "Next: " + next.name);
            UiKit.Paragraph(detail, next.description, 20);
            var cost = new List<string> { Have(C.Wallet.Gold, next.goldCost) + " gold" };
            foreach (var m in next.materials) cost.Add(Have(C.Inventory.Count(m.itemId), m.count) + " " + C.Db.ItemName(m.itemId));
            foreach (var r in next.requirements) cost.Add((C.Conditions.Check(r) ? "<color=#9be38a>✓</color> " : "<color=#ff8a6a>✗</color> ") + C.Conditions.Describe(r));
            UiKit.Paragraph(detail, "<color=#f5d68c>Cost and requirements</color>\n" + string.Join("\n", cost), 20);
            UiKit.Paragraph(detail, "<color=#f5d68c>Benefits</color>\nIncome " + next.incomePerDay + " gold/day, storage " + next.storageSlots + " slots, pack +" + next.inventoryBonus + (next.stations.Count > 0 ? ", " + string.Join(", ", next.stations) : ""), 19);
            var up = e.CanUpgrade();
            UiKit.Button(detail, "Build " + next.name, () => Ui.Confirm("Build " + next.name + "?", "This will spend " + next.goldCost + " gold" + (next.materials.Count > 0 ? " and " + string.Join(", ", next.materials.Select(m => m.count + "× " + C.Db.ItemName(m.itemId))) : "") + ".", "Build", () => { var r = e.Upgrade(); if (!r.Ok) S.Hud.Flash(r.Reason); Refresh(); })).Enabled(up.Ok, up.Reason);
        }

        static string Have(int have, int need) => (have >= need ? "<color=#9be38a>" : "<color=#ff8a6a>") + have + "/" + need + "</color>";

        // ---------------- bonds ----------------
        void Bonds(RectTransform content)
        {
            Layout.Split(content, .5f, out list, out detail);
            Layout.Heading(list, "People");
            foreach (var n in C.Db.Npcs.Values)
            {
                var rel = C.Relationships.Get(n.id);
                var stage = C.Relationships.Stage(n.id);
                var nn = n;
                Layout.Row(list, n.name + "   <color=#a9a59a>" + n.title + "</color>   <color=#f5d68c>" + stage.name + "</color>", () => { selected = nn.id; ShowPerson(nn); }, () => ShowPerson(nn), selected == n.id);
            }
            Layout.Heading(list, "Standing");
            foreach (var f in C.Db.Factions.Values) UiKit.Paragraph(list, f.name + "   " + C.State.Faction(f.id) + "\n<color=#a9a59a>" + f.description + "</color>", 19);
            UiKit.Paragraph(list, "Hunter reputation " + C.State.player.hunterReputation + "  ·  " + C.Progression.Rank.name, 19);
            var sel = selected != null && C.Db.Npcs.TryGetValue(selected, out var sn) ? sn : C.Db.Npcs.Values.First();
            ShowPerson(sel);
        }

        void ShowPerson(NpcDef n)
        {
            Layout.Clear(detail);
            var rel = C.Relationships.Get(n.id);
            Layout.Heading(detail, n.name);
            UiKit.Paragraph(detail, n.title + (C.Db.Factions.TryGetValue(n.factionId ?? "", out var f) ? "  ·  " + f.name : ""), 19, UiKit.Muted);
            UiKit.Paragraph(detail, "Bond " + rel.affinity + "   ·   " + C.Relationships.Stage(n.id).name, 21, UiKit.Gold);
            var next = C.Relationships.NextStage(n.id);
            if (next != null)
            {
                var gate = C.Relationships.CanAdvance(n.id, next.id);
                string how = next.automatic ? "Grows naturally as your bond deepens." : "Must be asked in conversation.";
                UiKit.Paragraph(detail, "Next: " + next.name + " (bond " + next.affinityRequired + ").  " + how + (gate.Ok ? "" : "\n<color=#ffb36b>" + gate.Reason + "</color>"), 19);
            }
            var liked = n.gifts.Where(g => g.affinity >= 6).Select(g => C.Db.ItemName(g.itemId)).ToList();
            UiKit.Paragraph(detail, "Talk each day and bring gifts to grow closer." + (liked.Count > 0 && rel.affinity >= 4 ? "\nSeems to love: " + string.Join(", ", liked) : ""), 18, UiKit.Muted);
            if (C.Relationships.IsSpouse(n.id)) UiKit.Paragraph(detail, n.name + " lives with you at the cottage.", 19, UiKit.Good);
            if (rel.lastGiftDay == C.State.world.Day) UiKit.Paragraph(detail, "You already gave a gift today.", 17, UiKit.Muted);
        }
    }
}
