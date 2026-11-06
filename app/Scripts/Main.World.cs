using FileScape.App.Places;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Scanning;
using Godot;

namespace FileScape.App;

/// <summary>
/// The connected world (AD-17/18/19/20): live data comes from a <see cref="WorldExplorer"/> (one namespace from
/// "This computer" down), places are listed when visited, links are followed on purpose, typed or pasted paths and
/// the Locations hub reach drives and WSL distributions, and hidden/system entries are filtered from places.
/// </summary>
public partial class Main
{
    private WorldExplorer? _explorer;
    private readonly Dictionary<int, int> _pendingFollow = []; // ticket → link id
    private readonly Dictionary<int, string> _pendingGoTo = []; // ticket → typed path
    private readonly HashSet<int> _ordinaryOverride = [];
    private bool _showHidden;
    private bool _awaitingArrival;
    private string? _followCaption;
    private int _pendingNewRoot = -1;

    public WorldExplorer? Explorer => _explorer;
    public bool ShowHidden => _showHidden;
    public bool AwaitingArrival => _awaitingArrival;
    public string? LastExplorerError { get; private set; }
    public string? LastWorldNote { get; private set; }
    public int PendingRequests => _pendingFollow.Count + _pendingGoTo.Count;

    /// <summary>Starts exploring the live filesystem with <paramref name="path"/> as the arrival root.</summary>
    public void StartLive(string? path)
    {
        var delay = _testListDelayMs;
        var options = new WorldExplorer.Options
        {
            DefaultDistribution = Locations.DefaultWslDistribution(),
            // SHUT-01 instrument only: an uncancellable stall before each listing, like an unresponsive share.
            BeforeEnumerate = delay > 0 ? _ => Thread.Sleep(delay) : null,
        };
        StartScan(new WorldExplorer(path, options));
    }

    /// <summary>
    /// The whole-tree reference mode (2026-11-05 correction, DJ: keep the previous whole-tree visualization): one chosen
    /// folder scanned completely in the background with the original policy (links shown, never followed), so the
    /// Classic FSN and District maps show that whole tree. Broad places ask first (FS-12). No hub, no link travel.
    /// </summary>
    public void StartBoundedTree(string path, bool confirmBroad = true)
    {
        var normalized = Locations.Normalize(path, Locations.DefaultWslDistribution(), OperatingSystem.IsWindows()) ?? path;
        if (confirmBroad && Locations.IsBroad(normalized))
        {
            Confirm($"“{normalized}” is a broad place (a drive, a profile, a distribution root or a cloud folder).\n\nA bounded tree scan lists everything below it (metadata only, up to {ScanSession.DefaultMaxNodes:N0} entries), which can take a long time.\nThe connected world lists only what you visit.\n\nScan it as a bounded tree anyway?",
                () => StartScan(new DirectoryScanner(normalized)));
            return;
        }

        StartScan(new DirectoryScanner(normalized));
    }

    /// <summary>The current mode, as named in the UI and docs.</summary>
    public string ModeName => _explorer != null ? "Connected world (explore on demand)" : _tree?.Source.IsLive == true ? "Bounded tree scan (whole-tree reference)" : "Sample data";

    private void StartFreshHere()
    {
        _hud.HideLocations();
        if (_tree == null || !_tree.Source.IsLive || _tree.Count == 0) return;
        var path = _tree.FullPath(_nav.Current);
        if (string.IsNullOrEmpty(path)) return;
        StartLive(path);
    }

    private void ScanHereAsTree()
    {
        _hud.HideLocations();
        if (_tree == null || _tree.Count == 0) return;
        var path = _tree.FullPath(_nav.Current);
        if (string.IsNullOrEmpty(path) || !_tree.Source.IsLive)
        {
            _hud.Toast("Choose a real folder first (Open folder… or a path in Locations).");
            return;
        }

        StartBoundedTree(path);
    }

    private void OnScanPathAsTree(string text)
    {
        _hud.HideLocations();
        if (string.IsNullOrWhiteSpace(text)) return;
        if (LocationRequest.Parse(text) is { IsHub: true })
        {
            GoToHub();
            return;
        }

        StartBoundedTree(text);
    }

