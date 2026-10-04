using System.Collections;
using System.Collections.Generic;
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
    /// <summary>Chapter 4's skills: Bait &amp; Switch, Smoke Bomb, Pep Talk, Shoulder Check.</summary>
    public class BastionSkillTests
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
            foreach (var c in Object.FindObjectsByType<SmokeCloud>(FindObjectsSortMode.None)) Object.DestroyImmediate(c.gameObject);
            if (ProjectileSystem.Instance) Object.DestroyImmediate(ProjectileSystem.Instance.gameObject);
            Object.Destroy(_ground);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject);
        }

        (SidekickAgent sk, SidekickSkills skills, ScriptedCommands cmd) Sidekick(Vector3 pos, string skill)
        {
            var sk = SidekickTests.Spawn<SidekickAgent>(pos, 0.32f);
            var skills = sk.gameObject.AddComponent<SidekickSkills>();
            var cmd = new ScriptedCommands();
            sk.Commands = cmd;
            Ctx.Sidekick = sk;
            skills.System.AtCamp = true;
            Assert.IsTrue(skills.Learn(skill), "learn " + skill);
            skills.System.AtCamp = false;
            return (sk, skills, cmd);
        }

        static EnemyAgent Thug(Vector3 pos, Vector3 facing)
        {
            var e = SidekickTests.Spawn<EnemyAgent>(pos);
            e.transform.rotation = Quaternion.LookRotation(facing);
            e.Archetype = "thug";
            e.Configure("thug");
            e.Activate();
            return e;
        }

        HeroAgent Callum(Vector3 pos)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Gameplay/Callum.prefab");
            var hero = Object.Instantiate(prefab, pos + Vector3.up * 0.05f, Quaternion.identity).GetComponent<HeroAgent>();
            hero.Route.SetNodes(new List<RouteNode>());
            Ctx.Hero = hero;
            return hero;
#else
            return null;
#endif
        }

        void Use(ScriptedCommands cmd, Vector3 aim)
        {
            cmd.Current = new SidekickCommand { Skill = 0, AimPoint = aim, HasAim = true };
            Loop.Step();
            cmd.Current = SidekickCommand.None;
        }

        void Seconds(float s) => Loop.StepMany(Mathf.CeilToInt(s / SimLoop.Dt));

        static SkillDefinition Def(string id) => SkillCatalog.Load().Get(id);

        [UnityTest]
        public IEnumerator Bait_And_Switch_Draws_Them_Off_But_Not_A_Man_In_A_Duel()
        {
            var (sk, skills, cmd) = Sidekick(Vector3.zero, "bait_and_switch");
            var loose = Thug(new Vector3(-2f, 0f, 9f), Vector3.back);
            var duellist = Thug(new Vector3(2f, 0f, 9f), Vector3.back);
            var opponent = Thug(new Vector3(2f, 0f, 11f), Vector3.forward);
            duellist.DuelOpponent = opponent;
            yield return null;
            Use(cmd, new Vector3(0f, 0f, 6f));
            Loop.Step();
            Assert.IsInstanceOf<Decoy>(loose.Target, "a decoy within reach draws him");
            Assert.AreSame(opponent, duellist.Target, "never a man in his duel");
            Seconds(Def("bait_and_switch").A(1) + 0.3f);
            Assert.IsNotInstanceOf<Decoy>(loose.Target, "the decoy is gone after its time");
        }

        [UnityTest]
        public IEnumerator Smoke_Bomb_Blocks_Every_Line_Of_Sight_And_Loses_Her()
        {
            var (sk, skills, cmd) = Sidekick(Vector3.zero, "smoke_bomb");
            var e = Thug(new Vector3(0f, 0f, 6f), Vector3.back);
            e.TakeDamage(DamageInfo.Make(sk, e, 1f, DamageKind.Knife, "knife"));
            Assert.IsTrue(e.AwareOfSidekick);
            yield return null;
            Use(cmd, new Vector3(0f, 0f, 5f));
            Seconds(0.4f);
            Assert.IsTrue(SmokeCloud.Blocks(sk.Position, e.Position + Vector3.forward * 2f), "nobody sees through it");
            Assert.IsFalse(e.AwareOfSidekick, "whoever is in it loses track of her");
            Seconds(Def("smoke_bomb").A(1) + 0.3f);
            Assert.IsFalse(SmokeCloud.Blocks(sk.Position, e.Position + Vector3.forward * 2f), "it clears");
        }

        [UnityTest]
        public IEnumerator Pep_Talk_Only_Within_Earshot()
        {
            var hero = Callum(new Vector3(0f, 0f, 20f));
            if (hero == null) yield break;
            var (sk, skills, cmd) = Sidekick(Vector3.zero, "pep_talk");
            yield return null;
            Use(cmd, hero.Position);
            Assert.IsFalse(hero.Inspired, "20 m: he can't hear her");
            hero.Motor.Teleport(new Vector3(0f, 0.05f, 6f));
            Use(cmd, hero.Position);
            Assert.IsTrue(hero.Inspired);
            var def = Def("pep_talk");
            Assert.AreEqual(def.A(1), hero.PepBonus, 1e-4f);
            Assert.AreEqual(def.B(1), hero.PepRemaining, 0.1f);
        }

        [UnityTest]
        public IEnumerator Shoulder_Check_Knocks_Back_And_Staggers()
        {
            var (sk, skills, cmd) = Sidekick(Vector3.zero, "shoulder_check");
            var e = Thug(new Vector3(0f, 0f, 2f), Vector3.back);
            yield return null;
            float z0 = e.Position.z;
            Use(cmd, e.Position);
            var def = Def("shoulder_check");
            Assert.IsTrue(e.Status.Has(StatusType.Staggered));
            Assert.AreEqual(def.B(1), e.Status.Remaining(StatusType.Staggered), 0.05f);
            Assert.Greater(e.Position.z - z0, def.A(1) - 0.5f, "shoved back");
        }
    }
}
