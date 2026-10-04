using System.Collections;
using System.Collections.Generic;
using HS.Core;
using HS.Flow;
using HS.Hero;
using HS.Rapport;
using HS.Rooms;
using HS.Sidekick;
using HS.Skills;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>The campfire (Task 14): hidden Stage check → cold/warm scene, rest, picks, Restore Point round trip.</summary>
    public class CampfireTests
    {
        GameObject _ctxGo;
        SimLoop Loop => SimLoop.Instance;
        RunContext Ctx => RunContext.Current;
        readonly List<GameObject> _extra = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _ctxGo = new GameObject("RunContext");
            _ctxGo.AddComponent<RunContext>();
            SimLoop.Ensure().Paused = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var g in _extra) if (g) Object.DestroyImmediate(g);
            _extra.Clear();
            foreach (var a in Object.FindObjectsByType<Agent>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (a) Object.DestroyImmediate(a.gameObject);
            foreach (var n in new[] { "Chapter", "Campfire", "OpportunityDirector", "StoneSystem", "UIRoot", "CameraRig", "Encounters", "RiggedDuel", "Main Camera", "GameFlow", "AudioDirector" })
            {
                var g = GameObject.Find(n);
                if (g) Object.DestroyImmediate(g);
            }
            foreach (var p in Object.FindObjectsByType<ProjectileSystem>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject);
        }

        (HeroAgent hero, SidekickAgent sk, RoomModule camp) Stage()
        {
            var assets = GameAssets.Load();
            var camp = Object.Instantiate(assets.campfire, new Vector3(300f, 0f, 0f), Quaternion.identity).GetComponent<RoomModule>();
            _extra.Add(camp.gameObject);
            var hero = Object.Instantiate(assets.hero, new Vector3(300f, 0.05f, 2f), Quaternion.identity).GetComponent<HeroAgent>();
            var sk = Object.Instantiate(assets.sidekick, new Vector3(302f, 0.05f, 2f), Quaternion.identity).GetComponent<SidekickAgent>();
            var pc = sk.GetComponent<PlayerCommands>();
            if (pc != null) pc.enabled = false;
            sk.Commands = new ScriptedCommands();
            Ctx.Hero = hero;
            Ctx.Sidekick = sk;
            return (hero, sk, camp);
        }

        CampfireDirector Camp(HeroAgent hero, SidekickAgent sk, RoomModule room, float earned, float offered, int picks = 0, params string[] auto)
        {
            var dir = OpportunityDirector.Create(Ctx, hero);
            _extra.Add(dir.gameObject);
            for (int i = 0; i < offered; i++)
            {
                var m = dir.Ledger.Offer("unseen_assist", 1f);
                if (i < earned) dir.Ledger.Capture(m);
            }
            var c = new GameObject("Campfire").AddComponent<CampfireDirector>();
            _extra.Add(c.gameObject);
            c.AutoSceneSeconds = 0.05f;
            c.Begin(room, hero, sk, picks, true, auto);
            return c;
        }

        [UnityTest]
        public IEnumerator Below_30_Percent_The_Fire_Is_Cold_At_30_It_Is_Warm()
        {
            var (hero, sk, room) = Stage();
            yield return null;
            var cold = Camp(hero, sk, room, 2f, 10f);
            Assert.AreEqual(HS.Core.Stage.S0, hero.Stage);
            Assert.IsFalse(cold.Warm);
            TearDown();
            SetUp();
            (hero, sk, room) = Stage();
            yield return null;
            var warm = Camp(hero, sk, room, 3f, 10f);
            Assert.AreEqual(HS.Core.Stage.S1, hero.Stage, "30%: Stage 1");
            Assert.IsTrue(warm.Warm);
            TearDown();
            SetUp();
            (hero, sk, room) = Stage();
            yield return null;
            Camp(hero, sk, room, 10f, 10f);
            Assert.AreEqual(HS.Core.Stage.S1, hero.Stage, "the slice's check acts as Ch2: never above S1");
        }

        [UnityTest]
        public IEnumerator Rest_Tends_The_Worst_Wound_And_Heals_Both()
        {
            var (hero, sk, room) = Stage();
            yield return null;
            hero.Wounds.Add(WoundType.SprainedAnkle);
            hero.Wounds.Add(WoundType.CrackedRibs);
            hero.Health.SetCurrent(90f);
            sk.Health.SetCurrent(20f);
            var c = Camp(hero, sk, room, 0f, 1f);
            CollectionAssert.AreEqual(new[] { WoundType.SprainedAnkle }, hero.Wounds.All, "the serious wound is tended first");
            Assert.AreEqual(hero.Health.Max, hero.Health.Current, 0.01f);
            Assert.AreEqual(sk.Health.Max, sk.Health.Current, 0.01f);
            StringAssert.Contains("tended", c.RestNote);
        }

        [UnityTest]
        public IEnumerator Level_Up_Picks_Are_Spent_And_Camp_Ends()
        {
            var (hero, sk, room) = Stage();
            yield return null;
            var skills = sk.GetComponent<SidekickSkills>();
            bool done = false;
            var c = Camp(hero, sk, room, 0f, 1f, 2, "quiet_feet", "bandage", "cover_story");
            c.Finished += () => done = true;
            Assert.AreEqual(1, skills.System.RankOf("quiet_feet"));
            Assert.AreEqual(1, skills.System.RankOf("bandage"));
            Assert.AreEqual(0, skills.System.RankOf("cover_story"), "only as many picks as levels");
            Assert.IsTrue(sk.HasQuietFeet, "passives take effect immediately");
            Assert.IsFalse(skills.System.AtCamp);
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(done);
        }

        [UnityTest]
        public IEnumerator Restore_Point_Round_Trip()
        {
            var go = new GameObject("GameFlow");
            var flow = go.AddComponent<GameFlow>();
            flow.AutoPlay = true;
            yield return null; // Start → chapter build + auto opening picks
            yield return null;
            var hero = flow.Chapter.Hero;
            var sk = flow.Chapter.Sidekick;
            var skills = sk.GetComponent<SidekickSkills>();
            var ledger = Ctx.Get<RapportLedger>();
            ledger.Capture(ledger.Offer("averted_cheat", 4f));
            hero.ApplyStage(HS.Core.Stage.S1);
            hero.Wounds.Add(WoundType.Fever);
            var point = flow.Snapshot();
            // the run moves on...
            skills.System.AtCamp = true;
            skills.Learn("bandage");
            skills.System.AtCamp = false;
            ledger.Penalize("caught", 3f);
            hero.ApplyStage(HS.Core.Stage.S0);
            hero.Wounds.Clear();
            // ...then the player restores.
            flow.Apply(point);
            Assert.AreEqual(0, skills.System.RankOf("bandage"), "skills as they were at the point");
            Assert.AreEqual(1, skills.System.RankOf("pocket_sand"));
            Assert.AreEqual(HS.Core.Stage.S1, hero.Stage);
            Assert.AreEqual(0f, ledger.RawPenaltiesIn(1), 1e-3f, "Rapport restored to the snapshot");
            Assert.AreEqual(4f, ledger.Earned, 1e-3f);
            CollectionAssert.AreEqual(new[] { WoundType.Fever }, hero.Wounds.All);
        }

        CampfireDirector CampAt(HeroAgent hero, SidekickAgent sk, RoomModule room, int chapter, float earned, float offered)
        {
            var dir = OpportunityDirector.Create(Ctx, hero);
            _extra.Add(dir.gameObject);
            dir.Ledger.Chapter = chapter;
            for (int i = 0; i < offered; i++)
            {
                var m = dir.Ledger.Offer("unseen_assist", 1f);
                if (i < earned) dir.Ledger.Capture(m);
            }
            var c = new GameObject("Campfire").AddComponent<CampfireDirector>();
            _extra.Add(c.gameObject);
            c.AutoSceneSeconds = 0.05f;
            var rules = CampaignSchedule.For(chapter);
            c.Begin(room, hero, sk, new CampfireDirector.Options { Chapter = chapter, Check = rules.CampCheck, AutoPicks = true });
            return c;
        }

        [UnityTest]
        public IEnumerator Chapter_1_Has_No_Check_Chapter_3_Allows_S2()
        {
            var (hero, sk, room) = Stage();
            yield return null;
            var c1 = CampAt(hero, sk, room, 1, 9f, 10f);
            Assert.AreEqual(HS.Core.Stage.S0, hero.Stage, "no Stage check at the end of chapter 1");
            Assert.AreEqual(CampfireVariant.Neutral, c1.Variant);
            TearDown();
            SetUp();
            (hero, sk, room) = Stage();
            yield return null;
            hero.ApplyStage(HS.Core.Stage.S1);
            var c3 = CampAt(hero, sk, room, 3, 6f, 10f);
            Assert.AreEqual(HS.Core.Stage.S2, hero.Stage, "60% at the chapter 3 check: S2");
            Assert.AreEqual(CampfireVariant.Warm, c3.Variant);
        }

        [Test]
        public void Every_Chapter_And_Variant_Has_Lines_Without_Spoilers()
        {
            foreach (int ch in new[] { 1, 2, 3, 4 })
            foreach (CampfireVariant v in System.Enum.GetValues(typeof(CampfireVariant)))
            {
                var lines = CampfireScenes.Lines(ch, v, false);
                Assert.GreaterOrEqual(lines.Length, 3, $"ch{ch} {v}");
                foreach (var l in lines)
                {
                    Assert.IsTrue(l.Contains("~") || l.Contains("|"), l);
                    foreach (var banned in new[] { "Rapport", "Stage", "Moment", "points" }) StringAssert.DoesNotContain(banned, l);
                }
            }
        }
    }
}
