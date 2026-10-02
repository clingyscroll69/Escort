using System.Collections;
using HS.Core;
using HS.Sidekick;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>Hostile target for tests.</summary>
    public sealed class DummyHostile : Agent
    {
        public override Faction Faction => Faction.Hostile;
        protected override void Awake()
        {
            base.Awake();
            Health = new Health(100f);
        }
        protected override void OnSimTick(float dt) => Motor.Move(Vector3.zero, 50f, dt);
    }

    public class SidekickTests
    {
        GameObject _ground, _ctxGo;
        SimLoop Loop => SimLoop.Instance;

        public static GameObject Ground()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Plane);
            g.transform.localScale = new Vector3(20f, 1f, 20f);
            return g;
        }

        [SetUp]
        public void SetUp()
        {
            _ground = Ground();
            _ctxGo = new GameObject("RunContext");
            _ctxGo.AddComponent<RunContext>();
            SimLoop.Ensure().Paused = true; // tests step the clock manually
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var a in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None)) Object.DestroyImmediate(a.gameObject);
            Object.Destroy(_ground);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject); // deferred Destroy lets the next test register into a dying loop
        }

        public static T Spawn<T>(Vector3 pos, float radius = 0.35f) where T : Agent
        {
            var go = new GameObject(typeof(T).Name);
            go.transform.position = pos + Vector3.up * 0.05f;
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.7f;
            cc.radius = radius;
            cc.center = new Vector3(0f, 0.85f, 0f);
            return go.AddComponent<T>();
        }

        [UnityTest]
        public IEnumerator Moves_At_Jog_Speed()
        {
            var s = Spawn<SidekickAgent>(Vector3.zero);
            var cmd = new ScriptedCommands { Current = new SidekickCommand { Move = Vector3.forward, Skill = -1 } };
            s.Commands = cmd;
            yield return null;
            Loop.StepMany(30); // accelerate
            float z0 = s.Position.z;
            Loop.StepMany(60); // 1 s
            float v = s.Position.z - z0;
            Assert.That(v, Is.InRange(6f * 0.95f, 6f * 1.05f), "jog speed m/s");
        }

        [UnityTest]
        public IEnumerator Dodge_Uses_Three_Charges_And_Recharges()
        {
            var s = Spawn<SidekickAgent>(Vector3.zero);
            var cmd = new ScriptedCommands();
            s.Commands = cmd;
            yield return null;
            for (int i = 0; i < 3; i++)
            {
                cmd.Current = new SidekickCommand { Dodge = true, Move = Vector3.right, Skill = -1 };
                Loop.StepMany(30);
            }
            Assert.AreEqual(0, s.Dodge.Charges, "three dodges spend three charges");
            cmd.Current = new SidekickCommand { Dodge = true, Skill = -1 };
            Loop.Step();
            Assert.IsFalse(s.IsDodging, "no charge left → no dodge");
            Loop.StepMany(Mathf.CeilToInt(1.85f / SimLoop.Dt));
            Assert.GreaterOrEqual(s.Dodge.Charges, 1, "a charge refills after 1.8 s");
        }

        [UnityTest]
        public IEnumerator Dodge_IFrames_Block_Damage_For_0_3s()
        {
            var s = Spawn<SidekickAgent>(Vector3.zero);
            var cmd = new ScriptedCommands { Current = new SidekickCommand { Dodge = true, Move = Vector3.forward, Skill = -1 } };
            s.Commands = cmd;
            yield return null;
            Loop.Step();
            Loop.StepMany(10); // 0.18 s into the roll
            float hp = s.Health.Current;
            s.TakeDamage(DamageInfo.Make(null, s, 20f, DamageKind.Melee, "test"));
            Assert.AreEqual(hp, s.Health.Current, "invulnerable during i-frames");
            Loop.StepMany(12); // 0.38 s: past i-frames
            s.TakeDamage(DamageInfo.Make(null, s, 20f, DamageKind.Melee, "test"));
            Assert.AreEqual(hp - 20f, s.Health.Current, 0.01f, "vulnerable after 0.3 s");
        }

        [UnityTest]
        public IEnumerator Knife_Deals_8_To_Target_In_Front_Only()
        {
            var s = Spawn<SidekickAgent>(Vector3.zero);
            var front = Spawn<DummyHostile>(new Vector3(0f, 0f, 1.2f));
            var behind = Spawn<DummyHostile>(new Vector3(0f, 0f, -1.2f));
            var cmd = new ScriptedCommands { Current = new SidekickCommand { Attack = true, AimPoint = new Vector3(0, 0, 5), HasAim = true, Skill = -1 } };
            s.Commands = cmd;
            yield return null;
            Loop.StepMany(20);
            Assert.AreEqual(92f, front.Health.Current, 0.01f, "8 damage in front");
            Assert.AreEqual(100f, behind.Health.Current, 0.01f, "nothing behind");
        }

        [UnityTest]
        public IEnumerator Support_Range_Flag_Flips_At_25m()
        {
            var ctx = RunContext.Current;
            var hero = Spawn<DummyHostile>(new Vector3(0f, 0f, 0f));
            ctx.Hero = hero;
            var s = Spawn<SidekickAgent>(new Vector3(24f, 0f, 0f));
            s.Commands = new ScriptedCommands();
            yield return null;
            Loop.Step();
            Assert.IsTrue(s.InSupportRange, "24 m is inside support range");
            s.Motor.Teleport(new Vector3(26f, 0.05f, 0f));
            Loop.Step();
            Assert.IsFalse(s.InSupportRange, "26 m is outside");
        }
    }
}
