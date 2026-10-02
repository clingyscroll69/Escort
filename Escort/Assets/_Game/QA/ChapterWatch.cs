using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Hero.Callum;
using UnityEngine;

namespace HS.QA
{
    /// <summary>
    /// QA observer for integrated runs: writes a timestamped event log (Library/Agent/watch_&lt;prefix&gt;.log) and
    /// captures frames on key events and periodically, so a whole play session can be reviewed afterwards.
    /// </summary>
    public sealed class ChapterWatch : MonoBehaviour
    {
        public string Prefix = "chapter";
        public float Period = 8f;
        public float MaxTime = 300f;
        public float TimeScale = 1f;
        public int MaxShots = 40;
        /// <summary>Custom end condition (e.g. the duel's outcome); default: hero dead or route complete.</summary>
        public System.Func<bool> Done;
        /// <summary>QA only: the hero can't be hurt on the road (to exercise camp → duel with a passive bot).</summary>
        public bool GodChapter;
        /// <summary>QA: play the 3-second opening a later run gets (as if this were a replay).</summary>
        public bool ShortOpening;

        void Awake()
        {
            if (ShortOpening) HS.Flow.RunState.Runs = 1; // GameFlow.Start counts this run as the second
        }
        /// <summary>Extra captures at real (unscaled) seconds — for menus/opening while the sim is paused.</summary>
        public float[] RealShots = new float[0];
        public float MaxRealTime = 0f;
        int _realShot;
        public float EndDelay;
        float _endAt = -1f;

        readonly StringBuilder _log = new StringBuilder();
        float _nextShot = 2f;
        int _shots;
        bool _hooked, _done;
        string _lastRule;
        string _pendingShot;
        float _pendingAt;

        string LogPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/Agent/watch_" + Prefix + ".log"));

        float _startReal;

        void Start()
        {
            _startReal = Time.realtimeSinceStartup;
            // Editor-assigned lambdas don't survive entering play mode: wire the duel's end condition here.
            var flow = FindAnyObjectByType<HS.Flow.GameFlow>();
            if (flow != null && Done == null)
            {
                Done = () => flow.Current == HS.Flow.GameFlow.State.End;
                if (EndDelay <= 0f) EndDelay = 12f;
            }
            var duel = FindAnyObjectByType<HS.Boss.DuelBootstrap>();
            if (duel != null && Done == null)
            {
                Done = () => duel.Director != null && (duel.Director.Current == HS.Boss.RiggedDuelDirector.Phase.Won || duel.Director.Current == HS.Boss.RiggedDuelDirector.Phase.Lost);
                if (EndDelay <= 0f) EndDelay = 12f;
            }
            Time.timeScale = TimeScale; // presentation (animators, UI timers)
            if (SimLoop.Instance != null) SimLoop.Instance.TimeScale = TimeScale; // the simulation clock
            File.WriteAllText(LogPath, "");
        }

