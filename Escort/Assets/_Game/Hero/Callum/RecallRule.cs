using HS.Core;
using HS.Enemies;
using HS.Sidekick;
using UnityEngine;

namespace HS.Hero.Callum
{
    /// <summary>
    /// Squire's Recall (GDD §4.2, Callum's version): the sidekick is Downed; he goes to her and channels her back up. How
    /// is his Stage's — args [channel s, max hostiles within 10 m of her (−1: any), wound 0/1, rise fraction, leaves a duel
    /// 0/1, sprints 0/1]. At S0–S1 he won't leave an active challenge until the duel ends or the target is Unready.
    /// S0 8 s, only with nobody near, +1 wound, "This is so useless. Get up." … S3 1.5 s, sprints mid-fight, she rises at 50%.
    /// </summary>
    public sealed class RecallRule : HeroRule
    {
        public const float HostileRadius = 10f, KneelReach = 1.6f;
        public override string Id => "callum_recall";
        public override string Icon => "recall";
        public override string Label => "Going back for you";

        float Channel => Arg(0, 8f);
        int MaxHostiles => Mathf.RoundToInt(Arg(1, 0f));
        bool Wounds => Arg(2, 1f) > 0.5f;
        float RiseFraction => Arg(3, 0.3f);
        bool LeavesDuel => Arg(4, 0f) > 0.5f;
        bool Sprints => Arg(5, 0f) > 0.5f;

        float _t;
        bool _committed;

        static SidekickAgent Sk => RunContext.Current != null ? RunContext.Current.Sidekick as SidekickAgent : null;
        static RecallState State => RunContext.Current != null ? RunContext.Current.Get<RecallState>() : null;

        static int HostilesNear(Vector3 p)
        {
            int n = 0;
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] is EnemyAgent e && e.IsAlive && (e.IsActive || e.State == EnemyState.Surrendered) && !e.Asleep && Geo.FlatDistance(e.Position, p) <= HostileRadius) n++;
            return n;
        }

        public override bool CanRun(HeroContext c)
        {
            var sk = Sk;
            var st = State;
            if (sk == null || !sk.IsDowned || st == null || !st.Learned) return false;
            if (_committed) return true; // once he's kneeling, he finishes
            if (st.UsesLeft <= 0 || sk.DownedRemaining <= Channel) return false;
            if (MaxHostiles >= 0 && HostilesNear(sk.Position) > MaxHostiles) return false;
            if (!LeavesDuel && c.Hero.Module is CallumModule cm && cm.Challenged != null && !cm.Challenged.IsUnreadyFor(c.Hero)) return false;
            return true;
        }

        public override void Enter(HeroContext c)
        {
            _t = 0f;
            if (c.Hero.Module is CallumModule cm && cm.Challenged != null) cm.EndDuel(DuelEndReason.Abandoned);
        }

        public override void Tick(HeroContext c, float dt)
        {
            var sk = Sk;
            if (sk == null || !sk.IsDowned)
            {
                _committed = false;
                return;
            }
            var hero = c.Hero;
            if (!_committed && Geo.FlatDistance(hero.Position, sk.Position) > KneelReach)
            {
                hero.MoveTowards(sk.Position, hero.CombatSpeed * (Sprints ? 1.35f : 1f), KneelReach * 0.8f, dt);
                return;
            }
            if (!_committed)
            {
                _committed = true;
                _t = 0f;
                hero.Presenter?.PlayAction("kneel", Channel);
            }
            hero.Hold(dt);
            hero.FaceTowards(sk.Position, dt);
            _t += dt;
            if (_t < Channel) return;
            _committed = false;
            if (!State.Use()) return;
            sk.Rise(RiseFraction);
            hero.Presenter?.PlayAction("none");
            if (Wounds) hero.Wounds.Add(WoundType.SwordArmStrain);
            if (hero.Module is CallumModule m) m.Bark(Lines[Mathf.Clamp((int)hero.Stage, 0, Lines.Length - 1)], 3);
        }

        public override void Exit(HeroContext c)
        {
            if (!_committed) return;
            _committed = false;
            c.Hero.Presenter?.PlayAction("none");
        }

        static readonly string[][] Lines =
        {
            new[] { "This is so useless. Get up." },
            new[] { "...Fine. Don't make it a habit." },
            new[] { "Up. I've got you." },
            new[] { "I'm not losing you. Not today." },
        };
    }
}
