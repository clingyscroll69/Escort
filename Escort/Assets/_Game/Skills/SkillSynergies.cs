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
