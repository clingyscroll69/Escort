using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace HS.Agent
{
    /// <summary>
    /// Runs Unity tests and writes results to Library/Agent/tests.json. Callbacks are re-registered on every domain
    /// load so PlayMode runs (which reload the domain) still report back. Args: Library/Agent/tests_args.json
    /// {"mode":"EditMode|PlayMode","filter":"optional full or partial test name"}.
    /// </summary>
    [InitializeOnLoad]
    public static class AgentTests
    {
        static readonly TestRunnerApi Api;
        static readonly Callbacks Cb = new Callbacks();

        static AgentTests()
        {
            Api = ScriptableObject.CreateInstance<TestRunnerApi>();
            Api.RegisterCallbacks(Cb);
        }

        [MenuItem("Tools/Agent/Run Tests")]
        public static void Run()
        {
            var args = AgentBridge.ReadArgs("tests_args.json");
            var mode = args.TryGetValue("mode", out var m) && m == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode;
            var filter = new Filter { testMode = mode };
            if (args.TryGetValue("filter", out var f) && !string.IsNullOrEmpty(f)) filter.groupNames = new[] { f };
            AgentBridge.Write("tests.json", $"{{\"state\":\"running\",\"mode\":\"{mode}\",\"at\":\"{DateTime.UtcNow:o}\"}}");
            Api.Execute(new ExecutionSettings(filter));
        }

        class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                var leaves = new List<ITestResultAdaptor>();
                Collect(result, leaves);
                int pass = 0, fail = 0, skip = 0;
                var sb = new StringBuilder();
                sb.Append("{\"state\":\"finished\",\"at\":\"").Append(DateTime.UtcNow.ToString("o")).Append("\",\"results\":[");
                bool first = true;
                foreach (var r in leaves)
                {
                    if (r.TestStatus == TestStatus.Passed) pass++;
                    else if (r.TestStatus == TestStatus.Failed) fail++;
                    else skip++;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"name\":\"").Append(Esc(r.FullName)).Append("\",\"status\":\"").Append(r.TestStatus)
                      .Append("\",\"duration\":").Append(r.Duration.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))
                      .Append(",\"message\":\"").Append(Esc(r.Message)).Append("\",\"stack\":\"").Append(Esc(Trim(r.StackTrace)))
                      .Append("\",\"output\":\"").Append(Esc(Trim(r.Output))).Append("\"}");
                }
                sb.Append("],\"pass\":").Append(pass).Append(",\"fail\":").Append(fail).Append(",\"skip\":").Append(skip).Append('}');
                AgentBridge.Write("tests.json", sb.ToString());
            }

            static string Trim(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 1500 ? s.Substring(0, 1500) : s);

            static void Collect(ITestResultAdaptor r, List<ITestResultAdaptor> into)
            {
                if (!r.HasChildren) { into.Add(r); return; }
                foreach (var c in r.Children) Collect(c, into);
            }

            static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", " ");
        }
    }
}
