using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Hero.Callum;
using HS.Presentation;
using HS.Sidekick;
using UnityEngine;

namespace HS.Skills.Impl
{
    /// <summary>
    /// The Link window (the Mirror's Duet Finisher, Plan 5) while it is open: a capstone fired now is the Duet. Registered in
    /// the RunContext by the boss; the skill system asks it first.
    /// </summary>
    public interface ILinkWindow
    {
        bool Open { get; }
        /// <summary>The capstone was fired: true when the Link took it (the Duet happened instead of its normal use).</summary>
        bool TryLink(string capstoneId);
    }

    /// <summary>Shared numbers for the capstones (campaign spec §6; Plan 4's table).</summary>
    public static class CapstoneRules
    {
        public const float DominoArmRadius = 12f, DominoRange = 40f;
        public const int DominoAutoArm = 2;
        public const float HoldRadius = 40f;
        public const float PartnerRange = 12f, PartnerRegen = 0.006f, PartnerMendEvery = 45f;

        /// <summary>Her attack ranks (Crossfire): the kitchen knife counts one, every rank of a Combat trick one more.</summary>
        public static int AttackRanks(SkillSystem sys)
        {
            int n = 1;
            if (sys == null) return n;
            foreach (var s in sys.Known.Values)
                if (s.Def != null && s.Def.family == SkillFamily.Combat) n += s.Rank;
            return n;
        }

        public static float CrossfireBolt(SkillSystem sys) => 20f + 10f * AttackRanks(sys);

        /// <summary>The Link window by capstone (GDD §4.5a): Hold Please doubles it, Silent Partner widens it.</summary>
        public static float LinkWindow(string capstoneId) => capstoneId == "hold_please" ? 2f : capstoneId == "silent_partner" ? 1.6f : 1f;
    }

    /// <summary>Domino Effect: arm up to two idle props near you, then bring down every armed prop in the room.</summary>
    public sealed class DominoEffectSkill : ISkillBehaviour
    {
        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            foreach (var p in ArmableProp.Instances)
            {
                if (p == null || !p.isActiveAndEnabled) continue;
                float d = Geo.FlatDistance(p.InteractPosition, c.User.Position);
                if ((p.State == ArmableProp.PropState.Armed && d <= CapstoneRules.DominoRange) || (p.State == ArmableProp.PropState.Idle && d <= CapstoneRules.DominoArmRadius))
                    return true;
            }
            reason = "Nothing here that would fall.";
            return false;
        }

        public bool Execute(in SkillUseContext c, SkillState s) => Topple(c.User) > 0;

