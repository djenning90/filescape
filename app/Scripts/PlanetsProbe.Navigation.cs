using FileScape.App.Planets;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-navigation --root=sample --test-list-delay-ms=12000 --test-list-delay-match="Pressed Flowers"</c>
/// (PLANET-39..41, AD-56): direct navigation on the Starter Sky with injected input — the denser list; a branch opened
/// by clicking ▸ and one opened with the keyboard, several levels down; an empty folder and one still listing (the delay
/// instrument holds "Pressed Flowers" in its listing); a four-hop trip watched to the end; a trip stopped in the air and
/// one stopped in a wind-up, each leaving you on the last world reached; a hop made unavailable ([direct] instrument);
/// Backspace and U afterwards; Locations marking where you are; Open Location and the list both open when a destination
/// is committed; the favourites and repositories route (a pin and the sample's repository).
/// <para>
/// <c>--automate=planets-navigation-denied --root=&lt;Atlas Studio fixture&gt;</c>: the inaccessible folder (a deny ACE
/// made by FileScape.Fixtures) and the empty room, as the list shows them.
/// </para>
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>The previous list's row pitch, pixels at UI scale 1 (measured on its screenshot: rows at 245, 272, 299).</summary>
    private const float PreviousRowPitchPixels = 27f;

    private async Task NavigationRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.5);
        var root = App.Navigator.Current;
        var lwd = Child(root, "Long Way Down");
        var meadow = Child(lwd, "Meadow Gate");
        var fern = Child(meadow, "Fern Trail");
        var bridge = Child(fern, "Old Bridge");
        var woods = Child(bridge, "Deep Woods");
        var oak = Child(woods, "Hollow Oak");
        Check(oak >= 0, "the Long Way Down is in the sample, six levels deep");

        await DenseList(root);
        await EmptyAndListing(root);
        await KeyboardBranch(lwd, meadow, fern, bridge);
        await WatchedTrip(root, lwd, fern, bridge);
        await StoppedTrips(root, lwd, fern, oak);
        await UnavailableHop(root, fern, bridge);
        await LocationsHere(root);
        await OverlappingDialogs(root);
        await Favourites(root);
        Check(P.MaxDestinationBuildMs < 16, F($"the list stays responsive: {P.DestinationBuilds} rebuilds, the slowest {P.MaxDestinationBuildMs:0.0} ms after the first ({P.FirstDestinationBuildMs:0.0} ms, with JIT); one frame is 16.7 ms"));
        var asked = P.ExpandRequests.Select(id => Tree[id].Name).ToList();
        Log($"folders listed because the list opened them: {asked.Count} ({string.Join(", ", asked)})");
    }

    private string Key0(int id) => $"f{id}";

    private DestinationRow? RowFor(int id) => P.DestinationRows.FirstOrDefault(r => r.Key == Key0(id));

    private async Task OpenList()
    {
        if (!P.ListOpen) await Key(Godot.Key.J);
        await Frames(4);
    }

    private async Task CloseList()
    {
        if (P.ListOpen) await Key(Godot.Key.Escape);
        await Frames(3);
    }

    // ---- the list itself

    private async Task DenseList(int root)
    {
        await OpenList();
        Check(P.ListOpen, "J opens the Destinations list");
        var pitch = P.DestinationRowHeight;
        Check(pitch is > 0 and <= 22, F($"denser rows: {pitch:0} px a row (the previous list: {PreviousRowPitchPixels:0} px); {P.DestinationRowsThatFit} rows fit at 1600×900"));
        var before = (int)(P.DestinationRowsThatFit * pitch / PreviousRowPitchPixels);
        Check(P.DestinationRowsThatFit >= before * 1.3f, F($"more destinations at once: {P.DestinationRowsThatFit} rows fit where the previous pitch fitted {before}"));
        var here = Tree[root].Children.Count(c => App.IsVisibleInPlaceForProbe(Tree[c]));
        Check(P.ListCount == here, $"every entry here is reachable from the list ({P.ListCount} of {here})");
        await Shot("list-dense");
    }

    private async Task EmptyAndListing(int root)
    {
        var moon = Child(root, "Quiet Moon");
        var garden = Child(root, "Garden Photos");
        var pressed = Child(garden, "Pressed Flowers");
        var moonRow = RowFor(moon);
        Check(moonRow is { Detail: "empty", CanExpand: false }, $"an empty folder says so before it is opened: Quiet Moon “{moonRow?.Detail}”, no ▸");

        // Garden Photos opened by clicking its ▸: Pressed Flowers is still listing (the delay instrument holds it).
        await Click(P.DestinationArrowPoint(Key0(garden)));
        await Until(() => RowFor(garden)?.Expanded == true, 2);
        Check(RowFor(garden)?.Expanded == true, "clicking ▸ opens Garden Photos in place");
        var listing = Listing.Of(Tree[pressed]);
        var pr = RowFor(pressed);
        Log($"Pressed Flowers: {listing}, row “{pr?.Detail}”");
        Check(listing is ListingState.Listing or ListingState.Pending && pr?.Detail == "listing…", $"a folder still loading says so: Pressed Flowers “{pr?.Detail}” ({listing}; if Complete the instrument missed its window)");
        await Click(P.DestinationArrowPoint(Key0(pressed)));
        await Until(() => RowFor(pressed)?.Expanded == true, 2);
        var note = P.DestinationRows.SkipWhile(r => r.Key != Key0(pressed)).Skip(1).FirstOrDefault();
        Check(note is { Kind: DestinationRowKind.Note, Name: "listing…" }, $"opened while listing, its branch says “{note?.Name}”");
        await Shot("list-still-listing");
        await Until(() => Tree[pressed].ScanState == DirScanState.Complete, 15);
        await Seconds(0.5);
        pr = RowFor(pressed);
        Check(pr is { CanExpand: false } && pr.Detail.EndsWith("files", StringComparison.Ordinal), $"when the listing arrives the row updates in place: “{pr?.Detail}” (files only: nothing further down)");
        await Shot("list-listed");
        // Close Garden Photos again by its ▸.
        await Click(P.DestinationArrowPoint(Key0(garden)));
        await Until(() => RowFor(garden)?.Expanded == false, 2);
        Check(RowFor(garden)?.Expanded == false, "clicking ▸ again closes it");
    }

    /// <summary>Type to find the Long Way Down, then → opens and steps down, three levels, ← closes one.</summary>
    private async Task KeyboardBranch(int lwd, int meadow, int fern, int bridge)
    {
        await TypeText("long way");
        await Frames(4);
        await Key(Godot.Key.Down);
        Check(P.SelectedDestinationKey == Key0(lwd) && P.DestinationTreeFocused, $"↓ from the filter lands on the match ({P.SelectedDestinationKey})");
        foreach (var (open, child, name) in new[] { (lwd, meadow, "Meadow Gate"), (meadow, fern, "Fern Trail"), (fern, bridge, "Old Bridge") })
        {
            await Key(Godot.Key.Right);
            await Until(() => RowFor(open)?.Expanded == true, 2);
            await Frames(2);
            await Key(Godot.Key.Right);
            await Frames(2);
            Check(P.SelectedDestinationKey == Key0(child), $"→ opens {Tree[open].Name}, → again steps into it: {name} ({RowFor(child)?.Depth} levels in)");
        }

        await Shot("list-keyboard-deep");
        await Key(Godot.Key.Left);
        await Frames(2);
        Check(P.SelectedDestinationKey == Key0(fern), "← on a closed row steps up to its folder");
        await Key(Godot.Key.Left);
        await Until(() => RowFor(fern)?.Expanded == false, 2);
        Check(RowFor(fern)?.Expanded == false, "← on an open row closes it");
        await Key(Godot.Key.Right);
        await Until(() => RowFor(fern)?.Expanded == true, 2);
        await Key(Godot.Key.Right);
        await Frames(2);
    }

    // ---- trips

    private int Landings => P.LaunchLog.Count(l => l.Phase == "land");

    /// <summary>Old Bridge is selected, four levels down: Enter flies there hop by hop; then U and Backspace.</summary>
    private async Task WatchedTrip(int root, int lwd, int fern, int bridge)
    {
        var landed = Landings;
        Check(P.SelectedDestinationKey == Key0(bridge), "Old Bridge chosen in the list");
        await Key(Godot.Key.Enter);
        await Until(() => P.TripActive, 2);
        Check(P.TripActive && P.TripHops == 4 && !P.ListOpen, $"Enter starts a trip of {P.TripHops} hops and the list steps aside");
        var shots = 0;
        var panelSeen = "";
        while (P.TripActive && shots < 4)
        {
            var reached = P.TripReached;
            await Until(() => !P.TripActive || (P.TripReached == reached && P.PhaseName == "Flying" && P.FlightProgress > 0.5f), 10);
            if (!P.TripActive) break;
            panelSeen = P.TripPanelText;
            await Shot($"trip-hop-{reached + 1}");
            shots++;
            await Until(() => !P.TripActive || P.TripReached > reached, 10);
        }

        Check(panelSeen.Contains("hop 4 of 4") && panelSeen.Contains("Old Bridge"), $"the trip panel guides it: “{panelSeen.Replace('\n', ' ')}”");
        await Until(() => !P.TripActive && !P.Busy, 20);
        await Frames(3);
        Check(App.Navigator.Current == bridge && P.Composition?.FolderId == bridge && P.LastTripOutcome == "arrived", $"arrived at Old Bridge ({Tree[App.Navigator.Current].Name}, {P.LastTripOutcome})");
        Check(Landings - landed == 4, $"four launches and landings, one per level ({Landings - landed})");
        Log($"trip phases: {string.Join(" ", P.LaunchLog.TakeLast(20).Select(l => l.Phase))}");
        await Shot("trip-arrived");

        await Key(Godot.Key.U);
        await Until(() => App.Navigator.Current == fern && !P.Busy, 12);
        Check(App.Navigator.Current == fern, $"U after the trip: the parent of where you are ({Tree[App.Navigator.Current].Name})");
        await Key(Godot.Key.Backspace);
        await Until(() => App.Navigator.Current == bridge && !P.Busy, 12);
        Check(App.Navigator.Current == bridge, $"Backspace undoes the U ({Tree[App.Navigator.Current].Name})");
        await Key(Godot.Key.Backspace);
        await Until(() => App.Navigator.Current == root && !P.Busy, 12);
        Check(App.Navigator.Current == root, $"Backspace again: the whole trip was one step, back to where it began ({Tree[App.Navigator.Current].Name})");
        _ = lwd;
    }

    /// <summary>Two trips to Hollow Oak (six hops): Stop in the air (that hop lands, then it ends); Esc in a wind-up (cancelled at once).</summary>
    private async Task StoppedTrips(int root, int lwd, int fern, int oak)
    {
        await OpenList();
        await TypeText("hollow");
        await Frames(4);
        await Key(Godot.Key.Down);
        Check(P.SelectedDestinationKey == Key0(oak), $"typing “hollow” and ↓ reaches Hollow Oak, six levels down ({P.SelectedDestinationKey})");
        await Shot("list-filter-deep");
        await Click(P.DestinationGoRect.GetCenter());
        await Until(() => P.TripActive, 2);
        Check(P.TripHops == 6, $"Go: a trip of {P.TripHops} hops");
        await Until(() => P.TripReached == 2 && P.PhaseName == "Flying", 20);
        await Click(P.TripStopRect.GetCenter());
        await Frames(2);
        Check(P.TripPanelText.Contains("stopping"), $"Stop in the air: “{P.TripPanelText.Replace('\n', ' ')}”");
        await Shot("trip-stopping");
        await Until(() => !P.TripActive && !P.Busy, 12);
        await Frames(3);
        Check(App.Navigator.Current == fern && P.Composition?.FolderId == fern && P.LastTripOutcome == "stopped", $"the hop in the air lands and the trip ends there: {Tree[App.Navigator.Current].Name} ({P.LastTripOutcome})");
        await Shot("trip-stopped");
        await Key(Godot.Key.Backspace);
        await Until(() => App.Navigator.Current == root && !P.Busy, 12);
        Check(App.Navigator.Current == root, "Backspace after a stopped trip: back to where it began");

        await OpenList();
        await TypeText("hollow");
        await Frames(4);
        await Key(Godot.Key.Down);
        await Key(Godot.Key.Enter);
        await Until(() => P.TripReached == 1 && P.PhaseName == "WindUp", 15);
        await Key(Godot.Key.Escape);
        await Until(() => !P.TripActive && !P.Busy, 6);
        await Frames(3);
        Check(App.Navigator.Current == lwd && P.Composition?.FolderId == lwd && P.LastTripOutcome == "stopped", $"Esc in a wind-up cancels that launch: you stay on {Tree[App.Navigator.Current].Name}");
        await Key(Godot.Key.Backspace);
        await Until(() => App.Navigator.Current == root && !P.Busy, 12);
        Check(App.Navigator.Current == root, "and Backspace returns to the start");
    }

    /// <summary>[direct] instrument: the probe says Old Bridge "went away"; the trip stops on Fern Trail and says why.</summary>
    private async Task UnavailableHop(int root, int fern, int bridge)
    {
        P.TripTestUnavailable = bridge; // [direct]: the next hop's check treats it as gone (a real folder is never touched)
        await OpenList();
        await TypeText("hollow");
        await Frames(4);
        await Key(Godot.Key.Down);
        await Key(Godot.Key.Enter);
        await Until(() => !P.TripActive && !P.Busy && P.LastTripOutcome != "", 30);
        await Frames(3);
        var text = P.TripPanelText;
        Check(App.Navigator.Current == fern && P.LastTripOutcome == "unavailable" && text.Contains("no longer there"), $"a hop that has become unavailable: the trip stops on {Tree[App.Navigator.Current].Name} and says “{text}”");
        await Shot("trip-unavailable");
        P.TripTestUnavailable = -1;
        await Key(Godot.Key.Backspace);
        await Until(() => App.Navigator.Current == root && !P.Busy, 12);
    }

    // ---- dialogs

    private async Task LocationsHere(int root)
    {
        await Seconds(1);
        await Key(Godot.Key.L);
        await Frames(4);
        var i = App.Hud.LocationRowIndex(root);
        Check(i >= 0 && App.Hud.LocationRowDisabled(i) && App.Hud.LocationRowText(i).Contains("you are here"), $"Locations marks where you are and disables it: “{(i >= 0 ? App.Hud.LocationRowText(i) : "-")}”");
        await Shot("locations-here");
        var transfers = P.Transfers;
        await Click(App.Hud.LocationRowRect(i).GetCenter());
        await Frames(10);
        Check(App.Navigator.Current == root && P.Transfers == transfers && !P.Busy, "choosing it does nothing (no trip to where you are)");
        await Key(Godot.Key.Escape);
        await Frames(3);
        Check(!App.Hud.LocationsVisible, "Esc closes Locations");
    }

    /// <summary>
    /// Open Location (L) and the list (J; then the ☰ button) both open, then a destination committed from the list:
    /// every navigation dialog that would cover the flight is gone by the time it starts.
    /// </summary>
    private async Task OverlappingDialogs(int root)
    {
        await Key(Godot.Key.L);
        await Frames(4);
        await Key(Godot.Key.J);
        await Frames(4);
        Log($"before commit: locations={App.Hud.LocationsVisible} list={P.ListOpen} drawer={App.Hud.DestinationsOpen}");
        await Shot("dialogs-both-open");
        await TypeText("music");
        await Frames(3);
        await Key(Godot.Key.Down);
        await Frames(2);
        await Key(Godot.Key.Enter);
        await Until(() => P.PhaseName == "Flying", 4);
        await Seconds(0.5);
        Log($"in flight: locations={App.Hud.LocationsVisible} list={P.ListOpen} drawer={App.Hud.DestinationsOpen} phase={P.PhaseName}");
        await Shot("dialogs-during-flight");
        Check(!App.Hud.LocationsVisible && !P.ListOpen && !App.Hud.DestinationsOpen, "committing a destination dismisses Open Location and the list before the flight");
        await Until(() => !P.Busy, 8);
        await Frames(3);
        Check(Tree[App.Navigator.Current].Name == "Music Box", $"the commit went to Music Box ({Tree[App.Navigator.Current].Name})");

        // The same with the ☰ Destinations button (the place bar's) instead of J: it opens the same list in Planets.
        await Key(Godot.Key.L);
        await Frames(4);
        await Click(App.Hud.DestinationsButtonRect.GetCenter());
        await Frames(4);
        Check(P.ListOpen && App.Hud.LocationsVisible, "☰ Destinations opens the planet's list (Locations still open)");
        await Click(P.DestinationRowRect("up").GetCenter());
        await Click(P.DestinationGoRect.GetCenter());
        await Until(() => P.PhaseName is "Flying" or "Warping", 4);
        await Frames(5);
        Check(!App.Hud.LocationsVisible && !P.ListOpen, "the way back chosen with the mouse: both dialogs gone for the flight");
        await Until(() => App.Navigator.Current == root && !P.Busy, 10);
        Check(App.Navigator.Current == root, "and it went up to the Starter Sky");
    }

    // ---- places I care about

    /// <summary>Pin Music Box (P on its card); the list's ★ section shows the pin and the sample's repository; each is one warp away.</summary>
    private async Task Favourites(int root)
    {
        var music = Child(root, "Music Box");
        var workshop = Child(root, "Lantern Workshop");
        App.Selection.Select(music); // [direct] as a click on its world: P pins the selected folder
        await Frames(2);
        await Key(Godot.Key.P);
        await Frames(3);
        // Fly somewhere deeper first, so the favourites are not just next door.
        var lwd = Child(root, "Long Way Down");
        var fern = Child(Child(lwd, "Meadow Gate"), "Fern Trail");
        await OpenList();
        await TypeText("fern trail");
        await Frames(4);
        await Key(Godot.Key.Down);
        await Key(Godot.Key.Enter);
        await Until(() => App.Navigator.Current == fern && !P.Busy, 25);
        Check(App.Navigator.Current == fern, $"a trip three levels down to Fern Trail first ({Tree[App.Navigator.Current].Name})");
        await OpenList();
        await Frames(6);
        var favs = P.DestinationRows.Where(r => r.Kind == DestinationRowKind.Favourite).ToList();
        Log($"favourites: {string.Join(", ", favs.Select(f => $"{f.Name} ({f.Detail})"))}");
        Check(favs.Any(f => f.Id == music && f.Detail.StartsWith("pinned", StringComparison.Ordinal)) && favs.Any(f => f.Id == workshop && f.IsRepository),
            "★ Favourites & repositories lists the pin (Music Box) and the repository FileScape found (Lantern Workshop)");
        await Shot("favourites");
        var transfers = P.Transfers;
        await Click(P.DestinationRowRect($"r{workshop}").GetCenter());
        await Click(P.DestinationGoRect.GetCenter());
        await Until(() => App.Navigator.Current == workshop && !P.Busy, 15);
        await Frames(3);
        Check(App.Navigator.Current == workshop && P.Character.Kind == WorldKind.Repository && P.Transfers == transfers + 1 && P.LastWasWarp,
            $"the repository is one warp away from three levels down ({Tree[App.Navigator.Current].Name}, {P.Character.Kind})");
        await Shot("favourite-repository");
        await OpenList();
        await TypeText("music");
        await Frames(4);
        await Key(Godot.Key.Down);
        await Key(Godot.Key.Enter);
        await Until(() => App.Navigator.Current == music && !P.Busy, 15);
        Check(App.Navigator.Current == music, "typing finds the pin as well: Music Box");
        await Key(Godot.Key.Backspace);
        await Until(() => App.Navigator.Current == workshop && !P.Busy, 12);
        Check(App.Navigator.Current == workshop, "Backspace after a favourite: back where you were");
    }

    // ---------------------------------------------------------------- the inaccessible fixture

    private async Task NavigationDeniedRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(2.5);
        var root = App.Navigator.Current;
        await OpenList();
        var locked = Child(root, "locked");
        var empty = Child(root, "empty-room");
        var deep = Child(root, "deep-chain");
        var lr = RowFor(locked);
        Check(lr is { Detail: "access denied", Warning: true, CanExpand: false }, $"an inaccessible folder says so, in the warning colour, with nothing to open: “{lr?.Detail}”");
        Check(RowFor(empty) is { Detail: "empty", CanExpand: false }, $"the empty room: “{RowFor(empty)?.Detail}”");
        if (deep >= 0)
        {
            await Click(P.DestinationArrowPoint(Key0(deep)));
            await Until(() => RowFor(deep)?.Expanded == true, 3);
            Check(RowFor(deep)?.Expanded == true, "deep-chain opens in place");
        }

        await Shot("denied-list");
        var asked = P.ExpandRequests.Select(id => Tree[id].Name).ToList();
        Check(asked.All(n => n == "deep-chain" || n == "locked"), $"only folders opened in the list were asked for ({string.Join(", ", asked)})");
        await CloseList();
    }
}
