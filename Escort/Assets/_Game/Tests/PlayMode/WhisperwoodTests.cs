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
    /// <summary>Chapter 2's pressures: snares, sleeping poachers, hunger and rations.</summary>
    public class WhisperwoodTests
    {
        GameObject _ground, _ctxGo;
        readonly List<GameObject> _extra = new List<GameObject>();
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
            yield return null;
#else
            yield break;
#endif
        }

        HazardMarker Hazard(HazardKind kind, Vector3 pos)
        {
            var go = new GameObject("Hazard_" + kind);
            go.transform.position = pos;
            var h = go.AddComponent<HazardMarker>();
            h.Kind = kind;
            _extra.Add(go);
            return h;
        }

        EnemyAgent Sleeper(Vector3 pos, bool engage)
        {
            var e = SidekickTests.Spawn<EnemyAgent>(pos);
            e.Archetype = "thug";
            e.StartsAsleep = true;
            e.Configure("thug");
            if (engage) e.Activate();
            return e;
        }

        [UnityTest]
        public IEnumerator Snare_Trips_Him_And_Sprains_His_Ankle()
        {
            yield return MakeCallum(Vector3.zero);
            var h = Hazard(HazardKind.Snare, new Vector3(0f, 0f, 3f));
            _hero.Route.SetNodes(new List<RouteNode> { new RouteNode { Position = new Vector3(0f, 0f, 9f) } });
            int ticks = 0;
            while (h.State == HazardMarker.HazardState.Armed && ticks++ < 200) Loop.Step();
            Assert.AreEqual(HazardMarker.HazardState.Sprung, h.State);
            Assert.IsTrue(_hero.Status.Has(StatusType.Staggered), "yanked off his feet");
            Assert.Greater(_hero.Status.Remaining(StatusType.Staggered), 1.4f);
            Assert.AreEqual(260f - Mathf.Round(260f * 0.06f), _hero.Health.Current, 0.5f, "6% of max HP");
            CollectionAssert.AreEqual(new[] { WoundType.SprainedAnkle }, _hero.Wounds.All);
        }

        [UnityTest]
        public IEnumerator A_Careful_Sidekick_Steps_Over_A_Snare()
        {
            yield return MakeCallum(new Vector3(0f, 0f, -20f));
            var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(0f, 0f, 1.5f), 0.32f);
            var cmd = new ScriptedCommands();
            sk.Commands = cmd;
            Ctx.Sidekick = sk;
            var h = Hazard(HazardKind.Snare, new Vector3(0f, 0f, 3f));
            yield return null;
            cmd.Current = new SidekickCommand { Move = Vector3.forward, Walk = true, Skill = -1 };
            Loop.StepMany(Mathf.CeilToInt(2.5f / SimLoop.Dt));
            Assert.AreEqual(HazardMarker.HazardState.Armed, h.State, "walking: she sees the loop");
            cmd.Current = new SidekickCommand { Move = Vector3.back, Skill = -1 };
            Loop.StepMany(Mathf.CeilToInt(1.5f / SimLoop.Dt));
            Assert.AreEqual(HazardMarker.HazardState.Sprung, h.State, "running back through it: caught");
            Assert.IsTrue(sk.Status.Has(StatusType.Staggered));
        }

        [UnityTest]
        public IEnumerator Sleepers_Are_Helpless_Until_His_Challenge_Wakes_Them()
        {
            yield return MakeCallum(Vector3.zero);
            var a = Sleeper(new Vector3(0f, 0f, 5f), true);
            var b = Sleeper(new Vector3(3f, 0f, 6f), true);
            yield return null; // Start → asleep
            Assert.IsTrue(a.Asleep);
            Assert.IsTrue(a.IsHelpless(_hero), "a sleeper can't fight back");
            Loop.StepMany(3);
            Assert.IsNotNull(_cm.Challenged, "he challenges the nearest");
            Assert.IsFalse(a.Asleep, "his challenge wakes them");
            Assert.IsFalse(b.Asleep);
            Assert.IsTrue(a.IsUnreadyFor(_hero), "still scrambling up");
            Loop.StepMany(Mathf.CeilToInt((EnemyAgent.GetUpTime + 0.3f) / SimLoop.Dt));
            Assert.IsFalse(a.Status.Has(StatusType.Stunned), "up and fighting");
        }

        [UnityTest]
        public IEnumerator A_Knife_In_A_Sleeper_Is_Major_If_He_Sees_It()
        {
            yield return MakeCallum(Vector3.zero);
            _hero.Motor.FaceInstant(Vector3.forward);
            var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(1f, 0f, 5f), 0.32f);
            sk.Commands = new ScriptedCommands();
            Ctx.Sidekick = sk;
            var e = Sleeper(new Vector3(0f, 0f, 6f), false);
            yield return null;
            Assert.IsTrue(e.Asleep);
            e.TakeDamage(DamageInfo.Make(sk, e, 8f, DamageKind.Knife, "knife"));
            Assert.AreEqual(_cm.T.honorMax - _cm.T.honorLossMajor, _cm.Honor, 0.01f, "striking a sleeping man in his sight");
            Assert.IsFalse(e.Asleep, "the blow woke him");
        }

        [UnityTest]
        public IEnumerator Starving_Callum_Hits_For_80_Percent()
        {
            yield return MakeCallum(Vector3.zero);
            _hero.Hunger.Enabled = true;
            Assert.AreEqual(100f, _cm.OutgoingDamage(100f), 0.01f);
            _hero.Hunger.Restore(0f);
            Assert.AreEqual(80f, _cm.OutgoingDamage(100f), 0.01f);
        }

        [UnityTest]
        public IEnumerator Feeding_Takes_A_Ration_And_Needs_A_Lull()
        {
            yield return MakeCallum(Vector3.zero);
            _hero.Hunger.Enabled = true;
            _hero.Hunger.Restore(10f);
            var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(1.2f, 0f, 0f), 0.32f);
            var cmd = new ScriptedCommands();
            sk.Commands = cmd;
            Ctx.Sidekick = sk;
            var feed = _hero.GetComponent<FeedInteraction>();
            Assert.IsNotNull(feed, "every hero can be fed");
            yield return null;
            Assert.IsFalse(feed.CanInteract(sk), "no rations");
            sk.Rations.Give(1);
            var bandit = SidekickTests.Spawn<EnemyAgent>(new Vector3(0f, 0f, 6f));
            bandit.Configure("thug");
            bandit.Status.Apply(StatusType.Stunned, 99f);
            _cm.StartChallenge(bandit);
            Assert.IsFalse(feed.CanInteract(sk), "not mid-duel");
            _cm.EndDuel(DuelEndReason.Abandoned);
            Assert.IsTrue(feed.CanInteract(sk));
            cmd.Current = new SidekickCommand { Interact = true, Skill = -1 };
            Loop.Step();
            cmd.Current = new SidekickCommand { Skill = -1 };
            Loop.StepMany(Mathf.CeilToInt((FeedInteraction.Duration + 0.2f) / SimLoop.Dt));
            Assert.Greater(_hero.Hunger.Value, Hunger.Max - 1f, "fed (and draining again)");
            Assert.AreEqual(0, sk.Rations.Count, "one ration spent");
        }

        [UnityTest]
        public IEnumerator A_Forage_Cache_Gives_Rations()
        {
            yield return MakeCallum(new Vector3(0f, 0f, -10f));
            var sk = SidekickTests.Spawn<SidekickAgent>(Vector3.zero, 0.32f);
            Ctx.Sidekick = sk;
            var go = new GameObject("Cache");
            _extra.Add(go);
            var cache = go.AddComponent<ExploreAnchor>();
            cache.Title = "Mushroom patch";
            cache.Rations = 2;
            yield return null;
            cache.Interact(sk);
            Assert.AreEqual(2, sk.Rations.Count);
            Assert.IsTrue(cache.Searched);
        }

        [UnityTest]
        public IEnumerator A_Sleeper_He_Calls_Out_From_Afar_Wakes()
        {
            yield return MakeCallum(Vector3.zero);
            var far = Sleeper(new Vector3(0f, 0f, 12f), true);
            var buddy = Sleeper(new Vector3(2f, 0f, 14f), true);
            yield return null;
            _cm.StartChallenge(far);
            Assert.IsFalse(far.Asleep, "the man he names wakes, however far");
            Assert.IsFalse(buddy.Asleep, "and the man dozing beside him");
        }
    }
}
