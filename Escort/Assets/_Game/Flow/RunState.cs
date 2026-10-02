using System.Collections.Generic;
using HS.Core;

namespace HS.Flow
{
    /// <summary>
    /// What survives a Restore Point (GDD §11.3 "restore to a chapter start, skills kept, Rapport restored to that
    /// snapshot"). Static so it outlives a scene reload. The slice has two points: chapter start, and the campfire.
    /// </summary>
    public static class RunState
    {
        public sealed class Point
        {
            public int Seed;
            public int Level;
            public int Xp;
            public List<(string id, int rank)> Skills = new List<(string, int)>();
            public List<string> Loadout = new List<string>();
            public Stage HeroStage;
            public HS.Rapport.RapportLedger.State Ledger;
            public (int relayed, int forged) Intel;
            public float HeroHp = -1f;
            public List<HS.Hero.WoundType> Wounds = new List<HS.Hero.WoundType>();
        }

        public static Point ChapterStart, Campfire;
        /// <summary>Set before reloading the scene: which point to resume from ("chapter" or "campfire").</summary>
        public static string Resume;
        public static int Runs;

        /// <summary>New play session (not a scene reload): forget everything.</summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Clear();
            Runs = 0;
        }

        public static void Clear()
        {
            ChapterStart = null;
            Campfire = null;
            Resume = null;
        }
    }
}
