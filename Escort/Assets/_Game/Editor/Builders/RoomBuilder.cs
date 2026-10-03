using System.Collections.Generic;
using System.IO;
using HS.Core;
using HS.Rooms;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static HS.EditorTools.RoomKit;

namespace HS.EditorTools
{
    /// <summary>
    /// Authors the Chapter 1 "Old Road" room modules (GDD §3, §11.2) plus the campfire clearing and the rigged-duel
    /// arena as prefabs. Layouts are data in code (reproducible, reviewable); decoration uses a fixed per-room seed so
    /// rebuilds are identical — the run seed only chooses modules/variants at runtime.
    /// </summary>
    public static class RoomBuilder
    {
        public const string Dir = "Assets/_Game/Prefabs/Rooms";

        static Material _grass, _dirt, _flag;

        [MenuItem("Tools/HS/Build/Rooms")]
        public static void BuildAll()
        {
            ResetIndex();
            Directory.CreateDirectory(Dir);
            _grass = GroundMat("Env_Ground_Grass", "T_Ground_Grass", Color.white);
            _dirt = GroundMat("Env_Road_Dirt", "T_Ground_Dirt", Color.white);
            _flag = GroundMat("Env_Floor_Flagstone", "T_Ground_Flagstone", new Color(0.92f, 0.9f, 0.86f));
            var built = new List<string>
            {
                Save(CrossroadsShrine()), Save(TollGate()), Save(RuinedGatehouse()), Save(WagonCamp()),
                Save(Campfire()), Save(RiggedDuel()), Save(RoadCap(true)), Save(RoadCap(false)),
            };
            AssetDatabase.SaveAssets();
            HS.Agent.AgentBridge.Write("builder.json", $"{{\"ok\":true,\"msg\":\"rooms: {string.Join(",", built)}\"}}");
        }

        static string Save(GameObject root)
        {
            string name = root.name;
            PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/{name}.prefab");
            Object.DestroyImmediate(root);
            return name;
        }

        static (GameObject root, RoomModule mod) NewRoom(string id, string display, RoomKind kind, float length, float width = 26f)
        {
            var root = new GameObject(id);
            var mod = root.AddComponent<RoomModule>();
            mod.ModuleId = id;
            mod.DisplayName = display;
            mod.Kind = kind;
            mod.Length = length;
            mod.Width = width;
            return (root, mod);
        }

        static Transform Variant(Transform root, int i)
        {
            var v = root.Find("Variants") ?? Empty(root, "Variants", Vector3.zero).transform;
            var existing = v.Find("Variant_" + i);
            return existing != null ? existing : Empty(v, "Variant_" + i, Vector3.zero).transform;
        }

        static SpawnMarker Spawn(Transform parent, string archetype, Vector3 pos, float rotY, bool hidden = false, bool elevated = false, float delay = 0f, int group = 0)
        {
            var m = Marker<SpawnMarker>(parent, "Spawn_" + archetype, pos, rotY);
            m.Archetype = archetype;
            m.Hidden = hidden;
            m.Elevated = elevated;
            m.Delay = delay;
            m.Group = group;
            return m;
        }

        static void Encounter(Transform parent, Vector3 pos, float radius, int group = 0)
        {
            var e = Marker<EncounterZone>(parent, "Encounter_" + group, pos);
            e.Radius = radius;
            e.Group = group;
        }

        static void Explore(Transform parent, Vector3 pos, float rotY, string model, string title, string note)
        {
            var e = Marker<ExploreAnchor>(parent, "Explore", pos, rotY);
            e.Title = title;
            e.Note = note;
            Place(e.transform, model, Vector3.zero, 0f, 1f, Col.None);
        }

