using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using HS.Core;
using HS.Skills;
using HS.Tutorial;
using NUnit.Framework;
using UnityEngine;

namespace HS.Tests
{
    /// <summary>
    /// The tutorial's data layer: art it needs, progress and settings, key glyphs, the lesson table and its copy rules,
    /// skill guides and synergies, and the modal gate.
    /// </summary>
    public class TutorialDataTests
    {
        ITutorialStore _savedStore;

        [SetUp]
        public void SetUp()
        {
            _savedStore = TutorialProgress.Store;
            TutorialProgress.Store = new MemoryStore();
        }

        [TearDown]
        public void TearDown() => TutorialProgress.Store = _savedStore;

        // ------------------------------------------------------------------------------------------------------ art
        [Test]
        public void Tutorial_Sprites_Are_Imported()
        {
            foreach (var id in new[]
                     {
                         "skill_pocket_sand", "skill_loosen_bolt", "skill_quiet_feet", "skill_crossbow", "skill_bandage",
                         "skill_cover_story", "verb_knife", "verb_dodge", "verb_ping", "verb_crouch", "verb_interact",
                         "wound_ankle", "wound_ribs", "wound_arm", "wound_fever", "wound_concussion", "fam_fixer", "fam_handler",
                         "fam_provisioner", "fam_scholar", "fam_combat", "blind", "alert", "question", "check",
                     })
                Assert.IsNotNull(Resources.Load<Sprite>("Icons/" + id), "Icons/" + id);
            foreach (var (id, border) in new[] { ("keycap", 14f), ("slot", 22f), ("card", 24f) })
            {
                var s = Resources.Load<Sprite>("UI/" + id);
                Assert.IsNotNull(s, "UI/" + id);
                Assert.AreEqual(border, s.border.x, 0.01f, id + " is 9-sliced");
            }
            foreach (var id in new[] { "corner", "ring", "spot", "grid", "hatch", "diamond", "line" })
                Assert.IsNotNull(Resources.Load<Sprite>("UI/" + id), "UI/" + id);
        }

        // ------------------------------------------------------------------------------------------------- progress
        [Test]
        public void Progress_Remembers_Seen_Lessons_Until_Reset()
        {
            Assert.IsFalse(TutorialProgress.IsSeen("cone"));
            TutorialProgress.MarkSeen("cone");
            TutorialProgress.MarkSeen("cone");
            Assert.IsTrue(TutorialProgress.IsSeen("cone"));
            CollectionAssert.AreEquivalent(new[] { "cone" }, TutorialProgress.Seen);
            TutorialProgress.ResetSeen();
            Assert.IsFalse(TutorialProgress.IsSeen("cone"));
        }

        [Test]
        public void Progress_Defaults_Tips_On_Pauses_On_Insight_Off()
        {
            Assert.IsTrue(TutorialProgress.TipsEnabled);
            Assert.IsTrue(TutorialProgress.LessonPauses);
            Assert.IsFalse(TutorialProgress.InsightDefault);
            TutorialProgress.TipsEnabled = false;
            Assert.IsFalse(TutorialProgress.TipsEnabled);
            TutorialProgress.ResetSeen();
            Assert.IsFalse(TutorialProgress.TipsEnabled, "resetting the tutorial keeps the settings");
        }

