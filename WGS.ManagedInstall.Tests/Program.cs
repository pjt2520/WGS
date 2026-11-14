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

// Tangerang Service Foundation: no live game processes or external services.
// Locks use only a temporary test directory that is deleted when complete.
var serviceHome = Path.Combine(Path.GetTempPath(),
    "tangerang-lock-test-" + Guid.NewGuid().ToString("N"));
try
{
    Check("No active service owner before first lock",
        !TangerangServiceOwnership.IsOwned(serviceHome));
    using (var owner = TangerangServiceOwnership.TryAcquire(serviceHome))
    {
        Check("Service can acquire exclusive engine ownership", owner is not null);
        Check("Second service cannot acquire the same Data directory",
            TangerangServiceOwnership.TryAcquire(serviceHome) is null);
        Check("Detached GUI detects that service owns worlds",
            TangerangServiceOwnership.IsOwned(serviceHome));
        Check("Local GUI denied while service owns worlds",
            !TangerangServiceBootPolicy.MayLaunchLocalGui(false, true));
        Check("Service-mode GUI may attach without local ownership",
            TangerangServiceBootPolicy.MayLaunchLocalGui(true, true));
    }
    Check("Stale lock file after clean stop does not imply live owner",
        !TangerangServiceOwnership.IsOwned(serviceHome));
}
finally
{
    if (Directory.Exists(serviceHome))
        Directory.Delete(serviceHome, recursive: true);
}

var bootServer = new WGS.Models.GameServer
{
    GameId = "valheim",
    InstallPath = @"C:\Games\Valheim",
    AutoStart = false
};
Check("Service does not auto-start servers by default",
    !TangerangServiceBootPolicy.ShouldAutoStart(bootServer, false));
bootServer.AutoStart = true;
Check("Explicit AutoStart server eligible on engine boot",
    TangerangServiceBootPolicy.ShouldAutoStart(bootServer, false));
Check("Reattached running game is never started twice",
    !TangerangServiceBootPolicy.ShouldAutoStart(bootServer, true));
Check("Missing install path prevents accidental autostart",
    !TangerangServiceBootPolicy.ShouldAutoStart(
        new WGS.Models.GameServer { GameId = "valheim", AutoStart = true }, false));
Check("Service API address defaults to loopback",
    TangerangServiceBootPolicy.LocalApiUrl == "http://localhost:8766");

const string serviceResponse = """
[
  { "Id": "valheim-1", "Status": "Running", "CurrentPlayers": 0, "PlayerCountUnknown": true },
  { "Id": "palworld-1", "Status": "Stopped", "CurrentPlayers": 0, "PlayerCountUnknown": false }
]
""";
var remote = TangerangServiceStatusParser.Parse(serviceResponse);
Check("Service feed contains two game statuses", remote.Count == 2);
Check("Valheim crossplay unknown count remains explicitly unknown",
    remote[0].Id == "valheim-1" &&
    remote[0].Status == WGS.Models.ServerStatus.Running &&
    remote[0].PlayerCountUnknown);
Check("Stopped Palworld status is parsed",
    remote[1].Status == WGS.Models.ServerStatus.Stopped);
var malformed = TangerangServiceStatusParser.Parse(
    """[{ "Id":"one", "Status":"Running" }, { "Id":"two", "Status":"WAT" }]""");
Check("Missing count is unknown, not an invented zero",
    malformed.Count == 1 && malformed[0].PlayerCountUnknown);
Check("Malformed remote status is rejected rather than guessing",
    malformed.Count == 1);

bool InvalidPayload()
{
    try { TangerangServiceStatusParser.Parse("""{"wrong":"object"}"""); return false; }
    catch (FormatException) { return true; }
}
Check("Non-array server status cannot overwrite the GUI model", InvalidPayload());

Console.WriteLine($"Results: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
