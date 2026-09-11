using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RunBeat;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class RunBeatChecks
{
    public static void Run()
    {
        var log = new StringBuilder(); int count = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL " + message); count++; log.AppendLine("PASS " + message); }
        var p = new Preset { targetSpm = 300, beatEverySteps = 8, soundId = -2, durationSec = -10, volume = float.NaN, favorites = null }; p.Validate();
        Check(p.targetSpm == 220 && p.beatEverySteps == 1 && p.soundId == 0 && p.durationSec == 1200 && p.volume == .65f && p.favorites.Count == 0,"Invalid preference recovery");
        for (int spm=100; spm<=220; spm++) for(int steps=1;steps<=2;steps++) Check(Math.Abs(LocalRunEngine.Interval(spm,steps)*spm - 60.0*steps) < 1e-10,"Beat interval " + spm + "/" + steps);
        Check(RunBeatApp.FormatTime(754000)=="12:34","12:34 formatting");
        Check(RunBeatApp.FormatTime(3661000)=="01:01:01","Long session formatting");
        Check(RunBeatApp.FormatTime(-1)=="00:00","No negative clock");
        var track = new MusicTrack { bpm = 90 };
        Check(track.Difference(180)==0 && track.MatchingSteps(180)==2,"Half-tempo match");
        Check(track.Difference(185)==5 && track.Difference(186)==6,"Nearby match boundary");
        string dir = "BuildArtifacts/QA/StorageTest", path = Path.Combine(dir,"preferences.json"); Directory.CreateDirectory(dir);
        AtomicJson.Write(path,new Preset { targetSpm=170 }); AtomicJson.Write(path,new Preset { targetSpm=190 });
        Check(AtomicJson.Read(path,()=>new Preset()).targetSpm==190,"Atomic latest write");
        File.WriteAllText(path,"not-json");
        Check(AtomicJson.Read(path,()=>new Preset()).targetSpm==170,"Backup recovery after corrupt primary");
        var asset = Resources.Load<TextAsset>("RunBeat/MusicCatalog");
        Check(asset!=null,"Music catalog exists");
        var catalog = JsonUtility.FromJson<MusicCatalog>(asset.text);
        Check(catalog.tracks.Count==24,"24 music links");
        Check(catalog.tracks.Select(x=>x.videoId).Distinct().Count()==24,"No duplicate video links");
        Check(catalog.tracks.All(x=>Regex.IsMatch(x.videoId,"^[A-Za-z0-9_-]{11}$")),"Valid YouTube IDs");
        Check(catalog.tracks.All(x=>!string.IsNullOrWhiteSpace(x.sourceUrl)&&!string.IsNullOrWhiteSpace(x.confidence)),"Music evidence metadata");
        Check(Resources.Load<Font>("RunBeat/NotoSansKR")!=null,"Korean font included");
        Check(PlayerSettings.defaultInterfaceOrientation==UIOrientation.Portrait,"Portrait orientation");
        Check(PlayerSettings.SplashScreen.show && !PlayerSettings.SplashScreen.showUnityLogo && PlayerSettings.SplashScreen.logos.Length==1 && PlayerSettings.SplashScreen.logos[0].duration==2,"W05 splash settings");
        log.AppendLine("TOTAL " + count + " passed");
        Directory.CreateDirectory("BuildArtifacts/QA"); File.WriteAllText("BuildArtifacts/QA/unity-checks.txt",log.ToString()); Debug.Log(log.ToString());
    }
}

