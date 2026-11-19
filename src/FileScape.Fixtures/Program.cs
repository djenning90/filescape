using FileScape.Fixtures;

// FileScape.Fixtures: the command-line tool that creates and deletes the owned, marked test trees (never private
// folders: see CLAUDE.md "Safety"). Each create prints the paths a test, probe or person needs next.
// Usage:
//   FileScape.Fixtures create [parentDir]   creates a fixture (default parent: %TEMP%\FileScape-fixtures) and prints its manifest
//   FileScape.Fixtures create-places [parent] creates the Places fixture (PLACE-*) and prints its roots
//   FileScape.Fixtures create-walk [parent]   creates the walking-world fixture (links, identities, visibility)
//   FileScape.Fixtures create-deep [parent]   creates "Deep Atlas" (7 levels × 3 folders, links, denied; PLANET-76)
//   FileScape.Fixtures create-sky [parent]    creates a marked sky-<stamp>-<hex> holding the "Starter Sky" sample; prints its path
//   FileScape.Fixtures delete <fixtureRoot>  deletes a fixture; refuses directories without the marker
//   FileScape.Fixtures delete-all            deletes every marked fixture under the default parent
switch (args.FirstOrDefault())
{
    case "create":
        var manifest = FixtureBuilder.Create(args.Length > 1 ? args[1] : null, includeVanishing: false);
        Console.WriteLine(manifest);
        return 0;
    case "create-walk":
        Console.WriteLine(WalkFixture.Create(args.Length > 1 ? args[1] : null));
        return 0;
    case "create-deep":
        Console.WriteLine(DeepFixture.Create(args.Length > 1 ? args[1] : null));
        return 0;
    case "create-sky":
        Console.WriteLine(SkyFixture.Create(args.Length > 1 ? args[1] : null));
        return 0;
    case "create-places":
        var places = PlacesFixture.Create(args.Length > 1 ? args[1] : null);
        Console.WriteLine(places);
        return 0;
    case "delete" when args.Length > 1:
        FixtureBuilder.Delete(args[1]);
        Console.WriteLine($"Deleted {args[1]}");
        return 0;
    case "delete-all":
        Console.WriteLine($"Deleted {FixtureBuilder.DeleteAllUnderDefaultParent()} fixture(s) under {FixtureBuilder.DefaultParent}");
        return 0;
    default:
        Console.Error.WriteLine("usage: FileScape.Fixtures create [parentDir] | create-walk [parentDir] | create-deep [parentDir] | create-places [parentDir] | create-sky [parentDir] | delete <fixtureRoot> | delete-all");
        return 2;
}

/// <summary>
/// The Starter Sky fixture: an owned, marked directory (sky-&lt;stamp&gt;-&lt;hex&gt;) holding the curated sample written by
/// <see cref="FileScape.Core.Samples.SampleSky"/>, so the planetary world can be tried without touching anyone's own folders.
/// </summary>
internal static class SkyFixture
{
    /// <summary>Creates the fixture under <paramref name="parent"/> (default: the fixtures parent) and returns the Starter Sky path.</summary>
    public static string Create(string? parent)
    {
        parent ??= FixtureBuilder.DefaultParent;
        var dir = Path.Combine(parent, "sky-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, FixtureBuilder.MarkerName), "FileScape Starter Sky fixture. Safe to delete.\n");
        return FileScape.Core.Samples.SampleSky.Create(dir);
    }
}