    /// <summary>Called after StartScan: remember the explorer and wait for the arrival folder.</summary>
    private void OnSourceStarted(IScanSource source)
    {
        _explorer = source as WorldExplorer;
        _pendingFollow.Clear();
        _pendingGoTo.Clear();
        _awaitingArrival = _explorer?.ArrivalPath != null;
        _followCaption = null;
        _hud.SetLocationsAvailable(_explorer != null);
        _hud.SetMode(ModeName, _tree?.Source.IsLive == true);
    }

    private void HandleReplies(ScanBatch batch)
    {
        foreach (var r in batch.Replies) OnExplorerReply(r);
    }

    private void OnExplorerReply(ExplorerReply r)
    {
        if (_tree == null) return;
        if (r.Ticket == WorldExplorer.ArrivalTicket)
        {
            _awaitingArrival = false;
            var arrival = r.NodeId >= 0 ? r.NodeId : 0;
            if (r.Error != null)
            {
                LastExplorerError = r.Error;
                _hud.Toast($"{r.Error} Starting at {WorldExplorer.HubName} instead.", new Color(1f, 0.8f, 0.5f), 8);
            }

            if (_tree[arrival].Kind != NodeKind.Directory) arrival = _tree[arrival].ParentId;
            _nav.Reset(arrival);
            _place = null; // compose the arrival place on the next frame
            GD.Print($"[world] arrival {_tree.FullPath(arrival)} id={arrival} error={r.Error ?? "-"}");
            return;
        }

        if (_pendingFollow.Remove(r.Ticket, out var linkId))
        {
            _followCaption = null;
            _hud.SetTransitionNote(null);
            if (r.NodeId < 0)
            {
                LastExplorerError = r.Error;
                _hud.Toast(r.Error ?? "That link could not be followed.", new Color(1f, 0.8f, 0.5f), 7);
                _selection.Select(linkId);
                UpdateInspector();
                return;
            }

            GD.Print($"[world] link {_tree.FullPath(linkId)} -> {_tree.FullPath(r.NodeId)}");
            var dest = _tree[r.NodeId].IsDirectory ? r.NodeId : _tree[r.NodeId].ParentId;
            if (dest == _nav.Current)
            {
                // A loop to where you stand (LINK-05): say so instead of doing nothing.
                LastWorldNote = $"“{_tree[linkId].Name}” leads back to {_tree.DisplayName(dest)} — where you already are (a loop).";
                _hud.Toast(LastWorldNote, new Color(0.6f, 0.95f, 1f), 6);
                return;
            }

            if (!_tree[r.NodeId].IsDirectory) _selection.Select(r.NodeId); // a file link: its folder, with the file selected
            _nav.FollowLink(_tree, linkId, r.NodeId);
            return;
        }

        if (_pendingGoTo.Remove(r.Ticket, out var typed))
        {
            _hud.SetTransitionNote(null);
            if (r.NodeId < 0)
            {
                LastExplorerError = r.Error;
                _hud.Toast(r.Error ?? $"“{typed}” could not be reached.", new Color(1f, 0.8f, 0.5f), 7);
                return;
            }

            GD.Print($"[world] go to {typed} -> {_tree.FullPath(r.NodeId)}");
            var target = r.NodeId;
            if (r.Ticket == _pendingNewRoot)
            {
                // Open folder… in a live world: a new arrival root, reached without restarting.
                _pendingNewRoot = -1;
                if (!_tree[target].IsDirectory) target = _tree[target].ParentId;
                _nav.Reset(target);
                _place = null;
                return;
            }

            if (!_tree[target].IsDirectory)
            {
                _selection.Select(target); // a file: go to its folder with it selected
                target = _tree[target].ParentId;
            }

            if (_experience != Experience.Places) SetExperience(Experience.Places);
            if (target == _nav.Current) _hud.Toast("You are already here.");
            else _nav.GoTo(_tree, target);
        }
    }

