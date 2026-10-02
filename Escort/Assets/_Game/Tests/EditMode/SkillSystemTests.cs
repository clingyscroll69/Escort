using System.Collections.Generic;
using HS.Core;
using HS.Skills;
using NUnit.Framework;
using UnityEngine;

namespace HS.Tests
{
    public class SkillSystemTests
    {
        sealed class CountingBehaviour : ISkillBehaviour
        {
            public int Uses;
            public bool Allow = true;
            public bool CanUse(in SkillUseContext c, SkillState s, out string reason) { reason = Allow ? null : "blocked"; return Allow; }
            public bool Execute(in SkillUseContext c, SkillState s) { Uses++; return true; }
        }

        static SkillDefinition Def(string id, SkillType t = SkillType.Active, float cd1 = 8f, float cd2 = 5f)
        {
            var d = ScriptableObject.CreateInstance<SkillDefinition>();
            d.id = id;
            d.type = t;
            d.cooldown = new[] { cd1, cd2 };
            d.implemented = true;
            return d;
        }

        [Test]
        public void Four_Slots_At_Start_Fifth_Active_Waits_In_Reserve()
        {
            var sys = new SkillSystem();
            sys.SetChapter(1);
            for (int i = 0; i < 5; i++) Assert.IsTrue(sys.Learn(Def("a" + i)));
            Assert.AreEqual(4, sys.Loadout.Count);
            CollectionAssert.DoesNotContain(sys.Loadout, "a4");
            Assert.AreEqual(1, new List<SkillState>(sys.Reserve).Count);
        }

        [Test]
        public void Passives_Do_Not_Use_Slots()
        {
            var sys = new SkillSystem();
            sys.Learn(Def("quiet_feet", SkillType.Passive));
            Assert.AreEqual(0, sys.Loadout.Count);
            Assert.IsTrue(sys.Has("quiet_feet"));
        }

        [Test]
        public void Second_Pick_Is_Rank2_Third_Refused()
        {
            var sys = new SkillSystem();
            var d = Def("pocket_sand");
            Assert.IsTrue(sys.Learn(d));
            Assert.IsTrue(sys.Learn(d));
            Assert.AreEqual(2, sys.RankOf("pocket_sand"));
            Assert.IsFalse(sys.Learn(d), "max rank 2");
            Assert.AreEqual(1, sys.Loadout.Count, "rank-up does not take a second slot");
        }

        [Test]
        public void Capstones_Are_Not_In_The_Soup()
        {
            var sys = new SkillSystem();
            var cap = Def("second_wind");
            cap.capstone = true;
            Assert.IsFalse(sys.Learn(cap));
            Assert.IsTrue(sys.LearnCapstone(cap));
            var cap2 = Def("hold_please");
            cap2.capstone = true;
            Assert.IsFalse(sys.LearnCapstone(cap2), "choose one");
        }

        [Test]
        public void Cooldown_Gates_Activation_And_Uses_Rank_Values()
        {
            var b = new CountingBehaviour();
            var sys = new SkillSystem(new Dictionary<string, ISkillBehaviour> { { "pocket_sand", b } });
            var d = Def("pocket_sand", SkillType.Active, 8f, 5f);
            sys.Learn(d);
            var ctx = new SkillUseContext();
            Assert.IsTrue(sys.TryActivate(0, ctx));
            Assert.IsFalse(sys.TryActivate(0, ctx));
            Assert.AreEqual("cooldown", sys.LastFailReason);
            sys.Tick(7.9f);
            Assert.IsFalse(sys.TryActivate(0, ctx));
            sys.Tick(0.2f);
            Assert.IsTrue(sys.TryActivate(0, ctx));
            sys.Learn(d); // rank 2
            sys.Tick(8f);
            Assert.IsTrue(sys.TryActivate(0, ctx));
            Assert.AreEqual(5f, sys.Get("pocket_sand").CooldownRemaining, 1e-4f, "rank-2 cooldown");
            Assert.AreEqual(3, b.Uses);
        }

        [Test]
        public void CanUse_Failure_Does_Not_Start_Cooldown()
        {
            var b = new CountingBehaviour { Allow = false };
            var sys = new SkillSystem(new Dictionary<string, ISkillBehaviour> { { "x", b } });
            sys.Learn(Def("x"));
            Assert.IsFalse(sys.TryActivate(0, new SkillUseContext()));
            Assert.AreEqual("blocked", sys.LastFailReason);
            Assert.IsTrue(sys.Get("x").Ready);
        }

        [Test]
        public void Loadout_Swap_Only_At_Camp()
        {
            var sys = new SkillSystem();
            for (int i = 0; i < 5; i++) sys.Learn(Def("s" + i));
            Assert.IsFalse(sys.Equip("s4", 0), "no swaps on the road");
            sys.AtCamp = true;
            Assert.IsTrue(sys.Equip("s4", 0));
            Assert.AreEqual("s4", sys.Loadout[0]);
            CollectionAssert.DoesNotContain(sys.Loadout, "s0");
        }

        [Test]
        public void Slots_Grow_By_Chapter()
        {
            Assert.AreEqual(4, SkillSystem.SlotsForChapter(1));
            Assert.AreEqual(5, SkillSystem.SlotsForChapter(2));
            Assert.AreEqual(6, SkillSystem.SlotsForChapter(3));
            Assert.AreEqual(6, SkillSystem.SlotsForChapter(5));
        }

        [Test]
        public void Snapshot_Restore_RoundTrips()
        {
            var sys = new SkillSystem();
            var a = Def("a");
            var b2 = Def("b");
            sys.Learn(a);
            sys.Learn(a);
            sys.Learn(b2);
            var snap = sys.Snapshot();
            var lo = sys.LoadoutSnapshot();
            var other = new SkillSystem();
            other.Restore(snap, lo, id => id == "a" ? a : b2);
            Assert.AreEqual(2, other.RankOf("a"));
            CollectionAssert.AreEqual(lo, other.Loadout);
        }
    }
}
