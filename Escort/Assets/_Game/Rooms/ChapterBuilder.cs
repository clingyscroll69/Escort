using System.Collections.Generic;
using System.Linq;
using HS.Core;
using HS.Hero;
using UnityEngine;

namespace HS.Rooms
{
    /// <summary>
    /// Runtime assembly of a chapter from the seeded plan (GDD §3): modules chained along +Z (exit of room i = entry of
    /// room i+1), variants applied, caps at both ends, static batching per room, and a concatenated hero route.
    /// The campfire and boss arena live off to the side and are reached by a fade transition.
    /// </summary>
    public sealed class ChapterBuilder : MonoBehaviour
    {
        public GameObject[] ModulePrefabs;
        public GameObject StartCapPrefab, EndCapPrefab, CampfirePrefab, BossPrefab;
        public Vector3 CampfireOrigin = new Vector3(300f, 0f, 0f);
        public Vector3 BossOrigin = new Vector3(400f, 0f, 0f);

        public ChapterPlan Plan { get; private set; }
        public readonly List<RoomModule> Rooms = new List<RoomModule>();
        public RoomModule Campfire { get; private set; }
        public RoomModule Boss { get; private set; }
        public float ChapterLength { get; private set; }

        public List<ModuleInfo> Library() => ModulePrefabs.Where(p => p != null).Select(p => p.GetComponent<RoomModule>())
            .Select(m => new ModuleInfo { Id = m.ModuleId, Kind = m.Kind, Variants = m.VariantCount }).ToList();

        public void Build(int seed, ChapterDef chapter = null)
        {
            Clear();
            chapter ??= ChapterDef.OldRoad();
            Plan = RoomAssembler.Plan(seed, chapter, Library());
            float z = 0f;
            for (int i = 0; i < Plan.Rooms.Count; i++)
            {
                var pr = Plan.Rooms[i];
                var prefab = ModulePrefabs.First(p => p != null && p.GetComponent<RoomModule>().ModuleId == pr.ModuleId);
                var go = Instantiate(prefab, new Vector3(0f, 0f, z), Quaternion.identity, transform);
                go.name = $"Room{i}_{pr.ModuleId}";
                var m = go.GetComponent<RoomModule>();
                m.ApplyVariant(pr.Variant);
                m.SetRoomIndex(i);
                CombineStatic(go);
                Rooms.Add(m);
                z += m.Length;
            }
            ChapterLength = z;
            if (StartCapPrefab != null) CombineStatic(Instantiate(StartCapPrefab, Vector3.zero, Quaternion.identity, transform));
            if (EndCapPrefab != null) CombineStatic(Instantiate(EndCapPrefab, new Vector3(0f, 0f, z), Quaternion.identity, transform));
            if (CampfirePrefab != null)
            {
                Campfire = Instantiate(CampfirePrefab, CampfireOrigin, Quaternion.identity, transform).GetComponent<RoomModule>();
                Campfire.SetRoomIndex(100);
                CombineStatic(Campfire.gameObject);
            }
            if (BossPrefab != null)
            {
                Boss = Instantiate(BossPrefab, BossOrigin, Quaternion.identity, transform).GetComponent<RoomModule>();
                Boss.SetRoomIndex(200);
                CombineStatic(Boss.gameObject);
            }
        }

        /// <summary>Static-batch the scenery only: anything under an <see cref="IDynamicVisual"/> must stay movable.</summary>
        public static void CombineStatic(GameObject root)
        {
            var gos = new List<GameObject>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.GetComponentInParent<IDynamicVisual>(true) == null) gos.Add(mf.gameObject);
            if (gos.Count > 0) StaticBatchingUtility.Combine(gos.ToArray(), root);
        }

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(c);
                else DestroyImmediate(c);
            }
            Rooms.Clear();
            Campfire = null;
            Boss = null;
        }

        /// <summary>Hero route through every chapter room in order.</summary>
        public List<RouteNode> ChapterRoute()
        {
            var nodes = new List<RouteNode>();
            foreach (var r in Rooms)
                if (r.Route != null) nodes.AddRange(r.Route.Nodes());
            return nodes;
        }

        public int RoomIndexAt(float z)
        {
            for (int i = 0; i < Rooms.Count; i++)
            {
                float z0 = Rooms[i].transform.position.z;
                if (z >= z0 && z < z0 + Rooms[i].Length) return i;
            }
            return z < 0f ? 0 : Rooms.Count - 1;
        }
    }
}
