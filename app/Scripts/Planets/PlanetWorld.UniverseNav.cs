using FileScape.Core.Interaction;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// Moving through a large overview (PLANET-93, AD-77). DJ (fifth playtest follow-up): "Investigate selecting a world as
/// an orbit anchor, framing that selection, zooming toward it, and panning through nearby systems … teach the controls
/// briefly in the view." What was there: a click selects a world, and the orbit (drag, ← →) turns round it; F frames it
/// again; WASD and middle-drag pan; the wheel zooms toward the orbit's centre only. Added here:
/// <list type="bullet">
/// <item><b>The wheel zooms toward the pointer:</b> rolling in over a world also slides the orbit's centre a share of the
/// way to it (<see cref="OverviewControls.WheelTowardShare"/>; with Shift two notches' worth, PLANET-110), so a few notches dive into the system you point at without a click; once
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
    /// <summary>How long the controls hint stays when the overview opens, seconds, before fading over
    /// <see cref="UniHintFadeSeconds"/>; shorter after the first few openings (<see cref="UniHintRepeatSeconds"/>).</summary>
    private const float UniHintSeconds = 9f, UniHintRepeatSeconds = 4f, UniHintFadeSeconds = 0.8f;

    /// <summary>Openings that show the long hint; later ones show it for <see cref="UniHintRepeatSeconds"/>.</summary>
    private const int HintLongOpenings = 2;

    /// <summary>The controls the hint teaches, noted as they are used (two used: it steps aside early).</summary>
    private enum UniverseControl { Select, Wheel, Ring, Up }

    private PanelContainer _uniHintPanel = null!;
    private RichTextLabel _uniHintLabel = null!;

    /// <summary>The controls the hint teaches (<see cref="UiText"/> markup: keys as caps).</summary>
    private const string UniverseHintMarkup = "Click a world to orbit it · {F} frames it · the wheel zooms toward the pointer\n"
        + "{[} {]} the next world round its ring · {Z} up a level · {W}{A}{S}{D} or middle-drag pan\n"
        + "{Shift}: faster · {Esc}: back down · {F1}: every control · a haze holds folded worlds";
    private float _uniHintT = float.MaxValue, _uniHintFor;
    private int _uniHintOpenings;
    private readonly HashSet<UniverseControl> _uniHintUsed = [];

    /// <summary>The hint's text (probes).</summary>
    public string UniverseHintText => UiText.Plain(UniverseHintMarkup);

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
        // Viewport units: 700 wide, three lines of ≤ 80 characters at 15 px, centred, 150 below the top edge: clear of the
        // toolbar and of the "You are in" bar under it (at 64 the bar covered the hint's left half; PLANET-113).
        _uniHintPanel.OffsetLeft = -350; _uniHintPanel.OffsetRight = 350; _uniHintPanel.OffsetTop = 150; _uniHintPanel.OffsetBottom = 236;
        root.AddChild(_uniHintPanel);
        _uniHintLabel = MakeRichLine(UiTextStyle.Panel, new Color(0.88f, 0.93f, 1f), outline: 0);
        _uniHintLabel.AddThemeConstantOverride("line_separation", 3); // room for the caps' backgrounds between lines
        SetRich(_uniHintLabel, UniverseHintMarkup, UiTextStyle.Panel);
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
    private void ZoomTowardPointer(Vector2 screen, bool fast)
    {
        var id = PickUniverse(screen);
        if (id < 0 || !_uniIndex.TryGetValue(id, out var i)) return;
        var to = UniverseOrigin + _uniMotion[i].To;
        _uniFocusTarget = _uniFocusTarget.Lerp(to, OverviewControls.WheelTowardShareFor(fast));
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
