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

    /// <summary>Opens the folder that contains the item in File Explorer, with the item selected (a link: the link itself; PLANET-138).</summary>
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

    /// <summary>
    /// A Win32 error code the next <see cref="Open"/> fails with, as Windows would (then cleared): how a probe checks
    /// that a refused open is reported (PLANET-43). The failure is logged as <c>open-failed</c>; nothing is launched.
    /// </summary>
    public int? FailNextOpenWith { get; set; }

    public ShellResult Open(string path)
    {
        if (FailNextOpenWith is { } code)
        {
            FailNextOpenWith = null;
            _log.Add("open-failed " + path);
            return new(false, false, "TEST — " + ShellErrors.OpenFailed(path, code, new Win32Exception(code).Message));
        }

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
        _log.Add($"start {program} {WindowsShellActions.CommandLine(args)}".TrimEnd()); // with its arguments, so a probe can check where a terminal would start
        return new(true, false, $"TEST — would start {program} {string.Join(' ', args)}");
    }
}

/// <summary>
/// What to tell the user when Windows refuses to open something (PLANET-43, DJ 2026-11-16: "If an open fails, tell me
/// what happened"): the item's name, the likely reason in plain words for the common errors, and Windows' own text and
/// code for the record. Pure, so the wording is tested.
/// </summary>
public static class ShellErrors
{
    // Win32 error codes (winerror.h), fixed by Windows.
    private const int FileNotFound = 2, PathNotFound = 3, AccessDenied = 5, NoAssociation = 1155, Cancelled = 1223;

    /// <summary>"Could not open Lantern.sln: no app is set to open .sln files on this PC. (Windows: … error 1155)".</summary>
    public static string OpenFailed(string path, int code, string windowsText)
    {
        // Split by hand: Windows paths are named the same on any OS these tests run on ('\\' and '/').
        var trimmed = path.TrimEnd('\\', '/');
        var name = trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..];
        if (name.Length == 0) name = path;
        var ext = Path.GetExtension(name);
        var why = code switch
        {
            NoAssociation => ext.Length > 0
                ? $"no app is set to open {ext.ToLowerInvariant()} files on this PC (choose one in Windows: right-click the file, Open with)"
                : "no app is set to open this kind of item on this PC",
            FileNotFound or PathNotFound => "it is no longer there (moved, renamed or deleted since FileScape listed it)",
            AccessDenied => "Windows denied access to it",
            Cancelled => "the open was cancelled",
            _ => "Windows refused",
        };
        return $"Could not open {name}: {why}. (Windows: {windowsText.TrimEnd('.')}; error {code}.)";
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
/// Windows implementation built on ShellExecuteExW and the shell: the real actions behind Open, Show in Explorer and the
/// editor launches (DEV-03). Every call is synchronous and reports Windows' own error text rather than a shell dialog.
/// <para>
/// <see cref="OutsideJob"/> (PLANET-89, AD-76): when FileScape runs in a job object (under FileScape.console.exe), what
/// it starts for the person is started by the desktop's Explorer instead (<see cref="DesktopShell"/>), so it is not in
/// the job: the console wrapper waits for every process in its job and kills them on Ctrl+C. Explorer reports no
/// errors back, so then only "it is no longer there" is checked first; anything else Windows shows itself (its own
/// "How do you want to open this file?" for a type with no app). Show in Explorer never starts a child with
/// FileScape's handles (SHOpenFolderAndSelectItems asks the running shell).
/// </para>
/// </summary>
public sealed class WindowsShellActions : IShellActions
{
    /// <summary>Start what the person opens through the desktop shell, outside FileScape's job (set when FileScape runs in one).</summary>
    public bool OutsideJob { get; init; }

    /// <summary>"shell:" names (This PC) are not files: the existence check does not apply to them.</summary>
    private static bool IsShellName(string path) => path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);

    public ShellResult Open(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ShellResult(false, false, "Opening files requires Windows.");
        }

