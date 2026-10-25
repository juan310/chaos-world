using System.IO;
using System.Linq;
using FH.Core;
using NUnit.Framework;

namespace FH.Tests
{
    public class ContractTests
    {
        [Test]
        public void StarterContractTracksKillsAndOnlyLootedBones()
        {
            var core = TestSupport.NewCore();
            Assert.That(core.Contracts.Get("c_gravebound_cull").status, Is.EqualTo(ContractStatus.Available));
            Assert.That(core.Contracts.Accept("c_gravebound_cull").Ok);
            core.Vendors.Buy("v_bram", "iron_nails", 1);
            core.Inventory.Add("bone_shard", 3, ItemSource.Purchase);
            var cs = core.Contracts.Get("c_gravebound_cull");
            Assert.That(cs.progress[1], Is.EqualTo(0), "bought or gifted bones do not count");
            for (int i = 0; i < 3; i++) TestSupport.KillAndLoot(core, "gravebound", "sp_bridge_1", new MaxRandom());
            Assert.That(cs.progress[0], Is.EqualTo(3));
            Assert.That(cs.progress[1], Is.EqualTo(3), "progress is capped at the objective count");
            Assert.That(cs.status, Is.EqualTo(ContractStatus.Completed));
        }

        [Test]
        public void RewardIsGrantedExactlyOnceEvenAcrossSaveAndLoad()
        {
            var core = TestSupport.NewCore();
            core.Contracts.Accept("c_gravebound_cull");
            for (int i = 0; i < 3; i++) TestSupport.KillAndLoot(core, "gravebound", "sp_bridge_1", new MaxRandom());
            int gold = core.Wallet.Gold;
            Assert.That(core.Contracts.Claim("c_gravebound_cull").Ok);
            Assert.That(core.Wallet.Gold, Is.EqualTo(gold + 60));
            Assert.That(core.Contracts.Claim("c_gravebound_cull").Ok, Is.False);
            // Even if the status were tampered back to Completed, the reward key guard holds.
            var reloaded = SaveCodec.Decode(SaveCodec.Encode(core.State), core.Db).State;
            var core2 = new GameCore(core.Db, reloaded);
            core2.Contracts.Get("c_gravebound_cull").status = ContractStatus.Completed;
            core2.Contracts.Get("c_gravebound_cull").timesClaimed = 0;
            var again = core2.Contracts.Claim("c_gravebound_cull");
            Assert.That(again.Ok, Is.False);
            Assert.That(again.Reason, Does.Contain("already been claimed"));
            Assert.That(core2.Wallet.Gold, Is.EqualTo(gold + 60));
        }

        [Test]
        public void ClaimWithFullPackFailsWithoutSideEffects()
        {
            var core = TestSupport.NewCore();
            core.Contracts.Accept("c_gravebound_cull");
            for (int i = 0; i < 3; i++) TestSupport.KillAndLoot(core, "gravebound", "sp_bridge_1", new MaxRandom());
            core.Inventory.Remove("potion_mooncap", core.Inventory.Count("potion_mooncap"));
            while (core.Inventory.Bag.FreeSlots > 0) core.Inventory.Add("padded_jerkin", 1, ItemSource.System);
            int gold = core.Wallet.Gold;
            var r = core.Contracts.Claim("c_gravebound_cull");
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Reason, Does.Contain("Make room"));
            Assert.That(core.Wallet.Gold, Is.EqualTo(gold));
            Assert.That(core.Contracts.Get("c_gravebound_cull").status, Is.EqualTo(ContractStatus.Completed));
            Assert.That(core.State.world.claimedRewards, Is.Empty);
        }

        [Test]
        public void RankAndPrerequisitesGateHigherContracts()
        {
            var core = TestSupport.NewCore();
            var warden = core.Contracts.CanAccept("c_grave_warden");
            Assert.That(warden.Ok, Is.False);
            Assert.That(warden.Reason, Does.Contain("Bronze"));
            Assert.That(core.Contracts.CanAccept("c_werewolf").Reason, Does.Contain("Rank 3"));
            TestSupport.SetRank(core, 3);
            Assert.That(core.Contracts.CanAccept("c_werewolf").Reason, Does.Contain("not been sighted"));
        }