        [Test]
        public void Progress_In_The_Editor_Starts_Fresh_Each_Play()
        {
            // Lessons are marked seen as they show, so a remembered editor would show its owner fewer each Play.
            const string key = "hs.tut.seen";
            string saved = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
            bool remember = TutorialProgress.RememberInEditor;
            try
            {
                TutorialProgress.RememberInEditor = false;
                TutorialProgress.Store = null; // the default, as a Play starts
                TutorialProgress.MarkSeen("cone");
                Assert.IsTrue(TutorialProgress.IsSeen("cone"), "seen for the rest of this Play");
                Assert.AreEqual(saved, PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null, "but never saved");
                TutorialProgress.Store = null; // the next Play
                Assert.IsFalse(TutorialProgress.IsSeen("cone"), "the next Play shows it again");
                TutorialProgress.RememberInEditor = true;
                TutorialProgress.Store = null;
                Assert.IsInstanceOf<PlayerPrefsStore>(TutorialProgress.Store, "remembering works as a build does");
            }
            finally
            {
                TutorialProgress.RememberInEditor = remember;
                if (saved == null) PlayerPrefs.DeleteKey(key);
                else PlayerPrefs.SetString(key, saved);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void Progress_Lives_In_The_Store_Not_A_Cache()
        {
            var store = new MemoryStore();
            TutorialProgress.Store = store;
            TutorialProgress.MarkSeen("move");
            TutorialProgress.Store = new MemoryStore();
            Assert.IsFalse(TutorialProgress.IsSeen("move"));
            TutorialProgress.Store = store;
            Assert.IsTrue(TutorialProgress.IsSeen("move"));
        }

        // --------------------------------------------------------------------------------------------------- glyphs
        [TestCase("ping", "Q", "Y")]
        [TestCase("attack", "LMB", "X")]
        [TestCase("dodge", "Space", "B")]
        [TestCase("interact", "E", "A")]
        [TestCase("crouch", "C", "L3")]
        [TestCase("insight", "Tab", "Select")]
        [TestCase("pause", "Esc", "Start")]
        [TestCase("skill1", "1", "RT")]
        [TestCase("skill2", "2", "RB")]
        [TestCase("skill4", "4", "LB")]
        [TestCase("move", "WASD", "LS")]
        [TestCase("walk", "Shift", "LS (lightly)")]
        public void Glyphs_Follow_The_Real_Bindings(string token, string kb, string pad)
        {
            Assert.AreEqual(kb, KeyGlyphs.Label(token, GlyphDevice.Keyboard));
            Assert.AreEqual(pad, KeyGlyphs.Label(token, GlyphDevice.Gamepad));
        }

        [Test]
        public void Format_Replaces_Known_Tokens_And_Leaves_Others()
        {
            var s = KeyGlyphs.Format("Press {ping} near {nothing}.", GlyphDevice.Keyboard);
            StringAssert.Contains(KeyGlyphs.Chip("Q"), s);
            StringAssert.Contains("{nothing}", s);
            Assert.IsNull(KeyGlyphs.Label("nothing", GlyphDevice.Keyboard));
            Assert.AreEqual("skill3", KeyGlyphs.SlotToken(2));
        }

        // -------------------------------------------------------------------------------------------------- lessons
        static string Fill(string body) => body.Replace("{coverHint}", "").Replace("{slots}", "4").Replace("{loosen}", "1");

        [Test]
        public void Lessons_Are_Unique_Complete_And_Format_On_Both_Devices()
        {
            var ids = new HashSet<string>();
            foreach (var l in Lessons.All)
            {
                Assert.IsTrue(ids.Add(l.Id), "duplicate " + l.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(l.Body), l.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(l.Title), l.Id);
                if (l.Kind == LessonKind.Tip || l.Kind == LessonKind.Focus)
                    Assert.IsNotNull(Resources.Load<Sprite>("Icons/" + l.Icon), l.Id + " icon " + l.Icon);
                foreach (var d in new[] { GlyphDevice.Keyboard, GlyphDevice.Gamepad })
                {
                    var s = KeyGlyphs.Format(Fill(l.Body), d);
                    Assert.IsFalse(Regex.IsMatch(s, @"\{[a-z0-9]+\}"), $"{l.Id}: unresolved token in '{s}'");
                }
            }
            CollectionAssert.AreEquivalent(new[] { "hero_rules", "cone", "duel", "mirror" }, Lessons.All.Where(l => l.Kind == LessonKind.Focus).Select(l => l.Id),
                "four freeze-frame lessons");
            Assert.IsNull(Lessons.Get("nope"));
            Assert.AreEqual("cone", Lessons.Get("cone").Id);
        }

        [Test]
        public void Copy_Never_Mentions_The_Hidden_Stat()
        {
            foreach (var l in Lessons.All)
                CollectionAssert.IsEmpty(Lessons.CopyViolations(l.Title + " " + l.Body), l.Id);
            CollectionAssert.IsNotEmpty(Lessons.CopyViolations("Your Rapport is at Stage 1"), "the scan works");
            CollectionAssert.IsEmpty(Lessons.CopyViolations("<color=#FF0000>stagehand</color>"), "whole words only, tags ignored");
        }

        // ------------------------------------------------------------------------------------------ guides, synergies
        static string Inv(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        [Test]
        public void Every_Implemented_Skill_Has_A_Guide_With_Live_Numbers()
        {
            var cat = SkillCatalog.Load();
            Assert.IsNotNull(cat);
            foreach (var def in cat.Implemented)
            {
                var g = SkillGuides.Get(def.id);
                Assert.IsNotNull(g, def.id);
                Assert.IsNotEmpty(SkillGuides.StatLines(def), def.id);
                Assert.IsNotNull(Resources.Load<Sprite>("Icons/" + SkillGuides.IconId(def.id)), def.id + " icon");
                Assert.IsNotNull(Resources.Load<Sprite>("Icons/" + SkillGuides.FamilyIcon(def.family)), def.id + " family icon");
                CollectionAssert.IsEmpty(Lessons.CopyViolations(g.Tagline + " " + g.HowTo + " " + g.CallumView), def.id);
                Assert.IsFalse(Regex.IsMatch(KeyGlyphs.Format(g.HowTo, GlyphDevice.Gamepad), @"\{[a-z0-9]+\}"), def.id + " how-to tokens");
            }
            var sand = cat.Get("pocket_sand");
            var blind = SkillGuides.StatLines(sand).First(l => l.label.StartsWith("Blind"));
            Assert.AreEqual(Inv(sand.A(1)) + " s", blind.rank1);
            Assert.AreEqual(Inv(sand.A(2)) + " s", blind.rank2);
            Assert.AreEqual("DIRTY TRICK IF SEEN", SkillGuides.Conduct(sand));
            Assert.AreEqual("ABOVE BOARD", SkillGuides.Conduct(cat.Get("bandage")));
        }

        [Test]
        public void Synergies_Are_Symmetric_And_Only_Between_Implemented_Skills()
        {
            var impl = new HashSet<string>(SkillCatalog.Load().Implemented.Select(d => d.id));
            var seen = new HashSet<string>();
            foreach (var p in SkillSynergies.All)
            {
                Assert.IsTrue(impl.Contains(p.A) && impl.Contains(p.B), $"{p.A}+{p.B}");
                Assert.AreNotEqual(p.A, p.B);
                Assert.IsTrue(seen.Add(string.CompareOrdinal(p.A, p.B) < 0 ? p.A + "|" + p.B : p.B + "|" + p.A), "duplicate pair");
                Assert.AreEqual(SkillSynergies.Note(p.A, p.B), SkillSynergies.Note(p.B, p.A));
                CollectionAssert.IsEmpty(Lessons.CopyViolations(p.Note));
            }
            var with = SkillSynergies.With("quiet_feet", new[] { "pocket_sand", "bandage" });
            CollectionAssert.AreEqual(new[] { "pocket_sand" }, with.Select(w => w.other));
            CollectionAssert.DoesNotContain(SkillSynergies.Without("quiet_feet", new[] { "pocket_sand" }).Select(w => w.other), "pocket_sand");
            Assert.IsNull(SkillSynergies.Note("bandage", "quiet_feet"), "pairs that don't interact show nothing");
        }

        // ---------------------------------------------------------------------------------------------------- gate
        [Test]
        public void Gate_Pauses_And_Restores_What_Was_There()
        {
            var loop = SimLoop.Ensure();
            try
            {
                ModalGate.Clear();
                loop.Paused = false;
                GameInput.Instance.Gameplay.Enable();
                var a = ModalGate.Push("focus");
                var b = ModalGate.Push("pause");
                Assert.IsTrue(loop.Paused);
                Assert.IsFalse(GameInput.Instance.Gameplay.enabled);
                Assert.IsTrue(ModalGate.Has("pause"));
                ModalGate.Pop(a);
                Assert.IsTrue(loop.Paused, "still one modal open");
                ModalGate.Pop(b);
                ModalGate.Pop(b);
                Assert.AreEqual(0, ModalGate.Count);
                Assert.IsFalse(loop.Paused);
                Assert.IsTrue(GameInput.Instance.Gameplay.enabled);
                loop.Paused = true;
                var c = ModalGate.Push("x");
                ModalGate.Pop(c);
                Assert.IsTrue(loop.Paused, "a sim that was already paused stays paused");
                _ = ModalGate.Push("ui only", pauseSim: false, blockGameplay: true);
                loop.Paused = false;
                ModalGate.Clear();
                Assert.IsFalse(loop.Paused, "a token that doesn't pause leaves the sim alone");
                Assert.IsTrue(GameInput.Instance.Gameplay.enabled);
            }
            finally
            {
                ModalGate.Clear();
                Object.DestroyImmediate(loop.gameObject);
            }
        }
    }
}
