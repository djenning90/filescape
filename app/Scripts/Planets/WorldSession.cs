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
/// </summary>
public interface IWorldSession
{
    FsTree? Tree { get; }
    PlaceNavigator Navigator { get; }
    Core.Interaction.SelectionState Selection { get; }
    WorldExplorer? Explorer { get; }
    Hud Hud { get; }

    /// <summary>Screen-space destination names, shared with Places (LABEL-01).</summary>
    SignLayer Signs { get; }
    Quality Quality { get; }

    /// <summary>The app clock (time since start).</summary>
    TimeSpan Now { get; }

    /// <summary>True for a live filesystem (open and reveal act on real files); false for synthetic data.</summary>
    bool IsLive { get; }

    /// <summary>The shared visibility rule (hidden and system entries unless shown with H).</summary>
    bool IsVisible(FsNode node);

    /// <summary>Into a child folder, through a link, up, or a jump elsewhere: the navigator decides, the world animates (Changed).</summary>
    bool TravelInto(int id);

    bool TravelUp();

    /// <summary>A new world is starting and its arrival folder is not known yet (show nothing rather than the wrong place).</summary>
    bool AwaitingArrival { get; }

    /// <summary>Requests to the explorer still waiting for an answer (a link being resolved, a path).</summary>
    int PendingRequests { get; }
    bool GoBack();
    void Select(int id);

    /// <summary>Open the selection with Windows, with the usual confirmations (code, links, cloud placeholders).</summary>
    void OpenSelection();

    bool ShowReadme();
    void Toast(string text);

    /// <summary>A folder's one-line summary ("3 folders · 12 files · 4.2 MB in all").</summary>
    string Summary(int id);

    ProminenceInfo ProminenceOf(int id);
}
