using UnityEngine;

namespace HS.Core
{
    /// <summary>
    /// Per-chapter combat scaling (campaign spec §2). Callum: damage ×2.5 and HP ×2 per chapter (GDD §4.2). Enemies: HP
    /// ×2.5 and damage-to-hero ×2, so his hits-to-kill and hits-to-die stay constant; the sidekick's outgoing damage ×2.5
    /// so hers do too. Status durations never scale. Difficulty grows from the pressure mix, not the numbers.
    /// </summary>
    public static class ChapterTier
    {
        public const int First = 1, Last = 5;
        public static int Clamp(int chapter) => Mathf.Clamp(chapter, First, Last);
        public static float HeroDamage(int chapter) => Mathf.Pow(2.5f, Clamp(chapter) - 1);
        public static float HeroHp(int chapter) => Mathf.Pow(2f, Clamp(chapter) - 1);
        public static float EnemyHp(int chapter) => HeroDamage(chapter);
        public static float EnemyDamageToHero(int chapter) => HeroHp(chapter);
        public static float SidekickDamage(int chapter) => HeroDamage(chapter);
        public static int Current => RunContext.Current != null ? Clamp(RunContext.Current.Chapter) : First;
    }
}
