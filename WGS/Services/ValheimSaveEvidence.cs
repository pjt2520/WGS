using System.IO;

namespace WGS.Services;

/// <summary>File activity is supporting evidence, never proof of a consistent world save.</summary>
public static class ValheimSaveEvidence
{
    public sealed record Snapshot(Dictionary<string, (long Size, DateTime Written)> Files, bool Complete);
    public static Snapshot Capture(string root)
    {
        var files = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                return new(files, false);
            foreach (var path in Directory.EnumerateFiles(root, "*", new EnumerationOptions
                     { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                // Do not follow links to external save trees.
                var info = new FileInfo(path);
                files[path] = (info.Length, info.LastWriteTimeUtc);
            }
            return new(files, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new(files, false); }
    }
    public static string DescribeChanges(Snapshot before, Snapshot after)
    {
        if (!before.Complete || !after.Complete)
            return "World-save activity unknown: configured saves directory was missing or unreadable. Verify the game's save logs.";
        var changed = after.Files.Count(kv => !before.Files.TryGetValue(kv.Key, out var old) || old != kv.Value);
        return changed > 0
            ? $"Observed {changed} changed/new file(s) under configured saves directory during Stop. File activity alone does not prove save integrity."
            : "No file changes observed under configured saves directory during Stop. Final world save is unverified.";
    }
}
