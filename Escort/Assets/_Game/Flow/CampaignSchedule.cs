using HS.Hero;
using HS.Rapport;
using UnityEngine;

namespace HS.Flow
{
    /// <summary>What a chapter is, mechanically (campaign spec §3.2).</summary>
    public sealed class ChapterRules
    {
        public int Chapter;
        public string Name;
        public int Slots;
        public Signature Unlocks;
        /// <summary>The hidden Stage check at this chapter's campfire (null: none).</summary>
        public StageCheck? CampCheck;
        /// <summary>False for the Gallery: it ends at the door, not a campfire.</summary>
        public bool HasCamp = true;
        public bool LearnsRecall, CapstoneReveal, Hunger;
        /// <summary>Between-room recovery, a fraction of his max HP (wounds stay).</summary>
        public float Recovery;
        /// <summary>Multiplier on each room module's XP pot.</summary>
        public float XpFactor;
    }

    /// <summary>
    /// The campaign's fixed schedule (GDD §3, §4.1, §4.2, §4.4, §4.6). Pure data: GameFlow, the campfire and the tests read
    /// it; nothing else decides chapter numbers.
    /// </summary>
    public static class CampaignSchedule
    {
        public const int Chapters = 5;
        const Signature Ch1 = Signature.StrikeI;
        const Signature Ch2 = Ch1 | Signature.StanceI;
        const Signature Ch3 = Ch2 | Signature.FinisherI;
        const Signature Ch4 = Ch3 | Signature.StrikeII | Signature.StanceII;
        const Signature Ch5 = Ch4 | Signature.FinisherII;

        static readonly ChapterRules[] Table =
        {
            new ChapterRules { Chapter = 1, Name = "The Old Road", Slots = 4, Unlocks = Ch1, Recovery = 0.5f, XpFactor = 1.0f },
            new ChapterRules { Chapter = 2, Name = "Whisperwood", Slots = 5, Unlocks = Ch2, CampCheck = StageCheck.Chapter2, LearnsRecall = true, Hunger = true, Recovery = 0.3f, XpFactor = 1.8f },
            new ChapterRules { Chapter = 3, Name = "Catacombs of Ends", Slots = 6, Unlocks = Ch3, CampCheck = StageCheck.Chapter3, Hunger = true, Recovery = 0.3f, XpFactor = 2.4f },
            new ChapterRules { Chapter = 4, Name = "The Sunken Bastion", Slots = 6, Unlocks = Ch4, CapstoneReveal = true, Hunger = true, Recovery = 0.3f, XpFactor = 4.7f },
            new ChapterRules { Chapter = 5, Name = "The Gallery", Slots = 6, Unlocks = Ch5, HasCamp = false, Recovery = 0.3f, XpFactor = 0f },
        };

        public static ChapterRules For(int chapter) => Table[Mathf.Clamp(chapter, 1, Chapters) - 1];

        /// <summary>A room's XP pot in this chapter (GDD §4.1: fixed pots, 13 levels across chapters 1–4).</summary>
        public static int RoomPot(int chapter, int modulePot) => Mathf.RoundToInt(modulePot * For(chapter).XpFactor);

        /// <summary>The level a thorough player has at the end of a chapter.</summary>
        public static int LevelTarget(int chapter) => chapter <= 1 ? 3 : chapter == 2 ? 6 : chapter == 3 ? 9 : 13;
    }
}
