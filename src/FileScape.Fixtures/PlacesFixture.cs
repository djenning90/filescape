namespace FileScape.Fixtures;

/// <summary>
/// The deterministic on-disk tree for the Places experience (PLACE-*): a modest, evaluable folder tree plus the
/// difficult cases — an empty root, an empty child, many files with few folders, many folders, a deep chain, an
/// access-denied folder and a junction. File sizes are real logical sizes; files above 64 KiB are sparse, so the
/// fixture uses little disk. It lives in an owned temporary directory with the fixture marker, so cleanup can never
/// touch anything else.
/// </summary>
public static class PlacesFixture
{
    public const string RootName = "Atlas Studio";
    public const string EmptyRootName = "empty-root";

    public sealed record Result(string FixtureDir, string Root, string EmptyRoot, FixtureManifest Manifest)
    {
        public override string ToString() => $"places fixture: {FixtureDir}\n  root: {Root}\n  empty root: {EmptyRoot}\n{Manifest}";
    }

    public static Result Create(string? parent = null)
    {
        parent ??= FixtureBuilder.DefaultParent;
        var dir = Path.Combine(parent, "places-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        var m = new FixtureManifest { Root = dir };
        FixtureBuilder.WriteFile(m, Path.Combine(dir, FixtureBuilder.MarkerName), "FileScape Places fixture. Safe to delete.\n"u8.ToArray());
        var emptyRoot = FixtureBuilder.MakeDir(m, dir, EmptyRootName);
        var root = FixtureBuilder.MakeDir(m, dir, RootName);

        File(m, root, "readme.txt", 1_240);
        File(m, root, "plan.md", 8_300);
        File(m, root, "logo.png", 52_000);
        File(m, root, "budget.xlsx", 19_400);
        File(m, root, "release-build.zip", 3_400_000);
        File(m, root, "settings.json", 640);

        var docs = Dir(m, root, "Documents");
        string[] docNames = ["proposal.docx", "minutes-2025-03.md", "minutes-2025-06.md", "contract.pdf", "handbook.pdf", "ideas.txt", "roadmap.pptx", "invoice-0042.pdf", "budget-2026.xlsx", "letter.rtf", "notes.txt", "summary.odt"];
        for (var i = 0; i < docNames.Length; i++) File(m, docs, docNames[i], 2_000L * (i + 1) * (i + 1) * 7);
        var letters = Dir(m, docs, "Letters");
        for (var i = 1; i <= 5; i++) File(m, letters, $"letter-{i:D2}.docx", 9_000 + i * 1_300);
        var reports = Dir(m, docs, "Reports 2025");
        for (var i = 1; i <= 8; i++) File(m, reports, $"report-q{(i - 1) % 4 + 1}-{i:D2}.pdf", 120_000 + i * 45_000);

        var photos = Dir(m, root, "Photos");
        for (var i = 1; i <= 40; i++) File(m, photos, $"IMG_{2400 + i}.jpg", 280_000 + (i * 7919 % 37) * 90_000);
        foreach (var year in new[] { "2024", "2025" })
        {
            var y = Dir(m, photos, year);
            for (var i = 1; i <= 12; i++) File(m, y, $"{year}-{i:D2}-trip.jpg", 1_100_000 + i * 120_000);
        }

        var music = Dir(m, root, "Music");
        foreach (var album in new[] { "Nightlines", "Glass Harbor", "Signal Bloom" })
        {
            var a = Dir(m, music, album);
            for (var t = 1; t <= 10; t++) File(m, a, $"{t:D2} - track.mp3", 3_000_000 + t * 210_000);
            File(m, a, "cover.png", 640_000);
        }

        var code = Dir(m, root, "Code");
        var src = Dir(m, code, "src");
        foreach (var f in new[] { "Program.cs", "World.cs", "Gate.cs", "Plaza.cs", "Scanner.cs", "Camera.cs" }) File(m, src, f, 3_000 + f.Length * 900);
        var tests = Dir(m, code, "tests");
        foreach (var f in new[] { "WorldTests.cs", "GateTests.cs" }) File(m, tests, f, 4_200);
        var cdocs = Dir(m, code, "docs");
        File(m, cdocs, "architecture.md", 11_000);
        File(m, code, "app.csproj", 1_400);
        File(m, code, "README.md", 2_900);

        var archive = Dir(m, root, "Archive");
        foreach (var (name, size) in new[] { ("2023-backup.zip", 48_000_000L), ("old-site.tar", 9_200_000L), ("photos-2019.7z", 210_000_000L), ("disk-image.iso", 2_400_000_000L) }) File(m, archive, name, size);

        Dir(m, root, "empty-room");

        var many = Dir(m, root, "many-files");
        string[] ext = [".txt", ".png", ".cs", ".zip", ".json", ".mp3", ".log", ".pdf", ".dat"];
        for (var i = 0; i < 800; i++) File(m, many, $"item-{i:D4}{ext[i % ext.Length]}", (long)(37 * Math.Pow(1.9, i % 21)));
        Dir(m, many, "one");
        Dir(m, many, "two");

        var wide = Dir(m, root, "wide-hall");
        for (var i = 1; i <= 240; i++)
        {
            var unit = Dir(m, wide, $"unit-{i:D3}");
            for (var f = 0; f <= i % 3; f++) File(m, unit, $"part-{f}.dat", 1_000 + i * 37 + f * 11);
        }

        var cursor = Dir(m, root, "deep-chain");
        for (var level = 1; level <= 25; level++) cursor = Dir(m, cursor, $"level-{level:D2}");
        File(m, cursor, "bottom.txt", 25);

        if (OperatingSystem.IsWindows())
        {
            FixtureBuilder.CreateDenied(m, Path.Combine(root, "locked"));
            FixtureBuilder.CreateJunction(m, Path.Combine(root, "link-to-documents"), docs);
        }
        else
        {
            m.Skipped.Add("locked (deny ACE) and junction need Windows");
        }

        return new Result(dir, root, emptyRoot, m);
    }

    private static string Dir(FixtureManifest m, string parent, string name) => FixtureBuilder.MakeDir(m, parent, name);

    /// <summary>Writes a file of an exact logical size; larger files are sparse so the fixture stays small on disk.</summary>
    private static void File(FixtureManifest m, string dir, string name, long size)
    {
        var path = Path.Combine(dir, name);
        if (size > 65_536 && OperatingSystem.IsWindows())
        {
            FixtureBuilder.CreateSparse(m, path, size);
        }
        else
        {
            FixtureBuilder.WriteFile(m, path, new byte[Math.Min(size, 65_536)]);
        }
    }
}
