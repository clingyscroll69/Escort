using System.Collections.Generic;
using HS.Core;
using HS.Rooms;
using UnityEditor;
using UnityEngine;
using static HS.EditorTools.RoomKit;
using static HS.EditorTools.RoomBuilder;

namespace HS.EditorTools
{
    /// <summary>
    /// Chapter 2, Whisperwood (campaign spec §4): five forest modules — attrition and ambushes. Same authoring as the Old
    /// Road (layouts are data in code, decoration from a fixed per-room seed): a darker floor, a trodden trail, pines and
    /// twisted trees, ferns and mushrooms. Snares, sleeping poachers, bogs, forage caches, Mr. Quill's glade.
    /// </summary>
    public static class WhisperwoodRooms
    {
        public const int Chapter = 2;
        public static readonly string[] Ids = { "snare_line", "fern_hollow", "mire_crossing", "quills_glade", "poacher_camp" };

        static Material _floor, _path, _bog;

        static readonly string[] Trees = { "Pine_1", "Pine_2", "Pine_3", "Pine_4", "Pine_5", "TwistedTree_1", "TwistedTree_2", "TwistedTree_3", "TwistedTree_4", "CommonTree_3" };
        static readonly string[] Undergrowth = { "Fern_1", "Fern_1", "Plant_1", "Plant_7", "Mushroom_Common", "Mushroom_Laetiporus", "Clover_1", "Grass_Wispy_Tall", "Grass_Common_Tall" };

        [MenuItem("Tools/HS/Build/Rooms · Whisperwood")]
        public static void BuildAll()
        {
            ResetIndex();
            System.IO.Directory.CreateDirectory(RoomBuilder.Dir);
            _floor = GroundMat("Env_Ground_Forest", "T_Ground_Forest", Color.white);
            _path = GroundMat("Env_Path_Forest", "T_Ground_ForestPath", Color.white);
            _bog = GroundMat("Env_Ground_Bog", "T_Ground_Bog", Color.white);
            var built = new List<string> { Save(SnareLine()), Save(FernHollow()), Save(MireCrossing()), Save(QuillsGlade()), Save(PoacherCamp()) };
            AssetDatabase.SaveAssets();
            HS.Agent.AgentBridge.Write("builder.json", $"{{\"ok\":true,\"msg\":\"whisperwood: {string.Join(",", built)}\"}}");
        }

        static (GameObject root, RoomModule mod) Room(string id, string display, RoomKind kind, float length)
        {
            var (root, mod) = NewRoom(id, display, kind, length);
            mod.Chapter = Chapter;
            return (root, mod);
        }

        // ------------------------------------------------------------------------------------------------- shared kit

        /// <summary>Floor, trail, wood on both sides (two rows, a blocker behind), undergrowth.</summary>
        static void Forest(Transform root, float length, List<Vector3> trail, int seed, float innerX = 10f, int undergrowth = 60)
        {
            Ground(root, "Ground", new Vector3(0f, 0f, length * 0.5f), 48f, length, _floor);
            Road(root, "Trail", trail, 3.0f, _path);
            var rng = new DetRandom(seed);
            var b = Empty(root, "Wood", Vector3.zero).transform;
            foreach (int side in new[] { -1, 1 })
            {
                float z = 1.2f;
                while (z < length - 1f)
                {
                    Place(b, Trees[rng.Range(0, Trees.Length)], new Vector3(side * (innerX + 1.6f + (float)rng.NextDouble() * 1.4f), 0f, z), rng.Range(0, 360), 0.9f + (float)rng.NextDouble() * 0.35f, Col.Trunk);
                    Place(b, Trees[rng.Range(0, Trees.Length)], new Vector3(side * (innerX + 5f + (float)rng.NextDouble() * 2.5f), 0f, z + 1.5f), rng.Range(0, 360), 1f + (float)rng.NextDouble() * 0.4f, Col.None);
                    if (rng.Chance(0.6))
                        Place(b, rng.Chance(0.5) ? "Fern_1" : "Bush_Common", new Vector3(side * (innerX - 0.3f + (float)rng.NextDouble() * 0.8f), 0f, z + 0.7f), rng.Range(0, 360), 1f + (float)rng.NextDouble() * 0.4f, Col.None, false);
                    if (rng.Chance(0.35))
                        Place(b, "Rock_Medium_" + (1 + rng.Range(0, 3)), new Vector3(side * (innerX + 0.4f), 0f, z + 1.9f), rng.Range(0, 360), 0.5f + (float)rng.NextDouble() * 0.4f, Col.Box);
                    z += 2.7f + (float)rng.NextDouble() * 1.1f;
                }
                Blocker(b, "Blocker_" + (side < 0 ? "L" : "R"), new Vector3(side * (innerX + 1.1f), 2f, length * 0.5f), new Vector3(1f, 4f, length + 0.5f));
            }
            RoadEdges(root, trail, 1.5f, rng);
            var u = Empty(root, "Undergrowth", Vector3.zero).transform;
            int placed = 0, guard = 0;
            while (placed < undergrowth && guard++ < undergrowth * 10)
            {
                var p = new Vector3(-innerX + 0.5f + (float)rng.NextDouble() * (innerX * 2f - 1f), 0f, 1f + (float)rng.NextDouble() * (length - 2f));
                if (DistanceToPolyline(p, trail) < 2.4f) continue;
                Place(u, Undergrowth[rng.Range(0, Undergrowth.Length)], p, rng.Range(0, 360), 0.7f + (float)rng.NextDouble() * 0.6f, Col.None, false);
                placed++;
            }
        }

