using System.Collections;
using System.Linq;
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
        public IEnumerator Picker_Explains_Demos_And_Pairs_With_Your_Kit()
        {
            var sys = new SkillSystem(SidekickSkills.SliceBehaviours());
            sys.Learn(SkillCatalog.Load().Get("pocket_sand"));
            var p = SkillPicker.Show(UIRoot.Ensure(), sys, 1, "» TEST", false, "GO");
            yield return null;
            Assert.IsNotNull(p.Shown, "something is on show from the start");
            TestUi.Click("Skill_quiet_feet"); // first click reads
            yield return null;
            Assert.AreEqual("quiet_feet", p.Selected);
            Assert.AreEqual(0, sys.RankOf("quiet_feet"), "reading is not learning");
            Assert.IsTrue(p.Demo.Playing);
            Assert.AreEqual("quiet_feet", p.Demo.SkillId);
            CollectionAssert.Contains(p.SynergyPartners, "pocket_sand", "Quiet Feet pairs with the Pocket Sand you own");
            string formed = null;
            p.SynergyFormed += (a, b) => formed = b;
            TestUi.Click("Skill_quiet_feet"); // second click learns
            yield return null;
            Assert.AreEqual(1, sys.RankOf("quiet_feet"));
            Assert.AreEqual("pocket_sand", formed, "learning it completes the pair");
            Assert.IsFalse(GameInput.Instance.Gameplay.enabled, "clicks in the picker never reach the sidekick");
            TestUi.Click("Continue");
            yield return null;
            Assert.IsFalse(p, "the picker closes");
            Assert.IsTrue(GameInput.Instance.Gameplay.enabled);
        }

        [UnityTest]
        public IEnumerator Picker_At_Camp_Edits_The_Loadout_And_Shows_Rank_II()
        {
            var sys = new SkillSystem(SidekickSkills.SliceBehaviours());
            foreach (var id in new[] { "pocket_sand", "crossbow", "bandage" }) sys.Learn(SkillCatalog.Load().Get(id));
            var p = SkillPicker.Show(UIRoot.Ensure(), sys, 1, "» CAMP", true, "ON");
            yield return null;
            Assert.IsTrue(p.ToggleEquip("bandage"), "bench it");
            Assert.IsFalse(sys.Loadout.Contains("bandage"));
            Assert.IsTrue(p.ToggleEquip("bandage"), "and bring it back");
            Assert.IsTrue(sys.Loadout.Contains("bandage"));
            p.Select("crossbow");
            yield return null;
            StringAssert.Contains("RANK II", GameObject.Find("DemoViewport").transform.Find("Header").GetComponent<TMPro.TMP_Text>().text,
                "a skill you know is shown at the rank your next pick gives");
            Assert.IsTrue(p.Pick("crossbow"));
            Assert.AreEqual(2, sys.RankOf("crossbow"));
            p.Continue();
            yield return null;
            Assert.IsFalse(sys.AtCamp);
        }

        [UnityTest]
        public IEnumerator Camp_Picker_Waits_For_The_Fireside_Scene()
        {
            var assets = GameAssets.Load();
            var camp = Object.Instantiate(assets.campfire, new Vector3(300f, 0f, 0f), Quaternion.identity).GetComponent<HS.Rooms.RoomModule>();
            var hero = Object.Instantiate(assets.hero, new Vector3(300f, 0.05f, 2f), Quaternion.identity).GetComponent<HeroAgent>();
            var sk = Object.Instantiate(assets.sidekick, new Vector3(302f, 0.05f, 2f), Quaternion.identity).GetComponent<SidekickAgent>();
            var pc = sk.GetComponent<PlayerCommands>();
            if (pc != null) pc.enabled = false;
            sk.Commands = new ScriptedCommands();
            RunContext.Current.Hero = hero;
            RunContext.Current.Sidekick = sk;
            UIRoot.Ensure();
            var c = new GameObject("Campfire").AddComponent<CampfireDirector>();
            c.Begin(camp, hero, sk, 1, false, new string[0]);
            yield return null;
            Assert.IsNull(c.Picker, "the fireside scene plays first");
            Assert.IsFalse(c.SceneDone);
            Assert.Greater(c.SceneLength, 10f);
            Assert.IsNotNull(GameObject.Find("CampSkip"), "with a way to skip to the level-up");
            c.SkipScene();
            yield return null;
            Assert.IsNotNull(c.Picker, "the level-up opens");
            Assert.IsTrue(c.SceneDone);
            Assert.IsNull(GameObject.Find("CampSkip"));
            Object.Destroy(camp.gameObject);
        }

        [UnityTest]
        public IEnumerator End_Screen_Shows_A_First_Time_Hint()
        {
            var m = new EndScreen.Model { Title = "HERO: CALLUM. DECEASED", Error = true, Hint = Lessons.Get("restore").Body };
            m.Buttons.Add(("QUIT", () => { }));
            EndScreen.Show(UIRoot.Ensure(), m);
            yield return null;
            var hint = GameObject.Find("Hint");
            Assert.IsNotNull(hint);
            StringAssert.Contains("Restore Points", hint.GetComponent<TMPro.TMP_Text>().text);
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
