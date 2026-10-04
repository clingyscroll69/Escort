using System.Collections;
using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Hero.Callum;
using HS.Rooms;
using HS.Sidekick;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>Chapter 4's pressures: the sluice (a split threat), hostages, challenge-baiters.</summary>
    public class BastionTests
    {
        GameObject _ground, _ctxGo;
        readonly List<GameObject> _extra = new List<GameObject>();
        SimLoop Loop => SimLoop.Instance;
        RunContext Ctx => RunContext.Current;

        [SetUp]
        public void SetUp()
        {
            _ground = SidekickTests.Ground();
            _ctxGo = new GameObject("RunContext");
            _ctxGo.AddComponent<RunContext>();
            SimLoop.Ensure().Paused = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var g in _extra) if (g) Object.DestroyImmediate(g);
            _extra.Clear();
            foreach (var a in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None)) Object.DestroyImmediate(a.gameObject);
            Object.Destroy(_ground);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject);
        }

        void Seconds(float s) => Loop.StepMany(Mathf.CeilToInt(s / SimLoop.Dt));

        static EnemyAgent Enemy(Vector3 pos, string arch, Vector3 facing)
        {
            var e = SidekickTests.Spawn<EnemyAgent>(pos);
            e.transform.rotation = Quaternion.LookRotation(facing);
            e.Archetype = arch;
            e.Configure(arch);
            e.Activate();
            return e;
        }

        SluiceWheel Wheel(Vector3 at, float workSeconds)
        {
            var go = new GameObject("SluiceWheel");
            go.transform.position = at;
            var w = go.AddComponent<SluiceWheel>();
            w.WorkSeconds = workSeconds;
            _extra.Add(go);
            return w;
        }

        [UnityTest]
        public IEnumerator A_Crew_Opens_The_Sluice_Unless_The_Wheel_Is_Jammed()
        {
            var w = Wheel(new Vector3(0f, 0f, 10f), 2f);
            var crew = Enemy(new Vector3(1f, 0f, 10f), "sluice_crew", Vector3.left);
            yield return null;
            Seconds(0.2f);
            Assert.IsNotNull(crew.Brain, "recruited to the wheel");
            Assert.IsTrue(crew.Unchallengeable, "out of his reach while he works it");
            Seconds(3f);
            Assert.IsTrue(w.Flooded, "worked to the end, it opens");

            var w2 = Wheel(new Vector3(30f, 0f, 10f), 2f);
            Enemy(new Vector3(31f, 0f, 10f), "sluice_crew", Vector3.left);
            yield return null;
            Seconds(0.6f);
            var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(30f, 0f, 8f), 0.32f);
            sk.Commands = new ScriptedCommands();
            Ctx.Sidekick = sk;
            Assert.IsTrue(w2.CanInteract(sk));
            w2.Interact(sk);
            float progress = w2.Progress;
            Seconds(3f);
            Assert.IsTrue(w2.Jammed);
            Assert.IsFalse(w2.Flooded, "a jammed wheel never opens");
            Assert.AreEqual(progress, w2.Progress, 1e-4f);
        }

        [UnityTest]
        public IEnumerator A_Hostage_Shields_Her_Captor_From_His_Challenge_Until_She_Is_Untied()
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Gameplay/Callum.prefab");
            var hero = Object.Instantiate(prefab, new Vector3(0f, 0.05f, 0f), Quaternion.identity).GetComponent<HeroAgent>();
            Ctx.Hero = hero;
            var cm = hero.GetComponent<CallumModule>();
            var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(-2f, 0f, 6f), 0.32f);
            sk.Commands = new ScriptedCommands();
            Ctx.Sidekick = sk;
            var archer = Enemy(new Vector3(0f, 0f, 9f), "bastion_archer", Vector3.back);
            var hostage = SidekickTests.Spawn<Hostage>(new Vector3(0f, 0f, 7.6f), 0.32f);
            var thug = Enemy(new Vector3(5f, 0f, 12f), "thug", Vector3.back);
            yield return null;
            Seconds(0.3f);
            Assert.AreSame(archer, hostage.Captor);
            Assert.IsTrue(hostage.Held);
            Assert.IsTrue(Hostage.Shields(archer));
            Assert.AreSame(thug, cm.PickChallengeTarget(), "a knight does not strike through a hostage");
            int released = 0;
            Hostage.Released += (h, by, how) => { if (how == "untied") released++; };
            Assert.IsTrue(hostage.CanInteract(sk));
            hostage.Interact(sk);
            Assert.AreEqual(1, released);
            Assert.IsFalse(Hostage.Shields(archer), "untied: fair game again");
            Assert.IsFalse(archer.HoldsPosition);
#else
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator A_Baiter_Takes_The_Challenge_Then_Gives_Ground()
        {
            var opponent = SidekickTests.Spawn<EnemyAgent>(new Vector3(0f, 0f, 0f));
            opponent.Archetype = "thug";
            opponent.Configure("thug");
            var baiter = Enemy(new Vector3(0f, 0f, 2.5f), "baiter", Vector3.back);
            baiter.DuelOpponent = opponent;
            yield return null;
            Assert.IsTrue(baiter.Baiting);
            float z0 = baiter.Position.z;
            Seconds(1.5f);
            Assert.Greater(baiter.Position.z, z0 + 0.5f, "he backs away instead of fighting");
            Seconds(EnemyAgent.BaitTime);
            Assert.IsFalse(baiter.Baiting, "then he fights");
        }
    }
}
