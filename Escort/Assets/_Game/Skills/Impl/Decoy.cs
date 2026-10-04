using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using UnityEngine;

namespace HS.Skills.Impl
{
    /// <summary>
    /// Bait &amp; Switch's decoy (GDD §5): a stuffed cloak in her shape, left standing. Enemies within its radius go for it
    /// instead for as long as it lasts — except a man squared up with the hero (his duel is his). It soaks whatever is
    /// thrown at it; the Mirror counts it as two more blades on its back (Plan 5).
    /// </summary>
    public sealed class Decoy : Agent
    {
        public override Faction Faction => Faction.Sidekick;
        public override int TickOrder => TickOrders.Skills - 5;
        public override bool Immovable => true;

        public float Radius = 8f;
        public float Until;

        public static readonly List<Decoy> All = new List<Decoy>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        float Now => Ctx != null ? Ctx.SimTime : 0f;
        public bool Live => isActiveAndEnabled && Now < Until;

        protected override void Awake()
        {
            base.Awake();
            Health = new Health(1e6f); // a sack of straw: nothing to win by hitting it
            if (Presenter == null) Presenter = GetComponentInChildren<IAgentPresenter>();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!All.Contains(this)) All.Add(this);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            All.Remove(this);
        }

        /// <summary>The live decoy that draws this enemy, if any (nearest within its radius).</summary>
        public static Decoy LureFor(EnemyAgent e)
        {
            Decoy best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                var d = All[i];
                if (d == null || !d.Live) continue;
                float dist = Geo.FlatDistance(d.Position, e.Position);
                if (dist <= d.Radius && dist < bestD)
                {
                    bestD = dist;
                    best = d;
                }
            }
            return best;
        }

        /// <summary>Live decoys within <paramref name="range"/> of a point (the Mirror's count of engagers).</summary>
        public static int CountNear(Vector3 p, float range)
        {
            int n = 0;
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null && All[i].Live && Geo.FlatDistance(All[i].Position, p) <= range) n++;
            return n;
        }

        protected override void OnSimTick(float dt)
        {
            Motor.Move(Vector3.zero, 100f, dt);
            Presenter?.SetLocomotion(0f, false);
            if (Now >= Until)
            {
                HS.Presentation.Vfx.Burst(HS.Presentation.VfxKind.Dust, Position + Vector3.up * 0.8f, 0.9f);
                Destroy(gameObject);
            }
        }

        protected override void OnHurt(DamageInfo d, float applied)
        {
            if (applied > 0f) HS.Presentation.Vfx.Burst(HS.Presentation.VfxKind.Dust, Position + Vector3.up * 1.0f, 0.5f);
        }

        /// <summary>Stand one up: her own silhouette (a copy of her visual), greyed.</summary>
        public static Decoy Plant(Agent owner, Vector3 at, float seconds, float radius, RunContext ctx)
        {
            var go = new GameObject("Decoy");
            go.SetActive(false);
            go.transform.SetPositionAndRotation(at, owner != null ? owner.transform.rotation : Quaternion.identity);
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.7f;
            cc.radius = 0.32f;
            cc.center = new Vector3(0f, 0.87f, 0f);
            if (owner != null) HS.Rooms.CastFactory.SwapVisual(go, owner.gameObject);
            var d = go.AddComponent<Decoy>();
            d.Radius = radius;
            d.Until = (ctx != null ? ctx.SimTime : 0f) + seconds;
            HS.Rooms.CastFactory.Tint(go, new Color(0.62f, 0.6f, 0.58f, 1f));
            go.SetActive(true);
            return d;
        }

        public override bool IsHostileTo(Agent other) => false;
    }
}
