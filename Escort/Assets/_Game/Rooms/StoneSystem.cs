using System.Collections.Generic;
using HS.Core;
using HS.Hero;
using HS.Hero.Callum;
using UnityEngine;

namespace HS.Rooms
{
    /// <summary>What wakes a hero's stones and what they record (GDD: "each hero treats them differently").</summary>
    public interface IStonePolicy
    {
        void Bind(StoneSystem system, HeroAgent hero);
        void Unbind();
        void Tick(float dt);
    }

    /// <summary>
    /// Runs the chronicle stones for the current hero: wakes them per the hero's policy, answers "did an active stone see
    /// this?" for the witness checks, collects flaw clips for <see cref="CuratorIntel"/> and relays them at the campfire.
    /// </summary>
    public sealed class StoneSystem : MonoBehaviour, ISimTickable
    {
        public int TickOrder => TickOrders.Judges - 20;
        public CuratorIntel Intel { get; private set; }
        public IStonePolicy Policy { get; private set; }
        RunContext _ctx;

        public static StoneSystem Create(RunContext ctx, HeroAgent hero)
        {
            var s = new GameObject("StoneSystem").AddComponent<StoneSystem>();
            s.Bind(ctx, hero);
            return s;
        }

        void OnEnable() => SimLoop.Register(this);

        void OnDisable()
        {
            SimLoop.Unregister(this);
            Policy?.Unbind();
            Policy = null;
        }

        public void Bind(RunContext ctx, HeroAgent hero)
        {
            _ctx = ctx;
            Intel = ctx.Get<CuratorIntel>();
            if (Intel == null)
            {
                Intel = new CuratorIntel();
                ctx.Register(Intel);
            }
            ctx.Register(this);
            Policy?.Unbind();
            Policy = hero != null && hero.Module is CallumModule ? new CallumStonePolicy() : null;
            Policy?.Bind(this, hero);
        }

        public static bool AnyActiveSees(Vector3 point)
        {
            var all = ChronicleStone.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Sees(point)) return true;
            return false;
        }

        public int PendingClips
        {
            get
            {
                int n = 0;
                foreach (var s in ChronicleStone.All) if (s != null && s.State != ChronicleStone.StoneState.Broken) n += s.PendingClips;
                return n;
            }
        }

        public int IntelLevel => Intel.Level(PendingClips);

        public void Record(ChronicleStone stone, string flaw)
        {
            stone.PendingClips++; // no UI text: the hidden stat is only ever felt
            Recorded?.Invoke(stone, flaw);
        }

        public event System.Action<ChronicleStone, string> Recorded;

        /// <summary>Chapter end (campfire): every unbroken stone relays what it holds.</summary>
        public void RelayAll()
        {
            foreach (var s in ChronicleStone.All)
            {
                if (s == null || s.State == ChronicleStone.StoneState.Broken) continue;
                Intel.Relay(s.PendingClips);
                s.PendingClips = 0;
            }
        }

        public void SimTick(float dt) => Policy?.Tick(dt);
    }

    /// <summary>
    /// Callum (GDD §6.1): stones stream only formal duels. A stone with him in view wakes when he begins a duel and stays
    /// awake while it lasts (+2 s); an active stone counts as a witness. It records his S0 flaws for the Curator: waiting on
    /// an Unready foe, and falling for treachery — at most one clip of each per stone per duel.
    /// </summary>
    public sealed class CallumStonePolicy : IStonePolicy
    {
        public const float Linger = 2f;
        StoneSystem _sys;
        HeroAgent _hero;
        CallumModule _cm;
        readonly HashSet<(ChronicleStone, string)> _clippedThisDuel = new HashSet<(ChronicleStone, string)>();

        public void Bind(StoneSystem system, HeroAgent hero)
        {
            _sys = system;
            _hero = hero;
            _cm = hero.Module as CallumModule;
            _cm.StoneWitness = StoneSystem.AnyActiveSees;
            _cm.DuelBegan += OnDuelBegan;
            hero.Brain.RuleChanged += OnRuleChanged;
            var ctx = RunContext.Current;
            if (ctx != null) ctx.Events.Damage += OnDamage;
        }

        public void Unbind()
        {
            if (_cm == null) return;
            _cm.StoneWitness = null;
            _cm.DuelBegan -= OnDuelBegan;
            _hero.Brain.RuleChanged -= OnRuleChanged;
            var ctx = RunContext.Current;
            if (ctx != null) ctx.Events.Damage -= OnDamage;
            _cm = null;
        }

        void OnDuelBegan(HS.Enemies.EnemyAgent target)
        {
            _clippedThisDuel.Clear();
            foreach (var s in ChronicleStone.All)
                if (s != null && s.InView(_hero.Position)) s.Wake(Linger);
        }

        public void Tick(float dt)
        {
            if (_cm == null || _cm.Challenged == null || !_hero.IsAlive) return;
            foreach (var s in ChronicleStone.All)
                if (s != null && s.State == ChronicleStone.StoneState.Active && s.InView(_hero.Position)) s.Wake(Linger);
        }

        void OnRuleChanged(IHeroRule prev, IHeroRule next)
        {
            if (next != null && next.Id == "callum_wait_unready") Clip("etiquette");
        }

        void OnDamage(DamageInfo d, float applied)
        {
            if (d.Target == _hero && (d.Tag == "cheap_shot" || d.Tag == "ambush")) Clip("treachery");
        }

        void Clip(string flaw)
        {
            foreach (var s in ChronicleStone.All)
            {
                if (s == null || !s.Sees(_hero.Position)) continue;
                if (_clippedThisDuel.Add((s, flaw))) _sys.Record(s, flaw);
            }
        }
    }
}
