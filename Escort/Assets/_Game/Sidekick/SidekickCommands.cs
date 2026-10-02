using UnityEngine;

namespace HS.Sidekick
{
    /// <summary>One simulation tick of sidekick intent. Produced by the player's input or by a bot (balance harness).</summary>
    public struct SidekickCommand
    {
        /// <summary>World-space XZ move direction, magnitude 0..1.</summary>
        public Vector3 Move;
        /// <summary>World-space aim point on the ground plane.</summary>
        public Vector3 AimPoint;
        public bool HasAim;
        public bool Walk;          // held
        public bool CrouchToggle;  // edge
        public bool Attack;        // edge
        public bool Dodge;         // edge
        public bool Ping;          // edge
        public bool Interact;      // edge
        public int Skill;          // -1 = none, else loadout slot index (edge)
        public bool SkillHeld;     // for channelled skills

        public static SidekickCommand None => new SidekickCommand { Skill = -1 };
    }

    public interface ISidekickCommands
    {
        /// <summary>Called once per sim tick; edges must be consumed (returned once).</summary>
        SidekickCommand Next(SidekickAgent self);
    }

    /// <summary>Test/bot helper: a fixed command every tick (edges fire once unless Repeat).</summary>
    public sealed class ScriptedCommands : ISidekickCommands
    {
        public SidekickCommand Current = SidekickCommand.None;
        public bool RepeatEdges;

        public SidekickCommand Next(SidekickAgent self)
        {
            var c = Current;
            if (!RepeatEdges)
            {
                Current.CrouchToggle = false;
                Current.Attack = false;
                Current.Dodge = false;
                Current.Ping = false;
                Current.Interact = false;
                Current.Skill = -1;
            }
            return c;
        }
    }
}
