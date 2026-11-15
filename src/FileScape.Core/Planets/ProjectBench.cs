using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;

namespace FileScape.Core.Planets;

/// <summary>What a key location is to a project (PLANET-29).</summary>
public enum BenchRole
{
    Readme,
    Solution,
    Manifest,
    Entry,
    Source,
    Tests,
    Docs,
    Tools,
    Build,
    Ci,
    License,
}

/// <summary>One of a project's key locations: the node, what it is, and a short reason shown on the bench.</summary>
public sealed record BenchEntry(int NodeId, BenchRole Role, string Why);

/// <summary>
/// The repository workbench's reading of a project (PLANET-29, AD-50): its README and key entry points, chosen from the
/// names of the project folder's own entries and, when already listed, of its <c>src</c>-like folders. Names only (FS-01):
/// nothing is read and nothing is listed for it. Ordered for the bench: README first, then how to build it (solution,
/// manifest), where it starts (entry files), then its main folders; at most <see cref="MaxEntries"/>.
/// </summary>
/// <remarks>The app (<c>PlanetWorld.Bench.cs</c>) shows these as cards rising from the workbench and lists them in its panel.</remarks>
public static class ProjectBench
{
    /// <summary>The most key locations listed (AD-50: "at most 9").</summary>
    public const int MaxEntries = 9;

    /// <summary>File names that usually start a program, across common languages and Godot (exact, case-sensitive).</summary>
    private static readonly string[] EntryNames =
    [
        "Program.cs", "Main.cs", "App.cs", "main.py", "__main__.py", "app.py", "manage.py", "cli.py", "main.go", "main.rs", "lib.rs",
        "index.js", "index.ts", "main.js", "main.ts", "server.js", "app.js", "main.c", "main.cpp", "Main.java", "main.swift", "main.gd",
        "Main.tscn", "main.tscn",
    ];

    /// <summary>Conventional top-level folder names and what they hold, in bench order (case-insensitive).</summary>
    private static readonly (string Name, BenchRole Role, string Why)[] FolderRoles =
    [
        ("src", BenchRole.Source, "the source"), ("app", BenchRole.Source, "the app"), ("lib", BenchRole.Source, "the library"),
        ("source", BenchRole.Source, "the source"), ("cmd", BenchRole.Source, "the commands"), ("packages", BenchRole.Source, "the packages"),
        ("tests", BenchRole.Tests, "the tests"), ("test", BenchRole.Tests, "the tests"), ("spec", BenchRole.Tests, "the specs"),
        ("docs", BenchRole.Docs, "the documentation"), ("doc", BenchRole.Docs, "the documentation"),
        ("tools", BenchRole.Tools, "scripts and tools"), ("scripts", BenchRole.Tools, "scripts"), ("bin", BenchRole.Tools, "scripts"),
        (".github", BenchRole.Ci, "CI and community files"),
    ];

    /// <summary>Build recipe file names (case-insensitive).</summary>
    private static readonly string[] BuildNames = ["Makefile", "Dockerfile", "justfile", "build.sh", "build.ps1", "CMakeLists.txt", "Taskfile.yml"];

    /// <summary>The project's key locations, or empty when the folder has none (an empty folder, say).</summary>
    /// <param name="visible">Entries the user sees (hidden ones still count if they are a README or a manifest).</param>
    public static IReadOnlyList<BenchEntry> KeyLocations(FsTree tree, int folderId, Func<FsNode, bool>? visible = null)
    {
        if (!tree.Contains(folderId)) return [];
        var folder = tree[folderId];
        var children = folder.Children.Select(c => tree[c]).ToList();
        var list = new List<BenchEntry>();
        void Add(FsNode n, BenchRole role, string why)
        {
            if (list.Count < MaxEntries && list.All(e => e.NodeId != n.Id)) list.Add(new BenchEntry(n.Id, role, why));
        }

        bool Shown(FsNode n) => visible == null || visible(n);
        var files = children.Where(n => n.Kind == NodeKind.File).OrderBy(n => n.Name, NaturalNameOrder.Instance).ToList();
        var dirs = children.Where(n => n.IsDirectory || n.Kind == NodeKind.Link).Where(n => visible == null || visible(n)).OrderBy(n => n.Name, NaturalNameOrder.Instance).ToList();

        // README: prefer README.md, then any README.*.
        foreach (var r in files.Where(n => PlaceKinds.IsReadme(n.Name)).OrderBy(n => n.Name.Equals("README.md", StringComparison.OrdinalIgnoreCase) ? 0 : 1).Take(1))
        {
            Add(r, BenchRole.Readme, "start here");
        }

        foreach (var s in files.Where(n => n.Name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || n.Name.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)).Take(1))
        {
            Add(s, BenchRole.Solution, "open the whole solution");
        }

        foreach (var m in files.Where(n => PlaceKinds.IsManifest(n.Name) && !n.Name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) && !n.Name.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)).Take(2))
        {
            Add(m, BenchRole.Manifest, "how it builds");
        }

        // Entry files: here, or one level down in an already listed source folder.
        var sourceDirs = dirs.Where(d => FolderRoles.Any(f => f.Role == BenchRole.Source && f.Name.Equals(d.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        var entryCandidates = files.Where(Shown).Concat(sourceDirs.Where(d => d.IsDirectory).SelectMany(d => d.Children.Select(c => tree[c]).Where(n => n.Kind == NodeKind.File && Shown(n))));
        foreach (var e in entryCandidates.Where(n => EntryNames.Contains(n.Name, StringComparer.Ordinal)).Take(2))
        {
            Add(e, BenchRole.Entry, e.ParentId == folderId ? "where it starts" : $"where it starts (in {tree[e.ParentId].Name})");
        }

        foreach (var (name, role, why) in FolderRoles)
        {
            if (dirs.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } d) Add(d, role, why);
        }

        foreach (var b in files.Where(Shown).Where(n => BuildNames.Contains(n.Name, StringComparer.OrdinalIgnoreCase)).Take(1))
        {
            Add(b, BenchRole.Build, "build recipe");
        }

        foreach (var l in files.Where(Shown).Where(n => n.Name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase) || n.Name.StartsWith("COPYING", StringComparison.OrdinalIgnoreCase)).Take(1))
        {
            Add(l, BenchRole.License, "the licence");
        }

        return list;
    }
}
