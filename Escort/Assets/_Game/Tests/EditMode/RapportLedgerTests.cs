using HS.Rapport;
using NUnit.Framework;

namespace HS.Tests
{
    public class RapportLedgerTests
    {
        static RapportLedger Ledger(int chapter = 1) => new RapportLedger { Chapter = chapter };

        [Test]
        public void CaptureRate_Is_Earned_Over_Offered_So_Far()
        {
            var l = Ledger();
            var a = l.Offer("unseen_assist", 3f);
            var b = l.Offer("averted_cheat", 4f);
            l.Offer("covered_lapse", 2f);
            Assert.IsTrue(l.Capture(a));
            Assert.IsTrue(l.Capture(b));
            Assert.AreEqual(9f, l.Offered, 1e-4f);
            Assert.AreEqual(7f, l.Earned, 1e-4f);
            Assert.AreEqual(7f / 9f, l.CaptureRate, 1e-4f);
        }

        [Test]
        public void A_Moment_Captures_Once_And_Only_While_Open()
        {
            var l = Ledger();
            var a = l.Offer("unseen_assist", 3f);
            Assert.IsTrue(l.Capture(a));
            Assert.IsFalse(l.Capture(a), "double capture");
            var b = l.Offer("averted_cheat", 4f);
            l.Close(b);
            Assert.IsFalse(l.Capture(b), "closed window");
            Assert.AreEqual(3f, l.Earned, 1e-4f);
        }

        [Test]
        public void Uncaptured_Moments_Still_Count_As_Offered()
        {
            var l = Ledger();
            var a = l.Offer("averted_cheat", 4f);
            l.Close(a);
            l.Offer("unseen_assist", 3f);
            Assert.AreEqual(7f, l.Offered, 1e-4f);
            Assert.AreEqual(0f, l.CaptureRate, 1e-4f, "neglect earns zero, not a negative");
        }

        [Test]
        public void Penalties_Are_Capped_At_30_Percent_Of_The_Chapters_Offered_Points()
        {
            var l = Ledger();
            for (int i = 0; i < 5; i++) l.Capture(l.Offer("unseen_assist", 2f)); // 10 offered, 10 earned
            l.Penalize("caught", 3f);
            Assert.AreEqual(3f, l.EffectivePenalties, 1e-4f);
            l.Penalize("caught", 6f);
            Assert.AreEqual(3f, l.EffectivePenalties, 1e-4f, "cap = 30% of 10");
            Assert.AreEqual((10f - 3f) / 10f, l.CaptureRate, 1e-4f);
            l.Offer("averted_cheat", 10f);
            Assert.AreEqual(6f, l.EffectivePenalties, 1e-4f, "the cap grows with the offer");
        }

        [Test]
        public void The_Cap_Is_Per_Chapter()
        {
            var l = Ledger(1);
            l.Capture(l.Offer("unseen_assist", 10f));
            l.Penalize("caught", 9f); // capped at 3
            l.Chapter = 2;
            l.Capture(l.Offer("unseen_assist", 10f));
            Assert.AreEqual(3f, l.EffectivePenalties, 1e-4f, "chapter 1's excess does not spill into chapter 2's headroom");
            Assert.AreEqual((20f - 3f) / 20f, l.CaptureRate, 1e-4f);
        }

        [Test]
        public void The_Rate_Never_Goes_Negative()
        {
            var l = Ledger();
            l.Offer("unseen_assist", 3f);
            l.Penalize("friendly_fire", 3f);
            Assert.AreEqual(0f, l.CaptureRate, 1e-4f);
        }

        [Test]
        public void Offers_Stop_At_The_Chapter_Budget()
        {
            var l = Ledger();
            Assert.AreEqual(60f, l.Budget(1));
            Assert.AreEqual(90f, l.Budget(2));
            Assert.AreEqual(40f, l.Budget(5));
            for (int i = 0; i < 15; i++) Assert.IsNotNull(l.Offer("averted_cheat", 4f));
            Assert.IsNull(l.Offer("unseen_assist", 3f), "60 offered: the director is out of budget");
            Assert.AreEqual(60f, l.Offered, 1e-4f);
        }

        [Test]
        public void Refunded_Penalties_No_Longer_Count()
        {
            var l = Ledger();
            l.Capture(l.Offer("unseen_assist", 10f));
            var p = l.Penalize("caught", 3f);
            Assert.IsTrue(l.Refund(p));
            Assert.IsFalse(l.Refund(p));
            Assert.AreEqual(0f, l.EffectivePenalties, 1e-4f);
        }

        [Test]
        public void Snapshot_Restore_Round_Trips()
        {
            var l = Ledger();
            l.Capture(l.Offer("unseen_assist", 3f));
            l.Offer("averted_cheat", 4f);
            var snap = l.Snapshot();
            l.Penalize("caught", 3f);
            l.Capture(l.Offer("covered_lapse", 2f));
            l.Restore(snap);
            Assert.AreEqual(7f, l.Offered, 1e-4f);
            Assert.AreEqual(3f, l.Earned, 1e-4f);
            Assert.AreEqual(0f, l.EffectivePenalties, 1e-4f);
            Assert.AreEqual(3, l.Log.Count, "log rewinds too (offer, capture, offer)");
        }

        [Test]
        public void Withdrawn_Offers_Leave_The_Denominator_But_Captures_Cannot_Be_Withdrawn()
        {
            var l = Ledger();
            var a = l.Offer("unseen_assist", 3f);
            var b = l.Offer("unseen_assist", 3f);
            l.Capture(b);
            Assert.IsTrue(l.Withdraw(a));
            Assert.IsFalse(l.Withdraw(b), "a capture stands");
            Assert.AreEqual(3f, l.Offered, 1e-4f);
            Assert.AreEqual(1f, l.CaptureRate, 1e-4f);
        }
    }
}
