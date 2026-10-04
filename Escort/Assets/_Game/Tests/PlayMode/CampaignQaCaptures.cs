using System.Collections;
using HS.Core;
using HS.Flow;
using HS.QA;
using HS.Tutorial;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>
    /// Visual QA for the campaign spine: the real Main scene started at each chapter (its light, its rooms), a bot playing
    /// the sidekick, captured to docs/qa/shots/campaign_ch*.png. Category QA: run on purpose, then look at the pictures.
    /// </summary>
    [Category("QA")]
    public class CampaignQaCaptures
    {
        const string MainScene = "Assets/_Game/Scenes/Main.unity";
        ITutorialStore _savedStore;

        [SetUp]
        public void SetUp()
        {
            _savedStore = TutorialProgress.Store;
            TutorialProgress.Store = new MemoryStore();
            TutorialProgress.TipsEnabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            TutorialProgress.Store = _savedStore;
            ModalGate.Clear();
            RunState.Clear();
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Each_Chapter_Under_Its_Light()
        {
            for (int ch = 1; ch <= 5; ch++)
            {
                if (ch == 1)
                {
                    var point = new RunState.Point { Chapter = 1, Seed = 2, Level = 1 };
                    point.Skills.Add(("pocket_sand", 1));
                    point.Loadout.Add("pocket_sand");
                    RunState.SetChapterStart(1, point);
                    RunState.Resume = "chapter:1";
                }
#if UNITY_EDITOR
                else UnityEditor.SessionState.SetInt(GameFlow.PlayFromChapterKey, ch);
                RunState.Runs = 1;
                yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MainScene, new LoadSceneParameters(LoadSceneMode.Single));
#endif
                yield return null;
                var flow = Object.FindAnyObjectByType<GameFlow>();
                var sk = flow.Chapter.Sidekick;
                var pc = sk.GetComponent<HS.Sidekick.PlayerCommands>();
                if (pc != null) pc.enabled = false;
                sk.Commands = HS.Bots.BotFactory.Make("follow", sk);
                yield return new WaitForSecondsRealtime(5f);
                Assert.AreEqual(ch, flow.CurrentChapter);
                QaCapture.Capture(Camera.main, "campaign_ch" + ch, 1600, 900);
            }
        }

        /// <summary>The Whisperwood cast stood in front of the chapter 2 camera (poacher, woodsman, fern ambusher, Mr. Quill).</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Whisperwood_Cast()
        {
#if UNITY_EDITOR
            UnityEditor.SessionState.SetInt(GameFlow.PlayFromChapterKey, 2);
            RunState.Runs = 1;
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MainScene, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            var flow = Object.FindAnyObjectByType<GameFlow>();
            SimLoop.Instance.Paused = true;
            foreach (var e in Object.FindObjectsByType<HS.Enemies.EnemyAgent>(FindObjectsSortMode.None)) e.gameObject.SetActive(false);
            var assets = GameAssets.Load();
            var hero = flow.Chapter.Hero;
            var basePos = hero.Position + new Vector3(-3f, 0f, 3f);
            string[] ids = { "poacher", "woodsman", "fern_ambusher", "scout_quill" };
            for (int i = 0; i < ids.Length; i++)
            {
                var go = Object.Instantiate(assets.Enemy(ids[i]), basePos + new Vector3(i * 2f, 0.05f, 0f), Quaternion.Euler(0f, 180f, 0f));
                if (go.TryGetComponent<HS.Enemies.EnemyAgent>(out var e)) e.Scripted = true;
            }
            yield return new WaitForSecondsRealtime(2.5f);
            QaCapture.Capture(Camera.main, "ch2_cast", 1600, 900);
        }
    }
}
