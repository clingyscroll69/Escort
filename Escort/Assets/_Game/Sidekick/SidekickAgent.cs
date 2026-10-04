using System;
using System.Collections.Generic;
using HS.Core;
using UnityEngine;

namespace HS.Sidekick
{
    /// <summary>
    /// The player: unlisted, unseen, the only reason the hero survives (GDD §1, §4.1).
    /// Move 6 m/s (walk 3.4, crouch 2.5), 3-charge dodge roll with 0.3 s invulnerability, 8-damage kitchen knife,
    /// ping, interact, 25 m support range. Driven by an ISidekickCommands source (human or bot).
    /// </summary>
    public sealed class SidekickAgent : Agent
    {
        public override Faction Faction => Faction.Sidekick;
        public override int TickOrder => TickOrders.SidekickInput;

        public ISidekickCommands Commands;
        public int Level { get; private set; } = 1;
        public bool Crouched { get; private set; }
        public DodgeCharges Dodge { get; private set; }
        public Vector3 AimPoint { get; private set; }
        public bool IsDodging => _dodgeT > 0f;
        public bool IsChanneling => _channel != null;
        public string ChannelLabel => _channel?.Label;
        public float ChannelProgress => _channel == null ? 0f : Mathf.Clamp01(_channel.Elapsed / _channel.Duration);
        public float KnifeCooldownRemaining => _knifeCd;
        public float DistanceToHero { get; private set; }
        public bool InSupportRange { get; private set; } = true;
        public float TimeInSupportRange { get; private set; }
        /// <summary>Quiet Feet passive (skill system sets this).</summary>
        public bool HasQuietFeet;
        /// <summary>Detection radius while sneaking with Quiet Feet (2 m; rank 2: 1.5 m).</summary>
        public float QuietFeetRadius = 2f;
        /// <summary>Hero witness-cone multiplier while sneaking with Quiet Feet.</summary>
        public float QuietFeetConeMul = 0.67f;
        /// <summary>True while sneaking with Quiet Feet: unaware enemies can't detect beyond 2 m (GDD §5).</summary>
        public bool IsSneaking => Crouched && HasQuietFeet;

        /// <summary>Skill hook: slot index → handled. Set by the skill system.</summary>
        public Func<int, SidekickCommand, bool> SkillHandler;
        public event Action<IInteractable> Interacted;
        public event Action Downed;

        public SidekickCommand LastCommand { get; private set; }
        // ------------------------------------------------------------------ Downed (after Recall is learned, GDD §4.2)
        public const float DownedTime = 20f;
        /// <summary>Set by the flow once he has learned Recall: 0 HP downs her instead of killing her.</summary>
        public bool CanBeDowned;
        public bool IsDowned { get; private set; }
        public float DownedRemaining { get; private set; }
        public event Action WentDown, Rose, DownedTimedOut;
        bool _goDown;

        protected override float ModifyIncomingDamage(DamageInfo d)
        {
            if (IsDowned) return 0f; // nobody finishes off the unlisted
            if (CanBeDowned && d.Amount >= Health.Current)
            {
                _goDown = true;
                return Mathf.Max(0f, Health.Current - 1f);
            }
            return d.Amount;
        }

        public override float TakeDamage(DamageInfo d)
        {
            float applied = base.TakeDamage(d);
            if (_goDown)
            {
                _goDown = false;
                GoDown();
            }
            return applied;
        }

        void GoDown()
        {
            if (IsDowned) return;
            IsDowned = true;
            DownedRemaining = DownedTime;
            CancelChannel();
            Crouched = false;
            _dodgeT = 0f;
            Health.Invulnerable = false;
            Status.Apply(StatusType.Downed, float.PositiveInfinity, this);
            Presenter?.PlayAction("death");
            Downed?.Invoke();
            WentDown?.Invoke();
        }

        /// <summary>Recalled (or back at the door): on her feet with a fraction of her HP.</summary>
        public void Rise(float hpFraction)
        {
            if (!IsDowned) return;
            IsDowned = false;
            DownedRemaining = 0f;
            Status.Clear(StatusType.Downed);
            Health.SetCurrent(Mathf.Max(1f, Health.Max * hpFraction));
            Presenter?.PlayAction("getup");
            Rose?.Invoke();
        }

        void TickDowned(float dt)
        {
            Motor.Move(Vector3.zero, 60f, dt);
            Presenter?.SetLocomotion(0f, false);
            DownedRemaining -= dt;
            if (DownedRemaining <= 0f)
            {
                DownedRemaining = 0f;
                DownedTimedOut?.Invoke();
            }
        }

        /// <summary>Food for the hero (chapters 2–4).</summary>
        public Rations Rations { get; } = new Rations();

        SidekickTuning T => (Ctx != null ? Ctx.Tuning : Tuning.LoadDefault()).sidekick;

