using System;
using System.Collections.Generic;
using HS.Boss;
using HS.Core;
using HS.Enemies;
using HS.Flow;
using HS.Hero;
using HS.Hero.Callum;
using HS.Rooms;
using HS.Sidekick;
using HS.Skills;
using HS.UI;
using UnityEngine;

namespace HS.Tutorial
{
    /// <summary>
    /// When each lesson's situation first happens (spec §4 "fires when"), offered to the <see cref="TutorialDirector"/>.
    /// Event subscriptions for moments (a challenge, being caught, a wound, XP) and a per-frame poll for states (his
    /// current rule, a stone or trap in range, an off-screen shooter). Read-only: it never touches the simulation.
    /// </summary>
    public sealed class LessonTriggers
    {
        readonly TutorialDirector _d;
        readonly GameFlow _flow;
        RunContext _ctx;
        CallumModule _cm;
        XpTracker _xp;
        ThreatIndicators _threats;
        HazardMarker[] _hazards = new HazardMarker[0];
        ExploreAnchor[] _caches = new ExploreAnchor[0];
        RiggedDuelDirector _duel;
        float _chapterT, _scanT;
        bool _welcomed;
        int _knife, _skills, _dodges, _pings, _salutes, _insightToggles;
        bool _lastInsight;

        public LessonTriggers(TutorialDirector d, GameFlow flow)
        {
            _d = d;
            _flow = flow;
            Bind(RunContext.Current);
            // While "what he sees" is explained, his cone on the ground is what's lit.
            _d.Shown += l => SetConeSpotlight(l.Id == "cone");
            _d.Closed += l =>
            {
                if (l.Id == "cone") SetConeSpotlight(false);
            };
        }

        void SetConeSpotlight(bool on)
        {
            var view = Hero != null ? Hero.GetComponent<HS.Presentation.WitnessConeView>() : null;
            if (view != null) view.Spotlight = on;
        }

        HeroAgent Hero => _ctx != null ? _ctx.Hero as HeroAgent : null;
        SidekickAgent Sk => _ctx != null ? _ctx.Sidekick as SidekickAgent : null;
        HudView Hud => _flow != null && _flow.Chapter != null ? _flow.Chapter.Hud : null;

        void Bind(RunContext ctx)
        {
            _ctx = ctx;
            if (_ctx == null) return;
            var ev = _ctx.Events;
            ev.SkillUsed += OnSkillUsed;
            ev.Damage += OnDamage;
            ev.DuelStarted += OnDuelStarted;
            ev.SaluteFinished += OnSalute;
            ev.WoundChanged += OnWound;
            ev.Ping += OnPing;
        }

        public void Unbind()
        {
            if (_ctx != null)
            {
                var ev = _ctx.Events;
                ev.SkillUsed -= OnSkillUsed;
                ev.Damage -= OnDamage;
                ev.DuelStarted -= OnDuelStarted;
                ev.SaluteFinished -= OnSalute;
                ev.WoundChanged -= OnWound;
                ev.Ping -= OnPing;
            }
            if (_cm != null)
            {
                _cm.Caught -= OnCaught;
                _cm.SpoiledDuel -= OnSpoiled;
            }
            if (_xp != null) _xp.Awarded -= OnXp;
            _ctx = null;
            _cm = null;
            _xp = null;
        }

        // ------------------------------------------------------------------------------------------------- events
        void OnSkillUsed(string id, Agent user)
        {
            if (!(user is SidekickAgent)) return;
            if (id == "dodge") _dodges++;
            else _skills++;
        }

        void OnDamage(DamageInfo d, float applied)
        {
            if (d.Source is SidekickAgent && d.Tag == "knife") _knife++;
        }

        void OnDuelStarted(Agent hero, Agent target)
        {
            if (hero != Hero) return;
            // Some roads put a bandit in his way before the first doorway: meet his rules first, then his eyes.
            if (!Known("hero_rules")) _d.Offer("hero_rules", spotlight: HeroSpot);
            _d.Offer("cone", spotlight: ConeSpot);
        }

        void OnSalute(Agent hero) => _salutes++;

        void OnWound(Agent who, string type, bool added)
        {
            if (added && who == Hero) _d.Offer("wounds");
        }

        void OnPing(PingInfo p) => _pings++;
        void OnCaught(SabotageEvent e, bool byStone) => _d.Offer("caught");
        void OnSpoiled(EnemyAgent target) => _d.Offer("spoiled");
        void OnXp(int amount, string reason) => _d.Offer("xp");

        /// <summary>Completion for chained lessons (offered by the director when their freeze-frame closes).</summary>
        public Func<bool> DoneFor(string id)
        {
            switch (id)
            {
                case "insight":
                    int toggles = _insightToggles;
                    return () => _insightToggles > toggles;
                case "salute":
                    int salutes = _salutes;
                    return () => _salutes > salutes;
            }
            return null;
        }

