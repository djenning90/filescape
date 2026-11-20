using FileScape.Core.Model;

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
/// live (real files: Show in folder needs a real location); whether it is the top of the tree (no parent);
/// whether you stand on it (planet surface); and whether it can be shown from here — on this world or in its sky on a
/// planet, drawn in the overview.
/// </summary>
public sealed record CardTargetInfo(CardTarget Kind, string Name, bool Live, bool IsTop, bool StandingOn, bool InView)
{
    /// <summary>For a location root: a drive, a WSL distribution, a share or a volume (where Show in folder can show it).</summary>
    public LocationKind Location { get; init; }

    /// <summary>Whether Windows can reach it, as far as FileScape's listing knows (PLANET-139).</summary>
    public CardAvailability Availability { get; init; }

    /// <summary>For a link (reparse point): its kind and status; Open follows it, Show in folder selects the link itself.</summary>
    public LinkInfo? Link { get; init; }

    /// <summary>A Windows shortcut (.lnk, .url): a file whose Open starts what it points to.</summary>
    public bool Shortcut { get; init; }

    /// <summary>An online-only cloud placeholder (opening it downloads it; a folder may download what is opened inside).</summary>
    public bool CloudPlaceholder { get; init; }

    /// <summary>A Linux pipe, socket or device seen through WSL: never opened from Windows.</summary>
    public bool LinuxSpecial { get; init; }
}

/// <summary>Whether the selected thing can be reached, from the listing's flags (PLANET-139).</summary>
public enum CardAvailability
{
    Available,

    /// <summary>It disappeared since it was listed (<see cref="NodeFlags.Vanished"/>).</summary>
    Missing,

    /// <summary>Windows refused to list it (<see cref="NodeFlags.AccessDenied"/>).</summary>
    Denied,

    /// <summary>Its drive, share or distribution could not be reached (<see cref="NodeFlags.Unavailable"/>).</summary>
    Unreachable,
}

/// <summary>One action as the card shows it: enabled, its label (a shortcut in brackets only if that key does the same
/// thing here), and its tooltip (for a disabled action it starts with <see cref="SelectionActions.Unavailable"/> and
/// says why).</summary>
public sealed record CardAction(bool Enabled, string Label, string Tooltip);

/// <summary>The card's five actions, in its order.</summary>
public sealed record CardActions(CardAction Open, CardAction Reveal, CardAction Parent, CardAction Fly, CardAction Copy);

/// <summary>
/// The rules for the selection card's actions — Open, Show in folder, Parent, Fly to, Copy path — for every kind of
/// target in every view (PLANET-87, AD-76). Before 2026-11-19 the card had one fixed set of labels, written for the
/// legacy maps: on the planets "Parent (Esc)" showed a key that clears the selection, "Fly to (F)" called the maps'
/// camera and did nothing, "Open (Enter)" in the overview showed a key that lands, and the hub asked Windows to open
/// and reveal "This computer", which is not a path. Pure, so the table is tested in Core.
/// <para>
/// 2026-11-20 (PLANET-137..140, AD-87; DJ: "Open on a folder and Show in Explorer appeared to do the same thing"): Open
/// opens the thing itself (a folder shows what is inside it) and is labelled with what opens; Show in folder (before:
/// "Show in Explorer") opens the containing folder with the item selected, and the shell does that
/// (<c>WindowsShellActions.RevealInExplorer</c>). Links, shortcuts, roots and items Windows cannot reach each have their
/// own rule; <see cref="Describe"/> reads them from the app's tree.
/// </para>
/// <para>
/// What a change must preserve: a label shows a shortcut only when the key does what the button does in that view; a
/// disabled action's tooltip starts with <see cref="Unavailable"/> and says why and what to do instead; nothing hands the
/// hub's name to Windows as a path (Open shows This PC through <see cref="ThisPcShellPath"/>); Open and Show in folder
/// never share a label; nothing FileScape enables leads Windows to offer a permission change (an access-denied folder's
/// Open is off).
/// </para>
/// </summary>
public static class SelectionActions
{
    /// <summary>How a disabled action's tooltip starts (the probes look for it).</summary>
    public const string Unavailable = "Unavailable: ";

    /// <summary>What Open hands to Windows for the hub: the shell's own name for This PC (File Explorer opens it).</summary>
    public const string ThisPcShellPath = "shell:MyComputerFolder";