        /// <summary>A forage cache: searching it gives rations (and the exploration share of the room's XP).</summary>
        static void Forage(Transform parent, Vector3 pos, float rotY, string model, string title, string note, int rations)
        {
            var e = Marker<ExploreAnchor>(parent, "Forage", pos, rotY);
            e.Title = title;
            e.Note = note;
            e.Rations = rations;
            Place(e.transform, model, Vector3.zero, 0f, 1f, Col.None);
            Place(e.transform, "Mushroom_Common", new Vector3(0.6f, 0f, 0.3f), 40f, 1.1f, Col.None);
        }

        /// <summary>A poacher's snare on the trail: a rope loop pegged between two stakes.</summary>
        static void Snare(Transform parent, Vector3 pos, float rotY = 0f)
        {
            var h = Marker<HazardMarker>(parent, "Hazard_Snare", pos, rotY);
            h.Kind = HazardKind.Snare;
            h.Radius = 1.0f;
            var v = Empty(h.transform, "Visual", Vector3.zero).transform;
            Place(v, "Rope_1", new Vector3(0f, 0.02f, 0f), 0f, 0.9f, Col.None, true, false);
            Place(v, "Prop_Support", new Vector3(-0.7f, 0f, 0f), 0f, 0.28f, Col.None, true, false);
            Place(v, "Prop_Support", new Vector3(0.7f, 0f, 0f), 0f, 0.28f, Col.None, true, false);
            Unstatic(h.gameObject);
        }

        /// <summary>A poacher's tree stand: planks 2.2 m up on posts, a ladder ramp the sidekick can climb.</summary>
        static void Stand(Transform parent, string name, Vector3 pos, float yaw, string archetype, int variant = -1, Transform root = null)
        {
            var perch = Empty(parent, name, pos, yaw).transform;
            Place(perch, "Floor_WoodDark", new Vector3(0f, 2.2f, 0f), 0f, 1.1f);
            Place(perch, "Floor_WoodDark", new Vector3(0f, 2.2f, -0.4f), 0f, 1.1f);
            var top = perch.gameObject.AddComponent<BoxCollider>();
            top.center = new Vector3(0f, 2.05f, 0f);
            top.size = new Vector3(2.2f, 0.3f, 2.4f);
            Place(perch, "Prop_Support", new Vector3(-0.9f, 0f, 0.8f), 0f, 1.1f);
            Place(perch, "Prop_Support", new Vector3(0.9f, 0f, 0.8f), 0f, 1.1f);
            Place(perch, "Prop_Support", new Vector3(-0.9f, 0f, -0.8f), 0f, 1.1f);
            Place(perch, "Prop_Support", new Vector3(0.9f, 0f, -0.8f), 0f, 1.1f);
            Place(perch, "Stairs_Exterior_Straight", new Vector3(2.1f, 1.0f, -0.4f), 90f, 1f);
            Place(perch, "Stairs_Exterior_Straight", new Vector3(4.1f, 0f, -0.4f), 90f, 1f);
            var ramp = Empty(perch, "Ramp", new Vector3(3.1f, 1.05f, -0.4f)).AddComponent<BoxCollider>();
            ramp.transform.localRotation = Quaternion.Euler(0f, 0f, 28f);
            ramp.size = new Vector3(4.6f, 0.2f, 1.8f);
            Place(perch, "Banner_2_Cloth", new Vector3(0f, 2.3f, 0.9f), 0f, 0.6f);
            // The poacher stands on the planks; in a variant-only stand the marker lives under that variant's group.
            var spawnParent = variant >= 0 && root != null ? Variant(root, variant) : perch;
            var sp = Spawn(spawnParent, archetype, Vector3.zero, 0f, elevated: true);
            sp.transform.position = perch.TransformPoint(new Vector3(0f, 2.25f, 0f));
            sp.transform.rotation = perch.rotation;
        }