    /// <summary>Follows a link deliberately (LINK-02): resolve on demand, then travel; explain when it cannot.</summary>
    private bool FollowLink(int linkId)
    {
        if (_tree == null) return false;
        var node = _tree[linkId];
        if (_explorer == null)
        {
            _hud.Toast($"“{node.Name}” is a link in sample data; only live folders can be followed.");
            return false;
        }

        if (node.Link is not { } info || !info.CanFollow)
        {
            _hud.Toast(node.Link is { } li ? WorldExplorer.Explain(li) : $"“{node.Name}” cannot be followed.", new Color(1f, 0.8f, 0.5f), 7);
            _selection.Select(linkId);
            return false;
        }

        if (_pendingFollow.ContainsValue(linkId)) return true;
        var ticket = _explorer.FollowLink(linkId);
        _pendingFollow[ticket] = linkId;
        _followCaption = $"Following {info.KindName} “{node.Name}”…";
        _hud.SetTransitionNote(_followCaption);
        return true;
    }

    /// <summary>Retries listing the selected folder or resolving the selected link (LINK-06, LOC-04).</summary>
    public void RetrySelection()
    {
        if (_tree == null || _explorer == null) return;
        var id = _selection.Selected >= 0 ? _selection.Selected : _nav.Current;
        _explorer.Retry(id);
        _hud.Toast($"Trying “{_tree.DisplayName(id)}” again…");
    }

    // ------------------------------------------------------------------ Go to path and Locations (LOC-01)

    private void ShowGoToPath()
    {
        ReleaseAllInput();
        _hud.ShowLocations(LocationRows(), focusPath: true);
    }

    private void ShowLocations()
    {
        ReleaseAllInput();
        _hud.ShowLocations(LocationRows(), focusPath: false);
    }

    private List<(int Id, string Text, string Tooltip)> LocationRows()
    {
        var rows = new List<(int, string, string)>();
        if (_tree == null || _tree.Count == 0 || !_tree.Root.IsHub)
        {
            // Sample data or a bounded tree: the hub is still one choice away (LOC-06).
            rows.Add((HubRow, $"⌂  {WorldExplorer.HubName}  ·  explore your computer: every drive and WSL distribution", "Starts the connected world at the hub (the place Windows calls This PC). Nothing is scanned until you go somewhere."));
            return rows;
        }

        rows.Add((0, $"⌂  {WorldExplorer.HubName}  ·  all drives and WSL distributions (This PC)", "The hub: drives and WSL distributions as gates."));
        if (_nav.Root > 0) rows.Add((_nav.Root, $"★  Arrival: {_tree.DisplayName(_nav.Root)}", _tree.FullPath(_nav.Root)));
        foreach (var c in _tree.Root.Children)
        {
            var n = _tree[c];
            var kind = n.Location switch
            {
                LocationKind.Drive => "Windows drive",
                LocationKind.Wsl => "WSL · Linux (case-sensitive)",
                LocationKind.Network => "network location",
                _ => "location",
            };
            var state = Listing.Of(n) is var s && s is not (ListingState.Open or ListingState.Empty) ? $" · {Listing.Describe(s)}" : "";
            rows.Add((c, $"{n.Name}   ·  {kind}{state}", _tree.FullPath(c)));
        }

        return rows;
    }

    /// <summary>The Locations row that starts the connected world at the hub when there is none yet.</summary>
    private const int HubRow = -2;

    /// <summary>LOC-06: the hub, from anywhere: in the connected world travel there; otherwise start it there.</summary>
    public void GoToHub()
    {
        _hud.HideLocations();
        if (_explorer == null || _tree == null || _tree.Count == 0 || !_tree.Root.IsHub)
        {
            GD.Print("[world] starting the connected world at the hub");
            StartLive(null);
            return;
        }

        if (_experience != Experience.Places) SetExperience(Experience.Places);
        if (_nav.Current == 0) _hud.Toast($"You are already at {WorldExplorer.HubName}.");
        else _nav.GoTo(_tree, 0);
    }

    private void OnLocationChosen(int id)
    {
        if (id == HubRow || id == 0)
        {
            GoToHub();
            return;
        }

        _hud.HideLocations();
        if (_tree == null || !_tree.Contains(id)) return;
        if (_experience != Experience.Places) SetExperience(Experience.Places);
        if (id == _nav.Current) _hud.Toast("You are already here.");
        else _nav.GoTo(_tree, id);
    }