        // --------------------------------------------------------------------------------------------------- poll
        public void Tick(float dt)
        {
            if (_ctx == null || _ctx != RunContext.Current) return;
            var hero = Hero;
            var sk = Sk;
            if (hero == null || sk == null) return;
            LateBind(hero);
            var hud = Hud;
            if (hud != null && hud.InsightOn != _lastInsight)
            {
                _lastInsight = hud.InsightOn;
                _insightToggles++;
            }
            var state = _flow != null ? _flow.Current : GameFlow.State.Chapter;
            bool road = state == GameFlow.State.Chapter, duel = state == GameFlow.State.Duel;
            bool live = SimLoop.Instance != null && !SimLoop.Instance.Paused;
            if (state == GameFlow.State.Camp) TickCamp();
            if (duel) TickDuel(hero);
            if (!road && !duel) return;
            if (road && !_welcomed)
            {
                _welcomed = true;
                _d.Offer("welcome");
            }
            if (road && live) _chapterT += dt;

            // ---- controls
            if (road && _chapterT >= 1.5f && !Known("move"))
            {
                var from = sk.Position;
                _d.Offer("move", () => Geo.FlatDistance(from, Sk != null ? Sk.Position : from) >= 4f);
            }
            bool settled = TutorialProgress.IsSeen("move"); // the first thing anyone needs; the road's sights can wait
            if (road && settled && _chapterT >= 75f) _d.Offer("pause");
            bool fightNearYou = AnyActiveEnemyNear(sk.Position, 16f);
            if (road && fightNearYou && !Known("attack"))
            {
                int k = _knife;
                _d.Offer("attack", () => _knife > k);
            }
            if (road && fightNearYou && HasActiveSkill(sk) && !Known("tricks"))
            {
                int s = _skills;
                _d.Offer("tricks", () => _skills > s);
            }
            if ((EnemyTargets(sk) || sk.TimeSinceHurt < 0.4f) && !Known("dodge"))
            {
                int dd = _dodges;
                _d.Offer("dodge", () => _dodges > dd);
            }
            if (sk.IsChanneling) _d.Offer("channel");
            if (ArmableProp.ArmedCount() > 0 && !Known("ping"))
            {
                int p = _pings;
                _d.Offer("ping", () => _pings > p);
            }

            // ---- the hero
            string rule = hero.ActiveRuleId;
            if (road && rule == "threshold_pause")
            {
                _d.Offer("hero_rules", spotlight: HeroSpot);
                if (Owns(sk, "quiet_feet") && Known("hero_rules") && !Known("crouch")) _d.Offer("crouch", () => Sk != null && Sk.Crouched);
            }
            if (rule == "callum_wait_unready" && _cm != null && _cm.Challenged != null && _cm.Challenged.State != EnemyState.Surrendered) _d.Offer("unready");
            if (rule == "callum_fallback") _d.Offer("fallback");
            if (_cm != null && _cm.HonorLow) _d.Offer("honor_low");
            if (!sk.InSupportRange && sk.IsAlive && (AnyActiveEnemyNear(hero.Position, 20f) || (_cm != null && _cm.Challenged != null)) && !Known("out_of_reach"))
                _d.Offer("out_of_reach", () => Sk != null && Sk.InSupportRange);

            // ---- the road (scans are cheap but needn't run every frame)
            _scanT -= dt;
            if (_scanT > 0f) return;
            _scanT = 0.25f;
            if (_threats == null) _threats = UnityEngine.Object.FindAnyObjectByType<ThreatIndicators>();
            if (_threats != null && _threats.AnyThreatShown) _d.Offer("threats");
            if (_threats != null && _threats.HeroMarkerShown) _d.Offer("hero_offscreen");
            ScanEnemies(hero);
            if (road && settled) ScanRoad(sk, hero);
        }

        void LateBind(HeroAgent hero)
        {
            if (_cm == null && hero.Module is CallumModule cm)
            {
                _cm = cm;
                _cm.Caught += OnCaught;
                _cm.SpoiledDuel += OnSpoiled;
            }
            if (_xp == null && (_xp = _ctx.Get<XpTracker>()) != null) _xp.Awarded += OnXp;
        }

        void TickCamp()
        {
            var fade = ScreenFade.Instance;
            if (_flow.Camp != null && (fade == null || !fade.Busy)) _d.Offer("camp");
        }

        void TickDuel(HeroAgent hero)
        {
            if (_flow.Duel != null && _duel != _flow.Duel) _duel = _flow.Duel;
            var fade = ScreenFade.Instance;
            if (_duel != null && _duel.Current == RiggedDuelDirector.Phase.Terms && (fade == null || !fade.Busy))
                _d.Offer("duel", spotlight: DuelSpot);
        }

