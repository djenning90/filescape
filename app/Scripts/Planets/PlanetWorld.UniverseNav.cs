using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// Moving through a large overview (PLANET-93, AD-77). DJ (fifth playtest follow-up): "Investigate selecting a world as
/// an orbit anchor, framing that selection, zooming toward it, and panning through nearby systems … teach the controls
/// briefly in the view." What was there: a click selects a world, and the orbit (drag, ← →) turns round it; F frames it
/// again; WASD and middle-drag pan; the wheel zooms toward the orbit's centre only. Added here:
/// <list type="bullet">
/// <item><b>The wheel zooms toward the pointer:</b> rolling in over a world also slides the orbit's centre a share of the
/// way to it (<see cref="WheelTowardShare"/>), so a few notches dive into the system you point at without a click; once
/// it is the world nearest the centre it is "approached" and its system opens (PLANET-91).</item>
/// <item><b>[ and ]</b> step to the previous or next world round the selected world's ring (its siblings in the layout's
/// name order), each a selection (anchor, frame, card): panning through nearby systems one at a time.</item>
/// <item><b>Z</b> selects the system the selected world orbits (up a level), framing the wider system.</item>
/// <item>A short hint in the view (top centre) when the overview opens, fading after <see cref="UniHintSeconds"/> or once
/// two of the controls it teaches have been used.</item>
/// </list>
/// Nothing here travels: landing stays Enter (or a double-click) through the shared path (stream 1's), and none of
/// these keys is a shortcut outside the overview. Main thread only.
/// </summary>
public partial class PlanetWorld
{
    /// <summary>One wheel notch in over a world moves the orbit's centre this share of the way to it (and the distance by
    /// <see cref="WheelZoomIn"/>): about five notches centre it. Higher dives faster but makes the view swing more.</summary>
    private const float WheelTowardShare = 0.3f;

    /// <summary>How long the controls hint stays when the overview opens, seconds, before fading over
    /// <see cref="UniHintFadeSeconds"/>; shorter after the first few openings (<see cref="UniHintRepeatSeconds"/>).</summary>
    private const float UniHintSeconds = 9f, UniHintRepeatSeconds = 4f, UniHintFadeSeconds = 0.8f;

    /// <summary>Openings that show the long hint; later ones show it for <see cref="UniHintRepeatSeconds"/>.</summary>
    private const int HintLongOpenings = 2;

    /// <summary>The controls the hint teaches, noted as they are used (two used: it steps aside early).</summary>
    private enum UniverseControl { Select, Wheel, Ring, Up }

    private PanelContainer _uniHintPanel = null!;
    private Label _uniHintLabel = null!;
    private float _uniHintT = float.MaxValue, _uniHintFor;
    private int _uniHintOpenings;
    private readonly HashSet<UniverseControl> _uniHintUsed = [];

    /// <summary>The hint's text (probes).</summary>
    public string UniverseHintText => _uniHintLabel.Text;

    /// <summary>The hint is showing (probes).</summary>
    public bool UniverseHintShown => _uniHintPanel.Visible && _uniHintPanel.Modulate.A > 0.05f;

    /// <summary>The controls hint: a small panel at the top centre, below the toolbar, that ignores the mouse.</summary>
    private void BuildUniverseHint(Control root)
    {
        _uniHintPanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = PanelStyle();
        style.ContentMarginTop = style.ContentMarginBottom = 6;
        _uniHintPanel.AddThemeStyleboxOverride("panel", style);
        _uniHintPanel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        // Viewport units: 620 wide, centred, 64 below the top edge (clear of the toolbar).
        _uniHintPanel.OffsetLeft = -310; _uniHintPanel.OffsetRight = 310; _uniHintPanel.OffsetTop = 64; _uniHintPanel.OffsetBottom = 120;
        root.AddChild(_uniHintPanel);
        _uniHintLabel = MakeLabel(15, new Color(0.88f, 0.93f, 1f), HorizontalAlignment.Center, outline: 0);
        _uniHintLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _uniHintLabel.Text = "Click a world to orbit it · F frames it · the wheel zooms toward the pointer\n"
            + "[ ] the next world round its ring · Z up a level · WASD pan · a haze holds folded worlds: come closer";
        _uniHintPanel.AddChild(_uniHintLabel);
    }

