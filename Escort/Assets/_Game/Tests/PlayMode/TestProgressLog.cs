using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(typeof(HS.Tests.TestProgressLog))]

namespace HS.Tests
{
    /// <summary>One log line per test start and finish, so a headless run (tools/unity-tests.sh) shows where it is.</summary>
    public sealed class TestProgressLog : ITestRunCallback
    {
        public void RunStarted(ITest testsToRun) { }
        public void RunFinished(ITestResult testResults) { }

        public void TestStarted(ITest test)
        {
            if (!test.IsSuite) Debug.Log($"[Test] start {test.FullName} t={Time.realtimeSinceStartup:0.0}");
        }

        public void TestFinished(ITestResult result)
        {
            if (!result.Test.IsSuite) Debug.Log($"[Test] {result.ResultState.Status} {result.Test.FullName} ({result.Duration:0.0}s)");
        }
    }
}
