using Godot;

namespace FileScape.App.Planets;

/// <summary>How the walkthrough's target is shown on screen this frame (PLANET-38, AD-55).</summary>
public enum TutorialCueMode
{
    /// <summary>No target (no lesson, a lesson with nothing to walk to, between steps, travelling, the overview).</summary>
    None,

    /// <summary>The spot is in view: a chevron pinned above it.</summary>
    OnScreen,

    /// <summary>Ahead, but hidden by the planet's curve: a chevron where the horizon is in that direction.</summary>
    Horizon,

    /// <summary>Out of view (to the side or behind): an arrow at the screen's edge pointing the way to turn.</summary>
    Edge,
}

/// <summary>How close you are to the walkthrough's target: the cue changes as you approach (AD-55).</summary>
public enum TutorialCuePhase
{
    /// <summary>More than the approach distance from the spot's edge.</summary>
    Far,

    /// <summary>Within the approach distance: the ring tightens and brightens, the action appears.</summary>
    Near,

    /// <summary>On the spot (within its reach): what to do now.</summary>
    Here,
}

/// <summary>
/// The walkthrough's target cue, frozen for one frame: what the screen shows and where (probes and the real-input
/// probe's state file read it). <see cref="Anchor"/> is in viewport units (docs/units.md); <see cref="AngleDegrees"/> is
/// the screen direction the edge arrow points, 0 = right, positive clockwise (screen y grows downwards), so 90 = down
/// and 180 = left; a pinned chevron points down (90). <see cref="OverHorizon"/>: the spot itself is behind the planet's
/// curve (the chevron says "over the horizon").
/// </summary>
public readonly record struct TutorialCueState(
    TutorialCueMode Mode,
    Vector2 Anchor,
    float AngleDegrees,
    float DistanceMetres,
    float BearingDegrees,
    TutorialCuePhase Phase,
    string Action,
    bool OverHorizon = false)
{
    public static readonly TutorialCueState Hidden = new(TutorialCueMode.None, Vector2.Zero, 0, -1, 0, TutorialCuePhase.Far, "");
}

/// <summary>
/// Draws the walkthrough's screen cue (PLANET-38, AD-55): a bobbing lime chevron pinned over the spot to go to, the
/// same at the horizon when the planet's curve hides it, or a pulsing arrow at the screen's edge pointing the way to
/// turn when it is out of view or behind you; near the spot a lime pill says what to do there. It has a look of its own
/// (lime, filled shapes, a dark outline, no panel or icon) so it cannot be mistaken for a destination sign (dark panels
/// with an icon and white text, SignLayer). It only draws: <c>PlanetWorld.Tutorial.cs</c> decides the mode and position
/// every frame and hands them over with <see cref="Show"/>. It never takes the mouse.
/// <para>Sizes are viewport units; they are kept small so the world stays in view (the chevron ≈ 40 × 30, the arrow ≈ 64 long).</para>
/// </summary>
public partial class TutorialCue : Control
{
    /// <summary>The walkthrough's own colour: a lime green nothing else in the planets uses (the turn hint text shares it).</summary>
    public static readonly Color CueColor = new(0.55f, 1f, 0.3f);

    /// <summary>The dark outline round every shape and word, so the cue reads over any sky or ground.</summary>
    private static readonly Color OutlineColor = new(0.03f, 0.08f, 0.02f, 0.95f);

    /// <summary>The pill's text: dark on lime.</summary>
    private static readonly Color PillTextColor = new(0.05f, 0.12f, 0.03f);

    /// <summary>The chevron bobs this far up and down, viewport units, at <see cref="BobHz"/>: motion draws the eye without flashing.</summary>
    private const float BobPixels = 6f;

    /// <summary>The chevron's bob and the arrow's nudge, cycles per second (rationale not recorded beyond "calm, not urgent").</summary>
    private const float BobHz = 0.9f;

    /// <summary>The edge arrow's pulse, cycles per second, and how much it grows at the peak (fraction of its size).</summary>
    private const float PulseHz = 1.2f, PulseGrowth = 0.12f;

    /// <summary>The edge arrow nudges this far along its direction, viewport units ("this way").</summary>
    private const float NudgePixels = 7f;

    /// <summary>Font sizes, pixels: the distance under a chevron or an arrow, and the action pill.</summary>
    private const int DistanceFontSize = 15, PillFontSize = 16;

    private TutorialCueState _state = TutorialCueState.Hidden;
    private float _time;

