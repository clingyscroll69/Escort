using System.Collections.Generic;
using HS.Core;
using HS.Hero;
using NUnit.Framework;
using UnityEngine;

namespace HS.Tests
{
    public class HeroBrainTests
    {
        sealed class FakeRule : HeroRule
        {
            readonly string _id;
            public bool Can;
            public int Enters, Exits, Ticks;
            public FakeRule(string id, bool can) { _id = id; Can = can; }
            public override string Id => _id;
            public override string Icon => _id + "_icon";
            public override bool CanRun(HeroContext c) => Can;
            public override void Enter(HeroContext c) => Enters++;
            public override void Exit(HeroContext c) => Exits++;
            public override void Tick(HeroContext c, float dt) => Ticks++;
        }

        static HeroContext Ctx() => new HeroContext();

        [Test]
        public void HighestPriority_SatisfiableRule_Wins()
        {
            var a = new FakeRule("fight", false);
            var b = new FakeRule("challenge", true);
            var c = new FakeRule("route", true);
            var brain = new HeroBrain();
            var ctx = Ctx();
            brain.SetRules(new IHeroRule[] { a, b, c }, ctx);
            brain.Tick(ctx, 0.1f);
            Assert.AreEqual("challenge", brain.ActiveId);
            Assert.AreEqual("challenge_icon", brain.ActiveIcon);
            a.Can = true;
            brain.Tick(ctx, 0.1f);
            Assert.AreEqual("fight", brain.ActiveId);
            Assert.AreEqual(1, b.Exits);
            Assert.AreEqual(1, a.Enters);
        }

        [Test]
        public void ReactionDelay_Postpones_Switch()
        {
            var a = new FakeRule("fight", false);
            var b = new FakeRule("route", true);
            var brain = new HeroBrain { ReactionDelay = 0.5f };
            var ctx = Ctx();
            brain.SetRules(new IHeroRule[] { a, b }, ctx);
            brain.Tick(ctx, 0.1f);
            Assert.AreEqual("route", brain.ActiveId, "first selection is immediate");
            a.Can = true;
            for (int i = 0; i < 4; i++) brain.Tick(ctx, 0.1f);
            Assert.AreEqual("route", brain.ActiveId, "still reacting after 0.4 s");
            brain.Tick(ctx, 0.1f);
            Assert.AreEqual("fight", brain.ActiveId, "switches at 0.5 s (Concussion +0.5 s)");
        }

        [Test]
        public void RuleChanged_Event_Fires()
        {
            var a = new FakeRule("a", true);
            var brain = new HeroBrain();
            var ctx = Ctx();
            string seen = null;
            brain.RuleChanged += (prev, next) => seen = next?.Id;
            brain.SetRules(new IHeroRule[] { a }, ctx);
            brain.Tick(ctx, 0.1f);
            Assert.AreEqual("a", seen);
        }

        static List<RouteNode> Line() => new List<RouteNode>
        {
            new RouteNode { Position = new Vector3(0, 0, 0) },
            new RouteNode { Position = new Vector3(0, 0, 5), Threshold = true, Label = "gate" },
            new RouteNode { Position = new Vector3(0, 0, 10) },
        };

        [Test]
        public void Route_Pauses_At_Threshold_Until_Sidekick_Near()
        {
            var r = new RouteFollower();
            r.SetNodes(Line());
            var pos = new Vector3(0, 0, 0);
            var dir = r.Tick(pos, 0.1f, false, 1.5f, 6f);
            Assert.AreEqual(1, r.Index, "first node reached, heading to the gate");
            Assert.Greater(dir.z, 0.9f);
            pos = new Vector3(0, 0, 4.8f);
            r.Tick(pos, 0.1f, false, 1.5f, 6f);
            Assert.IsTrue(r.Paused, "pauses at threshold");
            for (int i = 0; i < 20; i++) r.Tick(pos, 0.1f, false, 1.5f, 6f); // 2 s, sidekick far
            Assert.IsTrue(r.Paused, "waits for the sidekick while under the max wait");
            r.Tick(pos, 0.1f, true, 1.5f, 6f);
            Assert.IsFalse(r.Paused, "released once the sidekick is near after the min pause");
            Assert.AreEqual(2, r.Index);
        }

        [Test]
        public void Route_Releases_At_MaxWait_And_Never_Repauses()
        {
            var r = new RouteFollower();
            r.SetNodes(Line(), 1);
            var pos = new Vector3(0, 0, 5f);
            r.Tick(pos, 0.1f, false, 1.5f, 6f);
            Assert.IsTrue(r.Paused);
            for (int i = 0; i < 61; i++) r.Tick(pos, 0.1f, false, 1.5f, 6f);
            Assert.IsFalse(r.Paused, "max wait releases without the sidekick");
            r.Tick(pos, 0.1f, false, 1.5f, 6f);
            Assert.IsFalse(r.Paused, "a passed threshold does not pause again");
        }

        [Test]
        public void Route_Held_Blocks_Release()
        {
            var r = new RouteFollower();
            r.SetNodes(Line(), 1);
            var pos = new Vector3(0, 0, 5f);
            r.Tick(pos, 0.1f, true, 0.5f, 6f);
            r.Held = true;
            for (int i = 0; i < 100; i++) r.Tick(pos, 0.1f, true, 0.5f, 6f);
            Assert.IsTrue(r.Paused, "held (encounter/cutscene) keeps the hero at the threshold");
        }

        [Test]
        public void RuleSet_FallsBack_To_Lower_Stage()
        {
            var def = ScriptableObject.CreateInstance<HeroRuleSetDef>();
            def.s0.Add(new RuleEntry("follow_route"));
            def.s1.Add(new RuleEntry("idle"));
            Assert.AreEqual("follow_route", def.For(Stage.S0)[0].id);
            Assert.AreEqual("idle", def.For(Stage.S1)[0].id);
            Assert.AreEqual("idle", def.For(Stage.S3)[0].id, "S3 unset → S1 list");
            Object.DestroyImmediate(def);
        }

        [Test]
        public void Factory_Appends_Idle_As_Last_Resort()
        {
            var rules = HeroRuleFactory.Build(new[] { new RuleEntry("follow_route") });
            Assert.AreEqual("idle", rules[rules.Count - 1].Id);
        }
    }
}
