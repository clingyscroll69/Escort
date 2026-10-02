using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HS.EditorTools
{
    /// <summary>
    /// Palette toon materials for everything (GDD §2: toon shader + shared palette unify mixed sources).
    /// Kit FBX materials are remapped by source name onto shared HS/Toon materials so every model in a kit batches.
    /// </summary>
    public static class MaterialLibrary
    {
        public const string Dir = "Assets/_Game/Art/Materials";
        public static readonly string[] KitDirs =
        {
            "Assets/_Game/Art/Props", "Assets/_Game/Art/Props/Kit", "Assets/_Game/Art/Environment/Nature", "Assets/_Game/Art/Environment/Village",
        };

        public static Shader Toon => Shader.Find("HS/Toon");

        public static Material GetOrCreate(string name, Texture tex, Color color, System.Action<Material> configure = null)
        {
            Directory.CreateDirectory(Dir);
            string path = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Toon) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Toon;
            m.SetTexture("_BaseMap", tex != null ? tex : Texture2D.whiteTexture);
            m.SetColor("_BaseColor", color);
            m.enableInstancing = true;
            configure?.Invoke(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static void Cutout(Material m, bool on)
        {
            m.SetFloat("_AlphaClip", on ? 1f : 0f);
            if (on) m.EnableKeyword("_ALPHATEST_ON");
            else m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", on ? 0f : 2f);
            m.SetFloat("_Cutoff", 0.5f);
            m.renderQueue = on ? 2450 : -1;
        }

        static Texture2D FindTexture(string fileNameNoExt)
        {
            foreach (var guid in AssetDatabase.FindAssets(fileNameNoExt + " t:Texture2D", new[] { "Assets/_Game/Art" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == fileNameNoExt) return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            return null;
        }

        /// <summary>Source-material name → texture file for kits whose FBX texture references don't resolve.</summary>
        static readonly Dictionary<string, string> KnownTextures = new Dictionary<string, string>
        {
            { "MI_Trim_Props", "T_Trim_Props_BaseColor" }, { "MI_Trim_Props_Vertex", "T_Trim_Props_BaseColor" },
            { "MI_Trim_Furniture", "T_Trim_Furniture_BaseColor" }, { "MI_Trim_Metal", "T_Trim_Metal_BaseColor" },
            { "MI_Trim_Metal_Vertex", "T_Trim_Metal_BaseColor" }, { "MI_Trim_Cloth", "T_Trim_Cloth_BaseColor" },
            { "MI_Vine", "T_VineLeaf" }, { "MI_Banner", "T_Trim_Cloth_BaseColor" }, { "Leaves_Pine", "Leaf_Pine_C" },
            { "MI_WoodTrim_Wear", "T_WoodTrim_BaseColor" },
        };

        /// <summary>Art-direction overrides: source material → texture regardless of what the FBX references.</summary>
        static readonly Dictionary<string, string> TextureOverrides = new Dictionary<string, string>
        {
            { "Leaves_TwistedTree", "Leaves_Bush_C" }, // kit bushes use the autumn-red atlas; Old Road is late summer
        };

        static readonly Dictionary<string, Color> CustomPropColors = new Dictionary<string, Color>
        {
            { "M_Wood", new Color(0.45f, 0.29f, 0.17f) }, { "M_Iron", new Color(0.5f, 0.53f, 0.57f) },
            { "M_String", new Color(0.86f, 0.82f, 0.7f) }, { "M_Stone", new Color(0.5f, 0.54f, 0.58f) },
            { "M_Gem", new Color(0.3f, 0.8f, 0.9f) }, { "M_Cloth", new Color(0.93f, 0.9f, 0.84f) }, { "M_Leather", new Color(0.32f, 0.21f, 0.13f) },
            { "MI_MetalOrnaments", new Color(0.24f, 0.24f, 0.27f) }, { "MI_Page_Empty", new Color(0.9f, 0.86f, 0.74f) },
            { "MI_WindowGlass", new Color(0.2f, 0.26f, 0.33f) },
        };

        static IEnumerable<string> ModelsIn(string dir) =>
            AssetDatabase.FindAssets("t:Model", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetDirectoryName(p).Replace('\\', '/') == dir);

        [MenuItem("Tools/HS/QA/Dump Kit Materials")]
        public static void Dump()
        {
            var sb = new StringBuilder("{");
            bool first = true;
            var names = new SortedDictionary<string, string>();
            foreach (var dir in KitDirs)
            foreach (var path in ModelsIn(dir))
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
                names[m.name] = tex != null ? tex.name : "";
            }
            foreach (var kv in names)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(kv.Key).Append("\":\"").Append(kv.Value).Append('"');
            }
            sb.Append('}');
            HS.Agent.AgentBridge.Write("kit_materials.json", sb.ToString());
        }

        struct SourceMat
        {
            public Texture Tex;
            public Color Color;
        }

        /// <summary>Clears existing remaps so embedded source materials (with their tints) can be read again.</summary>
        [MenuItem("Tools/HS/Build/Kit Materials (Full)")]
        public static void RemapKitsFull()
        {
            foreach (var dir in KitDirs)
            foreach (var path in ModelsIn(dir))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                bool any = false;
                foreach (var kv in importer.GetExternalObjectMap())
                {
                    if (kv.Key.type != typeof(Material)) continue;
                    importer.RemoveRemap(kv.Key);
                    any = true;
                }
                if (any) importer.SaveAndReimport();
            }
            RemapKits();
        }

        [MenuItem("Tools/HS/Build/Kit Materials")]
        public static void RemapKits()
        {
            // Pass 1: read source materials (texture + tint), create/refresh every toon material and persist them.
            // (Creating assets inside StartAssetEditing leaves them without a GUID → AddRemap stores {instanceID: 0}.)
            var missing = new HashSet<string>();
            var plan = new List<(ModelImporter importer, List<(string src, Material dst)> maps)>();
            foreach (var dir in KitDirs)
            foreach (var path in ModelsIn(dir))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                var embedded = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().ToList();
                var names = new HashSet<string>(embedded.Select(m => m.name));
                foreach (var kv in importer.GetExternalObjectMap())
                    if (kv.Key.type == typeof(Material)) names.Add(kv.Key.name);
                var maps = new List<(string, Material)>();
                foreach (var n in names)
                {
                    var src = embedded.FirstOrDefault(m => m.name == n);
                    if (src != null) RememberSource(n, src);
                    var toon = KitMaterial(n, src, missing);
                    if (toon != null) maps.Add((n, toon));
                }
                plan.Add((importer, maps));
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Pass 2: remap by source name onto the persisted materials.
            int remapped = 0;
            foreach (var (importer, maps) in plan)
            {
                foreach (var (src, dst) in maps)
                {
                    var persisted = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GetAssetPath(dst));
                    importer.RemoveRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), src));
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), src), persisted);
                    remapped++;
                }
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            HS.Agent.AgentBridge.Write("builder.json", $"{{\"ok\":true,\"msg\":\"kit remap {remapped}\",\"missing\":\"{string.Join(",", missing)}\"}}");
        }

        /// <summary>Source tints survive in EditorPrefs-free form: stored on the toon material itself (_BaseColor)
        /// the first time a source is seen, so later incremental remaps keep them.</summary>
        static readonly Dictionary<string, SourceMat> Sources = new Dictionary<string, SourceMat>();

        static void RememberSource(string name, Material src)
        {
            var tex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.mainTexture;
            var col = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
            Sources[name] = new SourceMat { Tex = tex, Color = col };
        }

        static Material KitMaterial(string n, Material src, HashSet<string> missing)
        {
            if (CustomPropColors.TryGetValue(n, out var col))
            {
                return GetOrCreate("Prop_" + n, null, col, m =>
                {
                    m.SetFloat("_OutlineWidth", 1.2f);
                    if (n == "M_Gem") m.SetColor("_EmissionColor", new Color(0.12f, 0.45f, 0.55f) * 1.5f);
                    else m.SetColor("_EmissionColor", Color.black);
                });
            }
            // Texture resolution, in order: source material → remembered source → known table → name guess →
            // art-direction override → pre-coloured "_C" twin (kit foliage ships white masks for a tint shader).
            bool haveSource = Sources.TryGetValue(n, out var remembered);
            Texture tex = src == null ? null : src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.mainTexture;
            if (tex == null && haveSource) tex = remembered.Tex;
            if (tex == null && KnownTextures.TryGetValue(n, out var texName)) tex = FindTexture(texName);
            if (tex == null) tex = GuessTexture(n);
            if (TextureOverrides.TryGetValue(n, out var overrideTex)) tex = FindTexture(overrideTex) ?? tex;
            if (tex != null && !tex.name.EndsWith("_C"))
            {
                var coloured = FindTexture(tex.name + "_C");
                if (coloured != null) tex = coloured;
            }
            var existing = AssetDatabase.LoadAssetAtPath<Material>($"{Dir}/Kit_{n}.mat");
            Color tint = haveSource ? remembered.Color : existing != null ? existing.GetColor("_BaseColor") : Color.white;
            tint.a = 1f;
            if (tex == null)
            {
                missing.Add(n);
                return GetOrCreate("Kit_" + n, null, src != null && src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : Color.white);
            }
            bool cutout = tex.name.Contains("Leaf") || tex.name.Contains("Leaves") || tex.name.Contains("Flowers") || tex.name.Contains("Vine") || tex.name == "Grass";
            bool big = n.Contains("Brick") || n.Contains("Plaster") || n.Contains("Floor") || n.Contains("Rock") || n.Contains("Path") || n.Contains("Tiles");
            return GetOrCreate("Kit_" + n, tex, tint, m =>
            {
                Cutout(m, cutout);
                if (m.GetFloat("_OutlineWidth") <= 0.001f && cutout) m.SetShaderPassEnabled("SRPDefaultUnlit", false);
                m.SetFloat("_VertexColorMul", n.EndsWith("_Vertex") ? 1f : 0f);
                m.SetFloat("_OutlineWidth", cutout ? 0f : big ? 0.8f : 1.1f);
                m.SetFloat("_RimStrength", cutout ? 0.1f : 0.18f);
            });
        }

        static Texture GuessTexture(string matName)
        {
            // e.g. "MI_UnevenBrick" → T_UnevenBrick_BaseColor, "Bark_NormalTree" → Bark_NormalTree
            string core = matName.Replace("MI_", "").Replace("M_", "").Replace("_Vertex", "");
            return FindTexture(core) ?? FindTexture("T_" + core + "_BaseColor") ?? FindTexture(core + "_Diffuse");
        }
    }
}
