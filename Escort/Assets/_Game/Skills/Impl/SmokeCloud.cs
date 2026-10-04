using System.Collections.Generic;
using HS.Core;
using HS.Presentation;
using UnityEngine;

namespace HS.Skills.Impl
{
    /// <summary>
    /// Smoke Bomb's cloud (GDD §5: "breaks aggro and blocks a stone's view"): a column of smoke nobody sees through — not
    /// Callum, not a chronicle stone, not a shooter. Sight lines are judged in the ground plane: a line that passes within
    /// the cloud's radius, or starts or ends inside it, is blocked. Lives on sim time.
    /// </summary>
    public sealed class SmokeCloud : MonoBehaviour, ISimTickable
    {
        public float Radius = 3f;
        public float Until;
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

        public bool Contains(Vector3 p) => Geo.FlatDistance(p, transform.position) <= Radius;

        /// <summary>Does any live cloud stand between these two points?</summary>
        public static bool Blocks(Vector3 from, Vector3 to)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i];
                if (c == null || !c.isActiveAndEnabled) continue;
                var p = Geo.Flat(c.transform.position);
                var a = Geo.Flat(from);
                var b = Geo.Flat(to);
                var ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                if (Vector3.Distance(p, a + ab * t) <= c.Radius) return true;
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
