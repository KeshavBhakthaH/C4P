using Android.Content;

namespace A2dpRemote;

public static class Prefs
{
    private const string PrefName = "a2dp_remote_prefs";
    private const string LogKey = "log_tail";

    public static string? Get(Context context, string key, string? fallback = null)
    {
        var prefs = context.GetSharedPreferences(PrefName, FileCreationMode.Private);
        return prefs?.GetString(key, fallback);
    }

    public static void Set(Context context, string key, string value)
    {
        var prefs = context.GetSharedPreferences(PrefName, FileCreationMode.Private);
        prefs?.Edit()?.PutString(key, value)?.Apply();
    }

    public static void AppendLog(Context context, string line)
    {
        var existing = Get(context, LogKey, string.Empty) ?? string.Empty;
        var tail = $"{line}\n{existing}";
        if (tail.Length > 4000)
            tail = tail[..4000];
        Set(context, LogKey, tail);
    }
}
