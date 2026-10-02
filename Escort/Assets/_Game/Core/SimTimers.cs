using System;
using System.Collections.Generic;

namespace HS.Core
{
    /// <summary>Delayed actions on the deterministic clock (throw arcs, collapse delays). Stable order for equal times.</summary>
    public sealed class SimTimers : ISimTickable
    {
        public int TickOrder => TickOrders.Director;
        float _now;
        long _seq;
        readonly List<(float at, long seq, Action act)> _pending = new List<(float, long, Action)>();

        public void After(float delay, Action act) => _pending.Add((_now + Math.Max(0f, delay), _seq++, act));

        public void SimTick(float dt)
        {
            _now += dt;
            if (_pending.Count == 0) return;
            _pending.Sort((a, b) => a.at != b.at ? a.at.CompareTo(b.at) : a.seq.CompareTo(b.seq));
            while (_pending.Count > 0 && _pending[0].at <= _now)
            {
                var a = _pending[0].act;
                _pending.RemoveAt(0);
                a?.Invoke();
            }
        }

        public void Clear() => _pending.Clear();
        public int Count => _pending.Count;
    }

    /// <summary>Wound carrier (hero). Implemented by the injury system (Task 11).</summary>
    public interface IWounded
    {
        int WoundCount { get; }
        bool HasMinorWound { get; }
        bool TreatMinorWound();
    }
}
