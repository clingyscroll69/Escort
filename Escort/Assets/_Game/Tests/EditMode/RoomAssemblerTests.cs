using System.Collections.Generic;
using System.Linq;
using HS.Rooms;
using NUnit.Framework;

namespace HS.Tests
{
    public class RoomAssemblerTests
    {
        static List<ModuleInfo> Library() => new List<ModuleInfo>
        {
            new ModuleInfo { Id = "crossroads_shrine", Kind = RoomKind.Ambush, Variants = 2 },
            new ModuleInfo { Id = "toll_gate", Kind = RoomKind.Combat, Variants = 2 },
            new ModuleInfo { Id = "ruined_gatehouse", Kind = RoomKind.TrapCorridor, Variants = 2 },
            new ModuleInfo { Id = "wagon_camp", Kind = RoomKind.Combat, Variants = 2 },
        };

        [Test]
        public void SameSeed_SamePlan()
        {
            var a = RoomAssembler.Plan(1234, ChapterDef.OldRoad(), Library());
            var b = RoomAssembler.Plan(1234, ChapterDef.OldRoad(), Library());
            Assert.AreEqual(a.Signature, b.Signature);
        }

        [Test]
        public void LibraryOrder_DoesNotMatter()
        {
            var lib = Library();
            var shuffled = new List<ModuleInfo> { lib[3], lib[1], lib[0], lib[2] };
            Assert.AreEqual(RoomAssembler.Plan(77, ChapterDef.OldRoad(), lib).Signature, RoomAssembler.Plan(77, ChapterDef.OldRoad(), shuffled).Signature);
        }

        [Test]
        public void DifferentSeeds_ProduceVariety()
        {
            var sigs = new HashSet<string>();
            for (int s = 0; s < 10; s++) sigs.Add(RoomAssembler.Plan(s, ChapterDef.OldRoad(), Library()).Signature);
            Assert.GreaterOrEqual(sigs.Count, 4, "10 seeds should give several distinct layouts: " + string.Join(" / ", sigs));
        }

        [Test]
        public void Slots_Respect_Kinds_And_NoRepeats()
        {
            var def = ChapterDef.OldRoad();
            for (int s = 0; s < 50; s++)
            {
                var plan = RoomAssembler.Plan(s, def, Library());
                Assert.AreEqual(3, plan.Rooms.Count);
                Assert.AreEqual(3, plan.Rooms.Select(r => r.ModuleId).Distinct().Count(), "no module twice in a chapter");
                for (int i = 0; i < 3; i++) CollectionAssert.Contains(def.Slots[i].Allowed, plan.Rooms[i].Kind);
                Assert.IsTrue(plan.Rooms.All(r => r.Variant >= 0 && r.Variant < 2));
            }
        }

        [Test]
        public void Every_Module_Appears_Across_Seeds()
        {
            var seen = new HashSet<string>();
            for (int s = 0; s < 200; s++) foreach (var r in RoomAssembler.Plan(s, ChapterDef.OldRoad(), Library()).Rooms) seen.Add(r.ModuleId);
            Assert.AreEqual(4, seen.Count);
        }

        [Test]
        public void Every_Chapter_Plans_Deterministically_From_Its_Library()
        {
            for (int ch = 1; ch <= 5; ch++)
            {
                var def = ChapterDef.For(ch);
                Assert.AreEqual(ch, def.Chapter);
                var a = RoomAssembler.Plan(42, def, Library());
                var b = RoomAssembler.Plan(42, def, Library());
                Assert.AreEqual(def.Slots.Count, a.Rooms.Count, def.Name);
                Assert.AreEqual(a.Signature, b.Signature, def.Name);
            }
            Assert.AreEqual(new[] { 3, 4, 4, 4, 2 }, new[] { 1, 2, 3, 4, 5 }.Select(c => ChapterDef.For(c).Slots.Count).ToArray());
            Assert.IsTrue(ChapterDef.For(5).Final);
            Assert.IsFalse(ChapterDef.For(4).Final);
        }

        [Test]
        public void Chapters_With_The_Same_Seed_Differ()
        {
            int differ = 0;
            for (int s = 0; s < 10; s++)
                if (RoomAssembler.Plan(s, ChapterDef.For(1), Library()).Signature != RoomAssembler.Plan(s, ChapterDef.For(2), Library()).Signature) differ++;
            Assert.GreaterOrEqual(differ, 8, "the chapter number is mixed into the seed");
        }
    }
}
