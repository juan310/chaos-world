using System.Linq;
using FH.Core;
using NUnit.Framework;

namespace FH.Tests
{
    public class ContentTests
    {
        [Test]
        public void ShippedContentValidatesWithoutErrors()
        {
            var db = TestSupport.Db();
            Assert.That(db.Errors, Is.Empty, string.Join("\n", db.Errors));
            Assert.That(db.Items.Count, Is.GreaterThan(10));
            Assert.That(db.EstateTiers.Select(t => t.tier), Is.EqualTo(Enumerable.Range(0, db.EstateTiers.Count)));
        }

        [Test]
        public void ValidatorCatchesBrokenReferencesAndUnreadableTelegraphs()
        {
            var db = GameDatabase.FromJson(@"{ ""items"": [ { ""id"": ""sword"", ""kind"": ""Weapon"", ""maxStack"": 5 } ],
                ""monsters"": [ { ""id"": ""m"", ""lootTable"": ""missing"", ""attacks"": [ { ""id"": ""a"", ""telegraph"": 0.1 } ] } ],
                ""recipes"": [ { ""id"": ""r"", ""inputs"": [ { ""itemId"": ""ghost"", ""count"": 1 } ], ""output"": { ""itemId"": ""sword"", ""count"": 1 } } ] }");
            Assert.That(db.Errors.Any(e => e.Contains("must be unique")));
            Assert.That(db.Errors.Any(e => e.Contains("no weapon definition")));
            Assert.That(db.Errors.Any(e => e.Contains("missing loot table 'missing'")));
            Assert.That(db.Errors.Any(e => e.Contains("telegraph shorter")));
            Assert.That(db.Errors.Any(e => e.Contains("missing item 'ghost'")));
        }

        [Test]
        public void PlaceholderWerewolfIsNotSpawnableAndSaysWhy()
        {
            var db = TestSupport.Db();
            var wolf = db.Monsters["werewolf"];
            Assert.That(wolf.assetStatus, Does.StartWith("Missing"));
            Assert.That(db.Spawns.Values.Any(s => s.monsterId == "werewolf"), Is.False);
        }
    }

    public class InventoryTests
    {
        [Test]
        public void StackablesMergeAndRespectMaxStack()
        {
            var core = TestSupport.NewCore();
            int before = core.Inventory.Bag.UsedSlots;
            Assert.That(core.Inventory.Add("bone_shard", 150, ItemSource.Loot).Ok);
            Assert.That(core.Inventory.Count("bone_shard"), Is.EqualTo(150));
            Assert.That(core.Inventory.Bag.UsedSlots, Is.EqualTo(before + 2), "150 shards with maxStack 99 need two slots");
        }

        [Test]
        public void FullPackRefusesWholeBatchAndChangesNothing()
        {
            var core = TestSupport.NewCore();
            while (core.Inventory.Bag.FreeSlots > 1) core.Inventory.Add("padded_jerkin", 1, ItemSource.System);
            var snapshot = core.Inventory.Bag.Stacks.Select(s => s.itemId + s.count).ToList();
            var r = core.Inventory.AddAll(new[] { new ItemAmount { itemId = "bone_vest", count = 1 }, new ItemAmount { itemId = "warden_helm", count = 1 } }, ItemSource.Reward);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Reason, Does.Contain("needs 2"));
            Assert.That(core.Inventory.Bag.Stacks.Select(s => s.itemId + s.count), Is.EqualTo(snapshot));
        }

