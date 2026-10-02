using HS.Rooms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HS.EditorTools
{
    /// <summary>QA scene: the four Old Road modules chained along +Z (as the game assembles them) + campfire + arena.</summary>
    public static class RoomsPreviewBuilder
    {
        public const string ScenePath = "Assets/_Game/Scenes/QA_Rooms.unity";

        [MenuItem("Tools/HS/QA/Build Rooms Preview")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneLighting.Apply();
            float z = 0f;
            int i = 0;
            foreach (var id in new[] { "crossroads_shrine", "toll_gate", "ruined_gatehouse", "wagon_camp" })
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{RoomBuilder.Dir}/{id}.prefab");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(p);
                go.transform.position = new Vector3(0f, 0f, z);
                var m = go.GetComponent<RoomModule>();
                m.ApplyVariant(i % 2);
                z += m.Length;
                i++;
            }
            foreach (var (id, pos) in new[] { ("campfire", new Vector3(120f, 0f, 0f)), ("rigged_duel", new Vector3(200f, 0f, 0f)) })
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{RoomBuilder.Dir}/{id}.prefab");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(p);
                go.transform.position = pos;
            }
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.fieldOfView = 32f;
            cam.transform.position = new Vector3(0f, 16f, 6f);
            cam.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
            EditorSceneManager.SaveScene(scene, ScenePath);
            HS.Agent.AgentBridge.Write("builder.json", "{\"ok\":true,\"msg\":\"rooms preview\"}");
        }
    }
}
