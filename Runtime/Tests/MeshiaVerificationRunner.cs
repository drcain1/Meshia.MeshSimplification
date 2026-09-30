using System;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Meshia.MeshSimplification.Tests
{
    /// <summary>Allows editor automation to run the package suites and collect NUnit XML without blocking the editor.</summary>
    public static class MeshiaVerificationRunner
    {
        static TestRunnerApi api;
        const string StatusKey = "Meshia.Verification.Status";
        public static string Status => SessionState.GetString(StatusKey, "Idle");

        public static string Run(string resultPath, string nameFilter = null)
        {
            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Compile the current source successfully before running verification.");
            if (api != null) throw new InvalidOperationException("A Meshia test run is already active.");
            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks(resultPath));
            SessionState.SetString(StatusKey, "Starting");
            return api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                assemblyNames = new[] { "Meshia.MeshSimplification.Runtime.Tests", "Meshia.MeshSimplification.Ndmf.Editor.Tests" },
                testNames = string.IsNullOrEmpty(nameFilter) ? null : new[] { nameFilter },
            }));
        }

        sealed class Callbacks : ICallbacks
        {
            readonly string path;
            public Callbacks(string path) { this.path = path; }
            public void RunStarted(ITestAdaptor testsToRun) => SessionState.SetString(StatusKey, "Running");
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                try
                {
                    TestRunnerApi.SaveResultToFile(result, path);
                    SessionState.SetString(StatusKey, $"{result.ResultState}: {result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped; {path}");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(api);
                    api = null;
                }
            }
        }
    }
}