        /// <summary>Arm the nearest idle props (up to two), then trigger every armed one in range. Returns how many fall.</summary>
        public static int Topple(Agent user)
        {
            var idle = new List<ArmableProp>();
            foreach (var p in ArmableProp.Instances)
                if (p != null && p.isActiveAndEnabled && p.State == ArmableProp.PropState.Idle && Geo.FlatDistance(p.InteractPosition, user.Position) <= CapstoneRules.DominoArmRadius)
                    idle.Add(p);
            idle.Sort((a, b) => Geo.FlatDistance(a.InteractPosition, user.Position).CompareTo(Geo.FlatDistance(b.InteractPosition, user.Position)));
            for (int i = 0; i < idle.Count && i < CapstoneRules.DominoAutoArm; i++) idle[i].Arm();
            int n = 0;
            foreach (var p in new List<ArmableProp>(ArmableProp.Instances))
                if (p != null && p.State == ArmableProp.PropState.Armed && Geo.FlatDistance(p.InteractPosition, user.Position) <= CapstoneRules.DominoRange)
                {
                    p.Trigger();
                    n++;
                }
            user.Presenter?.PlayAction("throw", 0.6f);
            Vfx.Burst(VfxKind.Dust, user.Position + Vector3.up * 0.5f, 0.8f);
            return n;
        }
    }

    /// <summary>
    /// Crossfire: your volley. With his Judgment charging, it goes into his target and lands with his blow (it adds the
    /// volley's damage to it); otherwise three bolts at the aim point, each 20 + 10 per attack rank.
    /// </summary>
    public sealed class CrossfireSkill : ISkillBehaviour
    {
        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            return c.User != null && c.User.IsAlive;
        }

        public bool Execute(in SkillUseContext c, SkillState s)
        {
            var user = c.User;
            var sys = user.GetComponent<SidekickSkills>()?.System;
            float bolt = CrossfireBolt(sys);
            var hero = c.Run != null ? c.Run.Hero as HeroAgent : null;
            var cm = hero != null ? hero.Module as CallumModule : null;
            if (cm != null && cm.FinisherCharging && cm.FinisherTarget != null)
            {
                var target = cm.FinisherTarget;
                user.Motor.FaceInstant(Geo.DirTo(user.Position, target.Position));
                user.Presenter?.PlayAction("shoot", 0.5f);
                // Three bolts, timed to the blow: scaled into the tier like any of her damage.
                cm.JoinFinisher(bolt * 3f * ChapterTier.SidekickDamage(c.Run != null ? c.Run.Chapter : 1));
                Vfx.Burst(VfxKind.Sparks, target.Position + Vector3.up * 1.3f, 1.2f);
                c.Run.Events.RaiseThought("On his mark.");
                return true;
            }
            var dir = Geo.DirTo(user.Position, c.AimPoint);
            if (dir == Vector3.zero) dir = user.Forward;
            user.Motor.FaceInstant(dir);
            user.Presenter?.PlayAction("shoot", 0.5f);
            var run = c.Run;
            for (int k = 0; k < 3; k++)
            {
                float spread = (k - 1) * 6f;
                var shotDir = Quaternion.Euler(0f, spread, 0f) * dir;
                run?.Timers.After(0.1f + 0.12f * k, () =>
                {
                    if (!user.IsAlive) return;
                    ProjectileSystem.Ensure().Fire(new ProjectileSpec
                    {
                        Owner = user, Origin = user.Position + Vector3.up * 1.25f + shotDir * 0.45f, Direction = shotDir, Speed = 42f,
                        Damage = bolt, Kind = DamageKind.Ranged, Tag = "crossfire", MaxRange = 24f, Pierce = false, Stagger = 0.3f,
                        HitsFaction = a => a != user,
                    });
                });
            }
            return true;
        }

        static float CrossfireBolt(SkillSystem sys) => CapstoneRules.CrossfireBolt(sys);
    }

    /// <summary>Hold Please: everyone within 40 m but you is held (stunned) for A s — him included.</summary>
    public sealed class HoldPleaseSkill : ISkillBehaviour
    {
        readonly List<Agent> _scratch = new List<Agent>();

        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            return c.User != null && c.User.IsAlive;
        }

        public bool Execute(in SkillUseContext c, SkillState s)
        {
            Hold(c.User, Mathf.Max(0.5f, s.Def.A(s.Rank)), _scratch);
            c.Run?.Events.RaiseBark("sidekick", "Hold, please.", 1.8f, 2);
            return true;
        }

        public static void Hold(Agent user, float seconds, List<Agent> scratch)
        {
            AgentRegistry.InRadius(user.Position, CapstoneRules.HoldRadius, scratch, a => a != user && !(a is HS.Rooms.ChronicleStone));
            foreach (var a in scratch)
            {
                a.Status.Apply(StatusType.Stunned, seconds, user);
                Freeze(a, seconds);
            }
            Vfx.Burst(VfxKind.Glint, user.Position + Vector3.up * 1.5f, 2f);
        }

        /// <summary>The look of it: the body stops mid-motion until the hold ends.</summary>
        public static void Freeze(Agent a, float seconds)
        {
            if (a.Presenter == null) return;
            a.Presenter.SetFlag("frozen", true);
            var p = a.Presenter;
            RunContext.Current?.Timers.After(seconds, () => p.SetFlag("frozen", false));
        }
    }

    /// <summary>Silent Partner (passive): near you (12 m) he regains 0.6% of his max HP a second; every 45 s near you one
    /// minor wound is tended. Its key does nothing outside the Duet (he is already listening).</summary>
    public sealed class SilentPartnerSkill : ISkillBehaviour
    {
        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = "He's already listening.";
            return false;
        }

        public bool Execute(in SkillUseContext c, SkillState s) => false;

        /// <summary>Called every sim tick by the sidekick's skills while she has it.</summary>
        public static void Tick(SidekickAgent sk, ref float mendT, float dt)
        {
            var hero = RunContext.Current != null ? RunContext.Current.Hero as HeroAgent : null;
            if (hero == null || !hero.IsAlive || sk == null || !sk.IsAlive || sk.IsDowned) return;
            if (Geo.FlatDistance(hero.Position, sk.Position) > CapstoneRules.PartnerRange) return;
            hero.Health.Heal(hero.Health.Max * CapstoneRules.PartnerRegen * dt);
            mendT += dt;
            if (mendT < CapstoneRules.PartnerMendEvery) return;
            mendT = 0f;
            if (hero.HasMinorWound && hero.TreatMinorWound()) RunContext.Current.Events.WoundTreated?.Invoke(sk, hero);
        }
    }
}
