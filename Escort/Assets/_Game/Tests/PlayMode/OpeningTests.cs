using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HS.Core;
using HS.Flow;
using HS.Opening;
using HS.Skills;
using HS.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>
    /// The opening (GDD §8): the walk with the earbuds in, the truck, white; the class roll settling on HERO, the error,
    /// HERO's SIDEKICK; the 3-second version on later runs; and "HERO" becoming his name when you meet him.
    /// </summary>
    public class OpeningTests
    {
        GameObject _ctxGo;

        [SetUp]
        public void SetUp()
        {
            _ctxGo = new GameObject("RunContext");
            _ctxGo.AddComponent<RunContext>();
            SimLoop.Ensure().Paused = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var a in Object.FindObjectsByType<Agent>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (a) Object.DestroyImmediate(a.gameObject);
            foreach (var n in new[] { "OpeningStreet", "Chapter", "OpportunityDirector", "StoneSystem", "UIRoot", "CameraRig", "Encounters", "Main Camera", "GameFlow", "AudioDirector", "TestSun" })
            {
                var g = GameObject.Find(n);
                if (g) Object.DestroyImmediate(g);
            }
            foreach (var p in Object.FindObjectsByType<ProjectileSystem>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject);
            RunState.Clear();
        }

        static OpeningView Make(bool shortVersion)
        {
            var v = OpeningView.Show(UIRoot.Ensure(), shortVersion);
            v.ManualClock = true;
            return v;
        }

        static readonly Regex Tags = new Regex("<[^>]*>");
        static string Plain(string s) => Tags.Replace(s, "");

        /// <summary>Advance the opening (30 fps) until its own clock reaches t, recording what the class line shows.</summary>
        static void RunTo(OpeningView v, float t, List<string> shown = null)
        {
            int guard = 0;
            while (v.Current != OpeningView.Phase.Done && (v.Current == OpeningView.Phase.PreRoll || v.Elapsed < t) && guard++ < 5000)
            {
                v.Advance(1f / 30f);
                if (shown == null || v.Current == OpeningView.Phase.Done) continue;
                string c = Plain(v.ClassLine);
                if (c.Length > 0 && (shown.Count == 0 || shown[shown.Count - 1] != c)) shown.Add(c);
            }
        }

        [Test]
        public void The_Walk_Ends_In_A_Truck_And_A_White_Screen()
        {
            var v = Make(false);
            RunTo(v, 0.5f);
            Assert.AreEqual(OpeningView.Phase.Walk, v.Current);
            RunTo(v, 6f);
            Assert.IsTrue(v.PhoneVisible, "the phone (the song in the earbuds) is up while walking");
            int expected = Mathf.FloorToInt((v.Elapsed - OpeningView.StepAt) / OpeningView.Step + 1e-4f) + 1;
            Assert.AreEqual(expected, v.Steps, "a step counted on every footfall");
            Assert.AreEqual(1, v.NotificationsShown);
            Assert.AreEqual(0f, v.GlowAlpha, 1e-3f, "no headlights before the horn");
            RunTo(v, OpeningView.CutAt - 0.1f);
            Assert.Greater(v.GlowAlpha, 0.6f, "headlights flood in as the horn rises");
            Assert.AreEqual(0f, v.WhiteAlpha, "not white until the cut");
            Assert.AreEqual(OpeningView.MaxSteps, v.Steps, "the walker stopped dead when the horn got close");
            RunTo(v, OpeningView.CutAt + 0.02f);
            Assert.AreEqual(OpeningView.Phase.White, v.Current);
            Assert.AreEqual(1f, v.WhiteAlpha, "a hard cut to white, on the same beat the song cuts");
            Assert.AreEqual(0f, v.GlowAlpha);
            Assert.IsFalse(v.PhoneVisible);
            RunTo(v, OpeningView.SystemAt - 0.05f);
            Assert.AreEqual(0f, v.WhiteAlpha, 1e-3f, "white fades to black before the System speaks");
        }

        [Test]
        public void The_Class_Rolls_Lands_On_HERO_Then_Errors_Into_HEROs_SIDEKICK()
        {
            var v = Make(false);
            bool done = false;
            v.Done += () => done = true;
            var shown = new List<string>();
            string heroThought = null;
            RunTo(v, OpeningView.SystemAt - 0.05f, shown);
            Assert.IsEmpty(shown, "no status window during the walk");
            while (!done)
            {
                v.Advance(1f / 30f);
                if (done) break;
                string c = Plain(v.ClassLine);
                if (c.Length > 0 && (shown.Count == 0 || shown[shown.Count - 1] != c)) shown.Add(c);
                if (c == "CLASS: HERO" && v.Thought.Length > 0) heroThought = v.Thought;
            }
            int hero = shown.IndexOf("CLASS: HERO");
            Assert.Greater(hero, 3, string.Join(" | ", shown));
            CollectionAssert.AreEqual(new[] { "CLASS: Barista", "CLASS: Tax Auditor", "CLASS: Dark Lord" }, shown.GetRange(hero - 3, 3),
                "it slows down through the GDD's classes and settles on HERO");
            Assert.AreEqual("YES. I finally get to be the hero.", heroThought);
            Assert.AreEqual("CLASS: HERO's SIDEKICK", shown[shown.Count - 1]);
            Assert.Greater(shown.Count - hero, 3, "red glyphs cycle before it settles");
            float total = OpeningView.PreRoll + v.Elapsed;
            Assert.That(total, Is.InRange(36f, 44f), "about 40 seconds (GDD §8; 41 s with the longer spin and the storm)");
        }

        [Test]
        public void The_Street_Plays_Out_On_The_Audio_Cues()
        {
            var v = Make(false);
            RunTo(v, 3f);
            var st = v.Street;
            Assert.IsNotNull(st, "the walk is a 3D street");
            Assert.IsFalse(st.WalkSignal, "the red hand while the cross street still has traffic");
            Assert.IsFalse(st.TruckVisible, "no truck yet");
            RunTo(v, OpeningStreet.WalkAt + 0.1f);
            Assert.IsTrue(st.WalkSignal, "WALK as the crossing ticks speed up");
            RunTo(v, OpeningStreet.FlashAt + 0.1f);
            Assert.IsFalse(st.WalkSignal, "flashing hand for the rest of the crossing");
            RunTo(v, OpeningStreet.TruckAt + 0.5f);
            Assert.IsTrue(st.TruckVisible);
            float far = st.TruckFront;
            Assert.Less(st.HeadYaw, 10f, "still looking at the phone");
            RunTo(v, OpeningView.LookUpAt + 0.6f);
            Assert.Less(st.TruckFront, far, "it's coming");
            Assert.Greater(st.HeadYaw, 60f, "the walker looks right, into it");
            Assert.AreEqual(OpeningStreet.TruckLane, st.EyeStreet.z, 0.5f, "and has stopped dead in its lane");
            RunTo(v, OpeningView.CutAt - 0.02f);
            Assert.AreEqual(OpeningStreet.TruckEndGap, st.TruckFront - st.EyeStreet.x, 0.4f, "the grille fills the frame on the cut");
        }

        [Test]
        public void The_Street_Hands_The_Chapter_Its_World_Back()
        {
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            var sun = new GameObject("TestSun").AddComponent<Light>();
            sun.type = LightType.Directional;
            RenderSettings.sun = sun;
            float fogEnd = RenderSettings.fogEndDistance = 140f;
            var v = Make(false);
            RunTo(v, 5f);
            Assert.IsFalse(cam.enabled, "the chapter's camera stands down during the walk");
            Assert.IsFalse(sun.enabled, "and its sun");
            Assert.AreNotEqual(fogEnd, RenderSettings.fogEndDistance, "the street has its own haze");
            Assert.AreEqual("OpeningCamera", Camera.main.name);
            RunTo(v, OpeningView.CutAt + 0.05f);
            Assert.IsNull(v.Street, "gone with the cut to white");
            Assert.IsTrue(cam.enabled);
            Assert.IsTrue(sun.enabled);
            Assert.AreEqual(sun, RenderSettings.sun);
            Assert.AreEqual(fogEnd, RenderSettings.fogEndDistance, 1e-3f);
        }

        [Test]
        public void The_Wired_Earphones_Run_From_The_Phone_To_The_Ears()
        {
            var v = Make(false);
            RunTo(v, 6f);
            var cable = v.Street.Cable;
            Assert.IsNotNull(cable);
            Assert.Less(Vector3.Distance(cable.Start, v.Street.Jack), 0.01f, "plugged into the phone");
            var head = v.Street.Head;
            Assert.Less(Vector3.Distance(cable.EndL, head.TransformPoint(cable.EarL)), 0.01f, "left earbud in");
            Assert.Less(Vector3.Distance(cable.EndR, head.TransformPoint(cable.EarR)), 0.01f, "right earbud in");
            Assert.IsTrue(v.PhoneVisible);
        }

        [Test]
        public void The_Reel_Spins_Five_Seconds_Through_Dozens_Of_Classes()
        {
            var v = Make(false);
            var roll = v.Roll;
            float reelAt = OpeningView.SystemAt + ClassRoll.ReelAt;
            Assert.AreEqual(ClassRoll.ReelLength, roll.HeroAt - reelAt, 0.01f, "about five seconds of spin");
            RunTo(v, reelAt);
            var names = new HashSet<string>();
            while (v.Elapsed < roll.HeroAt - 0.01f)
            {
                v.Advance(1f / 60f);
                names.Add(Plain(v.ClassLine));
            }
            Assert.GreaterOrEqual(ClassRoll.Classes.Length, 60);
            Assert.Greater(names.Count, 45, "the eye sees dozens of classes fly by");
            Assert.IsFalse(names.Contains("CLASS: HERO's SIDEKICK"));
            RunTo(v, roll.HeroAt + 0.5f);
            StringAssert.Contains("#F2C14E", v.ClassLine, "HERO lands in gold");
        }

        [Test]
        public void The_Error_Storms_The_Margins_Then_Clears_To_Blue()
        {
            var v = Make(false);
            var roll = v.Roll;
            RunTo(v, roll.ErrorAt + 0.1f);
            Assert.AreEqual(1f, roll.Redness, 1e-3f, "the window goes red");
            int peak = 0;
            while (v.Elapsed < roll.RecoverAt)
            {
                v.Advance(1f / 30f);
                peak = Mathf.Max(peak, roll.PopupsShown);
            }
            Assert.GreaterOrEqual(peak, 25, "a storm of error pop-ups");
            float stormLength = roll.RecoverAt - roll.ErrorAt;
            Assert.That(stormLength, Is.InRange(5f, 6f));
            var window = roll.WindowRect;
            for (int i = 0; i < roll.PopupTotal; i++)
            {
                var r = roll.PopupRect(i);
                Assert.IsFalse(r.Overlaps(window), $"pop-up {i} stays in the side margins");
                Assert.That(Mathf.Abs(r.center.x), Is.GreaterThan(window.width / 2f), $"pop-up {i} is left or right of the window");
            }
            Assert.That(roll.SettleAt - roll.RecoverAt, Is.InRange(2.5f, 3.5f), "about three seconds to clear");
            RunTo(v, roll.SettleAt + 0.05f);
            Assert.AreEqual(0, roll.PopupsShown, "all closed");
            Assert.AreEqual(0f, roll.Redness, 1e-3f, "the window is back to System blue");
            Assert.AreEqual("CLASS: HERO's SIDEKICK", Plain(v.ClassLine));
            StringAssert.Contains("#FF564A", v.ClassLine, "the class itself stays red");
        }

        [Test]
        public void Later_Runs_Get_The_Three_Second_Version()
        {
            var v = Make(true);
            bool done = false, flashed = false, settled = false, popped = false;
            v.Done += () => done = true;
            RunTo(v, 0f);
            while (!done)
            {
                v.Advance(1f / 30f);
                if (done) break;
                flashed |= v.WhiteAlpha >= 1f;
                settled |= Plain(v.ClassLine) == "CLASS: HERO's SIDEKICK";
                popped |= v.Roll.PopupsShown > 0;
                Assert.IsFalse(v.PhoneVisible);
            }
            Assert.IsTrue(flashed, "the horn sting and the flash, as a callback");
            Assert.IsTrue(settled);
            Assert.IsTrue(popped, "a few error pop-ups even in the short version");
            Assert.LessOrEqual(v.Elapsed, OpeningView.ShortLength + 0.05f);
        }

        [Test]
        public void Skipping_Takes_Two_Presses()
        {
            var v = Make(false);
            bool done = false;
            v.Done += () => done = true;
            RunTo(v, 2f);
            v.PressSkip();
            Assert.IsFalse(done, "a stray click doesn't eat the opening");
            Assert.IsTrue(v.SkipArmed);
            v.Advance(3f);
            Assert.IsFalse(v.SkipArmed, "the arm expires");
            v.PressSkip();
            Assert.IsFalse(done);
            v.PressSkip();
            Assert.IsTrue(done);
        }

        [Test]
        public void A_Silent_Audio_Clock_Cannot_Freeze_The_Opening()
        {
            var v = Make(false);
            RunTo(v, 0f);
            v.AudioClock = () => 100.0; // no audio device: the clock never moves
            v.FollowAudio(100.1);
            for (int i = 0; i < 60; i++) v.Advance(1f / 30f);
            Assert.Greater(v.Elapsed, 1.5f, "it falls back to real time");
        }

        [Test]
        public void The_Song_Street_And_Truck_Are_Cut_On_The_Same_Sample()
        {
            AudioClip Load(string n) => Resources.Load<AudioClip>("Audio/" + n);
            var song = Load("syn_open_song");
            var street = Load("syn_open_street");
            var horn = Load("syn_open_horn");
            var sting = Load("syn_open_sting");
            var ring = Load("syn_open_ring");
            Assert.AreEqual(OpeningView.CutAt, song.length, 0.01f, "tools/make_opening.py and OpeningView must agree");
            Assert.AreEqual(OpeningView.CutAt, street.length, 0.01f);
            Assert.AreEqual(OpeningView.CutAt - OpeningView.HornAt, horn.length, 0.01f, "the horn ends exactly at the cut");
            Assert.AreEqual(OpeningView.ShortFlash, sting.length, 0.01f);
            Assert.Less(ring.length, OpeningView.SystemAt - OpeningView.CutAt, "the ringing is over before the System speaks");
            Assert.AreEqual(2, song.channels, "the earbuds are stereo");
            Assert.AreEqual(2, horn.channels, "the truck comes from the right");
        }

        [Test]
        public void HERO_Quietly_Becomes_His_Name()
        {
            var hud = HudView.Create(UIRoot.Ensure());
            StringAssert.Contains("HERO's SIDEKICK", Plain(hud.SidekickLabel));
            hud.MeetHero("CALLUM");
            hud.TickSwap(0.3f);
            StringAssert.DoesNotContain("HERO", Plain(hud.SidekickLabel), "glyphs while it changes");
            hud.TickSwap(1f);
            Assert.AreEqual("YOU  CALLUM's SIDEKICK", Plain(hud.SidekickLabel));
        }

        [UnityTest]
        public IEnumerator Meeting_Callum_At_The_Start_Of_The_Road()
        {
            var go = new GameObject("GameFlow");
            var flow = go.AddComponent<GameFlow>();
            flow.AutoPlay = true;
            flow.Fast = true;
            yield return null; // Start → chapter build, opening picks, the meeting (Fast: at once)
            Assert.AreEqual(GameFlow.State.Chapter, flow.Current);
            yield return new WaitForSecondsRealtime(1f);
            Assert.AreEqual("YOU  CALLUM's SIDEKICK", Plain(flow.Chapter.Hud.SidekickLabel));
        }
    }
}
