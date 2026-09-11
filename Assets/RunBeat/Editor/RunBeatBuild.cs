using System;
using System.IO;
using System.Linq;
using System.Reflection;
using RunBeat;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class RunBeatBuild
{
    public const string ScenePath = "Assets/RunBeat/Scenes/RunBeat.unity";
    const string CommandPath = "BuildArtifacts/editor-command.txt";
    static double nextPoll;
    static bool busy;
    static RunBeatBuild() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (busy || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(CommandPath)) return;
        string command = File.ReadAllText(CommandPath).Trim(); File.Delete(CommandPath); busy = true;
        try
        {
            if (command == "setup") Setup();
            else if (command == "apk") BuildAndroid();
            else if (command == "aab") BuildStoreBundle();
            else if (command == "windows") BuildWindows();
            else if (command == "test") RunBeatChecks.Run();
            else if (command == "play") { SetGameSize(390, 844); EditorApplication.isPlaying = true; }
            else if (command == "stop") EditorApplication.isPlaying = false;
            else if (command == "capture") ScreenCapture.CaptureScreenshot("BuildArtifacts/QA/editor.png");
            else if (command.StartsWith("page:")) UnityEngine.Object.FindFirstObjectByType<RunBeatApp>().Navigate(command.Substring(5));
            else if (command == "qa") UnityEngine.Object.FindFirstObjectByType<RunBeatApp>().gameObject.AddComponent<RunBeatVisualQA>();
            else if (command.StartsWith("size:")) { var parts = command.Substring(5).Split('x'); SetGameSize(int.Parse(parts[0]), int.Parse(parts[1])); }
            else if (command == "refresh") AssetDatabase.Refresh();
            else if (command == "logs") DumpLogs();
            else if (command == "store") RunBeatStoreAssets.Begin();
            File.WriteAllText("BuildArtifacts/editor-result.txt", command + " OK " + DateTime.UtcNow.ToString("o"));
        }
        catch (Exception ex) { File.WriteAllText("BuildArtifacts/editor-result.txt", command + " FAILED\n" + ex); Debug.LogException(ex); }
        finally { busy = false; }
    }
    [MenuItem("RunBeat/Setup Android Portrait")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before setup.");
        Directory.CreateDirectory("Assets/RunBeat/Scenes");
        if (!File.Exists(ScenePath))
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("UI Camera", typeof(Camera), typeof(AudioListener));
            var camera = cameraObject.GetComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(19,21,22,255); camera.orthographic = true; camera.cullingMask = 0;
            new GameObject("RunBeat", typeof(RunBeatApp));
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        PlayerSettings.companyName = "SecondWindGames"; PlayerSettings.productName = "런비트";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.secondwindgames.runbeat");
        PlayerSettings.bundleVersion = "1.0.0"; PlayerSettings.Android.bundleVersionCode = 1;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.allowedAutorotateToPortrait = true; PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false; PlayerSettings.allowedAutorotateToLandscapeRight = false;
        PlayerSettings.defaultScreenWidth = 390; PlayerSettings.defaultScreenHeight = 844;
        PlayerSettings.resizableWindow = true; PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.runInBackground = true;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.androidIsGame = false;
        PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.ForceInternal;
        PlayerSettings.Android.renderOutsideSafeArea = true;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android,ManagedStrippingLevel.Minimal);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[] { GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        QualitySettings.vSyncCount = 0;
        var logo = AssetDatabase.LoadAllAssetsAtPath("Assets/RunBeat/Branding/SecondWindGamesLogo.png").OfType<Sprite>().Single();
        PlayerSettings.SplashScreen.show = true; PlayerSettings.SplashScreen.showUnityLogo = false;
        PlayerSettings.SplashScreen.backgroundColor = Color.white; PlayerSettings.SplashScreen.background = null; PlayerSettings.SplashScreen.backgroundPortrait = null;
        PlayerSettings.SplashScreen.blurBackgroundImage = false; PlayerSettings.SplashScreen.overlayOpacity = 1;
        PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Static;
        PlayerSettings.SplashScreen.animationBackgroundZoom = 1; PlayerSettings.SplashScreen.animationLogoZoom = 1;
        PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.DarkOnLight;
        PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.UnityLogoBelow;
        PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2,logo) };
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/RunBeat/Branding/RunBeatIcon.png");
        if (icon) PlayerSettings.SetIcons(NamedBuildTarget.Unknown,new[] { icon },IconKind.Any);
        var serialized = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        serialized.FindProperty("AndroidSplashScreenScale").intValue = 2;
        serialized.FindProperty("androidApplicationEntry").intValue = 1;
        serialized.FindProperty("AndroidIsGame").boolValue = false;
        var internet = serialized.FindProperty("forceInternetPermission"); if (internet != null) internet.boolValue = false;
        var backup = serialized.FindProperty("allowBackup"); if (backup != null) backup.boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        var connect = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/UnityConnectSettings.asset")[0]);
        var engineDiagnostics = connect.FindProperty("InsightsSettings.m_EngineDiagnosticsEnabled"); if (engineDiagnostics != null) engineDiagnostics.boolValue = false;
        var analytics = connect.FindProperty("UnityAnalyticsSettings.m_InitializeOnStartup"); if (analytics != null) analytics.boolValue = false;
        connect.ApplyModifiedPropertiesWithoutUndo();
        EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
        EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Disabled;
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        if (SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
        Debug.Log("RunBeat Android portrait configured; W05 splash applied.");
    }
    [MenuItem("RunBeat/Build Android APK")]
    public static void BuildAndroid() { Setup(); EditorUserBuildSettings.buildAppBundle = false; Build(BuildTarget.Android,"Builds/Android/RunBeat-1.0.0.apk"); }
    [MenuItem("RunBeat/Build Store AAB")]
    public static void BuildStoreBundle()
    {
        Setup();
        string keyPath = Environment.GetEnvironmentVariable("RUNBEAT_KEYSTORE");
        string keyAlias = Environment.GetEnvironmentVariable("RUNBEAT_KEY_ALIAS");
        if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath) || string.IsNullOrWhiteSpace(keyAlias))
            throw new InvalidOperationException("Set RUNBEAT_KEYSTORE, RUNBEAT_KEY_ALIAS, RUNBEAT_STORE_PASSWORD and RUNBEAT_KEY_PASSWORD for the publisher upload key.");
        string storePass = Environment.GetEnvironmentVariable("RUNBEAT_STORE_PASSWORD"), keyPass = Environment.GetEnvironmentVariable("RUNBEAT_KEY_PASSWORD");
        if (string.IsNullOrEmpty(storePass) || string.IsNullOrEmpty(keyPass)) throw new InvalidOperationException("Upload signing passwords are missing.");
        try
        {
            PlayerSettings.Android.useCustomKeystore = true; PlayerSettings.Android.keystoreName = keyPath; PlayerSettings.Android.keyaliasName = keyAlias;
            PlayerSettings.Android.keystorePass = storePass; PlayerSettings.Android.keyaliasPass = keyPass;
            EditorUserBuildSettings.buildAppBundle = true; Build(BuildTarget.Android,"Builds/Android/RunBeat-1.0.0.aab");
        }
        finally { PlayerSettings.Android.keystorePass = ""; PlayerSettings.Android.keyaliasPass = ""; }
    }
    [MenuItem("RunBeat/Build Windows QA")]
    public static void BuildWindows() { Setup(); Build(BuildTarget.StandaloneWindows64,"Builds/Windows/RunBeat.exe"); }
    static void Build(BuildTarget target,string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, target = target, locationPathName = path, options = BuildOptions.None });
        Directory.CreateDirectory("BuildArtifacts/QA");
        File.WriteAllText("BuildArtifacts/QA/build-" + target + ".txt", report.summary.result + "\nErrors: " + report.summary.totalErrors + "\nWarnings: " + report.summary.totalWarnings + "\nBytes: " + report.summary.totalSize + "\nDuration: " + report.summary.totalTime);
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("RunBeat build failed: " + report.summary.result);
    }
    public static void SetGameSize(int width,int height)
    {
        foreach(var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            if(window.GetType().FullName.Contains("DeviceSimulation") || window.GetType().Name=="SimulatorWindow")window.Close();
        var assembly = typeof(Editor).Assembly; var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
        var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var sizes = singleton.GetProperty("instance",BindingFlags.Public|BindingFlags.Static).GetValue(null);
        var groupType = assembly.GetType("UnityEditor.GameViewSizeGroupType");
        string sizeGroup = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ? "Android" : "Standalone";
        var group = sizesType.GetMethod("GetGroup").Invoke(sizes,new[] { Enum.Parse(groupType,sizeGroup) });
        var sizeType = assembly.GetType("UnityEditor.GameViewSize");
        var enumType = assembly.GetType("UnityEditor.GameViewSizeType");
        var size = Activator.CreateInstance(sizeType,new[] { Enum.Parse(enumType,"FixedResolution"), (object)width, height, "RunBeat " + width + "x" + height });
        group.GetType().GetMethod("AddCustomSize").Invoke(group,new[] { size });
        int total = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group,null);
        var viewType = assembly.GetType("UnityEditor.GameView"); var view = EditorWindow.GetWindow(viewType); view.Show();
        viewType.GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(view,total-1);
        view.position=new Rect(450,65,Math.Max(440,width+30),Math.Min(980,height+85)); view.Focus(); view.Repaint();
    }
    static void DumpLogs()
    {
        var type=typeof(Editor).Assembly.GetType("UnityEditor.LogEntries");var entryType=typeof(Editor).Assembly.GetType("UnityEditor.LogEntry");
        var entry=Activator.CreateInstance(entryType);int count=(int)type.GetMethod("GetCount").Invoke(null,null);
        var output=new System.Text.StringBuilder();type.GetMethod("StartGettingEntries").Invoke(null,null);
        try{for(int i=Math.Max(0,count-40);i<count;i++){type.GetMethod("GetEntryInternal").Invoke(null,new[]{(object)i,entry});output.AppendLine((string)entryType.GetField("message").GetValue(entry));}}
        finally{type.GetMethod("EndGettingEntries").Invoke(null,null);}
        File.WriteAllText("BuildArtifacts/QA/editor-console.txt",output.ToString());
    }
}

public sealed class RunBeatAssetImporter : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/RunBeat/Resources/RunBeat/") && !assetPath.EndsWith("RunBeatIcon.png")) return;
        var t=(TextureImporter)assetImporter; t.mipmapEnabled=false; t.textureType=TextureImporterType.Default;
        t.wrapMode=TextureWrapMode.Clamp; t.filterMode=FilterMode.Bilinear; t.npotScale=TextureImporterNPOTScale.None;
        t.maxTextureSize=1024; t.textureCompression=TextureImporterCompression.CompressedHQ;
    }
}
