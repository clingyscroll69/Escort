using HS.Core;
using UnityEngine;

namespace HS.Hero
{
    /// <summary>Feeds scene RouteGraphs (in order) into the hero's route follower at start.</summary>
    public sealed class RouteBinder : MonoBehaviour
    {
        public HeroAgent Hero;
        public RouteGraph[] Graphs;

        void Start()
        {
            if (Hero == null) Hero = FindAnyObjectByType<HeroAgent>();
            if (Hero == null || Graphs == null) return;
            var nodes = new System.Collections.Generic.List<RouteNode>();
            foreach (var g in Graphs) if (g != null) nodes.AddRange(g.Nodes());
            Hero.Route.SetNodes(nodes);
        }
    }
}
