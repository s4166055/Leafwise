using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Forage.Testing;

namespace Forage.EditorTools
{
    /// <summary>
    /// Runs the world invariants from the editor and writes a report to
    /// Temp/forage-selftest-results.txt.
    ///
    /// The forest is built at runtime in ForestGenerator.Awake(), so it can only
    /// be inspected in play mode. This harness drives the editor through
    /// open scene -> enter play -> settle -> assert -> exit play. The checks
    /// themselves live in <see cref="WorldInvariants"/>, shared with the Unity
    /// Test Framework PlayMode suite, so there is one definition of "healthy".
    ///
    /// It resumes across the domain reloads that play mode causes by keeping its
    /// phase in SessionState, so it can also be kicked off from outside the
    /// editor by dropping a Temp/forage-selftest.request file.
    /// </summary>
    [InitializeOnLoad]
    public static class ForageSelfTest
    {
        const string ScenePath = "Assets/Scenes/Forage.unity";
        const string PhaseKey = "Forage.SelfTest.Phase";
        const string RequestFile = "Temp/forage-selftest.request";
        const string ResultFile = "Temp/forage-selftest-results.txt";

        /// <summary>Simulated seconds the world gets before assertions run.</summary>
        const float SettleGameSeconds = 6f;

        static ForageSelfTest()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        [MenuItem("Forage/Test/Run Forest Self-Test", priority = 200)]
        public static void Request()
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(RequestFile, "go");
            SessionState.SetString(PhaseKey, "");
            Debug.Log("[SelfTest] queued - the forest test will run on the next editor tick.");
        }

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            string phase = SessionState.GetString(PhaseKey, "");

            switch (phase)
            {
                case "":
                    if (!File.Exists(RequestFile)) return;
                    File.Delete(RequestFile);
                    if (File.Exists(ResultFile)) File.Delete(ResultFile);
                    Debug.Log("[SelfTest] opening " + ScenePath);
                    EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    SessionState.SetString(PhaseKey, "enter");
                    return;

                case "enter":
                    if (EditorApplication.isPlaying)
                    {
                        SessionState.SetString(PhaseKey, "settle");
                        return;
                    }
                    if (!EditorApplication.isPlayingOrWillChangePlaymode)
                        EditorApplication.EnterPlaymode();
                    return;

                case "settle":
                    if (!EditorApplication.isPlaying) { SessionState.SetString(PhaseKey, ""); return; }

                    // Drive the simulation by hand. An unfocused editor throttles
                    // (often halts) the player loop, so Awake/Start run but nothing
                    // after the first yield does. Step() advances exactly one
                    // player frame per editor tick, and we settle on *simulated*
                    // time (Time.timeSinceLevelLoad) rather than wall-clock, so
                    // the amount of game the world has lived through is the same
                    // whether the window is focused or the machine is loaded.
                    //
                    // Step() pauses the editor as a side effect; that is undone
                    // below before leaving play mode, or the next manual Play
                    // would start frozen with the Pause toggle lit.
                    if (Time.timeSinceLevelLoad < SettleGameSeconds)
                    {
                        EditorApplication.Step();
                        return;
                    }

                    var report = WorldInvariants.Run();
                    Directory.CreateDirectory("Temp");
                    File.WriteAllText(ResultFile, report.ToText("Forage forest self-test"));
                    if (report.Failed == 0)
                        Debug.Log("[SelfTest] all " + report.Passed + " checks passed.");
                    else
                        Debug.LogError($"[SelfTest] {report.Failed} of {report.Passed + report.Failed} checks failed - see {ResultFile}");

                    EditorApplication.isPaused = false;
                    SessionState.SetString(PhaseKey, "done");
                    EditorApplication.ExitPlaymode();
                    return;

                case "done":
                    if (EditorApplication.isPlaying) return;
                    EditorApplication.isPaused = false;   // safety net
                    SessionState.SetString(PhaseKey, "");
                    Debug.Log("[SelfTest] finished. Report: " + ResultFile);
                    return;
            }
        }
    }
}
