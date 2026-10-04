using System;
using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Sidekick;
using UnityEngine;

namespace HS.Hero.Callum
{
    /// <summary>
    /// Sir Callum the Honorable (GDD §6.1). Belief: a fight is only worth winning if it's fair.
    /// Owns his duel state (challenge → salute → fight / wait on the Unready → spare), sword combat with Riposte
    /// (Strike I: parry-counter 2×), Honor (witnessed dishonour; below 40 he deals −25% and scolds you) and the
    /// witness cone (120°, 12 m, line of sight; Quiet Feet narrows it; active stones count as witnesses).
    /// </summary>
    public sealed class CallumModule : HeroModule
    {
        public CallumTuning T { get; private set; }
        public EnemyAgent Challenged { get; private set; }
        public float SaluteRemaining { get; private set; }
        public bool Saluting => SaluteRemaining > 0f;
        public bool DuelActive => Challenged != null && !Saluting;
        public float WaitT { get; private set; }
        public float Honor { get; private set; }
        public bool HonorLow => Honor < T.honorLow;
        public float DoubtRemaining { get; private set; }
        public float FallbackCooldown { get; private set; }
        /// <summary>Committed retreat to a chokepoint in progress (no thrashing back and forth).</summary>
        public bool FallingBack { get; private set; }
        /// <summary>Holding a chokepoint after a fall-back: bandits must come one at a time.</summary>
        public bool HoldingNarrows => FallbackCooldown > 0f && !FallingBack && _hasHoldPoint && Geo.FlatDistance(Hero.Position, _holdPoint) <= 2.5f;
        public float RiposteCooldown { get; private set; }
        public int CaughtThisFight { get; private set; }
        public float WoundDamageMul = 1f;
        /// <summary>Signature skills unlocked so far (GDD §4.2 schedule; set per chapter by the campaign).</summary>
        public Signature Unlocks { get; private set; } = Signature.StrikeI;
        /// <summary>Between-room recovery, a fraction of max HP (Ch1 50%, Ch2+ 30%).</summary>
        public float Recovery { get; private set; }
        public float RiposteMultiplier => (Unlocks & Signature.StrikeII) != 0 ? T.riposteMultiplierII : T.riposteMultiplier;
        public bool Attacking => _atk != Atk.None;

        /// <summary>Witnessed dishonour: (event, witnessed by a stone rather than Callum's own eyes).</summary>
        public event Action<SabotageEvent, bool> Caught;
        /// <summary>Hit his target before the salute finished (GDD: Spoiled duel −2).</summary>
        public event Action<EnemyAgent> SpoiledDuel;
        /// <summary>A dirty deed nobody (Callum or stone) saw — for Unseen assist / Averted cheat judges.</summary>
        public event Action<SabotageEvent> UnseenDeed;
        public event Action<EnemyAgent> DuelBegan;
        public event Action<EnemyAgent, DuelEndReason> DuelFinished;

        enum Atk { None, Windup, Recover }
        Atk _atk;
        float _atkT;
        int _combo;
        readonly List<Agent> _scratch = new List<Agent>();
        RunContext _ctx;
        Vector3 _fallbackPoint, _holdPoint;
        bool _hasHoldPoint;
        float _fallbackT;
        readonly Dictionary<string[], float> _barkSetAt = new Dictionary<string[], float>();
        readonly Dictionary<string[], int> _barkSetIx = new Dictionary<string[], int>();
        float _lastBarkAt = -99f;

        /// <summary>Stones (Task 12) answer "did an active stone see this point?".</summary>
        public Func<Vector3, bool> StoneWitness;

