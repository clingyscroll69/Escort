using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS.Core;
using HS.Flow;
using HS.Hero;
using HS.Sidekick;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HS.QA
{
    /// <summary>
    /// Balance harness (GDD §11.4): runs the whole slice for seeds × bots at high sim speed, each run in its own throwaway
    /// scene, and writes one CSV row per run plus a summary to docs/qa/balance/. Bots: idle (solo rate, encounters
    /// isolated), sloppy, supportive.
    /// </summary>
    public sealed class BalanceHarness : MonoBehaviour
    {
        public sealed class Row
        {
            public int Seed;
            public string Bot;
            /// <summary>The chapter the run ended in.</summary>
            public int Chapter = 1;
            public string Outcome, Reached;
            public int RoomsCleared, Encounters, SoloOk, SeriousWounds, Xp;
            public string StageAtCamp = "-";
            public float RateAtCamp = -1f, DuelSeconds, SimSeconds, Offered, Earned, Penalties, FinalRate;
            public string DuelResult = "-";
            public ulong Hash;
            public string Rooms = "";
        }

        public List<int> Seeds = new List<int> { 1, 2, 3 };
        public List<string> Bots = new List<string> { "idle", "sloppy", "supportive" };
        public int TicksPerFrame = 40;
        public float MaxSimSeconds = 420f;
        [Tooltip("The chapter each run starts at (later chapters start with a preset kit).")]
        public int StartChapter = 1;
        [Tooltip("Each run ends at this chapter's campfire (chapter_done); 0 plays through to the boss.")]
        public int StopAfterChapter = 1;
        public string OutDir;
        public readonly List<Row> Rows = new List<Row>();
        public bool Done { get; private set; }

        static string ShotsRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/qa/balance"));

        public bool AutoRun = true;

        void Start()
        {
            if (!AutoRun) return;
            ReadArgs();
            StartCoroutine(RunAll());
        }

        /// <summary>Library/Agent/harness_args.json: {"seeds":"1-20","bots":"idle,sloppy,supportive","maxsim":"420","chapters":"1"}</summary>
        void ReadArgs()
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/Agent/harness_args.json"));
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            string Get(string key)
            {
                int i = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
                if (i < 0) return null;
                int a = json.IndexOf('"', json.IndexOf(':', i) + 1) + 1;
                return json.Substring(a, json.IndexOf('"', a) - a);
            }
            var seeds = Get("seeds");
            if (!string.IsNullOrEmpty(seeds))
            {
                Seeds.Clear();
                foreach (var part in seeds.Split(','))
                {
                    var r = part.Split('-');
                    int lo = int.Parse(r[0]), hi = r.Length > 1 ? int.Parse(r[1]) : lo;
                    for (int k = lo; k <= hi; k++) Seeds.Add(k);
                }
            }
            var bots = Get("bots");
            if (!string.IsNullOrEmpty(bots)) Bots = bots.Split(',').Select(b => b.Trim()).ToList();
            var chapters = Get("chapters");
            if (!string.IsNullOrEmpty(chapters))
            {
                var r = chapters.Split('-');
                StartChapter = int.Parse(r[0]);
                StopAfterChapter = r.Length > 1 ? int.Parse(r[1]) : StartChapter;
            }
            var max = Get("maxsim");
            if (!string.IsNullOrEmpty(max)) MaxSimSeconds = float.Parse(max, CultureInfo.InvariantCulture);
        }

        public IEnumerator RunAll()
        {
            Done = false;
            AudioListener.volume = 0f; // 40 ticks a frame: nobody wants to hear that
            foreach (var bot in Bots)
            foreach (var seed in Seeds)
            {
                var row = new Row { Seed = seed, Bot = bot };
                yield return RunOne(row);
                Rows.Add(row);
            }
            Write();
            AudioListener.volume = 1f;
            Done = true;
        }

        public IEnumerator RunOne(Row row)
        {
            var scene = SceneManager.CreateScene($"harness_{row.Bot}_{row.Seed}_{Time.frameCount}");
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            RunState.Clear();
            // The run ticks on its own loop, from tick 0. A loop left running outside the run (a test that built rooms
            // without a TearDown) would win SimLoop's one-instance check, and the run would tick on it at wall-clock
            // speed from wherever it had got to: slow, and a different state hash every time.
            if (SimLoop.Instance != null)
            {
                Debug.LogWarning($"[Harness] a SimLoop was left running ({SimLoop.Instance.gameObject.scene.name}/{SimLoop.Instance.name}, tick {SimLoop.Instance.TickIndex}); the run replaces it");
                DestroyImmediate(SimLoop.Instance);
            }
            var loopGo = new GameObject("SimLoop");
            var loop = loopGo.AddComponent<SimLoop>();
            loop.FastTicksPerFrame = TicksPerFrame; // from the very first frame: no real-time ticks, ever (determinism)
            var ctx = new GameObject("RunContext").AddComponent<RunContext>();
            HeroAgent hero = null;
            int roomWounds = 0, currentRoom = -1;
            string currentModule = "";
            GameFlow flowRef = null;
            // A room counts as solo-OK when he leaves it alive (next room or the camp) with at most one serious wound.
            void CloseRoom(bool alive)
            {
                if (currentRoom < 0) return;
                bool ok = alive && roomWounds <= 1;
                if (ok) row.SoloOk++;
                row.Rooms += $"{currentRoom}:{currentModule}:{(alive ? (ok ? "ok" : "wounded") : "died")};";
                currentRoom = -1;
            }
            // Subscribe before anything ticks (room 0 is entered on the first tick).
            ctx.Events.RoomEntered += r =>
            {
                CloseRoom(true);
                currentRoom = r;
                currentModule = flowRef != null && flowRef.Chapter != null && flowRef.Chapter.Chapter != null && r < flowRef.Chapter.Chapter.Rooms.Count
                    ? flowRef.Chapter.Chapter.Rooms[r].ModuleId : "?";
                roomWounds = 0;
                row.Encounters++;
            };
            ctx.Events.WoundChanged += (who, type, added) =>
            {
                if (!added || !(who is HeroAgent)) return;
                if (type != "SprainedAnkle" && type != "SwordArmStrain")
                {
                    row.SeriousWounds++;
                    roomWounds++;
                }
            };
            ctx.Events.RoomCleared += r => row.RoomsCleared++;
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.enabled = false; // nothing to see at 40 ticks a frame
            var flowGo = new GameObject("GameFlow");
            flowGo.SetActive(false);
            var flow = flowGo.AddComponent<GameFlow>();
            flowRef = flow;
            flow.Seed = row.Seed;
            flow.AutoPlay = true;
            flow.Fast = true;
            flow.IsolateEncounters = row.Bot == "idle";
            flow.StartChapter = StartChapter;
            flow.StopAfterChapter = StopAfterChapter;
            var build = HS.Bots.BotFactory.Build(row.Bot);
            flow.OpeningPicks = build.opening;
            flow.CampPicks = build.camp;
            string bot = row.Bot;
            flow.BotName = build.bot;
            flowGo.SetActive(true);
            flow.StateChanged += st =>
            {
                if (st == GameFlow.State.Camp) CloseRoom(true);
                row.Reached = st == GameFlow.State.End ? row.Reached : st.ToString();
                if (st == GameFlow.State.Camp)
                {
                    var l = ctx.Get<HS.Rapport.RapportLedger>();
                    row.RateAtCamp = l != null ? l.CaptureRate : 0f;
                }
                if (st == GameFlow.State.Duel) row.StageAtCamp = hero.Stage.ToString();
            };
            yield return null; // GameFlow.Start builds the chapter
            hero = flow.Chapter.Hero;
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false; // nobody watches at 40x
            float duelStart = -1f;
            while (flow.Current != GameFlow.State.End && ctx.SimTime < MaxSimSeconds)
            {
                if (flow.Current == GameFlow.State.Duel && duelStart < 0f) duelStart = ctx.SimTime;
                yield return null;
            }
            CloseRoom(hero != null && hero.IsAlive);
            row.Outcome = flow.Current == GameFlow.State.End ? flow.Outcome : "timeout";
            row.Chapter = flow.CurrentChapter;
            if (row.Reached == null) row.Reached = "Chapter";
            if (flow.Duel != null)
            {
                row.DuelResult = flow.Duel.Current.ToString();
                row.DuelSeconds = flow.Duel.DuelTime;
            }
            var led = ctx.Get<HS.Rapport.RapportLedger>();
            if (led != null)
            {
                row.Offered = led.Offered;
                row.Earned = led.Earned;
                row.Penalties = led.EffectivePenalties;
                row.FinalRate = led.CaptureRate;
            }
            row.Xp = flow.Xp != null ? flow.Xp.Xp : 0;
            row.SimSeconds = ctx.SimTime;
            row.Hash = StateHash.Compute();
            loop.FastTicksPerFrame = 0;
            SceneManager.SetActiveScene(previous);
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        public static string Csv(IEnumerable<Row> rows)
        {
            var sb = new StringBuilder("seed,bot,outcome,reached,chapter,rooms_cleared,encounters,solo_ok,serious_wounds,stage_at_camp,rate_at_camp,duel,duel_s,offered,earned,penalties,final_rate,xp,sim_s,hash,rooms\n");
            var ci = CultureInfo.InvariantCulture;
            foreach (var r in rows)
                sb.AppendLine(string.Join(",", r.Seed, r.Bot, r.Outcome, r.Reached, r.Chapter, r.RoomsCleared, r.Encounters, r.SoloOk, r.SeriousWounds, r.StageAtCamp,
                    r.RateAtCamp.ToString("0.00", ci), r.DuelResult, r.DuelSeconds.ToString("0.0", ci), r.Offered.ToString("0", ci), r.Earned.ToString("0", ci),
                    r.Penalties.ToString("0.0", ci), r.FinalRate.ToString("0.00", ci), r.Xp, r.SimSeconds.ToString("0", ci), r.Hash.ToString("x16"), r.Rooms));
            return sb.ToString();
        }

        public static string Summary(IEnumerable<Row> rows)
        {
            var sb = new StringBuilder("# Balance harness summary\n\n| bot | runs | reached camp | reached duel | duel won | solo encounters | mean capture rate at camp | S1 at camp |\n|---|---|---|---|---|---|---|---|\n");
            foreach (var g in rows.GroupBy(r => r.Bot))
            {
                int n = g.Count();
                int camp = g.Count(r => r.Reached == "Camp" || r.Reached == "Duel" || r.Outcome == "chapter_done");
                int duel = g.Count(r => r.Reached == "Duel");
                int won = g.Count(r => r.Outcome == "won");
                int enc = g.Sum(r => r.Encounters), ok = g.Sum(r => r.SoloOk);
                var rates = g.Where(r => r.RateAtCamp >= 0f).Select(r => r.RateAtCamp).ToList();
                int s1 = g.Count(r => r.StageAtCamp == "S1");
                sb.AppendLine($"| {g.Key} | {n} | {camp}/{n} | {duel}/{n} | {won}/{n} | {ok}/{enc} ({(enc > 0 ? 100f * ok / enc : 0):0}%) | {(rates.Count > 0 ? rates.Average() : 0):P0} | {s1}/{n} |");
            }
            return sb.ToString();
        }

        void Write()
        {
            var dir = string.IsNullOrEmpty(OutDir) ? ShotsRoot : OutDir;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "runs.csv"), Csv(Rows));
            File.WriteAllText(Path.Combine(dir, "summary.md"), Summary(Rows));
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/Agent/harness.done")), Rows.Count.ToString());
        }
    }
}
