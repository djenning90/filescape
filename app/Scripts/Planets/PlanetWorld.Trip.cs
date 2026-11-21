using FileScape.Core.Planets;
using FileScape.Core.Places;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// A trip several levels down (PLANET-40, AD-56): chosen in the Destinations tree, flown one level at a time with the
/// ordinary launch, flight and landing for each hop (a little quicker: <see cref="TripFlightScale"/>), with a short
/// breath on each world between hops so you see where you are passing through. A panel says where the trip goes and
/// which hop this is, with Stop (Esc).
/// <para>
/// The rules: stopping during a wind-up cancels that launch, and stopping in the air lets that hop land — either way you
/// are left on the last world reached, and the trip ends there. Before each hop the next folder is checked
/// (<see cref="TripPlan.Unavailable"/>); if it has gone, or the launch is refused, the trip stops where you are and
/// says why. Backspace: the whole trip is one step of history (<see cref="PlaceNavigator.MergeLastMoves"/>), so it
/// returns to where the trip began; U is always the parent of where you are (the navigator's Up, unchanged).
/// </para>
/// Preserve: a trip is <see cref="Busy"/> from start to end (other travel waits; Esc stops it), it only ever moves the
/// navigator through its own hops (so merging its moves is safe), and it never outlives the world being active or the tree.
/// </summary>
public partial class PlanetWorld
{
    /// <summary>A trip's hop flies this fraction of an ordinary flight's time (1.2–2.0 s instead of 1.5–2.5 s): brisk enough for five levels, still the same arc and landing (by eye; DJ to judge).</summary>
    private const float TripFlightScale = 0.8f;

    /// <summary>Between landing and the next wind-up, seconds: long enough to read the world's name, short enough to keep the trip moving (by eye).</summary>
    private const float TripHopPauseSeconds = 0.55f;

    /// <summary>The trip panel shows the outcome for this long after the trip ends, seconds.</summary>
    private const float TripOutcomeSeconds = 3.5f;

    /// <summary>A trip in progress: the folders to fly through (the last is the destination) and how far it has got.</summary>
    private sealed class Trip
    {
        public required int Origin;
        public required List<int> Hops;

        /// <summary>Hops landed so far (also the navigator moves the trip has made).</summary>
        public int Reached;

        /// <summary>A hop's launch has begun and has not landed yet.</summary>
        public bool InHop;

        public bool StopRequested;
        public float Pause;
    }

    private Trip? _trip;
    private PanelContainer _tripPanel = null!;
    private RichTextLabel _tripText = null!;
    private Button _tripStop = null!;
    private float _tripOutcomeTime;

    public bool TripActive => _trip != null;

    /// <summary>For probes: the hops of the trip in progress (0 when none) and how many have landed.</summary>
    public int TripHops => _trip?.Hops.Count ?? 0;

    public int TripReached => _trip?.Reached ?? 0;

    /// <summary>The trip panel's text (probes), and whether it is shown.</summary>
    public string TripPanelText => _tripPanel.Visible ? _tripText.GetParsedText() : "";

    public Rect2 TripStopRect => _tripStop.GetGlobalRect();

    /// <summary>How the last trip ended: "arrived", "stopped", "unavailable", "refused" (probes).</summary>
    public string LastTripOutcome { get; private set; } = "";

    /// <summary>Probe instrument only: the next hop to this folder is treated as unavailable (the folder "went away"), labelled [direct] in the probe.</summary>
    public int TripTestUnavailable { get; set; } = -1;