        // ------------------------------------------------------------------ the rigged duel (GDD §6.1 boss)
        /// <summary>Formal "no aid" terms in force: any harm the sidekick does that he witnesses breaks them (Major).</summary>
        public bool NoAidTerms;
        /// <summary>The Oath circle (radius 0 = none). At S0 he orders the sidekick out whenever he sees her inside.</summary>
        public Vector3 OathCenter;
        public float OathRadius;
        /// <summary>S1+: after the first volley he fights with his guard up against arrows.</summary>
        public bool GuardingArrows { get; private set; }
        public int ArrowsTaken { get; private set; }
        public const float ArrowGuardMul = 0.4f;
        float _circleNagT;

        public override void Bind(HeroAgent hero)
        {
            base.Bind(hero);
            T = (RunContext.Current != null ? RunContext.Current.Tuning : Tuning.LoadDefault()).callum;
            Honor = T.honorMax;
            Recovery = T.secondWind;
            int ch = RunContext.Current != null ? RunContext.Current.Chapter : 1;
            hero.SetMaxHp(T.MaxHp(ch), true);
            hero.RouteSpeed = T.routeSpeed;
            hero.CombatSpeed = T.combatSpeed;
            hero.TurnSpeed = T.turnSpeed;
            hero.MinThresholdPause = T.thresholdPause;
            hero.MaxThresholdWait = T.thresholdMaxWait;
            hero.SidekickNearRange = T.thresholdSidekickNear;
        }

        /// <summary>A new chapter (campaign): his power tier, his signature skills, the road's attrition. Starts rested.</summary>
        public void ApplyChapter(int chapter, Signature unlocks, float recovery)
        {
            Unlocks = unlocks;
            Recovery = recovery;
            Hero.SetMaxHp(T.MaxHp(chapter), true);
            _lastRoom = -1; // room indices start again at 0
        }

        void OnEnable()
        {
            _ctx = RunContext.Current;
            if (_ctx == null) return;
            _ctx.Events.Sabotage += OnSabotage;
            _ctx.Events.Damage += OnDamage;
            _ctx.Events.CoverStory += OnCoverStory;
            _ctx.Events.RoomCleared += OnRoomCleared;
            _ctx.Events.AttackResolving += OnAttackResolving;
            _ctx.Events.WoundChanged += OnWoundChanged;
            _ctx.Events.RoomEntered += OnRoomEntered;
        }

        int _lastRoom = -1;

        /// <summary>
        /// Second wind: walking into the next room he has caught his breath (wounds stay — those need dressing or the
        /// camp). Granted on leaving a room, not on clearing it: rooms with shooters he won't chase never "clear".
        /// </summary>
        void OnRoomEntered(int room)
        {
            if (room > _lastRoom && _lastRoom >= 0 && Hero.IsAlive) Hero.Health.Heal(Hero.Health.Max * Recovery);
            _lastRoom = Mathf.Max(_lastRoom, room);
        }

        void OnWoundChanged(Agent who, string type, bool added)
        {
            if (who != Hero || !added || !Hero.IsAlive) return;
            if (Hero.Crippled) Bark(CrippledLines, 2);
            else switch (type)
            {
                case nameof(WoundType.CrackedRibs): Bark(RibsLines, 1); break;
                case nameof(WoundType.Concussion): Bark(ConcussionLines, 1); break;
                case nameof(WoundType.Fever): Bark(FeverLines, 1); break;
                case nameof(WoundType.SprainedAnkle): Bark(AnkleLines, 1); break;
                default: Bark(ArmLines, 1); break;
            }
        }

        void OnDisable()
        {
            if (_ctx == null) return;
            _ctx.Events.Sabotage -= OnSabotage;
            _ctx.Events.Damage -= OnDamage;
            _ctx.Events.CoverStory -= OnCoverStory;
            _ctx.Events.RoomCleared -= OnRoomCleared;
            _ctx.Events.AttackResolving -= OnAttackResolving;
            _ctx.Events.WoundChanged -= OnWoundChanged;
            _ctx.Events.RoomEntered -= OnRoomEntered;
        }

        public Stage Stage => Hero != null ? Hero.Stage : Stage.S0;
        public float WaitCap => Stage >= Stage.S1 ? T.waitUnreadyS1 : T.waitUnreadyS0;

