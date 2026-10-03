using System.Collections;
using HS.Core;
using HS.Flow;
using HS.Hero;
using HS.Hero.Callum;
using HS.QA;
using HS.Tutorial;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>
    /// Visual QA for the tutorial and HUD pass: the real Main scene (its sun, fog and grade), resumed straight into the
    /// Old Road with a fresh tutorial and a bot playing the sidekick, captured at each lesson to docs/qa/shots/ui_*.png.
    /// Category QA: skipped by default; run on purpose (CATEGORY=QA tools/unity-tests.sh PlayMode), then look at the
    /// pictures.
    /// </summary>
    [Category("QA")]
    public class UiQaCaptures
    {
        const string MainScene = "Assets/_Game/Scenes/Main.unity";
        ITutorialStore _savedStore;

        [SetUp]
        public void SetUp()
        {
            _savedStore = TutorialProgress.Store;
            TutorialProgress.Store = new MemoryStore();
        }

        [TearDown]
        public void TearDown()
        {
            TutorialProgress.Store = _savedStore;
            ModalGate.Clear();
            RunState.Clear();
        }

        static IEnumerator LoadMainResumingChapter(params string[] skills)
        {
            var point = new RunState.Point { Seed = 2, Level = 1, Xp = 0, HeroStage = Stage.S0 };
            foreach (var id in skills)
            {
                point.Skills.Add((id, 1));
                var def = HS.Skills.SkillCatalog.Load().Get(id);
                if (def.UsesSlot) point.Loadout.Add(id);
            }
            RunState.ChapterStart = point;
            RunState.Resume = "chapter";
            RunState.Runs = 1;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MainScene, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            yield return null;
        }

        static void Shot(string name) => QaCapture.Capture(Camera.main, name, 1600, 900);

        /// <summary>Each skill's demo in a large viewport, captured at two moments that carry its point.</summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Skill_Demos_Mid_Play()
        {
            new GameObject("RunContext").AddComponent<RunContext>();
            SimLoop.Ensure().Paused = true;
            var sun = new GameObject("TestSun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.35f;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -38f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.74f, 0.86f);
            RenderSettings.ambientEquatorColor = new Color(0.56f, 0.58f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.3f, 0.26f);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.04f, 0.07f);
            HS.Audio.AudioDirector.Ensure();
            var vp = HS.UI.DemoViewport.Create(HS.UI.UIKit.Stretch(HS.UI.UIRoot.Ensure().Overlay, "Host"), new Vector2(1280f, 720f));
            var moments = new (string id, int rank, float[] at)[]
            {
                ("pocket_sand", 1, new[] { 3.6f, 7.9f }), ("loosen_bolt", 1, new[] { 2.4f, 6.3f }), ("quiet_feet", 1, new[] { 2.8f, 6.8f }),
                ("crossbow", 2, new[] { 1.85f, 6.3f }), ("bandage", 1, new[] { 2.6f, 5.2f }), ("cover_story", 1, new[] { 3.6f, 6.4f }),
            };
            foreach (var (id, rank, at) in moments)
            {
                vp.Play(id, rank);
                for (int k = 0; k < at.Length; k++)
                {
                    yield return TestUi.WaitUntil(() => vp.Time >= at[k] || vp.AtEndCard, 20f, $"{id} at {at[k]} s");
                    yield return null;
                    Shot($"ui_demo_{id}_{k}");
                }
                vp.Skip();
                yield return new WaitForSecondsRealtime(0.5f);
                if (id == "pocket_sand") Shot("ui_demo_endcard");
            }
            vp.Stop();
            TestUi.TearDownAll();
        }

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator Tutorial_And_Hud_On_The_Old_Road()
        {
            yield return LoadMainResumingChapter("pocket_sand", "crossbow", "bandage", "quiet_feet");
            var flow = Object.FindAnyObjectByType<GameFlow>();
            Assert.IsNotNull(flow);
            var sk = flow.Chapter.Sidekick;
            var pc = sk.GetComponent<HS.Sidekick.PlayerCommands>();
            if (pc != null) pc.enabled = false;
            sk.Commands = HS.Bots.BotFactory.Make("supportive", sk);
            var dir = flow.Tutorial;
            Assert.IsNotNull(dir);

            // Capture each lesson as it shows (the order depends on the road), until both freeze-frames and a few tips
            // have been seen; freeze-frames are continued after their picture.
            var captured = new System.Collections.Generic.HashSet<string>();
            int tips = 0;
            float until = Time.realtimeSinceStartup + 110f;
            while (Time.realtimeSinceStartup < until && !(captured.Contains("hero_rules") && captured.Contains("cone") && tips >= 4))
            {
                var id = dir.Showing;
                if (id != null && !captured.Contains(id))
                {
                    bool focus = dir.FocusOpen;
                    yield return new WaitForSecondsRealtime(focus ? 0.6f : 0.75f);
                    if (dir.Showing == id)
                    {
                        Shot((focus ? "ui_focus_" : "ui_tip_") + id);
                        captured.Add(id);
                        if (!focus) tips++;
                    }
                    if (dir.FocusOpen) dir.ContinueFocus();
                }
                yield return null;
            }
            Debug.Log("[QA] captured lessons: " + string.Join(", ", captured));
            Assert.IsTrue(captured.Contains("hero_rules") && captured.Contains("cone"), "both freeze-frame lessons were seen on the road");

            // Mid-fight HUD with everything showing: wounds, low Honor, Insight, a banked level.
            yield return new WaitForSecondsRealtime(2.5f);
            var hero = flow.Chapter.Hero;
            hero.Wounds.Add(WoundType.CrackedRibs);
            hero.Wounds.Add(WoundType.SprainedAnkle);
            if (hero.Module is CallumModule cm) cm.SetHonor(30f);
            flow.Chapter.Hud.InsightOn = true;
            flow.Xp.Restore(XpTracker.Thresholds[0] + 40);
            yield return new WaitForSecondsRealtime(0.8f);
            Shot("ui_hud_fight");
        }
    }
}