        if (OutsideJob)
        {
            if (!IsShellName(path) && !File.Exists(path) && !Directory.Exists(path))
                return new ShellResult(false, false, ShellErrors.OpenFailed(path, FileNotFoundCode, new Win32Exception(FileNotFoundCode).Message));
            LetTheViewerComeForward();
            if (DesktopShell.TryShellExecute(path, null, null, "", out _)) return new ShellResult(true, false, $"Opened with Windows: {path}");
            // No desktop shell to ask (no Explorer, another shell): open directly, as outside a job.
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
        LetTheViewerComeForward();
        if (ShellExecuteExW(ref info))
        {
            return new ShellResult(true, false, $"Opened with Windows: {path}");
        }

        var error = Marshal.GetLastWin32Error();
        return new ShellResult(false, false, ShellErrors.OpenFailed(path, error, new Win32Exception(error).Message));
    }

    /// <summary>
    /// ShellExecuteEx, not Process.Start with inherited handles: the editor must not hold FileScape's console or pipes
    /// (a child holding them keeps a waiting launcher open after FileScape closes, SHUT-01).
    /// </summary>
    public ShellResult Start(string program, IReadOnlyList<string> args)
    {
        if (!OperatingSystem.IsWindows()) return new ShellResult(false, false, "Starting programs requires Windows.");
        if (OutsideJob && DesktopShell.TryShellExecute(program, CommandLine(args), null, "", out _))
            return new ShellResult(true, false, $"Started {Path.GetFileName(program)} {CommandLine(args)}");
        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            fMask = SeeMaskNoAsync | SeeMaskFlagNoUi,
            lpFile = program,
            lpParameters = CommandLine(args),
            nShow = SwShowNormal,
        };
        _ = CoInitializeEx(IntPtr.Zero, CoinitApartmentThreaded | CoinitDisableOle1Dde);
        if (ShellExecuteExW(ref info)) return new ShellResult(true, false, $"Started {Path.GetFileName(program)} {info.lpParameters}");
        var error = Marshal.GetLastWin32Error();
        return new ShellResult(false, false, $"Could not start {Path.GetFileName(program)} (error {error}: {new Win32Exception(error).Message}).");
    }

    /// <summary>The arguments as one Windows command line, each quoted by <see cref="Quote"/>: what <see cref="Start"/> passes
    /// (the terminal tests start programs with exactly this string, PLANET-45).</summary>
    public static string CommandLine(IEnumerable<string> args) => string.Join(' ', args.Select(Quote));

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

        // Before 2026-11-19 this was Process.Start("explorer.exe /select,...") with UseShellExecute = false: a child that
        // inherited FileScape's handles and, under the console wrapper, joined its job (PLANET-89). From 2026-11-19 to
        // 2026-11-20 a job sent "explorer /select" through the desktop shell first; that route cannot report a missing
        // item and did not select a WSL distribution in Linux (tools/os-file-actions-probe.ps1). SHOpenFolderAndSelectItems
        // asks the running shell and starts no process of FileScape's (the probe checks FileScape's children after every
        // step), so it is the route in and out of a job; the desktop shell is only the fallback.
        if (!IsShellName(path) && !ItselfExists(path))
            return new ShellResult(false, false, $"Could not show {path} in File Explorer: it is no longer there (moved, renamed or deleted since FileScape listed it).");
        _ = CoInitializeEx(IntPtr.Zero, CoinitApartmentThreaded | CoinitDisableOle1Dde);
        var hr = SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _);
        if (hr != 0)
        {
            if (OutsideJob && DesktopShell.TryShellExecute("explorer.exe", $"/select,\"{path}\"", null, "", out _))
                return new ShellResult(true, false, $"Explorer asked (through the desktop shell) to show: {path}");
            var code = hr & 0xFFFF;
            var why = code is FileNotFoundCode or PathNotFoundCode ? "it is no longer there (moved, renamed or deleted since FileScape listed it)" : "Windows refused";
            return new ShellResult(false, false, $"Could not show {path} in File Explorer: {why}. (Windows: {new Win32Exception(code).Message.TrimEnd('.')}; HRESULT 0x{hr:X8}.)");
        }

        // PLANET-138 (2026-11-20, DJ: "Open on a folder and Show in Explorer appeared to do the same thing"): the item is
        // named as the one child of its parent's ID list, so File Explorer opens the parent with the item selected; for
        // a drive the parent is This PC, for \\wsl.localhost\Ubuntu it is Linux, for a share its server. (The documented
        // cidl-0 form did the same for ordinary folders, measured on the base build; the explicit form also says when
        // nothing contains the item. What made the two actions look alike was mostly their labels: SelectionActions.)
        var parent = ILClone(pidl);
        try
        {
            if (parent == IntPtr.Zero || !ILRemoveLastID(parent))
                return new ShellResult(false, false, $"Could not show {path} in File Explorer: nothing in File Explorer contains it.");
            var child = ILFindLastID(pidl);
            var children = new[] { child };
            LetTheViewerComeForward();
            hr = SHOpenFolderAndSelectItems(parent, 1, children, 0);
            return hr == 0
                ? new ShellResult(true, false, $"Explorer asked to show: {path}")
                : new ShellResult(false, false, $"Could not show {path} in File Explorer (HRESULT 0x{hr:X8}: {new Win32Exception(hr & 0xFFFF).Message.TrimEnd('.')}).");
        }
        finally
        {
            if (parent != IntPtr.Zero) Marshal.FreeCoTaskMem(parent);
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>
    /// Whether the item itself is there — a link or junction counts even when its target is gone (its attributes are the
    /// link's own; File.Exists and Directory.Exists would follow it). Show in folder selects the link, not the target.
    /// </summary>
    private static bool ItselfExists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return e is UnauthorizedAccessException; // denied: it is there, Windows only refuses to say more
        }
    }

    /// <summary>Win32 ERROR_FILE_NOT_FOUND and ERROR_PATH_NOT_FOUND (winerror.h).</summary>
    private const int FileNotFoundCode = 2, PathNotFoundCode = 3;

    /// <summary>
    /// PLANET-52: lets the app that opens the file take the foreground, so it comes forward instead of only flashing on
    /// the taskbar (an app that was already running, such as an editor receiving the file, is otherwise refused by
    /// Windows' foreground lock). Only when FileScape itself is the foreground app, which it is when a person has just
    /// pressed Enter or clicked Open: FileScape passes on its own right and never takes the foreground from anyone else.
    /// The right lasts until the next input (AllowSetForegroundWindow, ASFW_ANY); Alt+Tab returns to FileScape as usual.
    /// </summary>
    private static void LetTheViewerComeForward()
    {
        try
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return;
            _ = GetWindowThreadProcessId(fg, out var pid);
            if (pid != (uint)Environment.ProcessId) return;
            if (AllowSetForegroundWindow(AsfwAny)) ViewerAllowedForward++;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not Windows' user32 (never on Windows): opening goes on without it.
        }
    }

    /// <summary>Opens for which FileScape was the foreground app and passed on its right to come forward (diagnostics, logged by the app).</summary>
    public static int ViewerAllowedForward { get; private set; }

    // Win32 values (shellapi.h, winuser.h, objbase.h), fixed by Windows.
    private const uint AsfwAny = 0xFFFFFFFF;
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint count, IntPtr[] children, uint flags);

    /// <summary>A copy of an ID list (freed with CoTaskMemFree).</summary>
    [DllImport("shell32.dll")]
    private static extern IntPtr ILClone(IntPtr pidl);

    /// <summary>Cuts the last item off an ID list in place: the parent's ID list. False for the desktop (nothing above).</summary>
    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ILRemoveLastID(IntPtr pidl);

    /// <summary>The last item of an ID list (a pointer into it, not a copy): the child relative to its parent.</summary>
    [DllImport("shell32.dll")]
    private static extern IntPtr ILFindLastID(IntPtr pidl);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AllowSetForegroundWindow(uint processId);
#pragma warning restore SYSLIB1054
}