    private void OnPathSubmitted(string text)
    {
        _hud.HideLocations();
        if (string.IsNullOrWhiteSpace(text)) return;
        if (_explorer == null)
        {
            // From sample data: start exploring the live filesystem at that path.
            var normalized = Locations.Normalize(text, Locations.DefaultWslDistribution(), OperatingSystem.IsWindows());
            if (normalized == null)
            {
                _hud.Toast($"“{text}” is not a full path FileScape understands.");
                return;
            }

            StartLive(normalized);
            return;
        }

        var ticket = _explorer.GoTo(text);
        _pendingGoTo[ticket] = text;
        _hud.SetTransitionNote($"Finding {text}…");
    }

    // ------------------------------------------------------------------ place texts (LINK-03, VISI-03)

    /// <summary>A short note on what kind of location this is (WSL names are case-sensitive; live listing is on demand).</summary>
    private string LocationNote(int id)
    {
        if (_tree == null) return "";
        for (var a = id; a >= 0; a = _tree[a].ParentId)
        {
            if (_tree[a].Location == LocationKind.Wsl) return " · WSL · Linux, case-sensitive names";
            if (_tree[a].Location != LocationKind.None) break;
        }

        return "";
    }

    /// <summary>How you got here, when it was through a link: the connection and the way back (LINK-03).</summary>
    private string? ConnectionText()
    {
        if (_tree == null) return null;
        var via = _nav.CurrentVia;
        if (via < 0 || !_tree.Contains(via)) return null;
        var link = _tree[via];
        var kind = link.Link?.KindName ?? "link";
        var others = _tree.Nodes.Count(n => n.Kind == NodeKind.Link && n.Id != via && n.Link?.TargetId == _nav.Current);
        var also = others > 0 ? $" · also reached by {others} other link{(others == 1 ? "" : "s")}" : "";
        var loop = IsAncestor(_nav.Current, via) ? " · this leads back to a folder above the link (a loop)" : "";
        return $"⇢ You came through the {kind} “{link.Name}” in {_tree.DisplayName(link.ParentId)}{loop}{also}. Backspace returns through it; ▲ Up goes to this folder's real parent.";
    }

    /// <summary>The place's folders and links that need attention, aggregated (VISI-03).</summary>
    private List<(int Id, string Text)> PlaceProblems()
    {
        var list = new List<(int, string)>();
        if (_tree == null || _place == null) return list;
        foreach (var g in _place.Gates)
        {
            var n = _tree[g.TargetId];
            if (g.IsLink)
            {
                if (n.Link is { Status: not (LinkStatus.Unresolved or LinkStatus.Resolved) } li) list.Add((n.Id, $"⇢ {n.Name} · {PlaceText.LinkShort(_tree, n)}"));
                continue;
            }

            if (g.State is ListingState.Denied or ListingState.Failed or ListingState.Unavailable)
            {
                list.Add((n.Id, $"{n.Name} · {Listing.Describe(g.State)}"));
            }
        }

        return list;
    }

    // ------------------------------------------------------------------ visibility (VISI-04, AD-19)

    private void ToggleHiddenVisibility()
    {
        _showHidden = !_showHidden;
        _hud.SetHiddenToggle(_showHidden);
        if (_experience == Experience.Places && _place != null) Recompose(rebuildOnly: true);
        _hud.Toast(_showHidden ? "Showing hidden and system items." : "Hiding hidden and system items (as Explorer does by default). Totals still include them.");
    }

    /// <summary>Windows hidden/system entries are filtered unless shown; dot-names are always shown (AD-19).</summary>
    private bool IsVisibleInPlace(FsNode n) => _showHidden || (n.Flags & (NodeFlags.Hidden | NodeFlags.System)) == 0;

    // ------------------------------------------------------------------ identity override (IDENT-03)

    private void ToggleOrdinaryOverride()
    {
        if (_tree == null) return;
        var id = _nav.Current;
        if (!_ordinaryOverride.Add(id)) _ordinaryOverride.Remove(id);
        if (_experience == Experience.Places) Recompose(rebuildOnly: true);
        UpdateInspector();
    }

    private PlaceKindInfo KindOf(int folderId) => _tree == null ? PlaceKindInfo.Ordinary : PlaceKinds.Classify(_tree, folderId, _ordinaryOverride.Contains(folderId));
}
