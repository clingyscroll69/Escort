using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HS.Tutorial
{
    /// <summary>Tip: a toast, never pauses. Focus: freeze-frame with a spotlight. Inline: text inside a screen. Notice: the System window.</summary>
    public enum LessonKind { Tip, Focus, Inline, Notice }

    public enum LessonCategory { Controls, TheHero, TheRoad, Camp }

    /// <summary>The HUD element a lesson points at (it pulses while the lesson shows).</summary>
    public enum CoachTarget { None, RuleChip, HonorBar, SkillBar, DodgePips, WoundChips, XpBar }

    public sealed class Lesson
    {
        public string Id, Title, Body, Icon;
        public LessonKind Kind;
        public LessonCategory Category;
        public CoachTarget Coach;
        /// <summary>Seconds a tip stays up when nothing completes it first.</summary>
        public float Duration = 9f;
        /// <summary>Seconds a queued tip may wait for its turn before its moment has passed (it can fire again later).</summary>
        public float Expiry = 6f;
        public int Priority;
    }

    /// <summary>
    /// Every lesson the tutorial can teach (spec: docs/superpowers/specs/2026-10-03-tutorial-and-hud-design.md §4).
    /// Mechanics are explained fully; strategy only as hints; the hidden stat is never named (see <see cref="Forbidden"/>).
    /// Draft copy for the owner to rewrite. {tokens} are keys (see <see cref="KeyGlyphs"/>); {coverHint} and {slots} are
    /// filled in by the director.
    /// </summary>
    public static class Lessons
    {
        static Lesson L(string id, LessonKind kind, LessonCategory cat, string icon, string title, string body,
            CoachTarget coach = CoachTarget.None, int priority = 20, float duration = 9f, float expiry = 6f)
            => new Lesson { Id = id, Kind = kind, Category = cat, Icon = icon, Title = title, Body = body, Coach = coach, Priority = priority, Duration = duration, Expiry = expiry };

        const LessonKind Tip = LessonKind.Tip, Focus = LessonKind.Focus, Inline = LessonKind.Inline, Notice = LessonKind.Notice;
        const LessonCategory Controls = LessonCategory.Controls, Hero = LessonCategory.TheHero, Road = LessonCategory.TheRoad, Camp = LessonCategory.Camp;

        static readonly List<Lesson> _all = new List<Lesson>
        {
            L("welcome", Notice, Controls, null, "Tutorial",
                "TUTORIAL: no file found for class HERO's SIDEKICK.\nGenerating one... <size=80%>(unofficial)</size>"),

            // ---- controls ------------------------------------------------------------------------------------------
            L("move", Tip, Controls, "route", "Moving",
                "{move} to move. Hold {walk} to walk carefully.", priority: 45, duration: 12f, expiry: 20f),
            L("attack", Tip, Controls, "verb_knife", "Your knife",
                "{attack} stabs with the kitchen knife. It isn't much. Your tricks are the real weapons.", priority: 30, expiry: 8f),
            L("tricks", Tip, Controls, "fam_fixer", "Your tricks",
                "{skills} use your tricks. Aim with {aim}; each slot shows its cooldown.", CoachTarget.SkillBar, 28, 10f, 10f),
            L("dodge", Tip, Controls, "verb_dodge", "Dodge",
                "{dodge} rolls. Three charges (the pips under your health), and you're untouchable for a split second.", CoachTarget.DodgePips, 35),
            L("insight", Tip, Controls, "eye", "Hero Insight",
                "{insight} toggles Hero Insight: his current rule, spelled out. Leave it off to keep the mystery.", CoachTarget.RuleChip, 20, 10f),
            L("crouch", Tip, Controls, "verb_crouch", "Quiet feet",
                "{crouch} crouches. With Quiet Feet, unaware enemies can't hear you beyond 2 m, and his cone narrows.", priority: 20, expiry: 12f),
            L("ping", Tip, Controls, "verb_ping", "Ping",
                "Armed. {ping} on it brings it down. Or let him lead a bandit underneath.", priority: 30),
            L("cache", Tip, Controls, "verb_interact", "Off the road",
                "Something's tucked away here. {interact} to search it: exploring pays a share of every room's XP.", priority: 25),
            L("channel", Tip, Controls, "wait", "Hold still",
                "Channelled tricks need you still. Moving or taking a hit breaks them.", priority: 20, duration: 7f),
            L("pause", Tip, Controls, "fam_scholar", "Field Guide",
                "{pause} pauses. Everything you've learned is in the Field Guide.", priority: 5, duration: 8f, expiry: 30f),

            // ---- the hero ------------------------------------------------------------------------------------------
            L("hero_rules", Focus, Hero, "threshold", "He runs on rules",
                "Sir Callum follows a code, one rule at a time. The icon over his head is the rule he's on right now. Learn them and you'll know what he'll do next.\n\n" +
                "He stops at every threshold before going in. That pause is yours: look ahead, get ready. You can only help him from close by.",
                CoachTarget.RuleChip, 100),
            L("cone", Focus, Hero, "eye", "What he sees",
                "The pale wedge in front of him is what he sees. He's a knight, and he judges what he sees.\n\n" +
                "Dirty tricks in his sight (sand in a man's eyes, a bolt in someone else's duel, striking the helpless) cost him HONOR, the gold bar. Low Honor makes his sword arm heavy.\n\n" +
                "What he doesn't see, he can't judge.",
                CoachTarget.HonorBar, 100),
            L("salute", Tip, Hero, "challenge", "The salute",
                "He salutes before every duel. Touch his opponent before it's over and you've spoiled it.", priority: 40, duration: 7f),
            L("unready", Tip, Hero, "wait", "A fair fight",
                "He won't strike a foe who can't fight back: blinded, staggered or turned away. He lowers his sword and waits (the hourglass).", priority: 45),
            L("surrender", Tip, Hero, "question", "A yield",
                "He spares anyone who yields; it's in the Code. Whether they meant it is another matter.", priority: 55),
            L("caught", Tip, Hero, "scold", "He saw that",
                "Honor falls, and for a while he watches you more closely (his cone widens). {coverHint}", CoachTarget.HonorBar, 60),
            L("honor_low", Tip, Hero, "honor", "Honor is low",
                "His blows land softer until his Honor recovers. Clearing a room steadies him.", CoachTarget.HonorBar, 40),
            L("spoiled", Tip, Hero, "challenge", "The salute",
                "He takes the salute seriously. Next time, let him finish it.", priority: 40, duration: 7f),
            L("fallback", Tip, Hero, "fallback", "To the narrows",
                "Three or more on him and he falls back to the narrows, making them come one at a time (the shield).", priority: 35),
            L("wounds", Tip, Hero, "wound_ribs", "Wounds",
                "A hard hit wounded him (listed under his health). Wounds stay until treated: Bandage handles minor ones, and the camp tends one.",
                CoachTarget.WoundChips, 50, 10f),
            L("duel", Focus, Hero, "challenge", "A formal duel",
                "Callum swore to the terms: no aid. He keeps his word, and he'll hold you to it.\n\n" +
                "His word binds him. It doesn't bind anyone else in this room.", priority: 100),

            // ---- the road ------------------------------------------------------------------------------------------
            L("ambush", Tip, Road, "alert", "Ambush",
                "Hedges and ruins hide people. Hidden foes can be flushed out early, by someone who gets there before he does.", priority: 45),
            L("stone", Tip, Road, "eye", "A chronicle stone",
                "While it glows, anything in its blue view is on record: a dirty trick it sees counts as witnessed. Three knife cuts or one bolt break it.",
                priority: 25, duration: 11f),
            L("trap", Tip, Road, "wound_ankle", "Traps",
                "He walks his road straight over traps. You can see them: stand on one and {interact} to disarm it. Walking or crouching, you step over them.",
                priority: 25, duration: 11f),
            L("prop", Tip, Road, "skill_loosen_bolt", "Loose bolts",
                "This could come down on someone. Kneel beside it and use Loosen Bolt ({loosen}) to arm it.", priority: 25),
            L("threats", Tip, Road, "alert", "Incoming",
                "A red chevron on the edge: someone off screen is drawing on one of you.", priority: 50, duration: 7f),
            L("hero_offscreen", Tip, Road, "route", "Where he went",
                "The gold marker is Callum, off screen. He won't wait for you.", priority: 30, duration: 7f),
            L("out_of_reach", Tip, Road, "route", "Too far",
                "Too far. From here, nothing you do helps him.", priority: 45, duration: 7f),
            // ---- Whisperwood and after (chapter 2 on)
            L("hunger", Tip, Hero, "ration", "Hungry",
                "A long road on an empty belly. STARVING, his blows land softer and he stops catching his breath between rooms. Rations turn up in forage caches; out of a fight, stand beside him and {interact} to feed him one.",
                CoachTarget.WoundChips, 55, 11f),
            L("snare", Tip, Road, "snare", "A snare",
                "A poacher's snare. Whoever steps in is yanked off their feet, and on him that means a sprained ankle. Walk or crouch over it, or stand on it and {interact} to cut it.",
                priority: 30, duration: 10f),
            L("sleeper", Tip, Road, "wait", "Asleep at his post",
                "A sleeping man can't fight back. Callum won't strike him: he calls him out first, and that wakes him. A sleeper Callum doesn't see is between you and the sleeper.",
                priority: 40, duration: 10f),
            L("scout", Tip, Road, "pendant", "A dull pendant",
                "That pendant is the grey of a dead chronicle stone. Some people carry things back to whoever sent them. Pinging someone as soon as you first see them, or reading the room, can catch them out.",
                priority: 45, duration: 11f),
            L("mud", Tip, Road, "wound_ankle", "Mud",
                "Everyone wades through the mire, and the man in armour wades worst.", priority: 20, duration: 7f),
            L("downed", Tip, Hero, "recall", "Down, not dead",
                "He learned to come back for you. How fast, and at what cost to him, is his to decide. If nobody comes within 20 s, you come round at the last door.",
                priority: 80, duration: 10f),

            // ---- the Catacombs (chapter 3 on)
            L("seal", Tip, Road, "skill_read_runes", "A seal",
                "The way on is sealed. Runes can be read open, gates picked, and wardens tend to carry keys. Left alone, he'll put his shoulder to it, and the ward will answer.",
                priority: 50, duration: 11f),
            L("hidden_plate", Tip, Road, "wound_ankle", "In the dark",
                "Plates are hidden in this floor. You find them up close; he never does. Sand settles on their edges, and a map shows them all.",
                priority: 40, duration: 10f),
            L("finisher", Tip, Hero, "judgment", "Judgment",
                "When his opponent weakens, he gathers himself for one great blow. A hard hit while he gathers breaks it.",
                CoachTarget.WoundChips, 45, 9f),
            L("look_away", Tip, Hero, "look_away", "Not looking",
                "He turned his back. For three seconds, he isn't watching anything you do.", priority: 60, duration: 8f),
            L("pit", Tip, Road, "alert", "The drop",
                "Whoever goes over the edge falls hard and climbs back slowly. Someone teetering can still be hauled clear.", priority: 40, duration: 9f),

            // ---- the Sunken Bastion (chapter 4 on)
            L("wading", Tip, Road, "wound_ankle", "Deep water",
                "Deep water slows everyone, and a man in plate slowest of all. A smaller target than him, you can wade where he can't.",
                priority: 20, duration: 8f),
            L("sluice", Tip, Road, "sluice", "The sluice",
                "Men at that wheel are flooding the floor below, and they are out of his reach up there. Stop them, or jam the wheel ({interact}).",
                priority: 50, duration: 11f),
            L("hostage", Tip, Road, "hostage", "A captive",
                "He will not strike through an innocent, and the man behind her knows it. Cut her ropes ({interact}) and he's fair game again.",
                priority: 55, duration: 11f),
            L("baiter", Tip, Hero, "challenge", "Backing away",
                "He took the challenge, then gave ground. Whatever he's leading Callum towards, Callum will follow.", priority: 45, duration: 9f),
            L("capstone", Tip, Controls, "link", "Your last trick",
                "Your last trick has a key of its own ({capstone}). It never takes a slot, and a long rest comes after it.", priority: 35, duration: 10f),

            // ---- the Gallery (chapter 5)
            L("mirror", Focus, Hero, "mirror", "The Mirror",
                "It is Callum as he was: every old habit, the same strength. It has no entry for you and never once looks your way.\n\n" +
                "Whatever he has outgrown, it still does. That's where it breaks.", priority: 100),
            L("habit_break", Tip, Hero, "mirror", "An old habit",
                "It reeled, and it takes double while it does. Its habits are the openings: courtesy, the narrows, the Code.", priority: 70, duration: 9f),
            L("link_ring", Tip, Hero, "link", "He's looking at you",
                "Fire your last trick ({capstone}) before the ring closes. With nothing ready on that key, ping it.", priority: 110, duration: 4f, expiry: 2f),
            L("duet_window", Tip, Hero, "duet", "A hand",
                "He asked for a hand on this one. Every blow you land on his opponent while he gathers himself goes into his.", priority: 70, duration: 8f),

            L("xp", Tip, Road, "check", "XP",
                "Every room pays the same XP: for the clear, for helping, for exploring. Levels arrive at the camp.", CoachTarget.XpBar, 15, 8f, 10f),

            // ---- levels and the camp -------------------------------------------------------------------------------
            L("levelup", Inline, Camp, "fam_scholar", "Picking tricks",
                "Pick any trick, any time; picking one you know raises it to rank II. Click a trick to read it and watch the simulation, click again to learn it."),
            L("camp", Tip, Camp, "honor", "The camp",
                "Rest tended his worst wound and patched you both up. Your level-up comes when he's done talking. Watch him by the fire: what he does here says what he makes of you.",
                priority: 50, duration: 12f, expiry: 4f),
            L("loadout", Inline, Camp, "fam_fixer", "Loadout",
                "Loadout: {slots} slots for active tricks. Click one below to bench it or bring it back. Passives are always on."),
            L("restore", Inline, Camp, "check", "Restore Points",
                "Restore Points: start again from any chapter start you have reached, or from just before the Gallery door. Your tricks come with you."),
        };

        static readonly Dictionary<string, Lesson> _byId = _all.ToDictionary(l => l.Id);

        public static IReadOnlyList<Lesson> All => _all;

        public static Lesson Get(string id) => id != null && _byId.TryGetValue(id, out var l) ? l : null;

        public static string CategoryName(LessonCategory c) => c switch
        {
            LessonCategory.Controls => "CONTROLS",
            LessonCategory.TheHero => "THE HERO",
            LessonCategory.TheRoad => "THE ROAD",
            _ => "LEVELS & THE CAMP",
        };

        /// <summary>Words that would give away the hidden stat (GDD §4.4: it is never shown or named).</summary>
        public static readonly string[] Forbidden =
        {
            "rapport", "stage", "stages", "s0", "s1", "s2", "s3", "moment", "moments", "capture", "captured", "points", "trust",
            "score", "affinity", "approval", "meter", "penalty", "penalties",
        };

        static readonly Regex Words = new Regex(@"[A-Za-z0-9]+");
        /// <summary>The game's own feature names that happen to contain a forbidden word.</summary>
        static readonly Regex Allowed = new Regex(@"Restore Points?", RegexOptions.IgnoreCase);

        public static IEnumerable<string> CopyViolations(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            var plain = Allowed.Replace(Regex.Replace(text, "<[^>]*>", " "), " ");
            foreach (Match m in Words.Matches(plain))
            {
                var w = m.Value.ToLowerInvariant();
                if (System.Array.IndexOf(Forbidden, w) >= 0) yield return w;
            }
        }
    }
}