    public TutorialCue()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Shows this frame's cue (call every frame while the walkthrough has a target; <see cref="TutorialCueState.Hidden"/> clears it).</summary>
    public void Show(TutorialCueState state, float dtSeconds)
    {
        _state = state;
        _time += dtSeconds;
        Visible = state.Mode != TutorialCueMode.None;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        var bob = BobPixels * Mathf.Sin(_time * Mathf.Tau * BobHz);
        switch (_state.Mode)
        {
            case TutorialCueMode.OnScreen:
            case TutorialCueMode.Horizon:
            {
                var near = _state.Phase != TutorialCuePhase.Far;
                var tip = _state.Anchor + new Vector2(0, bob);
                // A bigger chevron when you are close: the cue changes as you approach. (A halo disc behind it was tried
                // and read as a grey smudge over the pink and violet skies; the dark outline is enough.)
                var w = near ? 28f : 22f;
                var h = near ? 36f : 30f;
                Chevron(tip, w, h, 1f);
                // Far: how far, and whether it is over the horizon; near: what to do there (the pill).
                var label = _state.Phase != TutorialCuePhase.Far ? "" : _state.OverHorizon ? $"{_state.DistanceMetres:0} m · over the horizon" : $"{_state.DistanceMetres:0} m";
                if (label.Length > 0) Text(font, label, tip + new Vector2(0, -h - 10), DistanceFontSize, CueColor);
                if (near && _state.Action.Length > 0) Pill(font, _state.Action, tip + new Vector2(0, -h - 8));
                break;
            }
            case TutorialCueMode.Edge:
            {
                var dir = Vector2.FromAngle(Mathf.DegToRad(_state.AngleDegrees));
                var pulse = 1f + PulseGrowth * (0.5f + 0.5f * Mathf.Sin(_time * Mathf.Tau * PulseHz));
                var at = _state.Anchor + dir * (NudgePixels * Mathf.Sin(_time * Mathf.Tau * BobHz));
                DrawCircle(at, 46f * pulse, new Color(0.02f, 0.05f, 0.02f, 0.35f));
                Arrow(at, dir, pulse);
                var behind = MathF.Abs(_state.BearingDegrees) > 120f;
                var words = behind ? $"behind you · {_state.DistanceMetres:0} m" : $"{_state.DistanceMetres:0} m";
                Text(font, words, at + new Vector2(0, 66f), DistanceFontSize, CueColor);
                break;
            }
        }
    }

    /// <summary>A filled chevron pointing down with its tip at <paramref name="tip"/>: half-width <paramref name="w"/>, height <paramref name="h"/>.</summary>
    private void Chevron(Vector2 tip, float w, float h, float alpha)
    {
        // A broad "V" with a shallow notch (mostly filled, so it reads at a glance): tip, upper right, notch, upper left.
        Vector2[] pts = [tip, tip + new Vector2(w, -h), tip + new Vector2(0, -h * 0.68f), tip + new Vector2(-w, -h)];
        Outline(pts, 3f);
        DrawColoredPolygon(pts, CueColor with { A = alpha });
    }

    /// <summary>A filled arrow (head and shaft) centred on <paramref name="c"/>, pointing along <paramref name="dir"/>, scaled by <paramref name="scale"/>.</summary>
    private void Arrow(Vector2 c, Vector2 dir, float scale)
    {
        var n = new Vector2(-dir.Y, dir.X);
        Vector2 P(float along, float across) => c + (dir * along + n * across) * scale;
        // Overall 64 long: a 34-long head 44 wide, a 30-long shaft 16 wide.
        Vector2[] pts = [P(32, 0), P(-2, 22), P(-2, 8), P(-32, 8), P(-32, -8), P(-2, -8), P(-2, -22)];
        Outline(pts, 4f);
        DrawColoredPolygon(pts, CueColor);
    }

    private void Outline(Vector2[] pts, float width)
    {
        var loop = new Vector2[pts.Length + 1];
        pts.CopyTo(loop, 0);
        loop[^1] = pts[0];
        DrawPolyline(loop, OutlineColor, width * 2f, antialiased: true);
    }

    /// <summary>Outlined text centred horizontally on <paramref name="at"/> (its baseline).</summary>
    private void Text(Font font, string text, Vector2 at, int size, Color color)
    {
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        // Kept inside the view (8 units from either side): an arrow at the edge has its words beside it, not cut off.
        var p = new Vector2(Mathf.Clamp(at.X - width / 2, 8f, Size.X - width - 8f), at.Y);
        DrawStringOutline(font, p, text, HorizontalAlignment.Left, -1, size, 5, OutlineColor);
        DrawString(font, p, text, HorizontalAlignment.Left, -1, size, color);
    }

    /// <summary>The action pill: lime, rounded, dark text, its bottom centre at <paramref name="bottom"/>. Nothing else in FileScape looks like it.</summary>
    private void Pill(Font font, string text, Vector2 bottom)
    {
        var size = font.GetStringSize(text, HorizontalAlignment.Left, -1, PillFontSize);
        var box = new Rect2(bottom.X - size.X / 2 - 12, bottom.Y - size.Y - 8, size.X + 24, size.Y + 8);
        var style = new StyleBoxFlat { BgColor = CueColor, BorderColor = OutlineColor };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(12);
        DrawStyleBox(style, box);
        DrawString(font, new Vector2(box.Position.X + 12, box.End.Y - 4 - font.GetDescent(PillFontSize)), text, HorizontalAlignment.Left, -1, PillFontSize, PillTextColor);
    }
}