        float _dodgeT, _dodgeIFrames;
        Vector3 _dodgeDir;
        float _knifeCd, _knifeWindup;
        float _pingCd;
        bool _stabAlt;
        readonly List<Agent> _hits = new List<Agent>();

        sealed class Channel
        {
            public string Label;
            public float Duration;
            public float Elapsed;
            public bool CancelOnMove;
            public Action OnComplete;
            public Action OnCancel;
            public Func<bool> ValidWhile;
        }

        Channel _channel;

        protected override void Awake()
        {
            base.Awake();
            Health = new Health(T.maxHp);
            Dodge = new DodgeCharges(T.dodgeCharges, T.dodgeRecharge);
            if (Commands == null) Commands = GetComponent<ISidekickCommands>();
            if (Presenter == null) Presenter = GetComponentInChildren<IAgentPresenter>();
            AimPoint = transform.position + transform.forward * 3f;
        }

        public void SetLevel(int level, bool refill)
        {
            Level = Mathf.Max(1, level);
            Health.SetBaseMax(T.maxHp + T.hpPerLevel * (Level - 1), refill);
        }

        protected override void OnSimTick(float dt)
        {
            if (!IsAlive) return;
            if (IsDowned)
            {
                LastCommand = SidekickCommand.None;
                TickDowned(dt);
                return;
            }
            var t = T;
            Dodge.Tick(dt);
            if (_knifeCd > 0f) _knifeCd -= dt;
            if (_pingCd > 0f) _pingCd -= dt;

            var cmd = Commands != null ? Commands.Next(this) : SidekickCommand.None;
            LastCommand = cmd;
            if (cmd.HasAim) AimPoint = cmd.AimPoint;
            UpdateSupportRange(dt, t);

            // Dodge in progress: committed movement, i-frames for the first 0.3 s.
            if (_dodgeT > 0f)
            {
                _dodgeT -= dt;
                _dodgeIFrames -= dt;
                Health.Invulnerable = _dodgeIFrames > 0f;
                Motor.MoveExact(_dodgeDir * (t.dodgeDistance / t.dodgeDuration), dt);
                Presenter?.SetLocomotion(0f, false);
                if (_dodgeT <= 0f) Health.Invulnerable = false;
                return;
            }

            if (Status.Incapacitated)
            {
                CancelChannel();
                Motor.Move(Vector3.zero, t.accel, dt);
                Presenter?.SetLocomotion(0f, Crouched);
                return;
            }

            if (cmd.Dodge && Dodge.TryConsume())
            {
                CancelChannel();
                _knifeWindup = 0f;
                _dodgeDir = cmd.Move.sqrMagnitude > 0.01f ? Geo.Flat(cmd.Move).normalized : Forward;
                _dodgeT = t.dodgeDuration;
                _dodgeIFrames = t.dodgeIFrames;
                Health.Invulnerable = true;
                Crouched = false;
                Motor.FaceInstant(_dodgeDir);
                Presenter?.PlayAction("dodge", t.dodgeDuration);
                Ctx?.Events.RaiseSkillUsed("dodge", this);
                return;
            }

            if (_channel != null)
            {
                bool moving = cmd.Move.sqrMagnitude > 0.04f;
                if ((moving && _channel.CancelOnMove) || (_channel.ValidWhile != null && !_channel.ValidWhile()))
                {
                    CancelChannel();
                }
                else
                {
                    _channel.Elapsed += dt;
                    Motor.Move(Vector3.zero, t.accel, dt);
                    Presenter?.SetLocomotion(0f, Crouched);
                    if (_channel.Elapsed >= _channel.Duration)
                    {
                        var done = _channel;
                        _channel = null;
                        Presenter?.PlayAction("none");
                        done.OnComplete?.Invoke();
                    }
                    return;
                }
            }

            if (cmd.CrouchToggle) Crouched = !Crouched;

            // Kitchen knife: short windup, arc hit on hostiles only.
            if (_knifeWindup > 0f)
            {
                _knifeWindup -= dt;
                if (_knifeWindup <= 0f) ResolveKnife(t);
            }
            else if (cmd.Attack && _knifeCd <= 0f)
            {
                var toAim = Geo.DirTo(Position, AimPoint);
                if (toAim != Vector3.zero) Motor.FaceInstant(toAim);
                _knifeWindup = t.knifeWindup;
                _knifeCd = t.knifeCooldown;
                _stabAlt = !_stabAlt; // alternate jab/cross deterministically (no randomness in gameplay)
                Presenter?.PlayAction(_stabAlt ? "stab" : "stab2", t.knifeCooldown);
            }

            if (cmd.Ping && _pingCd <= 0f) DoPing(t);
            if (cmd.Interact) TryInteract(t);
            if (cmd.Skill >= 0) SkillHandler?.Invoke(cmd.Skill, cmd);
            if (_channel != null) return; // a skill or interaction may have started a channel

            float speed = Crouched ? t.crouchSpeed : cmd.Walk ? t.walkSpeed : t.jogSpeed;
            if (_knifeWindup > 0f) speed *= 0.5f;
            var desired = Vector3.ClampMagnitude(cmd.Move, 1f) * speed;
            Motor.Move(desired, t.accel, dt);
            if (_knifeWindup > 0f || (cmd.SkillHeld && cmd.HasAim))
                Motor.FaceDirection(Geo.DirTo(Position, AimPoint), t.turnSpeed, dt);
            else if (desired.sqrMagnitude > 0.01f)
                Motor.FaceDirection(desired, t.turnSpeed, dt);
            Presenter?.SetLocomotion(Motor.Speed, Crouched);
        }

