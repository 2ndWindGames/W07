#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RunBeat;
using UnityEngine;

// Store assets use the real app UI with an isolated, in-memory example data source.
// This Editor-only code never writes session or preference data.
public sealed class RunBeatStoreCapture : MonoBehaviour
{
    const string Destination = "StoreAssets/GooglePlay/screenshots/ko-KR";
    IEnumerator Start()
    {
        Directory.CreateDirectory(Destination);
        var app = GetComponent<RunBeatApp>();
        if (app.Engine.State.current.IsOpen) throw new InvalidOperationException("Finish the active Editor workout before store capture.");
        var originalEngine = app.Engine;
        var originalSettings = app.Settings;
        bool originalEnabled = app.enabled;
        string originalPage = app.CurrentPage;
        var preview = new PreviewEngine();
        try
        {
            app.enabled = false;
            SetProperty(app, "Engine", preview);
            SetProperty(app, "Settings", new Preset { targetSpm = 180, durationSec = 1200 });
            app.Navigate("home");
            yield return Capture("01-home");

            preview.State.current = Example("store-running", "running", "2026-09-11T09:00:00+09:00", 754000, 180);
            app.Navigate("run");
            yield return Capture("02-running");

            preview.State.current = new Session();
            app.Navigate("home");
            Invoke(app, "ShowSound");
            yield return Capture("03-sound");

            app.Navigate("home");
            Invoke(app, "ShowDuration");
            yield return Capture("04-duration");

            app.Navigate("music");
            yield return Capture("05-music");

            preview.State.history.Add(Example("store-history-1", "completed", "2026-09-11T07:10:00+09:00", 1200000, 180));
            preview.State.history.Add(Example("store-history-2", "stopped", "2026-09-10T18:30:00+09:00", 1110000, 175));
            preview.State.history.Add(Example("store-history-3", "stopped", "2026-09-09T07:30:00+09:00", 735000, 170));
            app.Navigate("history");
            yield return Capture("06-history");

            app.ShowSession(preview.State.history[0], "history");
            yield return Capture("07-session-detail");
            File.WriteAllText("StoreAssets/capture-result.txt", "PASS: 7 actual UI renders, 1080x1920 RGB PNG. In-memory example workout data. Original user data unchanged.\n" + DateTime.UtcNow.ToString("o"));
        }
        finally
        {
            SetProperty(app, "Engine", originalEngine);
            SetProperty(app, "Settings", originalSettings);
            app.enabled = originalEnabled;
            app.Navigate(originalPage == "detail" ? "home" : originalPage);
            Destroy(this);
        }
    }
    static IEnumerator Capture(string name)
    {
        yield return new WaitForSecondsRealtime(.8f);
        yield return new WaitForEndOfFrame();
        var screen = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            if (screen.width != 1080 || screen.height != 1920) throw new InvalidOperationException("Store screenshot resolution mismatch: " + screen.width + "x" + screen.height);
            var rgb = new Texture2D(screen.width, screen.height, TextureFormat.RGB24, false);
            try
            {
                rgb.SetPixels32(screen.GetPixels32()); rgb.Apply();
                File.WriteAllBytes(Path.Combine(Destination, name + ".png"), ImageConversion.EncodeToPNG(rgb));
            }
            finally { Destroy(rgb); }
        }
        finally { Destroy(screen); }
    }
    static Session Example(string id, string status, string startedAt, long activeMs, int target)
    {
        var result = new Session { id = id, status = status, startedAt = startedAt, activeMs = activeMs, targetSpm = target, durationSec = 1200 };
        result.segments = new List<Segment> {
            new Segment { targetSpm = target - 10, startActiveMs = 0, endActiveMs = 240000 },
            new Segment { targetSpm = target - 5, startActiveMs = 240000, endActiveMs = 600000 },
            new Segment { targetSpm = target, startActiveMs = 600000, endActiveMs = activeMs }
        };
        return result;
    }
    static void SetProperty(RunBeatApp app, string name, object value) => typeof(RunBeatApp).GetProperty(name).SetValue(app, value);
    static void Invoke(RunBeatApp app, string method) => typeof(RunBeatApp).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(app, null);
    sealed class PreviewEngine : IRunEngine
    {
        public EngineState State { get; } = new EngineState();
        public void Tick() { }
        public void Dispose() { }
        public void Start(Preset preset) => throw new NotSupportedException();
        public void Pause(string reason = "사용자 일시정지") => throw new NotSupportedException();
        public void Resume() => throw new NotSupportedException();
        public void Stop() => throw new NotSupportedException();
        public void SetTarget(int target) => throw new NotSupportedException();
        public void DeleteHistory(string id) => throw new NotSupportedException();
        public void ClearHistory() => throw new NotSupportedException();
    }
}
#endif
