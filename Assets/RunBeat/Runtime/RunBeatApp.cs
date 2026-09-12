using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RunBeat
{
    [DisallowMultipleComponent]
    public sealed class RunBeatApp : MonoBehaviour
    {
        static readonly Color Lime = new Color32(195, 255, 84, 255);
        static readonly string[] SoundNames = { "부드러운 전자음", "우드 클릭", "맑은 벨" };
        static readonly string[] SoundDescriptions = { "가볍고 부드럽게, 일정한 리듬", "짧고 또렷하게 들리는 나무 소리", "밝고 깨끗하게 울리는 소리" };
        public IRunEngine Engine { get; private set; }
        public Preset Settings { get; private set; }
        public string CurrentPage { get; private set; } = "home";
        string presetPath, lastStatus, lastSessionId, genre = "전체", musicReturn = "home", detailReturn = "history";
        bool favoritesOnly, initialized, pendingStart;
        float nextPoll, countdownStarted, previewUntil;
        double nextPreviewBeat;
        int previewVoice;
        AudioSource[] previews;
        AudioClip[] previewClips;
        PanelSettings panel;
        UnityEngine.TextCore.Text.FontAsset uiFont;
        UnityEngine.TextCore.Text.FontAsset numberFont;
        VisualElement root, safe, body, overlay;
        Label cadenceLabel, timerLabel, stateLabel, remainingLabel, reasonLabel;
        Button pauseButton, minusButton, plusButton;
        BeatRing ring;
        VisualElement progress;
        MusicCatalog catalog;
        readonly Dictionary<string, Texture2D> artwork = new Dictionary<string, Texture2D>();

        void Start()
        {
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;
            presetPath = Path.Combine(Application.persistentDataPath, "runbeat-preferences.json");
            Settings = AtomicJson.Read(presetPath, () => new Preset()); Settings.Validate();
            var resource = Resources.Load<TextAsset>("RunBeat/MusicCatalog");
            catalog = resource ? JsonUtility.FromJson<MusicCatalog>(resource.text) : new MusicCatalog();
            artwork["LimeFlow"] = Resources.Load<Texture2D>("RunBeat/LimeFlow");
            artwork["Sunset"] = Resources.Load<Texture2D>("RunBeat/Sunset");
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize; panel.referenceResolution = new Vector2Int(390, 844);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; panel.match = 0;
            panel.sortingOrder = 10;
            var theme = Resources.Load<ThemeStyleSheet>("RunBeat/RunBeatTheme"); if (theme) panel.themeStyleSheet = theme;
            var document = gameObject.AddComponent<UIDocument>(); document.panelSettings = panel;
            root = document.rootVisualElement; root.AddToClassList("app");
            root.styleSheets.Add(Resources.Load<StyleSheet>("RunBeat/RunBeat"));
            uiFont = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(Resources.Load<Font>("RunBeat/NotoSansKR"), 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048, UnityEngine.TextCore.Text.AtlasPopulationMode.Dynamic, true);
            root.style.unityFontDefinition = FontDefinition.FromSDFFont(uiFont);
            var numericSource = Resources.Load<Font>("RunBeat/NotoSans-Black");
            if (numericSource) numberFont = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(numericSource,144,12,UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,1024,1024,UnityEngine.TextCore.Text.AtlasPopulationMode.Dynamic,true);
            root.RegisterCallback<GeometryChangedEvent>(_ => ApplySafeArea());
            previewClips = new[] { LocalRunEngine.MakeSound(0), LocalRunEngine.MakeSound(1), LocalRunEngine.MakeSound(2) };
            previews = new AudioSource[4];
            for (int i = 0; i < previews.Length; i++) { previews[i] = gameObject.AddComponent<AudioSource>(); previews[i].playOnAwake = false; }
            InitializeEngine();
        }
        void InitializeEngine()
        {
            try
            {
                if (Application.platform == RuntimePlatform.Android && !Application.isEditor) Engine = new AndroidRunEngine();
                else Engine = new LocalRunEngine(gameObject);
            }
            catch (Exception ex)
            {
                // Keep a visible, actionable screen if the native bridge cannot start.
                Debug.LogException(ex);
                root.Clear(); safe = Element(root, "safe"); ApplySafeArea();
                var error = Element(safe, "card");
                Label(error, "앱을 시작하지 못했어요", "empty-title");
                Label(error, "오디오 기능을 준비하지 못했어요.\n다시 시도하거나 앱을 최신 버전으로 업데이트해 주세요.", "empty-copy");
                ActionButton(error, "다시 시도", "play", InitializeEngine, "retry-startup", true);
                return;
            }
            initialized = true;
            lastStatus = Engine.State.current.status; lastSessionId = Engine.State.current.id;
            Navigate(Engine.State.current.IsOpen ? "run" : "home");
            if (Engine.State.current.status == "interrupted") ShowRecovery();
        }
        void ApplySafeArea()
        {
            if (safe == null || Screen.width == 0) return;
            float ratio = 390f / Screen.width; Rect area = Screen.safeArea;
            safe.style.paddingTop = Mathf.Max(12, (Screen.height - area.yMax) * ratio);
            safe.style.paddingBottom = Mathf.Max(8, area.yMin * ratio);
            root.EnableInClassList("compact", area.height / Screen.width < 2.05f);
            root.EnableInClassList("large", Settings.largeText);
        }
        bool SaveSettings()
        {
            try { Settings.Validate(); AtomicJson.Write(presetPath, Settings); ApplyPreferences(); return true; }
            catch (Exception ex) { Debug.LogWarning(ex.Message); Toast("설정을 저장하지 못했어요. 저장 공간을 확인해 주세요."); return false; }
        }
        void ApplyPreferences()
        {
            root?.EnableInClassList("large", Settings.largeText);
            Screen.sleepTimeout = Settings.keepScreenOn && Engine.State.current.IsOpen ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
        }
        void Update()
        {
            if (!initialized) return;
            if (previewUntil > Time.unscaledTime && AudioSettings.dspTime + .12 > nextPreviewBeat)
            {
                var source = previews[previewVoice++ % previews.Length]; source.clip = previewClips[Settings.soundId]; source.volume = Settings.volume;
                nextPreviewBeat = Math.Max(nextPreviewBeat, AudioSettings.dspTime + .025); source.PlayScheduled(nextPreviewBeat);
                nextPreviewBeat += LocalRunEngine.Interval(Settings.targetSpm, Settings.beatEverySteps);
            }
            if (Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + .08f; Engine.Tick(); var s = Engine.State.current;
                if (s.status != lastStatus || s.id != lastSessionId)
                {
                    bool finished = !s.IsOpen && (lastStatus == "running" || lastStatus == "paused" || lastStatus == "countdown");
                    lastStatus = s.status; lastSessionId = s.id;
                    if (finished) { CloseSheet(); ShowSession(s, "home"); }
                    else if (CurrentPage == "run") RenderRun();
                    ApplyPreferences();
                }
                UpdateRun();
            }
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Back();
#else
            if (Input.GetKeyDown(KeyCode.Escape)) Back();
#endif
        }
        public void Navigate(string page)
        {
            CloseSheet(); StopPreview(); CurrentPage = page;
            if (page == "home") RenderHome();
            else if (page == "run") RenderRun();
            else if (page == "music") RenderMusic();
            else if (page == "history") RenderHistory();
            else if (page == "settings") RenderSettings();
        }
        void Shell(string title, bool home = false)
        {
            root.Clear(); overlay = null; cadenceLabel = timerLabel = stateLabel = remainingLabel = reasonLabel = null; ring = null; progress = null;
            safe = Element(root, "safe"); ApplySafeArea();
            var header = Element(safe, "header");
            if (home)
            {
                header.Add(new RunIcon("shoe", Lime, 34));
                Label(header, "러닝 메트로놈", "brand-label");
                IconButton(header, "settings", () => Navigate("settings"), "설정", "settings");
            }
            else
            {
                IconButton(header, "back", Back, "뒤로", "back");
                Label(header, title, "header-title").style.unityTextAlign = TextAnchor.MiddleCenter;
                Element(header, "icon-button");
            }
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "page-scroll", verticalScrollerVisibility = ScrollerVisibility.Hidden, horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("scroll"); scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped; safe.Add(scroll); body = scroll.contentContainer;
        }
        public void RenderHome()
        {
            Shell("", true);
            var target = Element(body, "card target-card");
            Label(target, "목표 케이던스", "target-title");
            cadenceLabel = Label(target, Settings.targetSpm.ToString(), "target-number", "cadence");
            Label(target, "step/min", "unit");
            var stepper = Element(target, "stepper");
            minusButton = IconButton(stepper, "minus", () => ChangeTarget(-1), "목표 케이던스 낮추기", "target-minus", "circle");
            minusButton.clickable = new Clickable(() => ChangeTarget(-1), 420, 85);
            Label(stepper, "100 – 220", "range-hint");
            plusButton = IconButton(stepper, "plus", () => ChangeTarget(1), "목표 케이던스 높이기", "target-plus", "circle");
            plusButton.clickable = new Clickable(() => ChangeTarget(1), 420, 85);
            UpdateTargetButtons();
            SettingRow("sound", "사운드", SoundNames[Settings.soundId], ShowSound, "sound");
            SettingRow("clock", "운동 시간", DurationText(Settings.durationSec), ShowDuration, "duration");
            var start = ActionButton(body, Engine.State.current.IsOpen ? "운동으로 돌아가기" : "달리기 시작", "run", BeginRun, "start", true);
            if (Engine.State.current.IsOpen) start.tooltip = "진행 중인 운동으로 돌아갑니다";
            ActionButton(body, "음악 찾기", "music", () => { musicReturn = "home"; Navigate("music"); }, "music", false);
            Button(body, "최근 운동 기록", () => Navigate("history"), "link", "history");
            Label(body, "RUNBEAT  /  FIND YOUR RHYTHM", "footer");
        }
        void UpdateTargetButtons()
        {
            minusButton?.SetEnabled(Settings.targetSpm > 100); plusButton?.SetEnabled(Settings.targetSpm < 220);
        }
        public void ChangeTarget(int delta)
        {
            Settings.targetSpm = Mathf.Clamp(Settings.targetSpm + delta, 100, 220); SaveSettings();
            if (cadenceLabel != null && CurrentPage == "home") cadenceLabel.text = Settings.targetSpm.ToString();
            if (Engine.State.current.IsOpen) Engine.SetTarget(Settings.targetSpm);
            UpdateTargetButtons();
        }
        void SettingRow(string icon, string caption, string value, Action action, string name)
        {
            var button = Button(body, "", action, "setting-row", name); button.Add(new RunIcon(icon, null, 27));
            var text = Element(button, "setting-text"); Label(text, caption, "caption"); Label(text, value, "value");
            button.Add(new RunIcon("chevron", new Color32(143,154,162,255), 20));
        }
        public void BeginRun()
        {
            if (Engine.State.current.IsOpen) { Navigate("run"); return; }
            if (pendingStart) return;
            StopPreview(); if (!SaveSettings()) return;
            pendingStart = true;
            if (Application.platform == RuntimePlatform.Android && !Application.isEditor)
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    if (version.GetStatic<int>("SDK_INT") >= 33 && !UnityEngine.Android.Permission.HasUserAuthorizedPermission("android.permission.POST_NOTIFICATIONS"))
                    {
                        var callbacks = new UnityEngine.Android.PermissionCallbacks();
                        callbacks.PermissionGranted += _ => StartAfterPermission();
                        callbacks.PermissionDenied += _ => StartAfterPermission();
                        UnityEngine.Android.Permission.RequestUserPermission("android.permission.POST_NOTIFICATIONS", callbacks); return;
                    }
                }
            }
            StartAfterPermission();
        }
        void StartAfterPermission()
        {
            if (!pendingStart) return; pendingStart = false; countdownStarted = Time.unscaledTime;
            Engine.Start(Settings); Engine.Tick(); Navigate("run"); ApplyPreferences();
        }
        public void RenderRun()
        {
            Shell("나의 리듬");
            Label(body, "일정한 리듬이\n더 멀리 데려다 줍니다.", "run-intro run-message");
            stateLabel = Label(body, "달리는 중", "status");
            var wrap = Element(body, "ring-wrap"); ring = new BeatRing(); ring.AddToClassList("ring"); wrap.Add(ring);
            cadenceLabel = Label(wrap, Engine.State.current.targetSpm.ToString(), "run-number", "run-cadence");
            Label(wrap, "step/min", "unit");
            var adjust = Element(body, "run-adjust");
            IconButton(adjust, "minus", () => ChangeTarget(-1), "목표 1 낮추기", "run-minus", "tiny-step");
            Label(adjust, "다음 박자부터 목표 변경", "adjust-label");
            IconButton(adjust, "plus", () => ChangeTarget(1), "목표 1 높이기", "run-plus", "tiny-step");
            var timer = Element(body, "card timer-card"); Label(timer, "운동 시간", "unit");
            timerLabel = Label(timer, "00:00", "timer", "timer"); remainingLabel = Label(timer, "", "caption");
            progress = Element(Element(timer, "progress-track"), "progress-fill");
            var controls = Element(safe, "run-controls");
            var pause = Element(controls, "control-stack");
            bool paused = Engine.State.current.status == "paused";
            pauseButton = IconButton(pause, paused ? "play" : "pause", TogglePause, paused ? "계속하기" : "일시정지", "pause", "circle control-circle");
            Label(pause, paused ? "계속하기" : "일시정지", "control-label");
            var stop = Element(controls, "control-stack");
            IconButton(stop, "stop", ConfirmStop, "운동 종료", "stop", "circle control-circle stop"); Label(stop, "종료", "control-label");
            reasonLabel = Label(safe, "", "notice");
            Button(safe, "음악 찾기", () => { musicReturn = "run"; Navigate("music"); }, "link", "run-music");
            UpdateRun();
        }
        void UpdateRun()
        {
            if (CurrentPage != "run" || cadenceLabel == null) return;
            var s = Engine.State.current;
            bool countdown = s.status == "countdown" || s.status == "idle";
            cadenceLabel.text = countdown ? Mathf.Clamp(3 - Mathf.FloorToInt(Time.unscaledTime - countdownStarted), 1, 3).ToString() : s.targetSpm.ToString();
            stateLabel.text = countdown ? "잠시 후 시작해요" : s.status == "paused" ? "일시정지" : "●  달리는 중";
            timerLabel.text = FormatTime(s.activeMs);
            remainingLabel.text = s.durationSec == 0 ? "자유 러닝" : FormatTime(Math.Max(0, s.durationSec * 1000L - s.activeMs)) + " 남음";
            reasonLabel.text = string.IsNullOrEmpty(Engine.State.error) ? s.reason : Engine.State.error;
            reasonLabel.style.display = string.IsNullOrEmpty(reasonLabel.text) || reasonLabel.text == "사용자 일시정지" ? DisplayStyle.None : DisplayStyle.Flex;
            progress.style.width = Length.Percent(s.durationSec == 0 ? 0 : Mathf.Clamp01((float)s.activeMs / (s.durationSec * 1000)) * 100);
            ring.playing = s.status == "running";
            ring.phase = (float)(s.activeMs / (LocalRunEngine.Interval(s.targetSpm, s.beatEverySteps) * 1000) % 12) / 12;
            ring.MarkDirtyRepaint();
        }
        public void TogglePause()
        {
            if (Engine.State.current.status == "paused") Engine.Resume(); else Engine.Pause();
            Engine.Tick(); RenderRun();
        }
        void ConfirmStop()
        {
            var sheet = Sheet("운동을 마칠까요?");
            Label(sheet, "지금까지 달린 시간과 목표 구간을 기기에 저장합니다.", "body-copy");
            ActionButton(sheet, "저장하고 종료", "check", () => { Engine.Stop(); CloseSheet(); Engine.Tick(); if (!Engine.State.current.IsOpen) ShowSession(Engine.State.current, "home"); }, "confirm-stop", true);
            Button(sheet, "계속 달리기", CloseSheet, "link");
        }
        void ShowSound()
        {
            var sheet = Sheet("나에게 맞는 소리");
            Label(sheet, "음색을 선택하고 박자를 미리 들어보세요.", "body-copy");
            for (int i = 0; i < SoundNames.Length; i++)
            {
                int index = i;
                var option = Button(sheet, "", () => { Settings.soundId = index; SaveSettings(); ShowSound(); PreviewSound(); }, "option" + (Settings.soundId == i ? " selected" : ""), "sound-" + i);
                var copy = Element(option, "grow"); Label(copy, SoundNames[i], "value"); Label(copy, SoundDescriptions[i], "caption");
                option.Add(new RunIcon(Settings.soundId == i ? "check" : "play", Settings.soundId == i ? Lime : Color.white, 21));
            }
            Label(sheet, "앱 음량", "section-title");
            var slider = new Slider(0, 1) { value = Settings.volume, name = "volume" }; slider.AddToClassList("slider");
            slider.RegisterValueChangedCallback(e => { Settings.volume = e.newValue; SaveSettings(); }); sheet.Add(slider);
            Label(sheet, "박자 간격", "section-title");
            var options = Element(sheet, "chip-row");
            Button(options, "매 걸음", () => { Settings.beatEverySteps = 1; SaveSettings(); ShowSound(); }, "chip" + (Settings.beatEverySteps == 1 ? " selected" : ""), "beat-one");
            Button(options, "두 걸음마다", () => { Settings.beatEverySteps = 2; SaveSettings(); ShowSound(); }, "chip" + (Settings.beatEverySteps == 2 ? " selected" : ""), "beat-two");
            Button(sheet, "소리 미리 듣기", PreviewSound, "secondary", "preview");
            Button(sheet, "설정 완료", () => { CloseSheet(); RenderHome(); }, "primary", "sound-done");
        }
        void PreviewSound()
        {
            if (Engine.State.current.IsOpen) { Toast("진행 중인 운동을 마친 뒤 미리 들을 수 있어요."); return; }
            StopPreview(); previewUntil = Time.unscaledTime + 3; nextPreviewBeat = AudioSettings.dspTime + .05;
        }
        void StopPreview() { previewUntil = 0; if (previews != null) foreach (var source in previews) source.Stop(); }
        void ShowDuration()
        {
            var sheet = Sheet("오늘은 얼마나 달릴까요?");
            Label(sheet, "설정한 시간이 되면 운동을 마치고 기록을 저장해요.", "body-copy");
            foreach (int duration in new[] { 600, 1200, 1800, 0 })
            {
                int value = duration; var option = Button(sheet, "", () => { Settings.durationSec = value; SaveSettings(); CloseSheet(); RenderHome(); }, "option" + (duration == Settings.durationSec ? " selected" : ""), "duration-" + value);
                Label(option, DurationText(value), "option-title"); if (duration == Settings.durationSec) option.Add(new RunIcon("check", Lime));
            }
        }
        public void RenderMusic()
        {
            Shell("음악 찾기");
            var target = Element(body, "card summary-target"); Label(target, "목표 케이던스", "muted grow");
            Label(target, Settings.targetSpm.ToString(), "music-target lime"); Label(target, "step/min", "muted");
            var filter = new ScrollView(ScrollViewMode.Horizontal) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, verticalScrollerVisibility = ScrollerVisibility.Hidden }; body.Add(filter);
            var chips = Element(filter.contentContainer, "chip-row");
            foreach (string category in new[] { "전체", "일렉트로닉", "힙합", "팝", "인디" })
            {
                string chosen = category; Button(chips, category, () => { genre = chosen; RenderMusic(); }, "chip" + (genre == category ? " selected" : ""), "genre-" + category);
            }
            Label(body, "러닝에 어울리는 리듬의 음악을 찾아보세요.", "muted");
            Button(body, favoritesOnly ? "♥  즐겨찾기만 보는 중" : "♡  즐겨찾기만 보기", () => { favoritesOnly = !favoritesOnly; RenderMusic(); }, "favorite", "favorites-filter");
            var pool = catalog.tracks.Where(x => (genre == "전체" || x.genre == genre) && (!favoritesOnly || Settings.favorites.Contains(x.videoId))).ToList();
            var matches = pool.Where(x => x.Difference(Settings.targetSpm) == 0).ToList();
            if (matches.Count == 0)
            {
                matches = pool.Where(x => x.Difference(Settings.targetSpm) <= 5).OrderBy(x => x.Difference(Settings.targetSpm)).ToList();
                if (matches.Count > 0) Label(body, "가까운 리듬 · 목표 ±5 step/min", "section-title");
            }
            foreach (var track in matches) MusicCard(track);
            if (matches.Count == 0) Empty(body, "music", favoritesOnly ? "아직 담아둔 음악이 없어요" : "이 목표에 맞는 음악을 찾고 있어요", favoritesOnly ? "마음에 드는 음악의 즐겨찾기를 눌러보세요." : "다른 장르나 160·170·180·190 목표로 찾아보세요.");
            var tip = Element(body, "card tip"); tip.Add(new RunIcon("bulb", Lime, 27));
            Label(tip, "YouTube를 열면 메트로놈은 일시정지해요. 음악에 맞춘 자동 동기화는 하지 않아요.", "tip-copy");
            Label(body, "BPM은 영상의 표기를 기준으로 안내해요.\n실제 템포와 재생 가능 여부는 영상에서 확인하세요.", "notice");
        }
        void MusicCard(MusicTrack track)
        {
            var card = Element(body, "card music-card"); var cover = Element(card, "cover");
            if (artwork.TryGetValue(track.cover ?? "LimeFlow", out var texture) && texture) cover.style.backgroundImage = texture;
            var copy = Element(card, "track-body");
            Label(copy, track.title, "track-title");
            Label(copy, track.bpm + " BPM · " + (track.MatchingSteps(Settings.targetSpm) == 1 ? "한 박자에 한 걸음" : "한 박자에 두 걸음"), "track-sub");
            Label(copy, "YouTube · " + track.genre + "\n" + (track.Difference(Settings.targetSpm) == 0 ? "영상 표기 기준 일치" : "목표와 가까운 템포"), "track-meta");
            var open = Button(copy, "", () => OpenTrack(track), "youtube", "open-" + track.videoId);
            open.Add(new RunIcon("youtube", null, 19)); Label(open, "YouTube에서 열기", "button-label").style.marginLeft = 7;
            bool favorite = Settings.favorites.Contains(track.videoId);
            Button(copy, favorite ? "♥  담은 음악" : "♡  즐겨찾기", () => { if (favorite) Settings.favorites.Remove(track.videoId); else Settings.favorites.Add(track.videoId); SaveSettings(); RenderMusic(); }, "favorite", "favorite-" + track.videoId);
            Button(copy, "영상 정보", () => TrackInfo(track), "favorite").style.height = 24;
        }
        void OpenTrack(MusicTrack track)
        {
            StopPreview(); bool success = true;
            if (Engine is AndroidRunEngine android) success = android.OpenMusic(track.videoId);
            else { Engine.Pause("YouTube에서 음악을 듣고 있어요."); Application.OpenURL("https://www.youtube.com/watch?v=" + track.videoId); }
            if (!success) { var sheet = Sheet("영상을 열 수 없어요"); Label(sheet, "YouTube 앱이나 브라우저를 확인하거나 다른 음악을 선택해 주세요.", "body-copy"); Button(sheet, "다른 음악 보기", CloseSheet, "primary"); }
        }
        void TrackInfo(MusicTrack track)
        {
            var sheet = Sheet("영상 정보"); Label(sheet, track.title, "value");
            string evidence = track.confidence == "paper-reported-bpm" ? "논문에 보고된 BPM" : "영상·게시자 자료";
            Label(sheet, "자료 기준: " + track.bpm + " BPM\n자료 확인일: " + track.verifiedAt + "\n확인 수준: " + evidence + "\n청취·탭 측정: 미완료\n템포 변화: 확인 전", "body-copy");
            Button(sheet, "자료 출처 보기", () => Application.OpenURL(track.sourceUrl), "secondary");
            Label(sheet, "삭제·비공개·지역 제한으로 재생되지 않으면 다른 음악을 선택해 주세요.", "body-copy");
            Button(sheet, "다른 음악 보기", CloseSheet, "secondary");
        }
        public void RenderHistory()
        {
            Shell("운동 기록"); Engine.Tick(); var sessions = Engine.State.history;
            if (!string.IsNullOrEmpty(Engine.State.error)) Label(body, Engine.State.error, "notice danger");
            if (sessions.Count == 0) { Empty(body, "history", "첫 리듬을 기다리고 있어요", "달리기를 마치면 운동 시간과\n목표 구간이 여기에 남아요."); Button(body, "달리기 시작하기", () => Navigate("home"), "primary"); return; }
            long total = sessions.Sum(x => x.activeMs);
            var summary = Element(body, "card settings-card"); Label(summary, "쌓인 운동 시간", "caption"); Label(summary, FormatTime(total), "history-time"); Label(summary, sessions.Count + "번의 운동 · 기기에 저장됨", "muted");
            Label(body, "최근 운동", "section-title");
            foreach (var session in sessions)
            {
                var s = session; var row = Button(body, "", () => ShowSession(s, "history"), "card history-row", "session-" + s.id);
                var top = Element(row, "history-top"); Label(top, DateText(s.startedAt), "caption grow"); Label(top, StatusText(s.status), "history-tag");
                Label(row, FormatTime(s.activeMs), "history-time"); Label(row, "목표 " + s.targetSpm + " step/min · " + s.segments.Count + "개 구간", "muted");
            }
        }
        public void ShowSession(Session session, string returnPage)
        {
            CurrentPage = "detail"; detailReturn = returnPage; Shell(returnPage == "home" ? "오늘의 리듬" : "운동 상세");
            if (!string.IsNullOrEmpty(Engine.State.error)) Label(body, Engine.State.error, "notice danger");
            Label(body, session.status == "completed" ? "목표 시간만큼 달렸어요" : session.status == "interrupted" ? "마지막 저장 지점까지 복구했어요" : "오늘도 한 걸음 더 달렸어요", "run-intro");
            Label(body, FormatTime(session.activeMs), "big-result"); Label(body, "실제 실행 시간", "unit");
            Label(body, DateText(session.startedAt) + " · " + StatusText(session.status), "notice");
            var card = Element(body, "card settings-card"); Label(card, "나의 목표 구간", "value");
            foreach (var segment in session.segments.Where(x => x.endActiveMs > x.startActiveMs))
            {
                var row = Element(card, "segment"); Label(row, segment.targetSpm + " step/min", "value lime"); Label(row, FormatTime(segment.endActiveMs - segment.startActiveMs), "muted");
            }
            if (session.activeMs == 0) Label(card, "박자 재생 전에 종료한 운동이에요.", "notice");
            Label(body, "설정한 목표와 실행 시간을 기록합니다.\n거리와 실제 케이던스는 측정하지 않아요.", "notice");
            Button(body, "확인", () => Navigate(returnPage), "primary", "result-done");
            Button(body, "이 기록 삭제", () => { var sheet = Sheet("이 운동 기록을 삭제할까요?"); Label(sheet, "삭제한 기록은 복구할 수 없어요.", "body-copy"); Button(sheet, "삭제", () => { Engine.DeleteHistory(session.id); CloseSheet(); Navigate("history"); }, "secondary danger", "delete-session"); }, "link danger");
        }
        public void RenderSettings()
        {
            Shell("설정");
            var card = Element(body, "card settings-card"); Label(card, "나에게 맞게", "section-title").style.marginTop = 0;
            var screen = new Toggle("운동 중 화면 켜두기") { value = Settings.keepScreenOn }; screen.AddToClassList("switch-row"); screen.RegisterValueChangedCallback(e => { Settings.keepScreenOn = e.newValue; SaveSettings(); }); card.Add(screen);
            var large = new Toggle("설명 글자 크게 보기") { value = Settings.largeText }; large.AddToClassList("switch-row"); large.RegisterValueChangedCallback(e => { Settings.largeText = e.newValue; SaveSettings(); }); card.Add(large);
            Label(card, "화면을 꺼도 박자는 계속 재생됩니다. 전화나 이어폰 분리로 멈추면 직접 다시 시작해 주세요.", "body-copy");
            Button(body, "잠금 화면 · 알림 설정", () => { if (Engine is AndroidRunEngine android) android.NotificationSettings(); else Toast("안드로이드에서 시스템 알림 설정을 열어요."); }, "secondary");
            Label(body, "데이터와 개인정보", "section-title");
            Button(body, "개인정보 처리 안내", ShowPrivacy, "setting-row");
            Button(body, "운동 기록 모두 삭제", () => {
                var sheet = Sheet("모든 운동 기록을 삭제할까요?"); Label(sheet, "즐겨찾기와 기본 설정은 유지됩니다. 삭제한 기록은 복구할 수 없어요.", "body-copy");
                Button(sheet, "모두 삭제", () => { Engine.ClearHistory(); Engine.Tick(); CloseSheet(); Toast(string.IsNullOrEmpty(Engine.State.error) ? "운동 기록을 삭제했어요." : Engine.State.error); }, "secondary danger", "clear-history");
            }, "setting-row danger");
            Button(body, "사용 안내", ShowHelp, "setting-row");
            Button(body, "오픈소스 라이선스", () => { var sheet = Sheet("오픈소스 라이선스"); Label(sheet, "Noto Sans KR · Noto Sans Black\nSIL Open Font License 1.1\n\n글꼴의 저작권 고지와 전체 라이선스를 확인할 수 있습니다.", "body-copy"); Button(sheet, "전체 라이선스 보기", () => { var full = Sheet("SIL Open Font License"); foreach (string asset in new[] { "Font-LICENSE", "NotoSans-LICENSE" }) { var license = Resources.Load<TextAsset>("RunBeat/" + asset); if (license) Label(full, license.text, "body-copy"); } }, "secondary"); }, "setting-row");
            Label(body, "런비트 " + Application.version + "\nSecondWindGames", "notice");
            Label(body, "FIND YOUR RHYTHM", "footer");
        }
        void ShowPrivacy()
        {
            var sheet = Sheet("개인정보 처리 안내"); var scroll = new ScrollView(); scroll.style.maxHeight = 520; sheet.Add(scroll);
            Label(scroll, "런비트는 로그인 없이 사용합니다. 운동 시간, 목표 구간, 즐겨찾기와 설정을 이 기기에 저장합니다.\n\n위치, 마이크, 연락처, 광고 식별자를 수집하지 않으며 자체 분석·광고 서버로 보내지 않습니다.\n\n알림 권한은 운동 상태와 잠금 화면 제어를 위해 요청합니다. 거부해도 앱에서 운동을 시작하고 멈출 수 있습니다.\n\nYouTube 링크를 누르면 외부 앱이나 브라우저가 열리며 해당 서비스의 개인정보 처리방침이 적용됩니다. 런비트의 운동 기록은 링크에 포함하지 않습니다.\n\n기록은 설정에서 삭제할 수 있습니다. 앱을 삭제하면 기기에 저장된 정보도 삭제됩니다. 클라우드 동기화와 재설치 복원은 제공하지 않습니다.\n\n시행일: 2026년 9월 11일\n운영: SecondWindGames", "body-copy");
            Button(sheet, "확인", CloseSheet, "primary");
        }
        void ShowHelp()
        {
            var sheet = Sheet("나의 리듬으로 달리기");
            Label(sheet, "1. 목표를 100~220 step/min에서 고르세요.\n2. 음색과 매 걸음·두 걸음 박자를 선택하세요.\n3. 운동 시간을 고르고 달리기를 시작하세요.\n\n3초 뒤 박자음이 시작됩니다. 달리는 중에도 목표를 바꿀 수 있고 다음 박자에 반영돼요.\n\n음악 찾기에서 YouTube를 열면 박자음이 멈춥니다. 앱으로 돌아온 뒤 직접 계속하기를 눌러주세요.\n\n내 몸에 편안한 리듬으로 시작하고 주변 소리도 들을 수 있는 음량을 사용하세요.", "body-copy");
            Button(sheet, "확인", CloseSheet, "primary");
        }
        void ShowRecovery()
        {
            var sheet = Sheet("이전 운동 기록을 복구했어요"); Label(sheet, "앱이 종료되기 전 마지막으로 저장한 실행 시간까지만 기록했어요. 박자음은 자동으로 시작하지 않습니다.", "body-copy");
            Button(sheet, "기록 확인", () => { CloseSheet(); ShowSession(Engine.State.current, "home"); }, "primary");
        }
        void Back()
        {
            if (overlay != null) { CloseSheet(); return; }
            if (CurrentPage == "detail") Navigate(detailReturn);
            else if (CurrentPage == "music") Navigate(musicReturn);
            else if (CurrentPage == "run") Navigate("home");
            else if (CurrentPage != "home") Navigate("home");
            else if (Application.platform == RuntimePlatform.Android) { using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer")) using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity")) activity.Call<bool>("moveTaskToBack", true); }
        }
        VisualElement Sheet(string title)
        {
            CloseSheet(); overlay = Element(root, "overlay", "modal");
            overlay.RegisterCallback<ClickEvent>(e => { if (e.target == overlay) CloseSheet(); });
            var sheet = Element(overlay, "sheet");
            sheet.style.paddingBottom = Mathf.Max(24, Screen.safeArea.yMin * 390 / Mathf.Max(1, Screen.width) + 12);
            Element(sheet, "sheet-handle"); var header = Element(sheet, "sheet-header"); Label(header, title, "sheet-title"); IconButton(header, "close", CloseSheet, "닫기", "close-sheet");
            var scroll = new ScrollView(ScrollViewMode.Vertical) { verticalScrollerVisibility = ScrollerVisibility.Hidden, horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.style.maxHeight = Mathf.Max(240, Screen.height * 390f / Mathf.Max(1, Screen.width) * .92f - 118 - Screen.safeArea.yMin * 390f / Mathf.Max(1, Screen.width));
            scroll.style.flexShrink = 1; sheet.Add(scroll); return scroll.contentContainer;
        }
        void CloseSheet() { overlay?.RemoveFromHierarchy(); overlay = null; StopPreview(); }
        void Toast(string message)
        {
            root.Q<Label>("toast")?.RemoveFromHierarchy(); var toast = Label(root, message, "toast", "toast"); toast.pickingMode = PickingMode.Ignore; toast.schedule.Execute(() => toast.RemoveFromHierarchy()).StartingIn(3500);
        }
        static VisualElement Element(VisualElement parent, string classes, string name = null)
        {
            var e = new VisualElement { name = name }; foreach (string c in classes.Split(' ')) e.AddToClassList(c); parent.Add(e); return e;
        }
        Label Label(VisualElement parent, string text, string classes, string name = null)
        {
            var label = new Label(text) { name = name, pickingMode = PickingMode.Ignore }; foreach (string c in classes.Split(' ')) label.AddToClassList(c);
            if (numberFont && (classes.Contains("number") || classes == "timer" || classes.Contains("history-time") || classes.Contains("music-target") || classes.Contains("big-result")))
            { label.style.unityFontDefinition = FontDefinition.FromSDFFont(numberFont); label.style.unityFontStyleAndWeight = FontStyle.Normal; }
            parent.Add(label); return label;
        }
        static Button Button(VisualElement parent, string text, Action clicked, string classes, string name = null)
        {
            var button = new Button(clicked) { text = text, name = name }; foreach (string c in classes.Split(' ')) button.AddToClassList(c); parent.Add(button); return button;
        }
        static Button IconButton(VisualElement parent, string icon, Action clicked, string tooltip, string name = null, string classes = "icon-button")
        {
            var button = Button(parent, "", clicked, classes, name); button.tooltip = tooltip;
            button.Add(new RunIcon(icon, null, classes.Contains("control-circle") ? 32 : 25)); return button;
        }
        Button ActionButton(VisualElement parent, string text, string icon, Action clicked, string name, bool primary)
        {
            var b = Button(parent, "", clicked, primary ? "primary" : "secondary", name);
            b.Add(new RunIcon(icon, primary ? new Color32(17,24,7,255) : Color.white, primary ? 28 : 25)); Label(b, text, "button-label"); return b;
        }
        void Empty(VisualElement parent, string icon, string title, string text)
        {
            var empty = Element(parent, "empty"); empty.Add(new RunIcon(icon, Lime, 42)); Label(empty, title, "empty-title"); Label(empty, text, "empty-copy");
        }
        public static string FormatTime(long milliseconds)
        {
            long seconds = Math.Max(0, milliseconds) / 1000; return seconds >= 3600 ? (seconds / 3600).ToString("00") + ":" + (seconds / 60 % 60).ToString("00") + ":" + (seconds % 60).ToString("00") : (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }
        static string DurationText(int seconds) => seconds == 0 ? "자유 러닝" : seconds / 60 + "분";
        static string StatusText(string value) => value == "completed" ? "목표 완료" : value == "interrupted" ? "중단 후 복구" : "운동 종료";
        static string DateText(string utc) => DateTime.TryParse(utc, out var date) ? date.ToLocalTime().ToString("yyyy.MM.dd  HH:mm") : "";
        void OnApplicationPause(bool pause)
        {
            if (!initialized || !pause) return; StopPreview();
            if (!(Engine is AndroidRunEngine)) Engine.Pause("앱이 백그라운드로 이동했어요.");
        }
        void OnDestroy()
        {
            Engine?.Dispose(); if (panel) Destroy(panel);
            if (uiFont) { foreach (var texture in uiFont.atlasTextures) if (texture) Destroy(texture); if (uiFont.material) Destroy(uiFont.material); Destroy(uiFont); }
            if (numberFont) { foreach (var texture in numberFont.atlasTextures) if (texture) Destroy(texture); if (numberFont.material) Destroy(numberFont.material); Destroy(numberFont); }
            if (previewClips != null) foreach (var clip in previewClips) if (clip) Destroy(clip);
        }
    }
}
