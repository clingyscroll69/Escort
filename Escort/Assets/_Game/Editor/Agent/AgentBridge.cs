using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace HS.Agent
{
    /// <summary>
    /// Editor-side helpers for the coding agent driving Unity through the MCP bridge.
    /// Lives in its own assembly with no game references so it keeps working while game code has compile errors.
    /// Writes machine-readable status to Library/Agent/*.json.
    /// </summary>
    [InitializeOnLoad]
    public static class AgentBridge
    {
        public static readonly string Dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/Agent"));
        static readonly List<CompilerMessage> Messages = new List<CompilerMessage>();

        static AgentBridge()
        {
            Directory.CreateDirectory(Dir);
            CompilationPipeline.compilationStarted -= OnStarted;
            CompilationPipeline.compilationStarted += OnStarted;
            CompilationPipeline.assemblyCompilationFinished -= OnAssembly;
            CompilationPipeline.assemblyCompilationFinished += OnAssembly;
            CompilationPipeline.compilationFinished -= OnFinished;
            CompilationPipeline.compilationFinished += OnFinished;
            Write("domain.json", $"{{\"loadedAt\":\"{Now()}\"}}");
        }

        static string Now() => DateTime.UtcNow.ToString("o");

        static void OnStarted(object _)
        {
            Messages.Clear();
            Write("compile.json", $"{{\"state\":\"compiling\",\"at\":\"{Now()}\"}}");
        }

        static void OnAssembly(string asm, CompilerMessage[] msgs) => Messages.AddRange(msgs);

        static void OnFinished(object _)
        {
            var errors = Messages.Where(m => m.type == CompilerMessageType.Error).ToList();
            var warnings = Messages.Where(m => m.type == CompilerMessageType.Warning).ToList();
            var sb = new StringBuilder();
            sb.Append("{\"state\":\"finished\",\"at\":\"").Append(Now()).Append("\",\"errors\":").Append(errors.Count)
              .Append(",\"warnings\":").Append(warnings.Count).Append(",\"messages\":[");
            bool first = true;
            foreach (var m in errors.Concat(warnings).Take(200))
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"type\":\"").Append(m.type).Append("\",\"file\":\"").Append(Esc(m.file)).Append("\",\"line\":")
                  .Append(m.line).Append(",\"msg\":\"").Append(Esc(m.message)).Append("\"}");
            }
            sb.Append("]}");
            Write("compile.json", sb.ToString());
        }

        static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");

        public static void Write(string file, string content)
        {
            try { File.WriteAllText(Path.Combine(Dir, file), content); }
            catch (Exception e) { Debug.LogWarning("[Agent] write failed: " + e.Message); }
        }

        /// <summary>Refresh the AssetDatabase; records whether a compile was triggered.</summary>
        [MenuItem("Tools/Agent/Refresh")]
        public static void Refresh()
        {
            Write("refresh.json", $"{{\"state\":\"refreshing\",\"at\":\"{Now()}\"}}");
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            EditorApplication.delayCall += () =>
                Write("refresh.json", $"{{\"state\":\"done\",\"at\":\"{Now()}\",\"compiling\":{(EditorApplication.isCompiling ? "true" : "false")}}}");
        }

        /// <summary>Re-run the audio import rules on every clip (new clips import before a changed postprocessor compiles).</summary>
        [MenuItem("Tools/Agent/Reimport Audio")]
        public static void ReimportAudio()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/_Game/Resources/Audio" }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            Write("reimport.json", $"{{\"state\":\"done\",\"at\":\"{Now()}\"}}");
        }

        [MenuItem("Tools/Agent/Clear Console")]
        public static void ClearConsole()
        {
            var t = System.Type.GetType("UnityEditor.LogEntries, UnityEditor");
            t?.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)?.Invoke(null, null);
        }

        /// <summary>Report compile errors of a shader by name (Library/Agent/shader.json). Args: shader_args.json {"shader":"HS/Toon"}</summary>
        [MenuItem("Tools/Agent/Shader Report")]
        public static void ShaderReport()
        {
            var args = ReadArgs("shader_args.json");
            var name = args.TryGetValue("shader", out var n) ? n : "HS/Toon";
            var sh = Shader.Find(name);
            if (sh == null) { Write("shader.json", "{\"ok\":false,\"error\":\"not found\"}"); return; }
            var msgs = ShaderUtil.GetShaderMessages(sh);
            var sb = new StringBuilder("{\"ok\":true,\"supported\":" + (sh.isSupported ? "true" : "false") + ",\"messages\":[");
            for (int i = 0; i < msgs.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("\"").Append(msgs[i].severity).Append(": ").Append(Esc(msgs[i].message)).Append(" @").Append(msgs[i].line).Append("\"");
            }
            sb.Append("]}");
            Write("shader.json", sb.ToString());
        }

        /// <summary>Import TextMeshPro's essential resources (shaders, default font, settings) without a dialog.</summary>
        [MenuItem("Tools/Agent/Import TMP Essentials")]
        public static void ImportTmpEssentials()
        {
            const string pkg = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
            AssetDatabase.importPackageCompleted += _ => Write("tmp.json", "{\"ok\":true}");
            AssetDatabase.importPackageFailed += (_, err) => Write("tmp.json", "{\"ok\":false,\"error\":\"" + Esc(err) + "\"}");
            AssetDatabase.ImportPackage(pkg, false);
        }

        /// <summary>Resolve packages after manifest edits.</summary>
        [MenuItem("Tools/Agent/Resolve Packages")]
        public static void ResolvePackages() => UnityEditor.PackageManager.Client.Resolve();

        /// <summary>
        /// Render a camera to docs/qa/shots/&lt;name&gt;.png. Parameters are read from Library/Agent/capture_args.json:
        /// {"name":"x","width":1600,"height":900,"camera":"optional GameObject name",
        ///  "pos":[x,y,z],"lookAt":[x,y,z],"fov":40}. pos/lookAt create a temporary camera.
        /// </summary>
        [MenuItem("Tools/Agent/Capture")]
        public static void Capture()
        {
            var args = ReadArgs("capture_args.json");
            string name = args.TryGetValue("name", out var n) ? n : "capture";
            int w = args.TryGetValue("width", out var ws) ? int.Parse(ws) : 1600;
            int h = args.TryGetValue("height", out var hs) ? int.Parse(hs) : 900;
            Camera cam = null;
            GameObject temp = null;
            if (args.TryGetValue("pos", out var pos) && args.TryGetValue("lookAt", out var look))
            {
                temp = new GameObject("__AgentCaptureCam") { hideFlags = HideFlags.HideAndDontSave };
                cam = temp.AddComponent<Camera>();
                var main = Camera.main;
                if (main != null) cam.CopyFrom(main);
                cam.transform.position = ParseV3(pos);
                cam.transform.LookAt(ParseV3(look));
                cam.fieldOfView = args.TryGetValue("fov", out var fov) ? float.Parse(fov) : 40f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 500f;
            }
            else if (args.TryGetValue("camera", out var camName) && !string.IsNullOrEmpty(camName))
            {
                var go = GameObject.Find(camName);
                cam = go ? go.GetComponent<Camera>() : null;
            }
            if (cam == null) cam = Camera.main;
            if (cam == null) cam = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (cam == null) { Write("capture.json", "{\"ok\":false,\"error\":\"no camera\"}"); return; }

            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/qa/shots"));
                Directory.CreateDirectory(outDir);
                var file = Path.Combine(outDir, name + ".png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                Write("capture.json", $"{{\"ok\":true,\"file\":\"{Esc(file)}\",\"camera\":\"{Esc(cam.name)}\"}}");
            }
            catch (Exception e)
            {
                Write("capture.json", $"{{\"ok\":false,\"error\":\"{Esc(e.Message)}\"}}");
            }
            finally
            {
                cam.targetTexture = prev;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                if (temp) UnityEngine.Object.DestroyImmediate(temp);
            }
        }

        public static Dictionary<string, string> ReadArgs(string file)
        {
            var result = new Dictionary<string, string>();
            var path = Path.Combine(Dir, file);
            if (!File.Exists(path)) return result;
            // Tiny flat JSON reader: "key": value | "string" | [a,b,c]
            var s = File.ReadAllText(path).Trim().TrimStart('{').TrimEnd('}');
            int i = 0;
            while (i < s.Length)
            {
                int k0 = s.IndexOf('"', i); if (k0 < 0) break;
                int k1 = s.IndexOf('"', k0 + 1);
                string key = s.Substring(k0 + 1, k1 - k0 - 1);
                int colon = s.IndexOf(':', k1);
                int v = colon + 1;
                while (v < s.Length && char.IsWhiteSpace(s[v])) v++;
                string val;
                if (s[v] == '"') { int e = s.IndexOf('"', v + 1); val = s.Substring(v + 1, e - v - 1); i = e + 1; }
                else if (s[v] == '[') { int e = s.IndexOf(']', v); val = s.Substring(v + 1, e - v - 1); i = e + 1; }
                else { int e = s.IndexOf(',', v); if (e < 0) e = s.Length; val = s.Substring(v, e - v).Trim(); i = e; }
                result[key] = val;
                int comma = s.IndexOf(',', i); if (comma < 0) break; i = comma + 1;
            }
            return result;
        }

        public static Vector3 ParseV3(string s)
        {
            var p = s.Split(',').Select(x => float.Parse(x.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            return new Vector3(p[0], p[1], p[2]);
        }
    }
}
