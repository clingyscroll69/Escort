using System.Collections.Generic;
using HS.Core;
using HS.Hero;
using UnityEngine;

namespace HS.Rooms
{
    /// <summary>
    /// Bog (Whisperwood's Mire Crossing): sucking mud over the path. Everyone in it wades — the armoured hero worst
    /// (×0.6), everyone else ×0.8. A box in local XZ (Size). All zones are applied together once per tick, so overlapping
    /// patches never fight over an agent.
    /// </summary>
    public sealed class BogZone : MonoBehaviour, ISimTickable
    {
        public Vector2 Size = new Vector2(6f, 5f);
        public float HeroMul = 0.6f, OtherMul = 0.8f;
        public int TickOrder => TickOrders.Director - 10;

        static readonly List<BogZone> Zones = new List<BogZone>();
        static int _lastTick = -1;
        readonly HashSet<Agent> _barked = new HashSet<Agent>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Zones.Clear();
            _lastTick = -1;
        }

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            Zones.Add(this);
            SimLoop.Register(this);
        }

        void OnDisable()
        {
            if (!Application.isPlaying) return;
            Zones.Remove(this);
            SimLoop.Unregister(this);
            if (Zones.Count == 0)
                foreach (var a in AgentRegistry.All)
                    if (a != null && a.Motor != null) a.Motor.TerrainMul = 1f;
        }

        public bool Contains(Vector3 p)
        {
            var l = transform.InverseTransformPoint(p);
            return Mathf.Abs(l.x) <= Size.x * 0.5f && Mathf.Abs(l.z) <= Size.y * 0.5f;
        }

        public void SimTick(float dt)
        {
            int tick = SimLoop.Instance != null ? SimLoop.Instance.TickIndex : 0;
            if (tick == _lastTick) return;
            _lastTick = tick;
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || a.Motor == null) continue;
                float mul = 1f;
                foreach (var z in Zones)
                    if (z.Contains(a.Position))
                    {
                        mul = Mathf.Min(mul, a is HeroAgent ? z.HeroMul : z.OtherMul);
                        if (a is HeroAgent h && z._barked.Add(a) && h.Module is HS.Hero.Callum.CallumModule cm) cm.Bark(MudLines, 1);
                    }
                a.Motor.TerrainMul = mul;
            }
        }

        static readonly string[] MudLines = { "Mud. Of course there's mud.", "This armour was not made for wading." };

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.3f, 0.1f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(Size.x, 0.1f, Size.y));
        }
    }
}
