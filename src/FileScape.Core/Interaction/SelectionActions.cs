namespace FileScape.Core.Interaction;

/// <summary>Where the selection card is shown: its actions mean different things there (PLANET-87, AD-76).</summary>
public enum CardView
{
    /// <summary>A legacy map (Classic, District): Esc selects the parent, F frames the selection.</summary>
    Map,

    /// <summary>Places (walking, legacy): Esc clears the selection, F frames it in the place.</summary>
    Places,

    /// <summary>A planet's surface: Esc clears the selection, F turns the camera to face it.</summary>
    PlanetSurface,

    /// <summary>The planets' overview (T): Enter and G land, F zooms to the selected world, Esc closes the overview.</summary>
    PlanetOverview,
}

/// <summary>What kind of thing is selected, as far as the card's actions care.</summary>
public enum CardTarget
{
    File,
    Folder,
    Link,

    /// <summary>A drive, a WSL distribution or another location root: a real path with nothing above it but the hub.</summary>
    LocationRoot,

    /// <summary>FileScape's own "This computer": it has no filesystem path.</summary>
    Hub,
}

/// <summary>
/// The selected thing, described for <see cref="SelectionActions.For"/>: its kind and display name; whether the data is
/// live (real files: Show in Explorer needs a real location); whether it is the top of the tree (no parent);
/// whether you stand on it (planet surface); and whether it can be shown from here — on this world or in its sky on a
/// planet, drawn in the overview.
/// </summary>
public sealed record CardTargetInfo(CardTarget Kind, string Name, bool Live, bool IsTop, bool StandingOn, bool InView);

/// <summary>One action as the card shows it: enabled, its label (a shortcut in brackets only if that key does the same
/// thing here), and its tooltip (for a disabled action it starts with <see cref="SelectionActions.Unavailable"/> and
/// says why).</summary>
public sealed record CardAction(bool Enabled, string Label, string Tooltip);

/// <summary>The card's five actions, in its order.</summary>
public sealed record CardActions(CardAction Open, CardAction Reveal, CardAction Parent, CardAction Fly, CardAction Copy);

/// <summary>
/// The rules for the selection card's actions — Open, Show in Explorer, Parent, Fly to, Copy path — for every kind of
/// target in every view (PLANET-87, AD-76). Before 2026-11-19 the card had one fixed set of labels, written for the
/// legacy maps: on the planets "Parent (Esc)" showed a key that clears the selection, "Fly to (F)" called the maps'
/// camera and did nothing, "Open (Enter)" in the overview showed a key that lands, and the hub asked Windows to open
/// and reveal "This computer", which is not a path. Pure, so the table is tested in Core.
/// <para>
/// What a change must preserve: a label shows a shortcut only when the key does what the button does in that view; a
/// disabled action's tooltip starts with <see cref="Unavailable"/> and says why and what to do instead; nothing hands the
/// hub's name to Windows as a path (Open shows This PC through <see cref="ThisPcShellPath"/>).
/// </para>
/// </summary>
public static class SelectionActions
{
    /// <summary>How a disabled action's tooltip starts (the probes look for it).</summary>
    public const string Unavailable = "Unavailable: ";

    /// <summary>What Open hands to Windows for the hub: the shell's own name for This PC (File Explorer opens it).</summary>
    public const string ThisPcShellPath = "shell:MyComputerFolder";

    public static CardActions For(CardView view, CardTargetInfo t)
    {
        var hub = t.Kind == CardTarget.Hub;
        var overview = view == CardView.PlanetOverview;

        var open = new CardAction(true, overview ? "Open" : "Open (Enter)", t.Kind switch
        {
            CardTarget.Hub => "Open This PC in File Explorer.",
            CardTarget.File => "Open with the Windows-associated application. Asks first for programs and scripts.",
            CardTarget.Link => "Open the link's target with Windows (asks first: FileScape did not follow it).",
            _ => "Open this folder in File Explorer.",
        } + (overview ? " (Enter lands on the world; this opens it with Windows.)" : ""));

        var reveal = hub
            ? Off("Show in Explorer", "This computer is FileScape's hub, not a folder. Open shows This PC in File Explorer.")
            : !t.Live
                ? Off("Show in Explorer", "synthetic data has no real location.")
                : new CardAction(true, "Show in Explorer", "Open File Explorer with this item selected.");

        var parent = t.IsTop || hub
            ? Off(view == CardView.Map ? "Parent (Esc)" : "Parent", $"{t.Name} is the top: nothing contains it.")
            : view switch
            {
                CardView.Map => new CardAction(true, "Parent (Esc)", "Select the containing directory (Esc)."),
                CardView.PlanetOverview => new CardAction(true, "Parent", "Select the world that contains this one."),
                _ => new CardAction(true, "Parent", "Select the folder that contains it. (Esc clears the selection; U travels up a level.)"),
            };

        var fly = view switch
        {
            CardView.Map => new CardAction(true, "Fly to (F)", "Bring the selection into view."),
            CardView.Places => new CardAction(true, "Fly to (F)", "Frame the selection in this place."),
            CardView.PlanetOverview => t.InView
                ? new CardAction(true, "Zoom to (F)", "Zoom the overview to this world.")
                : Off("Zoom to (F)", $"{t.Name} is not drawn in the overview."),
            _ => t.StandingOn
                ? Off("Face it (F)", $"you are standing on {t.Name}.")
                : t.InView
                    ? new CardAction(true, "Face it (F)", "Turn to face it on this world or in its sky.")
                    : Off("Face it (F)", $"{t.Name} is not on this world or in its sky. Go inside (G) or the list (J) takes you there."),
        };

        var copy = hub
            ? Off("Copy path", "This computer has no path to copy.")
            : new CardAction(true, "Copy path", "Copy the full path to the clipboard.");
        return new CardActions(open, reveal, parent, fly, copy);
    }

    private static CardAction Off(string label, string why) => new(false, label, Unavailable + why);
}
