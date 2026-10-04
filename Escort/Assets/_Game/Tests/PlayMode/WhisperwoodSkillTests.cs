using System.Collections;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Sidekick;
using HS.Skills;
using HS.Skills.Impl;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>Chapter 2's skills: Splint &amp; Stitch, Pull Back, Sling, Read the Room.</summary>
    public class WhisperwoodSkillTests
    {
        GameObject _ground, _ctxGo;
        SimLoop Loop => SimLoop.Instance;
        RunContext Ctx => RunContext.Current;

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
            foreach (var a in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None)) Object.DestroyImmediate(a.gameObject);
            if (ProjectileSystem.Instance) Object.DestroyImmediate(ProjectileSystem.Instance.gameObject);
            Object.Destroy(_ground);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject);
        }

        HeroAgent Callum(Vector3 pos)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Gameplay/Callum.prefab");
            var hero = Object.Instantiate(prefab, pos + Vector3.up * 0.05f, Quaternion.identity).GetComponent<HeroAgent>();
            Ctx.Hero = hero;
            return hero;
#else
            return null;
#endif
        }

        (SidekickAgent sk, SidekickSkills skills, ScriptedCommands cmd) Sidekick(Vector3 pos, string skill)
        {
            var sk = SidekickTests.Spawn<SidekickAgent>(pos, 0.32f);
            var skills = sk.gameObject.AddComponent<SidekickSkills>();
            var cmd = new ScriptedCommands();
            sk.Commands = cmd;
            Ctx.Sidekick = sk;
            Assert.IsTrue(skills.Learn(skill), "learn " + skill);
            return (sk, skills, cmd);
        }

        void Use(ScriptedCommands cmd, Vector3? aim = null)
        {
            cmd.Current = new SidekickCommand { Skill = 0, AimPoint = aim ?? Vector3.zero, HasAim = aim.HasValue };
            Loop.Step();
            cmd.Current = SidekickCommand.None;
        }

        void Seconds(float s) => Loop.StepMany(Mathf.CeilToInt(s / SimLoop.Dt));

        [UnityTest]
        public IEnumerator Splint_And_Stitch_Treats_A_Serious_Wound()
        {
            var hero = Callum(Vector3.zero);
            var (sk, skills, cmd) = Sidekick(new Vector3(1.2f, 0f, 0f), "splint_and_stitch");
            yield return null;
            hero.Route.SetNodes(new System.Collections.Generic.List<RouteNode>());
            hero.Wounds.Add(WoundType.CrackedRibs);
            hero.Wounds.Add(WoundType.SprainedAnkle);
            int treated = 0;
            Ctx.Events.WoundTreated += (a, b) => treated++;
            Use(cmd);
            Assert.IsTrue(sk.IsChanneling, "a long kneel");
            Seconds(6.2f);
            CollectionAssert.AreEqual(new[] { WoundType.SprainedAnkle }, hero.Wounds.All, "the serious one is set");
            Assert.AreEqual(1, treated);
            hero.Wounds.Clear();
            hero.Wounds.Add(WoundType.SprainedAnkle);
            var def = skills.System.Get("splint_and_stitch");
            def.CooldownRemaining = 0f;
            Use(cmd);
            Assert.IsFalse(sk.IsChanneling, "a sprain isn't its job");
        }

        [UnityTest]
        public IEnumerator Pull_Back_Yanks_Him_Out_Of_A_Trip()
        {
            var hero = Callum(new Vector3(0f, 0f, 8f));
            var (sk, skills, cmd) = Sidekick(Vector3.zero, "pull_back");
            yield return null;
            hero.Route.SetNodes(new System.Collections.Generic.List<RouteNode>());
            hero.Status.Apply(StatusType.Staggered, 2f);
            Use(cmd);
            Assert.AreEqual(4f, hero.Position.z, 0.3f, "hauled 4 m toward her");
            Assert.IsFalse(hero.Status.Has(StatusType.Staggered), "the trip is shaken off");
        }

        [UnityTest]
        public IEnumerator Sling_Staggers_And_Scales_With_The_Chapter()
        {
            Ctx.Chapter = 2;
            var (sk, skills, cmd) = Sidekick(Vector3.zero, "sling");
            var thug = SidekickTests.Spawn<EnemyAgent>(new Vector3(0f, 0f, 8f));
            thug.Configure("thug");
            yield return null;
            float hp = thug.Health.Current;
            bool staggered = false;
            Ctx.Events.Damage += (d, a) => { if (d.Target == thug && d.Tag == "sling") staggered = d.Stagger > 0f; };
            Use(cmd, thug.Position);
            Seconds(1f);
            Assert.AreEqual(9f * 2.5f, hp - thug.Health.Current, 0.01f, "9 x the chapter 2 tier");
            Assert.IsTrue(staggered);
        }

        [UnityTest]
        public IEnumerator Read_The_Room_Marks_Intent_And_Hidden_Foes_Without_Springing_Them()
        {
            var (sk, skills, cmd) = Sidekick(Vector3.zero, "read_the_room");
            var hidden = SidekickTests.Spawn<EnemyAgent>(new Vector3(-3f, 0f, 10f));
            hidden.Archetype = "ambusher";
            hidden.Configure("ambusher");
            var turncoat = SidekickTests.Spawn<EnemyAgent>(new Vector3(3f, 0f, 8f));
            turncoat.Configure("turncoat");
            turncoat.Activate();
            var far = SidekickTests.Spawn<EnemyAgent>(new Vector3(0f, 0f, 40f));
            far.Configure("thug");
            yield return null;
            Assert.IsTrue(hidden.IsHidden);
            Use(cmd);
            var read = ReadTheRoom.Get(Ctx);
            Assert.IsNotNull(read);
            Assert.IsTrue(read.Active(Ctx));
            Assert.IsTrue(read.Covers(Ctx, hidden.Position));
            Assert.IsFalse(read.Covers(Ctx, far.Position), "only within its radius");
            Assert.AreEqual("AMBUSH", ReadTheRoom.IntentOf(hidden));
            Assert.AreEqual("WILL FEIGN SURRENDER", ReadTheRoom.IntentOf(turncoat));
            Assert.IsTrue(hidden.IsHidden, "marked, not sprung");
            Seconds(6.2f);
            Assert.IsFalse(read.Active(Ctx), "6 s at rank 1");
        }
    }
}
