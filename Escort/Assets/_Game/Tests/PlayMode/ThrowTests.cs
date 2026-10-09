using System.Collections;
using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Sidekick;
using HS.Skills;
using HS.Skills.Impl;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>Thrown things and volleys in three dimensions: up a level when nothing solid is in the way, range in a straight line.</summary>
    public class ThrowTests
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
            foreach (var c in Object.FindObjectsByType<SmokeCloud>(FindObjectsSortMode.None)) Object.DestroyImmediate(c.gameObject);
            if (ProjectileSystem.Instance) Object.DestroyImmediate(ProjectileSystem.Instance.gameObject);
            Object.Destroy(_ground);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject);
        }

        GameObject Box(Vector3 centre, Vector3 size)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.transform.position = centre;
            b.transform.localScale = size;
            _extra.Add(b);
            return b;
        }

        /// <summary>A 2.4 m square deck whose top is <paramref name="height"/> m up, centred over (x, z).</summary>
        void Perch(Vector3 at, float height = 2.2f) => Box(new Vector3(at.x, height - 0.15f, at.z), new Vector3(2.4f, 0.3f, 2.4f));

        static EnemyAgent Enemy(Vector3 at, string arch)
        {
            var e = SidekickTests.Spawn<EnemyAgent>(at);
            e.Archetype = arch;
            e.Configure(arch);
            e.Scripted = true; // dormant and still: it stands where it is put
            return e;
        }

        (SidekickAgent sk, SidekickSkills skills, ScriptedCommands cmd) Sidekick(Vector3 at, string skill, bool capstone = false)
        {
            var sk = SidekickTests.Spawn<SidekickAgent>(at, 0.32f);
            var skills = sk.gameObject.AddComponent<SidekickSkills>();
            var cmd = new ScriptedCommands();
            sk.Commands = cmd;
            Ctx.Sidekick = sk;
            var def = SkillCatalog.Load().Get(skill);
            Assert.IsTrue(capstone ? skills.System.LearnCapstone(def) : skills.Learn(skill), "learn " + skill);
            return (sk, skills, cmd);
        }

        void Use(ScriptedCommands cmd, int slot, Vector3 aim)
        {
            cmd.Current = new SidekickCommand { Skill = slot, AimPoint = aim, HasAim = true };
            Loop.Step();
            cmd.Current = SidekickCommand.None;
        }

        void Seconds(float s) => Loop.StepMany(Mathf.CeilToInt(s / SimLoop.Dt));

        [UnityTest]
        public IEnumerator Sand_Reaches_A_Perch_From_The_Side_But_Not_Through_Its_Deck()
        {
            Perch(new Vector3(0f, 0f, 6f));
            var archer = Enemy(new Vector3(0f, 2.2f, 6f), "archer");
            var (sk, skills, cmd) = Sidekick(new Vector3(0f, 0f, 5.6f), "pocket_sand");
            yield return null;
            Use(cmd, 0, new Vector3(0f, 0f, 6f));
            Seconds(0.5f);
            Assert.IsFalse(archer.Status.Has(StatusType.Blinded), "from beneath, the deck is in the way");

            sk.Motor.Teleport(new Vector3(-5.5f, 0.05f, 6f));
            skills.System.Get("pocket_sand").CooldownRemaining = 0f;
            Loop.Step();
            Use(cmd, 0, new Vector3(0f, 0f, 6f));
            Seconds(0.5f);
            Assert.IsTrue(archer.Status.Has(StatusType.Blinded), "from the side, a clear throw up a level");
        }

        [UnityTest]
        public IEnumerator Sand_Range_Is_A_Straight_Line_From_Her_Hand()
        {
            // A man 10 m up and 7.5 m away along the floor: within 8 m on the ground plane, 12.2 m in a straight line. The
            // pouch comes down 3.4 m short of his feet, with nothing in its way.
            Perch(new Vector3(0f, 0f, 7.5f), 10f);
            var high = Enemy(new Vector3(0f, 10f, 7.5f), "archer");
            var (sk, skills, cmd) = Sidekick(Vector3.zero, "pocket_sand");
            yield return null;
            Use(cmd, 0, new Vector3(0f, 0f, 7.5f));
            Seconds(0.5f);
            Assert.IsFalse(high.Status.Has(StatusType.Blinded), "out of reach: it falls short");

            Perch(new Vector3(-3f, 0f, 6f));
            var low = Enemy(new Vector3(-3f, 2.2f, 6f), "archer");
            skills.System.Get("pocket_sand").CooldownRemaining = 0f;
            Loop.Step();
            Use(cmd, 0, new Vector3(-3f, 0f, 6f));
            Seconds(0.5f);
            Assert.IsTrue(low.Status.Has(StatusType.Blinded), "6.7 m along the floor, 7 m in a straight line: in reach");
        }

        [UnityTest]
        public IEnumerator A_Wall_Stops_A_Throw_And_Shelters_Whoever_Is_Behind_It()
        {
            Box(new Vector3(0f, 1.5f, 3f), new Vector3(6f, 3f, 0.3f));
            var thug = Enemy(new Vector3(0f, 0f, 4.2f), "thug");
            var (sk, skills, cmd) = Sidekick(Vector3.zero, "pocket_sand");
            int dirty = 0;
            Ctx.Events.Sabotage += e => dirty++;
            yield return null;
            Use(cmd, 0, thug.Position);
            Seconds(0.5f);
            Assert.IsFalse(thug.Status.Has(StatusType.Blinded), "the pouch bursts against the wall; he is behind it");
            Assert.AreEqual(0, dirty, "sand that reached nobody is a miss, not a dirty trick");
        }

        [UnityTest]
        public IEnumerator Smoke_Is_A_Column_On_The_Floor_It_Lands_On()
        {
            yield return null;
            var low = SmokeCloud.Release(Vector3.zero, 3f, 30f, Ctx);
            Assert.IsTrue(SmokeCloud.Blocks(new Vector3(-5f, 1.6f, 0f), new Vector3(5f, 1.6f, 0f)), "through it");
            Assert.IsFalse(SmokeCloud.Blocks(new Vector3(-6f, 3.85f, 0f), new Vector3(6f, 3.85f, 0f)), "over it, perch to perch");
            Assert.IsTrue(low.Contains(new Vector3(1f, 1f, 0f)));
            Assert.IsFalse(low.Contains(new Vector3(1f, 4f, 0f)), "above it");
            SmokeCloud.Release(new Vector3(20f, 2.2f, 0f), 3f, 30f, Ctx);
            Assert.IsFalse(SmokeCloud.Blocks(new Vector3(15f, 1.6f, 0f), new Vector3(25f, 1.6f, 0f)), "under a cloud on a deck");
            Assert.IsTrue(SmokeCloud.Blocks(new Vector3(15f, 3.85f, 0f), new Vector3(25f, 3.85f, 0f)), "through a cloud on a deck");
        }

        [UnityTest]
        public IEnumerator Smoke_Thrown_At_A_Man_On_A_Perch_Stands_On_His_Deck()
        {
            Perch(new Vector3(0f, 0f, 6f));
            Enemy(new Vector3(0f, 2.2f, 6f), "archer");
            var (sk, skills, cmd) = Sidekick(new Vector3(-5.5f, 0f, 6f), "smoke_bomb");
            yield return null;
            Use(cmd, 0, new Vector3(0f, 0f, 6f));
            Seconds(0.5f);
            Assert.AreEqual(1, SmokeCloud.All.Count);
            Assert.AreEqual(2.2f, SmokeCloud.All[0].transform.position.y, 0.05f, "on the deck, not the floor below");
        }

        [UnityTest]
        public IEnumerator Crossfire_Reaches_A_Perch_From_The_Side_But_Not_Through_Its_Deck()
        {
            Perch(new Vector3(0f, 0f, 6f));
            var archer = Enemy(new Vector3(0f, 2.2f, 6f), "archer");
            var (sk, skills, cmd) = Sidekick(new Vector3(0f, 0f, 5.6f), "crossfire", capstone: true);
            yield return null;
            Use(cmd, SkillSystem.CapstoneSlot, new Vector3(0f, 0f, 6f));
            Seconds(1f);
            Assert.AreEqual(archer.Health.Max, archer.Health.Current, 0.01f, "from beneath, the deck takes the volley");

            sk.Motor.Teleport(new Vector3(-6f, 0.05f, 6f));
            skills.System.Capstone.CooldownRemaining = 0f;
            Loop.Step();
            Use(cmd, SkillSystem.CapstoneSlot, new Vector3(0f, 0f, 6f));
            Seconds(1f);
            Assert.Less(archer.Health.Current, archer.Health.Max, "from the side, the volley goes up to him");
        }
    }
}
