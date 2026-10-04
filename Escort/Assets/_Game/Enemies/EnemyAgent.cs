using System;
using System.Collections.Generic;
using HS.Core;
using UnityEngine;

namespace HS.Enemies
{
    public enum EnemyState { Dormant, Hidden, Engaged, Surrendered, Fleeing, Spared, Dead }

    /// <summary>
    /// Data-driven, deterministic enemy (GDD §2: fixed damage, deterministic AI). Archetype numbers come from
    /// Tuning.enemies. Heroes are loud (always noticed once an encounter starts); the sidekick is unlisted and easy to
    /// overlook (GDD §4.3) — detected only close up, in view, or after hurting the enemy.
    /// </summary>
    public sealed class EnemyAgent : Agent
    {
        public override Faction Faction => Faction.Hostile;
        public override int TickOrder => TickOrders.Enemy;

        public string Archetype = "thug";
        public int Group;
        public bool StartsHidden;
        public bool Elevated;
        public float JoinDelay;
        /// <summary>Dozing at his post (Whisperwood's poacher camp): Unready until something wakes him.</summary>
        public bool StartsAsleep;
        /// <summary>Set-piece actor (the rigged duel): only a director wakes it — no sidekick spotting, no ambush springing.</summary>
        public bool Scripted;

        public EnemyStats Stats { get; private set; }
        public EnemyState State { get; private set; } = EnemyState.Dormant;
        public Agent Target { get; private set; }
        public bool AwareOfSidekick { get; private set; }
        /// <summary>Set by the hero when he challenges this enemy (formal duel).</summary>
        public Agent DuelOpponent;
        public bool IsCheater => Stats != null && Stats.cheater;
        public bool IsRanged => Stats != null && Stats.ranged;
        public bool IsAttackWindup => _phase == Phase.Windup;
        public bool IsAiming => _phase == Phase.Aim;
        public float WindupRemaining => _phaseT;
        public bool NextIsHeavy => Stats != null && Stats.heavyEvery > 0 && (_attackCount + 1) % Mathf.RoundToInt(Stats.heavyEvery) == 0;
        public Vector3 AimDirection => _aimDir;
        public float TimeSinceSidekickHurtMe { get; private set; } = 999f;
        public float SurrenderElapsed { get; private set; }
        /// <summary>The fake surrender turned into a cheap shot (GDD: "fake surrenders").</summary>
        public bool CheapShotReady => State == EnemyState.Surrendered && SurrenderElapsed >= SurrenderStab;

        public event Action<EnemyAgent, EnemyState> StateChanged;
        public event Action<EnemyAgent, Agent> Attacked; // (self, victim) at the moment of the hit

        enum Phase { None, Approach, Windup, Recover, Aim, Reload }
        Phase _phase = Phase.None;
        float _phaseT;
        int _attackCount;
        Vector3 _aimDir;
        float _joinT;
        const float SurrenderStab = 2.4f;
        bool _surrenderUsed;
        readonly List<Agent> _scratch = new List<Agent>();

        protected override void Awake()
        {
            base.Awake();
            if (Presenter == null) Presenter = GetComponentInChildren<IAgentPresenter>();
        }

        void Start()
        {
            Configure(Archetype);
            if (StartsHidden) SetState(EnemyState.Hidden);
            if (StartsAsleep) FallAsleep();
        }

        public void Configure(string archetype)
        {
            Archetype = archetype;
            var t = Ctx != null ? Ctx.Tuning : Tuning.LoadDefault();
            Stats = t.Enemy(archetype);
            float hp = Stats.maxHp * ChapterTier.EnemyHp(Ctx != null ? Ctx.Chapter : 1);
            Health = new Health(hp);
            if (Stats.startsHidden) StartsHidden = true;
        }

