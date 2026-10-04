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
            ["splint_and_stitch"] = new SkillGuide
            {
                Id = "splint_and_stitch", Tagline = "Bones set, wounds closed, complaints ignored.",
                HowTo = "Press its key right beside him and stay still for the whole channel. It treats one serious wound: cracked ribs, fever or a concussion.",
                CallumView = "Honest work. He holds still for it, and grumbles.",
                Stats = new[] { Stat("Channel", (d, r) => N(d.A(r)) + " s"), Stat("Per chapter", (d, r) => N(d.B(r))), Cooldown },
            },
            ["pull_back"] = new SkillGuide
            {
                Id = "pull_back", Tagline = "A rope, a heave, and an indignant knight.",
                HowTo = "Press its key with him in reach: he's hauled toward you, out of whatever had him. A trip or a snare is shaken off.",
                CallumView = "Undignified, but no dishonour in it. He may even thank you.",
                Stats = new[] { Stat("Reach", (d, r) => N(d.A(r)) + " m"), Stat("Pull", (d, r) => N(d.B(r)) + " m"), Cooldown },
            },
            ["sling"] = new SkillGuide
            {
                Id = "sling", Tagline = "A stone, a strap, and a long tradition of annoying people.",
                HowTo = "Aim with {aim} and press its key. Light, fast, and it never runs out; a hit makes them flinch.",
                CallumView = "A stone into his duel is a slight; one at a helpless man is a disgrace. Unseen, it's just a stone.",
                Stats = new[] { Stat("Damage", (d, r) => N(d.A(r))), Stat("Range", (d, r) => N(d.B(r)) + " m"), Cooldown },
            },
            ["read_the_room"] = new SkillGuide
            {
                Id = "read_the_room", Tagline = "Everyone is planning something. Now you know what.",
                HowTo = "Press its key. For a few seconds, every foe near you shows what he means to do, and anyone hiding is marked where he lies.",
                CallumView = "He never notices you doing it.",
                Stats = new[] { Stat("Lasts", (d, r) => N(d.A(r)) + " s"), Stat("Radius", (d, r) => N(d.B(r)) + " m"), Cooldown },
            },
            ["read_runes"] = new SkillGuide
            {
                Id = "read_runes", Tagline = "Old words, written to keep people out. You read them anyway.",
                HowTo = "Stand at a rune seal and press its key, then stay still while you read. The seal goes dark and the way opens.",
                CallumView = "Scholarship. He finds it faintly suspicious, and very useful.",
                Stats = new[] { Stat("Channel", (d, r) => N(d.A(r)) + " s"), Stat("Reach", (d, r) => N(d.B(r)) + " m"), Cooldown },
            },
            ["lockpick"] = new SkillGuide
            {
                Id = "lockpick", Tagline = "Every lock is a puzzle someone else already solved.",
                HowTo = "Stand at an iron gate and press its key, then stay still. At rank II, press it near a trap you can see to cut it from a step away.",
                CallumView = "He doesn't ask where you learned it.",
                Stats = new[] { Stat("Channel", (d, r) => N(d.A(r)) + " s"), Stat("Cuts traps from", (d, r) => r >= 2 ? N(d.B(r)) + " m" : "-"), Cooldown },
            },
            ["map_sketch"] = new SkillGuide
            {
                Id = "map_sketch", Tagline = "Charcoal, a scrap of vellum, and a good guess.",
                HowTo = "Press its key. His road ahead appears as a line for a while, and any hidden plates and caches near you are found.",
                CallumView = "He never notices you doing it.",
                Stats = new[] { Stat("Lasts", (d, r) => N(d.A(r)) + " s"), Stat("Finds within", (d, r) => N(d.B(r)) + " m"), Cooldown },
            },
            ["buckler"] = new SkillGuide
            {
                Id = "buckler", Tagline = "Small, round, and extremely rude to anyone who swings at you.",
                HowTo = "Face the blow with {aim} and press its key. A strike from the front glances off and the striker reels. At rank II, stand by him and it takes shots meant for him.",
                CallumView = "Defending yourself is your business. Defending him, he'll pretend not to notice.",
                Stats = new[] { Stat("Raised for", (d, r) => N(d.A(r)) + " s"), Stat("Covers him within", (d, r) => r >= 2 ? N(d.B(r)) + " m" : "-"), Cooldown },
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
