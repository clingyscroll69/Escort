using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using UnityEngine;

namespace HS.QA
{
    /// <summary>Stable 64-bit FNV-1a hash of the simulation state (determinism tests): every agent, sorted by id.</summary>
    public static class StateHash
    {
        public static ulong Compute()
        {
            ulong h = 1469598103934665603UL;
            void Mix(long v)
            {
                for (int i = 0; i < 8; i++)
                {
                    h ^= (byte)(v >> (i * 8));
                    h *= 1099511628211UL;
                }
            }
            var agents = new List<Agent>(AgentRegistry.All);
            agents.Sort((a, b) => string.CompareOrdinal(a.AgentId, b.AgentId));
            foreach (var a in agents)
            {
                if (a == null) continue;
                Mix(DetRandom.HashString(a.AgentId));
                var p = a.Position;
                Mix(Mathf.RoundToInt(p.x * 100f));
                Mix(Mathf.RoundToInt(p.y * 100f));
                Mix(Mathf.RoundToInt(p.z * 100f));
                Mix(Mathf.RoundToInt(a.Health.Current * 10f));
                if (a is EnemyAgent e) Mix((int)e.State);
            }
            var ctx = RunContext.Current;
            var l = ctx != null ? ctx.Get<HS.Rapport.RapportLedger>() : null;
            if (l != null)
            {
                Mix(Mathf.RoundToInt(l.Offered * 100f));
                Mix(Mathf.RoundToInt(l.Earned * 100f));
                Mix(Mathf.RoundToInt(l.EffectivePenalties * 100f));
            }
            if (SimLoop.Instance != null) Mix(SimLoop.Instance.TickIndex);
            return h;
        }
    }
}
