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
    }
}
