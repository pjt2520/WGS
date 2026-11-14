using System.IO;

namespace WGS.Services;

/// <summary>
/// Decides where durable Tangerang state lives without touching the filesystem.
///
/// A compiled app can be replaced any number of times while Data, Servers and
/// Backups remain in a separate, stable location. This makes updates and
/// rollback possible without moving Valheim worlds or Discord credentials.
///
/// TANGERANG_WGS_DEV=1 takes precedence and preserves the isolated test
/// sandbox behavior. TANGERANG_WGS_HOME=<absolute path> is the explicit
/// managed-install opt-in. With neither set, original WGS directory behavior
/// remains unchanged for backwards compatibility.
/// </summary>
public static class TangerangManagedPaths
{
    public static string? ResolveHome(string? requestedHome, bool isDev)
    {
        if (isDev || string.IsNullOrWhiteSpace(requestedHome))
            return null;

        string home = requestedHome.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(home))
            throw new ArgumentException(
                "TANGERANG_WGS_HOME must be an absolute directory path.", nameof(requestedHome));

        home = Path.GetFullPath(home);
        var driveRoot = Path.GetPathRoot(home);
        if (string.Equals(home.TrimEnd(Path.DirectorySeparatorChar),
                driveRoot?.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "TANGERANG_WGS_HOME must not be a drive root. Choose a dedicated folder.",
                nameof(requestedHome));
        return home;
    }

    public static string DataDir(string exeDir, bool isDev, string? home)
        => isDev ? Path.Combine(exeDir, "Tangerang_DevData")
           : home is not null ? Path.Combine(home, "Data")
           : Path.Combine(exeDir, "WGS_Data");

    public static string ServersDir(string exeDir, bool isDev, string? home)
        => isDev ? Path.Combine(exeDir, "Tangerang_DevServers")
           : home is not null ? Path.Combine(home, "Servers")
           : Path.Combine(exeDir, "servers");

    public static string BackupsDir(string exeDir, bool isDev, string? home)
        => isDev ? Path.Combine(exeDir, "Tangerang_DevBackups")
           : home is not null ? Path.Combine(home, "Backups")
           : Path.Combine(exeDir, "backups");
}
