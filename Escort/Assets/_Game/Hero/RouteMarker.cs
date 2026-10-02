using UnityEngine;

namespace HS.Hero
{
    /// <summary>Flags for a route node (child of a RouteGraph).</summary>
    public sealed class RouteMarker : MonoBehaviour
    {
        public bool Threshold;
        public bool Chokepoint;
        public string Label;
    }
}
