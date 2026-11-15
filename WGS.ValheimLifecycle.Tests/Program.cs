using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using WGS.Models;
using WGS.Services;

// Run in this test project's private bin folder, never the managed installation.
// The fake game is this executable copied beside itself as valheim_server.exe.
if (args.Contains("-nographics"))
{
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; Console.WriteLine("FIXTURE_SAVED"); Console.Out.Flush(); Environment.Exit(0); };
    // Registering CancelKeyPress can initialize a console in the runtime.
    // Detach afterwards so this fixture really models a console-less process.
    if (args.Contains("--no-console")) NativeConsole.FreeConsole();
    Console.WriteLine("FIXTURE_READY"); Console.Out.Flush();
    await Task.Delay(Timeout.Infinite);
    return;
}
Environment.SetEnvironmentVariable("TANGERANG_WGS_DEV", "1");
Environment.SetEnvironmentVariable("TANGERANG_WGS_HOME", null);
var exe = Environment.ProcessPath!;
if (!exe.EndsWith("WGS.ValheimLifecycle.Tests.exe", StringComparison.OrdinalIgnoreCase))
    throw new Exception("Run the built test EXE directly.");
var fixtureExe = Path.Combine(AppContext.BaseDirectory, "valheim_server.exe");
File.Copy(exe, fixtureExe, overwrite: true);
var config = new ConfigService { OptimizeRamBeforeStart = false };
using var network = new NetworkMonitorService();
var manager = new ServerManagerService(config, network);
int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); passed++; }
GameServer Server() => new()
{
    GameId = "valheim", InstallPath = AppContext.BaseDirectory, ServerPassword = "fixture-only",
    FirewallAutoManage = false, AutoRestart = false, DailyRestartEnabled = false,
    ServerPort = 29951, QueryPort = 29952, BackupOnShutdown = true
};
async Task WaitReady(GameServer s)
{
    var deadline = DateTime.UtcNow.AddSeconds(15);
    while (DateTime.UtcNow < deadline)
    {
        if (manager.GetLogInstance(s.Id)?.GetLogSnapshot().Any(m => m.Text == "FIXTURE_READY") == true) return;
        await Task.Delay(100);
    }
    throw new Exception("Fixture startup timed out");
}
var server = Server();
Process? legacy = null;
try
{
    await manager.StartAsync(server); await WaitReady(server);
    var firstPid = server.RunningPid;
    bool refused = false;
    try { await manager.StartAsync(server); } catch (InvalidOperationException) { refused = true; }
    Check(refused && manager.IsRunning(server.Id) && server.RunningPid == firstPid, "duplicate Start preserves existing process");
    server.AutoRestart = true; server.AutoRestartDelaySec = 1;
    await manager.StopAsync(server);
    await Task.Delay(1500);
    Check(!manager.IsRunning(server.Id) && server.RunningPid == 0, "intentional Stop does not trigger crash restart");
    Check(manager.GetLogInstance(server.Id)!.GetLogSnapshot().Any(m => m.Text == "FIXTURE_SAVED"), "final game output remains available after Stop");

    legacy = Process.Start(new ProcessStartInfo(fixtureExe, "-nographics --no-console")
    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
    await legacy.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
    var old = Server(); old.RunningPid = legacy.Id;
    Check(manager.TryReattach(old), "legacy process identity reattached");
    refused = false;
    try { await manager.StopAsync(old); } catch (InvalidOperationException) { refused = true; }
    Check(refused && manager.IsRunning(old.Id) && old.Status == ServerStatus.Running, "refused legacy Stop preserves running status and tracking");
    // Dependencies after Stop are deliberately absent: if backup/update proceeds,
    // this test fails with NullReferenceException instead of the Stop refusal.
    var backend = new LocalServerBackend(manager, null!, null!, null!, config, null!, null!, null!, null!, null!, null!, null!, null!, null!);
    refused = false;
    try { await backend.StopAsync(old); } catch (InvalidOperationException) { refused = true; }
    Check(refused && !legacy.HasExited, "shutdown backup cannot proceed after refused Stop");
    refused = false;
    try { await backend.InstallAsync(old); } catch (InvalidOperationException) { refused = true; }
    Check(refused && !legacy.HasExited, "update cannot proceed after refused Stop");
    manager.KillAll();
    Check(!legacy.HasExited && manager.IsRunning(old.Id), "host cleanup preserves Valheim after failed Stop");
    await manager.KillAsync(old);
    await legacy.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    Check(!manager.IsRunning(old.Id), "explicit Kill remains available");
}
finally
{
    if (manager.IsRunning(server.Id)) await manager.KillAsync(server);
    if (legacy is { HasExited: false }) legacy.Kill();
    legacy?.Dispose();
    File.Delete(fixtureExe);
}
Console.WriteLine($"{passed} lifecycle checks passed using only synthetic game processes.");

internal static class NativeConsole
{
    [DllImport("kernel32.dll")]
    internal static extern bool FreeConsole();
}
