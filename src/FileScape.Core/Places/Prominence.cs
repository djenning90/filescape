using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Scanning;

namespace FileScape.Core.Places;

/// <summary>Why a destination stands out (PROM-01). <see cref="None"/> is the ordinary case.</summary>
public enum ProminenceKind
{
    None,

    /// <summary>The user pinned it (PROM-03): the strongest, and always the user's own choice.</summary>
    Pinned,

    /// <summary>A home: <c>/home</c>, a Linux user's home, or the Windows profile folder.</summary>
    Home,

    /// <summary>A folder the platform itself names (Downloads, Pictures, Documents, …), resolved by the OS.</summary>
    KnownFolder,

    /// <summary>A software project, from the same name-only evidence as the project place (IDENT-01).</summary>
    Project,

    /// <summary>A folder whose listed child folders include software projects.</summary>
    Projects,
}

/// <summary>A destination's prominence: what it is, how strongly to show it (0–2), a short mark, and the reason in words.</summary>
public sealed record ProminenceInfo(ProminenceKind Kind, int Level, string Mark, string Reason)
{
    public static readonly ProminenceInfo None = new(ProminenceKind.None, 0, "", "");

    public bool IsProminent => Kind != ProminenceKind.None;
}

/// <summary>What the platform and the user say about places (nothing here comes from file contents).</summary>
public sealed class ProminenceContext
{
    public static readonly ProminenceContext Empty = new();

    /// <summary>Full paths the user pinned.</summary>
    public IReadOnlySet<string> Pins { get; init; } = new HashSet<string>();

    /// <summary>Platform known folders: full path → name (e.g. Downloads), as resolved by the OS.</summary>
    public IReadOnlyDictionary<string, string> KnownFolders { get; init; } = new Dictionary<string, string>();

    /// <summary>Folders the user said are not projects (IDENT-03); they get no project prominence either.</summary>
    public IReadOnlySet<int> NotProjects { get; init; } = new HashSet<int>();

    public static StringComparer PathComparer => StringComparer.OrdinalIgnoreCase;
}

/// <summary>
/// PROM-01..04 (AD-32): explainable prominence for destinations. Evidence only from what is already known: the user's
/// pins, folders the platform names, home locations by their standard paths, and project evidence from names in folders
/// already listed (never contents, and nothing is listed to find candidates). It marks and emphasises; it never hides
/// or reorders anything.
/// </summary>
public static class Prominence
{
    public static ProminenceInfo For(FsTree tree, int id, ProminenceContext ctx)
    {
        var n = tree[id];
        if (!n.IsDirectory || n.IsHub) return ProminenceInfo.None;
        if (ByPath(tree.FullPath(id), ctx) is { } byPath) return byPath;
        return ByEvidence(tree, id, ctx);
    }

    /// <summary>The path-based reasons: a pin, a platform known folder, a home location. Null when none applies.</summary>
    public static ProminenceInfo? ByPath(string path, ProminenceContext ctx)
    {
        if (ctx.Pins.Contains(path)) return new(ProminenceKind.Pinned, 2, "★", "Pinned by you (unpin from its card).");
        if (ctx.KnownFolders.TryGetValue(path.TrimEnd('\\'), out var known))
        {
            return new(ProminenceKind.KnownFolder, known == "Home" ? 2 : 1, known == "Home" ? "⌂" : "◆", known == "Home"
                ? "Your Windows home folder (as Windows reports it)."
                : $"Your {known} folder (as Windows reports it).");
        }

        if (ListAhead.LinuxPath(path) is { } linux)
        {
            if (linux == "/home") return new(ProminenceKind.Home, 2, "⌂", "Home folders of this Linux system's users.");
            if (linux == "/root" || (linux.StartsWith("/home/", StringComparison.Ordinal) && linux.Count(c => c == '/') == 2))
            {
                return new(ProminenceKind.Home, 2, "⌂", "A Linux user's home folder.");
            }
        }

        return null;
    }

    /// <summary>Project evidence from names in folders already listed (IDENT-01); never contents, never a new listing.</summary>
    public static ProminenceInfo ByEvidence(FsTree tree, int id, ProminenceContext ctx)
    {
        var n = tree[id];
        if (!ctx.NotProjects.Contains(id))
        {
            var kind = PlaceKinds.Classify(tree, id);
            if (kind.Kind == PlaceKind.Project)
            {
                return new(ProminenceKind.Project, 1, "⚙", $"Software project: {string.Join(", ", kind.Evidence.Take(3))}.");
            }

            var projects = new List<string>();
            foreach (var c in n.Children)
            {
                var child = tree[c];
                if (child.IsDirectory && !ctx.NotProjects.Contains(c) && PlaceKinds.Classify(tree, c).Kind == PlaceKind.Project) projects.Add(child.Name);
            }

            if (projects.Count > 0)
            {
                projects.Sort(NaturalNameOrder.Instance);
                var shown = string.Join(", ", projects.Take(3)) + (projects.Count > 3 ? $" and {projects.Count - 3} more" : "");
                return new(ProminenceKind.Projects, 1, "⚙", $"Contains software projects: {shown}.");
            }
        }

        return ProminenceInfo.None;
    }
}
