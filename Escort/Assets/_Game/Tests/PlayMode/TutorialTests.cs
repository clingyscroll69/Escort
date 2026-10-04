using System.Collections;
using System.Collections.Generic;
using HS.Core;
using HS.Flow;
using HS.Tutorial;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>The learn-as-you-go tutorial in a real chapter: when lessons fire, the freeze-frame pause, seen-once.</summary>
    public class TutorialTests
    {
        ITutorialStore _savedStore;

        [SetUp]
        public void SetUp()
        {
            _savedStore = TutorialProgress.Store;
            TutorialProgress.Store = new MemoryStore();
            new GameObject("RunContext").AddComponent<RunContext>();
            SimLoop.Ensure();
            ModalGate.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            TestUi.TearDownAll();
            TutorialProgress.Store = _savedStore;
        }

        static TutorialDirector Director() => Object.FindAnyObjectByType<TutorialDirector>();

        [UnityTest]
        public IEnumerator First_Threshold_Pause_Freezes_On_His_Rules()
        {
            var flow = TestUi.StartChapterAsPlayer();
            yield return null;
            TestUi.Script(flow);
            yield return TestUi.WaitUntil(() => flow.Chapter.Hero.ActiveRuleId == "threshold_pause", 30f, "his first threshold pause");
            var dir = Director();
            yield return TestUi.WaitUntil(() => dir.Showing == "hero_rules", 2f, "the hero_rules lesson");
            Assert.IsTrue(dir.FocusOpen);
            Assert.IsTrue(SimLoop.Instance.Paused, "a freeze-frame lesson pauses the road");
            Assert.IsFalse(GameInput.Instance.Gameplay.enabled, "the continue key can't also dodge");
            int tick = SimLoop.Instance.TickIndex;
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(tick, SimLoop.Instance.TickIndex, "nothing moves while he's being explained");
            dir.ContinueFocus();
            yield return null;
            Assert.IsFalse(SimLoop.Instance.Paused);
            Assert.IsTrue(GameInput.Instance.Gameplay.enabled);
            Assert.IsTrue(TutorialProgress.IsSeen("hero_rules"));
            yield return TestUi.WaitUntil(() => dir.Showing == "insight", 4f, "its follow-up tip (Tab)");
        }

        [UnityTest]
        public IEnumerator Seen_Lessons_Never_Repeat_And_Tips_Off_Is_Silent()
        {
            TutorialProgress.MarkSeen("hero_rules");
            var flow = TestUi.StartChapterAsPlayer();
            yield return null;
            TestUi.Script(flow);
            yield return TestUi.WaitUntil(() => flow.Chapter.Hero.ActiveRuleId == "threshold_pause", 30f, "his first threshold pause");
            yield return new WaitForSecondsRealtime(0.6f);
            var dir = Director();
            Assert.AreNotEqual("hero_rules", dir.Showing);
            Assert.IsFalse(dir.FocusOpen);
            Assert.IsFalse(SimLoop.Instance.Paused);

            TutorialProgress.TipsEnabled = false;
            Assert.IsFalse(dir.Offer("cone"), "tips off: nothing is offered");
        }

        [UnityTest]
        public IEnumerator Lesson_Pauses_Off_Turns_Focus_Into_A_Tip()
        {
            TutorialProgress.LessonPauses = false;
            var flow = TestUi.StartChapterAsPlayer();
            yield return null;
            TestUi.Script(flow);
            yield return TestUi.WaitUntil(() => flow.Chapter.Hero.ActiveRuleId == "threshold_pause", 30f, "his first threshold pause");
            var dir = Director();
            yield return TestUi.WaitUntil(() => dir.Showing == "hero_rules", 14f, "the hero_rules lesson");
            Assert.IsFalse(dir.FocusOpen);
            Assert.IsFalse(SimLoop.Instance.Paused);
        }

        [UnityTest]
        public IEnumerator Move_Tip_Completes_When_You_Move()
        {
            TutorialProgress.MarkSeen("hero_rules"); // no freeze-frame interrupting this one
            var flow = TestUi.StartChapterAsPlayer();
            yield return null;
            var script = TestUi.Script(flow);
            var dir = Director();
            var completed = new List<string>();
            dir.Completed += completed.Add;
            yield return TestUi.WaitUntil(() => dir.Showing == "move", 6f, "the move tip");
            script.Current.Move = Vector3.right;
            yield return TestUi.WaitUntil(() => completed.Contains("move"), 4f, "the move tip to complete");
            script.Current.Move = Vector3.zero;
            yield return TestUi.WaitUntil(() => dir.Showing != "move", 3f, "the tip to leave");
        }

        [UnityTest]
        public IEnumerator Knowing_It_Already_Means_It_Never_Shows()
        {
            TutorialProgress.MarkSeen("hero_rules");
            var flow = TestUi.StartChapterAsPlayer();
            yield return null;
            TestUi.Script(flow);
            var dir = Director();
            Assert.IsTrue(dir.Offer("dodge"));
            dir.Complete("dodge");
            Assert.IsTrue(TutorialProgress.IsSeen("dodge"), "they did it before the tip's turn came");
            Assert.IsFalse(dir.IsQueued("dodge"));
        }

        [UnityTest]
        public IEnumerator Bots_Get_No_Tutorial()
        {
            var go = new GameObject("GameFlow");
            var flow = go.AddComponent<GameFlow>();
            flow.AutoPlay = true;
            flow.Fast = true;
            yield return null;
            Assert.IsNull(Director());
            Assert.IsNull(flow.Tutorial);
        }

        [UnityTest]
        public IEnumerator The_Hunger_Lesson_Fires_When_He_First_Gets_Hungry()
        {
            var point = new RunState.Point { Chapter = 2, Seed = 1, Level = 3 };
            point.Skills.Add(("pocket_sand", 1));
            point.Loadout.Add("pocket_sand");
            RunState.SetChapterStart(2, point);
            RunState.Resume = "chapter:2";
            var flow = new GameObject("GameFlow").AddComponent<GameFlow>();
            yield return null;
            TestUi.Script(flow);
            foreach (var id in new[] { "move", "welcome", "hero_rules", "insight", "cone" }) TutorialProgress.MarkSeen(id);
            var dir = Director();
            Assert.IsFalse(dir.IsQueued("hunger") || dir.Showing == "hunger");
            flow.Chapter.Hero.Hunger.Restore(10f);
            yield return TestUi.WaitUntil(() => dir.IsQueued("hunger") || dir.Showing == "hunger" || TutorialProgress.IsSeen("hunger"), 5f, "the hunger lesson");
            HS.Presentation.ChapterTheme.ForgetSceneDefaults();
        }
    }
}
