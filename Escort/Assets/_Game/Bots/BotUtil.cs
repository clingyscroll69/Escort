using HS.Core;
using HS.Hero;
using HS.Hero.Callum;
using HS.Sidekick;
using HS.Skills;
using UnityEngine;

namespace HS.Bots
{
    /// <summary>Shared bot helpers: skill slots by id, movement toward a goal, Callum's cone as a player would read it.</summary>
    public static class BotUtil
    {
        public static int SlotOf(SidekickAgent sk, string id)
        {
            var skills = sk.GetComponent<SidekickSkills>();
            if (skills == null) return -1;
            var lo = skills.System.Loadout;
            for (int i = 0; i < lo.Count; i++) if (lo[i] == id) return i;
            return -1;
        }

        public static bool Ready(SidekickAgent sk, string id)
        {
            var skills = sk.GetComponent<SidekickSkills>();
            var st = skills != null ? skills.System.Get(id) : null;
            return st != null && st.Ready && SlotOf(sk, id) >= 0;
        }

        public static Vector3 MoveTo(SidekickAgent self, Vector3 goal, float stopAt = 0.6f)
        {
            float d = Geo.FlatDistance(self.Position, goal);
            if (d <= stopAt) return Vector3.zero;
            var via = Steer(self.Position, goal);
            float dv = Geo.FlatDistance(self.Position, via);
            return Vector3.ClampMagnitude(Geo.DirTo(self.Position, via) * Mathf.Max(0.35f, Mathf.Min(d, dv + 2f) / 2.5f), 1f);
        }

        static bool Clear(Vector3 a, Vector3 b) => WitnessCone.HasLineOfSight(a + Vector3.up * 0.5f, b + Vector3.up * 0.5f);

        /// <summary>
        /// Minimal navigation: straight at the goal when nothing is in the way, otherwise via the hero's route nodes
        /// (walkable by construction — the arena route runs through its gate): the visible node with the shortest detour.
        /// </summary>
        public static Vector3 Steer(Vector3 from, Vector3 goal)
        {
            if (Clear(from, goal)) return goal;
            var hero = RunContext.Current != null ? RunContext.Current.Hero as HeroAgent : null;
            if (hero == null) return goal;
            Vector3 best = goal;
            float bestCost = float.MaxValue;
            var nodes = hero.Route.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i].Position;
                if (Geo.FlatDistance(from, n) < 0.8f || !Clear(from, n)) continue;
                float cost = Geo.FlatDistance(from, n) + Geo.FlatDistance(n, goal) * (Clear(n, goal) ? 1f : 1.6f);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = n;
                }
            }
            return best;
        }

        /// <summary>Would Callum see a deed at this point, done from that spot? (the same check the game uses)</summary>
        public static bool Watched(HeroAgent hero, Vector3 deed, Vector3 actor)
        {
            var cm = hero != null ? hero.Module as CallumModule : null;
            return cm != null && cm.Witnesses(deed, actor, out _);
        }

        /// <summary>A spot behind his back (out of his cone), offset sideways, at a distance.</summary>
        public static Vector3 BehindHim(HeroAgent hero, float distance, float side) =>
            hero.Position - hero.Forward * distance + hero.transform.right * side;
    }
}
