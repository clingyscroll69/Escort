using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS.Core
{
    /// <summary>Anything that advances with the deterministic gameplay clock.</summary>
    public interface ISimTickable
    {
        /// <summary>Lower runs first. Ties resolve by registration order (deterministic for a given seed).</summary>
        int TickOrder { get; }
        void SimTick(float dt);
    }

    /// <summary>Canonical tick orders so systems run in a fixed, documented sequence.</summary>
    public static class TickOrders
    {
        public const int Director = 0;
        public const int SidekickInput = 100;
        public const int Hero = 200;
        public const int Enemy = 300;
        public const int Skills = 400;
        public const int Projectiles = 450;
        public const int Physics = 500;
        public const int Status = 600;
        public const int Judges = 700;
        public const int Telemetry = 900;
    }

    /// <summary>
    /// Fixed-step gameplay clock (GDD §2 determinism). Gameplay never reads Time.deltaTime; it runs in SimTick(Dt).
    /// Presentation reads <see cref="Alpha"/> to interpolate. The balance harness sets <see cref="FastTicksPerFrame"/>
    /// to run many ticks per rendered frame.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class SimLoop : MonoBehaviour
    {
        public const float Dt = 1f / 60f;
        const float MaxCatchUp = 0.25f;

        public static SimLoop Instance { get; private set; }

        public int TickIndex { get; private set; }
        public float SimTime => TickIndex * Dt;
        public float Alpha { get; private set; } = 1f;

        /// <summary>Gameplay pause (menus, level-up window). Presentation keeps running.</summary>
        public bool Paused { get; set; }

        /// <summary>Presentation-time scale (slow motion). Steps stay fixed size.</summary>
        public float TimeScale { get; set; } = 1f;

        /// <summary>&gt;0 = harness fast mode: exactly N ticks per frame, ignoring wall-clock time.</summary>
        public int FastTicksPerFrame { get; set; }

        public event Action BeforeTick;
        public event Action<int> AfterTick;

        struct Entry
        {
            public ISimTickable Item;
            public int Order;
            public long Seq;
        }

        readonly List<Entry> _items = new List<Entry>(256);
        readonly List<Entry> _pendingAdd = new List<Entry>();
        readonly HashSet<ISimTickable> _pendingRemove = new HashSet<ISimTickable>();
        long _seq;
        bool _ticking;
        float _acc;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        /// <summary>Returns the loop in the scene, creating one if needed.</summary>
        public static SimLoop Ensure()
        {
            if (Instance != null) return Instance; // Unity null-check also rejects destroyed instances
            var existing = FindAnyObjectByType<SimLoop>();
            // Awake does not run for components added in Edit Mode, so always assign explicitly.
            Instance = existing != null ? existing : new GameObject("SimLoop").AddComponent<SimLoop>();
            return Instance;
        }

        public static void Register(ISimTickable t)
        {
            var loop = Ensure();
            loop._pendingRemove.Remove(t);
            loop._pendingAdd.Add(new Entry { Item = t, Order = t.TickOrder, Seq = loop._seq++ });
            if (!loop._ticking) loop.MergePending();
        }

        /// <summary>Is this tickable registered with the current loop (or about to be)?</summary>
        public static bool Contains(ISimTickable t)
        {
            if (Instance == null) return false;
            foreach (var e in Instance._items) if (ReferenceEquals(e.Item, t)) return !Instance._pendingRemove.Contains(t);
            foreach (var e in Instance._pendingAdd) if (ReferenceEquals(e.Item, t)) return true;
            return false;
        }

        public static void Unregister(ISimTickable t)
        {
            if (Instance == null) return;
            Instance._pendingRemove.Add(t);
            Instance._pendingAdd.RemoveAll(e => ReferenceEquals(e.Item, t));
            if (!Instance._ticking) Instance.MergePending();
        }

        void MergePending()
        {
            if (_pendingRemove.Count > 0)
            {
                _items.RemoveAll(e => _pendingRemove.Contains(e.Item));
                _pendingRemove.Clear();
            }
            if (_pendingAdd.Count > 0)
            {
                _items.AddRange(_pendingAdd);
                _pendingAdd.Clear();
                _items.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Seq.CompareTo(b.Seq));
            }
        }

        void Update()
        {
            if (Paused)
            {
                return;
            }
            if (FastTicksPerFrame > 0)
            {
                for (int i = 0; i < FastTicksPerFrame && !Paused; i++) Step();
                Alpha = 1f;
                return;
            }
            _acc += Mathf.Min(Time.unscaledDeltaTime * TimeScale, MaxCatchUp);
            while (_acc >= Dt && !Paused)
            {
                Step();
                _acc -= Dt;
            }
            Alpha = Mathf.Clamp01(_acc / Dt);
        }

        /// <summary>Advance one fixed step. Public so tests and the harness can drive the clock directly.</summary>
        public void Step()
        {
            // Physics queries (motors, sight lines, bolts) must see this tick's transforms regardless of whether a
            // physics step happened to run this frame — otherwise results depend on frame timing (non-deterministic).
            Physics.SyncTransforms();
            MergePending();
            BeforeTick?.Invoke();
            _ticking = true;
            try
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    var e = _items[i];
                    if (_pendingRemove.Contains(e.Item)) continue;
                    if (e.Item is UnityEngine.Object uo && uo == null) continue; // destroyed
                    e.Item.SimTick(Dt);
                }
            }
            finally
            {
                _ticking = false;
            }
            TickIndex++;
            MergePending();
            AfterTick?.Invoke(TickIndex);
        }

        /// <summary>Run n ticks immediately (tests/harness).</summary>
        public void StepMany(int n)
        {
            for (int i = 0; i < n; i++) Step();
        }

        public int RegisteredCount => _items.Count + _pendingAdd.Count;
    }
}