        static void Armable(Transform parent, ArmableKind kind, Vector3 pos, float rotY, Vector3 impactOffset, float radius)
        {
            var a = Marker<ArmableAnchor>(parent, "Armable_" + kind, pos, rotY);
            a.gameObject.AddComponent<HS.Skills.ArmableProp>();
            a.Kind = kind;
            a.ImpactOffset = impactOffset;
            a.ImpactRadius = radius;
            switch (kind)
            {
                case ArmableKind.FallingTree:
                    var tree = Place(a.transform, "DeadTree_1", Vector3.zero, 0f, 0.62f, Col.None);
                    tree.name = "Visual";
                    tree.transform.localRotation = Quaternion.Euler(0f, 0f, -24f); // leaning over the road
                    break;
                case ArmableKind.BarrelStack:
                {
                    var v = Empty(a.transform, "Visual", Vector3.zero).transform;
                    Place(v, "Barrel_Holder", Vector3.zero, 90f, 1f, Col.Box);
                    Place(v, "Barrel", new Vector3(-0.35f, 1.1f, 0f), 0f, 1f);
                    Place(v, "Barrel", new Vector3(0.35f, 1.1f, 0f), 30f, 1f);
                    Place(v, "Barrel", new Vector3(0f, 1.95f, 0f), 60f, 1f);
                    Place(v, "Rope_2", new Vector3(0f, 0.05f, 0.7f), 0f, 1f);
                    break;
                }
                case ArmableKind.LooseMasonry:
                {
                    var v = Empty(a.transform, "Visual", Vector3.zero).transform;
                    for (int i = 0; i < 6; i++)
                        Place(v, "Prop_Brick" + (1 + i % 4), new Vector3(-0.2f + 0.15f * (i % 3), 3.2f + 0.24f * (i / 3), -0.4f + 0.3f * (i % 2)), i * 37f, 1.4f);
                    Place(v, "Prop_Support", new Vector3(0f, 1.2f, 0f), 90f, 0.9f);
                    break;
                }
                case ArmableKind.LogPile:
                {
                    var v = Empty(a.transform, "Visual", Vector3.zero).transform;
                    for (int i = 0; i < 5; i++)
                    {
                        var log = Place(v, "Roof_Log", new Vector3(0f, 0.25f + 0.42f * (i / 3), -0.7f + 0.46f * (i % 3) + 0.23f * (i / 3)), 0f, 0.32f);
                        log.transform.localRotation = Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(90f, 0f, 0f);
                    }
                    break;
                }
            }
            Unstatic(a.gameObject);
        }

        /// <summary>Moving parts must not be static-batched (the prop tips over when it collapses).</summary>
        static void Unstatic(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        }

        static void Stone(Transform parent, Vector3 pos, float rotY, float range = 14f)
        {
            var s = Marker<StoneAnchor>(parent, "StoneAnchor", pos, rotY);
            s.ViewRange = range;
            // The stone itself: a breakable agent (knife/bolt) that wakes for the hero's duels (see ChronicleStone).
            Place(s.transform, "ChronicleStone", Vector3.zero, 0f, 1f, Col.None, true, false);
            var cc = s.gameObject.AddComponent<CharacterController>();
            cc.radius = 0.45f;
            cc.height = 1.7f;
            cc.center = new Vector3(0f, 0.85f, 0f);
            s.gameObject.AddComponent<ChronicleStone>();
        }

        static void Hazard(Transform parent, HazardKind kind, Vector3 pos, float rotY = 0f, float span = 4f)
        {
            var h = Marker<HazardMarker>(parent, "Hazard_" + kind, pos, rotY);
            h.Kind = kind;
            h.Span = span;
            Place(h.transform, kind == HazardKind.SpikePlate ? "SpikePlate" : "TripwireStakes", Vector3.zero, 0f, kind == HazardKind.Tripwire ? span / 4f : 1f, Col.None, true, false);
        }

        static List<Vector3> V(params float[] xz)
        {
            var l = new List<Vector3>();
            for (int i = 0; i < xz.Length; i += 2) l.Add(new Vector3(xz[i], 0f, xz[i + 1]));
            return l;
        }

        static void Base(Transform root, float length, List<Vector3> road, int seed, float innerX = 10.5f, int scatter = 70)
        {
            Ground(root, "Ground", new Vector3(0f, 0f, length * 0.5f), 48f, length, _grass);
            Road(root, "Road", road, 3.6f, _dirt);
            var rng = new DetRandom(seed);
            ForestBorders(root, length, innerX, rng);
            RoadEdges(root, road, 1.8f, rng);
            Scatter(root, new Rect(-innerX + 0.5f, 1f, innerX * 2f - 1f, length - 2f), scatter, road, 2.6f, rng);
        }

