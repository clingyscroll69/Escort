using System.Collections;
using System.Collections.Generic;
using HS.Boss;
using HS.Core;
using HS.Enemies;
using HS.Flow;
using HS.Hero;
using HS.Hero.Callum;
using HS.Rooms;
using HS.Sidekick;
using HS.Skills;
using HS.Skills.Impl;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>The Gallery (campaign spec §5; Plan 5): the phases, the Mirror's habits and counters, the Duet Finisher.</summary>
    public class GalleryBossTests
    {
        GameObject _ctxGo;
        SimLoop Loop => SimLoop.Instance;
        RunContext Ctx => RunContext.Current;
        RoomModule _arena;
        HeroAgent _hero;
        CallumModule _cm;
        SidekickAgent _sk;
        GalleryBoss _boss;

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
            foreach (var p in Object.FindObjectsByType<ProjectileSystem>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
            if (_boss) Object.DestroyImmediate(_boss.gameObject);
            if (_arena) Object.DestroyImmediate(_arena.gameObject);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject);
        }

        IEnumerator Gallery(Stage stage, string capstone = null, bool heroCanDie = false)
        {
            var assets = GameAssets.Load();
            ProjectileSystem.Ensure();
            Ctx.Chapter = 5;
            var arenaPrefab = assets.gallery != null ? assets.gallery : assets.boss;
            _arena = Object.Instantiate(arenaPrefab, Vector3.zero, Quaternion.identity).GetComponent<RoomModule>();
            _arena.SetRoomIndex(200);
            _hero = Object.Instantiate(assets.hero, new Vector3(0f, 0.05f, 1f), Quaternion.identity).GetComponent<HeroAgent>();
            _sk = Object.Instantiate(assets.sidekick, new Vector3(-1.8f, 0.05f, 0.4f), Quaternion.identity).GetComponent<SidekickAgent>();
            var pc = _sk.GetComponent<PlayerCommands>();
            if (pc != null) pc.enabled = false;
            _sk.Commands = new ScriptedCommands();
            Ctx.Hero = _hero;
            Ctx.Sidekick = _sk;
            yield return null; // Start
            _hero.ApplyStage(stage);
            _cm = _hero.Module as CallumModule;
            var rules = CampaignSchedule.For(5);
            _cm.ApplyChapter(5, rules.Unlocks, rules.Recovery);
            _hero.Health.Invulnerable = !heroCanDie;
            _sk.Health.Invulnerable = true;
            if (capstone != null) Assert.IsTrue(_sk.GetComponent<SidekickSkills>().System.LearnCapstone(SkillCatalog.Load().Get(capstone)));
            _boss = new GameObject("GalleryBoss").AddComponent<GalleryBoss>();
            _boss.Begin(_arena, _hero, _sk);
            yield return null;
        }

        void StepUntil(System.Func<bool> done, float maxSeconds, string what)
        {
            int n = Mathf.CeilToInt(maxSeconds / SimLoop.Dt), i = 0;
            while (!done() && i++ < n) Loop.Step();
            Assert.IsTrue(done(), what);
        }

        void Seconds(float s) => Loop.StepMany(Mathf.CeilToInt(s / SimLoop.Dt));

        /// <summary>Through the diagnosis and the terms, Ashgrave felled, the unmasking: the Mirror, fighting.</summary>
        void ToMirror()
        {
            StepUntil(() => _boss.Current == GalleryBoss.Phase.Duel, GalleryBoss.DiagnosisLength + GalleryBoss.TermsLength + 1f, "the duel proper");
            _boss.Ashgrave.TakeDamage(DamageInfo.Make(_hero, _boss.Ashgrave, 1e9f, DamageKind.Blade, "sword"));
            StepUntil(() => _boss.Current == GalleryBoss.Phase.Mirror, GalleryBoss.UnmaskLength + 2f, "the Mirror");
            StepUntil(() => _boss.Brain.Current == MirrorBrain.Mode.Fight, 3f, "its salute done");
        }

        void Ping(Agent target) => Ctx.Events.RaisePing(new PingInfo { Point = target.Position, Target = target, Meaning = "mark" });

        void ToFloor()
        {
            var m = _boss.Mirror;
            m.TakeDamage(DamageInfo.Make(_hero, m, 1e9f, DamageKind.Blade, "judgment"));
            Assert.AreEqual(MirrorCounters.Floor, m.Health.Fraction, 1e-3f, "nothing but the Duet takes the last quarter");
            Assert.IsTrue(m.IsAlive);
        }

        [UnityTest]
        public IEnumerator Phases_Advance_From_The_Diagnosis_To_The_Aftermath()
        {
            yield return Gallery(Stage.S3, "hold_please");
            Assert.AreEqual(GalleryBoss.Phase.Diagnosis, _boss.Current);
            var lines = new List<string>();
            Ctx.Events.Bark += b => lines.Add(b.SpeakerId + ": " + b.Text);
            Loop.Step();
            _boss.Ashgrave.TakeDamage(DamageInfo.Make(_sk, _boss.Ashgrave, 1e9f, DamageKind.Knife, "knife"));
            Assert.IsTrue(_boss.Ashgrave.IsAlive, "nothing lands during the diagnosis");
            StepUntil(() => _boss.Current == GalleryBoss.Phase.Terms, GalleryBoss.DiagnosisLength + 0.5f, "terms");
            Assert.IsTrue(lines.Exists(l => l.Contains("no page for you")), "the line that has no entry for her");
            StepUntil(() => _boss.Current == GalleryBoss.Phase.Duel, GalleryBoss.TermsLength + 0.5f, "duel");
            Assert.IsFalse(_cm.NoAidTerms, "S3: a rigged duel binds no one");
            Assert.IsFalse(_boss.Glyph.Sworn);
            _boss.Ashgrave.Health.SetCurrent(_boss.Ashgrave.Health.Max * 0.55f);
            Seconds(0.1f);
            Assert.IsTrue(_boss.Signalled, "the signal comes at once when he falls to 60%");
            Assert.AreEqual(GalleryBoss.Phase.Persona, _boss.Current);
            Assert.AreEqual("ashgrave_unmasked", _boss.Ashgrave.Archetype);
            Assert.IsTrue(_boss.Ashgrave.IsCheater, "the persona is a flagged cheater");
            foreach (var a in _boss.Archers) Assert.IsFalse(a.IsHidden, "every archer stands");
            _boss.Ashgrave.TakeDamage(DamageInfo.Make(_hero, _boss.Ashgrave, 1e9f, DamageKind.Blade, "sword"));
            Seconds(0.1f);
            Assert.AreEqual(GalleryBoss.Phase.Unmasking, _boss.Current);
            foreach (var a in _boss.Archers) Assert.IsFalse(a.gameObject.activeSelf, "the gallery melts away");
            StepUntil(() => _boss.Current == GalleryBoss.Phase.Mirror, GalleryBoss.UnmaskLength + 1f, "the Mirror");
            Assert.IsTrue(lines.Exists(l => l.Contains("first I could not finish")));
            Assert.IsNotNull(_boss.Mirror);
            Assert.AreEqual("mirror", _boss.Mirror.Archetype);
            StepUntil(() => _boss.Brain.Current == MirrorBrain.Mode.Fight, 3f, "its salute done");
            ToFloor();
            StepUntil(() => _boss.Duet.Open, MirrorCounters.TellTime + 1f, "the Link ring");
            Assert.AreEqual(2f, _boss.Duet.Window, 1e-3f, "Hold Please doubles the window");
            Assert.IsTrue(Ctx.Get<ILinkWindow>().TryLink("hold_please"));
            StepUntil(() => !_boss.Mirror.IsAlive, 4f, "the Duet lands");
            StepUntil(() => _boss.Current == GalleryBoss.Phase.Won, GalleryBoss.AftermathLength + 1f, "the aftermath");
            Assert.IsTrue(lines.Exists(l => l.Contains(CuratorDiagnosis.WeLine(Stage.S3))), "his \"we\" line");
            Assert.IsTrue(lines.Exists(l => l.Contains("STATUS: LISTED")));
        }

        [UnityTest]
        public IEnumerator The_Terms_By_Stage()
        {
            foreach (var (stage, sworn) in new[] { (Stage.S0, true), (Stage.S1, true), (Stage.S2, true), (Stage.S3, false) })
            {
                yield return Gallery(stage);
                StepUntil(() => _boss.Current == GalleryBoss.Phase.Duel, GalleryBoss.DiagnosisLength + GalleryBoss.TermsLength + 1f, "duel at " + stage);
                Assert.AreEqual(sworn, _cm.NoAidTerms, stage.ToString());
                TearDown();
                SetUp();
            }
        }

        [UnityTest]
        public IEnumerator The_Mirror_Never_Targets_Her()
        {
            yield return Gallery(Stage.S1);
            ToMirror();
            var m = _boss.Mirror;
            int onHer = 0;
            Ctx.Events.Damage += (d, applied) => { if (d.Source == m && d.Target == _sk) onHer++; };
            _sk.Motor.Teleport(m.Position + m.Forward * 1.2f);
            for (int i = 0; i < 16; i++)
            {
                m.TakeDamage(DamageInfo.Make(_sk, m, 5f, DamageKind.Knife, "knife"));
                Seconds(0.5f);
                _sk.Motor.Teleport(m.Position + m.Forward * 1.2f);
            }
            Assert.AreEqual(0, onHer, "no entry, no target");
            Assert.IsNull(m.Target);
        }

        [UnityTest]
        public IEnumerator A_Ping_Stalls_It_At_S1_Not_At_S0()
        {
            yield return Gallery(Stage.S0);
            ToMirror();
            Ping(_boss.Mirror);
            Assert.IsFalse(_boss.Brain.Broken, "S0 is too busy fighting to hear it");
            Assert.AreEqual(MirrorBrain.Mode.Fight, _boss.Brain.Current);
            TearDown();
            SetUp();

            yield return Gallery(Stage.S1);
            ToMirror();
            Ping(_boss.Mirror);
            Assert.AreEqual(MirrorBrain.Mode.Salute, _boss.Brain.Current, "it must salute back");
            Assert.IsTrue(_boss.Brain.Broken);
            Assert.AreSame(_boss.Mirror, _cm.Challenged);
            Assert.IsTrue(_cm.Saluting, "he re-challenges");
            Seconds(MirrorCounters.SaluteTime + 0.1f);
            Assert.AreEqual(MirrorBrain.Mode.Wait, _boss.Brain.Current, "and waits");
            float hp = _boss.Mirror.Health.Current;
            _boss.Mirror.TakeDamage(DamageInfo.Make(_hero, _boss.Mirror, 100f, DamageKind.Blade, "judgment"));
            Assert.AreEqual(100f * MirrorCounters.HeroBlowMul * MirrorCounters.BreakDamageMul, hp - _boss.Mirror.Health.Current, 0.01f, "+100% during the break");
            Ping(_boss.Mirror);
            Assert.AreEqual(MirrorCounters.EtiquetteCooldown - MirrorCounters.SaluteTime - 0.1f, _boss.Brain.EtiquetteCooldown, 0.1f, "12 s cooldown");
        }

        [UnityTest]
        public IEnumerator S3_Strikes_During_Its_Salute_And_Its_Honor_Breaks()
        {
            yield return Gallery(Stage.S3);
            ToMirror();
            Ping(_boss.Mirror);
            Assert.AreEqual(MirrorBrain.Mode.Salute, _boss.Brain.Current);
            Assert.IsFalse(_boss.Brain.Broken, "no break until he takes the opening");
            _boss.Mirror.TakeDamage(DamageInfo.Make(_hero, _boss.Mirror, 50f, DamageKind.Blade, "sword"));
            Assert.AreEqual(1, _boss.Brain.DishonourBreaks);
            Assert.AreEqual(0.75f, _boss.Brain.DamageMul, 1e-4f, "−25% damage");
            Assert.IsTrue(_boss.Mirror.Status.Has(StatusType.Staggered));
            Assert.AreEqual(MirrorCounters.DishonourStagger, _boss.Mirror.Status.Remaining(StatusType.Staggered), 0.05f);
            Assert.IsTrue(_boss.Brain.Broken);
            Assert.AreEqual(0.5f, MirrorCounters.DamageAfterDishonour(5), 1e-4f, "never below half");
        }

        [UnityTest]
        public IEnumerator A_Collapse_In_Its_Niche_Staggers_It_6s()
        {
            yield return Gallery(Stage.S2);
            ToMirror();
            var m = _boss.Mirror;
            _hero.Motor.Teleport(m.Position + m.Forward * 2f);
            Decoy.Plant(_sk, m.Position + m.transform.right * 1.5f, 10f, 8f, Ctx);
            StepUntil(() => _boss.Brain.InNiche, 8f, "three engagers: it falls back into a niche");
            m.TakeDamage(DamageInfo.Make(_sk, m, 10f, DamageKind.Heavy, "loosen_bolt", 1.5f));
            Assert.AreEqual(MirrorCounters.CollapseStagger, m.Status.Remaining(StatusType.Staggered), 0.05f);
            Assert.IsTrue(_boss.Brain.Broken);
        }

        [UnityTest]
        public IEnumerator Feint_Draws_His_Wait_Then_A_Free_Heavy()
        {
            yield return Gallery(Stage.S1, heroCanDie: true);
            ToMirror();
            float landed = 0f;
            _boss.Brain.FeintLanded += dmg => landed = dmg;
            int n = Mathf.CeilToInt((MirrorCounters.FeintEvery + 8f) / SimLoop.Dt);
            for (int i = 0; i < n && _boss.Brain.Current != MirrorBrain.Mode.Feint; i++)
            {
                Loop.Step();
                if (i % 60 == 0) _hero.Health.Heal(1e9f); // the copy hits as hard as he does: keep him standing for the count
            }
            Assert.AreEqual(MirrorBrain.Mode.Feint, _boss.Brain.Current, "the feint");
            Assert.IsTrue(_boss.Mirror.IsUnreadyFor(_hero), "it looks like a man off balance");
            StepUntil(() => _boss.Brain.Current != MirrorBrain.Mode.Feint, MirrorCounters.FeintTime + 0.2f, "the feint ends");
            Assert.Greater(landed, 0f, "he waited, and paid");
        }

        [UnityTest]
        public IEnumerator S0_Refuses_The_Duet_And_Takes_Its_Riposte()
        {
            yield return Gallery(Stage.S0, "crossfire", heroCanDie: true);
            ToMirror();
            _hero.Health.Heal(1e9f);
            int wounds = _hero.WoundCount;
            ToFloor();
            bool opened = false;
            int n = Mathf.CeilToInt(20f / SimLoop.Dt);
            for (int i = 0; i < n && _boss.Duet.Misses == 0; i++)
            {
                if (_boss.Duet.Current == DuetFinisher.Step.Idle) wounds = _hero.WoundCount; // only the riposte counts
                Loop.Step();
                opened |= _boss.Duet.Open;
            }
            Assert.IsFalse(opened, "\"No aid.\" The window never opens");
            Assert.AreEqual(1, _boss.Duet.Refusals);
            Assert.AreEqual(wounds + 1, _hero.WoundCount, "its riposte wounds him");
            Assert.AreEqual(MirrorCounters.RingReturn, _boss.Duet.Cooldown, 0.1f, "it reaches again in 10 s");
        }

        [UnityTest]
        public IEnumerator A_Missed_Ring_Returns_In_10s_And_A_Ping_Is_The_Duet_Without_A_Capstone()
        {
            yield return Gallery(Stage.S2);
            ToMirror();
            ToFloor();
            StepUntil(() => _boss.Duet.Open, MirrorCounters.TellTime + 1f, "the Link ring");
            Assert.AreEqual(1f, _boss.Duet.Window, 1e-3f, "no capstone: 1 s");
            int wounds = _hero.WoundCount;
            StepUntil(() => _boss.Duet.Misses == 1, 1.2f, "the ring closes");
            Assert.AreEqual(wounds + 1, _hero.WoundCount);
            Assert.IsTrue(_boss.Mirror.IsAlive);
            StepUntil(() => _boss.Duet.Open, MirrorCounters.RingReturn + MirrorCounters.TellTime + 2f, "the ring returns");
            Ping(_boss.Mirror);
            Assert.AreEqual("ping", _boss.Duet.LinkedWith);
            StepUntil(() => !_boss.Mirror.IsAlive, 4f, "the Duet lands");
        }

        [UnityTest]
        public IEnumerator Crossfire_Duet_Joins_His_Judgment()
        {
            yield return Gallery(Stage.S3, "crossfire");
            ToMirror();
            ToFloor();
            StepUntil(() => _boss.Duet.Open, MirrorCounters.TellTime + 1f, "the Link ring");
            Assert.IsTrue(_cm.FinisherCharging, "his Judgment charges with the ring");
            Assert.IsTrue(Ctx.Get<ILinkWindow>().TryLink("crossfire"));
            Assert.Greater(_cm.FinisherBonus, 0f, "the volley rides his blow");
            float honor = _cm.Honor;
            StepUntil(() => !_boss.Mirror.IsAlive, 4f, "the Duet lands");
            Assert.AreEqual(honor, _cm.Honor, 0.01f, "he asked for it");
        }
    }
}
