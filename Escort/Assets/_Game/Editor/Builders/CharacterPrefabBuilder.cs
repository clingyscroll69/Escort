using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS.Presentation;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace HS.EditorTools
{
    /// <summary>
    /// Builds "&lt;Name&gt;_Visual" prefabs from the Blender-exported characters (tools/blender/recipes.json is the single
    /// source of truth): palette toon materials remapped onto the FBX, shared Humanoid animator, AnimDriver, and props
    /// placed at bone sockets. Grip transforms are derived from the bind pose (blade forward, grip in the palm) so
    /// they don't depend on bone-axis conventions.
    /// </summary>
    public static class CharacterPrefabBuilder
    {
        public const string PrefabDir = "Assets/_Game/Prefabs/Characters";
        const string CharDir = "Assets/_Game/Art/Characters";
        const string TexDir = "Assets/_Game/Art/Characters/Textures";
        public static string RecipesPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../tools/blender/recipes.json"));

        enum Hold { Blade, Pistol, Shield, Chest }

        struct PropSpec
        {
            public string Model;
            public Hold Hold;
            public float Scale;
            public bool Flip;          // pommel is at the max end of the long axis
            public float Grip;         // metres from the pommel end to the fist centre
            public string Material;    // optional material override for every slot
        }

        static readonly Dictionary<string, PropSpec> Props = new Dictionary<string, PropSpec>
        {
            { "Sword", new PropSpec { Model = "Assets/_Game/Art/Props/Kit/Sword_Bronze.fbx", Hold = Hold.Blade, Scale = 0.95f, Grip = 0.12f, Material = "Prop_SteelBlade" } },
            { "DarkSword", new PropSpec { Model = "Assets/_Game/Art/Props/Kit/Sword_Bronze.fbx", Hold = Hold.Blade, Scale = 1.0f, Grip = 0.12f, Material = "Prop_DarkBlade" } },
            { "Shield", new PropSpec { Model = "Assets/_Game/Art/Props/Kit/Shield_Wooden.fbx", Hold = Hold.Shield, Scale = 0.85f } },
            { "Knife", new PropSpec { Model = "Assets/_Game/Art/Props/Kit/Table_Knife.fbx", Hold = Hold.Blade, Scale = 1.35f, Grip = 0.07f } },
            { "Club", new PropSpec { Model = "Assets/_Game/Art/Props/Club.fbx", Hold = Hold.Blade, Scale = 1f, Grip = 0.09f } },
            { "Axe", new PropSpec { Model = "Assets/_Game/Art/Props/Kit/Axe_Bronze.fbx", Hold = Hold.Blade, Scale = 1f, Grip = 0.12f } },
            { "Dagger", new PropSpec { Model = "Assets/_Game/Art/Props/Dagger.fbx", Hold = Hold.Blade, Scale = 1f, Grip = 0.05f } },
            { "Crossbow", new PropSpec { Model = "Assets/_Game/Art/Props/Crossbow.fbx", Hold = Hold.Pistol, Scale = 1f } },
            { "Pendant", new PropSpec { Model = "Assets/_Game/Art/Props/Pendant.fbx", Hold = Hold.Chest, Scale = 1.3f, Material = "Prop_DullPendant" } },
        };

        static readonly Dictionary<string, Color> Undershirt = new Dictionary<string, Color>
        {
            { "Callum", new Color(0.10f, 0.13f, 0.26f) }, { "Ashgrave", new Color(0.07f, 0.05f, 0.09f) },
            { "Archer", new Color(0.08f, 0.07f, 0.1f) }, { "Crossbowman", new Color(0.16f, 0.07f, 0.07f) },
            { "Ambusher", new Color(0.12f, 0.13f, 0.08f) },
        };

        [MenuItem("Tools/HS/Build/Character Prefabs")]
        public static void BuildAll()
        {
            var recipes = JObject.Parse(File.ReadAllText(RecipesPath));
            EnsurePropMaterials();
            Directory.CreateDirectory(PrefabDir);
            var built = new List<string>();
            foreach (var kv in recipes)
            {
                if (kv.Key.StartsWith("_")) continue;
                if (Build(kv.Key, (JObject)kv.Value)) built.Add(kv.Key);
            }
            AssetDatabase.SaveAssets();
            HS.Agent.AgentBridge.Write("builder.json", $"{{\"ok\":true,\"msg\":\"characters: {string.Join(",", built)}\"}}");
        }

        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}.png");

        static void EnsurePropMaterials()
        {
            var trim = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Game/Art/Props/Textures/T_Trim_Props_BaseColor.png");
            MaterialLibrary.GetOrCreate("Prop_SteelBlade", trim, new Color(0.92f, 0.95f, 1f), m => { m.SetFloat("_VertexColorMul", 0f); m.SetFloat("_OutlineWidth", 1.2f); m.SetFloat("_RimStrength", 0.45f); });
            MaterialLibrary.GetOrCreate("Prop_DarkBlade", trim, new Color(0.45f, 0.4f, 0.55f), m => { m.SetFloat("_VertexColorMul", 0f); m.SetFloat("_OutlineWidth", 1.2f); });
            MaterialLibrary.GetOrCreate("Prop_DullPendant", null, new Color(0.46f, 0.5f, 0.5f), m => { m.SetFloat("_OutlineWidth", 0.8f); m.SetColor("_EmissionColor", Color.black); });
        }

        static bool Build(string name, JObject r)
        {
            string fbxPath = $"{CharDir}/{name}.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null)
            {
                Debug.LogError("[CharacterPrefabBuilder] missing " + fbxPath);
                return false;
            }

            // 1. Palette materials, remapped onto the FBX's named slots.
            bool isChar = true;
            var outfit = MaterialLibrary.GetOrCreate($"{name}_Outfit", Tex($"T_{name}_Outfit"), Color.white, CharMat);
            var head = MaterialLibrary.GetOrCreate($"{name}_Head", Tex($"T_{name}_Head"), Color.white, CharMat);
            var hands = MaterialLibrary.GetOrCreate($"{name}_Hands", Tex($"T_{name}_Hands"), Color.white, CharMat);
            var hair = MaterialLibrary.GetOrCreate($"{name}_Hair", Tex($"T_{name}_Hair"), Color.white, m => { CharMat(m); m.SetFloat("_OutlineWidth", 1.1f); });
            var eyes = MaterialLibrary.GetOrCreate("Shared_Eyes", Tex("T_Eyes"), Color.white, m => { CharMat(m); m.SetFloat("_OutlineWidth", 0f); m.SetFloat("_RimStrength", 0f); });
            var under = MaterialLibrary.GetOrCreate($"{name}_Undershirt", null,
                Undershirt.TryGetValue(name, out var uc) ? uc : new Color(0.15f, 0.12f, 0.1f), CharMat);
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            var map = new Dictionary<string, Material>
            {
                { "M_Outfit", outfit }, { "M_SkinHead", head }, { "M_SkinHands", hands }, { "M_Hair", hair }, { "M_Eyes", eyes }, { "M_Undershirt", under },
            };
            foreach (var kv in map) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            importer.SaveAndReimport();
            model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);

            // 2. Instance + animator + driver.
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = name + "_Visual";
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var anim = inst.GetComponent<Animator>() ?? inst.AddComponent<Animator>();
            bool ranged = r["props"]?["hand_r"]?.ToString() == "Crossbow";
            anim.runtimeAnimatorController = ranged
                ? (RuntimeAnimatorController)AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(AnimatorBuilder.RangedOverridePath)
                : AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimatorBuilder.ControllerPath);
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            if (inst.GetComponent<AnimDriver>() == null) inst.AddComponent<AnimDriver>();
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.updateWhenOffscreen = false;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                smr.skinnedMotionVectors = false;
            }

            // 3. Props at bone sockets (bind pose = T-pose, facing +Z).
            if (r["props"] is JObject props)
            {
                foreach (var p in props)
                {
                    if (!Props.TryGetValue(p.Value.ToString(), out var spec))
                    {
                        Debug.LogWarning($"[CharacterPrefabBuilder] {name}: unknown prop {p.Value}");
                        continue;
                    }
                    string boneName = p.Key == "chest" ? "spine_03" : p.Key;
                    var bone = FindDeep(inst.transform, boneName);
                    if (bone == null)
                    {
                        Debug.LogWarning($"[CharacterPrefabBuilder] {name}: no bone {boneName}");
                        continue;
                    }
                    Attach(inst.transform, bone, p.Value.ToString(), spec, p.Key == "hand_l");
                }
            }

            // 4. Anchors for UI (rule icons, barks) — on the root so they don't bob with the head.
            var headBone = FindDeep(inst.transform, "Head");
            var anchor = new GameObject("Anchor_HeadTop").transform;
            anchor.SetParent(inst.transform, false);
            anchor.localPosition = new Vector3(0f, (headBone != null ? headBone.position.y : 1.7f) + 0.55f, 0f);

            string prefabPath = $"{PrefabDir}/{name}_Visual.prefab";
            PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
            Object.DestroyImmediate(inst);
            return isChar;
        }

        static void CharMat(Material m)
        {
            m.SetFloat("_OutlineWidth", 1.5f);
            m.SetFloat("_RimStrength", 0.3f);
            m.SetFloat("_VertexColorMul", 0f);
        }

        public static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>Combined mesh bounds of a prop model in its own root space.</summary>
        static Bounds LocalBounds(GameObject root)
        {
            var b = new Bounds();
            bool init = false;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var c in Corners(mb))
                {
                    var p = m.MultiplyPoint3x4(c);
                    if (!init) { b = new Bounds(p, Vector3.zero); init = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++)
                yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
        }

        static void Attach(Transform charRoot, Transform bone, string propName, PropSpec spec, bool left)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(spec.Model);
            if (modelAsset == null)
            {
                Debug.LogWarning("[CharacterPrefabBuilder] missing prop model " + spec.Model);
                return;
            }
            var socket = new GameObject("Prop_" + propName).transform;
            var prop = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            prop.transform.SetParent(socket, false);
            prop.transform.localScale *= spec.Scale; // keep the FBX root's own unit scale (kits import with x100 roots)
            if (!string.IsNullOrEmpty(spec.Material))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialLibrary.Dir}/{spec.Material}.mat");
                foreach (var r in prop.GetComponentsInChildren<Renderer>())
                    r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
            }
            foreach (var c in prop.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

            // Bounds in socket space: the FBX root carries its own axis-conversion rotation (e.g. -90 X).
            var b = LocalBounds(socket.gameObject);
            var size = b.size;
            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            var longAxis = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            if (spec.Flip) longAxis = -longAxis;

            // Fist centre in world (bind pose): along the fingers from the wrist, slightly on the palm side.
            float side = left ? -1f : 1f;
            Vector3 fingers = charRoot.right * side;
            Vector3 fist = bone.position + fingers * 0.085f - charRoot.up * 0.035f;
            Quaternion world;
            Vector3 worldPos;
            switch (spec.Hold)
            {
                case Hold.Blade:
                {
                    // long axis → forward (blade ahead of the fist); pommel end sits Grip metres behind the fist.
                    world = Quaternion.FromToRotation(longAxis, charRoot.forward);
                    var pommelLocal = b.center - longAxis * (Vector3.Dot(b.size, Abs(longAxis)) * 0.5f);
                    worldPos = fist - world * (pommelLocal * spec.Scale) - charRoot.forward * spec.Grip;
                    break;
                }
                case Hold.Pistol:
                {
                    // Crossbow authored along +Y (Blender) → after import runs along ±Z; point it forward from the fist.
                    world = Quaternion.FromToRotation(longAxis, charRoot.forward);
                    var gripLocal = b.center - longAxis * (Vector3.Dot(b.size, Abs(longAxis)) * 0.3f);
                    worldPos = fist - world * (gripLocal * spec.Scale);
                    break;
                }
                case Hold.Shield:
                {
                    // Strapped to the back of the forearm (points up in a palms-down T-pose), so it faces outward at
                    // rest and forward when the arm comes up to block.
                    int thin = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
                    var normal = thin == 0 ? Vector3.right : thin == 1 ? Vector3.up : Vector3.forward;
                    if (spec.Flip) normal = -normal;
                    world = Quaternion.FromToRotation(normal, charRoot.up);
                    worldPos = bone.position - fingers * 0.13f + charRoot.up * 0.07f - world * (b.center * spec.Scale);
                    break;
                }
                default:
                {
                    // Chest pendant: disc faces forward on the sternum.
                    int thin = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
                    var normal = thin == 0 ? Vector3.right : thin == 1 ? Vector3.up : Vector3.forward;
                    world = Quaternion.FromToRotation(normal, charRoot.forward);
                    worldPos = bone.position + charRoot.forward * 0.17f + charRoot.up * 0.02f - world * (b.center * spec.Scale);
                    break;
                }
            }
            socket.SetPositionAndRotation(worldPos, world);
            var meta = socket.gameObject.AddComponent<PropSocket>();
            meta.PropName = propName;
            meta.BladeAxis = longAxis;
            // Flat of the blade = the prop's thinnest axis (perpendicular to the blade).
            int thinAxis = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            meta.FlatNormal = thinAxis == 0 ? Vector3.right : thinAxis == 1 ? Vector3.up : Vector3.forward;
            if (Mathf.Abs(Vector3.Dot(meta.FlatNormal, longAxis)) > 0.9f) meta.FlatNormal = Vector3.Cross(longAxis, Vector3.up).normalized;
            socket.SetParent(bone, true);
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}
