using System.Linq;
using System.Text.RegularExpressions;
using HS.Rapport;
using NUnit.Framework;

namespace HS.Tests
{
    public class PostMortemTests
    {
        [Test]
        public void Names_What_Happened_In_Words_Never_Numbers()
        {
            var l = new RapportLedger();
            l.Capture(l.Offer("averted_cheat", 4f, note: "Room1_crossroads_shrine_ambusher_1 ambush"), "flushed out before the ambush");
            l.Close(l.Offer("unseen_assist", 3f, note: "GalleryArcher_0"), "the duel ended");
            l.Close(l.Offer("unseen_assist", 3f, note: "GalleryArcher_1"), "the duel ended");
            l.Penalize("caught", 3f, "pocket_sand");
            l.Close(l.Offer("wound_treated", 2f, note: "wounded after the duel"), "wound left untreated");
            var lines = PostMortem.From(l);
            var text = string.Join("\n", lines.Select(x => (x.Good ? "+ " : "- ") + x.Text));
            StringAssert.Contains("flushed out an ambush", text);
            StringAssert.Contains("A gallery archer fought dirty during his duel", text);
            StringAssert.Contains("He caught you fighting dirty", text);
            StringAssert.Contains("wounds you could have dressed", text);
            Assert.AreEqual(1, lines.Count(x => x.Text.Contains("gallery archer")), "repeats are folded into one line");
            Assert.IsFalse(Regex.IsMatch(text, @"\d"), "Rapport is never shown as a number:\n" + text);
            Assert.IsTrue(lines[0].Good, "lead with what went right");
        }
    }
}
