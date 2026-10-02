using HS.Core;
using UnityEngine;

namespace HS.Rooms
{
    /// <summary>Chronicle stone placement (GDD §4.3). Forward = the direction its eye faces.</summary>
    public sealed class StoneAnchor : MonoBehaviour, IDynamicVisual
    {
        public float ViewRange = 14f;
        public float ViewAngle = 200f;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 0.9f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up, 0.6f);
        }
    }
}