        public float OutgoingDamage(float baseDamage) =>
            baseDamage * (HonorLow ? T.honorLowDamageMul : 1f) * WoundDamageMul * Hero.DamageMultiplier;

        // --------------------------------------------------------------------------------------------- tick

        public override void PreTick(float dt)
        {
            if (DoubtRemaining > 0f) DoubtRemaining -= dt;
            if (FallbackCooldown > 0f) FallbackCooldown -= dt;
            if (RiposteCooldown > 0f) RiposteCooldown -= dt;
            ValidateChallenge();
            if (Challenged != null && !Challenged.IsUnreadyFor(Hero) && WaitT > 0f && Challenged.State != EnemyState.Surrendered) WaitT = 0f;
            TickOathCircle(dt);
        }

        void TickOathCircle(float dt)
        {
            if (_circleNagT > 0f) _circleNagT -= dt;
            if (!NoAidTerms || OathRadius <= 0f || Stage >= Stage.S1 || !Hero.IsAlive) return;
            var sk = _ctx?.Sidekick as SidekickAgent;
            if (sk == null || !sk.IsAlive || Geo.FlatDistance(sk.Position, OathCenter) > OathRadius) return;
            if (_circleNagT > 0f || !Sees(sk.Position, sk.IsSneaking)) return;
            // S0: the terms are sacred — out of the circle. He keeps a closer eye on you afterwards (Doubt widens his cone).
            _circleNagT = 4f;
            DoubtRemaining = 8f;
            Bark(CircleLines, 2);
        }

        void ValidateChallenge()
        {
            if (Challenged == null) return;
            if (!Challenged.IsAlive) EndDuel(DuelEndReason.TargetDied);
            else if (!Challenged.gameObject.activeInHierarchy || Challenged.State == EnemyState.Fleeing) EndDuel(DuelEndReason.TargetFled);
            else if (Challenged.State == EnemyState.Spared) EndDuel(DuelEndReason.Abandoned);
        }

        public void StartChallenge(EnemyAgent target)
        {
            Challenged = target;
            target.DuelOpponent = Hero;
            SaluteRemaining = T.saluteTime;
            WaitT = 0f;
            CaughtThisFight = 0;
            _atk = Atk.None;
            Hero.Presenter?.SetFlag("combat", true);
            Hero.Presenter?.PlayAction("salute");
            Bark(ChallengeLines, 1);
            _ctx?.Events.DuelStarted?.Invoke(Hero, target);
            DuelBegan?.Invoke(target);
        }

        public void EndDuel(DuelEndReason reason)
        {
            var t = Challenged;
            Challenged = null;
            SaluteRemaining = 0f;
            WaitT = 0f;
            _atk = Atk.None;
            if (t != null && t.DuelOpponent == Hero) t.DuelOpponent = null;
            Hero.Presenter?.PlayAction("none");
            _ctx?.Events.DuelEnded?.Invoke(Hero, t, reason);
            DuelFinished?.Invoke(t, reason);
        }

        public void TickSalute(float dt)
        {
            Hero.Hold(dt);
            if (Challenged != null) Hero.FaceTowards(Challenged.Position, dt);
            SaluteRemaining -= dt;
            if (SaluteRemaining <= 0f)
            {
                SaluteRemaining = 0f;
                Hero.Presenter?.PlayAction("none");
                _ctx?.Events.SaluteFinished?.Invoke(Hero);
            }
        }

        public void TickWait(float dt)
        {
            Hero.Hold(dt);
            if (Challenged == null) return;
            Hero.FaceTowards(Challenged.Position, dt);
            if (WaitT == 0f)
            {
                Hero.Presenter?.PlayAction("guard");
                Bark(Challenged.State == EnemyState.Surrendered ? SurrenderLines : WaitLines, 1);
            }
            WaitT += dt;
            if (WaitT >= WaitCap && Challenged.State == EnemyState.Surrendered)
            {
                // Honour: a yielded man is spared.
                Bark(SpareLines, 1);
                var t = Challenged;
                t.Spare();
                EndDuel(DuelEndReason.Abandoned);
            }
        }

