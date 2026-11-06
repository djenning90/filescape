using System.Runtime.InteropServices;
using FileScape.App.Places;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Shell;
using Godot;
using Environment = System.Environment;

namespace FileScape.App;

/// <summary>
/// PROM-01..04 and DEV-01..03 (AD-32, AD-33): prominent destinations and the project journey. Prominence comes from
/// pins, platform known folders, home paths and project evidence already listed; it marks gates, signs and Destinations
/// rows and never reorders them. A project offers its README (read on request, bounded, shown as inert text) and the
/// editors this computer has, each launched only after the user confirms.
/// </summary>
public partial class Main
{
    private readonly Dictionary<int, ProminenceInfo> _prominence = [];
    private long _prominenceVersion = -1;
    private ProminenceContext? _prominenceCtx;
    private IReadOnlyList<EditorOption> _editorOptions = [];
    private Dictionary<string, string>? _knownFolders;

    /// <summary>Last README shown (for probes): its path and whether it was cut at the size bound.</summary>
    public ReadmeText? LastReadme { get; private set; }

    /// <summary>The editor options last offered on the card (for probes).</summary>
    public IReadOnlyList<EditorOption> EditorOptions => _editorOptions;

    public ProminenceInfo ProminenceOf(int id)
    {
        if (_tree == null || !_tree.Contains(id)) return ProminenceInfo.None;
        if (_prominenceVersion != _tree.Version || _prominenceCtx == null)
        {
            _prominence.Clear();
            _prominenceVersion = _tree.Version;
            _prominenceCtx = new ProminenceContext
            {
                Pins = new HashSet<string>(_prefs.Pins, ProminenceContext.PathComparer),
                KnownFolders = _tree.Source.IsLive ? KnownFolders() : new Dictionary<string, string>(),
                NotProjects = new HashSet<int>(_ordinaryOverride),
            };
        }

        if (!_prominence.TryGetValue(id, out var p))
        {
            p = Prominence.For(_tree, id, _prominenceCtx);
            _prominence[id] = p;
        }

        return p;
    }

    /// <summary>Forget cached prominence (pins or a project correction changed).</summary>
    private void InvalidateProminence()
    {
        _prominenceCtx = null;
        _place = null; // recompose so gates, signs and rows show the change at once
    }

    /// <summary>Folders Windows itself names (the profile, Desktop, Documents, Downloads, Pictures, Music, Videos).</summary>
    private Dictionary<string, string> KnownFolders()
    {
        if (_knownFolders != null) return _knownFolders;
        var map = new Dictionary<string, string>(ProminenceContext.PathComparer);
        if (OperatingSystem.IsWindows())
        {
            void Add(Environment.SpecialFolder f, string name)
            {
                var path = Environment.GetFolderPath(f, Environment.SpecialFolderOption.DoNotVerify);
                if (!string.IsNullOrEmpty(path)) map.TryAdd(path.TrimEnd('\\'), name);
            }

            Add(Environment.SpecialFolder.UserProfile, "Home");
            Add(Environment.SpecialFolder.Desktop, "Desktop");
            Add(Environment.SpecialFolder.MyDocuments, "Documents");
            Add(Environment.SpecialFolder.MyPictures, "Pictures");
            Add(Environment.SpecialFolder.MyMusic, "Music");
            Add(Environment.SpecialFolder.MyVideos, "Videos");
            if (DownloadsFolder() is { } downloads) map.TryAdd(downloads.TrimEnd('\\'), "Downloads");
        }

        _knownFolders = map;
        return map;
    }

    private static string? DownloadsFolder()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads
        if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var p) != 0) return null;
        try
        {
            return Marshal.PtrToStringUni(p);
        }
        finally
        {
            Marshal.FreeCoTaskMem(p);
        }
    }

#pragma warning disable SYSLIB1054
    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
