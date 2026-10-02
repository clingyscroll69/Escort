using System.Collections;
using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Hero.Callum;
using HS.Rapport;
using HS.Sidekick;
using HS.Skills;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>Callum's Dance judged end-to-end: real hero, enemies, sidekick skills, ledger (GDD §4.4, §6.1).</summary>
    public class RapportTests
    {
        GameObject _ground, _ctxGo;
        SimLoop Loop => SimLoop.Instance;
        RunContext Ctx => RunContext.Current;
        HeroAgent _hero;
        CallumModule _cm;
        SidekickAgent _sk;
        SidekickSkills _skills;
        ScriptedCommands _cmd;
        RapportLedger L;
        OpportunityDirector _dir;

        [SetUp]
        public void SetUp()
        {
            _ground = SidekickTests.Ground();
            _ctxGo = new GameObject("RunContext");
            _ctxGo.AddComponent<RunContext>();
            SimLoop.Ensure().Paused = true;
            ProjectileSystem.Ensure();
        }

        [TearDown]
        public void TearDown()
        {
            if (_dir != null) Object.DestroyImmediate(_dir.gameObject);
            foreach (var a in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None)) Object.DestroyImmediate(a.gameObject);
            if (ProjectileSystem.Instance) Object.DestroyImmediate(ProjectileSystem.Instance.gameObject);
            Object.Destroy(_ground);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject);
        }

        IEnumerator Setup(Vector3 sidekickAt, params string[] skills)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Gameplay/Callum.prefab");
            var go = Object.Instantiate(prefab, Vector3.up * 0.05f, Quaternion.identity);
            _hero = go.GetComponent<HeroAgent>();
            _cm = go.GetComponent<CallumModule>();
            Ctx.Hero = _hero;
