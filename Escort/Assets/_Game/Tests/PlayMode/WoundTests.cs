using System.Collections;
using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Hero.Callum;
using HS.Rapport;
using HS.Rooms;
using HS.Sidekick;
using HS.Skills;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>GDD §4.2 wounds on the real hero + trap-corridor hazards.</summary>
    public class WoundTests
    {
        GameObject _ground, _ctxGo;
        SimLoop Loop => SimLoop.Instance;
        RunContext Ctx => RunContext.Current;
        HeroAgent _hero;
        CallumModule _cm;
        readonly List<GameObject> _extra = new List<GameObject>();

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

        IEnumerator MakeCallum(Vector3 pos)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Gameplay/Callum.prefab");
            var go = Object.Instantiate(prefab, pos + Vector3.up * 0.05f, Quaternion.identity);
            _hero = go.GetComponent<HeroAgent>();
            _cm = go.GetComponent<CallumModule>();
            Ctx.Hero = _hero;
#endif
            yield return null;
        }

        static DamageInfo Hit(float amount, DamageKind kind) => DamageInfo.Make(null, null, amount, kind, "test");

        [UnityTest]
        public IEnumerator A_Hit_Of_A_Quarter_Max_HP_Wounds_Smaller_Ones_Do_Not()
        {
            yield return MakeCallum(Vector3.zero);
            _hero.TakeDamage(Hit(48f, DamageKind.Blade)); // ambush-sized: 18%
            Assert.AreEqual(0, _hero.WoundCount);
            _hero.TakeDamage(Hit(65f, DamageKind.Heavy)); // 25% of 260
            Assert.AreEqual(1, _hero.WoundCount);
            Assert.AreEqual(WoundType.CrackedRibs, _hero.Wounds.All[0]);
            Assert.AreEqual(260f * 0.8f, _hero.Health.Max, 0.01f, "cracked ribs: −20% max HP");
            Assert.AreEqual(1, _hero.Wounds.SeriousCount);
        }

        [UnityTest]
        public IEnumerator Wound_Type_Follows_The_Damage_Kind_And_Each_Has_Its_Malus()
        {
            yield return MakeCallum(Vector3.zero);
            _hero.TakeDamage(Hit(70f, DamageKind.Blade));
            Assert.AreEqual(WoundType.SwordArmStrain, _hero.Wounds.All[0]);
            Assert.AreEqual(26f * 0.8f, _cm.OutgoingDamage(26f), 0.01f, "sword-arm strain: −20% damage");
            _hero.Health.Heal(999f);
            _hero.TakeDamage(Hit(70f, DamageKind.Melee));
            Assert.AreEqual(WoundType.Concussion, _hero.Wounds.All[1]);
            Assert.AreEqual(0.5f, _hero.Brain.ReactionDelay, 1e-4f, "concussion: rules react 0.5 s late");
            Assert.AreEqual(1f, _hero.SpeedMultiplier, 1e-4f);
        }

        [UnityTest]
        public IEnumerator Three_Wounds_Cripple_Him()
        {
            yield return MakeCallum(Vector3.zero);
            _hero.Wounds.Add(WoundType.SprainedAnkle);
            Assert.AreEqual(0.85f, _hero.SpeedMultiplier, 1e-4f, "sprained ankle −15%");
            _hero.Wounds.Add(WoundType.SwordArmStrain);
            Assert.IsFalse(_hero.Crippled);
            _hero.Wounds.Add(WoundType.Concussion);
            Assert.IsTrue(_hero.Crippled);
            Assert.AreEqual(0.85f * 0.7f, _hero.SpeedMultiplier, 1e-4f, "crippled: −30% on top");
        }

        [UnityTest]
        public IEnumerator Fever_Drains_Until_Treated_But_Not_Below_The_Floor()
        {
            yield return MakeCallum(Vector3.zero);
            _hero.Wounds.Add(WoundType.Fever);
            float hp = _hero.Health.Current;
            Loop.StepMany(Mathf.CeilToInt(10f / SimLoop.Dt));
            Assert.AreEqual(hp - 260f * 0.0045f * 10f, _hero.Health.Current, 1.01f, "~1.2 HP/s");
            _hero.Health.SetCurrent(260f * 0.16f);
            Loop.StepMany(Mathf.CeilToInt(20f / SimLoop.Dt));
            Assert.GreaterOrEqual(_hero.Health.Current, 260f * 0.15f - 0.01f, "fever never finishes him");
            Assert.IsTrue(_hero.IsAlive);
        }

        [UnityTest]
        public IEnumerator Bandage_Treats_A_Minor_Wound_Camp_Treats_The_Worst()
        {
            yield return MakeCallum(Vector3.zero);
            _hero.Wounds.Add(WoundType.CrackedRibs);
            _hero.Wounds.Add(WoundType.SprainedAnkle);
            _hero.Wounds.Add(WoundType.Fever);
            Assert.IsTrue(_hero.TreatMinorWound());
            CollectionAssert.AreEqual(new[] { WoundType.CrackedRibs, WoundType.Fever }, _hero.Wounds.All);
            Assert.IsFalse(_hero.TreatMinorWound(), "a bandage can't fix ribs");
            Assert.IsTrue(_hero.Wounds.TreatWorst());
            CollectionAssert.AreEqual(new[] { WoundType.Fever }, _hero.Wounds.All, "camp: serious first, oldest first");
            Assert.AreEqual(260f, _hero.Health.Max, 0.01f, "ribs healed: max HP restored");
        }

        [UnityTest]
        public IEnumerator Real_Bandage_On_Callum_Treats_His_Wound_And_Captures_The_Moment()
        {
            yield return MakeCallum(Vector3.zero);
            ProjectileSystem.Ensure();
            var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(1.5f, 0f, 0f), 0.32f);
            var skills = sk.gameObject.AddComponent<SidekickSkills>();
            var cmd = new ScriptedCommands();
            sk.Commands = cmd;
            Ctx.Sidekick = sk;
            Assert.IsTrue(skills.Learn("bandage"));
            yield return null;
            var dir = OpportunityDirector.Create(Ctx, _hero);
            _extra.Add(dir.gameObject);
            _extra.Add(ProjectileSystem.Instance.gameObject);
            _hero.Route.SetNodes(new List<RouteNode> { new RouteNode { Position = Vector3.zero } });
            // A duel that ends with him cut: the moment is offered.
            var e = SidekickTests.Spawn<EnemyAgent>(new Vector3(0f, 0f, 2.4f));
            e.transform.rotation = Quaternion.LookRotation(Vector3.back);
            e.Configure("thug");
            e.Activate();
            int guard = 0;
            while (_cm.Challenged == null && guard++ < 60) Loop.Step();
            _hero.TakeDamage(DamageInfo.Make(e, _hero, 70f, DamageKind.Blade, "test"));
            Assert.IsTrue(_hero.HasMinorWound);
            e.TakeDamage(DamageInfo.Make(_hero, e, 999f, DamageKind.Blade, "sword"));
            Loop.StepMany(3);
            Assert.AreEqual(CallumDance.WWoundTreated, dir.Ledger.Offered, 1e-3f, "wounded after the duel: offered");
            cmd.Current = new SidekickCommand { Skill = 0, AimPoint = _hero.Position, HasAim = true };
            Loop.StepMany(Mathf.CeilToInt(3.3f / SimLoop.Dt));
            Assert.AreEqual(0, _hero.WoundCount, "bandaged");
            Assert.AreEqual(CallumDance.WWoundTreated, dir.Ledger.Earned, 1e-3f);
        }

        HazardMarker Hazard(HazardKind kind, Vector3 pos, float span = 4f)
        {
            var go = new GameObject("Hazard_" + kind);
            go.transform.position = pos;
            var h = go.AddComponent<HazardMarker>();
            h.Kind = kind;
            h.Span = span;
            _extra.Add(go);
            return h;
        }

        [UnityTest]
        public IEnumerator Spike_Plate_On_His_Route_Hurts_And_Always_Wounds()
        {
            yield return MakeCallum(Vector3.zero);
            var h = Hazard(HazardKind.SpikePlate, new Vector3(0f, 0f, 4f));
            _hero.Route.SetNodes(new List<RouteNode> { new RouteNode { Position = new Vector3(0f, 0f, 9f) } });
            Loop.StepMany(Mathf.CeilToInt(2.2f / SimLoop.Dt));
            Assert.AreEqual(HazardMarker.HazardState.Sprung, h.State);
            Assert.AreEqual(260f - Mathf.Round(260f * 0.18f), _hero.Health.Current, 0.5f, "18% of max HP");
            Assert.AreEqual(WoundType.SprainedAnkle, _hero.Wounds.All[0]);
        }

        [UnityTest]
        public IEnumerator A_Disarmed_Plate_Is_Harmless_And_The_Careful_Sidekick_Steps_Over()
        {
            yield return MakeCallum(new Vector3(0f, 0f, -20f));
            var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(0f, 0f, 2.5f), 0.32f);
            var cmd = new ScriptedCommands();
            sk.Commands = cmd;
            Ctx.Sidekick = sk;
            var h = Hazard(HazardKind.SpikePlate, new Vector3(0f, 0f, 4f));
            yield return null;
            // walk across it carefully
            cmd.Current = new SidekickCommand { Move = Vector3.forward, Walk = true, Skill = -1 };
            Loop.StepMany(Mathf.CeilToInt(2.5f / SimLoop.Dt));
            Assert.AreEqual(HazardMarker.HazardState.Armed, h.State, "walking: careful feet");
            Assert.AreEqual(sk.Health.Max, sk.Health.Current);
            // back next to it and disarm (Interact, 1.2 s channel)
            sk.Motor.Teleport(new Vector3(0f, 0.05f, 3.2f));
            cmd.Current = new SidekickCommand { Interact = true, Skill = -1 };
            Loop.StepMany(Mathf.CeilToInt(1.5f / SimLoop.Dt));
            Assert.AreEqual(HazardMarker.HazardState.Disarmed, h.State);
            _hero.Motor.Teleport(new Vector3(0f, 0.05f, 2f));
            _hero.Route.SetNodes(new List<RouteNode> { new RouteNode { Position = new Vector3(0f, 0f, 9f) } });
            Loop.StepMany(Mathf.CeilToInt(2f / SimLoop.Dt));
            Assert.AreEqual(260f, _hero.Health.Current, 0.01f, "disarmed: he walks straight over it");
            Assert.AreEqual(0, _hero.WoundCount);
        }

        [UnityTest]
        public IEnumerator Tripwire_Trips_Him_And_Rings_The_Alarm()
        {
            yield return MakeCallum(Vector3.zero);
            var h = Hazard(HazardKind.Tripwire, new Vector3(0f, 0f, 3f), 6f);
            var bandit = SidekickTests.Spawn<EnemyAgent>(new Vector3(0f, 0f, 16f));
            bandit.Configure("thug");
            _hero.Route.SetNodes(new List<RouteNode> { new RouteNode { Position = new Vector3(0f, 0f, 9f) } });
            Loop.StepMany(Mathf.CeilToInt(1.2f / SimLoop.Dt));
            Assert.AreEqual(HazardMarker.HazardState.Sprung, h.State);
            Assert.IsTrue(_hero.Status.Has(StatusType.Staggered), "tripped");
            Assert.AreEqual(0, _hero.WoundCount, "a stumble, not a wound");
            Assert.AreEqual(EnemyState.Engaged, bandit.State, "the bell brought the bandits");
        }
    }
}
