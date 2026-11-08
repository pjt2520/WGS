namespace WGS;

public static class AppInfo
{
    public static string Version =>
        System.Reflection.Assembly.GetExecutingAssembly()
              .GetName().Version?.ToString(4) ?? "?";

    /// <summary>
    /// True only for the opt-in isolated Tangerang development launcher.
    /// Used by the window chrome to distinguish the sandbox from production.
    /// </summary>
    public static bool IsDeveloperSandbox
        => Environment.GetEnvironmentVariable("TANGERANG_WGS_DEV") == "1";

    public static string VersionDisplay => $"v{Version}";
    public static string VersionTag => $"v{Version}  ·  2026 © MadBee71";
}
