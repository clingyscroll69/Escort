using System;
using System.Collections.Generic;
using HS.Core;
using UnityEngine;

namespace HS.Hero
{
    /// <summary>Walk the authored route (lowest priority besides Idle).</summary>
    public sealed class FollowRouteRule : HeroRule
    {
        public override string Id => "follow_route";
        public override string Icon => "route";
        public override string Label => "Walking the road";
        public override bool CanRun(HeroContext c) => !c.Route.AtEnd && !c.Route.Paused && !c.Route.Held;
        public override void Tick(HeroContext c, float dt) => c.Hero.StepRoute(dt, c.Hero.RouteSpeed);
    }

    /// <summary>Pause at a threshold (door, arena, bridge) so the sidekick can prepare — predictable pacing.</summary>
    public sealed class ThresholdPauseRule : HeroRule
    {
        public override string Id => "threshold_pause";
        public override string Icon => "threshold";
        public override string Label => "Waiting at the threshold";
        public override bool CanRun(HeroContext c) => c.Route.Paused || (c.Route.Held && !c.Route.AtEnd);
        public override void Tick(HeroContext c, float dt)
        {
            c.Hero.StepRoute(dt, 0f);
            c.Hero.Hold(dt);
            if (!c.Route.AtEnd) c.Hero.FaceTowards(c.Route.Current.Position, dt);
        }
    }

    /// <summary>End of the road / nothing to do.</summary>
    public sealed class IdleRule : HeroRule
    {
        public override string Id => "idle";
        public override string Icon => "";
        public override string Label => "At rest";
        public override bool CanRun(HeroContext c) => true;
        public override void Tick(HeroContext c, float dt) => c.Hero.Hold(dt);
    }

    /// <summary>Id → rule constructor. Hero modules register their rules (e.g. CallumRules.Register()).</summary>
    public static class HeroRuleFactory
    {
        static readonly Dictionary<string, Func<HeroRule>> Ctors = new Dictionary<string, Func<HeroRule>>
        {
            { "follow_route", () => new FollowRouteRule() },
            { "threshold_pause", () => new ThresholdPauseRule() },
            { "idle", () => new IdleRule() },
        };

        public static void Register(string id, Func<HeroRule> ctor) => Ctors[id] = ctor;

        static bool _defaults;

        /// <summary>Hero modules register here explicitly (RuntimeInitializeOnLoad hooks don't fire when play mode
        /// starts without a domain reload).</summary>
        static void EnsureDefaults()
        {
            if (_defaults) return;
            _defaults = true;
            HS.Hero.Callum.CallumRules.Register();
        }

        public static HeroRule Create(RuleEntry e)
        {
            EnsureDefaults();
            if (!Ctors.TryGetValue(e.id, out var ctor))
            {
                Debug.LogError($"[HeroRuleFactory] unknown rule '{e.id}'");
                return null;
            }
            var r = ctor();
            r.Args = e.args ?? Array.Empty<float>();
            return r;
        }

        public static List<IHeroRule> Build(IEnumerable<RuleEntry> entries)
        {
            var list = new List<IHeroRule>();
            foreach (var e in entries)
            {
                var r = Create(e);
                if (r != null) list.Add(r);
            }
            // Idle is always the implicit last resort.
            if (list.Count == 0 || list[list.Count - 1].Id != "idle") list.Add(new IdleRule());
            return list;
        }
    }
}
