using System.Collections;
using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Hero.Callum;
using HS.Sidekick;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    public class CallumTests
    {
        GameObject _ground, _ctxGo;
        SimLoop Loop => SimLoop.Instance;
        RunContext Ctx => RunContext.Current;
        HeroAgent _hero;
        CallumModule _cm;

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
            foreach (var a in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None)) Object.DestroyImmediate(a.gameObject);
            foreach (var w in Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None)) if (w.name == "TestWall") Object.DestroyImmediate(w.gameObject);
            Object.Destroy(_ground);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject);
        }

        IEnumerator MakeCallum(Vector3 pos, Stage stage = Stage.S0)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Gameplay/Callum.prefab");
            var go = Object.Instantiate(prefab, pos + Vector3.up * 0.05f, Quaternion.identity);
            _hero = go.GetComponent<HeroAgent>();
            _cm = go.GetComponent<CallumModule>();
            Ctx.Hero = _hero;
            yield return null; // Start → rules
            _hero.ApplyStage(stage);
#else
            yield break;
#endif
        }

        EnemyAgent Enemy(Vector3 pos, string arch = "thug", bool engage = true)
        {
            var e = SidekickTests.Spawn<EnemyAgent>(pos);
            e.transform.rotation = Quaternion.LookRotation(-pos.normalized == Vector3.zero ? Vector3.back : -new Vector3(pos.x, 0, pos.z).normalized);
            e.Archetype = arch;
            e.Configure(arch);
            if (engage) e.Activate();
            return e;
        }

        [UnityTest]
        public IEnumerator Challenges_Nearest_Within_15m_With_A_1_2s_Salute()
        {
            yield return MakeCallum(Vector3.zero);
            var far = Enemy(new Vector3(0f, 0f, 16.5f));
            far.Status.Apply(StatusType.Stunned, 99f); // keep it put
            Loop.StepMany(5);
            Assert.IsNull(_cm.Challenged, "nobody within 15 m → no challenge");
            var near = Enemy(new Vector3(1f, 0f, 9f));
            near.Status.Apply(StatusType.Stunned, 1.1f); // recovers just before the salute ends
            var mid = Enemy(new Vector3(-2f, 0f, 12f));
            mid.Status.Apply(StatusType.Stunned, 99f);
            Loop.Step();
            Loop.Step();
            Assert.AreEqual(near, _cm.Challenged, "nearest hostile");
            Assert.AreEqual("challenge", _hero.ActiveIcon);
            Assert.AreEqual(_hero, near.DuelOpponent);
            int ticks = 0;
            while (_cm.Saluting && ticks < 200) { Loop.Step(); ticks++; }
            Assert.That(ticks * SimLoop.Dt, Is.InRange(1.1f, 1.25f), "salute lasts 1.2 s");
            Loop.StepMany(3);
            Assert.AreEqual("callum_fight", _hero.ActiveRuleId);
        }

        IEnumerator WaitDuration(Stage stage, System.Action<float> result)
        {
            yield return MakeCallum(Vector3.zero, stage);
            var e = Enemy(new Vector3(0f, 0f, 3f));
            e.Status.Apply(StatusType.Stunned, 1.6f);
            Loop.StepMany(2);
            while (_cm.Saluting) Loop.Step();
            e.Status.Apply(StatusType.Blinded, 6f);
            Loop.Step();
            int waited = 0;
            while (_hero.ActiveRuleId == "callum_wait_unready" && waited < 1000) { Loop.Step(); waited++; }
            result(waited * SimLoop.Dt);
        }

        [UnityTest]
        public IEnumerator Waits_On_Unready_Target_S0_3s()
        {
            float t = -1f;
            yield return WaitDuration(Stage.S0, x => t = x);
            Assert.That(t, Is.InRange(2.9f, 3.1f));
        }

        [UnityTest]
        public IEnumerator Waits_On_Unready_Target_S1_2s()
        {
            float t = -1f;
            yield return WaitDuration(Stage.S1, x => t = x);
            Assert.That(t, Is.InRange(1.9f, 2.1f), "S1: waiting cap 2 s");
        }

        [UnityTest]
        public IEnumerator Turncoat_Fake_Surrender_Cheap_Shot_Wounds_At_S0()
        {
            yield return MakeCallum(Vector3.zero, Stage.S0);
            var tc = Enemy(new Vector3(0f, 0f, 2.2f), "turncoat");
            tc.Status.Apply(StatusType.Stunned, 1.5f);
            Loop.StepMany(2);
            while (_cm.Saluting) Loop.Step();
            // bring him to surrender
            tc.TakeDamage(DamageInfo.Make(_hero, tc, 60f, DamageKind.Blade, "sword"));
            Assert.AreEqual(EnemyState.Surrendered, tc.State);
            float hp = _hero.Health.Current;
            Loop.StepMany(Mathf.CeilToInt(2.7f / SimLoop.Dt)); // the stab lands at 2.4 s, before a normal follow-up
            Assert.AreEqual(hp - 66f, _hero.Health.Current, 0.5f, "S0 waits 3 s; the stab lands at 2.4 s");
        }

        [UnityTest]
        public IEnumerator Turncoat_Is_Spared_Before_The_Stab_At_S1()
        {
            yield return MakeCallum(Vector3.zero, Stage.S1);
            var tc = Enemy(new Vector3(0f, 0f, 2.2f), "turncoat");
            tc.Status.Apply(StatusType.Stunned, 1.5f);
            Loop.StepMany(2);
            while (_cm.Saluting) Loop.Step();
            tc.TakeDamage(DamageInfo.Make(_hero, tc, 60f, DamageKind.Blade, "sword"));
            float hp = _hero.Health.Current;
            Loop.StepMany(Mathf.CeilToInt(3.2f / SimLoop.Dt));
            Assert.AreEqual(EnemyState.Spared, tc.State, "S1 accepts the surrender at 2 s");
            Assert.AreEqual(hp, _hero.Health.Current, 0.01f);
        }

        [UnityTest]
        public IEnumerator Riposte_Parries_The_First_Swing_And_Counters_Double()
        {
            yield return MakeCallum(Vector3.zero);
            var e = Enemy(new Vector3(0f, 0f, 1.9f), "brute");
            Loop.StepMany(2);
            while (_cm.Saluting) Loop.Step();
            float heroHp = _hero.Health.Current, eHp = e.Health.Current;
            // run until the brute's first swing resolves
            int guard = 0;
            while (_cm.RiposteCooldown <= 0f && guard++ < 600) Loop.Step();
            Assert.Greater(_cm.RiposteCooldown, 0f, "riposte triggered");
            Assert.AreEqual(heroHp, _hero.Health.Current, 0.01f, "the parried swing does nothing");
            Assert.LessOrEqual(e.Health.Current, eHp - 52f + 0.01f, "counter for 2 × 26");
        }

        static SabotageEvent Deed(Vector3 at, SabotageSeverity sev) => new SabotageEvent { Tag = "pocket_sand", Severity = sev, Position = at, ActorPosition = at };

        [UnityTest]
        public IEnumerator Honor_Costs_Major20_Minor8_And_S1_Halves_Minor()
        {
            yield return MakeCallum(Vector3.zero, Stage.S0);
            Assert.AreEqual(100f, _cm.Honor);
            Ctx.Events.RaiseSabotage(Deed(new Vector3(0f, 0f, 6f), SabotageSeverity.Major));
            Assert.AreEqual(80f, _cm.Honor, 0.01f);
            Ctx.Events.RaiseSabotage(Deed(new Vector3(1f, 0f, 5f), SabotageSeverity.Minor));
            Assert.AreEqual(72f, _cm.Honor, 0.01f);
            _hero.ApplyStage(Stage.S1);
            Ctx.Events.RaiseSabotage(Deed(new Vector3(-1f, 0f, 5f), SabotageSeverity.Minor));
            Assert.AreEqual(68f, _cm.Honor, 0.01f, "S1: minor assists cost half");
            bool unseen = false;
            _cm.UnseenDeed += _ => unseen = true;
            Ctx.Events.RaiseSabotage(Deed(new Vector3(0f, 0f, -6f), SabotageSeverity.Major)); // behind him
            Assert.AreEqual(68f, _cm.Honor, 0.01f, "unseen: no cost");
            Assert.IsTrue(unseen);
        }

        [UnityTest]
        public IEnumerator Low_Honor_Cuts_His_Damage_By_A_Quarter()
        {
            yield return MakeCallum(Vector3.zero);
            Assert.AreEqual(26f, _cm.OutgoingDamage(26f), 0.01f);
            _cm.SetHonor(39f);
            Assert.IsTrue(_cm.HonorLow);
            Assert.AreEqual(19.5f, _cm.OutgoingDamage(26f), 0.01f);
        }

        [UnityTest]
        public IEnumerator QuietFeet_Narrows_The_Witness_Cone_And_Walls_Block_Sight()
        {
            yield return MakeCallum(Vector3.zero);
            var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(20f, 0f, 0f));
            sk.Commands = new ScriptedCommands();
            Ctx.Sidekick = sk;
            var p = Quaternion.Euler(0f, 50f, 0f) * Vector3.forward * 8f; // 50° off-axis, 8 m
            Assert.IsTrue(_cm.Sees(p, false), "inside 120°/12 m");
            sk.HasQuietFeet = true;
            Assert.IsFalse(_cm.Sees(p, true), "Quiet Feet sneak: ~80°/7.2 m");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "TestWall";
            wall.transform.position = new Vector3(0f, 1.5f, 4f);
            wall.transform.localScale = new Vector3(4f, 3f, 0.3f);
            Physics.SyncTransforms();
            Assert.IsFalse(_cm.Sees(new Vector3(0f, 0f, 8f), false), "no line of sight through walls");
        }

        [UnityTest]
        public IEnumerator Falls_Back_To_A_Chokepoint_At_Three_Engagers()
        {
            yield return MakeCallum(Vector3.zero);
            _hero.Route.SetNodes(new List<RouteNode>
            {
                new RouteNode { Position = new Vector3(0f, 0f, -7f), Chokepoint = true, Label = "narrows" },
                new RouteNode { Position = new Vector3(0f, 0f, 20f) },
            }, 1);
            Enemy(new Vector3(1.5f, 0f, 1.5f));
            Enemy(new Vector3(-1.5f, 0f, 1.5f));
            Enemy(new Vector3(0f, 0f, 2.2f));
            int switchesWhileRetreating = 0;
            _hero.Brain.RuleChanged += (p, n) => { if (p != null && p.Id == "callum_fallback" && _cm.FallingBack) switchesWhileRetreating++; };
            Loop.StepMany(30);
            Assert.AreEqual("callum_fallback", _hero.ActiveRuleId);
            Assert.AreEqual("fallback", _hero.ActiveIcon);
            Loop.StepMany(90);
            Assert.Less(_hero.Position.z, -2f, "retreats toward the narrows");
            Assert.AreEqual(0, switchesWhileRetreating, "the retreat is committed (no fight/fallback thrash)");
            int guard = 0;
            while (_cm.FallingBack && guard++ < 400) Loop.Step();
            Assert.IsTrue(_cm.HoldingNarrows, "arrived and holding the chokepoint");
            Assert.AreEqual(1, _hero.MeleeSlots, "single file: one attacker at a time");
        }

        [UnityTest]
        public IEnumerator His_Own_Blows_And_Parries_Do_Not_Make_The_Target_Unready()
        {
            yield return MakeCallum(Vector3.zero);
            var e = Enemy(new Vector3(0f, 0f, 2.0f), "brute");
            Loop.StepMany(2);
            while (_cm.Saluting) Loop.Step();
            bool waited = false;
            _hero.Brain.RuleChanged += (p, n) => { if (n != null && n.Id == "callum_wait_unready") waited = true; };
            Loop.StepMany(Mathf.CeilToInt(6f / SimLoop.Dt));
            Assert.IsFalse(waited, "sword staggers and ripostes are his own doing");
            Assert.Less(e.Health.Current, e.Health.Max - 52f, "and he kept fighting");
        }

        [UnityTest]
        public IEnumerator Only_Two_Bandits_Swing_At_Once_The_Rest_Circle()
        {
            yield return MakeCallum(Vector3.zero);
            _hero.Route.SetNodes(new List<RouteNode> { new RouteNode { Position = new Vector3(0f, 0f, 30f) } }); // no chokepoint
            var foes = new List<EnemyAgent>();
            for (int i = 0; i < 4; i++) foes.Add(Enemy(Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward * 2.5f));
            int maxSwinging = 0, everSwung = 0;
            var swung = new HashSet<EnemyAgent>();
            for (int k = 0; k < 900; k++)
            {
                Loop.Step();
                int swinging = 0;
                foreach (var f in foes)
                    if (f != null && f.IsAlive && f.IsAttackWindup)
                    {
                        swinging++;
                        swung.Add(f);
                    }
                maxSwinging = Mathf.Max(maxSwinging, swinging);
            }
            everSwung = swung.Count;
            Assert.LessOrEqual(maxSwinging, 2, "two attack tokens on the hero");
            Assert.GreaterOrEqual(everSwung, 3, "tokens rotate: the circlers get their turn");
        }

        [UnityTest]
        public IEnumerator A_Staggering_Hit_Is_Judged_On_The_Victim_As_It_Was_Not_As_It_Became()
        {
            yield return MakeCallum(Vector3.zero);
            var duel = Enemy(new Vector3(0f, 0f, 2.6f));
            duel.Status.Apply(StatusType.Stunned, 5f);
            var other = Enemy(new Vector3(3f, 0f, 6f));
            other.Status.Apply(StatusType.Stunned, 0.01f);
            Loop.StepMany(3);
            var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(4f, 0f, 3f));
            sk.Commands = new ScriptedCommands();
            Ctx.Sidekick = sk;
            float honor = _cm.Honor;
            // a crossbow bolt (0.3 s stagger) on a ready bandit he can see: interfering, not "striking the helpless"
            other.TakeDamage(DamageInfo.Make(sk, other, 38f, DamageKind.Ranged, "crossbow", 0.3f));
            Assert.AreEqual(honor - _cm.T.honorLossMinor, _cm.Honor, 0.01f, "Minor (−8), not Major (−20)");
        }

        [UnityTest]
        public IEnumerator The_Turncoat_Fakes_His_Surrender_Only_Once()
        {
            yield return MakeCallum(Vector3.zero, Stage.S0);
            var tc = Enemy(new Vector3(0f, 0f, 2.2f), "turncoat");
            tc.Status.Apply(StatusType.Stunned, 1.5f);
            Loop.StepMany(2);
            while (_cm.Saluting) Loop.Step();
            tc.TakeDamage(DamageInfo.Make(_hero, tc, 60f, DamageKind.Blade, "sword"));
            Assert.AreEqual(EnemyState.Surrendered, tc.State);
            Loop.StepMany(Mathf.CeilToInt(2.7f / SimLoop.Dt)); // the stab lands; he's back to fighting
            Assert.AreEqual(EnemyState.Engaged, tc.State);
            tc.TakeDamage(DamageInfo.Make(_hero, tc, 5f, DamageKind.Blade, "sword"));
            Assert.AreEqual(EnemyState.Engaged, tc.State, "the ruse doesn't work twice");
        }
    }
}
