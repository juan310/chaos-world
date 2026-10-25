using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FH.Core;
using UnityEngine;

namespace FH.Game
{
    public enum SessionMode { Title, Playing, Transition }

    /// <summary>Connects one GameCore (rules + state) to the Unity world: spawning, interaction, world reactions,
    /// saving and loading. Rules stay in the core systems; this class translates between them and the scene.</summary>
    [DefaultExecutionOrder(-50)]
    public class GameSession : MonoBehaviour
    {
        public static GameSession I;
        public static bool ShowDamageNumbers = true;

        public WorldRefs World;
        public HunterController Hunter;
        public CameraRig CameraRig;
        public FH.UI.UiManager Ui;
        public FH.UI.Hud Hud => Ui ? Ui.Hud : null;

        public GameDatabase Db { get; private set; }
        public GameCore Core { get; private set; }
        public SaveStore Store { get; private set; }
        public SettingsState Settings { get; private set; } = new SettingsState();
        public SessionMode Mode { get; private set; } = SessionMode.Title;
        public int Slot { get; private set; } = 1;
        public string CurrentArea { get; private set; } = "home";
        public bool Automated { get; private set; }
        public bool Simulating => Mode == SessionMode.Playing && (Ui == null || !Ui.BlocksWorld);
        public Interactable Focus { get; private set; }
        public readonly List<MonsterActor> Monsters = new List<MonsterActor>();
        public readonly List<string> Log = new List<string>();
        public string SaveRoot => Store?.Root;
        public event Action<MonsterActor> MonsterKilledEvent;

        readonly List<LootBag> bags = new List<LootBag>();
        float spawnCheck, autosaveClock;
        bool wasSimulating;
        IRandom rng = new SeededRandom(Environment.TickCount);

        void Awake()
        {
            I = this;
            var args = Environment.GetCommandLineArgs();
            Automated = args.Any(a => a.StartsWith("--verify") || a.StartsWith("--tour"));
            string saveDir = Arg(args, "--save-dir") ?? Path.Combine(Application.persistentDataPath, "Saves");
            Store = new SaveStore(saveDir);
            Db = GameDatabase.FromJson(Resources.Load<TextAsset>("Data/content").text);
            foreach (var e in Db.Errors) Debug.LogError("Content: " + e);
            LoadSettings();
            MaterialLibrary.Register();
            Application.targetFrameRate = 60;
            // A placeholder core lets the title screen render the world before a game is chosen.
            Core = new GameCore(Db, GameCore.NewGameState(Db, "title"));
            Hunter.Initialize(this);
            CameraRig.Follow = Hunter.transform;
            Ui.Initialize(this);
            EnterTitle();
            if (args.Contains("--verify-rpg")) gameObject.AddComponent<GameVerifier>();
            if (args.Contains("--tour-rpg")) gameObject.AddComponent<GameTour>();
            if (args.Contains("--input-log")) gameObject.AddComponent<InputLogger>();
        }

