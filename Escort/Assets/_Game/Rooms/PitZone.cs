using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Sidekick;
using UnityEngine;

namespace HS.Rooms
{
    /// <summary>
    /// The drop either side of the bone bridge (Catacombs). Whoever ends up over it teeters for a second — Pull Back can
    /// still haul him clear — then falls: the hero takes a serious wound (cracked ribs) and climbs back onto the bridge
    /// 2.5 s later; the sidekick likewise, without the wound; an enemy is gone for good. A box in local XZ (Size); the
    /// bridge's centre line is local x = 0, so climbing back means x = 0 at the same z.
    /// </summary>
    public sealed class PitZone : MonoBehaviour, ISimTickable
    {
        public Vector2 Size = new Vector2(8f, 20f);
        [Tooltip("Where survivors climb back up, in this zone's local X (the bridge's centre line).")]
        public float ClimbBackX;
        public const float Teeter = 1f, ClimbBack = 2.5f, FallDamageFraction = 0.1f;
        public int TickOrder => TickOrders.Physics + 8;

        readonly Dictionary<Agent, float> _teetering = new Dictionary<Agent, float>();
        readonly Dictionary<Agent, float> _climbing = new Dictionary<Agent, float>();
        readonly List<Agent> _scratch = new List<Agent>();
        public int Falls { get; private set; }

        void OnEnable()
        {
            if (Application.isPlaying) SimLoop.Register(this);
        }

        void OnDisable()
        {
            if (Application.isPlaying) SimLoop.Unregister(this);
        }

        public bool Contains(Vector3 p)
        {
            var l = transform.InverseTransformPoint(p);
            return Mathf.Abs(l.x) <= Size.x * 0.5f && Mathf.Abs(l.z) <= Size.y * 0.5f;
        }

        public void SimTick(float dt)
        {
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || !a.IsAlive || _climbing.ContainsKey(a)) continue;
                if (a is SidekickAgent sk && sk.IsDowned) continue;
                bool over = Contains(a.Position);
                if (!over)
                {
                    _teetering.Remove(a);
                    continue;
                }
                if (!_teetering.ContainsKey(a))
                {
                    _teetering[a] = 0f;
                    a.Status.Apply(StatusType.Stunned, Teeter, null);
                    if (a is HeroAgent h && h.Module is HS.Hero.Callum.CallumModule cm) cm.Bark(TeeterLines, 2);
                    continue;
                }
                _teetering[a] += dt;
                if (_teetering[a] >= Teeter) Fall(a);
            }
            _scratch.Clear();
            foreach (var kv in _climbing) _scratch.Add(kv.Key);
            foreach (var a in _scratch)
            {
                _climbing[a] -= dt;
                if (_climbing[a] > 0f || a == null) continue;
                _climbing.Remove(a);
                if (!a.IsAlive) continue;
                var l = transform.InverseTransformPoint(a.Position);
                a.Motor.Teleport(transform.TransformPoint(new Vector3(ClimbBackX, 0.05f, Mathf.Clamp(l.z, -Size.y * 0.5f, Size.y * 0.5f))));
                a.Presenter?.PlayAction("getup");
            }
        }

        void Fall(Agent a)
        {
            _teetering.Remove(a);
            Falls++;
            if (a is EnemyAgent e)
            {
                e.TakeDamage(DamageInfo.Make(null, e, e.Health.Max * 10f, DamageKind.Environment, "fall"));
                e.gameObject.SetActive(false);
                return;
            }
            a.TakeDamage(DamageInfo.Make(null, a, Mathf.Round(a.Health.Max * FallDamageFraction), DamageKind.Environment, "fall"));
            if (a is HeroAgent hero && hero.IsAlive) hero.Wounds.Add(WoundType.CrackedRibs);
            a.Status.Apply(StatusType.Stunned, ClimbBack, null);
            a.Presenter?.PlayAction("death");
            _climbing[a] = ClimbBack;
            RunContext.Current?.Events.RaiseNotice(a is HeroAgent ? "Callum goes over the edge." : "You go over the edge.");
        }

        static readonly string[] TeeterLines = { "Whoa— the edge—!", "Steady— steady—" };

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.1f, 0.05f, 0.2f, 0.7f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(Size.x, 0.1f, Size.y));
        }
    }
}
