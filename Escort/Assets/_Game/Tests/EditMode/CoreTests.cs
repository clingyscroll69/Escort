using System.Collections.Generic;
using HS.Core;
using NUnit.Framework;
using UnityEngine;

namespace HS.Tests
{
    public class CoreTests
    {
        [TearDown]
        public void TearDown()
        {
            if (SimLoop.Instance != null) Object.DestroyImmediate(SimLoop.Instance.gameObject);
            foreach (var go in Object.FindObjectsByType<SimLoop>(FindObjectsSortMode.None)) Object.DestroyImmediate(go.gameObject);
        }

        [Test]
        public void DetRandom_SameSeed_SameSequence()
        {
            var a = new DetRandom(42);
            var b = new DetRandom(42);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(a.NextULong(), b.NextULong());
        }

        [Test]
        public void DetRandom_DifferentSeeds_Diverge()
        {
            var a = new DetRandom(1);
            var b = new DetRandom(2);
            int same = 0;
            for (int i = 0; i < 100; i++) if (a.Range(0, 1000) == b.Range(0, 1000)) same++;
            Assert.Less(same, 10);
        }

        [Test]
        public void DetRandom_Range_StaysInBounds()
        {
            var r = new DetRandom(7);
            for (int i = 0; i < 5000; i++)
            {
                int v = r.Range(-3, 4);
                Assert.GreaterOrEqual(v, -3);
                Assert.Less(v, 4);
            }
        }

        class Probe : ISimTickable
        {
            public int TickOrder { get; set; }
            public string Name;
            public List<string> Log;
            public void SimTick(float dt) => Log.Add(Name);
        }

        [Test]
        public void SimLoop_TicksByOrderThenRegistration()
        {
            var log = new List<string>();
            SimLoop.Register(new Probe { TickOrder = 300, Name = "enemy1", Log = log });
            SimLoop.Register(new Probe { TickOrder = 200, Name = "hero", Log = log });
            SimLoop.Register(new Probe { TickOrder = 300, Name = "enemy2", Log = log });
            SimLoop.Register(new Probe { TickOrder = 100, Name = "input", Log = log });
            SimLoop.Instance.Step();
            CollectionAssert.AreEqual(new[] { "input", "hero", "enemy1", "enemy2" }, log);
            Assert.AreEqual(1, SimLoop.Instance.TickIndex);
        }

        class SelfRemover : ISimTickable
        {
            public int TickOrder => 0;
            public int Count;
            public void SimTick(float dt)
            {
                Count++;
                SimLoop.Unregister(this);
            }
        }

        [Test]
        public void SimLoop_UnregisterDuringTick_IsSafe()
        {
            var r = new SelfRemover();
            SimLoop.Register(r);
            SimLoop.Instance.StepMany(3);
            Assert.AreEqual(1, r.Count);
        }

        [Test]
        public void Health_DamageAndDeath()
        {
            var h = new Health(100);
            bool died = false;
            h.Died += _ => died = true;
            Assert.AreEqual(30f, h.ApplyDamage(new DamageInfo { Amount = 30 }));
            Assert.AreEqual(70f, h.Current);
            Assert.AreEqual(70f, h.ApplyDamage(new DamageInfo { Amount = 500 }));
            Assert.IsTrue(died);
            Assert.AreEqual(0f, h.ApplyDamage(new DamageInfo { Amount = 5 }));
        }

        [Test]
        public void Health_MaxMultiplier_ClampsCurrent()
        {
            var h = new Health(100);
            h.SetMaxMultiplier(0.8f);
            Assert.AreEqual(80f, h.Max);
            Assert.AreEqual(80f, h.Current);
        }

        [Test]
        public void StatusSet_TimersExpire()
        {
            var s = new StatusSet();
            s.Apply(StatusType.Blinded, 1f);
            s.Tick(0.5f);
            Assert.IsTrue(s.Has(StatusType.Blinded));
            s.Tick(0.6f);
            Assert.IsFalse(s.Has(StatusType.Blinded));
        }

        [Test]
        public void Geo_Cone()
        {
            Assert.IsTrue(Geo.InCone(Vector3.zero, Vector3.forward, new Vector3(0, 0, 10), 120, 12));
            Assert.IsTrue(Geo.InCone(Vector3.zero, Vector3.forward, new Vector3(5, 0, 5), 120, 12));   // 45 deg
            Assert.IsFalse(Geo.InCone(Vector3.zero, Vector3.forward, new Vector3(10, 0, 1), 120, 12)); // ~84 deg
            Assert.IsFalse(Geo.InCone(Vector3.zero, Vector3.forward, new Vector3(0, 0, 13), 120, 12));
        }
    }
}
