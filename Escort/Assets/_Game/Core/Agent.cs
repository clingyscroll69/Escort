using System.Collections.Generic;
using UnityEngine;

namespace HS.Core
{
    /// <summary>Presentation hook implemented by AnimDriver. Gameplay never waits on animation (determinism).</summary>
    public interface IAgentPresenter
    {
        void SetLocomotion(float planarSpeed, bool crouched);
        /// <summary>Fire-and-forget action clip, e.g. "attack", "salute", "hit", "death", "dodge", "block", "throw",
        /// "shoot", "kneel", "surrender", "scold", "cheer", "bandage", "interact", "getup".</summary>
        void PlayAction(string action, float duration = -1f);
        void SetFlag(string flag, bool on);
    }

    /// <summary>
    /// Base for every character in the simulation (hero, sidekick, enemies). Ticks from <see cref="SimLoop"/>.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public abstract class Agent : MonoBehaviour, ISimTickable
    {
        [SerializeField] string agentId;

        public string AgentId
        {
            get => string.IsNullOrEmpty(agentId) ? name : agentId;
            set => agentId = value;
        }

        public abstract Faction Faction { get; }
        public Health Health { get; protected set; }
        public StatusSet Status { get; } = new StatusSet();
        public CharacterMotor Motor { get; private set; }
        public IAgentPresenter Presenter { get; set; }
        public CharacterController Controller { get; private set; }

        public virtual int TickOrder => TickOrders.Enemy;
        public Vector3 Position => transform.position;
        public Vector3 Forward => transform.forward;
        /// <summary>How many melee attackers may swing at this agent at once (attack tokens).</summary>
        public virtual int MeleeSlots => 2;
        /// <summary>Fixed in place (stones): separation pushes others away instead of sharing the push.</summary>
        public virtual bool Immovable => false;
        public float Radius => Controller != null ? Controller.radius : 0.4f;
        public bool IsAlive => Health != null && !Health.IsDead;

        /// <summary>Seconds since this agent last took damage (sim time).</summary>
        public float TimeSinceHurt { get; private set; } = 999f;

        protected RunContext Ctx => RunContext.Current;

        protected virtual void Awake()
        {
            Controller = GetComponent<CharacterController>();
            Motor = new CharacterMotor(Controller);
            if (Health == null) Health = new Health(100f);
        }

        protected virtual void OnEnable()
        {
            AgentRegistry.Add(this);
            SimLoop.Register(this);
        }

        protected virtual void OnDisable()
        {
            AgentRegistry.Remove(this);
            SimLoop.Unregister(this);
        }

        float _hotRate, _hotRemaining;

        /// <summary>Heal-over-time (Bandage). Stacks by refreshing to the larger remaining total.</summary>
        public void AddHealOverTime(float total, float duration)
        {
            float remainingTotal = _hotRate * _hotRemaining;
            float newTotal = Mathf.Max(remainingTotal, total);
            _hotRemaining = duration;
            _hotRate = newTotal / Mathf.Max(0.01f, duration);
        }

        public bool IsHealing => _hotRemaining > 0f;

        public void SimTick(float dt)
        {
            TimeSinceHurt += dt;
            Status.Tick(dt);
            if (_hotRemaining > 0f && IsAlive)
            {
                float step = Mathf.Min(dt, _hotRemaining);
                Health.Heal(_hotRate * step);
                _hotRemaining -= step;
            }
            OnSimTick(dt);
        }

        protected abstract void OnSimTick(float dt);

        /// <summary>Single entry point for damage so every hit is published on the event bus.</summary>
        public virtual float TakeDamage(DamageInfo d)
        {
            if (!IsAlive) return 0f;
            d.Target = this;
            if (d.Point == Vector3.zero) d.Point = Position;
            d.Amount = ModifyIncomingDamage(d);
            float applied = Health.ApplyDamage(d);
            if (applied <= 0f) return 0f;
            TimeSinceHurt = 0f;
            // Publish first: listeners judge the victim as it was when struck (a hit's own stagger must not make its
            // target look "already helpless" to Callum's code).
            Ctx?.Events.RaiseDamage(d, applied);
            if (d.Stagger > 0f && IsAlive) Status.Apply(StatusType.Staggered, d.Stagger, d.Source);
            if (IsAlive) OnHurt(d, applied);
            else HandleDeath(d);
            return applied;
        }

        protected virtual float ModifyIncomingDamage(DamageInfo d) => d.Amount;

        protected virtual void OnHurt(DamageInfo d, float applied)
        {
            Presenter?.PlayAction(d.Kind == DamageKind.Heavy ? "hit_heavy" : "hit");
        }

        void HandleDeath(DamageInfo d)
        {
            Motor.Stop();
            Presenter?.PlayAction("death");
            OnDied(d);
            Ctx?.Events.RaiseDeath(this, d);
        }

        protected virtual void OnDied(DamageInfo d)
        {
            if (Controller != null) Controller.enabled = false;
        }

        public virtual bool IsHostileTo(Agent other)
        {
            if (other == null || other == this) return false;
            if (Faction == Faction.Hostile) return other.Faction == Faction.Hero || other.Faction == Faction.Sidekick;
            if (Faction == Faction.Hero || Faction == Faction.Sidekick) return other.Faction == Faction.Hostile;
            return false;
        }
    }