    /// <summary>
    /// Describes a node of the app's tree for <see cref="For"/>: what kind it is (a directory whose parent is the hub, or
    /// with its own location path, is a location root), whether it can be reached, and what Open and Show in folder need
    /// to know about it. <paramref name="standingOn"/> and <paramref name="inView"/> come from the view (the planet you
    /// stand on; what can be faced or is drawn).
    /// </summary>
    public static CardTargetInfo Describe(FsTree tree, int id, bool standingOn = false, bool inView = true)
    {
        var node = tree[id];
        var kind = node.IsHub ? CardTarget.Hub
            : node.Kind == NodeKind.Link ? CardTarget.Link
            : node.IsDirectory ? (node.LocationPath != null || (node.ParentId >= 0 && tree[node.ParentId].IsHub) ? CardTarget.LocationRoot : CardTarget.Folder)
            : CardTarget.File;
        var f = node.Flags;
        var availability = (f & NodeFlags.Vanished) != 0 ? CardAvailability.Missing
            : (f & NodeFlags.Unavailable) != 0 ? CardAvailability.Unreachable
            : (f & NodeFlags.AccessDenied) != 0 ? CardAvailability.Denied
            : CardAvailability.Available;
        var ext = Path.GetExtension(node.Name);
        return new CardTargetInfo(kind, tree.DisplayName(id), tree.Source.IsLive, node.IsRoot, standingOn, inView)
        {
            Location = node.Location,
            Availability = availability,
            Link = kind == CardTarget.Link ? node.Link : null,
            Shortcut = kind == CardTarget.File && (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals(".url", StringComparison.OrdinalIgnoreCase)),
            CloudPlaceholder = (f & NodeFlags.CloudPlaceholder) != 0,
            LinuxSpecial = (f & NodeFlags.LinuxSpecial) != 0,
        };
    }

    public static CardActions For(CardView view, CardTargetInfo t)
    {
        var hub = t.Kind == CardTarget.Hub;
        var overview = view == CardView.PlanetOverview;

        var open = OpenAction(t, overview);
        var reveal = RevealAction(t);

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
                    ? new CardAction(true, "Face it (F)", "Turn to face it on this world or in its sky; F again turns back.")
                    : Off("Face it (F)", $"{t.Name} is not on this world or in its sky. Go inside (G) or the list (J) takes you there."),
        };

