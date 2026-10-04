using System.Collections.Generic;
using System.Linq;

namespace HS.Skills
{
    /// <summary>
    /// How two skills interact (spec §5.3), shown in the picker against the skills you already own. Unordered pairs; a
    /// pair that doesn't interact simply isn't listed ("when applicable"). Notes describe interactions, never "pick this"
    /// (GDD §4.7). Draft copy for the owner to rewrite.
    /// </summary>
    public static class SkillSynergies
    {
        public readonly struct Pair
        {
            public readonly string A, B, Note;
            public Pair(string a, string b, string note)
            {
                A = a;
                B = b;
                Note = note;
            }
            public bool Has(string id) => A == id || B == id;
            public string Other(string id) => A == id ? B : A;
        }

        static readonly List<Pair> _all = new List<Pair>
        {
            new Pair("pocket_sand", "quiet_feet", "Crouched, you shrink his cone: a cloud thrown from the side is much harder for him to see."),
            new Pair("pocket_sand", "crossbow", "A blinded shooter can't fire back. A bolt into a blinded man is \"striking the helpless\" if he sees it."),
            new Pair("pocket_sand", "cover_story", "If he does see the sand, a cover story right away softens it and steadies his Honor."),
            new Pair("pocket_sand", "loosen_bolt", "Blinded bandits stand still. Under an armed prop, for instance."),
            new Pair("pocket_sand", "bandage", "Sand buys the quiet seconds a bandage needs."),
            new Pair("loosen_bolt", "quiet_feet", "Crouched, his cone narrows: a collapse at its edge may go unnoticed."),
            new Pair("loosen_bolt", "crossbow", "The collapse takes the crowd; the crossbow takes whoever walks out of the dust."),
            new Pair("loosen_bolt", "cover_story", "Seen dropping a wagonload of barrels on someone? \"Loose rocks, sir. Very old road.\""),
            new Pair("quiet_feet", "crossbow", "Shoot from a crouch: his cone is narrower, and bandits who haven't spotted you stay unaware."),
            new Pair("quiet_feet", "cover_story", "Two answers to the same problem: don't be seen, or talk your way out when you are."),
            new Pair("crossbow", "cover_story", "A bolt into his duel is only a slight. A cover story smooths it over."),
            new Pair("bandage", "cover_story", "Patch his body, mend his pride."),
            new Pair("splint_and_stitch", "bandage", "Two kits for two kinds of hurt: the bandage takes a sprain or a strain, the splint the broken things."),
            new Pair("splint_and_stitch", "pocket_sand", "A splint is a long kneel. Sand buys it."),
            new Pair("pull_back", "bandage", "Haul him clear of the fight, then dress him where nobody is swinging."),
            new Pair("pull_back", "loosen_bolt", "Pull him back from under the prop before it drops, and whoever followed him gets it instead."),
            new Pair("sling", "quiet_feet", "A stone from a crouch: his cone is narrower, and the bandits don't look your way."),
            new Pair("sling", "crossbow", "The sling for the flinch, the crossbow for the finish."),
            new Pair("sling", "cover_story", "A stone into his duel is only a slight. A cover story smooths it over."),
            new Pair("read_the_room", "pocket_sand", "Marked where he hides, a hidden man can be sanded out before the ambush."),
            new Pair("read_the_room", "crossbow", "Shows which shooter is drawing, and which is reloading."),
            new Pair("read_the_room", "quiet_feet", "Read them from the shadows: they never notice who's watching."),
            new Pair("map_sketch", "pocket_sand", "Two ways to find a hidden plate: sketch the road, or throw grit and watch where it settles."),
            new Pair("map_sketch", "lockpick", "Found by the map, cut by the pick (rank II) from a step away."),
            new Pair("read_runes", "lockpick", "Between them, no seal and no gate keeps you out."),
            new Pair("buckler", "bandage", "Shield up, then dress him while the man who swung at you reels."),
            new Pair("buckler", "crossbow", "Parry, and the reeling man is an easy shot. If he's helpless in Callum's sight, it's a disgrace."),
            new Pair("buckler", "pull_back", "Haul him clear, then stand between him and the shooters."),
            new Pair("bait_and_switch", "loosen_bolt", "Stand the decoy under a rigged prop: they crowd round it, and it all comes down."),
            new Pair("bait_and_switch", "pocket_sand", "They bunch up on the decoy. One handful of sand finds them all."),
            new Pair("bait_and_switch", "bandage", "While they argue with a stuffed cloak, there's time to dress him."),
            new Pair("smoke_bomb", "crossbow", "From inside the smoke, nobody sees where the bolt came from. The stones see nothing at all."),
            new Pair("smoke_bomb", "pocket_sand", "Grit for one man's eyes, smoke for everyone's. One of them is a dirty trick if he sees it."),
            new Pair("smoke_bomb", "quiet_feet", "Two ways not to be seen: crouch at the edge of his cone, or stand where nobody's cone reaches."),
            new Pair("smoke_bomb", "bandage", "Shooters can't aim through smoke: a cloud over him buys a dressing."),
            new Pair("pep_talk", "cover_story", "Words, both of them. One lifts his sword arm; the other his conscience."),
            new Pair("pep_talk", "bandage", "Patch him up, then talk him up."),
            new Pair("shoulder_check", "loosen_bolt", "A shove can put a man right under a rigged prop."),
            new Pair("shoulder_check", "buckler", "Block the swing, then barge the man who made it."),
            new Pair("shoulder_check", "sling", "A reeling man is an easy mark. If he's helpless in Callum's sight, it's a disgrace."),
        };

        public static IReadOnlyList<Pair> All => _all;

        /// <summary>The note for two skills in either order, or null when they don't interact.</summary>
        public static string Note(string a, string b)
        {
            foreach (var p in _all)
                if ((p.A == a && p.B == b) || (p.A == b && p.B == a)) return p.Note;
            return null;
        }

        /// <summary>Partners of <paramref name="id"/> that are in the kit, in table order.</summary>
        public static List<(string other, string note)> With(string id, IEnumerable<string> kit)
        {
            var owned = new HashSet<string>(kit ?? Enumerable.Empty<string>());
            return _all.Where(p => p.Has(id) && p.Other(id) != id && owned.Contains(p.Other(id))).Select(p => (p.Other(id), p.Note)).ToList();
        }

        /// <summary>Partners of <paramref name="id"/> that are not in the kit, in table order.</summary>
        public static List<(string other, string note)> Without(string id, IEnumerable<string> kit)
        {
            var owned = new HashSet<string>(kit ?? Enumerable.Empty<string>());
            return _all.Where(p => p.Has(id) && !owned.Contains(p.Other(id))).Select(p => (p.Other(id), p.Note)).ToList();
        }
    }
}
