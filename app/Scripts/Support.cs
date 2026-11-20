using System.Globalization;
using System.Text;
using FileScape.App.Visual;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using Godot;

namespace FileScape.App;

/// <summary>
/// Command-line options, passed after "--". For example:
/// <c>--root=C:\path --vis=district --quality=low --synthetic=50000 --automate=journey --out=C:\dir --window=1600x900</c>
/// </summary>
public sealed record CommandLine
{
    public string? Root { get; init; }

    /// <summary>--scan=tree: scan --root as a bounded tree (whole-tree reference) instead of the connected world.</summary>
    public bool BoundedTree { get; init; }
    public int? SyntheticNodes { get; init; }
    public int? SyntheticRate { get; init; }
    public int? Visualization { get; init; }

    /// <summary>--vis=places|classic|district (AD-15). Null means the default for the run.</summary>
    public Experience? Experience { get; init; }
    public Quality? Quality { get; init; }
    public string? Automation { get; init; }
    public string? OutputDirectory { get; init; }
    public Vector2I? WindowSize { get; init; }
    public float? UiScale { get; init; }
    public string? OpenTest { get; init; }
    public bool NoVsync { get; init; }

    /// <summary>When set, Main writes its navigation state here as JSON every 150 ms (for OS-level input probes).</summary>
    public string? StateFile { get; init; }

    /// <summary>--state-interval-ms=N (instrument): write the state file every N ms instead of 150 (0: every frame), for probes that time presses from it.</summary>
    public int? StateIntervalMs { get; init; }
    public bool StressDistrictOnly { get; init; }

    /// <summary>--prefs=path: the preferences file. Default: %APPDATA%\FileScape\preferences.json, except under --automate, where nothing is read or saved unless --prefs is given (probes never touch DJ's settings).</summary>
    public string? PreferencesFile { get; init; }

    /// <summary>--experiment=planetoids: the isolated planetoid traversal experiment (PLANET-02) instead of FileScape.</summary>
    public string? Experiment { get; init; }

    /// <summary>
    /// --test-hold=dialog|fly|listing|reveal|open:&lt;path&gt; (SHUT-01 instrument): after start-up, put the app into that state
    /// and stay there (no automation, no quit) so an outside probe can close the window and time the exit. open: opens
    /// that file with the real shell (PLANET-88/89: tools/os-open-probe.ps1, tools/os-shutdown-probe.ps1 -AppArgs).
    /// </summary>
    public string? TestHold { get; init; }

    /// <summary>--test-list-delay-ms=N (SHUT-01 instrument): every live listing first blocks its worker for N ms, uncancellably, like a stalled share.</summary>
    public int? TestListDelayMs { get; init; }

    /// <summary>--test-list-delay-match=TEXT (instrument, PLANET-39): the delay applies only to folders whose path contains TEXT, so a probe can see one folder "still listing" while the rest list normally.</summary>
    public string? TestListDelayMatch { get; init; }

    /// <summary>
    /// --test-broad (instrument, PLANET-76): the arrival folder is treated as a broad place (a drive, a profile, a
    /// distribution root), so it is not listed in the background and only visits and the overview's discovery list it.
    /// A deep fixture then behaves as a real drive does, and the discovery journey measures discovery, not the background
    /// traversal.
    /// </summary>
    public bool TestBroad { get; init; }

    /// <summary>
    /// Before/after instruments for the fifth playtest follow-up (PLANET-90..92, AD-77), so one build measures both:
    /// --test-play-discovery=off (no background discovery while you play), --test-overview-detail=off (every laid-out
    /// world drawn, as before the level of detail), --test-overview-look=plain (every solid world in the old palette
    /// colour and the old material, as before the varied looks).
    /// </summary>
    public bool TestPlayDiscoveryOff { get; init; }

    public bool TestOverviewDetailOff { get; init; }

    public bool TestOverviewLookPlain { get; init; }

