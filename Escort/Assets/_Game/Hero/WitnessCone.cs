using HS.Core;
using UnityEngine;

namespace HS.Hero
{
    /// <summary>
    /// Can an observer see a point? Cone (full angle) + range + line of sight against level geometry.
    /// Used by Callum (120°, 12 m; narrowed by Quiet Feet) and by chronicle stones.
    /// </summary>
    public static class WitnessCone
    {
        static int _mask = -1;

        static int Mask
        {
            get
            {
                if (_mask == -1) _mask = LayerMask.GetMask("Default");
                return _mask;
            }
        }

        public static bool Sees(Vector3 eye, Vector3 forward, float fullAngle, float range, Vector3 point)
        {
            if (Geo.FlatDistance(eye, point) > range) return false;
            if (Geo.AngleTo(eye, forward, point) > fullAngle * 0.5f) return false;
            return HasLineOfSight(eye, point + Vector3.up * 1.0f);
        }

        public static bool HasLineOfSight(Vector3 eye, Vector3 target)
        {
            var dir = target - eye;
            float dist = dir.magnitude;
            if (dist < 0.05f) return true;
            if (HS.Skills.Impl.SmokeCloud.Blocks(eye, target)) return false; // nobody sees through smoke
            var hits = Physics.RaycastAll(eye, dir / dist, dist, Mask, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.collider.GetComponentInParent<Agent>() != null) continue; // people don't block sight lines
                if (h.distance > dist - 0.3f) continue;
                return false;
            }
            return true;
        }
    }
}
