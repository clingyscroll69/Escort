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
    /// Chapter 3, Catacombs of Ends (campaign spec §4): five crypt modules — information, seals, social. Open-topped
    /// dioramas (side walls only, so the fixed camera always reads the floor), torch light in warm pools, slab floors.
    /// Hidden pressure plates, rune seals and iron gates, sleeping cultists, the Prisoner, the bone bridge over the pit.
    /// </summary>
    public static class CatacombRooms
    {
        public const int Chapter = 3;
        public static readonly string[] Ids = { "sealed_vault", "dark_gallery", "crypt_of_sleepers", "prisoners_cell", "bone_bridge" };
        public static readonly string[] Caps = { "crypt_start", "crypt_end" };

        static Material _floor, _aisle, _pit;

        [MenuItem("Tools/HS/Build/Rooms · Catacombs")]
        public static void BuildAll()
        {
            ResetIndex();
            System.IO.Directory.CreateDirectory(RoomBuilder.Dir);
            _floor = GroundMat("Env_Floor_Crypt", "T_Ground_CryptFloor", Color.white);
            _aisle = GroundMat("Env_Aisle_Crypt", "T_Ground_CryptAisle", Color.white);
            _pit = GroundMat("Env_Pit", "T_Ground_Pit", Color.white);
            var built = new List<string>
            {
                Save(SealedVault()), Save(DarkGallery()), Save(CryptOfSleepers()), Save(PrisonersCell()), Save(BoneBridge()),
                Save(Cap(true)), Save(Cap(false)),
            };
            AssetDatabase.SaveAssets();
            HS.Agent.AgentBridge.Write("builder.json", $"{{\"ok\":true,\"msg\":\"catacombs: {string.Join(",", built)}\"}}");
        }

        static (GameObject root, RoomModule mod) Room(string id, string display, RoomKind kind, float length)
        {
            var (root, mod) = NewRoom(id, display, kind, length);
            mod.Chapter = Chapter;
            return (root, mod);
        }

        // ------------------------------------------------------------------------------------------------- shared kit

        /// <summary>Place a kit model scaled (non-uniformly) to fill a box: centre at the bottom middle, size in metres.</summary>
        static GameObject Box(Transform parent, string model, Vector3 bottomCentre, Vector3 size, float rotY = 0f, Col col = Col.Box)
        {
            var w = Place(parent, model, bottomCentre, rotY, 1f, Col.None);
            var b = LocalBounds(w);
            if (b.size.x > 1e-3f && b.size.y > 1e-3f && b.size.z > 1e-3f)
            {
                var s = new Vector3(size.x / b.size.x, size.y / b.size.y, size.z / b.size.z);
                w.transform.localScale = s;
                // re-centre: the model's own pivot may not sit at its bottom middle
                var off = new Vector3(-b.center.x * s.x, -b.min.y * s.y, -b.center.z * s.z);
                foreach (Transform c in w.transform) c.localPosition += Vector3.Scale(off, new Vector3(1f / s.x, 1f / s.y, 1f / s.z));
            }
            if (col == Col.Box)
            {
                var bc = w.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, size.y * 0.5f, 0f);
                bc.size = size;
                bc.center = new Vector3(0f, size.y * 0.5f / w.transform.localScale.y, 0f);
                bc.size = new Vector3(size.x / w.transform.localScale.x, size.y / w.transform.localScale.y, size.z / w.transform.localScale.z);
            }
            return w;
        }

        static void Torch(Transform parent, Vector3 pos, float rotY, float range = 8f, float intensity = 2.4f)
        {
            var t = Empty(parent, "Torch", pos, rotY).transform;
            Place(t, "Torch_Metal", Vector3.zero, 0f, 1f);
            var l = new GameObject("Light").AddComponent<Light>();
            l.transform.SetParent(t, false);
            l.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            l.type = LightType.Point;
            l.color = new Color(1f, 0.64f, 0.34f);
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
        }

        /// <summary>Slab floor, worn aisle, brick walls down both sides (a blocker behind), torches on the walls.</summary>
        static void Crypt(Transform root, float length, List<Vector3> aisle, int seed, float innerX = 8.5f, float torchEvery = 11f)
        {
            Ground(root, "Ground", new Vector3(0f, 0f, length * 0.5f), 40f, length, _floor, 8f);
            Road(root, "Aisle", aisle, 3.2f, _aisle);
            var rng = new DetRandom(seed);
            var w = Empty(root, "Walls", Vector3.zero).transform;
            foreach (int side in new[] { -1, 1 })
            {
                for (float z = 1f; z < length; z += 2f)
                    Place(w, rng.Chance(0.12) ? "Wall_Arch" : "Wall_UnevenBrick_Straight", new Vector3(side * innerX, 0f, z), side < 0 ? 90f : -90f, 1f, Col.Box);
                Blocker(w, "Blocker_" + (side < 0 ? "L" : "R"), new Vector3(side * (innerX + 0.6f), 2f, length * 0.5f), new Vector3(1f, 4f, length + 0.5f));
                for (float z = 5f + (side > 0 ? torchEvery * 0.5f : 0f); z < length - 2f; z += torchEvery)
                    Torch(w, new Vector3(side * (innerX - 0.35f), 2.2f, z), side < 0 ? 90f : -90f);
                // rubble and urns along the foot of the walls
                for (float z = 3f; z < length - 2f; z += 3.5f + (float)rng.NextDouble() * 2f)
                {
                    var p = new Vector3(side * (innerX - 0.9f - (float)rng.NextDouble() * 0.6f), 0f, z);
                    if (rng.Chance(0.5)) Place(w, "Prop_Brick" + (1 + rng.Range(0, 4)), p, rng.Range(0, 360), 1.3f, Col.None, false);
                    else Place(w, rng.Chance(0.5) ? "Vase_Rubble_Medium" : "Pot_1", p, rng.Range(0, 360), 1f, Col.Round);
                }
            }
        }

        /// <summary>A stone tomb: a brick block with a slab lid.</summary>
        static void Tomb(Transform parent, Vector3 pos, float rotY)
        {
            var t = Empty(parent, "Tomb", pos, rotY).transform;
            Box(t, "Wall_UnevenBrick_Straight", Vector3.zero, new Vector3(2.1f, 0.8f, 1f));
            Box(t, "Floor_Brick", new Vector3(0f, 0.8f, 0f), new Vector3(2.3f, 0.12f, 1.2f), 0f, Col.None);
            Place(t, "CandleStick_Stand", new Vector3(1.3f, 0f, 0.7f), 0f, 0.7f, Col.None);
        }

        static void Pillar(Transform parent, Vector3 pos) => Box(parent, "Wall_UnevenBrick_Straight", pos, new Vector3(0.9f, 3.4f, 0.9f));

        static void Plate(Transform parent, Vector3 pos, bool hidden = true)
        {
            var h = Marker<HazardMarker>(parent, hidden ? "Hazard_HiddenPlate" : "Hazard_SpikePlate", pos);
            h.Kind = HazardKind.SpikePlate;
            h.Hidden = hidden;
            h.SpikeDamageFraction = 0.1f; // the crypt's plates are old: the wound is the real cost
            Place(h.transform, "SpikePlate", Vector3.zero, 0f, 1f, Col.None, true, false);
        }

        /// <summary>A seal across the way at local z: the slab (or gate) plus walls out to the room's sides.</summary>
        static SealDoor Seal(Transform parent, float z, SealKind kind, float innerX, bool guardians)
        {
            var s = Marker<SealDoor>(parent, kind == SealKind.Rune ? "RuneSeal" : "IronGate", new Vector3(0f, 0f, z));
            s.Kind = kind;
            var bc = s.gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 1.5f, 0f);
            bc.size = new Vector3(s.Width, 3f, 0.6f);
            var v = Empty(s.transform, "Visual", Vector3.zero).transform;
            if (kind == SealKind.Rune)
            {
                Box(v, "Wall_UnevenBrick_Straight", Vector3.zero, new Vector3(s.Width, 3.2f, 0.5f), 0f, Col.None);
                Place(v, "Banner_2_Cloth", new Vector3(0f, 2.2f, -0.3f), 180f, 0.8f, Col.None, true, false);
            }
            else
            {
                Box(v, "Prop_MetalFence_Ornament", Vector3.zero, new Vector3(s.Width, 2.6f, 0.25f), 0f, Col.None);
            }
            Unstatic(v.gameObject);
            Place(s.transform, "CandleStick_Stand", new Vector3(-s.Width * 0.5f - 0.6f, 0f, -0.8f), 0f, 0.9f, Col.Round);
            Place(s.transform, "CandleStick_Stand", new Vector3(s.Width * 0.5f + 0.6f, 0f, -0.8f), 0f, 0.9f, Col.Round);
            // walls from the seal out to the sides: no way round
            for (float x = s.Width * 0.5f + 1f; x < innerX; x += 2f)
                foreach (int side in new[] { -1, 1 })
                    Place(parent, "Wall_UnevenBrick_Straight", new Vector3(side * x, 0f, z), 0f, 1f, Col.Box);
            if (guardians)
            {
                var g = Empty(s.transform, "Guardians", Vector3.zero).transform;
                Spawn(g, "ward_guardian", new Vector3(-2.6f, 0f, -3.4f), 180f);
            }
            return s;
        }

        static void Key(Transform parent, Vector3 pos, float rotY, string model, string title, string note, SealDoor seal)
        {
            var e = Marker<ExploreAnchor>(parent, "SealKey", pos, rotY);
            e.Title = title;
            e.Note = note;
            e.OpensSeal = seal;
            Place(e.transform, model, Vector3.zero, 0f, 1f, Col.None);
            Place(e.transform, "CandleStick_Stand", new Vector3(0.7f, 0f, 0.4f), 0f, 0.6f, Col.None);
        }

        // =====================================================================================================
        // The Sealed Vault (Seal): a hall of tombs; the way on is a rune seal. Its key is on a dead warden.
        // =====================================================================================================
        static GameObject SealedVault()
        {
            var (root, mod) = Room("sealed_vault", "The Sealed Vault", RoomKind.Seal, 42f);
            var t = root.transform;
            var aisle = V(0, 0, 0, 10, 0, 20, 0, 30, 0, 42);
            Crypt(t, 42f, aisle, 3101);
            var set = Empty(t, "Set", Vector3.zero).transform;
            foreach (var p in new[] { new Vector3(-5f, 0f, 14f), new Vector3(5f, 0f, 14f), new Vector3(-5f, 0f, 22f), new Vector3(5f, 0f, 22f) })
                Tomb(set, p, 90f);
            Pillar(set, new Vector3(-2.6f, 0f, 9f));
            Pillar(set, new Vector3(2.6f, 0f, 9f));
            var seal = Seal(set, 33f, SealKind.Rune, 8.5f, true);
            // variant 1: the ward keeps a second guardian
            var extra = Empty(Variant(t, 1), "SealGuardians", Vector3.zero).transform;
            Spawn(extra, "ward_guardian", new Vector3(2.6f, 0f, 29.6f), 180f).Delay = 0.6f;
            Key(t, new Vector3(-7f, 0f, 27.4f), 60f, "Pouch_Large", "A dead warden", "A ring of keys, one carved with the same runes as the seal. 'For the Collector's eyes only.'", seal);
            Stone(t, new Vector3(6.6f, 0f, 27f), 220f, 14f);
            Encounter(t, new Vector3(0f, 0f, 19f), 10f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            Spawn(enc, "cultist", new Vector3(-1.6f, 0f, 24.6f), 180f);
            Spawn(enc, "cultist", new Vector3(1.8f, 0f, 26f), 190f);
            Spawn(Variant(t, 0), "tomb_robber", new Vector3(0.4f, 0f, 28.2f), 180f);
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0, 0, 7.4f), true, true, "the vault"),
                (new Vector3(0, 0, 15), false, false, ""), (new Vector3(0, 0, 23), false, false, ""),
                (new Vector3(0, 0, 31), false, false, ""), (new Vector3(0, 0, 36), false, false, ""), (new Vector3(0, 0, 41.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // The Dark Gallery (TrapCorridor): a long walk with plates you can't see; archers in the alcoves.
        // =====================================================================================================
        static GameObject DarkGallery()
        {
            var (root, mod) = Room("dark_gallery", "The Dark Gallery", RoomKind.TrapCorridor, 44f);
            var t = root.transform;
            var aisle = V(0, 0, 0, 12, 0, 24, 0, 36, 0, 44);
            Crypt(t, 44f, aisle, 3202, 8.5f, 16f);
            var set = Empty(t, "Set", Vector3.zero).transform;
            // the corridor proper: inner walls at x = ±3.4 from z 10 to 34, with two alcove gaps each side
            var gapsL = new HashSet<int> { 17, 27 };
            var gapsR = new HashSet<int> { 21, 31 };
            for (int z = 11; z <= 33; z += 2)
            {
                if (!gapsL.Contains(z)) Place(set, "Wall_UnevenBrick_Straight", new Vector3(-3.4f, 0f, z), 90f, 1f, Col.Box);
                if (!gapsR.Contains(z)) Place(set, "Wall_UnevenBrick_Straight", new Vector3(3.4f, 0f, z), -90f, 1f, Col.Box);
            }
            Plate(set, new Vector3(0.6f, 0.01f, 15.6f));
            Plate(set, new Vector3(-0.3f, 0.01f, 27.4f));
            Plate(Variant(t, 1), new Vector3(0.4f, 0.01f, 21.2f)); // variant 1: a third
            Plate(set, new Vector3(0.8f, 0.01f, 36.2f), false);
            Explore(t, new Vector3(-6.4f, 0f, 17f), 90f, "Scroll_1", "A mapmaker's satchel", "A chalk map of this very corridor, every third flagstone marked with a cross. The crosses stop halfway.");
            Encounter(t, new Vector3(0f, 0f, 26f), 12f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            Spawn(enc, "alcove_archer", new Vector3(-5.6f, 0f, 27f), 100f, elevated: true);
            Spawn(enc, "alcove_archer", new Vector3(5.6f, 0f, 31f), 260f, elevated: true);
            Spawn(enc, "cultist", new Vector3(0.2f, 0f, 38.6f), 180f);
            Spawn(Variant(t, 1), "cultist", new Vector3(-1.8f, 0f, 40.2f), 170f);
            Variant(t, 0);
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0, 0, 8.6f), true, true, "the dark gallery"),
                (new Vector3(0, 0, 16), false, false, ""), (new Vector3(0, 0, 22), false, true, ""),
                (new Vector3(0, 0, 29), false, false, ""), (new Vector3(0, 0, 36), false, false, ""), (new Vector3(0, 0, 43.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // The Crypt of Sleepers (Combat): cultists doze among the tombs; a robber waits to surrender falsely.
        // =====================================================================================================
        static GameObject CryptOfSleepers()
        {
            var (root, mod) = Room("crypt_of_sleepers", "The Crypt of Sleepers", RoomKind.Combat, 42f);
            var t = root.transform;
            var aisle = V(0, 0, 0.3f, 12, -0.3f, 22, 0.2f, 32, 0, 42);
            Crypt(t, 42f, aisle, 3303);
            var set = Empty(t, "Set", Vector3.zero).transform;
            foreach (var p in new[] { new Vector3(-5.4f, 0f, 12f), new Vector3(-5.4f, 0f, 18f), new Vector3(5.4f, 0f, 15f), new Vector3(5.4f, 0f, 24f), new Vector3(-5.4f, 0f, 30f), new Vector3(5.4f, 0f, 33f) })
                Tomb(set, p, 90f);
            Pillar(set, new Vector3(-2.8f, 0f, 21f));
            Pillar(set, new Vector3(2.8f, 0f, 27.6f));
            Armable(t, ArmableKind.LooseMasonry, new Vector3(-8f, 0f, 24f), 90f, new Vector3(6f, 0f, 0.2f), 2.8f);
            Stone(t, new Vector3(7f, 0f, 19.4f), 240f);
            Explore(t, new Vector3(7.2f, 0f, 36.4f), -50f, "Chest_Wood", "An open sarcophagus", "Empty but for a sketch of a knight saluting, and beside it, in a neat hand: 'Every time.'");
            Encounter(t, new Vector3(0f, 0f, 22f), 11f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            Spawn(enc, "cultist", new Vector3(-3.6f, 0f, 18.6f), 90f).Sleeping = true;
            Spawn(enc, "cultist", new Vector3(3.6f, 0f, 23.4f), 270f).Sleeping = true;
            Spawn(enc, "cultist", new Vector3(-3.4f, 0f, 30.2f), 90f).Sleeping = true;
            Spawn(enc, "tomb_robber", new Vector3(0.6f, 0f, 27.6f), 180f);
            Spawn(Variant(t, 1), "alcove_archer", new Vector3(6.6f, 0f, 29.4f), 240f, elevated: true);
            Variant(t, 0);
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0.2f, 0, 9.4f), true, true, "the crypt"),
                (new Vector3(-0.2f, 0, 17), false, false, ""), (new Vector3(0, 0, 24), false, false, ""),
                (new Vector3(0.1f, 0, 32), false, false, ""), (new Vector3(0, 0, 41.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // The Prisoner's Cell (Social): a row of cells; one man in chains begs to be freed (a scout).
        // =====================================================================================================
        static GameObject PrisonersCell()
        {
            var (root, mod) = Room("prisoners_cell", "The Prisoner's Cell", RoomKind.Social, 40f);
            var t = root.transform;
            var aisle = V(0, 0, 0, 10, 0, 20, 0, 30, 0, 40);
            Crypt(t, 40f, aisle, 3404);
            var set = Empty(t, "Set", Vector3.zero).transform;
            // cells along the east wall: bars facing the aisle, one open
            foreach (var z in new[] { 12f, 18f, 24f })
            {
                Box(set, "Prop_MetalFence_Simple", new Vector3(4.2f, 0f, z), new Vector3(3.6f, 2.4f, 0.15f), 90f);
                Place(set, "Wall_UnevenBrick_Straight", new Vector3(6.3f, 0f, z - 1.8f), 0f, 1f, Col.Box);
            }
            Place(set, "Chain_Coil", new Vector3(7.4f, 0f, 19.6f), 0f, 1.2f);
            Place(set, "Cage_Small", new Vector3(-6.6f, 0f, 14.2f), 20f, 1.2f, Col.Box);
            Place(set, "Bucket_Wooden_1", new Vector3(-6.8f, 0f, 22.8f), 0f, 1f);
            Spawn(set, "scout_prisoner", new Vector3(3.2f, 0f, 20.6f), -90f);
            Stone(t, new Vector3(-6.8f, 0f, 27.6f), 110f, 15f);
            Explore(t, new Vector3(-7f, 0f, 8.6f), 30f, "Book_Stack_1", "The gaoler's ledger", "One entry, fresh ink: 'Cell three: occupant to be released by whoever comes. He knows what to tell us.'");
            var v1 = Variant(t, 1);
            Encounter(v1, new Vector3(0f, 0f, 28f), 9f);
            Spawn(v1, "tomb_robber", new Vector3(-1.2f, 0f, 30f), 170f);
            Spawn(v1, "cultist", new Vector3(1.6f, 0f, 31.2f), 190f);
            Variant(t, 0);
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0, 0, 8.4f), true, true, "the cells"),
                (new Vector3(-0.4f, 0, 16), false, false, ""), (new Vector3(0, 0, 24), false, false, ""),
                (new Vector3(0, 0, 32), false, false, ""), (new Vector3(0, 0, 39.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // The Bone Bridge (SetPiece): a narrow span over a pit; shield-bearers shove.
        // =====================================================================================================
        static GameObject BoneBridge()
        {
            var (root, mod) = Room("bone_bridge", "The Bone Bridge", RoomKind.SetPiece, 46f);
            var t = root.transform;
            const float z0 = 12f, z1 = 34f, half = 1.6f;
            // floor only on the landings; the pit between is a black drop (still solid underfoot: the PitZone decides falls)
            Ground(t, "Ground", new Vector3(0f, 0f, 23f), 40f, 46f, _pit, 6f);
            Ground(t, "LandingA", new Vector3(0f, 0.02f, z0 * 0.5f), 18f, z0, _floor, 4f).GetComponent<BoxCollider>().enabled = false;
            Ground(t, "LandingB", new Vector3(0f, 0.02f, z1 + (46f - z1) * 0.5f), 18f, 46f - z1, _floor, 4f).GetComponent<BoxCollider>().enabled = false;
            var deck = Ground(t, "Bridge", new Vector3(0f, 0.04f, (z0 + z1) * 0.5f), half * 2f, z1 - z0, _aisle, 2f);
            deck.GetComponent<BoxCollider>().enabled = false;
            var set = Empty(t, "Set", Vector3.zero).transform;
            for (float z = z0 + 1f; z < z1; z += 2.2f)
                foreach (int side in new[] { -1, 1 })
                    Place(set, "Prop_Brick" + (1 + Mathf.RoundToInt(z) % 4), new Vector3(side * (half + 0.15f), 0.02f, z), z * 13f, 1.1f);
            // landing walls
            var rng = new DetRandom(3505);
            var w = Empty(t, "Walls", Vector3.zero).transform;
            foreach (int side in new[] { -1, 1 })
            {
                for (float z = 1f; z < z0; z += 2f) Place(w, "Wall_UnevenBrick_Straight", new Vector3(side * 8.5f, 0f, z), side < 0 ? 90f : -90f, 1f, Col.Box);
                for (float z = z1 + 1f; z < 46f; z += 2f) Place(w, "Wall_UnevenBrick_Straight", new Vector3(side * 8.5f, 0f, z), side < 0 ? 90f : -90f, 1f, Col.Box);
                Blocker(w, "Blocker_" + (side < 0 ? "L" : "R"), new Vector3(side * 9.1f, 2f, 23f), new Vector3(1f, 4f, 46.5f));
                Torch(w, new Vector3(side * 8.15f, 2.2f, 6f), side < 0 ? 90f : -90f);
                Torch(w, new Vector3(side * 8.15f, 2.2f, 40f), side < 0 ? 90f : -90f);
            }
            // the drop either side of the span
            foreach (int side in new[] { -1, 1 })
            {
                var pit = Marker<PitZone>(t, side < 0 ? "Pit_W" : "Pit_E", new Vector3(side * (half + 4f), 0f, (z0 + z1) * 0.5f));
                pit.Size = new Vector2(8f, z1 - z0);
                pit.ClimbBackX = -side * (half + 4f);
            }
            // nothing on the camera side to hide the bridge; a few bones (bricks) and candles at the landings
            Place(set, "CandleStick_Stand", new Vector3(-2.4f, 0f, z0 - 1f), 0f, 0.9f, Col.Round);
            Place(set, "CandleStick_Stand", new Vector3(2.4f, 0f, z0 - 1f), 0f, 0.9f, Col.Round);
            Explore(t, new Vector3(-6.8f, 0f, 40.2f), 60f, "Pouch_Large", "A climber's pack", "Rope, pitons, and a note: 'The span holds. The men on it do not.'");
            Encounter(t, new Vector3(0f, 0f, 22f), 12f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            Spawn(enc, "shield_bearer", new Vector3(0f, 0.04f, 20.4f), 180f);
            Spawn(enc, "shield_bearer", new Vector3(0f, 0.04f, 27.6f), 180f);
            Spawn(enc, "cultist", new Vector3(0.6f, 0f, 37.6f), 180f);
            Spawn(Variant(t, 1), "alcove_archer", new Vector3(-5.6f, 0f, 38.6f), 140f, elevated: true);
            Variant(t, 0);
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0, 0, 9.6f), true, true, "the bridge"),
                (new Vector3(0, 0, 17), false, false, ""), (new Vector3(0, 0, 23), false, false, ""),
                (new Vector3(0, 0, 29), false, false, ""), (new Vector3(0, 0, 36), false, false, ""), (new Vector3(0, 0, 45.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // Caps: steps down into the dark behind; a dark arch beyond the last room.
        // =====================================================================================================
        static GameObject Cap(bool start)
        {
            var root = new GameObject(start ? "crypt_cap_start" : "crypt_cap_end");
            var t = root.transform;
            float z0 = start ? -16f : 0f;
            Ground(t, "Ground", new Vector3(0f, 0f, z0 + 8f), 40f, 16f, _floor, 6f);
            var w = Empty(t, "Walls", Vector3.zero).transform;
            foreach (int side in new[] { -1, 1 })
                for (float z = z0 + 1f; z < z0 + 16f; z += 2f)
                    Place(w, "Wall_UnevenBrick_Straight", new Vector3(side * 8.5f, 0f, z), side < 0 ? 90f : -90f, 1f, Col.Box);
            float wallZ = start ? -6f : 6f;
            for (float x = -7.5f; x <= 7.5f; x += 2f)
                if (Mathf.Abs(x) > 1.6f) Place(w, "Wall_UnevenBrick_Straight", new Vector3(x, 0f, wallZ), 0f, 1f, Col.Box);
            Place(w, "Wall_Arch", new Vector3(0f, 0f, wallZ), 0f, 1f, Col.None);
            Torch(w, new Vector3(-2.2f, 2.2f, wallZ + (start ? 0.4f : -0.4f)), start ? 0f : 180f, 7f, 2f);
            Torch(w, new Vector3(2.2f, 2.2f, wallZ + (start ? 0.4f : -0.4f)), start ? 0f : 180f, 7f, 2f);
            if (start) Blocker(t, "Back", new Vector3(0f, 2f, -3.2f), new Vector3(30f, 4f, 1f));
            else Blocker(t, "Front", new Vector3(0f, 2f, 3.5f), new Vector3(30f, 4f, 1f));
            return root;
        }
    }
}
