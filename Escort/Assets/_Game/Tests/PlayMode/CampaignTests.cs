using System.Collections;
using HS.Core;
using HS.Enemies;
using HS.Flow;
using HS.Hero;
using HS.Hero.Callum;
using HS.Sidekick;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>The campaign spine: tiers, chapter rebuilds, the chapter loop, restore points.</summary>
    public class CampaignTests
    {
        [TearDown]
        public void TearDown() => TestUi.TearDownAll();

        static RunContext Ctx(int chapter)
        {
            var ctx = new GameObject("RunContext").AddComponent<RunContext>();
            ctx.Chapter = chapter;
            SimLoop.Ensure().Paused = true;
            return ctx;
        }

        [UnityTest]
        public IEnumerator Enemies_And_The_Pair_Scale_With_The_Chapter()
        {
            var ctx = Ctx(3);
            var assets = GameAssets.Load();
            var thug = Object.Instantiate(assets.Enemy("thug"), new Vector3(0f, 0.05f, 5f), Quaternion.identity).GetComponent<EnemyAgent>();
            var hero = Object.Instantiate(assets.hero, new Vector3(0f, 0.05f, 0f), Quaternion.identity).GetComponent<HeroAgent>();
            var sk = Object.Instantiate(assets.sidekick, new Vector3(2f, 0.05f, 0f), Quaternion.identity).GetComponent<SidekickAgent>();
            ctx.Hero = hero;
            ctx.Sidekick = sk;
            yield return null;
            Assert.AreEqual(80f * 6.25f, thug.Health.Max, 0.01f, "enemy HP x2.5 per chapter");
            float before = hero.Health.Current;
            hero.TakeDamage(DamageInfo.Make(thug, hero, 10f, DamageKind.Melee, "melee"));
            Assert.AreEqual(40f, before - hero.Health.Current, 0.01f, "enemy damage to him x2 per chapter");
            before = thug.Health.Current;
            thug.TakeDamage(DamageInfo.Make(sk, thug, 8f, DamageKind.Knife, "knife"));
            Assert.AreEqual(50f, before - thug.Health.Current, 0.01f, "her damage x2.5 per chapter");
            float skBefore = sk.Health.Current;
            sk.TakeDamage(DamageInfo.Make(thug, sk, 10f, DamageKind.Melee, "melee"));
            Assert.AreEqual(10f, skBefore - sk.Health.Current, 0.01f, "her own HP grows with levels, not chapters");
        }

        [UnityTest]
        public IEnumerator Callum_Takes_His_Chapter()
        {
            Ctx(4);
            var hero = Object.Instantiate(GameAssets.Load().hero, Vector3.zero, Quaternion.identity).GetComponent<HeroAgent>();
            yield return null;
            var cm = (CallumModule)hero.Module;
            var rules = CampaignSchedule.For(4);
            cm.ApplyChapter(4, rules.Unlocks, rules.Recovery);
            Assert.AreEqual(260f * 8f, hero.Health.Max, 0.01f);
            Assert.AreEqual(hero.Health.Max, hero.Health.Current, 0.01f, "a new chapter starts rested");
            Assert.AreEqual(3f, cm.RiposteMultiplier, 1e-4f, "Strike II");
            Assert.AreEqual(0.3f, cm.Recovery, 1e-4f);
            cm.ApplyChapter(1, CampaignSchedule.For(1).Unlocks, 0.5f);
            Assert.AreEqual(2f, cm.RiposteMultiplier, 1e-4f, "Strike I");
        }

        [UnityTest]
        public IEnumerator A_Chapter_Theme_Changes_The_Light_And_The_Old_Road_Restores_The_Scene()
        {
            HS.Presentation.ChapterTheme.ForgetSceneDefaults();
            var sun = new GameObject("TestSun").AddComponent<Light>();
            sun.type = LightType.Directional;
            RenderSettings.sun = sun;
            sun.color = Color.white;
            RenderSettings.fog = false;
            RenderSettings.fogColor = Color.magenta;
            yield return null;
            HS.Presentation.ChapterTheme.Apply("catacombs");
            Assert.IsTrue(RenderSettings.fog);
            Assert.AreNotEqual(Color.white, sun.color);
            HS.Presentation.ChapterTheme.Apply("old_road");
            Assert.IsFalse(RenderSettings.fog, "chapter 1 is the scene as authored");
            Assert.AreEqual(Color.magenta, RenderSettings.fogColor);
            Assert.AreEqual(Color.white, sun.color);
            HS.Presentation.ChapterTheme.ForgetSceneDefaults();
        }

        [UnityTest]
        public IEnumerator Chapters_Rebuild_In_Place_And_The_Pair_Carries_Over()
        {
            Ctx(1);
            var boot = new GameObject("GameFlow").AddComponent<ChapterBootstrap>();
            boot.AutoBuild = false;
            boot.Seed = 5;
            boot.BuildRun();
            boot.BuildChapter(1, 5);
            yield return null;
            var hero = boot.Hero;
            var sk = boot.Sidekick;
            Assert.AreEqual(3, boot.Chapter.Rooms.Count);
            hero.Motor.Teleport(new Vector3(0f, 0.05f, 60f));
            boot.TeardownChapter();
            boot.BuildChapter(2, 5);
            yield return null;
            Assert.AreSame(hero, boot.Hero, "the same Callum");
            Assert.AreSame(sk, boot.Sidekick);
            Assert.AreEqual(2, RunContext.Current.Chapter);
            Assert.AreEqual(2, boot.Director.Ledger.Chapter, "the ledger books chapter 2's offers");
            Assert.AreEqual(4, boot.Chapter.Rooms.Count);
            Assert.AreEqual("whisperwood", boot.Def.Theme);
            Assert.Less(hero.Position.z, 1f, "he starts at the new road's start");
            Assert.Greater(hero.Route.Nodes.Count, 12);
            Assert.AreEqual(1, Object.FindObjectsByType<HS.UI.HudView>(FindObjectsSortMode.None).Length, "one HUD for the whole run");
            var now = new System.Collections.Generic.List<EnemyAgent>(boot.Encounters.AllEnemies);
            foreach (var e in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
                CollectionAssert.Contains(now, e, "no chapter 1 bandit survives the rebuild");
            HS.Presentation.ChapterTheme.ForgetSceneDefaults();
        }

        static GameFlow Flow(int startChapter = 1, int stopAfter = 0)
        {
            var go = new GameObject("GameFlow");
            go.SetActive(false);
            var flow = go.AddComponent<GameFlow>();
            flow.AutoPlay = true;
            flow.Fast = true;
            flow.StartChapter = startChapter;
            flow.StopAfterChapter = stopAfter;
            go.SetActive(true);
            return flow;
        }

        /// <summary>Skip the road: no bandits, the hero at its end (exercises the loop, not the fights).</summary>
        static void SkipRoad(GameFlow flow)
        {
            foreach (var e in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None)) e.gameObject.SetActive(false);
            var hero = flow.Chapter.Hero;
            hero.Route.SetNodes(new System.Collections.Generic.List<RouteNode>());
            hero.Motor.Teleport(new Vector3(0f, 0.05f, flow.Chapter.Chapter.ChapterLength - 2f));
        }

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator The_Campaign_Runs_Chapter_To_Chapter_Into_The_Gallery()
        {
            new GameObject("RunContext").AddComponent<RunContext>();
            var flow = Flow();
            yield return null;
            flow.Chapter.Hero.Health.Invulnerable = true;
            var seen = new System.Collections.Generic.List<int>();
            for (int ch = 1; ch <= 5; ch++)
            {
                yield return TestUi.WaitUntil(() => flow.Current == GameFlow.State.Chapter && flow.CurrentChapter == ch, 20f, "chapter " + ch);
                seen.Add(flow.CurrentChapter);
                var skills = flow.Chapter.Sidekick.GetComponent<HS.Skills.SidekickSkills>();
                Assert.AreEqual(CampaignSchedule.For(ch).Slots, skills.System.SlotCount, "slots in chapter " + ch);
                Assert.AreEqual(260f * Mathf.Pow(2f, ch - 1), flow.Chapter.Hero.Health.BaseMax, 0.01f, "his HP tier in chapter " + ch);
                Assert.IsNotNull(RunState.ChapterStartOf(ch), "a Restore Point at chapter " + ch);
                SkipRoad(flow);
            }
            yield return TestUi.WaitUntil(() => flow.Current == GameFlow.State.Duel, 20f, "the door, then the boss");
            Assert.IsNotNull(RunState.Door, "a Restore Point before the door");
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, seen);
            flow.Duel.Ashgrave.TakeDamage(DamageInfo.Make(flow.Chapter.Hero, flow.Duel.Ashgrave, 1e9f, DamageKind.Blade, "sword"));
            yield return TestUi.WaitUntil(() => flow.Current == GameFlow.State.End, 10f, "the end");
            Assert.AreEqual("won", flow.Outcome);
            HS.Presentation.ChapterTheme.ForgetSceneDefaults();
        }

        [UnityTest]
        public IEnumerator Starting_At_Chapter_4_Skips_The_Opening_With_A_Fitting_Kit()
        {
            new GameObject("RunContext").AddComponent<RunContext>();
            var flow = Flow(4);
            yield return null;
            Assert.AreEqual(GameFlow.State.Chapter, flow.Current);
            Assert.AreEqual(4, RunContext.Current.Chapter);
            Assert.AreEqual(CampaignSchedule.LevelTarget(3), flow.Chapter.Sidekick.Level);
            Assert.AreEqual(6, flow.Chapter.Sidekick.GetComponent<HS.Skills.SidekickSkills>().System.SlotCount);
            Assert.AreEqual(260f * 8f, flow.Chapter.Hero.Health.BaseMax, 0.01f);
            Assert.IsTrue(RenderSettings.fog);
            HS.Presentation.ChapterTheme.ForgetSceneDefaults();
        }

        [UnityTest]
        public IEnumerator A_Chapter_3_Restore_Point_Brings_Back_Chapter_3()
        {
            new GameObject("RunContext").AddComponent<RunContext>();
            var flow = Flow(3);
            yield return null;
            var point = RunState.ChapterStartOf(3);
            Assert.IsNotNull(point);
            Assert.AreEqual(3, point.Chapter);
            var hero = flow.Chapter.Hero;
            hero.ApplyStage(HS.Core.Stage.S2);
            var later = flow.Snapshot();
            Assert.AreEqual(3, later.Chapter);
            flow.Apply(point);
            Assert.AreEqual(HS.Core.Stage.S0, hero.Stage);
            Assert.AreEqual(6, flow.Chapter.Sidekick.GetComponent<HS.Skills.SidekickSkills>().System.SlotCount);
            HS.Presentation.ChapterTheme.ForgetSceneDefaults();
        }

        [UnityTest]
        public IEnumerator Stop_After_Chapter_Ends_The_Run_At_Its_Campfire()
        {
            new GameObject("RunContext").AddComponent<RunContext>();
            var flow = Flow(1, 1);
            yield return null;
            SkipRoad(flow);
            yield return TestUi.WaitUntil(() => flow.Current == GameFlow.State.End, 20f, "the end of chapter 1");
            Assert.AreEqual("chapter_done", flow.Outcome);
        }
    }
}