#endif
            _sk = SidekickTests.Spawn<SidekickAgent>(sidekickAt, 0.32f);
            _skills = _sk.gameObject.AddComponent<SidekickSkills>();
            _cmd = new ScriptedCommands();
            _sk.Commands = _cmd;
            Ctx.Sidekick = _sk;
            foreach (var id in skills) Assert.IsTrue(_skills.Learn(id), "learn " + id);
            yield return null;
            _hero.ApplyStage(Stage.S0);
            _hero.Route.SetNodes(new List<RouteNode> { new RouteNode { Position = new Vector3(0f, 0f, 40f) } });
            _dir = OpportunityDirector.Create(Ctx, _hero);
            L = _dir.Ledger;
        }

        EnemyAgent Enemy(Vector3 pos, string arch, bool engage = true, bool perch = false)
        {
            var e = SidekickTests.Spawn<EnemyAgent>(pos);
            var face = new Vector3(-pos.x, 0f, -pos.z);
            e.transform.rotation = Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face.normalized : Vector3.back);
            e.Archetype = arch;
            e.Elevated = perch;
            e.Configure(arch);
            if (engage) e.Activate();
            return e;
        }

        void Use(int slot, Vector3 aim) => _cmd.Current = new SidekickCommand { Skill = slot, AimPoint = aim, HasAim = true };

        void StepUntilDuel()
        {
            int guard = 0;
            while (_cm.Challenged == null && guard++ < 120) Loop.Step();
            Assert.IsNotNull(_cm.Challenged, "a duel started");
        }

        [UnityTest]
        public IEnumerator Unseen_Assist_Crossbow_On_A_Shooter_Behind_Him_During_A_Duel()
        {
            yield return Setup(new Vector3(2f, 0f, -11f), "crossbow");
            var thug = Enemy(new Vector3(0f, 0f, 2.6f), "thug");
            thug.Status.Apply(StatusType.Stunned, 3f);
            var shooter = Enemy(new Vector3(0f, 0f, -9f), "crossbowman", perch: true);
            StepUntilDuel();
            Loop.StepMany(3);
            Assert.AreEqual(CallumDance.WUnseenAssist, L.Offered, 1e-3f, "the shooter (a cheater) is offered; the thug is not");
            Use(0, shooter.Position);
            Loop.StepMany(30);
            Assert.Less(shooter.Health.Current, shooter.Health.Max);
            Assert.AreEqual(CallumDance.WUnseenAssist, L.Earned, 1e-3f, "disabled behind his back: unseen assist");
            Assert.AreEqual(0f, L.RawPenaltiesIn(1), 1e-3f);
        }

        [UnityTest]
        public IEnumerator A_Witnessed_Assist_Is_Caught_Not_Credited()
        {
            yield return Setup(new Vector3(4f, 0f, 2f), "crossbow");
            var thug = Enemy(new Vector3(0f, 0f, 2.6f), "thug");
            thug.Status.Apply(StatusType.Stunned, 3f);
            var shooter = Enemy(new Vector3(3f, 0f, 8f), "crossbowman", perch: true);
            StepUntilDuel();
            Use(0, shooter.Position);
            Loop.StepMany(30);
            Assert.Less(shooter.Health.Current, shooter.Health.Max);
            Assert.AreEqual(0f, L.Earned, 1e-3f);
            Assert.AreEqual(CallumDance.PCaught, L.RawPenaltiesIn(1), 1e-3f, "seen shooting from the shadows: Caught −3");
            Assert.Less(_cm.Honor, 100f);
        }

        [UnityTest]
        public IEnumerator Sanding_A_False_Surrender_Averts_The_Cheat()
        {
            yield return Setup(new Vector3(2.5f, 0f, -1.5f), "pocket_sand");
            var tc = Enemy(new Vector3(0f, 0f, 2.2f), "turncoat");
            tc.Status.Apply(StatusType.Stunned, 1.5f);
            StepUntilDuel();
            while (_cm.Saluting) Loop.Step();
            tc.TakeDamage(DamageInfo.Make(_hero, tc, 60f, DamageKind.Blade, "sword"));
            Assert.AreEqual(EnemyState.Surrendered, tc.State);
            Loop.Step();
            float hp = _hero.Health.Current;
            Use(0, tc.Position);
            Loop.StepMany(Mathf.CeilToInt(3.5f / SimLoop.Dt));
            Assert.AreEqual(hp, _hero.Health.Current, 0.01f, "no cheap shot landed");
            Assert.GreaterOrEqual(L.Earned, CallumDance.WAvertedCheat - 1e-3f, "averted cheat captured");
            Assert.AreEqual(CallumDance.PCaught, L.RawPenaltiesIn(1), 1e-3f, "...but sanding a yielded man in front of him is Caught");
        }

        [UnityTest]
        public IEnumerator Flushing_Out_A_Hedge_Ambusher_Averts_The_Ambush()
        {
            yield return Setup(new Vector3(3f, 0f, 5f), "pocket_sand");
            var amb = Enemy(new Vector3(4.6f, 0f, 11f), "ambusher", engage: false);
            yield return null; // Start() hides him
            Assert.IsTrue(amb.IsHidden);
            Loop.StepMany(2);
            Assert.AreEqual(CallumDance.WAvertedCheat, L.Offered, 1e-3f, "he's walking into an ambush: offered");
            Use(0, amb.Position);
            Loop.StepMany(30);
            Assert.IsFalse(amb.IsHidden);
            Assert.AreEqual("sidekick", amb.RevealedBy);
            Assert.AreEqual(CallumDance.WAvertedCheat, L.Earned, 1e-3f);
        }

        [UnityTest]
        public IEnumerator An_Ambush_That_Lands_Closes_The_Moment_Uncaptured()
        {
            yield return Setup(new Vector3(-3f, 0f, -6f));
            var amb = Enemy(new Vector3(4.6f, 0f, 11f), "ambusher", engage: false);
            yield return null; // Start() hides him
            Assert.IsTrue(amb.IsHidden);
            float hp = _hero.Health.Current;
            int guard = 0;
            while (amb.IsHidden && guard++ < 600) Loop.Step();
            Loop.StepMany(40);
            Assert.AreEqual("ambush", amb.RevealedBy);
            Assert.Less(_hero.Health.Current, hp, "the ambush blow landed");
            Assert.That(L.Log.Exists(e => e.Kind == "offer" && e.Id == "averted_cheat"), "offered");
            Assert.That(L.Log.Exists(e => e.Kind == "close" && e.Id == "averted_cheat"), "closed uncaptured (still counts as offered)");
            Assert.AreEqual(0f, L.Earned, 1e-3f);
        }

        [UnityTest]
        public IEnumerator Cover_Story_In_Time_Removes_The_Caught_Penalty()
        {
            yield return Setup(new Vector3(3f, 0f, 1f), "pocket_sand", "cover_story");
            var thug = Enemy(new Vector3(0f, 0f, 2.6f), "thug");
            StepUntilDuel();
            while (_cm.Saluting) Loop.Step();
            Use(0, thug.Position);
            Loop.StepMany(30);
            Assert.AreEqual(CallumDance.PCaught, L.RawPenaltiesIn(1), 1e-3f, "caught sanding his duel");
            Loop.StepMany(30);
            Use(1, _hero.Position);
            Loop.StepMany(5);
            Assert.AreEqual(0f, L.RawPenaltiesIn(1), 1e-3f, "covered: the penalty is removed");
            Assert.AreEqual(CallumDance.WCoveredLapse, L.Earned, 1e-3f, "covered lapse +2");
        }

        [UnityTest]
        public IEnumerator Honest_Misses_Cost_Nothing()
        {
            yield return Setup(new Vector3(2f, 0f, 1f), "pocket_sand", "crossbow");
            Loop.StepMany(2);
            Use(0, new Vector3(1f, 0f, 6f)); // sand on an empty road, right in front of him
            Loop.StepMany(30);
            Use(1, new Vector3(6f, 0f, 14f)); // a bolt into the trees
            Loop.StepMany(40);
            Assert.AreEqual(0f, L.RawPenaltiesIn(1), 1e-3f);
            Assert.AreEqual(100f, _cm.Honor, 1e-3f, "no victim, no dishonour");
        }

        [UnityTest]
        public IEnumerator Friendly_Fire_Over_Ten_Percent_Costs_Three_Once_Per_Fight()
        {
            yield return Setup(new Vector3(0f, 0f, -5f), "crossbow");
            Loop.StepMany(2);
            Use(0, new Vector3(0f, 0f, 10f)); // straight through him
            Loop.StepMany(30);
            Assert.Less(_hero.Health.Current, _hero.Health.Max, "the bolt hit him");
            Assert.AreEqual(CallumDance.PFriendlyFire, L.RawPenaltiesIn(1), 1e-3f);
        }

        [UnityTest]
        public IEnumerator Abandoning_Him_Under_Forty_Percent_Costs_Three()
        {
            yield return Setup(new Vector3(0f, 0f, -24f));
            Enemy(new Vector3(0f, 0f, 2.4f), "thug");
            _hero.Health.SetCurrent(_hero.Health.Max * 0.35f);
            StepUntilDuel();
            Loop.StepMany(Mathf.CeilToInt(2.4f / SimLoop.Dt));
            Assert.AreEqual(CallumDance.PAbandon, L.RawPenaltiesIn(1), 1e-3f);
        }

        [UnityTest]
        public IEnumerator Spoiling_The_Salute_Costs_Two()
        {
            yield return Setup(new Vector3(-1.5f, 0f, -3f), "crossbow");
            var thug = Enemy(new Vector3(0f, 0f, 3f), "thug");
            StepUntilDuel();
            Assert.IsTrue(_cm.Saluting);
            Use(0, thug.Position);
            Loop.StepMany(30);
            Assert.Less(thug.Health.Current, thug.Health.Max, "the bolt landed during the salute");
            Assert.GreaterOrEqual(L.RawPenaltiesIn(1), CallumDance.PSpoiledDuel - 1e-3f);
            Assert.That(L.Log.Exists(e => e.Kind == "penalty" && e.Id == "spoiled_duel"));
        }

        [UnityTest]
        public IEnumerator Caught_Twice_In_One_Fight_Costs_Three_Then_Six()
        {
            yield return Setup(new Vector3(3f, 0f, 1f), "pocket_sand");
            var thug = Enemy(new Vector3(0f, 0f, 2.6f), "thug");
            thug.Health.Invulnerable = true; // the same fight must still be going after the 8 s sand cooldown
            StepUntilDuel();
            while (_cm.Saluting) Loop.Step();
            Use(0, thug.Position);
            Loop.StepMany(Mathf.CeilToInt(8.2f / SimLoop.Dt)); // cooldown
            Use(0, thug.Position);
            Loop.StepMany(30);
            Assert.AreEqual(CallumDance.PCaught + CallumDance.PCaughtAgain, L.RawPenaltiesIn(1), 1e-3f);
        }

        [UnityTest]
        public IEnumerator When_The_Chapter_Budget_Is_Spent_Judging_Still_Works()
        {
            yield return Setup(new Vector3(2f, 0f, -11f), "crossbow", "pocket_sand");
            for (int i = 0; i < 15; i++) L.Offer("averted_cheat", 4f); // the chapter's 60 points are all on the table
            Assert.AreEqual(60f, L.Offered, 1e-3f);
            var thug = Enemy(new Vector3(0f, 0f, 2.6f), "thug");
            thug.Status.Apply(StatusType.Stunned, 3f);
            var shooter = Enemy(new Vector3(0f, 0f, -9f), "crossbowman", perch: true);
            StepUntilDuel();
            Loop.StepMany(3);
            Use(0, shooter.Position);   // a bolt on a cheater: no offer exists for him (budget spent)
            Loop.StepMany(30);
            Use(1, shooter.Position);   // and sand on him
            Loop.StepMany(30);
            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(60f, L.Offered, 1e-3f, "nothing more is offered past the budget");
        }
    }
}
