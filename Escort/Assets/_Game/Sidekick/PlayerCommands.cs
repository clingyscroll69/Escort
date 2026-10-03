using HS.Core;
using UnityEngine;

namespace HS.Sidekick
{
    /// <summary>
    /// Human input → SidekickCommand. Samples every rendered frame and latches button edges so a press is consumed by
    /// exactly one sim tick regardless of frame rate. Movement is camera-relative (fixed-yaw camera).
    /// </summary>
    public sealed class PlayerCommands : MonoBehaviour, ISidekickCommands
    {
        public bool InputEnabled = true;

        GameInput _in;
        SidekickCommand _latched = SidekickCommand.None;
        Vector3 _move;
        Vector3 _aim;
        bool _hasAim;
        bool _walk;
        bool _skillHeld;
        Plane _ground = new Plane(Vector3.up, Vector3.zero);

        void OnEnable() => _in = GameInput.Instance;

        void Update()
        {
            if (_in == null) _in = GameInput.Instance;
            if (!InputEnabled)
            {
                _move = Vector3.zero;
                return;
            }
            var cam = Camera.main;
            var m = _in.Move.ReadValue<Vector2>();
            m = Vector2.ClampMagnitude(m, 1f);
            Vector3 fwd = Vector3.forward, right = Vector3.right;
            if (cam != null)
            {
                fwd = Geo.Flat(cam.transform.forward).normalized;
                right = Geo.Flat(cam.transform.right).normalized;
            }
            _move = fwd * m.y + right * m.x;
            _walk = _in.Walk.IsPressed();

            // Aim: mouse ray on the ground plane at the sidekick's height, or right stick around the sidekick.
            var self = GetComponent<SidekickAgent>();
            var origin = self != null ? self.Position : transform.position;
            if (_in.UsingGamepad)
            {
                var s = _in.AimStick.ReadValue<Vector2>();
                if (s.sqrMagnitude > 0.09f)
                {
                    _aim = origin + (fwd * s.y + right * s.x).normalized * Mathf.Lerp(3f, 12f, Mathf.Clamp01(s.magnitude));
                    _hasAim = true;
                }
                else if (_move.sqrMagnitude > 0.01f)
                {
                    _aim = origin + _move.normalized * 6f;
                    _hasAim = true;
                }
            }
            else if (cam != null)
            {
                _ground = new Plane(Vector3.up, new Vector3(0f, origin.y, 0f));
                var ray = cam.ScreenPointToRay(_in.AimPointer.ReadValue<Vector2>());
                if (_ground.Raycast(ray, out float d))
                {
                    _aim = ray.GetPoint(d);
                    _hasAim = true;
                }
                // Pointing at a shooter up on a perch lands the ground-plane aim metres behind him (too far for skills to
                // mark him). Pointing at his body means aiming at *him*.
                var raised = RaisedEnemyUnder(ray, origin.y);
                if (raised != null)
                {
                    _aim = new Vector3(raised.Position.x, origin.y, raised.Position.z);
                    _hasAim = true;
                }
            }

            if (_in.Attack.WasPressedThisFrame()) _latched.Attack = true;
            if (_in.Dodge.WasPressedThisFrame()) _latched.Dodge = true;
            if (_in.Ping.WasPressedThisFrame()) _latched.Ping = true;
            if (_in.Interact.WasPressedThisFrame()) _latched.Interact = true;
            if (_in.Crouch.WasPressedThisFrame()) _latched.CrouchToggle = true;
            _skillHeld = false;
            for (int i = 0; i < _in.Skills.Length; i++)
            {
                if (_in.Skills[i].WasPressedThisFrame()) _latched.Skill = i;
                if (_in.Skills[i].IsPressed()) _skillHeld = true;
            }
        }

        /// <summary>The visible enemy standing well above the sidekick's ground whose body the pointer ray passes through.</summary>
        static Agent RaisedEnemyUnder(Ray ray, float groundY)
        {
            Agent best = null;
            float bestD = 0.6f;
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is HS.Enemies.EnemyAgent e) || !e.IsAlive || e.IsHidden || e.Position.y - groundY < 0.6f) continue;
                for (float h = 0.3f; h <= 1.6f; h += 0.65f)
                {
                    float dist = Vector3.Cross(ray.direction, e.Position + Vector3.up * h - ray.origin).magnitude;
                    if (dist < bestD)
                    {
                        bestD = dist;
                        best = e;
                    }
                }
            }
            return best;
        }

        public SidekickCommand Next(SidekickAgent self)
        {
            var c = _latched;
            c.Move = _move;
            c.Walk = _walk;
            c.AimPoint = _aim;
            c.HasAim = _hasAim;
            c.SkillHeld = _skillHeld;
            _latched = SidekickCommand.None;
            return c;
        }
    }
}
