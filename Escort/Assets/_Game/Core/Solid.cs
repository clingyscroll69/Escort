using UnityEngine;

namespace HS.Core
{
    /// <summary>
    /// Level geometry between two points (walls, decks, rocks; never people): what stops a throw, a burst or a sight line.
    /// The same layer the projectiles and sight lines test against.
    /// </summary>
    public static class Solid
    {
        static int _mask = -1;
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        static int Mask
        {
            get
            {
                if (_mask == -1) _mask = LayerMask.GetMask("Default");
                return _mask;
            }
        }

        /// <summary>The first solid surface on the segment a→b (ignoring the last <paramref name="endSlack"/> metres), if any.</summary>
        public static bool FirstHit(Vector3 a, Vector3 b, out Vector3 point, float endSlack = 0.05f)
        {
            point = b;
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f) return false;
            int n = Physics.RaycastNonAlloc(a, d / len, Hits, len, Mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider.GetComponentInParent<Agent>() != null) continue; // people are not walls
                if (h.distance > len - endSlack || h.distance >= best) continue;
                best = h.distance;
                point = h.point;
            }
            return best < float.MaxValue;
        }

        /// <summary>Is anything solid between these two points?</summary>
        public static bool Between(Vector3 a, Vector3 b, float endSlack = 0.05f) => FirstHit(a, b, out _, endSlack);

        /// <summary>The floor under a point (the first surface within <paramref name="depth"/> metres below it), or the point lowered by its fallback.</summary>
        public static Vector3 FloorUnder(Vector3 p, float depth = 8f, float fallback = 0.4f) =>
            FirstHit(p, p + Vector3.down * depth, out var floor, 0f) ? floor : p + Vector3.down * fallback;
    }
}
