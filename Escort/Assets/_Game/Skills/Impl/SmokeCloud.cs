using System.Collections.Generic;
using HS.Core;
using HS.Presentation;
using UnityEngine;

namespace HS.Skills.Impl
{
    /// <summary>
    /// Smoke Bomb's cloud (GDD §5: "breaks aggro and blocks a stone's view"): a column of smoke standing on the floor where
    /// it landed, <see cref="Height"/> m tall, that nobody sees through — not Callum, not a chronicle stone, not a shooter.
    /// A sight line that passes through the column (or starts or ends inside it) is blocked; one that passes over or under
    /// it is not. Lives on sim time.
    /// </summary>
    public sealed class SmokeCloud : MonoBehaviour, ISimTickable
    {
        public float Radius = 3f;
        public float Until;
        public const float Height = 3f;

        Vector3 Base => transform.position;
        Vector3 Top => transform.position + Vector3.up * Height;
        public int TickOrder => TickOrders.Status;

        public static readonly List<SmokeCloud> All = new List<SmokeCloud>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        float _puffT;

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            SimLoop.Register(this);
        }

        void OnDisable()
        {
            All.Remove(this);
            SimLoop.Unregister(this);
        }

        public bool Contains(Vector3 p) =>
            Geo.FlatDistance(p, Base) <= Radius && p.y >= Bottom && p.y <= Top.y;

        /// <summary>A little below the floor it stands on, so feet in it count as inside.</summary>
        float Bottom => Base.y - 0.5f;

        /// <summary>Does the segment pass through this column? (Clipped to the column's height, then judged on the ground plane.)</summary>
        public bool Crosses(Vector3 from, Vector3 to)
        {
            float lo = 0f, hi = 1f, dy = to.y - from.y;
            if (Mathf.Abs(dy) < 1e-6f)
            {
                if (from.y < Bottom || from.y > Top.y) return false;
            }
            else
            {
                float ta = (Bottom - from.y) / dy, tb = (Top.y - from.y) / dy;
                lo = Mathf.Max(lo, Mathf.Min(ta, tb));
                hi = Mathf.Min(hi, Mathf.Max(ta, tb));
                if (lo > hi) return false;
            }
            var a = Geo.Flat(Vector3.Lerp(from, to, lo));
            var b = Geo.Flat(Vector3.Lerp(from, to, hi));
            var p = Geo.Flat(Base);
            var ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector3.Distance(p, a + ab * t) <= Radius;
        }

        /// <summary>Does any live cloud stand between these two points?</summary>
        public static bool Blocks(Vector3 from, Vector3 to)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i];
                if (c == null || !c.isActiveAndEnabled) continue;
                if (c.Crosses(from, to)) return true;
            }
            return false;
        }

        public static bool Inside(Vector3 p)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null && All[i].isActiveAndEnabled && All[i].Contains(p)) return true;
            return false;
        }

        public void SimTick(float dt)
        {
            var ctx = RunContext.Current;
            if (ctx != null && ctx.SimTime >= Until)
            {
                Destroy(gameObject);
                return;
            }
            _puffT -= dt;
            if (_puffT > 0f) return;
            _puffT = 0.3f;
            // A slow churn of puffs over the column (presentation; the sim reads only the radius).
            float a = (ctx != null ? ctx.SimTime : 0f) * 2.3f;
            for (int k = 0; k < 3; k++)
            {
                float ang = a + k * 2.094f;
                var off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * Radius * 0.55f;
                Vfx.Burst(VfxKind.Dust, transform.position + off + Vector3.up * (0.6f + 0.5f * k), Radius * 0.55f);
            }
        }

        public static SmokeCloud Release(Vector3 at, float radius, float seconds, RunContext ctx)
        {
            var go = new GameObject("SmokeCloud");
            go.transform.position = at;
            var c = go.AddComponent<SmokeCloud>();
            c.Radius = radius;
            c.Until = (ctx != null ? ctx.SimTime : 0f) + seconds;
            Vfx.Burst(VfxKind.Dust, at + Vector3.up * 1f, radius);
            return c;
        }
    }
}