        // =====================================================================================================
        // Room 1 — Crossroads Shrine (Ambush): hedges hide cheaters; a chronicle stone watches the crossroads.
        // =====================================================================================================
        static GameObject CrossroadsShrine()
        {
            var (root, mod) = NewRoom("crossroads_shrine", "Crossroads Shrine", RoomKind.Ambush, 42f);
            var t = root.transform;
            var road = V(0, 0, 0.5f, 8, -0.4f, 15, 0, 21, 0.8f, 28, 0.2f, 35, 0, 42);
            Base(t, 42f, road, 101);
            Road(t, "SideRoad", V(0, 21, -6, 21.8f, -10.5f, 21.3f), 2.6f, _dirt);
            var set = Empty(t, "Set", Vector3.zero).transform;
            // Shrine: flagstones, candles, the stone itself (see StoneAnchor)
            Place(set, "Floor_UnevenBrick", new Vector3(6.6f, 0.01f, 20.6f), 0f, 1.1f);
            Place(set, "CandleStick_Stand", new Vector3(5.4f, 0f, 19.4f), 0f, 0.9f, Col.Round);
            Place(set, "CandleStick_Stand", new Vector3(7.8f, 0f, 19.6f), 40f, 0.9f, Col.Round);
            Place(set, "Banner_1", new Vector3(-2.6f, 2.4f, 18.6f), 90f, 1f);
            Place(set, "Banner_1_Cloth", new Vector3(-2.6f, 2.4f, 18.6f), 90f, 1f, Col.None, true);
            // Broken fence along the side road
            for (int i = 0; i < 4; i++) Place(set, i % 2 == 0 ? "Prop_WoodenFence_Single" : "Prop_WoodenFence_Extension1", new Vector3(-2.6f - 2.05f * i, 0f, 23.3f), 0f, 1f, Col.Box);
            Place(set, "Prop_WoodenFence_Extension2", new Vector3(-4.8f, 0f, 19.2f), 12f, 1f, Col.Box);
            // Hedges where the ambushers lie in wait
            foreach (var p in new[] { new Vector3(-4.6f, 0, 26.2f), new Vector3(-5.1f, 0, 28.3f), new Vector3(-4.7f, 0, 30.4f), new Vector3(4.9f, 0, 28.1f), new Vector3(5.2f, 0, 30.2f), new Vector3(4.8f, 0, 32.3f) })
                Place(set, "Bush_Common", p, p.z * 37f, 1.25f, Col.None, false);
            Armable(t, ArmableKind.FallingTree, new Vector3(-7.4f, 0f, 27.3f), 0f, new Vector3(6.6f, 0f, 0.2f), 3.2f);
            Stone(t, new Vector3(6.6f, 0f, 21.2f), -90f);
            Explore(t, new Vector3(-9.2f, 0f, 8.2f), 30f, "Bag", "A traveller's pack", "Half a loaf, a map with the crossroads circled, and a note: 'Don't stop for the man in the road.'");
            Encounter(t, new Vector3(0f, 0f, 28.5f), 11f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            Spawn(enc, "thug", new Vector3(0.6f, 0f, 33.2f), 180f);
            Spawn(enc, "ambusher", new Vector3(-4.9f, 0f, 28.3f), 90f, hidden: true);
            Spawn(enc, "ambusher", new Vector3(5.1f, 0f, 30.2f), -90f, hidden: true);
            var v0 = Variant(t, 0);
            Spawn(v0, "turncoat", new Vector3(3.2f, 0f, 35.8f), 200f);
            var v1 = Variant(t, 1);
            Spawn(v1, "crossbowman", new Vector3(8.6f, 0f, 36.4f), 225f, elevated: true);
            Place(v1, "Crate_Wooden", new Vector3(7.4f, 0f, 34.6f), 10f, 1f, Col.Box);
            Place(v1, "Crate_Wooden", new Vector3(8.5f, 0f, 34.3f), -8f, 1f, Col.Box);
            Place(v1, "Crate_Wooden", new Vector3(8.0f, 0.93f, 34.5f), 25f, 1f, Col.Box);
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0.4f, 0, 9), false, false, ""),
                (new Vector3(0, 0, 19.2f), true, true, "the crossroads"), (new Vector3(0.6f, 0, 26), false, false, ""),
                (new Vector3(0.5f, 0, 34), false, false, ""), (new Vector3(0, 0, 41.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // Room 2 — Toll Gate (Combat): a barricade with a gap; crossbowmen shoot from behind it during duels.
        // =====================================================================================================
        static GameObject TollGate()
        {
            var (root, mod) = NewRoom("toll_gate", "The Toll Gate", RoomKind.Combat, 40f);
            var t = root.transform;
            var road = V(0, 0, -0.4f, 10, 0.3f, 20, 0, 26, -0.3f, 33, 0, 40);
            Base(t, 40f, road, 202);
            var set = Empty(t, "Set", Vector3.zero).transform;
            // Barricade line at z = 26 with a 4.4 m gate in the middle and flanking gaps at the forest edge.
            Place(set, "Wall_UnevenBrick_Straight", new Vector3(-2.7f, 0f, 26f), 90f, 1f, Col.Box);
            Place(set, "Wall_UnevenBrick_Straight", new Vector3(2.7f, 0f, 26f), 90f, 1f, Col.Box);
            Place(set, "Banner_1", new Vector3(-2.7f, 2.9f, 25.3f), 180f, 1f);
            Place(set, "Banner_1_Cloth", new Vector3(-2.7f, 2.9f, 25.3f), 180f, 1f);
            Place(set, "Prop_Wagon", new Vector3(-6.2f, 0f, 26.6f), 95f, 1f, Col.Box);
            Place(set, "Crate_Wooden", new Vector3(-3.9f, 0f, 26.4f), 12f, 1f, Col.Box);
            Place(set, "Prop_WoodenFence_Single", new Vector3(-8.9f, 0f, 26.2f), 0f, 1f, Col.Box);
            Place(set, "Stall_Empty", new Vector3(4.6f, 0f, 26.6f), 180f, 1f, Col.Box);
            Place(set, "Crate_Wooden", new Vector3(6.4f, 0f, 26.1f), -15f, 1f, Col.Box);
            Place(set, "Barrel", new Vector3(7.3f, 0f, 26.9f), 0f, 1f, Col.Round);
            Place(set, "Prop_WoodenFence_Extension1", new Vector3(8.9f, 0f, 26.2f), 0f, 1f, Col.Box);
            Place(set, "Table_Large", new Vector3(4.8f, 0f, 22.8f), 90f, 0.8f, Col.Box);
            Place(set, "Coin_Pile", new Vector3(4.8f, 0.66f, 22.8f), 0f, 1.4f);
            Place(set, "Mug", new Vector3(4.6f, 0.66f, 23.4f), 0f, 1.4f);
            Armable(t, ArmableKind.BarrelStack, new Vector3(-7.8f, 0f, 21.8f), 0f, new Vector3(5.2f, 0f, 1.2f), 3f);
            Stone(t, new Vector3(7f, 0f, 21.6f), 225f);
            Explore(t, new Vector3(9.2f, 0f, 11.5f), -40f, "Chest_Wood", "Smuggler's strongbox", "Tolls collected: nine silver. Tolls paid to 'the collector': eight. Someone upstream is keeping accounts.");
            Encounter(t, new Vector3(0f, 0f, 23.5f), 11f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            Spawn(enc, "brute", new Vector3(0f, 0f, 24.6f), 180f);
            Spawn(enc, "crossbowman", new Vector3(-4.8f, 0f, 28.6f), 190f, elevated: true);
            Spawn(enc, "crossbowman", new Vector3(5.6f, 0f, 28.8f), 170f, elevated: true);
            Spawn(Variant(t, 0), "thug", new Vector3(-2.1f, 0f, 21.8f), 170f);
            Spawn(Variant(t, 1), "turncoat", new Vector3(2.2f, 0f, 21.6f), 190f);
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(-0.3f, 0, 8), false, false, ""),
                (new Vector3(0, 0, 15.2f), true, true, "the toll gate"), (new Vector3(0.2f, 0, 21), false, false, ""),
                (new Vector3(0, 0, 27), false, false, ""), (new Vector3(-0.2f, 0, 33), false, false, ""), (new Vector3(0, 0, 39.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // Room 3 — Ruined Gatehouse (Trap corridor): spike plates, a tripwire, loose masonry, a crossbowman on rubble.
        // =====================================================================================================
        static GameObject RuinedGatehouse()
        {
            var (root, mod) = NewRoom("ruined_gatehouse", "The Ruined Gatehouse", RoomKind.TrapCorridor, 44f);
            var t = root.transform;
            var road = V(0, 0, 0.2f, 9, 0.3f, 16, -0.2f, 22, 0.2f, 29, 0, 36, 0, 44);
            Base(t, 44f, road, 303, 10.5f, 55);
            var set = Empty(t, "Set", Vector3.zero).transform;
            // Corridor walls along x = ±3.6 from z = 10..34 with gaps (flanking routes / alcove).
            var skipL = new HashSet<int> { 17, 29 };
            var skipR = new HashSet<int> { 21, 31 };
            for (int z = 11; z <= 33; z += 2)
            {
                if (!skipL.Contains(z)) Place(set, "Wall_UnevenBrick_Straight", new Vector3(-3.7f, 0f, z), 90f, 1f, Col.Box);
                else Place(set, "Prop_Brick" + (1 + z % 4), new Vector3(-3.7f, 0.1f, z), z * 17f, 1.6f);
                if (!skipR.Contains(z)) Place(set, z % 6 == 3 ? "Wall_UnevenBrick_Window_Wide_Round" : "Wall_UnevenBrick_Straight", new Vector3(3.7f, 0f, z), -90f, 1f, Col.Box);
                else Place(set, "Prop_Brick" + (1 + z % 4), new Vector3(3.7f, 0.1f, z), z * 23f, 1.6f);
            }
            Place(set, "Prop_Vine1", new Vector3(-3.45f, 3f, 14f), 90f, 1f);
            Place(set, "Prop_Vine2", new Vector3(3.45f, 3f, 26f), -90f, 1f);
            // Ruined gate: two pillars and a log lintel at z = 22
            Place(set, "Corner_ExteriorWide_Brick", new Vector3(-1.9f, 0f, 22f), 0f, 1.2f, Col.Box);
            Place(set, "Corner_ExteriorWide_Brick", new Vector3(1.9f, 0f, 22f), 90f, 1.2f, Col.Box);
            var lintel = Place(set, "Roof_Log", new Vector3(0f, -1.3f, 22.1f), 0f, 0.36f);
            lintel.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            // Rubble mound the crossbowman stands behind. The rock colliders are big boxes (~6 m across), so he stands on open
            // ground north of them, covering the corridor mouth — not inside them, where he'd be shoved up onto an unreachable
            // invisible perch that blocks both the knife and bolts.
            Place(set, "Rock_Medium_2", new Vector3(6.4f, 0f, 29.6f), 30f, 0.8f, Col.Box);
            Place(set, "Rock_Medium_1", new Vector3(8.2f, 0f, 30.8f), 110f, 0.7f, Col.Box);
            // Alcove with a cache behind the left wall gap at z = 17
            Place(set, "Wall_UnevenBrick_Straight", new Vector3(-6.8f, 0f, 15.2f), 0f, 1f, Col.Box);
            Place(set, "Wall_UnevenBrick_Straight", new Vector3(-6.8f, 0f, 19.2f), 0f, 1f, Col.Box);
            Explore(t, new Vector3(-6.3f, 0f, 17.2f), 90f, "Chest_Wood", "Gatekeeper's ledger", "Last entry: 'Bolts loosened on the east wall. Ask the knight to walk under it.' The ink is fresh.");
            Hazard(t, HazardKind.SpikePlate, new Vector3(0.7f, 0.01f, 16.2f));
            Hazard(t, HazardKind.SpikePlate, new Vector3(-0.8f, 0.01f, 27.6f));
            Hazard(t, HazardKind.Tripwire, new Vector3(0f, 0f, 31.2f), 0f, 6.4f);
            Armable(t, ArmableKind.LooseMasonry, new Vector3(3.7f, 0f, 24.6f), 0f, new Vector3(-3.2f, 0f, 0.4f), 2.6f);
            Stone(t, new Vector3(-6.2f, 0f, 36.5f), 120f);
            Encounter(t, new Vector3(0f, 0f, 32f), 12.5f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            Spawn(enc, "crossbowman", new Vector3(6.4f, 0f, 33.8f), 290f, elevated: true);
            Spawn(enc, "thug", new Vector3(0f, 0f, 38.4f), 180f);
            var v1 = Variant(t, 1);
            Spawn(v1, "thug", new Vector3(-2.6f, 0f, 39.6f), 170f);
            Spawn(v1, "crossbowman", new Vector3(-6.6f, 0f, 33.8f), 70f, elevated: true);
            Place(v1, "Rock_Medium_3", new Vector3(-6.2f, 0f, 30.2f), 60f, 0.7f, Col.Box);
            Variant(t, 0); // base layout only
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0.2f, 0, 8.6f), true, true, "the gatehouse"),
                (new Vector3(0.3f, 0, 15), false, false, ""), (new Vector3(-0.2f, 0, 22), false, true, ""),
                (new Vector3(0.2f, 0, 29), false, false, ""), (new Vector3(0, 0, 36), false, false, ""), (new Vector3(0, 0, 43.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // Room 4 — Wagon Camp (Combat): a bandit camp around a cook-fire; a stone records the brawl.
        // =====================================================================================================
        static GameObject WagonCamp()
        {
            var (root, mod) = NewRoom("wagon_camp", "The Wagon Camp", RoomKind.Combat, 42f);
            var t = root.transform;
            var road = V(0, 0, 0.4f, 10, -0.5f, 18, 0, 24, 0.6f, 31, 0, 42);
            Base(t, 42f, road, 404);
            var set = Empty(t, "Set", Vector3.zero).transform;
            Place(set, "Prop_Wagon", new Vector3(-6.8f, 0f, 24.5f), 70f, 1f, Col.Box);
            Place(set, "Prop_Wagon", new Vector3(6.8f, 0f, 26.5f), -110f, 1f, Col.Box);
            Place(set, "Prop_Wagon", new Vector3(-3.4f, 0f, 34.2f), 15f, 1f, Col.Box);
            Place(set, "Table_Large", new Vector3(4.6f, 0f, 32.8f), 20f, 0.85f, Col.Box);
            Place(set, "Mug", new Vector3(4.4f, 0.7f, 32.7f), 0f, 1.4f);
            Place(set, "Pot_1", new Vector3(4.9f, 0.7f, 33.0f), 0f, 1f);
            Place(set, "Crate_Wooden", new Vector3(8.4f, 0f, 30.8f), 20f, 1f, Col.Box);
            Place(set, "Crate_Wooden", new Vector3(-8.6f, 0f, 29.4f), -10f, 1f, Col.Box);
            Place(set, "Barrel_Apples", new Vector3(-7.6f, 0f, 30.6f), 0f, 1f, Col.Round);
            Place(set, "Cage_Small", new Vector3(9.3f, 0f, 36.3f), 30f, 1.2f, Col.Box);
            // cook fire
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                Place(set, "Pebble_Round_" + (1 + i % 5), new Vector3(1.6f + Mathf.Cos(a) * 0.8f, 0f, 29f + Mathf.Sin(a) * 0.8f), i * 40f, 1.8f);
            }
            Place(set, "Cauldron", new Vector3(1.6f, 0f, 29f), 0f, 0.8f, Col.Round);
            Armable(t, ArmableKind.BarrelStack, new Vector3(-8.2f, 0f, 19.6f), 0f, new Vector3(5.2f, 0f, 3.2f), 3f);
            Stone(t, new Vector3(-9.2f, 0f, 30f), 90f);
            Explore(t, new Vector3(9.3f, 0f, 38.2f), -30f, "Scroll_1", "A folded bounty", "'Wanted: the knight Callum, alive. Payment on delivery, in the Gallery.' Signed only with a dull grey thumbprint.");
            Encounter(t, new Vector3(0f, 0f, 26.5f), 11f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            Spawn(enc, "brute", new Vector3(0.5f, 0f, 26.4f), 180f);
            Spawn(enc, "thug", new Vector3(-4f, 0f, 28.2f), 150f);
            Spawn(enc, "thug", new Vector3(4.4f, 0f, 29.6f), 210f);
            Spawn(Variant(t, 0), "turncoat", new Vector3(2f, 0f, 31.8f), 180f);
            Spawn(Variant(t, 1), "crossbowman", new Vector3(6.6f, 0f, 30.4f), 215f, elevated: true); // behind the wagon, not on it
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0.3f, 0, 7), false, false, ""),
                (new Vector3(0, 0, 14.2f), true, true, "the camp"), (new Vector3(0, 0, 21), false, false, ""),
                (new Vector3(0.3f, 0, 28), false, false, ""), (new Vector3(0.4f, 0, 35), false, false, ""), (new Vector3(0, 0, 41.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // Road caps: forest closes behind the start and swallows the road past the end (no void at the edges).
        // =====================================================================================================
        static GameObject RoadCap(bool start)
        {
            var root = new GameObject(start ? "road_cap_start" : "road_cap_end");
            var t = root.transform;
            float z0 = start ? -16f : 0f;
            Ground(t, "Ground", new Vector3(0f, 0f, z0 + 8f), 48f, 16f, _grass);
            var rng = new DetRandom(start ? 707 : 808);
            var road = start ? V(0, -8, 0, 0) : V(0, 0, 0, 7);
            Road(t, "Road", road, 3.6f, _dirt);
            var f = Empty(t, "Forest", Vector3.zero).transform;
            for (int i = 0; i < 26; i++)
            {
                float x = -20f + i * 1.6f + (float)rng.NextDouble();
                float z = start ? -9f - (float)rng.NextDouble() * 6f : 8f + (float)rng.NextDouble() * 6f;
                Place(f, i % 3 == 0 ? "Pine_" + (1 + i % 4) : "CommonTree_" + (1 + i % 5), new Vector3(x, 0f, z), rng.Range(0, 360), 0.9f + (float)rng.NextDouble() * 0.3f, Col.Trunk);
            }
            foreach (int side in new[] { -1, 1 })
                for (int k = 0; k < 4; k++)
                    Place(f, "CommonTree_" + (1 + k), new Vector3(side * (13f + (float)rng.NextDouble() * 4f), 0f, z0 + 2f + k * 3.6f), rng.Range(0, 360), 1f, Col.Trunk);
            Scatter(t, new Rect(-10f, z0 + 1f, 20f, 14f), 24, road, 2.2f, rng);
            if (start)
            {
                Place(t, "Banner_2", new Vector3(2.6f, 2.2f, -1.5f), 90f, 1f);
                Place(t, "Banner_2_Cloth", new Vector3(2.6f, 2.2f, -1.5f), 90f, 1f);
                Blocker(t, "Back", new Vector3(0f, 2f, -3.2f), new Vector3(30f, 4f, 1f));
            }
            else Blocker(t, "Front", new Vector3(0f, 2f, 3.5f), new Vector3(30f, 4f, 1f));
            return root;
        }

        // =====================================================================================================
        // Campfire (GDD §4.6): rest, cook, loadout, hero scene.
        // =====================================================================================================
        static GameObject Campfire()
        {
            var (root, mod) = NewRoom("campfire", "Campfire", RoomKind.Campfire, 22f, 22f);
            var t = root.transform;
            Ground(t, "Ground", new Vector3(0f, 0f, 11f), 44f, 30f, _grass);
            var rng = new DetRandom(505);
            var ring = Empty(t, "TreeRing", Vector3.zero).transform;
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f + 0.2f;
                float r = 9.5f + (float)rng.NextDouble() * 2.5f;
                var p = new Vector3(Mathf.Cos(a) * r, 0f, 11f + Mathf.Sin(a) * r);
                if (p.z < 4f && Mathf.Abs(p.x) < 5f) continue; // keep the camera side open
                Place(ring, i % 3 == 0 ? "Pine_" + (1 + i % 4) : "CommonTree_" + (1 + i % 5), p, rng.Range(0, 360), 0.9f + (float)rng.NextDouble() * 0.3f, Col.Trunk);
            }
            Scatter(t, new Rect(-8f, 4f, 16f, 14f), 40, V(0, 9, 0, 13), 2.2f, rng);
            var set = Empty(t, "Set", Vector3.zero).transform;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                Place(set, "Pebble_Round_" + (1 + i % 5), new Vector3(Mathf.Cos(a) * 0.75f, 0f, 11f + Mathf.Sin(a) * 0.75f), i * 45f, 2f);
            }
            for (int i = 0; i < 3; i++)
            {
                var log = Place(set, "Roof_Log", new Vector3(0f, 0.12f, 11f), 0f, 0.12f);
                log.transform.localRotation = Quaternion.Euler(0f, i * 60f, 0f) * Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(90f, 0f, 0f);
            }
            var heroLog = Place(set, "Roof_Log", new Vector3(-2.4f, 0.22f, 12.2f), 0f, 0.28f, Col.Box);
            heroLog.transform.localRotation = Quaternion.Euler(0f, 62f, 0f) * Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(90f, 0f, 0f);
            var sideLog = Place(set, "Roof_Log", new Vector3(2.4f, 0.22f, 9.6f), 0f, 0.28f, Col.Box);
            sideLog.transform.localRotation = Quaternion.Euler(0f, -60f, 0f) * Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(90f, 0f, 0f);
            Place(set, "Bucket_Wooden_1", new Vector3(3.4f, 0f, 8.4f), 30f, 1.2f);          // the reel-hook bucket
            Place(set, "Pot_1", new Vector3(0.9f, 0f, 10.2f), 0f, 1f);
            Place(set, "Bag", new Vector3(-4.2f, 0f, 13.8f), 60f, 1f);
            Place(set, "Bag", new Vector3(4.4f, 0f, 11.9f), -40f, 0.9f);
            Place(set, "Shield_Wooden", new Vector3(-3.4f, 0.3f, 14f), 20f, 1f);
            Place(set, "Whetstone", new Vector3(-4.6f, 0f, 11.2f), 70f, 0.6f, Col.Box);
            Empty(t, "HeroSeat", new Vector3(-2.2f, 0f, 12.3f), 118f);
            Empty(t, "SidekickSeat", new Vector3(2.3f, 0f, 9.4f), -64f);
            Empty(t, "Fire", new Vector3(0f, 0.25f, 11f));
            Empty(t, "CampCamera", new Vector3(0.4f, 3.1f, 3.2f), 0f);
            Blocker(t, "Ring", new Vector3(0f, 2f, 22.5f), new Vector3(30f, 4f, 1f));
            return root;
        }

        // =====================================================================================================
        // Boss — The Rigged Duel (GDD §6.1, §11.2): octagon, Oath Glyph circle, gallery of hidden archers.
        // =====================================================================================================
        static GameObject RiggedDuel()
        {
            var (root, mod) = NewRoom("rigged_duel", "The Rigged Duel", RoomKind.Boss, 30f, 28f);
            var t = root.transform;
            const float cz = 15f, R = 12f;
            Ground(t, "Ground", new Vector3(0f, 0f, 15f), 48f, 34f, _grass);
            Ground(t, "Floor", new Vector3(0f, 0.01f, cz), 2f * R, 2f * R, _flag, 4f).GetComponent<BoxCollider>().enabled = false;
            var walls = Empty(t, "Walls", Vector3.zero).transform;
            float side = 2f * R * Mathf.Tan(Mathf.PI / 8f);
            for (int s = 0; s < 8; s++)
            {
                float a = s * Mathf.PI / 4f; // s=0 → +X side ... s=6 → -Z (south, entry)
                var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var along = Vector3.Cross(Vector3.up, n);
                var center = new Vector3(0f, 0f, cz) + n * R;
                int pieces = Mathf.RoundToInt(side / 2f);
                for (int k = 0; k < pieces; k++)
                {
                    float off = (k - (pieces - 1) * 0.5f) * 2f;
                    if (s == 6 && Mathf.Abs(off) < 1.5f) continue; // entry gate
                    float yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg + 90f;
                    // The three sides nearest the fixed camera (SW, S, SE) are low walls so they never hide the floor.
                    bool nearSide = s >= 5;
                    var w = Place(walls, !nearSide && (k + s) % 4 == 1 ? "Wall_UnevenBrick_Window_Wide_Round" : "Wall_UnevenBrick_Straight", center + along * off, yaw, 1f, Col.Box);
                    if (nearSide) w.transform.localScale = new Vector3(1f, 0.38f, 1f);
                }
            }
            // Gallery perches (the hidden archers): N, NE, NW, 2.2 m up with stairs and a ramp collider.
            var gal = Empty(t, "Gallery", Vector3.zero).transform;
            foreach (var (label, ang) in new[] { ("N", 90f), ("NE", 55f), ("NW", 125f) })
            {
                float a = ang * Mathf.Deg2Rad;
                var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var p = new Vector3(0f, 0f, cz) + n * (R - 1.6f);
                float yaw = Mathf.Atan2(-n.x, -n.z) * Mathf.Rad2Deg;
                var perch = Empty(gal, "Perch_" + label, p, yaw).transform;
                Place(perch, "Floor_WoodDark", new Vector3(0f, 2.2f, 0f), 0f, 1.2f);
                Place(perch, "Floor_WoodDark", new Vector3(0f, 2.2f, -0.4f), 0f, 1.2f);
                var top = perch.gameObject.AddComponent<BoxCollider>();
                top.center = new Vector3(0f, 2.05f, 0f);
                top.size = new Vector3(2.4f, 0.3f, 2.6f);
                Place(perch, "Prop_Support", new Vector3(-1f, 0f, 0.8f), 0f, 1.1f);
                Place(perch, "Prop_Support", new Vector3(1f, 0f, 0.8f), 0f, 1.1f);
                Place(perch, "Balcony_Simple_Straight", new Vector3(0f, 2.2f, 0.2f), 180f, 1.1f);
                // stairs down the side, with a ramp collider for the CharacterController
                Place(perch, "Stairs_Exterior_Straight", new Vector3(2.2f, 1.0f, -0.6f), 90f, 1f);
                Place(perch, "Stairs_Exterior_Straight", new Vector3(4.2f, 0f, -0.6f), 90f, 1f);
                var ramp = Empty(perch, "Ramp", new Vector3(3.2f, 1.05f, -0.6f)).AddComponent<BoxCollider>();
                ramp.transform.localRotation = Quaternion.Euler(0f, 0f, 28f);
                ramp.size = new Vector3(4.6f, 0.2f, 1.9f);
                Spawn(perch, "archer", new Vector3(0f, 2.25f, 0f), 0f, hidden: true, elevated: true);
                Place(perch, "Torch_Metal", new Vector3(-1.2f, 3.1f, 1.1f), 0f, 1f);
            }
            // The Oath Glyph circle (radius 5) and duel marks
            Empty(t, "OathCircle", new Vector3(0f, 0.03f, cz));
            Empty(t, "AshgraveMark", new Vector3(0f, 0f, cz + 3.2f), 180f);
            Empty(t, "CallumMark", new Vector3(0f, 0f, cz - 3.2f), 0f);
            Empty(t, "SidekickOut", new Vector3(-6.5f, 0f, cz - 7.5f), 30f);
            Stone(t, new Vector3(8.6f, 0f, cz + 6.8f), 225f, 16f);
            var deco = Empty(t, "Deco", Vector3.zero).transform;
            // Banners hang on the tall north wall behind the gallery (the near walls are low for the camera).
            Place(deco, "Banner_2", new Vector3(-3.2f, 3f, cz + R - 0.35f), 180f, 1f);
            Place(deco, "Banner_2_Cloth", new Vector3(-3.2f, 3f, cz + R - 0.35f), 180f, 1f);
            Place(deco, "Banner_2", new Vector3(3.2f, 3f, cz + R - 0.35f), 180f, 1f);
            Place(deco, "Banner_2_Cloth", new Vector3(3.2f, 3f, cz + R - 0.35f), 180f, 1f);
            Place(deco, "WeaponStand", new Vector3(-8.2f, 0f, cz - 2.5f), 90f, 1f, Col.Box);
            Place(deco, "CandleStick_Stand", new Vector3(5.5f, 0f, cz - 1f), 0f, 1f, Col.Round);
            Place(deco, "CandleStick_Stand", new Vector3(-5.5f, 0f, cz - 1f), 0f, 1f, Col.Round);
            var rng = new DetRandom(606);
            var outer = Empty(t, "Outer", Vector3.zero).transform;
            for (int i = 0; i < 14; i++)
            {
                float a = i * Mathf.PI * 2f / 14f;
                var p = new Vector3(Mathf.Cos(a) * (R + 4.5f), 0f, cz + Mathf.Sin(a) * (R + 4.5f));
                if (p.z < 4f && Mathf.Abs(p.x) < 6f) continue;
                Place(outer, "Pine_" + (1 + i % 5), p, rng.Range(0, 360), 1f, Col.Trunk);
            }
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0, 0, 3.8f), true, true, "the oath circle"),
                (new Vector3(0, 0, cz - 3.2f), false, false, "duel"));
            return root;
        }
    }
}
