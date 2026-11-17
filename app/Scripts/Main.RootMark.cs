using FileScape.App.Planets;
using FileScape.Core.Shell;
using Godot;

namespace FileScape.App;

/// <summary>
/// The session side of a root landmark's panel (PLANET-45, AD-58; PLANET-57): start a terminal and show the folder in File
/// Explorer through the shell actions (so
/// <c>--test-shell=record</c> records it and starts nothing), copy text to the clipboard (recorded, not copied, under the
/// test shell: a probe must never overwrite the person's clipboard), travel to a path as Ctrl+L does, and the known
/// folders and Windows Terminal this computer has. Every action here is an explicit player command from the panel.
/// </summary>
public partial class Main : IDeveloperSession
{
    private string? _windowsTerminal;
    private bool _windowsTerminalLooked;
    private readonly List<string> _clipboardLog = [];

    /// <summary>What the test shell recorded instead of copying (probes), oldest first.</summary>
    public IReadOnlyList<string> ClipboardLog => _clipboardLog;

    string? IDeveloperSession.WindowsTerminal
    {
        get
        {
            if (!_windowsTerminalLooked) { _windowsTerminal = TerminalLaunch.FindWindowsTerminal(); _windowsTerminalLooked = true; }
            return _windowsTerminal;
        }
    }

    IReadOnlyDictionary<string, string> IDeveloperSession.KnownFolders => _tree?.Source.IsLive == true ? KnownFolders() : new Dictionary<string, string>();

    bool IDeveloperSession.StartTerminal(TerminalCommand command)
    {
        var r = _shell.Start(command.Program, command.Args);
        // The test shell's own message says nothing was started; only a real start is reported as an opened terminal (UX-06).
        ReportShell(r.Succeeded && !r.WasMock && !_recordShell ? r with { Message = $"Terminal opened in {command.StartsIn}" } : r);
        return r.Succeeded;
    }

    void IDeveloperSession.CopyText(string text, string what)
    {
        if (_recordShell || _tree?.Source.IsLive != true)
        {
            _clipboardLog.Add(text);
            GD.Print($"[shell] clipboard (recorded, not copied): {text}");
        }
        else
        {
            DisplayServer.ClipboardSet(text);
        }

        _hud.Toast($"{what} copied: {text}");
    }

    void IDeveloperSession.GoToPath(string path) => OnPathSubmitted(path);

    /// <summary>
    /// PLANET-57: the folder opened with Windows (a folder's default verb shows it in File Explorer, or in whatever the
    /// person has made their file manager). ShellExecute rather than <c>explorer.exe path</c>: Explorer parses commas in
    /// its arguments as switches, a folder name may hold one. Recorded, not opened, under <c>--test-shell=record</c>.
    /// </summary>
    bool IDeveloperSession.ShowInExplorer(string folder)
    {
        var r = _shell.Open(folder);
        ReportShell(r.Succeeded && !r.WasMock && !_recordShell ? r with { Message = $"Explorer opened at {folder}" } : r);
        return r.Succeeded;
    }
}
