using HS.Core;

namespace HS.Rapport
{
    /// <summary>The three Stage checks (GDD §4.4). The vertical slice runs its campfire check as <see cref="Chapter2"/>.</summary>
    public enum StageCheck { Chapter2, Chapter3, Door }

    /// <summary>
    /// Rapport → Stage (GDD §4.4): S1 ≥ 30%, S2 ≥ 55%, S3 ≥ 75% capture rate. Caps by check: Ch2 ≤ S1, Ch3 ≤ S2,
    /// door ≤ S3. Between the Ch3 check and the door the Stage can rise at most +1 (recovery cap). At any check it can
    /// fall at most one step when penalties push the rate below its band.
    /// </summary>
    public static class StageEvaluator
    {
        public const float S1Threshold = 0.30f, S2Threshold = 0.55f, S3Threshold = 0.75f;

        public static Stage Band(float rate) =>
            rate >= S3Threshold ? Stage.S3 : rate >= S2Threshold ? Stage.S2 : rate >= S1Threshold ? Stage.S1 : Stage.S0;

        public static Stage Cap(StageCheck check) =>
            check == StageCheck.Chapter2 ? Stage.S1 : check == StageCheck.Chapter3 ? Stage.S2 : Stage.S3;

        public static Stage Evaluate(Stage current, float rate, StageCheck check)
        {
            // The cap is absolute. (A Stage above the check's cap can't arise in a campaign — caps only rise check to
            // check and Restore Points restore the Stage with the chapter — but if it ever did, the cap wins and the
            // one-step fall is measured from the capped Stage.)
            int cur = System.Math.Min((int)current, (int)Cap(check));
            int target = System.Math.Min((int)Band(rate), (int)Cap(check));
            if (target < cur) return (Stage)(cur - 1);                    // fall: at most one per check
            if (target > cur && check == StageCheck.Door) return (Stage)System.Math.Min(target, cur + 1); // recovery cap
            return (Stage)target;
        }
    }
}
