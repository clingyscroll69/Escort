using System.Collections.Generic;
using HS.Core;

namespace HS.Flow
{
    /// <summary>
    /// What survives a Restore Point (GDD §11.3 "restore to a chapter start, skills kept, Rapport restored to that
    /// snapshot"). Static so it outlives a scene reload. One point per chapter start reached this run, plus the Gallery door.
    /// </summary>
    public static class RunState
    {
        public sealed class Point
        {
            public int Chapter = 1;
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

        static readonly Point[] Starts = new Point[6]; // [1..5]
        /// <summary>Before the Gallery door (after the chapter 5 approach).</summary>
        public static Point Door;
        /// <summary>Set before reloading the scene: "chapter:N", "chapter" (the latest start), or "door".</summary>
        public static string Resume;
        public static int Runs;

        public static Point ChapterStartOf(int chapter) => chapter >= 1 && chapter <= 5 ? Starts[chapter] : null;

        public static void SetChapterStart(int chapter, Point p)
        {
            p.Chapter = chapter;
            Starts[UnityEngine.Mathf.Clamp(chapter, 1, 5)] = p;
        }

        /// <summary>The latest chapter start reached. Setting it files the point under its own chapter.</summary>
        public static Point ChapterStart
        {
            get
            {
                for (int c = 5; c >= 1; c--) if (Starts[c] != null) return Starts[c];
                return null;
            }
            set
            {
                if (value != null) SetChapterStart(value.Chapter, value);
            }
        }

        public static Point Resolve(string resume)
        {
            if (resume == "door") return Door;
            if (resume == "chapter") return ChapterStart;
            if (resume != null && resume.StartsWith("chapter:") && int.TryParse(resume.Substring(8), out int ch)) return ChapterStartOf(ch);
            return null;
        }

        /// <summary>Restoring to chapter N: the later timeline (its chapter starts, the door) is gone.</summary>
        public static void ForgetAfter(int chapter)
        {
            for (int c = chapter + 1; c <= 5; c++) Starts[c] = null;
            Door = null;
        }

        /// <summary>New play session (not a scene reload): forget everything.</summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Clear();
            Runs = 0;
        }

        public static void Clear()
        {
            for (int c = 0; c < Starts.Length; c++) Starts[c] = null;
            Door = null;
            Resume = null;
        }
    }
}
