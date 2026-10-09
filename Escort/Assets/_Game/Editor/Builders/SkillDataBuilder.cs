using System.IO;
using HS.Core;
using HS.Skills;
using UnityEditor;
using UnityEngine;

namespace HS.EditorTools
{
    /// <summary>
    /// GDD §5 skill catalogue as data (40 + 8 capstones). The slice implements six (Pocket Sand, Loosen Bolt, Quiet Feet,
    /// Crossbow, Bandage, Cover Story — GDD §11.2), the campaign chapters twelve more and four capstones; the rest are
    /// listed for the System window and future tasks.
    /// Text describes uses, never recommendations (GDD §4.7).
    /// </summary>
    public static class SkillDataBuilder
    {
        const string Dir = "Assets/_Game/Data/Skills";

        struct Row
        {
            public string Id, Name, Uses, Icon;
            public SkillFamily F;
            public SkillType T;
            public bool Impl;
            public float[] Cd, A, B;
            public SabotageSeverity D;
        }

        static Row R(string id, string name, SkillFamily f, SkillType t, string uses, bool impl = false, float[] cd = null, float[] a = null, float[] b = null,
            SabotageSeverity d = SabotageSeverity.None, string icon = null)
            => new Row { Id = id, Name = name, F = f, T = t, Uses = uses, Impl = impl, Cd = cd ?? new[] { 0f, 0f }, A = a ?? new[] { 0f, 0f }, B = b ?? new[] { 0f, 0f }, D = d, Icon = icon ?? id };

