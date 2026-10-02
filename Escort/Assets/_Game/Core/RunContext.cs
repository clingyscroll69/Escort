using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS.Core
{
    /// <summary>
    /// Per-run root: event bus, tuning, seed and a small service registry that gameplay systems register into.
    /// One per gameplay scene. EditMode tests may create one on a bare GameObject.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public sealed class RunContext : MonoBehaviour
    {
        public static RunContext Current { get; private set; }

        public EventBus Events { get; private set; } = new EventBus();
        public Tuning Tuning { get; private set; }

        [Tooltip("Run seed: the only source of randomness (level assembly).")]
        public int Seed = 1;
        public int Chapter = 1;

        public Agent Hero;
        public Agent Sidekick;
        public SimTimers Timers { get; private set; }

        readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        AgentSeparation _separation;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;

        void Awake()
        {
            Init();
        }

        /// <summary>Idempotent. Tests can call it directly.</summary>
        public void Init(Tuning tuning = null)
        {
            if (Current != null && Current != this && Current.gameObject != null)
            {
                Debug.LogWarning("[RunContext] replacing previous context");
            }
            Current = this;
            if (tuning != null) Tuning = tuning;
            if (Tuning == null) Tuning = Tuning.LoadDefault();
            if (_separation == null)
            {
                SimLoop.Ensure();
                _separation = new AgentSeparation();
                SimLoop.Register(_separation);
                Timers = new SimTimers();
                SimLoop.Register(Timers);
            }
        }

        void OnDestroy()
        {
            if (_separation != null) SimLoop.Unregister(_separation);
            if (Timers != null) SimLoop.Unregister(Timers);
            if (Current == this) Current = null;
        }

        public void Register<T>(T service) where T : class => _services[typeof(T)] = service;

        public T Get<T>() where T : class => _services.TryGetValue(typeof(T), out var s) ? (T)s : null;

        public bool Has<T>() where T : class => _services.ContainsKey(typeof(T));

        public float SimTime => SimLoop.Instance != null ? SimLoop.Instance.SimTime : 0f;
    }
}
