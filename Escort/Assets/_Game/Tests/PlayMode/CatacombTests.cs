using System.Collections;
using System.Collections.Generic;
using HS.Core;
using HS.Enemies;
using HS.Hero;
using HS.Rooms;
using HS.Sidekick;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    /// <summary>Chapter 3's pressures: plates hidden in the dark, seals, the seal key, the ward that breaks.</summary>
    public class CatacombTests
    {
        GameObject _ground, _ctxGo;
        readonly List<GameObject> _extra = new List<GameObject>();
        SimLoop Loop => SimLoop.Instance;
        RunContext Ctx => RunContext.Current;
        HeroAgent _hero;

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
            foreach (var a in Object.FindObjectsByType<Agent>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(a.gameObject);
            Object.Destroy(_ground);
            Object.DestroyImmediate(_ctxGo);
            if (SimLoop.Instance) Object.DestroyImmediate(SimLoop.Instance.gameObject);
        }

        IEnumerator MakeCallum(Vector3 pos)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Gameplay/Callum.prefab");
            _hero = Object.Instantiate(prefab, pos + Vector3.up * 0.05f, Quaternion.identity).GetComponent<HeroAgent>();
            Ctx.Hero = _hero;
            yield return null;
            _hero.Route.SetNodes(new List<RouteNode>());
#else
            yield break;
#endif
        }

        HazardMarker HiddenPlate(Vector3 pos)
        {
            var go = new GameObject("Plate");
            go.transform.position = pos;
            var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(vis.GetComponent<Collider>());
            vis.transform.SetParent(go.transform, false);
            vis.transform.localScale = new Vector3(1.2f, 0.05f, 1.2f);
            var h = go.AddComponent<HazardMarker>();
            h.Kind = HazardKind.SpikePlate;
            h.Hidden = true;
            _extra.Add(go);
            return h;
        }

        SealDoor Seal(Vector3 pos, bool guardians)
        {
            var go = new GameObject("Seal");
            go.transform.position = pos;
            if (guardians)
            {
                var g = new GameObject("Guardians");
                g.transform.SetParent(go.transform, false);
                var m = new GameObject("Spawn_thug").AddComponent<SpawnMarker>();
                m.transform.SetParent(g.transform, false);
                m.transform.localPosition = new Vector3(0f, 0f, 4f);
                m.Archetype = "thug";
            }
            var s = go.AddComponent<SealDoor>();
            _extra.Add(go);
            return s;
        }

        [UnityTest]
        public IEnumerator A_Hidden_Plate_Shows_Only_When_She_Comes_Close()
        {
            yield return MakeCallum(new Vector3(0f, 0f, -30f));
            var sk = SidekickTests.Spawn<SidekickAgent>(new Vector3(0f, 0f, 0f), 0.32f);
            sk.Commands = new ScriptedCommands();
            Ctx.Sidekick = sk;
            var plate = HiddenPlate(new Vector3(0f, 0.01f, 5f));
            yield return null;
            Assert.IsFalse(plate.Visible);
            Assert.IsFalse(plate.GetComponentInChildren<Renderer>().enabled, "nothing to see");
            Assert.IsFalse(plate.CanInteract(sk), "can't disarm what you haven't found");
            sk.Motor.Teleport(new Vector3(0f, 0.05f, 3f));
            Loop.Step();
            Assert.IsTrue(plate.Revealed, "close up, she finds it");
            Assert.IsTrue(plate.GetComponentInChildren<Renderer>().enabled);
            Assert.AreEqual(HazardMarker.HazardState.Armed, plate.State, "found, not sprung");
        }

        [UnityTest]
        public IEnumerator Sand_Finds_A_Plate_And_He_Springs_One_Nobody_Found()
        {
            yield return MakeCallum(Vector3.zero);
            var a = HiddenPlate(new Vector3(-6f, 0.01f, 10f));
            var b = HiddenPlate(new Vector3(0f, 0.01f, 3f));
            yield return null;
            Assert.AreEqual(1, HazardMarker.RevealAround(new Vector3(-6f, 0f, 10.5f), 2.3f));
            Assert.IsTrue(a.Revealed);
            _hero.Route.SetNodes(new List<RouteNode> { new RouteNode { Position = new Vector3(0f, 0f, 8f) } });
            Loop.StepMany(Mathf.CeilToInt(1.5f / SimLoop.Dt));
            Assert.AreEqual(HazardMarker.HazardState.Sprung, b.State, "he walks straight onto it");
        }

        [UnityTest]
        public IEnumerator The_Seal_Key_Opens_The_Seal()
        {
            yield return MakeCallum(new Vector3(0f, 0f, -20f));
            var sk = SidekickTests.Spawn<SidekickAgent>(Vector3.zero, 0.32f);
            Ctx.Sidekick = sk;
            var seal = Seal(new Vector3(0f, 0f, 8f), false);
            var go = new GameObject("Key");
            _extra.Add(go);
            var key = go.AddComponent<ExploreAnchor>();
            key.OpensSeal = seal;
            yield return null;
            Assert.IsFalse(seal.IsOpen);
            key.Interact(sk);
            Assert.IsTrue(seal.IsOpen);
            Assert.AreEqual("key", seal.OpenedBy);
        }

        [UnityTest]
        public IEnumerator After_8s_He_Breaks_It_And_The_Ward_Answers()
        {
            yield return MakeCallum(new Vector3(0f, 0f, 6f));
            var director = new GameObject("Encounters").AddComponent<EncounterDirector>();
            _extra.Add(director.gameObject);
            director.EnemyPrefab = GameAssets.Load().Enemy;
            var seal = Seal(new Vector3(0f, 0f, 8f), true);
            yield return null;
            Loop.StepMany(Mathf.CeilToInt(7f / SimLoop.Dt));
            Assert.IsFalse(seal.IsOpen, "he waits a while");
            Loop.StepMany(Mathf.CeilToInt(1.3f / SimLoop.Dt));
            Assert.IsTrue(seal.IsOpen);
            Assert.AreEqual("broken", seal.OpenedBy);
            CollectionAssert.Contains(_hero.Wounds.All, WoundType.CrackedRibs, "the ward pulses through him");
            Assert.AreEqual(1, director.Encounters.Count, "its guardian wakes");
            Assert.AreEqual(1, director.Encounters[0].Enemies.Count);
        }
    }
}
