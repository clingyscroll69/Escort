using HS.Boss;
using HS.Core;
using NUnit.Framework;

namespace HS.Tests
{
    /// <summary>The Mirror's counters by Stage and its numbers (campaign spec §5; GDD §4.5a).</summary>
    public class MirrorCountersTests
    {
        [Test]
        public void One_Counter_For_Each_S0_Rule_He_Still_Shares()
        {
            Assert.AreEqual(MirrorCounter.Feint | MirrorCounter.Goad | MirrorCounter.Terms, MirrorCounters.For(Stage.S0));
            Assert.AreEqual(MirrorCounter.Feint | MirrorCounter.Goad, MirrorCounters.For(Stage.S1));
            Assert.AreEqual(MirrorCounter.Feint, MirrorCounters.For(Stage.S2));
            Assert.AreEqual(MirrorCounter.None, MirrorCounters.For(Stage.S3), "an S3 hero shares none: it just fights its old self");
        }

        [Test]
        public void Habit_Breaks_By_Stage()
        {
            Assert.IsFalse(MirrorCounters.EtiquetteHeard(Stage.S0), "too busy fighting to listen");
            Assert.IsTrue(MirrorCounters.EtiquetteHeard(Stage.S1));
            Assert.IsFalse(MirrorCounters.Dishonour(Stage.S2));
            Assert.IsTrue(MirrorCounters.Dishonour(Stage.S3), "cheat a cheater");
        }

        [Test]
        public void Engagers_Count_A_Decoy_Twice()
        {
            Assert.AreEqual(1, MirrorCounters.Engagers(true, false, false));
            Assert.AreEqual(2, MirrorCounters.Engagers(true, true, false), "him and her are not enough");
            Assert.AreEqual(3, MirrorCounters.Engagers(true, false, true), "him and a decoy are");
            Assert.GreaterOrEqual(MirrorCounters.Engagers(true, false, true), MirrorCounters.NicheEngagers);
        }

        [Test]
        public void The_Link_Window_By_Capstone()
        {
            Assert.AreEqual(1f, MirrorCounters.LinkWindow(null));
            Assert.AreEqual(1f, MirrorCounters.LinkWindow("crossfire"));
            Assert.AreEqual(1f, MirrorCounters.LinkWindow("domino_effect"));
            Assert.AreEqual(2f, MirrorCounters.LinkWindow("hold_please"));
            Assert.AreEqual(1.6f, MirrorCounters.LinkWindow("silent_partner"), 1e-4f);
        }

        [Test]
        public void Dishonour_Cuts_A_Quarter_A_Break_To_Half()
        {
            Assert.AreEqual(1f, MirrorCounters.DamageAfterDishonour(0), 1e-4f);
            Assert.AreEqual(0.75f, MirrorCounters.DamageAfterDishonour(1), 1e-4f);
            Assert.AreEqual(0.5f, MirrorCounters.DamageAfterDishonour(2), 1e-4f);
            Assert.AreEqual(0.5f, MirrorCounters.DamageAfterDishonour(4), 1e-4f);
        }

        [Test]
        public void Its_HP_Holds_About_20s_Of_His_Chapter_5_Damage()
        {
            var t = Tuning.LoadDefault();
            var mirror = t.Enemy("mirror");
            float hp = mirror.maxHp * ChapterTier.EnemyHp(5);
            float perBlow = t.callum.Damage(5) * MirrorCounters.HeroBlowMul;
            float dps = perBlow / (t.callum.attackWindup + t.callum.attackRecovery);
            Assert.That(hp / dps, Is.InRange(16f, 24f), "GDD §4.5a: about 20 s of his sustained damage");
            Assert.IsTrue(mirror.cheater, "a cheat by construction: S3 owes it no courtesy");
        }

        [Test]
        public void Its_Icons_Are_His_S0_Rules()
        {
            Assert.AreEqual("challenge", MirrorCounters.Icon(MirrorBrain.Mode.Salute));
            Assert.AreEqual("wait", MirrorCounters.Icon(MirrorBrain.Mode.Wait));
            Assert.AreEqual("fallback", MirrorCounters.Icon(MirrorBrain.Mode.Niche));
            Assert.AreEqual("fight", MirrorCounters.Icon(MirrorBrain.Mode.Fight));
            Assert.AreEqual("fight", MirrorCounters.Icon(MirrorBrain.Mode.Feint), "a feint shows what it is really doing");
        }
    }
}