        static void Bog(Transform parent, Vector3 pos, Vector2 size, float rotY = 0f)
        {
            var z = Marker<BogZone>(parent, "Bog", pos, rotY);
            z.Size = size;
            var g = Ground(z.transform, "Mire", new Vector3(0f, 0.02f, 0f), size.x + 0.6f, size.y + 0.6f, _bog, 2f);
            g.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            var c = g.GetComponent<BoxCollider>();
            if (c != null) c.enabled = false;
            var rng = new DetRandom(Mathf.RoundToInt(pos.z * 13f + pos.x * 7f));
            for (int i = 0; i < 6; i++)
            {
                var p = new Vector3(((float)rng.NextDouble() - 0.5f) * size.x, 0f, ((float)rng.NextDouble() - 0.5f) * size.y);
                Place(z.transform, rng.Chance(0.5) ? "Grass_Wispy_Tall" : "Plant_7", p, rng.Range(0, 360), 0.8f, Col.None, false);
            }
        }

        // =====================================================================================================
        // Snare Line (TrapCorridor): a narrow trail strung with snares; poachers in tree stands cover it.
        // =====================================================================================================
        static GameObject SnareLine()
        {
            var (root, mod) = Room("snare_line", "The Snare Line", RoomKind.TrapCorridor, 44f);
            var t = root.transform;
            var trail = V(0, 0, 0.4f, 8, -0.3f, 15, 0.4f, 22, -0.2f, 29, 0.3f, 36, 0, 44);
            Forest(t, 44f, trail, 2101, 8.5f, 55);
            var set = Empty(t, "Set", Vector3.zero).transform;
            Snare(set, new Vector3(0.2f, 0.01f, 13.4f));
            Snare(set, new Vector3(-0.3f, 0.01f, 21.8f), 30f);
            Snare(set, new Vector3(0.4f, 0.01f, 30.2f), -20f);
            // Hollow log and a bundle of stakes: the poachers' work.
            var log = Place(set, "Roof_Log", new Vector3(-4.8f, 0.3f, 17f), 0f, 0.4f, Col.Box);
            log.transform.localRotation = Quaternion.Euler(0f, 70f, 0f) * Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(90f, 0f, 0f);
            Place(set, "Rope_3", new Vector3(4.4f, 0f, 24.5f), 30f, 1f);
            Place(set, "Bag", new Vector3(4.9f, 0f, 25.3f), -20f, 1f);
            Stand(set, "Stand_W", new Vector3(-6.2f, 0f, 26.5f), 110f, "poacher");
            Stand(set, "Stand_E", new Vector3(6.4f, 0f, 34f), 250f, "poacher", 1, t);
            Armable(t, ArmableKind.FallingTree, new Vector3(7.2f, 0f, 18.4f), 0f, new Vector3(-6.4f, 0f, 0.2f), 3.2f);
            Forage(t, new Vector3(-6.4f, 0f, 7.6f), 30f, "Bucket_Wooden_1", "Mushroom ring", "Edible, mostly. The poachers left a few untouched. Their mistake.", 1);
            Encounter(t, new Vector3(0f, 0f, 27f), 11f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            Spawn(enc, "thug", new Vector3(0.6f, 0f, 37.6f), 180f);
            Spawn(Variant(t, 0), "fern_ambusher", new Vector3(-4.6f, 0f, 31.6f), 90f, hidden: true);
            Spawn(Variant(t, 1), "thug", new Vector3(-1.6f, 0f, 39.6f), 170f);
            // the first stand's poacher sits in the shared encounter; the second only in variant 1 (see Stand)
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0.3f, 0, 8.6f), true, true, "the snare line"),
                (new Vector3(-0.2f, 0, 15.2f), false, false, ""), (new Vector3(0.1f, 0, 22), false, true, ""),
                (new Vector3(0.2f, 0, 29), false, false, ""), (new Vector3(0, 0, 36), false, false, ""), (new Vector3(0, 0, 43.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // Fern Hollow (Ambush): a dip choked with ferns; ambushers wait in them either side of the trail.
        // =====================================================================================================
        static GameObject FernHollow()
        {
            var (root, mod) = Room("fern_hollow", "Fern Hollow", RoomKind.Ambush, 42f);
            var t = root.transform;
            var trail = V(0, 0, 0.5f, 9, -0.4f, 17, 0.2f, 24, 0.6f, 31, 0, 42);
            Forest(t, 42f, trail, 2202, 10f, 70);
            var set = Empty(t, "Set", Vector3.zero).transform;
            // Fern clumps: where the ambushers lie, and three more where nobody does.
            foreach (var p in new[] { new Vector3(-4.4f, 0, 24.4f), new Vector3(4.6f, 0, 27.2f), new Vector3(-4.8f, 0, 30.6f), new Vector3(4.2f, 0, 20.4f), new Vector3(-4.6f, 0, 17.6f), new Vector3(5.0f, 0, 33.6f) })
            {
                Place(set, "Plant_7_Big", p, p.z * 31f, 1.3f, Col.None, false);
                Place(set, "Fern_1", p + new Vector3(0.6f, 0f, 0.5f), p.z * 17f, 1.6f, Col.None, false);
                Place(set, "Fern_1", p + new Vector3(-0.5f, 0f, -0.4f), p.z * 23f, 1.4f, Col.None, false);
            }
            Place(set, "DeadTree_3", new Vector3(-7.2f, 0f, 12.4f), 40f, 0.7f, Col.Trunk);
            Place(set, "TwistedTree_5", new Vector3(7.6f, 0f, 15.2f), 200f, 0.9f, Col.Trunk);
            Armable(t, ArmableKind.LogPile, new Vector3(-7.6f, 0f, 21.4f), 0f, new Vector3(5.2f, 0f, 2.6f), 3f);
            Stone(t, new Vector3(7.4f, 0f, 22.6f), 240f);
            Forage(t, new Vector3(8.2f, 0f, 9.4f), -40f, "Bag", "Berry bramble", "Blackberries, and a poacher's snack bag caught in the thorns.", 1);
            Encounter(t, new Vector3(0f, 0f, 27.5f), 11f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            Spawn(enc, "fern_ambusher", new Vector3(-4.4f, 0f, 24.4f), 90f, hidden: true);
            Spawn(enc, "fern_ambusher", new Vector3(4.6f, 0f, 27.2f), -90f, hidden: true);
            Spawn(enc, "fern_ambusher", new Vector3(-4.8f, 0f, 30.6f), 90f, hidden: true);
            Spawn(enc, "woodsman", new Vector3(0.4f, 0f, 35.2f), 180f);
            Stand(set, "Stand_N", new Vector3(5.8f, 0f, 37.4f), 230f, "poacher", 1, t);
            Variant(t, 0);
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(-0.2f, 0, 12), true, true, "the hollow"),
                (new Vector3(0.2f, 0, 19), false, false, ""), (new Vector3(0.5f, 0, 26), false, false, ""),
                (new Vector3(0.3f, 0, 33), false, false, ""), (new Vector3(0, 0, 41.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // Mire Crossing (SetPiece): the trail sinks into bog twice; a woodsman and his men wait on firm ground.
        // =====================================================================================================
        static GameObject MireCrossing()
        {
            var (root, mod) = Room("mire_crossing", "The Mire Crossing", RoomKind.SetPiece, 46f);
            var t = root.transform;
            var trail = V(0, 0, 0.3f, 9, 0, 16, -0.4f, 23, 0.3f, 30, 0, 38, 0, 46);
            Forest(t, 46f, trail, 2303, 10f, 50);
            var set = Empty(t, "Set", Vector3.zero).transform;
            Bog(set, new Vector3(0f, 0f, 17.6f), new Vector2(8f, 5.6f));
            Bog(set, new Vector3(0.6f, 0f, 27.4f), new Vector2(7f, 4.6f), 12f);
            // Dead trees standing in the water, a fallen trunk for a bridge nobody uses.
            Place(set, "DeadTree_1", new Vector3(-6.4f, 0f, 19.2f), 30f, 0.9f, Col.Trunk);
            Place(set, "DeadTree_4", new Vector3(5.8f, 0f, 25.6f), 210f, 0.8f, Col.Trunk);
            Place(set, "DeadTree_2", new Vector3(-5.2f, 0f, 29.6f), 120f, 0.7f, Col.Trunk);
            var trunk = Place(set, "Roof_Log", new Vector3(5.4f, 0.25f, 16.6f), 0f, 0.45f, Col.Box);
            trunk.transform.localRotation = Quaternion.Euler(0f, 10f, 0f) * Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(90f, 0f, 0f);
            Armable(t, ArmableKind.FallingTree, new Vector3(-7.4f, 0f, 33.4f), 0f, new Vector3(6.8f, 0f, 0f), 3.2f);
            Explore(t, new Vector3(-8.2f, 0f, 22.6f), 60f, "Pouch_Large", "A drowned satchel", "A merchant's ledger, swollen with water. One name in it is underlined twice: 'Quill'.");
            Encounter(t, new Vector3(0f, 0f, 34f), 12f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            Spawn(enc, "woodsman", new Vector3(0.3f, 0f, 37.6f), 180f);
            Spawn(enc, "thug", new Vector3(-3.2f, 0f, 36.4f), 160f);
            Spawn(enc, "thug", new Vector3(3.4f, 0f, 38.8f), 200f);
            Spawn(Variant(t, 0), "turncoat", new Vector3(1.8f, 0f, 40.6f), 190f);
            Stand(set, "Stand_E", new Vector3(6.6f, 0f, 40.2f), 240f, "poacher", 1, t);
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0, 0, 10.4f), true, true, "the mire"),
                (new Vector3(-0.3f, 0, 17.6f), false, false, ""), (new Vector3(0.3f, 0, 23), false, false, ""),
                (new Vector3(0.4f, 0, 27.4f), false, false, ""), (new Vector3(0, 0, 33), false, true, ""),
                (new Vector3(0, 0, 39), false, false, ""), (new Vector3(0, 0, 45.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // Quill's Glade (Social): a clearing, a merchant's cart, a stone that watches the road. Mr. Quill (a scout).
        // =====================================================================================================
        static GameObject QuillsGlade()
        {
            var (root, mod) = Room("quills_glade", "Quill's Glade", RoomKind.Social, 40f);
            var t = root.transform;
            var trail = V(0, 0, -0.3f, 8, 0.2f, 16, -0.6f, 22, 0.2f, 30, 0, 40);
            Forest(t, 40f, trail, 2404, 10.5f, 40);
            var set = Empty(t, "Set", Vector3.zero).transform;
            // The clearing: flowers, a cart with wares, a lantern on a post.
            Place(set, "Stall_Cart_Empty", new Vector3(5.8f, 0f, 20.6f), -100f, 1f, Col.Box);
            Place(set, "Crate_Wooden", new Vector3(4.2f, 0f, 22.6f), 15f, 0.9f, Col.Box);
            Place(set, "Barrel_Apples", new Vector3(7.2f, 0f, 23.2f), 0f, 1f, Col.Round);
            Place(set, "FarmCrate_Apple", new Vector3(4.4f, 0f, 18.4f), -30f, 1f, Col.Box);
            Place(set, "Pouch_Large", new Vector3(6.6f, 0.95f, 20.2f), 0f, 1f);
            Place(set, "Lantern_Wall", new Vector3(3.6f, 2.1f, 21.2f), 90f, 1f);
            Place(set, "Prop_Support", new Vector3(3.6f, 0f, 21.4f), 0f, 0.9f, Col.Round);
            foreach (var p in new[] { new Vector3(-4.6f, 0, 14.2f), new Vector3(-6.2f, 0, 18.8f), new Vector3(-3.8f, 0, 25.4f), new Vector3(2.6f, 0, 12.6f) })
                Place(set, "Flower_4_Group", p, p.z * 29f, 1.2f, Col.None, false);
            Stone(t, new Vector3(-7f, 0f, 24.2f), 100f, 16f);
            Spawn(set, "scout_quill", new Vector3(4.4f, 0f, 20.2f), -100f);
            Explore(t, new Vector3(-8.4f, 0f, 8.2f), 20f, "Scroll_2", "A price list", "Rope, rations, lamp oil — and 'information, by arrangement'. The prices for the last are blank.");
            var v1 = Variant(t, 1);
            Encounter(v1, new Vector3(0f, 0f, 28f), 10f);
            Spawn(v1, "thug", new Vector3(-1.2f, 0f, 29.6f), 170f);
            Spawn(v1, "thug", new Vector3(2.4f, 0f, 31.2f), 200f);
            Variant(t, 0); // a quiet glade
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0.2f, 0, 10.4f), true, true, "the glade"),
                (new Vector3(-0.4f, 0, 18), false, false, ""), (new Vector3(0.1f, 0, 26), false, false, ""),
                (new Vector3(0, 0, 33), false, false, ""), (new Vector3(0, 0, 39.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // Poacher Camp (Combat): two poachers doze by a fire; a woodsman keeps watch; their larder is full.
        // =====================================================================================================
        static GameObject PoacherCamp()
        {
            var (root, mod) = Room("poacher_camp", "The Poacher Camp", RoomKind.Combat, 42f);
            var t = root.transform;
            var trail = V(0, 0, 0.4f, 9, -0.3f, 17, 0.2f, 24, -0.4f, 31, 0, 42);
            Forest(t, 42f, trail, 2505, 10.5f, 45);
            var set = Empty(t, "Set", Vector3.zero).transform;
            // The fire and the lean-tos.
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                Place(set, "Pebble_Round_" + (1 + i % 5), new Vector3(3.6f + Mathf.Cos(a) * 0.8f, 0f, 27f + Mathf.Sin(a) * 0.8f), i * 40f, 1.8f);
            }
            Place(set, "Cauldron", new Vector3(3.6f, 0f, 27f), 0f, 0.8f, Col.Round);
            Place(set, "Stall_Empty", new Vector3(7.2f, 0f, 29.6f), -110f, 1f, Col.Box);
            Place(set, "Stall_Empty", new Vector3(-7f, 0f, 31.4f), 100f, 1f, Col.Box);
            Place(set, "Peg_Rack", new Vector3(-6.4f, 0f, 26.2f), 80f, 1f, Col.Box);
            Place(set, "Cage_Small", new Vector3(8.4f, 0f, 34.4f), 30f, 1.1f, Col.Box);
            Place(set, "Crate_Wooden", new Vector3(-8.4f, 0f, 22.8f), 10f, 1f, Col.Box);
            Place(set, "Barrel", new Vector3(-7.4f, 0f, 23.6f), 0f, 1f, Col.Round);
            Armable(t, ArmableKind.LogPile, new Vector3(-8f, 0f, 19.6f), 0f, new Vector3(5.6f, 0f, 3f), 3f);
            Stone(t, new Vector3(8.6f, 0f, 30.2f), 250f);
            Forage(t, new Vector3(-8.8f, 0f, 35.6f), 70f, "Chest_Wood", "The poachers' larder", "Smoked hare, hard cheese, and a list of every traveller who passed this week. Yours is the only name missing.", 2);
            Encounter(t, new Vector3(0f, 0f, 26f), 11f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            var s1 = Spawn(enc, "poacher", new Vector3(2.4f, 0f, 26.2f), 60f);
            s1.Sleeping = true;
            var s2 = Spawn(enc, "poacher", new Vector3(4.8f, 0f, 28.2f), 240f);
            s2.Sleeping = true;
            Spawn(enc, "woodsman", new Vector3(-1.8f, 0f, 30.8f), 180f);
            Spawn(Variant(t, 0), "turncoat", new Vector3(0.6f, 0f, 33.6f), 190f);
            Spawn(Variant(t, 1), "fern_ambusher", new Vector3(-4.8f, 0f, 24.6f), 90f, hidden: true);
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0.2f, 0, 13.4f), true, true, "the camp"),
                (new Vector3(-0.2f, 0, 20), false, false, ""), (new Vector3(0, 0, 26), false, false, ""),
                (new Vector3(-0.3f, 0, 33), false, false, ""), (new Vector3(0, 0, 41.6f), false, false, ""));
            return root;
        }
    }
}
