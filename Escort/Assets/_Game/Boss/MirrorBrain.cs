using System;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Hero.Callum;
using HS.Presentation;
using HS.Sidekick;
using UnityEngine;

namespace HS.Boss
{
    /// <summary>
    /// The Mirror (GDD §4.5a): Callum's chapter 5 numbers running his Stage 0 rules — challenge, salute, wait on the
    /// Unready, fight with a Riposte, fall back to a niche at three engagers — plus the counters he still shares with it
    /// (<see cref="MirrorCounters"/>). It has no entry for her: it never targets her. The Habit Breaks it suffers are
    /// player-driven: a ping (Etiquette Reset; at S3, Witnessed dishonour), a collapse in its niche (Chokepoint trap).
    /// Its HP stops at 25% for anything but the Duet (<see cref="DuetFinisher"/>).
    /// </summary>
    public sealed class MirrorBrain : IEnemyController, IEnemyDamageFilter
    {
        public enum Mode { Salute, Fight, Wait, Niche, Feint, Reach, Held }

        public Mode Current { get; private set; } = Mode.Salute;
        public EnemyAgent Self { get; }
        public float BreakRemaining { get; private set; }
        public bool Broken => BreakRemaining > 0f;
        public int Breaks { get; private set; }
        public int DishonourBreaks { get; private set; }
        public float DamageMul => MirrorCounters.DamageAfterDishonour(DishonourBreaks);
        public float EtiquetteCooldown { get; private set; }
        public float RiposteCooldown { get; private set; }
        public float FeintClock { get; private set; }
        /// <summary>The Duet landed: its floor is lifted.</summary>
        public bool Finishing;
        public bool AtFloor => Self.IsAlive && Self.Health.Fraction <= MirrorCounters.Floor + 1e-4f;
        public string Icon => MirrorCounters.Icon(Current);
        /// <summary>A Habit Break began: "etiquette", "niche", "dishonour".</summary>
        public event Action<string> HabitBreak;
        /// <summary>Its feint drew him into waiting, and the free blow landed (damage).</summary>
        public event Action<float> FeintLanded;

        readonly HeroAgent _hero;
        readonly CallumModule _cm;
        readonly RunContext _ctx;
        readonly Vector3[] _niches;
        float _modeT, _waitT, _nicheCd, _atkT;
        int _combo;
        bool _windup, _etiquette, _dishonourOpen, _riposteDue;
        Vector3 _niche;

        public MirrorBrain(EnemyAgent self, HeroAgent hero, Vector3[] niches)
        {
            Self = self;
            _hero = hero;
            _cm = hero != null ? hero.Module as CallumModule : null;
            _ctx = RunContext.Current;
            _niches = niches ?? new Vector3[0];
            Self.Brain = this;
            Self.FlaggedCheater = true;
            if (_ctx != null) _ctx.Events.Ping += OnPing;
            Enter(Mode.Salute, MirrorCounters.SaluteTime);
            Self.Presenter?.PlayAction("salute");
            Say(OpenLines);
        }

        public void Dispose()
        {
            if (_ctx != null) _ctx.Events.Ping -= OnPing;
            if (Self != null && Self.Brain == this) Self.Brain = null;
        }

        Stage Stage => _hero != null ? _hero.Stage : Stage.S0;

        void Enter(Mode m, float t)
        {
            Current = m;
            _modeT = t;
            _windup = false;
        }

        void Say(string[] lines, int priority = 1)
        {
            if (lines == null || lines.Length == 0 || _ctx == null) return;
            _ctx.Events.RaiseBark("mirror", lines[(Breaks + _combo) % lines.Length], 2.4f, priority);
        }

        // ------------------------------------------------------------------ tick

