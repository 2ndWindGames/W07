using System;
using System.IO;
using UnityEngine;

namespace RunBeat
{
    // Editor/desktop backend. Android uses its own foreground AudioTrack service.
    public sealed class LocalRunEngine : IRunEngine
    {
        readonly string path;
        readonly AudioSource[] voices;
        readonly AudioClip[] sounds;
        int voice, pendingTarget;
        double resumeTime, countdownEnd, nextBeat, nextCheckpoint, pendingAt;
        long baseMs;
        public EngineState State { get; private set; }
        public LocalRunEngine(GameObject owner, string storagePath = null)
        {
            path = storagePath ?? Path.Combine(Application.persistentDataPath, "runbeat-sessions.json");
            State = AtomicJson.Read(path, () => new EngineState());
            if (State.current == null) State.current = new Session();
            if (State.current.IsOpen)
            {
                State.current.status = "interrupted";
                State.current.reason = "앱이 종료되어 마지막 저장 지점까지 복구했어요.";
                Finish();
            }
            voices = new AudioSource[8];
            for (int i = 0; i < voices.Length; i++) { voices[i] = owner.AddComponent<AudioSource>(); voices[i].playOnAwake = false; }
            sounds = new[] { MakeSound(0), MakeSound(1), MakeSound(2) };
        }
        public static double Interval(int target, int steps) => 60.0 * steps / target;
        public static AudioClip MakeSound(int sound)
        {
            const int rate = 48000;
            var data = new float[2400];
            for (int i = 0; i < data.Length; i++)
            {
                double t = (double)i / rate;
                double attack = Math.Min(1, t / .0015);
                double wave = sound == 0 ? Math.Sin(2 * Math.PI * 880 * t) * Math.Exp(-t * 110) :
                    sound == 1 ? (Math.Sin(2 * Math.PI * 1500 * t) + .4 * Math.Sin(2 * Math.PI * 2310 * t)) * Math.Exp(-t * 170) :
                    Math.Sin(2 * Math.PI * (1200 * t + 8 * (1 - Math.Exp(-t * 100)))) * Math.Exp(-t * 90);
                data[i] = (float)(wave * attack * .48);
            }
            var clip = AudioClip.Create("RunBeat original tone " + sound, data.Length, 1, rate, false);
            clip.SetData(data, 0); return clip;
        }
        void Persist()
        {
            try { AtomicJson.Write(path, State); State.error = ""; }
            catch (Exception ex) { State.error = "기록을 저장하지 못했어요. 저장 공간을 확인해 주세요."; Debug.LogException(ex); }
        }
        public void Start(Preset p)
        {
            if (State.current.IsOpen) return;
            p.Validate();
            State.current = new Session { id = Guid.NewGuid().ToString("N"), status = "countdown", startedAt = DateTime.UtcNow.ToString("o"), targetSpm = p.targetSpm, beatEverySteps = p.beatEverySteps, soundId = p.soundId, durationSec = p.durationSec, volume = p.volume };
            State.current.segments.Add(new Segment { targetSpm = p.targetSpm });
            countdownEnd = AudioSettings.dspTime + 3; pendingTarget = 0; Persist();
        }
        public void Resume()
        {
            if (State.current.status != "paused") return;
            BeginAudio(); Persist();
        }
        void BeginAudio()
        {
            var s = State.current; s.status = "running"; s.reason = "";
            baseMs = s.activeMs; resumeTime = AudioSettings.dspTime + .08;
            nextBeat = resumeTime; nextCheckpoint = resumeTime + 10;
        }
        public void Tick()
        {
            double now = AudioSettings.dspTime; var s = State.current;
            if (s.status == "countdown" && now >= countdownEnd) BeginAudio();
            if (s.status != "running") return;
            s.activeMs = baseMs + (long)(Math.Max(0, now - resumeTime) * 1000);
            if (pendingTarget != 0 && pendingAt > 0 && now >= pendingAt)
            {
                CloseSegment(); s.targetSpm = pendingTarget;
                s.segments.Add(new Segment { targetSpm = pendingTarget, startActiveMs = s.activeMs });
                pendingTarget = 0; pendingAt = 0; Persist();
            }
            if (s.durationSec > 0 && s.activeMs >= s.durationSec * 1000L)
            {
                s.activeMs = s.durationSec * 1000L; StopVoices(); s.status = "completed"; Finish(); return;
            }
            if (nextBeat < now - .1) { Pause("오디오 출력이 중단되었어요. 준비되면 다시 시작하세요."); return; }
            while (nextBeat < now + .12)
            {
                if (s.durationSec > 0 && baseMs + (nextBeat - resumeTime) * 1000 >= s.durationSec * 1000L) break;
                var source = voices[voice++ % voices.Length]; source.clip = sounds[s.soundId]; source.volume = s.volume;
                source.PlayScheduled(nextBeat);
                int target = s.targetSpm;
                if (pendingTarget != 0) { if (pendingAt == 0) pendingAt = nextBeat; target = pendingTarget; }
                nextBeat += Interval(target, s.beatEverySteps);
            }
            if (now >= nextCheckpoint) { nextCheckpoint = now + 10; Persist(); }
        }
        public void Pause(string reason = "사용자 일시정지")
        {
            var s = State.current; if (s.status != "running" && s.status != "countdown") return;
            if (s.status == "running") s.activeMs = baseMs + (long)(Math.Max(0, AudioSettings.dspTime - resumeTime) * 1000);
            StopVoices(); s.status = "paused"; s.reason = reason;
            pendingAt = 0;
            if (pendingTarget != 0) { ChangePausedTarget(pendingTarget); pendingTarget = 0; }
            Persist();
        }
        public void Stop()
        {
            if (!State.current.IsOpen) return;
            Pause(); State.current.status = "stopped"; Finish();
        }
        void CloseSegment() { var s = State.current; if (s.segments.Count > 0) s.segments[s.segments.Count - 1].endActiveMs = s.activeMs; }
        void Finish()
        {
            CloseSegment(); State.current.endedAt = DateTime.UtcNow.ToString("o");
            if (!string.IsNullOrEmpty(State.current.id) && !State.history.Exists(x => x.id == State.current.id)) State.history.Insert(0, State.current);
            Persist();
        }
        void ChangePausedTarget(int target)
        {
            var s = State.current; CloseSegment(); s.targetSpm = target;
            s.segments.Add(new Segment { targetSpm = target, startActiveMs = s.activeMs });
        }
        public void SetTarget(int target)
        {
            target = Mathf.Clamp(target, 100, 220);
            if (State.current.status == "running") pendingTarget = target;
            else if (State.current.IsOpen) { ChangePausedTarget(target); Persist(); }
        }
        public void DeleteHistory(string id) { State.history.RemoveAll(x => x.id == id); if (State.current.id == id && !State.current.IsOpen) State.current = new Session(); Persist(); }
        public void ClearHistory() { State.history.Clear(); if (!State.current.IsOpen) State.current = new Session(); Persist(); }
        void StopVoices() { foreach (var v in voices) if (v) v.Stop(); }
        public void Dispose() { Pause("앱이 백그라운드로 이동했어요."); StopVoices(); foreach (var clip in sounds) UnityEngine.Object.Destroy(clip); }
    }
}
