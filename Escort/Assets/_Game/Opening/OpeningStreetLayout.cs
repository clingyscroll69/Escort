using System.Collections.Generic;
using HS.UI;
using TMPro;
using UnityEngine;

namespace HS.Opening
{
    /// <summary>The street's layout (spec §3) and everything in it that moves: traffic, the truck, signals, pigeons.</summary>
    public sealed partial class OpeningStreet
    {
        // ------------------------------------------------------------------------------------------------- layout
        public const float SouthCurb = 10.5f, NorthCurb = 22.5f, RampTop = 9.3f, TruckLane = 18.5f;
        const float WestCurbE = -2.2f, EastCurbW = -14.2f;            // the N-S street's curbs (x)
        const float FrontE = 2.6f, FrontW = -18.6f, FrontS = 6.1f, FrontN = 26.9f;  // building front lines
        const float XwalkX0 = -1.1f, XwalkX1 = 1.9f;                   // our crosswalk (and its ramps)
        const float CornerR = 1f, Far = 170f;

        enum Sub { Road, Sidewalk, Curb, Tactile, Paint, Yellow, Gutter, Count }

        readonly List<Mover> _movers = new List<Mover>();
        readonly List<Pigeon> _pigeons = new List<Pigeon>();
        readonly List<Lamp> _pedHand = new List<Lamp>(), _pedWalk = new List<Lamp>();
        readonly List<Lamp> _nsRed = new List<Lamp>(), _nsAmber = new List<Lamp>(), _nsGreen = new List<Lamp>();
        readonly List<Lamp> _ewRed = new List<Lamp>(), _ewAmber = new List<Lamp>(), _ewGreen = new List<Lamp>();
        readonly List<TextMeshPro> _countdowns = new List<TextMeshPro>();
        Transform _staticRoot, _truck, _truckBody;
        readonly List<Transform> _truckWheels = new List<Transform>();
        readonly List<Light> _headlights = new List<Light>();
        readonly List<Transform> _flares = new List<Transform>();
        readonly List<Renderer> _flareRenderers = new List<Renderer>();
        ParticleSystem _steam, _tyreSmoke;
        MaterialPropertyBlock _mpb;

        sealed class Mover
        {
            public Transform T;
            public readonly List<Transform> Wheels = new List<Transform>();
            public float Radius = 0.32f;
            public System.Func<float, Vector3> Pos;
            public float Yaw, From = -99f, To = 99f;
        }

        sealed class Pigeon
        {
            public Transform T, WingL, WingR;
            public Vector3 Home, Flight;
            public float Yaw, TakeOff, Phase;
        }

        /// <summary>One lamp face on a signal: a material slot we light or darken.</summary>
        struct Lamp
        {
            public Material Mat;
            public Color On, Off;
        }

        // -------------------------------------------------------------------------------------------------- build
        void BuildWorld()
        {
            _mpb = new MaterialPropertyBlock();
            _staticRoot = new GameObject("Static").transform;
            _staticRoot.SetParent(_root, false);
            BuildGround();
            BuildBuildings();
            BuildFurniture();
            BuildSignals();
            BuildTraffic();
            BuildTruck();
            BuildPigeons();
            BuildParticles();
            SetLayer(_staticRoot, _layer);
            StaticBatchingUtility.Combine(_staticRoot.gameObject);
        }

        Material Mat(Material m, Color fallback)
        {
            if (m != null) return m;
            var mat = new Material(Shader.Find("HS/Toon")) { color = fallback };
            mat.SetColor("_BaseColor", fallback);
            return mat;
        }