        static string Arg(string[] args, string key)
        {
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        // ---------------- settings ----------------
        string SettingsPath => Path.Combine(Store.Root, "settings.json");

        void LoadSettings()
        {
            try { if (File.Exists(SettingsPath)) Settings = JsonMapper.To<SettingsState>(Json.Parse(File.ReadAllText(SettingsPath))); }
            catch (Exception e) { Debug.LogWarning("Settings unreadable, using defaults: " + e.Message); Settings = new SettingsState(); }
            ApplySettings();
        }

        public void ApplySettings()
        {
            AudioListener.volume = Mathf.Clamp01(Settings.masterVolume);
            InputRouter.MouseSensitivity = Mathf.Clamp(Settings.mouseSensitivity, .2f, 3);
            InputRouter.InvertY = Settings.invertY;
            ShowDamageNumbers = Settings.showDamageNumbers;
            if (!Automated && Screen.fullScreen != Settings.fullscreen) Screen.fullScreen = Settings.fullscreen;
            if (Ui) Ui.SetScale(Settings.uiScale);
        }

        public void SaveSettings()
        {
            ApplySettings();
            try { File.WriteAllText(SettingsPath, Json.Write(JsonMapper.ToTree(Settings))); }
            catch (Exception e) { Debug.LogWarning("Could not save settings: " + e.Message); }
        }

        // ---------------- lifecycle ----------------
        public void EnterTitle()
        {
            Mode = SessionMode.Title;
            ClearMonsters();
            // The title backdrop shows the valley as a new game starts it: modest cottage, everyone at home in Thornbury.
            Core = new GameCore(Db, GameCore.NewGameState(Db, "title"));
            World.Estate.Apply(0);
            foreach (var n in World.Npcs) n.PlaceFor(this);
            CameraRig.Manual = true;
            Ui.ShowTitle();
        }

        public void NewGame(int slot, string hunterName)
        {
            Slot = slot;
            var state = GameCore.NewGameState(Db, "slot" + slot + "-" + DateTime.UtcNow.Ticks);
            state.contentVersion = "1";
            if (!string.IsNullOrEmpty(hunterName)) state.player.name = hunterName;
            var home = World.Anchor("spawn_home");
            state.player.x = home.position.x; state.player.y = home.position.y; state.player.z = home.position.z; state.player.yaw = home.eulerAngles.y;
            Begin(state, true);
            Hud.Notify("Your cottage. Your hunt. Mira at Thornbury Crossing has work for you.", NoticeKind.Story);
            Save();
        }

        public LoadResult LoadSlot(int slot)
        {
            var r = Store.Load(slot, Db);
            if (!r.Ok) return r;
            Slot = slot;
            Begin(r.State, false);
            if (r.Status == LoadStatus.LoadedFromBackup) Hud.Notify(r.Message, NoticeKind.Warning);
            if (r.Repairs.Count > 0) Debug.Log("Save repaired: " + string.Join("; ", r.Repairs));
            return r;
        }

        public int MostRecentSlot() => Store.MostRecent(1, 2, 3);

        void Begin(GameState state, bool fresh)
        {
            ClearMonsters();
            Core = new GameCore(Db, state);
            Core.Conditions.CurrentArea = () => CurrentArea;
            WireEvents();
            Hunter.RefreshStats(true);
            var p = state.player;
            if (p.health > 0) Hunter.Health = Mathf.Min(p.health, Hunter.Stats[Stat.MaxHealth]);
            if (p.stamina >= 0) Hunter.Stamina.Current = Mathf.Min(p.stamina, Hunter.Stamina.Max);
            if (p.magic >= 0) Hunter.Magic = Mathf.Min(p.magic, Hunter.Stats[Stat.MaxMagic]);
            CurrentArea = p.areaId;
            Vector3 pos = new Vector3(p.x, p.y, p.z);
            if (Db.Areas.TryGetValue(CurrentArea, out var area) && area.kind == AreaKind.Dungeon && !fresh)
            {
                // Never resume inside an instance mid-fight: return to its entrance outside.
                var door = World.Anchor("crypt_outside");
                CurrentArea = area.returnArea; pos = door.position;
            }
            Hunter.Revive(SafeGround(pos), p.yaw);
            if (p.health > 0) Hunter.Health = Mathf.Min(p.health, Hunter.Stats[Stat.MaxHealth]);
            ApplyWorld();
            Mode = SessionMode.Playing;
            CameraRig.Manual = false;
            CameraRig.Snap();
            Ui.ShowGameplay();
            Core.AfterStateChange();
            Calm(2f);
        }

        void WireEvents()
        {
            var e = Core.Events;
            e.Notice += (t, k) => Hud?.Notify(t, k);
            e.EquipmentChanged += () => Hunter.RefreshStats();
            e.StatsChanged += () => Hunter.RefreshStats();
            e.EstateTierChanged += t => { World.Estate.Apply(t); RefreshInteractables(); };
            e.RelationshipChanged += npc => { foreach (var n in World.Npcs.Where(x => x.NpcId == npc)) n.PlaceFor(this); };
            e.DayChanged += d =>
            {
                Core.State.world.usedPickups.RemoveAll(k => k.StartsWith("regrow:"));
                Core.State.vendorSales.RemoveAll(v => v.day < Core.State.world.Day - 1);
                RefreshInteractables();
                Hud?.Notify("A new day dawns. Day " + (d + 1) + ".", NoticeKind.Info);
            };
            e.LevelUp += l => WorldKit.Sound(Hunter.transform.position, "chime", .4f);
        }

        void ApplyWorld()
        {
            World.Estate.Apply(Core.State.estate.tier);
            foreach (var n in World.Npcs) n.PlaceFor(this);
            RefreshInteractables();
            foreach (var b in bags) if (b) Destroy(b.gameObject);
            bags.Clear();
            foreach (var st in Core.State.world.lootBags) bags.Add(SpawnBag(st));
            Core.Spawns.ResetRespawned();
            RefreshSpawns(true);
            UpdateLighting();
        }

        public void RefreshInteractables()
        {
            foreach (var g in FindObjectsByType<GatherNode>(FindObjectsInactive.Include)) g.Refresh(this);
            foreach (var c in FindObjectsByType<SupplyChest>(FindObjectsInactive.Include)) c.Refresh(this);
        }

        // ---------------- saving ----------------
        public void CaptureState()
        {
            var s = Core.State;
            var p = s.player;
            var t = Hunter.transform;
            p.x = t.position.x; p.y = t.position.y; p.z = t.position.z; p.yaw = t.eulerAngles.y;
            p.health = Hunter.Health; p.stamina = Hunter.Stamina.Current; p.magic = Hunter.Magic;
            p.areaId = CurrentArea;
            s.world.lootBags = bags.Where(b => b && b.State.items.Count > 0).Select(b => b.State).ToList();
        }

        public bool Save(int? slot = null)
        {
            if (Mode == SessionMode.Title) return false;
            if (slot.HasValue) Slot = slot.Value;
            CaptureState();
            try
            {
                Store.Save(Slot, Core.State);
                Log.Add("Saved slot " + Slot + " at " + Core.Clock.Label);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("Save failed: " + e);
                Hud?.Notify("The game could not be saved: " + e.Message, NoticeKind.Warning);
                return false;
            }
        }

        public void QuitToTitle(bool save)
        {
            if (save) Save();
            EnterTitle();
        }

        public void QuitGame(bool save)
        {
            if (save && Mode == SessionMode.Playing) Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnApplicationQuit()
        {
            // Closing the window keeps progress, like any modern game; corruption is avoided by the atomic writer.
            if (Mode == SessionMode.Playing && !Automated) Save();
        }

        // ---------------- per-frame ----------------
        void Update()
        {
            if (Mode != SessionMode.Playing) return;
            bool sim = Simulating;
            if (sim && !wasSimulating) Calm(.8f);   // returning from a menu never lands the player in an unavoidable hit
            wasSimulating = sim;
            if (!sim) return;
            float dt = Time.deltaTime;
            Core.State.playSeconds += dt;
            Core.Clock.TickRealSeconds(dt);
            UpdateArea();
            UpdateLighting();
            UpdateFocus();
            if (InputRouter.Down(GameAction.Interact) && Focus) Focus.Use(this);
            spawnCheck -= dt;
            if (spawnCheck <= 0) { spawnCheck = 2; RefreshSpawns(false); }
            autosaveClock += dt;
            if (autosaveClock > 300 && !Hunter.Dead && !InFight) { autosaveClock = 0; Save(); }
        }

        void LateUpdate() => InputRouter.EndFrame();

        void UpdateFocus()
        {
            Focus = null;
            if (Hunter.Dead) return;
            var hp = Hunter.transform.position;
            float best = 999;
            foreach (var i in FindObjectsByType<Interactable>())
            {
                if (!i.Available(this)) continue;
                float d = Vector3.Distance(Flat(hp), Flat(i.Point));
                if (d > i.Range || Mathf.Abs(hp.y - i.Point.y) > 2.5f) continue;
                // People win ties: facing someone and pressing E should always start a conversation.
                float score = d - Vector3.Dot((i.Point - hp).normalized, Hunter.transform.forward) * .6f - (i is NpcActor ? .25f : 0);
                if (score < best) { best = score; Focus = i; }
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        void UpdateArea()
        {
            string area = AreaAt(Hunter.transform.position);
            if (area == CurrentArea) return;
            CurrentArea = area;
            if (Db.Areas.TryGetValue(area, out var def)) Hud?.ShowArea(def.name);
            Core.Events.RaiseArea(area);
        }

        public string AreaAt(Vector3 p)
        {
            if (World.IsInCrypt(p)) return "crypt";
            string best = "old_bridge"; float bestD = 999;
            foreach (var a in Db.Areas.Values.Where(a => a.kind != AreaKind.Dungeon))
            {
                float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(a.x, a.z));
                if (d <= a.radius && d / a.radius < bestD) { bestD = d / a.radius; best = a.id; }
            }
            return best;
        }

        public bool PlayerInSafeArea(MonsterDef def)
        {
            return Db.Areas.TryGetValue(CurrentArea, out var a) && a.safe;
        }

        void UpdateLighting()
        {
            if (!World.Sun) return;
            float h = (float)Core.Clock.Hour;
            // Full daylight from about 08:00 to 18:00, warm dawn and dusk ramps, dim blue night.
            float day = Mathf.SmoothStep(0, 1, Mathf.Clamp01(Mathf.Min((h - 5f) / 3f, (21f - h) / 3f)));
            float arc = Mathf.Clamp01((h - 5f) / 16f);
            World.Sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(8, 50, Mathf.Sin(arc * Mathf.PI)), -110 + arc * 150, 0);
            World.Sun.intensity = Mathf.Lerp(.12f, 1.15f, day);
            World.Sun.color = Color.Lerp(new Color(.55f, .62f, .95f), new Color(1, .88f, .69f), day);
            float amb = Mathf.Lerp(.32f, 1, day);
            bool below = World.IsInCrypt(Hunter.transform.position);
            if (below) amb = .75f;
            RenderSettings.ambientSkyColor = new Color(.62f, .69f, .70f) * amb;
            RenderSettings.ambientEquatorColor = new Color(.40f, .45f, .34f) * amb;
            RenderSettings.ambientGroundColor = new Color(.23f, .24f, .20f) * amb;
            RenderSettings.fogColor = below ? new Color(.05f, .05f, .06f) : new Color(.63f, .70f, .62f) * Mathf.Lerp(.3f, 1, day);
            foreach (var l in World.Lanterns) if (l) l.intensity = Mathf.Lerp(3.2f, .9f, day);
        }

        // ---------------- monsters ----------------
        void ClearMonsters()
        {
            foreach (var m in Monsters) if (m) Destroy(m.gameObject);
            Monsters.Clear();
        }

        void RefreshSpawns(bool initial)
        {
            foreach (var sp in Db.Spawns.Values)
            {
                var existing = Monsters.FirstOrDefault(m => m && m.SpawnId == sp.id);
                bool alive = Core.Spawns.ShouldBeAlive(sp);
                Vector3 pos = World.SpawnPosition(sp);
                float playerDist = Vector3.Distance(Hunter.transform.position, pos);
                if (existing && !existing.Alive)
                {
                    if (alive && playerDist > 25) { Monsters.Remove(existing); Destroy(existing.gameObject); existing = null; }
                    else continue;
                }
                if (alive && !existing && (initial || playerDist > 25)) Monsters.Add(SpawnMonster(sp, pos));
                else if (!alive && existing && existing.Alive && !existing.Brain.Engaged && playerDist > 25) { Monsters.Remove(existing); Destroy(existing.gameObject); }
            }
        }

        MonsterActor SpawnMonster(SpawnDef sp, Vector3 pos)
        {
            var def = Db.Monsters[sp.monsterId];
            var go = CharacterFactory.Actor(def.name, SafeGround(pos) + Vector3.up * .05f, def.height * .95f, .34f * def.height / 1.8f);
            go.transform.rotation = Quaternion.Euler(0, rng.Range(0, 359), 0);
            var actor = go.AddComponent<MonsterActor>();
            actor.SpawnId = sp.id; actor.AreaId = sp.areaId; actor.PatrolRadius = sp.patrolRadius;
            actor.Visual = CharacterFactory.Create(def.model, go.transform, def.height);
            var st = Core.State.world.spawns.FirstOrDefault(x => x.spawnId == sp.id);
            actor.Initialize(this, def, st?.brokenParts);
            return actor;
        }

        /// <summary>True while an engaged monster is close enough to matter (saving is refused mid-fight).</summary>
        public bool InFight => Monsters.Any(m => m && m.Alive && m.Brain.Engaged && Vector3.Distance(m.transform.position, Hunter.transform.position) < 25);

        public MonsterActor SpawnForTest(string monsterId, Vector3 pos)
        {
            var sp = new SpawnDef { id = "test_" + Monsters.Count, monsterId = monsterId, areaId = CurrentArea, x = pos.x, z = pos.z, respawnHours = -1, patrolRadius = 1 };
            var m = SpawnMonster(sp, pos);
            Monsters.Add(m);
            return m;
        }

        public void Calm(float seconds) { foreach (var m in Monsters) if (m && m.Alive) m.Calm(seconds); }

        public MonsterActor NearestMonster(Vector3 from, float range, Vector3 forward, float maxAngle)
        {
            MonsterActor best = null; float bestScore = 1e9f;
            foreach (var m in Monsters)
            {
                if (!m || !m.Alive) continue;
                var d = m.transform.position - from; d.y = 0;
                if (d.magnitude > range) continue;
                float angle = forward.sqrMagnitude > .01f ? Vector3.Angle(forward, d) : 0;
                if (angle > maxAngle) continue;
                float score = d.magnitude + angle * .05f;
                if (score < bestScore) { bestScore = score; best = m; }
            }
            return best;
        }

        public void OnMonsterHit(MonsterActor m, HitInfo hit, MonsterPartDef part) { }

        public void OnPartBroken(MonsterActor m, MonsterPartDef part)
        {
            Core.State.Spawn(m.SpawnId).brokenParts.Add(part.id);
            Hud?.Notify(m.DisplayName + ": " + part.name + " broken!", NoticeKind.Progress);
            var drops = Core.Loot.Roll(part.breakLootTable, rng);
            if (drops.Count > 0) DropLoot(m.transform.position, drops, part.name + " fragments");   // the monster's feet are always reachable
            Core.Events.RaisePartBroken(m.MonsterId, part.id);
            WorldKit.Sound(m.transform.position, "hit", .6f);
            CameraRig.Shake(.35f);
        }

        public void OnMonsterKilled(MonsterActor m)
        {
            var def = m.Def;
            if (!m.SpawnId.StartsWith("test_")) Core.Spawns.MarkKilled(m.SpawnId);
            Core.Wallet.Earn(def.gold);
            Core.Progression.AddXp(def.xp);
            var drops = Core.Loot.Roll(def.lootTable, rng);
            if (drops.Count > 0) DropLoot(m.transform.position, drops, def.name + " remains");
            Hud?.Notify(def.name + " defeated. +" + def.gold + " gold, +" + def.xp + " XP", NoticeKind.Reward);
            if (Hunter.LockTarget == m) Hunter.LockTarget = null;
            Core.Events.RaiseKilled(def.id, m.SpawnId);
            MonsterKilledEvent?.Invoke(m);
        }

        public LootBag DropLoot(Vector3 at, List<ItemAmount> items, string label)
        {
            // One sack per kill site: drops landing next to an existing sack are added to it.
            var near = bags.FirstOrDefault(b => b && b.State.areaId == CurrentArea && Vector3.Distance(b.transform.position, at) < 2.5f);
            if (near)
            {
                foreach (var it in items)
                {
                    var same = near.State.items.FirstOrDefault(x => x.itemId == it.itemId);
                    if (same != null) same.count += it.count; else near.State.items.Add(new ItemAmount { itemId = it.itemId, count = it.count });
                }
                return near;
            }
            var st = new LootBagState { id = "bag" + Environment.TickCount + bags.Count, areaId = CurrentArea, label = label, x = at.x, y = at.y, z = at.z, items = items };
            var bag = SpawnBag(st);
            bags.Add(bag);
            return bag;
        }

        LootBag SpawnBag(LootBagState st)
        {
            var p = SafeGround(new Vector3(st.x, st.y, st.z));
            var go = new GameObject("Loot - " + st.label);
            go.transform.position = p;
            var sack = WorldKit.Orb("Sack", p + Vector3.up * .25f, .25f, WorldKit.Mat("Loot sack", new Color(.55f, .42f, .25f)), go.transform);
            sack.transform.localScale = new Vector3(.5f, .42f, .5f);
            WorldKit.Orb("Glint", p + Vector3.up * .55f, .07f, WorldKit.Glow(new Color(1f, .85f, .3f), 3), go.transform);
            var light = new GameObject("Loot glow").AddComponent<Light>();
            light.transform.SetParent(go.transform, false); light.transform.localPosition = Vector3.up * .7f;
            light.color = new Color(1f, .8f, .35f); light.range = 2.5f; light.intensity = 1.4f;
            var bag = go.AddComponent<LootBag>();
            bag.State = st; bag.Id = st.id; bag.Range = 2.0f;
            return bag;
        }

        public void RemoveBag(LootBag bag)
        {
            bags.Remove(bag);
            Destroy(bag.gameObject);
        }

        // ---------------- player events ----------------
        public void OnPlayerDefeated()
        {
            int lost = Core.Wallet.LoseFraction(Db.Balance.deathGoldLossFraction);
            Core.State.player.deaths++;
            Core.Clock.Advance(6);
            foreach (var m in Monsters.Where(m => m && m.Alive)) m.ResetToHome();
            var home = World.Anchor("spawn_home");
            CurrentArea = "home";
            Hunter.Revive(SafeGround(home.position), home.eulerAngles.y);
            CameraRig.Snap();
            Calm(3);
            Ui.ShowRecovery(lost);
        }

        public void Sleep()
        {
            Core.Clock.SleepUntilMorning();
            Hunter.RefreshStats(true);
            Calm(2);
            Hud?.Notify("You wake rested. " + Core.Clock.Label, NoticeKind.Info);
            if (Core.Estate.PendingIncome() > 0) Hud?.Notify("Rent is waiting in the estate ledger.", NoticeKind.Reward);
            Save();
            Hud?.Notify("Game saved.", NoticeKind.Info);
        }

        public void Travel(AreaDoor door)
        {
            if (Db.Areas.TryGetValue(door.TargetArea, out var area))
            {
                var gate = Core.Conditions.Explain(area.entryRequirements);
                if (!gate.Ok) { Hud.Flash(gate.Reason); return; }
            }
            Ui.Fade(() =>
            {
                Hunter.Teleport(SafeGround(door.Destination.position), door.Destination.eulerAngles.y);
                CameraRig.Yaw = door.Destination.eulerAngles.y;   // behind the hunter, looking where they face
                CameraRig.Snap();
                UpdateArea();
                RefreshSpawns(true);
                Calm(2.5f);
                Save();
            });
        }

        // ---------------- world queries ----------------
        public float GroundHeight(Vector3 p)
        {
            if (Physics.Raycast(new Vector3(p.x, p.y + 3, p.z), Vector3.down, out var hit, 12, Layers.WorldMask, QueryTriggerInteraction.Ignore)) return hit.point.y;
            return World.IsInCrypt(p) ? World.CryptFloor : WorldBuilder.Height(p.x, p.z);
        }

        public Vector3 SafeGround(Vector3 p) => new Vector3(p.x, GroundHeight(p) + .08f, p.z);

        /// <summary>Overworld edges and the river (crossed only on the bridge) constrain player movement.</summary>
        public Vector3 ConstrainMove(Vector3 from, Vector3 to)
        {
            if (World.IsInCrypt(from)) return to;
            to.x = Mathf.Clamp(to.x, -43, 43);
            to.z = Mathf.Clamp(to.z, -41, 41);
            if (Mathf.Abs(to.z - WorldBuilder.RiverZ(to.x)) < 1.4f && Mathf.Abs(to.x - 3) > 1.25f) return from;
            return to;
        }
    }
}
