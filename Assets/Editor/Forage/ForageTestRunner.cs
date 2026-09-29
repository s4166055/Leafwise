using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Forage.EditorTools
{
    /// <summary>
    /// Runs the Unity Test Framework suites and writes a plain-text report to
    /// Temp/, so a test run can be driven and read from outside the editor
    /// (a terminal, CI, or an agent) without the Test Runner window.
    ///
    /// Menu: Forage ▸ Test ▸ Run EditMode Tests / Run PlayMode Tests.
    ///
    /// Callbacks are re-registered on every domain reload via the static
    /// constructor, which is what lets a PlayMode run (which reloads the
    /// domain on enter and exit) still deliver RunFinished to us.
    /// </summary>
    [InitializeOnLoad]
    public static class ForageTestRunner
    {
        static readonly TestRunnerApi Api;

        static ForageTestRunner()
        {
            Api = ScriptableObject.CreateInstance<TestRunnerApi>();
            Api.RegisterCallbacks(new Callbacks());
        }

        static string ResultPath(TestMode mode) => $"Temp/test-results-{mode}.txt";
        static string DonePath(TestMode mode) => $"Temp/test-results-{mode}.done";

        [MenuItem("Forage/Test/Run EditMode Tests", priority = 210)]
        public static void RunEditMode() => Run(TestMode.EditMode);

        [MenuItem("Forage/Test/Run PlayMode Tests", priority = 211)]
        public static void RunPlayMode() => Run(TestMode.PlayMode);

        public static void Run(TestMode mode)
        {
            Directory.CreateDirectory("Temp");
            File.Delete(ResultPath(mode));
            File.Delete(DonePath(mode));

            var filter = new Filter
            {
                testMode = mode,
                assemblyNames = new[] { mode == TestMode.EditMode ? "Forage.Tests.EditMode" : "Forage.Tests.PlayMode" }
            };
            Debug.Log($"[Tests] starting {mode} run for {filter.assemblyNames[0]}");
            Api.Execute(new ExecutionSettings(filter));
        }

        sealed class Callbacks : ICallbacks
        {
            readonly StringBuilder _sb = new StringBuilder();
            int _pass, _fail, _skip;
            TestMode _mode;

            public void RunStarted(ITestAdaptor testsToRun)
            {
                _sb.Clear();
                _pass = _fail = _skip = 0;
                _mode = testsToRun.TestMode;
                _sb.AppendLine($"Forage {_mode} tests");
                _sb.AppendLine("run: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                _sb.AppendLine();
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.Test.IsSuite) return;
                string status;
                switch (result.TestStatus)
                {
                    case TestStatus.Passed: status = "PASS"; _pass++; break;
                    case TestStatus.Failed: status = "FAIL"; _fail++; break;
                    default: status = "SKIP"; _skip++; break;
                }
                _sb.AppendLine($"{status}  {result.Test.FullName}  ({result.Duration:F2}s)");
                if (result.TestStatus == TestStatus.Failed && !string.IsNullOrEmpty(result.Message))
                    foreach (var line in result.Message.Trim().Split('\n'))
                        _sb.AppendLine("        " + line.TrimEnd());
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                _sb.AppendLine();
                _sb.AppendLine($"RESULT: {_pass} passed, {_fail} failed, {_skip} skipped");
                Directory.CreateDirectory("Temp");
                File.WriteAllText(ResultPath(_mode), _sb.ToString());
                File.WriteAllText(DonePath(_mode), _fail == 0 ? "passed" : "failed");
                if (_fail == 0) Debug.Log($"[Tests] {_mode}: all {_pass} passed.");
                else Debug.LogError($"[Tests] {_mode}: {_fail} failed - see {ResultPath(_mode)}");
            }
        }
    }
}
