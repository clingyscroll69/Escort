using System;
using System.Collections.Generic;
using HS.Core;
using HS.Sidekick;
using UnityEngine;

namespace HS.Enemies
{
    /// <summary>
    /// A civilian an archer shelters behind (the Sunken Bastion's Rigged Gauntlet, campaign spec §3.2). While she is held,
    /// Callum will not raise a sword at the man behind her, and that archer never holds the room gate — he is the sidekick's
    /// problem. She goes free when the sidekick unties her (Interact, 1.2 s), when her captor dies, or when he is staggered,
    /// stunned or blinded (she bolts). Harm she takes from the sidekick is a Major dishonour if he sees it.
    /// </summary>
    public sealed class Hostage : Agent, IInteractable
    {
        public override Faction Faction => Faction.Neutral;
        public override int TickOrder => TickOrders.Enemy + 6;

        public const float CaptorRange = 2.6f, UntieTime = 1.2f, LeaveTime = 5f;

        public EnemyAgent Captor { get; private set; }
        public bool Freed { get; private set; }
        public bool Harmed { get; private set; }
        /// <summary>Still a shield: tied, her captor alive.</summary>
        public bool Held => !Freed && !Harmed && Captor != null && Captor.IsAlive && Captor.gameObject.activeInHierarchy;

        /// <summary>(hostage, who freed her or null, how: "untied" | "captor_down" | "slipped" | "harmed").</summary>
        public static event Action<Hostage, Agent, string> Released;
        public static readonly List<Hostage> All = new List<Hostage>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            Released = null;
        }

        float _leaveT, _bindT;
        bool _pleaded;

        /// <summary>Is this archer sheltering behind a hostage right now?</summary>
        public static bool Shields(EnemyAgent e)
        {
            if (e == null) return false;
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null && All[i].Held && All[i].Captor == e) return true;
            return false;
        }

        protected override void Awake()
        {
            base.Awake();
            Health = new Health(60f);
            if (Presenter == null) Presenter = GetComponentInChildren<IAgentPresenter>();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!All.Contains(this)) All.Add(this);
            if (!Application.isPlaying) return;
            Interactables.Register(this);
            HS.UI.BarkView.RegisterSpeaker("hostage", transform);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            All.Remove(this);
            if (Application.isPlaying) Interactables.Unregister(this);
            if (Captor != null) Captor.HoldsPosition = false;
        }

        /// <summary>Bind to the shooter standing behind her (the nearest ranged enemy within reach).</summary>
        void FindCaptor()
        {
            EnemyAgent best = null;
            float bestD = CaptorRange;
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is EnemyAgent e) || !e.IsAlive) continue;
                float d = Geo.FlatDistance(e.Position, Position);
                if (d <= bestD && (e.Stats == null || e.Stats.ranged))
                {
                    bestD = d;
                    best = e;
                }
            }
            Captor = best;
            if (Captor != null) Captor.HoldsPosition = true;
        }

        protected override void OnSimTick(float dt)
        {
            if (_leaveT > 0f)
            {
                _leaveT -= dt;
                // Off the road to the nearer side, like a spared man: never left standing in his way.
                float side = Position.x >= 0f ? 1f : -1f;
                var dir = new Vector3(side, 0f, -0.3f).normalized;
                Motor.Move(dir * 4.5f, 30f, dt);
                Motor.FaceDirection(dir, 540f, dt);
                Presenter?.SetLocomotion(Motor.Speed, false);
                if (_leaveT <= 0f) gameObject.SetActive(false);
                return;
            }
            Motor.Move(Vector3.zero, 30f, dt);
            Presenter?.SetLocomotion(0f, false);
            if (Freed || Harmed) return;
            if (Captor == null)
            {
                // Spawned with her captor in the same frame: look for him for a moment, then she's simply a bystander.
                _bindT += dt;
                if (_bindT <= 0.5f) FindCaptor();
                if (Captor == null) return;
                Presenter?.PlayAction("surrender");
            }
            if (!Captor.IsAlive || !Captor.gameObject.activeInHierarchy)
            {
                Release(null, "captor_down");
                return;
            }
            if (Captor.Status.Incapacitated || Captor.Status.Has(StatusType.Blinded))
            {
                Release(null, "slipped");
                return;
            }
            var hero = Ctx != null ? Ctx.Hero : null;
            if (!_pleaded && hero != null && hero.IsAlive && Geo.FlatDistance(hero.Position, Position) <= 14f)
            {
                _pleaded = true;
                Ctx.Events.RaiseBark("hostage", "Please — he'll shoot! Don't come closer!", 2.6f, 1);
                Ctx.Events.RaiseBark("callum", "Coward! Let her go and face me!", 2.6f, 2);
            }
        }

        void Release(Agent by, string how)
        {
            if (Freed || Harmed) return;
            if (how == "harmed") Harmed = true;
            else Freed = true;
            if (Captor != null) Captor.HoldsPosition = false;
            Interactables.Unregister(this);
            Presenter?.PlayAction("none");
            _leaveT = LeaveTime;
            if (how != "harmed") Ctx?.Events.RaiseBark("hostage", "Thank you — thank you!", 2.2f, 1);
            Released?.Invoke(this, by, how);
        }

        protected override float ModifyIncomingDamage(DamageInfo d) => Freed ? 0f : d.Amount;

        protected override void OnHurt(DamageInfo d, float applied)
        {
            base.OnHurt(d, applied);
            if (!(d.Source is SidekickAgent sk) || Harmed) return;
            // A bolt through the woman in the way: exactly the dishonour a knight watches for.
            var ctx = Ctx;
            ctx?.Events.RaiseSabotage(new SabotageEvent
            {
                Tag = "hostage", Severity = SabotageSeverity.Major, Position = Position, ActorPosition = sk.Position, Victim = this,
                Time = ctx.SimTime,
            });
            Release(sk, "harmed");
        }

        protected override void OnDied(DamageInfo d)
        {
            base.OnDied(d);
            if (!Harmed && !Freed) Release(d.Source, "harmed");
        }

        // ------------------------------------------------------------------ IInteractable: untie her
        public Vector3 InteractPosition => Position;
        public string Prompt => "Untie the hostage";
        public float InteractDuration => UntieTime;
        public bool CanInteract(Agent who) => who is SidekickAgent && Held;
        public void Interact(Agent who) => Release(who, "untied");

        public override bool IsHostileTo(Agent other) => false;
    }
}
