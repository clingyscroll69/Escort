using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Presentation;
using HS.Sidekick;
using UnityEngine;

namespace HS.Skills.Impl
{
    /// <summary>Pocket Sand (Fixer): thrown cloud, blinds 3/4 s, reveals hidden things. Dirty trick if witnessed.</summary>
    public sealed class PocketSandSkill : ISkillBehaviour
    {
        public const float Radius = 2.3f;
        readonly List<Agent> _scratch = new List<Agent>();

        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            return c.User != null && c.User.IsAlive;
        }

        public bool Execute(in SkillUseContext c, SkillState s)
        {
            var user = c.User;
            var run = c.Run;
            float range = s.Def.B(s.Rank);
            var aim = c.AimPoint;
            if (Geo.FlatDistance(user.Position, aim) > range) aim = user.Position + Geo.DirTo(user.Position, aim) * range;
            aim.y = user.Position.y;
            var dir = Geo.DirTo(user.Position, aim);
            if (dir != Vector3.zero) user.Motor.FaceInstant(dir);
            user.Presenter?.PlayAction("throw", 0.6f);
            float blind = s.Def.A(s.Rank);
            var point = aim;
            run?.Timers.After(0.3f, () => Cloud(run, user, point, blind));
            return true;
        }

        void Cloud(RunContext run, Agent user, Vector3 point, float blind)
        {
            Vfx.Burst(VfxKind.Sand, point + Vector3.up * 0.4f);
            HS.Rooms.HazardMarker.RevealAround(point, Radius); // sand settles on a hidden plate's edges
            AgentRegistry.InRadius(point, Radius, _scratch, a => a != user);
            Agent firstHostile = null;
            foreach (var a in _scratch)
            {
                if (a is EnemyAgent e)
                {
                    if (e.IsHidden)
                    {
                        e.Reveal(false);
                        run?.Events.Revealed?.Invoke(e, "pocket_sand");
                    }
                    e.Status.Apply(StatusType.Blinded, blind);
                    e.Presenter?.PlayAction("hit_head", 0.5f);
                    firstHostile ??= e;
                }
                else if (a.Faction == Faction.Hero)
                {
                    a.Status.Apply(StatusType.Blinded, 1.2f); // sand in the wrong eyes
                }
            }
            if (run != null && firstHostile != null) // sand that hit nobody is a miss, not a dirty trick
                run.Events.RaiseSabotage(new SabotageEvent
                {
                    Tag = "pocket_sand", Severity = SabotageSeverity.Major, Position = point, ActorPosition = user.Position,
                    Victim = firstHostile, Time = run.SimTime,
                });
        }
    }

    /// <summary>Loosen Bolt (Fixer): kneel at a nearby prop to arm it (see ArmableProp).</summary>
    public sealed class LoosenBoltSkill : ISkillBehaviour
    {
        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            if (ArmableProp.ArmedCount() >= Mathf.RoundToInt(s.Def.A(s.Rank)))
            {
                reason = "Everything I've loosened is still waiting.";
                return false;
            }
            if (Nearest(c.User) == null)
            {
                reason = "Nothing loose enough around here.";
                return false;
            }
            return true;
        }

        static ArmableProp Nearest(Agent user)
        {
            ArmableProp best = null;
            float bestD = 2.8f;
            foreach (var p in ArmableProp.Instances)
            {
                if (p.State != ArmableProp.PropState.Idle) continue;
                float d = Geo.FlatDistance(user.Position, p.InteractPosition);
                if (d < bestD)
                {
                    bestD = d;
                    best = p;
                }
            }
            return best;
        }

        public bool Execute(in SkillUseContext c, SkillState s)
        {
            var prop = Nearest(c.User);
            if (prop == null || !(c.User is SidekickAgent sk)) return false;
            var dir = Geo.DirTo(sk.Position, prop.InteractPosition);
            if (dir != Vector3.zero) sk.Motor.FaceInstant(dir);
            sk.StartChannel("Loosening bolts", s.Def.B(s.Rank), "kneel", true, prop.Arm);
            return true;
        }
    }

    /// <summary>Crossbow (Combat): slow-reload aimed bolt; rank 2 pierces. Can hit the hero if he's in the way.</summary>
    public sealed class CrossbowSkill : ISkillBehaviour
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
            user.Presenter?.PlayAction("shoot", 0.5f);
            float dmg = s.Def.A(s.Rank), range = s.Def.B(s.Rank);
            bool pierce = s.Rank >= 2;
            // Aiming at someone (even up on a perch) means shooting at *them*: the bolt goes to their centre in 3D.
            var marked = AgentRegistry.Nearest(c.AimPoint, 1.6f, a => a is EnemyAgent e && e.IsAlive && !e.IsHidden);
            c.Run?.Timers.After(0.12f, () =>
            {
                if (!user.IsAlive) return;
                var origin = user.Position + Vector3.up * 1.25f + dir * 0.45f;
                var shotDir = dir;
                if (marked != null && marked.IsAlive)
                {
                    var to = marked.Position + Vector3.up * 1.05f - origin;
                    if (to.sqrMagnitude > 0.25f) shotDir = to.normalized;
                }
                ProjectileSystem.Ensure().Fire(new ProjectileSpec
                {
                    Owner = user, Origin = origin, Direction = shotDir, Speed = 42f,
                    Damage = dmg, Kind = DamageKind.Ranged, Tag = "crossbow", MaxRange = range, Pierce = pierce, Stagger = 0.3f,
                    HitsFaction = a => a != user,
                });
            });
            return true;
        }
    }

    /// <summary>Bandage (Provisioner): 3 s channel beside the hero (or yourself): heal over time + treat a minor wound.</summary>
    public sealed class BandageSkill : ISkillBehaviour
    {
        const float Reach = 2.4f;

        static Agent Patient(in SkillUseContext c)
        {
            var hero = c.Run != null ? c.Run.Hero : null;
            if (hero != null && hero.IsAlive && Geo.FlatDistance(hero.Position, c.User.Position) <= Reach) return hero;
            return c.User;
        }

        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            var p = Patient(c);
            var wounded = p.GetComponent<IWounded>();
            if (p.Health.Current >= p.Health.Max - 0.5f && (wounded == null || !wounded.HasMinorWound))
            {
                reason = p == c.User ? "I'm fine. He's the one who'd need it — get closer." : "He doesn't need patching. Yet.";
                return false;
            }
            return true;
        }

        public bool Execute(in SkillUseContext c, SkillState s)
        {
            if (!(c.User is SidekickAgent sk)) return false;
            var patient = Patient(c);
            var run = c.Run;
            float heal = s.Def.A(s.Rank);
            var state = s;
            var dir = Geo.DirTo(sk.Position, patient.Position);
            if (dir != Vector3.zero && patient != sk) sk.Motor.FaceInstant(dir);
            // The patient holds still for the dressing (a hero walking his route would leave reach mid-bandage).
            var hero = patient as HS.Hero.HeroAgent;
            if (hero != null) hero.TreatmentHold = true;
            sk.StartChannel("Bandaging", s.Def.B(s.Rank), "bandage", true,
                onComplete: () =>
                {
                    patient.AddHealOverTime(heal, 6f);
                    patient.GetComponent<IWounded>()?.TreatMinorWound();
                    Vfx.Burst(VfxKind.Heal, patient.Position + Vector3.up * 0.8f);
                    run?.Events.WoundTreated?.Invoke(sk, patient);
                    if (hero != null) hero.TreatmentHold = false;
                },
                onCancel: () =>
                {
                    state.CooldownRemaining = 0f; // an interrupted bandage isn't wasted
                    if (hero != null) hero.TreatmentHold = false;
                },
                validWhile: () => patient == sk || (patient.IsAlive && Geo.FlatDistance(patient.Position, sk.Position) <= Reach + 0.6f));
            return true;
        }
    }

    /// <summary>Cover Story (Handler): talk the hero round — clears doubt/tarnish; within 3 s of being caught it halves the fallout.</summary>
    public sealed class CoverStorySkill : ISkillBehaviour
    {
        static readonly string[] Lines =
        {
            "That? The wind, sir. Terrible gusts on this road.",
            "A rabbit. An enormous, violent rabbit.",
            "He tripped over his own guilty conscience, I expect.",
            "Loose rocks everywhere. Very old road.",
            "Sand? Around here? Must be the season.",
        };
        int _line;

        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            var hero = c.Run != null ? c.Run.Hero : null;
            if (hero == null || !hero.IsAlive || Geo.FlatDistance(hero.Position, c.User.Position) > 14f)
            {
                reason = "He can't hear me from here.";
                return false;
            }
            return true;
        }

        public bool Execute(in SkillUseContext c, SkillState s)
        {
            c.User.Presenter?.PlayAction("talk", 1.2f);
            var run = c.Run;
            if (run != null)
            {
                run.Events.RaiseBark("sidekick", Lines[_line++ % Lines.Length], 2.4f, 2);
                run.Events.CoverStory?.Invoke(s.Def.A(s.Rank), s.Def.B(s.Rank));
            }
            return true;
        }
    }
}
