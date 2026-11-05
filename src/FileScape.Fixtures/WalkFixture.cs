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
    public const string RootName = "Wayfarer";
    public const string OutsideName = "Shared Library";

    public sealed record Result(string FixtureDir, string Root, string Outside, FixtureManifest Manifest)
    {
        public override string ToString() => $"walk fixture: {FixtureDir}\n  root: {Root}\n  outside: {Outside}\n{Manifest}";
    }

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
        File(m, project, "README.md", 6_100);
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
        var dot = Dir(m, root, ".config");
        File(m, dot, "settings.json", 700);
        var hidden = Dir(m, root, "hidden-cache");
        File(m, hidden, "blob.bin", 50_000);
        var system = Dir(m, root, "system-stuff");
        File(m, system, "desktop.ini", 200);

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

    private static void File(FixtureManifest m, string dir, string name, long size)
    {
        var path = Path.Combine(dir, name);
        if (size > 65_536 && OperatingSystem.IsWindows()) FixtureBuilder.CreateSparse(m, path, size);
        else FixtureBuilder.WriteFile(m, path, new byte[Math.Min(size, 65_536)]);
    }
}