    /// <summary>
    /// --test-root-look=windows|wsl (instrument, PLANET-119, AD-83): the launch folder is shown with a Windows volume's or
    /// a WSL distribution's look, landmark included, so a real-input probe can open a root's location actions on a
    /// fixture and never on a real root (what the in-app journeys do with <c>PlanetWorld.ProbeCharacter</c>).
    /// </summary>
    public string? TestRootLook { get; init; }

    /// <summary>--test-relief=classic (comparison instrument, PLANET-59): ordinary worlds keep the hills they had before 2026-11-17, for before/after screenshots in one build.</summary>
    public bool TestClassicRelief { get; init; }

    /// <summary>--test-max-islands=N (AERIAL-07 instrument): a deliberately small aerial display budget, so a journey can put
    /// the current place outside the breadth-first selection and exercise landing and recovery.</summary>
    public int? TestMaxIslands { get; init; }

    /// <summary>Test instrument: <c>--test-shell=record</c> records Open/Reveal on live files and launches nothing.</summary>
    public bool TestShellRecord { get; init; }

    /// <summary>--layout=spacious: the place layout before 2026-11-06, for comparison (KD-28). Default: compact.</summary>
    public bool SpaciousLayout { get; init; }

    /// <summary>
    /// Parses the user arguments (after "--"): each is <c>--key=value</c> or a bare <c>--flag</c>, keys case-insensitive,
    /// numbers in the invariant culture. Unknown keys are ignored; a malformed number throws (FormatException) rather than being ignored.
    /// </summary>
    public static CommandLine Parse(string[] args)
    {
        var map = args.Select(a => a.TrimStart('-').Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "", StringComparer.OrdinalIgnoreCase);
        string? Get(string k) => map.TryGetValue(k, out var v) ? v : null;
        Vector2I? size = null;
        if (Get("window") is { } w && w.Split('x') is [var a, var b])
        {
            size = new Vector2I(int.Parse(a, CultureInfo.InvariantCulture), int.Parse(b, CultureInfo.InvariantCulture));
        }

        return new CommandLine
        {
            Root = Get("root"),
            BoundedTree = Get("scan") == "tree",
            SyntheticNodes = Get("synthetic") is { } s ? int.Parse(s, CultureInfo.InvariantCulture) : null,
            SyntheticRate = Get("synthetic-rate") is { } r ? int.Parse(r, CultureInfo.InvariantCulture) : null,
            Visualization = Get("vis") switch { "district" => 1, "classic" => 0, _ => null },
            Experience = Get("vis") switch { "places" => App.Experience.Places, "classic" => App.Experience.Classic, "district" => App.Experience.District, "planets" => App.Experience.Planets, _ => null },
            Quality = Get("quality") switch { "low" => Visual.Quality.Low, "high" => Visual.Quality.High, _ => null },
            Automation = Get("automate"),
            OutputDirectory = Get("out"),
            WindowSize = size,
            UiScale = Get("ui-scale") is { } u ? float.Parse(u, CultureInfo.InvariantCulture) : null,
            OpenTest = Get("open-test"),
            NoVsync = Get("no-vsync") != null,
            StateFile = Get("state-file"),
            StateIntervalMs = Get("state-interval-ms") is { } si ? int.Parse(si, CultureInfo.InvariantCulture) : null,
            StressDistrictOnly = Get("district-only") != null,
            PreferencesFile = Get("prefs"),
            Experiment = Get("experiment"),
            TestHold = Get("test-hold"),
            SpaciousLayout = Get("layout") == "spacious",
            TestMaxIslands = Get("test-max-islands") is { } mi ? int.Parse(mi, CultureInfo.InvariantCulture) : null,
            TestShellRecord = Get("test-shell") == "record",
            TestListDelayMs = Get("test-list-delay-ms") is { } ld ? int.Parse(ld, CultureInfo.InvariantCulture) : null,
            TestListDelayMatch = Get("test-list-delay-match"),
            TestClassicRelief = Get("test-relief") == "classic",
            TestBroad = Get("test-broad") != null,
            TestPlayDiscoveryOff = Get("test-play-discovery") == "off",
            TestOverviewDetailOff = Get("test-overview-detail") == "off",
            TestOverviewLookPlain = Get("test-overview-look") == "plain",
            TestRootLook = Get("test-root-look"),
        };
    }
}

