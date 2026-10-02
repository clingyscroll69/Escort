using HS.Core;
using UnityEngine;

namespace HS.Presentation
{
    /// <summary>
    /// Smooths the fixed-60 Hz simulation for rendering: the visual child is drawn between the previous and current
    /// sim poses of its parent using SimLoop.Alpha. Gameplay never reads the visual transform.
    /// </summary>
    public sealed class VisualInterpolator : MonoBehaviour
    {
        const float SnapDistance = 3f;
        Transform _root;
        Vector3 _prevPos, _curPos;
        Quaternion _prevRot, _curRot;
        bool _init;
        SimLoop _loop;

        void OnEnable()
        {
            _root = transform.parent;
            _loop = SimLoop.Ensure();
            _loop.BeforeTick += OnBeforeTick;
            _loop.AfterTick += OnAfterTick;
            _init = false;
        }

        void OnDisable()
        {
            if (_loop == null) return;
            _loop.BeforeTick -= OnBeforeTick;
            _loop.AfterTick -= OnAfterTick;
        }

        void OnBeforeTick()
        {
            if (_root == null) return;
            if (!_init) Snap();
            _prevPos = _curPos;
            _prevRot = _curRot;
        }

        void OnAfterTick(int tick)
        {
            if (_root == null) return;
            _curPos = _root.position;
            _curRot = _root.rotation;
            if ((_curPos - _prevPos).sqrMagnitude > SnapDistance * SnapDistance)
            {
                _prevPos = _curPos;
                _prevRot = _curRot;
            }
        }

        void Snap()
        {
            _prevPos = _curPos = _root.position;
            _prevRot = _curRot = _root.rotation;
            _init = true;
        }

        void LateUpdate()
        {
            if (_root == null) return;
            if (!_init) Snap();
            float a = _loop != null ? _loop.Alpha : 1f;
            transform.SetPositionAndRotation(Vector3.LerpUnclamped(_prevPos, _curPos, a), Quaternion.SlerpUnclamped(_prevRot, _curRot, a));
        }
    }
}
