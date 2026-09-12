using System;
using UnityEngine;

namespace RunBeat
{
    public sealed class AndroidRunEngine : IRunEngine
    {
        readonly AndroidJavaClass bridge;
        readonly AndroidJavaObject activity;
        public EngineState State { get; private set; } = new EngineState();
        public AndroidRunEngine()
        {
            try
            {
                using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")) activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
                bridge = new AndroidJavaClass("com.secondwindgames.runbeat.RunBeatService");
                bridge.CallStatic("initialize", activity);
                ReadState();
            }
            catch
            {
                bridge?.Dispose(); activity?.Dispose();
                throw;
            }
        }
        void Command(string action, string payload = "")
        {
            try { bridge.CallStatic("command", activity, action, payload); }
            catch (Exception ex) { State.error = "오디오를 시작하지 못했어요. 다시 시도해 주세요."; Debug.LogException(ex); }
        }
        public void Start(Preset preset) => Command("start", JsonUtility.ToJson(preset));
        public void Pause(string reason = "사용자 일시정지") => Command("pause", reason);
        public void Resume() => Command("resume");
        public void Stop() => Command("stop");
        public void SetTarget(int target) => Command("target", target.ToString());
        public void DeleteHistory(string id) => Command("delete", id);
        public void ClearHistory() => Command("clear");
        public void Tick()
        {
            try { ReadState(); }
            catch (Exception ex) { State.error = "운동 상태를 확인하지 못했어요."; Debug.LogWarning(ex.Message); }
        }
        void ReadState()
        {
            var state = JsonUtility.FromJson<EngineState>(bridge.CallStatic<string>("snapshot", activity));
            if (state == null || state.current == null || state.history == null) throw new InvalidOperationException("Invalid RunBeat engine snapshot.");
            State = state;
        }
        public bool OpenMusic(string videoId)
        {
            try { return bridge.CallStatic<bool>("openMusic", activity, videoId); }
            catch (Exception) { return false; }
        }
        public void NotificationSettings() => bridge.CallStatic("notificationSettings", activity);
        public void Dispose() { bridge.Dispose(); activity.Dispose(); }
    }
}
