using System;
using System.Collections.Generic;
using System.Linq;
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
    /// The campaign, start to finish (campaign spec §3.1): opening → five chapters, each built in place (chapters 1–4 end
    /// at a campfire: rest, picks, the Ch2/Ch3 Stage checks) → the Gallery door (the last check) → the boss → end screen.
    /// Restore Points: every chapter start reached, and the door (skills kept, Rapport restored to the snapshot — GDD
    /// §11.3). AutoPlay skips UI for bots and the balance harness; StartChapter / StopAfterChapter serve QA and the harness.
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
        [Tooltip("QA / Tools/HS/Play from chapter: start the run here with a preset kit (skips the opening).")]
        public int StartChapter = 1;
        public Stage StartStage = Stage.S0;
        [Tooltip("Harness: 0 plays the whole campaign; N ends the run at chapter N's campfire (outcome chapter_done).")]
        public int StopAfterChapter;
        public string[] OpeningPicks = { "pocket_sand", "crossbow" };
        public string[] CampPicks = { "quiet_feet", "bandage", "cover_story", "loosen_bolt" };
        /// <summary>AutoPlay's capstone at the chapter 4 campfire (and the preset kit's from chapter 5).</summary>
        public string CapstonePick = "hold_please";
        /// <summary>AutoPlay bot by name (idle, sloppy, supportive, follow) — serialized, so QA scenes keep it.</summary>
        public string BotName = "follow";
        /// <summary>Optional factory override (code-only).</summary>
        public Func<SidekickAgent, ISidekickCommands> Bot;

        public State Current { get; private set; }
        public event Action<State> StateChanged;
        public ChapterBootstrap Chapter { get; private set; }
        public CampfireDirector Camp { get; private set; }
        /// <summary>The Gallery's boss (chapter 5): Ashgrave, then the Mirror.</summary>
        public GalleryBoss Duel { get; private set; }
        public XpTracker Xp { get; private set; }
        /// <summary>Recall (learned at the chapter 2 campfire): Downed instead of dead, two Recalls a chapter.</summary>
        public RecallState Recall { get; private set; }
        /// <summary>The Curator's file on Callum, as far as the sidekick has seen it (scouts' fragments, the chapter 3 scrap).</summary>
        public HS.Curator.Dossier Dossier { get; private set; }
        /// <summary>The learn-as-you-go tutorial (players only: AutoPlay runs never get one).</summary>
        public HS.Tutorial.TutorialDirector Tutorial { get; private set; }
        /// <summary>Esc / Start (players only).</summary>
        public PauseMenu Pause { get; private set; }
        public string Outcome { get; private set; }
        public string LastHitTag { get; private set; }

        public int CurrentChapter => _ctx != null ? _ctx.Chapter : 1;
        /// <summary>Chapter 5's road is done: the door line and the last check are under way.</summary>
        public bool AtDoor { get; private set; }
        public const string PlayFromChapterKey = "hs.playFromChapter";
        ChapterRules Rules => CampaignSchedule.For(CurrentChapter);

        RunContext _ctx;
        int _levelAtChapterStart = 1;
        HeroAgent Hero => Chapter.Hero;
        SidekickAgent Sk => Chapter.Sidekick;

        /// <summary>Player-build automation: -hs-autoplay &lt;build&gt; -hs-seed N -hs-perf &lt;json&gt; -hs-quit (QA perf runs);
        /// -hs-shots "6;12" -hs-shots-dir &lt;dir&gt; (screenshots at real seconds).</summary>
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
                        CapstonePick = HS.Bots.BotFactory.Capstone(next ?? "supportive");
                        break;
                    case "-hs-seed": int.TryParse(next, out Seed); break;
                    case "-hs-perf": gameObject.AddComponent<HS.QA.PerfProbe>().OutPath = next; break;
                    case "-hs-quit": _quitAtEnd = true; break;
                    case "-hs-shots":
                        Shots().At = Array.ConvertAll((next ?? "").Split(';'), s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case "-hs-shots-dir": Shots().Dir = next; break;
                }
            }
        }

        bool _quitAtEnd;

        HS.QA.QaShots Shots() => TryGetComponent<HS.QA.QaShots>(out var s) ? s : gameObject.AddComponent<HS.QA.QaShots>();

        void Start()
        {
            if (!Application.isEditor) ReadCommandLine();
            HS.Tutorial.ModalGate.Clear(); // a reloaded scene (Restore Point) starts with nothing paused or blocked
            _ctx = RunContext.Current;
#if UNITY_EDITOR
            int fromMenu = UnityEditor.SessionState.GetInt(PlayFromChapterKey, 0);
            if (fromMenu > 0)
            {
                UnityEditor.SessionState.EraseInt(PlayFromChapterKey);
                StartChapter = fromMenu;
            }
#endif
            string resume = RunState.Resume;
            RunState.Resume = null;
            var point = RunState.Resolve(resume);
            Chapter = gameObject.AddComponent<ChapterBootstrap>();
            Chapter.AutoBuild = false;
            Chapter.Seed = point != null ? point.Seed : Seed;
            Chapter.SidekickBot = false;
            Chapter.StartingSkills = new string[0];
            Xp = new XpTracker();
            Xp.Bind(_ctx);
            _ctx.Register(Xp);
            Recall = new RecallState();
            _ctx.Register(Recall);
            Dossier = new HS.Curator.Dossier();
            _ctx.Register(Dossier);
            _ctx.Register(this);
            Chapter.BuildRun();
            Sk.DownedTimedOut += OnDownedTimedOut;
            PrepareChapter(point != null ? point.Chapter : Mathf.Clamp(StartChapter, 1, CampaignSchedule.Chapters));
            ScreenFade.Ensure(UIRoot.Ensure());
            if (AutoPlay)
            {
                var pc = Sk.GetComponent<PlayerCommands>();
                if (pc != null) pc.enabled = false;
                Sk.Commands = Bot != null ? Bot(Sk) : HS.Bots.BotFactory.Make(BotName, Sk);
            }
            if (!AutoPlay)
            {
                Chapter.Hud.InsightOn = HS.Tutorial.TutorialProgress.InsightDefault;
                Tutorial = HS.Tutorial.TutorialDirector.Create(this);
                Pause = PauseMenu.Create(UIRoot.Ensure(), this);
            }
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
                if (resume == "door")
                {
                    Hero.Route.SetNodes(new List<RouteNode>());
                    StartDuel();
                }
                else
                {
                    RunState.ForgetAfter(point.Chapter);
                    EnterChapter(false);
                }
                return;
            }
            RunState.Clear();
            if (CurrentChapter > 1)
            {
                ApplyPreset();
                EnterChapter(true);
                return;
            }
            BeginOpening();
        }

        /// <summary>Build chapter N in place and set everything the schedule says about it.</summary>
        void PrepareChapter(int ch)
        {
            var rules = CampaignSchedule.For(ch);
            Chapter.BuildChapter(ch, Chapter.Seed);
            Sk.GetComponent<SidekickSkills>().System.SetChapter(ch);
            if (Hero.Module is HS.Hero.Callum.CallumModule cm) cm.ApplyChapter(ch, rules.Unlocks, rules.Recovery);
            Recall.BeginChapter();
            Sk.CanBeDowned = Recall.Learned;
            Hero.Hunger.Enabled = rules.Hunger;
            Hero.Hunger.Paused = false;
            if (rules.Hunger && Sk.Rations.Count == 0) Sk.Rations.Give(1); // the camp's leftovers
            var rooms = Chapter.Chapter.Rooms;
            Xp.BeginChapter(r => CampaignSchedule.RoomPot(ch, rooms[Mathf.Clamp(r, 0, rooms.Count - 1)].XpPot));
            AtDoor = false;
        }

        /// <summary>A run started past chapter 1 (QA, "Play from chapter N"): the kit a thorough player would have by now.</summary>
        void ApplyPreset()
        {
            var skills = Sk.GetComponent<SidekickSkills>();
            var build = HS.Bots.BotFactory.Build("supportive");
            var ids = AutoPlay ? OpeningPicks.Concat(CampPicks) : build.opening.Concat(build.camp);
            skills.System.AtCamp = true;
            foreach (var id in ids) skills.Learn(id);
            skills.System.AtCamp = false;
            // Past chapter 4's campfire: the capstone came with it.
            if (CurrentChapter > 4) skills.System.LearnCapstone(SkillCatalog.Load()?.Get(CapstonePick));
            int level = CampaignSchedule.LevelTarget(CurrentChapter - 1);
            Sk.SetLevel(level, true);
            Xp.Restore(XpTracker.Thresholds[Mathf.Clamp(level - 2, 0, XpTracker.Thresholds.Length - 1)]);
            Hero.ApplyStage(StartStage);
            Recall.Learned = CurrentChapter > 2; // learned at the chapter 2 campfire
            Sk.CanBeDowned = Recall.Learned;
            Chapter.Hud.MeetHero("CALLUM", true);
        }

        /// <summary>
        /// Nobody came in time (GDD §4.2): she's back at the room's entrance at 30%, he takes a wound for fighting on alone,
        /// and whatever that room still had on offer is gone.
        /// </summary>
        void OnDownedTimedOut()
        {
            if (!Sk.IsDowned || Current == State.End) return;
            Vector3 at;
            if (Current == State.Duel && Chapter.Chapter.Boss != null) at = Chapter.Chapter.Boss.transform.position + new Vector3(-1.8f, 0.05f, 0.4f);
            else
            {
                var rooms = Chapter.Chapter.Rooms;
                int r = Mathf.Clamp(Chapter.Chapter.RoomIndexAt(Sk.Position.z), 0, rooms.Count - 1);
                at = rooms[r].transform.position + new Vector3(-1.6f, 0.05f, 1.2f);
            }
            Sk.Motor.Teleport(at);
            Sk.Rise(0.3f);
            if (Hero.IsAlive) Hero.Wounds.Add(WoundType.SwordArmStrain);
            _ctx.Get<OpportunityDirector>()?.EndOfFight("you were down");
            _ctx.Events.RaiseNotice("You come round at the last door. He fought on without you.");
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
            if (snapshot) RunState.SetChapterStart(CurrentChapter, Snapshot());
            _levelAtChapterStart = Sk.Level;
            SetState(State.Chapter);
            SimLoop.Instance.Paused = false;
            _ctx.Events.RaiseNotice($"PARTY: SIR CALLUM <size=80%>(Hero)</size>  ·  YOU <size=80%>(HERO's SIDEKICK)</size>\nQUEST: {Rules.Name}.");
            if (snapshot && CurrentChapter == 1) StartCoroutine(MeetCallum());
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
            if (Current == State.Chapter && !AtDoor)
            {
                if (!Hero.IsAlive || !Sk.IsAlive)
                {
                    EndRun(false, CuratorDiagnosis.ForRoad(LastHitTag, !Sk.IsAlive), !Sk.IsAlive);
                    return;
                }
                if (Hero.Route.AtEnd && Hero.Position.z > Chapter.Chapter.ChapterLength - 6f)
                {
                    if (Chapter.Def.Final) StartCoroutine(ToDoor());
                    else ToCamp();
                }
            }
        }

        void ToCamp()
        {
            Hero.Hunger.Paused = true;
            SetState(State.Camp);
            SimLoop.Instance.Paused = true;
            int ch = CurrentChapter;
            Transition(() =>
            {
                ClearEnemies();
                int levelNow = XpTracker.LevelFor(Xp.Xp);
                int cap = CampaignSchedule.LevelTarget(4);
                // The camp always brings at least one lesson, up to the last level (GDD §4.1: 13 levels across Ch1–4).
                int picks = Sk.Level >= cap ? 0 : Mathf.Min(cap - _levelAtChapterStart, Mathf.Max(1, levelNow - _levelAtChapterStart));
                Sk.SetLevel(_levelAtChapterStart + picks, true);
                Camp = new GameObject("Campfire").AddComponent<CampfireDirector>();
                if (Fast) Camp.AutoSceneSeconds = 0f;
                Camp.Finished += () =>
                {
                    if (StopAfterChapter == ch)
                    {
                        EndRun(true, new List<string> { $"CHAPTER {ch}: {CampaignSchedule.For(ch).Name.ToUpperInvariant()} — CLEARED." }, false, null, "chapter_done");
                        return;
                    }
                    Transition(NextChapter);
                };
                var next = CampaignSchedule.For(ch + 1);
                if (CampaignSchedule.For(ch).LearnsRecall) Recall.Learned = true;
                Camp.Begin(Chapter.Chapter.Campfire, Hero, Sk, new CampfireDirector.Options
                {
                    Chapter = ch, Check = CampaignSchedule.For(ch).CampCheck, Picks = picks, AutoPicks = AutoPlay, AutoPickIds = CampPicks,
                    LearnsRecall = CampaignSchedule.For(ch).LearnsRecall,
                    ReadsDossier = CampaignSchedule.For(ch).DossierScrap,
                    CapstoneReveal = CampaignSchedule.For(ch).CapstoneReveal, AutoCapstone = CapstonePick,
                    ContinueLabel = $"CONTINUE  »  {next.Name.ToUpperInvariant()}",
                });
                SimLoop.Instance.Paused = false;
            });
        }

        void NextChapter()
        {
            if (Camp != null) Destroy(Camp.gameObject);
            Camp = null;
            Chapter.TeardownChapter();
            PrepareChapter(CurrentChapter + 1);
            EnterChapter(true);
        }

        static readonly string[] DoorLines =
        {
            "Stay behind me. This is my fight.",
            "Whatever is behind this door... keep your distance. And your eyes open.",
            "If I don't look back in there, it isn't because I've forgotten you.",
            "Whatever comes, I will not lie about who helped me.",
        };

        /// <summary>The Gallery door (GDD §11.3.4: "the hero's own line at the door"): the last Stage check, a Restore Point.</summary>
        System.Collections.IEnumerator ToDoor()
        {
            AtDoor = true;
            Hero.Hunger.Paused = true;
            var ledger = _ctx.Get<RapportLedger>();
            Hero.ApplyStage(StageEvaluator.Evaluate(Hero.Stage, ledger != null ? ledger.CaptureRate : 0f, StageCheck.Door));
            _ctx.Events.RaiseBark("callum", DoorLines[(int)Hero.Stage], 3.6f, 3);
            if (!Fast) yield return new WaitForSeconds(3.8f);
            if (Current != State.Chapter) yield break;
            RunState.Door = Snapshot();
            Hero.Route.SetNodes(new List<RouteNode>());
            Transition(StartDuel);
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
            Duel = new GameObject("GalleryBoss").AddComponent<GalleryBoss>();
            Duel.Begin(Chapter.Chapter.Boss, Hero, Sk);
            Chapter.Camera.AddFocus(Duel.Ashgrave.transform, 0.6f, 1.6f);
            Chapter.Camera.TravelDirection = () => Vector3.forward;
            _ctx.Get<OpportunityDirector>()?.Judge?.NewFight();
            Duel.PhaseChanged += p =>
            {
                switch (p)
                {
                    case GalleryBoss.Phase.Duel: Chapter.Hud.ShowBoss(Duel.Ashgrave, "LORD ASHGRAVE"); break;
                    case GalleryBoss.Phase.Unmasking: Chapter.Hud.ShowBoss(null, ""); break;
                    case GalleryBoss.Phase.Mirror:
                        Chapter.Hud.ShowBoss(Duel.Mirror, "THE MIRROR");
                        Chapter.Camera.AddFocus(Duel.Mirror.transform, 0.6f, 1.6f);
                        _ctx.Get<OpportunityDirector>()?.Judge?.NewFight();
                        break;
                    case GalleryBoss.Phase.Aftermath: Chapter.Hud.ShowBoss(null, ""); break;
                    case GalleryBoss.Phase.Won:
                        EndRun(true, new List<string> { "CHAPTER 5: THE GALLERY — CLEARED.", "CLASS: CALLUM's SIDEKICK. STATUS: LISTED." }, false,
                            CuratorDiagnosis.WeLine(Hero.Stage));
                        break;
                    case GalleryBoss.Phase.Lost:
                        EndRun(false, CuratorDiagnosis.ForGallery(Hero.Stage, Duel.LossPhase, Duel.LossCause, Duel.SidekickDied), Duel.SidekickDied);
                        break;
                }
            };
        }

        // ------------------------------------------------------------------ end
        void EndRun(bool won, List<string> diagnosis, bool sidekickDied, string quote = null, string outcome = null)
        {
            if (Current == State.End) return;
            SetState(State.End);
            Outcome = outcome ?? (won ? "won" : sidekickDied ? "sidekick_died" : "hero_died");
            Debug.Log($"[Flow] outcome {Outcome}");
            HS.Audio.AudioDirector.Instance?.OnFlow("End", won);
            _ctx.Get<OpportunityDirector>()?.EndOfFight(won ? "the run ended" : "he fell");
            var ledger = _ctx.Get<RapportLedger>();
            var m = new EndScreen.Model
            {
                Error = !won,
                Title = outcome == "chapter_done" ? diagnosis[0] : won ? "THE GALLERY — WON" : sidekickDied ? "SIDEKICK: DECEASED. NO RECALL AVAILABLE." : "HERO: CALLUM. DECEASED",
                Diagnosis = diagnosis,
                Quote = quote,
                Lines = ledger != null ? PostMortem.From(ledger) : new List<PostMortem.Line>(),
            };
            // First loss: what a Restore Point is (the tutorial's inline lesson; players only).
            if (!won && !AutoPlay && HS.Tutorial.TutorialProgress.TipsEnabled && !HS.Tutorial.TutorialProgress.IsSeen("restore"))
            {
                m.Hint = HS.Tutorial.Lessons.Get("restore")?.Body;
                HS.Tutorial.TutorialProgress.MarkSeen("restore");
            }
            if (won && outcome == null)
            {
                // GDD §8 ending: the System offers a class re-roll, and she may decline it.
                m.Buttons.Add(("RE-ROLL CLASS", () => ShowClassRoll(true)));
                m.Buttons.Add(("DECLINE", () => ShowClassRoll(false)));
            }
            else if (won) m.Buttons.Add(("PLAY AGAIN · NEW ROAD", PlayAgain));
            else
            {
                if (RunState.Door != null) m.Buttons.Add(("RESTORE · BEFORE THE DOOR", () => Restore("door")));
                for (int c = CurrentChapter; c >= 1; c--)
                {
                    int chapter = c;
                    if (RunState.ChapterStartOf(chapter) != null)
                        m.Buttons.Add(($"RESTORE · CHAPTER {chapter} START", () => Restore("chapter:" + chapter)));
                }
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

        /// <summary>The re-roll, answered: the System tries, and the class it lands on is the one she already has.</summary>
        void ShowClassRoll(bool reroll)
        {
            var m = new EndScreen.Model
            {
                Title = reroll ? "» SYSTEM: RE-ROLLING CLASS" : "» SYSTEM: RE-ROLL DECLINED",
                Diagnosis = reroll
                    ? new List<string> { "ROLLING...", "CLASS: HERO.", "ERROR: CLASS 'HERO' IS TAKEN (CALLUM). REVERTING.", "CLASS: CALLUM's SIDEKICK. STATUS: LISTED." }
                    : new List<string> { "CLASS: CALLUM's SIDEKICK. STATUS: LISTED.", "ENTRY CREATED. FIRST OF ITS KIND." },
                Quote = reroll ? "Taken? Good. I'd have hated to break in a new hero." : CuratorDiagnosis.WeLine(Hero.Stage),
                Lines = _pendingEnd != null ? _pendingEnd.Lines : new List<PostMortem.Line>(),
            };
            m.Buttons.Add(("PLAY AGAIN · NEW ROAD", PlayAgain));
            m.Buttons.Add(("QUIT", Quit));
            EndScreen.Show(UIRoot.Ensure(), m);
        }

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
                Chapter = CurrentChapter,
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
                Hunger = Hero.Hunger.Value,
                RecallLearned = Recall.Learned,
                Dossier = Dossier.Snapshot(),
                Rations = Sk.Rations.Count,
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
            Hero.Hunger.Restore(p.Hunger);
            Recall.Learned = p.RecallLearned;
            Dossier.Restore(p.Dossier);
            Sk.CanBeDowned = Recall.Learned;
            Sk.Rations.Restore(p.Rations);
            Hero.ApplyWoundEffects();
            if (p.HeroHp > 0f) Hero.Health.SetCurrent(p.HeroHp);
        }
    }
}
