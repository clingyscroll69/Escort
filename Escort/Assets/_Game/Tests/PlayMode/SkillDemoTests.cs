using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using HS.Core;
using HS.Skills;
using HS.Tutorial;
using HS.Tutorial.Demo;
using HS.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace HS.Tests
{
    /// <summary>
    /// Skill demos: the stage's puppets live outside the simulation, every implemented skill's demo plays through at
    /// both ranks (explaining itself in steps) without raising a single event on the run, and skip/replay work.
    /// </summary>
    public class SkillDemoTests
    {
        [SetUp]
        public void SetUp()
        {
            new GameObject("RunContext").AddComponent<RunContext>();
            SimLoop.Ensure().Paused = true;
        }

        [TearDown]
        public void TearDown() => TestUi.TearDownAll();

        /// <summary>Count every event raised on the bus (each field gets a counting delegate of its own type).</summary>
        public static void HookAllEvents(EventBus bus, Action onAny)
        {
            foreach (var f in typeof(EventBus).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                var invoke = f.FieldType.GetMethod("Invoke");
                var ps = invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
                var lambda = Expression.Lambda(f.FieldType, Expression.Invoke(Expression.Constant(onAny)), ps).Compile();
                f.SetValue(bus, Delegate.Combine((Delegate)f.GetValue(bus), lambda));
            }
        }

        [UnityTest]
        public IEnumerator Stage_Puppets_Are_Outside_The_Simulation()
        {
            int events = 0;
            HookAllEvents(RunContext.Current.Events, () => events++);
            int agents = AgentRegistry.All.Count, ticks = SimLoop.Instance.RegisteredCount;
            var stage = DemoStage.Ensure();
            var sk = stage.Spawn("sidekick", new Vector2(-2f, 0f), 90f);
            var cal = stage.Spawn("callum", new Vector2(2f, 0f), -90f);
            stage.Prop("barrel_stack", new Vector2(0f, 3f), 0f);
            Assert.IsNotNull(sk.Anim, "the puppet keeps its animation driver");
            Assert.IsNull(sk.Root.GetComponentInChildren<Agent>(true), "and has no Agent");
            Assert.IsNotNull(GameAssets.Load().DemoProp("barrel_stack"), "demo props are registered");
            sk.Play("throw");
            cal.Locomotion(4f);
            stage.Rendering = true;
            yield return null;
            yield return null;
            Assert.AreEqual(agents, AgentRegistry.All.Count, "nothing joined the agent registry");
            Assert.AreEqual(ticks, SimLoop.Instance.RegisteredCount, "nothing ticks with the simulation");
            Assert.AreEqual(0, events, "nothing raised an event on the run");
            stage.Clear();
            yield return null;
            Assert.AreEqual(0, stage.transform.Find("Cast").childCount);
            Assert.AreEqual(0, stage.transform.Find("Props").childCount);
        }

        [UnityTest]
        public IEnumerator Every_Demo_Plays_Through_Without_Touching_The_Run([Values(1, 2)] int rank)
        {
            int events = 0;
            HookAllEvents(RunContext.Current.Events, () => events++);
            int agents = AgentRegistry.All.Count;
            var vp = DemoViewport.Create(UIKit.Stretch(UIRoot.Ensure().Overlay, "Host"), new Vector2(720f, 405f));
            vp.ManualClock = true;
            foreach (var def in SkillCatalog.Load().Implemented)
            {
                Assert.IsTrue(SkillDemos.Has(def.id), def.id + " has a demo");
                vp.Play(def.id, rank);
                var captions = new HashSet<string>();
                for (int i = 0; i < 1500 && !vp.AtEndCard; i++)
                {
                    vp.Advance(1f / 60f);
                    if (!string.IsNullOrEmpty(vp.CaptionText)) captions.Add(vp.CaptionText);
                    if (i % 6 == 0) yield return null; // let the animators and the overlay run some real frames
                }
                Assert.IsTrue(vp.AtEndCard, def.id + " reaches its end card");
                Assert.GreaterOrEqual(captions.Count, 3, def.id + " explains itself in steps");
                foreach (var cap in captions) CollectionAssert.IsEmpty(Lessons.CopyViolations(cap), def.id + ": " + cap);
            }
            vp.Stop();
            Assert.AreEqual(0, events, "a demo never raises an event on the run");
            Assert.AreEqual(agents, AgentRegistry.All.Count);
        }

        [UnityTest]
        public IEnumerator Skip_Jumps_To_The_End_Card_And_Replay_Restarts()
        {
            var vp = DemoViewport.Create(UIKit.Stretch(UIRoot.Ensure().Overlay, "Host"), new Vector2(720f, 405f));
            vp.ManualClock = true;
            vp.Play("pocket_sand", 1);
            vp.Advance(1f);
            yield return null;
            Assert.IsFalse(vp.AtEndCard);
            vp.Skip();
            yield return null;
            Assert.IsTrue(vp.AtEndCard);
            vp.Replay();
            yield return null;
            Assert.Less(vp.Time, 0.1f);
            Assert.IsFalse(vp.AtEndCard);
            Assert.AreEqual("pocket_sand", vp.SkillId);
        }

        [Test]
        public void End_Card_States_The_Numbers_At_Each_Rank()
        {
            var def = SkillCatalog.Load().Get("crossbow");
            StringAssert.Contains("Pierces <b>no</b>", DemoViewport.EndCardText(def, 1));
            StringAssert.Contains("Pierces <b>yes</b>", DemoViewport.EndCardText(def, 2));
            StringAssert.Contains("A SLIGHT IF SEEN", DemoViewport.EndCardText(def, 1));
        }
    }
}
