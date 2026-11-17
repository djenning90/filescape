using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;

namespace FileScape.App;

/// <summary>
/// Direct navigation's part of the session (PLANET-39..41, AD-56): dismissing the dialogs that would cover a flight,
/// Go to path for a favourite, the favourites (the player's pins and the repositories found so far, from a
/// <see cref="RepositoryIndex"/> that reads only what scanning has already found, a bounded step at a time), and the
/// Locations marks for where you are. The planet's Destinations tree and trips use these through
/// <see cref="Planets.IWorldSession"/>.
/// Preserve: nothing here lists a folder; reading the favourites costs at most <see cref="DiscoveryNodesPerRead"/> nodes.
/// </summary>
public partial class Main
{
    private readonly RepositoryIndex _repoIndex = new();
    private List<FavouritePlace> _favourites = [];
    private IReadOnlyList<string> _favouritePins = [];
    private int _favouriteRepoCount = -1;
    private int _favouriteTreeVersion = -1;
    private FsTree? _favouriteTree;

    /// <summary>
    /// Nodes the repository index reads per call (once a frame while the Destinations list is open): about a
    /// millisecond's work, so a large tree is caught up over a few frames and the list stays responsive (by measurement
    /// of the loop's cost, not tuned further).
    /// </summary>
    private const int DiscoveryNodesPerRead = 20_000;

    /// <summary>The ☰ button in Planets opens the planet's list (the same one J opens). Called once the HUD and the planets exist.</summary>
    private void ConnectPlanetsDestinations() => _hud.PlanetsDestinationsToggle = () => _planets.ToggleContents();

    void Planets.IWorldSession.DismissNavigationDialogs()
    {
        _hud.HideLocations();
        _hud.HideProblemList();
        if (_hud.DestinationsOpen) _hud.ToggleDestinations(focusFilter: false);
    }

    void Planets.IWorldSession.GoToPath(string path) => OnPathSubmitted(path);

    bool Planets.IWorldSession.FavouritesChanged
    {
        get
        {
            if (_tree == null) return false;
            _repoIndex.Update(_tree, DiscoveryNodesPerRead);
            return _repoIndex.Repositories.Count != _favouriteRepoCount || !ReferenceEquals(_prefs.Pins, _favouritePins) || !ReferenceEquals(_tree, _favouriteTree)
                   || (_favourites.Any(f => f.Id < 0) && _tree.Version != _favouriteTreeVersion); // a pin may have been reached
        }
    }

    (IReadOnlyList<FavouritePlace> Places, IReadOnlySet<int> Repositories) Planets.IWorldSession.Favourites()
    {
        if (_tree == null) return ([], new HashSet<int>());
        _repoIndex.Update(_tree, DiscoveryNodesPerRead);
        _favouriteRepoCount = _repoIndex.Repositories.Count;
        _favouritePins = _prefs.Pins;
        _favouriteTree = _tree;
        _favouriteTreeVersion = _tree.Version;
        var list = new List<FavouritePlace>();
        var pinnedIds = new HashSet<int>();
        foreach (var pin in _prefs.Pins)
        {
            var id = PathLookup.Find(_tree, pin);
            if (id >= 0) pinnedIds.Add(id);
            var trimmed = pin.TrimEnd('\\', '/');
            var cut = trimmed.LastIndexOfAny(['\\', '/']);
            var name = id >= 0 ? _tree.DisplayName(id) : cut >= 0 && cut < trimmed.Length - 1 ? trimmed[(cut + 1)..] : trimmed;
            list.Add(new FavouritePlace(FavouriteKind.Pinned, pin, name, id));
        }

        list.AddRange(_repoIndex.Repositories.Where(id => !pinnedIds.Contains(id) && _tree.Contains(id))
            .Select(id => new FavouritePlace(FavouriteKind.Repository, _tree.FullPath(id), _tree.DisplayName(id), id))
            .OrderBy(f => f.Name, NaturalNameOrder.Instance));
        _favourites = list;
        return (list, _repoIndex.Set);
    }

    /// <summary>Locations rows: a location you are inside (not at) says so; the row for where you are is marked by the HUD.</summary>
    private List<(int Id, string Text, string Tooltip)> MarkInside(List<(int Id, string Text, string Tooltip)> rows)
    {
        if (_tree == null || !_tree.Contains(_nav.Current)) return rows;
        var ancestors = new HashSet<int>(_tree.Ancestors(_nav.Current));
        return rows.Select(r => r.Id > 0 && r.Id != _nav.Current && ancestors.Contains(r.Id) ? (r.Id, r.Text + "   ·  you are inside", r.Tooltip) : r).ToList();
    }
}