        void SetState(EnemyState s)
        {
            if (State == s) return;
            State = s;
            if (s != EnemyState.Engaged) AttackTokens.Release(this);
            StateChanged?.Invoke(this, s);
            switch (s)
            {
                case EnemyState.Hidden: SetVisible(false); break;
                case EnemyState.Engaged: SetVisible(true); Presenter?.SetFlag("combat", true); break;
                case EnemyState.Surrendered: Presenter?.PlayAction("surrender"); Presenter?.SetFlag("combat", false); break;
                case EnemyState.Spared: Presenter?.PlayAction("none"); Presenter?.SetFlag("combat", false); break;
            }
        }

        void SetVisible(bool on)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        }

        /// <summary>Encounter start (hero entered the zone, or the enemy was provoked).</summary>
        public void Activate(float delay = 0f)
        {
            if (!IsAlive || State == EnemyState.Engaged || State == EnemyState.Surrendered || State == EnemyState.Spared) return;
            _joinT = Mathf.Max(delay, JoinDelay);
            if (State == EnemyState.Dormant) SetState(EnemyState.Engaged);
        }

        /// <summary>Hidden ambusher springs (hero passed close, or revealed by sand/bumping).</summary>
        /// <summary>How a hidden ambusher came out: "ambush" (sprang on the hero) or "sidekick" (sand, bump, knife).</summary>
        public string RevealedBy { get; private set; }

        public void Reveal(bool ambushStrike)
        {
            if (State != EnemyState.Hidden) return;
            RevealedBy = ambushStrike ? "ambush" : "sidekick";
            SetState(EnemyState.Engaged);
            var hero = Ctx != null ? Ctx.Hero : null;
            if (ambushStrike && hero != null && hero.IsAlive)
            {
                // Spring from the hedge: a short committed lunge, then the ambush blow.
                _lungeT = LungeTime;
                Motor.FaceInstant(Geo.DirTo(Position, hero.Position));
                Presenter?.PlayAction("attack", LungeTime + 0.2f);
            }
        }

        /// <summary>Hidden shooters stand up on a signal (the rigged duel's volley).</summary>
        public void Emerge(string cause = "signal")
        {
            if (State != EnemyState.Hidden) return;
            RevealedBy = cause;
            SetState(EnemyState.Engaged);
        }

        const float LungeTime = 0.38f;
        float _lungeT, _glintT;

        void TickLunge(float dt)
        {
            var hero = Ctx != null ? Ctx.Hero : null;
            if (hero == null || !hero.IsAlive)
            {
                _lungeT = 0f;
                return;
            }
            _lungeT -= dt;
            var to = Geo.Flat(hero.Position - Position);
            float d = to.magnitude;
            if (d > 1.4f) Motor.Move(to / d * 15f, 200f, dt);
            else Motor.Move(Vector3.zero, 200f, dt);
            Motor.FaceDirection(to, 900f, dt);
            if (_lungeT <= 0f || d <= 1.4f)
            {
                _lungeT = 0f;
                if (d <= 2.4f)
                {
                    var hit = DamageInfo.Make(this, hero, Stats.ambushDamage, DamageKind.Blade, "ambush");
                    Attacked?.Invoke(this, hero);
                    hero.TakeDamage(hit);
                }
            }
        }

        /// <summary>Steel in the hedge: the unlisted sidekick notices what the loud hero never will (fair tell).</summary>
        void TickHiddenTell(float dt)
        {
            _glintT -= dt;
            if (_glintT > 0f) return;
            _glintT = 2.4f;
            var sk = Ctx != null ? Ctx.Sidekick : null;
            if (sk != null && sk.IsAlive && Geo.FlatDistance(Position, sk.Position) <= 14f)
                HS.Presentation.Vfx.Burst(HS.Presentation.VfxKind.Glint, Position + Vector3.up * 1.15f, 0.55f);
        }

        // ------------------------------------------------------------------ sleep (Unready from the start)
        public const float WakeRadius = 3f, GetUpTime = 1.6f;
        public bool Asleep => Status.Has(StatusType.Sleeping);

        void FallAsleep()
        {
            Status.Apply(StatusType.Sleeping, float.PositiveInfinity, this);
            Presenter?.PlayAction("sit");
        }

