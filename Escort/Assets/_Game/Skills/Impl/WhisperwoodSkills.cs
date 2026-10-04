using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Presentation;
using HS.Sidekick;
using UnityEngine;

namespace HS.Skills.Impl
{
    /// <summary>Splint &amp; Stitch (Provisioner): a long channel beside the hero that treats a serious wound. Uses supplies:
    /// a few per chapter (B). Above board.</summary>
    public sealed class SplintAndStitchSkill : ISkillBehaviour
    {
        public const float Reach = 2.4f;
        int _chapter = -1, _used;

        int UsedThisChapter(RunContext run)
        {
            int ch = run != null ? run.Chapter : 1;
            if (ch != _chapter)
            {
                _chapter = ch;
                _used = 0;
            }
            return _used;
        }

        public int UsesLeft(SkillState s, RunContext run) => Mathf.RoundToInt(s.Def.B(s.Rank)) - UsedThisChapter(run);

        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            var hero = c.Run != null ? c.Run.Hero as HeroAgent : null;
            if (hero == null || !hero.IsAlive || Geo.FlatDistance(hero.Position, c.User.Position) > Reach)
            {
                reason = "I need to be right beside him for this.";
                return false;
            }
            if (hero.Wounds.SeriousCount == 0)
            {
                reason = hero.WoundCount > 0 ? "Nothing broken. A bandage would do." : "He's in one piece. For now.";
                return false;
            }
            if (UsesLeft(s, c.Run) <= 0)
            {
                reason = "Out of splints and thread until we camp.";
                return false;
            }
            return true;
        }

