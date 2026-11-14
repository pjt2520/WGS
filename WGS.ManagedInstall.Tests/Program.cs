using System.IO;
using WGS.Services;

// These are pure path-policy tests. They NEVER touch the user's actual game
// directories or start a server; CI runs them on a clean Windows agent.
int passed = 0, failed = 0;
void Check(string name, bool ok)
{
    if (ok) { passed++; Console.WriteLine("PASS " + name); }
    else { failed++; Console.Error.WriteLine("FAIL " + name); }
}

var exe = Path.Combine(Path.GetTempPath(), "compiled-tangerang", "App");
var root = Path.Combine(Path.GetTempPath(), "tangerang-owned-state");
var resolved = TangerangManagedPaths.ResolveHome(root, false);

Check("Managed root resolves absolute path",
    resolved == Path.GetFullPath(root));
Check("Data is separated from app binaries",
    TangerangManagedPaths.DataDir(exe, false, resolved) == Path.Combine(root, "Data"));
Check("Game servers are separated from app binaries",
    TangerangManagedPaths.ServersDir(exe, false, resolved) == Path.Combine(root, "Servers"));
Check("World backups are separated from app binaries",
    TangerangManagedPaths.BackupsDir(exe, false, resolved) == Path.Combine(root, "Backups"));
Check("Managed folders aren't descendants of App",
    !TangerangManagedPaths.ServersDir(exe, false, resolved).StartsWith(exe + Path.DirectorySeparatorChar));
Check("No managed root keeps original WGS_Data",
    TangerangManagedPaths.DataDir(exe, false, null) == Path.Combine(exe, "WGS_Data"));
Check("No managed root keeps original servers",
    TangerangManagedPaths.ServersDir(exe, false, null) == Path.Combine(exe, "servers"));
Check("No managed root keeps original backups",
    TangerangManagedPaths.BackupsDir(exe, false, null) == Path.Combine(exe, "backups"));

var ignoredHome = TangerangManagedPaths.ResolveHome(root, true);
Check("Developer sandbox ignores managed root", ignoredHome == null);
Check("Developer sandbox retains isolated data",
    TangerangManagedPaths.DataDir(exe, true, ignoredHome) ==
        Path.Combine(exe, "Tangerang_DevData"));
Check("Developer sandbox retains isolated servers",
    TangerangManagedPaths.ServersDir(exe, true, ignoredHome) ==
        Path.Combine(exe, "Tangerang_DevServers"));
Check("Developer sandbox retains isolated backups",
    TangerangManagedPaths.BackupsDir(exe, true, ignoredHome) ==
        Path.Combine(exe, "Tangerang_DevBackups"));

bool Rejects(string path)
{
    try { _ = TangerangManagedPaths.ResolveHome(path, false); return false; }
    catch (ArgumentException) { return true; }
}
Check("Relative managed root is rejected", Rejects("my-server-data"));
Check("Drive root is rejected", Rejects(Path.GetPathRoot(root)!));
Check("Blank managed root preserves legacy fallback",
    TangerangManagedPaths.ResolveHome(" ", false) == null);

Console.WriteLine($"Results: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