#pragma warning restore SYSLIB1054

    // ------------------------------------------------------------------ the card

    /// <summary>The folder the project actions apply to: the selected folder, else the place you are in.</summary>
    private int ProjectTarget() =>
        _tree == null ? -1 : _selection.Selected >= 0 && _tree[_selection.Selected].IsDirectory ? _selection.Selected : _nav.Current;

    private FsNode? ReadmeOf(int folder) =>
        _tree == null || folder < 0 ? null
        : _tree[folder].Children.Select(c => _tree[c]).Where(n => n.Kind == NodeKind.File && Readme.IsSupported(n.Name))
            .OrderBy(n => n.Name.Contains(".md", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(n => n.Name, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>Updates the card's prominence line, pin toggle and project actions for the selected folder.</summary>
    private void UpdateCardExtras(FsNode node)
    {
        if (_tree == null || !node.IsDirectory || node.IsHub)
        {
            _editorOptions = [];
            _hud.SetCardExtras(null, false, false, false, []);
            return;
        }

        var p = ProminenceOf(node.Id);
        var path = _tree.FullPath(node.Id);
        var live = _tree.Source.IsLive;
        var isProject = KindOf(node.Id).Kind == PlaceKind.Project;
        var readme = isProject ? ReadmeOf(node.Id) : null;
        _editorOptions = isProject && live
            ? EditorLaunch.Options(path, node.Children.Select(c => _tree[c]).Where(n => n.Kind == NodeKind.File && (n.Name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || n.Name.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))).Select(n => n.Name).ToList(), VsCode())
            : [];
        var why = p.IsProminent ? $"{p.Mark} {p.Reason}" : null;
        if (isProject && live && _editorOptions.Count == 0) why = (why == null ? "" : why + "\n") + "No editor found for this project here (FileScape installs none).";
        _hud.SetCardExtras(why, canPin: live, pinned: p.Kind == ProminenceKind.Pinned, hasReadme: readme != null, _editorOptions.Select(o => (o.Label, o.Explanation)).ToList());
    }

    /// <summary>VS Code's executable if it is installed for this user or machine (looked up, never installed).</summary>
    private static string? VsCode()
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Programs\Microsoft VS Code", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\Microsoft VS Code" })
        {
            var exe = System.IO.Path.Combine(dir, "Code.exe");
            if (System.IO.File.Exists(exe)) return exe;
        }

        return null;
    }

    private void TogglePin()
    {
        if (_tree == null || !_tree.Source.IsLive) return;
        var id = ProjectTarget();
        if (id < 0 || !_tree[id].IsDirectory || _tree[id].IsHub) return;
        var path = _tree.FullPath(id);
        var pinned = !_prefs.Pins.Contains(path, StringComparer.OrdinalIgnoreCase);
        SetPreferences(_prefs.WithPin(path, pinned));
        InvalidateProminence();
        _hud.Toast(pinned ? $"★ Pinned {_tree.DisplayName(id)}: it stands out wherever it appears." : $"Unpinned {_tree.DisplayName(id)}.");
        UpdateInspector();
    }

    private void ShowReadme()
    {
        if (_tree == null) return;
        var folder = ProjectTarget();
        if (ReadmeOf(folder) is not { } file)
        {
            _hud.Toast("No README to show here.");
            return;
        }

        if (!_tree.Source.IsLive)
        {
            _hud.Toast($"“{file.Name}” is in sample data: there is no real text to show.");
            return;
        }

        var path = _tree.FullPath(file.Id);
        var title = $"{_tree.DisplayName(folder)} · {file.Name}";
        _hud.Toast($"Reading {file.Name}…");
        // A deliberate, bounded read off the main thread (a slow share must not freeze the world).
        Task.Run(() => Readme.Read(path)).ContinueWith(t => Callable.From(() =>
        {
            var r = t.Result;
            LastReadme = r;
            GD.Print($"[readme] {path}: {r.Text.Length} chars, truncated={r.Truncated}, error={r.Error ?? "-"}");
            var body = r.Error != null ? $"[color=#ffb070]{r.Error.Replace("[", "[lb]")}[/color]" : Readme.ToBbcode(r.Text, r.Format == "markdown");
            var footer = $"{r.Format} · shown as text: links are not followed, images not loaded, nothing is run" + (r.Truncated ? $" · only the first {Readme.MaxBytes / 1024} KB / {Readme.MaxLines:N0} lines" : "");
            ReleaseAllInput();
            _hud.ShowReadme(title, body, footer);
        }).CallDeferred());
    }

    private void RequestEditor(int index)
    {
        if (index < 0 || index >= _editorOptions.Count) return;
        var o = _editorOptions[index];
        Confirm($"{o.Label}?\n\n{o.Explanation}", () =>
        {
            var r = o.ViaAssociation ? _shell.Open(o.Program) : _shell.Start(o.Program, o.Args);
            GD.Print($"[editor] {o.Label}: {r.Message}");
            _hud.Toast(r.Message, r.Succeeded ? new Color(0.7f, 1f, 0.8f) : new Color(1f, 0.8f, 0.5f), 6);
        });
    }

    /// <summary>Gate marks for prominent destinations (PlaceWorld draws a slim light above each, by level).</summary>
    private void UpdateProminentGates()
    {
        if (_place == null || _tree == null) return;
        var marks = new List<(int Id, Vector3 Top, float Level, Color Color)>();
        foreach (var g in _place.Gates)
        {
            var p = ProminenceOf(g.TargetId);
            if (!p.IsProminent) continue;
            marks.Add((g.TargetId, new Vector3(g.Position.X, g.Position.Y + g.Height, g.Position.Z), p.Level, ProminenceColor(p)));
        }

        _placeWorld.SetProminentGates(marks);
    }

    public static Color ProminenceColor(ProminenceInfo p) => p.Kind switch
    {
        ProminenceKind.Pinned => new Color(1f, 0.82f, 0.3f),
        ProminenceKind.Home or ProminenceKind.KnownFolder => new Color(1f, 0.68f, 0.45f),
        _ => new Color(0.5f, 0.95f, 0.85f),
    };
}
