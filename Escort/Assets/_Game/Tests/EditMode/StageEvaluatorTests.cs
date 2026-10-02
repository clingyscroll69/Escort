using System.Collections.Generic;
using HS.Core;
using HS.Rapport;
using NUnit.Framework;

namespace HS.Tests
{
    /// <summary>GDD §4.4 Stages: thresholds 30/55/75; caps Ch2 ≤ S1, Ch3 ≤ S2, door ≤ S3; +1 recovery cap Ch3→door; fall ≤ 1 per check.</summary>
    public class StageEvaluatorTests
    {
        static readonly StageCheck[] Checks = { StageCheck.Chapter2, StageCheck.Chapter3, StageCheck.Door };
        static readonly Stage[] Stages = { Stage.S0, Stage.S1, Stage.S2, Stage.S3 };

        static IEnumerable<float> Rates()
        {
            for (int i = 0; i <= 200; i++) yield return i / 200f;
            foreach (var t in new[] { 0.30f, 0.55f, 0.75f })
            {
                yield return t;
                yield return t - 1e-4f;
                yield return t + 1e-4f;
            }
        }

        [TestCase(0.00f, Stage.S0)]
        [TestCase(0.2999f, Stage.S0)]
        [TestCase(0.30f, Stage.S1)]
        [TestCase(0.5499f, Stage.S1)]
        [TestCase(0.55f, Stage.S2)]
        [TestCase(0.7499f, Stage.S2)]
        [TestCase(0.75f, Stage.S3)]
        [TestCase(1.00f, Stage.S3)]
        public void Bands(float rate, Stage expected) => Assert.AreEqual(expected, StageEvaluator.Band(rate));

        // current, rate, check -> expected
        [TestCase(Stage.S0, 0.90f, StageCheck.Chapter2, Stage.S1, TestName = "Ch2 caps at S1")]
        [TestCase(Stage.S0, 0.29f, StageCheck.Chapter2, Stage.S0, TestName = "Ch2 below 30% stays S0")]
        [TestCase(Stage.S0, 0.60f, StageCheck.Chapter3, Stage.S2, TestName = "Ch3 may rise two (only the door has a recovery cap)")]
        [TestCase(Stage.S1, 0.90f, StageCheck.Chapter3, Stage.S2, TestName = "Ch3 caps at S2")]
        [TestCase(Stage.S0, 0.80f, StageCheck.Door, Stage.S1, TestName = "door: rise at most +1 (S0 to S1)")]
        [TestCase(Stage.S1, 0.80f, StageCheck.Door, Stage.S2, TestName = "door: rise at most +1 (S1 to S2)")]
        [TestCase(Stage.S2, 0.80f, StageCheck.Door, Stage.S3, TestName = "door: S3 only at the door")]
        [TestCase(Stage.S2, 0.10f, StageCheck.Door, Stage.S1, TestName = "fall at most one per check")]
        [TestCase(Stage.S2, 0.10f, StageCheck.Chapter3, Stage.S1, TestName = "fall at most one at Ch3")]
        [TestCase(Stage.S1, 0.40f, StageCheck.Chapter3, Stage.S1, TestName = "rate inside the S1 band holds S1")]
        [TestCase(Stage.S1, 0.20f, StageCheck.Chapter3, Stage.S0, TestName = "below 30% falls to S0")]
        [TestCase(Stage.S3, 0.60f, StageCheck.Door, Stage.S2, TestName = "S3 slipping below 75% falls one")]
        public void Rules(Stage current, float rate, StageCheck check, Stage expected)
            => Assert.AreEqual(expected, StageEvaluator.Evaluate(current, rate, check));

        [Test]
        public void Never_Exceeds_The_Checks_Cap()
        {
            foreach (var c in Checks)
            foreach (var s in Stages)
            foreach (var r in Rates())
                Assert.LessOrEqual((int)StageEvaluator.Evaluate(s, r, c), (int)StageEvaluator.Cap(c), $"{s} {r} {c}");
        }

