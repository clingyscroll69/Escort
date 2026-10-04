using System;
using HS.Core;
using UnityEngine;

namespace HS.Hero
{
    /// <summary>
    /// The hero: a rule-driven AI (HeroBrain) walking an authored route. Hero modules (Callum) supply rules, stats and
    /// combat through <see cref="HeroModule"/>. GDD §4.2.
    /// </summary>
    public sealed class HeroAgent : Agent, IWounded
    {
        public override Faction Faction => Faction.Hero;
        public override int TickOrder => TickOrders.Hero;

        public string HeroId = "callum";
        public HeroRuleSetDef RuleSet;
        public HeroBrain Brain { get; } = new HeroBrain();
        public HeroContext Context { get; private set; }
        public RouteFollower Route { get; } = new RouteFollower();
        public Stage Stage { get; private set; } = Stage.S0;
        public HeroModule Module { get; private set; }

        public float RouteSpeed = 4.4f;
        public float CombatSpeed = 5.0f;
        public float TurnSpeed = 540f;
        public float SpeedMultiplier = 1f;
        /// <summary>Held still while the sidekick dresses a wound (Bandage channel).</summary>
        public bool TreatmentHold;
        public float DamageMultiplier = 1f;
        public float MinThresholdPause = 1.5f, MaxThresholdWait = 6f, SidekickNearRange = 9f;

        public event Action<Stage> StageApplied;
        public WoundSet Wounds { get; } = new WoundSet();
        /// <summary>Attrition (chapters 2–4): fed by the sidekick, refilled at camp.</summary>
        public Hunger Hunger { get; } = new Hunger();
        public int WoundCount => Wounds.Count;
        public bool HasMinorWound => Wounds.HasMinor;
        public bool TreatMinorWound() => Wounds.TreatMinor();
        public bool Crippled => Wounds.Crippled(WoundT);
        WoundTuning WoundT => (Ctx != null ? Ctx.Tuning : Tuning.LoadDefault()).wounds;
        float _feverAcc;
        public string ActiveRuleId => Brain.ActiveId;
        public string ActiveIcon => Brain.ActiveIcon;
        public string ActiveLabel => Brain.Active?.Label ?? "";

        Vector3 _desiredVelocity;
        bool _movedThisTick;

        protected override void Awake()
        {
            base.Awake();
            Context = new HeroContext { Hero = this, Route = Route };
            if (Presenter == null) Presenter = GetComponentInChildren<IAgentPresenter>();
            Module = GetComponent<HeroModule>();
            Module?.Bind(this);
            if (GetComponent<FeedInteraction>() == null) gameObject.AddComponent<FeedInteraction>();
            if (Health == null || Health.Max <= 0f) Health = new Health(260f);
            Wounds.Added += w => OnWoundsChanged(w, true);
            Wounds.Removed += w => OnWoundsChanged(w, false);
        }

        void OnWoundsChanged(WoundType w, bool added)
        {
            ApplyWoundEffects();
            Ctx?.Events.WoundChanged?.Invoke(this, w.ToString(), added);
        }

        /// <summary>Recompute every wound malus (GDD §4.2 table + Crippled).</summary>
        public void ApplyWoundEffects()
        {
            var t = WoundT;
            SpeedMultiplier = Wounds.SpeedMul(t);
            Health.SetMaxMultiplier(Wounds.MaxHpMul(t));
            Brain.ReactionDelay = Wounds.ReactionDelay(t);
            if (Module is HS.Hero.Callum.CallumModule cm) cm.WoundDamageMul = Wounds.DamageMul(t);
        }

        void Start()
        {
            if (Brain.Rules.Count == 0) ApplyStage(Stage);
            foreach (var d in GetComponentsInChildren<HS.Presentation.RuleIconDisplay>()) d.IconSource = () => IsAlive ? ActiveIcon : null;
            var cone = GetComponent<HS.Presentation.WitnessConeView>();
            if (Module is HS.Hero.Callum.CallumModule cm && cone != null)
            {
                cone.Angle = () => cm.CurrentWitnessAngle;
                cone.Range = () => cm.CurrentWitnessRange;
                cone.Visible = () => IsAlive;
                cone.Emphasis = () => Ctx != null && Ctx.Sidekick is HS.Sidekick.SidekickAgent sk && (sk.IsSneaking || sk.LastCommand.SkillHeld || sk.LastCommand.Skill >= 0);
                cm.Caught += (e, stone) => cone.Flash();
            }
        }

        public void SetMaxHp(float hp, bool refill) => Health.SetBaseMax(hp, refill);

        public override int MeleeSlots => Module != null ? Module.MeleeSlots : base.MeleeSlots;

