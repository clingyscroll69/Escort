using System.Collections;
using HS.Core;
using HS.Flow;
using HS.Hero;
using HS.Sidekick;
using HS.Skills;
using HS.Tutorial;
using HS.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>The HUD pass and the tutorial's views (toasts, freeze-frame cards).</summary>
    public class UiTests
    {
        ITutorialStore _savedStore;

        [SetUp]
        public void SetUp()
        {
            _savedStore = TutorialProgress.Store;
            TutorialProgress.Store = new MemoryStore();
            new GameObject("RunContext").AddComponent<RunContext>();
            SimLoop.Ensure().Paused = true;
            ModalGate.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            TestUi.TearDownAll();
            TutorialProgress.Store = _savedStore;
        }

        static (HudView hud, HeroAgent hero, SidekickAgent sk) BuildHudWithPair()
        {
            var assets = GameAssets.Load();
            var hero = Object.Instantiate(assets.hero, new Vector3(0f, 0.05f, 0f), Quaternion.identity).GetComponent<HeroAgent>();
            var sk = Object.Instantiate(assets.sidekick, new Vector3(2f, 0.05f, 0f), Quaternion.identity).GetComponent<SidekickAgent>();
            var pc = sk.GetComponent<PlayerCommands>();
            if (pc != null) pc.enabled = false;
            sk.Commands = new ScriptedCommands();
            var ctx = RunContext.Current;
            ctx.Hero = hero;
            ctx.Sidekick = sk;
            var xp = new XpTracker();
            xp.Bind(ctx);
            ctx.Register(xp);
            var hud = HudView.Create(UIRoot.Ensure());
            return (hud, hero, sk);
        }

        [UnityTest]
        public IEnumerator Hud_Shows_Icons_Keys_And_Level()
        {
            var (hud, _, sk) = BuildHudWithPair();
            sk.GetComponent<SidekickSkills>().Learn("pocket_sand");
            yield return null;
            Assert.AreEqual("skill_pocket_sand", hud.SlotIcon(0));
            Assert.IsNull(hud.SlotIcon(1));
            Assert.AreEqual("LV 1", hud.LevelText);
            Assert.IsFalse(hud.LevelBanked);
            RunContext.Current.Get<XpTracker>().Restore(XpTracker.Thresholds[0]);
            yield return null;
            Assert.IsTrue(hud.LevelBanked, "XP for level 2 is banked until the camp");
            foreach (CoachTarget t in System.Enum.GetValues(typeof(CoachTarget)))
                if (t != CoachTarget.None) Assert.IsNotNull(hud.CoachAnchor(t), t.ToString());
            // The slot's key follows the real binding.
            var key = GameObject.Find("Slot0").transform.Find("Key").GetComponentInChildren<TMPro.TMP_Text>();
            Assert.AreEqual(KeyGlyphs.Label("skill1", KeyGlyphs.Current), key.text);
        }

        [UnityTest]
        public IEnumerator Hud_Shows_His_Rule_And_Wounds()
        {
            var (hud, hero, _) = BuildHudWithPair();
            yield return null;
            hero.Wounds.Add(WoundType.CrackedRibs);
            yield return null;
            var row = hud.CoachAnchor(CoachTarget.WoundChips);
            Assert.AreEqual(1, row.childCount, "one chip per wound");
            StringAssert.Contains("Cracked ribs", row.GetComponentInChildren<TMPro.TMP_Text>().text);
            hud.InsightOn = true;
            yield return null;
            var insight = GameObject.Find("Insight").GetComponent<TMPro.TMP_Text>();
            StringAssert.StartsWith("» ", insight.text, "Hero Insight spells out his current rule");
        }

        [UnityTest]
        public IEnumerator Toast_Shows_Then_Times_Out()
        {
            var view = TipView.Create(UIRoot.Ensure(), null);
            view.ShowToast(Lessons.Get("move"), "Move.", 0.3f);
            yield return null;
            Assert.IsTrue(view.ToastVisible);
            Assert.AreEqual("move", view.ToastId);
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.IsFalse(view.ToastVisible);
        }

        [UnityTest]
        public IEnumerator Focus_Blocks_Clicks_And_Continues()
        {
            var view = TipView.Create(UIRoot.Ensure(), null);
            bool cont = false;
            view.ShowFocus(Lessons.Get("cone"), "What he sees.", () => new Rect(-100f, -100f, 200f, 200f), () => cont = true);
            yield return null;
            yield return null;
            Assert.IsTrue(view.FocusVisible);
            TestUi.Click("FocusContinue");
            yield return null;
            Assert.IsTrue(cont);
            Assert.IsFalse(view.FocusVisible);
        }
    }
}
