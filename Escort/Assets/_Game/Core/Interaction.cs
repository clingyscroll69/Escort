using System.Collections.Generic;
using UnityEngine;

namespace HS.Core
{
    /// <summary>Something the sidekick can use with the Interact verb (arm a prop, break a stone, pick up...).</summary>
    public interface IInteractable
    {
        Vector3 InteractPosition { get; }
        /// <summary>Short verb shown in the prompt, e.g. "Loosen bolt", "Break stone".</summary>
        string Prompt { get; }
        /// <summary>Seconds of channel; 0 = instant.</summary>
        float InteractDuration { get; }
        bool CanInteract(Agent who);
        void Interact(Agent who);
    }

    /// <summary>
    /// Marks a component whose child renderers move or change at runtime (falling props, sprung traps, stones).
    /// The chapter builder never folds these into a static batch.
    /// </summary>
    public interface IDynamicVisual { }

    public static class Interactables
    {
        static readonly List<IInteractable> _all = new List<IInteractable>();
        public static IReadOnlyList<IInteractable> All => _all;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => _all.Clear();

        public static void Register(IInteractable i)
        {
            if (!_all.Contains(i)) _all.Add(i);
        }

        public static void Unregister(IInteractable i) => _all.Remove(i);

        public static IInteractable Nearest(Agent who, float range)
        {
            IInteractable best = null;
            float bestD = range * range;
            for (int k = 0; k < _all.Count; k++)
            {
                var i = _all[k];
                if (i is Object uo && uo == null) continue;
                if (!i.CanInteract(who)) continue;
                if (!Melee.CanReach(who, i.InteractPosition)) continue; // a wheel on the walk above is jammed from up there
                float d = Geo.FlatSqrDistance(who.Position, i.InteractPosition);
                if (d <= bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }
    }

    /// <summary>Charge-based dodge (GDD 4.1: 3 charges, 0.3 s invulnerability). Charges refill one at a time.</summary>
    public sealed class DodgeCharges
    {
        public int Max { get; }
        public int Charges { get; private set; }
        public float RechargeTime { get; }
        float _t;

        public DodgeCharges(int max, float rechargeTime)
        {
            Max = max;
            Charges = max;
            RechargeTime = rechargeTime;
        }

        public float RechargeProgress => Charges >= Max ? 1f : _t / RechargeTime;

        public bool TryConsume()
        {
            if (Charges <= 0) return false;
            Charges--;
            return true;
        }

        public void Tick(float dt)
        {
            if (Charges >= Max)
            {
                _t = 0f;
                return;
            }
            _t += dt;
            if (_t >= RechargeTime)
            {
                _t -= RechargeTime;
                Charges++;
            }
        }
    }

    /// <summary>Deterministic melee arc test against the agent registry (no physics, no rolls).</summary>
    public static class Melee
    {
        /// <summary>How far a hand reaches above the head (or a blade below the feet).</summary>
        public const float ArmReach = 0.3f;

        /// <summary>
        /// Can these two touch? One's reach (head to feet, plus an arm's length either way) must overlap the other's body.
        /// Blows, shoves, bumping into someone and anything done by hand (<see cref="CanReach"/>) need it; shots and throws
        /// don't. A man on a 2.2 m perch is out of reach from the floor; a stone on a 1 m plinth is not.
        /// </summary>
        public static bool CanTouch(Agent a, Agent b)
        {
            BodySpan(a, out float a0, out float a1);
            BodySpan(b, out float b0, out float b1);
            return a0 - ArmReach <= b1 && b0 <= a1 + ArmReach;
        }

        /// <summary>Can this body put a hand on that point (its height within head to feet, plus an arm's length)?</summary>
        public static bool CanReach(Agent who, Vector3 point)
        {
            BodySpan(who, out float bottom, out float top);
            return point.y >= bottom - ArmReach && point.y <= top + ArmReach;
        }

        /// <summary>Bottom and top of a body in world height (its CharacterController; 1.8 m from the feet without one).</summary>
        public static void BodySpan(Agent x, out float bottom, out float top)
        {
            var cc = x.Controller;
            float s = x.transform.lossyScale.y;
            float h = cc != null ? cc.height * s : 1.8f;
            bottom = cc != null ? x.Position.y + cc.center.y * s - h * 0.5f : x.Position.y;
            top = bottom + h;
        }

        public static void Sweep(Agent attacker, float range, float arcDeg, List<Agent> results, System.Func<Agent, bool> filter)
        {
            results.Clear();
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || a == attacker || !a.IsAlive) continue;
                if (filter != null && !filter(a)) continue;
                float reach = range + a.Radius;
                if (Geo.FlatDistance(attacker.Position, a.Position) > reach) continue;
                if (!CanTouch(attacker, a)) continue; // nobody on a perch is knifed from below
                if (Geo.AngleTo(attacker.Position, attacker.Forward, a.Position) > arcDeg * 0.5f) continue;
                results.Add(a);
            }
        }
    }
}
