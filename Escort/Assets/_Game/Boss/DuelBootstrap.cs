using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Presentation;
using HS.Rooms;
using HS.Sidekick;
using HS.Skills;
using HS.UI;
using UnityEngine;

namespace HS.Boss
{
    /// <summary>
    /// Stages the rigged duel on its own (QA scene, and the game flow's boss step): arena, the pair, Ashgrave + gallery,
    /// judges, stones, UI, camera, and the end screen on the outcome.
    /// </summary>
    public sealed class DuelBootstrap : MonoBehaviour
    {
        public Stage HeroStage = Stage.S0;
        public bool SidekickBot = true;
        public string[] StartingSkills = { "quiet_feet", "pocket_sand", "crossbow", "bandage" };
        public System.Action OnRestoreDuel, OnRestoreChapter, OnQuit;

        public RiggedDuelDirector Director { get; private set; }
        public HeroAgent Hero { get; private set; }
        public SidekickAgent Sidekick { get; private set; }
        public RoomModule Arena { get; private set; }
        public HudView Hud { get; private set; }

        void Start() => Build();

        public void Build()
        {
            var assets = GameAssets.Load();
            var ctx = RunContext.Current;
            ProjectileSystem.Ensure();
            Arena = Instantiate(assets.boss, Vector3.zero, Quaternion.identity).GetComponent<RoomModule>();
            Arena.SetRoomIndex(200);
            ChapterBuilder.CombineStatic(Arena.gameObject);
            Hero = Instantiate(assets.hero, new Vector3(0f, 0.05f, 1f), Quaternion.identity).GetComponent<HeroAgent>();
            Sidekick = Instantiate(assets.sidekick, new Vector3(-1.8f, 0.05f, 0.4f), Quaternion.identity).GetComponent<SidekickAgent>();
            ctx.Hero = Hero;
            ctx.Sidekick = Sidekick;
            Hero.ApplyStage(HeroStage);
            var skills = Sidekick.GetComponent<SidekickSkills>();
            if (skills != null) skills.StartingSkills = StartingSkills;
            HS.Rapport.OpportunityDirector.Create(ctx, Hero);
            StoneSystem.Create(ctx, Hero);
            HS.Audio.AudioDirector.Ensure().OnFlow("Duel");
            var ui = UIRoot.Ensure();
            Hud = HudView.Create(ui);
            BarkView.Create(ui);
            SystemWindow.Create(ui);
            ThreatIndicators.Create(ui);
            HitFeedback.Create(ui, Hud);
            Director = new GameObject("RiggedDuel").AddComponent<RiggedDuelDirector>();
            Director.Begin(Arena, Hero, Sidekick, assets.Enemy);
            Hud.ShowBoss(null, "");
            if (SidekickBot)
            {
                var pc = Sidekick.GetComponent<PlayerCommands>();
                if (pc != null) pc.enabled = false;
                var bot = Sidekick.gameObject.AddComponent<HS.Bots.FollowBot>();
                foreach (var t in Arena.GetComponentsInChildren<Transform>(true)) if (t.name == "SidekickOut") bot.Anchor = t;
                bot.Waypoints.Add(Arena.transform.position + new Vector3(0f, 0f, 1.2f)); // the gate
                bot.Waypoints.Add(Arena.transform.position + new Vector3(0f, 0f, 5.5f));
                Sidekick.Commands = bot;
            }
            var cam = CameraRig.Build(Hero.transform.Find("CamTarget") ?? Hero.transform, Sidekick.transform.Find("CamTarget") ?? Sidekick.transform, ctx.Tuning.camera);
            cam.AddFocus(Director.Ashgrave.transform, 0.6f, 1.6f);
            Director.PhaseChanged += OnPhase;
        }

        void OnPhase(RiggedDuelDirector.Phase p)
        {
            if (p == RiggedDuelDirector.Phase.Duel) Hud.ShowBoss(Director.Ashgrave, "LORD ASHGRAVE");
            if (p != RiggedDuelDirector.Phase.Won && p != RiggedDuelDirector.Phase.Lost) return;
            var ctx = RunContext.Current;
            ctx.Get<HS.Rapport.OpportunityDirector>()?.EndOfFight("the duel ended");
            var ledger = ctx.Get<HS.Rapport.RapportLedger>();
            var m = new EndScreen.Model { Lines = ledger != null ? HS.Rapport.PostMortem.From(ledger) : new System.Collections.Generic.List<HS.Rapport.PostMortem.Line>() };
            if (p == RiggedDuelDirector.Phase.Won)
            {
                int silenced = 0;
                foreach (var a in Director.Archers) if (a != null && !a.IsAlive) silenced++;
                m.Title = "THE RIGGED DUEL — WON";
                m.Diagnosis.Add("CHAPTER 1: THE OLD ROAD — CLEARED.");
                m.Quote = CuratorDiagnosis.VictoryLine(Hero.Stage, silenced);
                m.Buttons.Add(("PLAY AGAIN", OnRestoreChapter));
                m.Buttons.Add(("QUIT", OnQuit));
                ctx.Events.RaiseBark("callum", m.Quote, 4f, 3);
            }
            else
            {
                m.Error = true;
                m.Title = Director.SidekickDied ? "SIDEKICK: DECEASED. NO RECALL AVAILABLE." : "HERO: CALLUM. DECEASED";
                m.Diagnosis.AddRange(CuratorDiagnosis.For(Hero.Stage, Director.LossCause, Director.SidekickDied));
                m.Buttons.Add(("RESTORE · BEFORE THE DUEL", OnRestoreDuel));
                m.Buttons.Add(("RESTORE · CHAPTER START", OnRestoreChapter));
                m.Buttons.Add(("QUIT", OnQuit));
            }
            Invoke(nameof(ShowEnd), 1.6f);
            _pending = m;
        }

        EndScreen.Model _pending;
        void ShowEnd() => EndScreen.Show(UIRoot.Ensure(), _pending);
    }
}
