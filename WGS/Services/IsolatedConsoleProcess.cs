using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WGS.Services;

/// <summary>Creates a hidden, private Windows console while retaining redirected
/// output. ProcessStartInfo.CreateNoWindow cannot provide this combination.</summary>
public sealed class IsolatedConsoleProcess : IDisposable
{
    public Process Process { get; }
    private readonly StreamReader output;
    private readonly StreamReader error;
    private readonly SafeFileHandle input;
    private IntPtr thread;
    public bool IsSuspended => thread != IntPtr.Zero;
    public Task OutputCompletion { get; private set; } = Task.CompletedTask;

    private IsolatedConsoleProcess(Process process, StreamReader stdout, StreamReader stderr,
        SafeFileHandle stdin, IntPtr suspendedThread)
        => (Process, output, error, input, thread) = (process, stdout, stderr, stdin, suspendedThread);

    public static IsolatedConsoleProcess StartSuspended(ProcessStartInfo info)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!Path.IsPathFullyQualified(info.FileName) || !File.Exists(info.FileName))
            throw new FileNotFoundException("Isolated console requires an absolute executable path.", info.FileName);
        var handles = new List<SafeFileHandle>();
        IntPtr attributes = IntPtr.Zero, handleList = IntPtr.Zero, environment = IntPtr.Zero;
        ProcessInformation pi = default;
        bool initialized = false;
        try
        {
            var sa = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Inherit = true };
            void Pipe(out SafeFileHandle read, out SafeFileHandle write, bool parentReads)
            {
                if (!CreatePipe(out read, out write, ref sa, 0)) throw new Win32Exception();
                handles.Add(read); handles.Add(write);
                if (!SetHandleInformation(parentReads ? read : write, 1, 0)) throw new Win32Exception();
            }
            Pipe(out var stdoutRead, out var stdoutWrite, true);
            Pipe(out var stderrRead, out var stderrWrite, true);
            Pipe(out var stdinRead, out var stdinWrite, false);
            nuint size = 0;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributes = Marshal.AllocHGlobal(checked((int)size));
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw new Win32Exception();
            initialized = true;
            handleList = Marshal.AllocHGlobal(IntPtr.Size * 3);
            Marshal.WriteIntPtr(handleList, 0, stdinRead.DangerousGetHandle());
            Marshal.WriteIntPtr(handleList, IntPtr.Size, stdoutWrite.DangerousGetHandle());
            Marshal.WriteIntPtr(handleList, IntPtr.Size * 2, stderrWrite.DangerousGetHandle());
            if (!UpdateProcThreadAttribute(attributes, 0, (nuint)0x20002, handleList,
                    (nuint)(IntPtr.Size * 3), IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
            var si = new StartupInfoEx
            {
                Info = new StartupInfo
                {
                    Size = Marshal.SizeOf<StartupInfoEx>(), Flags = 0x100 | 1, ShowWindow = 0,
                    Input = stdinRead.DangerousGetHandle(), Output = stdoutWrite.DangerousGetHandle(),
                    Error = stderrWrite.DangerousGetHandle()
                },
                Attributes = attributes
            };
            environment = Marshal.StringToHGlobalUni(string.Join('\0', info.Environment
                .Where(kv => kv.Value != null).OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => kv.Key + "=" + kv.Value)) + "\0\0");
            var command = new StringBuilder("\"" + info.FileName + "\" " + info.Arguments);
            // NEW_CONSOLE, SUSPENDED, UNICODE_ENVIRONMENT, EXTENDED_STARTUPINFO_PRESENT.
            // Do NOT use NEW_PROCESS_GROUP: that disables Ctrl+C in the child.
            if (!CreateProcess(info.FileName, command, IntPtr.Zero, IntPtr.Zero, true,
                    0x10 | 0x4 | 0x400 | 0x80000, environment, info.WorkingDirectory, ref si, out pi))
                throw new Win32Exception();
            var process = Process.GetProcessById((int)pi.ProcessId);
            var result = new IsolatedConsoleProcess(process,
                new StreamReader(new FileStream(stdoutRead, FileAccess.Read), Encoding.UTF8),
                new StreamReader(new FileStream(stderrRead, FileAccess.Read), Encoding.UTF8), stdinWrite, pi.Thread);
            handles.Remove(stdoutRead); handles.Remove(stderrRead); handles.Remove(stdinWrite);
            pi.Thread = IntPtr.Zero;
            return result;
        }
        catch
        {
            // Creation failed before ownership was handed to the caller. The
            // child is still suspended and has never loaded a world.
            if (pi.Process != IntPtr.Zero) TerminateProcess(pi.Process, 1);
            throw;
        }
        finally
        {
            foreach (var handle in handles) handle.Dispose();
            if (pi.Process != IntPtr.Zero) CloseHandle(pi.Process);
            if (pi.Thread != IntPtr.Zero) CloseHandle(pi.Thread);
            if (initialized) DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            if (handleList != IntPtr.Zero) Marshal.FreeHGlobal(handleList);
            if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
        }
    }

    public void Resume(Action<string> stdout, Action<string> stderr)
    {
        OutputCompletion = Task.WhenAll(Pump(output, stdout), Pump(error, stderr));
        if (ResumeThread(thread) == uint.MaxValue) throw new Win32Exception();
        CloseHandle(thread); thread = IntPtr.Zero;
    }
    private static Task Pump(StreamReader reader, Action<string> log) => Task.Run(async () =>
    {
        try { while (await reader.ReadLineAsync() is { } line) log(line); }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    });
    public void Dispose()
    {
        if (thread != IntPtr.Zero)
        {
            // Uncommitted launch: the primary thread never ran game code.
            try { if (!Process.HasExited) Process.Kill(); } catch (InvalidOperationException) { }
            CloseHandle(thread); thread = IntPtr.Zero;
        }
        input.Dispose(); output.Dispose(); error.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    {
        public int Size; public string? Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        public short ShowWindow, ReservedSize; public IntPtr Reserved2, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Info; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes attributes, int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcess(string app, StringBuilder command, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr process, uint code);
}
