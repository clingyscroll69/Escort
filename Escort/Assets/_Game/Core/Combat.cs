using System;
using UnityEngine;

namespace HS.Core
{
    public enum Faction { Hero, Sidekick, Hostile, Neutral }

    /// <summary>How the damage was dealt; drives wound selection (deterministic, GDD §4.2) and witness logic.</summary>
    public enum DamageKind { Melee, Heavy, Ranged, Trap, Environment, Knife, Fever, Blade }

    public struct DamageInfo
    {
        public Agent Source;
        public Agent Target;
        public float Amount;
        public DamageKind Kind;
        /// <summary>Seconds of stagger applied to the target (0 = none).</summary>
        public float Stagger;
        /// <summary>Short machine tag for the attack/skill ("sword", "knife", "loosen_bolt", "crossbow", "volley"...).</summary>
        public string Tag;
        public Vector3 Point;

        public bool FromSidekick => Source != null && Source.Faction == Faction.Sidekick;

        public static DamageInfo Make(Agent src, Agent dst, float amount, DamageKind kind, string tag, float stagger = 0f)
        {
            return new DamageInfo
            {
                Source = src,
                Target = dst,
                Amount = amount,
                Kind = kind,
                Tag = tag,
                Stagger = stagger,
                Point = dst != null ? dst.Position : Vector3.zero,
            };
        }
    }

    /// <summary>Plain hit-point pool. Deterministic: no crits, no rolls.</summary>
    public sealed class Health
    {
        public float BaseMax { get; private set; }
        /// <summary>Multiplier from wounds (Cracked ribs −20%).</summary>
        public float MaxMultiplier { get; private set; } = 1f;
        public float Max => BaseMax * MaxMultiplier;
        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;
        public float Fraction => Max <= 0f ? 0f : Current / Max;
        public bool Invulnerable;

        public Action<DamageInfo, float> Damaged;
        public Action<DamageInfo> Died;
        public Action<float> Healed;

        public Health(float max)
        {
            BaseMax = max;
            Current = max;
        }

        public void SetBaseMax(float max, bool refill)
        {
            BaseMax = max;
            Current = refill ? Max : Mathf.Min(Current, Max);
        }

        public void SetMaxMultiplier(float m)
        {
            MaxMultiplier = m;
            Current = Mathf.Min(Current, Max);
        }

        /// <summary>Returns the damage actually applied.</summary>
        public float ApplyDamage(DamageInfo d)
        {
            if (IsDead || Invulnerable || d.Amount <= 0f) return 0f;
            float applied = Mathf.Min(Current, d.Amount);
            Current -= applied;
            Damaged?.Invoke(d, applied);
            if (Current <= 0f)
            {
                Current = 0f;
                Died?.Invoke(d);
            }
            return applied;
        }

        public float Heal(float amount)
        {
            if (IsDead || amount <= 0f) return 0f;
            float before = Current;
            Current = Mathf.Min(Max, Current + amount);
            float healed = Current - before;
            if (healed > 0f) Healed?.Invoke(healed);
            return healed;
        }

        /// <summary>Used by revive/recall/restore. Bypasses IsDead.</summary>
        public void SetCurrent(float value) => Current = Mathf.Clamp(value, 0f, Max);
    }

    public enum StatusType
    {
        Blinded,
        Staggered,
        Surrendered,
        Fleeing,
        Sleeping,
        Hidden,
        Slowed,
        Stunned,
        Downed,
        Count
    }

    /// <summary>Timed status flags. A duration of float.PositiveInfinity means "until cleared".</summary>
    public sealed class StatusSet
    {
        readonly float[] _timers = new float[(int)StatusType.Count];
        readonly Agent[] _sources = new Agent[(int)StatusType.Count];

        public bool Has(StatusType s) => _timers[(int)s] > 0f;
        public float Remaining(StatusType s) => _timers[(int)s];
        /// <summary>Who applied the longest-running instance (null = environment/unknown).</summary>
        public Agent SourceOf(StatusType s) => _sources[(int)s];

        public void Apply(StatusType s, float duration, Agent source = null)
        {
            int i = (int)s;
            if (duration >= _timers[i]) _sources[i] = source;
            _timers[i] = Mathf.Max(_timers[i], duration);
        }

        public void Clear(StatusType s)
        {
            _timers[(int)s] = 0f;
            _sources[(int)s] = null;
        }

        public void ClearAll()
        {
            for (int i = 0; i < _timers.Length; i++)
            {
                _timers[i] = 0f;
                _sources[i] = null;
            }
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < _timers.Length; i++)
            {
                if (_timers[i] > 0f && !float.IsPositiveInfinity(_timers[i]))
                    _timers[i] = Mathf.Max(0f, _timers[i] - dt);
            }
        }

        /// <summary>Can't act (attack, move with intent).</summary>
        public bool Incapacitated => Has(StatusType.Staggered) || Has(StatusType.Stunned) || Has(StatusType.Sleeping) || Has(StatusType.Downed);
    }

    /// <summary>Shared geometry helpers (flat XZ plane).</summary>
    public static class Geo
    {
        public static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static float FlatSqrDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>Unsigned angle between forward and the direction to target, on the XZ plane.</summary>
        public static float AngleTo(Vector3 from, Vector3 forward, Vector3 target)
        {
            var dir = Flat(target - from);
            if (dir.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.Angle(Flat(forward), dir);
        }

        public static bool InCone(Vector3 from, Vector3 forward, Vector3 target, float fullAngleDeg, float range)
        {
            return FlatDistance(from, target) <= range && AngleTo(from, forward, target) <= fullAngleDeg * 0.5f;
        }

        public static Vector3 DirTo(Vector3 from, Vector3 to)
        {
            var d = Flat(to - from);
            return d.sqrMagnitude < 1e-8f ? Vector3.zero : d.normalized;
        }
    }
}
