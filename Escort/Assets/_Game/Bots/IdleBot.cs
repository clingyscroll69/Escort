using HS.Core;
using HS.Sidekick;
using UnityEngine;

namespace HS.Bots
{
    /// <summary>
    /// Balance harness "Idle" bot (GDD §11.4): the sidekick does nothing — trails well behind (crouched, out of the way of
    /// view cones) so the hero's solo-clear rate can be measured.
    /// </summary>
    public sealed class IdleBot : MonoBehaviour, ISidekickCommands
    {
        public float Trail = 11f;

        public SidekickCommand Next(SidekickAgent self)
        {
            var c = SidekickCommand.None;
            var hero = RunContext.Current != null ? RunContext.Current.Hero : null;
            if (hero == null || !hero.IsAlive) return c;
            var spot = hero.Position - Vector3.forward * Trail;
            c.Move = BotUtil.MoveTo(self, spot, 1.5f);
            c.Walk = Geo.FlatDistance(self.Position, spot) < 4f;
            c.CrouchToggle = !self.Crouched && Geo.FlatDistance(self.Position, spot) < 4f;
            c.AimPoint = hero.Position;
            c.HasAim = true;
            return c;
        }
    }
}
