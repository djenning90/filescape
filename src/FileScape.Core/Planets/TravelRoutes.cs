namespace FileScape.Core.Planets;

/// <summary>
/// Every way FileScape starts travel or moves the navigator (PLANET-147..148, AD-92; the departure inventory of AD-84 as
/// data). Each route's name is the tag on the app line that starts it (<c>// travel: Name</c>), so a test can find every
/// navigator move and departure in the app's sources and check it is one of these (<c>TravelRoutesTests</c>).
/// </summary>
public enum TravelRoute
{
    // ---- departures from a launcher (DeparturePlanner)

    /// <summary>Space on a launcher pad.</summary>
    PadPress,

    /// <summary>G, the card's Go inside.</summary>
    GoInside,

    /// <summary>A world or pad double-clicked on the planet or in its sky.</summary>
    WorldDoubleClick,

    /// <summary>The planet's list (Destinations) Enter or double-click on a world or link here, and the bench's list.</summary>
    ListHere,

    /// <summary>Destinations, a folder deeper down (Enter): a multi-hop trip; each hop leaves from its own pad.</summary>
    DestinationsTrip,

    /// <summary>Destinations, Straight there (Shift+Enter), and a trip's target that is not below you.</summary>
    DestinationsStraightThere,

    /// <summary>Destinations, a link deeper down.</summary>
    DestinationsLink,

    /// <summary>Destinations, the way back row.</summary>
    DestinationsWayBack,

    /// <summary>Destinations, a favourite or a repository (by id, or by path when not yet listed).</summary>
    FavouriteOrRepository,

    /// <summary>U on a world (the navigator moves at the take-off).</summary>
    ParentKey,

    /// <summary>The place bar's ▲ Up button.</summary>
    UpButton,

    /// <summary>Backspace (history back).</summary>
    HistoryBack,

    /// <summary>A breadcrumb segment ("You are in …").</summary>
    Breadcrumb,

    /// <summary>Ctrl+Home and the ⌂ Root button: the folder you started in.</summary>
    RootPlace,

    /// <summary>A Locations row (a drive, a distribution, a favourite place).</summary>
    LocationsRow,

    /// <summary>Locations' This PC / the hub.</summary>
    ThisPc,

    /// <summary>Ctrl+L, a typed or pasted path.</summary>
    GoToPath,

    /// <summary>Ctrl+O / Open folder… choosing a new root in the live world.</summary>
    OpenFolderNewRoot,

    /// <summary>A link answering (resolved on demand): the navigator follows it.</summary>
    LinkAnswer,

    /// <summary>The walkthrough's start (to the Starter Sky's top world).</summary>
    WalkthroughStart,

    /// <summary>The walkthrough's "Back to the lesson".</summary>
    WalkthroughBack,

    /// <summary>Enter or G at the telescope, or a double-click on the world it frames (PLANET-188): that world.</summary>
    TelescopeEnter,

    // ---- from the sky (no launcher by design)

    /// <summary>The overview: Enter, G, a double-click, the list or a trip while it is open; its own dive or a descent out of the light.</summary>
    OverviewLanding,

    /// <summary>Any navigator change while the overview is open (Backspace, the breadcrumb, a path): a descent out of the light.</summary>
    OverviewNavigation,

    /// <summary>A fall off a Hazard World's edge: back up to the folder above, out of the light.</summary>
    HazardFall,

    // ---- not departures

    /// <summary>Esc before the take-off takes back a dialog's navigator move (<c>PlaceNavigator.Revert</c>).</summary>
    DepartureRevert,

    /// <summary>A new tree or the explorer's first answer: the world is built (an arrival, nothing departs).</summary>
    SessionArrival,

    /// <summary>The legacy Places aerial view's G / T over an island (its own flight, AERIAL-03).</summary>
    LegacyAerial,

    /// <summary>G on a folder in a legacy map: into Places there.</summary>
    LegacyMapEnter,
}

/// <summary>One route's rule: the departure entry it asks for (null when it is not a planets departure), whether it
/// leaves from the launcher that leads there, where in the app it starts, and why.</summary>
public sealed record TravelRouteRule(TravelRoute Route, DepartureEntry? Entry, bool FromLauncher, string Code, string Why);

/// <summary>
/// The inventory of travel routes (PLANET-147..148, AD-92). DJ (seventh pass): "Audit all routes that initiate travel.
/// Applicable departures should move through the intended launcher sequence, including dialog actions, direct paths,
/// favorites, repositories, parent navigation, and history." AD-84 made every departure ask
/// <see cref="DeparturePlanner.Plan"/>; this table names every route, its <see cref="DepartureEntry"/>, and whether it
/// leaves from a launcher, and <c>TravelRoutesTests</c> checks (a) every app line that moves the navigator or starts a
/// departure carries a <c>// travel:</c> tag naming a route here (or <c>relay</c>, for the plumbing that passes a route
/// on), (b) every route is tagged somewhere, (c) every launcher route's entry is used in the app and plans a launcher
/// when one leads there, (d) only the sky routes set the descent out of the light. A new route cannot be added without
/// an entry here, so it cannot quietly bypass the launcher.
/// Preserve: <see cref="TravelRouteRule.FromLauncher"/> is false only for the sky routes and the non-departures.
/// </summary>
public static class TravelRoutes
{
    /// <summary>The tag for a line that passes a route on (a session method, the release, the common departure).</summary>
    public const string RelayTag = "relay";

