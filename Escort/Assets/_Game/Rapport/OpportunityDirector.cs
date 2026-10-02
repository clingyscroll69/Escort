using HS.Core;
using HS.Hero;
using HS.Hero.Callum;
using UnityEngine;

namespace HS.Rapport
{
    /// <summary>A hero's Dance: which Moments exist, when they're offered, how they're captured, what costs Rapport.</summary>
    public interface IDanceJudge
    {
        void Bind(RunContext ctx, HeroAgent hero, RapportLedger ledger);
        void Unbind();
        void Tick(float dt);
        /// <summary>A new fight begins (room or boss): per-fight counters reset ("twice in the same fight").</summary>
        void NewFight();
        /// <summary>The run (or a set piece) ended: every still-open Moment closes uncaptured — it still counts as offered.</summary>
        void CloseAll(string reason);
    }

    /// <summary>
    /// Opportunity Director (GDD §4.4): owns the run's <see cref="RapportLedger"/> and runs the current hero's Dance judge
    /// every sim tick. The ledger lives in the RunContext so restore points and the campfire's Stage check can read it.
    /// </summary>
    public sealed class OpportunityDirector : MonoBehaviour, ISimTickable
    {
        public int TickOrder => TickOrders.Judges;
        public RapportLedger Ledger { get; private set; }
        public IDanceJudge Judge { get; private set; }
        RunContext _ctx;

        void OnEnable() => SimLoop.Register(this);

        void OnDisable()
        {
            SimLoop.Unregister(this);
            Unbind();
        }

        public static OpportunityDirector Create(RunContext ctx, HeroAgent hero)
        {
            var d = new GameObject("OpportunityDirector").AddComponent<OpportunityDirector>();
            d.Bind(ctx, hero);
            return d;
        }

        public void Bind(RunContext ctx, HeroAgent hero)
        {
            Unbind();
            _ctx = ctx;
            Ledger = ctx.Get<RapportLedger>();
            if (Ledger == null)
            {
                Ledger = new RapportLedger();
                ctx.Register(Ledger);
            }
            Ledger.Clock = () => ctx.SimTime;
            Ledger.Chapter = ctx.Chapter;
            ctx.Register(this);
            ctx.Events.RoomEntered += OnRoomEntered;
            Judge = hero != null && hero.Module is CallumModule ? new CallumDance() : null;
            Judge?.Bind(ctx, hero, Ledger);
        }

        void Unbind()
        {
            if (_ctx != null) _ctx.Events.RoomEntered -= OnRoomEntered;
            Judge?.Unbind();
            Judge = null;
        }

        void OnRoomEntered(int room)
        {
            Ledger.Room = room;
            Judge?.NewFight();
        }

        public void SimTick(float dt) => Judge?.Tick(dt);

        /// <summary>End of a run or set piece: close every open Moment so the Post-Mortem can name what was missed.</summary>
        public void EndOfFight(string reason) => Judge?.CloseAll(reason);
    }
}
