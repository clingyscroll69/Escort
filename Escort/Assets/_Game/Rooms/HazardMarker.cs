using HS.Core;
using HS.Enemies;
using HS.Presentation;
using HS.Sidekick;
using UnityEngine;

namespace HS.Rooms
{
    public enum HazardKind { SpikePlate, Tripwire }

    /// <summary>
    /// Environmental hazard (trap corridor, GDD §3 Ch1 "traps"). The loud hero walks his route straight over them; the
    /// unlisted sidekick can see them and disarm them (Interact, 1.2 s). Spike plate: 18% of the victim's max HP.
    /// Tripwire: trips whoever crosses it (1.2 s down, 8%) and rings the alarm for the bandits nearby. A hazard always
    /// wounds the hero (GDD §4.2) — that is its real cost, so the damage scales with his per-chapter HP instead of
    /// being a flat death trap. Bandits know their own traps; a careful (walking or crouching) sidekick steps over them.
    /// </summary>
    public sealed class HazardMarker : MonoBehaviour, ISimTickable, IInteractable, IDynamicVisual
    {
        public enum HazardState { Armed, Sprung, Disarmed }

        public HazardKind Kind = HazardKind.SpikePlate;
        public float Radius = 1.1f;
        [Tooltip("Tripwire length along local X.")]
        public float Span = 4f;
        [Tooltip("Fraction of the victim's max HP.")]
        public float SpikeDamageFraction = 0.18f;
        public float TripwireDamageFraction = 0.08f;
        public float AlarmRadius = 24f;

        public HazardState State { get; private set; } = HazardState.Armed;
        public int TickOrder => TickOrders.Physics;
        readonly System.Collections.Generic.Dictionary<Agent, float> _side = new System.Collections.Generic.Dictionary<Agent, float>();

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            SimLoop.Register(this);
            Interactables.Register(this);
        }

        void OnDisable()
        {
            if (!Application.isPlaying) return;
            SimLoop.Unregister(this);
            Interactables.Unregister(this);
        }

        // ------------------------------------------------------------------ IInteractable (disarm)
        public Vector3 InteractPosition => transform.position;
        public string Prompt => "Disarm";
        public float InteractDuration => 1.2f;
        public bool CanInteract(Agent who) => State == HazardState.Armed && who is SidekickAgent;

        public void Interact(Agent who)
        {
            if (State != HazardState.Armed) return;
            State = HazardState.Disarmed;
            SetVisualSpent(true);
            Vfx.Burst(VfxKind.Glint, transform.position + Vector3.up * 0.4f, 0.6f);
            RunContext.Current?.Events.RaiseThought(Kind == HazardKind.Tripwire ? "Wire's cut." : "Spikes jammed.");
            RunContext.Current?.Events.RaiseSkillUsed("disarm", who);
        }

        // ------------------------------------------------------------------ trigger
        public void SimTick(float dt)
        {
            if (State != HazardState.Armed) return;
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || !a.IsAlive || a is EnemyAgent) continue;
                bool isSidekick = a is SidekickAgent;
                if (isSidekick && Careful((SidekickAgent)a))
                {
                    Remember(a);
                    continue;
                }
                // The sidekick sees the plate; only a careless run straight across its middle springs it.
                float r = isSidekick ? Radius * 0.6f : Radius;
                if (Kind == HazardKind.SpikePlate ? Geo.FlatDistance(a.Position, transform.position) <= r : CrossedWire(a))
                {
                    Spring(a);
                    return;
                }
            }
        }

        /// <summary>Walking, crouching or standing still: she steps over it.</summary>
        static bool Careful(SidekickAgent sk) => sk.Crouched || sk.LastCommand.Walk || sk.Motor.Speed < 2.4f;

        float LocalZ(Agent a) => transform.InverseTransformPoint(a.Position).z;

        void Remember(Agent a)
        {
            if (Kind == HazardKind.Tripwire) _side[a] = LocalZ(a);
        }

        bool CrossedWire(Agent a)
        {
            var local = transform.InverseTransformPoint(a.Position);
            bool within = Mathf.Abs(local.x) <= Span * 0.5f;
            bool crossed = _side.TryGetValue(a, out var prev) && within && Mathf.Sign(prev) != Mathf.Sign(local.z) && Mathf.Abs(prev - local.z) < 2f;
            _side[a] = local.z;
            return crossed;
        }

        void Spring(Agent victim)
        {
            State = HazardState.Sprung;
            var ctx = RunContext.Current;
            float max = victim.Health.Max;
            if (Kind == HazardKind.SpikePlate)
            {
                victim.TakeDamage(DamageInfo.Make(null, victim, Mathf.Round(max * SpikeDamageFraction), DamageKind.Trap, "spike_plate", 0.5f));
                Vfx.Burst(VfxKind.Sparks, transform.position + Vector3.up * 0.3f);
            }
            else
            {
                victim.TakeDamage(DamageInfo.Make(null, victim, Mathf.Round(max * TripwireDamageFraction), DamageKind.Trap, "tripwire", 1.2f));
                Vfx.Burst(VfxKind.Dust, victim.Position + Vector3.up * 0.2f);
                // The wire rings a bell: bandits nearby come running.
                var all = AgentRegistry.All;
                for (int i = 0; i < all.Count; i++)
                    if (all[i] is EnemyAgent e && e.State == EnemyState.Dormant && Geo.FlatDistance(e.Position, transform.position) <= AlarmRadius)
                        e.Activate(0.3f);
                ctx?.Events.RaiseNotice("A bell rings somewhere ahead.");
            }
            SetVisualSpent(true);
            ctx?.Events.HazardSprung?.Invoke(transform.position, Kind.ToString(), victim);
        }

        void SetVisualSpent(bool spent)
        {
            // Sprung or jammed spikes sink; a cut wire drops.
            for (int i = 0; i < transform.childCount; i++)
            {
                var c = transform.GetChild(i);
                c.localScale = new Vector3(c.localScale.x, spent ? c.localScale.y * 0.35f : c.localScale.y, c.localScale.z);
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.magenta;
            if (Kind == HazardKind.Tripwire) Gizmos.DrawLine(transform.position - transform.right * Span * 0.5f, transform.position + transform.right * Span * 0.5f);
            else Gizmos.DrawWireSphere(transform.position, Radius);
        }
    }
}
