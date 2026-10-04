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
    }
}
