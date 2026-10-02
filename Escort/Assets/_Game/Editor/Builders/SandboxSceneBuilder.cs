using HS.Core;
using HS.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS.EditorTools
{
    /// <summary>Minimal scene for camera/controller QA (Tasks 1–3). Rebuilt from code so it is reproducible.</summary>
    public static class SandboxSceneBuilder
    {
        public const string ScenePath = "Assets/_Game/Scenes/Sandbox.unity";

        [MenuItem("Tools/HS/Build Sandbox Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.70f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.55f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.28f, 0.25f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(8f, 1f, 8f);
            ground.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.GetOrCreate("Env_Grass", null, new Color(0.42f, 0.58f, 0.3f), m => { m.SetFloat("_OutlineWidth", 0f); m.SetFloat("_RimStrength", 0f); });

            // Grid markers every 5 m to judge scale from screenshots.
            for (int x = -20; x <= 20; x += 5)
            for (int z = -20; z <= 20; z += 5)
            {
                var m = GameObject.CreatePrimitive(PrimitiveType.Cube);
                m.name = $"Marker_{x}_{z}";
                m.transform.position = new Vector3(x, 0.05f, z);
                m.transform.localScale = new Vector3(0.3f, 0.1f, 0.3f);
                Object.DestroyImmediate(m.GetComponent<Collider>());
            }

            new GameObject("SimLoop").AddComponent<SimLoop>();
            new GameObject("RunContext").AddComponent<RunContext>();

            GameObject hero;
            var heroPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameplayPrefabBuilder.Dir + "/Callum.prefab");
            if (heroPrefab != null)
            {
                hero = (GameObject)PrefabUtility.InstantiatePrefab(heroPrefab);
                hero.transform.position = new Vector3(-8f, 0.05f, -6f);
                var route = new GameObject("Route").AddComponent<HS.Hero.RouteGraph>();
                var pts = new[] { new Vector3(-8, 0, -6), new Vector3(-8, 0, 6), new Vector3(0, 0, 8), new Vector3(8, 0, 6), new Vector3(8, 0, -6), new Vector3(0, 0, -8) };
                for (int i = 0; i < pts.Length; i++)
                {
                    var n = new GameObject("Node" + i);
                    n.transform.SetParent(route.transform, false);
                    n.transform.position = pts[i];
                    var mk = n.AddComponent<HS.Hero.RouteMarker>();
                    mk.Threshold = i == 2;
                    mk.Label = i == 2 ? "gate" : "";
                }
                var binder = new GameObject("RouteBinder").AddComponent<HS.Hero.RouteBinder>();
                binder.Graphs = new[] { route };
                new GameObject("RunContextHook").AddComponent<HS.QA.SandboxHook>();
            }
            else hero = MakeCapsule("HeroPlaceholder", new Vector3(-4f, 1f, 0f), new Color(0.2f, 0.35f, 0.8f));
            var skPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameplayPrefabBuilder.Dir + "/Sidekick.prefab");
            GameObject side;
            if (skPrefab != null)
            {
                side = (GameObject)PrefabUtility.InstantiatePrefab(skPrefab);
                side.transform.position = new Vector3(4f, 0.05f, 3f);
                side.AddComponent<HS.QA.QaAutoPilot>().Center = new Vector3(0f, 0f, 2f);
                Object.DestroyImmediate(side.GetComponent<HS.Sidekick.PlayerCommands>());
            }
            else side = MakeCapsule("SidekickPlaceholder", new Vector3(4f, 1f, 3f), new Color(0.85f, 0.65f, 0.2f));
            var ht = hero.transform.Find("CamTarget") ?? hero.transform;
            var st = side.transform.Find("CamTarget") ?? side.transform;
            CameraRig.Build(ht, st, new CameraTuning());

            EditorSceneManager.SaveScene(scene, ScenePath);
            AgentLog("sandbox built");
        }

        static GameObject MakeCapsule(string name, Vector3 pos, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.position = pos;
            go.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = c };
            return go;
        }

        static void AgentLog(string msg) => HS.Agent.AgentBridge.Write("builder.json", $"{{\"ok\":true,\"msg\":\"{msg}\"}}");
    }
}
