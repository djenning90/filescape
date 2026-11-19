namespace FileScape.Fixtures;

/// <summary>
/// The deep fixture for the overview's discovery (PLANET-76..78, 2026-11-19): an arrival root "Deep Atlas" whose folders
/// branch three ways for seven levels below it (3 + 9 + … + 2,187 = 3,279 folders, each with one small file), so the
/// overview can show several more levels after a wait without any of them being visited. It also holds what discovery
/// must treat as ordinary exploration does: a junction back to the root (a loop) and one to a folder outside it, an
/// access-denied folder three levels down, and a hidden folder. Owned, marked directory under the fixtures parent; names
/// and sizes are synthetic.
/// </summary>
public static class DeepFixture
{
    public const string RootName = "Deep Atlas";

    /// <summary>Levels of folders below the root, and folders per folder: 3⁷ = 2,187 at the bottom, more than the
    /// overview draws (1,500), so its budget is exercised too.</summary>
    public const int Levels = 7, Fanout = 3;

    /// <summary>Names per level (each folder's children are this level's names), so a path reads as a route.</summary>
    private static readonly string[][] Names =
    [
        ["North", "East", "West"],
        ["Hills", "Lakes", "Woods"],
        ["Ash", "Birch", "Cedar"],
        ["Dawn", "Noon", "Dusk"],
        ["Amber", "Jade", "Slate"],
        ["One", "Two", "Three"],
        ["Fern", "Moss", "Reed"],
    ];

    public sealed record Result(string FixtureDir, string Root, int Folders, FixtureManifest Manifest)
    {
        public override string ToString() => $"deep fixture: {FixtureDir}\n  root: {Root}\n  folders below the root: {Folders}\n{Manifest}";
    }

    public static Result Create(string? parent = null)
    {
        parent ??= FixtureBuilder.DefaultParent;
        var dir = Path.Combine(parent, "deep-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        var m = new FixtureManifest { Root = dir };
        FixtureBuilder.WriteFile(m, Path.Combine(dir, FixtureBuilder.MarkerName), "FileScape deep fixture. Safe to delete.\n"u8.ToArray());
        var root = FixtureBuilder.MakeDir(m, dir, RootName);
        var outside = FixtureBuilder.MakeDir(m, dir, "Outer Reaches");
        FixtureBuilder.WriteFile(m, Path.Combine(outside, "beyond.txt"), new byte[64]);
        var folders = 0;
        var level = new List<string> { root };
        for (var l = 0; l < Levels; l++)
        {
            var next = new List<string>(level.Count * Fanout);
            foreach (var p in level)
            {
                foreach (var name in Names[l])
                {
                    var d = FixtureBuilder.MakeDir(m, p, name);
                    FixtureBuilder.WriteFile(m, Path.Combine(d, "notes.txt"), new byte[100 + l * 10]);
                    next.Add(d);
                    folders++;
                }
            }

            level = next;
        }

        if (OperatingSystem.IsWindows())
        {
            FixtureBuilder.CreateJunction(m, Path.Combine(root, "North", "back-to-atlas"), root);
            FixtureBuilder.CreateJunction(m, Path.Combine(root, "East", "to-outer-reaches"), outside);
            FixtureBuilder.CreateDenied(m, Path.Combine(root, "West", "Woods", "Ash", "Sealed"));
            var hidden = FixtureBuilder.MakeDir(m, Path.Combine(root, "West"), "Hidden Grove");
            File.SetAttributes(hidden, FileAttributes.Directory | FileAttributes.Hidden);
        }

        return new Result(dir, root, folders, m);
    }
}
