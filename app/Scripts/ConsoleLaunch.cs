using System.Diagnostics;
using System.Runtime.InteropServices;
using Godot;

namespace FileScape.App;

/// <summary>
/// SHUT-01/02 (AD-28, AD-76): how FileScape relates to the terminal it was started from.
/// <list type="bullet">
/// <item><b>FileScape.exe typed at a prompt.</b> It is a window (GUI-subsystem) program, so cmd and PowerShell print
/// their next prompt at once. Godot's Windows build then attaches to that console anyway (AttachConsole in its native
/// start-up, before any C#) and prints its log under the prompt, so after X the last line on screen was FileScape's
/// "[exit]" and no prompt: the command looked busy until Enter or Ctrl+C (DJ, 2026-11-19; reproduced with
/// tools/os-shutdown-probe.ps1). Now, when stdout is an interactive console that did not wait, FileScape lets go of it
/// at once (<see cref="DetachedConsole"/>: FreeConsole) and writes nothing more there; with the engine header off
/// (project.godot) nothing appears under the prompt at all. The log still goes to Godot's log file
/// (%APPDATA%\Godot\app_userdata\FileScape\logs) and, in full, through FileScape.console.exe.</item>
/// <item><b>FileScape.console.exe</b> (Godot's console wrapper) waits until the window closes, relays the log and returns
/// the exit code. It puts FileScape in a job object and waits until <i>every</i> process in the job has ended, and
/// closes the job with kill-on-close on Ctrl+C. <see cref="InJob"/> tells the shell actions to start the person's apps
/// outside that job (FileScape.Core.Shell.DesktopShell), so an app opened from FileScape neither keeps the terminal
/// waiting after X nor dies with it.</item>
/// </list>
/// Preserve: nothing here may block or wait on the console; detaching happens once, at start-up, before other output.
/// </summary>
public static class ConsoleLaunch
{
    /// <summary>True when stdout was an interactive console that did not wait for us (not the wrapper, not a pipe or file); FileScape has let go of it.</summary>
    public static bool DetachedConsole { get; private set; }

    /// <summary>True when FileScape runs inside a job object (under FileScape.console.exe, Godot's console wrapper).</summary>
    public static bool InJob { get; private set; }

    /// <summary>A one-line description of stdout, the parent process and the job, for the start-up log.</summary>
    public static string Describe { get; private set; } = "unknown";

    /// <summary>Win32 STD_OUTPUT_HANDLE.</summary>
    private const int StdOutputHandle = -11;

    /// <summary>
    /// Works out once, at start-up, whether stdout is a console that did not wait for us, and if so lets go of it
    /// (FreeConsole: what Godot writes from here on reaches only its log file). Also records whether FileScape runs in a
    /// job. Off Windows, or if the Win32 calls fail, it only records why.
    /// </summary>
    public static void Detect()
    {
        if (!OperatingSystem.IsWindows()) { Describe = "not Windows"; return; }
        try
        {
            var type = GetFileType(GetStdHandle(StdOutputHandle)); // FILE_TYPE_DISK = 1, CHAR (a console) = 2, PIPE = 3
            var parent = ParentName();
            var wrapper = parent?.EndsWith(".console", StringComparison.OrdinalIgnoreCase) == true;
            InJob = IsProcessInJob(Process.GetCurrentProcess().Handle, IntPtr.Zero, out var inJob) && inJob;
            Describe = $"stdout {(type switch { 2 => "console", 3 => "pipe", 1 => "file", _ => "none" })}, parent {parent ?? "?"}, {(InJob ? "in a job" : "no job")}";
            DetachedConsole = type == 2 && !wrapper && !OS.HasFeature("editor");
            if (DetachedConsole)
            {
                FreeConsole(); // first: anything printed before it would land under the terminal's prompt
                GD.Print($"[console] {Describe}: the terminal did not wait, so FileScape let go of it (the log continues in the log file).");
            }
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException or InvalidOperationException or ArgumentException)
        {
            Describe = $"not determined ({e.GetType().Name})";
        }
    }

    /// <summary>The last line FileScape writes (to the log file only when it has let go of the console).</summary>
    public static void ReportExit(int code, string why, TimeSpan shutdown) =>
        GD.Print($"[exit] FileScape closed ({why}), {(code >= 0 ? $"exit code {code}" : "exit code as requested")}, shutdown {shutdown.TotalMilliseconds:0} ms");

    /// <summary>The parent process's name (the console wrapper is "FileScape.console"), or null if it cannot be found.</summary>
    private static string? ParentName()
    {
        var info = new ProcessBasicInformation();
        if (NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 0 /* ProcessBasicInformation */, ref info, Marshal.SizeOf(info), out _) != 0) return null;
        try
        {
            return Process.GetProcessById((int)info.InheritedFromUniqueProcessId.ToInt64()).ProcessName;
        }
        catch (ArgumentException)
        {
            return null; // the parent has already exited
        }
    }

    /// <summary>The Win32 PROCESS_BASIC_INFORMATION layout; only the parent's process id is read.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1, PebBaseAddress, Reserved2a, Reserved2b, UniqueProcessId, InheritedFromUniqueProcessId;
    }

#pragma warning disable SYSLIB1054
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref ProcessBasicInformation info, int size, out int returned);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int which);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);
#pragma warning restore SYSLIB1054
}
