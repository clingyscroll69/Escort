using HS.Core;
using HS.QA;
using HS.Rooms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HS.EditorTools
{
    public static class SkillShowcaseBuilder
    {
        [MenuItem("Tools/HS/QA/Build Skill Showcase")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneLighting.Apply();
            new GameObject("SimLoop").AddComponent<SimLoop>();
            new GameObject("RunContext").AddComponent<RunContext>();
            new GameObject("Projectiles").AddComponent<HS.Skills.ProjectileSystem>();
            var room = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RoomBuilder.Dir + "/toll_gate.prefab"));
            room.GetComponent<RoomModule>().ApplyVariant(0);
            GameObject P(string path, Vector3 pos, float yaw)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                return go;
            }
            var sk = P(GameplayPrefabBuilder.Dir + "/Sidekick.prefab", new Vector3(-6.4f, 0.05f, 20.2f), 60f);
            Object.DestroyImmediate(sk.GetComponent<HS.Sidekick.PlayerCommands>());
            P(GameplayPrefabBuilder.Dir + "/Callum.prefab", new Vector3(0f, 0.05f, 14f), 0f);
            P(GameplayPrefabBuilder.EnemyPrefabPath("thug"), new Vector3(-2.6f, 0.05f, 23f), 180f);
            P(GameplayPrefabBuilder.EnemyPrefabPath("thug"), new Vector3(-1.2f, 0.05f, 24.2f), 190f);
            P(GameplayPrefabBuilder.EnemyPrefabPath("crossbowman"), new Vector3(5.6f, 0.05f, 28.8f), 170f);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.fieldOfView = 34f;
            cam.transform.position = new Vector3(0f, 16f, 8f);
            cam.transform.LookAt(new Vector3(-2f, 0f, 22f));
            new GameObject("Showcase").AddComponent<SkillShowcase>();
            EditorSceneManager.SaveScene(scene, "Assets/_Game/Scenes/QA_Skills.unity");
        }
    }
}
