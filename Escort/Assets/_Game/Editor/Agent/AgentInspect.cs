using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HS.Agent
{
    public static class AgentInspect
    {
        /// <summary>Renderers whose bounds contain the XZ point (args {"x":..,"z":..}) with materials + texture names.</summary>
        [MenuItem("Tools/Agent/Inspect Near")]
        public static void InspectNear()
        {
            var args = AgentBridge.ReadArgs("inspect_args.json");
            float x = float.Parse(args["x"], System.Globalization.CultureInfo.InvariantCulture);
            float z = float.Parse(args["z"], System.Globalization.CultureInfo.InvariantCulture);
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var b = r.bounds;
                if (x < b.min.x || x > b.max.x || z < b.min.z || z > b.max.z) continue;
                if (!first) sb.Append(',');
                first = false;
                var mats = string.Join(";", r.sharedMaterials.Select(m => m == null ? "null" : m.name + ":" + (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") ? m.GetTexture("_BaseMap").name : "-")));
                sb.Append($"\"{GetPath(r.transform)} [{mats}] size={b.size:F1}\"");
            }
            sb.Append(']');
            AgentBridge.Write("inspect.json", sb.ToString());
        }

        static string GetPath(Transform t) => t.parent == null ? t.name : GetPath(t.parent) + "/" + t.name;

        /// <summary>World-space renderer bounds (size + base offset) of each model asset in its native root transform.</summary>
        [MenuItem("Tools/Agent/Inspect Model Bounds")]
        public static void InspectModelBounds()
        {
            var args = AgentBridge.ReadArgs("inspect_args.json");
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var path in (args.TryGetValue("paths", out var p) ? p : "").Split(','))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path.Trim());
                if (asset == null) continue;
                var go = (GameObject)Object.Instantiate(asset);
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) { Object.DestroyImmediate(go); continue; }
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                int tris = 0;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh) tris += mf.sharedMesh.triangles.Length / 3;
                if (!first) sb.Append(',');
                first = false;
                sb.Append($"\"{System.IO.Path.GetFileNameWithoutExtension(path.Trim())} size={b.size:F2} center={b.center:F2} tris={tris}\"");
                Object.DestroyImmediate(go);
            }
            sb.Append(']');
            AgentBridge.Write("inspect.json", sb.ToString());
        }

        [MenuItem("Tools/Agent/Inspect Game View")]
        public static void InspectGameView()
        {
            var size = UnityEditor.Handles.GetMainGameViewSize();
            AgentBridge.Write("inspect.json", $"[\"game view {size.x}x{size.y} aspect={size.x / size.y:F3}\"]");
        }

        /// <summary>Every transform of model assets as imported (local pos/rot/scale) plus overall bounds. Args {"paths":"..."}</summary>
        [MenuItem("Tools/Agent/Inspect Model Hierarchy")]
        public static void InspectModelHierarchy()
        {
            var args = AgentBridge.ReadArgs("inspect_args.json");
            var sb = new StringBuilder("[");
            foreach (var path in (args.TryGetValue("paths", out var p) ? p : "").Split(','))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path.Trim());
                if (asset == null) continue;
                var go = (GameObject)Object.Instantiate(asset);
                var rs = go.GetComponentsInChildren<Renderer>();
                var b = rs.Length > 0 ? rs[0].bounds : new Bounds();
                foreach (var r in rs) b.Encapsulate(r.bounds);
                sb.Append($"\"{asset.name}: bounds size={b.size:F2} center={b.center:F2}\",");
                foreach (var t in go.GetComponentsInChildren<Transform>())
                    sb.Append($"\"  {GetPath(t)} pos={t.localPosition:F3} rot={t.localEulerAngles:F1} scale={t.localScale:F2}\",");
                Object.DestroyImmediate(go);
            }
            sb.Append("\"end\"]");
            AgentBridge.Write("inspect.json", sb.ToString());
        }

        /// <summary>World-space arm/leg directions of model assets in their default pose. Args {"paths":"..."}</summary>
        [MenuItem("Tools/Agent/Inspect Pose")]
        public static void InspectPose()
        {
            var args = AgentBridge.ReadArgs("inspect_args.json");
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var path in (args.TryGetValue("paths", out var p) ? p : "").Split(','))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path.Trim());
                if (asset == null) continue;
                var go = (GameObject)Object.Instantiate(asset);
                Transform F(string n) => go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
                string D(string a, string b) { var ta = F(a); var tb = F(b); return ta && tb ? (tb.position - ta.position).normalized.ToString("F2") : "?"; }
                string P(string a) { var t = F(a); return t ? t.position.ToString("F2") : "?"; }
                if (!first) sb.Append(',');
                first = false;
                sb.Append($"\"{System.IO.Path.GetFileName(path.Trim())}: upperarm_l->hand_l {D("upperarm_l", "hand_l")} upperarm_r->hand_r {D("upperarm_r", "hand_r")} thigh_l->foot_l {D("thigh_l", "foot_l")} pelvis {P("pelvis")} head {P("Head")} hand_l {P("hand_l")} hand_r {P("hand_r")} pelvisFwd {F("pelvis")?.forward:F2} headFwd {F("Head")?.forward:F2}\"");
                Object.DestroyImmediate(go);
            }
            sb.Append(']');
            AgentBridge.Write("inspect.json", sb.ToString());
        }

        /// <summary>World transforms of key humanoid bones for every Animator in the scene → inspect.json</summary>
        [MenuItem("Tools/Agent/Inspect Bones")]
        public static void InspectBones()
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsSortMode.InstanceID))
            {
                if (!a.isHuman) continue;
                foreach (var hb in new[] { HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head, HumanBodyBones.RightHand })
                {
                    var t = a.GetBoneTransform(hb);
                    if (t == null) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append($"\"{a.name}.{hb}({t.name}): pos={t.position:F2} rootRel={a.transform.InverseTransformPoint(t.position):F2} lossy={t.lossyScale:F2} fwd={t.forward:F2}\"");
                }
                if (!first) sb.Append(',');
                sb.Append($"\"{a.name}: rootFwd={a.transform.forward:F2} humanScale={a.humanScale:F3} bodyRot={a.bodyRotation.eulerAngles:F0}\"");
            }
            sb.Append(']');
            AgentBridge.Write("inspect.json", sb.ToString());
        }

        /// <summary>SkinnedMeshRenderer setup for a prefab/model asset. Args {"paths":"Assets/..prefab"}</summary>
        [MenuItem("Tools/Agent/Inspect Skinning")]
        public static void InspectSkinning()
        {
            var args = AgentBridge.ReadArgs("inspect_args.json");
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var path in (args.TryGetValue("paths", out var p) ? p : "").Split(','))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path.Trim());
                if (go == null) continue;
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    var mesh = smr.sharedMesh;
                    sb.Append($"\"{smr.name}: root={(smr.rootBone ? smr.rootBone.name : "null")} bones={smr.bones.Length} nullBones={smr.bones.Count(b => b == null)} bounds=c{smr.localBounds.center:F2}/s{smr.localBounds.size:F2} bindposes={(mesh ? mesh.bindposes.Length : 0)} verts={(mesh ? mesh.vertexCount : 0)} lpos={smr.transform.localPosition:F2} lrot={smr.transform.localEulerAngles:F0} lscale={smr.transform.localScale:F2} enabled={smr.enabled}\"");
                }
            }
            sb.Append(']');
            AgentBridge.Write("inspect.json", sb.ToString());
        }

        /// <summary>Root transform + mesh bounds of model assets. Args inspect_args.json {"paths":"a.fbx,b.fbx"}</summary>
        [MenuItem("Tools/Agent/Inspect Models")]
        public static void InspectModels()
        {
            var args = AgentBridge.ReadArgs("inspect_args.json");
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var path in (args.TryGetValue("paths", out var p) ? p : "").Split(','))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path.Trim());
                if (go == null) continue;
                foreach (var t in go.GetComponentsInChildren<Transform>())
                {
                    var mf = t.GetComponent<MeshFilter>();
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append($"\"{path.Trim()}::{t.name} lpos={t.localPosition:F3} lrot={t.localEulerAngles:F1} lscale={t.localScale:F4} mesh={(mf && mf.sharedMesh ? mf.sharedMesh.bounds.size.ToString("F3") : "-")}\"");
                }
            }
            sb.Append(']');
            AgentBridge.Write("inspect.json", sb.ToString());
        }

        /// <summary>Dump renderers/materials/shaders under objects whose name contains args.name → Library/Agent/inspect.json</summary>
        [MenuItem("Tools/Agent/Inspect Renderers")]
        public static void InspectRenderers()
        {
            var args = AgentBridge.ReadArgs("inspect_args.json");
            string needle = args.TryGetValue("name", out var n) ? n : "";
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                if (!r.name.Contains(needle) && (r.transform.parent == null || !r.transform.parent.name.Contains(needle))) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"renderer\":\"").Append(r.name).Append("\",\"mats\":[");
                sb.Append(string.Join(",", r.sharedMaterials.Select(m => m == null ? "\"<null>\"" :
                    $"\"{m.name}|{(m.shader != null ? m.shader.name : "<noshader>")}|supported={(m.shader != null && m.shader.isSupported)}|{AssetDatabase.GetAssetPath(m)}\"")));
                var mf = r.GetComponent<MeshFilter>();
                sb.Append("],\"mesh\":\"").Append(mf && mf.sharedMesh ? mf.sharedMesh.name + " sub=" + mf.sharedMesh.subMeshCount : "").Append("\"}");
            }
            sb.Append(']');
            AgentBridge.Write("inspect.json", sb.ToString());
        }
    }
}