        public bool Tick(EnemyAgent self, float dt)
        {
            if (EtiquetteCooldown > 0f) EtiquetteCooldown -= dt;
            if (RiposteCooldown > 0f) RiposteCooldown -= dt;
            if (BreakRemaining > 0f) BreakRemaining -= dt;
            if (_nicheCd > 0f) _nicheCd -= dt;
            var p = Self.Presenter;
            if (_hero == null || !_hero.IsAlive)
            {
                Hold(dt);
                return true;
            }
            if (_riposteDue)
            {
                _riposteDue = false;
                Riposte();
            }
            if (Current == Mode.Feint)
            {
                TickFeint(dt);
                return true;
            }
            if (Current == Mode.Held || Current == Mode.Reach)
            {
                Hold(dt);
                if (Current == Mode.Reach) Face(dt);
                if (Current == Mode.Held && (_modeT -= dt) <= 0f) Resume();
                return true;
            }
            if (Self.Status.Incapacitated || Self.Status.Has(StatusType.Blinded))
            {
                _windup = false;
                Hold(dt);
                return true;
            }
            switch (Current)
            {
                case Mode.Salute:
                    Hold(dt);
                    Face(dt);
                    if ((_modeT -= dt) > 0f) return true;
                    _dishonourOpen = false;
                    if (_etiquette)
                    {
                        _etiquette = false;
                        Enter(Mode.Wait, MirrorCounters.SaluteWait);
                        p?.PlayAction("guard", MirrorCounters.SaluteWait);
                    }
                    else Resume();
                    return true;
                case Mode.Wait:
                    Hold(dt);
                    Face(dt);
                    _waitT += dt;
                    if ((_modeT -= dt) <= 0f) Resume();
                    return true;
                case Mode.Niche:
                    TickNiche(dt);
                    return true;
                default:
                    TickFight(dt);
                    return true;
            }
        }

        void Resume()
        {
            Enter(Mode.Fight, 0f);
            Self.Presenter?.PlayAction("none");
        }

        void Hold(float dt)
        {
            Self.Motor.Move(Vector3.zero, 40f, dt);
            Self.Presenter?.SetLocomotion(0f, false);
        }

        void Face(float dt) => Self.Motor.FaceDirection(Geo.Flat(_hero.Position - Self.Position), 540f, dt);

        /// <summary>His own rule 3, seen from the other side: a man who can't fight back is waited on (up to 3 s).</summary>
        bool HeroUnready()
        {
            if (_cm != null && _cm.LookingAway) return true;
            var st = _hero.Status;
            if (st.Has(StatusType.Blinded)) return true;
            if (st.Has(StatusType.Stunned) && st.SourceOf(StatusType.Stunned) != Self) return true;
            if (st.Has(StatusType.Staggered) && st.SourceOf(StatusType.Staggered) != Self) return true;
            return Geo.AngleTo(_hero.Position, _hero.Forward, Self.Position) > 100f;
        }

        int Engagers()
        {
            bool heroClose = Geo.FlatDistance(_hero.Position, Self.Position) <= MirrorCounters.EngageRadius;
            var sk = _ctx != null ? _ctx.Sidekick as SidekickAgent : null;
            bool skEngaged = sk != null && sk.IsAlive && !sk.IsDowned && Self.TimeSinceSidekickHurtMe < 3f
                             && Geo.FlatDistance(sk.Position, Self.Position) <= MirrorCounters.EngageRadius;
            bool decoy = HS.Skills.Impl.Decoy.CountNear(Self.Position, MirrorCounters.EngageRadius) > 0;
            return MirrorCounters.Engagers(heroClose, skEngaged, decoy);
        }

