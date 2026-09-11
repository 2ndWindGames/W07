package com.secondwindgames.runbeat;

import android.app.*;
import android.content.*;
import android.content.pm.ServiceInfo;
import android.media.*;
import android.media.session.*;
import android.net.Uri;
import android.os.*;
import android.provider.Settings;
import android.util.AtomicFile;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.*;

/** Owns the Android session and PCM output even while Unity is suspended. */
public final class RunBeatService extends Service {
    private static final Object LOCK = new Object();
    private static final int RATE = 48000, NOTIFICATION = 180;
    private static final String CHANNEL = "runbeat_playback";
    private static JSONObject state;
    private static Context app;
    private static RunBeatService instance;
    private static String loadWarning = "";
    private final Handler main = new Handler(Looper.getMainLooper());
    private AudioManager manager;
    private AudioFocusRequest focus;
    private MediaSession media;
    private PowerManager.WakeLock wake;
    private volatile AudioTrack track;
    private volatile boolean playing;
    private volatile long generation;
    private RhythmSynth synth;
    private long baseMs, lastCheckpoint, countdownUntil;
    private long lastPlaybackHead, playbackWrapFrames;
    private int requestedTarget=180;
    private String lastNotification = "";
    private final BroadcastReceiver noisy = new BroadcastReceiver() {
        @Override public void onReceive(Context c, Intent i) { pause("이어폰 연결이 해제되었어요."); }
    };
    private final Runnable heartbeat = new Runnable() {
        @Override public void run() {
            synchronized (LOCK) {
                if (is("countdown") && SystemClock.elapsedRealtime() >= countdownUntil) beginAudio();
                updateTime();
                if (playing && SystemClock.elapsedRealtime() - lastCheckpoint >= 10000) { save(); lastCheckpoint = SystemClock.elapsedRealtime(); }
                if (isOpen()) updateNotification();
            }
            main.postDelayed(this, 200);
        }
    };
    private static String now() {
        SimpleDateFormat f = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.US);
        f.setTimeZone(TimeZone.getTimeZone("UTC")); return f.format(new Date());
    }
    private static void put(JSONObject o, String key, Object value) { try { o.put(key, value); } catch (JSONException ex) { throw new IllegalStateException(ex); } }
    private static JSONObject current() { return state.optJSONObject("current"); }
    private static JSONArray history() { return state.optJSONArray("history"); }
    private static boolean is(String status) { return current().optString("status").equals(status); }
    private static boolean isOpen() { return is("running") || is("paused") || is("countdown"); }
    private static JSONObject idle() { JSONObject s = new JSONObject(); put(s, "status", "idle"); put(s,"targetSpm",180); put(s,"segments",new JSONArray()); return s; }
    private static AtomicFile storage() { return new AtomicFile(new File(app.getFilesDir(), "runbeat-engine.json")); }
    public static void initialize(Context context) {
        synchronized (LOCK) {
            if (state != null) return;
            app = context.getApplicationContext();
            try {
                byte[] bytes = storage().readFully();
                if (bytes.length > 32 * 1024 * 1024) throw new IOException("Oversized state");
                state = new JSONObject(new String(bytes, StandardCharsets.UTF_8));
                if (current() == null || history() == null) throw new JSONException("Invalid state");
            } catch (FileNotFoundException ex) { state = null; }
            catch (Exception ex) { state = null; loadWarning = "저장 파일을 읽지 못했어요. 기기 저장 공간을 확인해 주세요."; }
            if (state == null) { state = new JSONObject(); put(state,"version",1); put(state,"current",idle()); put(state,"history",new JSONArray()); }
            put(state, "error", loadWarning);
            if (isOpen()) {
                put(current(), "status", "interrupted"); put(current(),"reason","앱이 종료되어 마지막 저장 지점까지 복구했어요.");
                finishRecord();
            }
        }
    }
    public static String snapshot(Context c) {
        initialize(c);
        synchronized (LOCK) { if (instance != null) instance.updateTime(); return state.toString(); }
    }
    public static void command(Context c, String action, String payload) {
        initialize(c);
        synchronized (LOCK) {
            if (action.equals("clear") || action.equals("delete")) {
                JSONArray clean = new JSONArray();
                for (int i = 0; i < history().length(); i++) { JSONObject s = history().optJSONObject(i); if (!action.equals("clear") && !s.optString("id").equals(payload)) clean.put(s); }
                put(state,"history",clean);
                if (!isOpen() && (action.equals("clear") || current().optString("id").equals(payload))) put(state,"current",idle());
                save(); return;
            }
            if (instance == null && !action.equals("start") && !action.equals("resume")) return;
        }
        Intent intent = new Intent(c, RunBeatService.class).setAction(action).putExtra("payload",payload);
        if (action.equals("start") || action.equals("resume")) c.startForegroundService(intent);
        else c.startService(intent);
    }
    public static boolean openMusic(Activity activity, String id) {
        if (id == null || !id.matches("[A-Za-z0-9_-]{11}")) return false;
        synchronized (LOCK) { if (instance != null) instance.pause("YouTube에서 음악을 듣고 있어요."); }
        try { activity.startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse("https://www.youtube.com/watch?v=" + id))); return true; }
        catch (ActivityNotFoundException ex) { return false; }
    }
    public static void notificationSettings(Activity a) {
        a.startActivity(new Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(Settings.EXTRA_APP_PACKAGE,a.getPackageName()));
    }
    private static boolean save() {
        FileOutputStream stream = null;
        try {
            stream = storage().startWrite(); stream.write(state.toString().getBytes(StandardCharsets.UTF_8));
            storage().finishWrite(stream); put(state,"error",loadWarning); return true;
        } catch (Exception ex) { storage().failWrite(stream); put(state,"error","기록을 저장하지 못했어요. 저장 공간을 확인해 주세요."); return false; }
    }
    private static void closeSegment() {
        JSONObject s = current(); JSONArray segments = s.optJSONArray("segments"); long end = s.optLong("activeMs");
        if (segments != null && segments.length() > 0) {
            // A scheduled beat can be in the output buffer when playback is interrupted.
            for (int i = segments.length()-1; i > 0; i--) {
                if (segments.optJSONObject(i).optLong("startActiveMs") >= end) segments.remove(i); else break;
            }
            JSONObject segment = segments.optJSONObject(segments.length()-1); put(segment,"endActiveMs",end); put(s,"targetSpm",segment.optInt("targetSpm",180));
        }
    }
    private static void finishRecord() {
        closeSegment(); put(current(),"endedAt",now());
        JSONArray updated = new JSONArray(); updated.put(current());
        for (int i=0; i<history().length(); i++) { JSONObject s=history().optJSONObject(i); if (!s.optString("id").equals(current().optString("id"))) updated.put(s); }
        put(state,"history",updated); save();
    }
    @Override public void onCreate() {
        super.onCreate(); initialize(this); instance = this;
        manager = (AudioManager)getSystemService(AUDIO_SERVICE);
        NotificationChannel channel = new NotificationChannel(CHANNEL,"런비트 운동 제어",NotificationManager.IMPORTANCE_LOW);
        channel.setSound(null,null); channel.setDescription("잠금 화면에서 운동을 일시정지하거나 종료합니다.");
        getSystemService(NotificationManager.class).createNotificationChannel(channel);
        media = new MediaSession(this,"RunBeat");
        media.setCallback(new MediaSession.Callback() {
            @Override public void onPlay() { synchronized (LOCK) { if (is("paused")) beginAudio(); } }
            @Override public void onPause() { pause("잠금 화면에서 일시정지했어요."); }
            @Override public void onStop() { stopSession(false); }
            @Override public void onSeekTo(long pos) { }
        });
        media.setActive(true);
        AudioAttributes attributes = new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build();
        focus = new AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN).setAudioAttributes(attributes)
            .setWillPauseWhenDucked(true).setAcceptsDelayedFocusGain(false)
            .setOnAudioFocusChangeListener(value -> { if (value < 0) pause("다른 오디오가 재생되어 일시정지했어요."); },main).build();
        wake = ((PowerManager)getSystemService(POWER_SERVICE)).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK,"RunBeat:Audio");
        wake.setReferenceCounted(false);
        if (Build.VERSION.SDK_INT >= 33) registerReceiver(noisy,new IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY),Context.RECEIVER_NOT_EXPORTED);
        else registerReceiver(noisy,new IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY));
        main.post(heartbeat);
    }
    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        synchronized (LOCK) {
            // Android 15+ requires foreground eligibility before acquiring audio focus.
            if (Build.VERSION.SDK_INT >= 29) startForeground(NOTIFICATION,notification(),ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK);
            else startForeground(NOTIFICATION,notification());
            if (intent == null) { stopSelf(); return START_NOT_STICKY; }
            String action = intent.getAction(), payload = intent.getStringExtra("payload");
            try {
                if ("start".equals(action) && !isOpen()) startSession(new JSONObject(payload));
                else if ("pause".equals(action)) pause(payload == null ? "사용자 일시정지" : payload);
                else if ("resume".equals(action) && is("paused")) beginAudio();
                else if ("stop".equals(action)) stopSession(false);
                else if ("target".equals(action) && isOpen()) {
                    int target = RhythmSynth.clamp(Integer.parseInt(payload)); requestedTarget=target;
                    if (playing && synth != null) synth.setTarget(target);
                    else { changeTarget(target,current().optLong("activeMs")); save(); }
                }
            } catch (Exception ex) { put(state,"error","오디오 요청을 처리하지 못했어요."); pause("다시 시작해 주세요."); }
            if (isOpen()) updateNotification();
        }
        return START_NOT_STICKY;
    }
    private void startSession(JSONObject p) {
        JSONObject s = new JSONObject(); int target=RhythmSynth.clamp(p.optInt("targetSpm",180)); requestedTarget=target;
        put(s,"id",UUID.randomUUID().toString()); put(s,"status","countdown"); put(s,"startedAt",now()); put(s,"activeMs",0);
        put(s,"targetSpm",target); put(s,"beatEverySteps",p.optInt("beatEverySteps",1)==2?2:1);
        put(s,"soundId",Math.max(0,Math.min(2,p.optInt("soundId",0))));
        int duration=p.optInt("durationSec",1200); if(duration!=0&&duration!=600&&duration!=1200&&duration!=1800)duration=1200;
        put(s,"durationSec",duration); put(s,"volume",Math.max(0,Math.min(1,p.optDouble("volume",.65))));
        put(s,"reason",""); put(s,"segments",new JSONArray()); put(state,"current",s);
        addSegment(target,0); countdownUntil=SystemClock.elapsedRealtime()+3000; save();
        // Keep the countdown advancing if the screen locks immediately after Start.
        if(!wake.isHeld())wake.acquire();
    }
    private void addSegment(int target,long activeMs) {
        JSONObject segment = new JSONObject(); put(segment,"targetSpm",target); put(segment,"startActiveMs",activeMs); put(segment,"endActiveMs",activeMs);
        current().optJSONArray("segments").put(segment);
    }
    private void changeTarget(int target,long activeMs) {
        if(current().optInt("targetSpm")==target)return;
        JSONArray segments=current().optJSONArray("segments");
        if(segments.length()>0)put(segments.optJSONObject(segments.length()-1),"endActiveMs",activeMs);
        put(current(),"targetSpm",target); addSegment(target,activeMs);
    }
    private void beginAudio() {
        if(playing)return;
        if(manager.requestAudioFocus(focus)!=AudioManager.AUDIOFOCUS_REQUEST_GRANTED) { pause("다른 오디오가 사용 중이에요. 준비되면 다시 시작하세요."); return; }
        try {
            JSONObject s=current(); baseMs=s.optLong("activeMs"); lastPlaybackHead=0; playbackWrapFrames=0; requestedTarget=s.optInt("targetSpm",180);
            AudioAttributes attr=new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build();
            int buffer=Math.max(2048,AudioTrack.getMinBufferSize(RATE,AudioFormat.CHANNEL_OUT_MONO,AudioFormat.ENCODING_PCM_16BIT));
            AudioTrack output=new AudioTrack.Builder().setAudioAttributes(attr).setAudioFormat(new AudioFormat.Builder().setSampleRate(RATE).setEncoding(AudioFormat.ENCODING_PCM_16BIT).setChannelMask(AudioFormat.CHANNEL_OUT_MONO).build())
                .setTransferMode(AudioTrack.MODE_STREAM).setBufferSizeInBytes(buffer).setPerformanceMode(AudioTrack.PERFORMANCE_MODE_LOW_LATENCY).build();
            if(output.getState()!=AudioTrack.STATE_INITIALIZED){output.release();throw new IllegalStateException("AudioTrack init failed");}
            track=output; synth=new RhythmSynth(RATE,s.optInt("targetSpm",180),s.optInt("beatEverySteps",1),s.optInt("soundId",0),(float)s.optDouble("volume",.65));
            playing=true; long token=++generation, origin=baseMs; RhythmSynth renderer=synth;
            long maxFrames=s.optInt("durationSec",0)==0?Long.MAX_VALUE:Math.max(0,(s.optLong("durationSec")*1000-baseMs)*RATE/1000);
            put(s,"status","running"); put(s,"reason",""); lastCheckpoint=SystemClock.elapsedRealtime();
            if(!wake.isHeld())wake.acquire();
            output.play();
            int initialTarget=requestedTarget;
            new Thread(() -> render(output,renderer,token,origin,maxFrames,initialTarget),"RunBeat-PCM").start(); save(); updateNotification();
        }catch(Exception ex){pause("오디오를 시작하지 못했어요. 다시 시도해 주세요.");save();}
    }
    private void render(AudioTrack output,RhythmSynth renderer,long token,long origin,long maxFrames,int initialTarget) {
        android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_AUDIO);
        short[] block=new short[256]; long lastHead=-1,lastProgress=SystemClock.elapsedRealtime();
        final int[] reportedTarget={initialTarget};
        try {
            while(playing&&generation==token) {
                int count=(int)Math.min(block.length,maxFrames-renderer.frames());
                if(count>0){
                    renderer.render(block,count,(frame,target)->{
                        // Steady playback never waits for checkpoint file I/O.
                        if(target!=reportedTarget[0]){reportedTarget[0]=target;synchronized(LOCK){if(generation==token)changeTarget(target,origin+frame*1000/RATE);}}
                    });
                    int offset=0;
                    while(offset<count&&playing&&generation==token){int written=output.write(block,offset,count-offset,AudioTrack.WRITE_NON_BLOCKING);if(written<0)throw new IOException("AudioTrack write "+written);if(written==0){Thread.sleep(2);}else offset+=written;}
                }else Thread.sleep(4);
                long head=Integer.toUnsignedLong(output.getPlaybackHeadPosition());
                if(head!=lastHead){lastHead=head;lastProgress=SystemClock.elapsedRealtime();}
                else if(SystemClock.elapsedRealtime()-lastProgress>2000)throw new IOException("Audio output stalled");
                if(head>=maxFrames){main.post(()->{synchronized(LOCK){if(generation==token)stopSession(true);}});break;}
            }
        }catch(Exception ex){main.post(()->{synchronized(LOCK){if(generation==token)pause("오디오 출력이 중단되었어요. 다시 시작해 주세요.");}});}
        finally{try{output.release();}catch(Exception ignored){}}
    }
    private void updateTime() {
        AudioTrack output=track;
        if(playing&&output!=null){try{long head=Integer.toUnsignedLong(output.getPlaybackHeadPosition());if(head<lastPlaybackHead&&lastPlaybackHead-head>0x80000000L)playbackWrapFrames+=0x100000000L;lastPlaybackHead=head;long ms=baseMs+(head+playbackWrapFrames)*1000/RATE;int duration=current().optInt("durationSec");if(duration>0)ms=Math.min(ms,duration*1000L);put(current(),"activeMs",ms);}catch(IllegalStateException ignored){}}
    }
    private void halt() {
        updateTime();playing=false;generation++;
        AudioTrack output=track;track=null;
        if(output!=null){try{output.pause();output.flush();}catch(IllegalStateException ignored){}}
        manager.abandonAudioFocusRequest(focus);if(wake.isHeld())wake.release();
    }
    private void pause(String reason) {
        synchronized(LOCK){if(!is("running")&&!is("countdown"))return;halt();closeSegment();changeTarget(requestedTarget,current().optLong("activeMs"));put(current(),"status","paused");put(current(),"reason",reason);save();updateNotification();}
    }
    private void stopSession(boolean completed) {
        synchronized(LOCK){if(!isOpen())return;halt();if(completed)put(current(),"activeMs",current().optLong("durationSec")*1000);put(current(),"status",completed?"completed":"stopped");put(current(),"reason","");finishRecord();media.setActive(false);stopForeground(STOP_FOREGROUND_REMOVE);stopSelf();}
    }
    private PendingIntent action(String name) {
        Intent i=new Intent(this,RunBeatService.class).setAction(name);
        return PendingIntent.getService(this,name.hashCode(),i,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
    }
    private Notification notification() {
        boolean paused=is("paused");long seconds=current().optLong("activeMs")/1000;
        String text=String.format(Locale.KOREA,"%02d:%02d · %s",seconds/60,seconds%60,paused?"일시정지":is("countdown")?"준비 중":"달리는 중");
        Intent launch=getPackageManager().getLaunchIntentForPackage(getPackageName());
        Notification.Builder b=new Notification.Builder(this,CHANNEL).setSmallIcon(com.secondwindgames.runbeat.nativeaudio.R.drawable.ic_runbeat).setContentTitle("런비트 · "+current().optInt("targetSpm",180)+" step/min")
            .setContentText(text).setVisibility(Notification.VISIBILITY_PUBLIC).setOnlyAlertOnce(true).setOngoing(!paused).setShowWhen(false)
            .addAction(new Notification.Action.Builder(paused?android.R.drawable.ic_media_play:android.R.drawable.ic_media_pause,paused?"계속하기":"일시정지",action(paused?"resume":"pause")).build())
            .addAction(new Notification.Action.Builder(android.R.drawable.ic_menu_close_clear_cancel,"종료",action("stop")).build());
        if(launch!=null)b.setContentIntent(PendingIntent.getActivity(this,0,launch,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE));
        if(media!=null)b.setStyle(new Notification.MediaStyle().setMediaSession(media.getSessionToken()).setShowActionsInCompactView(0,1));
        return b.build();
    }
    private void updateNotification() {
        String key=current().optString("status")+current().optInt("targetSpm")+current().optLong("activeMs")/1000;
        if(key.equals(lastNotification))return;lastNotification=key;
        boolean paused=is("paused");
        media.setPlaybackState(new PlaybackState.Builder().setActions(PlaybackState.ACTION_PLAY|PlaybackState.ACTION_PAUSE|PlaybackState.ACTION_STOP|PlaybackState.ACTION_PLAY_PAUSE)
            .setState(paused?PlaybackState.STATE_PAUSED:PlaybackState.STATE_PLAYING,current().optLong("activeMs"),paused?0:1).build());
        media.setMetadata(new MediaMetadata.Builder().putString(MediaMetadata.METADATA_KEY_TITLE,"런비트 · "+current().optInt("targetSpm")+" step/min").putString(MediaMetadata.METADATA_KEY_ARTIST,paused?"일시정지":"나의 리듬으로 달리기").build());
        getSystemService(NotificationManager.class).notify(NOTIFICATION,notification());
    }
    @Override public void onDestroy(){synchronized(LOCK){if(isOpen()){pause("오디오 서비스가 종료되었어요.");}halt();instance=null;}main.removeCallbacksAndMessages(null);try{unregisterReceiver(noisy);}catch(Exception ignored){}if(media!=null)media.release();super.onDestroy();}
    @Override public IBinder onBind(Intent i){return null;}
}
