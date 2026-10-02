using UnityEngine;

namespace HS.Presentation
{
    /// <summary>Returns a pooled burst after its lifetime.</summary>
    public sealed class VfxReturn : MonoBehaviour
    {
        VfxKind _kind;
        ParticleSystem _ps;
        float _t;

        public void Arm(VfxKind kind, ParticleSystem ps, float seconds)
        {
            _kind = kind;
            _ps = ps;
            _t = seconds;
        }

        void Update()
        {
            _t -= Time.deltaTime;
            if (_t <= 0f) Vfx.Return(_kind, _ps);
        }
    }
}
