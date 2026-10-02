using HS.Core;
using HS.Sidekick;
using UnityEngine;

namespace HS.QA
{
    /// <summary>QA-only command source: drives the sidekick through a scripted loop (circle, sneak, dodge, stab).</summary>
    public sealed class QaAutoPilot : MonoBehaviour, ISidekickCommands
    {
        public float Radius = 6f;
        public Vector3 Center;
        float _t;

        public SidekickCommand Next(SidekickAgent self)
        {
            _t += SimLoop.Dt;
            var c = SidekickCommand.None;
            float phase = _t % 12f;
            var toCenter = Center - self.Position;
            var tangent = Vector3.Cross(Vector3.up, Geo.Flat(toCenter)).normalized;
            var radial = Geo.Flat(toCenter).normalized * (Geo.Flat(toCenter).magnitude - Radius) * 0.5f;
            c.Move = Vector3.ClampMagnitude(tangent + radial, 1f);
            c.AimPoint = self.Position + c.Move * 4f;
            c.HasAim = true;
            if (phase > 4f && phase < 4f + SimLoop.Dt * 1.5f) c.CrouchToggle = true;   // sneak 4–7 s
            if (phase > 7f && phase < 7f + SimLoop.Dt * 1.5f) c.CrouchToggle = true;
            if (phase > 8f && phase < 8f + SimLoop.Dt * 1.5f) c.Dodge = true;
            if (phase > 9.5f && (int)(phase * 3f) % 2 == 0) c.Attack = true;
            return c;
        }
    }
}
