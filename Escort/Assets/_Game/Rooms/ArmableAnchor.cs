using UnityEngine;

namespace HS.Rooms
{
    public enum ArmableKind { FallingTree, LogPile, BarrelStack, LooseMasonry, HangingCart }

    /// <summary>A prop the sidekick can arm with Loosen Bolt (GDD §5): collapses on ping or when the hero passes.</summary>
    public sealed class ArmableAnchor : MonoBehaviour
    {
        public ArmableKind Kind;
        [Tooltip("Where the collapse lands, relative to this anchor (local).")]
        public Vector3 ImpactOffset = new Vector3(0f, 0f, 2.5f);
        public float ImpactRadius = 3f;
        public float Damage = 90f;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.5f, Vector3.one);
            Gizmos.DrawWireSphere(transform.TransformPoint(ImpactOffset), ImpactRadius);
        }
    }
}
