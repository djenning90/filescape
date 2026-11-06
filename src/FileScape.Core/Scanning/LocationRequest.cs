namespace FileScape.Core.Scanning;

/// <summary>What a location request names: the "This computer" hub, or an ordinary filesystem path.</summary>
public enum LocationTargetKind
{
    Hub,
    Path,
}

/// <summary>
/// LOC-06 (AD-30): an explicit navigation target. "This PC" is a Windows shell location, not a directory: it has no
/// filesystem path, and the native folder picker refuses it ("You can't open this location using this program"). Any
/// name the user may give for it — This PC, This computer, Computer, My Computer, its shell CLSID or shell: alias, or
/// <c>hub</c> on the command line — maps to FileScape's own hub, never to an invented path. Everything else stays a path.
/// </summary>
public sealed record LocationRequest(LocationTargetKind Kind, string? Path)
{
    /// <summary>The shell CLSID of This PC (Computer folder), as Explorer and shell pickers spell it.</summary>
    public const string ThisPcClsid = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";

    public static readonly LocationRequest Hub = new(LocationTargetKind.Hub, null);

    private static readonly HashSet<string> HubNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "hub", "this pc", "this computer", "computer", "my computer", "shell:mycomputerfolder", ThisPcClsid,
        "shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", WorldExplorer.HubName,
    };

    public bool IsHub => Kind == LocationTargetKind.Hub;

    /// <summary>Parses user or command-line input; null for blank input.</summary>
    public static LocationRequest? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var t = input.Trim().Trim('"').TrimEnd('\\', '/');
        if (HubNames.Contains(t) || t.Contains("20D04FE0-3AEA-1069-A2D8-08002B30309D", StringComparison.OrdinalIgnoreCase))
        {
            return Hub;
        }

        return new LocationRequest(LocationTargetKind.Path, input.Trim());
    }
}
