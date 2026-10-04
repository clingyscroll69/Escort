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
    /// Chapter 4, The Sunken Bastion (campaign spec §4): five modules — adaptation, split threats, environment. A drowned
    /// fortress: wet stone floors, knee-deep water (wading zones), dressed-stone walks, walls down both sides (open-topped,
    /// so the fixed camera always reads the floor). The nemesis squad's extra cheats are markers with MinIntel 1–3.
    /// </summary>
    public static class BastionRooms
    {
        public const int Chapter = 4;
        public static readonly string[] Ids = { "flooded_gate", "sluice_works", "hostage_court", "baiters_causeway", "wrens_rampart" };
        public static readonly string[] Caps = { "bastion_start", "bastion_end" };

        static Material _floor, _walk, _water, _wood, _iron;

        [MenuItem("Tools/HS/Build/Rooms · Sunken Bastion")]
        public static void BuildAll()
        {
            ResetIndex();
            System.IO.Directory.CreateDirectory(RoomBuilder.Dir);
            _floor = GroundMat("Env_Floor_WetStone", "T_Ground_WetStone", Color.white);
            _walk = GroundMat("Env_Walk_Rampart", "T_Ground_Rampart", Color.white);
            _water = GroundMat("Env_Water", "T_Ground_Water", Color.white);
            _wood = MaterialLibrary.GetOrCreate("Env_SluiceWood", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Game/Art/Environment/Textures/T_WoodTrim_BaseColor.png"), new Color(0.75f, 0.68f, 0.6f));
            _iron = MaterialLibrary.GetOrCreate("Env_SluiceIron", null, new Color(0.32f, 0.34f, 0.38f));
            var built = new List<string>
            {
                Save(FloodedGate()), Save(SluiceWorks()), Save(HostageCourt()), Save(BaitersCauseway()), Save(WrensRampart()),
                Save(Cap(true)), Save(Cap(false)),
            };
            AssetDatabase.SaveAssets();
            HS.Agent.AgentBridge.Write("builder.json", $"{{\"ok\":true,\"msg\":\"bastion: {string.Join(",", built)}\"}}");
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
                var off = new Vector3(-b.center.x * s.x, -b.min.y * s.y, -b.center.z * s.z);
                foreach (Transform c in w.transform) c.localPosition += Vector3.Scale(off, new Vector3(1f / s.x, 1f / s.y, 1f / s.z));
            }
            if (col == Col.Box)
            {
                var bc = w.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, size.y * 0.5f / w.transform.localScale.y, 0f);
                bc.size = new Vector3(size.x / w.transform.localScale.x, size.y / w.transform.localScale.y, size.z / w.transform.localScale.z);
            }
            return w;
        }

        /// <summary>A Unity primitive with a kit-look material and no collider (the wheel's parts).</summary>
        static GameObject Prim(Transform parent, PrimitiveType type, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return go;
        }

        /// <summary>Wet slab floor, a dressed-stone walk, fortress walls down both sides (a blocker behind), lanterns.</summary>
        static void Fortress(Transform root, float length, List<Vector3> walk, int seed, float innerX = 8.5f)
        {
            Ground(root, "Ground", new Vector3(0f, 0f, length * 0.5f), 40f, length, _floor, 6f);
            Road(root, "Walk", walk, 3.4f, _walk);
            var rng = new DetRandom(seed);
            var w = Empty(root, "Walls", Vector3.zero).transform;
            foreach (int side in new[] { -1, 1 })
            {
                for (float z = 1f; z < length; z += 2f)
                    Place(w, rng.Chance(0.14) ? "Wall_UnevenBrick_Window_Wide_Round" : "Wall_UnevenBrick_Straight", new Vector3(side * innerX, 0f, z), side < 0 ? 90f : -90f, 1f, Col.Box);
                Blocker(w, "Blocker_" + (side < 0 ? "L" : "R"), new Vector3(side * (innerX + 0.6f), 2f, length * 0.5f), new Vector3(1f, 4f, length + 0.5f));
                for (float z = 6f + (side > 0 ? 5f : 0f); z < length - 2f; z += 12f)
                    Place(w, "Lantern_Wall", new Vector3(side * (innerX - 0.3f), 2.3f, z), side < 0 ? 90f : -90f, 1f);
                for (float z = 3f; z < length - 2f; z += 4f + (float)rng.NextDouble() * 2f)
                {
                    var p = new Vector3(side * (innerX - 0.9f - (float)rng.NextDouble() * 0.5f), 0f, z);
                    double r = rng.NextDouble();
                    if (r < 0.4) Place(w, "Prop_Brick" + (1 + rng.Range(0, 4)), p, rng.Range(0, 360), 1.3f, Col.None, false);
                    else if (r < 0.7) Place(w, "Barrel", p, rng.Range(0, 360), 1f, Col.Round);
                    else Place(w, "Crate_Wooden", p, rng.Range(0, 360), 1f, Col.Box);
                }
                Place(w, "Prop_Vine" + (side < 0 ? "1" : "2"), new Vector3(side * (innerX - 0.25f), 2.8f, length * 0.4f), side < 0 ? 90f : -90f, 1f);
            }
        }

        /// <summary>Knee-deep water over the floor: everyone wades (him worst), and it reads as water.</summary>
        static BogZone Water(Transform parent, string name, Vector3 pos, Vector2 size, float rotY = 0f)
        {
            var z = Marker<BogZone>(parent, name, pos, rotY);
            z.Size = size;
            z.Water = true;
            z.HeroMul = 0.75f;
            z.OtherMul = 0.85f;
            var g = Ground(z.transform, name + "Surface", new Vector3(0f, 0.05f, 0f), size.x + 0.4f, size.y + 0.4f, _water, 3f); // unique mesh per room and patch
            g.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            var c = g.GetComponent<BoxCollider>();
            if (c != null) c.enabled = false;
            return z;
        }

        /// <summary>A wooden ramp (the visual stairs and a sloped collider the CharacterController can climb) up to height h.</summary>
        static void Ramp(Transform parent, Vector3 foot, float h, float run, float yaw)
        {
            var r = Empty(parent, "Ramp", foot, yaw).transform;
            Place(r, "Stairs_Exterior_Straight", new Vector3(0f, 0f, run * 0.3f), 180f, 1f);
            Place(r, "Stairs_Exterior_Straight", new Vector3(0f, h * 0.48f, run * 0.72f), 180f, 1f);
            float ang = Mathf.Atan2(h, run) * Mathf.Rad2Deg;
            var col = Empty(r, "Slope", new Vector3(0f, h * 0.5f, run * 0.5f)).AddComponent<BoxCollider>();
            col.transform.localRotation = Quaternion.Euler(-ang, 0f, 0f);
            col.size = new Vector3(2f, 0.2f, Mathf.Sqrt(h * h + run * run) + 0.4f);
        }

        // =====================================================================================================
        // The Flooded Gate (Combat): the bastion's lower gate, the floor under water; a stone watches over the arch.
        // =====================================================================================================
        static GameObject FloodedGate()
        {
            var (root, mod) = Room("flooded_gate", "The Flooded Gate", RoomKind.Combat, 42f);
            var t = root.transform;
            var walk = V(0, 0, 0.3f, 10, -0.2f, 21, 0.2f, 32, 0, 42);
            Fortress(t, 42f, walk, 4101);
            var set = Empty(t, "Set", Vector3.zero).transform;
            Water(set, "Water", new Vector3(0f, 0f, 22f), new Vector2(15f, 14f));
            // The gate: two piers and a beam, the portcullis long rusted open.
            Place(set, "Corner_ExteriorWide_Brick", new Vector3(-2.2f, 0f, 24f), 0f, 1.2f, Col.Box);
            Place(set, "Corner_ExteriorWide_Brick", new Vector3(2.2f, 0f, 24f), 90f, 1.2f, Col.Box);
            var beam = Place(set, "Roof_Log", new Vector3(0f, -1.3f, 24.1f), 0f, 0.4f);
            beam.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            for (float x = 4.2f; x < 8.5f; x += 2f)
                foreach (int side in new[] { -1, 1 }) Place(set, "Wall_UnevenBrick_Straight", new Vector3(side * x, 0f, 24f), 0f, 1f, Col.Box);
            Place(set, "Banner_1", new Vector3(-2.2f, 3.2f, 23.4f), 180f, 1f);
            Place(set, "Banner_1_Cloth", new Vector3(-2.2f, 3.2f, 23.4f), 180f, 1f);
            Armable(t, ArmableKind.LooseMasonry, new Vector3(2.4f, 0f, 24.6f), 0f, new Vector3(-2.4f, 0f, 1.6f), 2.6f);
            Stone(t, new Vector3(6.4f, 0f, 27.6f), 200f, 15f);
            Explore(t, new Vector3(-6.8f, 0f, 12.4f), 40f, "Chest_Wood", "A drowned quartermaster's chest", "Rations, sodden; a muster roll; and in the margin, in a neat grey hand: 'He will challenge the first man he sees. Make it a man worth losing.'");
            Encounter(t, new Vector3(0f, 0f, 21f), 11f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            Spawn(enc, "bastion_soldier", new Vector3(-1.2f, 0f, 19.6f), 180f);
            Spawn(enc, "bastion_soldier", new Vector3(1.6f, 0f, 21.2f), 190f);
            Spawn(enc, "bastion_soldier", new Vector3(0.2f, 0f, 29.4f), 180f);
            // The nemesis squad's extras (Curator Intel 1+): a turncoat in the ranks; at 2+, a bowman on the crates.
            Spawn(enc, "turncoat", new Vector3(-2.6f, 0f, 27.6f), 170f).MinIntel = 1;
            var perch = Empty(enc, "Crates", Vector3.zero).transform;
            Place(perch, "Crate_Wooden", new Vector3(-6.6f, 0f, 30.2f), 10f, 1f, Col.Box);
            Place(perch, "Crate_Wooden", new Vector3(-7.5f, 0f, 30.6f), -12f, 1f, Col.Box);
            Spawn(enc, "bastion_archer", new Vector3(-6.4f, 0f, 32.4f), 160f, elevated: true).MinIntel = 2;
            Spawn(Variant(t, 1), "bastion_archer", new Vector3(6.6f, 0f, 33.2f), 210f, elevated: true);
            Variant(t, 0);
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0.2f, 0, 10.4f), true, true, "the flooded gate"),
                (new Vector3(-0.1f, 0, 17), false, false, ""), (new Vector3(0, 0, 24), false, true, ""),
                (new Vector3(0.1f, 0, 31), false, false, ""), (new Vector3(0, 0, 41.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // The Sluice Works (SetPiece, always 3rd): he fights on the lower floor; above, a crew cranks the sluice.
        // =====================================================================================================
        static GameObject SluiceWorks()
        {
            var (root, mod) = Room("sluice_works", "The Sluice Works", RoomKind.SetPiece, 46f);
            var t = root.transform;
            var walk = V(-1.5f, 0, -1.5f, 12, -1.6f, 23, -1.4f, 34, -0.8f, 46);
            Fortress(t, 46f, walk, 4202);
            var set = Empty(t, "Set", Vector3.zero).transform;
            // The gallery above: a raised walk along the east wall, 2.2 m up, stairs at its south end.
            const float top = 2.2f;
            Box(set, "Floor_Brick", new Vector3(6.4f, 0f, 24f), new Vector3(4f, top, 20f));
            for (float z = 15f; z < 34f; z += 2.2f)
                Place(set, "Prop_MetalFence_Simple", new Vector3(4.35f, top, z), 90f, 0.9f);
            Ramp(set, new Vector3(6.4f, 0f, 9.4f), top, 4.6f, 0f);
            // The wheel: hub, rim and spokes (primitives in the kit's colours), on a frame; the flood below is its child.
            var wheel = Marker<SluiceWheel>(set, "SluiceWheel", new Vector3(6.6f, top, 25f), -90f);
            var vis = Empty(wheel.transform, "Visual", new Vector3(0f, 1.5f, 0f)).transform;
            Prim(vis, PrimitiveType.Cylinder, Vector3.zero, new Vector3(90f, 0f, 0f), new Vector3(0.5f, 0.25f, 0.5f), _iron);
            for (int k = 0; k < 6; k++)
                Prim(vis, PrimitiveType.Cube, Vector3.zero, new Vector3(0f, 0f, k * 30f), new Vector3(2.6f, 0.14f, 0.14f), _wood);
            for (int k = 0; k < 12; k++)
            {
                float a = k * 30f * Mathf.Deg2Rad;
                Prim(vis, PrimitiveType.Cube, new Vector3(Mathf.Cos(a) * 1.25f, Mathf.Sin(a) * 1.25f, 0f), new Vector3(0f, 0f, k * 30f + 90f), new Vector3(0.7f, 0.16f, 0.2f), _wood);
            }
            Place(wheel.transform, "Prop_Support", new Vector3(0f, 0f, -0.45f), 0f, 0.9f);
            Place(wheel.transform, "Prop_Support", new Vector3(0f, 0f, 0.45f), 0f, 0.9f);
            Place(wheel.transform, "Chain_Coil", new Vector3(0.6f, 0f, 0.6f), 0f, 1f);
            // The lower floor's flood (inactive until the sluice opens): world x −8.5…4.2, z 12…36.
            var flood = Empty(wheel.transform, "Flood", Vector3.zero).AddComponent<BogZone>();
            flood.transform.position = t.TransformPoint(new Vector3(-2.15f, 0f, 24f));
            flood.transform.rotation = t.rotation;
            flood.Size = new Vector2(12.7f, 24f);
            flood.Water = true;
            flood.HeroMul = 0.7f;
            flood.OtherMul = 0.85f;
            var water = Ground(flood.transform, "Water", Vector3.zero, 13f, 24.4f, _water, 3f);
            water.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            var wc = water.GetComponent<BoxCollider>();
            if (wc != null) wc.enabled = false;
            wheel.Flood = flood;
            Unstatic(wheel.gameObject);
            // The sluice itself: a grate in the north wall the water will come through.
            Box(set, "Prop_MetalFence_Ornament", new Vector3(-2f, 0f, 45.4f), new Vector3(5f, 2.4f, 0.2f), 0f, Col.None);
            Place(set, "Barrel", new Vector3(-6.6f, 0f, 16f), 0f, 1f, Col.Round);
            Place(set, "Barrel", new Vector3(-7.2f, 0f, 17f), 30f, 1f, Col.Round);
            Armable(t, ArmableKind.BarrelStack, new Vector3(-7.6f, 0f, 22f), 0f, new Vector3(4.6f, 0f, 1f), 2.8f);
            Stone(t, new Vector3(-6.8f, 0f, 31.6f), 120f, 15f);
            Explore(t, new Vector3(7.4f, top, 31.6f), -90f, "Book_Stack_1", "The sluice-keeper's log", "'Gates to open on the knight's arrival. He will not climb to us: we are not his to fight. Let the water do it.'");
            Encounter(t, new Vector3(0f, 0f, 23f), 13f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            // Down below, the fight he can see.
            Spawn(enc, "bastion_soldier", new Vector3(-2.4f, 0f, 22.6f), 180f);
            Spawn(enc, "bastion_soldier", new Vector3(-0.4f, 0f, 27.2f), 190f);
            // Up on the gallery, the crew at the wheel.
            Spawn(enc, "sluice_crew", new Vector3(6.2f, top + 0.05f, 21.6f), 0f);
            Spawn(enc, "sluice_crew", new Vector3(6.8f, top + 0.05f, 28.6f), 180f);
            Spawn(Variant(t, 1), "sluice_crew", new Vector3(6.4f, top + 0.05f, 31f), 180f);
            Spawn(enc, "baiter", new Vector3(-4.6f, 0f, 30.4f), 170f).MinIntel = 1;
            Variant(t, 0);
            RoomKit.Route(t, (new Vector3(-1.5f, 0, 0.6f), false, false, ""), (new Vector3(-1.6f, 0, 10.4f), true, true, "the sluice works"),
                (new Vector3(-1.6f, 0, 17), false, false, ""), (new Vector3(-1.5f, 0, 24), false, true, ""),
                (new Vector3(-1.4f, 0, 31), false, false, ""), (new Vector3(-0.8f, 0, 38), false, false, ""), (new Vector3(-0.8f, 0, 45.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // The Hostage Court (Ambush, nemesis): archers shelter behind hostages; a false yield in the ranks.
        // =====================================================================================================
        static GameObject HostageCourt()
        {
            var (root, mod) = Room("hostage_court", "The Hostage Court", RoomKind.Ambush, 42f);
            var t = root.transform;
            var walk = V(0, 0, 0, 10, 0.2f, 20, -0.2f, 30, 0, 42);
            Fortress(t, 42f, walk, 4303);
            var set = Empty(t, "Set", Vector3.zero).transform;
            // Pillars round the court: cover for whoever goes round the side.
            foreach (var p in new[] { new Vector3(-5f, 0f, 16f), new Vector3(5f, 0f, 16f), new Vector3(-5f, 0f, 30f), new Vector3(5f, 0f, 30f) })
                Box(set, "Wall_UnevenBrick_Straight", p, new Vector3(1f, 3.2f, 1f));
            Water(set, "Water", new Vector3(-6.2f, 0f, 23f), new Vector2(3.6f, 8f));
            Place(set, "Cage_Small", new Vector3(6.8f, 0f, 20f), 20f, 1.2f, Col.Box);
            Place(set, "Chain_Coil", new Vector3(6.2f, 0f, 21.4f), 0f, 1.2f);
            Stone(t, new Vector3(-7f, 0f, 33.4f), 135f, 15f);
            Explore(t, new Vector3(7.2f, 0f, 9.6f), -30f, "Scroll_1", "A ransom note, unsent", "'He will not strike through a hostage. Stand behind one and shoot him at leisure.' Signed with a thumbprint the grey of dead stone.");
            Encounter(t, new Vector3(0f, 0f, 24f), 12f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            // Two hostages, an archer close behind each.
            Spawn(enc, "hostage", new Vector3(-3.4f, 0f, 25.2f), 180f);
            Spawn(enc, "bastion_archer", new Vector3(-3.4f, 0f, 26.8f), 180f);
            Spawn(enc, "hostage", new Vector3(3.2f, 0f, 27.4f), 180f);
            Spawn(enc, "bastion_archer", new Vector3(3.2f, 0f, 29f), 190f);
            Spawn(enc, "bastion_soldier", new Vector3(0.4f, 0f, 21.6f), 180f);
            Spawn(enc, "turncoat", new Vector3(-0.8f, 0f, 31.8f), 180f);
            // Nemesis: a third pair at Intel 1; a baiter at 2; another bowman on the cage at 3.
            Spawn(enc, "hostage", new Vector3(0.2f, 0f, 33.4f), 180f).MinIntel = 1;
            Spawn(enc, "bastion_archer", new Vector3(0.2f, 0f, 35f), 180f).MinIntel = 1;
            Spawn(enc, "baiter", new Vector3(2.4f, 0f, 22.8f), 200f).MinIntel = 2;
            Spawn(enc, "bastion_archer", new Vector3(6.8f, 0f, 22.4f), 230f, elevated: true).MinIntel = 3;
            Spawn(Variant(t, 1), "bastion_soldier", new Vector3(-2f, 0f, 20.2f), 170f);
            Variant(t, 0);
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0, 0, 10.8f), true, true, "the hostage court"),
                (new Vector3(0.1f, 0, 17), false, false, ""), (new Vector3(0, 0, 23), false, true, ""),
                (new Vector3(0, 0, 30), false, false, ""), (new Vector3(0, 0, 36), false, false, ""), (new Vector3(0, 0, 41.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // The Baiters' Causeway (Ambush, nemesis): a raised walk over the water; baiters take his challenge and back
        // away down it, past the men lying in the water.
        // =====================================================================================================
        static GameObject BaitersCauseway()
        {
            var (root, mod) = Room("baiters_causeway", "The Baiters' Causeway", RoomKind.Ambush, 44f);
            var t = root.transform;
            var walk = V(0, 0, 0, 11, 0, 22, 0, 33, 0, 44);
            Ground(t, "Ground", new Vector3(0f, 0f, 22f), 40f, 44f, _floor, 6f);
            var rng = new DetRandom(4404);
            var w = Empty(t, "Walls", Vector3.zero).transform;
            foreach (int side in new[] { -1, 1 })
            {
                for (float z = 1f; z < 44f; z += 2f) Place(w, "Wall_UnevenBrick_Straight", new Vector3(side * 8.5f, 0f, z), side < 0 ? 90f : -90f, 1f, Col.Box);
                Blocker(w, "Blocker_" + (side < 0 ? "L" : "R"), new Vector3(side * 9.1f, 2f, 22f), new Vector3(1f, 4f, 44.5f));
                Place(w, "Lantern_Wall", new Vector3(side * 8.2f, 2.3f, side < 0 ? 12f : 30f), side < 0 ? 90f : -90f, 1f);
                // The water either side of the causeway, from the first landing to the last.
                Water(t, side < 0 ? "WaterW" : "WaterE", new Vector3(side * 5.4f, 0f, 22f), new Vector2(6f, 26f));
                for (float z = 10f; z < 35f; z += 3.2f + (float)rng.NextDouble())
                    Place(w, "Rock_Medium_" + (1 + rng.Range(0, 3)), new Vector3(side * (7f + (float)rng.NextDouble()), -0.3f, z), rng.Range(0, 360), 0.5f, Col.None, false);
            }
            Road(t, "Causeway", walk, 4.2f, _walk);
            var set = Empty(t, "Set", Vector3.zero).transform;
            for (float z = 9.6f; z < 35f; z += 2.4f)
                foreach (int side in new[] { -1, 1 })
                    Place(set, "Prop_Brick" + (1 + Mathf.RoundToInt(z) % 4), new Vector3(side * 2.3f, 0.02f, z), z * 17f, 1.2f);
            Place(set, "Banner_2", new Vector3(2.4f, 2.4f, 37f), 180f, 1f);
            Place(set, "Banner_2_Cloth", new Vector3(2.4f, 2.4f, 37f), 180f, 1f);
            Stone(t, new Vector3(6.6f, 0f, 38.4f), 220f, 16f);
            Explore(t, new Vector3(-6.8f, 0f, 39.6f), 50f, "Pouch_Large", "A baiter's purse", "Paid to accept the knight's challenge and walk backwards. 'He follows. He always follows. Mind the water.'");
            Encounter(t, new Vector3(0f, 0f, 24f), 13f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            Spawn(enc, "baiter", new Vector3(-0.6f, 0f, 30.6f), 180f);
            Spawn(enc, "baiter", new Vector3(0.8f, 0f, 33.4f), 180f);
            Spawn(enc, "drowned_ambusher", new Vector3(-3.4f, 0f, 24.6f), 90f, hidden: true);
            Spawn(enc, "drowned_ambusher", new Vector3(3.4f, 0f, 28f), -90f, hidden: true);
            Spawn(enc, "bastion_soldier", new Vector3(0f, 0f, 37.6f), 180f);
            // Nemesis: another in the water at Intel 1; a bowman on the far landing at 2; a third baiter at 3.
            Spawn(enc, "drowned_ambusher", new Vector3(-3.4f, 0f, 31.2f), 90f, hidden: true).MinIntel = 1;
            Spawn(enc, "bastion_archer", new Vector3(-5.8f, 0f, 39.4f), 150f, elevated: true).MinIntel = 2;
            Spawn(enc, "baiter", new Vector3(-0.2f, 0f, 36f), 180f).MinIntel = 3;
            Spawn(Variant(t, 1), "drowned_ambusher", new Vector3(3.4f, 0f, 20.6f), -90f, hidden: true);
            Variant(t, 0);
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0, 0, 8.6f), true, true, "the causeway"),
                (new Vector3(0, 0, 16), false, true, ""), (new Vector3(0, 0, 24), false, false, ""),
                (new Vector3(0, 0, 32), false, false, ""), (new Vector3(0, 0, 38), false, false, ""), (new Vector3(0, 0, 43.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // Wren's Rampart (Social, always 2nd): Darian Wren, a rival hero, waits on the wall walk (a scout).
        // =====================================================================================================
        static GameObject WrensRampart()
        {
            var (root, mod) = Room("wrens_rampart", "Wren's Rampart", RoomKind.Social, 40f);
            var t = root.transform;
            var walk = V(0, 0, 0, 10, 0, 20, 0, 30, 0, 40);
            Fortress(t, 40f, walk, 4505);
            var set = Empty(t, "Set", Vector3.zero).transform;
            // Crenellations along the east wall, a weapon rack, a bench for a hero who likes to be seen.
            for (float z = 10f; z < 32f; z += 2f)
                Box(set, "Wall_UnevenBrick_Straight", new Vector3(7.6f, 0f, z), new Vector3(0.8f, z % 4f < 1f ? 1.6f : 1.1f, 1.6f));
            Place(set, "WeaponStand", new Vector3(5.8f, 0f, 17.6f), -90f, 1f, Col.Box);
            Place(set, "Bench", new Vector3(4.6f, 0f, 22.4f), -90f, 1f, Col.Box);
            Place(set, "Shield_Wooden", new Vector3(5.4f, 0.4f, 23.6f), -60f, 1f);
            Place(set, "Banner_1", new Vector3(-7.9f, 3f, 20f), 90f, 1f);
            Place(set, "Banner_1_Cloth", new Vector3(-7.9f, 3f, 20f), 90f, 1f);
            Spawn(set, "scout_wren", new Vector3(3.2f, 0f, 21.2f), -110f);
            Stone(t, new Vector3(-6.8f, 0f, 26.6f), 110f, 15f);
            Explore(t, new Vector3(-7f, 0f, 9.4f), 30f, "Book_Stack_1", "A herald's satchel", "Notices for 'Sir Darian Wren, Hero of the Weir', every one unsigned. The ink is the grey of dead stone.");
            var v1 = Variant(t, 1);
            Encounter(v1, new Vector3(0f, 0f, 30f), 9f);
            Spawn(v1, "bastion_soldier", new Vector3(-1.2f, 0f, 31.6f), 180f);
            Spawn(v1, "baiter", new Vector3(1.6f, 0f, 33f), 190f);
            Variant(t, 0);
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0, 0, 8.4f), true, true, "the rampart"),
                (new Vector3(-0.4f, 0, 16), false, false, ""), (new Vector3(0, 0, 24), false, false, ""),
                (new Vector3(0, 0, 32), false, false, ""), (new Vector3(0, 0, 39.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // Caps: the sea gate behind; a drowned arch beyond the last room.
        // =====================================================================================================
        static GameObject Cap(bool start)
        {
            var root = new GameObject(start ? "bastion_cap_start" : "bastion_cap_end");
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
            Water(t, "Water", new Vector3(0f, 0f, start ? -11f : 11f), new Vector2(15f, 7f));
            if (start) Blocker(t, "Back", new Vector3(0f, 2f, -3.2f), new Vector3(30f, 4f, 1f));
            else Blocker(t, "Front", new Vector3(0f, 2f, 3.5f), new Vector3(30f, 4f, 1f));
            return root;
        }
    }
}
