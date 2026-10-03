using HS.Presentation;
using UnityEngine;

namespace HS.Tutorial.Demo
{
    /// <summary>
    /// A character on the demo stage: the real model, rig and animations (its <see cref="AnimDriver"/>) with no Agent
    /// behind it, so it never enters the simulation. A script moves it and tells it what to play.
    /// </summary>
    public sealed class Puppet
    {
        public string Kind;
        public Transform Root;
        public AnimDriver Anim;
        public DemoStage Stage;
        /// <summary>Standing on something (a perch): metres above the floor.</summary>
        public float Height;
        Vector2 _pos;
        float _yaw;

        public Vector2 Pos
        {
            get => _pos;
            set
            {
                _pos = value;
                Root.position = Stage.World(value) + Vector3.up * Height;
            }
        }

        public float Yaw
        {
            get => _yaw;
            set
            {
                _yaw = value;
                Root.rotation = Quaternion.Euler(0f, value, 0f);
            }
        }

        /// <summary>Turn to face a stage point.</summary>
        public void Face(Vector2 at)
        {
            var d = at - _pos;
            if (d.sqrMagnitude > 1e-4f) Yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        }

        public Vector3 Head => Root.position + Vector3.up * 2.05f;
        public Vector3 Feet => Root.position;

        public void Locomotion(float speed, bool crouched = false) => Anim?.SetLocomotion(speed, crouched);
        public void Play(string action, float duration = -1f) => Anim?.PlayAction(action, duration);
        public void Flag(string flag, bool on) => Anim?.SetFlag(flag, on);
    }
}