    /// <summary>All live agents in insertion order (deterministic iteration).</summary>
    public static class AgentRegistry
    {
        static readonly List<Agent> _all = new List<Agent>(128);
        public static IReadOnlyList<Agent> All => _all;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => _all.Clear();

        public static void Add(Agent a)
        {
            if (!_all.Contains(a)) _all.Add(a);
        }

        public static void Remove(Agent a) => _all.Remove(a);

        public static void Clear() => _all.Clear();

        public static Agent Nearest(Vector3 pos, float maxRange, System.Func<Agent, bool> filter)
        {
            Agent best = null;
            float bestD = maxRange * maxRange;
            for (int i = 0; i < _all.Count; i++)
            {
                var a = _all[i];
                if (a == null || !a.IsAlive || (filter != null && !filter(a))) continue;
                float d = Geo.FlatSqrDistance(pos, a.Position);
                if (d <= bestD)
                {
                    bestD = d;
                    best = a;
                }
            }
            return best;
        }

        public static void InRadius(Vector3 pos, float radius, List<Agent> results, System.Func<Agent, bool> filter = null)
        {
            results.Clear();
            float r2 = radius * radius;
            for (int i = 0; i < _all.Count; i++)
            {
                var a = _all[i];
                if (a == null || !a.IsAlive || (filter != null && !filter(a))) continue;
                if (Geo.FlatSqrDistance(pos, a.Position) <= r2) results.Add(a);
            }
        }

        public static int Count(System.Func<Agent, bool> filter)
        {
            int n = 0;
            for (int i = 0; i < _all.Count; i++)
                if (_all[i] != null && _all[i].IsAlive && filter(_all[i])) n++;
            return n;
        }
    }

    /// <summary>Soft push-apart so agents don't stack. Runs after movement each tick.</summary>
    public sealed class AgentSeparation : ISimTickable
    {
        public int TickOrder => TickOrders.Physics;

        public void SimTick(float dt)
        {
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || !a.IsAlive || !a.Controller.enabled) continue;
                for (int j = i + 1; j < all.Count; j++)
                {
                    var b = all[j];
                    if (b == null || !b.IsAlive || !b.Controller.enabled) continue;
                    float minD = a.Radius + b.Radius;
                    var d = Geo.Flat(b.Position - a.Position);
                    float dist = d.magnitude;
                    if (dist >= minD) continue;
                    if (a.Immovable && b.Immovable) continue;
                    var n = dist > 1e-4f ? d / dist : new Vector3(1f, 0f, 0f);
                    float overlap = minD - dist;
                    float pa = a.Immovable ? 0f : b.Immovable ? overlap : overlap * 0.5f;
                    float pb = b.Immovable ? 0f : a.Immovable ? overlap : overlap * 0.5f;
                    if (pa > 0f) a.Motor.Nudge(-n * pa);
                    if (pb > 0f) b.Motor.Nudge(n * pb);
                }
            }
        }
    }
}
