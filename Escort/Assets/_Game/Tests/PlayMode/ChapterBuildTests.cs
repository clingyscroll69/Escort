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
                Assert.NotNull(b.Campfire, "the Old Road ends at its campfire");
                Assert.IsNull(b.Boss, "the boss is the Gallery's");
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

        [UnityTest]
        public IEnumerator The_Gallery_Builds_The_Boss_Arena_And_No_Campfire()
        {
            var b = MakeBuilder();
            b.Build(3, ChapterDef.For(5));
            yield return null;
            Assert.AreEqual(2, b.Rooms.Count);
            Assert.NotNull(b.Boss);
            Assert.IsNull(b.Campfire);
            Object.Destroy(b.gameObject);
        }

        [UnityTest]
        public IEnumerator Whisperwood_Builds_Four_Forest_Rooms_With_Quill()
        {
            var assets = HS.Core.GameAssets.Load();
            var b = new GameObject("Chapter").AddComponent<ChapterBuilder>();
            b.ModulePrefabs = assets.roomModules;
            b.StartCapPrefab = assets.startCap;
            b.EndCapPrefab = assets.endCap;
            b.CampfirePrefab = assets.campfire;
            foreach (int seed in new[] { 1, 2, 3 })
            {
                b.Build(seed, ChapterDef.For(2));
                yield return null;
                Assert.AreEqual(4, b.Rooms.Count);
                Assert.IsTrue(b.Rooms.All(r => r.Chapter == 2), "only forest rooms: " + b.Plan.Signature);
                Assert.AreEqual("quills_glade", b.Rooms[1].ModuleId);
                var route = b.ChapterRoute();
                for (int i = 1; i < route.Count; i++)
                    Assert.Greater(route[i].Position.z, route[i - 1].Position.z - 0.01f, $"route runs forward (seed {seed}, node {i})");
                Assert.AreEqual(4, route.Count(n => n.Threshold), "one threshold per room");
                Assert.IsTrue(b.Rooms[1].GetComponentsInChildren<SpawnMarker>().Any(m => m.Archetype == "scout_quill"), "Mr. Quill's mark");
            }
            Object.Destroy(b.gameObject);
        }

        [UnityTest]
        public IEnumerator Catacombs_Build_Four_Crypt_Rooms_Ending_At_The_Vault()
        {
            var assets = HS.Core.GameAssets.Load();
            var b = new GameObject("Chapter").AddComponent<ChapterBuilder>();
            b.ModulePrefabs = assets.roomModules;
            b.StartCapPrefab = assets.Cap("crypt_start");
            b.EndCapPrefab = assets.Cap("crypt_end");
            Assert.IsNotNull(b.StartCapPrefab, "crypt caps registered");
            foreach (int seed in new[] { 1, 2, 3 })
            {
                b.Build(seed, ChapterDef.For(3));
                yield return null;
                Assert.AreEqual(4, b.Rooms.Count);
                Assert.IsTrue(b.Rooms.All(r => r.Chapter == 3), b.Plan.Signature);
                Assert.AreEqual("prisoners_cell", b.Rooms[1].ModuleId);
                Assert.AreEqual("sealed_vault", b.Rooms[3].ModuleId, "the way out is sealed");
                Assert.IsNotNull(b.Rooms[3].GetComponentInChildren<SealDoor>());
                var route = b.ChapterRoute();
                for (int i = 1; i < route.Count; i++)
                    Assert.Greater(route[i].Position.z, route[i - 1].Position.z - 0.01f, $"route runs forward (seed {seed}, node {i})");
            }
            Object.Destroy(b.gameObject);
        }

        static ChapterBuilder FromRegistry(string capStart, string capEnd, out bool own)
        {
            var assets = HS.Core.GameAssets.Load();
            var b = new GameObject("Chapter").AddComponent<ChapterBuilder>();
            b.ModulePrefabs = assets.roomModules;
            var start = assets.Cap(capStart);
            var end = assets.Cap(capEnd);
            own = start != null; // built by Tools/HS/Build/Chapters 4–5 (everything); until then it borrows the Old Road
            b.StartCapPrefab = start != null ? start : assets.startCap;
            b.EndCapPrefab = end != null ? end : assets.endCap;
            b.CampfirePrefab = assets.campfire;
            b.BossPrefab = assets.gallery != null ? assets.gallery : assets.boss;
            return b;
        }

        [UnityTest]
        public IEnumerator The_Sunken_Bastion_Builds_Four_Rooms_With_Wren_And_The_Sluice()
        {
            var b = FromRegistry("bastion_start", "bastion_end", out bool own);
            foreach (int seed in new[] { 1, 2, 3 })
            {
                b.Build(seed, ChapterDef.For(4));
                yield return null;
                Assert.AreEqual(4, b.Rooms.Count, b.Plan.Signature);
                Assert.IsNotNull(b.Campfire, "chapter 4 ends at the last campfire");
                var route = b.ChapterRoute();
                for (int i = 1; i < route.Count; i++)
                    Assert.Greater(route[i].Position.z, route[i - 1].Position.z - 0.01f, $"route runs forward (seed {seed}, node {i})");
                if (!own) continue;
                Assert.IsTrue(b.Rooms.All(r => r.Chapter == 4), b.Plan.Signature);
                Assert.AreEqual("wrens_rampart", b.Rooms[1].ModuleId);
                Assert.AreEqual("sluice_works", b.Rooms[2].ModuleId);
                Assert.IsNotNull(b.Rooms[2].GetComponentInChildren<SluiceWheel>(true), "the split threat");
                Assert.IsTrue(b.Rooms[1].GetComponentsInChildren<SpawnMarker>(true).Any(m => m.Archetype == "scout_wren"), "Darian Wren's mark");
            }
            Object.Destroy(b.gameObject);
        }

        [UnityTest]
        public IEnumerator The_Gallery_Approach_Is_The_Hall_Then_The_Long_Gallery()
        {
            var b = FromRegistry("gallery_start", "gallery_end", out bool own);
            b.Build(1, ChapterDef.For(5));
            yield return null;
            Assert.AreEqual(2, b.Rooms.Count);
            Assert.IsNull(b.Campfire, "the Gallery ends at the door");
            Assert.IsNotNull(b.Boss);
            Assert.IsNotNull(b.Boss.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "OathCircle"), "the oath circle");
            if (own)
            {
                Assert.AreEqual("hall_of_exhibits", b.Rooms[0].ModuleId);
                Assert.AreEqual("long_gallery", b.Rooms[1].ModuleId);
                Assert.AreEqual(6, b.Boss.GetComponentsInChildren<SpawnMarker>(true).Count(m => m.Archetype == "archer"), "six hidden archers");
                foreach (var niche in new[] { "MirrorNiche_E", "MirrorNiche_W" })
                    Assert.IsNotNull(b.Boss.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == niche), niche);
                Assert.AreEqual(2, b.Boss.GetComponentsInChildren<HS.Skills.ArmableProp>(true).Length, "a prop over each niche");
            }
            Object.Destroy(b.gameObject);
        }
    }
}