        void TickFight(float dt)
        {
            var toT = Geo.Flat(_hero.Position - Self.Position);
            float dist = toT.magnitude;
            var stats = Self.Stats;
            if (_windup)
            {
                Hold(dt);
                Self.Motor.FaceDirection(toT, 240f, dt);
                if ((_atkT -= dt) <= 0f) Strike();
                return;
            }
            if (_atkT > 0f)
            {
                Hold(dt);
                _atkT -= dt;
                return;
            }
            // Rule 4: at three engagers, fall back to the narrows — here, the nearer niche.
            if (_nicheCd <= 0f && _niches.Length > 0 && Engagers() >= MirrorCounters.NicheEngagers)
            {
                _niche = Nearest(_niches, Self.Position);
                Enter(Mode.Niche, MirrorCounters.NicheHold);
                Say(NicheLines);
                return;
            }
            // Counter: the Feint (he still waits on the Unready).
            if (MirrorCounters.Has(Stage, MirrorCounter.Feint))
            {
                FeintClock += dt;
                if (FeintClock >= MirrorCounters.FeintEvery && dist <= stats.range + 1.5f)
                {
                    FeintClock = 0f;
                    Enter(Mode.Feint, MirrorCounters.FeintTime);
                    Self.Status.Apply(StatusType.Staggered, MirrorCounters.FeintTime, Self);
                    Self.Presenter?.PlayAction("stagger", MirrorCounters.FeintTime);
                    return;
                }
            }
            // Rule 3: wait on the Unready (S0: 3 s).
            if (HeroUnready() && _waitT < MirrorCounters.WaitUnready)
            {
                _waitT += dt;
                Hold(dt);
                Face(dt);
                return;
            }
            if (!HeroUnready()) _waitT = 0f;
            Approach(toT, dist, dt);
        }

        void Approach(Vector3 toT, float dist, float dt)
        {
            var stats = Self.Stats;
            float reach = stats.range + _hero.Radius * 0.5f;
            if (dist > reach * 0.9f)
            {
                Self.Motor.Move(toT.normalized * stats.speed, 30f, dt);
                Self.Motor.FaceDirection(toT, 400f, dt);
                Self.Presenter?.SetLocomotion(Self.Motor.Speed, false);
                return;
            }
            Hold(dt);
            Self.Motor.FaceDirection(toT, 400f, dt);
            // Duellist's patience, his own: Riposte ready and he is mid-swing? Hold guard for the parry.
            if (RiposteCooldown <= 0f && _cm != null && _cm.Attacking)
            {
                Self.Presenter?.PlayAction("block", 0.4f);
                return;
            }
            _windup = true;
            _atkT = stats.windup;
            Self.Presenter?.PlayAction(_combo % 3 == 2 ? "attack3" : _combo % 2 == 0 ? "attack" : "attack2", stats.windup + stats.recovery);
        }

        void Strike()
        {
            _windup = false;
            _atkT = Self.Stats.recovery;
            _combo++;
            float reach = Self.Stats.range + _hero.Radius + 0.3f;
            if (Geo.FlatDistance(Self.Position, _hero.Position) > reach || Geo.AngleTo(Self.Position, Self.Forward, _hero.Position) > 70f) return;
            var d = DamageInfo.Make(Self, _hero, Self.Stats.damage * DamageMul, DamageKind.Blade, "mirror", 0.25f);
            _ctx?.Events.AttackResolving?.Invoke(Self, _hero); // his Riposte works on it, as on anyone he challenged
            if (Self.ConsumeParry()) return;
            _hero.TakeDamage(d);
        }

        /// <summary>Its Riposte: his swing turned aside, and a counter for 2× (resolved on its own tick).</summary>
        void Riposte()
        {
            if (!_hero.IsAlive) return;
            _hero.Status.Apply(StatusType.Staggered, MirrorCounters.RiposteStagger, Self);
            Self.Motor.FaceInstant(Geo.DirTo(Self.Position, _hero.Position));
            Self.Presenter?.PlayAction("attack3", 0.5f);
            Vfx.Burst(VfxKind.Sparks, Self.Position + Self.Forward * 0.9f + Vector3.up * 1.3f);
            _hero.TakeDamage(DamageInfo.Make(Self, _hero, Self.Stats.damage * MirrorCounters.RiposteMul * DamageMul, DamageKind.Blade, "riposte", 0.6f));
            Say(RiposteLines, 0);
        }

