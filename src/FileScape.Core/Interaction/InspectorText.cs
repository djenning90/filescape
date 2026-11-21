using System.Globalization;
using System.Text;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Scanning;

namespace FileScape.Core.Interaction;

/// <summary>
/// The legacy views' inspector text (INSP-01..04): true numerical metadata, with honest labels for estimates and gaps,
/// as BBCode for the HUD's inspector. Moved to Core from the app (PLANET-152) so that what it does with names is tested.
/// Preserve: every name, path, link target, OS message and summary value goes through <see cref="Escape"/>.
/// </summary>
public static class InspectorText
{
    /// <summary>The inspector's BBCode text for one node: what it is, where a link leads, sizes, scan state, times and flags, and whether it is live or synthetic.</summary>
    public static string Describe(FsNode n, FsTree tree, SizeScale scale)
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
            _ => $"File · {FileCategories.Of(n.Name)}{(System.IO.Path.GetExtension(n.Name) is { Length: > 0 } ext ? $" ({Escape(ext)})" : "")}", // the extension is part of the name
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
                _ => $"[color=#ffb070]{Escape(WorldExplorer.Explain(link))}[/color]",
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
            Row(sb, "Visual", scale.Describe(n.Size));
            Row(sb, "On-disk size", "[color=#9fb0cc]not queried — FileScape does not open files; compressed, sparse and cloud files may use less[/color]");
        }
        else if (n.Kind == NodeKind.Directory)
        {
            // What is here and what is in the subtree, with completeness, from the incremental aggregates (SUM-01..04).
            foreach (var (label, value) in FolderSummary.Inspector(n))
            {
                Row(sb, label, label == "Found so far" ? $"[color=#ffd36b]{Escape(value)}[/color]" : Escape(value));
            }

            sb.Append($"[color=#7f8fab][font_size=12]{FolderSummary.Conventions}[/font_size][/color]\n");
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
        Add(NodeFlags.CloudSynced, "kept in sync by a cloud provider", "#cfe3ff");
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

    /// <summary>Escapes a file-system string for BBCode: a '[' in a name would otherwise start a tag (<see cref="UiText.Escape"/>).</summary>
    private static string Escape(string s) => UiText.Escape(s);
}