        /// <summary>Swap to the rule list for a Stage (visible behaviour change; GDD §11.3.1).</summary>
        public void ApplyStage(Stage s)
        {
            Stage = s;
            Context.Stage = s;
            var entries = RuleSet != null ? RuleSet.For(s) : null;
            Brain.SetRules(entries != null ? HeroRuleFactory.Build(entries) : HeroRuleFactory.Build(new[] { new RuleEntry("threshold_pause"), new RuleEntry("follow_route") }), Context);
            StageApplied?.Invoke(s);
        }

        protected override void OnSimTick(float dt)
        {
            if (!IsAlive) return;
            TickFever(dt);
            Hunger.Tick(dt);
            _movedThisTick = false;
            Module?.PreTick(dt);
            if (Status.Incapacitated)
            {
                Hold(dt);
            }
            else
            {
                Brain.Tick(Context, dt);
            }
            if (!_movedThisTick) Hold(dt);
            Module?.PostTick(dt);
            Presenter?.SetLocomotion(Motor.Speed, false);
        }

        // ---------------- movement helpers for rules ----------------

        public void StepRoute(float dt, float speed)
        {
            if (TreatmentHold)
            {
                Hold(dt);
                return;
            }
            var dir = Route.Tick(Position, dt, Context.SidekickNear(SidekickNearRange), MinThresholdPause, MaxThresholdWait);
            if (speed > 0f && dir != Vector3.zero) Move(dir * speed, dt);
        }

        public void Move(Vector3 planarVelocity, float dt)
        {
            _movedThisTick = true;
            Motor.SpeedMultiplier = SpeedMultiplier;
            Motor.Move(planarVelocity, 30f, dt);
            if (planarVelocity.sqrMagnitude > 0.01f) Motor.FaceDirection(planarVelocity, TurnSpeed, dt);
        }

        public void MoveTowards(Vector3 point, float speed, float stopDistance, float dt)
        {
            var d = Geo.FlatDistance(Position, point);
            if (d <= stopDistance)
            {
                Hold(dt);
                FaceTowards(point, dt);
                return;
            }
            Move(Geo.DirTo(Position, point) * speed, dt);
        }

        public void Hold(float dt)
        {
            _movedThisTick = true;
            Motor.Move(Vector3.zero, 30f, dt);
        }

        public void FaceTowards(Vector3 point, float dt)
        {
            var dir = Geo.DirTo(Position, point);
            if (dir != Vector3.zero) Motor.FaceDirection(dir, TurnSpeed, dt);
        }

        protected override float ModifyIncomingDamage(DamageInfo d)
        {
            // Enemies hit harder each chapter (x2, matching his HP), so the danger of a blow stays the same (campaign spec §2).
            // Hazards already scale with his max HP; fever is his own.
            if (d.Source != null && d.Source.Faction == Faction.Hostile) d.Amount *= ChapterTier.EnemyDamageToHero(Ctx != null ? Ctx.Chapter : 1);
            return Module != null ? Module.ModifyIncoming(d) : d.Amount;
        }

        protected override void OnHurt(DamageInfo d, float applied)
        {
            base.OnHurt(d, applied);
            // A hit of 25%+ of his max HP wounds him; spike traps always do (GDD §4.2 "hazards can too"). A tripwire is a
            // stumble and an alarm, not a wound — one trap corridor must not cripple him on its own.
            bool hazard = (d.Kind == DamageKind.Trap || d.Kind == DamageKind.Environment) && d.Tag != "tripwire";
            if (d.Kind != DamageKind.Fever && (hazard || applied >= WoundT.woundThresholdFraction * Health.Max - 1e-3f))
                Wounds.Add(WoundSet.TypeFor(d.Kind));
            Module?.OnHurt(d, applied);
        }

        void TickFever(float dt)
        {
            float frac = Wounds.FeverDrainFraction(WoundT);
            if (frac <= 0f) return;
            _feverAcc += frac * Health.Max * dt;
            if (_feverAcc < 1f) return;
            float amount = Mathf.Floor(_feverAcc);
            _feverAcc -= amount;
            float floor = WoundT.feverFloor * Health.Max;
            amount = Mathf.Min(amount, Health.Current - floor);
            if (amount > 0f) TakeDamage(DamageInfo.Make(null, this, amount, DamageKind.Fever, "fever"));
        }

        protected override void OnDied(DamageInfo d)
        {
            base.OnDied(d);
            Module?.OnDied(d);
        }
    }

    /// <summary>Hero-specific systems (combat, meters, witness) attached next to HeroAgent.</summary>
    public abstract class HeroModule : MonoBehaviour
    {
        protected HeroAgent Hero { get; private set; }
        public virtual void Bind(HeroAgent hero) => Hero = hero;
        public virtual void PreTick(float dt) { }
        public virtual void PostTick(float dt) { }
        public virtual float ModifyIncoming(DamageInfo d) => d.Amount;
        public virtual void OnHurt(DamageInfo d, float applied) { }
        public virtual void OnDied(DamageInfo d) { }
        public virtual int MeleeSlots => 2;
    }
}