/// <summary>Per-frame timing. A rolling window feeds the overlay; an optional full recording feeds the automation CSVs.</summary>
public sealed class PerfRecorder
{
    /// <summary>The rolling window: the last 600 frame times, about 10 s at 60 fps (the most <see cref="Summary"/> can cover).</summary>
    private readonly double[] _ring = new double[600];
    private int _ringCount, _ringNext;
    private ulong _lastTicks;
    private double _pendingRelayoutMs;

    /// <summary>One recorded frame: the time (seconds since start), the frame and GPU times and any re-layout in it (milliseconds), whether a scan was running and the node count.</summary>
    public sealed record Sample(double TimeS, double FrameMs, double GpuMs, bool Scanning, double RelayoutMs, int Nodes);

    public List<Sample>? Recording { get; private set; }
    public Func<(double GpuMs, int Nodes)>? Probe { get; set; }

    /// <summary>
    /// Records the frame that just ended, once per frame. The frame time is measured from the wall clock between calls
    /// (what the user sees, including vsync waits); only the first frame falls back to Godot's <paramref name="delta"/> (seconds).
    /// Re-layout time marked since the last call is attributed to this frame.
    /// </summary>
    public void BeginFrame(double delta, bool scanning)
    {
        var now = Time.GetTicksUsec();
        var ms = _lastTicks == 0 ? delta * 1000 : (now - _lastTicks) / 1000.0;
        _lastTicks = now;
        _ring[_ringNext] = ms;
        _ringNext = (_ringNext + 1) % _ring.Length;
        _ringCount = Math.Min(_ringCount + 1, _ring.Length);
        if (Recording != null)
        {
            var (gpu, nodes) = Probe?.Invoke() ?? (0, 0);
            Recording.Add(new Sample(now / 1e6, ms, gpu, scanning, _pendingRelayoutMs, nodes));
        }

        _pendingRelayoutMs = 0;
    }

    public void MarkRelayout(double ms) => _pendingRelayoutMs += ms;

    public void StartRecording() => Recording = [];

    public List<Sample> StopRecording()
    {
        var r = Recording ?? [];
        Recording = null;
        return r;
    }

    /// <summary>Frame-time statistics, milliseconds (percentiles by nearest rank), over <c>Count</c> frames.</summary>
    public record struct Stats(double Mean, double P50, double P95, double P99, double Max, int Count);

    /// <summary>Statistics of the most recent <paramref name="lastN"/> frames in the rolling window (fewer if fewer were seen).</summary>
    public Stats Summary(int lastN)
    {
        var n = Math.Min(lastN, _ringCount);
        var values = new double[n];
        for (var i = 0; i < n; i++)
        {
            values[i] = _ring[(_ringNext - 1 - i + _ring.Length) % _ring.Length];
        }

        return Summarize(values);
    }

    public static Stats Summarize(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return default;
        }

        var sorted = values.OrderBy(v => v).ToArray();
        double Pct(double p) => sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
        return new Stats(sorted.Average(), Pct(0.5), Pct(0.95), Pct(0.99), sorted[^1], sorted.Length);
    }
}

