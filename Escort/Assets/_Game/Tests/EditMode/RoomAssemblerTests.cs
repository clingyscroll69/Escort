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

        static List<ModuleInfo> Bastion() => new List<ModuleInfo>
        {
            new ModuleInfo { Id = "flooded_gate", Kind = RoomKind.Combat, Variants = 2, Chapter = 4 },
            new ModuleInfo { Id = "sluice_works", Kind = RoomKind.SetPiece, Variants = 2, Chapter = 4 },
            new ModuleInfo { Id = "hostage_court", Kind = RoomKind.Ambush, Variants = 2, Chapter = 4 },
            new ModuleInfo { Id = "baiters_causeway", Kind = RoomKind.Ambush, Variants = 2, Chapter = 4 },
            new ModuleInfo { Id = "wrens_rampart", Kind = RoomKind.Social, Variants = 2, Chapter = 4 },
        };

        static List<ModuleInfo> Gallery() => new List<ModuleInfo>
        {
            new ModuleInfo { Id = "hall_of_exhibits", Kind = RoomKind.Combat, Variants = 2, Chapter = 5 },
            new ModuleInfo { Id = "long_gallery", Kind = RoomKind.TrapCorridor, Variants = 2, Chapter = 5 },
        };

        /// <summary>The libraries by chapter.</summary>
        static List<ModuleInfo> LibraryFor(int moduleChapter) => moduleChapter == 4 ? Bastion() : moduleChapter == 5 ? Gallery() : moduleChapter == 2
            ? new List<ModuleInfo>
            {
                new ModuleInfo { Id = "snare_line", Kind = RoomKind.TrapCorridor, Variants = 2, Chapter = 2 },
                new ModuleInfo { Id = "fern_hollow", Kind = RoomKind.Ambush, Variants = 2, Chapter = 2 },
                new ModuleInfo { Id = "mire_crossing", Kind = RoomKind.SetPiece, Variants = 2, Chapter = 2 },
                new ModuleInfo { Id = "quills_glade", Kind = RoomKind.Social, Variants = 2, Chapter = 2 },
                new ModuleInfo { Id = "poacher_camp", Kind = RoomKind.Combat, Variants = 2, Chapter = 2 },
            }
            : moduleChapter == 3
                ? new List<ModuleInfo>
                {
                    new ModuleInfo { Id = "sealed_vault", Kind = RoomKind.Seal, Variants = 2, Chapter = 3 },
                    new ModuleInfo { Id = "dark_gallery", Kind = RoomKind.TrapCorridor, Variants = 2, Chapter = 3 },
                    new ModuleInfo { Id = "crypt_of_sleepers", Kind = RoomKind.Combat, Variants = 2, Chapter = 3 },
                    new ModuleInfo { Id = "prisoners_cell", Kind = RoomKind.Social, Variants = 2, Chapter = 3 },
                    new ModuleInfo { Id = "bone_bridge", Kind = RoomKind.SetPiece, Variants = 2, Chapter = 3 },
                }
                : Library();

        [Test]
        public void Whisperwood_Always_Has_Quills_Glade_And_Four_Distinct_Rooms()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var plan = RoomAssembler.Plan(seed, ChapterDef.For(2), LibraryFor(2));
                Assert.AreEqual(4, plan.Rooms.Count);
                Assert.AreEqual("quills_glade", plan.Rooms[1].ModuleId, "the merchant is always on the road");
                Assert.AreEqual(4, plan.Rooms.Select(r => r.ModuleId).Distinct().Count(), "no room twice: " + plan.Signature);
            }
        }

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
                var a = RoomAssembler.Plan(42, def, LibraryFor(def.ModuleChapter));
                var b = RoomAssembler.Plan(42, def, LibraryFor(def.ModuleChapter));
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
            var borrowed = ChapterDef.For(4).Borrowed();
            for (int s = 0; s < 10; s++)
                if (RoomAssembler.Plan(s, ChapterDef.For(1), Library()).Signature != RoomAssembler.Plan(s, borrowed, Library()).Signature) differ++;
            Assert.GreaterOrEqual(differ, 8, "the chapter number is mixed into the seed");
        }

        [Test]
        public void The_Bastion_Always_Has_Wren_Then_The_Sluice()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var plan = RoomAssembler.Plan(seed, ChapterDef.For(4), Bastion());
                Assert.AreEqual(4, plan.Rooms.Count);
                Assert.AreEqual("wrens_rampart", plan.Rooms[1].ModuleId, "the rival hero's rampart: " + plan.Signature);
                Assert.AreEqual("sluice_works", plan.Rooms[2].ModuleId, "the split threat: " + plan.Signature);
                Assert.AreEqual(4, plan.Rooms.Select(r => r.ModuleId).Distinct().Count(), plan.Signature);
            }
        }

        [Test]
        public void The_Gallery_Walks_The_Hall_Then_The_Long_Gallery()
        {
            var plan = RoomAssembler.Plan(7, ChapterDef.For(5), Gallery());
            CollectionAssert.AreEqual(new[] { "hall_of_exhibits", "long_gallery" }, plan.Rooms.Select(r => r.ModuleId).ToArray());
        }

        [Test]
        public void A_Chapter_Without_Its_Rooms_Borrows_The_Old_Road()
        {
            var def = ChapterDef.For(4);
            Assert.IsFalse(RoomAssembler.CanFill(def, Library()), "the Old Road has no social room or set piece");
            Assert.IsTrue(RoomAssembler.CanFill(def, Bastion()));
            var borrowed = def.Borrowed();
            Assert.AreEqual(1, borrowed.ModuleChapter);
            Assert.AreEqual(def.Slots.Count, borrowed.Slots.Count);
            Assert.AreEqual(def.Theme, borrowed.Theme, "under its own light");
            Assert.IsTrue(RoomAssembler.CanFill(borrowed, Library()));
            Assert.IsTrue(ChapterDef.For(5).Borrowed().Final, "the Gallery still ends at the door");
            Assert.AreEqual(4, RoomAssembler.Plan(3, borrowed, Library()).Rooms.Count);
        }
    }
}
