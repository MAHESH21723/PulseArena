using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PulseArena.Editor
{
    // Runs PulseSmoke tests inside the (signed) Unity Editor's Play Mode instead of a Windows
    // player build. Needed on PCs where Smart App Control blocks unsigned game executables.
    //
    // Usage (no -batchmode, Play Mode needs the editor UI):
    //   Unity.exe -projectPath <p> --burst-disable-compilation -executeMethod PulseArena.Editor.PulseEditorTests.Run
    //             -pulse-test practice|screens -test-output <dir>
    // The editor exits with 0 on PASS, 2 on FAIL, 3 on timeout. PulseSmoke reads the same command-line arguments.
    [InitializeOnLoad]
    public static class PulseEditorTests
    {
        const string ActiveKey = "PulseEditorTests.Active", DeadlineKey = "PulseEditorTests.Deadline";
        const string ScenePath = "Assets/PulseArena/Scenes/PulseArena.unity";

        static PulseEditorTests()
        {
            if (SessionState.GetBool(ActiveKey, false)) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            string mode = PulseSmoke.Argument("-pulse-test"), dir = PulseSmoke.Argument("-test-output");
            if (string.IsNullOrEmpty(mode) || string.IsNullOrEmpty(dir)) { Debug.LogError("PULSE_EDITOR_TEST needs -pulse-test and -test-output"); EditorApplication.Exit(2); return; }
            string result = Path.Combine(dir, mode + ".txt");
            if (File.Exists(result)) File.Delete(result);
            EditorSceneManager.OpenScene(ScenePath);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetFloat(DeadlineKey, (float)EditorApplication.timeSinceStartup + (mode == "showcase" ? 1500f : 240f));
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static double passedAt = -1;

        static void Tick()
        {
            string mode = PulseSmoke.Argument("-pulse-test"), dir = PulseSmoke.Argument("-test-output");
            string result = Path.Combine(dir ?? "", (mode ?? "") + ".txt");
            double now = EditorApplication.timeSinceStartup;
            if (File.Exists(result))
            {
                if (passedAt < 0) passedAt = now;
                if (now - passedAt < 4) return; // let the test finish its last captures and leave the match
                string text = File.ReadAllText(result);
                Debug.Log("PULSE_EDITOR_TEST " + text);
                Finish(text.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 2);
            }
            else if (now > SessionState.GetFloat(DeadlineKey, float.MaxValue))
            {
                Debug.LogError("PULSE_EDITOR_TEST TIMEOUT");
                Finish(3);
            }
        }

        static void Finish(int code)
        {
            SessionState.EraseBool(ActiveKey);
            EditorApplication.update -= Tick;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            EditorApplication.Exit(code);
        }
    }
}
