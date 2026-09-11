using RunBeat;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class RunBeatStoreAssets
{
    static double deadline;
    static RunBeatStoreAssets()
    {
        if (!SessionState.GetBool("RunBeat.StoreCapturePending", false)) return;
        deadline = EditorApplication.timeSinceStartup + 30;
        EditorApplication.update += AwaitPlay;
    }
    [MenuItem("RunBeat/Capture Google Play Screenshots")]
    public static void Begin()
    {
        if (!EditorApplication.isPlaying) RunBeatBuild.Setup();
        RunBeatBuild.SetGameSize(1080, 1920);
        SessionState.SetBool("RunBeat.StoreCapturePending", true);
        deadline = EditorApplication.timeSinceStartup + 30;
        EditorApplication.update -= AwaitPlay;
        EditorApplication.update += AwaitPlay;
        EditorApplication.isPlaying = true;
    }
    static void AwaitPlay()
    {
        if (EditorApplication.timeSinceStartup > deadline)
        {
            EditorApplication.update -= AwaitPlay;
            SessionState.SetBool("RunBeat.StoreCapturePending", false);
            Debug.LogError("Store capture could not enter Play mode.");
            return;
        }
        var app = Object.FindFirstObjectByType<RunBeatApp>();
        if (!EditorApplication.isPlaying || !app || app.Engine == null || app.Settings == null) return;
        EditorApplication.update -= AwaitPlay;
        SessionState.SetBool("RunBeat.StoreCapturePending", false);
        app.gameObject.AddComponent<RunBeatStoreCapture>();
    }
}
