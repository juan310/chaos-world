using FH.Core;
using NUnit.Framework;

namespace FH.Tests
{
    /// <summary>The hunter's look: ten hairstyles per body, model fallbacks, names, and the choice kept in the save.</summary>
    public class AppearanceTests
    {
        [Test]
        public void TenHairstylesEachNamedAfterTheSheets()
        {
            Assert.That(Appearance.MaleHair.Length, Is.EqualTo(10));
            Assert.That(Appearance.FemaleHair.Length, Is.EqualTo(10));
            Assert.That(Appearance.Hairstyles(Appearance.Female)[4], Is.EqualTo("High Ponytail"));
            Assert.That(Appearance.Hairstyles(Appearance.Male)[7], Is.EqualTo("Warrior Braid"));
        }

        [Test]
        public void EveryLookIsARealisticHunter()
        {
            Assert.That(Appearance.Models(Appearance.Female, 5), Is.EqualTo(new[] { "Hunter_Female_Slim", "Hunter_Female_05", "Hunter_Female", "Hunter" }));
            Assert.That(Appearance.Models(Appearance.Male, 99)[1], Is.EqualTo("Hunter_Male_10"));
            Assert.That(Appearance.Models("", 1)[0], Is.EqualTo("Hunter_Male_Slim"), "a save from before 0.9 wears the male hunter");
            Assert.That(Appearance.Models(Appearance.Classic, 3)[1], Is.EqualTo("Hunter_Male_03"), "the chibi knight is no longer worn");
            Assert.That(Appearance.Describe("", 2), Does.StartWith("Male hunter"));
        }

        [Test]
        public void NamesAreCleaned()
        {
            Assert.That(Appearance.CleanName("  Elowen  "), Is.EqualTo("Elowen"));
            Assert.That(Appearance.CleanName(""), Is.EqualTo("Hunter"));
            Assert.That(Appearance.CleanName("<color=red>X</color>").Contains("<"), Is.False);
            Assert.That(Appearance.CleanName(new string('a', 40)).Length, Is.EqualTo(18));
        }

        [Test]
        public void TheLookIsKeptInTheSave()
        {
            var core = TestSupport.NewCore();
            core.State.player.body = Appearance.Female; core.State.player.hair = 8; core.State.player.name = "Wren";
            var back = SaveCodec.Decode(SaveCodec.Encode(core.State), core.Db).State;
            Assert.That(back.player.body, Is.EqualTo(Appearance.Female));
            Assert.That(back.player.hair, Is.EqualTo(8));
            Assert.That(back.player.name, Is.EqualTo("Wren"));
        }
    }
}
