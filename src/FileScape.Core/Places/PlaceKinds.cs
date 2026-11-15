using FileScape.Core.Model;

namespace FileScape.Core.Places;

/// <summary>What a place is (AD-20, IDENT-01..04): separate from the world style and from its listing/access state.</summary>
public enum PlaceKind
{
    /// <summary>Any other folder: the default whenever the evidence is not strong.</summary>
    Ordinary,

    /// <summary>"This computer": the arrival hub for drives and distributions.</summary>
    Hub,

    /// <summary>A drive, a WSL distribution, a share or a volume root.</summary>
    LocationRoot,

    /// <summary>A software project, recognised from strong local evidence (a manifest plus a README or .git).</summary>
    Project,
}

/// <summary>A place's identity with the evidence for it, so it can be explained and overridden (IDENT-03).</summary>
public sealed record PlaceKindInfo(PlaceKind Kind, string Label, IReadOnlyList<string> Evidence, IReadOnlyList<int> CoreFiles, bool Overridden = false)
{
    /// <summary>The plain folder, with no evidence and no core files.</summary>
    public static readonly PlaceKindInfo Ordinary = new(PlaceKind.Ordinary, "Folder", [], []);
}

/// <summary>
/// Recognises place identities from names only — never file contents (FS-01). Uncertain cases stay ordinary folders.
/// <para>
/// The composer uses the result to give a project its "core" (README and manifests by the avenue, IDENT-04), the HUD
/// and inspector show the evidence, and <see cref="Prominence"/> reuses the same test so a project's mark and its place
/// always agree. It reads only the folder's already listed children: it never lists anything.
/// </para>
/// </summary>
public static class PlaceKinds
{
    /// <summary>At most this many manifests are named in the evidence and placed in a project's core (the rest are counted).</summary>
    private const int MaxManifestsShown = 4;

    /// <summary>At most this many READMEs are named in the evidence and placed in a project's core.</summary>
    private const int MaxReadmesShown = 2;

    private static readonly string[] ManifestNames =
    [
        "package.json", "Cargo.toml", "pyproject.toml", "go.mod", "pom.xml", "build.gradle", "build.gradle.kts",
        "CMakeLists.txt", "project.godot", "setup.py", "composer.json", "Gemfile", "mix.exs", "deno.json", "Package.swift",
    ];

    private static readonly string[] ManifestExtensions = [".sln", ".slnx", ".csproj", ".fsproj", ".vbproj", ".vcxproj"];

    /// <summary>A build or package manifest by its exact name (case-insensitive) or its project-file extension.</summary>
    public static bool IsManifest(string name) =>
        ManifestNames.Any(m => m.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
        ManifestExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// "README" alone or followed by '.', '_' or '-' (README.md, README_ja.txt, README-dev), case-insensitive; index 6 is
    /// the character after "README", so a longer word such as "READMEFIRST" is not a README.
    /// </summary>
    public static bool IsReadme(string name) => name.StartsWith("README", StringComparison.OrdinalIgnoreCase) &&
                                                (name.Length == 6 || name[6] == '.' || name[6] == '_' || name[6] == '-');

    /// <summary>
    /// The folder's identity, checked in order: the hub, a location root, then a project — at least one manifest plus a
    /// README or a .git entry (AD-20, IDENT-03: "strong local evidence"; a manifest alone is not enough).
    /// </summary>
    /// <param name="treatAsOrdinary">The user's override for this folder (IDENT-03): the evidence is kept, so the inspector can still explain it.</param>
    public static PlaceKindInfo Classify(FsTree tree, int folderId, bool treatAsOrdinary = false)
    {
        var folder = tree[folderId];
        if (folder.IsHub)
        {
            return new PlaceKindInfo(PlaceKind.Hub, "Locations hub", [$"{folder.Children.Count} locations: drives and WSL distributions"], []);
        }

        if (folder.Location is not LocationKind.None)
        {
            var what = folder.Location switch
            {
                LocationKind.Drive => $"Windows drive {folder.Name}",
                LocationKind.Wsl => $"WSL distribution {folder.Name} (Linux, case-sensitive names)",
                LocationKind.Network => $"Network location {folder.Name}",
                _ => $"Volume {folder.Name}",
            };
            return new PlaceKindInfo(PlaceKind.LocationRoot, folder.Location == LocationKind.Wsl ? "Linux distribution" : folder.Location == LocationKind.Drive ? "Drive" : "Location", [what], []);
        }

        var manifests = new List<int>();
        var readmes = new List<int>();
        var git = false;
        foreach (var c in folder.Children)
        {
            var n = tree[c];
            if (n.Name.Equals(".git", StringComparison.Ordinal)) git = true;
            if (n.Kind != NodeKind.File) continue;
            if (IsManifest(n.Name)) manifests.Add(c);
            else if (IsReadme(n.Name)) readmes.Add(c);
        }

        if (manifests.Count == 0 || (readmes.Count == 0 && !git))
        {
            return PlaceKindInfo.Ordinary;
        }

        var evidence = new List<string>();
        evidence.AddRange(manifests.Take(MaxManifestsShown).Select(m => $"manifest {tree[m].Name}"));
        if (manifests.Count > MaxManifestsShown) evidence.Add($"+{manifests.Count - MaxManifestsShown} more manifests");
        evidence.AddRange(readmes.Take(MaxReadmesShown).Select(r => $"readme {tree[r].Name}"));
        if (git) evidence.Add(".git (version control)");
        var core = readmes.Take(MaxReadmesShown).Concat(manifests.Take(MaxManifestsShown)).ToList();
        return treatAsOrdinary
            ? PlaceKindInfo.Ordinary with { Evidence = evidence, Overridden = true, Label = "Folder (project look turned off)" }
            : new PlaceKindInfo(PlaceKind.Project, "Software project", evidence, core);
    }
}
