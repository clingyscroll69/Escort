using System;
using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Hero.Callum;
using HS.Rooms;
using HS.Sidekick;
using UnityEngine;

namespace HS.Boss
{
    /// <summary>
    /// Lord Ashgrave, the Rigged Duel (GDD §6.1; slice: 3 archers, fixed signal). Terms ("no aid") → the formal duel → at
    /// T+25 s the hidden gallery stands up and looses volleys at Callum. S0: he orders you out of the circle and fights on
    /// through the arrows. S1: after the first volley he raises his guard against them. You can sneak up the gallery and
    /// silence archers unseen (Quiet Feet helps) — every archer is an Unseen-assist Moment. Win: Ashgrave falls. Loss:
    /// Callum (or you) fall → Curator diagnosis → Post-Mortem → Restore.
    /// </summary>
    public sealed class RiggedDuelDirector : MonoBehaviour, ISimTickable
    {
        public enum Phase { Idle, Terms, Duel, Won, Lost }

        public const float TermsLength = 8f, TellAt = 20f, SignalAt = 25f, OathRadius = 5f;
        public int TickOrder => TickOrders.Director + 5;

        public Phase Current { get; private set; } = Phase.Idle;
        public float DuelTime { get; private set; }
        public bool Signalled { get; private set; }
        public EnemyAgent Ashgrave { get; private set; }
        public readonly List<EnemyAgent> Archers = new List<EnemyAgent>();
        public OathGlyph Glyph { get; private set; }
        public string LossCause { get; private set; }
        public bool SidekickDied { get; private set; }
        public event Action<Phase> PhaseChanged;

        RunContext _ctx;
        HeroAgent _hero;
        CallumModule _cm;
        SidekickAgent _sk;
        RoomModule _arena;
        float _termsT;
        int _termsStep;
        bool _told;
        readonly List<(float t, string who)> _recentHits = new List<(float, string)>();

