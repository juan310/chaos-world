using System;
using System.IO;
using System.Linq;
using FH.Core;

namespace FH.Tests
{
    public static class TestSupport
    {
        static string contentText;

        /// <summary>Finds the shipped content file by walking up from the working and binary directories.</summary>
        public static string ContentText()
        {
            if (contentText != null) return contentText;
            string env = Environment.GetEnvironmentVariable("FH_CONTENT");
            var starts = new[] { env == null ? null : Path.GetDirectoryName(env), Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
            foreach (var start in starts.Where(s => !string.IsNullOrEmpty(s)))
            {
                if (env != null && File.Exists(env)) return contentText = File.ReadAllText(env);
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    var p = Path.Combine(dir.FullName, "Assets", "Resources", "Data", "content.json");
                    if (File.Exists(p)) return contentText = File.ReadAllText(p);
                    dir = dir.Parent;
                }
            }
            throw new FileNotFoundException("content.json not found");
        }

        public static GameDatabase Db() => GameDatabase.FromJson(ContentText());

        public static GameCore NewCore(GameDatabase db = null)
        {
            db = db ?? Db();
            return new GameCore(db, GameCore.NewGameState(db, "test"));
        }

        public static string TempDir()
        {
            var d = Path.Combine(Path.GetTempPath(), "fh-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        /// <summary>Simulates killing a monster and picking up its loot through the same events the game uses.</summary>
        public static void KillAndLoot(GameCore core, string monsterId, string spawnId, IRandom rng)
        {
            var def = core.Db.Monsters[monsterId];
            core.Events.RaiseKilled(monsterId, spawnId);
            core.Spawns.MarkKilled(spawnId);
            foreach (var drop in core.Loot.Roll(def.lootTable, rng)) core.Inventory.Add(drop.itemId, drop.count, ItemSource.Loot);
            core.Wallet.Earn(def.gold);
            core.Progression.AddXp(def.xp);
        }

        public static void SetRank(GameCore core, int rank)
        {
            core.State.player.hunterReputation = core.Db.Ranks.First(r => r.rank == rank).reputationRequired;
            core.AfterStateChange();
        }
    }

    /// <summary>Random source that always succeeds rolls and picks the maximum, for deterministic loot.</summary>
    public class MaxRandom : IRandom
    {
        public float Value() => 0;
        public int Range(int min, int max) => max;
    }
}
