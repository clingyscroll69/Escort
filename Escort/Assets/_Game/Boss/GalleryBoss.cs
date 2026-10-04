using System;
using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Hero.Callum;
using HS.Presentation;
using HS.Rooms;
using HS.Sidekick;
using HS.Skills;
using UnityEngine;

namespace HS.Boss
{
    /// <summary>
    /// The Gallery (campaign spec §5; GDD §4.5, §4.5a): the run's last fight.
    /// Phase 0, the diagnosis (12 s): the figure with the pendant names Callum's flaw, then dissolves into Lord Ashgrave.
    /// Phase 1, the flaw trap: the terms by Stage (S3 voids them), the formal duel; at T+25 s (or at once if Ashgrave falls
    /// to 60%) the hidden gallery stands. Phase 2, the persona: Ashgrave drops the pretence. Phase 3, the Mirror: the
    /// unmasking (10 s), then Callum's S0 self (<see cref="MirrorBrain"/>), finished only by the Duet
    /// (<see cref="DuetFinisher"/>). Aftermath: the pendant glows, his "we" line, STATUS: LISTED.
    /// The slice's <see cref="RiggedDuelDirector"/> stays for the QA duel scene; this replaces it in the campaign.
    /// </summary>
    public sealed class GalleryBoss : MonoBehaviour, ISimTickable
    {
        public enum Phase { Idle, Diagnosis, Terms, Duel, Persona, Unmasking, Mirror, Aftermath, Won, Lost }

        public const float DiagnosisLength = 12f, TermsLength = 8f, TellAt = 20f, SignalAt = 25f, PersonaAt = 0.6f;
        public const float UnmaskLength = 10f, AftermathLength = 6f, OathRadius = 5f;
        public int TickOrder => TickOrders.Director + 5;

        public Phase Current { get; private set; } = Phase.Idle;
        public float PhaseTime { get; private set; }
        public float DuelTime { get; private set; }
        public bool Signalled { get; private set; }
        public EnemyAgent Ashgrave { get; private set; }
        public EnemyAgent Mirror { get; private set; }
        public MirrorBrain Brain { get; private set; }
        public DuetFinisher Duet { get; private set; }
        public readonly List<EnemyAgent> Archers = new List<EnemyAgent>();
        public OathGlyph Glyph { get; private set; }
        public string LossCause { get; private set; }
        /// <summary>The phase he (or she) fell in.</summary>
        public Phase LossPhase { get; private set; }
        public bool SidekickDied { get; private set; }
        public event Action<Phase> PhaseChanged;

        RunContext _ctx;
        HeroAgent _hero;
        CallumModule _cm;
        SidekickAgent _sk;
        RoomModule _arena;
        Vector3[] _niches;
        bool _told;
        readonly List<(float t, string who)> _recentHits = new List<(float, string)>();
        List<string> _opening;

        void OnEnable() => SimLoop.Register(this);

