using System.Collections;
using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Rooms;
using HS.Sidekick;
using HS.Skills;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>Chapter 3's skills: Read Runes, Lockpick, Map Sketch, Buckler.</summary>
    public class CatacombSkillTests
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
            if (HS.Skills.Impl.PathSketch.Current) Object.DestroyImmediate(HS.Skills.Impl.PathSketch.Current.gameObject);
            Object.Destroy(_ground);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject);
        }

        (SidekickAgent sk, SidekickSkills skills, ScriptedCommands cmd) Sidekick(Vector3 pos, string skill, int rank = 1)
        {
            var sk = SidekickTests.Spawn<SidekickAgent>(pos, 0.32f);
            var skills = sk.gameObject.AddComponent<SidekickSkills>();
            var cmd = new ScriptedCommands();
            sk.Commands = cmd;
            Ctx.Sidekick = sk;
            for (int r = 0; r < rank; r++)
            {
                skills.System.AtCamp = true;
                Assert.IsTrue(skills.Learn(skill), "learn " + skill);
                skills.System.AtCamp = false;
            }
            return (sk, skills, cmd);
        }

        void Use(ScriptedCommands cmd, Vector3? aim = null)
        {
            cmd.Current = new SidekickCommand { Skill = 0, AimPoint = aim ?? Vector3.zero, HasAim = aim.HasValue };
            Loop.Step();
            cmd.Current = SidekickCommand.None;
        }

        void Seconds(float s) => Loop.StepMany(Mathf.CeilToInt(s / SimLoop.Dt));

        SealDoor Seal(Vector3 pos, SealKind kind)
        {
            var go = new GameObject("Seal");
            go.transform.position = pos;
            var s = go.AddComponent<SealDoor>();
            s.Kind = kind;
            _extra.Add(go);
            return s;
        }

        [UnityTest]
        public IEnumerator Read_Runes_Opens_A_Rune_Seal_But_Not_A_Gate()
        {
            var (sk, skills, cmd) = Sidekick(Vector3.zero, "read_runes");
            var gate = Seal(new Vector3(0f, 0f, 2f), SealKind.Gate);
            yield return null;
            Use(cmd);
            Assert.IsFalse(sk.IsChanneling, "a gate needs a pick");
            Object.DestroyImmediate(gate.gameObject);
            var rune = Seal(new Vector3(0f, 0f, 2f), SealKind.Rune);
            yield return null;
            Use(cmd);
            Assert.IsTrue(sk.IsChanneling);
            Seconds(2.2f);
            Assert.IsTrue(rune.IsOpen);
            Assert.AreEqual("runes", rune.OpenedBy);
        }

        [UnityTest]
        public IEnumerator Lockpick_Opens_A_Gate_And_At_Rank_2_Cuts_A_Trap_From_A_Step_Away()
        {
            var (sk, skills, cmd) = Sidekick(Vector3.zero, "lockpick", 2);
            var gate = Seal(new Vector3(0f, 0f, 2f), SealKind.Gate);
            yield return null;
            Use(cmd);
            Seconds(2.2f);
            Assert.IsTrue(gate.IsOpen, "rank 2: a 2 s pick");
            Object.DestroyImmediate(gate.gameObject);
            var go = new GameObject("Plate");
            go.transform.position = new Vector3(2f, 0f, 0f);
            var plate = go.AddComponent<HazardMarker>();
            _extra.Add(go);
            yield return null;
            skills.System.Get("lockpick").CooldownRemaining = 0f;
            Use(cmd);
            Assert.AreEqual(HazardMarker.HazardState.Disarmed, plate.State);
        }

        [UnityTest]
        public IEnumerator Map_Sketch_Finds_Hidden_Plates_And_Shows_His_Path()
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Gameplay/Callum.prefab");
            var hero = Object.Instantiate(prefab, new Vector3(0f, 0.05f, -5f), Quaternion.identity).GetComponent<HeroAgent>();
            Ctx.Hero = hero;
#endif
            var (sk, skills, cmd) = Sidekick(Vector3.zero, "map_sketch");
            var go = new GameObject("Plate");
            go.transform.position = new Vector3(0f, 0f, 20f);
            var plate = go.AddComponent<HazardMarker>();
            plate.Hidden = true;
            _extra.Add(go);
            yield return null;
            Use(cmd);
            Assert.IsTrue(plate.Revealed, "20 m away, found on the sketch");
            Assert.IsTrue(HS.Skills.Impl.PathSketch.Current != null && HS.Skills.Impl.PathSketch.Current.Showing);
        }

        [UnityTest]
        public IEnumerator Buckler_Parries_A_Blow_And_At_Rank_2_Catches_A_Bolt_Meant_For_Him()
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Gameplay/Callum.prefab");
            var hero = Object.Instantiate(prefab, new Vector3(1.2f, 0.05f, 0f), Quaternion.identity).GetComponent<HeroAgent>();
            Ctx.Hero = hero;
#endif
            var (sk, skills, cmd) = Sidekick(Vector3.zero, "buckler", 2);
            var thug = SidekickTests.Spawn<EnemyAgent>(new Vector3(0f, 0f, 1.5f));
            thug.Configure("thug");
            yield return null;
            Use(cmd, thug.Position);
            Assert.IsTrue(sk.Blocking);
            float hp = sk.Health.Current;
            sk.TakeDamage(DamageInfo.Make(thug, sk, 11f, DamageKind.Melee, "melee"));
            Assert.AreEqual(hp, sk.Health.Current, 0.01f, "glances off");
            Assert.IsTrue(thug.Status.Has(StatusType.Staggered), "and he reels");
            float heroHp = Ctx.Hero.Health.Current;
            ProjectileSystem.Ensure().Fire(new ProjectileSpec
            {
                Owner = thug, Origin = Ctx.Hero.Position + new Vector3(0f, 1.2f, 8f), Direction = Vector3.back, Speed = 40f,
                Damage = 30f, Kind = DamageKind.Ranged, Tag = "bolt", MaxRange = 20f, HitsFaction = a => a != thug,
            });
            Seconds(0.4f);
            Assert.AreEqual(heroHp, Ctx.Hero.Health.Current, 0.01f, "rank 2: the bolt meant for him hits her shield");
        }
    }
}
