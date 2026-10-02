using System.Collections;
using System.Linq;
using HS.Rooms;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HS.Tests
{
    public class ChapterBuildTests
    {
        public static ChapterBuilder MakeBuilder()
        {
#if UNITY_EDITOR
            GameObject L(string id) => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Game/Prefabs/Rooms/{id}.prefab");
            var b = new GameObject("Chapter").AddComponent<ChapterBuilder>();
            b.ModulePrefabs = new[] { L("crossroads_shrine"), L("toll_gate"), L("ruined_gatehouse"), L("wagon_camp") };
            b.StartCapPrefab = L("road_cap_start");
            b.EndCapPrefab = L("road_cap_end");
            b.CampfirePrefab = L("campfire");
            b.BossPrefab = L("rigged_duel");
            return b;
#else
            return null;
#endif
        }

        [UnityTest]
        public IEnumerator Seeded_Chapter_Is_Continuous_And_Deterministic()
        {
            var b = MakeBuilder();
            Assert.IsTrue(b.ModulePrefabs.All(p => p != null), "room prefabs present (Tools/HS/Build/Rooms)");
            foreach (int seed in new[] { 1, 7, 42, 1234, 99999 })
            {
                var t0 = Time.realtimeSinceStartup;
                b.Build(seed);
                float buildMs = (Time.realtimeSinceStartup - t0) * 1000f;
                yield return null;
                Assert.AreEqual(3, b.Rooms.Count);
                float z = 0f;
                foreach (var r in b.Rooms)
                {
                    Assert.AreEqual(z, r.transform.position.z, 0.001f, "rooms abut: exit of one is the entry of the next");
                    z += r.Length;
                }
                var route = b.ChapterRoute();
                Assert.Greater(route.Count, 12);
                for (int i = 1; i < route.Count; i++)
                    Assert.Greater(route[i].Position.z, route[i - 1].Position.z - 0.01f, $"route runs forward (seed {seed}, node {i})");
                Assert.AreEqual(3, route.Count(n => n.Threshold), "one threshold pause per room");
                for (int i = 0; i < 3; i++) Assert.IsTrue(route.Any(n => n.RoomIndex == i), "every room contributes route nodes");
                Assert.NotNull(b.Campfire);
                Assert.NotNull(b.Boss);
                Debug.Log($"[Chapter] seed {seed}: {b.Plan.Signature} length {b.ChapterLength:F0} m, built in {buildMs:F0} ms");
                Assert.Less(buildMs, 2500f, "chapter assembly must be quick enough for restore points");
                var sig = b.Plan.Signature;
                b.Build(seed);
                Assert.AreEqual(sig, b.Plan.Signature, "same seed → same chapter");
            }
            Object.Destroy(b.gameObject);
        }

        [UnityTest]
        public IEnumerator Scenery_Is_Static_Batched_But_Moving_Parts_Are_Not()
        {
            var b = MakeBuilder();
            b.Build(1);
            yield return null;
            int frozenDynamic = 0, dynamicCount = 0, batchedScenery = 0;
            foreach (var r in b.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool dynamic = r.GetComponentInParent<HS.Core.IDynamicVisual>(true) != null;
                if (dynamic)
                {
                    dynamicCount++;
                    if (r.isPartOfStaticBatch) frozenDynamic++;
                }
                else if (r.isPartOfStaticBatch) batchedScenery++;
            }
            Assert.Greater(dynamicCount, 0, "chapter has armable props / hazards / stones");
            Assert.AreEqual(0, frozenDynamic, "falling props, traps and stones must stay movable");
            Assert.Greater(batchedScenery, 100, "the scenery is still batched");
            Object.Destroy(b.gameObject);
        }
    }
}
