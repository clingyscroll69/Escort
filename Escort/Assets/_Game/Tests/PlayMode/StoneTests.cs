using System.Collections;
using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Hero.Callum;
using HS.Rooms;
using HS.Sidekick;
using HS.Skills;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>Chronicle stones (GDD §4.3, §6.1): wake for Callum's formal duels, witness, record flaws, break.</summary>
    public class StoneTests
    {
        GameObject _ground, _ctxGo;
        SimLoop Loop => SimLoop.Instance;
        RunContext Ctx => RunContext.Current;
        HeroAgent _hero;
        CallumModule _cm;
        StoneSystem _sys;
        readonly List<GameObject> _extra = new List<GameObject>();

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
            if (_sys != null) Object.DestroyImmediate(_sys.gameObject);
            foreach (var g in _extra) if (g) Object.DestroyImmediate(g);
            _extra.Clear();
            foreach (var a in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None)) Object.DestroyImmediate(a.gameObject);
            if (ProjectileSystem.Instance) Object.DestroyImmediate(ProjectileSystem.Instance.gameObject);
            Object.Destroy(_ground);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject);
        }

        IEnumerator MakeCallum()
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Gameplay/Callum.prefab");
            var go = Object.Instantiate(prefab, Vector3.up * 0.05f, Quaternion.identity);
            _hero = go.GetComponent<HeroAgent>();
            _cm = go.GetComponent<CallumModule>();
            Ctx.Hero = _hero;