        /// <summary>Woken (a challenge, a blow, an alarm, the loud hero walking up): he scrambles up — still Unready.</summary>
        public void Wake(string why)
        {
            if (!Asleep) return;
            Status.Clear(StatusType.Sleeping);
            Status.Apply(StatusType.Stunned, GetUpTime, this);
            Presenter?.PlayAction("getup", GetUpTime);
            if (State == EnemyState.Dormant) Activate();
        }

        void TickAsleep()
        {
            Motor.Move(Vector3.zero, 30f, SimLoop.Dt);
            Presenter?.SetLocomotion(0f, false);
            var hero = Ctx != null ? Ctx.Hero : null;
            if (hero != null && hero.IsAlive && Geo.FlatDistance(Position, hero.Position) <= WakeRadius) Wake("footsteps");
        }

        public bool IsHidden => State == EnemyState.Hidden;
        public bool IsActive => State == EnemyState.Engaged;

        /// <summary>Callum's code: sleeping, fleeing, surrendered, staggered, blinded, or turned away (GDD §6.1).</summary>
        public bool IsUnreadyFor(Agent hero) =>
            IsHelpless(hero) || (hero != null && Geo.AngleTo(Position, Forward, hero.Position) > 100f && Target != hero); // turned away

        /// <summary>
        /// Can't fight back: sleeping, fleeing, surrendered, blinded, or knocked off balance by someone other than the
        /// fighters named (their own blows and parries are part of the fight). Callum judges the sidekick's blows by this,
        /// not IsUnreadyFor: "turned away" is his own etiquette, and a bandit squared up with her is turned away from him.
        /// </summary>
        public bool IsHelpless(Agent fighter, Agent otherFighter = null)
        {
            if (State == EnemyState.Surrendered || State == EnemyState.Fleeing || Status.Has(StatusType.Sleeping)) return true;
            return KnockedByOther(StatusType.Staggered, fighter, otherFighter) || KnockedByOther(StatusType.Stunned, fighter, otherFighter)
                   || Status.Has(StatusType.Blinded);
        }

        bool KnockedByOther(StatusType s, Agent a, Agent b) =>
            Status.Has(s) && (a == null || Status.SourceOf(s) != a) && (b == null || Status.SourceOf(s) != b);

        public const float SparedLeaveTime = 5f;
        float _sparedT;

        /// <summary>
        /// Spared: he walks off the road to the nearer side and is gone — never left standing in the hero's path (a body
        /// in the way is a soft-lock: CharacterControllers don't pass through each other).
        /// </summary>
        void TickSpared(float dt)
        {
            _sparedT += dt;
            float side = Position.x >= 0f ? 1f : -1f;
            var dir = new Vector3(side, 0f, 0.25f).normalized;
            Motor.Move(dir * Mathf.Max(2.5f, Stats != null ? Stats.speed * 0.6f : 2.5f), 30f, dt);
            Motor.FaceDirection(dir, 360f, dt);
            Presenter?.SetLocomotion(Motor.Speed, false);
            if (_sparedT >= SparedLeaveTime) gameObject.SetActive(false);
        }

        public void Spare()
        {
            if (State == EnemyState.Surrendered) SetState(EnemyState.Spared);
        }

        protected override float ModifyIncomingDamage(DamageInfo d)
        {
            if (State == EnemyState.Hidden && d.FromSidekick) Reveal(false);
            if (d.FromSidekick)
            {
                TimeSinceSidekickHurtMe = 0f;
                AwareOfSidekick = true;
            }
            if (State == EnemyState.Dormant) Activate();
            return d.FromSidekick ? d.Amount * ChapterTier.SidekickDamage(Ctx != null ? Ctx.Chapter : 1) : d.Amount;
        }

