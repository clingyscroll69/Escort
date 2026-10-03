using System;
using System.Collections.Generic;
using System.Globalization;

namespace HS.Skills
{
    /// <summary>How a skill is explained in the picker and the Field Guide: a hook, how to use it, its numbers per rank
    /// (computed from the <see cref="SkillDefinition"/> so balance changes flow through), and how Callum's code reads it.</summary>
    public sealed class SkillGuide
    {
        public string Id, Tagline, HowTo, CallumView;
        public (string label, Func<SkillDefinition, int, string> value)[] Stats;
    }

    /// <summary>
    /// Guides for the implemented skills (spec §5.2). Text describes uses, never recommendations (GDD §4.7). Draft copy
    /// for the owner to rewrite. {tokens} are keys (HS.Tutorial.KeyGlyphs).
    /// </summary>
    public static class SkillGuides
    {
        static string N(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        static (string, Func<SkillDefinition, int, string>) Stat(string label, Func<SkillDefinition, int, string> f) => (label, f);
        static readonly (string, Func<SkillDefinition, int, string>) Cooldown = Stat("Cooldown", (d, r) => N(d.Cooldown(r)) + " s");

        static readonly Dictionary<string, SkillGuide> Guides = new Dictionary<string, SkillGuide>
        {
            ["pocket_sand"] = new SkillGuide
            {
                Id = "pocket_sand", Tagline = "A handful of grit, thrown where it hurts.",
                HowTo = "Aim at a spot with {aim} and press its key. Everyone in the cloud is blinded, and hidden foes are revealed.",
                CallumView = "A dirty trick if he (or a glowing stone) sees it. Sand his own opponent and he'll wait for the man to recover.",
                Stats = new[] { Stat("Blind", (d, r) => N(d.A(r)) + " s"), Stat("Range", (d, r) => N(d.B(r)) + " m"), Cooldown },
            },
            ["loosen_bolt"] = new SkillGuide
            {
                Id = "loosen_bolt", Tagline = "Everything on this road is held together by optimism.",
                HowTo = "Stand by a marked prop and press its key to kneel and arm it. {ping} brings it down; so does Callum leading a bandit under it.",
                CallumView = "A dirty trick if he sees the collapse. Arming it isn't: it's the drop that counts.",
                Stats = new[] { Stat("Armed at once", (d, r) => N(d.A(r))), Stat("Arming", (d, r) => N(d.B(r)) + " s") },
            },
            ["quiet_feet"] = new SkillGuide
            {
                Id = "quiet_feet", Tagline = "Nobody notices the help.",
                HowTo = "Passive. Works while you crouch ({crouch}).",
                CallumView = "Nothing to see. That's the point.",
                Stats = new[] { Stat("Heard within", (d, r) => N(d.A(r)) + " m"), Stat("His cone", (d, r) => N(d.B(r) * 100f) + "%") },
            },
            ["crossbow"] = new SkillGuide
            {
                Id = "crossbow", Tagline = "Slow to load, hard to argue with.",
                HowTo = "Aim with {aim} and press its key. Bolts stop at cover, and at him if he's in the way.",
                CallumView = "A bolt into his duel is a slight; one into a helpless man is a disgrace. Unseen, it's just a bolt.",
                Stats = new[]
                {
                    Stat("Damage", (d, r) => N(d.A(r))), Stat("Range", (d, r) => N(d.B(r)) + " m"), Stat("Reload", (d, r) => N(d.Cooldown(r)) + " s"),
                    Stat("Pierces", (d, r) => r >= 2 ? "yes" : "no"),
                },
            },
            ["bandage"] = new SkillGuide
            {
                Id = "bandage", Tagline = "Hold still. This will sting.",
                HowTo = "Press its key beside him (or alone, for yourself), then stay still for the whole channel. He holds still too.",
                CallumView = "Honest work. He holds still for it.",
                Stats = new[] { Stat("Heals", (d, r) => N(d.A(r)) + " over 6 s"), Stat("Channel", (d, r) => N(d.B(r)) + " s"), Cooldown },
            },
            ["cover_story"] = new SkillGuide
            {
                Id = "cover_story", Tagline = "\"That? The wind, sir.\"",
                HowTo = "Press its key within earshot of him (14 m). Right after he catches you, it also softens the fallout.",
                CallumView = "He chooses to believe you. Mostly.",
                Stats = new[] { Stat("Honor", (d, r) => "+" + N(d.A(r))), Stat("After being caught", (d, r) => "within " + N(d.B(r)) + " s"), Cooldown },
            },
        };

        public static SkillGuide Get(string id) => id != null && Guides.TryGetValue(id, out var g) ? g : null;

        public static IEnumerable<SkillGuide> All => Guides.Values;

        public static List<(string label, string rank1, string rank2)> StatLines(SkillDefinition def)
        {
            var lines = new List<(string, string, string)>();
            var g = def != null ? Get(def.id) : null;
            if (g?.Stats == null) return lines;
            foreach (var (label, value) in g.Stats) lines.Add((label, value(def, 1), value(def, 2)));
            return lines;
        }

        public static string IconId(string skillId) => "skill_" + skillId;

        public static string FamilyIcon(SkillFamily f) => f == SkillFamily.Capstone ? "honor" : "fam_" + f.ToString().ToLowerInvariant();

        /// <summary>How Callum's code classes a skill, from its data (the guide's note says the rest).</summary>
        public static string Conduct(SkillDefinition def) => def == null ? "" : def.dishonor switch
        {
            HS.Core.SabotageSeverity.Major => "DIRTY TRICK IF SEEN",
            HS.Core.SabotageSeverity.Minor => "A SLIGHT IF SEEN",
            _ => "ABOVE BOARD",
        };
    }
}
