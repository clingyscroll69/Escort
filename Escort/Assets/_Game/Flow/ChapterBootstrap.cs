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
    /// Assembles a playable chapter from the run seed: rooms, hero route, encounters, the pair, camera, streaming.
    /// The game flow (Task 13/14) drives it; QA scenes use it directly.
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

        void Start()
        {
            if (AutoBuild) Build();
        }

        public void Build()
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
            Chapter.BossPrefab = assets.boss;
            Chapter.Build(Seed);

            Hero = Instantiate(assets.hero, new Vector3(0f, 0.05f, -1.5f), Quaternion.identity).GetComponent<HeroAgent>();
            Sidekick = Instantiate(assets.sidekick, new Vector3(-1.6f, 0.05f, -1.9f), Quaternion.identity).GetComponent<SidekickAgent>();
            ctx.Hero = Hero;
            ctx.Sidekick = Sidekick;
            Hero.Route.SetNodes(Chapter.ChapterRoute());
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

            Encounters = new GameObject("Encounters").AddComponent<EncounterDirector>();
            Encounters.Chapter = Chapter;
            Encounters.EnemyPrefab = assets.Enemy;
            Encounters.SpawnAll();

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
            var streamer = gameObject.AddComponent<RoomStreamer>();
            streamer.Chapter = Chapter;
            streamer.Focus = Hero.transform;
        }
    }
}