        void Hook()
        {
            var ctx = RunContext.Current;
            if (ctx == null || ctx.Hero == null) return;
            _hooked = true;
            var ev = ctx.Events;
            ev.DuelStarted += (h, t) => { Log($"DUEL start vs {Name(t)}"); Shot("duel_" + Name(t), 1.4f); };
            ev.DuelEnded += (h, t, r) => Log($"DUEL end vs {Name(t)}: {r}");
            ev.Death += (a, d) => Log($"DEATH {Name(a)} by {Name(d.Source)} ({d.Tag})");
            ev.Sabotage += s => Log($"SABOTAGE {s.Tag} {s.Severity} victim={Name(s.Victim)}");
            ev.Bark += b => Log($"BARK {b.SpeakerId}: {b.Text}");
            ev.SystemNotice += n => Log($"SYSTEM {n}");
            ev.ThoughtPopup += n => Log($"THOUGHT {n}");
            ev.RoomEntered += i => { Log($"ROOM {i} entered"); Shot($"room{i}_enter", 0.5f); };
            ev.RoomCleared += i => { Log($"ROOM {i} cleared"); Shot($"room{i}_cleared", 0.3f); };
            ev.HazardSprung += (pos, kind, victim) => { Log($"HAZARD {kind} sprung by {Name(victim)}"); Shot("hazard_" + kind, 0.25f); };
            ev.WoundChanged += (who, type, added) => Log($"WOUND {(added ? "+" : "-")}{type} on {Name(who)}");
            ev.Damage += (d, applied) =>
            {
                if (applied >= 10f) Log($"HIT {Name(d.Source)} -> {Name(d.Target)} {applied:0} ({d.Tag})");
            };
            if (ctx.Hero is HeroAgent hero)
            {
                hero.Brain.RuleChanged += (prev, next) =>
                {
                    var id = next?.Id ?? "none";
                    Log($"RULE {prev?.Id ?? "none"} -> {id}");
                    if (id == "callum_fallback" || id == "callum_wait_unready") Shot("rule_" + id, 0.6f);
                };
                if (hero.Module is CallumModule cm)
                {
                    cm.Caught += (e, stone) => Log($"CAUGHT {e.Tag} {e.Severity} stone={stone} honor={cm.Honor:0}");
                    cm.UnseenDeed += e => Log($"UNSEEN {e.Tag}");
                    cm.SpoiledDuel += e => Log($"SPOILED duel vs {Name(e)}");
                }
            }
            foreach (var st in HS.Rooms.ChronicleStone.All)
                st.StateChanged += (stone, state) =>
                {
                    Log($"STONE {stone.transform.parent?.parent?.name}/{stone.name} -> {state}");
                    if (state == HS.Rooms.ChronicleStone.StoneState.Active) Shot("stone_active", 0.4f);
                };
            var stones = ctx.Get<HS.Rooms.StoneSystem>();
            if (stones != null) stones.Recorded += (stone, flaw) => Log($"CLIP {flaw} by {stone.name} intel={stones.IntelLevel}");
            var ledger = ctx.Get<HS.Rapport.RapportLedger>();
            if (ledger != null)
                ledger.Changed += e => Log($"RAPPORT {e.Kind} {e.Id} {e.Points:+0.##;-0.##;0} ({e.Note}) offered={ledger.Offered:0} earned={ledger.Earned:0} pen={ledger.EffectivePenalties:0.#} rate={ledger.CaptureRate:P0}");
            var gf = FindAnyObjectByType<HS.Flow.GameFlow>();
            if (gf != null && GodChapter && ctx.Hero != null) ctx.Hero.Health.Invulnerable = true;
            if (gf != null) gf.StateChanged += st =>
            {
                if (GodChapter && st == HS.Flow.GameFlow.State.Camp && ctx.Hero != null) ctx.Hero.Health.Invulnerable = false;
                Log($"FLOW -> {st} stage={(ctx.Hero as HeroAgent)?.Stage} rate={(ctx.Get<HS.Rapport.RapportLedger>()?.CaptureRate ?? 0f):P0} xp={gf.Xp?.Xp}");
                if (st == HS.Flow.GameFlow.State.Camp) Shot("camp", 4.5f);
            };
            Log($"START seed={ctx.Seed} hero={Name(ctx.Hero)} stage={(ctx.Hero as HeroAgent)?.Stage}");
            // Dynamic visuals must not be frozen into a static batch.
            int frozen = 0, dyn = 0;
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!(mb is HS.Skills.ArmableProp) && !(mb is HS.Rooms.HazardMarker) && !(mb is HS.Rooms.StoneAnchor)) continue;
                foreach (var r in mb.GetComponentsInChildren<MeshRenderer>(true))
                {
                    dyn++;
                    if (r.isPartOfStaticBatch) frozen++;
                }
            }
            Log($"BATCH dynamic renderers={dyn} frozen-in-static-batch={frozen}");
        }

        static string Name(Agent a) => a == null ? "-" : a.name.Replace("(Clone)", "");

        void Log(string s)
        {
            var t = RunContext.Current != null ? RunContext.Current.SimTime : 0f;
            var line = string.Format(CultureInfo.InvariantCulture, "{0,7:0.00} {1}", t, s);
            _log.AppendLine(line);
            File.AppendAllText(LogPath, line + "\n");
        }

        void Shot(string name, float delay)
        {
            if (_pendingShot != null) return;
            _pendingShot = name;
            _pendingAt = Time.time + delay;
        }

        string _lastIntent;

        void LateUpdate()
        {
            if (_done) return;
            if (!_hooked) { Hook(); return; }
            var sb = RunContext.Current?.Sidekick != null ? RunContext.Current.Sidekick.GetComponent<HS.Bots.SupportiveBot>() : null;
            if (sb != null && sb.Intent != _lastIntent)
            {
                _lastIntent = sb.Intent;
                Log("BOT " + sb.Intent);
            }
            var ctx = RunContext.Current;
            float t = ctx.SimTime;
            var hero = ctx.Hero as HeroAgent;
            if (_realShot < RealShots.Length && Time.realtimeSinceStartup - _startReal >= RealShots[_realShot])
                Capture("real" + Mathf.RoundToInt(RealShots[_realShot++]).ToString("00"));
            if (MaxRealTime > 0f && Time.realtimeSinceStartup - _startReal > MaxRealTime && !_done)
            {
                Log("END real-time limit");
                Summary();
                _done = true;
                return;
            }
            if (hero != null && hero.ActiveRuleId != _lastRule) _lastRule = hero.ActiveRuleId;
            if (_pendingShot != null && Time.time >= _pendingAt)
            {
                Capture(_pendingShot);
                _pendingShot = null;
            }
            else if (t >= _nextShot)
            {
                _nextShot = t + Period;
                Capture("t" + Mathf.RoundToInt(t).ToString("000"));
            }
            bool heroDead = hero != null && !hero.IsAlive;
            bool done = Done != null ? Done() : heroDead || (hero != null && hero.Route.AtEnd);
            if (done && _endAt < 0f) _endAt = Time.unscaledTime + EndDelay;
            if (t > MaxTime || (done && Time.unscaledTime >= _endAt))
            {
                Log(heroDead ? "END hero died" : done ? "END done" : "END time limit");
                Summary();
                Capture("end");
                _done = true;
            }
        }

        void Capture(string tag)
        {
            if (_shots >= MaxShots) return;
            _shots++;
            var ctx = RunContext.Current;
            var hero = ctx.Hero as HeroAgent;
            var file = QaCapture.Capture(Camera.main, $"{Prefix}_{_shots:00}_{tag}", 1280, 720);
            Log($"SHOT {Path.GetFileName(file)} rule={hero?.ActiveRuleId} hp={hero?.Health.Current:0}/{hero?.Health.Max:0}");
        }

        void Summary()
        {
            var ctx = RunContext.Current;
            var enemies = FindObjectsByType<EnemyAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var byState = enemies.GroupBy(e => e.State).Select(g => $"{g.Key}={g.Count()}");
            var hero = ctx.Hero as HeroAgent;
            var cm = hero != null ? hero.Module as CallumModule : null;
            var l = ctx.Get<HS.Rapport.RapportLedger>();
            if (l != null) Log($"RAPPORT-SUMMARY offered={l.Offered:0}/{l.Budget(ctx.Chapter):0} earned={l.Earned:0} penalties={l.EffectivePenalties:0.#} (raw {l.RawPenaltiesIn(ctx.Chapter):0.#}) rate={l.CaptureRate:P1}");
            Log($"SUMMARY hero hp={hero?.Health.Current:0} honor={cm?.Honor:0} sidekick hp={(ctx.Sidekick != null ? ctx.Sidekick.Health.Current : 0):0} enemies: {string.Join(" ", byState)} fps~{1f / Mathf.Max(0.001f, Time.smoothDeltaTime):0}");
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/Agent/watch_" + Prefix + ".done")), "1");
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
            if (SimLoop.Instance != null) SimLoop.Instance.TimeScale = 1f;
        }
    }
}