        /// <summary>Reset the Unready wait when the target is ready again (e.g. blindness wore off).</summary>
        public void ResetWait() => WaitT = 0f;

        public void TickFight(float dt)
        {
            var target = Challenged;
            if (target == null) return;
            var toT = Geo.Flat(target.Position - Hero.Position);
            float dist = toT.magnitude;
            float reach = T.attackRange + target.Radius * 0.5f;
            switch (_atk)
            {
                case Atk.Windup:
                    Hero.Hold(dt);
                    Hero.FaceTowards(target.Position, dt);
                    _atkT -= dt;
                    if (_atkT <= 0f) Strike(target);
                    return;
                case Atk.Recover:
                    Hero.Hold(dt);
                    _atkT -= dt;
                    if (_atkT <= 0f) _atk = Atk.None;
                    return;
            }
            if (dist > reach * 0.9f)
            {
                Hero.Move(toT.normalized * Hero.CombatSpeed, dt);
                return;
            }
            Hero.Hold(dt);
            Hero.FaceTowards(target.Position, dt);
            // Duellist's patience: with Riposte ready and his opponent telegraphing a swing, hold guard and parry.
            if (RiposteCooldown <= 0f && target.IsAttackWindup)
            {
                Hero.Presenter?.PlayAction("block", 0.6f);
                return;
            }
            _atk = Atk.Windup;
            _atkT = T.attackWindup;
            Hero.Presenter?.PlayAction(_combo % 3 == 2 ? "attack3" : _combo % 2 == 0 ? "attack" : "attack2", T.attackWindup + T.attackRecovery);
        }

        void Strike(EnemyAgent target)
        {
            _atk = Atk.Recover;
            _atkT = T.attackRecovery;
            _combo++;
            if (!target.IsAlive) return;
            bool inReach = Geo.FlatDistance(Hero.Position, target.Position) <= T.attackRange + target.Radius + 0.35f
                           && Geo.AngleTo(Hero.Position, Hero.Forward, target.Position) <= 75f;
            if (!inReach) return;
            int ch = _ctx != null ? _ctx.Chapter : 1;
            var d = DamageInfo.Make(Hero, target, OutgoingDamage(T.Damage(ch)), DamageKind.Blade, "sword", 0.25f);
            target.TakeDamage(d);
        }

        /// <summary>Strike I — Riposte: parry his challenged opponent's swing and counter for 2×.</summary>
        void OnAttackResolving(Agent attacker, Agent victim)
        {
            if (victim != Hero || attacker != Challenged || Challenged == null) return;
            if (RiposteCooldown > 0f || Saluting || Hero.Status.Incapacitated || _atk == Atk.Windup) return;
            if (Geo.AngleTo(Hero.Position, Hero.Forward, attacker.Position) > 60f) return;
            RiposteCooldown = T.riposteCooldown;
            Challenged.Parry(0.9f, Hero);
            int ch = _ctx != null ? _ctx.Chapter : 1;
            var d = DamageInfo.Make(Hero, Challenged, OutgoingDamage(T.Damage(ch)) * RiposteMultiplier, DamageKind.Blade, "riposte", 0.6f);
            Hero.Presenter?.PlayAction("attack3", 0.5f);
            HS.Presentation.Vfx.Burst(HS.Presentation.VfxKind.Sparks, Hero.Position + Hero.Forward * 0.9f + Vector3.up * 1.3f);
            Challenged.TakeDamage(d);
            Bark(RiposteLines);
        }

        /// <summary>Melee enemies on him (swinging or circling for an opening).</summary>
        public int Engagers => AgentRegistry.Count(a => a is EnemyAgent e && e.IsActive && e.Target == Hero && !e.IsRanged
                                                        && Geo.FlatDistance(e.Position, Hero.Position) <= T.engageRadius);

