using System.Linq;
using HS.Core;
using HS.Hero;
using HS.Presentation;
using HS.Rooms;
using HS.Sidekick;
using HS.Skills;
using UnityEngine;

namespace HS.Flow
{
    /// <summary>
    /// The run's world: once per run the pair, the ledger, the stones, the UI and the camera (<see cref="BuildRun"/>); per
    /// chapter its rooms, theme, route and encounters (<see cref="BuildChapter"/>, <see cref="TeardownChapter"/>). The game
    /// flow drives it; QA scenes call <see cref="Build"/>.
    /// </summary>
    public sealed class ChapterBootstrap : MonoBehaviour
    {
        public int Seed = 1;
        public bool SidekickBot = true;
        public string[] StartingSkills = { "pocket_sand", "crossbow" };
        public Stage HeroStage = Stage.S0;
        [Tooltip("QA: start with Hero Insight (rule labels) on.")]
        public bool QaInsight;

        public ChapterBuilder Chapter { get; private set; }
        public EncounterDirector Encounters { get; private set; }
        public HeroAgent Hero { get; private set; }
        public SidekickAgent Sidekick { get; private set; }
        public CameraRig Camera { get; private set; }
        public HS.Rapport.OpportunityDirector Director { get; private set; }
        public StoneSystem Stones { get; private set; }
        public HS.UI.HudView Hud { get; private set; }

        [Tooltip("Off when a GameFlow drives the build.")]
        public bool AutoBuild = true;

        public ChapterDef Def { get; private set; }
        RoomStreamer _streamer;

        void Start()
        {
            if (AutoBuild) Build();
        }

        /// <summary>QA scenes: the run and one chapter (chapter 1 unless the run context says otherwise), as before.</summary>
        public void Build()
        {
            BuildRun();
            BuildChapter(RunContext.Current != null && RunContext.Current.Chapter > 1 ? RunContext.Current.Chapter : 1, Seed);
        }

        /// <summary>Once per run: the pair, the ledger, the stones, the UI, the camera. They outlive every chapter.</summary>
        public void BuildRun()
        {
            var assets = GameAssets.Load();
            var ctx = RunContext.Current;
            ctx.Seed = Seed;
            ProjectileSystem.Ensure();
            Chapter = new GameObject("Chapter").AddComponent<ChapterBuilder>();
            Chapter.ModulePrefabs = assets.roomModules;
            Chapter.StartCapPrefab = assets.startCap;
            Chapter.EndCapPrefab = assets.endCap;
            Chapter.CampfirePrefab = assets.campfire;
            Chapter.BossPrefab = assets.gallery != null ? assets.gallery : assets.boss;

            Hero = Instantiate(assets.hero, new Vector3(0f, 0.05f, -1.5f), Quaternion.identity).GetComponent<HeroAgent>();
            Sidekick = Instantiate(assets.sidekick, new Vector3(-1.6f, 0.05f, -1.9f), Quaternion.identity).GetComponent<SidekickAgent>();
            ctx.Hero = Hero;
            ctx.Sidekick = Sidekick;
            Hero.ApplyStage(HeroStage);
            if (SidekickBot)
            {
                var pc = Sidekick.GetComponent<PlayerCommands>();
                if (pc != null) pc.enabled = false;
                Sidekick.Commands = Sidekick.gameObject.AddComponent<HS.Bots.FollowBot>();
            }
            Director = HS.Rapport.OpportunityDirector.Create(ctx, Hero);
            Stones = StoneSystem.Create(ctx, Hero);
            var skills = Sidekick.GetComponent<SidekickSkills>();
            if (skills != null) skills.StartingSkills = StartingSkills;

            Camera = CameraRig.Build(Hero.transform.Find("CamTarget") ?? Hero.transform, Sidekick.transform.Find("CamTarget") ?? Sidekick.transform, ctx.Tuning.camera);
            Camera.TravelDirection = () =>
            {
                if (Hero == null || Hero.Route.AtEnd) return Vector3.forward;
                var d = Hero.Route.Current.Position - Hero.Position;
                d.y = 0f;
                return d.sqrMagnitude > 1f ? d.normalized : Vector3.forward;
            };
            HS.Audio.AudioDirector.Ensure().OnFlow("Chapter");
            var ui = HS.UI.UIRoot.Ensure();
            Hud = HS.UI.HudView.Create(ui);
            Hud.InsightOn = QaInsight;
            HS.UI.BarkView.Create(ui);
            HS.UI.SystemWindow.Create(ui);
            HS.UI.ThreatIndicators.Create(ui);
            HS.UI.HitFeedback.Create(ui, Hud);
            HS.UI.IntentMarkers.Create(ui);
            _streamer = gameObject.AddComponent<RoomStreamer>();
            _streamer.Chapter = Chapter;
            _streamer.Focus = Hero.transform;
        }

        /// <summary>A chapter: its rooms, theme, route and encounters. The pair is placed at the road's start.</summary>
        public void BuildChapter(int chapter, int seed)
        {
            var assets = GameAssets.Load();
            var ctx = RunContext.Current;
            ctx.Chapter = chapter;
            ctx.Seed = seed;
            if (Director != null) Director.Ledger.Chapter = chapter;
            Def = ChapterDef.For(chapter);
            Chapter.StartCapPrefab = assets.Cap(Def.CapStart) ?? assets.startCap;
            Chapter.EndCapPrefab = assets.Cap(Def.CapEnd) ?? assets.endCap;
            Chapter.Build(seed, Def);
            ChapterTheme.Apply(Def.Theme);
            Hero.Motor.Teleport(new Vector3(0f, 0.05f, -1.5f));
            Hero.Motor.FaceInstant(Vector3.forward);
            Sidekick.Motor.Teleport(new Vector3(-1.6f, 0.05f, -1.9f));
            Sidekick.Motor.FaceInstant(Vector3.forward);
            Hero.Route.SetNodes(Chapter.ChapterRoute());
            Encounters = new GameObject("Encounters").AddComponent<EncounterDirector>();
            Encounters.Chapter = Chapter;
            Encounters.EnemyPrefab = assets.Enemy;
            Encounters.SpawnAll();
            _streamer?.Reset();
        }

        /// <summary>Between chapters, at black: everything of the old road goes, now (not at the end of the frame).</summary>
        public void TeardownChapter()
        {
            if (Encounters != null)
            {
                Encounters.gameObject.SetActive(false);
                Destroy(Encounters.gameObject);
                Encounters = null;
            }
            foreach (var p in FindObjectsByType<ProjectileSystem>(FindObjectsSortMode.None)) p.ClearAll();
            Chapter.Clear();
        }
    }
}
