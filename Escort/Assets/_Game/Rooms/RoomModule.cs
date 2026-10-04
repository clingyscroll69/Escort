using System.Collections.Generic;
using HS.Hero;
using UnityEngine;

namespace HS.Rooms
{
    /// <summary>
    /// Root of a hand-authored room prefab. Local +Z is the travel direction: entry at z = 0, exit at z = Length.
    /// Children carry markers (spawns, encounter zones, armable anchors, stones, hazards, explore caches) and
    /// "Variants/Variant_N" groups the assembler toggles by seed.
    /// </summary>
    public sealed class RoomModule : MonoBehaviour
    {
        public string ModuleId;
        public string DisplayName;
        public RoomKind Kind;
        public float Length = 40f;
        public float Width = 26f;
        [Tooltip("XP pot for this room (GDD 4.1: 50% clear, 25% assist, 25% exploration).")]
        public int XpPot = 100;
        [Tooltip("Which chapter's library this module belongs to.")]
        public int Chapter = 1;
        public int RoomIndex { get; private set; }
        public int Variant { get; private set; }

        public RouteGraph Route => GetComponentInChildren<RouteGraph>(true);

        public int VariantCount
        {
            get
            {
                var v = transform.Find("Variants");
                return v == null ? 1 : Mathf.Max(1, v.childCount);
            }
        }

        /// <summary>Enable one variant group (0-based) and disable the others.</summary>
        public void ApplyVariant(int variant)
        {
            Variant = variant;
            var v = transform.Find("Variants");
            if (v == null) return;
            for (int i = 0; i < v.childCount; i++) v.GetChild(i).gameObject.SetActive(i == variant);
        }

        public void SetRoomIndex(int index)
        {
            RoomIndex = index;
            foreach (var g in GetComponentsInChildren<RouteGraph>(true)) g.RoomIndex = index;
        }

        public IEnumerable<T> Active<T>() where T : Component => GetComponentsInChildren<T>(false);

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.4f);
            var c = transform.position + transform.forward * Length * 0.5f;
            Gizmos.matrix = Matrix4x4.TRS(c, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(Width, 0.1f, Length));
        }
    }
}
