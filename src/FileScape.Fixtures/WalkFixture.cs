namespace FileScape.Fixtures;

/// <summary>
/// The walking-world fixture (WALK/LINK/VISI/IDENT, 2026-11-05): an arrival root "Wayfarer" with a software project,
/// ordinary folders, visibility cases (a dot folder, a Hidden folder, a System folder, a dot file), a Unicode name,
/// an access-denied folder, and junctions: to a folder outside the root, back to the root (a loop), two routes to
/// one folder, and a broken one. Everything lives in an owned, marked temporary directory; only names and sizes
/// are synthetic content.
/// </summary>
public static class WalkFixture
{
    /// <summary>Files above this size are created sparse (on Windows) and smaller ones written in full, bytes (64 KiB).</summary>
    private const long SparseAboveBytes = 65_536;

    public const string RootName = "Wayfarer";
    public const string OutsideName = "Shared Library";

    /// <summary>The created fixture: its own directory, the arrival root, the folder outside it that a junction reaches, and the manifest.</summary>
    public sealed record Result(string FixtureDir, string Root, string Outside, FixtureManifest Manifest)
    {
        public override string ToString() => $"walk fixture: {FixtureDir}\n  root: {Root}\n  outside: {Outside}\n{Manifest}";
    }

    /// <summary>Creates the fixture in a new, uniquely named, marked directory under <paramref name="parent"/> (default: the fixtures parent).</summary>
    public static Result Create(string? parent = null)
    {
        parent ??= FixtureBuilder.DefaultParent;
        var dir = Path.Combine(parent, "walk-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        var m = new FixtureManifest { Root = dir };
        FixtureBuilder.WriteFile(m, Path.Combine(dir, FixtureBuilder.MarkerName), "FileScape walking-world fixture. Safe to delete.\n"u8.ToArray());
        var root = Dir(m, dir, RootName);
        var outside = Dir(m, dir, OutsideName);

        File(m, root, "welcome.txt", 900);
        File(m, root, "map.png", 240_000);
        File(m, root, "itinerary.md", 4_200);
        File(m, root, ".env", 120); // a dot file: shown by default (AD-19)

        var project = Dir(m, Dir(m, root, "Projects"), "lighthouse");
        File(m, project, "Lighthouse.csproj", 1_800);
        File(m, project, "Lighthouse.sln", 2_400);
        // Real text (2026-11-06, DEV-02): the README is read on request, so it must be text, with markup FileScape must not apply.
        FixtureBuilder.WriteFile(m, Path.Combine(project, "README.md"), System.Text.Encoding.UTF8.GetBytes(LighthouseReadme));
        File(m, project, "LICENSE", 1_070);
        File(m, project, ".gitignore", 310);
        var git = Dir(m, project, ".git");
        File(m, git, "HEAD", 23);
        var src = Dir(m, project, "src");
        foreach (var f in new[] { "Program.cs", "Beam.cs", "Keeper.cs", "Fog.cs", "Harbor.cs" }) File(m, src, f, 2_000 + f.Length * 700);
        var tests = Dir(m, project, "tests");
        File(m, tests, "BeamTests.cs", 3_300);
        var docs = Dir(m, project, "docs");
        File(m, docs, "design.md", 9_000);
        var notes = Dir(m, Dir(m, root, "Projects"), "notes-only");
        File(m, notes, "README.md", 800); // a README without a manifest: not a project
        File(m, notes, "ideas.txt", 1_200);

        var photos = Dir(m, root, "Photos");
        for (var i = 1; i <= 24; i++) File(m, photos, $"IMG_{5100 + i}.jpg", 900_000 + (i * 7919 % 29) * 110_000);
        foreach (var year in new[] { "2024", "2025" })
        {
            var y = Dir(m, photos, year);
            for (var i = 1; i <= 8; i++) File(m, y, $"{year}-{i:D2}-coast.jpg", 1_400_000 + i * 90_000);
        }

        var documents = Dir(m, root, "Documents");
        foreach (var (n, size) in new[] { ("letter.docx", 18_000L), ("plan.pdf", 240_000L), ("budget.xlsx", 31_000L), ("notes.txt", 2_000L), ("slides.pptx", 1_200_000L) }) File(m, documents, n, size);
        var reports = Dir(m, documents, "Reports");
        for (var i = 1; i <= 6; i++) File(m, reports, $"report-{i:D2}.pdf", 150_000 + i * 30_000);

        var unicode = Dir(m, root, "Café Notes – 東京");
        File(m, unicode, "résumé.txt", 3_100);
        Dir(m, root, "empty-room");
        var longName = Dir(m, root, "a folder with a deliberately long descriptive name for label testing 2026");
        File(m, longName, "note.txt", 400);
        var dot = Dir(m, root, ".config");
        File(m, dot, "settings.json", 700);
        var hidden = Dir(m, root, "hidden-cache");
        File(m, hidden, "blob.bin", 50_000);
        var system = Dir(m, root, "system-stuff");
        File(m, system, "desktop.ini", 200);

        // A chain of ordinary folders for continuous upward travel (PLACE-16, 2026-11-05 evening): Pier → Harbour →
        // Coast → Journeys → Wayfarer, each with files and side folders, and numbered legs for natural order (ORD-01).
        var journeys = Dir(m, root, "Journeys");
        foreach (var leg in new[] { "Leg 1", "Leg 2", "Leg 10" }) File(m, Dir(m, journeys, leg), "log.txt", 1_500);
        File(m, journeys, "routes.gpx", 64_000);
        var coast = Dir(m, journeys, "Coast");
        foreach (var side in new[] { "North", "South" }) File(m, Dir(m, coast, side), "survey.csv", 9_000);
        for (var i = 1; i <= 6; i++) File(m, coast, $"coast-{i:D2}.jpg", 700_000 + i * 40_000);
        var harbour = Dir(m, coast, "Harbour");
        foreach (var side in new[] { "Boats", "Nets" }) File(m, Dir(m, harbour, side), "list.txt", 800);
        File(m, harbour, "tides.pdf", 120_000);
        var pier = Dir(m, harbour, "Pier");
        File(m, pier, "plank-count.txt", 300);

        var manuals = Dir(m, outside, "Manuals");
        foreach (var n in new[] { "boat.pdf", "radio.pdf", "engine.pdf" }) File(m, manuals, n, 400_000);
        File(m, outside, "catalogue.xlsx", 88_000);
        File(m, outside, "index.md", 2_000);

        if (OperatingSystem.IsWindows())
        {
            System.IO.File.SetAttributes(hidden, FileAttributes.Directory | FileAttributes.Hidden);
            System.IO.File.SetAttributes(system, FileAttributes.Directory | FileAttributes.System);
            FixtureBuilder.CreateDenied(m, Path.Combine(root, "locked"));
            FixtureBuilder.CreateJunction(m, Path.Combine(root, "to-shared-library"), outside);
            FixtureBuilder.CreateJunction(m, Path.Combine(root, "back-to-wayfarer"), root);
            FixtureBuilder.CreateJunction(m, Path.Combine(root, "photos-shortcut"), photos);
            FixtureBuilder.CreateJunction(m, Path.Combine(root, "also-photos"), photos);
            // A broken junction: made to a real folder (mklink needs one), whose target is then removed.
            var gone = Dir(m, dir, "gone");
            FixtureBuilder.CreateJunction(m, Path.Combine(root, "broken-link"), gone);
            Directory.Delete(gone);
        }
        else
        {
            m.Skipped.Add("attributes, deny ACE and junctions need Windows");
        }

        return new Result(dir, root, outside, m);
    }

    private static string Dir(FixtureManifest m, string parent, string name) => FixtureBuilder.MakeDir(m, parent, name);

    /// <summary>The lighthouse project's README: headings, a list, a link, an image, code and markup that must stay inert.</summary>
    public const string LighthouseReadme = """
        # Lighthouse

        A small **fixture** project for FileScape's project journey.

        - Builds with `dotnet build Lighthouse.sln`
        - See [the guide](https://example.invalid/guide) and ![the logo](https://example.invalid/logo.png)
        - [color=red]This markup must stay text[/color]

        ```
        echo "FileScape never runs this"
        ```
        """;

    /// <summary>Writes a file of an exact logical size; larger files are sparse so the fixture stays small on disk (off Windows, capped at 64 KiB).</summary>
    private static void File(FixtureManifest m, string dir, string name, long size)
    {
        var path = Path.Combine(dir, name);
        if (size > SparseAboveBytes && OperatingSystem.IsWindows()) FixtureBuilder.CreateSparse(m, path, size);
        else FixtureBuilder.WriteFile(m, path, new byte[Math.Min(size, SparseAboveBytes)]);
    }
}
