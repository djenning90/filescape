using FileScape.App.Ui;
using FileScape.App.Visual;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Scanning;

namespace FileScape.App.Planets;

/// <summary>
/// The shared FileScape session as a walking world sees it (AD-39). One tree, one navigator (current folder and
/// history), one selection and one set of shell actions serve every experience; a world implementation varies only
/// its surface and gravity, camera frame, placement and travel, and visual treatment. Everything a world does to the
/// filesystem model goes through here, so switching worlds keeps the folder, the selected file and the history.
/// <para>
/// Main implements it; <see cref="PlanetWorld"/> is the world that uses it. A world asks (<see cref="TravelInto"/>,
/// <see cref="TravelUp"/>, <see cref="GoBack"/>) and animates when the navigator answers (its Changed event reaches the
/// world as <see cref="PlanetWorld.OnNavChanged"/>), so a refusal or a slow link never leaves the world somewhere the
/// session is not.
/// </para>
/// </summary>
public interface IWorldSession
{
    /// <summary>The scanned tree (null before the first scan); a new instance after Open… or a rescan, with new node ids.</summary>
    FsTree? Tree { get; }

    /// <summary>The current folder and the history (Backspace).</summary>
    PlaceNavigator Navigator { get; }

    /// <summary>The selection shared by every world (the HUD's card shows it).</summary>
    Core.Interaction.SelectionState Selection { get; }

    /// <summary>The live scanner behind the tree (null for synthetic data).</summary>
    WorldExplorer? Explorer { get; }

    /// <summary>The shared HUD (toolbar, cards, drawers); worlds read its blocking rectangles to leave its clicks alone.</summary>
    Hud Hud { get; }

    /// <summary>Screen-space destination names, shared with Places (LABEL-01).</summary>
    SignLayer Signs { get; }

    /// <summary>The graphics quality setting.</summary>
    Quality Quality { get; }

    /// <summary>The app clock (time since start).</summary>
    TimeSpan Now { get; }

    /// <summary>True for a live filesystem (open and reveal act on real files); false for synthetic data.</summary>
    bool IsLive { get; }

    /// <summary>The shared visibility rule (hidden and system entries unless shown with H).</summary>
    bool IsVisible(FsNode node);

    /// <summary>Visited this session (the overview rings them).</summary>
    bool Visited(int id);

    /// <summary>Into a child folder, through a link, up, or a jump elsewhere: the navigator decides, the world animates (Changed).</summary>
    bool TravelInto(int id);

    /// <summary>Up to the parent folder (false when there is none); animates like <see cref="TravelInto"/>.</summary>
    bool TravelUp();

    /// <summary>A new world is starting and its arrival folder is not known yet (show nothing rather than the wrong place).</summary>
    bool AwaitingArrival { get; }

    /// <summary>Requests to the explorer still waiting for an answer (a link being resolved, a path).</summary>
    int PendingRequests { get; }

    /// <summary>Back through the history (Backspace).</summary>
    bool GoBack();

    /// <summary>Selects a node in the shared selection.</summary>
    void Select(int id);

    /// <summary>Open the selection with Windows, with the usual confirmations (code, links, cloud placeholders).</summary>
    void OpenSelection();

    /// <summary>Shows the current folder's README (Main's implementation always returns true).</summary>
    bool ShowReadme();

    /// <summary>A short message on the HUD.</summary>
    void Toast(string text);

    /// <summary>A folder's one-line summary ("3 folders · 12 files · 4.2 MB in all").</summary>
    string Summary(int id);

    /// <summary>Whether a folder is prominent (a mark shown before its name), and the mark.</summary>
    ProminenceInfo ProminenceOf(int id);

    /// <summary>
    /// A destination was committed: close the dialogs that would cover the flight (Open Location, the HUD's Destinations
    /// drawer, the problem list). Panels that guide the sequence (a trip, the walkthrough) are the world's and stay (PLANET-41).
    /// </summary>
    void DismissNavigationDialogs();

    /// <summary>Goes to a full path, as Go to path does (the connected world finds it; the world animates the change).</summary>
    void GoToPath(string path);

    /// <summary>
    /// The places the player cares about (PLANET-41): their pins, then the repositories found so far, and the repository
    /// set (to mark rows). Reading it advances the repository index by a bounded step.
    /// </summary>
    (IReadOnlyList<FavouritePlace> Places, IReadOnlySet<int> Repositories) Favourites();

    /// <summary>True when the favourites have changed since <see cref="Favourites"/> was last read (advances discovery a bounded step).</summary>
    bool FavouritesChanged { get; }
}