        static readonly Row[] Rows =
        {
            // Fixer
            R("pocket_sand", "Pocket Sand", SkillFamily.Fixer, SkillType.Active, "Thrown at a spot up to 8 m away. Enemies caught in it are blinded for 3 s (rank 2: 4 s) and hidden things are revealed. Cooldown 8 s.", true, new[] { 8f, 8f }, new[] { 3f, 4f }, new[] { 8f, 8f }, SabotageSeverity.Major),
            R("loosen_bolt", "Loosen Bolt", SkillFamily.Fixer, SkillType.Active, "Kneel at a marked prop to arm it (2 armed at once; rank 2: 3). An armed prop collapses when you ping it or when the hero walks past it, heavily damaging everything in its area.", true, new[] { 1f, 1f }, new[] { 2f, 3f }, new[] { 1.5f, 1.2f }, SabotageSeverity.Major),
            R("quiet_feet", "Quiet Feet", SkillFamily.Fixer, SkillType.Passive, "While crouch-walking, unaware enemies can't detect you beyond 2 m (rank 2: 1.5 m), and the hero's witness cone narrows.", true, null, new[] { 2f, 1.5f }, new[] { 0.67f, 0.5f }),
            R("bait_and_switch", "Bait & Switch", SkillFamily.Fixer, SkillType.Active, "Plant a decoy in your shape up to 8 m away. Enemies within 8 m of it go for it for 4 s (rank 2: 6 s), all but a man in the hero's duel. Cooldown 16 s (rank 2: 13 s).", true, new[] { 16f, 13f }, new[] { 4f, 6f }, new[] { 8f, 8f }),
            R("grease", "Grease", SkillFamily.Fixer, SkillType.Active, "A slick patch that staggers enemies. The hero slips too (rank 2: hero-safe toggle)."),
            R("rope_trick", "Rope Trick", SkillFamily.Fixer, SkillType.Active, "A tether: trip or pull enemies, cross gaps, pull the hero out of pits."),
            R("lockpick", "Lockpick", SkillFamily.Fixer, SkillType.Active, "Kneel at an iron gate and open it: a 3 s channel (rank 2: 2 s). Rank 2 also cuts a trap you can see from 2.5 m away, at once. Cooldown 6 s (rank 2: 4 s).", true, new[] { 6f, 4f }, new[] { 3f, 2f }, new[] { 0f, 2.5f }),
            R("caltrops", "Caltrops", SkillFamily.Fixer, SkillType.Active, "Area denial that slows by 40%. Rank 2 adds bleed."),
            // Handler
            R("signal_codes", "Signal Codes", SkillFamily.Handler, SkillType.Passive, "Pings carry a second meaning (hold, advance, fall back) and reach further."),
            R("pep_talk", "Pep Talk", SkillFamily.Handler, SkillType.Active, "Within earshot (14 m), the hero hits 15% harder and moves 15% faster for 8 s (rank 2: 20% for 10 s). Cooldown 25 s (rank 2: 20 s).", true, new[] { 25f, 20f }, new[] { 0.15f, 0.2f }, new[] { 8f, 10f }),
            R("cover_story", "Cover Story", SkillFamily.Handler, SkillType.Active, "Talk the hero round: clears shame, doubt or tarnish. Used within 3 s of being caught cheating, it halves the fallout.", true, new[] { 20f, 16f }, new[] { 12f, 20f }, new[] { 3f, 4f }),
            R("pull_back", "Pull Back", SkillFamily.Handler, SkillType.Active, "Yank the hero up to 4 m toward you (rank 2: 5 m) from as far as 10 m (rank 2: 14 m): out of a snare, a bog or a bad spot. A trip is shaken off. Cooldown 18 s (rank 2: 14 s).", true, new[] { 18f, 14f }, new[] { 10f, 14f }, new[] { 4f, 5f }),
            R("hold_that_thought", "Hold That Thought", SkillFamily.Handler, SkillType.Active, "Delay the hero's next ability by 2 s (rank 2: 4 s)."),
            R("cue_card", "Cue Card", SkillFamily.Handler, SkillType.Active, "Queue up to 3 orders."),
            R("flattery", "Flattery", SkillFamily.Handler, SkillType.Active, "Draw the hero's attention to a target; a small morale bonus."),
            R("ready_call", "Ready Call", SkillFamily.Handler, SkillType.Active, "Declare 'ready': the bonus scales with your active prep (armed props, buffs)."),
            // Provisioner
            R("potion_belt", "Potion Belt", SkillFamily.Provisioner, SkillType.Passive, "Carry 3 potions (rank 2: 5) and craft them."),
            R("bandage", "Bandage", SkillFamily.Provisioner, SkillType.Active, "A 3 s channel next to the hero (or yourself): heals 40 over 6 s (rank 2: 60) and treats one minor wound. Cooldown 12 s (rank 2: 10 s).", true, new[] { 12f, 10f }, new[] { 40f, 60f }, new[] { 3f, 3f }),
            R("field_kitchen", "Field Kitchen", SkillFamily.Provisioner, SkillType.Active, "At camp: cook meals from foraged ingredients for run-long buffs."),
            R("repair_kit", "Repair Kit", SkillFamily.Provisioner, SkillType.Active, "Fix gear and bridges."),
            R("spare_sword", "Spare Sword", SkillFamily.Provisioner, SkillType.Active, "Hand the hero a slashing, piercing or blunt weapon for 20 s (soft bonuses, never immunity)."),
            R("smoke_bomb", "Smoke Bomb", SkillFamily.Provisioner, SkillType.Active, "Throw smoke up to 8 m away: a cloud 6 m across (rank 2: 7.2 m) for 6 s (rank 2: 8 s). Nobody sees through it, not the hero, not a chronicle stone, not a shooter, and anyone inside loses track of you. Cooldown 20 s (rank 2: 16 s).", true, new[] { 20f, 16f }, new[] { 6f, 8f }, new[] { 3f, 3.6f }),
            R("heavy_pack", "Heavy Pack", SkillFamily.Provisioner, SkillType.Passive, "+2 item capacity."),
            R("splint_and_stitch", "Splint & Stitch", SkillFamily.Provisioner, SkillType.Active, "A 6 s channel beside the hero (rank 2: 4.5 s) that treats one serious wound: cracked ribs, fever or a concussion. Supplies for 2 a chapter (rank 2: 3). Cooldown 30 s (rank 2: 24 s).", true, new[] { 30f, 24f }, new[] { 6f, 4.5f }, new[] { 2f, 3f }),
            // Scholar
            R("read_the_room", "Read the Room", SkillFamily.Scholar, SkillType.Active, "For 6 s (rank 2: 8 s) every foe within 20 m of you (rank 2: 26 m) shows what he means to do; hidden ones are marked where they lie; a scout's pendant shows. Cooldown 20 s (rank 2: 16 s).", true, new[] { 20f, 16f }, new[] { 6f, 8f }, new[] { 20f, 26f }),
            R("map_sketch", "Map Sketch", SkillFamily.Scholar, SkillType.Active, "For 10 s (rank 2: 14 s) the hero's path ahead shows as a line; hidden traps and caches within 30 m (rank 2: 40 m) are found. Cooldown 24 s (rank 2: 18 s).", true, new[] { 24f, 18f }, new[] { 10f, 14f }, new[] { 30f, 40f }),
            R("read_runes", "Read Runes", SkillFamily.Scholar, SkillType.Active, "Kneel at a rune seal within 3 m and read it open: a 2 s channel (rank 2: 1.2 s). Cooldown 6 s (rank 2: 4 s).", true, new[] { 6f, 4f }, new[] { 2f, 1.2f }, new[] { 3f, 3f }),
            R("bestiary", "Bestiary", SkillFamily.Scholar, SkillType.Passive, "After 3 encounters with a kind of enemy, its weak points are flagged (soft damage bonus)."),
            R("translate", "Translate", SkillFamily.Scholar, SkillType.Active, "Parley with creatures or people who can be parleyed with. Some heroes hate it."),
            R("forecast", "Forecast", SkillFamily.Scholar, SkillType.Passive, "Warns 1.5 s earlier of ambushes and hazards."),
            R("prepared_notes", "Prepared Notes", SkillFamily.Scholar, SkillType.Active, "Pre-solve one puzzle or seal per chapter (2 uses)."),
            R("forgery", "Forgery", SkillFamily.Scholar, SkillType.ActivePassive, "Detect forgeries (passive); forge passes or false stone footage (active)."),
            // Combat
            R("sling", "Sling", SkillFamily.Combat, SkillType.Active, "A stone out to 16 m (rank 2: 18 m): 9 damage (rank 2: 13), a small stagger, unlimited ammunition. Cooldown 1.4 s (rank 2: 1.1 s).", true, new[] { 1.4f, 1.1f }, new[] { 9f, 13f }, new[] { 16f, 18f }, SabotageSeverity.Minor),
            R("crossbow", "Crossbow", SkillFamily.Combat, SkillType.Active, "Aimed bolt: 38 damage out to 22 m, slow reload (3.5 s). Rank 2: 44 damage and the bolt pierces.", true, new[] { 3.5f, 3f }, new[] { 38f, 44f }, new[] { 22f, 24f }, SabotageSeverity.Minor),
            R("throwing_knives", "Throwing Knives", SkillFamily.Combat, SkillType.Active, "5 charges that cause bleeding."),
            R("dagger_flurry", "Dagger Flurry", SkillFamily.Combat, SkillType.Active, "Melee burst; can be cancelled into a dodge."),
            R("backstab", "Backstab", SkillFamily.Combat, SkillType.Passive, "+100% damage from behind against unaware enemies or ones engaged with the hero."),
            R("bomb_bag", "Bomb Bag", SkillFamily.Combat, SkillType.Active, "Area damage. Hits the hero too."),
            R("buckler", "Buckler", SkillFamily.Combat, SkillType.Active, "Raise a small shield for 1.2 s (rank 2: 1.5 s): a blow from the front glances off and the attacker reels for 1 s. Rank 2 also catches shots aimed at the hero while you stand within 2 m of him. Cooldown 6 s (rank 2: 5 s).", true, new[] { 6f, 5f }, new[] { 1.2f, 1.5f }, new[] { 0f, 2f }),
            R("shoulder_check", "Shoulder Check", SkillFamily.Combat, SkillType.Active, "Charge into the nearest foe in front of you: a light blow, 3 m of knockback (rank 2: 4 m) and 1.2 s of stagger (rank 2: 1.6 s). Cooldown 7 s (rank 2: 6 s).", true, new[] { 7f, 6f }, new[] { 3f, 4f }, new[] { 1.2f, 1.6f }, SabotageSeverity.Minor),
            // Capstones (revealed at the end of chapter 4)
            R("domino_effect", "Domino Effect", SkillFamily.Capstone, SkillType.Active, "Arm up to two idle props within 12 m of you, then bring down every armed prop within 40 m. Cooldown 45 s.", true, new[] { 45f, 45f }, new[] { 2f, 2f }, new[] { 40f, 40f }, SabotageSeverity.Major),
            R("second_wind", "Second Wind", SkillFamily.Capstone, SkillType.Passive, "Revive the hero once per chapter."),
            R("crossfire", "Crossfire", SkillFamily.Capstone, SkillType.Active, "Your volley: three bolts at the aim point, each 20 damage plus 10 for every rank of your fighting tricks (the knife counts one). While the hero gathers his Judgment, the volley lands with his blow instead. Cooldown 30 s.", true, new[] { 30f, 30f }, new[] { 20f, 20f }, new[] { 10f, 10f }, SabotageSeverity.Minor),
            R("killing_blow", "Killing Blow", SkillFamily.Capstone, SkillType.Active, "A huge personal execute."),
            R("hold_please", "Hold Please", SkillFamily.Capstone, SkillType.Active, "Everyone within 40 m but you is held still for 4 s, the hero too. Cooldown 60 s.", true, new[] { 60f, 60f }, new[] { 4f, 4f }, new[] { 40f, 40f }),
            R("silent_partner", "Silent Partner", SkillFamily.Capstone, SkillType.Passive, "Within 12 m of you the hero recovers 0.6% of his health every second, and every 45 s one minor wound of his is tended.", true, null, new[] { 0.006f, 0.006f }, new[] { 12f, 12f }),
            R("bond_strike", "Bond Strike", SkillFamily.Capstone, SkillType.Active, "A joint attack that scales with how far the hero has come with you."),
            R("forged_papers", "Forged Papers", SkillFamily.Capstone, SkillType.Active, "Bypass one social or seal obstacle per chapter."),
        };

