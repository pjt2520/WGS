using System.IO;

namespace WGS.Services;

/// <summary>
/// Cross-process ownership guard for the managed Tangerang home.
///
/// Exactly one engine may own Data/Servers at a time. The ServiceHost holds
/// an exclusive FileStream for its entire lifetime, not merely a marker file
/// that would remain stale after power loss. The GUI may inspect ownership
/// before initializing its own ServerManagerService and reject local mode.
///
/// Does not require administrator rights and never deletes the world files.
/// </summary>
public static class TangerangServiceOwnership
{
    public static string LockFile(string dataDirectory) =>
        Path.Combine(dataDirectory, "service-owner.lock");

    /// <summary>Null means another process holds the lock.</summary>
    public static FileStream? TryAcquire(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        try
        {
            return new FileStream(LockFile(dataDirectory), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// A file remaining on disk after a crash is not an active lock. Only
    /// a second process being denied exclusive access means live ownership.
    /// </summary>
    public static bool IsOwned(string dataDirectory)
    {
        if (!File.Exists(LockFile(dataDirectory))) return false;
        using var probe = TryAcquire(dataDirectory);
        return probe is null;
    }
}
