using System.Collections;
using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Rooms;
using HS.Sidekick;
using HS.Skills;
using HS.Skills.Impl;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>The capstones (revealed at the chapter 4 campfire): their normal uses, on a key of their own.</summary>
    public class CapstoneTests
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
            ProjectileSystem.Ensure();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var g in _extra) if (g) Object.DestroyImmediate(g);
            _extra.Clear();
            foreach (var a in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None)) Object.DestroyImmediate(a.gameObject);
            if (ProjectileSystem.Instance) Object.DestroyImmediate(ProjectileSystem.Instance.gameObject);
            Object.Destroy(_ground);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject);
        }

        (SidekickAgent sk, SidekickSkills skills, ScriptedCommands cmd) Sidekick(Vector3 pos, string capstone)
        {
            var sk = SidekickTests.Spawn<SidekickAgent>(pos, 0.32f);
            var skills = sk.gameObject.AddComponent<SidekickSkills>();
            var cmd = new ScriptedCommands();
            sk.Commands = cmd;
            Ctx.Sidekick = sk;
            var def = SkillCatalog.Load().Get(capstone);
            Assert.IsFalse(skills.System.Learn(def), "a capstone is never learned like a trick");
            Assert.IsTrue(skills.System.LearnCapstone(def));
            Assert.IsFalse(skills.System.LearnCapstone(SkillCatalog.Load().Get(capstone == "crossfire" ? "hold_please" : "crossfire")), "one per run");
            return (sk, skills, cmd);
        }

        static EnemyAgent Thug(Vector3 pos)
        {
            var e = SidekickTests.Spawn<EnemyAgent>(pos);
            e.transform.rotation = Quaternion.LookRotation(Vector3.back);
            e.Archetype = "thug";
            e.Configure("thug");
            e.Activate();
            return e;
        }

        void Capstone(ScriptedCommands cmd, Vector3 aim)
        {
            cmd.Current = new SidekickCommand { Skill = SkillSystem.CapstoneSlot, AimPoint = aim, HasAim = true };
            Loop.Step();
            cmd.Current = SidekickCommand.None;
        }

        void Seconds(float s) => Loop.StepMany(Mathf.CeilToInt(s / SimLoop.Dt));

        ArmableProp Prop(Vector3 at)
        {
            var go = new GameObject("Prop");
            go.transform.position = at;
            var anchor = go.AddComponent<ArmableAnchor>();
            anchor.Kind = ArmableKind.BarrelStack;
            var p = go.AddComponent<ArmableProp>();
            _extra.Add(go);
            return p;
        }

        [UnityTest]
        public IEnumerator Hold_Please_Holds_Everyone_Within_40m_But_Her()
        {
            var (sk, skills, cmd) = Sidekick(Vector3.zero, "hold_please");
            var near = Thug(new Vector3(0f, 0f, 6f));
            var far = Thug(new Vector3(0f, 0f, 45f));
            yield return null;
            Capstone(cmd, near.Position);
            float hold = SkillCatalog.Load().Get("hold_please").A(1);
            Assert.AreEqual(hold, near.Status.Remaining(StatusType.Stunned), 0.05f);
            Assert.IsFalse(far.Status.Has(StatusType.Stunned), "40 m, no further");
            Assert.IsFalse(sk.Status.Has(StatusType.Stunned), "everyone but her");
            Assert.Greater(skills.System.Capstone.CooldownRemaining, 0f);
            Assert.AreEqual(SkillSystem.CapstoneSlot, 6);
            CollectionAssert.DoesNotContain(skills.System.Loadout, "hold_please", "it never takes a slot");
        }

        [UnityTest]
        public IEnumerator Domino_Effect_Arms_Two_Idle_Props_And_Brings_Them_Down()
        {
            var (sk, skills, cmd) = Sidekick(Vector3.zero, "domino_effect");
            var a = Prop(new Vector3(3f, 0f, 0f));
            var b = Prop(new Vector3(-3f, 0f, 0f));
            var c = Prop(new Vector3(0f, 0f, 9f));
            yield return null;
            Capstone(cmd, Vector3.forward);
            Assert.AreEqual(ArmableProp.PropState.Falling, a.State);
            Assert.AreEqual(ArmableProp.PropState.Falling, b.State);
            Assert.AreEqual(ArmableProp.PropState.Idle, c.State, "two armed, no more");
        }

        [UnityTest]
        public IEnumerator Crossfire_Bolts_Grow_With_Her_Fighting_Ranks()
        {
            var (sk, skills, cmd) = Sidekick(Vector3.zero, "crossfire");
            Assert.AreEqual(30f, CapstoneRules.CrossfireBolt(skills.System), 1e-4f, "the knife counts one");
            skills.System.AtCamp = true;
            Assert.IsTrue(skills.Learn("shoulder_check"));
            skills.System.AtCamp = false;
            Assert.AreEqual(40f, CapstoneRules.CrossfireBolt(skills.System), 1e-4f, "each rank of a fighting trick, ten more");
            var e = Thug(new Vector3(0f, 0f, 5f));
            yield return null;
            float hp = e.Health.Current;
            Capstone(cmd, e.Position);
            Seconds(0.8f);
            Assert.GreaterOrEqual(hp - e.Health.Current, 40f - 0.01f, "at least one bolt lands");
        }

        [UnityTest]
        public IEnumerator Silent_Partner_Mends_Him_Near_Her_And_Its_Key_Does_Nothing()
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Gameplay/Callum.prefab");
            var hero = Object.Instantiate(prefab, new Vector3(0f, 0.05f, 25f), Quaternion.identity).GetComponent<HeroAgent>();
            Ctx.Hero = hero;
            var (sk, skills, cmd) = Sidekick(Vector3.zero, "silent_partner");
            yield return null;
            hero.Route.SetNodes(new List<RouteNode>());
            hero.Health.SetCurrent(hero.Health.Max * 0.4f);
            float h0 = hero.Health.Current;
            Seconds(5f);
            float apart = hero.Health.Current - h0;
            hero.Motor.Teleport(new Vector3(0f, 0.05f, 5f));
            h0 = hero.Health.Current;
            Seconds(5f);
            float near = hero.Health.Current - h0;
            Assert.AreEqual(CapstoneRules.PartnerRegen * 5f * hero.Health.Max, near - apart, hero.Health.Max * 0.004f, "0.6% a second, near her");
            Capstone(cmd, hero.Position);
            Assert.AreEqual(0f, skills.System.Capstone.CooldownRemaining, "a passive: the key does nothing");
#else
            yield break;
#endif
        }
    }
}