        void OnEnable() => SimLoop.Register(this);
        void OnDisable()
        {
            SimLoop.Unregister(this);
            if (_ctx != null) _ctx.Events.Damage -= OnDamage;
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        /// <summary>Stage the arena: marks, Ashgrave (waiting), the hidden gallery, the glyph. Then the terms begin.</summary>
        public void Begin(RoomModule arena, HeroAgent hero, SidekickAgent sidekick, Func<string, GameObject> enemyPrefab)
        {
            _ctx = RunContext.Current;
            _arena = arena;
            _hero = hero;
            _cm = hero.Module as CallumModule;
            _sk = sidekick;
            var root = arena.transform;
            var heroMark = Find(root, "CallumMark");
            var ashMark = Find(root, "AshgraveMark");
            var skMark = Find(root, "SidekickOut");
            var circle = Find(root, "OathCircle");
            hero.Motor.Teleport(root.position + new Vector3(0f, 0.05f, 1.2f));
            hero.Motor.FaceInstant(Vector3.forward);
            if (sidekick != null) sidekick.Motor.Teleport(root.position + new Vector3(-1.8f, 0.05f, 0.4f));
            hero.Route.SetNodes(arena.Route.Nodes());
            var ap = enemyPrefab("ashgrave");
            Ashgrave = Instantiate(ap, ashMark.position + Vector3.up * 0.05f, ashMark.rotation, transform).GetComponent<EnemyAgent>();
            Ashgrave.name = "Lord Ashgrave";
            Ashgrave.AgentId = "ashgrave";
            Ashgrave.Archetype = "ashgrave";
            Ashgrave.Scripted = true;
            HS.UI.BarkView.RegisterSpeaker("ashgrave", Ashgrave.transform);
            foreach (var m in root.GetComponentsInChildren<SpawnMarker>(true))
            {
                if (m.Archetype != "archer") continue;
                var a = Instantiate(enemyPrefab("archer"), m.transform.position + Vector3.up * 0.05f, m.transform.rotation, transform).GetComponent<EnemyAgent>();
                a.name = "GalleryArcher_" + Archers.Count;
                a.AgentId = a.name;
                a.Archetype = "archer";
                a.StartsHidden = true;
                a.Elevated = true;
                a.Scripted = true;
                Archers.Add(a);
            }
            Glyph = OathGlyph.Create(circle, OathRadius);
            _cm.OathCenter = circle.position;
            _cm.OathRadius = OathRadius;
            _ctx.Events.Damage += OnDamage;
            SetPhase(Phase.Terms);
        }

        void SetPhase(Phase p)
        {
            Current = p;
            PhaseChanged?.Invoke(p);
        }

        void Say(string who, string text, float dur = 3.2f) => _ctx?.Events.RaiseBark(who, text, dur, 3);

        public void SimTick(float dt)
        {
            switch (Current)
            {
                case Phase.Terms: TickTerms(dt); break;
                case Phase.Duel: TickDuel(dt); break;
            }
        }

        void TickTerms(float dt)
        {
            _termsT += dt;
            bool s1 = _hero.Stage >= Stage.S1;
            if (_termsStep == 0 && _termsT >= 0.3f)
            {
                _termsStep++;
                Say("ashgrave", "Sir Callum. Single combat, by the old terms: no aid, no interference.");
            }
            else if (_termsStep == 1 && _termsT >= 3.2f)
            {
                _termsStep++;
                Say("callum", s1 ? "Agreed. ...Stay near the edge, though. Just in case." : "Agreed. Sidekick — out of the circle. These are the terms.");
            }
            else if (_termsStep == 2 && _termsT >= 6.0f)
            {
                _termsStep++;
                Say("ashgrave", "Then salute, and let the gallery judge.");
            }
            if (_termsT >= TermsLength)
            {
                _cm.NoAidTerms = true;
                Glyph.Sworn = true;
                Ashgrave.Activate();
                DuelTime = 0f;
                SetPhase(Phase.Duel);
            }
        }

        void TickDuel(float dt)
        {
            DuelTime += dt;
            if (!_told && DuelTime >= TellAt)
            {
                _told = true;
                Say("ashgrave", "Do you hear the gallery, Sir Callum? They came to see a knight die properly.", 3.6f);
            }
            if (!Signalled && DuelTime >= SignalAt && Ashgrave.IsAlive)
            {
                Signalled = true;
                Ashgrave.Presenter?.PlayAction("cheer", 1.0f);
                Say("ashgrave", "Now!", 1.8f);
                HS.Audio.AudioDirector.Instance?.Play("syn_horn", Ashgrave.Position, 1f, 1f, 0f);
                foreach (var a in Archers)
                    if (a != null && a.IsAlive && a.IsHidden) a.Emerge();
            }
            if (!Ashgrave.IsAlive)
            {
                _cm.NoAidTerms = false;
                Glyph.Sworn = false;
                foreach (var a in Archers)
                    if (a != null && a.IsAlive) a.gameObject.SetActive(false); // the gallery melts away
                SetPhase(Phase.Won);
                return;
            }
            if (!_hero.IsAlive)
            {
                LossCause = CauseOfDeath();
                SetPhase(Phase.Lost);
                return;
            }
            if (_sk != null && !_sk.IsAlive)
            {
                SidekickDied = true;
                LossCause = "sidekick";
                SetPhase(Phase.Lost);
            }
        }

        void OnDamage(DamageInfo d, float applied)
        {
            if (d.Target != _hero || d.Source == null) return;
            string who = d.Tag == "arrow" ? "arrows" : d.Source == Ashgrave ? "ashgrave" : d.Source is SidekickAgent ? "sidekick" : "other";
            _recentHits.Add((_ctx.SimTime, who));
            if (_recentHits.Count > 24) _recentHits.RemoveAt(0);
        }

        /// <summary>What finished him, weighed over his last few seconds (attributable losses, GDD §11.3).</summary>
        string CauseOfDeath()
        {
            float now = _ctx.SimTime;
            int arrows = 0, blade = 0;
            foreach (var (t, who) in _recentHits)
            {
                if (now - t > 6f) continue;
                if (who == "arrows") arrows++;
                else if (who == "ashgrave") blade++;
            }
            if (arrows > 0 && arrows >= blade) return "arrows";
            return blade > 0 ? "ashgrave" : "other";
        }
    }
}
