using System.Collections.Generic;
using HS.Core;
using UnityEngine;

namespace HS.Flow
{
    /// <summary>
    /// Fixed XP pots per room (GDD §4.1; per chapter via <see cref="BeginChapter"/>): 50% for the clear, 25% for assisting (you used a skill or drew blood in that
    /// room), 25% for exploring (you searched its cache) — so pacing doesn't depend on playstyle. Levels from thresholds.
    /// </summary>
    public sealed class XpTracker
    {
        public const int PotPerRoom = 100;
        /// <summary>Total XP needed to reach level i+2 (level 1 is the start).</summary>
        public static readonly int[] Thresholds = { 120, 240, 420, 640, 900, 1200, 1540, 1920, 2340, 2800, 3300, 3840 };

        public int Xp { get; private set; }
        /// <summary>(amount, "clear" | "assist" | "explore") — HUD feedback and the tutorial.</summary>
        public event System.Action<int, string> Awarded;
        readonly HashSet<int> _cleared = new HashSet<int>(), _assisted = new HashSet<int>(), _explored = new HashSet<int>();
        RunContext _ctx;
        int _currentRoom = -1;

        System.Func<int, int> _pot = r => PotPerRoom;

        /// <summary>A new chapter: room indices start again at 0, and its rooms pay this chapter's pots.</summary>
        public void BeginChapter(System.Func<int, int> potForRoom)
        {
            _cleared.Clear();
            _assisted.Clear();
            _explored.Clear();
            _currentRoom = -1;
            _pot = potForRoom ?? (r => PotPerRoom);
        }

        public void Bind(RunContext ctx)
        {
            _ctx = ctx;
            ctx.Events.RoomEntered += r => _currentRoom = r;
            ctx.Events.RoomCleared += r => Award(_cleared, r, 0.5f, "clear");
            ctx.Events.SkillUsed += (id, user) =>
            {
                if (user is HS.Sidekick.SidekickAgent && id != "dodge") Award(_assisted, _currentRoom, 0.25f, "assist");
            };
            ctx.Events.Damage += (d, applied) =>
            {
                if (d.Source is HS.Sidekick.SidekickAgent && d.Target is HS.Enemies.EnemyAgent) Award(_assisted, _currentRoom, 0.25f, "assist");
            };
            ctx.Events.Explored += (r, title) => Award(_explored, r >= 0 ? r : _currentRoom, 0.25f, "explore");
        }

        void Award(HashSet<int> set, int room, float share, string reason)
        {
            if (room < 0 || room >= 100 || !set.Add(room)) return;
            int amount = Mathf.RoundToInt(_pot(room) * share);
            if (amount <= 0) return;
            Xp += amount;
            Awarded?.Invoke(amount, reason);
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
