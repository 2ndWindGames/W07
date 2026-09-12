#if UNITY_STANDALONE_WIN
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace RunBeat
{
    // Runs only on explicit QA launch; no Android player code or user data mutations.
    public sealed class RunBeatPlayerSmoke : MonoBehaviour
    {
        readonly List<string> errors = new List<string>();
        string output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args, "-runbeat-smoke");
            if (flag >= 0 && flag + 1 < args.Length)
                new GameObject("Player smoke check").AddComponent<RunBeatPlayerSmoke>().output = args[flag + 1];
        }
        void Awake() { Application.logMessageReceived += Log; }
        void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
        }
        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(4);
            var app = FindFirstObjectByType<RunBeatApp>();
            var doc = app ? app.GetComponent<UIDocument>() : null;
            // Render the real panel offscreen so QA also works in a hidden player.
            var target = RenderTexture.GetTemporary(390,844,0,RenderTextureFormat.ARGB32);
            if (doc) doc.panelSettings.targetTexture = target;
            yield return new WaitForSecondsRealtime(1);
            var start = doc ? doc.rootVisualElement.Q<Button>("start") : null;
            bool ready = app && app.Engine != null && start != null && start.worldBound.width > 100 && start.worldBound.height > 30;
            Directory.CreateDirectory(output);
            yield return new WaitForEndOfFrame();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(390,844,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,390,844),0,0); texture.Apply(); RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(output,"home.png"),texture.EncodeToPNG()); Destroy(texture);
            if (doc) doc.panelSettings.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            File.WriteAllText(Path.Combine(output,"result.txt"), (ready && errors.Count == 0 ? "PASS" : "FAIL") +
                " built player startup; home/start control visible=" + ready + "\n" + string.Join("\n",errors));
            Application.Quit(ready && errors.Count == 0 ? 0 : 1);
        }
        void OnDestroy() { Application.logMessageReceived -= Log; }
    }
}
#endif
