using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HS.EditorTools
{
    /// <summary>QA report for imported art: avatar validity, humanoid mapping, clip list. → Library/Agent/art_report.json</summary>
    public static class ArtReport
    {
        [MenuItem("Tools/HS/QA/Reimport Characters")]
        public static void ReimportCharacters()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Game/Art/Characters" }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            HS.Agent.AgentBridge.Write("reimport.json", "{\"ok\":true}");
        }

        /// <summary>Force-reimport every model under the art folders (after import-rule changes).</summary>
        [MenuItem("Tools/HS/QA/Reimport All Art Models")]
        public static void ReimportAllModels()
        {
            int n = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Game/Art" }))
                {
                    AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
                    n++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            HS.Agent.AgentBridge.Write("reimport.json", "{\"ok\":true,\"models\":" + n + "}");
        }

        [MenuItem("Tools/HS/QA/Reimport Icons")]
        public static void ReimportIcons()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/_Game/Resources/Icons", "Assets/_Game/Art/UI" }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            HS.Agent.AgentBridge.Write("reimport.json", "{\"ok\":true,\"what\":\"icons\"}");
        }

        [MenuItem("Tools/HS/QA/Art Report")]
        public static void Run()
        {
            var sb = new StringBuilder("{\"characters\":[");
            bool first = true;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Game/Art/Characters", "Assets/_Game/Art/Animations" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var objs = AssetDatabase.LoadAllAssetsAtPath(path);
                var avatar = objs.OfType<Avatar>().FirstOrDefault();
                var clips = objs.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var bounds = new Bounds();
                bool init = false;
                foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!init) { bounds = r.bounds; init = true; } else bounds.Encapsulate(r.bounds);
                }
                string missing = "";
                if (avatar != null && avatar.isHuman)
                {
                    var hd = avatar.humanDescription;
                    var mapped = hd.human.Select(h => h.humanName).ToHashSet();
                    var required = new[] { "Hips", "Spine", "Chest", "Neck", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" };
                    missing = string.Join(",", required.Where(r => !mapped.Contains(r)));
                }
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"path\":\"").Append(path).Append("\",\"avatarValid\":").Append(avatar != null && avatar.isValid ? "true" : "false")
                  .Append(",\"isHuman\":").Append(avatar != null && avatar.isHuman ? "true" : "false")
                  .Append(",\"missingBones\":\"").Append(missing).Append("\"")
                  .Append(",\"size\":\"").Append(bounds.size.ToString("F2")).Append("\"")
                  .Append(",\"rootRot\":\"").Append(go.transform.rotation.eulerAngles.ToString("F1")).Append("\"")
                  .Append(",\"clips\":").Append(clips.Length);
                if (clips.Length > 0)
                    sb.Append(",\"clipNames\":\"").Append(string.Join(",", clips.Select(c => $"{c.name}:{c.length:F2}{(c.isLooping ? "L" : "")}"))).Append("\"");
                sb.Append('}');
            }
            sb.Append("]}");
            File.WriteAllText(Path.Combine(HS.Agent.AgentBridge.Dir, "art_report.json"), sb.ToString());
        }
    }
}