        public bool Execute(in SkillUseContext c, SkillState s)
        {
            if (!(c.User is SidekickAgent sk)) return false;
            var hero = (HeroAgent)c.Run.Hero;
            var run = c.Run;
            var state = s;
            var dir = Geo.DirTo(sk.Position, hero.Position);
            if (dir != Vector3.zero) sk.Motor.FaceInstant(dir);
            hero.TreatmentHold = true;
            sk.StartChannel("Splinting", s.Def.A(s.Rank), "bandage", true,
                onComplete: () =>
                {
                    hero.TreatmentHold = false;
                    if (!hero.Wounds.TreatWorst()) return;
                    UsedThisChapter(run);
                    _used++;
                    Vfx.Burst(VfxKind.Heal, hero.Position + Vector3.up * 0.9f);
                    run?.Events.WoundTreated?.Invoke(sk, hero);
                },
                onCancel: () =>
                {
                    hero.TreatmentHold = false;
                    state.CooldownRemaining = 0f; // an interrupted splint isn't wasted
                },
                validWhile: () => hero.IsAlive && Geo.FlatDistance(hero.Position, sk.Position) <= Reach + 0.6f);
            return true;
        }
    }

    /// <summary>Pull Back (Handler): yank the hero toward you (B m) out of a snare, a bog or a bad spot; clears a trip.
    /// Reach A m. Above board.</summary>
    public sealed class PullBackSkill : ISkillBehaviour
    {
        static readonly string[] Lines = { "Unhand— ...oh. Thank you.", "Was that a rope? Where did you get a rope?", "I had it. ...Thank you." };
        int _line;

        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            var hero = c.Run != null ? c.Run.Hero : null;
            if (hero == null || !hero.IsAlive || Geo.FlatDistance(hero.Position, c.User.Position) > s.Def.A(s.Rank))
            {
                reason = "He's too far to pull.";
                return false;
            }
            if (Geo.FlatDistance(hero.Position, c.User.Position) < 1.6f)
            {
                reason = "He's right here.";
                return false;
            }
            return true;
        }

        public bool Execute(in SkillUseContext c, SkillState s)
        {
            var hero = c.Run.Hero as HeroAgent;
            var user = c.User;
            var dir = Geo.DirTo(hero.Position, user.Position);
            float dist = Geo.FlatDistance(hero.Position, user.Position);
            float pull = Mathf.Min(s.Def.B(s.Rank), dist - 1.3f);
            user.Motor.FaceInstant(-dir);
            user.Presenter?.PlayAction("throw", 0.6f);
            var to = hero.Position + dir * pull;
            Vfx.Burst(VfxKind.Dust, hero.Position + Vector3.up * 0.2f);
            hero.Motor.Teleport(to + Vector3.up * 0.05f);
            hero.Status.Clear(StatusType.Staggered);
            hero.Status.Clear(StatusType.Stunned);
            hero.Status.Clear(StatusType.Slowed);
            hero.Presenter?.PlayAction("none");
            if (hero.Module is HS.Hero.Callum.CallumModule cm) cm.Bark(new[] { Lines[_line++ % Lines.Length] }, 1);
            return true;
        }
    }

    /// <summary>Sling (Combat): a cheap stone, a small stagger, unlimited ammunition. Damage A, range B.</summary>
    public sealed class SlingSkill : ISkillBehaviour
    {
        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            return c.User != null && c.User.IsAlive;
        }

        public bool Execute(in SkillUseContext c, SkillState s)
        {
            var user = c.User;
            var dir = Geo.DirTo(user.Position, c.AimPoint);
            if (dir == Vector3.zero) dir = user.Forward;
            user.Motor.FaceInstant(dir);
            user.Presenter?.PlayAction("throw", 0.45f);
            float dmg = s.Def.A(s.Rank), range = s.Def.B(s.Rank);
            var marked = AgentRegistry.Nearest(c.AimPoint, 1.6f, a => a is EnemyAgent e && e.IsAlive && !e.IsHidden);
            c.Run?.Timers.After(0.15f, () =>
            {
                if (!user.IsAlive) return;
                var origin = user.Position + Vector3.up * 1.4f + dir * 0.4f;
                var shotDir = dir;
                if (marked != null && marked.IsAlive)
                {
                    var to = marked.Position + Vector3.up * 1.1f - origin;
                    if (to.sqrMagnitude > 0.25f) shotDir = to.normalized;
                }
                ProjectileSystem.Ensure().Fire(new ProjectileSpec
                {
                    Owner = user, Origin = origin, Direction = shotDir, Speed = 30f,
                    Damage = dmg, Kind = DamageKind.Ranged, Tag = "sling", MaxRange = range, Pierce = false, Stagger = 0.25f,
                    HitsFaction = a => a != user,
                });
            });
            return true;
        }
    }

    /// <summary>Read the Room (Scholar): for A seconds, every foe within B m of you shows what he means to do; hidden ones
    /// are marked where they lie (not sprung); a scout's dull pendant shows. Above board.</summary>
    public sealed class ReadTheRoomSkill : ISkillBehaviour
    {
        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            return c.User != null && c.User.IsAlive;
        }

        public bool Execute(in SkillUseContext c, SkillState s)
        {
            c.User.Presenter?.PlayAction("folded", 1.2f);
            ReadTheRoom.Begin(c.Run, c.User, s.Def.A(s.Rank), s.Def.B(s.Rank));
            return true;
        }
    }

    /// <summary>The Read the Room effect, as a run service: who is read, and what each foe means to do.</summary>
    public sealed class ReadTheRoom
    {
        public float Until, Radius;
        public Agent Reader;
        public static event System.Action<ReadTheRoom> Began;

        public static ReadTheRoom Get(RunContext run) => run != null ? run.Get<ReadTheRoom>() : null;

        public static void Begin(RunContext run, Agent reader, float seconds, float radius)
        {
            if (run == null) return;
            var r = run.Get<ReadTheRoom>();
            if (r == null)
            {
                r = new ReadTheRoom();
                run.Register(r);
            }
            r.Reader = reader;
            r.Until = run.SimTime + seconds;
            r.Radius = radius;
            Began?.Invoke(r);
        }

        public bool Active(RunContext run) => run != null && run.SimTime < Until && Reader != null && Reader.IsAlive;

        public bool Covers(RunContext run, Vector3 p) => Active(run) && Geo.FlatDistance(Reader.Position, p) <= Radius;

        /// <summary>One word for what a foe means to do (shown over his head while the room is read).</summary>
        public static string IntentOf(EnemyAgent e)
        {
            if (e == null || !e.IsAlive) return null;
            if (e.IsHidden) return e.IsRanged ? "WAITING TO SHOOT" : "AMBUSH";
            if (e.Asleep) return "ASLEEP";
            if (e.State == EnemyState.Surrendered) return e.Stats != null && e.Stats.cheapShotDamage > 0f ? "FAKE SURRENDER" : "YIELDING";
            if (e.State == EnemyState.Fleeing || e.State == EnemyState.Spared) return null;
            if (e.IsRanged) return e.IsAiming ? "AIMING" : "RELOADING";
            if (e.NextIsHeavy) return "HEAVY BLOW NEXT";
            if (e.Stats != null && e.Stats.surrenderAtHp > 0f) return "WILL FEIGN SURRENDER";
            return e.IsCheater ? "CHEAT" : "FAIR FIGHT";
        }
    }
}
