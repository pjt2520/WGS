using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

// This helper never launches or kills a game. Console attachment is process-wide,
// so it must not happen inside the service (which owns multiple game instances).
if (!OperatingSystem.IsWindows()) return 2;
if (args.Length != 2 || !int.TryParse(args[0], out var pid) ||
    !long.TryParse(args[1], CultureInfo.InvariantCulture, out var ticks)) return 2;
try
{
    using var target = Process.GetProcessById(pid);
    if (target.StartTime.ToUniversalTime().Ticks != ticks || target.HasExited) return 3;
    Native.FreeConsole();
    if (!Native.AttachConsole((uint)pid)) throw new Win32Exception();
    try
    {
        if (!Native.SetConsoleCtrlHandler(IntPtr.Zero, true)) throw new Win32Exception();
        var members = new uint[16];
        var count = Native.GetConsoleProcessList(members, (uint)members.Length);
        if (count != 2 || !members.Take((int)count).Contains((uint)pid) ||
            !members.Take((int)count).Contains((uint)Environment.ProcessId))
            throw new InvalidOperationException("Refusing Ctrl+C: console is not isolated to the target and helper.");
        if (target.HasExited || target.StartTime.ToUniversalTime().Ticks != ticks) return 3;
        // CTRL_C cannot be targeted to a PID/group. Broadcast only inside the
        // freshly checked isolated console, never the service's own console.
        if (!Native.GenerateConsoleCtrlEvent(0, 0)) throw new Win32Exception();
        Thread.Sleep(250); // let Windows dispatch before detaching
        return 0;
    }
    finally { Native.FreeConsole(); }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

internal static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll")] internal static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint GetConsoleProcessList(uint[] ids, uint count);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
}
