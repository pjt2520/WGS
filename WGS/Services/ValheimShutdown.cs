using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace WGS.Services;

public static class ValheimShutdown
{
    public const int TimeoutSeconds = 120;
    public static string HelperPath => Path.Combine(AppContext.BaseDirectory, "WGS.ValheimControl.exe");
    public static void RequireHelper()
    {
        if (!File.Exists(HelperPath))
            throw new FileNotFoundException("Valheim console helper is missing. Build/publish the complete preview package before starting Valheim.", HelperPath);
    }

    public static async Task RequestAsync(Process process, Action<string> log, TimeSpan? exitTimeout = null)
    {
        if (process.HasExited) return;
        RequireHelper();
        var info = new ProcessStartInfo(HelperPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add(process.Id.ToString(CultureInfo.InvariantCulture));
        info.ArgumentList.Add(process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
        using var helper = Process.Start(info) ?? throw new InvalidOperationException("Could not launch Valheim console helper.");
        var error = helper.StandardError.ReadToEndAsync();
        try { await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (TimeoutException)
        {
            // Kill only the short-lived helper, NEVER the game or its tree.
            try { helper.Kill(); } catch (InvalidOperationException) { }
            throw new InvalidOperationException("Console helper timed out. Valheim was not force-killed.");
        }
        if (process.HasExited) return;
        if (helper.ExitCode != 0)
            throw new InvalidOperationException("Ctrl+C refused: " + (await error).Trim() +
                " Valheim remains running. Older no-console instances require a separate shutdown plan.");
        log("Ctrl+C dispatched to isolated Valheim console. Waiting for process exit...");
        try { await process.WaitForExitAsync().WaitAsync(exitTimeout ?? TimeSpan.FromSeconds(TimeoutSeconds)); }
        catch (TimeoutException)
        {
            throw new InvalidOperationException($"Valheim did not exit within {(exitTimeout ?? TimeSpan.FromSeconds(TimeoutSeconds)).TotalSeconds}s. Stop failed; no force kill was attempted.");
        }
    }
}