        void UpdateSupportRange(float dt, SidekickTuning t)
        {
            var hero = Ctx != null ? Ctx.Hero : null;
            if (hero == null)
            {
                InSupportRange = true;
                DistanceToHero = 0f;
                return;
            }
            DistanceToHero = Geo.FlatDistance(Position, hero.Position);
            InSupportRange = DistanceToHero <= t.supportRange;
            if (InSupportRange) TimeInSupportRange += dt;
        }

        void ResolveKnife(SidekickTuning t)
        {
            Melee.Sweep(this, t.knifeRange, t.knifeArc, _hits, a => a.Faction == Faction.Hostile || (a is IBreakable));
            // Deterministic: strike the nearest target in the arc only (a paring knife, not a cleave).
            Agent best = null;
            float bestD = float.MaxValue;
            foreach (var a in _hits)
            {
                float d = Geo.FlatSqrDistance(Position, a.Position);
                if (d < bestD)
                {
                    bestD = d;
                    best = a;
                }
            }
            if (best != null)
                best.TakeDamage(DamageInfo.Make(this, best, t.knifeDamage, DamageKind.Knife, "knife"));
        }

        void DoPing(SidekickTuning t)
        {
            _pingCd = t.pingCooldown;
            var point = AimPoint;
            if (Geo.FlatDistance(Position, point) > t.pingRange)
                point = Position + Geo.DirTo(Position, point) * t.pingRange;
            var target = AgentRegistry.Nearest(point, 1.8f, a => a != this && a.Faction != Faction.Sidekick);
            Ctx?.Events.RaisePing(new PingInfo { Point = point, Target = target, Meaning = "mark" });
        }

        void TryInteract(SidekickTuning t)
        {
            var i = Interactables.Nearest(this, t.interactRange);
            if (i == null) return;
            var dir = Geo.DirTo(Position, i.InteractPosition);
            if (dir != Vector3.zero) Motor.FaceInstant(dir);
            if (i.InteractDuration <= 0f)
            {
                i.Interact(this);
                Presenter?.PlayAction("interact", 0.6f);
                Interacted?.Invoke(i);
                return;
            }
            StartChannel(i.Prompt, i.InteractDuration, "kneel", true, () =>
            {
                if (i.CanInteract(this))
                {
                    i.Interact(this);
                    Interacted?.Invoke(i);
                }
            });
        }

        /// <summary>Begin a channelled action (interactions, Bandage, Loosen Bolt...). Moving cancels if requested.</summary>
        public void StartChannel(string label, float duration, string anim, bool cancelOnMove, Action onComplete, Action onCancel = null, Func<bool> validWhile = null)
        {
            CancelChannel();
            _channel = new Channel { Label = label, Duration = duration, CancelOnMove = cancelOnMove, OnComplete = onComplete, OnCancel = onCancel, ValidWhile = validWhile };
            if (!string.IsNullOrEmpty(anim)) Presenter?.PlayAction(anim, duration);
        }

        public void CancelChannel()
        {
            if (_channel == null) return;
            var c = _channel;
            _channel = null;
            Presenter?.PlayAction("none");
            c.OnCancel?.Invoke();
        }

        protected override void OnHurt(DamageInfo d, float applied)
        {
            base.OnHurt(d, applied);
            if (d.Stagger > 0f) CancelChannel();
        }

        protected override void OnDied(DamageInfo d)
        {
            base.OnDied(d);
            CancelChannel();
            Downed?.Invoke();
        }

        /// <summary>Restore after Recall / restore points.</summary>
        public void Revive(float hpFraction)
        {
            Health.SetCurrent(Health.Max * hpFraction);
            Status.ClearAll();
            if (Controller != null) Controller.enabled = true;
            Presenter?.PlayAction("getup");
        }
    }

    /// <summary>Marker for non-agent-hostile things the knife can hit (e.g. chronicle stones implement Agent + this).</summary>
    public interface IBreakable { }
}
