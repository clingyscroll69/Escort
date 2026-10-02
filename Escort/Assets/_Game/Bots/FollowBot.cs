using HS.Core;
using HS.Sidekick;
using UnityEngine;

namespace HS.Bots
{
    /// <summary>
    /// Minimal deterministic sidekick bot: trails the hero at a distance and never acts (the "Idle" bot of the balance
    /// harness when Distance is large; a follow-cam stand-in for visual QA otherwise).
    /// </summary>
    public sealed class FollowBot : MonoBehaviour, ISidekickCommands
    {
        public float Distance = 6f;
        public bool Act;
        /// <summary>Hold this spot instead of following (e.g. outside the Oath circle).</summary>
        public Transform Anchor;
        /// <summary>Points to pass through first (doors, gates), in order.</summary>
        public readonly System.Collections.Generic.List<Vector3> Waypoints = new System.Collections.Generic.List<Vector3>();

        public SidekickCommand Next(SidekickAgent self)
        {
            var c = SidekickCommand.None;
            var hero = RunContext.Current != null ? RunContext.Current.Hero : null;
            if (hero == null || !hero.IsAlive) return c;
            while (Waypoints.Count > 0 && Geo.FlatDistance(self.Position, Waypoints[0]) < 1.0f) Waypoints.RemoveAt(0);
            var behind = Waypoints.Count > 0 ? Waypoints[0]
                : Anchor != null ? Anchor.position : hero.Position - hero.Forward * Distance + hero.transform.right * 2.5f;
            float d = Geo.FlatDistance(self.Position, behind);
            if (d > 1.2f) c.Move = Vector3.ClampMagnitude(Geo.DirTo(self.Position, behind) * (d / 3f), 1f);
            c.Walk = d < 3f && Waypoints.Count == 0;
            c.AimPoint = hero.Position;
            c.HasAim = true;
            return c;
        }
    }
}