        void BuildGround()
        {
            var a = _assets;
            var mk = new MeshKit((int)Sub.Count);
            // the road: one sheet under everything
            mk.Flat((int)Sub.Road, -Far, Far, -120f, 230f, 0f, 4f);
            // gutters along every curb
            Gutter(mk, WestCurbE, WestCurbE + 0.0f, -120f, SouthCurb, true);
            Gutter(mk, WestCurbE, WestCurbE, NorthCurb, 230f, true);
            Gutter(mk, EastCurbW, EastCurbW, -120f, SouthCurb, false);
            Gutter(mk, EastCurbW, EastCurbW, NorthCurb, 230f, false);
            mk.Flat((int)Sub.Gutter, WestCurbE + 0.0f, Far, SouthCurb, SouthCurb + 0.32f, 0.003f, 2f);
            mk.Flat((int)Sub.Gutter, -Far, EastCurbW, SouthCurb, SouthCurb + 0.32f, 0.003f, 2f);
            mk.Flat((int)Sub.Gutter, WestCurbE, Far, NorthCurb - 0.32f, NorthCurb, 0.003f, 2f);
            mk.Flat((int)Sub.Gutter, -Far, EastCurbW, NorthCurb - 0.32f, NorthCurb, 0.003f, 2f);

            // ---- SE block (we start here): our sidewalk, the corner, the ramp onto the crosswalk, the cross-street sidewalk
            Slab(mk, WestCurbE, FrontE, -120f, RampTop, w: true);
            Slab(mk, WestCurbE, XwalkX0, RampTop, SouthCurb - CornerR, w: true);
            Slab(mk, WestCurbE + CornerR, XwalkX0, SouthCurb - CornerR, SouthCurb, n: true);
            Corner(mk, new Vector2(WestCurbE + CornerR, SouthCurb - CornerR), 90f, 180f);
            Ramp(mk, XwalkX0, XwalkX1, RampTop, SouthCurb, down: true);
            Slab(mk, XwalkX1, FrontE, RampTop, SouthCurb, n: true);
            Slab(mk, FrontE, Far, FrontS, SouthCurb, n: true);
            mk.Flat((int)Sub.Sidewalk, FrontE, Far, -120f, FrontS, Curb, 2f);
            // ---- NE block (ahead)
            Slab(mk, WestCurbE, FrontE, NorthCurb + 1.2f, 230f, w: true);
            Slab(mk, WestCurbE, XwalkX0, NorthCurb + CornerR, NorthCurb + 1.2f, w: true);
            Slab(mk, WestCurbE + CornerR, XwalkX0, NorthCurb, NorthCurb + CornerR, s: true);
            Corner(mk, new Vector2(WestCurbE + CornerR, NorthCurb + CornerR), 180f, 270f);
            Ramp(mk, XwalkX0, XwalkX1, NorthCurb + 1.2f, NorthCurb, down: false);
            Slab(mk, XwalkX1, FrontE, NorthCurb, NorthCurb + 1.2f, s: true);
            Slab(mk, FrontE, Far, NorthCurb, FrontN, s: true);
            mk.Flat((int)Sub.Sidewalk, FrontE, Far, FrontN, 230f, Curb, 2f);
            // ---- NW block (ahead-left)
            Slab(mk, FrontW, EastCurbW, NorthCurb + CornerR, 230f, e: true);
            Slab(mk, -Far, EastCurbW - CornerR, NorthCurb, FrontN, s: true);
            Slab(mk, EastCurbW - CornerR, EastCurbW, NorthCurb + CornerR, FrontN, e: true);
            Corner(mk, new Vector2(EastCurbW - CornerR, NorthCurb + CornerR), 270f, 360f);
            mk.Flat((int)Sub.Sidewalk, -Far, FrontW, FrontN, 230f, Curb, 2f);
            // ---- SW block (left)
            Slab(mk, FrontW, EastCurbW, -120f, SouthCurb - CornerR, e: true);
            Slab(mk, -Far, EastCurbW - CornerR, FrontS, SouthCurb, n: true);
            Slab(mk, EastCurbW - CornerR, EastCurbW, FrontS, SouthCurb - CornerR, e: true);
            Corner(mk, new Vector2(EastCurbW - CornerR, SouthCurb - CornerR), 0f, 90f);
            mk.Flat((int)Sub.Sidewalk, -Far, FrontW, -120f, FrontS, Curb, 2f);

            Markings(mk);

            var go = new GameObject("Ground");
            go.transform.SetParent(_staticRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = mk.Build("OpeningGround");
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[]
            {
                Mat(a?.road, new Color(0.23f, 0.25f, 0.28f)), Mat(a?.sidewalk, new Color(0.73f, 0.71f, 0.67f)),
                Mat(a?.curb, new Color(0.8f, 0.79f, 0.75f)), Mat(a?.tactile, new Color(0.89f, 0.68f, 0.16f)),
                Mat(a?.paintWhite, new Color(0.92f, 0.92f, 0.9f)), Mat(a?.paintYellow, new Color(0.93f, 0.74f, 0.22f)),
                Mat(a?.gutter, new Color(0.55f, 0.54f, 0.52f)),
            };
        }

        static void Gutter(MeshKit mk, float curbX, float _, float z0, float z1, bool roadIsWest)
        {
            if (roadIsWest) mk.Flat((int)Sub.Gutter, curbX - 0.32f, curbX, z0, z1, 0.003f, 2f);
            else mk.Flat((int)Sub.Gutter, curbX, curbX + 0.32f, z0, z1, 0.003f, 2f);
        }

        /// <summary>A sidewalk slab with curb faces (and a granite curb strip on top) on the road-side edges.</summary>
        static void Slab(MeshKit mk, float x0, float x1, float z0, float z1, bool w = false, bool e = false, bool s = false, bool n = false)
        {
            if (x1 <= x0 || z1 <= z0) return;
            const float strip = 0.18f;
            mk.Flat((int)Sub.Sidewalk, x0, x1, z0, z1, Curb, 2f);
            if (w)
            {
                mk.Wall((int)Sub.Curb, new Vector2(x0, z1), new Vector2(x0, z0), 0f, Curb, 1f);
                mk.Flat((int)Sub.Curb, x0, x0 + strip, z0, z1, Curb + 0.002f, 1f);
            }
            if (e)
            {
                mk.Wall((int)Sub.Curb, new Vector2(x1, z0), new Vector2(x1, z1), 0f, Curb, 1f);
                mk.Flat((int)Sub.Curb, x1 - strip, x1, z0, z1, Curb + 0.002f, 1f);
            }
            if (s)
            {
                mk.Wall((int)Sub.Curb, new Vector2(x0, z0), new Vector2(x1, z0), 0f, Curb, 1f);
                mk.Flat((int)Sub.Curb, x0, x1, z0, z0 + strip, Curb + 0.002f, 1f);
            }
            if (n)
            {
                mk.Wall((int)Sub.Curb, new Vector2(x1, z1), new Vector2(x0, z1), 0f, Curb, 1f);
                mk.Flat((int)Sub.Curb, x0, x1, z1 - strip, z1, Curb + 0.002f, 1f);
            }
        }

        /// <summary>A rounded curb return: a quarter disc of sidewalk and its curb face.</summary>
        static void Corner(MeshKit mk, Vector2 centre, float a0, float a1)
        {
            mk.Fan((int)Sub.Sidewalk, centre, CornerR, a0, a1, Curb, 2f, 10);
            mk.ArcWall((int)Sub.Curb, centre, CornerR, a0, a1, 0f, Curb, 1f, 10);
        }

        /// <summary>A curb ramp spanning x0..x1, from the sidewalk at zTop down to the road at zRoad, with tactile paving.</summary>
        static void Ramp(MeshKit mk, float x0, float x1, float zTop, float zRoad, bool down)
        {
            const float roadY = 0.012f;
            Vector2 U(float x, float z) => new Vector2(x / 2f, z / 2f);
            if (down)
            {
                mk.Quad((int)Sub.Sidewalk, new Vector3(x0, Curb, zTop), new Vector3(x0, roadY, zRoad), new Vector3(x1, roadY, zRoad), new Vector3(x1, Curb, zTop),
                    U(x0, zTop), U(x0, zRoad), U(x1, zRoad), U(x1, zTop));
                // tactile warning pad on the last 0.6 m
                float za = zRoad - 0.62f, zb = zRoad - 0.04f;
                float ya = Mathf.Lerp(Curb, roadY, Mathf.InverseLerp(zTop, zRoad, za)) + 0.004f;
                float yb = Mathf.Lerp(Curb, roadY, Mathf.InverseLerp(zTop, zRoad, zb)) + 0.004f;
                mk.Quad((int)Sub.Tactile, new Vector3(x0 + 0.05f, ya, za), new Vector3(x0 + 0.05f, yb, zb), new Vector3(x1 - 0.05f, yb, zb), new Vector3(x1 - 0.05f, ya, za),
                    new Vector2(x0, za), new Vector2(x0, zb), new Vector2(x1, zb), new Vector2(x1, za));
                // the flares' side faces
                mk.Quad((int)Sub.Curb, new Vector3(x0, 0f, zRoad), new Vector3(x0, roadY, zRoad), new Vector3(x0, Curb, zTop), new Vector3(x0, 0f, zTop), Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
                mk.Quad((int)Sub.Curb, new Vector3(x1, 0f, zTop), new Vector3(x1, Curb, zTop), new Vector3(x1, roadY, zRoad), new Vector3(x1, 0f, zRoad), Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
            }
            else
            {
                mk.Quad((int)Sub.Sidewalk, new Vector3(x0, roadY, zRoad), new Vector3(x0, Curb, zTop), new Vector3(x1, Curb, zTop), new Vector3(x1, roadY, zRoad),
                    U(x0, zRoad), U(x0, zTop), U(x1, zTop), U(x1, zRoad));
                float za = zRoad + 0.04f, zb = zRoad + 0.62f;
                float ya = Mathf.Lerp(roadY, Curb, Mathf.InverseLerp(zRoad, zTop, za)) + 0.004f;
                float yb = Mathf.Lerp(roadY, Curb, Mathf.InverseLerp(zRoad, zTop, zb)) + 0.004f;
                mk.Quad((int)Sub.Tactile, new Vector3(x0 + 0.05f, ya, za), new Vector3(x0 + 0.05f, yb, zb), new Vector3(x1 - 0.05f, yb, zb), new Vector3(x1 - 0.05f, ya, za),
                    new Vector2(x0, za), new Vector2(x0, zb), new Vector2(x1, zb), new Vector2(x1, za));
                mk.Quad((int)Sub.Curb, new Vector3(x0, 0f, zTop), new Vector3(x0, Curb, zTop), new Vector3(x0, roadY, zRoad), new Vector3(x0, 0f, zRoad), Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
                mk.Quad((int)Sub.Curb, new Vector3(x1, 0f, zRoad), new Vector3(x1, roadY, zRoad), new Vector3(x1, Curb, zTop), new Vector3(x1, 0f, zTop), Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
            }
        }

        /// <summary>Zebra crossings on all four legs, stop lines, double yellow centre lines, parking-lane edges.</summary>
        static void Markings(MeshKit mk)
        {
            const float y = 0.006f;
            // continental bars: 0.5 m wide, 0.5 m apart, parallel to the traffic
            for (float z = SouthCurb + 0.35f; z + 0.5f <= NorthCurb - 0.3f; z += 1f)
            {
                Bar(mk, XwalkX0, XwalkX1, z, z + 0.5f, y);                  // ours (east leg)
                Bar(mk, EastCurbW - 3.4f, EastCurbW - 0.4f, z, z + 0.5f, y); // west leg
            }
            for (float x = EastCurbW + 0.35f; x + 0.5f <= WestCurbE - 0.3f; x += 1f)
            {
                Bar(mk, x, x + 0.5f, NorthCurb + 1.1f, NorthCurb + 4.1f, y);  // north leg
                Bar(mk, x, x + 0.5f, SouthCurb - 4.2f, SouthCurb - 1.2f, y);  // south leg
            }
            // stop lines
            Bar(mk, XwalkX1 + 1.2f, XwalkX1 + 1.6f, 16.62f, 20.5f, y);          // westbound (the truck's)
            Bar(mk, EastCurbW - 4.8f, EastCurbW - 4.4f, 12.5f, 16.38f, y);      // eastbound
            Bar(mk, -8.08f, -4.2f, SouthCurb - 5.6f, SouthCurb - 5.2f, y);      // northbound
            Bar(mk, -12.2f, -8.32f, NorthCurb + 5.2f, NorthCurb + 5.6f, y);     // southbound
            // centre lines (double yellow) and parking edges (white), stopping short of the crossings
            foreach (var (x0, x1) in new[] { (XwalkX1 + 1.7f, Far), (-Far, EastCurbW - 4.9f) })
            {
                Line(mk, x0, x1, 16.38f, 16.48f, y, true);
                Line(mk, x0, x1, 16.52f, 16.62f, y, true);
                Line(mk, x0, x1, 12.45f, 12.55f, y, false);
                Line(mk, x0, x1, 20.45f, 20.55f, y, false);
            }
            foreach (var (z0, z1) in new[] { (-120f, SouthCurb - 5.7f), (NorthCurb + 5.7f, 230f) })
            {
                Line(mk, -8.32f, -8.22f, z0, z1, y, true);
                Line(mk, -8.18f, -8.08f, z0, z1, y, true);
                Line(mk, -4.25f, -4.15f, z0, z1, y, false);
                Line(mk, -12.25f, -12.15f, z0, z1, y, false);
            }
        }

        static void Bar(MeshKit mk, float x0, float x1, float z0, float z1, float y)
        {
            mk.Quad((int)Sub.Paint, new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0),
                new Vector2(x0, z0) * 0.5f, new Vector2(x0, z1) * 0.5f, new Vector2(x1, z1) * 0.5f, new Vector2(x1, z0) * 0.5f);
        }

        static void Line(MeshKit mk, float x0, float x1, float z0, float z1, float y, bool yellow)
        {
            mk.Quad(yellow ? (int)Sub.Yellow : (int)Sub.Paint, new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0),
                new Vector2(x0, z0) * 0.5f, new Vector2(x0, z1) * 0.5f, new Vector2(x1, z1) * 0.5f, new Vector2(x1, z0) * 0.5f);
        }

        // ---------------------------------------------------------------------------------------------- placing
        /// <summary>An instance of a kit prefab (or a stand-in) at a street-space position, yaw in degrees (0 = front +Z).</summary>
        GameObject Place(string id, Vector3 pos, float yaw, Transform parent = null, float scale = 1f)
        {
            var prefab = _assets != null ? _assets.Get(id) : null;
            var go = prefab != null ? Instantiate(prefab) : StandIn(id);
            go.name = id;
            go.transform.SetParent(parent != null ? parent : _staticRoot, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            if (scale != 1f) go.transform.localScale *= scale;
            return go;
        }

        /// <summary>Building fronts (spec §3). Corner buildings sit on their exterior corner, others on their front's centre.</summary>
        void BuildBuildings()
        {
            const float g = Curb;
            // corners: SE café (front west), NE mart (front south), NW pharmacy (front south), SW diner (front east)
            Place("CornerCafe", new Vector3(FrontE, g, FrontS), -90f);
            Place("CornerMart", new Vector3(FrontE, g, FrontN), 180f);
            Place("CornerPharmacy", new Vector3(FrontW, g, FrontN), 180f);
            Place("CornerDiner", new Vector3(FrontW, g, FrontS), 90f);
            // the cross street, east of us: south side faces north, north side faces south
            float x = FrontE + 14f;
            foreach (var (id, w) in new[] { ("NoodleBar", 12f), ("Laundromat", 10f), ("Bakery", 16f), ("ForLease", 12f), ("Bank", 18f), ("Books", 12f), ("PhoneRepair", 14f) })
            {
                Place(id, new Vector3(x + w / 2f, g, FrontS), 0f);
                x += w;
            }
            x = FrontE + 15.4f;
            foreach (var (id, w) in new[] { ("Books", 12f), ("PhoneRepair", 14f), ("Florist", 10f), ("Bakery", 16f), ("Bank", 18f), ("NoodleBar", 12f), ("Laundromat", 10f) })
            {
                Place(id, new Vector3(x + w / 2f, g, FrontN), 180f);
                x += w;
            }
            // up our street, ahead on the right (fronts face west)
            float z = FrontN + 15f;
            foreach (var (id, w) in new[] { ("PhoneRepair", 14f), ("Laundromat", 10f), ("Florist", 10f), ("ForLease", 12f), ("Bakery", 16f), ("Books", 12f), ("Bank", 18f) })
            {
                Place(id, new Vector3(FrontE, g, z + w / 2f), -90f);
                z += w;
            }
            // across the avenue: the NW block faces east and south, the SW block faces east and north
            z = FrontN + 17f;
            foreach (var (id, w) in new[] { ("ForLease", 12f), ("Bank", 18f), ("NoodleBar", 12f), ("Books", 12f), ("Laundromat", 10f), ("Bakery", 16f) })
            {
                Place(id, new Vector3(FrontW, g, z + w / 2f), 90f);
                z += w;
            }
            x = FrontW - 15.4f;
            foreach (var (id, w) in new[] { ("Florist", 10f), ("Bakery", 16f), ("PhoneRepair", 14f), ("NoodleBar", 12f) })
            {
                Place(id, new Vector3(x - w / 2f, g, FrontN), 180f);
                x -= w;
            }
            z = FrontS - 16f;
            foreach (var (id, w) in new[] { ("Books", 12f), ("PhoneRepair", 14f), ("Bakery", 16f), ("Laundromat", 10f), ("Bank", 18f) })
            {
                Place(id, new Vector3(FrontW, g, z - w / 2f), 90f);
                z -= w;
            }
            x = FrontW - 15.4f;
            foreach (var (id, w) in new[] { ("Laundromat", 10f), ("Bakery", 16f), ("ForLease", 12f) })
            {
                Place(id, new Vector3(x - w / 2f, g, FrontS), 0f);
                x -= w;
            }
            // the skyline, in the haze
            Place("Tower1", new Vector3(36f, 0f, 205f), 180f);
            Place("Tower2", new Vector3(-64f, 0f, 240f), 180f);
            Place("Tower4", new Vector3(108f, 0f, 230f), 200f);
            Place("Tower3", new Vector3(205f, 0f, 30f), -90f);
            Place("Tower2", new Vector3(230f, 0f, -40f), -90f);
            Place("Tower3", new Vector3(-210f, 0f, 60f), 90f);
        }

        void BuildFurniture()
        {
            const float g = Curb;
            // street lamps along the curbs, heads over the road
            foreach (var z in new[] { -14f, 4.5f, 31f, 56f, 81f }) Place("StreetLamp", new Vector3(WestCurbE + 0.45f, g, z), -90f);
            foreach (var z in new[] { -2f, 31f, 58f }) Place("StreetLamp", new Vector3(EastCurbW - 0.45f, g, z), 90f);
            foreach (var xx in new[] { 13f, 39f, 65f }) Place("StreetLamp", new Vector3(xx, g, SouthCurb - 0.45f), 0f);
            foreach (var xx in new[] { 11f, 37f, 63f, -30f }) Place("StreetLamp", new Vector3(xx, g, NorthCurb + 0.45f), 180f);
            // our corner: street names, hydrant, news boxes, a bin; the café's tables and board
            Place("SignPost", new Vector3(WestCurbE + 0.4f, g, SouthCurb - 1.55f), 45f);
            Place("FireHydrant", new Vector3(WestCurbE + 0.5f, g, 7.4f), -90f);
            Place("NewsBox", new Vector3(WestCurbE + 0.55f, g, 5.3f), 90f);
            Place("NewsBox", new Vector3(WestCurbE + 0.55f, g, 6.05f), 90f);
            Place("TrashCan", new Vector3(WestCurbE + 0.55f, g, 1.6f), 90f);
            Place("ParkingMeter", new Vector3(WestCurbE + 0.35f, g, -6f), -90f);
            Place("ParkingMeter", new Vector3(WestCurbE + 0.35f, g, -12f), -90f);
            Place("BikeRack", new Vector3(WestCurbE + 0.6f, g, -2.6f), 0f);
            Place("CafeTable", new Vector3(FrontE - 0.75f, g, -1.2f), 0f);
            Place("CafeChair", new Vector3(FrontE - 0.75f, g, -1.85f), 0f);
            Place("CafeChair", new Vector3(FrontE - 0.75f, g, -0.55f), 180f);
            Place("CafeTable", new Vector3(FrontE - 0.75f, g, 1.6f), 0f);
            Place("CafeChair", new Vector3(FrontE - 0.75f, g, 0.95f), 0f);
            Place("AFrameSign", new Vector3(FrontE - 0.9f, g, 3.6f), 180f);
            // ahead: the NE corner's bins and bus stop (its ad glows across the street)
            Place("TrashCan", new Vector3(WestCurbE + 0.55f, g, NorthCurb + 2.6f), 90f);
            Place("Bollard", new Vector3(XwalkX1 + 0.35f, g, NorthCurb + 0.45f), 0f);
            Place("Bollard", new Vector3(XwalkX0 - 0.35f, g, NorthCurb + 0.45f), 0f);
            Place("BusShelter", new Vector3(10.5f, g, NorthCurb + 1.6f), 180f);
            Place("Planter", new Vector3(FrontE - 0.7f, g, NorthCurb + 3.0f), 0f);
            Place("NewsBox", new Vector3(5.3f, g, NorthCurb + 0.55f), 0f);
            Place("FireHydrant", new Vector3(EastCurbW - 0.5f, g, NorthCurb + 2.4f), 90f);
            Place("SignPost", new Vector3(EastCurbW - 0.4f, g, SouthCurb - 1.4f), -45f);
            Place("TrashCan", new Vector3(EastCurbW - 0.55f, g, -3f), -90f);
            Place("Bench", new Vector3(FrontW + 0.6f, g, 36f), 90f);
            // street trees in grates (late winter: mostly bare)
            var trees = new[] { "DeadTree_1", "DeadTree_3", "CommonTree_2", "DeadTree_2", "DeadTree_4", "CommonTree_4", "DeadTree_5" };
            int ti = 0;
            void Tree(Vector3 p, float yaw)
            {
                Place("TreeGrate", new Vector3(p.x, g + 0.003f, p.z), 0f);
                var tr = Place(trees[ti++ % trees.Length], new Vector3(p.x, g, p.z), yaw, null, 0.85f);
                if (_assets == null || _assets.Get(tr.name) == null) tr.transform.localScale = Vector3.one;
            }
            foreach (var z in new[] { -9f, -1.5f, 34f, 46f, 62f }) Tree(new Vector3(WestCurbE + 0.75f, 0f, z), z * 37f);
            foreach (var xx in new[] { 8f, 20.5f, 33f, 46f }) Tree(new Vector3(xx, 0f, SouthCurb - 0.8f), xx * 53f);
            foreach (var xx in new[] { 18f, 30.5f, 44f }) Tree(new Vector3(xx, 0f, NorthCurb + 0.8f), xx * 41f);
            foreach (var z in new[] { -12f, 38f, 52f }) Tree(new Vector3(EastCurbW - 0.75f, 0f, z), z * 29f);
            // the road: manholes, storm drains
            Place("ManholeCover", new Vector3(-6.4f, 0.004f, 14.2f), 0f);
            Place("ManholeCover", new Vector3(6.5f, 0.004f, 18.6f), 30f);
            Place("ManholeCover", new Vector3(-10.1f, 0.004f, 44f), 0f);
            Place("StormDrain", new Vector3(WestCurbE - 0.2f, 0.004f, 7.8f), 90f);
            Place("StormDrain", new Vector3(4.5f, 0.004f, SouthCurb + 0.2f), 0f);
            Place("StormDrain", new Vector3(7.5f, 0.004f, NorthCurb - 0.2f), 180f);
            // parked cars: the avenue's east lane (beside us), its west lane, the cross street
            Parked("SUV", new Vector3(-3.2f, 0f, -21f), 0f, new Color(0.24f, 0.27f, 0.3f));
            Parked("Van", new Vector3(-3.2f, 0f, -13.6f), 0f, new Color(0.86f, 0.86f, 0.84f));
            Parked("Sedan", new Vector3(-3.2f, 0f, -6.3f), 0f, new Color(0.62f, 0.64f, 0.67f));
            Parked("Hatchback", new Vector3(-13.2f, 0f, -5f), 180f, new Color(0.18f, 0.36f, 0.5f));
            Parked("Sedan", new Vector3(-13.2f, 0f, 2.6f), 180f, new Color(0.46f, 0.12f, 0.15f));
            Parked("SUV", new Vector3(-13.2f, 0f, 44f), 180f, new Color(0.88f, 0.88f, 0.86f));
            Parked("Hatchback", new Vector3(11.5f, 0f, 21.5f), -90f, new Color(0.85f, 0.55f, 0.18f));
            Parked("Sedan", new Vector3(18.8f, 0f, 21.5f), -90f, new Color(0.2f, 0.22f, 0.25f));
            Parked("Van", new Vector3(31f, 0f, 21.5f), -90f, new Color(0.32f, 0.45f, 0.36f));
            Parked("SUV", new Vector3(13f, 0f, 11.5f), 90f, new Color(0.52f, 0.53f, 0.56f));
            Parked("Sedan", new Vector3(20.5f, 0f, 11.5f), 90f, new Color(0.15f, 0.25f, 0.45f));
            Parked("Hatchback", new Vector3(-24f, 0f, 21.5f), -90f, new Color(0.7f, 0.18f, 0.16f));
        }

        void Parked(string id, Vector3 front, float yaw, Color paint)
        {
            var car = Place(id, front, yaw);
            Paint(car, paint);
        }

        /// <summary>Per-instance body colour (the kits author car paint as one neutral material).</summary>
        void Paint(GameObject car, Color c)
        {
            foreach (var r in car.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && (mats[i].name.StartsWith("MV_CarPaint") || mats[i].name == "StandInPaint"))
                    {
                        mats[i] = new Material(mats[i]) { name = "MV_CarPaint (" + car.name + ")" };
                        mats[i].SetColor("_BaseColor", c);
                        changed = true;
                    }
                if (changed) r.sharedMaterials = mats;
            }
        }

        // ------------------------------------------------------------------------------------------------ signals
        void BuildSignals()
        {
            const float g = Curb;
            // the one we watch: across the street, facing us
            var ped = Place("PedPole", new Vector3(WestCurbE + 0.35f, g, NorthCurb + 0.75f), 180f);
            Hook(ped, _pedHand, _pedWalk, true);
            // ours, facing back the other way (its back to us), and the avenue crossings
            Hook(Place("PedPole", new Vector3(WestCurbE + 0.35f, g, SouthCurb - 0.75f), 0f), _pedHand, _pedWalk, false);
            Hook(Place("PedPole", new Vector3(EastCurbW - 0.35f, g, NorthCurb + 0.75f), 180f), _pedHand, _pedWalk, true);
            Hook(Place("PedPole", new Vector3(EastCurbW - 0.35f, g, SouthCurb - 0.75f), 0f), _pedHand, _pedWalk, false);
            // vehicle signals: the avenue's heads over its lanes (north side faces us), the cross street's heads
            Lights(Place("TrafficSignalMast", new Vector3(WestCurbE + 0.45f, g, NorthCurb + 0.5f), 180f), _nsRed, _nsAmber, _nsGreen);
            Lights(Place("TrafficSignalMast", new Vector3(EastCurbW - 0.45f, g, SouthCurb - 0.5f), 0f), _nsRed, _nsAmber, _nsGreen);
            Lights(Place("TrafficSignalMast", new Vector3(EastCurbW - 0.5f, g, NorthCurb + 0.45f), 90f), _ewRed, _ewAmber, _ewGreen);
            Lights(Place("TrafficSignalMast", new Vector3(WestCurbE + 0.5f, g, SouthCurb - 0.45f), -90f), _ewRed, _ewAmber, _ewGreen);
        }

        /// <summary>Find the hand/walk faces (and the countdown) on a pedestrian signal and give them their own materials.</summary>
        void Hook(GameObject sig, List<Lamp> hand, List<Lamp> walk, bool countdown)
        {
            foreach (var r in sig.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string n = mats[i].name;
                    if (n.StartsWith("MS_PedHand")) { mats[i] = new Material(mats[i]); hand.Add(new Lamp { Mat = mats[i], On = new Color(1f, 0.42f, 0.08f) * 3.2f, Off = Color.black }); changed = true; }
                    else if (n.StartsWith("MS_PedWalk")) { mats[i] = new Material(mats[i]); walk.Add(new Lamp { Mat = mats[i], On = new Color(0.9f, 0.95f, 1f) * 3.4f, Off = Color.black }); changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
            if (!countdown) return;
            var anchor = Find(sig.transform, "Countdown");
            if (anchor == null) return;
            var go = new GameObject("CountdownDigits");
            go.transform.SetParent(anchor, false);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // TMP faces -Z; the panel faces +Z
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = UIKit.Mono;
            tmp.fontSize = 1.6f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(1f, 0.55f, 0.18f);
            tmp.rectTransform.sizeDelta = new Vector2(0.2f, 0.2f);
            tmp.text = "";
            _countdowns.Add(tmp);
        }

        void Lights(GameObject sig, List<Lamp> red, List<Lamp> amber, List<Lamp> green)
        {
            foreach (var r in sig.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string n = mats[i].name;
                    List<Lamp> list = n.StartsWith("MS_LensRed") ? red : n.StartsWith("MS_LensAmber") ? amber : n.StartsWith("MS_LensGreen") ? green : null;
                    if (list == null) continue;
                    mats[i] = new Material(mats[i]);
                    var on = list == red ? new Color(1f, 0.12f, 0.08f) : list == amber ? new Color(1f, 0.6f, 0.05f) : new Color(0.15f, 1f, 0.55f);
                    list.Add(new Lamp { Mat = mats[i], On = on * 3.5f, Off = Color.black });
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = Find(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }

        static void SetLamps(List<Lamp> lamps, bool on)
        {
            foreach (var l in lamps)
            {
                l.Mat.SetColor("_EmissionColor", on ? l.On : l.Off);
                l.Mat.SetFloat("_EmissionTexMul", 1f);
                l.Mat.SetFloat("_DirectStrength", on ? 0.4f : 0.9f);
                l.Mat.SetFloat("_AmbientStrength", on ? 0.3f : 0.4f);
            }
        }

        // ------------------------------------------------------------------------------------------------ traffic
        Mover Move(string id, Color paint, float yaw, System.Func<float, Vector3> pos, float from, float to)
        {
            var go = Place(id, pos(from), yaw, _root);
            Paint(go, paint);
            var m = new Mover { T = go.transform, Pos = pos, Yaw = yaw, From = from, To = to };
            foreach (var w in new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" })
            {
                var wt = Find(go.transform, w);
                if (wt != null) m.Wheels.Add(wt);
            }
            _movers.Add(m);
            return m;
        }

        /// <summary>The audio's passing cars (2.4 s, 6.6 s, 10.2 s) and the traffic that obeys the lights.</summary>
        void BuildTraffic()
        {
            // 2.4 s: eastbound across the street ahead, while the hand is still red
            Move("Sedan", new Color(0.17f, 0.32f, 0.62f), 90f, t => new Vector3(12f * (t - 2.4f), 0f, 14.4f), -3f, 9f);
            // 6.6 s: the last taxi through on the amber
            Move("Taxi", Color.white, -90f, t => new Vector3(-11f * (t - 6.6f), 0f, 18.6f), 0f, 13f);
            // 10.2 s: northbound on the avenue, overtaking on the left
            Move("Hatchback", new Color(0.66f, 0.13f, 0.12f), 0f, t => new Vector3(-6.2f, 0f, WalkerZ(10.2f) + 2.2f + 11f * (t - 10.2f)), 3f, 22f);
            Move("Sedan", new Color(0.7f, 0.71f, 0.73f), 0f, t => new Vector3(-6.4f, 0f, WalkerZ(10.2f) - 13f + 11f * (t - 10.2f)), 4f, 22f);
            // southbound van waiting at its red, then pulling away on the green
            Move("Van", new Color(0.9f, 0.9f, 0.88f), 180f, t =>
            {
                const float go = WalkAt + 0.4f, acc = 2.8f, top = 11f;
                float u = Mathf.Max(0f, t - go), tTop = top / acc;
                float d = u < tTop ? 0.5f * acc * u * u : 0.5f * acc * tTop * tTop + top * (u - tTop);
                return new Vector3(-10.2f, 0f, NorthCurb + 5.85f - d);
            }, -1f, 22f);
            // eastbound SUV slowing to a stop at its red (far left)
            Move("SUV", new Color(0.3f, 0.32f, 0.35f), 90f, t =>
            {
                const float stopAt = 9f, decel = 3f, stopX = EastCurbW - 5.1f;
                if (t >= stopAt) return new Vector3(stopX, 0f, 14.6f);
                float u = stopAt - t, v = decel * 4f;
                float d = u < 4f ? 0.5f * decel * u * u : 0.5f * decel * 16f + v * (u - 4f);
                return new Vector3(stopX - d, 0f, 14.6f);
            }, -1f, 22f);
        }

        void BuildTruck()
        {
            var go = Place("Truck", new Vector3(TruckX(TruckAt), 0f, TruckLane), -90f, _root);
            _truck = go.transform;
            _truckBody = Find(_truck, "Body") ?? _truck;
            foreach (var w in new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" })
            {
                var wt = Find(_truck, w);
                if (wt != null) _truckWheels.Add(wt);
            }
            var flareMat = Resources.Load<Material>("FX/FX_Soft");
            foreach (var hl in new[] { "HL_L", "HL_R" })
            {
                var anchor = Find(_truck, hl);
                if (anchor == null) continue;
                var lgo = new GameObject("Beam_" + hl);
                lgo.transform.SetParent(anchor, false);
                lgo.transform.localRotation = Quaternion.Euler(6f, 0f, 0f); // dipped a little toward the road
                var light = lgo.AddComponent<Light>();
                light.type = LightType.Spot;
                light.range = 48f;
                light.spotAngle = 78f;
                light.innerSpotAngle = 34f;
                light.color = new Color(1f, 0.95f, 0.84f);
                light.intensity = 0f;
                light.shadows = LightShadows.None;
                _headlights.Add(light);
                if (flareMat == null) continue;
                var flare = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(flare.GetComponent<Collider>());
                flare.name = "Flare_" + hl;
                flare.transform.SetParent(anchor, false);
                flare.transform.localPosition = new Vector3(0f, 0f, 0.05f);
                var fr = flare.GetComponent<Renderer>();
                fr.sharedMaterial = flareMat;
                fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _flares.Add(flare.transform);
                _flareRenderers.Add(fr);
            }
        }

        // ------------------------------------------------------------------------------------------------ pigeons
        void BuildPigeons()
        {
            var spots = new[] { new Vector3(-0.9f, Curb, 6.3f), new Vector3(-0.3f, Curb, 7.1f), new Vector3(0.65f, Curb, 6.6f), new Vector3(0.2f, Curb, 7.9f), new Vector3(-1.3f, Curb, 7.6f) };
            for (int i = 0; i < spots.Length; i++)
            {
                float yaw = 40f + i * 67f;
                var go = Place("Pigeon", spots[i], yaw, _root);
                var p = new Pigeon
                {
                    T = go.transform, WingL = Find(go.transform, "Wing_L"), WingR = Find(go.transform, "Wing_R"), Home = spots[i], Yaw = yaw,
                    TakeOff = 3.15f + i * 0.11f, Phase = i * 1.7f,
                    // scatter up and away from the walker, mostly over the avenue
                    Flight = new Vector3(-2.6f + i * 0.9f, 3.1f + 0.3f * i, 2.2f + 0.4f * i),
                };
                _pigeons.Add(p);
            }
        }

        void BuildParticles()
        {
            var smoke = Resources.Load<Material>("FX/FX_Smoke");
            _steam = Smoke("ManholeSteam", new Vector3(-6.4f, 0.05f, 14.2f), smoke, 9f, new Color(0.92f, 0.93f, 0.95f, 0.35f), 0.7f, 3.2f, 0.45f);
            _tyreSmoke = Smoke("TyreSmoke", new Vector3(0f, 0.15f, 0f), smoke, 0f, new Color(0.85f, 0.85f, 0.86f, 0.55f), 0.5f, 1.4f, 0.9f);
        }

        ParticleSystem Smoke(string name, Vector3 pos, Material mat, float rate, Color c, float size, float life, float speed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = (uint)name.Length * 7919u;
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = c;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var em = ps.emission;
            em.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.3f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.4f));
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            vel.y = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (rate > 0f) ps.Simulate(4f, true, true, false); // already steaming when the picture fades in
            return ps;
        }

        // ------------------------------------------------------------------------------------------------- phone
        void BuildPhone()
        {
            _phoneRig = new GameObject("PhoneRig").transform;
            _phoneRig.SetParent(_body, false);
            var prefab = _assets != null ? _assets.Get("PhoneInHand") : null;
            var phone = prefab != null ? Instantiate(prefab) : StandIn("PhoneInHand");
            phone.name = "PhoneInHand";
            phone.transform.SetParent(_phoneRig, false);
            var screen = Find(phone.transform, "ScreenCenter") ?? phone.transform;
            _cableStart = Find(phone.transform, "CableStart");

            // the lock screen (OpeningView builds it): a world-space canvas over the display, facing away from its own +Z
            var cgo = new GameObject("PhoneScreen", typeof(RectTransform));
            cgo.transform.SetParent(screen, false);
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera;
            var rt = (RectTransform)cgo.transform;
            rt.sizeDelta = new Vector2(414f, 854f);
            rt.localScale = Vector3.one * (0.0692f / 414f);
            rt.localPosition = new Vector3(0f, 0f, 0.0004f);
            rt.localRotation = Quaternion.Euler(0f, 180f, 0f);
            PhoneScreen = rt;

            var mat = _assets != null && _assets.cable != null ? _assets.cable : Mat(null, new Color(0.95f, 0.95f, 0.93f));
            _cable = new EarphoneCable(_root, mat, _layer);
            var ear = _assets != null ? _assets.Get("Earphones") : null;
            if (ear != null)
            {
                var parts = Instantiate(ear);
                parts.transform.SetParent(_root, false);
                _cable.Splitter = Find(parts.transform, "Splitter");
                _cable.Remote = Find(parts.transform, "Remote");
                var bud = Find(parts.transform, "Earbud");
                if (bud != null) bud.gameObject.SetActive(false); // in the ears, out of sight
            }
        }

        // ------------------------------------------------------------------------------------------------ render
        void RenderWorld(float t, float dt)
        {
            // signals: the cross street goes amber, all red, then the avenue gets green and we get WALK
            bool flashOn = Mathf.Repeat(t - FlashAt, 1f) < 0.5f;
            WalkSignal = t >= WalkAt && t < FlashAt;
            SetLamps(_pedWalk, WalkSignal);
            SetLamps(_pedHand, t < WalkAt || (t >= FlashAt && flashOn));
            foreach (var cd in _countdowns) cd.text = t >= FlashAt ? Mathf.CeilToInt(20f - t).ToString() : "";
            SetLamps(_nsRed, t < WalkAt);
            SetLamps(_nsAmber, false);
            SetLamps(_nsGreen, t >= WalkAt);
            SetLamps(_ewGreen, t < 5f);
            SetLamps(_ewAmber, t >= 5f && t < 6.5f);
            SetLamps(_ewRed, t >= 6.5f);

            foreach (var m in _movers)
            {
                bool on = t >= m.From && t <= m.To;
                if (m.T.gameObject.activeSelf != on) m.T.gameObject.SetActive(on);
                if (!on) continue;
                var p = m.Pos(t);
                m.T.localPosition = p;
                float spin = (p - m.Pos(m.From)).magnitude / m.Radius * Mathf.Rad2Deg;
                foreach (var w in m.Wheels) w.localRotation = Quaternion.Euler(spin, 0f, 0f);
            }

            // the truck: from the far end of the cross street, through its red, braking at the end
            TruckFront = TruckX(t);
            TruckVisible = _short || t >= TruckAt;
            if (_truck.gameObject.activeSelf != TruckVisible) _truck.gameObject.SetActive(TruckVisible);
            float tt = _short ? Mathf.Lerp(BrakeAt + 0.1f, OpeningView.CutAt, Mathf.Clamp01(t / OpeningView.ShortFlash)) : t;
            if (TruckVisible)
            {
                _truck.localPosition = new Vector3(TruckX(tt), 0f, TruckLane);
                float sinceBrake = tt - BrakeAt;
                float dive = sinceBrake > 0f ? 2.4f * (1f - Mathf.Exp(-sinceBrake / 0.12f)) * (1f + 0.15f * Mathf.Sin(sinceBrake * 18f) * Mathf.Exp(-sinceBrake / 0.2f)) : 0f;
                if (_truckBody != _truck) _truckBody.localRotation = Quaternion.Euler(dive, 0f, 0f);
                float spin = (TruckX(TruckAt) - TruckX(tt)) / 0.45f * Mathf.Rad2Deg;
                foreach (var w in _truckWheels) w.localRotation = Quaternion.Euler(spin, 0f, 0f);
            }
            float near = 1f - Mathf.Clamp01((TruckX(tt) - WalkX) / 46f);
            bool horn = !_short && ((tt >= OpeningView.HornAt + 0.45f && tt < OpeningView.HornAt + 0.85f) || tt >= OpeningView.HornAt + 1.25f);
            float beam = TruckVisible ? Mathf.Lerp(220f, 900f, near * near) * (horn ? 1.45f : 1f) : 0f;
            foreach (var l in _headlights) l.intensity = beam;
            Glow = _short ? Mathf.Clamp01(t / OpeningView.ShortFlash) : t < OpeningView.HornAt || t >= OpeningView.CutAt ? 0f : Mathf.Pow(Smooth(OpeningView.HornAt, OpeningView.CutAt, t), 1.4f);
            for (int i = 0; i < _flares.Count; i++)
            {
                var f = _flares[i];
                f.rotation = Quaternion.LookRotation(f.position - Camera.transform.position, Camera.transform.up);
                float size = Mathf.Lerp(0.6f, 2.6f, near * near) * (horn ? 1.25f : 1f);
                f.localScale = Vector3.one * size;
                _mpb.SetColor("_Color", new Color(1f, 0.93f, 0.8f, 1f) * Mathf.Lerp(0.6f, 2.4f, near));
                _flareRenderers[i].SetPropertyBlock(_mpb);
            }

            // tyre smoke under the front wheels once the brakes bite
            if (_tyreSmoke != null)
            {
                var em = _tyreSmoke.emission;
                em.rateOverTime = tt >= BrakeAt && TruckVisible ? 70f : 0f;
                _tyreSmoke.transform.localPosition = new Vector3(TruckX(tt) + 1.1f, 0.15f, TruckLane);
                if (dt > 0f) _tyreSmoke.Simulate(dt, true, false, false);
            }
            if (_steam != null && dt > 0f) _steam.Simulate(dt, true, false, false);

            foreach (var p in _pigeons) RenderPigeon(p, t);

            if (_cable != null && _phoneRig != null)
            {
                var start = _cableStart != null ? _cableStart : _phoneRig;
                var dir = _cableStart != null ? -_cableStart.up : -_phoneRig.up;
                _cable.Step(start.position, dir, _head, _body, dt);
                _cable.Object.SetActive(t < OpeningView.CutAt);
            }
        }

        void RenderPigeon(Pigeon p, float t)
        {
            float since = t - p.TakeOff;
            if (since < 0f)
            {
                // pecking about
                float peck = Mathf.Max(0f, Mathf.Sin((t + p.Phase) * 5.3f)) * 18f;
                p.T.localPosition = p.Home;
                p.T.localRotation = Quaternion.Euler(peck, p.Yaw + Mathf.Sin((t + p.Phase) * 0.9f) * 25f, 0f);
                Flap(p, 0f, t);
                return;
            }
            if (since > 3.5f)
            {
                if (p.T.gameObject.activeSelf) p.T.gameObject.SetActive(false);
                return;
            }
            var v = p.Flight;
            var pos = p.Home + new Vector3(v.x * since, v.y * since + 0.6f * since * since, v.z * since);
            p.T.localPosition = pos;
            var heading = new Vector3(v.x, 0f, v.z);
            p.T.localRotation = Quaternion.LookRotation(heading.normalized, Vector3.up) * Quaternion.Euler(-25f, 0f, 0f);
            Flap(p, 1f, t);
        }

        static void Flap(Pigeon p, float amount, float t)
        {
            float a = amount * Mathf.Sin(t * Mathf.PI * 2f * 11f) * 62f;
            if (p.WingL != null) p.WingL.localRotation = Quaternion.Euler(0f, 0f, -a);
            if (p.WingR != null) p.WingR.localRotation = Quaternion.Euler(0f, 0f, a);
        }

        // ------------------------------------------------------------------------------------------------ stand-ins
        /// <summary>Simple primitive stand-ins so the opening plays even without the kits (sizes match the briefs).</summary>
        GameObject StandIn(string id)
        {
            var root = new GameObject(id);
            Color c = new Color(0.6f, 0.6f, 0.62f);
            void Box(Vector3 size, Vector3 centre, Color col, string name = "Box")
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(b.GetComponent<Collider>());
                b.name = name;
                b.transform.SetParent(root.transform, false);
                b.transform.localPosition = centre;
                b.transform.localScale = size;
                var m = new Material(Shader.Find("HS/Toon")) { name = name == "Paint" ? "StandInPaint" : "StandIn" };
                m.SetColor("_BaseColor", col);
                b.GetComponent<Renderer>().sharedMaterial = m;
            }
            Transform Node(string name, Vector3 pos, Transform parent = null)
            {
                var n = new GameObject(name).transform;
                n.SetParent(parent != null ? parent : root.transform, false);
                n.localPosition = pos;
                return n;
            }
            switch (id)
            {
                case "CornerCafe": case "CornerMart": case "CornerPharmacy": case "CornerDiner":
                    Box(new Vector3(15f, 18f, 15f), new Vector3(-7.5f, 9f, -7.5f), new Color(0.6f, 0.34f, 0.28f));
                    break;
                case "Tower1": case "Tower2": case "Tower3": case "Tower4":
                    Box(new Vector3(30f, 90f, 30f), new Vector3(0f, 45f, -15f), new Color(0.55f, 0.6f, 0.66f));
                    break;
                case "Truck":
                {
                    var body = Node("Body", Vector3.zero);
                    var keep = root;
                    root = body.gameObject;
                    Box(new Vector3(2.0f, 1.9f, 1.9f), new Vector3(0f, 1.5f, -0.95f), new Color(0.94f, 0.94f, 0.92f));
                    Box(new Vector3(2.35f, 2.45f, 5.2f), new Vector3(0f, 2.23f, -4.6f), new Color(0.9f, 0.9f, 0.88f));
                    Box(new Vector3(2.1f, 0.3f, 0.2f), new Vector3(0f, 0.6f, -0.1f), new Color(0.75f, 0.76f, 0.78f));
                    root = keep;
                    foreach (var (n, p) in new[] { ("Wheel_FL", new Vector3(-0.85f, 0.45f, -1.2f)), ("Wheel_FR", new Vector3(0.85f, 0.45f, -1.2f)), ("Wheel_RL", new Vector3(-0.9f, 0.45f, -5.6f)), ("Wheel_RR", new Vector3(0.9f, 0.45f, -5.6f)) })
                        Node(n, p);
                    Node("HL_L", new Vector3(-0.75f, 0.95f, 0.02f), body);
                    Node("HL_R", new Vector3(0.75f, 0.95f, 0.02f), body);
                    break;
                }
                case "Sedan": case "Hatchback": case "Taxi": case "Van": case "SUV":
                    Box(new Vector3(1.8f, 0.8f, 4.5f), new Vector3(0f, 0.65f, -2.25f), id == "Taxi" ? new Color(0.95f, 0.72f, 0.02f) : c, "Paint");
                    Box(new Vector3(1.6f, 0.6f, 2.2f), new Vector3(0f, 1.35f, -2.5f), new Color(0.2f, 0.24f, 0.3f));
                    break;
                case "PhoneInHand":
                    Box(new Vector3(0.0736f, 0.1471f, 0.008f), new Vector3(0f, 0f, -0.0042f), new Color(0.05f, 0.05f, 0.07f));
                    Node("ScreenCenter", Vector3.zero);
                    Node("CableStart", new Vector3(-0.012f, -0.083f, -0.004f));
                    break;
                case "Pigeon":
                    Box(new Vector3(0.12f, 0.12f, 0.28f), new Vector3(0f, 0.12f, 0f), new Color(0.45f, 0.47f, 0.52f));
                    Node("Wing_L", new Vector3(-0.06f, 0.15f, 0f));
                    Node("Wing_R", new Vector3(0.06f, 0.15f, 0f));
                    break;
                case "StreetLamp": case "TrafficSignalMast": case "PedPole": case "SignPost":
                    Box(new Vector3(0.18f, id == "StreetLamp" ? 8.5f : id == "TrafficSignalMast" ? 6.8f : 3f, 0.18f), new Vector3(0f, id == "StreetLamp" ? 4.25f : id == "TrafficSignalMast" ? 3.4f : 1.5f, 0f), new Color(0.2f, 0.24f, 0.22f));
                    break;
                case "ManholeCover": case "StormDrain": case "TreeGrate":
                    break;
                default:
                    if (id.StartsWith("DeadTree") || id.StartsWith("CommonTree")) break;
                    if (id.Length > 0 && char.IsUpper(id[0]) && (id == "NoodleBar" || id == "Laundromat" || id == "Bakery" || id == "ForLease" || id == "Bank" || id == "Books" || id == "PhoneRepair" || id == "Florist"))
                    {
                        Box(new Vector3(12f, 15f, 16f), new Vector3(0f, 7.5f, -8f), new Color(0.62f, 0.5f, 0.42f));
                        break;
                    }
                    Box(new Vector3(0.5f, 1f, 0.5f), new Vector3(0f, 0.5f, 0f), c);
                    break;
            }
            return root;
        }
    }
}
