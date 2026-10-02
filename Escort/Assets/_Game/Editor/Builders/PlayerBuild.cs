using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HS.EditorTools
{
    /// <summary>macOS player build of the slice (Main scene) → Builds/macOS/HerosSidekick.app; result to Library/Agent/build.json.</summary>
    public static class PlayerBuild
    {
        [MenuItem("Tools/HS/Build/macOS Player")]
        public static void BuildMac()
        {
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/macOS"));
            Directory.CreateDirectory(outDir);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { GameAssetsBuilder.MainScenePath },
                locationPathName = Path.Combine(outDir, "HerosSidekick.app"),
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };
            // This project isn't linked to Unity Cloud: native-symbol upload can only fail (403) and log errors.
            UnityEditor.CrashReporting.CrashReportingSettings.enabled = false;
            var report = BuildPipeline.BuildPlayer(opts);
            var sum = report.summary;
            int errors = 0, warnings = 0;
            var msgs = new System.Text.StringBuilder();
            foreach (var step in report.steps)
            foreach (var m in step.messages)
            {
                if (m.type == LogType.Error || m.type == LogType.Exception) errors++;
                else if (m.type == LogType.Warning) warnings++;
                else continue;
                if (msgs.Length < 3000) msgs.Append(m.type).Append(": ").Append(m.content.Split('\n')[0].Replace("\"", "'")).Append(" | ");
            }
            File.WriteAllText(Path.Combine(outDir, "build_messages.txt"), msgs.ToString());
            HS.Agent.AgentBridge.Write("build.json",
                $"{{\"result\":\"{sum.result}\",\"errors\":{errors},\"warnings\":{warnings},\"size_mb\":{sum.totalSize / (1024f * 1024f):0.0},\"seconds\":{sum.totalTime.TotalSeconds:0},\"path\":\"{sum.outputPath.Replace("\\\\", "/")}\"}}");
        }
    }
}
