using UnityEngine;

namespace HS.Rooms
{
    /// <summary>
    /// An encounter: its spawns activate when the hero enters the zone (or reaches a route label). The room is clear
    /// when every encounter has resolved.
    /// </summary>
    public sealed class EncounterZone : MonoBehaviour
    {
        public int Group;
        public float Radius = 9f;
        [Tooltip("Hold the hero's route while this encounter is active (he fights here).")]
        public bool HoldRoute = true;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, Radius);
        }
    }
}