        void ScanEnemies(HeroAgent hero)
        {
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is EnemyAgent e) || !e.IsAlive) continue;
                if (e.State == EnemyState.Surrendered && Geo.FlatDistance(e.Position, hero.Position) <= 15f) _d.Offer("surrender");
                if (e.RevealedBy == "ambush" && !e.IsHidden) _d.Offer("ambush");
            }
        }

        void ScanRoad(SidekickAgent sk, HeroAgent hero)
        {
            if (_hazards.Length == 0) _hazards = UnityEngine.Object.FindObjectsByType<HazardMarker>(FindObjectsSortMode.None);
            if (_caches.Length == 0) _caches = UnityEngine.Object.FindObjectsByType<ExploreAnchor>(FindObjectsSortMode.None);
            foreach (var s in ChronicleStone.All)
            {
                if (s == null || s.State == ChronicleStone.StoneState.Broken || Geo.FlatDistance(s.Position, sk.Position) > 18f) continue;
                var stone = s;
                _d.Offer("stone", marker: () => stone != null && stone.State != ChronicleStone.StoneState.Broken ? stone.Position + Vector3.up * 2.2f : (Vector3?)null);
                break;
            }
            foreach (var h in _hazards)
            {
                if (h == null || !h.isActiveAndEnabled || h.State != HazardMarker.HazardState.Armed) continue;
                if (Geo.FlatDistance(h.transform.position, sk.Position) > 12f && Geo.FlatDistance(h.transform.position, hero.Position) > 10f) continue;
                var trap = h;
                _d.Offer("trap", () => trap == null || trap.State == HazardMarker.HazardState.Disarmed,
                    () => trap != null && trap.State == HazardMarker.HazardState.Armed ? trap.transform.position + Vector3.up * 0.6f : (Vector3?)null);
                break;
            }
            foreach (var c in _caches)
            {
                if (c == null || !c.isActiveAndEnabled || c.Searched || Geo.FlatDistance(c.transform.position, sk.Position) > 12f) continue;
                var cache = c;
                _d.Offer("cache", () => cache == null || cache.Searched,
                    () => cache != null && !cache.Searched ? cache.transform.position + Vector3.up * 1.2f : (Vector3?)null);
                break;
            }
            if (Owns(sk, "loosen_bolt"))
                foreach (var p in ArmableProp.Instances)
                {
                    if (p == null || p.State != ArmableProp.PropState.Idle || Geo.FlatDistance(p.InteractPosition, sk.Position) > 10f) continue;
                    var prop = p;
                    _d.Offer("prop", () => prop == null || prop.State != ArmableProp.PropState.Idle,
                        () => prop != null && prop.State == ArmableProp.PropState.Idle ? prop.InteractPosition + Vector3.up * 1.6f : (Vector3?)null);
                    break;
                }
        }

        // ---------------------------------------------------------------------------------------------- helpers
        bool Known(string id) => TutorialProgress.IsSeen(id) || _d.IsQueued(id) || _d.Showing == id;

        static bool AnyActiveEnemyNear(Vector3 at, float r)
        {
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] is EnemyAgent e && e.IsAlive && e.IsActive && Geo.FlatDistance(e.Position, at) <= r) return true;
            return false;
        }

        static bool EnemyTargets(SidekickAgent sk)
        {
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] is EnemyAgent e && e.IsAlive && e.IsActive && e.Target == sk) return true;
            return false;
        }

        static bool HasActiveSkill(SidekickAgent sk)
        {
            var s = sk.GetComponent<SidekickSkills>();
            return s != null && s.System.Loadout.Count > 0;
        }

        static bool Owns(SidekickAgent sk, string id)
        {
            var s = sk.GetComponent<SidekickSkills>();
            return s != null && s.System.Has(id);
        }

        // ------------------------------------------------------------------------------------------- spotlights
        /// <summary>Screen rect (Overlay-layer coordinates) around world points, padded.</summary>
        static Rect? ScreenRect(float pad, params Vector3[] world)
        {
            var root = UIRoot.Instance;
            if (root == null) return null;
            bool any = false;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (var w in world)
            {
                if (!root.WorldToLayer(w, root.Overlay, out var p)) continue;
                any = true;
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            if (!any) return null;
            var r = Rect.MinMaxRect(min.x - pad, min.y - pad, max.x + pad, max.y + pad);
            // Never smaller than a comfortable spotlight.
            if (r.width < 220f) r = new Rect(r.center.x - 110f, r.y, 220f, r.height);
            if (r.height < 220f) r = new Rect(r.x, r.center.y - 110f, r.width, 220f);
            return r;
        }

        Rect? HeroSpot()
        {
            var h = Hero;
            return h == null ? null : ScreenRect(60f, h.Position, h.Position + Vector3.up * 3.1f);
        }

        Rect? ConeSpot()
        {
            var h = Hero;
            if (h == null || _cm == null) return HeroSpot();
            float range = _cm.CurrentWitnessRange * 0.6f, half = _cm.CurrentWitnessAngle * 0.5f;
            var f = h.Forward;
            var left = Quaternion.Euler(0f, -half, 0f) * f * range;
            var right = Quaternion.Euler(0f, half, 0f) * f * range;
            return ScreenRect(30f, h.Position, h.Position + Vector3.up * 2.4f, h.Position + left, h.Position + right, h.Position + f * range);
        }

        Rect? DuelSpot()
        {
            var h = Hero;
            if (h == null) return null;
            var a = _duel != null && _duel.Ashgrave != null ? _duel.Ashgrave.Position : h.Position + h.Forward * 4f;
            return ScreenRect(70f, h.Position, h.Position + Vector3.up * 2.6f, a, a + Vector3.up * 2.6f);
        }
    }
}
