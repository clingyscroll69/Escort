using UnityEngine;

namespace HS.Rooms
{
    /// <summary>Where an enemy of an archetype appears, which encounter group it belongs to, and how it starts.</summary>
    public sealed class SpawnMarker : MonoBehaviour
    {
        public string Archetype = "thug";   // Tuning.enemies id
        public int Group;                   // EncounterZone.Group
        public bool Hidden;                 // ambusher in a hedge
        public bool Elevated;               // behind a barricade / on a ledge: prefers ranged
        public bool Sleeping;               // Unready from the start
        [Tooltip("Seconds after the encounter starts before this one joins (reinforcements).")]
        public float Delay;
        [Tooltip("Nemesis squad (chapter 4): appears only when Curator Intel is at least this (0–3).")]
        public int MinIntel;

        void OnDrawGizmos()
        {
            Gizmos.color = Hidden ? new Color(0.3f, 0.6f, 0.2f) : Archetype == "crossbowman" || Archetype == "archer" ? new Color(0.9f, 0.3f, 0.3f) : Color.red;
            Gizmos.DrawWireSphere(transform.position + Vector3.up, 0.5f);
            Gizmos.DrawLine(transform.position + Vector3.up, transform.position + Vector3.up + transform.forward);
        }
    }
}
