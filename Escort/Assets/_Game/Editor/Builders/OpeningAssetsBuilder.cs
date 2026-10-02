using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS.Opening;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS.EditorTools
{
    /// <summary>
    /// The opening street's assets: copies the Blender kits from build_art/opening/&lt;kit&gt; (fbx, textures, manifest.json;
    /// docs/superpowers/specs/2026-10-01-opening-street-design.md §5) into Art/Opening/&lt;Kit&gt;, turns each manifest's
    /// materials into HS/Toon materials (glass: transparent URP Lit), remaps the models' material slots onto them, and
    /// registers everything in Resources/OpeningAssets with the street-surface materials, the sky and the "Opening" layer.
    /// Re-runnable; only changed files are copied.
    /// </summary>
    public static class OpeningAssetsBuilder
    {
        public const string ArtDir = "Assets/_Game/Art/Opening";
        public const string RegistryPath = "Assets/_Game/Resources/OpeningAssets.asset";
        static readonly string[] Kits = { "vehicles", "street", "buildings" };
        static string Staging => Path.GetFullPath(Path.Combine(Application.dataPath, "../../build_art/opening"));
        static string Kit(string kit) => char.ToUpperInvariant(kit[0]) + kit.Substring(1);

        [MenuItem("Tools/HS/Build/Opening Assets")]
        public static void Build()
        {
            EnsureLayer("Opening", 8);
            var copied = CopyKits();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var reg = AssetDatabase.LoadAssetAtPath<OpeningAssets>(RegistryPath);
            if (reg == null)
            {
                reg = ScriptableObject.CreateInstance<OpeningAssets>();
                AssetDatabase.CreateAsset(reg, RegistryPath);
            }
            reg.prefabs.Clear();
            var report = new List<string>();
            foreach (var kit in Kits) BuildKit(kit, reg, report);
            BuildSurfaces(reg);
            foreach (var n in new[] { "DeadTree_1", "DeadTree_2", "DeadTree_3", "DeadTree_4", "DeadTree_5", "CommonTree_1", "CommonTree_2", "CommonTree_3", "CommonTree_4", "CommonTree_5" })
            {
                var tree = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Game/Art/Environment/Nature/{n}.fbx");
                if (tree != null) reg.prefabs.Add(new OpeningAssets.Entry { id = n, prefab = tree });
            }
            EditorUtility.SetDirty(reg);
            AssetDatabase.SaveAssets();
            string msg = $"opening assets: {reg.prefabs.Count} prefabs, {copied} files copied; " + string.Join("; ", report);
            Debug.Log("[OpeningAssets] " + msg);
            HS.Agent.AgentBridge.Write("builder.json", "{\"ok\":true,\"msg\":\"" + msg.Replace("\"", "'") + "\"}");
        }

        // ---------------------------------------------------------------------------------------------------- files
        static int CopyKits()
        {
            int copied = 0;
            foreach (var kit in Kits)
            {
                string src = Path.Combine(Staging, kit);
                if (!File.Exists(Path.Combine(src, "manifest.json"))) continue;
                string dst = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ArtDir, Kit(kit)));
                foreach (var (sub, pattern, to) in new[] { ("fbx", "*.fbx", ""), ("textures", "*.png", "Textures") })
                {
                    string from = Path.Combine(src, sub);
                    if (!Directory.Exists(from)) continue;
                    string toDir = Path.Combine(dst, to);
                    Directory.CreateDirectory(toDir);
                    foreach (var f in Directory.GetFiles(from, pattern))
                    {
                        string target = Path.Combine(toDir, Path.GetFileName(f));
                        if (File.Exists(target) && SameBytes(f, target)) continue;
                        File.Copy(f, target, true);
                        copied++;
                    }
                }
            }
            return copied;
        }

        static bool SameBytes(string a, string b)
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            return File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b));
        }

        // ------------------------------------------------------------------------------------------------ materials
        static void BuildKit(string kit, OpeningAssets reg, List<string> report)
        {
            string manifestPath = Path.Combine(Staging, kit, "manifest.json");
            if (!File.Exists(manifestPath)) return;
            var manifest = JObject.Parse(File.ReadAllText(manifestPath));
            string dir = $"{ArtDir}/{Kit(kit)}";
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Application.dataPath, "..", dir, "Materials")));
            var mats = new Dictionary<string, Material>();
            if (manifest["materials"] is JObject jm)
                foreach (var p in jm.Properties())
                    mats[p.Name] = MakeMaterial(dir, p.Name, (JObject)p.Value);
            AssetDatabase.SaveAssets();

            int models = 0;
            if (manifest["models"] is JObject models0)
                foreach (var p in models0.Properties())
                {
                    string file = (string)p.Value["file"] ?? $"fbx/{p.Name}.fbx";
                    string path = $"{dir}/{Path.GetFileName(file)}";
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null)
                    {
                        report.Add($"{p.Name}: missing {path}");
                        continue;
                    }
                    bool changed = false;
                    foreach (var embedded in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                    {
                        string name = embedded.name;
                        if (!mats.TryGetValue(name, out var mat)) mats.TryGetValue(name.Split('.')[0], out mat);
                        if (mat == null) continue;
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), mat);
                        changed = true;
                    }
                    if (changed) importer.SaveAndReimport();
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null)
                    {
                        reg.prefabs.Add(new OpeningAssets.Entry { id = p.Name, prefab = go });
                        models++;
                    }
                }
            report.Add($"{kit}: {models} models, {mats.Count} materials");
        }

        static Material MakeMaterial(string dir, string name, JObject j)
        {
            string path = $"{dir}/Materials/{name}.mat";
            string texName = (string)j["texture"];
            Texture2D tex = null;
            if (!string.IsNullOrEmpty(texName))
            {
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/Textures/{texName}");
                if (tex != null && (string)j["wrap"] == "clamp")
                {
                    var ti = (TextureImporter)AssetImporter.GetAtPath($"{dir}/Textures/{texName}");
                    if (ti.wrapMode != TextureWrapMode.Clamp)
                    {
                        ti.wrapMode = TextureWrapMode.Clamp;
                        ti.SaveAndReimport();
                    }
                }
            }
            ColorUtility.TryParseHtmlString((string)j["color"] ?? "#FFFFFF", out var color);
            string note = ((string)j["note"] ?? "").ToLowerInvariant();
            // the manifests say so explicitly ("transparent": true); older entries fall back to the note
            bool glass = j["transparent"] != null ? (bool)j["transparent"]
                : name.Contains("Glass") && (note.Contains("transparen") || note.Contains("opacity"));
            var shader = glass ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("HS/Toon");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetTexture("_BaseMap", tex != null ? tex : Texture2D.whiteTexture);
            m.SetColor("_BaseColor", color);
            m.enableInstancing = true;
            if (glass)
            {
                // see-through: cab interiors and drivers show behind the windshield; the sky reflects in it
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_Smoothness", 0.92f);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Cull", 2f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                var em = j["emission"] as JObject;
                if (em != null)
                {
                    ColorUtility.TryParseHtmlString((string)em["color"] ?? "#FFFFFF", out var ec);
                    float intensity = (float?)em["intensity"] ?? 1f;
                    m.SetColor("_EmissionColor", ec * intensity);
                    m.SetFloat("_EmissionTexMul", tex != null ? 1f : 0f);
                }
                else
                {
                    m.SetColor("_EmissionColor", Color.black);
                    m.SetFloat("_EmissionTexMul", 0f);
                }
                bool cutout = (bool?)j["cutout"] ?? false;
                MaterialLibrary.Cutout(m, cutout);
                if ((float?)j["outline"] is float ow) m.SetFloat("_OutlineWidth", ow);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Road, sidewalk, curb, tactile paving, paint, gutter; the earphone cable; the morning sky.</summary>
        static void BuildSurfaces(OpeningAssets reg)
        {
            string dir = $"{ArtDir}/Materials";
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Application.dataPath, "..", dir)));
            Texture2D T(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtDir}/Textures/T_Street_{n}.png");
            Material Toon(string n, Texture2D tex, Color c, float outline, System.Action<Material> extra = null)
            {
                string path = $"{dir}/{n}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(Shader.Find("HS/Toon")) { name = n };
                    AssetDatabase.CreateAsset(m, path);
                }
                m.shader = Shader.Find("HS/Toon");
                m.SetTexture("_BaseMap", tex != null ? tex : Texture2D.whiteTexture);
                m.SetColor("_BaseColor", c);
                m.SetFloat("_OutlineWidth", outline);
                m.SetFloat("_RimStrength", 0.08f);
                m.enableInstancing = true;
                extra?.Invoke(m);
                EditorUtility.SetDirty(m);
                return m;
            }
            reg.road = Toon("M_Op_Road", T("Asphalt"), Color.white, 0f, m => m.SetFloat("_ShadeSoftness", 0.25f));
            reg.sidewalk = Toon("M_Op_Sidewalk", T("Sidewalk"), Color.white, 0f, m => m.SetFloat("_ShadeSoftness", 0.25f));
            reg.curb = Toon("M_Op_Curb", T("Curb"), Color.white, 0f);
            reg.tactile = Toon("M_Op_Tactile", T("Tactile"), Color.white, 0f);
            reg.paintWhite = Toon("M_Op_PaintWhite", T("Paint"), Color.white, 0f);
            reg.paintYellow = Toon("M_Op_PaintYellow", T("Paint"), new Color(1f, 0.78f, 0.25f), 0f);
            reg.gutter = Toon("M_Op_Gutter", T("Curb"), new Color(0.72f, 0.71f, 0.69f), 0f);
            reg.cable = Toon("M_Op_Cable", null, new Color(0.96f, 0.96f, 0.94f), 1.1f, m =>
            {
                m.SetFloat("_RimStrength", 0.45f);
                m.SetFloat("_AmbientStrength", 0.8f);
            });
            reg.placeholder = Toon("M_Op_Placeholder", null, new Color(0.6f, 0.6f, 0.62f), 1f);
            string skyPath = $"{dir}/M_Op_Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural")) { name = "M_Op_Sky" };
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            sky.SetFloat("_SunDisk", 2f);
            sky.SetFloat("_SunSize", 0.03f);
            sky.SetFloat("_SunSizeConvergence", 7f);
            sky.SetFloat("_AtmosphereThickness", 0.75f);
            sky.SetColor("_SkyTint", new Color(0.58f, 0.66f, 0.78f));
            sky.SetColor("_GroundColor", new Color(0.46f, 0.47f, 0.5f));
            sky.SetFloat("_Exposure", 1.1f);
            EditorUtility.SetDirty(sky);
            reg.sky = sky;
            reg.smoke = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Resources/FX/FX_Smoke.mat");
            reg.glow = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Resources/FX/FX_Soft.mat");
        }

        /// <summary>A named layer for the street, so its camera sees nothing of the chapter (and vice versa).</summary>
        static void EnsureLayer(string name, int preferred)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == name) return;
            int slot = string.IsNullOrEmpty(layers.GetArrayElementAtIndex(preferred).stringValue) ? preferred : -1;
            for (int i = 8; slot < 0 && i < layers.arraySize; i++)
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) slot = i;
            if (slot < 0) return;
            layers.GetArrayElementAtIndex(slot).stringValue = name;
            tagManager.ApplyModifiedProperties();
        }
    }
}