        [Test]
        public void RemovingMoreThanOwnedFailsAndNeverGoesNegative()
        {
            var core = TestSupport.NewCore();
            core.Inventory.Add("grave_dust", 2, ItemSource.Loot);
            var r = core.Inventory.Remove("grave_dust", 3);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Reason, Does.Contain("have 2"));
            Assert.That(core.Inventory.Count("grave_dust"), Is.EqualTo(2));
            Assert.That(core.Inventory.Remove("grave_dust", -1).Ok, Is.False);
            Assert.That(core.Inventory.Add("grave_dust", 0, ItemSource.Loot).Ok, Is.False);
            Assert.That(core.State.inventory.All(s => s.count > 0));
        }

        [Test]
        public void UniqueItemsGetDistinctIdsAndEquippedOnesAreProtected()
        {
            var core = TestSupport.NewCore();
            core.Inventory.Add("hunter_blade", 1, ItemSource.System, out var spare);
            var blades = core.State.inventory.Where(s => s.itemId == "hunter_blade").ToList();
            Assert.That(blades.Count, Is.EqualTo(2));
            Assert.That(blades[0].uid, Is.Not.EqualTo(blades[1].uid));
            Assert.That(core.Inventory.Remove("hunter_blade", 2).Ok, Is.False, "the equipped blade must not be consumed");
            Assert.That(core.Inventory.Remove("hunter_blade", 1).Ok, Is.True);
            Assert.That(core.Inventory.Equipped(EquipSlot.Weapon), Is.Not.Null);
        }

        [Test]
        public void EquipChangesDerivedStatsAndPreviewMatches()
        {
            var core = TestSupport.NewCore();
            core.Inventory.Add("bone_vest", 1, ItemSource.Craft, out var vest);
            var before = core.Stats.Compute();
            var delta = core.Stats.PreviewDelta(vest.uid);
            Assert.That(core.Inventory.Equip(vest.uid).Ok);
            var after = core.Stats.Compute();
            Assert.That(after[Stat.Defense] - before[Stat.Defense], Is.EqualTo(14 - 4).Within(.001));
            Assert.That(after[Stat.MaxHealth] - before[Stat.MaxHealth], Is.EqualTo(15).Within(.001));
            Assert.That(delta[Stat.Defense], Is.EqualTo(after[Stat.Defense] - before[Stat.Defense]).Within(.001));
            Assert.That(core.Inventory.Unequip(EquipSlot.Weapon).Ok, Is.False, "weapon slot cannot be emptied");
        }
    }

    public class EconomyTests
    {
        [Test]
        public void BuyChargesExactPriceAndRefusesWithoutGold()
        {
            var core = TestSupport.NewCore();
            int gold = core.Wallet.Gold;
            Assert.That(core.Vendors.Buy("v_bram", "oak_timber", 2).Ok);
            Assert.That(core.Wallet.Gold, Is.EqualTo(gold - 24));
            Assert.That(core.Inventory.Count("oak_timber"), Is.EqualTo(2));
            core.State.gold = 5;
            var r = core.Vendors.Buy("v_bram", "oak_timber", 1);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Reason, Does.Contain("12 gold"));
            Assert.That(core.Inventory.Count("oak_timber"), Is.EqualTo(2));
            Assert.That(core.Wallet.Gold, Is.EqualTo(5));
            Assert.That(core.Vendors.Buy("v_bram", "oak_timber", 0).Ok, Is.False);
            Assert.That(core.Vendors.Buy("v_bram", "oak_timber", -3).Ok, Is.False);
            Assert.That(core.Wallet.Spend(-10).Ok, Is.False, "negative spend must not mint gold");
        }

        [Test]
        public void RankGatedAndLimitedStock()
        {
            var core = TestSupport.NewCore();
            core.State.gold = 1000;
            var gated = core.Vendors.Buy("v_bram", "silver_ingot", 1);
            Assert.That(gated.Ok, Is.False);
            Assert.That(gated.Reason, Does.Contain("Bronze"));
            TestSupport.SetRank(core, 1);
            Assert.That(core.Vendors.Buy("v_bram", "silver_ingot", 1).Ok);
            Assert.That(core.Vendors.Buy("v_bram", "silver_ingot", 1).Reason, Does.Contain("Sold out"));
            core.Clock.Advance(24);
            Assert.That(core.Vendors.Buy("v_bram", "silver_ingot", 1).Ok, "stock refreshes the next day");
        }

        [Test]
        public void SellingRespectsEquipmentKeysAndVendorType()
        {
            var core = TestSupport.NewCore();
            var equipped = core.Inventory.Equipped(EquipSlot.Weapon);
            Assert.That(core.Vendors.Sell("v_bram", equipped, 1).Reason, Does.Contain("Unequip"));
            core.Inventory.Add("bone_shard", 5, ItemSource.Loot);
            int gold = core.Wallet.Gold;
            var stack = core.State.inventory.First(s => s.itemId == "bone_shard");
            Assert.That(core.Vendors.Sell("v_bram", stack, 5).Ok);
            Assert.That(core.Wallet.Gold, Is.EqualTo(gold + 15));
            Assert.That(core.Inventory.Count("bone_shard"), Is.EqualTo(0));
            core.Inventory.Add("promise_ring", 1, ItemSource.Craft, out var ring);
            Assert.That(core.Vendors.Sell("v_mira", ring, 1).Ok, Is.False);
            var vest = core.Inventory.Add("bone_vest", 1, ItemSource.Craft, out var v);
            Assert.That(core.Vendors.Sell("v_mira", v, 1).Reason, Does.Contain("does not buy"));
            Assert.That(core.Vendors.Sell("v_bram", stack, 1).Ok, Is.False, "a sold stack cannot be sold again");
        }
    }

    public class CraftingTests
    {
        [Test]
        public void UnavailableRecipeExplainsEveryMissingRequirement()
        {
            var core = TestSupport.NewCore();
            var check = core.Crafting.Check("r_gravebane", Station.Forge);
            Assert.That(check.CanCraft, Is.False);
            Assert.That(check.Summary, Does.Contain("not learned"));
            Assert.That(check.Summary, Does.Contain("4× Gravebound Bone Shard (have 0)"));
            Assert.That(check.Summary, Does.Contain("1× Grave Dust (have 0)"));
            var wrongStation = core.Crafting.Check("r_mooncap_tonic", Station.Forge);
            Assert.That(wrongStation.Summary, Does.Contain("Alchemy"));
        }

        [Test]
        public void CraftConsumesExactlyAndUpgradeKeepsBladeEquipped()
        {
            var core = TestSupport.NewCore();
            core.State.knownRecipes.Add("r_gravebane");
            core.Inventory.Add("bone_shard", 5, ItemSource.Loot);
            core.Inventory.Add("grave_dust", 1, ItemSource.Loot);
            int gold = core.Wallet.Gold;
            float dmgBefore = core.Stats.EquippedWeapon().baseDamage;
            var r = core.Crafting.Craft("r_gravebane", Station.Forge);
            Assert.That(r.Ok, r.Reason);
            Assert.That(core.Inventory.Count("bone_shard"), Is.EqualTo(1));
            Assert.That(core.Inventory.Count("grave_dust"), Is.EqualTo(0));
            Assert.That(core.Wallet.Gold, Is.EqualTo(gold - 30));
            Assert.That(core.Inventory.Count("hunter_blade"), Is.EqualTo(0));
            Assert.That(core.Stats.EquippedWeapon().id, Is.EqualTo("hunter_blade_grave"));
            Assert.That(core.Stats.EquippedWeapon().baseDamage, Is.GreaterThan(dmgBefore));
            Assert.That(core.Crafting.Craft("r_gravebane", Station.Forge).Ok, Is.False, "no base blade left to upgrade");
        }

        [Test]
        public void RecipeUnlocksComeFromProgression()
        {
            var core = TestSupport.NewCore();
            Assert.That(core.Crafting.Known("r_gravebane"), Is.False);
            Assert.That(core.Crafting.Known("r_mooncap_tonic"), Is.True);
            core.Rewards.Grant(core.Db.Contracts["c_gravebound_cull"].reward, "test:unlock");
            Assert.That(core.Crafting.Known("r_gravebane"));
            Assert.That(core.Crafting.Known("r_bone_vest"));
        }
    }
}
