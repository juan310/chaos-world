using System.IO;
using System.Linq;
using FH.Core;
using NUnit.Framework;

namespace FH.Tests
{
    /// <summary>Regression tests for defects found in the 0.9 review of the game systems (each test is the failure
    /// scenario that was reported).</summary>
    public class SystemFixTests
    {
        [Test]
        public void EveryHuntContractCanBeClaimedFromItsGiverOrTheBoard()
        {
            var db = TestSupport.Db();
            foreach (var id in new[] { "c_thornback", "c_mirejaw" })
            {
                var nodes = db.ClaimNodes("d_mira", id).ToList();
                Assert.That(nodes, Does.Contain("mira_main"), id + " is reported to Mira");
                Assert.That(nodes, Does.Contain("mira_spouse"), id + " is reported to Mira after the wedding too");
            }
            Assert.That(db.ClaimNodes("d_mira", "c_grave_warden"), Does.Contain("mira_spouse"), "the Warden can still be reported once married");
            // a contract whose giver offers no claim choice is claimed at the board (the board checks this)
            Assert.That(db.ClaimedInDialogue("c_werewolf"), Is.False);
        }

        [Test]
        public void MarriedMiraStillOffersTheWarden()
        {
            var core = TestSupport.NewCore();
            core.State.SetFlag("rel_mira_spouse");
            core.State.SetFlag("met_mira");
            var run = new DialogueRunner(core, "d_mira", "mira");
            Assert.That(run.Node.id, Is.EqualTo("mira_spouse"));
            Assert.That(run.Node.choices.Any(c => c.next == "mira_warden"), "the Warden contract can be taken from the spouse dialogue");
        }

        [Test]
        public void ARespawnedMonsterComesBackWhole()
        {
            var core = TestSupport.NewCore();
            var sp = core.Db.Spawns["sp_bridge_1"];
            var st = core.State.Spawn(sp.id);
            st.brokenParts.Add("helmet");
            st.deadUntilHour = core.State.world.hours - 1;     // its time is up, midnight has not come
            Assert.That(core.Spawns.ShouldBeAlive(sp), Is.True);
            Assert.That(st.brokenParts, Is.Empty, "broken parts heal when it respawns");
        }

        [Test]
        public void TheStoryDoesNotWaitForASecondForgeOfABladeAlreadyMade()
        {
            var core = TestSupport.NewCore();
            var q = core.State.Quest("q_rise");
            core.Contracts.Accept("c_gravebound_cull");
            for (int i = 0; i < 3; i++) TestSupport.KillAndLoot(core, "gravebound", "sp_bridge_1", new MaxRandom());
            Assert.That(core.Contracts.Claim("c_gravebound_cull").Ok);
            Assert.That(core.Crafting.Craft("r_gravebane", Station.Forge).Ok, "the Gravebane forged before the story asks for it");
            Assert.That(q.stage, Is.EqualTo(0));
            new DialogueRunner(core, "d_mira", "mira");
            Assert.That(q.stage, Is.EqualTo(1));
            for (int i = 0; i < 3; i++) TestSupport.KillAndLoot(core, "gravebound", "sp_bridge_1", new MaxRandom());
            Assert.That(q.stage, Is.GreaterThanOrEqualTo(3), "the forge stage is met by the blade already owned");
        }

        [Test]
        public void ADamagedSaveRestoresTheBackupAndKeepsIt()
        {
            var db = TestSupport.Db();
            var store = new SaveStore(TestSupport.TempDir());
            var core = TestSupport.NewCore();
            core.State.gold = 111; store.Save(1, core.State);
            core.State.gold = 222; store.Save(1, core.State);          // backup holds 111, main 222
            File.WriteAllText(store.PathFor(1), "{ this is not a save");
            int damagedBefore = Directory.GetFiles(store.Root).Length;
            var summary = store.Summary(1, db);
            Assert.That(Directory.GetFiles(store.Root).Length, Is.EqualTo(damagedBefore), "the slot list only reads");
            var r = store.Load(1, db);
            Assert.That(r.Status, Is.EqualTo(LoadStatus.LoadedFromBackup));
            Assert.That(SaveCodec.Decode(File.ReadAllText(store.PathFor(1)), db).Ok, "the slot holds the good save again");
            core.State.gold = 333; store.Save(1, core.State);
            Assert.That(SaveCodec.Decode(File.ReadAllText(store.BackupFor(1)), db).State.gold, Is.EqualTo(111), "the next save keeps a good backup");
        }

        [Test]
        public void SavesWithEmptyListsOrNoWeaponStillLoad()
        {
            var db = TestSupport.Db();
            var core = TestSupport.NewCore();
            core.State.equipment.weapon = "";
            var tree = (System.Collections.Generic.Dictionary<string, object>)Json.Parse(SaveCodec.Encode(core.State));
            tree["contracts"] = null; tree["quests"] = null;
            var r = SaveCodec.Decode(Json.Write(tree), db);
            Assert.That(r.Ok, r.Message);
            Assert.That(r.State.contracts, Is.Not.Null);
            Assert.That(r.State.equipment.weapon, Is.Not.Empty, "a weapon is equipped so the hunter can fight");
        }

        [Test]
        public void ADaytimeRestIsANapNotANewDay()
        {
            var core = TestSupport.NewCore();
            core.State.world.hours = core.State.world.Day * 24 + 10;
            Assert.That(core.Clock.CanSleepThroughNight, Is.False);
            int day = core.Clock.Day;
            core.Clock.Nap();
            Assert.That(core.Clock.Day, Is.EqualTo(day));
            Assert.That(core.Clock.Hour, Is.EqualTo(12).Within(.01));
            core.State.world.hours = core.State.world.Day * 24 + 19;
            Assert.That(core.Clock.CanSleepThroughNight, Is.True);
            core.Clock.SleepUntilMorning();
            Assert.That(core.Clock.Day, Is.EqualTo(day + 1));
        }
    }
}
