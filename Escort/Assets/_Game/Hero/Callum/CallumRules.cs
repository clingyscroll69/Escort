using HS.Core;
using HS.Enemies;
using UnityEngine;

namespace HS.Hero.Callum
{
    /// <summary>Callum's rule vocabulary (GDD §6.1). Priority order and per-Stage numbers are data (Callum_RuleSet).</summary>
    public static class CallumRules
    {
        public static void Register()
        {
            HeroRuleFactory.Register("callum_recall", () => new RecallRule());
            HeroRuleFactory.Register("callum_finisher", () => new FinisherRule());
            HeroRuleFactory.Register("callum_look_away", () => new LookAwayRule());
            HeroRuleFactory.Register("callum_fallback", () => new FallbackRule());
            HeroRuleFactory.Register("callum_wait_unready", () => new WaitUnreadyRule());
            HeroRuleFactory.Register("callum_salute", () => new SaluteRule());
            HeroRuleFactory.Register("callum_fight", () => new FightRule());
            HeroRuleFactory.Register("callum_challenge", () => new ChallengeRule());
        }

        static CallumModule M(HeroContext c) => c.Hero.Module as CallumModule;

        /// <summary>S2 — Look Away: he turns his back for 3 s when you ask him to.</summary>
        public sealed class LookAwayRule : HeroRule
        {
            public override string Id => "callum_look_away";
            public override string Icon => "look_away";
            public override string Label => "Not looking";
            public override bool CanRun(HeroContext c) => M(c) != null && M(c).LookingAway;
            public override void Enter(HeroContext c) => c.Hero.Presenter?.PlayAction("folded", CallumModule.LookAwayTime);
            public override void Tick(HeroContext c, float dt)
            {
                c.Hero.Hold(dt);
                // His back to you: whatever you're about to do is behind him.
                var sk = RunContext.Current != null ? RunContext.Current.Sidekick : null;
                if (sk != null) c.Hero.FaceTowards(c.Hero.Position + (c.Hero.Position - sk.Position), dt);
            }
            public override void Exit(HeroContext c) => c.Hero.Presenter?.PlayAction("none");
        }

        /// <summary>Finisher — Judgment (chapter 3 on): a 3 s charge (2 s from chapter 5), then one blow for 6×.</summary>
        public sealed class FinisherRule : HeroRule
        {
            public override string Id => "callum_finisher";
            public override string Icon => _duet ? "duet" : "judgment";
            public override string Label => _duet ? "Charging his Judgment — and asking for a hand" : "Charging his Judgment";
            bool _duet;
            public override bool CanRun(HeroContext c)
            {
                var m = M(c);
                if (m == null) return false;
                // A Duet Window (S3) asked for her blows: a man reeling from them is no reason to stop.
                if (m.FinisherCharging) return m.Challenged != null && (m.DuetWindow || !m.Challenged.IsUnreadyFor(c.Hero));
                return m.WantsFinisher;
            }
            public override void Enter(HeroContext c)
            {
                if (!M(c).FinisherCharging) M(c).BeginFinisher();
            }
            public override void Tick(HeroContext c, float dt)
            {
                _duet = M(c).DuetWindow;
                M(c).TickFinisher(dt);
            }

            public override void Exit(HeroContext c)
            {
                _duet = false;
                M(c).CancelFinisher();
            }
        }

        /// <summary>Rule 4: fall back to a chokepoint if 3+ enemies engage him.</summary>
        public sealed class FallbackRule : HeroRule
        {
            Vector3 _choke;
            public override string Id => "callum_fallback";
            public override string Icon => "fallback";
            public override string Label => "Falling back to the narrows";
            public override bool CanRun(HeroContext c) => M(c) != null && M(c).NeedsFallback(out _choke);
            public override void Enter(HeroContext c) => M(c).BeginFallback(_choke);
            public override void Tick(HeroContext c, float dt) => M(c).TickFallback(_choke, dt);
        }

        /// <summary>Rule 3: wait up to N s (S0 3, S1 2) if the target is Unready; a yielded man is spared.</summary>
        public sealed class WaitUnreadyRule : HeroRule
        {
            public override string Id => "callum_wait_unready";
            public override string Icon => "wait";
            public override string Label => "Waiting for a fair fight";

            public override bool CanRun(HeroContext c)
            {
                var m = M(c);
                if (m == null || m.Challenged == null || m.Saluting) return false;
                if (!m.Challenged.IsUnreadyFor(c.Hero)) return false;
                float cap = Mathf.Min(Arg(0, m.WaitCap), m.WaitCap);
                return m.WaitT < cap || m.Challenged.State == EnemyState.Surrendered;
            }

            public override void Tick(HeroContext c, float dt) => M(c).TickWait(dt);
        }

        /// <summary>Rule 2 (part): the 1.2 s salute that opens every duel.</summary>
        public sealed class SaluteRule : HeroRule
        {
            public override string Id => "callum_salute";
            public override string Icon => "challenge";
            public override string Label => "Saluting his opponent";
            public override bool CanRun(HeroContext c) => M(c) != null && M(c).Saluting;
            public override void Tick(HeroContext c, float dt) => M(c).TickSalute(dt);
        }

        /// <summary>Rule 1: fight the challenged target.</summary>
        public sealed class FightRule : HeroRule
        {
            public override string Id => "callum_fight";
            public override string Icon => "fight";
            public override string Label => "Duelling his challenger";
            public override bool CanRun(HeroContext c) => M(c) != null && M(c).DuelActive;
            public override void Tick(HeroContext c, float dt) => M(c).TickFight(dt);
        }

        /// <summary>Rule 2: challenge the nearest hostile within 15 m.</summary>
        public sealed class ChallengeRule : HeroRule
        {
            EnemyAgent _pick;
            public override string Id => "callum_challenge";
            public override string Icon => "challenge";
            public override string Label => "Issuing a challenge";

            public override bool CanRun(HeroContext c)
            {
                var m = M(c);
                if (m == null || m.Challenged != null) return false;
                _pick = m.PickChallengeTarget();
                return _pick != null;
            }

            public override void Tick(HeroContext c, float dt)
            {
                if (_pick != null && M(c).Challenged == null) M(c).StartChallenge(_pick);
                c.Hero.Hold(dt);
            }
        }
    }
}