        void TickFeint(float dt)
        {
            Hold(dt);
            if ((_modeT -= dt) > 0f) return;
            Self.Status.Clear(StatusType.Staggered);
            Resume();
            // He waited on it. The heavy blow comes unannounced: no Riposte for a man lowering his guard.
            if (_cm == null || _cm.WaitT <= 0f) return;
            if (Geo.FlatDistance(Self.Position, _hero.Position) > Self.Stats.range + _hero.Radius + 0.6f) return;
            Self.Motor.FaceInstant(Geo.DirTo(Self.Position, _hero.Position));
            Self.Presenter?.PlayAction("attack3", 0.5f);
            float applied = _hero.TakeDamage(DamageInfo.Make(Self, _hero, Self.Stats.heavyDamage * DamageMul, DamageKind.Heavy, "feint", 0.6f));
            Say(FeintLines);
            FeintLanded?.Invoke(applied);
        }

        void TickNiche(float dt)
        {
            _modeT -= dt;
            if (_modeT <= 0f)
            {
                _nicheCd = 6f;
                Resume();
                return;
            }
            float d = Geo.FlatDistance(Self.Position, _niche);
            if (d > 0.6f)
            {
                var dir = Geo.DirTo(Self.Position, _niche);
                Self.Motor.Move(dir * Self.Stats.speed, 30f, dt);
                Self.Motor.FaceDirection(dir, 400f, dt);
                Self.Presenter?.SetLocomotion(Self.Motor.Speed, false);
                return;
            }
            // Holding the narrows: one at a time, and only whoever comes within reach.
            var toT = Geo.Flat(_hero.Position - Self.Position);
            if (_windup)
            {
                Hold(dt);
                Self.Motor.FaceDirection(toT, 240f, dt);
                if ((_atkT -= dt) <= 0f) Strike();
                return;
            }
            if (_atkT > 0f)
            {
                Hold(dt);
                _atkT -= dt;
                return;
            }
            Hold(dt);
            Face(dt);
            if (toT.magnitude <= Self.Stats.range + _hero.Radius * 0.5f) Approach(toT, toT.magnitude, dt);
        }

        public bool InNiche => Current == Mode.Niche && Geo.FlatDistance(Self.Position, _niche) <= 1.2f;

        static Vector3 Nearest(Vector3[] points, Vector3 from)
        {
            var best = points[0];
            for (int i = 1; i < points.Length; i++)
                if (Geo.FlatDistance(points[i], from) < Geo.FlatDistance(best, from)) best = points[i];
            return best;
        }

        // ------------------------------------------------------------------ Habit Breaks

        void BeginBreak(string kind, float seconds)
        {
            Breaks++;
            BreakRemaining = Mathf.Max(BreakRemaining, seconds);
            Vfx.Burst(VfxKind.Glint, Self.Position + Vector3.up * 1.6f, 1.4f);
            // Counter: the Goad (he still guards his Honor) — the Mirror makes him watch it, and he pays.
            if (_cm != null && MirrorCounters.Has(Stage, MirrorCounter.Goad) && _cm.Sees(Self.Position, false))
            {
                _cm.SetHonor(_cm.Honor - _cm.T.honorLossMajor);
                _cm.Bark(GoadLines, 2);
            }
            HabitBreak?.Invoke(kind);
        }

        void OnPing(PingInfo p)
        {
            if (p.Target != Self || !Self.IsAlive || _hero == null || !_hero.IsAlive) return;
            if (Current == Mode.Reach || Current == Mode.Held || Current == Mode.Feint) return; // the Duet answers that ping
            if (!MirrorCounters.EtiquetteHeard(Stage))
            {
                _ctx?.Events.RaiseThought("He's too busy fighting to hear me.");
                return;
            }
            if (EtiquetteCooldown > 0f)
            {
                _ctx?.Events.RaiseThought("Not again so soon. It's watching for it.");
                return;
            }
            EtiquetteCooldown = MirrorCounters.EtiquetteCooldown;
            Enter(Mode.Salute, MirrorCounters.SaluteTime);
            Self.Presenter?.PlayAction("salute", MirrorCounters.SaluteTime);
            if (MirrorCounters.Dishonour(Stage))
            {
                // S3: it must answer a salute. He doesn't offer one.
                _dishonourOpen = true;
                return;
            }
            // S1–S2, Etiquette Reset: he re-challenges; the code makes it salute back and wait.
            _etiquette = true;
            _cm?.StartChallenge(Self);
            BeginBreak("etiquette", MirrorCounters.SaluteTime + MirrorCounters.SaluteWait);
        }

