using HS.Core;
using HS.Flow;
using HS.Hero;
using HS.Rapport;
using HS.Rooms;
using HS.Skills;
using NUnit.Framework;

namespace HS.Tests
{
    /// <summary>The campaign's fixed schedule: tiers, unlocks, slots, checks, XP pots, restore points.</summary>
    public class CampaignScheduleTests
    {
        [Test]
        public void Tiers_Follow_The_GDD_And_Keep_Hits_To_Kill_Constant()
        {
            Assert.AreEqual(1f, ChapterTier.HeroDamage(1), 1e-4f);
            Assert.AreEqual(39.0625f, ChapterTier.HeroDamage(5), 1e-3f, "x2.5 per chapter: about x39 by chapter 5");
            Assert.AreEqual(16f, ChapterTier.HeroHp(5), 1e-4f);
            for (int ch = 1; ch <= 5; ch++)
            {
                Assert.AreEqual(ChapterTier.HeroDamage(ch), ChapterTier.EnemyHp(ch), 1e-3f, "his hits-to-kill stay constant");
                Assert.AreEqual(ChapterTier.HeroHp(ch), ChapterTier.EnemyDamageToHero(ch), 1e-3f, "his hits-to-die stay constant");
                Assert.AreEqual(ChapterTier.EnemyHp(ch), ChapterTier.SidekickDamage(ch), 1e-3f, "her hits-to-kill stay constant");
            }
            Assert.AreEqual(5, ChapterTier.Clamp(9));
            Assert.AreEqual(1, ChapterTier.Clamp(0));
        }

        [Test]
        public void Callum_Tuning_Reads_The_Tier()
        {
            var t = new CallumTuning();
            Assert.AreEqual(26f * 6.25f, t.Damage(3), 1e-3f);
            Assert.AreEqual(260f * 4f, t.MaxHp(3), 1e-3f);
        }

        [Test]
        public void Signature_Unlocks_Accumulate()
        {
            Assert.AreEqual(Signature.StrikeI, CampaignSchedule.For(1).Unlocks);
            Assert.AreEqual(Signature.StrikeI | Signature.StanceI, CampaignSchedule.For(2).Unlocks);
            Assert.AreEqual(Signature.StrikeI | Signature.StanceI | Signature.FinisherI, CampaignSchedule.For(3).Unlocks);
            Assert.IsTrue((CampaignSchedule.For(4).Unlocks & (Signature.StrikeII | Signature.StanceII)) == (Signature.StrikeII | Signature.StanceII));
            Assert.IsTrue((CampaignSchedule.For(5).Unlocks & Signature.FinisherII) != 0);
        }

        [Test]
        public void Slots_Checks_And_Camps_By_Chapter()
        {
            int[] slots = { 4, 5, 6, 6, 6 };
            for (int ch = 1; ch <= 5; ch++)
            {
                Assert.AreEqual(slots[ch - 1], CampaignSchedule.For(ch).Slots);
                Assert.AreEqual(SkillSystem.SlotsForChapter(ch), CampaignSchedule.For(ch).Slots, "one source of truth");
            }
            Assert.IsNull(CampaignSchedule.For(1).CampCheck, "no check at the end of chapter 1");
            Assert.AreEqual(StageCheck.Chapter2, CampaignSchedule.For(2).CampCheck);
            Assert.AreEqual(StageCheck.Chapter3, CampaignSchedule.For(3).CampCheck);
            Assert.IsNull(CampaignSchedule.For(4).CampCheck);
            Assert.IsFalse(CampaignSchedule.For(5).HasCamp, "chapter 5 ends at the door");
            Assert.IsTrue(CampaignSchedule.For(2).LearnsRecall);
            Assert.IsTrue(CampaignSchedule.For(4).CapstoneReveal);
            Assert.AreEqual(0.5f, CampaignSchedule.For(1).Recovery, 1e-4f);
            Assert.AreEqual(0.3f, CampaignSchedule.For(3).Recovery, 1e-4f);
            Assert.AreEqual("Whisperwood", CampaignSchedule.For(2).Name);
        }

        [Test]
        public void Full_Pots_Reach_Level_3_6_9_13()
        {
            int xp = 0;
            for (int ch = 1; ch <= 4; ch++)
            {
                int rooms = ChapterDef.For(ch).Slots.Count;
                for (int r = 0; r < rooms; r++) xp += CampaignSchedule.RoomPot(ch, 100);
                Assert.AreEqual(CampaignSchedule.LevelTarget(ch), XpTracker.LevelFor(xp), $"end of chapter {ch} ({xp} xp)");
            }
            Assert.AreEqual(0, CampaignSchedule.RoomPot(5, 100), "no levels in the Gallery");
        }

        [Test]
        public void Xp_Pots_Count_Each_Chapter_Afresh()
        {
            var go = new UnityEngine.GameObject("RunContext");
            try
            {
                var ctx = go.AddComponent<RunContext>();
                ctx.Init();
                var xp = new XpTracker();
                xp.Bind(ctx);
                xp.BeginChapter(r => 100);
                ctx.Events.RoomEntered(0);
                ctx.Events.RoomCleared(0);
                Assert.AreEqual(50, xp.Xp, "half the pot for the clear");
                xp.BeginChapter(r => 180);
                ctx.Events.RoomEntered(0);
                ctx.Events.RoomCleared(0);
                Assert.AreEqual(140, xp.Xp, "room 0 of the next chapter pays its own pot");
                ctx.Events.Explored(0, "cache");
                Assert.AreEqual(185, xp.Xp, "a quarter for exploring");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                if (SimLoop.Instance) UnityEngine.Object.DestroyImmediate(SimLoop.Instance.gameObject);
            }
        }

        [Test]
        public void Restore_Points_Resolve_By_Name()
        {
            RunState.Clear();
            var p2 = new RunState.Point();
            var p3 = new RunState.Point();
            var door = new RunState.Point { Chapter = 5 };
            RunState.SetChapterStart(2, p2);
            RunState.SetChapterStart(3, p3);
            RunState.Door = door;
            Assert.AreSame(p3, RunState.ChapterStart, "the latest chapter start");
            Assert.AreSame(p2, RunState.Resolve("chapter:2"));
            Assert.AreSame(p3, RunState.Resolve("chapter"));
            Assert.AreSame(door, RunState.Resolve("door"));
            Assert.AreEqual(3, p3.Chapter);
            RunState.ForgetAfter(2);
            Assert.IsNull(RunState.ChapterStartOf(3), "restoring to chapter 2 forgets the later timeline");
            Assert.IsNull(RunState.Door);
            RunState.Clear();
        }
    }
}
