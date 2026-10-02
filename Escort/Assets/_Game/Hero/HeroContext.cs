using System.Collections.Generic;
using HS.Core;
using UnityEngine;

namespace HS.Hero
{
    /// <summary>Everything a hero rule may read or write. One per hero.</summary>
    public sealed class HeroContext
    {
        public HeroAgent Hero;
        public RouteFollower Route;
        public Stage Stage;
        public RunContext Run => RunContext.Current;
        public Agent Sidekick => Run != null ? Run.Sidekick : null;
        public readonly List<Agent> Scratch = new List<Agent>();

        /// <summary>Per-hero blackboard for rule state that must survive rule switches.</summary>
        public readonly Dictionary<string, float> Values = new Dictionary<string, float>();

        public float Get(string key, float fallback = 0f) => Values.TryGetValue(key, out var v) ? v : fallback;
        public void Set(string key, float v) => Values[key] = v;

        public bool SidekickNear(float range)
        {
            var s = Sidekick;
            return s == null || !s.IsAlive || Geo.FlatDistance(s.Position, Hero.Position) <= range;
        }
    }
}
