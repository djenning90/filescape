using FileScape.App.Places;
using FileScape.App.Planets;
using FileScape.App.Ui;
using FileScape.App.Visual;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Scanning;
using Godot;

namespace FileScape.App;

/// <summary>
/// The planetary world on the shared session (AD-39). <see cref="Main"/> keeps owning the tree, the scan, the
/// explorer, the navigator (current folder and history), the selection, the shell actions, the HUD and the state file;
/// <see cref="PlanetWorld"/> owns only what varies between the two walking worlds: the surface and gravity, the
/// camera frame, placement and travel, and the visual and audio treatment. It talks to the session only through
/// <see cref="IWorldSession"/>, so the same folder, selection and history carry over when you switch (V).
/// </summary>
public partial class Main : IWorldSession
{
    private PlanetWorld _planets = null!;

    public PlanetWorld Planets => _planets;

    /// <summary>Adds the planetary world; it stays inactive (no camera, no ticks) until Planets is the experience.</summary>
    private void BuildPlanets()
    {
        _planets = new PlanetWorld(this) { Name = "Planets" };
        AddChild(_planets);
        SetupSound(); // Main.Sound.cs (PLANET-47)
        ConnectPlanetsDestinations(); // Main.Navigation
    }

    // ---------------------------------------------------------------- IWorldSession

    PlaceNavigator IWorldSession.Navigator => _nav;
    bool IWorldSession.IsLive => _tree?.Source.IsLive ?? false;
    bool IWorldSession.IsVisible(FsNode node) => IsVisibleInPlace(node);
    bool IWorldSession.Visited(int id) => _visitedPlaces.Contains(id);
    bool IWorldSession.MayListAhead(int id) => MayReveal(id);
    bool IWorldSession.TravelInto(int id) => TravelIntoFromPlanets(id);
    int IWorldSession.PendingRequests => PendingRequests;
    bool IWorldSession.AwaitingArrival => _awaitingArrival;
    bool IWorldSession.TravelUp() => TravelUp();
    bool IWorldSession.GoBack() => _nav.Back();
    void IWorldSession.Select(int id) => Select(id);
    void IWorldSession.OpenSelection() => RequestOpen();
    void IWorldSession.Toast(string text) => _hud.Toast(text);
    void IWorldSession.ShowDocument(string path, string title, string? anchor) => ShowDocument(path, title, anchor);
    void IWorldSession.FollowLink(string documentPath, FileScape.Core.Shell.MarkdownLink link) => FollowReaderLink(new Ui.ReaderDocument("", "", "", documentPath), link);
    bool IWorldSession.ShowReadme() { ShowReadme(); return true; }
    string IWorldSession.Summary(int id) => _tree == null ? "" : PlaceText.Summary(_tree, _tree[id]);
    TimeSpan IWorldSession.Now => _clock.Elapsed;
    SignLayer IWorldSession.Signs => _signs;

    /// <summary>The commands that mean something different on a planet; the rest (open, back, up, J, L, Ctrl+L…) are shared.</summary>
    private bool HandlePlanetCommand(InputEventKey key)
    {
        if (key.IsAction(InputBindings.RootPlace) || key.CtrlPressed) return false; // Ctrl+Home (root), Ctrl+L, Ctrl+O: shared
        if (key.IsAction(InputBindings.Home)) { _planets.ReturnToArrival(); return true; }
        if (key.IsAction(InputBindings.Readme) && _planets.ExpandPreview()) return true; // R with a text preview open: read it in full (PLANET-44)
        if (key.IsAction(InputBindings.Overview)) { _planets.ToggleOverlookView(); return true; }
        if (key.IsAction(InputBindings.Frame)) { _planets.FaceSelection(); return true; }
        if (key.IsAction(InputBindings.EnterFolder)) { _planets.LaunchToSelection(); return true; }
        if (key.IsAction(InputBindings.FlightToggle)) { _planets.ToggleUniverse(); _hud.SetPlanetsOverview(_planets.UniverseOpen); return true; }
        return false;
    }

    /// <summary>
    /// Travel requested by the planets: into a child, through a link, or a jump elsewhere. Unlike <see cref="TravelInto"/>
    /// it never consults the Places composition (its parent gate and aerial view can be stale while Planets is shown).
    /// </summary>
    private bool TravelIntoFromPlanets(int id)
    {
        if (_tree == null || !_tree.Contains(id)) return false;
        if (_tree[id].Kind == NodeKind.Link) return FollowLink(id);
        if (!_tree[id].IsDirectory) return false;
        if (id == _tree[_nav.Current].ParentId) return TravelUp();
        return _tree[id].ParentId == _nav.Current ? _nav.Enter(_tree, id) : _nav.GoTo(_tree, id);
    }

    /// <summary>The shared visibility rule, for probes.</summary>
    public bool IsVisibleInPlaceForProbe(FsNode node) => IsVisibleInPlace(node);

    /// <summary>
    /// The curated first impression (PLANET-15): the Starter Sky, a small folder of real files FileScape writes for
    /// itself into its own data folder (never anywhere else), so open and preview work on it like on any folder.
    /// </summary>
    public string EnsureStarterSky()
    {
        var parent = System.IO.Path.Combine(OS.GetUserDataDir(), "samples");
        System.IO.Directory.CreateDirectory(parent);
        return FileScape.Core.Samples.SampleSky.Create(parent);
    }

    /// <summary>The Starter Sky's path, or null (with a toast saying why) if it could not be written.</summary>
    private string? TryStarterSky()
    {
        try
        {
            return EnsureStarterSky();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            GD.PrintErr($"[planets] the Starter Sky could not be prepared: {e.Message}");
            _hud.Toast($"The Starter Sky could not be prepared ({e.Message}). The planets show the current data instead; Open… (Ctrl+O) explores a folder.", new Color(1f, 0.7f, 0.5f), 9);
            return null;
        }
    }

    /// <summary>The planets' legend: the file-kind lantern colours, then the launchers' and veiled worlds' colours.</summary>
    private static IReadOnlyList<(Color, string)> PlanetLegend()
    {
        var list = Palette.Category.Select(kv => (kv.Value, PlaceText.CategoryName(kv.Key) + " (lantern colour)")).ToList();
        list.Add((new Color(1f, 0.85f, 0.45f), "launcher: stand on it and press Space"));
        list.Add((new Color(0.75f, 0.8f, 0.95f), "the way back: launcher by the arrival meadow"));
        list.Add((new Color(0.55f, 0.6f, 0.75f), "veiled world: not listed yet (it lists as you approach)"));
        return list;
    }
}
