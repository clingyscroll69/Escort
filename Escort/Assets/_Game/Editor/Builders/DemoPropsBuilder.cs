using HS.Core;
using UnityEditor;
using UnityEngine;
using static HS.EditorTools.RoomKit;

namespace HS.EditorTools
{
    /// <summary>
    /// Props for the skill-demo stage (the "System simulation" in the picker and Field Guide), built from the same CC0
    /// kit models as the rooms and registered in GameAssets.demoProps. Each barrel of the stack is its own child named
    /// "Barrel" so the Loosen Bolt demo can drop them.
    /// </summary>
    public static class DemoPropsBuilder
    {
        public const string Dir = "Assets/_Game/Prefabs/Demo";

        [MenuItem("Tools/HS/Build/Demo Props")]
        public static void Build()
        {
            System.IO.Directory.CreateDirectory(Dir);
            var assets = AssetDatabase.LoadAssetAtPath<GameAssets>(GameAssetsBuilder.Path);
            assets.demoProps.Clear();
            Save(assets, "barrel_stack", root =>
            {
                Place(root.transform, "Barrel_Holder", Vector3.zero, 90f, 1f, Col.None, true, false);
                Place(root.transform, "Barrel", new Vector3(-0.35f, 1.1f, 0f), 0f, 1f, Col.None, true, false);
                Place(root.transform, "Barrel", new Vector3(0.35f, 1.1f, 0f), 30f, 1f, Col.None, true, false);
                Place(root.transform, "Barrel", new Vector3(0f, 1.95f, 0f), 60f, 1f, Col.None, true, false);
            });
            Save(assets, "crate_perch", root =>
            {
                Place(root.transform, "Crate_Wooden", new Vector3(-0.5f, 0f, 0f), 6f, 1f, Col.None, true, false);
                Place(root.transform, "Crate_Wooden", new Vector3(0.5f, 0f, 0.05f), -8f, 1f, Col.None, true, false);
                Place(root.transform, "Crate_Wooden", new Vector3(0f, 0f, -0.95f), 14f, 1f, Col.None, true, false);
                Place(root.transform, "Crate_Wooden", new Vector3(0f, 0f, 0.95f), -4f, 1f, Col.None, true, false);
            });
            Save(assets, "bush", root => Place(root.transform, "Bush_Common", Vector3.zero, 0f, 1.1f, Col.None, true, false));
            Save(assets, "stone", root => Place(root.transform, "ChronicleStone", Vector3.zero, 0f, 1f, Col.None, true, false));
            Save(assets, "rock", root => Place(root.transform, "Rock_Medium_2", Vector3.zero, 0f, 0.8f, Col.None, true, false));
            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();
            HS.Agent.AgentBridge.Write("builder.json", "{\"ok\":true,\"msg\":\"demo props\"}");
        }

        static void Save(GameAssets assets, string id, System.Action<GameObject> build)
        {
            var root = new GameObject("Demo_" + id);
            build(root);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/{id}.prefab");
            Object.DestroyImmediate(root);
            assets.demoProps.Add(new GameAssets.Entry { id = id, prefab = prefab });
        }
    }
}
