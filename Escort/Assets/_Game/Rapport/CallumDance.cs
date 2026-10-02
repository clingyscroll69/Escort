using System.Collections.Generic;
using System.Linq;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Hero.Callum;
using HS.Sidekick;
using HS.Skills;
using HS.Skills.Impl;
using UnityEngine;

namespace HS.Rapport
{
    /// <summary>
    /// Callum's Dance (GDD §6.1). Moments: Unseen assist 3 (kill or disable a cheater/archer during a duel, unwitnessed),
    /// Averted cheat 4 (stop an ambush or a fake surrender), Covered lapse 2 (Cover Story removed a penalty), Wound
    /// treated after a duel 2. Negative assets: Caught −3 (−6 the second time in a fight), Spoiled duel −2, Friendly
    /// fire −3 (over 10% of his HP in a fight), Abandon −3 (over 20 m away while he's under 40% HP in combat).
    /// Honest failures (misses, interrupted bandages, sand on nobody) never cost anything.
    /// </summary>
    public sealed class CallumDance : IDanceJudge
    {
        public const float WUnseenAssist = 3f, WAvertedCheat = 4f, WCoveredLapse = 2f, WWoundTreated = 2f;
        public const float PCaught = 3f, PCaughtAgain = 6f, PSpoiledDuel = 2f, PFriendlyFire = 3f, PAbandon = 3f;
        public const float FriendlyFireFraction = 0.10f, AbandonRange = 20f, AbandonHp = 0.40f, AbandonGrace = 2f;
        // The ambush is "on offer" from the room's approach (26 m): scouting ahead during his threshold pause and
        // flushing it while he's still far away — unseen — must be the best play, not an uncredited one.
        public const float LapseWindowMax = 4f, WoundWindow = 30f, AssistRange = 30f, AmbushOfferRange = 26f;

        RunContext _ctx;
        HeroAgent _hero;
        CallumModule _cm;
        RapportLedger _l;
        readonly Dictionary<EnemyAgent, MomentOffer> _unseen = new Dictionary<EnemyAgent, MomentOffer>();
        readonly Dictionary<EnemyAgent, MomentOffer> _averted = new Dictionary<EnemyAgent, MomentOffer>();
        readonly List<(PenaltyEntry penalty, MomentOffer lapse, float at)> _lapses = new List<(PenaltyEntry, MomentOffer, float)>();
        readonly List<EnemyAgent> _scratch = new List<EnemyAgent>();
        MomentOffer _wound;
        int _caughtThisFight;
        float _ffThisFight, _abandonT;
        bool _ffDone, _abandonDone;

        SidekickAgent Sidekick => _ctx != null ? _ctx.Sidekick as SidekickAgent : null;
        bool InDuel => _cm.Challenged != null;
        float Now => _ctx != null ? _ctx.SimTime : 0f;

        public void Bind(RunContext ctx, HeroAgent hero, RapportLedger ledger)
        {
            _ctx = ctx;
            _hero = hero;
            _cm = hero.Module as CallumModule;
            _l = ledger;
            var ev = ctx.Events;
            ev.Damage += OnDamage;
            ev.Sabotage += OnSabotage;
            ev.CoverStory += OnCoverStory;
            ev.WoundTreated += OnWoundTreated;
            _cm.Caught += OnCaught;
            _cm.SpoiledDuel += OnSpoiled;
            _cm.DuelFinished += OnDuelFinished;
        }

        public void Unbind()
        {
            if (_ctx == null) return;
            var ev = _ctx.Events;
            ev.Damage -= OnDamage;
            ev.Sabotage -= OnSabotage;
            ev.CoverStory -= OnCoverStory;
            ev.WoundTreated -= OnWoundTreated;
            if (_cm != null)
            {
                _cm.Caught -= OnCaught;
                _cm.SpoiledDuel -= OnSpoiled;
                _cm.DuelFinished -= OnDuelFinished;
            }
            _ctx = null;
        }

        public void CloseAll(string reason)
        {
            foreach (var m in _unseen.Values) _l.Close(m, reason);
            foreach (var m in _averted.Values) _l.Close(m, reason);
            foreach (var (p, lapse, at) in _lapses) _l.Close(lapse, reason);
            _lapses.Clear();
            _l.Close(_wound, reason);
        }

        public void NewFight()
        {
            _caughtThisFight = 0;
            _ffThisFight = 0f;
            _ffDone = false;
            _abandonDone = false;
            _abandonT = 0f;
        }

