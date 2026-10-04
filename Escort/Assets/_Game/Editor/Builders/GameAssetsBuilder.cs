using System.Linq;
using HS.Core;
using HS.Flow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HS.EditorTools
{
    public static class GameAssetsBuilder
    {
        public const string Path = "Assets/_Game/Resources/GameAssets.asset";

        [MenuItem("Tools/HS/Build/Game Assets Registry")]
        public static void Build()
        {
            var a = AssetDatabase.LoadAssetAtPath<GameAssets>(Path);
            if (a == null)
            {
                a = ScriptableObject.CreateInstance<GameAssets>();
                AssetDatabase.CreateAsset(a, Path);
            }
            GameObject L(string p) => AssetDatabase.LoadAssetAtPath<GameObject>(p);
            a.hero = L(GameplayPrefabBuilder.Dir + "/Callum.prefab");
            a.sidekick = L(GameplayPrefabBuilder.Dir + "/Sidekick.prefab");
            a.roomModules = new[] { "crossroads_shrine", "toll_gate", "ruined_gatehouse", "wagon_camp" }.Concat(WhisperwoodRooms.Ids)
                .Select(id => L($"{RoomBuilder.Dir}/{id}.prefab")).Where(p => p != null).ToArray();
            a.startCap = L(RoomBuilder.Dir + "/road_cap_start.prefab");
            a.endCap = L(RoomBuilder.Dir + "/road_cap_end.prefab");
            a.campfire = L(RoomBuilder.Dir + "/campfire.prefab");
            a.boss = L(RoomBuilder.Dir + "/rigged_duel.prefab");
            a.enemies.Clear();
            foreach (var (arch, _, _) in GameplayPrefabBuilder.Enemies)
                a.enemies.Add(new GameAssets.Entry { id = arch, prefab = L(GameplayPrefabBuilder.EnemyPrefabPath(arch)) });
            foreach (var (id, _, _, _) in GameplayPrefabBuilder.Scouts)
                a.enemies.Add(new GameAssets.Entry { id = "scout_" + id, prefab = L(GameplayPrefabBuilder.ScoutPrefabPath(id)) });
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            HS.Agent.AgentBridge.Write("builder.json", "{\"ok\":true,\"msg\":\"game assets\"}");
        }

        public const string MainScenePath = "Assets/_Game/Scenes/Main.unity";

        static void FlowScene(string path, System.Action<HS.Flow.GameFlow> configure)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneLighting.Apply();
            new GameObject("SimLoop").AddComponent<SimLoop>();
            new GameObject("RunContext").AddComponent<RunContext>();
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.gameObject.AddComponent<AudioListener>();
            var flow = new GameObject("GameFlow").AddComponent<HS.Flow.GameFlow>();
            configure?.Invoke(flow);
            EditorSceneManager.SaveScene(scene, path);
        }

        [MenuItem("Tools/HS/Open Main Scene")]
        public static void OpenMainScene() => EditorSceneManager.OpenScene(MainScenePath);

        /// <summary>The playable slice: opening → the Old Road → campfire → the Rigged Duel.</summary>
        [MenuItem("Tools/HS/Build/Main Scene")]
        public static void BuildMainScene()
        {
            FlowScene(MainScenePath, f => f.Seed = 1);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };
            HS.Agent.AgentBridge.Write("builder.json", "{\"ok\":true,\"msg\":\"main scene\"}");
        }

        /// <summary>QA: the whole slice on AutoPlay (bot sidekick, auto picks) with the event log + captures.</summary>
        [MenuItem("Tools/HS/QA/Build Flow Scene")]
        public static void BuildFlowScene()
        {
            var args = HS.Agent.AgentBridge.ReadArgs("flow_args.json");
            FlowScene("Assets/_Game/Scenes/QA_Flow.unity", f =>
            {
                f.Seed = args.TryGetValue("seed", out var s) ? int.Parse(s) : 1;
                f.AutoPlay = !args.TryGetValue("autoplay", out var ap) || ap != "0";
                if (args.TryGetValue("bot", out var bn))
                {
                    var build = HS.Bots.BotFactory.Build(bn);
                    f.BotName = build.bot;
                    f.OpeningPicks = build.opening;
                    f.CampPicks = build.camp;
                }
                var watch = f.gameObject.AddComponent<HS.QA.ChapterWatch>();
                watch.Prefix = args.TryGetValue("prefix", out var px) ? px : "flow";
                if (args.TryGetValue("timescale", out var ts)) watch.TimeScale = float.Parse(ts, System.Globalization.CultureInfo.InvariantCulture);
                if (args.TryGetValue("maxtime", out var mt)) watch.MaxTime = float.Parse(mt, System.Globalization.CultureInfo.InvariantCulture);
                if (args.TryGetValue("period", out var pd)) watch.Period = float.Parse(pd, System.Globalization.CultureInfo.InvariantCulture);
                if (args.TryGetValue("shots", out var sh)) watch.MaxShots = int.Parse(sh);
                watch.GodChapter = args.TryGetValue("godchapter", out var gc) && gc == "1";
                watch.ShortOpening = args.TryGetValue("shortopening", out var so) && so == "1";
                if (args.TryGetValue("realshots", out var rs))
                    watch.RealShots = System.Array.ConvertAll(rs.Split(';'), x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
                if (args.TryGetValue("maxreal", out var mr)) watch.MaxRealTime = float.Parse(mr, System.Globalization.CultureInfo.InvariantCulture);
                watch.EndDelay = 12f;
            });
        }

        /// <summary>QA: the opening on a manual clock, captured at exact times (args: Library/Agent/scrub_args.json).</summary>
        [MenuItem("Tools/HS/QA/Build Opening Scrub Scene")]
        public static void BuildOpeningScrubScene()
        {
            var args = HS.Agent.AgentBridge.ReadArgs("scrub_args.json");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneLighting.Apply();
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.gameObject.AddComponent<AudioListener>();
            var scrub = new GameObject("OpeningScrub").AddComponent<HS.QA.OpeningScrub>();
            if (args.TryGetValue("times", out var ts))
                scrub.Times = System.Array.ConvertAll(ts.Split(';'), x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
            scrub.Short = args.TryGetValue("short", out var sh) && sh == "1";
            scrub.Prefix = args.TryGetValue("prefix", out var px) ? px : "scrub";
            EditorSceneManager.SaveScene(scene, "Assets/_Game/Scenes/QA_Opening.unity");
            HS.Agent.AgentBridge.Write("builder.json", "{\"ok\":true,\"msg\":\"opening scrub scene\"}");
        }

        /// <summary>Balance batch (args: Library/Agent/harness_args.json) → docs/qa/balance/runs.csv + summary.md.</summary>
        [MenuItem("Tools/HS/QA/Build Harness Scene")]
        public static void BuildHarnessScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneLighting.Apply();
            new GameObject("BalanceHarness").AddComponent<HS.QA.BalanceHarness>();
            EditorSceneManager.SaveScene(scene, "Assets/_Game/Scenes/QA_Harness.unity");
        }

        [MenuItem("Tools/HS/QA/Build Duel Scene")]
        public static void BuildDuelScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneLighting.Apply();
            new GameObject("SimLoop").AddComponent<SimLoop>();
            new GameObject("RunContext").AddComponent<RunContext>();
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.gameObject.AddComponent<AudioListener>();
            var args = HS.Agent.AgentBridge.ReadArgs("duel_args.json");
            var boot = new GameObject("DuelBootstrap").AddComponent<HS.Boss.DuelBootstrap>();
            if (args.TryGetValue("stage", out var st)) boot.HeroStage = (HS.Core.Stage)int.Parse(st);
            var watch = boot.gameObject.AddComponent<HS.QA.ChapterWatch>();
            watch.Prefix = args.TryGetValue("prefix", out var px) ? px : "duel";
            if (args.TryGetValue("timescale", out var ts)) watch.TimeScale = float.Parse(ts, System.Globalization.CultureInfo.InvariantCulture);
            if (args.TryGetValue("maxtime", out var mt)) watch.MaxTime = float.Parse(mt, System.Globalization.CultureInfo.InvariantCulture);
            if (args.TryGetValue("period", out var pd)) watch.Period = float.Parse(pd, System.Globalization.CultureInfo.InvariantCulture);
            watch.EndDelay = 12f; // real seconds: let the end screen type out for the capture (end condition wired at runtime)
            EditorSceneManager.SaveScene(scene, "Assets/_Game/Scenes/QA_Duel.unity");
        }

        [MenuItem("Tools/HS/QA/Build Chapter Scene")]
        public static void BuildChapterScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneLighting.Apply();
            new GameObject("SimLoop").AddComponent<SimLoop>();
            new GameObject("RunContext").AddComponent<RunContext>();
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.gameObject.AddComponent<AudioListener>();
            var args = HS.Agent.AgentBridge.ReadArgs("chapter_args.json");
            var boot = new GameObject("ChapterBootstrap").AddComponent<ChapterBootstrap>();
            boot.Seed = args.TryGetValue("seed", out var s) ? int.Parse(s) : 1;
            if (args.TryGetValue("stage", out var st)) boot.HeroStage = (HS.Core.Stage)int.Parse(st);
            boot.QaInsight = args.TryGetValue("insight", out var ins) && ins == "1";
            var watch = boot.gameObject.AddComponent<HS.QA.ChapterWatch>();
            watch.Prefix = args.TryGetValue("prefix", out var px) ? px : "chapter";
            if (args.TryGetValue("timescale", out var ts)) watch.TimeScale = float.Parse(ts, System.Globalization.CultureInfo.InvariantCulture);
            if (args.TryGetValue("maxtime", out var mt)) watch.MaxTime = float.Parse(mt, System.Globalization.CultureInfo.InvariantCulture);
            if (args.TryGetValue("period", out var pd)) watch.Period = float.Parse(pd, System.Globalization.CultureInfo.InvariantCulture);
            EditorSceneManager.SaveScene(scene, "Assets/_Game/Scenes/QA_Chapter.unity");
        }
    }
}
