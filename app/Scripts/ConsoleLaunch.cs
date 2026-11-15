using System.Diagnostics;
using System.Runtime.InteropServices;
using Godot;

namespace FileScape.App;

/// <summary>
/// SHUT-01/02 (AD-28): how FileScape relates to the terminal it was started from. FileScape.exe is a window
/// (GUI-subsystem) program, so cmd and PowerShell return to their prompt at once, and Godot's log lines then appear
/// after that prompt: closing the window looks as if the command never returned. FileScape.console.exe (Godot's console
/// wrapper) is the synchronous launcher: it waits until the window closes, relays the log and returns the exit code.
/// When FileScape.exe itself writes into an interactive console it says so once, and again when it closes.
/// </summary>
public static class ConsoleLaunch
{
    /// <summary>True when stdout is an interactive console that did not wait for us (not the wrapper, not a pipe or file).</summary>
    public static bool DetachedConsole { get; private set; }

    /// <summary>A one-line description of stdout and the parent process, for the start-up log.</summary>
    public static string Describe { get; private set; } = "unknown";

    /// <summary>Win32 STD_OUTPUT_HANDLE.</summary>
    private const int StdOutputHandle = -11;

    /// <summary>
    /// Works out once, at start-up, whether stdout is a console that did not wait for us (sets <see cref="DetachedConsole"/>
    /// and prints the two "[console]" hints if so). Off Windows, or if the Win32 calls fail, it only records why.
    /// </summary>
    public static void Detect()
    {
        if (!OperatingSystem.IsWindows()) { Describe = "not Windows"; return; }
        try
        {
            var type = GetFileType(GetStdHandle(StdOutputHandle)); // FILE_TYPE_DISK = 1, CHAR (a console) = 2, PIPE = 3
            var parent = ParentName();
            var wrapper = parent?.EndsWith(".console", StringComparison.OrdinalIgnoreCase) == true;
            Describe = $"stdout {(type switch { 2 => "console", 3 => "pipe", 1 => "file", _ => "none" })}, parent {parent ?? "?"}";
            DetachedConsole = type == 2 && !wrapper && !OS.HasFeature("editor");
            if (DetachedConsole)
            {
                GD.Print("[console] FileScape runs in its own window; this terminal did not wait for it (its prompt is above these lines).");
                GD.Print("[console] To keep the terminal until FileScape closes, start FileScape.console.exe instead (same arguments).");
            }
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException or InvalidOperationException or ArgumentException)
        {
            Describe = $"not determined ({e.GetType().Name})";
        }
    }

    /// <summary>The last line FileScape writes; in a console that did not wait, it also says how to get a prompt back.</summary>
    public static void ReportExit(int code, string why, TimeSpan shutdown)
    {
        GD.Print($"[exit] FileScape closed ({why}), {(code >= 0 ? $"exit code {code}" : "exit code as requested")}, shutdown {shutdown.TotalMilliseconds:0} ms");
        if (DetachedConsole) GD.Print("[exit] This terminal was already free: press Enter for a fresh prompt.");
    }

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
#pragma warning restore SYSLIB1054
}
