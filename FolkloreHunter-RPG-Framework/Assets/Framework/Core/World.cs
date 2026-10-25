using System;
using System.Linq;

namespace FH.Core
{
    /// <summary>In-game clock. Time only advances while the game runs (or when resting), never from wall-clock time.</summary>
    public class WorldClock
    {
        readonly GameDatabase db;
        readonly GameState state;
        readonly GameEvents events;
        public WorldClock(GameDatabase db, GameState state, GameEvents events) { this.db = db; this.state = state; this.events = events; }

        public int Day => state.world.Day;
        public double Hour => state.world.HourOfDay;
        public bool IsNight => Hour < 6 || Hour >= 20;
        public string Label => "Day " + (Day + 1) + "  " + ((int)Hour).ToString("00") + ":" + ((int)((Hour % 1) * 60)).ToString("00");

        public void Advance(double hours)
        {
            if (hours <= 0 || double.IsNaN(hours)) return;
            int before = Day;
            state.world.hours += hours;
            for (int d = before + 1; d <= Day; d++) events.RaiseDay(d);
        }

        public void TickRealSeconds(float seconds) => Advance(seconds / 60.0 * db.Balance.hoursPerRealMinute);

        /// <summary>Sleep until the next morning. Returns hours slept.</summary>
        public double SleepUntilMorning()
        {
            double target = (Day + 1) * 24.0 + db.Balance.dayStartHour;
            if (Hour < db.Balance.dayStartHour - 0.01) target = Day * 24.0 + db.Balance.dayStartHour;
            double slept = target - state.world.hours;
            Advance(slept);
            return slept;
        }
    }

    public class SpawnRules
    {
        readonly GameDatabase db;
        readonly GameState state;
        readonly Conditions conditions;
        readonly WorldClock clock;
        public SpawnRules(GameDatabase db, GameState state, Conditions conditions, WorldClock clock) { this.db = db; this.state = state; this.conditions = conditions; this.clock = clock; }

        public bool ShouldBeAlive(SpawnDef s)
        {
            var st = state.world.spawns.FirstOrDefault(x => x.spawnId == s.id);
            if (st != null && st.deadUntilHour > state.world.hours) return false;
            if (s.nightOnly && !clock.IsNight) return false;
            return conditions.All(s.requirements);
        }

        public void MarkKilled(string spawnId)
        {
            if (!db.Spawns.TryGetValue(spawnId, out var s)) return;
            var st = state.Spawn(spawnId);
            st.deadUntilHour = s.respawnHours < 0 ? double.MaxValue : state.world.hours + s.respawnHours;
        }

        /// <summary>Clears broken-part memory for spawns that have respawned.</summary>
        public void ResetRespawned()
        {
            foreach (var st in state.world.spawns)
                if (st.deadUntilHour >= 0 && st.deadUntilHour <= state.world.hours) { st.deadUntilHour = -1; st.brokenParts.Clear(); }
        }

        public double HoursUntilRespawn(string spawnId)
        {
            var st = state.world.spawns.FirstOrDefault(x => x.spawnId == spawnId);
            return st == null || st.deadUntilHour < 0 ? 0 : Math.Max(0, st.deadUntilHour - state.world.hours);
        }
    }
}
