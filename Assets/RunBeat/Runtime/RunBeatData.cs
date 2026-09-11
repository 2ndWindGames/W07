using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RunBeat
{
    [Serializable] public sealed class Preset
    {
        public int targetSpm = 180, beatEverySteps = 1, soundId = 0, durationSec = 1200;
        public float volume = .65f;
        public bool keepScreenOn, largeText;
        public List<string> favorites = new List<string>();
        public void Validate()
        {
            targetSpm = Mathf.Clamp(targetSpm, 100, 220);
            beatEverySteps = beatEverySteps == 2 ? 2 : 1;
            soundId = Mathf.Clamp(soundId, 0, 2);
            if (durationSec != 0 && durationSec != 600 && durationSec != 1200 && durationSec != 1800) durationSec = 1200;
            volume = float.IsNaN(volume) ? .65f : Mathf.Clamp01(volume);
            favorites = favorites ?? new List<string>();
        }
    }
    [Serializable] public sealed class Segment
    {
        public int targetSpm;
        public long startActiveMs, endActiveMs;
    }
    [Serializable] public sealed class Session
    {
        public string id, status = "idle", startedAt, endedAt, reason = "";
        public long activeMs;
        public int targetSpm = 180, beatEverySteps = 1, soundId, durationSec = 1200;
        public float volume = .65f;
        public List<Segment> segments = new List<Segment>();
        public bool IsOpen => status == "running" || status == "paused" || status == "countdown";
    }
    [Serializable] public sealed class EngineState
    {
        public int version = 1;
        public Session current = new Session();
        public List<Session> history = new List<Session>();
        public string error = "";
    }
    [Serializable] public sealed class MusicTrack
    {
        public string videoId, title, genre, sourceUrl, verifiedAt, confidence, cover;
        public int bpm;
        public bool tempoVariable;
        public int MatchingSteps(int target) => Math.Abs(bpm - target) <= Math.Abs(bpm * 2 - target) ? 1 : 2;
        public int Difference(int target) => Math.Min(Math.Abs(bpm - target), Math.Abs(bpm * 2 - target));
    }
    [Serializable] public sealed class MusicCatalog { public List<MusicTrack> tracks = new List<MusicTrack>(); }

    public static class AtomicJson
    {
        public static T Read<T>(string path, Func<T> fallback) where T : class
        {
            foreach (var candidate in new[] { path, path + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                try { var value = JsonUtility.FromJson<T>(File.ReadAllText(candidate)); if (value != null) return value; }
                catch (Exception ex) { Debug.LogWarning("RunBeat: 저장 파일 복구 시도: " + ex.Message); }
            }
            return fallback();
        }
        public static void Write<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(JsonUtility.ToJson(value, true)); writer.Flush(); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
    }

    public interface IRunEngine : IDisposable
    {
        EngineState State { get; }
        void Start(Preset preset);
        void Pause(string reason = "사용자 일시정지");
        void Resume();
        void Stop();
        void SetTarget(int target);
        void Tick();
        void DeleteHistory(string id);
        void ClearHistory();
    }
}