        void OnDisable()
        {
            SimLoop.Unregister(this);
            if (_ctx != null) _ctx.Events.Damage -= OnDamage;
            Brain?.Dispose();
            Duet?.Dispose();
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        /// <summary>Stage the arena: marks, the grey figure, the hidden gallery, the glyph, the niches. Then the diagnosis.</summary>
        public void Begin(RoomModule arena, HeroAgent hero, SidekickAgent sidekick)
        {
            _ctx = RunContext.Current;
            _arena = arena;
            _hero = hero;
            _cm = hero.Module as CallumModule;
            _sk = sidekick;
            var root = arena.transform;
            var ashMark = Find(root, "AshgraveMark");
            var circle = Find(root, "OathCircle");
            hero.Motor.Teleport(root.position + new Vector3(0f, 0.05f, 1.2f));
            hero.Motor.FaceInstant(Vector3.forward);
            if (sidekick != null) sidekick.Motor.Teleport(root.position + new Vector3(-1.8f, 0.05f, 0.4f));
            hero.Route.SetNodes(arena.Route.Nodes());
            var ag = CastFactory.Spawn("ashgrave", ashMark.position + Vector3.up * 0.05f, ashMark.rotation, transform);
            Ashgrave = ag.GetComponent<EnemyAgent>();
            Ashgrave.name = "Lord Ashgrave";
            Ashgrave.AgentId = "ashgrave";
            Ashgrave.Scripted = true;
            HoldAshgrave(true);
            HS.UI.BarkView.RegisterSpeaker("ashgrave", Ashgrave.transform);
            // The Curator, before the persona: Ashgrave's body gone grey.
            CastFactory.Glitch(ag, 1f, 0f, Color.black);
            foreach (var m in root.GetComponentsInChildren<SpawnMarker>(true))
            {
                if (m.Archetype != "archer") continue;
                var go = CastFactory.Spawn("gallery_archer", m.transform.position + Vector3.up * 0.05f, m.transform.rotation, transform);
                if (go == null) continue;
                var a = go.GetComponent<EnemyAgent>();
                a.name = "GalleryArcher_" + Archers.Count;
                a.AgentId = a.name;
                a.StartsHidden = true;
                a.Elevated = true;
                a.Scripted = true;
                Archers.Add(a);
            }
            Glyph = OathGlyph.Create(circle, OathRadius);
            _cm.OathCenter = circle.position;
            _cm.OathRadius = OathRadius;
            _niches = Niches(root, circle.position);
            _ctx.Events.Damage += OnDamage;
            SetPhase(Phase.Diagnosis);
        }

        /// <summary>The two niches (the gallery arena's marks; in the slice's arena, placed now with a prop over each).</summary>
        Vector3[] Niches(Transform root, Vector3 centre)
        {
            var e = Find(root, "MirrorNiche_E");
            var w = Find(root, "MirrorNiche_W");
            if (e != null && w != null) return new[] { e.position, w.position };
            var list = new List<Vector3>();
            foreach (float sx in new[] { 1f, -1f })
            {
                var at = centre + new Vector3(sx * 10.6f, 0f, 0f);
                list.Add(at);
                PlaceArmable(at + new Vector3(sx * 0.9f, 0f, 2.2f), new Vector3(-sx * 0.9f, 0f, -2.2f));
            }
            return list.ToArray();
        }

        void PlaceArmable(Vector3 at, Vector3 impactOffset)
        {
            var go = new GameObject("Armable_Niche");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var prefab = GameAssets.Load()?.DemoProp("barrel_stack");
            if (prefab != null)
            {
                var v = Instantiate(prefab, go.transform);
                v.name = "Visual";
                v.transform.localPosition = Vector3.zero;
            }
            var anchor = go.AddComponent<ArmableAnchor>();
            anchor.Kind = ArmableKind.BarrelStack;
            anchor.ImpactOffset = impactOffset;
            anchor.ImpactRadius = 2.2f;
            go.AddComponent<ArmableProp>();
            go.SetActive(true);
        }

        void SetPhase(Phase p)
        {
            Current = p;
            PhaseTime = 0f;
            Debug.Log($"[Gallery] phase {p} stage={(_hero != null ? _hero.Stage.ToString() : "-")}");
            PhaseChanged?.Invoke(p);
        }

        void Say(string who, string text, float dur = 3.2f) => _ctx?.Events.RaiseBark(who, text, dur, 3);

        /// <summary>Lines on a schedule within the current phase: each fires once, at its time.</summary>
        bool At(float t)
        {
            if (PhaseTime < t || _stepTimes.Contains(t)) return false;
            _stepTimes.Add(t);
            return true;
        }

        readonly HashSet<float> _stepTimes = new HashSet<float>();

        public void SimTick(float dt)
        {
            if (Current == Phase.Idle || Current == Phase.Won || Current == Phase.Lost) return;
            PhaseTime += dt;
            if (CheckLoss()) return;
            switch (Current)
            {
                case Phase.Diagnosis: TickDiagnosis(); break;
                case Phase.Terms: TickTerms(); break;
                case Phase.Duel: TickDuel(dt); break;
                case Phase.Persona: TickPersona(); break;
                case Phase.Unmasking: TickUnmasking(); break;
                case Phase.Mirror: TickMirror(dt); break;
                case Phase.Aftermath: TickAftermath(); break;
            }
        }

        void Next(Phase p)
        {
            _stepTimes.Clear();
            SetPhase(p);
        }

        // ------------------------------------------------------------------ Phase 0: the diagnosis

        /// <summary>Nobody fights before the terms are sworn: the scripted lines are not an opening.</summary>
        void HoldAshgrave(bool on)
        {
            if (Ashgrave == null) return;
            if (Ashgrave.Health != null) Ashgrave.Health.Invulnerable = on;
            Ashgrave.Brain = on ? StandStill.Instance : null;
        }

        /// <summary>He stands on his mark and speaks; a knife in the meantime starts nothing.</summary>
        sealed class StandStill : IEnemyController
        {
            public static readonly StandStill Instance = new StandStill();

            public bool Tick(EnemyAgent self, float dt)
            {
                self.Motor.Move(Vector3.zero, 40f, dt);
                self.Presenter?.SetLocomotion(0f, false);
                return true;
            }
        }

        void TickDiagnosis()
        {
            HoldAshgrave(true);
            var lines = _opening ?? (_opening = CuratorDiagnosis.Opening(_hero.Stage));
            float[] at = { 0.4f, 3.3f, 6.2f, 9.0f };
            for (int i = 0; i < lines.Count && i < at.Length; i++)
                if (At(at[i]))
                {
                    Say("ashgrave", lines[i], 2.8f);
                    Vfx.Burst(VfxKind.Glint, Ashgrave.Position + Vector3.up * 1.35f, 0.7f); // the dull pendant
                }
            // He dissolves into the persona: the grey burns off.
            if (PhaseTime >= 10.6f)
            {
                float k = Mathf.Clamp01((PhaseTime - 10.6f) / 1.4f);
                CastFactory.Glitch(Ashgrave.gameObject, 1f - k, 0.55f * Mathf.Sin(k * Mathf.PI), Color.black);
            }
            if (PhaseTime < DiagnosisLength) return;
            CastFactory.Glitch(Ashgrave.gameObject, 0f, 0f, Color.black);
            Next(Phase.Terms);
        }

        // ------------------------------------------------------------------ Phase 1: the flaw trap

        void TickTerms()
        {
            HoldAshgrave(true);
            var stage = _hero.Stage;
            if (At(0.3f)) Say("ashgrave", "Sir Callum. Single combat, by the old terms: no aid, no interference.");
            if (At(3.2f))
                Say("callum", stage >= Stage.S3 ? "No. A rigged duel binds no one."
                    : stage == Stage.S2 ? "Agreed. ...I'll see only what I must."
                    : stage == Stage.S1 ? "Agreed. ...Stay near the edge, though. Just in case."
                    : "Agreed. Sidekick — out of the circle. These are the terms.");
            if (At(6.0f)) Say("ashgrave", stage >= Stage.S3 ? "Refuse, then. The gallery will judge regardless." : "Then salute, and let the gallery judge.");
            if (PhaseTime < TermsLength) return;
            bool sworn = stage < Stage.S3;
            _cm.NoAidTerms = sworn;
            Glyph.Sworn = sworn;
            HoldAshgrave(false);
            Ashgrave.Activate();
            DuelTime = 0f;
            Next(Phase.Duel);
        }

        void TickDuel(float dt)
        {
            DuelTime += dt;
            if (!_told && DuelTime >= TellAt && !Signalled)
            {
                _told = true;
                Say("ashgrave", "Do you hear the gallery, Sir Callum? They came to see a knight die properly.", 3.6f);
            }
            bool hurt = Ashgrave.Health.Fraction <= PersonaAt;
            if (!Signalled && (DuelTime >= SignalAt || hurt)) Signal();
            if (Signalled && hurt) BeginPersona();
            else if (!Ashgrave.IsAlive) BeginPersona();
        }

        void Signal()
        {
            Signalled = true;
            Ashgrave.Presenter?.PlayAction("cheer", 1.0f);
            Say("ashgrave", "Now!", 1.8f);
            HS.Audio.AudioDirector.Instance?.Play("syn_horn", Ashgrave.Position, 1f, 1f, 0f);
            foreach (var a in Archers)
                if (a != null && a.IsAlive && a.IsHidden) a.Emerge();
        }

        // ------------------------------------------------------------------ Phase 2: the persona

        void BeginPersona()
        {
            if (Ashgrave.IsAlive) Ashgrave.SwapStats("ashgrave_unmasked");
            Say("ashgrave", "Enough theatre. Gallery — everyone!", 2.6f);
            foreach (var a in Archers)
                if (a != null && a.IsAlive)
                {
                    if (a.IsHidden) a.Emerge();
                    a.ReloadMul = 0.75f; // called to loose faster
                }
            // The pretence is gone: S1+ treats the terms as broken; S0 keeps his word to a man who never kept his.
            if (_hero.Stage >= Stage.S1)
            {
                _cm.NoAidTerms = false;
                Glyph.Sworn = false;
                Say("callum", "So much for the terms.", 2.4f);
            }
            else Say("callum", "I gave my word. Even if he did not.", 2.6f);
            Next(Phase.Persona);
        }

        void TickPersona()
        {
            if (Ashgrave.IsAlive) return;
            BeginUnmasking();
        }

        // ------------------------------------------------------------------ Phase 3: the Mirror

        void BeginUnmasking()
        {
            _cm.NoAidTerms = false;
            Glyph.Sworn = false;
            if (_cm.Challenged != null) _cm.EndDuel(DuelEndReason.TargetDied);
            foreach (var a in Archers)
                if (a != null && a.IsAlive) a.gameObject.SetActive(false); // the gallery melts away
            ProjectileSystem.Instance?.ClearAll();
            Next(Phase.Unmasking);
        }

        void TickUnmasking()
        {
            // The persona burns away.
            if (Ashgrave != null && Ashgrave.gameObject.activeSelf)
            {
                float k = Mathf.Clamp01(PhaseTime / 2.2f);
                CastFactory.Glitch(Ashgrave.gameObject, k, k, new Color(0.25f, 0.05f, 0.05f) * k);
                if (k >= 1f) Ashgrave.gameObject.SetActive(false);
            }
            if (At(1.0f)) Say("ashgrave", CuratorDiagnosis.Unmasking[0], 3.6f);
            if (At(4.0f)) FormMirror();
            if (At(6.4f)) Say("mirror", CuratorDiagnosis.Unmasking[1], 3.2f);
            if (PhaseTime >= 4.0f && Mirror != null)
            {
                // It flickers into being.
                float k = Mathf.Clamp01((PhaseTime - 4f) / 3f);
                CastFactory.Glitch(Mirror.gameObject, 0.85f, 0.45f * (1f - k) + 0.12f, new Color(0.35f, 0.05f, 0.08f) * (0.4f + 0.6f * Mathf.Abs(Mathf.Sin(PhaseTime * 9f))));
            }
            if (PhaseTime < UnmaskLength || Mirror == null) return;
            Mirror.Scripted = false;
            Mirror.Activate();
            Brain = new MirrorBrain(Mirror, _hero, _niches);
            Duet = new DuetFinisher(Brain, _hero, _sk);
            Brain.HabitBreak += kind => _ctx?.Events.RaiseThought(BreakThought(kind));
            HS.Audio.AudioDirector.Instance?.OnFlow("Mirror");
            Next(Phase.Mirror);
        }

        static string BreakThought(string kind) =>
            kind == "etiquette" ? "It has to salute back. Now!" : kind == "niche" ? "Got it — it's reeling!" : "It didn't like that. Not one bit.";

        void FormMirror()
        {
            var mark = Find(_arena.transform, "AshgraveMark");
            var at = mark != null ? mark.position : _arena.transform.position + new Vector3(0f, 0f, 18f);
            var rot = mark != null ? mark.rotation : Quaternion.Euler(0f, 180f, 0f);
            Mirror = CastFactory.BuildMirror(_hero.gameObject, at + Vector3.up * 0.05f, rot);
            Mirror.Scripted = true;
            CastFactory.Activate(Mirror.gameObject, transform);
            HS.UI.BarkView.RegisterSpeaker("mirror", Mirror.transform);
            var anchor = new GameObject("RuleIconAnchor");
            anchor.transform.SetParent(Mirror.transform, false);
            anchor.transform.localPosition = new Vector3(0f, 2.55f, 0f);
            var icons = anchor.AddComponent<RuleIconDisplay>();
            icons.Tint = new Color(0.75f, 0.78f, 0.82f, 1f);
            icons.IconSource = () => Brain != null && Mirror != null && Mirror.IsAlive ? Brain.Icon : null;
            Vfx.Burst(VfxKind.Glint, at + Vector3.up * 1.35f, 1.6f); // the pendant, on the copy
            Vfx.Burst(VfxKind.Dust, at, 1.2f);
        }

        void TickMirror(float dt)
        {
            Duet.Tick(dt);
            if (Mirror != null && !Mirror.IsAlive) BeginAftermath();
        }

        // ------------------------------------------------------------------ aftermath

        void BeginAftermath()
        {
            if (_cm.Challenged != null) _cm.EndDuel(DuelEndReason.TargetDied);
            Brain?.Dispose();
            Duet?.Dispose();
            Next(Phase.Aftermath);
        }

        void TickAftermath()
        {
            var at = Mirror != null ? Mirror.Position : _hero.Position;
            if (At(0.4f))
            {
                // The pendant glows for the first time, recording two figures.
                Vfx.Burst(VfxKind.Glint, at + Vector3.up * 0.4f, 2.4f);
                HS.Audio.AudioDirector.Instance?.Play("ui_confirm", at, 0.9f, 0.05f, 0f);
                if (Mirror != null) Mirror.gameObject.SetActive(false);
            }
            if (At(1.6f)) Say("callum", CuratorDiagnosis.WeLine(_hero.Stage), 4f);
            if (At(4.2f)) Say("system", "CLASS: CALLUM's SIDEKICK. STATUS: LISTED.", 4f);
            if (PhaseTime >= AftermathLength) Next(Phase.Won);
        }

        // ------------------------------------------------------------------ loss

        bool CheckLoss()
        {
            if (Current == Phase.Aftermath) return false;
            if (!_hero.IsAlive)
            {
                LossPhase = Current;
                LossCause = CauseOfDeath();
                Next(Phase.Lost);
                return true;
            }
            if (_sk != null && !_sk.IsAlive)
            {
                LossPhase = Current;
                SidekickDied = true;
                LossCause = "sidekick";
                Next(Phase.Lost);
                return true;
            }
            return false;
        }

        void OnDamage(DamageInfo d, float applied)
        {
            if (d.Target != _hero) return;
            string who = d.Tag == "arrow" ? "arrows"
                : d.Tag == "feint" ? "feint"
                : d.Tag == "mirror_riposte" ? "duet_missed"
                : d.Source != null && d.Source == Mirror ? "mirror"
                : d.Source != null && d.Source == Ashgrave ? "ashgrave"
                : "other";
            _recentHits.Add((_ctx.SimTime, who));
            if (_recentHits.Count > 24) _recentHits.RemoveAt(0);
        }

        /// <summary>What finished him, weighed over his last few seconds (attributable losses, GDD §11.3).</summary>
        string CauseOfDeath()
        {
            float now = _ctx.SimTime;
            var counts = new Dictionary<string, int>();
            string last = "other";
            foreach (var (t, who) in _recentHits)
            {
                if (now - t > 6f) continue;
                counts.TryGetValue(who, out var n);
                counts[who] = n + 1;
                last = who;
            }
            if (Current == Phase.Mirror || Current == Phase.Unmasking)
            {
                if (Duet != null && Duet.Refusals > 0) return "terms";
                if (last == "feint" || last == "duet_missed") return last;
                return "mirror";
            }
            counts.TryGetValue("arrows", out var arrows);
            counts.TryGetValue("ashgrave", out var blade);
            if (arrows > 0 && arrows >= blade) return "arrows";
            return blade > 0 ? "ashgrave" : "other";
        }
    }
}
