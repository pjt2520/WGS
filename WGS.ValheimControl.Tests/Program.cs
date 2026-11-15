using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using WGS.Services;

// Synthetic processes only: never opens a game installation or saved world.
if (args.Length > 0 && (args[0] == "--fixture" || args[0] == "-nographics"))
{
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        if (args.Contains("--ignore")) return;
        Console.WriteLine("FIXTURE_SAVED");
        Console.Out.Flush();
        Environment.Exit(0);
    };
    if (args.Contains("--shared"))
    {
        var shared = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--fixture")
        { UseShellExecute = false, CreateNoWindow = false, RedirectStandardOutput = true })!;
        await shared.StandardOutput.ReadLineAsync();
    }
    Console.WriteLine("FIXTURE_READY"); Console.Out.Flush();
    await Task.Delay(Timeout.Infinite);
    return 0;
}
int passed = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception("FAIL " + name);
    Console.WriteLine("PASS " + name); passed++;
}
var saveRoot = Path.Combine(Path.GetTempPath(), "WGS-save-evidence-" + Guid.NewGuid());
Directory.CreateDirectory(saveRoot);
try
{
    var before = ValheimSaveEvidence.Capture(saveRoot);
    Check(ValheimSaveEvidence.DescribeChanges(before, before).Contains("unverified"), "unchanged saves never imply success");
    File.WriteAllText(Path.Combine(saveRoot, "test.db2"), "fixture");
    var after = ValheimSaveEvidence.Capture(saveRoot);
    Check(ValheimSaveEvidence.DescribeChanges(before, after).Contains("1 changed/new"), "new world file detected");
    Check(ValheimSaveEvidence.DescribeChanges(before, after).Contains("does not prove"), "file activity not overstated as integrity verification");
    var missing = ValheimSaveEvidence.Capture(Path.Combine(saveRoot, "missing"));
    Check(!missing.Complete && ValheimSaveEvidence.DescribeChanges(before, missing).Contains("unknown"), "missing saves reported unknown");
}
finally { Directory.Delete(saveRoot, true); }
if (!OperatingSystem.IsWindows())
{
    Console.WriteLine($"{passed} portable checks passed. SKIP Windows console integration tests: Windows required.");
    return 0;
}
var executable = Environment.ProcessPath!;
if (!executable.EndsWith("WGS.ValheimControl.Tests.exe", StringComparison.OrdinalIgnoreCase))
    throw new Exception("Run the built test EXE directly, not dotnet test.dll.");
ValheimShutdown.RequireHelper();
ProcessStartInfo Fixture(bool noConsole = true) => new(executable, "--fixture")
{
    WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false,
    CreateNoWindow = noConsole, RedirectStandardOutput = true, RedirectStandardError = true
};
async Task<int> Signal(Process process, long ticks)
{
    var start = new ProcessStartInfo(ValheimShutdown.HelperPath)
    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
    start.ArgumentList.Add(process.Id.ToString(CultureInfo.InvariantCulture));
    start.ArgumentList.Add(ticks.ToString(CultureInfo.InvariantCulture));
    using var helper = Process.Start(start)!;
    await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    return helper.ExitCode;
}
using var a = IsolatedConsoleProcess.StartSuspended(Fixture());
using var b = IsolatedConsoleProcess.StartSuspended(Fixture());
var linesA = new ConcurrentQueue<string>();
var readyA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var readyB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
try
{
    a.Resume(line => { linesA.Enqueue(line); if (line == "FIXTURE_READY") readyA.TrySetResult(); }, Console.Error.WriteLine);
    b.Resume(line => { if (line == "FIXTURE_READY") readyB.TrySetResult(); }, Console.Error.WriteLine);
    await Task.WhenAll(readyA.Task, readyB.Task).WaitAsync(TimeSpan.FromSeconds(15));
    Check(!a.Process.HasExited && !b.Process.HasExited, "two isolated fixtures running");
    Check(await Signal(a.Process, 1) != 0 && !a.Process.HasExited, "recycled/stale identity refused");
    await ValheimShutdown.RequestAsync(a.Process, Console.WriteLine);
    await a.OutputCompletion.WaitAsync(TimeSpan.FromSeconds(5));
    Check(a.Process.HasExited && linesA.Contains("FIXTURE_SAVED"), "Ctrl+C handler finished and final output captured");
    Check(!b.Process.HasExited, "other console unaffected");
    await ValheimShutdown.RequestAsync(b.Process, Console.WriteLine);
    Check(b.Process.HasExited, "second isolated fixture stopped independently");

    foreach (var scenario in new[] { "--ignore", "--shared" })
    {
        var info = Fixture(); info.Arguments += " " + scenario;
        using var stubborn = IsolatedConsoleProcess.StartSuspended(info);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            stubborn.Resume(line => { if (line == "FIXTURE_READY") ready.TrySetResult(); }, Console.Error.WriteLine);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            bool refused = false;
            try { await ValheimShutdown.RequestAsync(stubborn.Process, Console.WriteLine, TimeSpan.FromSeconds(1)); }
            catch (InvalidOperationException) { refused = true; }
            Check(refused && !stubborn.Process.HasExited, scenario + " stop fails without killing target");
        }
        finally { if (!stubborn.Process.HasExited) stubborn.Process.Kill(entireProcessTree: true); }
    }

    using var legacy = Process.Start(Fixture())!;
    try
    {
        Check(await legacy.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) == "FIXTURE_READY", "legacy fixture ready");
        Check(await Signal(legacy, legacy.StartTime.ToUniversalTime().Ticks) != 0, "no-console process refuses signal");
        Check(!legacy.HasExited, "refused stop leaves legacy process running");
    }
    finally { if (!legacy.HasExited) legacy.Kill(); }
}
finally
{
    // Only test-owned synthetic children, never any process-name sweep.
    if (!a.Process.HasExited) a.Process.Kill();
    if (!b.Process.HasExited) b.Process.Kill();
}
Console.WriteLine($"{passed} Windows isolation checks passed.");
return 0;