        var copy = hub
            ? Off("Copy path", "This computer has no path to copy.")
            : new CardAction(true, "Copy path", "Copy the full path to the clipboard.");
        return new CardActions(open, reveal, parent, fly, copy);
    }

    private static CardAction Off(string label, string why) => new(false, label, Unavailable + why);

    /// <summary>Why Windows cannot reach it, for Open and Show in folder alike; null when it can (as far as the listing knows).</summary>
    private static string? Unreachable(CardTargetInfo t) => t.Availability switch
    {
        CardAvailability.Missing => $"{t.Name} is no longer there (moved, renamed or deleted since FileScape listed it). Parent selects its folder.",
        CardAvailability.Unreachable => $"the drive, share or distribution holding {t.Name} could not be reached. Reconnect it, then list it again.",
        _ => null,
    };

    /// <summary>
    /// Open (PLANET-137): a file with its associated app, a folder or a root in File Explorer showing what is inside it,
    /// a link's target, a shortcut's target, This PC for the hub. The label names what opens ("Open folder", "Open
    /// drive", "Open target", "Open This PC"), so it never reads like Show in folder. Off where Windows cannot do it, or
    /// would ask the person to do something FileScape must not start: an access-denied folder (File Explorer offers to
    /// change its permissions), a Linux pipe or device (opening blocks), a link whose target is not there.
    /// </summary>
    private static CardAction OpenAction(CardTargetInfo t, bool overview)
    {
        var name = t.Kind switch
        {
            CardTarget.Hub => "Open This PC",
            CardTarget.Link => "Open target",
            CardTarget.LocationRoot when t.Location == LocationKind.Drive => "Open drive",
            CardTarget.LocationRoot or CardTarget.Folder => "Open folder",
            _ => "Open",
        };
        var label = overview ? name : name + " (Enter)";
        if (t.Kind == CardTarget.Hub) return new CardAction(true, label, "Open This PC in File Explorer." + OverviewNote(overview));
        if (Unreachable(t) is { } gone) return Off(label, gone);

        string tip;
        if (t.Kind == CardTarget.Link)
        {
            var link = t.Link;
            var kind = link?.KindName ?? "link";
            var target = link?.TargetPath ?? link?.TargetText;
            var where = target != null ? $" ({target})" : "";
            switch (link?.Status)
            {
                case LinkStatus.Broken: return Off(label, $"the {kind}'s target{where} does not exist. Show in folder selects the link itself.");
                case LinkStatus.Denied: return Off(label, $"the {kind} or its target could not be read (access denied). Show in folder selects the link itself.");
                case LinkStatus.Unavailable: return Off(label, $"the {kind}'s target{where} is on a drive, share or distribution that could not be reached. Show in folder selects the link itself.");
                case LinkStatus.Unsupported:
                case not null when link.Kind is LinkKind.LinuxSymlink or LinkKind.Other:
                    return Off(label, $"Windows cannot follow this {kind}. Show in folder selects the link itself.");
            }

            tip = $"Open the {kind}'s target{where} with Windows. Asks first: FileScape did not follow it.";
        }
        else if (t.Kind == CardTarget.File)
        {
            if (t.LinuxSpecial) return Off(label, $"{t.Name} is a Linux pipe, socket or device: opening it from Windows could hang. Show in folder selects it.");
            var ext = Path.GetExtension(t.Name).ToLowerInvariant();
            tip = t.Shortcut ? $"Open the shortcut: Windows starts what {t.Name} points to. Asks first."
                : ext.Length > 0 ? $"Open {t.Name} with the app Windows uses for {ext} files. Asks first for programs and scripts."
                : $"Open {t.Name} with the app Windows uses for it. Asks first for programs and scripts.";
            if (t.CloudPlaceholder) tip += " It is online-only: opening it downloads it from the cloud provider (asks first).";
        }
        else
        {
            if (t.Availability == CardAvailability.Denied)
                return Off(label, $"Windows denied access to {t.Name}; File Explorer would offer to change its permissions, which FileScape leaves to you. Show in folder selects it in its folder.");
            tip = $"Open {t.Name} in File Explorer, showing what is inside it.";
            if (t.CloudPlaceholder) tip += " It is online-only: File Explorer may download what you open inside it.";
        }

        return new CardAction(true, label, tip + OverviewNote(overview));
    }

    private static string OverviewNote(bool overview) => overview ? " (Enter lands on the world; this opens it with Windows.)" : "";

    /// <summary>
    /// Show in folder (PLANET-138): File Explorer opens the folder that contains the item, with the item selected — for
    /// a folder that is its parent, not the folder itself (Open does that); for a link or a shortcut, the link itself,
    /// not its target. A drive is shown in This PC, a share on its server; a WSL distribution (File Explorer's Linux
    /// selects nothing) and a volume without a drive letter are off with the reason. Never for the hub or synthetic data.
    /// </summary>
    private static CardAction RevealAction(CardTargetInfo t)
    {
        const string InFolder = "Show in folder";
        if (t.Kind == CardTarget.Hub) return Off(InFolder, "This computer is FileScape's hub, not a folder. Open This PC shows it in File Explorer.");
        if (!t.Live) return Off(InFolder, "synthetic data has no real location.");
        if (t.Kind == CardTarget.LocationRoot)
        {
            var (label, container) = t.Location switch
            {
                LocationKind.Drive => ("Show in This PC", "This PC"),
                // Measured 2026-11-20 (tools/os-file-actions-probe.ps1, both shell routes): File Explorer opens Linux
                // (\\wsl.localhost) after ≈ 18 s and selects nothing there, so this is off with the reason.
                LocationKind.Wsl => ("Show in folder", null),
                LocationKind.Network => ("Show on server", "its server"),
                LocationKind.Volume => ("Show in folder", null),
                _ => (InFolder, "the folder that contains it"),
            };
            if (container == null && t.Location == LocationKind.Wsl) return Off(label, $"File Explorer lists WSL distributions under Linux but does not select one there. Open folder shows what is inside {t.Name}.");
            if (container == null) return Off(label, $"{t.Name} is a volume with no drive letter, so File Explorer has no folder that lists it. Open shows what is inside it.");
            if (Unreachable(t) is { } off) return Off(label, off);
            return new CardAction(true, label, $"Open {container} in File Explorer, with {t.Name} selected.");
        }

        if (Unreachable(t) is { } why) return Off(InFolder, why);
        var itself = t.Kind == CardTarget.Link ? $"the {t.Link?.KindName ?? "link"} itself selected (not its target)"
            : t.Shortcut ? "the shortcut itself selected (not its target)"
            : $"{t.Name} selected";
        return new CardAction(true, InFolder, $"Open the folder that contains {t.Name} in File Explorer, with {itself}.");
    }
}
