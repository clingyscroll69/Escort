using UnityEngine;

namespace HS.Core
{
    /// <summary>
    /// Kinematic movement on a CharacterController, driven only from SimTick. CharacterController.Move is immediate
    /// (sweep-and-slide against static colliders) so it needs no physics step and stays deterministic.
    /// </summary>
    public sealed class CharacterMotor
    {
        readonly CharacterController _cc;
        readonly Transform _t;
        float _verticalSpeed;

        public Vector3 Velocity { get; private set; }
        public float Speed => Geo.Flat(Velocity).magnitude;
        public float SpeedMultiplier = 1f;
        /// <summary>Ground underfoot (bogs, water): set each tick by the terrain zones; 1 on firm ground.</summary>
        public float TerrainMul = 1f;

        public CharacterMotor(CharacterController cc)
        {
            _cc = cc;
            _t = cc.transform;
        }

        public Vector3 Position => _t.position;
        public Vector3 Forward => _t.forward;

        /// <summary>Move toward a desired planar velocity with acceleration, then apply gravity.</summary>
        public void Move(Vector3 desiredPlanarVelocity, float accel, float dt)
        {
            var desired = Geo.Flat(desiredPlanarVelocity) * SpeedMultiplier * TerrainMul;
            var cur = Geo.Flat(Velocity);
            var planar = Vector3.MoveTowards(cur, desired, accel * dt);
            Integrate(planar, dt);
        }

        /// <summary>Move with an exact planar velocity (dodges, knockbacks).</summary>
        public void MoveExact(Vector3 planarVelocity, float dt) => Integrate(Geo.Flat(planarVelocity), dt);

        void Integrate(Vector3 planar, float dt)
        {
            if (_cc.isGrounded && _verticalSpeed < 0f) _verticalSpeed = -2f;
            else _verticalSpeed = Mathf.Max(_verticalSpeed - 25f * dt, -30f);
            var delta = planar * dt + Vector3.up * (_verticalSpeed * dt);
            if (_cc.enabled) _cc.Move(delta);
            else _t.position += delta;
            Velocity = planar;
            // Safety net: never fall forever through a hole in the floor.
            if (_t.position.y < -20f) Teleport(new Vector3(_t.position.x, 2f, _t.position.z));
        }

        public void Stop() => Velocity = Vector3.zero;

        public void FaceDirection(Vector3 dir, float turnSpeedDeg, float dt)
        {
            dir = Geo.Flat(dir);
            if (dir.sqrMagnitude < 1e-6f) return;
            var target = Quaternion.LookRotation(dir, Vector3.up);
            _t.rotation = Quaternion.RotateTowards(_t.rotation, target, turnSpeedDeg * dt);
        }

        public void FaceInstant(Vector3 dir)
        {
            dir = Geo.Flat(dir);
            if (dir.sqrMagnitude < 1e-6f) return;
            _t.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        public void Teleport(Vector3 pos)
        {
            bool was = _cc.enabled;
            _cc.enabled = false;
            _t.position = pos;
            _cc.enabled = was;
            Velocity = Vector3.zero;
            _verticalSpeed = 0f;
        }

        /// <summary>Push out of another agent (soft separation, called by the registry).</summary>
        public void Nudge(Vector3 planarOffset)
        {
            if (_cc.enabled) _cc.Move(Geo.Flat(planarOffset));
            else _t.position += Geo.Flat(planarOffset);
        }
    }
}