    /// <summary>The overview opened: the hint shows (longer the first times this session).</summary>
    private void ShowUniverseHint()
    {
        _uniHintOpenings++;
        _uniHintFor = _uniHintOpenings <= HintLongOpenings ? UniHintSeconds : UniHintRepeatSeconds;
        _uniHintT = 0;
        _uniHintUsed.Clear();
        _uniHintPanel.Visible = true;
        _uniHintPanel.Modulate = Colors.White;
    }

    private void HideUniverseHint()
    {
        _uniHintPanel.Visible = false;
        _uniHintT = float.MaxValue;
    }

    /// <summary>Every overview frame: the hint's time and fade.</summary>
    private void UpdateUniverseHint(float dt)
    {
        if (!_uniHintPanel.Visible) return;
        _uniHintT += dt;
        var fadeFrom = _uniHintUsed.Count >= 2 ? MathF.Min(_uniHintFor, _uniHintT) : _uniHintFor;
        if (_uniHintUsed.Count >= 2 && _uniHintT < _uniHintFor) _uniHintFor = _uniHintT; // two controls used: fade now
        var a = Mathf.Clamp(1f - (_uniHintT - fadeFrom) / UniHintFadeSeconds, 0f, 1f);
        _uniHintPanel.Modulate = new Color(1, 1, 1, a);
        if (a <= 0f) _uniHintPanel.Visible = false;
    }

    private void NoteUniverseControl(UniverseControl c)
    {
        if (_universe != null) _uniHintUsed.Add(c);
    }

    /// <summary>A wheel notch in at <paramref name="screen"/>: the orbit's centre slides toward the world there, if any.</summary>
    private void ZoomTowardPointer(Vector2 screen)
    {
        var id = PickUniverse(screen);
        if (id < 0 || !_uniIndex.TryGetValue(id, out var i)) return;
        var to = UniverseOrigin + _uniMotion[i].To;
        _uniFocusTarget = _uniFocusTarget.Lerp(to, WheelTowardShare);
    }

    /// <summary>[ or ]: the previous or next world round the selected world's ring (wrapping), selected and framed.</summary>
    public void StepRoundRing(int direction)
    {
        if (_universe == null || !_uniIndex.TryGetValue(_uniSelected, out var si)) return;
        var parent = _uniBodies[si].Body.ParentId;
        // From the root (no ring of its own to step round), step onto its own ring.
        if (parent < 0)
        {
            var first = _uniBodies.FindIndex(b => b.Body.ParentId == _uniSelected);
            if (first >= 0) SelectInUniverse(_uniBodies[first].Body.NodeId);
            NoteUniverseControl(UniverseControl.Ring);
            return;
        }

        var ring = new List<int>();
        for (var i = 0; i < _uniBodies.Count; i++)
        {
            if (_uniBodies[i].Body.ParentId == parent) ring.Add(_uniBodies[i].Body.NodeId);
        }

        var at = ring.IndexOf(_uniSelected);
        var next = ring[((at + direction) % ring.Count + ring.Count) % ring.Count];
        SelectInUniverse(next);
        NoteUniverseControl(UniverseControl.Ring);
    }

    /// <summary>Z: the system the selected world orbits, selected and framed (nothing above the overview's root).</summary>
    public void StepUpLevel()
    {
        if (_universe == null || !_uniIndex.TryGetValue(_uniSelected, out var si)) return;
        var parent = _uniBodies[si].Body.ParentId;
        if (parent >= 0 && _uniIndex.ContainsKey(parent)) SelectInUniverse(parent);
        NoteUniverseControl(UniverseControl.Up);
    }
}
