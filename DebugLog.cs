namespace OldColoredKeycards;

// Extra logging for testing, switched on by debugLogging. Off by default so the server console stays quiet.
internal static class DebugLog
{
    private static Action<string>? _write;

    public static void Bind(ConfigManager configManager, Action<string> write)
    {
        _write = configManager.Mod.DebugLogging ? write : null;
    }

    public static void Write(Func<string> message)
    {
        _write?.Invoke("[OldColoredKeycards] " + message());
    }
}
