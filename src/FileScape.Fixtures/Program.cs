using FileScape.Fixtures;

// Usage:
//   FileScape.Fixtures create [parentDir]   creates a fixture (default parent: %TEMP%\FileScape-fixtures) and prints its manifest
//   FileScape.Fixtures create-places [parent] creates the Places fixture (PLACE-*) and prints its roots
//   FileScape.Fixtures create-walk [parent]   creates the walking-world fixture (links, identities, visibility)
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
        Console.Error.WriteLine("usage: FileScape.Fixtures create [parentDir] | delete <fixtureRoot>");
        return 2;
}