        protected override void OnHurt(DamageInfo d, float applied)
        {
            base.OnHurt(d, applied);
            if (Asleep) Wake("hurt"); // after the blow was judged: it fell on a sleeping man
            if (d.Stagger > 0f) CancelAttack();
            // Turncoat: fake surrender at low health (GDD §6.1 "fake surrenders") — once; the ruse doesn't work twice.
            if (Stats.surrenderAtHp > 0f && !_surrenderUsed && State == EnemyState.Engaged && Health.Fraction <= Stats.surrenderAtHp)
            {
                _surrenderUsed = true;
                CancelAttack();
                SurrenderElapsed = 0f;
                SetState(EnemyState.Surrendered);
            }
            else if (State == EnemyState.Surrendered && d.Source != null)
            {
                // Hitting a surrendered man is exactly the dishonour Callum watches for; it also ends the ruse.
                SetState(EnemyState.Engaged);
            }
        }

        protected override void OnDied(DamageInfo d)
        {
            base.OnDied(d);
            AttackTokens.Release(this);
            SetState(EnemyState.Dead);
        }

        void CancelAttack()
        {
            _phase = Phase.None;
            _phaseT = 0f;
            AttackTokens.Release(this);
            Presenter?.PlayAction("none");
        }

        protected override void OnSimTick(float dt)
        {
            if (!IsAlive) return;
            if (Stats == null) Configure(Archetype); // ticked before Start (spawned mid-frame): configure now
            TimeSinceSidekickHurtMe += dt;
            if (Asleep)
            {
                TickAsleep();
                return;
            }
            switch (State)
            {
                case EnemyState.Dormant:
                    Motor.Move(Vector3.zero, 30f, dt);
                    Presenter?.SetLocomotion(0f, false);
                    if (!Scripted) WatchForSidekick();
                    return;
                case EnemyState.Hidden:
                    Motor.Move(Vector3.zero, 30f, dt);
                    TickHiddenTell(dt);
                    if (!Scripted) CheckAmbushTrigger();
                    else CheckBumped();
                    return;
                case EnemyState.Spared:
                    TickSpared(dt);
                    return;
                case EnemyState.Surrendered:
                    TickSurrender(dt);
                    return;
                case EnemyState.Fleeing:
                    TickFlee(dt);
                    return;
            }
            if (_lungeT > 0f)
            {
                TickLunge(dt);
                Presenter?.SetLocomotion(Motor.Speed, false);
                return;
            }
            if (_joinT > 0f)
            {
                _joinT -= dt;
                Motor.Move(Vector3.zero, 30f, dt);
                return;
            }
            if (Status.Incapacitated || Status.Has(StatusType.Blinded))
            {
                if (_phase == Phase.Windup || _phase == Phase.Aim) CancelAttack();
                // Blinded: stumble in place (deterministic wobble), can't target.
                var wobble = Status.Has(StatusType.Blinded) ? new Vector3(Mathf.Sin(Ctx != null ? Ctx.SimTime * 7f : 0f), 0f, 0f) * 0.8f : Vector3.zero;
                Motor.Move(wobble, 20f, dt);
                Presenter?.SetLocomotion(Motor.Speed, false);
                return;
            }
            ChooseTarget();
            if (Target == null)
            {
                Motor.Move(Vector3.zero, 30f, dt);
                Presenter?.SetLocomotion(0f, false);
                return;
            }
            if (Stats.ranged) TickRanged(dt);
            else TickMelee(dt);
            Presenter?.SetLocomotion(Motor.Speed, false);
        }

        void WatchForSidekick()
        {
            var sk = Ctx != null ? Ctx.Sidekick as HS.Sidekick.SidekickAgent : null;
            if (sk == null || !sk.IsAlive) return;
            if (CanDetect(sk))
            {
                AwareOfSidekick = true;
                Activate();
                GroupAlert();
            }
        }

        /// <summary>Unlisted: noticed only close up or in plain view; Quiet Feet crouch = 2 m (GDD §5).</summary>
        public bool CanDetect(HS.Sidekick.SidekickAgent sk)
        {
            var t = (Ctx != null ? Ctx.Tuning : Tuning.LoadDefault()).sidekick;
            float d = Geo.FlatDistance(Position, sk.Position);
            if (sk.IsSneaking) return d <= sk.QuietFeetRadius;
            if (d <= t.detectRadius) return true;
            return !Status.Has(StatusType.Blinded) && Geo.InCone(Position, Forward, sk.Position, t.detectConeAngle, t.detectConeRange);
        }

