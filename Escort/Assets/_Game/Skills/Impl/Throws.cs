using System;
using System.Collections.Generic;
using HS.Core;
using UnityEngine;

namespace HS.Skills.Impl
{
    /// <summary>
    /// Thrown things (Pocket Sand, Smoke Bomb, Bait &amp; Switch's decoy) in three dimensions. A throw flies from her hand
    /// to what she aims at: someone within 1.6 m of the aim (as the crossbow and the sling mark a target) means his chest,
    /// up or down a level; otherwise the aimed spot at her own height. Its range is the straight line from her hand, and
    /// the first solid thing on the way stops it. A burst then reaches anyone whose body is within its radius in 3D with
    /// nothing solid between: sand on the floor below a perch never reaches the man on it.
    /// </summary>
    public static class Throws
    {
        public const float HandHeight = 1.4f, MarkRadius = 1.6f, ChestHeight = 1.1f, SpotHeight = 0.4f;

        public static Vector3 Hand(Agent user) => user.Position + Vector3.up * HandHeight;

        /// <summary>Where a throw aimed at this point bursts (see the class summary). Agents passing <paramref name="markable"/> can be aimed at.</summary>
        public static Vector3 Landing(Agent user, Vector3 aim, float range, Func<Agent, bool> markable)
        {
            var hand = Hand(user);
            var marked = markable == null ? null : AgentRegistry.Nearest(aim, MarkRadius, a => a != user && a.IsAlive && markable(a));
            var target = marked != null ? marked.Position + Vector3.up * ChestHeight : new Vector3(aim.x, user.Position.y + SpotHeight, aim.z);
            var d = target - hand;
            float len = d.magnitude;
            if (len < 1e-3f) return target;
            if (len > range) target = hand + d / len * range; // out of reach: it falls short, along the same line
            if (Solid.FirstHit(hand, target, out var stop, 0f)) target = stop - d / len * 0.15f; // it bursts against the wall
            return target;
        }

        /// <summary>Does a burst at <paramref name="p"/> reach this body: within <paramref name="radius"/> of it in 3D, nothing solid between?</summary>
        public static bool Reaches(Vector3 p, float radius, Agent a)
        {
            Melee.BodySpan(a, out float bottom, out float top);
            var near = new Vector3(a.Position.x, Mathf.Clamp(p.y, bottom, top), a.Position.z);
            if (Vector3.Distance(p, near) > radius) return false;
            // The sight line ends a little inside the body, so the floor under his feet never counts as cover.
            var inside = new Vector3(near.x, Mathf.Clamp(p.y, bottom + 0.3f, Mathf.Max(bottom + 0.3f, top - 0.2f)), near.z);
            return !Solid.Between(p, inside);
        }

        /// <summary>Everyone a burst at <paramref name="p"/> reaches (filtered).</summary>
        public static void Caught(Vector3 p, float radius, List<Agent> results, Func<Agent, bool> filter)
        {
            results.Clear();
            var all = AgentRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || !a.IsAlive || (filter != null && !filter(a))) continue;
                if (Geo.FlatDistance(p, a.Position) > radius) continue;
                if (Reaches(p, radius, a)) results.Add(a);
            }
        }
    }
}