        public bool NeedsFallback(out Vector3 chokepoint)
        {
            if (FallingBack)
            {
                chokepoint = _fallbackPoint;
                return true;
            }
            chokepoint = default;
            if (FallbackCooldown > 0f) return false;
            if (Engagers < T.fallbackEngagers) return false;
            if (!Hero.Route.TryNearestChokepoint(Hero.Position, out chokepoint)) return false;
            return Geo.FlatDistance(chokepoint, Hero.Position) > 1.5f && Geo.FlatDistance(chokepoint, Hero.Position) < 14f;
        }

        /// <summary>Start a committed retreat: he runs for the narrows (max 4 s), then holds it for a while.</summary>
        public void BeginFallback(Vector3 chokepoint)
        {
            if (FallingBack) return;
            FallingBack = true;
            _fallbackPoint = chokepoint;
            _fallbackT = 0f;
            _atk = Atk.None;
            Bark(FallbackLines, 1);
        }

        public void TickFallback(Vector3 chokepoint, float dt)
        {
            _fallbackT += dt;
            Hero.MoveTowards(chokepoint, Hero.CombatSpeed, 1.2f, dt);
            if (Geo.FlatDistance(chokepoint, Hero.Position) <= 1.5f || _fallbackT >= 4f)
            {
                FallingBack = false;
                FallbackCooldown = T.fallbackHold;
                _holdPoint = chokepoint;
                _hasHoldPoint = true;
            }
        }

        public override int MeleeSlots => HoldingNarrows ? 1 : 2;

