using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace PulseArena.Editor
{
    public static class PackageInstaller
    {
        static AddAndRemoveRequest request;
        static double deadline;
        public static void Install()
        {
            request = Client.AddAndRemove(new[] { "com.unity.netcode.gameobjects", "com.unity.services.multiplayer" });
            deadline = EditorApplication.timeSinceStartup + 600;
            EditorApplication.update += Poll;
        }
        static void Poll()
        {
            if (!request.IsCompleted && EditorApplication.timeSinceStartup < deadline) return;
            EditorApplication.update -= Poll;
            if (request.IsCompleted && request.Status == StatusCode.Success)
            { Debug.Log("PULSE_PACKAGES_OK"); EditorApplication.Exit(0); }
            else { Debug.LogError(request.Error?.message ?? "Package installation timed out"); EditorApplication.Exit(1); }
        }
    }
}
