using System.Collections.Generic;
using UnityEngine;

namespace HS.Hero
{
    /// <summary>Authored route: ordered child markers (GDD §4.2 "authored route graph per room").</summary>
    public sealed class RouteGraph : MonoBehaviour
    {
        public int RoomIndex;

        public List<RouteNode> Nodes()
        {
            var list = new List<RouteNode>();
            foreach (Transform c in transform)
            {
                var m = c.GetComponent<RouteMarker>();
                list.Add(new RouteNode
                {
                    Position = c.position,
                    Threshold = m != null && m.Threshold,
                    Chokepoint = m != null && m.Chokepoint,
                    Label = m != null ? m.Label : c.name,
                    RoomIndex = RoomIndex,
                });
            }
            return list;
        }

        void OnDrawGizmos()
        {
            Transform prev = null;
            foreach (Transform c in transform)
            {
                var m = c.GetComponent<RouteMarker>();
                Gizmos.color = m != null && m.Threshold ? Color.yellow : m != null && m.Chokepoint ? Color.cyan : Color.white;
                Gizmos.DrawSphere(c.position + Vector3.up * 0.2f, 0.25f);
                if (prev != null) Gizmos.DrawLine(prev.position + Vector3.up * 0.2f, c.position + Vector3.up * 0.2f);
                prev = c;
            }
        }
    }
}
