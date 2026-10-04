using System.Collections.Generic;

namespace HS.Curator
{
    /// <summary>
    /// What the sidekick learns of the Curator's file on her hero (GDD §4.3, §8): a fragment from each unmasked scout, and
    /// the chapter 3 scrap. Registered in the RunContext; snapshotted with the Restore Points. Draft copy for the owner.
    /// </summary>
    public sealed class Dossier
    {
        public readonly List<string> Fragments = new List<string>();

        public static readonly Dictionary<string, string> ScoutFragments = new Dictionary<string, string>
        {
            ["quill"] = "\"...salutes before he strikes. Every time. A merchant could set his watch by it.\"",
            ["prisoner"] = "\"...will free any man in chains, and never asks what the chains were for.\"",
            ["wren"] = "\"...accepts help only from those who ask nothing. Offer him nothing, and he is yours.\"",
        };

        public bool Add(string fragment)
        {
            if (string.IsNullOrEmpty(fragment) || Fragments.Contains(fragment)) return false;
            Fragments.Add(fragment);
            return true;
        }

        public List<string> Snapshot() => new List<string>(Fragments);

        public void Restore(List<string> s)
        {
            Fragments.Clear();
            if (s != null) Fragments.AddRange(s);
        }
    }
}