    public static readonly IReadOnlyList<TravelRouteRule> Rules =
    [
        new(TravelRoute.PadPress, DepartureEntry.PadPress, true, "PlanetWorld.Walk", "The pad is the button: it winds up on its pad at once."),
        new(TravelRoute.GoInside, DepartureEntry.GoInside, true, "Main.HandlePlanetCommand, Main.EnterSelection → PlanetWorld.LaunchToSelection", "A world here: its own pad; a selection elsewhere: toward or the way out."),
        new(TravelRoute.WorldDoubleClick, DepartureEntry.GoInside, true, "PlanetWorld.Actions.DoubleClickOn", "The same departure as G for the world clicked."),
        new(TravelRoute.ListHere, DepartureEntry.GoInside, true, "PlanetWorld.Interact.Activate, PlanetWorld.Destinations (depth 0)", "A world or link on this planet chosen in its list: as G."),
        new(TravelRoute.DestinationsTrip, DepartureEntry.TripHop, true, "PlanetWorld.Destinations → StartTrip → each hop BeginLaunch", "Each hop leaves from its own pad, one world at a time."),
        new(TravelRoute.DestinationsStraightThere, DepartureEntry.NavigatorChange, true, "PlanetWorld.Destinations, Trip (not below) → TravelInto → OnNavChanged", "The navigator moves at once; the departure runs toward the pad on the way (Esc reverts)."),
        new(TravelRoute.DestinationsLink, DepartureEntry.NavigatorChange, true, "PlanetWorld.Destinations → TravelInto (FollowLink) → OnNavChanged", "Toward the link's folder once the link answers."),
        new(TravelRoute.DestinationsWayBack, DepartureEntry.NavigatorChange, true, "PlanetWorld.Destinations → TravelUp → OnNavChanged", "The way back pad."),
        new(TravelRoute.FavouriteOrRepository, DepartureEntry.NavigatorChange, true, "PlanetWorld.Destinations → TravelInto / GoToPath → OnNavChanged", "Own, toward or the way out, from where the favourite is."),
        new(TravelRoute.ParentKey, DepartureEntry.Parent, true, "Main.HandlePlanetCommand → PlanetWorld.LaunchUp", "The way back pad; the navigator moves at the take-off, so Esc simply stops it."),
        new(TravelRoute.UpButton, DepartureEntry.NavigatorChange, true, "Hud ▲ Up → Main.TravelUp → OnNavChanged", "The way back pad, as U."),
        new(TravelRoute.HistoryBack, DepartureEntry.HistoryBack, true, "Main.HandlePlanetCommand → PlanetWorld.LaunchBack (shared Back in the legacy views)", "The pad toward where you came from."),
        new(TravelRoute.Breadcrumb, DepartureEntry.NavigatorChange, true, "Hud.BreadcrumbRequested → Main", "An ancestor: the way back pad (up and over)."),
        new(TravelRoute.RootPlace, DepartureEntry.NavigatorChange, true, "Main.TravelRoot (Ctrl+Home, ⌂ Root)", "Own, toward or the way out."),
        new(TravelRoute.LocationsRow, DepartureEntry.NavigatorChange, true, "Main.OnLocationChosen", "Own, toward or the way out."),
        new(TravelRoute.ThisPc, DepartureEntry.NavigatorChange, true, "Main.GoToHub", "The way out, to the top."),
        new(TravelRoute.GoToPath, DepartureEntry.NavigatorChange, true, "Main.World (a typed path answering)", "Own, toward or the way out."),
        new(TravelRoute.OpenFolderNewRoot, DepartureEntry.NavigatorChange, true, "Main.World (Reset, then a change raised for the planets)", "Travelled to like any change, not cut to (AD-84); not revertible."),
        new(TravelRoute.LinkAnswer, DepartureEntry.NavigatorChange, true, "Main.World (FollowLink when the link resolves)", "A departure already under way is retargeted; else toward the link."),
        new(TravelRoute.WalkthroughStart, DepartureEntry.NavigatorChange, true, "Main.Tutorial (start)", "A normal trip to the lessons' top world."),
        new(TravelRoute.WalkthroughBack, DepartureEntry.NavigatorChange, true, "Main.Tutorial.ReturnToLesson", "A normal trip back to the lesson's folder."),
        new(TravelRoute.TelescopeEnter, DepartureEntry.Telescope, true, "PlanetWorld.Telescope → DepartFor", "The world in the eyepiece (Enter, G or a double-click on it): from the launcher that leads there, as G (it used to be labelled GoInside)."),
        new(TravelRoute.OverviewLanding, DepartureEntry.Overview, false, "PlanetWorld.Universe (BeginUniverseLanding, LandFromUniverse, LandFromOverview)", "The overview's own dive: from the sky, by design (AD-74)."),
        new(TravelRoute.OverviewNavigation, DepartureEntry.Overview, false, "PlanetWorld.OnNavChanged with the overview open", "A descent out of the light: the overview is above every launcher."),
        new(TravelRoute.HazardFall, DepartureEntry.FallWayBack, false, "PlanetWorld.Hazard (the fall's TravelUp)", "You fell: the way back is out of the light, from below."),
        new(TravelRoute.DepartureRevert, null, false, "PlanetWorld.Departure (Esc before take-off)", "Takes a dialog's move back: nothing departs."),
        new(TravelRoute.SessionArrival, null, false, "Main (a new tree), Main.World (the explorer's first answer)", "The world is built where you arrive; nothing departs."),
        new(TravelRoute.LegacyAerial, null, false, "Main.Aerial", "The legacy Places world's own flight between islands."),
        new(TravelRoute.LegacyMapEnter, null, false, "Main.EnterSelection in a legacy map", "Into Places at that folder (no planets departure)."),
    ];

    /// <summary>The rule for <paramref name="route"/>.</summary>
    public static TravelRouteRule Rule(TravelRoute route) => Rules.First(r => r.Route == route);
}
