using System.IO;
using System.Linq;
using HS.QA;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HS.EditorTools
{
    /// <summary>QA scene: every character visual in a row on a grass plane, for turntable/pose captures.</summary>
    public static class LineupSceneBuilder
    {
        public const string ScenePath = "Assets/_Game/Scenes/QA_Lineup.unity";

        [MenuItem("Tools/HS/QA/Build Character Lineup")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneLighting.Apply();
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(6f, 1f, 3f);
            ground.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.GetOrCreate("Env_Grass", null, new Color(0.42f, 0.58f, 0.3f), m => { m.SetFloat("_OutlineWidth", 0f); m.SetFloat("_RimStrength", 0f); });

            var prefabs = Directory.GetFiles(CharacterPrefabBuilder.PrefabDir, "*_Visual.prefab").OrderBy(p => p).ToArray();
            string[] order = { "Sidekick", "SidekickM", "Callum", "Thug", "Brute", "Crossbowman", "Turncoat", "Ambusher", "Archer", "Ashgrave" };
            float x = -(order.Length - 1) * 1.1f;
            foreach (var n in order)
            {
                var path = $"{CharacterPrefabBuilder.PrefabDir}/{n}_Visual.prefab";
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (p == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(p);
                go.transform.position = new Vector3(x, 0f, 0f);
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // face the -Z camera
                x += 2.2f;
            }
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 1.4f, -14f);
            cam.transform.LookAt(new Vector3(0f, 1.0f, 0f));
            cam.fieldOfView = 40f;
            new GameObject("LineupDriver").AddComponent<LineupDriver>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            HS.Agent.AgentBridge.Write("builder.json", "{\"ok\":true,\"msg\":\"lineup built\"}");
        }
    }

    /// <summary>Shared outdoor lighting setup (warm sun, cool trilight ambient, light fog) for all scenes.</summary>
    public static class SceneLighting
    {
        public static Light Apply()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.35f;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.transform.rotation = Quaternion.Euler(48f, -38f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.74f, 0.86f);
            RenderSettings.ambientEquatorColor = new Color(0.56f, 0.58f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.3f, 0.26f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.74f, 0.8f, 0.86f);
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 140f;
            ApplyPost();
            return sun;
        }

        public const string PostProfilePath = "Assets/_Game/Art/PostFX_Default.asset";

        /// <summary>Global volume: shared grade that unifies the palette (GDD §2).</summary>
        public static Volume ApplyPost()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, PostProfilePath);
                var tm = profile.Add<Tonemapping>(true);
                tm.mode.Override(TonemappingMode.Neutral);
                var ca = profile.Add<ColorAdjustments>(true);
                ca.postExposure.Override(0.15f);
                ca.contrast.Override(12f);
                ca.saturation.Override(14f);
                var bloom = profile.Add<Bloom>(true);
                bloom.threshold.Override(1.05f);
                bloom.intensity.Override(0.35f);
                bloom.scatter.Override(0.6f);
                var vig = profile.Add<Vignette>(true);
                vig.intensity.Override(0.22f);
                vig.smoothness.Override(0.45f);
                foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
                AssetDatabase.SaveAssets();
            }
            var go = new GameObject("GlobalVolume");
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.sharedProfile = profile;
            return v;
        }
    }
}