public sealed class RunBeatVisualQA : MonoBehaviour
{
    IEnumerator Start()
    {
        if (Screen.height <= Screen.width) throw new Exception("Visual QA requires a portrait Game View.");
        var app = GetComponent<RunBeatApp>();
        string dir = Path.GetFullPath("BuildArtifacts/QA"); Directory.CreateDirectory(dir);
        var log = new StringBuilder();
        foreach(var page in new[]{"home","music","history","settings"})
        {
            app.Navigate(page); yield return new WaitForSecondsRealtime(.6f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir,page+"-"+Screen.width+"x"+Screen.height+".png"));
            yield return new WaitForSecondsRealtime(.4f);
            var root = GetComponent<UIDocument>().rootVisualElement;
            int visible = root.Query<Button>().ToList().Count(x=>x.worldBound.Overlaps(root.worldBound));
            log.AppendLine(page+" visible controls="+visible+" viewport="+Screen.width+"x"+Screen.height);
        }
        app.Navigate("home"); yield return null;
        var doc=GetComponent<UIDocument>(); int initial=app.Settings.targetSpm;
        Click(doc.rootVisualElement.Q<Button>("target-plus"));
        yield return new WaitForSecondsRealtime(.3f);
        if(app.Settings.targetSpm!=Math.Min(220,initial+1))throw new Exception("UI target button must change by exactly one: before="+initial+" after="+app.Settings.targetSpm);
        app.ChangeTarget(-1);
        Click(doc.rootVisualElement.Q<Button>("sound"));
        yield return new WaitForSecondsRealtime(.4f);
        ScreenCapture.CaptureScreenshot(Path.Combine(dir,"sound-"+Screen.width+"x"+Screen.height+".png"));
        yield return new WaitForSecondsRealtime(.4f);
        app.Navigate("home"); app.BeginRun(); yield return new WaitForSecondsRealtime(4.5f);
        if(app.Engine.State.current.status!="running")throw new Exception("Countdown did not start audio");
        app.TogglePause(); yield return new WaitForSecondsRealtime(.3f);
        long paused=app.Engine.State.current.activeMs; yield return new WaitForSecondsRealtime(.7f);
        if(app.Engine.State.current.activeMs!=paused)throw new Exception("Paused time advances");
        ScreenCapture.CaptureScreenshot(Path.Combine(dir,"paused-"+Screen.width+"x"+Screen.height+".png"));
        app.TogglePause(); app.ChangeTarget(1); yield return new WaitForSecondsRealtime(1.1f);
        if(app.Engine.State.current.segments.Count<2)throw new Exception("Missing cadence segment");
        var viewport=doc.rootVisualElement.Q<ScrollView>("page-scroll").contentViewport.worldBound;
        var timerBounds=doc.rootVisualElement.Q<Label>("timer").parent.worldBound;
        if(timerBounds.yMin<viewport.yMin || timerBounds.yMax>viewport.yMax+.5f)throw new Exception("Exercise timer is clipped by the page viewport.");
        foreach(string control in new[]{"pause","stop"}){
            var bounds=doc.rootVisualElement.Q<Button>(control).worldBound;
            if(bounds.yMin<0 || bounds.yMax>doc.rootVisualElement.worldBound.yMax)throw new Exception("Exercise control is off screen: "+control);
        }
        ScreenCapture.CaptureScreenshot(Path.Combine(dir,"run-"+Screen.width+"x"+Screen.height+".png"));
        yield return new WaitForSecondsRealtime(.3f);app.Engine.Stop();yield return new WaitForSecondsRealtime(.4f);
        if(app.Engine.State.history.Count==0)throw new Exception("Session missing from history");
        ScreenCapture.CaptureScreenshot(Path.Combine(dir,"result-"+Screen.width+"x"+Screen.height+".png"));
        app.ChangeTarget(-1);log.AppendLine("PASS buttons, countdown, pause time, resume, cadence segment, stop, history");
        File.WriteAllText(Path.Combine(dir,"visual-qa-"+Screen.width+"x"+Screen.height+".txt"),log.ToString());
        yield return new WaitForSecondsRealtime(.3f);app.Navigate("home");Destroy(this);
    }
    static void Click(Button button)
    {
        var method=typeof(Clickable).GetMethod("SimulateSingleClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
        if(method==null)throw new Exception("Clickable simulation is not available");
        method.Invoke(button.clickable,new object[]{null,0});
    }
}
