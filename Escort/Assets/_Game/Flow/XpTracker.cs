using System.Collections.Generic;
using HS.Core;

namespace HS.Flow
{
    /// <summary>
    /// Fixed XP pots per room (GDD §4.1): 50% for the clear, 25% for assisting (you used a skill or drew blood in that
    /// room), 25% for exploring (you searched its cache) — so pacing doesn't depend on playstyle. Levels from thresholds.
    /// </summary>
    public sealed class XpTracker
    {
        public const int PotPerRoom = 100;
        /// <summary>Total XP needed to reach level i+2 (level 1 is the start).</summary>
        public static readonly int[] Thresholds = { 120, 240, 420, 640, 900, 1200, 1540, 1920, 2340, 2800, 3300, 3840 };

        public int Xp { get; private set; }
        readonly HashSet<int> _cleared = new HashSet<int>(), _assisted = new HashSet<int>(), _explored = new HashSet<int>();
        RunContext _ctx;
        int _currentRoom = -1;

        public void Bind(RunContext ctx)
        {
            _ctx = ctx;
            ctx.Events.RoomEntered += r => _currentRoom = r;
            ctx.Events.RoomCleared += r => Award(_cleared, r, PotPerRoom / 2);
            ctx.Events.SkillUsed += (id, user) =>
            {
                if (user is HS.Sidekick.SidekickAgent && id != "dodge") Award(_assisted, _currentRoom, PotPerRoom / 4);
            };
            ctx.Events.Damage += (d, applied) =>
            {
                if (d.Source is HS.Sidekick.SidekickAgent && d.Target is HS.Enemies.EnemyAgent) Award(_assisted, _currentRoom, PotPerRoom / 4);
            };
            ctx.Events.Explored += (r, title) => Award(_explored, r >= 0 ? r : _currentRoom, PotPerRoom / 4);
        }

        void Award(HashSet<int> set, int room, int amount)
        {
            if (room < 0 || room >= 100 || !set.Add(room)) return;
            Xp += amount;
        }

        public static int LevelFor(int xp)
        {
            int level = 1;
            foreach (var t in Thresholds)
            {
                if (xp < t) break;
                level++;
            }
            return level;
        }

        public void Restore(int xp) => Xp = xp;
    }
}
