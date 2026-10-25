using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FH.Core
{
    public enum LoadStatus { Loaded, LoadedFromBackup, NoSave, Corrupt, TooNew }

    public class LoadResult
    {
        public LoadStatus Status;
        public GameState State;
        public string Message = "";
        public int MigratedFrom;
        public readonly List<string> Repairs = new List<string>();
        public bool Ok => Status == LoadStatus.Loaded || Status == LoadStatus.LoadedFromBackup;
    }

    /// <summary>Versioned save encoding. Old saves are upgraded step by step on the raw JSON tree,
    /// then mapped onto GameState and sanitized against current content.</summary>
    public static class SaveCodec
    {
        public static string Encode(GameState s)
        {
            s.schemaVersion = GameState.CurrentSchema;
            return Json.Write(JsonMapper.ToTree(s), true);
        }

        public static LoadResult Decode(string text, GameDatabase db)
        {
            var result = new LoadResult();
            object tree;
            try { tree = Json.Parse(text); }
            catch (Exception e) { result.Status = LoadStatus.Corrupt; result.Message = "Unreadable save data (" + e.Message + ")."; return result; }
            if (!(tree is Dictionary<string, object> map)) { result.Status = LoadStatus.Corrupt; result.Message = "Save data is not an object."; return result; }
            int version = map.TryGetValue("schemaVersion", out var v) && v is double d ? (int)d : 1;
            result.MigratedFrom = version;
            if (version > GameState.CurrentSchema) { result.Status = LoadStatus.TooNew; result.Message = "This save was made by a newer version of the game."; return result; }
            try
            {
                if (version < 2) map = MigrateV1toV2(map);
                if (version < 3) map = MigrateV2toV3(map);
                result.State = JsonMapper.To<GameState>(map);
            }
            catch (Exception e) { result.Status = LoadStatus.Corrupt; result.Message = "Save data could not be upgraded (" + e.Message + ")."; return result; }
            Sanitize(result.State, db, result.Repairs);
            result.Status = LoadStatus.Loaded;
            return result;
        }

        static Dictionary<string, object> Obj(Dictionary<string, object> m, string key)
        {
            if (!m.TryGetValue(key, out var v) || !(v is Dictionary<string, object> o)) { o = new Dictionary<string, object>(); m[key] = o; }
            return o;
        }

        static double Num(Dictionary<string, object> m, string key, double fallback = 0) => m.TryGetValue(key, out var v) && v is double d ? d : fallback;
        static bool Bool(Dictionary<string, object> m, string key) => m.TryGetValue(key, out var v) && v is bool b && b;

        /// <summary>v1 = the 3D sample's flat prototype state (gold, potions, herbs, kills, upgraded, contractPaid, health).</summary>
        public static Dictionary<string, object> MigrateV1toV2(Dictionary<string, object> v1)
        {
            var v2 = new Dictionary<string, object> { ["schemaVersion"] = 2.0 };
            v2["gold"] = Num(v1, "gold", 75);
            var inv = new List<object>();
            int potions = (int)Num(v1, "potions", 0), herbs = (int)Num(v1, "herbs", 0);
            if (potions > 0) inv.Add(new Dictionary<string, object> { ["uid"] = "", ["itemId"] = "potion_mooncap", ["count"] = (double)potions });
            if (herbs > 0) inv.Add(new Dictionary<string, object> { ["uid"] = "", ["itemId"] = "mooncap_herb", ["count"] = (double)herbs });
            v2["inventory"] = inv;
            v2["player"] = new Dictionary<string, object> { ["health"] = Num(v1, "health", -1), ["level"] = 1.0 };
            v2["estateTier"] = Bool(v1, "upgraded") ? 1.0 : 0.0;
            var claimed = new List<object>();
            var contracts = new List<object>();
            if (Bool(v1, "contractPaid"))
            {
                claimed.Add("contract:c_gravebound_cull:0");
                contracts.Add(new Dictionary<string, object> { ["id"] = "c_gravebound_cull", ["status"] = "Claimed", ["timesClaimed"] = 1.0, ["progress"] = new List<object>() });
            }
            v2["claimed"] = claimed;
            v2["contracts"] = contracts;
            v2["flags"] = new List<object> { "migrated_from_sample" };
            return v2;
        }

        /// <summary>v2 kept estate tier, claimed rewards and flags at the root; v3 nests them under estate/world.</summary>
        public static Dictionary<string, object> MigrateV2toV3(Dictionary<string, object> v2)
        {
            var m = new Dictionary<string, object>(v2) { ["schemaVersion"] = 3.0 };
            var estate = Obj(m, "estate");
            if (m.TryGetValue("estateTier", out var tier)) { estate["tier"] = tier; m.Remove("estateTier"); }
            if (!estate.ContainsKey("storage")) estate["storage"] = new List<object>();
            var world = Obj(m, "world");
            if (m.TryGetValue("claimed", out var claimed)) { world["claimedRewards"] = claimed; m.Remove("claimed"); }
            if (m.TryGetValue("flags", out var flags)) { world["flags"] = flags; m.Remove("flags"); }
            if (!m.ContainsKey("relationships")) m["relationships"] = new List<object>();
            return m;
        }

        /// <summary>Repairs anything that would break invariants: unknown ids, negative counts, dangling equipment.</summary>
        public static void Sanitize(GameState s, GameDatabase db, List<string> repairs)
        {
            if (s.player == null) { s.player = new PlayerState(); repairs.Add("player reset"); }
            if (s.equipment == null) s.equipment = new EquipmentState();
            if (s.estate == null) s.estate = new EstateState();
            if (s.world == null) s.world = new WorldState();
            if (s.gold < 0) { repairs.Add("negative gold " + s.gold); s.gold = 0; }
            CleanStacks(s.inventory, db, repairs, "pack");
            CleanStacks(s.estate.storage, db, repairs, "storage");
            var uids = new HashSet<string>();
            foreach (var st in s.inventory.Concat(s.estate.storage))
            {
                var def = db.Item(st.itemId);
                if (def.IsUnique && (string.IsNullOrEmpty(st.uid) || !uids.Add(st.uid))) { st.uid = s.NewUid(); repairs.Add("reassigned id for " + st.itemId); }
                if (!def.IsUnique) st.uid = "";
            }
            int maxUid = s.inventory.Concat(s.estate.storage).Select(x => x.uid).Where(u => !string.IsNullOrEmpty(u) && u.StartsWith("u"))
                .Select(u => int.TryParse(u.Substring(1), out var n) ? n : 0).DefaultIfEmpty(0).Max();
            if (s.nextUid <= maxUid) s.nextUid = maxUid + 1;
            s.equipment.weapon = CheckEquip(s, db, s.equipment.weapon, EquipSlot.Weapon, repairs);
            s.equipment.head = CheckEquip(s, db, s.equipment.head, EquipSlot.Head, repairs);
            s.equipment.body = CheckEquip(s, db, s.equipment.body, EquipSlot.Body, repairs);
            s.equipment.charm = CheckEquip(s, db, s.equipment.charm, EquipSlot.Charm, repairs);
            int removed = s.contracts.RemoveAll(c => c == null || !db.Contracts.ContainsKey(c.id));
            if (removed > 0) repairs.Add("removed " + removed + " unknown contracts");
            s.contracts = s.contracts.GroupBy(c => c.id).Select(g => g.First()).ToList();
            s.quests.RemoveAll(q => q == null || !db.Quests.ContainsKey(q.id));
            s.knownRecipes = s.knownRecipes.Where(db.Recipes.ContainsKey).Distinct().ToList();
            s.player.skills = s.player.skills.Where(db.Skills.ContainsKey).Distinct().ToList();
            s.player.attributes.RemoveAll(a => a == null || !db.Attributes.ContainsKey(a.id) || a.points < 0);
            s.relationships.RemoveAll(r => r == null || !db.Npcs.ContainsKey(r.npcId));
            foreach (var r in s.relationships) if (!db.RelationshipStages.Any(x => x.id == r.stage)) { r.stage = db.RelationshipStages.FirstOrDefault()?.id ?? "stranger"; repairs.Add("relationship stage reset"); }
            int maxTier = Math.Max(0, db.EstateTiers.Count - 1);
            if (s.estate.tier < 0 || s.estate.tier > maxTier) { repairs.Add("estate tier clamped"); s.estate.tier = Math.Max(0, Math.Min(maxTier, s.estate.tier)); }
            if (s.estate.lastIncomeDay > s.world.Day) { repairs.Add("future income day clamped"); s.estate.lastIncomeDay = s.world.Day; }
            if (s.world.hours < 0 || double.IsNaN(s.world.hours)) { s.world.hours = db.Balance.dayStartHour; repairs.Add("clock reset"); }
            if (!db.Areas.ContainsKey(s.player.areaId ?? "")) { s.player.areaId = db.Balance.startArea; repairs.Add("unknown area reset"); }
            if (s.player.level < 1) s.player.level = 1;
            if (s.player.xp < 0) s.player.xp = 0;
            if (s.player.attributePoints < 0) s.player.attributePoints = 0;
            if (s.player.skillPoints < 0) s.player.skillPoints = 0;
            while (s.player.hotbar.Count < 4) s.player.hotbar.Add("");
            s.world.claimedRewards = s.world.claimedRewards.Distinct().ToList();
        }

        static void CleanStacks(List<ItemStack> stacks, GameDatabase db, List<string> repairs, string where)
        {
            int unknown = stacks.RemoveAll(x => x == null || db.Item(x.itemId) == null);
            if (unknown > 0) repairs.Add("removed " + unknown + " unknown items from " + where);
            int empty = stacks.RemoveAll(x => x.count <= 0);
            if (empty > 0) repairs.Add("removed " + empty + " empty stacks from " + where);
            foreach (var st in stacks)
            {
                var def = db.Item(st.itemId);
                if (st.count > def.maxStack) { repairs.Add("split oversized " + st.itemId); }
            }
            // Re-pack stackables to respect maxStack.
            var rebuilt = new List<ItemStack>();
            foreach (var g in stacks.GroupBy(x => x.itemId))
            {
                var def = db.Item(g.Key);
                if (def.IsUnique) { foreach (var st in g) { st.count = 1; rebuilt.Add(st); } continue; }
                int total = g.Sum(x => x.count);
                while (total > 0) { int n = Math.Min(def.maxStack, total); rebuilt.Add(new ItemStack { uid = "", itemId = def.id, count = n }); total -= n; }
            }
            stacks.Clear();
            stacks.AddRange(rebuilt);
        }

        static string CheckEquip(GameState s, GameDatabase db, string uid, EquipSlot slot, List<string> repairs)
        {
            if (string.IsNullOrEmpty(uid)) return "";
            var st = s.inventory.FirstOrDefault(x => x.uid == uid);
            if (st == null || db.Item(st.itemId).slot != slot) { repairs.Add("unequipped missing " + slot); return ""; }
            return uid;
        }
    }

    public class SlotSummary
    {
        public int Slot;
        public bool Exists;
        public string Label = "", SavedAtUtc = "";
        public LoadStatus Status;
    }

    /// <summary>Slot files with atomic replace and one backup generation. Root directory is injectable so tests stay isolated.</summary>
    public class SaveStore
    {
        public readonly string Root;
        public SaveStore(string root) { Root = root; Directory.CreateDirectory(root); }

        public string PathFor(int slot) => Path.Combine(Root, "slot" + slot + ".json");
        public string BackupFor(int slot) => Path.Combine(Root, "slot" + slot + ".bak");
        string TempFor(int slot) => Path.Combine(Root, "slot" + slot + ".tmp");

        public void Write(int slot, string text)
        {
            string path = PathFor(slot), tmp = TempFor(slot), bak = BackupFor(slot);
            var bytes = new UTF8Encoding(false).GetBytes(text);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, bak, true); }
                catch (PlatformNotSupportedException) { File.Copy(path, bak, true); File.Delete(path); File.Move(tmp, path); }
            }
            else File.Move(tmp, path);
        }

        public void Save(int slot, GameState state)
        {
            state.savedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            Write(slot, SaveCodec.Encode(state));
        }

        public LoadResult Load(int slot, GameDatabase db)
        {
            string path = PathFor(slot), bak = BackupFor(slot);
            if (!File.Exists(path) && !File.Exists(bak)) return new LoadResult { Status = LoadStatus.NoSave, Message = "No saved game in this slot." };
            LoadResult main = null;
            if (File.Exists(path))
            {
                main = SaveCodec.Decode(SafeRead(path), db);
                if (main.Ok || main.Status == LoadStatus.TooNew) return main;
            }
            if (File.Exists(bak))
            {
                var backup = SaveCodec.Decode(SafeRead(bak), db);
                if (backup.Ok)
                {
                    if (File.Exists(path)) Quarantine(path);
                    backup.Status = LoadStatus.LoadedFromBackup;
                    backup.Message = "The latest save was damaged; the previous save was restored.";
                    return backup;
                }
            }
            if (File.Exists(path)) Quarantine(path);
            return main ?? new LoadResult { Status = LoadStatus.Corrupt, Message = "Save data is damaged." };
        }

        static string SafeRead(string path)
        {
            try { return File.ReadAllText(path, Encoding.UTF8); } catch (Exception) { return ""; }
        }

        /// <summary>Keeps a damaged file for inspection instead of deleting the player's data.</summary>
        void Quarantine(string path)
        {
            try { File.Copy(path, path + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), true); } catch (Exception) { }
        }

        public bool Exists(int slot) => File.Exists(PathFor(slot)) || File.Exists(BackupFor(slot));

        public void Delete(int slot)
        {
            foreach (var p in new[] { PathFor(slot), BackupFor(slot), TempFor(slot) }) if (File.Exists(p)) File.Delete(p);
        }

        public SlotSummary Summary(int slot, GameDatabase db)
        {
            var s = new SlotSummary { Slot = slot, Exists = Exists(slot) };
            if (!s.Exists) { s.Label = "Empty"; return s; }
            var r = Load(slot, db);
            s.Status = r.Status;
            if (!r.Ok) { s.Label = r.Message; return s; }
            var st = r.State;
            string area = db.Areas.TryGetValue(st.player.areaId ?? "", out var a) ? a.name : "";
            var t = TimeSpan.FromSeconds(st.playSeconds);
            s.Label = "Level " + st.player.level + "  ·  Day " + (st.world.Day + 1) + "  ·  " + area + "  ·  " + (int)t.TotalHours + "h " + t.Minutes.ToString("00") + "m";
            s.SavedAtUtc = st.savedAtUtc;
            return s;
        }

        /// <summary>Most recently written slot, or -1.</summary>
        public int MostRecent(params int[] slots)
        {
            int best = -1; DateTime bestTime = DateTime.MinValue;
            foreach (int slot in slots)
            {
                string p = File.Exists(PathFor(slot)) ? PathFor(slot) : File.Exists(BackupFor(slot)) ? BackupFor(slot) : null;
                if (p == null) continue;
                var t = File.GetLastWriteTimeUtc(p);
                if (t > bestTime) { bestTime = t; best = slot; }
            }
            return best;
        }
    }
}
