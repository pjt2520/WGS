using System.Reflection;
using WGS.Games;
using WGS.Models;
using WGS.Services;

// Tangerang regression checks for the Valheim crossplay freeze-alert guard.
// This is deliberately an executable with no new NuGet dependencies.
// It never launches a game server, connects to PlayFab, or accesses saved worlds.
//
// Reflection is limited to the private policy predicate. If that method is
// refactored, change this test to use the replacement decision point.
var policy = typeof(ServerHealthService).GetMethod(
    "CanHealthCheck", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("CanHealthCheck predicate not found.");

int passed = 0;
int failed = 0;

void Check(string scenario, IGamePlugin plugin, string? crossplay, bool expected)
{
    var server = new GameServer
    {
        GameId = plugin.GameId,
        ServerPort = 2456
    };
    if (crossplay is not null)
        server.GameSpecificSettings["crossplay"] = crossplay;

    var result = (bool)(policy.Invoke(null, [server, plugin])
        ?? throw new InvalidOperationException("Health policy returned null."));

    if (result == expected)
    {
        passed++;
        Console.WriteLine($"PASS {scenario}");
    }
    else
    {
        failed++;
        Console.Error.WriteLine(
            $"FAIL {scenario}: expected health probe={expected}, actual={result}");
    }
}

var valheim = new ValheimPlugin();
// A PlayFab Valheim session must NOT be sent an A2S freeze check.
Check("Valheim: crossplay=true skips A2S freeze probe", valheim, "true", false);

// The Steam-backed modes must preserve existing active monitoring.
Check("Valheim: crossplay=false retains A2S probe", valheim, "false", true);
Check("Valheim: absent crossplay setting retains A2S probe", valheim, null, true);
Check("Valheim: blank crossplay setting retains A2S probe", valheim, "", true);

// The actual launcher checks the literal value 'true', not case-insensitively.
// Thus uppercase TRUE does not produce -crossplay and should keep A2S checks.
Check("Valheim: uppercase TRUE matches launch behavior", valheim, "TRUE", true);

// Other games still use their normal A2S checks, even if a custom setting is
// coincidentally called crossplay.
Check("Counter-Strike 2: A2S remains enabled", new CS2Plugin(), "true", true);

// Validate the developer sandbox selection without constructing ConfigService
// or touching disks. Reading the private static path is intentionally a
// lightweight regression test for the opt-in environment variable.
var previousDevMode = Environment.GetEnvironmentVariable("TANGERANG_WGS_DEV");
try
{
    Environment.SetEnvironmentVariable("TANGERANG_WGS_DEV", "1");
    var dataFolder = typeof(ConfigService).GetField("DataDir",
        BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as string;
    if (dataFolder is not null
        && string.Equals(System.IO.Path.GetFileName(dataFolder),
            "Tangerang_DevData", StringComparison.OrdinalIgnoreCase))
    {
        passed++;
        Console.WriteLine("PASS Developer mode chooses isolated WGS data folder");
    }
    else
    {
        failed++;
        Console.Error.WriteLine("FAIL Developer mode data directory isolation");
    }
}
finally
{
    Environment.SetEnvironmentVariable("TANGERANG_WGS_DEV", previousDevMode);
}

// SteamCMD may update itself during a brand-new install and return this
// specific app-catalogue error. The installer retries once for this case only.
// Do not turn unrelated failures (permissions, disks, subscription) into
// repeated download attempts.
var isMissing = typeof(SteamCmdService).GetMethod(
    "IsMissingAppConfiguration", BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("SteamCMD metadata error classifier not found.");

void SteamCase(string name, string message, int appId, bool expected)
{
    var actual = (bool)(isMissing.Invoke(null, [message, appId]) ?? false);
    if (actual == expected)
    {
        passed++;
        Console.WriteLine("PASS " + name);
    }
    else
    {
        failed++;
        Console.Error.WriteLine("FAIL " + name);
    }
}

SteamCase("SteamCMD recognizes matching app metadata failure",
    "ERROR! Failed to install app '896660' (Missing configuration)", 896660, true);
SteamCase("SteamCMD doesn't retry a disk write failure",
    "ERROR! Failed to install app '896660' (Disk write failure)", 896660, false);
SteamCase("SteamCMD doesn't retry a different app error",
    "ERROR! Failed to install app '2394010' (Missing configuration)", 896660, false);
SteamCase("SteamCMD doesn't retry a subscription error",
    "ERROR! Failed to install app '896660' (No subscription)", 896660, false);

Console.WriteLine($"Results: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