        void GroupAlert()
        {
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] is EnemyAgent e && e != this && e.Group == Group && e.State == EnemyState.Dormant && Geo.FlatDistance(Position, e.Position) < 18f)
                    e.Activate(0.4f);
        }

        /// <summary>Hedge ambushers sit a few metres off the road; they spring when he walks by.</summary>
        public const float AmbushTriggerRadius = 6.2f;

        void CheckBumped()
        {
            var sk = Ctx != null ? Ctx.Sidekick : null;
            if (sk != null && sk.IsAlive && Geo.FlatDistance(Position, sk.Position) < 1.1f) Reveal(false);
        }

        void CheckAmbushTrigger()
        {
            var hero = Ctx != null ? Ctx.Hero : null;
            if (hero != null && hero.IsAlive && Geo.FlatDistance(Position, hero.Position) < AmbushTriggerRadius) Reveal(true);
            var sk = Ctx != null ? Ctx.Sidekick : null;
            if (sk != null && sk.IsAlive && Geo.FlatDistance(Position, sk.Position) < 1.1f) Reveal(false); // bumped into
        }

        void ChooseTarget()
        {
            var hero = Ctx != null ? Ctx.Hero : null;
            var sk = Ctx != null ? Ctx.Sidekick : null;
            Agent pick = hero != null && hero.IsAlive ? hero : null;
            // Retaliate against the sidekick briefly after being hurt by them, if they're closer.
            if (sk != null && sk.IsAlive && !(sk is HS.Sidekick.SidekickAgent down && down.IsDowned) && AwareOfSidekick && TimeSinceSidekickHurtMe < 3.5f)
            {
                if (pick == null || Geo.FlatDistance(Position, sk.Position) < Geo.FlatDistance(Position, pick.Position) + 2f) pick = sk;
            }
            // Duel etiquette is the hero's, not the bandits': a duelled enemy keeps fighting the hero.
            if (DuelOpponent != null && DuelOpponent.IsAlive && TimeSinceSidekickHurtMe > 1.5f) pick = DuelOpponent;
            Target = pick;
        }

        void TickMelee(float dt)
        {
            var toT = Geo.Flat(Target.Position - Position);
            float dist = toT.magnitude;
            float reach = Stats.range + Target.Radius * 0.5f;
            switch (_phase)
            {
                case Phase.Windup:
                    _phaseT -= dt;
                    Motor.Move(Vector3.zero, 40f, dt);
                    Motor.FaceDirection(toT, 240f, dt);
                    if (_phaseT <= 0f) ResolveMelee();
                    return;
                case Phase.Recover:
                    _phaseT -= dt;
                    Motor.Move(Vector3.zero, 40f, dt);
                    if (_phaseT <= 0f)
                    {
                        _phase = Phase.None;
                        if (OthersWaitingOn(Target))
                        {
                            AttackTokens.Release(this); // step back and let the next one have a go
                            _tokenRest = TokenRest;
                        }
                    }
                    return;
            }
            if (_tokenRest > 0f) _tokenRest -= dt;
            // Keep a little spacing from other engagers so crowds don't stack (deterministic by registration order).
            var sep = Separation();
            bool hasToken = AttackTokens.Holds(this, Target) || (_tokenRest <= 0f && dist <= MenaceRadius + 0.5f && AttackTokens.TryAcquire(this, Target));
            if (!hasToken && dist <= MenaceRadius + 0.5f)
            {
                Circle(toT, dist, sep, dt);
                return;
            }
            if (dist > reach * 0.92f)
            {
                Motor.Move((toT.normalized + sep).normalized * Stats.speed, 30f, dt);
                Motor.FaceDirection(toT, 400f, dt);
                return;
            }
            Motor.Move(sep * 1.2f, 30f, dt);
            Motor.FaceDirection(toT, 400f, dt);
            bool heavy = NextIsHeavy;
            _phase = Phase.Windup;
            _phaseT = heavy ? Stats.heavyWindup : Stats.windup;
            Presenter?.PlayAction(heavy ? "attack3" : (_attackCount % 2 == 0 ? "attack" : "attack2"), _phaseT + 0.25f);
        }

        const float MenaceRadius = 3.6f;
        const float TokenRest = 0.7f;
        float _tokenRest;

        /// <summary>No attack token: hold menace range and circle the victim, facing them (waiting for an opening).</summary>
        void Circle(Vector3 toT, float dist, Vector3 sep, float dt)
        {
            var dir = toT / Mathf.Max(0.001f, dist);
            float sign = (StableHash(AgentId) & 1) == 0 ? 1f : -1f;
            var tangent = Vector3.Cross(Vector3.up, dir) * sign;
            float radial = Mathf.Clamp(dist - MenaceRadius, -1f, 1f); // >0: too far, <0: too close
            var v = (dir * radial * 1.6f + tangent * 0.9f + sep * 1.5f);
            Motor.Move(Vector3.ClampMagnitude(v, 1f) * Stats.speed * 0.45f, 20f, dt);
            Motor.FaceDirection(toT, 400f, dt);
        }

        bool OthersWaitingOn(Agent victim)
        {
            if (victim == null) return false;
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] is EnemyAgent e && e != this && e.IsActive && e.Target == victim && !e.IsRanged && !AttackTokens.Holds(e, victim)
                    && Geo.FlatDistance(e.Position, victim.Position) <= MenaceRadius + 1f) return true;
            return false;
        }

        static int StableHash(string s)
        {
            unchecked
            {
                int h = 23;
                if (s != null) foreach (char c in s) h = h * 31 + c;
                return h & 0x7fffffff;
            }
        }

        Vector3 Separation()
        {
            var push = Vector3.zero;
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is EnemyAgent e) || e == this || !e.IsAlive || e.State != EnemyState.Engaged) continue;
                var d = Geo.Flat(Position - e.Position);
                float m = d.magnitude;
                if (m < 1.6f && m > 1e-3f) push += d / m * (1.6f - m);
            }
            return push;
        }

        void ResolveMelee()
        {
            bool heavy = NextIsHeavy;
            _attackCount++;
            _phase = Phase.Recover;
            _phaseT = Stats.recovery;
            if (Target == null || !Target.IsAlive) return;
            float reach = Stats.range + Target.Radius + 0.3f;
            bool inReach = Geo.FlatDistance(Position, Target.Position) <= reach && Geo.AngleTo(Position, Forward, Target.Position) <= 70f;
            if (!inReach) return;
            float dmg = heavy ? Stats.heavyDamage : Stats.damage;
            var d = DamageInfo.Make(this, Target, dmg, heavy ? DamageKind.Heavy : Stats.kind, heavy ? "heavy" : "melee", heavy ? 0.6f : 0f);
            Attacked?.Invoke(this, Target);
            Ctx?.Events.AttackResolving?.Invoke(this, Target); // lets the hero parry (Riposte) before damage lands
            if (_parried)
            {
                _parried = false;
                return;
            }
            Target.TakeDamage(d);
        }

        bool _parried;

        /// <summary>Riposte hook: the defender negates this swing (called from Attacked handlers).</summary>
        public void Parry(float staggerSeconds, Agent by = null)
        {
            _parried = true;
            Status.Apply(StatusType.Staggered, staggerSeconds, by);
            Presenter?.PlayAction("stagger", staggerSeconds);
        }

        void TickRanged(float dt)
        {
            var toT = Geo.Flat(Target.Position - Position);
            float dist = toT.magnitude;
            switch (_phase)
            {
                case Phase.Aim:
                    _phaseT -= dt;
                    Motor.Move(Vector3.zero, 40f, dt);
                    Motor.FaceDirection(toT, 300f, dt);
                    _aimDir = toT.normalized;
                    if (_phaseT <= 0f) Fire();
                    return;
                case Phase.Reload:
                    _phaseT -= dt;
                    Motor.Move(Vector3.zero, 40f, dt);
                    if (_phaseT <= 0f) _phase = Phase.None;
                    return;
            }
            // Too close to the sidekick: back away (shooters hate knives).
            var sk = Ctx != null ? Ctx.Sidekick : null;
            if (sk != null && sk.IsAlive && AwareOfSidekick && Geo.FlatDistance(Position, sk.Position) < 2.5f)
            {
                Motor.Move(Geo.DirTo(sk.Position, Position) * Stats.speed * 0.8f, 30f, dt);
                Motor.FaceDirection(Geo.DirTo(Position, sk.Position), 400f, dt);
                return;
            }
            if (dist > Stats.range && !Elevated)
            {
                Motor.Move(toT.normalized * Stats.speed, 30f, dt);
                Motor.FaceDirection(toT, 400f, dt);
                return;
            }
            Motor.Move(Vector3.zero, 30f, dt);
            if (dist > Stats.range + 4f) return; // elevated shooters hold their perch
            _phase = Phase.Aim;
            _phaseT = Stats.aimTime;
            _aimDir = toT.normalized;
            Presenter?.PlayAction("aim");
        }

        void Fire()
        {
            _phase = Phase.Reload;
            _phaseT = Stats.reload;
            _attackCount++;
            Presenter?.PlayAction("shoot", 0.4f);
            var origin = Position + Vector3.up * (Elevated ? 1.4f : 1.3f) + _aimDir * 0.5f;
            var targetPoint = Target.Position + Vector3.up * 1.1f;
            var dir = (targetPoint - origin).normalized;
            HS.Skills.ProjectileSystem.Instance?.Fire(new HS.Skills.ProjectileSpec
            {
                Owner = this,
                Origin = origin,
                Direction = dir,
                Speed = Stats.projectileSpeed,
                Damage = Stats.damage,
                Kind = DamageKind.Ranged,
                Tag = Archetype == "archer" ? "arrow" : "bolt",
                MaxRange = Stats.range + 8f,
                HitsFaction = a => a.Faction == Faction.Hero || a.Faction == Faction.Sidekick,
            });
        }

        float _fleeT;

        void TickFlee(float dt)
        {
            _fleeT += dt;
            var hero = Ctx != null ? Ctx.Hero : null;
            var away = hero != null ? Geo.DirTo(hero.Position, Position) : -Forward;
            if (away == Vector3.zero) away = -Forward;
            Motor.Move(away * Stats.speed, 30f, dt);
            Motor.FaceDirection(away, 400f, dt);
            Presenter?.SetLocomotion(Motor.Speed, false);
            if (_fleeT > 5f) gameObject.SetActive(false); // gone: resolved for the encounter
        }

        void TickSurrender(float dt)
        {
            SurrenderElapsed += dt;
            Motor.Move(Vector3.zero, 40f, dt);
            Presenter?.SetLocomotion(0f, false);
            var hero = Ctx != null ? Ctx.Hero : null;
            if (hero == null || !hero.IsAlive) return;
            // The cheap shot: once the hero lowers his guard nearby (waiting on an Unready enemy), stab him.
            if (SurrenderElapsed >= SurrenderStab && Geo.FlatDistance(Position, hero.Position) <= 2.6f && !Status.Incapacitated && !Status.Has(StatusType.Blinded))
            {
                Motor.FaceInstant(Geo.DirTo(Position, hero.Position));
                Presenter?.PlayAction("attack", 0.3f);
                var d = DamageInfo.Make(this, hero, Stats.cheapShotDamage, DamageKind.Blade, "cheap_shot", 0.4f);
                Attacked?.Invoke(this, hero);
                hero.TakeDamage(d);
                SetState(EnemyState.Engaged);
            }
            else if (SurrenderElapsed > 9f)
            {
                SetState(EnemyState.Fleeing);
            }
        }
    }
}