        [Test]
        public void RepeatableContractReopensAfterCooldownDay()
        {
            var core = TestSupport.NewCore();
            TestSupport.SetRank(core, 1);
            Assert.That(core.Contracts.Accept("c_bone_harvest").Ok);
            core.Inventory.Add("bone_shard", 6, ItemSource.Loot);
            Assert.That(core.Contracts.Claim("c_bone_harvest").Ok);
            Assert.That(core.Contracts.CanAccept("c_bone_harvest").Reason, Does.Contain("1 day"));
            core.Clock.SleepUntilMorning();
            Assert.That(core.Contracts.Get("c_bone_harvest").status, Is.EqualTo(ContractStatus.Available));
            Assert.That(core.Contracts.Accept("c_bone_harvest").Ok);
            Assert.That(core.Contracts.Get("c_bone_harvest").progress.Sum(), Is.EqualTo(0));
        }

        [Test]
        public void StoryQuestAdvancesThroughConnectedSystems()
        {
            var core = TestSupport.NewCore();
            var q = core.State.Quest("q_rise");
            Assert.That(q.status, Is.EqualTo(QuestStatus.Active));
            new DialogueRunner(core, "d_mira", "mira");
            Assert.That(q.stage, Is.EqualTo(1));
            core.Contracts.Accept("c_gravebound_cull");
            for (int i = 0; i < 3; i++) TestSupport.KillAndLoot(core, "gravebound", "sp_bridge_1", new MaxRandom());
            Assert.That(q.stage, Is.EqualTo(2));
            core.Contracts.Claim("c_gravebound_cull");
            core.Crafting.Craft("r_gravebane", Station.Forge);
            Assert.That(q.stage, Is.EqualTo(3));
        }
    }

    public class EstateTests
    {
        static GameCore ReadyToUpgrade()
        {
            var core = TestSupport.NewCore();
            TestSupport.SetRank(core, 1);
            core.State.gold = 200;
            core.Vendors.Buy("v_bram", "oak_timber", 3);
            core.Vendors.Buy("v_bram", "iron_nails", 6);
            return core;
        }

        [Test]
        public void UpgradeExplainsMissingRequirementsAndSpendsExactly()
        {
            var core = TestSupport.NewCore();
            Assert.That(core.Estate.CanUpgrade().Reason, Does.Contain("Bronze"));
            TestSupport.SetRank(core, 1);
            Assert.That(core.Estate.CanUpgrade().Reason, Does.Contain("Oak Timber"));
            core = ReadyToUpgrade();
            int gold = core.Wallet.Gold;
            int cap = core.Inventory.Bag.Capacity;
            Assert.That(core.Estate.Upgrade().Ok);
            Assert.That(core.State.estate.tier, Is.EqualTo(1));
            Assert.That(core.Wallet.Gold, Is.EqualTo(gold - 75));
            Assert.That(core.Inventory.Count("oak_timber"), Is.EqualTo(0));
            Assert.That(core.Inventory.Bag.Capacity, Is.EqualTo(cap + 4));
            Assert.That(core.Estate.Storage.Capacity, Is.EqualTo(16));
            Assert.That(core.Estate.HasStation(Station.Workbench));
        }

        [Test]
        public void UnmodeledTiersAreHooksNotPurchases()
        {
            var core = ReadyToUpgrade();
            core.Estate.Upgrade();
            TestSupport.SetRank(core, 2);
            core.State.gold = 99999;
            var r = core.Estate.CanUpgrade();
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Reason, Does.Contain("not been drawn up"));
        }

        [Test]
        public void IncomeUsesGameDaysCapsAndCannotBeCollectedTwice()
        {
            var core = ReadyToUpgrade();
            core.Estate.Upgrade();
            Assert.That(core.Estate.CollectIncome().Ok, Is.False, "nothing accrues on the upgrade day");
            core.Clock.SleepUntilMorning();
            int gold = core.Wallet.Gold;
            Assert.That(core.Estate.CollectIncome().Ok);
            Assert.That(core.Wallet.Gold, Is.EqualTo(gold + 15));
            Assert.That(core.Estate.CollectIncome().Ok, Is.False);
            core.Clock.Advance(24 * 10);
            gold = core.Wallet.Gold;
            core.Estate.CollectIncome();
            Assert.That(core.Wallet.Gold, Is.EqualTo(gold + 45), "capped at 3 days");
            // Save, "close the game" for any length of real time, load: no new income.
            var reloaded = new GameCore(core.Db, SaveCodec.Decode(SaveCodec.Encode(core.State), core.Db).State);
            Assert.That(reloaded.Estate.PendingIncome(), Is.EqualTo(0));
        }

        [Test]
        public void StorageMovesItemsBothWaysWithinLimits()
        {
            var core = ReadyToUpgrade();
            Assert.That(core.Estate.Deposit(core.State.inventory.First(s => s.itemId == "potion_mooncap"), 1).Reason, Does.Contain("no storage"));
            core.Estate.Upgrade();
            core.Inventory.Add("bone_shard", 10, ItemSource.Loot);
            var stack = core.State.inventory.First(s => s.itemId == "bone_shard");
            Assert.That(core.Estate.Deposit(stack, 4).Ok);
            Assert.That(core.Inventory.Count("bone_shard"), Is.EqualTo(6));
            Assert.That(core.Estate.Storage.Count("bone_shard"), Is.EqualTo(4));
            Assert.That(core.Estate.Deposit(core.Inventory.Equipped(EquipSlot.Weapon), 1).Ok, Is.False);
            Assert.That(core.Estate.Withdraw(core.Estate.Storage.Stacks[0], 4).Ok);
            Assert.That(core.Inventory.Count("bone_shard"), Is.EqualTo(10));
        }
    }

    public class SocialTests
    {
        [Test]
        public void GiftsOncePerDayAndStagesAdvanceAutomaticallyUpToFriend()
        {
            var core = TestSupport.NewCore();
            core.Inventory.Add("moonpetal", 5, ItemSource.Gather);
            Assert.That(core.Relationships.Gift("mira", "moonpetal").Ok);
            Assert.That(core.Relationships.Get("mira").stage, Is.EqualTo("acquaintance"));
            Assert.That(core.Relationships.Gift("mira", "moonpetal").Reason, Does.Contain("today"));
            core.Clock.SleepUntilMorning();
            core.Relationships.Gift("mira", "moonpetal");
            Assert.That(core.Relationships.Get("mira").stage, Is.EqualTo("friend"));
            core.Clock.SleepUntilMorning(); core.Relationships.Gift("mira", "moonpetal");
            core.Clock.SleepUntilMorning(); core.Relationships.Gift("mira", "moonpetal");
            Assert.That(core.Relationships.Get("mira").stage, Is.EqualTo("friend"), "courting needs the player to ask");
        }

        [Test]
        public void CourtshipToMarriageRequiresDialogueRingAndHome()
        {
            var core = TestSupport.NewCore();
            var rel = core.Relationships.Get("mira");
            rel.affinity = 40; rel.stage = "friend";
            var d = new DialogueRunner(core, "d_mira", "mira");
            d.Choose(1); // first meeting: lore
            d = new DialogueRunner(core, "d_mira", "mira");
            int ask = d.Choices().FindIndex(c => c.Choice.text.StartsWith("Would you walk"));
            Assert.That(ask, Is.GreaterThanOrEqualTo(0));
            Assert.That(d.Choose(ask).Ok);
            Assert.That(rel.stage, Is.EqualTo("courting"));
            d = new DialogueRunner(core, "d_mira", "mira");
            var propose = d.Choices().First(c => c.Choice.text.Contains("marry me?"));
            Assert.That(propose.Available, Is.False);
            Assert.That(propose.Reason, Does.Contain("know you better").Or.Contain("Promise Ring"));
            rel.affinity = 50;
            core.Inventory.Add("promise_ring", 1, ItemSource.Craft);
            propose = new DialogueRunner(core, "d_mira", "mira").Choices().First(c => c.Choice.text.Contains("marry me?"));
            Assert.That(propose.Reason, Does.Contain("restore your cottage"));
            core.State.estate.tier = 1;
            d = new DialogueRunner(core, "d_mira", "mira");
            Assert.That(d.Choose(d.Choices().FindIndex(c => c.Choice.text.Contains("marry me?"))).Ok);
            Assert.That(rel.stage, Is.EqualTo("engaged"));
            Assert.That(core.Inventory.Count("promise_ring"), Is.EqualTo(0), "the ring was given");
            d = new DialogueRunner(core, "d_mira", "mira");
            Assert.That(d.Choose(d.Choices().FindIndex(c => c.Choice.text.StartsWith("Let's not wait"))).Ok);
            Assert.That(core.Relationships.IsSpouse("mira"));
            Assert.That(new DialogueRunner(core, "d_mira", "mira").Node.id, Is.EqualTo("mira_spouse"));
        }

        [Test]
        public void OnlyOneRomanceAndNonRomanceableNpcsDecline()
        {
            var core = TestSupport.NewCore();
            core.Relationships.Get("bram").affinity = 60; core.Relationships.Get("bram").stage = "friend";
            Assert.That(core.Relationships.CanAdvance("bram", "courting").Reason, Does.Contain("friendship"));
            core.Relationships.Get("mira").stage = "courting";
            core.Relationships.Get("lady_elowen").affinity = 60; core.Relationships.Get("lady_elowen").stage = "friend";
            Assert.That(core.Relationships.CanAdvance("lady_elowen", "courting").Ok, Is.False);
        }

        [Test]
        public void DialogueExplainsDisabledChoicesAndRefusesThem()
        {
            var core = TestSupport.NewCore();
            core.State.SetFlag("met_mira");
            var d = new DialogueRunner(core, "d_elowen", "lady_elowen");
            var court = d.Choices()[0];
            Assert.That(court.Available, Is.False);
            Assert.That(court.Reason, Does.Contain("Rank 2"));
            Assert.That(d.Choose(0).Ok, Is.False);
            Assert.That(core.State.HasFlag("court_access"), Is.False);
        }
    }

    public class SaveTests
    {
        static GameCore Played()
        {
            var core = TestSupport.NewCore();
            core.Contracts.Accept("c_gravebound_cull");
            for (int i = 0; i < 3; i++) TestSupport.KillAndLoot(core, "gravebound", "sp_bridge_" + (i % 2 + 1), new MaxRandom());
            core.Contracts.Claim("c_gravebound_cull");
            core.Relationships.ChangeAffinity("mira", 7);
            core.State.player.x = 3.5f; core.State.player.areaId = "ashen_fields";
            core.Clock.Advance(30);
            return core;
        }

        [Test]
        public void RoundTripPreservesEveryField()
        {
            var core = Played();
            string a = SaveCodec.Encode(core.State);
            var r = SaveCodec.Decode(a, core.Db);
            Assert.That(r.Status, Is.EqualTo(LoadStatus.Loaded));
            Assert.That(r.Repairs, Is.Empty, string.Join(",", r.Repairs));
            Assert.That(SaveCodec.Encode(r.State), Is.EqualTo(a));
            Assert.That(r.State.gold, Is.EqualTo(core.State.gold));
            Assert.That(r.State.Contract("c_gravebound_cull").status, Is.EqualTo(ContractStatus.Claimed));
            Assert.That(r.State.Relationship("mira").affinity, Is.EqualTo(core.State.Relationship("mira").affinity));
        }

        [Test]
        public void AtomicWriteKeepsBackupAndRecoversFromCorruption()
        {
            var dir = TestSupport.TempDir();
            try
            {
                var db = TestSupport.Db();
                var store = new SaveStore(dir);
                var core = Played();
                core.State.gold = 111; store.Save(1, core.State);
                core.State.gold = 222; store.Save(1, core.State);
                Assert.That(File.Exists(store.BackupFor(1)));
                Assert.That(Directory.GetFiles(dir, "*.tmp"), Is.Empty);
                Assert.That(store.Load(1, db).State.gold, Is.EqualTo(222));
                File.WriteAllText(store.PathFor(1), "{ \"schemaVersion\": 3, \"gold\": ");
                var r = store.Load(1, db);
                Assert.That(r.Status, Is.EqualTo(LoadStatus.LoadedFromBackup));
                Assert.That(r.State.gold, Is.EqualTo(111));
                Assert.That(Directory.GetFiles(dir, "*.damaged-*"), Is.Not.Empty, "damaged file kept for inspection");
                File.WriteAllText(store.BackupFor(1), "garbage");
                File.WriteAllText(store.PathFor(1), "\0\0\0");
                var bad = store.Load(1, db);
                Assert.That(bad.Status, Is.EqualTo(LoadStatus.Corrupt));
                Assert.That(bad.Message, Is.Not.Empty);
                Assert.That(store.Load(2, db).Status, Is.EqualTo(LoadStatus.NoSave));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void NewerSchemaIsRefusedNotMangled()
        {
            var r = SaveCodec.Decode("{ \"schemaVersion\": 99 }", TestSupport.Db());
            Assert.That(r.Status, Is.EqualTo(LoadStatus.TooNew));
        }

        [Test]
        public void SampleEraV1SaveMigratesToCurrentSchema()
        {
            var db = TestSupport.Db();
            var r = SaveCodec.Decode("{ \"gold\": 140, \"potions\": 4, \"herbs\": 2, \"kills\": 5, \"upgraded\": true, \"contractPaid\": true, \"health\": 80 }", db);
            Assert.That(r.Ok, r.Message);
            Assert.That(r.MigratedFrom, Is.EqualTo(1));
            var s = r.State;
            Assert.That(s.schemaVersion, Is.EqualTo(GameState.CurrentSchema));
            Assert.That(s.gold, Is.EqualTo(140));
            Assert.That(s.estate.tier, Is.EqualTo(1));
            Assert.That(s.inventory.First(x => x.itemId == "potion_mooncap").count, Is.EqualTo(4));
            Assert.That(s.Contract("c_gravebound_cull").status, Is.EqualTo(ContractStatus.Claimed));
            Assert.That(s.world.claimedRewards, Does.Contain("contract:c_gravebound_cull:0"));
            var core = new GameCore(db, s);
            Assert.That(core.Contracts.Claim("c_gravebound_cull").Ok, Is.False, "migrated reward cannot be claimed again");
        }

        [Test]
        public void SanitizeRepairsTamperedOrStaleData()
        {
            var db = TestSupport.Db();
            var core = Played();
            core.State.gold = -50;
            core.State.inventory.Add(new ItemStack { itemId = "removed_item", count = 3 });
            core.State.inventory.Add(new ItemStack { itemId = "bone_shard", count = -4 });
            core.State.inventory.Add(new ItemStack { itemId = "grave_dust", count = 300 });
            core.State.equipment.head = "u9999";
            core.State.estate.tier = 42;
            var r = SaveCodec.Decode(SaveCodec.Encode(core.State), db);
            Assert.That(r.Ok);
            Assert.That(r.State.gold, Is.EqualTo(0));
            Assert.That(r.State.inventory.Any(x => x.itemId == "removed_item"), Is.False);
            Assert.That(r.State.inventory.All(x => x.count > 0 && x.count <= db.Item(x.itemId).maxStack));
            Assert.That(r.State.inventory.Where(x => x.itemId == "grave_dust").Sum(x => x.count), Is.GreaterThanOrEqualTo(300));
            Assert.That(r.State.equipment.head, Is.Empty);
            Assert.That(r.State.estate.tier, Is.EqualTo(db.EstateTiers.Count - 1));
            Assert.That(r.Repairs, Is.Not.Empty);
        }
    }

    public class WorldTests
    {
        [Test]
        public void KilledSpawnsRespawnAfterTheirHoursAndNightSpawnsWaitForDark()
        {
            var core = TestSupport.NewCore();
            var sp = core.Db.Spawns["sp_bridge_1"];
            Assert.That(core.Spawns.ShouldBeAlive(sp));
            core.Spawns.MarkKilled("sp_bridge_1");
            Assert.That(core.Spawns.ShouldBeAlive(sp), Is.False);
            core.Clock.Advance(11.9);
            Assert.That(core.Spawns.ShouldBeAlive(sp), Is.False);
            core.Clock.Advance(0.2);
            Assert.That(core.Spawns.ShouldBeAlive(sp));
            var night = core.Db.Spawns["sp_night_1"];
            Assert.That(core.Spawns.ShouldBeAlive(night), Is.False, "starts at 07:00");
            core.Clock.Advance((21 - core.Clock.Hour + 24) % 24);
            Assert.That(core.Clock.Hour, Is.EqualTo(21).Within(1e-6));
            Assert.That(core.Spawns.ShouldBeAlive(night), Is.True, "21:00");
        }

        [Test]
        public void ClockOnlyMovesForwardAndSleepEndsAtMorning()
        {
            var core = TestSupport.NewCore();
            double h = core.State.world.hours;
            core.Clock.Advance(-5);
            core.Clock.Advance(double.NaN);
            Assert.That(core.State.world.hours, Is.EqualTo(h));
            core.Clock.Advance(10);
            core.Clock.SleepUntilMorning();
            Assert.That(core.Clock.Hour, Is.EqualTo(7).Within(1e-6));
            Assert.That(core.Clock.Day, Is.EqualTo(1));
        }
    }

    public class StarterLoopTests
    {
        [Test]
        public void FullStarterLoopSurvivesSaveQuitContinue()
        {
            var dir = TestSupport.TempDir();
            try
            {
                var db = TestSupport.Db();
                var core = TestSupport.NewCore(db);
                // Accept the hunt from Mira through dialogue.
                var d = new DialogueRunner(core, "d_mira", "mira");
                d.Choose(0);
                Assert.That(d.Choose(0).Ok, "accept contract");
                Assert.That(core.Contracts.Get("c_gravebound_cull").status, Is.EqualTo(ContractStatus.Active));
                // Fight and loot.
                for (int i = 0; i < 3; i++) TestSupport.KillAndLoot(core, "gravebound", "sp_bridge_" + (i + 1 > 2 ? 1 : i + 1), new MaxRandom());
                Assert.That(core.Inventory.Count("bone_shard"), Is.EqualTo(9));
                // Claim from Mira.
                d = new DialogueRunner(core, "d_mira", "mira");
                int claim = d.Choices().FindIndex(c => c.Choice.text.StartsWith("The bridge is clear"));
                Assert.That(d.Choose(claim).Ok);
                Assert.That(core.Progression.Rank.rank, Is.EqualTo(1));
                // Forge the blade.
                Assert.That(core.Crafting.Craft("r_gravebane", Station.Forge).Ok);
                // Buy materials and restore the cottage.
                Assert.That(core.Vendors.Buy("v_bram", "oak_timber", 3).Ok);
                Assert.That(core.Vendors.Buy("v_bram", "iron_nails", 4).Ok);
                var up = core.Estate.Upgrade();
                Assert.That(up.Ok, up.Reason);
                Assert.That(core.State.Quest("q_rise").stage, Is.EqualTo(4));
                // Save, quit, continue.
                var store = new SaveStore(dir);
                core.State.playSeconds = 1234;
                store.Save(1, core.State);
                var loaded = store.Load(store.MostRecent(1, 2, 3), db);
                Assert.That(loaded.Status, Is.EqualTo(LoadStatus.Loaded));
                var c2 = new GameCore(db, loaded.State);
                Assert.That(c2.Stats.EquippedWeapon().id, Is.EqualTo("hunter_blade_grave"));
                Assert.That(c2.State.estate.tier, Is.EqualTo(1));
                Assert.That(c2.State.gold, Is.EqualTo(core.State.gold));
                Assert.That(c2.Contracts.Get("c_gravebound_cull").status, Is.EqualTo(ContractStatus.Claimed));
                Assert.That(c2.Contracts.Get("c_grave_warden").status, Is.EqualTo(ContractStatus.Available));
                Assert.That(c2.Crafting.Known("r_bone_vest"));
                Assert.That(store.Summary(1, db).Label, Does.Contain("Level"));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
