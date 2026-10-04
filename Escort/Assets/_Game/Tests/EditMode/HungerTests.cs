using HS.Hero;
using HS.Sidekick;
using NUnit.Framework;

namespace HS.Tests
{
    public class HungerTests
    {
        [Test]
        public void Drains_Over_Six_Minutes_Then_Starves()
        {
            var h = new Hunger { Enabled = true };
            bool? starving = null;
            h.Changed += (hungry, s) => starving = s;
            for (int i = 0; i < 180; i++) h.Tick(1f);
            Assert.AreEqual(50f, h.Value, 0.01f, "half way after three minutes");
            Assert.IsFalse(h.Hungry);
            for (int i = 0; i < 80; i++) h.Tick(1f);
            Assert.IsTrue(h.Hungry, "under 30");
            for (int i = 0; i < 200; i++) h.Tick(1f);
            Assert.AreEqual(0f, h.Value);
            Assert.IsTrue(h.Starving);
            Assert.AreEqual(true, starving);
            h.Feed();
            Assert.AreEqual(Hunger.Max, h.Value);
            Assert.IsFalse(h.Hungry);
        }

        [Test]
        public void Off_The_Road_Or_Off_The_Schedule_It_Holds()
        {
            var h = new Hunger { Enabled = false };
            h.Tick(500f);
            Assert.AreEqual(Hunger.Max, h.Value);
            Assert.IsFalse(h.Starving, "a disabled meter never starves him");
            h.Enabled = true;
            h.Paused = true;
            h.Tick(500f);
            Assert.AreEqual(Hunger.Max, h.Value);
        }

        [Test]
        public void She_Carries_Three_Rations_At_Most()
        {
            var r = new Rations();
            Assert.AreEqual(2, r.Give(2));
            Assert.AreEqual(1, r.Give(4), "a full pack leaves the rest");
            Assert.AreEqual(3, r.Count);
            Assert.IsTrue(r.Take());
            Assert.AreEqual(2, r.Count);
            r.Restore(0);
            Assert.IsFalse(r.Take());
        }
    }
}
