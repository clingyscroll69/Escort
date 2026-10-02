using System;
using System.Collections.Generic;
using HS.Core;
using UnityEngine;

namespace HS.Hero
{
    /// <summary>One prioritised behaviour of the hero (GDD §4.2: "Each hero runs a prioritized rule list").</summary>
    public interface IHeroRule
    {
        string Id { get; }
        /// <summary>Icon shown over the hero's head while this rule is active (Resources/Icons/&lt;Icon&gt;).</summary>
        string Icon { get; }
        /// <summary>Plain-language label for the Hero Insight accessibility toggle.</summary>
        string Label { get; }
        bool CanRun(HeroContext c);
        void Enter(HeroContext c);
        void Tick(HeroContext c, float dt);
        void Exit(HeroContext c);
    }

    /// <summary>Convenience base with optional numeric arguments from the rule-set data.</summary>
    public abstract class HeroRule : IHeroRule
    {
        public abstract string Id { get; }
        public abstract string Icon { get; }
        public virtual string Label => Id;
        public float[] Args = Array.Empty<float>();
        protected float Arg(int i, float fallback) => Args != null && i < Args.Length ? Args[i] : fallback;
        public abstract bool CanRun(HeroContext c);
        public virtual void Enter(HeroContext c) { }
        public abstract void Tick(HeroContext c, float dt);
        public virtual void Exit(HeroContext c) { }
    }

    /// <summary>
    /// Priority selector: the first rule whose CanRun is true runs. A reaction delay (Concussion wound, +0.5 s) postpones
    /// switching to a new rule (GDD §4.2 wound table).
    /// </summary>
    public sealed class HeroBrain
    {
        readonly List<IHeroRule> _rules = new List<IHeroRule>();
        IHeroRule _pending;
        float _pendingT;

        public IReadOnlyList<IHeroRule> Rules => _rules;
        public IHeroRule Active { get; private set; }
        public string ActiveId => Active?.Id;
        public string ActiveIcon => Active?.Icon;
        public float ReactionDelay;
        public event Action<IHeroRule, IHeroRule> RuleChanged;

        public void SetRules(IEnumerable<IHeroRule> rules, HeroContext c)
        {
            if (Active != null) Active.Exit(c);
            Active = null;
            _pending = null;
            _rules.Clear();
            _rules.AddRange(rules);
        }

        public IHeroRule Select(HeroContext c)
        {
            for (int i = 0; i < _rules.Count; i++)
                if (_rules[i].CanRun(c)) return _rules[i];
            return null;
        }

        public void Tick(HeroContext c, float dt)
        {
            var best = Select(c);
            if (best != Active)
            {
                if (ReactionDelay > 0f && Active != null)
                {
                    if (_pending != best)
                    {
                        _pending = best;
                        _pendingT = 0f;
                    }
                    _pendingT += dt;
                    if (_pendingT >= ReactionDelay) Switch(best, c);
                }
                else Switch(best, c);
            }
            else _pending = null;
            Active?.Tick(c, dt);
        }

        void Switch(IHeroRule next, HeroContext c)
        {
            var prev = Active;
            prev?.Exit(c);
            Active = next;
            _pending = null;
            next?.Enter(c);
            RuleChanged?.Invoke(prev, next);
        }
    }

    [Serializable]
    public class RuleEntry
    {
        public string id;
        public float[] args = Array.Empty<float>();
        public RuleEntry() { }
        public RuleEntry(string id, params float[] args)
        {
            this.id = id;
            this.args = args;
        }
    }

    // ------------------------------------------------------------------ Route ------------------------------------

    [Serializable]
    public struct RouteNode
    {
        public Vector3 Position;
        /// <summary>Doors, arenas, bridges: the hero pauses here (predictable path, time to prepare).</summary>
        public bool Threshold;
        /// <summary>Candidate fall-back position (Callum rule 4).</summary>
        public bool Chokepoint;
        public int RoomIndex;
        public string Label;
    }

    /// <summary>
    /// Walks an authored route graph (GDD §4.2 Route). Pauses at thresholds until the sidekick is near (after a
    /// minimum pause) or a maximum wait elapses. Deterministic; plain C# so it's unit-testable.
    /// </summary>
    public sealed class RouteFollower
    {
        public readonly List<RouteNode> Nodes = new List<RouteNode>();
        public int Index { get; private set; }
        public bool Paused { get; private set; }
        public float PauseTimer { get; private set; }
        /// <summary>External hold (encounter in progress, cutscene).</summary>
        public bool Held;
        public float ArriveRadius = 0.6f;
        readonly HashSet<int> _passedThresholds = new HashSet<int>();

        public bool AtEnd => Index >= Nodes.Count;
        public RouteNode Current => Nodes[Mathf.Clamp(Index, 0, Mathf.Max(0, Nodes.Count - 1))];
        public event Action<RouteNode> ThresholdReached;
        public event Action<RouteNode> ThresholdReleased;

        public void SetNodes(IEnumerable<RouteNode> nodes, int startIndex = 0)
        {
            Nodes.Clear();
            Nodes.AddRange(nodes);
            Index = startIndex;
            Paused = false;
            _passedThresholds.Clear();
        }

        public void AppendNodes(IEnumerable<RouteNode> nodes) => Nodes.AddRange(nodes);

        /// <summary>Advance the follower. Returns the desired planar direction (zero when paused or done).</summary>
        public Vector3 Tick(Vector3 position, float dt, bool sidekickNear, float minPause, float maxWait)
        {
            if (AtEnd) return Vector3.zero;
            if (Paused)
            {
                PauseTimer += dt;
                if (!Held && ((PauseTimer >= minPause && sidekickNear) || PauseTimer >= maxWait))
                {
                    Paused = false;
                    _passedThresholds.Add(Index);
                    var n = Nodes[Index];
                    Index++;
                    ThresholdReleased?.Invoke(n);
                }
                return Vector3.zero;
            }
            if (Held) return Vector3.zero;
            var node = Nodes[Index];
            if (Geo.FlatDistance(position, node.Position) <= ArriveRadius)
            {
                if (node.Threshold && !_passedThresholds.Contains(Index))
                {
                    Paused = true;
                    PauseTimer = 0f;
                    ThresholdReached?.Invoke(node);
                    return Vector3.zero;
                }
                Index++;
                if (AtEnd) return Vector3.zero;
                node = Nodes[Index];
            }
            return Geo.DirTo(position, node.Position);
        }

        /// <summary>Nearest chokepoint node at or behind the current index (Callum's fall-back).</summary>
        public bool TryNearestChokepoint(Vector3 from, out Vector3 point)
        {
            point = default;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (!Nodes[i].Chokepoint) continue;
                float d = Geo.FlatSqrDistance(from, Nodes[i].Position);
                if (d < best)
                {
                    best = d;
                    point = Nodes[i].Position;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>Jump forward to the first node of a room (restore points).</summary>
        public void SkipToRoom(int roomIndex)
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i].RoomIndex == roomIndex)
                {
                    Index = i;
                    Paused = false;
                    return;
                }
            }
        }
    }
}