        /// <summary>The Duet's tell and ring: it stands reaching for the code until the Duet resolves.</summary>
        public void BeginReach()
        {
            Enter(Mode.Reach, 0f);
            Self.Presenter?.PlayAction("guard", 3f);
        }

        public void EndReach()
        {
            if (Current == Mode.Reach) Resume();
        }

        /// <summary>Held still (the Duet's Hold Please): no stun, so he never reads it as Unready.</summary>
        public void HoldFor(float seconds)
        {
            Enter(Mode.Held, seconds);
            HoldPleaseSkillFreeze(seconds);
        }

        void HoldPleaseSkillFreeze(float seconds) => HS.Skills.Impl.HoldPleaseSkill.Freeze(Self, seconds);

        // ------------------------------------------------------------------ the blows it takes

        public float Incoming(EnemyAgent self, DamageInfo d, float amount)
        {
            bool fromHero = d.Source != null && d.Source == _hero;
            if (fromHero)
            {
                // Its Riposte: his plain swing, met in guard while it isn't committed to one of its own.
                if (d.Tag == "sword" && RiposteCooldown <= 0f && Current == Mode.Fight && !_windup && !Self.Status.Incapacitated
                    && Geo.AngleTo(Self.Position, Self.Forward, _hero.Position) <= 60f)
                {
                    RiposteCooldown = MirrorCounters.RiposteCooldown;
                    _riposteDue = true;
                    return 0f;
                }
                amount *= MirrorCounters.HeroBlowMul;
                // Witnessed dishonour (S3): struck during its return salute.
                if (_dishonourOpen && Current == Mode.Salute)
                {
                    _dishonourOpen = false;
                    DishonourBreaks++;
                    Self.Status.Apply(StatusType.Staggered, MirrorCounters.DishonourStagger, _hero);
                    _cm?.Bark(CheatLines, 2);
                    Say(DishonourLines, 2);
                    Resume();
                    BeginBreak("dishonour", MirrorCounters.DishonourStagger);
                }
            }
            // Chokepoint trap: something heavy came down on it in its niche.
            if (d.Tag == "loosen_bolt" && Current == Mode.Niche)
            {
                Self.Status.Apply(StatusType.Staggered, MirrorCounters.CollapseStagger, d.Source);
                Self.Presenter?.PlayAction("stagger", MirrorCounters.CollapseStagger);
                _nicheCd = 6f;
                Resume();
                BeginBreak("niche", MirrorCounters.CollapseStagger);
            }
            if (Broken) amount *= MirrorCounters.BreakDamageMul;
            // The floor: nothing but the Duet takes the last quarter.
            bool duet = d.Tag == "duet" || (Finishing && (d.Tag == "judgment" || fromHero));
            if (!duet)
            {
                float floor = Self.Health.Max * MirrorCounters.Floor;
                amount = Mathf.Min(amount, Mathf.Max(0f, Self.Health.Current - floor));
            }
            return amount;
        }

        // Draft copy for the owner (GDD §8): the copy speaks his old lines, a little wrong.
        static readonly string[] OpenLines = { "I am Callum of the Code. Draw, and be judged." };
        static readonly string[] RiposteLines = { "Parried.", "Too slow. As always.", "Mind your guard. I never did." };
        static readonly string[] NicheLines = { "To the narrows. One at a time.", "Back. Make them come single file." };
        static readonly string[] FeintLines = { "You waited. You always wait.", "Rise, I said. You let me." };
        static readonly string[] DishonourLines = { "That— was not in the Code.", "You struck a saluting man?" };
        static readonly string[] CheatLines = { "Cheat a cheater.", "You're not me. Not any more." };
        static readonly string[] GoadLines = { "It's baiting us— and I saw you take it.", "Not like that! Not in front of it!" };
    }
}
