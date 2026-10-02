using System.Collections;
using HS.Boss;
using HS.Core;
using HS.Enemies;
using HS.Hero.Callum;
using HS.Rapport;
using HS.Sidekick;
using HS.Skills;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>The rigged duel end to end, plus its balance gradient (GDD §6.1 boss; slice: S0 vs S1).</summary>
    public class RiggedDuelTests
    {
        GameObject _ctxGo, _bootGo;
        SimLoop Loop => SimLoop.Instance;
        RunContext Ctx => RunContext.Current;
        DuelBootstrap _boot;
        RiggedDuelDirector D => _boot.Director;

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
            foreach (var n in new[] { "RiggedDuel", "OpportunityDirector", "StoneSystem", "UIRoot", "CameraRig", "Main Camera", "AudioDirector" })
            {
                var g = GameObject.Find(n);
                if (g) Object.DestroyImmediate(g);
            }
            if (_boot != null && _boot.Arena != null) Object.DestroyImmediate(_boot.Arena.gameObject);
            if (_bootGo) Object.DestroyImmediate(_bootGo);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject);
        }

        IEnumerator Duel(Stage stage)
        {
            _bootGo = new GameObject("DuelBootstrap");
            _boot = _bootGo.AddComponent<DuelBootstrap>();
            _boot.HeroStage = stage;
            yield return null; // Start → Build
            yield return null; // agents' Start
            Assert.IsNotNull(D, "duel staged");
        }

        int RunUntilOver(float maxSeconds = 150f)
        {
            int n = Mathf.CeilToInt(maxSeconds / SimLoop.Dt), i = 0;
            while (i < n && D.Current != RiggedDuelDirector.Phase.Won && D.Current != RiggedDuelDirector.Phase.Lost)
            {
                Loop.Step();
                i++;
            }
            return i;
        }

        [UnityTest]
        public IEnumerator Terms_Then_A_Formal_Duel_Then_The_Volley_At_25s()
        {
            yield return Duel(Stage.S0);
            Assert.AreEqual(RiggedDuelDirector.Phase.Terms, D.Current);
            Assert.AreEqual(3, D.Archers.Count);
            Loop.StepMany(Mathf.CeilToInt((RiggedDuelDirector.TermsLength - 0.5f) / SimLoop.Dt));
            Assert.AreEqual(RiggedDuelDirector.Phase.Terms, D.Current);
            Assert.IsNull(((CallumModule)_boot.Hero.Module).Challenged, "nobody duels during the terms");
            int guard = 0;
            while (D.Current == RiggedDuelDirector.Phase.Terms && guard++ < 120) Loop.Step();
            Loop.Step();
            Assert.AreEqual(RiggedDuelDirector.Phase.Duel, D.Current);
            Assert.AreEqual(D.Ashgrave, ((CallumModule)_boot.Hero.Module).Challenged, "he challenges Ashgrave");
            Assert.IsTrue(((CallumModule)_boot.Hero.Module).NoAidTerms);
            foreach (var a in D.Archers) Assert.IsTrue(a.IsHidden, "the gallery waits");
            Loop.StepMany(Mathf.CeilToInt((24.8f - D.DuelTime) / SimLoop.Dt));
            Assert.IsFalse(D.Signalled);
            Loop.StepMany(Mathf.CeilToInt(0.4f / SimLoop.Dt));
            Assert.IsTrue(D.Signalled, "fixed signal at T+25 s");
            foreach (var a in D.Archers) Assert.IsFalse(a.IsHidden);
            Assert.IsTrue(D.Ashgrave.IsAlive, "the duel must outlast the signal (otherwise the trap never springs)");
        }

        [UnityTest]
        public IEnumerator S0_Alone_The_Gallery_Decides_It()
        {
            yield return Duel(Stage.S0);
            RunUntilOver();
            Assert.AreEqual(RiggedDuelDirector.Phase.Lost, D.Current, "S0 with no help: the rigged duel kills him");
            Assert.AreEqual("arrows", D.LossCause, "and the loss is attributable to the gallery");
            Assert.IsFalse(((CallumModule)_boot.Hero.Module).GuardingArrows, "S0 never raises his guard");
        }

        [UnityTest]
        public IEnumerator S1_Alone_Raises_His_Guard_And_Lasts_Longer()
        {
            yield return Duel(Stage.S0);
            int s0 = RunUntilOver();
            float s0AshHp = D.Ashgrave.Health.Fraction;
            TearDown();
            SetUp();
            yield return Duel(Stage.S1);
            int s1 = RunUntilOver();
            var cm = (CallumModule)_boot.Hero.Module;
            Assert.IsTrue(cm.GuardingArrows, "S1: guard up after the first volley");
            Assert.IsTrue(D.Current == RiggedDuelDirector.Phase.Won || s1 > s0, "S1 survives longer than S0");
            Assert.Less(D.Ashgrave.Health.Fraction, s0AshHp + 1e-3f, "S1 gets further into Ashgrave");
            Debug.Log($"[balance] S0 alone: {s0 * SimLoop.Dt:0.0}s, Ashgrave left {s0AshHp:P0}; S1 alone: {s1 * SimLoop.Dt:0.0}s, {D.Current}, Ashgrave left {D.Ashgrave.Health.Fraction:P0}");
        }

        [UnityTest]
        public IEnumerator Silencing_The_Gallery_Unseen_Wins_It_Even_At_S0()
        {
            yield return Duel(Stage.S0);
            Loop.StepMany(Mathf.CeilToInt((RiggedDuelDirector.TermsLength + 12f) / SimLoop.Dt));
            var sk = _boot.Sidekick;
            // The "very hard" route, done: break the arena's chronicle stone (it sees most of the octagon), then the archers.
            foreach (var st in HS.Rooms.ChronicleStone.All) st.TakeDamage(DamageInfo.Make(sk, st, 999f, DamageKind.Knife, "knife"));
            foreach (var a in D.Archers) a.TakeDamage(DamageInfo.Make(sk, a, 999f, DamageKind.Knife, "knife"));
            RunUntilOver();
            Assert.AreEqual(RiggedDuelDirector.Phase.Won, D.Current, "without the volleys he wins the duel");
            var l = Ctx.Get<RapportLedger>();
            Assert.GreaterOrEqual(l.Earned, 3 * CallumDance.WUnseenAssist - 1e-3f, "three unseen assists");
            Assert.AreEqual(0f, l.RawPenaltiesIn(1), 1e-3f, "unseen: no terms broken");
        }

        [UnityTest]
        public IEnumerator At_S1_Silencing_One_Archer_Is_Enough_At_S0_It_Is_Not()
        {
            foreach (var stage in new[] { Stage.S1, Stage.S0 })
            {
                yield return Duel(stage);
                Loop.StepMany(Mathf.CeilToInt((RiggedDuelDirector.TermsLength + 12f) / SimLoop.Dt));
                D.Archers[0].TakeDamage(DamageInfo.Make(_boot.Sidekick, D.Archers[0], 999f, DamageKind.Knife, "knife"));
                RunUntilOver();
                Assert.AreEqual(stage == Stage.S1 ? RiggedDuelDirector.Phase.Won : RiggedDuelDirector.Phase.Lost, D.Current,
                    "the slope: an S1 hero needs a little help, an S0 hero needs a lot");
                TearDown();
                SetUp();
            }
        }

        [UnityTest]
        public IEnumerator Breaking_The_Terms_In_His_Sight_Is_Caught()
        {
            yield return Duel(Stage.S0);
            Loop.StepMany(Mathf.CeilToInt((RiggedDuelDirector.TermsLength + 3f) / SimLoop.Dt));
            var cm = (CallumModule)_boot.Hero.Module;
            var sk = _boot.Sidekick;
            sk.Motor.Teleport(D.Ashgrave.Position + (_boot.Hero.Position - D.Ashgrave.Position).normalized * 1.2f + Vector3.right * 1.5f);
            Physics.SyncTransforms();
            D.Ashgrave.TakeDamage(DamageInfo.Make(sk, D.Ashgrave, 8f, DamageKind.Knife, "knife"));
            Assert.Less(cm.Honor, 100f, "aid in his sight breaks the terms");
            Assert.Greater(Ctx.Get<RapportLedger>().RawPenaltiesIn(1), 0f);
        }

        string Report(string label, int ticks)
        {
            var cm = (CallumModule)_boot.Hero.Module;
            return $"[balance] {label}: {D.Current} after {ticks * SimLoop.Dt:0.0}s (duel {D.DuelTime:0.0}s) Callum {_boot.Hero.Health.Current:0}/{_boot.Hero.Health.Max:0} " +
                   $"arrows {cm.ArrowsTaken} guard {cm.GuardingArrows} Ashgrave {D.Ashgrave.Health.Current:0}/{D.Ashgrave.Health.Max:0} cause {D.LossCause}";
        }

        [UnityTest]
        public IEnumerator Balance_Report()
        {
            foreach (var (label, stage, silence) in new[] { ("S0 alone", Stage.S0, 0), ("S1 alone", Stage.S1, 0), ("S0 all silenced@T+12", Stage.S0, 3), ("S1 one silenced@T+12", Stage.S1, 1), ("S0 one silenced@T+12", Stage.S0, 1) })
            {
                yield return Duel(stage);
                if (silence > 0)
                {
                    Loop.StepMany(Mathf.CeilToInt((RiggedDuelDirector.TermsLength + 12f) / SimLoop.Dt));
                    for (int k = 0; k < silence; k++) D.Archers[k].TakeDamage(DamageInfo.Make(_boot.Sidekick, D.Archers[k], 999f, DamageKind.Knife, "knife"));
                }
                int t = RunUntilOver();
                Debug.Log(Report(label, t));
                TearDown();
                SetUp();
            }
            Assert.Pass();
        }
    }
}