        public EnemyAgent PickChallengeTarget()
        {
            EnemyAgent best = null;
            float bestD = T.challengeRange * T.challengeRange;
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is EnemyAgent e) || !e.IsAlive || !e.IsActive) continue;
                if (e.Elevated && e.IsRanged) continue; // shooters on a perch won't come down to be challenged
                float d = Geo.FlatSqrDistance(e.Position, Hero.Position);
                // deterministic tie-break by id
                if (d < bestD - 1e-4f || (Mathf.Abs(d - bestD) <= 1e-4f && best != null && string.CompareOrdinal(e.AgentId, best.AgentId) < 0))
                {
                    bestD = d;
                    best = e;
                }
            }
            return best;
        }

        // --------------------------------------------------------------------------------------------- honour

        /// <summary>Is this position seen by Callum (cone narrowed while the sidekick sneaks with Quiet Feet)?</summary>
        public bool Sees(Vector3 point, bool sneaky)
        {
            float angle = T.witnessAngle, range = T.witnessRange;
            if (sneaky && _ctx?.Sidekick is SidekickAgent sk)
            {
                angle *= sk.QuietFeetConeMul;
                range *= T.quietFeetRangeMul;
            }
            if (DoubtRemaining > 0f) angle = Mathf.Min(360f, angle * 1.35f); // he's watching you
            return WitnessCone.Sees(Hero.Position + Vector3.up * 1.6f, Hero.Forward, angle, range, point);
        }

        public float CurrentWitnessAngle
        {
            get
            {
                float angle = T.witnessAngle;
                if (_ctx?.Sidekick is SidekickAgent sk && sk.IsSneaking) angle *= sk.QuietFeetConeMul;
                if (DoubtRemaining > 0f) angle = Mathf.Min(360f, angle * 1.35f);
                return angle;
            }
        }

        public float CurrentWitnessRange => _ctx?.Sidekick is SidekickAgent sk && sk.IsSneaking ? T.witnessRange * T.quietFeetRangeMul : T.witnessRange;

        /// <summary>Would this deed be witnessed — by his own eyes (deed or doer in his cone) or an active stone?</summary>
        public bool Witnesses(Vector3 deed, Vector3 actor, out bool byStone)
        {
            bool sneaky = _ctx?.Sidekick is SidekickAgent sk && sk.IsSneaking;
            bool seen = Sees(deed, sneaky) || Sees(actor, sneaky);
            byStone = !seen && StoneWitness != null && StoneWitness(deed);
            return seen || byStone;
        }

        void OnSabotage(SabotageEvent e)
        {
            if (!Hero.IsAlive) return;
            if (Witnesses(e.Position, e.ActorPosition, out bool byStone)) Witnessed(e, byStone);
            else UnseenDeed?.Invoke(e);
        }

        void OnDamage(DamageInfo d, float applied)
        {
            if (!(d.Source is SidekickAgent) || d.Target == null || d.Target == Hero) return;
            if (d.Tag == "loosen_bolt" || d.Tag == "pocket_sand") return; // reported via Sabotage
            var target = d.Target as EnemyAgent;
            if (target == null) return;
            if (target == Challenged && Saluting)
            {
                SpoiledDuel?.Invoke(target);
                Bark(SpoiledLines, 1);
                return;
            }
            var sev = SabotageSeverity.None;
            if (NoAidTerms) sev = SabotageSeverity.Major;                              // "no aid": any help breaks the terms
            else if (target.IsHelpless(Hero, d.Source)) sev = SabotageSeverity.Major; // striking the helpless
            else if (target == Challenged && DuelActive) sev = SabotageSeverity.Minor; // interfering in his duel
            if (sev == SabotageSeverity.None) return;                                  // a fair fight of her own is her business
            OnSabotage(new SabotageEvent
            {
                Tag = d.Tag, Severity = sev, Position = d.Point, ActorPosition = d.Source.Position, Victim = target,
                Time = _ctx != null ? _ctx.SimTime : 0f,
            });
        }

        void Witnessed(SabotageEvent e, bool byStone)
        {
            float loss = e.Severity == SabotageSeverity.Major ? T.honorLossMajor : T.honorLossMinor;
            if (e.Severity == SabotageSeverity.Minor && Stage >= Stage.S1) loss *= 0.5f; // S1: minor assists halved
            bool wasLow = HonorLow;
            Honor = Mathf.Max(0f, Honor - loss);
            DoubtRemaining = 8f;
            if (e.Severity == SabotageSeverity.Major) CaughtThisFight++;
            Caught?.Invoke(e, byStone);
            if (!Attacking) Hero.Presenter?.PlayAction("scold", 1.6f);
            if (HonorLow && !wasLow) Bark(LowHonorLines, 3);
            else if (NoAidTerms) Bark(TermsLines, 2);
            else if (byStone) Bark(StoneLines, 2);
            else Bark(CaughtLines(e), 2);
        }

        /// <summary>He names what he saw: the trick itself, or what made the blow foul (judged on the victim as it was).</summary>
        public static string[] CaughtLines(SabotageEvent e)
        {
            if (e.Tag == "pocket_sand") return SandLines;
            if (e.Tag == "loosen_bolt") return CollapseLines;
            if (e.Severity == SabotageSeverity.Minor) return e.Tag == "crossbow" ? DuelBoltLines : DuelKnifeLines;
            if (!(e.Victim is EnemyAgent v)) return CaughtMajorLines;
            if (v.State == EnemyState.Surrendered) return HitYieldedLines;
            if (v.State == EnemyState.Fleeing) return HitFleeingLines;
            if (v.Status.Has(StatusType.Sleeping)) return HitSleepingLines;
            if (v.Status.Has(StatusType.Blinded)) return HitBlindedLines;
            return HitReelingLines; // staggered or stunned by someone else
        }

        void OnCoverStory(float restore, float window)
        {
            DoubtRemaining = 0f;
            Honor = Mathf.Min(T.honorMax, Honor + restore);
            Bark(CoverAcceptedLines, 1);
        }

        void OnRoomCleared(int room)
        {
            Honor = Mathf.Min(T.honorMax, Honor + T.honorRoomClearRegen);
            Hero.Presenter?.SetFlag("combat", false);
            Bark(ReciteLines);
        }

        /// <summary>S0 credits luck when a cheater falls unseen (GDD Ch2 beat: "Fortune favors the just").</summary>
        public void CreditLuck() => Bark(Stage >= Stage.S2 ? ThanksLines : LuckLines, 1);

        /// <summary>The first meeting (GDD §8): he introduces himself, and his first rule.</summary>
        public void Greet() => Bark(GreetLines, 2);

        public override float ModifyIncoming(DamageInfo d)
        {
            if (d.Tag != "arrow") return d.Amount;
            ArrowsTaken++;
            if (Stage >= Stage.S1)
            {
                if (GuardingArrows)
                {
                    if (!Attacking) Hero.Presenter?.PlayAction("block", 0.4f);
                    return d.Amount * ArrowGuardMul;
                }
                GuardingArrows = true; // the first volley lands in full; then his guard comes up
                Bark(GuardLines, 2);
            }
            else if (ArrowsTaken == 1) Bark(StoicLines, 1); // S0: he doesn't flinch
            return d.Amount;
        }

        public override void OnHurt(DamageInfo d, float applied)
        {
            if (d.Tag == "cheap_shot" || d.Tag == "ambush") Bark(TreacheryLines, 2);
            else if (d.Source is EnemyAgent e && e.IsRanged && e.Elevated && !NoAidTerms) Bark(CowardLines);
        }

        /// <summary>
        /// Paced barks. Tiers: 2+ always speaks (caught, treachery, low Honor, crippled); 1 = explains a rule change
        /// (challenge, wait/surrender, spare, fall-back, spoiled salute, wounds) — the player must hear why he acts, so
        /// these skip the chatter gap; 0 = chatter (ripostes, recitals, taunts) keeps a 3.5 s gap. A line set rests 9 s
        /// (5 s for tier 1+) so nothing repeats on a loop.
        /// </summary>
        public void Bark(string[] lines, int priority = 0)
        {
            if (lines == null || lines.Length == 0 || _ctx == null) return;
            float now = _ctx.SimTime;
            float setRest = priority >= 1 ? 5f : 9f;
            if (priority < 2)
            {
                if (_barkSetAt.TryGetValue(lines, out var at) && now - at < setRest) return;
                if (priority == 0 && now - _lastBarkAt < 3.5f) return;
            }
            _barkSetAt[lines] = now;
            _lastBarkAt = now;
            _barkSetIx.TryGetValue(lines, out var ix);
            _barkSetIx[lines] = ix + 1;
            _ctx.Events.RaiseBark("callum", lines[ix % lines.Length], 2.6f, priority);
        }

        public void SetHonor(float h) => Honor = Mathf.Clamp(h, 0f, T.honorMax);

        // --------------------------------------------------------------------------------------------- barks
        // Draft copy for the owner to rewrite (GDD §8: write barks yourself; AI-written text needs disclosure).
        // Tone: sincere and competent; the joke is a rule meeting a situation.

        static readonly string[] GreetLines = { "Callum, of the Code. Keep up, sidekick, and fight fair." };
        static readonly string[] ChallengeLines = { "You there! Face me, and fight with honour.", "I am Callum of the Code. Draw, and be judged.", "One of you. Me. Fairly. Now." };
        static readonly string[] WaitLines = { "Rise. I'll not strike a man who can't see.", "Get your feet under you. I'll wait.", "Take your time. Honour is patient." };
        static readonly string[] SurrenderLines = { "You yield? Then stay down, and I'll stay my hand.", "A yield is a yield. On your knees, then." };
        static readonly string[] SpareLines = { "Go, and sin no more.", "Your life is your own. Spend it better." };
        static readonly string[] RiposteLines = { "Parried!", "Too slow.", "Mind your guard!" };
        static readonly string[] FallbackLines = { "To the narrows! One at a time!", "Back — make them come single file!" };
        static readonly string[] SpoiledLines = { "I had not finished my salute!", "The salute, sidekick! The salute!" };
        static readonly string[] CaughtMajorLines = { "What was that?! We do not fight like thieves!", "I saw that. The Code saw that." };
        static readonly string[] SandLines = { "Sand? SAND? Have you no shame?", "Sand in a man's eyes? We are not street urchins!" };
        static readonly string[] CollapseLines = { "You dropped that on him?!", "Masonry is not a weapon, sidekick!" };
        static readonly string[] DuelKnifeLines = { "Let a man finish his own fight.", "Sheathe that knife. This duel is mine." };
        static readonly string[] DuelBoltLines = { "Hold your aim. This duel is mine.", "Lower that crossbow. He's mine to fight." };
        static readonly string[] HitYieldedLines = { "He yielded! A yield is sacred!", "You struck a man on his knees?!" };
        static readonly string[] HitFleeingLines = { "In the back, as he ran?! Never the back!", "He was running! Let him run!" };
        static readonly string[] HitSleepingLines = { "He was asleep! Wake a man before you fight him!", "A sleeping man? Have you no shame?" };
        static readonly string[] HitBlindedLines = { "He couldn't even see you!", "A blinded man? The Code saw that." };
        static readonly string[] HitReelingLines = { "He was reeling! Let him find his feet!", "Not while he's down! The Code forbids it." };
        static readonly string[] StoneLines = { "The stones saw that. Everyone will have seen that.", "That will be all over the taverns by nightfall." };
        static readonly string[] LowHonorLines = { "My blade feels heavy with your tricks.", "I cannot fight well with a stained conscience." };
        static readonly string[] CoverAcceptedLines = { "...Very well. Perhaps I misjudged.", "Hm. The wind, you say.", "I shall choose to believe you." };
        static readonly string[] ReciteLines = { "\"A knight's word is his shield; a knight's shield is his word.\"", "\"Strike the ready. Spare the yielded. Never the back.\"", "\"The Code does not bend, so that I need not.\"" };
        static readonly string[] LuckLines = { "Did you see that archer fall? Fortune favours the just.", "The gods keep their own score, it seems.", "Luck rides with the honest." };
        static readonly string[] ThanksLines = { "...That wasn't luck, was it.", "I saw nothing. Thank you for that." };
        static readonly string[] CircleLines = { "Out of the circle! Those are the terms.", "Sidekick. The circle. OUT.", "The terms, sidekick! Out!" };
        static readonly string[] TermsLines = { "We swore no aid! Stand down!", "No aid — I gave my word!", "You shame the oath!" };
        static readonly string[] GuardLines = { "Archers! Then I'll fight with my guard up.", "Treachery from the gallery — guard up!" };
        static readonly string[] StoicLines = { "Arrows? No matter. The duel is what matters.", "Let them shoot. I fight HIM." };
        static readonly string[] RibsLines = { "Hngh — the ribs. It's nothing.", "Something cracked. Not my resolve." };
        static readonly string[] ConcussionLines = { "Why are there two of you?", "The road... is tilting. Carry on." };
        static readonly string[] FeverLines = { "Is it warm, or is it me?", "A scratch. It's a scratch. It's warm." };
        static readonly string[] AnkleLines = { "Twisted it. Onward.", "Who leaves spikes on a public road?!" };
        static readonly string[] ArmLines = { "My sword arm — no matter. I have another.", "Just a cut. Just a cut." };
        static readonly string[] CrippledLines = { "I can still walk. Mostly.", "Do not carry me. I forbid it." };
        static readonly string[] CowardLines = { "Come down and face me, coward!", "Arrows from a rooftop. How brave.", "Hide up there, then. I have a road to walk." };
        static readonly string[] TreacheryLines = { "Treachery!", "A knife in the back — of course.", "Cheat!" };
    }
}
