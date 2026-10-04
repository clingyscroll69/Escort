using System.Collections;
using HS.Core;
using HS.QA;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>Task 15: determinism and the balance harness itself.</summary>
    public class HarnessTests
    {
        GameObject _go, _stray;

        [TearDown]
        public void TearDown()
        {
            if (_go) Object.Destroy(_go);
            if (_stray) Object.Destroy(_stray);
        }

        BalanceHarness Harness(float maxSim)
        {
            _go = new GameObject("Harness");
            var h = _go.AddComponent<BalanceHarness>();
            h.AutoRun = false;
            h.MaxSimSeconds = maxSim;
            return h;
        }

        [UnityTest]
        public IEnumerator Same_Seed_Same_Bot_Same_State_Hash()
        {
            var h = Harness(50f);
            var a = new BalanceHarness.Row { Seed = 3, Bot = "supportive" };
            var b = new BalanceHarness.Row { Seed = 3, Bot = "supportive" };
            var c = new BalanceHarness.Row { Seed = 4, Bot = "supportive" };
            yield return h.RunOne(a);
            yield return h.RunOne(b);
            yield return h.RunOne(c);
            Debug.Log($"[harness] hashes {a.Hash:x16} {b.Hash:x16} {c.Hash:x16}; sim {a.SimSeconds:0.0}/{b.SimSeconds:0.0}");
            Assert.Greater(a.SimSeconds, 40f, "the run actually played");
            Assert.AreEqual(a.Hash, b.Hash, "same seed + same deterministic bot ⇒ identical state after the same ticks");
            Assert.AreEqual(a.Earned, b.Earned);
            Assert.AreNotEqual(a.Hash, c.Hash, "a different seed is a different road");
        }

        [UnityTest]
        public IEnumerator A_Loop_Left_Running_Does_Not_Drive_The_Run()
        {
            var h = Harness(10f);
            var clean = new BalanceHarness.Row { Seed = 3, Bot = "supportive" };
            yield return h.RunOne(clean);
            // What a test that builds rooms without a TearDown leaves behind: a loop ticking at wall-clock speed.
            _stray = new GameObject("SimLoop");
            var stray = _stray.AddComponent<SimLoop>();
            yield return new WaitForSecondsRealtime(0.2f);
            var after = new BalanceHarness.Row { Seed = 3, Bot = "supportive" };
            yield return h.RunOne(after);
            Assert.AreEqual(clean.Hash, after.Hash, "the run ticked on its own loop from tick 0, not on the stray from wherever it had got to");
            Assert.IsFalse(stray, "the run replaced the stray loop");
        }

        [UnityTest]
        public IEnumerator A_Full_Run_Per_Bot_Completes_And_Writes_A_Row()
        {
            var h = Harness(420f);
            foreach (var bot in new[] { "idle", "sloppy", "supportive" })
            {
                var row = new BalanceHarness.Row { Seed = 1, Bot = bot };
                yield return h.RunOne(row);
                h.Rows.Add(row);
                Debug.Log($"[harness] {bot}: {row.Outcome} reached {row.Reached} rooms {row.RoomsCleared} solo {row.SoloOk}/{row.Encounters} rate@camp {row.RateAtCamp:0.00} stage {row.StageAtCamp} duel {row.DuelResult} {row.DuelSeconds:0}s earned {row.Earned:0}/{row.Offered:0}");
                Assert.AreNotEqual("timeout", row.Outcome, bot + " run finished");
            }
            var csv = BalanceHarness.Csv(h.Rows);
            Assert.AreEqual(4, csv.Trim().Split('\n').Length, "header + 3 rows");
        }
    }
}
