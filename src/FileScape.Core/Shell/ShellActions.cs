using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileScape.Core.Shell;

/// <summary>The outcome of a shell action. A mock action is never reported as a real OS action (UX-06).</summary>
public sealed record ShellResult(bool Succeeded, bool WasMock, string Message);

/// <summary>Deliberate user-initiated actions on a path. Nothing in FileScape calls these automatically (FILE-02).</summary>
public interface IShellActions
{
    /// <summary>Opens the item with its Windows association (the default verb).</summary>
    ShellResult Open(string path);

    /// <summary>Opens an Explorer window with the item selected.</summary>
    ShellResult RevealInExplorer(string path);

    /// <summary>Starts an installed program with separate arguments (an editor, DEV-03), detached from FileScape's console.</summary>
    ShellResult Start(string program, IReadOnlyList<string> args);
}

/// <summary>Used with synthetic data. It only describes what would have happened.</summary>
public sealed class MockShellActions : IShellActions
{
    public ShellResult Open(string path) =>
        new(false, true, $"MOCK — synthetic data has no real file to open ({path}).");

    public ShellResult RevealInExplorer(string path) =>
        new(false, true, $"MOCK — synthetic data has no real location ({path}).");

    public ShellResult Start(string program, IReadOnlyList<string> args) =>
        new(false, true, $"MOCK — would start {program} {string.Join(' ', args)}");
}

/// <summary>
/// Test instrument (<c>--test-shell=record</c>): records what would be opened, revealed or started and launches nothing,
/// so a journey can open a real file deliberately without starting another program on the desktop.
/// </summary>
public sealed class RecordingShellActions : IShellActions
{
    private readonly List<string> _log = [];

    public IReadOnlyList<string> Log => _log;

    public ShellResult Open(string path)
    {
        _log.Add("open " + path);
        return new(true, false, $"TEST — would open {path} with its default program (nothing was started).");
    }

    public ShellResult RevealInExplorer(string path)
    {
        _log.Add("reveal " + path);
        return new(true, false, $"TEST — would show {path} in File Explorer (nothing was started).");
    }

    public ShellResult Start(string program, IReadOnlyList<string> args)
    {
        _log.Add("start " + program);
        return new(true, false, $"TEST — would start {program} {string.Join(' ', args)}");
    }
}

/// <summary>Classifies paths that would run code if opened, so the UI can ask for confirmation first (FILE-03).</summary>
public static class LaunchRisk
{
    private static readonly HashSet<string> Executable = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".msi", ".msp", ".msc", ".scr", ".pif", ".cpl", ".hta", ".lnk", ".url", ".reg", ".jar", ".appref-ms",
        ".application", ".gadget", ".inf", ".sct", ".scf", ".appx", ".msix", ".appinstaller",
    };

    /// <summary>
    /// True for extensions Windows runs (or installs, or follows like a shortcut) when opened. Trailing dots and spaces
    /// are trimmed first because Windows ignores them, so "setup.exe." is still an executable.
    /// </summary>
    public static bool RunsCode(string path) => Executable.Contains(Path.GetExtension(path.TrimEnd('.', ' ')));
}

/// <summary>
/// Windows implementation built on ShellExecuteExW and Explorer: the real actions behind Open, Show in Explorer and the
/// editor launches (DEV-03). Every call is synchronous and reports Windows' own error text rather than a shell dialog.
/// </summary>
public sealed class WindowsShellActions : IShellActions
{
    public ShellResult Open(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ShellResult(false, false, "Opening files requires Windows.");
        }

        // Calls COM-backed shell code. Godot's main thread is a GUI thread with a message loop. NOASYNC makes the
        // call complete before returning, so errors can be reported. FLAG_NO_UI suppresses the shell's own error
        // dialog, so FileScape can show the error instead.
        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            fMask = SeeMaskNoAsync | SeeMaskFlagNoUi,
            lpVerb = null,
            lpFile = path,
            nShow = SwShowNormal,
        };
        _ = CoInitializeEx(IntPtr.Zero, CoinitApartmentThreaded | CoinitDisableOle1Dde);
        if (ShellExecuteExW(ref info))
        {
            return new ShellResult(true, false, $"Opened with Windows: {path}");
        }

        var error = Marshal.GetLastWin32Error();
        return new ShellResult(false, false, $"Windows could not open the item (error {error}: {new Win32Exception(error).Message}).");
    }

    /// <summary>
    /// ShellExecuteEx, not Process.Start with inherited handles: the editor must not hold FileScape's console or pipes
    /// (a child holding them keeps a waiting launcher open after FileScape closes, SHUT-01).
    /// </summary>
    public ShellResult Start(string program, IReadOnlyList<string> args)
    {
        if (!OperatingSystem.IsWindows()) return new ShellResult(false, false, "Starting programs requires Windows.");
        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            fMask = SeeMaskNoAsync | SeeMaskFlagNoUi,
            lpFile = program,
            lpParameters = string.Join(' ', args.Select(Quote)),
            nShow = SwShowNormal,
        };
        _ = CoInitializeEx(IntPtr.Zero, CoinitApartmentThreaded | CoinitDisableOle1Dde);
        if (ShellExecuteExW(ref info)) return new ShellResult(true, false, $"Started {Path.GetFileName(program)} {info.lpParameters}");
        var error = Marshal.GetLastWin32Error();
        return new ShellResult(false, false, $"Could not start {Path.GetFileName(program)} (error {error}: {new Win32Exception(error).Message}).");
    }

    /// <summary>
    /// Windows command-line quoting for one argument (CommandLineToArgvW rules): backslashes are literal except before a
    /// quote, where each must be doubled and the quote itself escaped; a closing quote doubles any trailing backslashes.
    /// </summary>
    public static string Quote(string a)
    {
        if (a.Length > 0 && a.IndexOfAny([' ', '\t', '"']) < 0) return a;
        var sb = new System.Text.StringBuilder("\"");
        var slashes = 0;
        foreach (var ch in a)
        {
            if (ch == '\\') { slashes++; continue; }
            sb.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes);
            slashes = 0;
            sb.Append(ch);
        }

        sb.Append('\\', slashes * 2).Append('"');
        return sb.ToString();
    }

    public ShellResult RevealInExplorer(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ShellResult(false, false, "Explorer requires Windows.");
        }

        try
        {
            using var p = Process.Start(new ProcessStartInfo("explorer.exe")
            {
                ArgumentList = { $"/select,{path}" },
                UseShellExecute = false,
            });
            return new ShellResult(true, false, $"Explorer asked to show: {path}");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new ShellResult(false, false, $"Could not start Explorer: {ex.Message}");
        }
    }

    // Win32 values (shellapi.h, winuser.h, objbase.h), fixed by Windows.
    private const uint SeeMaskNoAsync = 0x00000100;
    private const uint SeeMaskFlagNoUi = 0x00000400;
    private const int SwShowNormal = 1;
    private const uint CoinitApartmentThreaded = 0x2;
    private const uint CoinitDisableOle1Dde = 0x4;

    /// <summary>SHELLEXECUTEINFOW, field for field for P/Invoke (<c>hIconOrMonitor</c> is the DUMMYUNIONNAME union).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIconOrMonitor;
        public IntPtr hProcess;
    }

#pragma warning disable SYSLIB1054 // DllImport keeps the struct marshalling simple; nothing here is on a hot path
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteExW(ref ShellExecuteInfo info);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);
#pragma warning restore SYSLIB1054
}
