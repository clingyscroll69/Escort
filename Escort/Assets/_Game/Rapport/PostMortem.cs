using System.Collections.Generic;

namespace HS.Rapport
{
    /// <summary>
    /// The Post-Mortem (GDD §11.3 "attributable losses"): captured and missed Moments and the negative assets, in plain
    /// words — never numbers (Rapport stays hidden). Built from the ledger's log.
    /// </summary>
    public static class PostMortem
    {
        public struct Line
        {
            public bool Good;
            public string Text;
        }

        public static List<Line> From(RapportLedger l, int max = 8)
        {
            var good = new List<string>();
            var bad = new List<string>();
            foreach (var e in l.Log)
            {
                string t = Phrase(e, out bool isGood);
                if (t == null) continue;
                var list = isGood ? good : bad;
                if (!list.Contains(t)) list.Add(t);
            }
            var lines = new List<Line>();
            // Lead with what went right, then what he faced alone — but always show some of each if present.
            int g = System.Math.Min(good.Count, System.Math.Max(max / 2, max - bad.Count));
            for (int i = 0; i < g; i++) lines.Add(new Line { Good = true, Text = good[i] });
            for (int i = 0; i < bad.Count && lines.Count < max; i++) lines.Add(new Line { Good = false, Text = bad[i] });
            return lines;
        }

        /// <summary>Who a Moment was about, from the enemy's object name (most specific first).</summary>
        static readonly (string key, string who)[] Names =
        {
            ("bastion_archer", "a bastion archer"), ("alcove_archer", "an alcove archer"), ("gallery_archer", "a gallery archer"),
            ("marksman", "a marksman"), ("poacher", "a poacher"), ("crossbowman", "a crossbowman"), ("baiter", "a challenge-baiter"),
            ("drowned_ambusher", "a drowned ambusher"), ("fern_ambusher", "a fern ambusher"), ("steward", "a false steward"),
            ("tomb_robber", "a tomb robber"), ("cultist", "a cultist"), ("turncoat", "a turncoat"), ("ambusher", "a hedge ambusher"),
            ("archer", "a gallery archer"), ("Archer", "a gallery archer"),
        };

        public static string Who(string note)
        {
            if (string.IsNullOrEmpty(note)) return "someone";
            foreach (var (key, who) in Names)
                if (note.Contains(key)) return who;
            return "a cheat";
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        public static string Phrase(RapportLedger.LogEntry e, out bool good)
        {
            good = false;
            string note = e.Note ?? "";
            switch (e.Kind)
            {
                case "capture":
                    good = true;
                    switch (e.Id)
                    {
                        case "unseen_assist": return $"While he duelled, you quietly dealt with {Who(note)} — and he never saw it.";
                        case "averted_cheat":
                            if (note.Contains("hostage")) return "You got a hostage out from in front of an archer's bow.";
                            if (note.Contains("pulled")) return "You pulled him clear of a false surrender before the knife came out.";
                            return note.Contains("ambush") || note.Contains("flushed") ? "You flushed out an ambush before it sprang." : "You saw through a false surrender before the knife came out.";
                        case "covered_lapse": return "Your cover story smoothed over a lapse.";
                        case "wound_treated": return "You bandaged him after a duel.";
                    }
                    return null;
                case "close":
                    if (note.EndsWith("already credited")) return null;
                    switch (e.Id)
                    {
                        case "unseen_assist": return $"{Cap(Who(note))} fought dirty during his duel; nobody stopped them.";
                        case "averted_cheat":
                            if (note.Contains("hostage")) return "An archer kept his hostage, and his aim.";
                            return note.Contains("ambush") ? "An ambush caught him by surprise." : "A false surrender went unchallenged.";
                        case "covered_lapse": return "He caught you cheating, and you let it stand.";
                        case "wound_treated": return "He fought on with wounds you could have dressed.";
                    }
                    return null;
                case "penalty":
                    switch (e.Id)
                    {
                        case "caught": return note.StartsWith("stone") ? "A chronicle stone recorded your dirty trick." : "He caught you fighting dirty.";
                        case "spoiled_duel": return "You struck before his salute was done.";
                        case "friendly_fire": return "You hurt him.";
                        case "abandon": return "You were far away when he needed you.";
                    }
                    return null;
            }
            return null;
        }
    }
}