        // ------------------------------------------------------------------ offers & windows (polled each tick)

        public void Tick(float dt)
        {
            if (_hero == null || _cm == null) return;
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is EnemyAgent e) || !e.IsAlive) continue;
                // Unseen assist: a cheater/archer is in the fight while he duels.
                // (Hidden gallery archers count too: taking one out before the volley is the hardest assist of all.)
                bool inFight = (!e.IsHidden && (e.IsActive || e.State == EnemyState.Surrendered)) || (e.IsHidden && e.IsRanged && e.Elevated);
                if (InDuel && e.IsCheater && inFight && e != _cm.Challenged
                    && !_unseen.ContainsKey(e) && Geo.FlatDistance(e.Position, _hero.Position) <= AssistRange)
                    _unseen[e] = _l.Offer("unseen_assist", WUnseenAssist, e, note: Name(e));
                // Averted cheat: a fake surrender in progress, or an ambush he's walking into.
                if (!_averted.ContainsKey(e))
                {
                    if (e.State == EnemyState.Surrendered && e.Stats != null && e.Stats.cheapShotDamage > 0f)
                        _averted[e] = _l.Offer("averted_cheat", WAvertedCheat, e, note: Name(e) + " fake surrender");
                    else if (e.IsHidden && !(e.IsRanged && e.Elevated) && Geo.FlatDistance(e.Position, _hero.Position) <= AmbushOfferRange)
                        _averted[e] = _l.Offer("averted_cheat", WAvertedCheat, e, note: Name(e) + " ambush");
                }
            }
            // His own duel opponent can't be "assisted unseen" — that's interfering in his duel, in front of him.
            if (_cm.Challenged != null && _unseen.TryGetValue(_cm.Challenged, out var mine) && mine != null && mine.Open)
                _l.Withdraw(mine, "became his duel");
            CloseResolved(_unseen, e => !e.IsAlive || !e.gameObject.activeInHierarchy || e.State == EnemyState.Spared || e.State == EnemyState.Fleeing);
            // Averted-cheat windows: an ambush closes when he springs (or is flushed out by you); a fake surrender when
            // the stab lands, he's spared, or he runs.
            _scratch.Clear();
            foreach (var kv in _averted)
                if (kv.Value != null && kv.Value.Open) _scratch.Add(kv.Key);
            foreach (var e in _scratch)
            {
                var m = _averted[e];
                if (m == null) continue;
                if (e == null || !e.IsAlive) _l.Close(m, "gone");
                else if (m.Note.EndsWith("ambush") && !e.IsHidden)
                {
                    if (e.RevealedBy == "sidekick") Capture(e, m, "flushed out before the ambush");
                    else _l.Close(m, "the ambush landed");
                }
                else if (m.Note.EndsWith("fake surrender") && e.State != EnemyState.Surrendered) _l.Close(m, "surrender ended: " + e.State);
            }
            // Covered-lapse windows.
            for (int i = _lapses.Count - 1; i >= 0; i--)
            {
                if (_lapses[i].lapse == null || !_lapses[i].lapse.Open) _lapses.RemoveAt(i);
                else if (Now - _lapses[i].at > LapseWindowMax) _l.Close(_lapses[i].lapse, "not covered");
            }
            if (_wound != null && _wound.Open && Now > _wound.ClosesAt) _l.Close(_wound, "wound left untreated");
            TickAbandon(dt);
        }

        void CloseResolved(Dictionary<EnemyAgent, MomentOffer> offers, System.Func<EnemyAgent, bool> resolved)
        {
            _scratch.Clear();
            foreach (var kv in offers)
                if (kv.Value != null && kv.Value.Open && (kv.Key == null || resolved(kv.Key))) _scratch.Add(kv.Key);
            foreach (var e in _scratch) _l.Close(offers[e], "resolved without you");
        }

        void TickAbandon(float dt)
        {
            var sk = Sidekick;
            bool inCombat = InDuel || _cm.Engagers > 0;
            bool abandoned = sk != null && sk.IsAlive && _hero.IsAlive && inCombat && _hero.Health.Fraction < AbandonHp
                             && Geo.FlatDistance(sk.Position, _hero.Position) > AbandonRange;
            _abandonT = abandoned ? _abandonT + dt : 0f;
            if (!_abandonDone && _abandonT >= AbandonGrace)
            {
                _abandonDone = true;
                _l.Penalize("abandon", PAbandon, "over 20 m away while he was under 40%");
            }
        }

        // ------------------------------------------------------------------ captures

        void OnDamage(DamageInfo d, float applied)
        {
            if (!(d.Source is SidekickAgent sk)) return;
            if (d.Target == _hero)
            {
                _ffThisFight += applied;
                if (!_ffDone && _ffThisFight > FriendlyFireFraction * _hero.Health.Max)
                {
                    _ffDone = true;
                    _l.Penalize("friendly_fire", PFriendlyFire, d.Tag);
                }
                return;
            }
            if (!(d.Target is EnemyAgent e)) return;
            // Fake surrender stopped (the event fires before the hit changes his state).
            if (_averted.TryGetValue(e, out var av) && av != null && av.Open && e.State == EnemyState.Surrendered)
                Capture(e, av, d.Tag + " on the false surrender");
            bool disabled = !e.IsAlive || d.Stagger >= 0.3f || e.Status.Has(StatusType.Blinded);
            if (disabled) TryUnseenAssist(e, d.Point, sk.Position, d.Tag);
        }

        void OnSabotage(SabotageEvent s)
        {
            if (s.Tag != "pocket_sand") return; // prop collapses and bolts arrive as Damage events
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is EnemyAgent e) || !e.IsAlive || !e.Status.Has(StatusType.Blinded)) continue;
                if (Geo.FlatDistance(e.Position, s.Position) > PocketSandSkill.Radius + 0.3f) continue;
                if (_averted.TryGetValue(e, out var av) && av != null && av.Open && e.State == EnemyState.Surrendered) Capture(e, av, "sand on the false surrender");
                TryUnseenAssist(e, s.Position, s.ActorPosition, "pocket_sand");
            }
        }

        void TryUnseenAssist(EnemyAgent e, Vector3 deed, Vector3 actor, string how)
        {
            if (!InDuel || !_unseen.TryGetValue(e, out var m) || m == null || !m.Open) return;
            if (_cm.Witnesses(deed, actor, out _)) return; // seen: not an unseen assist (and Callum's Honor/Caught apply)
            Capture(e, m, how);
        }

        /// <summary>One capture per enemy across both moment kinds (a sanded false surrender is one save, not two).</summary>
        void Capture(EnemyAgent e, MomentOffer m, string note)
        {
            if (!_l.Capture(m, note)) return;
            if (_unseen.TryGetValue(e, out var u) && u != m) _l.Close(u, "already credited");
            if (_averted.TryGetValue(e, out var a) && a != m) _l.Close(a, "already credited");
        }

        void OnCoverStory(float restore, float window)
        {
            for (int i = 0; i < _lapses.Count; i++)
            {
                var (penalty, lapse, at) = _lapses[i];
                if (lapse == null || !lapse.Open || Now - at > window + 1e-4f) continue;
                if (_l.Capture(lapse, "cover story")) _l.Refund(penalty, "covered");
            }
        }

        void OnWoundTreated(Agent healer, Agent patient)
        {
            if (patient == _hero && healer is SidekickAgent && _wound != null && _wound.Open) _l.Capture(_wound, "bandaged");
        }

        void OnDuelFinished(EnemyAgent target, DuelEndReason reason)
        {
            // Only offered when there is a wound a bandage can treat (never an uncapturable offer).
            if (!_hero.IsAlive || !_hero.HasMinorWound) return;
            if (_wound != null && _wound.Open) return;
            _wound = _l.Offer("wound_treated", WWoundTreated, _hero, WoundWindow, "wounded after the duel");
        }

        // ------------------------------------------------------------------ penalties

        void OnCaught(SabotageEvent e, bool byStone)
        {
            _caughtThisFight++;
            float amount = _caughtThisFight >= 2 ? PCaughtAgain : PCaught;
            var p = _l.Penalize("caught", amount, (byStone ? "stone: " : "") + e.Tag);
            var skills = Sidekick != null ? Sidekick.GetComponent<SidekickSkills>() : null;
            bool canCover = skills != null && skills.System.Loadout.Contains("cover_story");
            if (canCover && p != null)
            {
                var lapse = _l.Offer("covered_lapse", WCoveredLapse, p, LapseWindowMax, "caught: " + e.Tag);
                if (lapse != null) _lapses.Add((p, lapse, Now));
            }
        }

        void OnSpoiled(EnemyAgent target) => _l.Penalize("spoiled_duel", PSpoiledDuel, Name(target));

        static string Name(Agent a) => a == null ? "-" : a.name.Replace("(Clone)", "");
    }
}