/// <summary>Builds the inspector text: true numerical metadata, with honest labels for estimates and gaps (INSP-01..04).</summary>
public static class Inspector
{
    /// <summary>The inspector's BBCode text for one node: what it is, where a link leads, sizes, scan state, times and flags, and whether it is live or synthetic.</summary>
    public static string Describe(FsNode n, FsTree tree, Visualization vis)
    {
        var sb = new StringBuilder();
        var path = tree.FullPath(n.Id);
        sb.Append(CultureInfo.InvariantCulture, $"[color=#8fa6c8]Full path ({path.Length} chars)[/color]\n[code]{Escape(path)}[/code]\n\n");

        var kind = n.Kind switch
        {
            NodeKind.Directory when n.IsHub => "The hub of all locations (drives and WSL distributions)",
            NodeKind.Directory when n.Location == LocationKind.Wsl => "WSL distribution root (Linux, through \\\\wsl.localhost)",
            NodeKind.Directory when n.Location == LocationKind.Drive => "Drive root",
            NodeKind.Directory when n.Location != LocationKind.None => "Location root",
            NodeKind.Directory => n.IsRoot ? "Directory (scan root)" : "Directory",
            NodeKind.Link => n.Link is { } li ? $"{Cap(li.KindName)}{((n.Flags & NodeFlags.DirectoryLink) != 0 ? " to a folder" : "")}" : (n.Flags & NodeFlags.DirectoryLink) != 0 ? "Link to a directory" : "Link to a file",
            _ when (n.Flags & NodeFlags.AppAlias) != 0 => "App execution alias (a Windows launcher; opening it runs a program)",
            _ when (n.Flags & NodeFlags.LinuxSpecial) != 0 => "Linux special file (FIFO, socket or device): shown, never opened",
            _ => $"File · {FileCategories.Of(n.Name)}{(System.IO.Path.GetExtension(n.Name) is { Length: > 0 } ext ? $" ({ext})" : "")}",
        };
        Row(sb, "Type", kind);
        if (n.Link is { } link && n.Kind == NodeKind.Link)
        {
            // LINK-06/08: where it points, whether it can be followed, and why not.
            if (link.TargetText != null) Row(sb, "Points to", $"[code]{Escape(link.TargetText)}[/code]");
            var status = link.Status switch
            {
                LinkStatus.Resolved when link.TargetId >= 0 && tree.Contains(link.TargetId) => $"[color=#7fe0e0]a connection to {Escape(tree.FullPath(link.TargetId))}[/color] — walk through its gate (or G) to go there; Backspace returns",
                LinkStatus.Unresolved when link.CanFollow => "[color=#7fe0e0]a connection, not followed yet[/color] — walk through its gate (or G) to follow it",
                _ => $"[color=#ffb070]{Escape(FileScape.Core.Scanning.WorldExplorer.Explain(link))}[/color]",
            };
            Row(sb, "Connection", status);
            if (link.Kind == LinkKind.LinuxSymlink)
            {
                sb.Append("[color=#7f8fab][font_size=12]Through \\\\wsl.localhost Windows reports only that this is a Linux link (and the length of its target). Its target cannot be read or followed from Windows.[/font_size][/color]\n");
            }

            var others = tree.Nodes.Count(o => o.Kind == NodeKind.Link && o.Id != n.Id && o.Link?.TargetId is { } t && t >= 0 && t == link.TargetId);
            if (others > 0) Row(sb, "Also", $"{others} other link{(others == 1 ? "" : "s")} lead{(others == 1 ? "s" : "")} to the same folder");
        }

        if (n.Kind == NodeKind.File)
        {
            Row(sb, "Logical size", $"{SizeFormat.Exact(n.Size)}  ({SizeFormat.Short(n.Size)})");
            Row(sb, "Visual", vis.LayoutParams.Scale.Describe(n.Size));
            Row(sb, "On-disk size", "[color=#9fb0cc]not queried — FileScape does not open files; compressed, sparse and cloud files may use less[/color]");
        }
        else if (n.Kind == NodeKind.Directory)
        {
            // What is here and what is in the subtree, with completeness, from the incremental aggregates (SUM-01..04).
            foreach (var (label, value) in FileScape.Core.Places.FolderSummary.Inspector(n))
            {
                Row(sb, label, label == "Found so far" ? $"[color=#ffd36b]{Escape(value)}[/color]" : Escape(value));
            }

            sb.Append($"[color=#7f8fab][font_size=12]{FileScape.Core.Places.FolderSummary.Conventions}[/font_size][/color]\n");
            Row(sb, "Scan state", n.ScanState switch
            {
                DirScanState.Complete => (n.Flags & NodeFlags.Incomplete) != 0 ? "[color=#ffd36b]listed partially[/color]" : "listed",
                DirScanState.Pending => "[color=#b6c2d6]queued — not listed yet[/color]",
                DirScanState.Failed => "[color=#ff6b5b]could not be listed[/color]",
                DirScanState.Cancelled => "[color=#ffd36b]not listed — scan cancelled[/color]",
                DirScanState.NotTraversed => (n.Flags & NodeFlags.CloudNotTraversed) != 0 ? "[color=#cfe3ff]not listed — cloud placeholder directory (listing could fetch from the provider)[/color]" : "[color=#ffd36b]not listed — entry limit reached[/color]",
                DirScanState.Unlisted => "[color=#b6c2d6]not listed yet — it is listed when you go in[/color]",
                _ => "-",
            });
        }

        if (n.Error != null)
        {
            Row(sb, "Windows said", $"[color=#ff8a7a]{Escape(n.Error)}[/color]");
        }

        Row(sb, "Modified", Time(n.ModifiedUtc) + (n.Kind == NodeKind.File ? $"  ·  age band {AgeBands.Labels[AgeBands.Band(n.ModifiedUtc, tree.Source.AgeReferenceUtc)]}" : ""));
        Row(sb, "Created", Time(n.CreatedUtc));
        Row(sb, "Accessed", Time(n.AccessedUtc) + "  [color=#7f8fab](Windows may update this lazily)[/color]");

        var flags = DescribeFlags(n);
        if (flags.Length > 0)
        {
            Row(sb, "Flags", flags);
        }

        if (tree.Source.IsLive)
        {
            Row(sb, "Attributes", n.Attributes == 0 ? "none" : $"{n.Attributes} (0x{(uint)n.Attributes:X})");
            sb.Append("\n[color=#7fe0a0]LIVE — read from the filesystem (metadata only) when this place was listed.[/color]");
        }
        else
        {
            sb.Append("\n[color=#ffcf70]SYNTHETIC — generated sample data; nothing on disk. Open is a mock.[/color]");
        }

        return sb.ToString();
    }

