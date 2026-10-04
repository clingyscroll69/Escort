using System.Collections;
using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Flow;
using HS.Hero;
using HS.Hero.Callum;
using HS.Sidekick;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>Downed and Recall (GDD §4.2): learned at the chapter 2 campfire; how he comes back for you is his Stage's.</summary>
    public class RecallTests
    {
        GameObject _ground, _ctxGo;
        SimLoop Loop => SimLoop.Instance;
        RunContext Ctx => RunContext.Current;
        HeroAgent _hero;
        SidekickAgent _sk;
        RecallState _recall;

        [TearDown]
        public void TearDown()
        {
            if (_ground) Object.Destroy(_ground);
            TestUi.TearDownAll();
            HS.Presentation.ChapterTheme.ForgetSceneDefaults();
        }

        IEnumerator Pair(Stage stage)
        {
            _ground = SidekickTests.Ground();
            _ctxGo = new GameObject("RunContext");
            _ctxGo.AddComponent<RunContext>();
            SimLoop.Ensure().Paused = true;
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Gameplay/Callum.prefab");
            _hero = Object.Instantiate(prefab, new Vector3(0f, 0.05f, 0f), Quaternion.identity).GetComponent<HeroAgent>();
#endif
            Ctx.Hero = _hero;
            _sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(2f, 0f, 0f), 0.32f);
            _sk.Commands = new ScriptedCommands();
            Ctx.Sidekick = _sk;
            _recall = new RecallState { Learned = true };
            Ctx.Register(_recall);
            yield return null;
            _hero.ApplyStage(stage);
            _sk.CanBeDowned = true;
        }

        void KnockDown() => _sk.TakeDamage(DamageInfo.Make(null, _sk, 9999f, DamageKind.Melee, "test"));

        void Seconds(float s) => Loop.StepMany(Mathf.CeilToInt(s / SimLoop.Dt));

        [UnityTest]
        public IEnumerator Downed_Not_Dead_And_Nobody_Finishes_Her()
        {
            yield return Pair(Stage.S0);
            KnockDown();
            Assert.IsTrue(_sk.IsDowned);
            Assert.IsTrue(_sk.IsAlive, "down, not dead");
            Assert.AreEqual(0f, _sk.TakeDamage(DamageInfo.Make(null, _sk, 50f, DamageKind.Melee, "test")), "nobody finishes off the unlisted");
        }

        [UnityTest]
        public IEnumerator S2_He_Recalls_In_3_Seconds_Without_A_Wound()
        {
            yield return Pair(Stage.S2);
            KnockDown();
            Seconds(2.5f);
            Assert.IsTrue(_sk.IsDowned, "still channelling");
            Assert.AreEqual("callum_recall", _hero.ActiveRuleId);
            Seconds(1.4f);
            Assert.IsFalse(_sk.IsDowned, "up");
            Assert.AreEqual(_sk.Health.Max * 0.3f, _sk.Health.Current, 1f, "she rises at 30%");
            Assert.AreEqual(0, _hero.WoundCount, "S2: no wound");
            Assert.AreEqual(1, _recall.UsesLeft);
        }

        [UnityTest]
        public IEnumerator S0_He_Waits_For_A_Lull_And_Takes_A_Wound()
        {
            yield return Pair(Stage.S0);
            var bandit = SidekickTests.Spawn<EnemyAgent>(new Vector3(2f, 0f, 5f));
            bandit.Configure("thug");
            bandit.Activate();
            bandit.Status.Apply(StatusType.Stunned, 99f);
            KnockDown();
            Seconds(3f);
            Assert.AreNotEqual("callum_recall", _hero.ActiveRuleId, "a bandit within 10 m of her: S0 won't start");
            bandit.TakeDamage(DamageInfo.Make(_hero, bandit, 99999f, DamageKind.Blade, "sword"));
            Seconds(9.5f);
            Assert.IsFalse(_sk.IsDowned, "the lull came; 8 s later she's up");
            CollectionAssert.Contains(_hero.Wounds.All, WoundType.SwordArmStrain, "S0: it cost him");
        }

        [UnityTest]
        public IEnumerator No_Recalls_Left_Means_She_Stays_Down()
        {
            yield return Pair(Stage.S2);
            _recall.UsesLeft = 0;
            KnockDown();
            Seconds(5f);
            Assert.IsTrue(_sk.IsDowned, "two Recalls a chapter");
        }

        static GameFlow Flow(int startChapter)
        {
            new GameObject("RunContext").AddComponent<RunContext>();
            var go = new GameObject("GameFlow");
            go.SetActive(false);
            var flow = go.AddComponent<GameFlow>();
            flow.AutoPlay = true;
            flow.Fast = true;
            flow.StartChapter = startChapter;
            go.SetActive(true);
            return flow;
        }

        [UnityTest]
        public IEnumerator Before_The_Chapter_2_Campfire_Death_Ends_The_Run()
        {
            var flow = Flow(2);
            yield return null;
            Assert.IsFalse(flow.Recall.Learned);
            var sk = flow.Chapter.Sidekick;
            sk.TakeDamage(DamageInfo.Make(null, sk, 99999f, DamageKind.Melee, "test"));
            yield return TestUi.WaitUntil(() => flow.Current == GameFlow.State.End, 5f, "the end");
            Assert.AreEqual("sidekick_died", flow.Outcome);
        }

        [UnityTest]
        public IEnumerator After_It_Nobody_Comes_And_She_Wakes_At_The_Last_Door()
        {
            var flow = Flow(3);
            yield return null;
            Assert.IsTrue(flow.Recall.Learned, "learned at the chapter 2 campfire");
            foreach (var e in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None)) e.gameObject.SetActive(false);
            var hero = flow.Chapter.Hero;
            hero.Route.SetNodes(new List<RouteNode>());
            var sk = flow.Chapter.Sidekick;
            sk.Motor.Teleport(new Vector3(1f, 0.05f, 14f));
            flow.Recall.UsesLeft = 0;
            SimLoop.Instance.Paused = true;
            sk.TakeDamage(DamageInfo.Make(null, sk, 99999f, DamageKind.Melee, "test"));
            Assert.IsTrue(sk.IsDowned);
            SimLoop.Instance.StepMany(Mathf.CeilToInt((SidekickAgent.DownedTime + 0.5f) / SimLoop.Dt));
            Assert.IsFalse(sk.IsDowned, "back on her feet");
            Assert.Less(sk.Position.z, 3f, "at the room's entrance");
            Assert.AreEqual(GameFlow.State.Chapter, flow.Current, "the run goes on");
            CollectionAssert.Contains(hero.Wounds.All, WoundType.SwordArmStrain, "he fought on alone");
            SimLoop.Instance.Paused = false;
        }
    }
}
