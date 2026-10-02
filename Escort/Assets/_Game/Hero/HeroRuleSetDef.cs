using System.Collections.Generic;
using HS.Core;
using UnityEngine;

namespace HS.Hero
{
    /// <summary>Data-driven rule lists per Stage (GDD §4.4: "Each stage changes the hero's rule set").</summary>
    [CreateAssetMenu(menuName = "HS/Hero Rule Set", fileName = "RuleSet")]
    public sealed class HeroRuleSetDef : ScriptableObject
    {
        public string heroId = "callum";
        public List<RuleEntry> s0 = new List<RuleEntry>();
        public List<RuleEntry> s1 = new List<RuleEntry>();
        public List<RuleEntry> s2 = new List<RuleEntry>();
        public List<RuleEntry> s3 = new List<RuleEntry>();

        public List<RuleEntry> For(Stage s)
        {
            // Higher stages fall back to the closest authored lower stage.
            var lists = new[] { s0, s1, s2, s3 };
            for (int i = (int)s; i >= 0; i--)
                if (lists[i] != null && lists[i].Count > 0) return lists[i];
            return s0;
        }
    }
}
