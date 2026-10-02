using System;
using System.Collections.Generic;
using HS.Core;
using UnityEngine;

namespace HS.Hero
{
    /// <summary>GDD §4.2 wound table.</summary>
    public enum WoundType { SprainedAnkle, CrackedRibs, SwordArmStrain, Fever, Concussion }

    /// <summary>
    /// Persistent wounds and their maluses (GDD §4.2): sprained ankle −15% speed, cracked ribs −20% max HP, sword-arm strain
    /// −20% damage, fever drains HP until treated, concussion delays rule reactions +0.5 s; 3+ wounds = Crippled (−30%
    /// speed). The type is deterministic from the damage kind. Minor wounds (ankle, arm) can be bandaged; serious ones
    /// (ribs, fever, concussion) need Splint &amp; Stitch or the camp.
    /// </summary>
    public sealed class WoundSet
    {
        readonly List<WoundType> _wounds = new List<WoundType>();
        public IReadOnlyList<WoundType> All => _wounds;
        public int Count => _wounds.Count;
        public event Action<WoundType> Added;
        public event Action<WoundType> Removed;

        public static WoundType TypeFor(DamageKind kind)
        {
            switch (kind)
            {
                case DamageKind.Heavy: return WoundType.CrackedRibs;     // smashed
                case DamageKind.Melee: return WoundType.Concussion;      // clubbed
                case DamageKind.Ranged: return WoundType.Fever;          // a dirty arrowhead festers
                case DamageKind.Trap:
                case DamageKind.Environment: return WoundType.SprainedAnkle;
                default: return WoundType.SwordArmStrain;                // blades, knives: a cut to the sword arm
            }
        }

        public static bool IsMinor(WoundType t) => t == WoundType.SprainedAnkle || t == WoundType.SwordArmStrain;

        public int CountOf(WoundType t)
        {
            int n = 0;
            for (int i = 0; i < _wounds.Count; i++) if (_wounds[i] == t) n++;
            return n;
        }

        public int SeriousCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _wounds.Count; i++) if (!IsMinor(_wounds[i])) n++;
                return n;
            }
        }

        public bool HasMinor
        {
            get
            {
                for (int i = 0; i < _wounds.Count; i++) if (IsMinor(_wounds[i])) return true;
                return false;
            }
        }

        public bool Crippled(WoundTuning t) => _wounds.Count >= t.crippledCount;

        public void Add(WoundType w)
        {
            _wounds.Add(w);
            Added?.Invoke(w);
        }

        /// <summary>Bandage: treat the oldest minor wound.</summary>
        public bool TreatMinor()
        {
            for (int i = 0; i < _wounds.Count; i++)
                if (IsMinor(_wounds[i])) return RemoveAt(i);
            return false;
        }

        /// <summary>Camp rest: the worst wound first (serious, oldest), else the oldest minor one.</summary>
        public bool TreatWorst()
        {
            for (int i = 0; i < _wounds.Count; i++)
                if (!IsMinor(_wounds[i])) return RemoveAt(i);
            return _wounds.Count > 0 && RemoveAt(0);
        }

        bool RemoveAt(int i)
        {
            var w = _wounds[i];
            _wounds.RemoveAt(i);
            Removed?.Invoke(w);
            return true;
        }

        public void Clear()
        {
            while (_wounds.Count > 0) RemoveAt(_wounds.Count - 1);
        }

        public float SpeedMul(WoundTuning t) => Mathf.Pow(t.sprainSpeedMul, CountOf(WoundType.SprainedAnkle)) * (Crippled(t) ? t.crippledSpeedMul : 1f);
        public float MaxHpMul(WoundTuning t) => Mathf.Pow(t.ribsMaxHpMul, CountOf(WoundType.CrackedRibs));
        public float DamageMul(WoundTuning t) => Mathf.Pow(t.armDamageMul, CountOf(WoundType.SwordArmStrain));
        public float ReactionDelay(WoundTuning t) => CountOf(WoundType.Concussion) > 0 ? t.concussionDelay : 0f;
        /// <summary>Fever drain as a fraction of max HP per second (scales with the chapter's HP).</summary>
        public float FeverDrainFraction(WoundTuning t) => CountOf(WoundType.Fever) > 0 ? t.feverDrainFraction : 0f;

        public List<WoundType> Snapshot() => new List<WoundType>(_wounds);

        public void Restore(List<WoundType> s)
        {
            _wounds.Clear();
            _wounds.AddRange(s);
        }
    }
}
