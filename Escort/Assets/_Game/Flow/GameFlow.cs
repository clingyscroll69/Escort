using System;
using System.Collections.Generic;
using HS.Boss;
using HS.Core;
using HS.Hero;
using HS.Rapport;
using HS.Rooms;
using HS.Sidekick;
using HS.Skills;
using HS.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HS.Flow
{
    /// <summary>
    /// The vertical slice, start to finish (GDD §11.2): opening → the Old Road (3 seeded rooms) → campfire (Stage check,
    /// rest, picks) → the Rigged Duel → end screen. Restore Points: chapter start and the campfire (skills kept, Rapport
    /// restored to the snapshot — GDD §11.3). AutoPlay skips UI for bots and the balance harness.
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        public enum State { Opening, Chapter, Camp, Duel, End }

        public int Seed = 1;
        public bool AutoPlay;
        /// <summary>Harness: no fades, no scene holds, no end screen.</summary>
        public bool Fast;
        /// <summary>Harness "solo" measurement: the hero enters every room fresh (GDD §3 "hero can solo" is per encounter).</summary>
        public bool IsolateEncounters;
        public string[] OpeningPicks = { "pocket_sand", "crossbow" };
        public string[] CampPicks = { "quiet_feet", "bandage", "cover_story", "loosen_bolt" };
        /// <summary>AutoPlay bot by name (idle, sloppy, supportive, follow) — serialized, so QA scenes keep it.</summary>
        public string BotName = "follow";
        /// <summary>Optional factory override (code-only).</summary>
        public Func<SidekickAgent, ISidekickCommands> Bot;

        public State Current { get; private set; }
        public event Action<State> StateChanged;
        public ChapterBootstrap Chapter { get; private set; }
        public CampfireDirector Camp { get; private set; }
        public RiggedDuelDirector Duel { get; private set; }
        public XpTracker Xp { get; private set; }
        public string Outcome { get; private set; }
        public string LastHitTag { get; private set; }

        RunContext _ctx;
        int _levelAtChapterStart = 1;
        HeroAgent Hero => Chapter.Hero;
        SidekickAgent Sk => Chapter.Sidekick;

        /// <summary>Player-build automation: -hs-autoplay &lt;build&gt; -hs-seed N -hs-perf &lt;json&gt; -hs-quit (QA perf runs).</summary>
        void ReadCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "-hs-autoplay":
                        AutoPlay = true;
                        var build = HS.Bots.BotFactory.Build(next ?? "supportive");
                        BotName = build.bot;
                        OpeningPicks = build.opening;
                        CampPicks = build.camp;
                        break;
                    case "-hs-seed": int.TryParse(next, out Seed); break;
                    case "-hs-perf": gameObject.AddComponent<HS.QA.PerfProbe>().OutPath = next; break;
                    case "-hs-quit": _quitAtEnd = true; break;
                }
            }
        }

        bool _quitAtEnd;

        void Start()
        {
            if (!Application.isEditor) ReadCommandLine();
            _ctx = RunContext.Current;
            string resume = RunState.Resume;
            RunState.Resume = null;
            var point = resume == "campfire" ? RunState.Campfire : resume == "chapter" ? RunState.ChapterStart : null;
            Chapter = gameObject.AddComponent<ChapterBootstrap>();
            Chapter.AutoBuild = false;
            Chapter.Seed = point != null ? point.Seed : Seed;
            Chapter.SidekickBot = false;
            Chapter.StartingSkills = new string[0];
            Chapter.Build();
            ScreenFade.Ensure(UIRoot.Ensure());
            if (AutoPlay)
            {
                var pc = Sk.GetComponent<PlayerCommands>();
                if (pc != null) pc.enabled = false;
                Sk.Commands = Bot != null ? Bot(Sk) : HS.Bots.BotFactory.Make(BotName, Sk);
            }
            Xp = new XpTracker();
            Xp.Bind(_ctx);
            _ctx.Register(Xp);
            _ctx.Register(this);
            _ctx.Events.Damage += (d, applied) =>
            {
                if (d.Target == Hero) LastHitTag = d.Tag;
            };
            if (IsolateEncounters)
                _ctx.Events.RoomEntered += r =>
                {
                    Hero.Wounds.Clear();
                    Hero.Health.Heal(99999f);
                };
            RunState.Runs++;
            if (point != null)
            {
                Chapter.Hud.MeetHero("CALLUM", true); // met him on the first attempt
                Apply(point);
                if (resume == "campfire")
                {
                    Hero.Route.SetNodes(new List<RouteNode>());
                    StartDuel();
                }
                else EnterChapter(false);
                return;
            }
            RunState.Clear();
            BeginOpening();
        }

        void SetState(State s)
        {
            if (s != State.End) HS.Audio.AudioDirector.Instance?.OnFlow(s.ToString());
            Debug.Log($"[Flow] {s} t={Time.realtimeSinceStartup:0.0}s stage={(Chapter != null && Chapter.Hero != null ? Chapter.Hero.Stage.ToString() : "-")}");
            Current = s;
            StateChanged?.Invoke(s);
        }

        // ------------------------------------------------------------------ opening
        void BeginOpening()
        {
            SetState(State.Opening);
            SimLoop.Instance.Paused = true;
            var skills = Sk.GetComponent<SidekickSkills>();
            if (AutoPlay)
            {
                foreach (var id in OpeningPicks) skills.Learn(id);
                EnterChapter(true);
                return;
            }
            var op = OpeningView.Show(UIRoot.Ensure(), RunState.Runs > 1);
            op.Done += () =>
            {
                var picker = SkillPicker.Show(UIRoot.Ensure(), skills.System, 2, "» CHOOSE YOUR FIRST TWO TRICKS", false, "SET OUT ON THE OLD ROAD");
                picker.Done += () => EnterChapter(true);
            };
        }

        void EnterChapter(bool snapshot)
        {
            if (snapshot) RunState.ChapterStart = Snapshot();
            _levelAtChapterStart = Sk.Level;
            SetState(State.Chapter);
            SimLoop.Instance.Paused = false;
            _ctx.Events.RaiseNotice("PARTY: SIR CALLUM <size=80%>(Hero)</size>  ·  YOU <size=80%>(HERO's SIDEKICK)</size>\nQUEST: The Old Road.");
            if (snapshot) StartCoroutine(MeetCallum());
        }

        /// <summary>GDD §8: the first meeting. He introduces himself, and the System's "HERO" quietly becomes his name.</summary>
        System.Collections.IEnumerator MeetCallum()
        {
            if (!Fast) yield return new WaitForSecondsRealtime(1.6f);
            if (Current != State.Chapter || Hero == null || !Hero.IsAlive) yield break;
            if (Hero.Module is HS.Hero.Callum.CallumModule cm) cm.Greet();
            Chapter.Hud.MeetHero("CALLUM");
        }

        // ------------------------------------------------------------------ chapter
        void Update()
        {
            if (Current == State.Chapter)
            {
                if (!Hero.IsAlive || !Sk.IsAlive)
                {
                    EndRun(false, CuratorDiagnosis.ForRoad(LastHitTag, !Sk.IsAlive), !Sk.IsAlive);
                    return;
                }
                if (Hero.Route.AtEnd && Hero.Position.z > Chapter.Chapter.ChapterLength - 6f) ToCamp();
            }
        }

        void ToCamp()
        {
            SetState(State.Camp);
            SimLoop.Instance.Paused = true;
            Transition(() =>
            {
                ClearEnemies();
                int levelNow = XpTracker.LevelFor(Xp.Xp);
                int picks = Mathf.Max(1, levelNow - _levelAtChapterStart); // the camp always brings at least one lesson
                Sk.SetLevel(_levelAtChapterStart + picks, true);
                Camp = new GameObject("Campfire").AddComponent<CampfireDirector>();
                if (Fast) Camp.AutoSceneSeconds = 0f;
                Camp.Finished += () =>
                {
                    RunState.Campfire = Snapshot();
                    Transition(StartDuel);
                };
                Camp.Begin(Chapter.Chapter.Campfire, Hero, Sk, picks, AutoPlay, CampPicks);
                SimLoop.Instance.Paused = false;
            });
        }

        void Transition(Action atBlack)
        {
            if (Fast) atBlack();
            else ScreenFade.Ensure(UIRoot.Ensure()).Through(atBlack);
        }

        void ClearEnemies()
        {
            // The road is behind us: nothing back there may still "clear" (XP, Code recitals) once we're at the fire.
            foreach (var d in FindObjectsByType<EncounterDirector>(FindObjectsSortMode.None)) d.enabled = false;
            foreach (var e in FindObjectsByType<HS.Enemies.EnemyAgent>(FindObjectsSortMode.None)) e.gameObject.SetActive(false);
            foreach (var p in FindObjectsByType<ProjectileSystem>(FindObjectsSortMode.None)) p.ClearAll();
        }

        // ------------------------------------------------------------------ duel
        void StartDuel()
        {
            if (Camp != null) Destroy(Camp.gameObject);
            SetState(State.Duel);
            SimLoop.Instance.Paused = false;
            Duel = new GameObject("RiggedDuel").AddComponent<RiggedDuelDirector>();
            var assets = GameAssets.Load();
            Duel.Begin(Chapter.Chapter.Boss, Hero, Sk, assets.Enemy);
            Chapter.Camera.AddFocus(Duel.Ashgrave.transform, 0.6f, 1.6f);
            Chapter.Camera.TravelDirection = () => Vector3.forward;
            _ctx.Get<OpportunityDirector>()?.Judge?.NewFight();
            Duel.PhaseChanged += p =>
            {
                if (p == RiggedDuelDirector.Phase.Duel) Chapter.Hud.ShowBoss(Duel.Ashgrave, "LORD ASHGRAVE");
                if (p == RiggedDuelDirector.Phase.Won)
                {
                    int silenced = 0;
                    foreach (var a in Duel.Archers) if (a != null && !a.IsAlive) silenced++;
                    var quote = CuratorDiagnosis.VictoryLine(Hero.Stage, silenced);
                    _ctx.Events.RaiseBark("callum", quote, 4f, 3);
                    EndRun(true, new List<string> { "CHAPTER 1: THE OLD ROAD — CLEARED." }, false, quote);
                }
                else if (p == RiggedDuelDirector.Phase.Lost)
                    EndRun(false, CuratorDiagnosis.For(Hero.Stage, Duel.LossCause, Duel.SidekickDied), Duel.SidekickDied);
            };
        }

        // ------------------------------------------------------------------ end
        void EndRun(bool won, List<string> diagnosis, bool sidekickDied, string quote = null)
        {
            if (Current == State.End) return;
            SetState(State.End);
            Outcome = won ? "won" : sidekickDied ? "sidekick_died" : "hero_died";
            Debug.Log($"[Flow] outcome {Outcome}");
            HS.Audio.AudioDirector.Instance?.OnFlow("End", won);
            _ctx.Get<OpportunityDirector>()?.EndOfFight(won ? "the run ended" : "he fell");
            var ledger = _ctx.Get<RapportLedger>();
            var m = new EndScreen.Model
            {
                Error = !won,
                Title = won ? "THE RIGGED DUEL — WON" : sidekickDied ? "SIDEKICK: DECEASED. NO RECALL AVAILABLE." : "HERO: CALLUM. DECEASED",
                Diagnosis = diagnosis,
                Quote = quote,
                Lines = ledger != null ? PostMortem.From(ledger) : new List<PostMortem.Line>(),
            };
            if (won) m.Buttons.Add(("PLAY AGAIN · NEW ROAD", PlayAgain));
            else
            {
                if (RunState.Campfire != null) m.Buttons.Add(("RESTORE · BEFORE THE DUEL", () => Restore("campfire")));
                m.Buttons.Add(("RESTORE · CHAPTER START", () => Restore("chapter")));
            }
            m.Buttons.Add(("QUIT", Quit));
            if (_quitAtEnd)
            {
                GetComponent<HS.QA.PerfProbe>()?.Write();
                Invoke(nameof(Quit), 12f); // let the end screen render a while for the probe
            }
            if (Fast) return;
            Invoke(nameof(ShowEnd), 1.6f);
            _pendingEnd = m;
        }

        EndScreen.Model _pendingEnd;
        void ShowEnd() => EndScreen.Show(UIRoot.Ensure(), _pendingEnd);

        public void Restore(string point)
        {
            RunState.Resume = point;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0 ? SceneManager.GetActiveScene().name : SceneManager.GetActiveScene().path);
        }

        public void PlayAgain()
        {
            RunState.Clear();
            Seed = Seed + 1;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ restore points
        public RunState.Point Snapshot()
        {
            var skills = Sk.GetComponent<SidekickSkills>();
            var p = new RunState.Point
            {
                Seed = Chapter.Seed,
                Level = Sk.Level,
                Xp = Xp.Xp,
                Skills = skills.System.Snapshot(),
                Loadout = skills.System.LoadoutSnapshot(),
                HeroStage = Hero.Stage,
                Ledger = _ctx.Get<RapportLedger>()?.Snapshot(),
                Intel = _ctx.Get<StoneSystem>() != null ? _ctx.Get<StoneSystem>().Intel.Snapshot() : default,
                HeroHp = Hero.Health.Current,
                Wounds = Hero.Wounds.Snapshot(),
            };
            return p;
        }

        public void Apply(RunState.Point p)
        {
            var skills = Sk.GetComponent<SidekickSkills>();
            var cat = SkillCatalog.Load();
            skills.System.Restore(p.Skills, p.Loadout, id => cat.Get(id));
            foreach (var (id, rank) in p.Skills)
                if (id == "quiet_feet")
                {
                    Sk.HasQuietFeet = true;
                    var def = cat.Get(id);
                    Sk.QuietFeetRadius = def.A(rank);
                    Sk.QuietFeetConeMul = def.B(rank);
                }
            Sk.SetLevel(p.Level, true);
            Xp.Restore(p.Xp);
            Hero.ApplyStage(p.HeroStage);
            if (p.Ledger != null) _ctx.Get<RapportLedger>()?.Restore(p.Ledger);
            _ctx.Get<StoneSystem>()?.Intel.Restore(p.Intel);
            Hero.Wounds.Restore(p.Wounds);
            Hero.ApplyWoundEffects();
            if (p.HeroHp > 0f) Hero.Health.SetCurrent(p.HeroHp);
        }
    }
}
