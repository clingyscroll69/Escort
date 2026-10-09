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

        /// <summary>Every Whisperwood module, staged mid-room (sim paused): docs/qa/shots/ch2_&lt;module&gt;.png.</summary>
        [UnityTest]
        [Timeout(240000)]
        public IEnumerator Whisperwood_Rooms() => RoomsOf(2, new[] { 1, 2, 4 }, 5);

        /// <summary>Every Catacombs module: docs/qa/shots/ch3_&lt;module&gt;.png.</summary>
        [UnityTest]
        [Timeout(240000)]
        public IEnumerator Catacombs_Rooms() => RoomsOf(3, new[] { 1, 2, 3, 4, 5 }, 5);

        IEnumerator RoomsOf(int chapter, int[] seeds, int expect)
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (int seed in seeds)
            {
                if (seen.Count >= expect) break;
#if UNITY_EDITOR
                UnityEditor.SessionState.SetInt(GameFlow.PlayFromChapterKey, chapter);
                RunState.Runs = 1;
                yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MainScene, new LoadSceneParameters(LoadSceneMode.Single));
#endif
                yield return null;
                var flow = Object.FindAnyObjectByType<GameFlow>();
                if (flow.Seed != seed)
                {
                    flow.Chapter.TeardownChapter();
                    flow.Chapter.Seed = seed;
                    flow.Chapter.BuildChapter(chapter, seed);
                }
                SimLoop.Instance.Paused = true;
                var hero = flow.Chapter.Hero;
                var sk = flow.Chapter.Sidekick;
                foreach (var room in flow.Chapter.Chapter.Rooms)
                {
                    if (!seen.Add(room.ModuleId)) continue;
                    var z = room.transform.position.z;
                    hero.Motor.Teleport(new Vector3(0f, 0.05f, z + room.Length * 0.42f));
                    sk.Motor.Teleport(new Vector3(-1.4f, 0.05f, z + room.Length * 0.32f));
                    SimLoop.Instance.StepMany(2); // the visuals interpolate between sim ticks: let them arrive
                    yield return new WaitForSecondsRealtime(1.6f);
                    QaCapture.Capture(Camera.main, $"ch{chapter}_" + room.ModuleId, 1600, 900);
                }
            }
            Assert.GreaterOrEqual(seen.Count, expect, "all modules: " + string.Join(",", seen));
        }

        /// <summary>Every Sunken Bastion module: docs/qa/shots/ch4_&lt;module&gt;.png (run Tools/HS/Build/Chapters 4–5 first).</summary>
        [UnityTest]
        [Timeout(240000)]
        public IEnumerator Bastion_Rooms() => RoomsOf(4, new[] { 1, 2, 3, 4, 5 }, 5);

        /// <summary>The Gallery's approach: docs/qa/shots/ch5_hall_of_exhibits.png, ch5_long_gallery.png.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Gallery_Rooms() => RoomsOf(5, new[] { 1 }, 2);

        /// <summary>The Bastion's cast under chapter 4's light (soldier, baiter, drowned ambusher, archer, crew, Darian Wren, a hostage).</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Bastion_Cast() =>
            Cast(4, new[] { "bastion_soldier", "baiter", "drowned_ambusher", "bastion_archer", "sluice_crew", "scout_wren", "hostage" }, "ch4_cast");

        /// <summary>The Gallery's cast (warden, marksman, steward, archer) under chapter 5's light.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Gallery_Cast() => Cast(5, new[] { "gallery_warden", "gallery_marksman", "gallery_steward", "gallery_archer" }, "ch5_cast");

        /// <summary>
        /// The final boss, phase by phase, from the door's Restore Point at S3 with Hold Please: docs/qa/shots/boss_*.png
        /// (the diagnosis, the terms, the duel, the persona, the unmasking, the Mirror, the Link ring, the aftermath, the end).
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Gallery_Boss_Phases()
        {
            var point = new RunState.Point { Chapter = 5, Seed = 2, Level = 13, HeroStage = Stage.S3 };
            foreach (var id in new[] { "pocket_sand", "crossbow", "quiet_feet", "smoke_bomb", "hold_please" })
            {
                point.Skills.Add((id, 1));
                if (HS.Skills.SkillCatalog.Load().Get(id).UsesSlot && id != "hold_please") point.Loadout.Add(id);
            }
            RunState.SetChapterStart(5, point);
            RunState.Door = point;
            RunState.Resume = "door";
            RunState.Runs = 1;
            foreach (var id in new[] { "move", "hero_rules", "cone", "insight", "salute", "duel", "mirror", "habit_break", "link_ring", "duet_window" })
                TutorialProgress.MarkSeen(id);
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MainScene, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            var flow = Object.FindAnyObjectByType<GameFlow>();
            var sk = flow.Chapter.Sidekick;
            var pc = sk.GetComponent<HS.Sidekick.PlayerCommands>();
            if (pc != null) pc.enabled = false;
            sk.Commands = HS.Bots.BotFactory.Make("follow", sk);
            flow.Chapter.Hero.Health.Invulnerable = true;
            sk.Health.Invulnerable = true;
            yield return TestUi.WaitUntil(() => flow.Duel != null, 20f, "the boss");
            var boss = flow.Duel;
            IEnumerator At(HS.Boss.GalleryBoss.Phase phase, float after, string shot)
            {
                yield return TestUi.WaitUntil(() => boss.Current == phase, 40f, phase.ToString());
                yield return new WaitForSecondsRealtime(after);
                QaCapture.Capture(Camera.main, shot, 1600, 900);
            }
            yield return At(HS.Boss.GalleryBoss.Phase.Diagnosis, 5f, "boss_0_diagnosis");
            yield return At(HS.Boss.GalleryBoss.Phase.Terms, 3.5f, "boss_1_terms");
            yield return At(HS.Boss.GalleryBoss.Phase.Duel, 4f, "boss_1_duel");
            boss.Ashgrave.Health.SetCurrent(boss.Ashgrave.Health.Max * 0.55f);
            yield return At(HS.Boss.GalleryBoss.Phase.Persona, 3f, "boss_2_persona");
            boss.Ashgrave.TakeDamage(DamageInfo.Make(flow.Chapter.Hero, boss.Ashgrave, 1e9f, DamageKind.Blade, "sword"));
            yield return At(HS.Boss.GalleryBoss.Phase.Unmasking, 5.5f, "boss_3_unmasking");
            yield return At(HS.Boss.GalleryBoss.Phase.Mirror, 3f, "boss_3_mirror");
            boss.Mirror.TakeDamage(DamageInfo.Make(flow.Chapter.Hero, boss.Mirror, 1e9f, DamageKind.Blade, "judgment"));
            yield return TestUi.WaitUntil(() => boss.Duet != null && boss.Duet.Open, 20f, "the Link ring");
            QaCapture.Capture(Camera.main, "boss_3_link_ring", 1600, 900);
            Assert.IsTrue(RunContext.Current.Get<HS.Skills.Impl.ILinkWindow>().TryLink("hold_please"), "the Duet");
            yield return At(HS.Boss.GalleryBoss.Phase.Aftermath, 2.5f, "boss_4_aftermath");
            yield return TestUi.WaitUntil(() => flow.Current == GameFlow.State.End, 20f, "the end");
            yield return new WaitForSecondsRealtime(7f);
            QaCapture.Capture(Camera.main, "boss_5_ending", 1600, 900);
            Assert.AreEqual("won", flow.Outcome);
        }

        /// <summary>The crypt cast under chapter 3's light (cultist, tomb robber, ward guardian, shield-bearer, the Prisoner).</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Catacombs_Cast() => Cast(3, new[] { "cultist", "tomb_robber", "ward_guardian", "shield_bearer", "scout_prisoner" }, "ch3_cast");

        IEnumerator Cast(int chapter, string[] ids, string shot)
        {
#if UNITY_EDITOR
            UnityEditor.SessionState.SetInt(GameFlow.PlayFromChapterKey, chapter);
            RunState.Runs = 1;
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MainScene, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            var flow = Object.FindAnyObjectByType<GameFlow>();
            SimLoop.Instance.Paused = true;
            foreach (var e in Object.FindObjectsByType<HS.Enemies.EnemyAgent>(FindObjectsSortMode.None)) e.gameObject.SetActive(false);
            var basePos = flow.Chapter.Hero.Position + new Vector3(-(ids.Length - 1), 0f, 3f);
            var cast = new System.Collections.Generic.List<HS.Enemies.EnemyAgent>();
            for (int i = 0; i < ids.Length; i++)
            {
                // Through the cast factory: a new archetype wears its body (and tint) until its own prefab is built.
                var go = HS.Rooms.CastFactory.Spawn(ids[i], basePos + new Vector3(i * 2f, 0.05f, 0f), Quaternion.Euler(0f, 180f, 0f), null);
                Assert.IsNotNull(go, ids[i]);
                if (go.TryGetComponent<HS.Enemies.EnemyAgent>(out var e))
                {
                    e.Scripted = true;
                    e.StartsHidden = false;
                    cast.Add(e);
                }
            }
            yield return null; // Start (a hidden archetype hides itself there)
            foreach (var e in cast) if (e.IsHidden) e.Emerge("qa");
            SimLoop.Instance.StepMany(2);
            yield return new WaitForSecondsRealtime(2.5f);
            QaCapture.Capture(Camera.main, shot, 1600, 900);
        }
    }
}
