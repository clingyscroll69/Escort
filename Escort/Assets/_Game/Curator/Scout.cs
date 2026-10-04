using HS.Core;
using HS.Rooms;
using HS.Sidekick;
using HS.Skills.Impl;
using UnityEngine;

namespace HS.Curator
{
    /// <summary>
    /// A disguised Curator scout (GDD §4.3: Mr. Quill, the Prisoner, Darian Wren). Tell: a dull chronicle-stone pendant.
    /// Ping him within 3 s of first sight, or catch him with Read the Room: he bolts — −1 Curator Intel and a dossier
    /// fragment. Missed, he reports at the campfire (+1 Intel). Neutral and untouchable; he talks, he trades, he watches.
    /// </summary>
    public sealed class Scout : Agent, IInteractable
    {
        public override Faction Faction => Faction.Neutral;
        public override int TickOrder => TickOrders.Enemy + 5;

        public const float SightRange = 16f, PingWindow = 3f, FleeTime = 5f, FleeSpeed = 6.5f, GreetRange = 6f;
        public string ScoutId = "quill";
        public string DisplayName = "Mr. Quill";
        [Tooltip("What he offers the sidekick, once (a ration for the road).")]
        public int GiftRations = 1;

        public float FirstSeenAt { get; private set; } = -1f;
        public bool Unmasked { get; private set; }
        public bool Reported { get; private set; }
        public bool Gave { get; private set; }
        float _fleeT, _glintT;
        bool _greeted, _shrugged;
        RunContext _ctx;

        public static readonly System.Collections.Generic.List<Scout> All = new System.Collections.Generic.List<Scout>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        protected override void Awake()
        {
            base.Awake();
            Health = new Health(1000f) { Invulnerable = true };
            if (Presenter == null) Presenter = GetComponentInChildren<IAgentPresenter>();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _ctx = RunContext.Current;
            if (!All.Contains(this)) All.Add(this);
            if (!Application.isPlaying) return;
            Interactables.Register(this);
            if (_ctx != null) _ctx.Events.Ping += OnPing;
            ReadTheRoom.Began += OnRead;
            HS.UI.BarkView.RegisterSpeaker(ScoutId, transform);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            All.Remove(this);
            if (!Application.isPlaying) return;
            Interactables.Unregister(this);
            if (_ctx != null) _ctx.Events.Ping -= OnPing;
            ReadTheRoom.Began -= OnRead;
        }

        float Now => _ctx != null ? _ctx.SimTime : 0f;
        SidekickAgent Sk => _ctx != null ? _ctx.Sidekick as SidekickAgent : null;
        public bool InWindow => FirstSeenAt >= 0f && Now - FirstSeenAt <= PingWindow;

        void Say(string text, int priority = 1) => _ctx?.Events.RaiseBark(ScoutId, text, 3f, priority);

        protected override void OnSimTick(float dt)
        {
            if (Unmasked)
            {
                TickFlee(dt);
                return;
            }
            Motor.Move(Vector3.zero, 30f, dt);
            Presenter?.SetLocomotion(0f, false);
            var sk = Sk;
            if (FirstSeenAt < 0f && sk != null && sk.IsAlive && Geo.FlatDistance(sk.Position, Position) <= SightRange)
            {
                FirstSeenAt = Now;
                Glint();
            }
            // The pendant catches the light now and then: the tell, for those who look.
            _glintT -= dt;
            if (FirstSeenAt >= 0f && _glintT <= 0f) Glint();
            var hero = _ctx != null ? _ctx.Hero : null;
            if (!_greeted && hero != null && hero.IsAlive && Geo.FlatDistance(hero.Position, Position) <= GreetRange)
            {
                _greeted = true;
                _ctx.Events.RaiseBark("callum", "Good day, merchant. Safe roads to you.", 2.6f, 1);
                Say("And to you, Sir Knight. Such a famous face!", 0);
            }
            var read = ReadTheRoom.Get(_ctx);
            if (read != null && read.Covers(_ctx, Position)) Unmask("read");
        }

        void Glint()
        {
            _glintT = 3.5f;
            HS.Presentation.Vfx.Burst(HS.Presentation.VfxKind.Glint, Position + Vector3.up * 1.35f, 0.4f);
        }

        void OnRead(ReadTheRoom r)
        {
            if (!Unmasked && r.Covers(_ctx, Position)) Unmask("read");
        }

        void OnPing(PingInfo p)
        {
            if (Unmasked || p.Target != this) return;
            if (InWindow) Unmask("ping");
            else if (!_shrugged)
            {
                _shrugged = true;
                Say("Pointing is rude, friend. Rations? Rope? No?", 1);
            }
        }

        /// <summary>Caught out: he bolts, and his file on the hero lets something slip.</summary>
        public void Unmask(string how)
        {
            if (Unmasked) return;
            Unmasked = true;
            _fleeT = FleeTime;
            Interactables.Unregister(this);
            _ctx?.Get<StoneSystem>()?.Intel.Forge(); // −1 Intel, like false footage
            var dossier = _ctx?.Get<Dossier>();
            string fragment = Dossier.ScoutFragments.TryGetValue(ScoutId, out var f) ? f : null;
            dossier?.Add(fragment);
            Say(how == "read" ? "Wh— you're not supposed to— I have to go." : "That's— no. No, I have places to be.", 2);
            _ctx?.Events.RaiseNotice($"{DisplayName.ToUpperInvariant()} BOLTED. HIS PENDANT WAS DULL GREY, LIKE A DEAD STONE.\n<size=80%>A page fell from his coat: {fragment}</size>");
            Presenter?.SetFlag("combat", false);
        }

        void TickFlee(float dt)
        {
            if (_fleeT <= 0f) return;
            _fleeT -= dt;
            var away = Geo.Flat(Position - (_ctx != null && _ctx.Hero != null ? _ctx.Hero.Position : Position + Vector3.back));
            var dir = new Vector3(Mathf.Sign(away.x == 0f ? 1f : away.x), 0f, 0.3f).normalized;
            Motor.Move(dir * FleeSpeed, 40f, dt);
            Motor.FaceDirection(dir, 720f, dt);
            Presenter?.SetLocomotion(Motor.Speed, false);
            if (_fleeT <= 0f) gameObject.SetActive(false);
        }

        /// <summary>The campfire: whoever wasn't caught sends his report (+1 Intel: two clips).</summary>
        public void Report(StoneSystem stones)
        {
            if (Unmasked || Reported || stones == null) return;
            Reported = true;
            stones.Intel.Relay(CuratorIntel.ClipsPerLevel);
        }

        // ------------------------------------------------------------------ IInteractable: his gift
        public Vector3 InteractPosition => Position;
        public string Prompt => "Talk to " + DisplayName;
        public float InteractDuration => 0f;
        public bool CanInteract(Agent who) => !Unmasked && !Gave && who is SidekickAgent;

        public void Interact(Agent who)
        {
            if (!CanInteract(who)) return;
            Gave = true;
            int got = GiftRations > 0 && who is SidekickAgent sk ? sk.Rations.Give(GiftRations) : 0;
            Say(got > 0 ? "For the road, friend. On the house. ...What's your name again?" : "Pack's full? Next time, then.", 1);
            if (got > 0) _ctx?.Events.RaiseNotice($"{DisplayName.ToUpperInvariant()}: +{got} ration.");
        }

        public override bool IsHostileTo(Agent other) => false;
    }
}
