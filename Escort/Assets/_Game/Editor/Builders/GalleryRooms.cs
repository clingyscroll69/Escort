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
    /// Chapter 5, the Gallery (campaign spec §4–5): the approach — the Hall of Exhibits (stones on plinths that wake for his
    /// duels, exhibits that replay his road) and the Long Gallery (stones that never sleep and sweep their gaze) — and the
    /// boss arena: an enlarged octagon round the Oath circle, six hidden archer posts, two niches with loose masonry
    /// over them for the Mirror, low walls on the camera side. Pale marble, a crimson runner, plaster walls.
    /// </summary>
    public static class GalleryRooms
    {
        public const int Chapter = 5;
        public static readonly string[] Ids = { "hall_of_exhibits", "long_gallery" };
        public static readonly string[] Caps = { "gallery_start", "gallery_end" };
        public const string ArenaId = "gallery_arena";

        static Material _floor, _runner;

        [MenuItem("Tools/HS/Build/Rooms · The Gallery")]
        public static void BuildAll()
        {
            ResetIndex();
            System.IO.Directory.CreateDirectory(RoomBuilder.Dir);
            _floor = GroundMat("Env_Floor_Marble", "T_Ground_Marble", Color.white);
            _runner = GroundMat("Env_Runner_Gallery", "T_Ground_GalleryRunner", Color.white);
            var built = new List<string> { Save(HallOfExhibits()), Save(LongGallery()), Save(Cap(true)), Save(Cap(false)), Save(Arena()) };
            AssetDatabase.SaveAssets();
            HS.Agent.AgentBridge.Write("builder.json", $"{{\"ok\":true,\"msg\":\"gallery: {string.Join(",", built)}\"}}");
        }

        static (GameObject root, RoomModule mod) Room(string id, string display, RoomKind kind, float length, float width = 26f)
        {
            var (root, mod) = NewRoom(id, display, kind, length, width);
            mod.Chapter = Chapter;
            return (root, mod);
        }

        // ------------------------------------------------------------------------------------------------- shared kit

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

        /// <summary>Marble floor, a crimson runner, plaster walls down both sides (a blocker behind), candle stands.</summary>
        static void Hall(Transform root, float length, List<Vector3> runner, int seed, float innerX = 8.5f)
        {
            Ground(root, "Ground", new Vector3(0f, 0f, length * 0.5f), 40f, length, _floor, 8f);
            Road(root, "Runner", runner, 3f, _runner);
            var rng = new DetRandom(seed);
            var w = Empty(root, "Walls", Vector3.zero).transform;
            foreach (int side in new[] { -1, 1 })
            {
                for (float z = 1f; z < length; z += 2f)
                    Place(w, rng.Chance(0.1) ? "Wall_Plaster_Door_Round" : "Wall_Plaster_Straight", new Vector3(side * innerX, 0f, z), side < 0 ? 90f : -90f, 1f, Col.Box);
                Blocker(w, "Blocker_" + (side < 0 ? "L" : "R"), new Vector3(side * (innerX + 0.6f), 2f, length * 0.5f), new Vector3(1f, 4f, length + 0.5f));
                for (float z = 5f + (side > 0 ? 4f : 0f); z < length - 2f; z += 9f)
                    Place(w, "CandleStick_Stand", new Vector3(side * (innerX - 0.8f), 0f, z), 0f, 1f, Col.Round);
                for (float z = 8f; z < length - 4f; z += 13f)
                {
                    Place(w, "Banner_2", new Vector3(side * (innerX - 0.25f), 3f, z), side < 0 ? 90f : -90f, 1f);
                    Place(w, "Banner_2_Cloth", new Vector3(side * (innerX - 0.25f), 3f, z), side < 0 ? 90f : -90f, 1f);
                }
            }
        }

        /// <summary>A plinth with something on it: a stone (that wakes for his duels), or a mannequin in someone's armour.</summary>
        static void Plinth(Transform parent, Vector3 pos, float rotY, bool stone, float range = 13f)
        {
            Box(parent, "Floor_Brick", pos, new Vector3(1.3f, 1f, 1.3f));
            if (stone) Stone(parent, pos + Vector3.up * 1.02f, rotY, range);
            else Place(parent, "Dummy", pos + Vector3.up * 1f, rotY, 1f);
        }

        /// <summary>An exhibit card: searching it reads the plaque (the exploration share; the Curator's view of his road).</summary>
        static void Exhibit(Transform parent, Vector3 pos, float rotY, string title, string note) =>
            Explore(parent, pos, rotY, "Scroll_2", title, note);

        /// <summary>A gallery balcony 2.2 m up (a marksman's perch), with stairs and a ramp the sidekick can climb.</summary>
        static void Balcony(Transform parent, string name, Vector3 pos, float yaw, string archetype, int variant = -1, Transform root = null)
        {
            var perch = Empty(parent, name, pos, yaw).transform;
            Place(perch, "Floor_WoodDark", new Vector3(0f, 2.2f, 0f), 0f, 1.1f);
            Place(perch, "Floor_WoodDark", new Vector3(0f, 2.2f, -0.4f), 0f, 1.1f);
            var top = perch.gameObject.AddComponent<BoxCollider>();
            top.center = new Vector3(0f, 2.05f, 0f);
            top.size = new Vector3(2.2f, 0.3f, 2.4f);
            Place(perch, "Prop_Support", new Vector3(-0.9f, 0f, 0.8f), 0f, 1.1f);
            Place(perch, "Prop_Support", new Vector3(0.9f, 0f, 0.8f), 0f, 1.1f);
            Place(perch, "Balcony_Simple_Straight", new Vector3(0f, 2.2f, -0.9f), 0f, 1f);
            Place(perch, "Stairs_Exterior_Straight", new Vector3(2.1f, 1.0f, -0.4f), 90f, 1f);
            Place(perch, "Stairs_Exterior_Straight", new Vector3(4.1f, 0f, -0.4f), 90f, 1f);
            var ramp = Empty(perch, "Ramp", new Vector3(3.1f, 1.05f, -0.4f)).AddComponent<BoxCollider>();
            ramp.transform.localRotation = Quaternion.Euler(0f, 0f, 28f);
            ramp.size = new Vector3(4.6f, 0.2f, 1.8f);
            var spawnParent = variant >= 0 && root != null ? Variant(root, variant) : perch;
            var sp = Spawn(spawnParent, archetype, Vector3.zero, 0f, elevated: true);
            sp.transform.position = perch.TransformPoint(new Vector3(0f, 2.25f, 0f));
            sp.transform.rotation = perch.rotation;
        }

        static void SleeplessStone(Transform parent, Vector3 pos, float rotY, float sweep, float period, float range = 14f)
        {
            Stone(parent, pos, rotY, range);
            var anchors = parent.GetComponentsInChildren<StoneAnchor>();
            var a = anchors[anchors.Length - 1];
            a.AlwaysActive = true;
            a.Sweep = sweep;
            a.SweepPeriod = period;
        }

        // =====================================================================================================
        // The Hall of Exhibits (Combat, always 1st): his road on plinths; wardens, a marksman, a steward.
        // =====================================================================================================
        static GameObject HallOfExhibits()
        {
            var (root, mod) = Room("hall_of_exhibits", "The Hall of Exhibits", RoomKind.Combat, 42f);
            var t = root.transform;
            var runner = V(0, 0, 0, 10, 0, 21, 0, 32, 0, 42);
            Hall(t, 42f, runner, 5101);
            var set = Empty(t, "Set", Vector3.zero).transform;
            Plinth(set, new Vector3(-5.6f, 0f, 12f), 60f, true);
            Plinth(set, new Vector3(5.6f, 0f, 16f), -60f, true);
            Plinth(set, new Vector3(-5.6f, 0f, 30f), 110f, true);
            Plinth(set, new Vector3(5.6f, 0f, 26f), -100f, false);
            Plinth(set, new Vector3(-5.6f, 0f, 21f), 90f, false);
            Exhibit(set, new Vector3(-7f, 0f, 14.4f), 90f, "Exhibit I: The Old Road", "A knight saluting a man with a knife behind his back. The plaque: 'He waited. They did not.'");
            Exhibit(set, new Vector3(7f, 0f, 18.6f), -90f, "Exhibit III: The Catacombs", "A cell door, open; a chain, empty. 'He freed whoever asked. He never asked why they were chained.'");
            Exhibit(set, new Vector3(7f, 0f, 34f), -90f, "Exhibit IV: The Bastion", "A woman with her wrists tied, an archer behind her. 'He would not strike through her. The archer knew it.'");
            Armable(t, ArmableKind.LooseMasonry, new Vector3(-8f, 0f, 24f), 90f, new Vector3(6.2f, 0f, 0.2f), 2.8f);
            Encounter(t, new Vector3(0f, 0f, 24f), 12f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            Spawn(enc, "gallery_warden", new Vector3(-1.4f, 0f, 23.6f), 180f);
            Spawn(enc, "gallery_warden", new Vector3(1.6f, 0f, 27.2f), 190f);
            Spawn(enc, "gallery_steward", new Vector3(0.4f, 0f, 32.6f), 180f);
            Balcony(enc, "Balcony_E", new Vector3(6.4f, 0f, 30.8f), -90f, "gallery_marksman");
            Balcony(t, "Balcony_W", new Vector3(-6.4f, 0f, 35.2f), 90f, "gallery_marksman", 1, t);
            Variant(t, 0);
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0, 0, 9.6f), true, true, "the hall of exhibits"),
                (new Vector3(0, 0, 17), false, false, ""), (new Vector3(0, 0, 24), false, true, ""),
                (new Vector3(0, 0, 31), false, false, ""), (new Vector3(0, 0, 41.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // The Long Gallery (TrapCorridor, always 2nd): stones that never sleep, sweeping; balconies; a false steward.
        // =====================================================================================================
        static GameObject LongGallery()
        {
            var (root, mod) = Room("long_gallery", "The Long Gallery", RoomKind.TrapCorridor, 46f);
            var t = root.transform;
            var runner = V(0, 0, 0, 12, 0, 23, 0, 34, 0, 46);
            Hall(t, 46f, runner, 5202);
            var set = Empty(t, "Set", Vector3.zero).transform;
            // The colonnade: columns down both sides of the runner; their shadows are where a sidekick goes unrecorded.
            for (float z = 9f; z < 40f; z += 5f)
                foreach (int side in new[] { -1, 1 })
                    Box(set, "Wall_Plaster_Straight", new Vector3(side * 3.6f, 0f, z), new Vector3(0.8f, 3.6f, 0.8f));
            SleeplessStone(set, new Vector3(-7.2f, 0f, 13f), 70f, 35f, 8f);
            SleeplessStone(set, new Vector3(7.2f, 0f, 21f), -70f, 35f, 9f);
            SleeplessStone(set, new Vector3(-7.2f, 0f, 29f), 70f, 40f, 7f);
            SleeplessStone(set, new Vector3(7.2f, 0f, 37f), -70f, 40f, 10f);
            Hazard(t, HazardKind.SpikePlate, new Vector3(0.6f, 0.01f, 17.4f));
            Hazard(t, HazardKind.SpikePlate, new Vector3(-0.5f, 0.01f, 31.2f));
            Exhibit(set, new Vector3(-6.8f, 0f, 41f), 60f, "The curator's catalogue", "Every page a person, every person a flaw, every flaw a room. One page is blank but for a question mark, and the word 'help?'");
            Encounter(t, new Vector3(0f, 0f, 28f), 13f);
            var enc = Empty(t, "Spawns", Vector3.zero).transform;
            Spawn(enc, "gallery_warden", new Vector3(0.6f, 0f, 30.4f), 180f);
            Spawn(enc, "gallery_steward", new Vector3(-0.8f, 0f, 35.6f), 180f);
            Balcony(enc, "Balcony_E", new Vector3(6.6f, 0f, 27f), -90f, "gallery_marksman");
            Balcony(enc, "Balcony_W", new Vector3(-6.6f, 0f, 33.4f), 90f, "gallery_marksman");
            Spawn(Variant(t, 1), "gallery_warden", new Vector3(-1.6f, 0f, 26.2f), 170f);
            Variant(t, 0);
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0, 0, 8.4f), true, true, "the long gallery"),
                (new Vector3(0, 0, 16), false, false, ""), (new Vector3(0, 0, 24), false, true, ""),
                (new Vector3(0, 0, 32), false, false, ""), (new Vector3(0, 0, 39), false, false, ""), (new Vector3(0, 0, 45.6f), false, false, ""));
            return root;
        }

        // =====================================================================================================
        // Caps: the outer doors behind; the gilded doors to the arena ahead.
        // =====================================================================================================
        static GameObject Cap(bool start)
        {
            var root = new GameObject(start ? "gallery_cap_start" : "gallery_cap_end");
            var t = root.transform;
            float z0 = start ? -16f : 0f;
            Ground(t, "Ground", new Vector3(0f, 0f, z0 + 8f), 40f, 16f, _floor, 8f);
            var w = Empty(t, "Walls", Vector3.zero).transform;
            foreach (int side in new[] { -1, 1 })
                for (float z = z0 + 1f; z < z0 + 16f; z += 2f)
                    Place(w, "Wall_Plaster_Straight", new Vector3(side * 8.5f, 0f, z), side < 0 ? 90f : -90f, 1f, Col.Box);
            float wallZ = start ? -6f : 6f;
            for (float x = -7.5f; x <= 7.5f; x += 2f)
                if (Mathf.Abs(x) > 1.6f) Place(w, "Wall_Plaster_Straight", new Vector3(x, 0f, wallZ), 0f, 1f, Col.Box);
            Place(w, "Wall_Plaster_Door_Round", new Vector3(0f, 0f, wallZ), 0f, 1f, Col.None);
            Place(w, "CandleStick_Stand", new Vector3(-2.4f, 0f, wallZ + (start ? 0.8f : -0.8f)), 0f, 1f, Col.Round);
            Place(w, "CandleStick_Stand", new Vector3(2.4f, 0f, wallZ + (start ? 0.8f : -0.8f)), 0f, 1f, Col.Round);
            if (start) Blocker(t, "Back", new Vector3(0f, 2f, -3.2f), new Vector3(30f, 4f, 1f));
            else Blocker(t, "Front", new Vector3(0f, 2f, 3.5f), new Vector3(30f, 4f, 1f));
            return root;
        }

        // =====================================================================================================
        // The arena (GDD §6.1, §4.5a): the Oath circle in a larger octagon; six perches on the far walls for the hidden
        // archers; two niches east and west, loose masonry over each (the Mirror's chokepoint trap).
        // =====================================================================================================
        static GameObject Arena()
        {
            var (root, mod) = Room(ArenaId, "The Gallery", RoomKind.Boss, 34f, 34f);
            var t = root.transform;
            const float cz = 17f, R = 14f;
            Ground(t, "Ground", new Vector3(0f, 0f, cz), 52f, 40f, _floor, 8f);
            var walls = Empty(t, "Walls", Vector3.zero).transform;
            float side = 2f * R * Mathf.Tan(Mathf.PI / 8f);
            for (int s = 0; s < 8; s++)
            {
                float a = s * Mathf.PI / 4f; // s=0 → +X (east) ... s=6 → −Z (south, the entry)
                var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var along = Vector3.Cross(Vector3.up, n);
                var center = new Vector3(0f, 0f, cz) + n * R;
                int pieces = Mathf.RoundToInt(side / 2f);
                for (int k = 0; k < pieces; k++)
                {
                    float off = (k - (pieces - 1) * 0.5f) * 2f;
                    if (s == 6 && Mathf.Abs(off) < 1.5f) continue; // the doors he came through
                    if ((s == 0 || s == 4) && Mathf.Abs(off) < 1.5f) continue; // the niches' mouths
                    float yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg + 90f;
                    bool nearSide = s >= 5; // the three sides nearest the camera stay low
                    var w = Place(walls, "Wall_Plaster_Straight", center + along * off, yaw, 1f, Col.Box);
                    if (nearSide) w.transform.localScale = new Vector3(1f, 0.38f, 1f);
                }
            }
            // The niches: three short walls each behind the gap in the east and west walls, loose masonry overhead.
            foreach (var (label, sx) in new[] { ("E", 1f), ("W", -1f) })
            {
                var c = new Vector3(sx * (R + 1.1f), 0f, cz);
                Place(walls, "Wall_Plaster_Straight", c + new Vector3(sx * 1.1f, 0f, 0f), 90f, 1f, Col.Box);
                Place(walls, "Wall_Plaster_Straight", c + new Vector3(0f, 0f, 1.3f), 0f, 1f, Col.Box);
                Place(walls, "Wall_Plaster_Straight", c + new Vector3(0f, 0f, -1.3f), 0f, 1f, Col.Box);
                Empty(t, "MirrorNiche_" + label, new Vector3(sx * (R - 0.6f), 0f, cz), sx > 0 ? -90f : 90f);
                Armable(t, ArmableKind.LooseMasonry, new Vector3(sx * (R + 0.4f), 0f, cz + 2.2f), 0f, new Vector3(-sx * 1.2f, 0f, -2.2f), 2.2f);
            }
            // Six perches on the far walls (the hidden gallery).
            var gal = Empty(t, "Gallery", Vector3.zero).transform;
            foreach (var (label, ang) in new[] { ("ENE", 28f), ("NE", 52f), ("N1", 78f), ("N2", 102f), ("NW", 128f), ("WNW", 152f) })
            {
                float a = ang * Mathf.Deg2Rad;
                var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var p = new Vector3(0f, 0f, cz) + n * (R - 1.7f);
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
                Place(perch, "Stairs_Exterior_Straight", new Vector3(2.2f, 1.0f, -0.6f), 90f, 1f);
                Place(perch, "Stairs_Exterior_Straight", new Vector3(4.2f, 0f, -0.6f), 90f, 1f);
                var ramp = Empty(perch, "Ramp", new Vector3(3.2f, 1.05f, -0.6f)).AddComponent<BoxCollider>();
                ramp.transform.localRotation = Quaternion.Euler(0f, 0f, 28f);
                ramp.size = new Vector3(4.6f, 0.2f, 1.9f);
                Spawn(perch, "archer", new Vector3(0f, 2.25f, 0f), 0f, hidden: true, elevated: true);
                Place(perch, "Lantern_Wall", new Vector3(-1.2f, 3.1f, 1.1f), 0f, 1f);
            }
            // The Oath circle and the marks.
            Empty(t, "OathCircle", new Vector3(0f, 0.03f, cz));
            Empty(t, "AshgraveMark", new Vector3(0f, 0f, cz + 3.2f), 180f);
            Empty(t, "CallumMark", new Vector3(0f, 0f, cz - 3.2f), 0f);
            Empty(t, "SidekickOut", new Vector3(-7f, 0f, cz - 8.5f), 30f);
            Stone(t, new Vector3(10.2f, 0f, cz + 7.4f), 225f, 16f);
            var deco = Empty(t, "Deco", Vector3.zero).transform;
            foreach (var x in new[] { -3.6f, 3.6f })
            {
                Place(deco, "Banner_2", new Vector3(x, 3.2f, cz + R - 0.35f), 180f, 1f);
                Place(deco, "Banner_2_Cloth", new Vector3(x, 3.2f, cz + R - 0.35f), 180f, 1f);
            }
            foreach (var x in new[] { -6f, 6f })
            {
                Box(deco, "Floor_Brick", new Vector3(x, 0f, cz - 1.5f), new Vector3(1.2f, 1f, 1.2f));
                Place(deco, "Dummy", new Vector3(x, 1f, cz - 1.5f), 180f, 1f);
                Place(deco, "CandleStick_Stand", new Vector3(x * 0.92f, 0f, cz + 1f), 0f, 1f, Col.Round);
            }
            RoomKit.Route(t, (new Vector3(0, 0, 0.6f), false, false, ""), (new Vector3(0, 0, 4.2f), true, true, "the gallery doors"),
                (new Vector3(0, 0, cz - 3.2f), false, false, "duel"));
            return root;
        }
    }
}
