using System.Collections.Generic;
using System.IO;
using HS.Core;
using HS.Hero;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS.EditorTools
{
    /// <summary>
    /// Level-authoring helpers for code-built room prefabs. Kit models are always wrapped: the wrapper carries our
    /// transform and colliders; the FBX root keeps its native unit scale/axis rotation (kits import with ×100 roots).
    /// </summary>
    public static class RoomKit
    {
        static Dictionary<string, string> _index;

        static readonly string[] Dirs =
        {
            "Assets/_Game/Art/Environment/Nature", "Assets/_Game/Art/Environment/Village", "Assets/_Game/Art/Props/Kit", "Assets/_Game/Art/Props",
        };

        public static GameObject Model(string name)
        {
            if (_index == null)
            {
                _index = new Dictionary<string, string>();
                foreach (var d in Dirs)
                foreach (var f in Directory.GetFiles(d, "*.fbx"))
                    _index[Path.GetFileNameWithoutExtension(f)] = f.Replace('\\', '/');
            }
            if (!_index.TryGetValue(name, out var p))
            {
                Debug.LogError("[RoomKit] unknown model " + name);
                return null;
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(p);
        }

        public static void ResetIndex() => _index = null;

        public enum Col { None, Box, Trunk, Round }

        public static GameObject Place(Transform parent, string model, Vector3 pos, float rotY = 0f, float scale = 1f, Col col = Col.None,
            bool castShadows = true, bool isStatic = true)
        {
            var m = Model(model);
            var wrap = new GameObject(model);
            wrap.transform.SetParent(parent, false);
            wrap.transform.localPosition = pos;
            wrap.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
            wrap.transform.localScale = Vector3.one * scale;
            if (m == null) return wrap;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(m, wrap.transform);
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            AddCollider(wrap, col);
            if (isStatic) SetStatic(wrap);
            return wrap;
        }

        public static Bounds LocalBounds(GameObject wrap)
        {
            var rs = wrap.GetComponentsInChildren<Renderer>();
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool init = false;
            var toLocal = wrap.transform.worldToLocalMatrix;
            foreach (var r in rs)
            {
                var wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? wb.min.x : wb.max.x, (i & 2) == 0 ? wb.min.y : wb.max.y, (i & 4) == 0 ? wb.min.z : wb.max.z);
                    var p = toLocal.MultiplyPoint3x4(c);
                    if (!init) { b = new Bounds(p, Vector3.zero); init = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        static void AddCollider(GameObject wrap, Col col)
        {
            if (col == Col.None) return;
            var b = LocalBounds(wrap);
            switch (col)
            {
                case Col.Box:
                {
                    var bc = wrap.AddComponent<BoxCollider>();
                    bc.center = b.center;
                    bc.size = b.size;
                    break;
                }
                case Col.Trunk:
                {
                    var cc = wrap.AddComponent<CapsuleCollider>();
                    cc.radius = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.1f, 0.3f, 0.6f);
                    cc.height = 4f;
                    cc.center = new Vector3(0f, 2f, 0f);
                    break;
                }
                case Col.Round:
                {
                    var cc = wrap.AddComponent<CapsuleCollider>();
                    cc.radius = Mathf.Max(b.size.x, b.size.z) * 0.42f;
                    cc.height = Mathf.Max(b.size.y, cc.radius * 2f);
                    cc.center = new Vector3(b.center.x, cc.height * 0.5f, b.center.z);
                    break;
                }
            }
        }

        public static void SetStatic(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
        }

        public static GameObject Empty(Transform parent, string name, Vector3 pos, float rotY = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
            return go;
        }

        public static T Marker<T>(Transform parent, string name, Vector3 pos, float rotY = 0f) where T : Component
            => Empty(parent, name, pos, rotY).AddComponent<T>();

        // ------------------------------------------------------------------------------------------ materials

        public static Material GroundMat(string name, string tex, Color tint)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/_Game/Art/Environment/Textures/{tex}.png");
            return MaterialLibrary.GetOrCreate(name, t, tint, m =>
            {
                m.SetFloat("_OutlineWidth", 0f);
                m.SetFloat("_RimStrength", 0f);
                m.SetShaderPassEnabled("SRPDefaultUnlit", false);
            });
        }

        // ------------------------------------------------------------------------------------------ meshes

        /// <summary>Flat ground slab (world-space UVs, 6 m tiles) with a thick box collider under it.</summary>
        public static GameObject Ground(Transform parent, string name, Vector3 center, float width, float length, Material mat, float uvTile = 6f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            var mesh = new Mesh { name = name };
            int nx = Mathf.Max(1, Mathf.CeilToInt(width / 4f)), nz = Mathf.Max(1, Mathf.CeilToInt(length / 4f));
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int z = 0; z <= nz; z++)
            for (int x = 0; x <= nx; x++)
            {
                var p = new Vector3(-width / 2f + width * x / nx, 0f, -length / 2f + length * z / nz);
                verts.Add(p);
                var w = center + p;
                uvs.Add(new Vector2(w.x / uvTile, w.z / uvTile));
            }
            for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                int i = z * (nx + 1) + x;
                tris.AddRange(new[] { i, i + nx + 1, i + 1, i + 1, i + nx + 1, i + nx + 2 });
            }
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            SaveMesh(mesh, parent.root.name + "_" + name); // unique per room (a shared path made every room reuse one mesh)
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, -0.5f, 0f);
            bc.size = new Vector3(width, 1f, length);
            SetStatic(go);
            return go;
        }

        /// <summary>Road strip along a polyline (local positions), slightly above the ground.</summary>
        public static GameObject Road(Transform parent, string name, IList<Vector3> pts, float width, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float v = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                var dir = i < pts.Count - 1 ? (pts[i + 1] - pts[i]) : (pts[i] - pts[i - 1]);
                if (i > 0 && i < pts.Count - 1) dir = (pts[i + 1] - pts[i - 1]);
                dir.y = 0f;
                dir.Normalize();
                var right = Vector3.Cross(Vector3.up, dir);
                if (i > 0) v += Vector3.Distance(pts[i], pts[i - 1]) / 4f;
                // slightly wobbly edges read as a trodden dirt road rather than a strip of tape
                float wob = 1f + 0.12f * Mathf.Sin(i * 1.7f);
                verts.Add(pts[i] - right * width * 0.5f * wob + Vector3.up * 0.02f);
                verts.Add(pts[i] + right * width * 0.5f * (2f - wob) + Vector3.up * 0.02f);
                uvs.Add(new Vector2(0f, v));
                uvs.Add(new Vector2(width / 4f, v));
                if (i > 0)
                {
                    int a = (i - 1) * 2;
                    tris.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            SaveMesh(mesh, parent.root.name + "_" + name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            SetStatic(go);
            return go;
        }

        static void SaveMesh(Mesh mesh, string name)
        {
            const string dir = "Assets/_Game/Art/Environment/Generated";
            Directory.CreateDirectory(dir);
            var path = $"{dir}/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
        }

        /// <summary>Invisible containment wall (players must not leave the diorama).</summary>
        public static void Blocker(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = Empty(parent, name, center);
            var bc = go.AddComponent<BoxCollider>();
            bc.size = size;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        // ------------------------------------------------------------------------------------------ dressing

        static readonly string[] BorderTrees = { "CommonTree_1", "CommonTree_2", "CommonTree_3", "CommonTree_4", "CommonTree_5", "Pine_1", "Pine_2", "Pine_3", "Pine_4" };
        static readonly string[] Rocks = { "Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3" };
        static readonly string[] Tufts = { "Grass_Common_Short", "Grass_Common_Tall", "Grass_Wispy_Short", "Grass_Wispy_Tall", "Clover_1", "Clover_2" };
        static readonly string[] Flowers = { "Flower_3_Group", "Flower_4_Group", "Flower_3_Single", "Flower_4_Single" };
        static readonly string[] Pebbles = { "Pebble_Round_1", "Pebble_Round_2", "Pebble_Round_3", "Pebble_Square_1", "Pebble_Square_4", "Pebble_Square_5" };

        /// <summary>Forest walls on both sides with invisible blockers; gaps only where the road enters/exits.</summary>
        public static void ForestBorders(Transform parent, float length, float innerX, DetRandom rng, float density = 1f)
        {
            var b = Empty(parent, "Borders", Vector3.zero).transform;
            foreach (int side in new[] { -1, 1 })
            {
                float z = 1.5f;
                while (z < length - 1f)
                {
                    float x = side * (innerX + 1.8f + (float)rng.NextDouble() * 1.6f);
                    var tree = BorderTrees[rng.Range(0, BorderTrees.Length)];
                    Place(b, tree, new Vector3(x, 0f, z), rng.Range(0, 360), 0.85f + (float)rng.NextDouble() * 0.3f, Col.Trunk);
                    if (rng.Chance(0.55))
                        Place(b, Rocks[rng.Range(0, Rocks.Length)], new Vector3(side * (innerX + 0.2f + (float)rng.NextDouble()), 0f, z + 1.6f), rng.Range(0, 360), 0.6f + (float)rng.NextDouble() * 0.4f, Col.Box);
                    if (rng.Chance(0.7))
                        Place(b, rng.Chance(0.3) ? "Bush_Common_Flowers" : "Bush_Common", new Vector3(side * (innerX - 0.4f + (float)rng.NextDouble() * 0.8f), 0f, z + 0.6f), rng.Range(0, 360), 0.8f + (float)rng.NextDouble() * 0.4f, Col.None, false);
                    // second row, further out, fills gaps seen from the camera
                    Place(b, BorderTrees[rng.Range(0, BorderTrees.Length)], new Vector3(side * (innerX + 5.5f + (float)rng.NextDouble() * 2f), 0f, z + 1.7f), rng.Range(0, 360), 0.9f + (float)rng.NextDouble() * 0.35f, Col.None);
                    z += 3.3f / density + (float)rng.NextDouble() * 1.2f;
                }
                Blocker(b, "Blocker_" + (side < 0 ? "L" : "R"), new Vector3(side * (innerX + 1.2f), 2f, length * 0.5f), new Vector3(1f, 4f, length + 0.5f));
            }
        }

        /// <summary>Grass tufts, flowers and pebbles in a rectangle, keeping clear of the road polyline.</summary>
        public static void Scatter(Transform parent, Rect area, int count, IList<Vector3> road, float roadClear, DetRandom rng, bool flowers = true)
        {
            var s = Empty(parent, "Scatter", Vector3.zero).transform;
            int placed = 0, guard = 0;
            while (placed < count && guard++ < count * 10)
            {
                var p = new Vector3(area.xMin + (float)rng.NextDouble() * area.width, 0f, area.yMin + (float)rng.NextDouble() * area.height);
                if (road != null && DistanceToPolyline(p, road) < roadClear) continue;
                double r = rng.NextDouble();
                string model = r < 0.62 ? Tufts[rng.Range(0, Tufts.Length)] : r < 0.8 && flowers ? Flowers[rng.Range(0, Flowers.Length)] : Pebbles[rng.Range(0, Pebbles.Length)];
                Place(s, model, p, rng.Range(0, 360), 0.7f + (float)rng.NextDouble() * 0.5f, Col.None, false);
                placed++;
            }
        }

        /// <summary>Pebbles and tufts along the road edges soften the strip.</summary>
        public static void RoadEdges(Transform parent, IList<Vector3> road, float halfWidth, DetRandom rng)
        {
            var s = Empty(parent, "RoadEdges", Vector3.zero).transform;
            for (int i = 0; i < road.Count - 1; i++)
            {
                var a = road[i];
                var bpt = road[i + 1];
                float len = Vector3.Distance(a, bpt);
                var dir = (bpt - a).normalized;
                var right = Vector3.Cross(Vector3.up, dir);
                for (float t = 0f; t < len; t += 1.4f)
                {
                    foreach (int side in new[] { -1, 1 })
                    {
                        if (!rng.Chance(0.55)) continue;
                        var p = a + dir * (t + (float)rng.NextDouble()) + right * side * (halfWidth + (float)rng.NextDouble() * 0.5f);
                        string model = rng.Chance(0.45) ? Pebbles[rng.Range(0, Pebbles.Length)] : Tufts[rng.Range(0, Tufts.Length)];
                        Place(s, model, p, rng.Range(0, 360), 0.6f + (float)rng.NextDouble() * 0.5f, Col.None, false);
                    }
                }
            }
        }

        public static float DistanceToPolyline(Vector3 p, IList<Vector3> pts)
        {
            float best = float.MaxValue;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var a = Geo.Flat(pts[i]);
                var b = Geo.Flat(pts[i + 1]);
                var ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(Geo.Flat(p) - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                best = Mathf.Min(best, Vector3.Distance(Geo.Flat(p), a + ab * t));
            }
            return best;
        }

        /// <summary>Route graph child with markers from (position, flags) tuples.</summary>
        public static RouteGraph Route(Transform parent, params (Vector3 p, bool threshold, bool choke, string label)[] nodes)
        {
            var g = Empty(parent, "Route", Vector3.zero).AddComponent<RouteGraph>();
            for (int i = 0; i < nodes.Length; i++)
            {
                var n = Empty(g.transform, "Node" + i, nodes[i].p);
                var m = n.AddComponent<RouteMarker>();
                m.Threshold = nodes[i].threshold;
                m.Chokepoint = nodes[i].choke;
                m.Label = nodes[i].label;
            }
            return g;
        }
    }
}