    private void BuildTripPanel(Control root)
    {
        _tripPanel = new PanelContainer { Visible = false };
        _tripPanel.AddThemeStyleboxOverride("panel", PanelStyle());
        _tripPanel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _tripPanel.GrowHorizontal = Control.GrowDirection.Both;
        _tripPanel.GrowVertical = Control.GrowDirection.Begin;
        _tripPanel.OffsetLeft = -330; _tripPanel.OffsetRight = 330; _tripPanel.OffsetBottom = -72;
        root.AddChild(_tripPanel);
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 12);
        _tripPanel.AddChild(h);
        _tripText = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _tripText.AddThemeFontSizeOverride("normal_font_size", 14);
        _tripText.AddThemeFontSizeOverride("bold_font_size", 15);
        h.AddChild(_tripText);
        _tripStop = new Button { Text = "■ Stop (Esc)", FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "Stop the trip: you stay on the last world reached (a hop in the air lands first)." };
        _tripStop.Pressed += RequestStopTrip;
        h.AddChild(_tripStop);
        _s.Hud.AddBlocking(_tripPanel);
    }

    /// <summary>
    /// Flies to <paramref name="target"/>, a folder below where you are, one level at a time. A target one level down
    /// is an ordinary launch; anything that is not below you is reached directly.
    /// </summary>
    public void StartTrip(int target)
    {
        var tree = _s.Tree;
        if (tree == null || _current == null || Busy) return;
        if (OverviewEnter.CommitLandsThroughOverview(_universe != null))
        {
            // From the overview a destination is landed on directly (PLANET-83; the rule is Core's OverviewEnter): a trip's hops run on the surface, which the
            // overview does not tick, so a trip started here wound up and never left (the overview stayed, silent).
            LandFromOverview(target); // travel: OverviewLanding
            return;
        }

        var hops = TripPlan.Hops(tree, _current.FolderId, target);
        if (hops == null)
        {
            _s.TravelInto(target); // travel: DestinationsStraightThere
            return;
        }

        if (hops.Count == 1)
        {
            _s.Select(target);
            LaunchToSelection(); // travel: DestinationsTrip
            return;
        }

        _trip = new Trip { Origin = _current.FolderId, Hops = hops };
        LastTripOutcome = "";
        Mark("trip");
        LaunchNextHop();
    }

    /// <summary>Stop (Esc, the button): a wind-up is cancelled at once; a hop in the air lands, then the trip ends.</summary>
    public void RequestStopTrip()
    {
        if (_trip is not { } t) return;
        t.StopRequested = true;
        if (_phase is Phase.Departing or Phase.WindUp && _transfer is { Released: false })
        {
            CancelDeparture(); // the navigator has not moved: you stay where you are (a run to the pad lands on it, PlanetWorld.Departure)
            t.InHop = false;
            EndTrip("stopped");
            return;
        }

        UpdateTripPanel();
    }

    /// <summary>Per frame: between hops, wait a moment and launch the next; after a landing, count it and decide.</summary>
    private void UpdateTrip(float dt)
    {
        _tripOutcomeTime = MathF.Max(0, _tripOutcomeTime - dt);
        if (_trip == null)
        {
            if (_tripPanel.Visible && _tripOutcomeTime <= 0) _tripPanel.Visible = false;
            return;
        }

        var t = _trip;
        if (!_active || _s.Tree == null || _current == null)
        {
            EndTrip("stopped");
            return;
        }

        if (_phase != Phase.Walking || _hop != null) return; // a hop in progress: wind-up, flight or warp, landing
        if (t.InHop)
        {
            t.InHop = false;
            if (_current.FolderId != t.Hops[t.Reached])
            {
                EndTrip("refused"); // the launch was refused or cancelled (the session said why)
                return;
            }

            t.Reached++;
            if (t.Reached >= t.Hops.Count)
            {
                EndTrip("arrived");
                return;
            }

            t.Pause = TripHopPauseSeconds;
            UpdateTripPanel();
        }

        if (t.StopRequested)
        {
            EndTrip("stopped");
            return;
        }

        t.Pause -= dt;
        if (t.Pause <= 0) LaunchNextHop();
    }

    /// <summary>Checks the next hop and winds up for it from where you stand (the launcher if there is one, else straight up).</summary>
    private void LaunchNextHop()
    {
        var t = _trip!;
        var tree = _s.Tree!;
        var here = _current!.FolderId;
        var next = t.Hops[t.Reached];
        if (_s.Navigator.Current != here)
        {
            EndTrip("refused");
            return;
        }

        var why = next == TripTestUnavailable ? "it is no longer there" : TripPlan.Unavailable(tree, here, next);
        if (why != null)
        {
            _unavailableWhy = $"{(tree.Contains(next) ? tree[next].Name : "the next folder")}: {why}";
            EndTrip("unavailable");
            return;
        }

        _s.Select(next);
        t.InHop = true;
        BeginLaunch(next, DepartureEntry.TripHop); // travel: DestinationsTrip
        if (_phase is not (Phase.Departing or Phase.WindUp))
        {
            t.InHop = false;
            EndTrip("refused");
            return;
        }

        UpdateTripPanel();
    }

    private string _unavailableWhy = "";

    /// <summary>Ends the trip where you are: the hops become one step of history, the panel shows how it ended for a moment.</summary>
    private void EndTrip(string outcome)
    {
        if (_trip is not { } t) return;
        _trip = null;
        LastTripOutcome = outcome;
        Mark("trip-" + outcome);
        if (t.Reached > 1) _s.Navigator.MergeLastMoves(t.Reached);
        var tree = _s.Tree;
        var hereName = tree != null && _current != null ? tree.DisplayName(_current.FolderId) : "";
        var originName = tree != null && tree.Contains(t.Origin) ? tree.DisplayName(t.Origin) : "where you started";
        var text = TripText.Outcome(outcome, hereName, originName, t.Hops.Count, t.Reached, _unavailableWhy);
        _tripText.Text = text;
        _tripStop.Visible = false;
        _tripPanel.Visible = true;
        _tripOutcomeTime = TripOutcomeSeconds;
        if (outcome == "unavailable") _audio.Deny();
    }

    /// <summary>The panel while the trip is under way: where it goes, which hop, the route with the reached part lit.</summary>
    private void UpdateTripPanel()
    {
        if (_trip is not { } t || _s.Tree is not { } tree) return;
        _tripText.Text = TripText.Progress(t.Hops.Select(id => tree[id].Name).ToList(), t.Reached, t.StopRequested);
        _tripStop.Visible = true;
        _tripStop.Disabled = t.StopRequested;
        _tripPanel.Visible = true;
    }

    /// <summary>Esc during a trip stops it (before anything else sees the key, Main.KeyboardOwner); J waits until the trip is over.</summary>
    public bool TripKey(InputEvent e)
    {
        if (_trip == null) return DepartureKey(e); // a departure's own Esc (PlanetWorld.Departure)
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return false;
        if (k.Keycode == Key.Escape)
        {
            RequestStopTrip();
            return true;
        }

        return k.PhysicalKeycode == Key.J;
    }
}
