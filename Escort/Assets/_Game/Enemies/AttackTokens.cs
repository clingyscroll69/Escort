using System.Collections.Generic;
using HS.Core;
using UnityEngine;

namespace HS.Enemies
{
    /// <summary>
    /// Melee attack tokens: only <see cref="Agent.MeleeSlots"/> bandits may swing at one victim at a time; the rest
    /// circle at menace range and wait their turn. Keeps crowd fights readable and the S0 hero soloable at the Ch1 target
    /// (GDD §3: "Hero can solo ~70%"). Deterministic: acquisition follows the fixed sim tick order.
    /// </summary>
    public static class AttackTokens
    {
        struct Hold
        {
            public Agent Victim;
            public EnemyAgent Holder;
        }

        static readonly List<Hold> _held = new List<Hold>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _held.Clear();

        public static bool TryAcquire(EnemyAgent e, Agent victim)
        {
            Prune();
            int count = 0;
            for (int i = 0; i < _held.Count; i++)
            {
                if (_held[i].Holder == e && _held[i].Victim == victim) return true;
                if (_held[i].Victim == victim) count++;
            }
            Release(e);
            if (count >= Mathf.Max(1, victim.MeleeSlots)) return false;
            _held.Add(new Hold { Victim = victim, Holder = e });
            return true;
        }

        public static bool Holds(EnemyAgent e, Agent victim)
        {
            for (int i = 0; i < _held.Count; i++)
                if (_held[i].Holder == e && _held[i].Victim == victim) return true;
            return false;
        }

        public static void Release(EnemyAgent e) => _held.RemoveAll(h => h.Holder == e);

        public static int HeldOn(Agent victim)
        {
            Prune();
            int n = 0;
            for (int i = 0; i < _held.Count; i++)
                if (_held[i].Victim == victim) n++;
            return n;
        }

        static void Prune() => _held.RemoveAll(h => h.Holder == null || h.Victim == null || !h.Holder.IsAlive || !h.Victim.IsAlive
                                                   || h.Holder.State != EnemyState.Engaged || h.Holder.Target != h.Victim);
    }
}