    private static string DescribeFlags(FsNode n)
    {
        var parts = new List<string>();
        void Add(NodeFlags f, string text, string color = "#c8d4ea")
        {
            if ((n.Flags & f) != 0) parts.Add($"[color={color}]{text}[/color]");
        }

        Add(NodeFlags.Hidden, "hidden");
        Add(NodeFlags.System, "system");
        Add(NodeFlags.ReadOnly, "read-only");
        Add(NodeFlags.ReparsePoint, "reparse point", "#66eeff");
        Add(NodeFlags.Unavailable, "UNAVAILABLE (drive, share or distribution not reachable)", "#ffaa44");
        Add(NodeFlags.AppAlias, "app alias", "#ffaa44");
        Add(NodeFlags.LinuxSpecial, "Linux special file", "#c8d4ea");
        Add(NodeFlags.CloudPlaceholder, "cloud placeholder (contents may not be local)", "#cfe3ff");
        Add(NodeFlags.Sparse, "sparse");
        Add(NodeFlags.Compressed, "compressed");
        Add(NodeFlags.Encrypted, "encrypted");
        Add(NodeFlags.AccessDenied, "ACCESS DENIED", "#ff6b5b");
        Add(NodeFlags.Vanished, "VANISHED during scan", "#ffaa44");
        Add(NodeFlags.EnumerationError, "listing error", "#ff6b5b");
        Add(NodeFlags.Incomplete, "incomplete", "#ffd36b");
        Add(NodeFlags.Synthetic, "synthetic", "#ffcf70");
        return string.Join(", ", parts);
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static void Row(StringBuilder sb, string key, string value) =>
        sb.Append(CultureInfo.InvariantCulture, $"[color=#8fa6c8]{key}[/color]  {value}\n");

    private static string Time(DateTime? utc) =>
        utc is { } t ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " local" : "[color=#9fb0cc]unavailable[/color]";

    /// <summary>Escapes a file-system string for BBCode: a '[' in a name would otherwise start a tag.</summary>
    private static string Escape(string s) => s.Replace("[", "[lb]", StringComparison.Ordinal);
}