        static int Effective(Stage s, StageCheck c) => System.Math.Min((int)s, (int)StageEvaluator.Cap(c));

        [Test]
        public void Never_Falls_More_Than_One_Per_Check()
        {
            foreach (var c in Checks)
            foreach (var s in Stages)
            foreach (var r in Rates())
                Assert.GreaterOrEqual((int)StageEvaluator.Evaluate(s, r, c), Effective(s, c) - 1, $"{s} {r} {c}");
        }

        [Test]
        public void Reachable_Stages_Never_Fall_More_Than_One()
        {
            // In a campaign the Stage entering a check never exceeds that check's cap.
            foreach (var c in Checks)
            foreach (var s in Stages)
            {
                if ((int)s > (int)StageEvaluator.Cap(c)) continue;
                foreach (var r in Rates())
                    Assert.GreaterOrEqual((int)StageEvaluator.Evaluate(s, r, c), (int)s - 1, $"{s} {r} {c}");
            }
        }

        [Test]
        public void The_Door_Never_Rises_More_Than_One()
        {
            foreach (var s in Stages)
            foreach (var r in Rates())
                Assert.LessOrEqual((int)StageEvaluator.Evaluate(s, r, StageCheck.Door), (int)s + 1, $"{s} {r}");
        }

        [Test]
        public void Only_Falls_When_The_Rate_Is_Below_The_Current_Band()
        {
            foreach (var c in Checks)
            foreach (var s in Stages)
            foreach (var r in Rates())
                if ((int)StageEvaluator.Evaluate(s, r, c) < Effective(s, c))
                    Assert.Less((int)StageEvaluator.Band(r), Effective(s, c), $"{s} {r} {c}");
        }

        [Test]
        public void Monotonic_In_Rate()
        {
            foreach (var c in Checks)
            foreach (var s in Stages)
            {
                var prev = Stage.S0;
                for (int i = 0; i <= 1000; i++)
                {
                    var st = StageEvaluator.Evaluate(s, i / 1000f, c);
                    Assert.GreaterOrEqual((int)st, (int)prev, $"{s} {c} at {i / 1000f}");
                    prev = st;
                }
            }
        }

        [Test]
        public void Reaches_The_Band_When_Nothing_Limits_It()
        {
            foreach (var c in Checks)
            foreach (var s in Stages)
            foreach (var r in Rates())
            {
                var band = StageEvaluator.Band(r);
                bool capped = band > StageEvaluator.Cap(c);
                bool fallLimited = (int)band < Effective(s, c) - 1;
                bool doorLimited = c == StageCheck.Door && (int)band > Effective(s, c) + 1;
                if (!capped && !fallLimited && !doorLimited)
                    Assert.AreEqual(band, StageEvaluator.Evaluate(s, r, c), $"{s} {r} {c}");
            }
        }

        [Test]
        public void A_Full_Campaign_Path()
        {
            // A strong player from S0: Ch2 check caps at S1, Ch3 reaches S2, the door grants S3.
            var s = StageEvaluator.Evaluate(Stage.S0, 0.80f, StageCheck.Chapter2);
            s = StageEvaluator.Evaluate(s, 0.80f, StageCheck.Chapter3);
            s = StageEvaluator.Evaluate(s, 0.80f, StageCheck.Door);
            Assert.AreEqual(Stage.S3, s);
            // A late bloomer (S0 until Ch3) can only reach S1 at the door, however good the final chapters were.
            s = StageEvaluator.Evaluate(Stage.S0, 0.10f, StageCheck.Chapter2);
            s = StageEvaluator.Evaluate(s, 0.25f, StageCheck.Chapter3);
            s = StageEvaluator.Evaluate(s, 0.95f, StageCheck.Door);
            Assert.AreEqual(Stage.S1, s);
        }
    }
}
