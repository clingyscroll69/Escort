using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Presentation;
using HS.Sidekick;
using UnityEngine;

namespace HS.Skills.Impl
{
    /// <summary>Bait &amp; Switch (Fixer): plant a decoy at the aim point (up to 8 m away); enemies within B m go for it for A s
    /// — never a man in his duel. Above board.</summary>
    public sealed class BaitAndSwitchSkill : ISkillBehaviour
    {
        public const float Reach = 8f;

        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            return c.User != null && c.User.IsAlive;
        }

        public bool Execute(in SkillUseContext c, SkillState s)
        {
            var user = c.User;
            var at = c.AimPoint;
            if (Geo.FlatDistance(user.Position, at) > Reach) at = user.Position + Geo.DirTo(user.Position, at) * Reach;
            at.y = user.Position.y;
            var dir = Geo.DirTo(user.Position, at);
            if (dir != Vector3.zero) user.Motor.FaceInstant(dir);
            user.Presenter?.PlayAction("throw", 0.6f);
            Decoy.Plant(user, at + Vector3.up * 0.05f, s.Def.A(s.Rank), s.Def.B(s.Rank), c.Run);
            c.Run?.Events.RaiseThought("Over here, lads. Over here.");
            return true;
        }
    }

    /// <summary>Smoke Bomb (Provisioner): a cloud at the aim point (up to 8 m) for A s, radius B m. Nobody sees through it —
    /// not him, not a stone, not a shooter — and anyone in it loses track of you. Above board.</summary>
    public sealed class SmokeBombSkill : ISkillBehaviour
    {
        public const float Reach = 8f;
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
            var at = c.AimPoint;
            if (Geo.FlatDistance(user.Position, at) > Reach) at = user.Position + Geo.DirTo(user.Position, at) * Reach;
            at.y = user.Position.y;
            var dir = Geo.DirTo(user.Position, at);
            if (dir != Vector3.zero) user.Motor.FaceInstant(dir);
            user.Presenter?.PlayAction("throw", 0.6f);
            float seconds = s.Def.A(s.Rank), radius = s.Def.B(s.Rank);
            var point = at;
            run?.Timers.After(0.3f, () =>
            {
                SmokeCloud.Release(point, radius, seconds, run);
                AgentRegistry.InRadius(point, radius + 0.5f, _scratch, a => a is EnemyAgent);
                foreach (var a in _scratch) ((EnemyAgent)a).LoseSidekick();
            });
            return true;
        }
    }

    /// <summary>Pep Talk (Handler): within earshot (14 m), +A damage and speed for him for B s. Above board.</summary>
    public sealed class PepTalkSkill : ISkillBehaviour
    {
        public const float Earshot = 14f;
        static readonly string[] Lines =
        {
            "You've got him, sir! Left side's soft!",
            "That's the Callum the songs are about!",
            "Two more of those and he's done for!",
            "Chin up, sword up — you're winning!",
        };
        static readonly string[] Replies = { "...Yes. Yes! Onward!", "I know. But thank you.", "Hah! Watch this, then." };
        int _line;

        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            var hero = c.Run != null ? c.Run.Hero as HeroAgent : null;
            if (hero == null || !hero.IsAlive || Geo.FlatDistance(hero.Position, c.User.Position) > Earshot)
            {
                reason = "He can't hear me from here.";
                return false;
            }
            return true;
        }

        public bool Execute(in SkillUseContext c, SkillState s)
        {
            var hero = (HeroAgent)c.Run.Hero;
            c.User.Presenter?.PlayAction("cheer", 1.2f);
            hero.Inspire(s.Def.A(s.Rank), s.Def.B(s.Rank));
            Vfx.Burst(VfxKind.Glint, hero.Position + Vector3.up * 2f, 0.9f);
            var run = c.Run;
            int i = _line++;
            run.Events.RaiseBark("sidekick", Lines[i % Lines.Length], 2.2f, 1);
            if (hero.Module is HS.Hero.Callum.CallumModule cm) run.Timers.After(1.2f, () => cm.Bark(new[] { Replies[i % Replies.Length] }, 0));
            return true;
        }
    }

    /// <summary>Shoulder Check (Combat): a short charge into the nearest foe in front (within 2.4 m of the aim line): a light
    /// blow, A m of knockback and B s of stagger. Into his own duel, a slight if he sees it; a shoved man is helpless to
    /// his code while he reels.</summary>
    public sealed class ShoulderCheckSkill : ISkillBehaviour
    {
        public const float Lunge = 1.6f, Reach = 2.4f, Damage = 6f;

        static EnemyAgent Victim(Agent user, Vector3 aim)
        {
            var dir = Geo.DirTo(user.Position, aim);
            if (dir == Vector3.zero) dir = user.Forward;
            EnemyAgent best = null;
            float bestD = float.MaxValue;
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is EnemyAgent e) || !e.IsAlive || e.IsHidden || !e.gameObject.activeInHierarchy) continue;
                float d = Geo.FlatDistance(user.Position, e.Position);
                if (d > Reach + Lunge || Geo.AngleTo(user.Position, dir, e.Position) > 50f) continue;
                if (d < bestD)
                {
                    bestD = d;
                    best = e;
                }
            }
            return best;
        }

        public bool CanUse(in SkillUseContext c, SkillState s, out string reason)
        {
            reason = null;
            if (Victim(c.User, c.AimPoint) != null) return true;
            reason = "Nobody close enough to shove.";
            return false;
        }

        public bool Execute(in SkillUseContext c, SkillState s)
        {
            var user = c.User;
            var e = Victim(user, c.AimPoint);
            if (e == null) return false;
            var dir = Geo.DirTo(user.Position, e.Position);
            if (dir == Vector3.zero) dir = user.Forward;
            user.Motor.FaceInstant(dir);
            float close = Mathf.Max(0f, Geo.FlatDistance(user.Position, e.Position) - (e.Radius + user.Radius + 0.2f));
            if (close > 0.05f) user.Motor.MoveExact(dir * Mathf.Min(close, Lunge) / SimLoop.Dt, SimLoop.Dt);
            user.Presenter?.PlayAction("hit_heavy", 0.5f);
            float knock = s.Def.A(s.Rank), stagger = s.Def.B(s.Rank);
            var d = DamageInfo.Make(user, e, Damage, DamageKind.Melee, "shoulder_check", stagger);
            e.TakeDamage(d);
            if (e.IsAlive && e.Motor != null) e.Motor.MoveExact(dir * knock / SimLoop.Dt, SimLoop.Dt);
            Vfx.Burst(VfxKind.Hit, e.Position + Vector3.up * 1.1f, 1.1f);
            return true;
        }
    }
}
