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
    public int? SyntheticNodes { get; init; }
    public int? SyntheticRate { get; init; }
    public int? Visualization { get; init; }
    public Quality? Quality { get; init; }
    public string? Automation { get; init; }
    public string? OutputDirectory { get; init; }
    public Vector2I? WindowSize { get; init; }
    public float? UiScale { get; init; }
    public string? OpenTest { get; init; }
    public bool NoVsync { get; init; }

    /// <summary>When set, Main writes its navigation state here as JSON every 150 ms (for OS-level input probes).</summary>
    public string? StateFile { get; init; }
    public bool StressDistrictOnly { get; init; }

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
            SyntheticNodes = Get("synthetic") is { } s ? int.Parse(s, CultureInfo.InvariantCulture) : null,
            SyntheticRate = Get("synthetic-rate") is { } r ? int.Parse(r, CultureInfo.InvariantCulture) : null,
            Visualization = Get("vis") switch { "district" => 1, "classic" => 0, _ => null },
            Quality = Get("quality") switch { "low" => Visual.Quality.Low, "high" => Visual.Quality.High, _ => null },
            Automation = Get("automate"),
            OutputDirectory = Get("out"),
            WindowSize = size,
            UiScale = Get("ui-scale") is { } u ? float.Parse(u, CultureInfo.InvariantCulture) : null,
            OpenTest = Get("open-test"),
            NoVsync = Get("no-vsync") != null,
            StateFile = Get("state-file"),
            StressDistrictOnly = Get("district-only") != null,
        };
    }
}

/// <summary>Per-frame timing. A rolling window feeds the overlay; an optional full recording feeds the automation CSVs.</summary>
public sealed class PerfRecorder
{
    private readonly double[] _ring = new double[600];
    private int _ringCount, _ringNext;
    private ulong _lastTicks;
    private double _pendingRelayoutMs;

    public sealed record Sample(double TimeS, double FrameMs, double GpuMs, bool Scanning, double RelayoutMs, int Nodes);

    public List<Sample>? Recording { get; private set; }
    public Func<(double GpuMs, int Nodes)>? Probe { get; set; }

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

    public record struct Stats(double Mean, double P50, double P95, double P99, double Max, int Count);

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
    public static string Describe(FsNode n, FsTree tree, Visualization vis)
    {
        var sb = new StringBuilder();
        var path = tree.FullPath(n.Id);
        sb.Append(CultureInfo.InvariantCulture, $"[color=#8fa6c8]Full path ({path.Length} chars)[/color]\n[code]{Escape(path)}[/code]\n\n");

        var kind = n.Kind switch
        {
            NodeKind.Directory => n.IsRoot ? "Directory (scan root)" : "Directory",
            NodeKind.Link => (n.Flags & NodeFlags.DirectoryLink) != 0 ? "Link to a directory (junction/symlink/mount) — not followed" : "Link to a file (symlink) — not followed",
            _ => $"File · {FileCategories.Of(n.Name)}{(System.IO.Path.GetExtension(n.Name) is { Length: > 0 } ext ? $" ({ext})" : "")}",
        };
        Row(sb, "Type", kind);

        if (n.Kind == NodeKind.File)
        {
            Row(sb, "Logical size", $"{SizeFormat.Exact(n.Size)}  ({SizeFormat.Short(n.Size)})");
            Row(sb, "Visual", vis.LayoutParams.Scale.Describe(n.Size));
            Row(sb, "On-disk size", "[color=#9fb0cc]not queried — FileScape does not open files; compressed, sparse and cloud files may use less[/color]");
        }
        else if (n.Kind == NodeKind.Directory)
        {
            Row(sb, "Direct contents", $"{n.DirectFileCount:N0} files ({SizeFormat.Exact(n.DirectFileBytes)}), {n.DirectDirCount:N0} directories, {n.Children.Count - n.DirectFileCount - n.DirectDirCount:N0} links");
            var partial = n.SubtreeDirCount > 0 && tree.Nodes.Count > 0 && (n.IsListingIncomplete || HasIncompleteDescendant(tree, n));
            Row(sb, partial ? "Subtree (so far)" : "Subtree total", $"{n.SubtreeFileCount:N0} files, {n.SubtreeDirCount:N0} directories, {SizeFormat.Exact(n.SubtreeBytes)} ({SizeFormat.Short(n.SubtreeBytes)})");
            sb.Append("[color=#7f8fab][font_size=12]Totals: logical sizes; hard-linked files count once per name (may double count); links are not followed.[/font_size][/color]\n");
            Row(sb, "Scan state", n.ScanState switch
            {
                DirScanState.Complete => (n.Flags & NodeFlags.Incomplete) != 0 ? "[color=#ffd36b]listed partially[/color]" : "listed",
                DirScanState.Pending => "[color=#b6c2d6]queued — not listed yet[/color]",
                DirScanState.Failed => "[color=#ff6b5b]could not be listed[/color]",
                DirScanState.Cancelled => "[color=#ffd36b]not listed — scan cancelled[/color]",
                DirScanState.NotTraversed => (n.Flags & NodeFlags.CloudNotTraversed) != 0 ? "[color=#cfe3ff]not listed — cloud placeholder directory (listing could fetch from the provider)[/color]" : "[color=#ffd36b]not listed — entry limit reached[/color]",
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
            sb.Append("\n[color=#7fe0a0]LIVE — read from the Windows filesystem during this scan.[/color]");
        }
        else
        {
            sb.Append("\n[color=#ffcf70]SYNTHETIC — generated sample data; nothing on disk. Open is a mock.[/color]");
        }

        return sb.ToString();
    }

    private static bool HasIncompleteDescendant(FsTree tree, FsNode dir)
    {
        var stack = new Stack<int>(dir.Children);
        var visited = 0;
        while (stack.Count > 0 && visited++ < 200_000)
        {
            var c = tree[stack.Pop()];
            if (c.IsListingIncomplete || (c.Flags & NodeFlags.Incomplete) != 0) return true;
            foreach (var cc in c.Children) stack.Push(cc);
        }

        return false;
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
        Add(NodeFlags.ReparsePoint, "reparse point (not traversed)", "#66eeff");
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

    private static void Row(StringBuilder sb, string key, string value) =>
        sb.Append(CultureInfo.InvariantCulture, $"[color=#8fa6c8]{key}[/color]  {value}\n");

    private static string Time(DateTime? utc) =>
        utc is { } t ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " local" : "[color=#9fb0cc]unavailable[/color]";

    private static string Escape(string s) => s.Replace("[", "[lb]", StringComparison.Ordinal);
}