#endif
            yield return null;
            _hero.Route.SetNodes(new List<RouteNode> { new RouteNode { Position = Vector3.zero } });
            _sys = StoneSystem.Create(Ctx, _hero);
        }

        ChronicleStone Stone(Vector3 pos, float yaw)
        {
            var go = new GameObject("Stone");
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.AddComponent<StoneAnchor>();
            var cc = go.AddComponent<CharacterController>();
            cc.radius = 0.45f;
            cc.height = 1.7f;
            cc.center = new Vector3(0f, 0.85f, 0f);
            return go.AddComponent<ChronicleStone>();
        }

        EnemyAgent Enemy(Vector3 pos, string arch, bool perch = false)
        {
            var e = SidekickTests.Spawn<EnemyAgent>(pos);
            var face = new Vector3(-pos.x, 0f, -pos.z);
            e.transform.rotation = Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face.normalized : Vector3.back);
            e.Archetype = arch;
            e.Elevated = perch;
            e.Configure(arch);
            e.Activate();
            return e;
        }

        void StepUntilDuel()
        {
            int guard = 0;
            while (_cm.Challenged == null && guard++ < 120) Loop.Step();
            Assert.IsNotNull(_cm.Challenged, "duel started");
        }

        [UnityTest]
        public IEnumerator Wakes_For_A_Formal_Duel_In_View_And_Sleeps_After()
        {
            yield return MakeCallum();
            var near = Stone(new Vector3(6f, 0f, 4f), -120f);
            var far = Stone(new Vector3(0f, 0f, -22f), 0f);
            Loop.StepMany(30);
            Assert.AreEqual(ChronicleStone.StoneState.Dormant, near.State, "asleep outside duels");
            var thug = Enemy(new Vector3(0f, 0f, 2.6f), "thug");
            StepUntilDuel();
            Assert.AreEqual(ChronicleStone.StoneState.Active, near.State, "streams the formal duel");
            Assert.AreEqual(ChronicleStone.StoneState.Dormant, far.State, "out of range: stays asleep");
            Loop.StepMany(Mathf.CeilToInt(4f / SimLoop.Dt));
            Assert.AreEqual(ChronicleStone.StoneState.Active, near.State, "awake for the whole duel");
            thug.TakeDamage(DamageInfo.Make(_hero, thug, 999f, DamageKind.Blade, "sword"));
            Loop.StepMany(Mathf.CeilToInt(2.3f / SimLoop.Dt));
            Assert.AreEqual(ChronicleStone.StoneState.Dormant, near.State, "sleeps 2 s after the duel");
        }

        IEnumerator ShooterScene(bool wall)
        {
            yield return MakeCallum();
            var stone = Stone(new Vector3(-6f, 0f, -6f), 90f);
            var thug = Enemy(new Vector3(0f, 0f, 2.6f), "thug");
            thug.Status.Apply(StatusType.Stunned, 3f);
            var shooter = Enemy(new Vector3(0f, 0f, -8f), "crossbowman", perch: true);
            shooter.Status.Apply(StatusType.Blinded, 5f); // a bolt into a blinded man is dishonour, if anyone sees it
            if (wall)
            {
                var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
                w.name = "TestWall";
                w.transform.position = new Vector3(-3f, 1.5f, -7f);
                w.transform.localScale = new Vector3(0.4f, 3f, 4f);
                _extra.Add(w);
                Physics.SyncTransforms();
            }
            var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(2f, 0f, -11f), 0.32f);
            var skills = sk.gameObject.AddComponent<SidekickSkills>();
            var cmd = new ScriptedCommands();
            sk.Commands = cmd;
            Ctx.Sidekick = sk;
            Assert.IsTrue(skills.Learn("crossbow"));
            yield return null;
            StepUntilDuel();
            Assert.AreEqual(ChronicleStone.StoneState.Active, stone.State);
            cmd.Current = new SidekickCommand { Skill = 0, AimPoint = shooter.Position, HasAim = true };
            Loop.StepMany(30);
            Assert.Less(shooter.Health.Current, shooter.Health.Max, "the bolt hit");
        }

        [UnityTest]
        public IEnumerator An_Active_Stone_Witnesses_What_Callum_Cannot_See()
        {
            yield return ShooterScene(false);
            Assert.Less(_cm.Honor, 100f, "the stone saw it: dishonour");
            Assert.IsTrue(StoneSystem.AnyActiveSees(new Vector3(0f, 0f, -8f)));
            Assert.IsFalse(_cm.Sees(new Vector3(0f, 0f, -8f), false), "Callum himself was facing the other way");
        }

        [UnityTest]
        public IEnumerator Walls_Block_A_Stones_View()
        {
            yield return ShooterScene(true);
            Assert.AreEqual(100f, _cm.Honor, 0.01f, "nobody saw it");
        }

        [UnityTest]
        public IEnumerator Records_His_Flaws_For_The_Curator_And_Relays_At_Camp()
        {
            yield return MakeCallum();
            var stone = Stone(new Vector3(6f, 0f, 4f), -120f);
            var thug = Enemy(new Vector3(0f, 0f, 2.6f), "thug");
            thug.Status.Apply(StatusType.Stunned, 1.6f);
            StepUntilDuel();
            while (_cm.Saluting) Loop.Step();
            thug.Status.Apply(StatusType.Blinded, 4f); // someone sanded him: Callum waits — on camera
            Loop.StepMany(10);
            Assert.AreEqual("callum_wait_unready", _hero.ActiveRuleId);
            Assert.AreEqual(1, stone.PendingClips, "etiquette clip");
            Loop.StepMany(30);
            Assert.AreEqual(1, stone.PendingClips, "one clip per flaw per duel");
            Assert.AreEqual(0, _sys.IntelLevel, "two clips per Intel level");
            _hero.TakeDamage(DamageInfo.Make(thug, _hero, 10f, DamageKind.Blade, "cheap_shot"));
            Assert.AreEqual(2, stone.PendingClips, "treachery clip");
            Assert.AreEqual(1, _sys.IntelLevel);
            _sys.RelayAll();
            Assert.AreEqual(0, stone.PendingClips);
            Assert.AreEqual(2, _sys.Intel.Relayed);
            Assert.AreEqual(1, _sys.IntelLevel);
        }

        [UnityTest]
        public IEnumerator Three_Knife_Cuts_Break_A_Stone_And_Its_Clips_Die_With_It()
        {
            yield return MakeCallum();
            var stone = Stone(new Vector3(0f, 0f, 8f), 180f);
            stone.PendingClips = 1;
            var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(0f, 0f, 6.9f), 0.32f);
            var cmd = new ScriptedCommands { RepeatEdges = false };
            sk.Commands = cmd;
            Ctx.Sidekick = sk;
            yield return null;
            Assert.AreEqual(0, _sys.IntelLevel);
            for (int i = 0; i < 3; i++)
            {
                cmd.Current = new SidekickCommand { Attack = true, AimPoint = stone.Position, HasAim = true, Skill = -1 };
                Loop.StepMany(Mathf.CeilToInt(0.6f / SimLoop.Dt));
            }
            Assert.AreEqual(ChronicleStone.StoneState.Broken, stone.State);
            Assert.AreEqual(0, stone.PendingClips, "what it saw dies with it");
            stone.Wake(5f);
            Assert.AreEqual(ChronicleStone.StoneState.Broken, stone.State, "a broken stone never wakes");
            Assert.IsFalse(stone.Sees(_hero.Position));
        }
    }
}
