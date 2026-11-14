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

    public static bool IsManagedTangerang => Services.ConfigService.UsesManagedData;

    // Explicitly label our manager so it is never confused with the
    // disposable developer build or an upstream WGS installation.
    public static string VersionDisplay => IsDeveloperSandbox
        ? $"v{Version} · Tangerang DEV"
        : IsManagedTangerang ? $"v{Version} · Tangerang Preview" : $"v{Version}";
    public static string VersionTag => $"v{Version}  ·  2026 © MadBee71";
}
