# AndroidRunEngine calls this API through Unity's AndroidJavaClass (JNI).
# Keep the class name and only its C# entry points; other code can still shrink.
-keep, includedescriptorclasses class com.secondwindgames.runbeat.RunBeatService {
    public static void initialize(android.content.Context);
    public static java.lang.String snapshot(android.content.Context);
    public static void command(android.content.Context, java.lang.String, java.lang.String);
    public static boolean openMusic(android.app.Activity, java.lang.String);
    public static void notificationSettings(android.app.Activity);
}