        [MenuItem("Tools/HS/Build/Skill Data")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            Directory.CreateDirectory("Assets/_Game/Resources");
            var catalog = AssetDatabase.LoadAssetAtPath<SkillCatalog>("Assets/_Game/Resources/SkillCatalog.asset");
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<SkillCatalog>();
                AssetDatabase.CreateAsset(catalog, "Assets/_Game/Resources/SkillCatalog.asset");
            }
            catalog.skills.Clear();
            foreach (var r in Rows)
            {
                string path = $"{Dir}/{r.Id}.asset";
                var def = AssetDatabase.LoadAssetAtPath<SkillDefinition>(path);
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<SkillDefinition>();
                    AssetDatabase.CreateAsset(def, path);
                }
                def.id = r.Id;
                def.displayName = r.Name;
                def.family = r.F;
                def.type = r.T;
                def.uses = r.Uses;
                def.implemented = r.Impl;
                def.capstone = r.F == SkillFamily.Capstone;
                def.cooldown = r.Cd;
                def.valueA = r.A;
                def.valueB = r.B;
                def.dishonor = r.D;
                def.icon = r.Icon;
                EditorUtility.SetDirty(def);
                catalog.skills.Add(def);
            }
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            HS.Agent.AgentBridge.Write("builder.json", $"{{\"ok\":true,\"msg\":\"skills {Rows.Length}\"}}");
        }
    }
}
