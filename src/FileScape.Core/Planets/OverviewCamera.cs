using System.Numerics;

namespace FileScape.Core.Planets;

/// <summary>
/// The movement controls' rates in the overview and what Shift does to each (PLANET-110, AD-82). DJ (sixth pass): "Make
/// Shift accelerate Overview movement, consistent with faster walking. Choose appropriate acceleration for the different
/// movement controls." Walking runs at about twice the walking speed with Shift and turns 1.6 × faster; the Places
/// flight flies 3 × faster. Here:
/// <list type="bullet">
/// <item><b>Keys that travel</b> (WASD pan, E/Q up and down) and the <b>middle-drag pan</b>: × <see cref="FastPanFactor"/>,
/// the flight's Shift. Pan is how you cross a large universe, so it takes the largest gain.</item>
/// <item><b>← → orbit</b>: × <see cref="FastKeyOrbitFactor"/>, the walking camera's Shift turn, so the same keys turn alike
/// on the ground and in space.</item>
/// <item><b>The wheel</b>: a Shift notch counts as <see cref="FastWheelNotches"/> notches, both for the distance and for the
/// slide toward the pointed-at world; in-then-out still comes back to about where it was.</item>
/// <item><b>A left- or right-drag orbit</b> is not accelerated (× <see cref="FastDragOrbitFactor"/>): it is direct
/// manipulation (a 360 px drag already turns ≈ 103°), and a faster one only overshoots.</item>
/// </list>
/// Engine-free so the rates are tested (<c>OverviewCameraTests</c>); <c>PlanetWorld.Universe.cs</c> reads them. A change
/// must keep a Shift-free notch, pan and orbit exactly as before (the plain values are the 2026-11-11 tuning).
/// </summary>
public static class OverviewControls
{
    /// <summary>One wheel notch: in multiplies the camera distance by 0.85, out by 1.18 (≈ 1/0.85, so in-then-out comes back).</summary>
    public const float WheelZoomIn = 0.85f, WheelZoomOut = 1.18f;

    /// <summary>One wheel notch in over a world moves the orbit's centre this share of the way to it (PLANET-93): about five
    /// notches centre it. Higher dives faster but makes the view swing more.</summary>
    public const float WheelTowardShare = 0.3f;

    /// <summary>Shift on the keyboard pan (WASD, E/Q) and the middle-drag pan: this many times faster (the Places flight's ×3).</summary>
    public const float FastPanFactor = 3f;

    /// <summary>Shift on ← → orbit: this many times faster (the walking camera's Shift turn, <c>KeyLookFastFactor</c>; a separate choice).</summary>
    public const float FastKeyOrbitFactor = 1.6f;

    /// <summary>Shift on a drag orbit: unchanged (direct manipulation; see the class summary).</summary>
    public const float FastDragOrbitFactor = 1f;

    /// <summary>A wheel notch with Shift counts as this many notches.</summary>
    public const int FastWheelNotches = 2;

    /// <summary>The multiplier Shift (<paramref name="fast"/>) applies to <paramref name="control"/>; 1 without Shift.</summary>
    public static float SpeedFactor(OverviewMove control, bool fast) => !fast ? 1f : control switch
    {
        OverviewMove.KeyPan or OverviewMove.DragPan => FastPanFactor,
        OverviewMove.KeyOrbit => FastKeyOrbitFactor,
        _ => FastDragOrbitFactor,
    };

    /// <summary>What one wheel notch multiplies the camera distance by.</summary>
    public static float WheelDistanceFactor(bool zoomIn, bool fast)
    {
        var one = zoomIn ? WheelZoomIn : WheelZoomOut;
        return fast ? MathF.Pow(one, FastWheelNotches) : one; // the plain notch exactly as before
    }

    /// <summary>The share of the way one wheel notch in slides the orbit's centre toward the pointed-at world.</summary>
    public static float WheelTowardShareFor(bool fast) =>
        fast ? 1f - MathF.Pow(1f - WheelTowardShare, FastWheelNotches) : WheelTowardShare; // n notches leave (1 − share)^n of the way
}

/// <summary>The overview's movement controls, for <see cref="OverviewControls.SpeedFactor"/>.</summary>
public enum OverviewMove
{
    /// <summary>WASD and E/Q: the orbit's centre travels.</summary>
    KeyPan,

    /// <summary>← →: the camera turns round the orbit's centre.</summary>
    KeyOrbit,

    /// <summary>Middle-drag: the orbit's centre travels with the pointer.</summary>
    DragPan,

    /// <summary>Left- or right-drag: the camera turns round the orbit's centre.</summary>
    DragOrbit,
}

/// <summary>Where the overview camera looks and from how far: its orbit centre (metres, scene frame) and distance (metres, &gt; 0).</summary>
public readonly record struct OverviewView(Vector3 Focus, float DistanceMetres);

/// <summary>
/// A smooth zoom-and-pan between two overview views (van Wijk and Nuij, "Smooth and efficient zooming and panning",
/// 2003): the orbit centre moves along the straight line between the two foci while the distance follows the path that
/// keeps the move short in "perceived" terms, pulling back first when the two are far apart compared with the views'
/// sizes, so what you leave and where you go stay in view. Used for Esc's way back to where you stand (PLANET-111,
/// <see cref="OverviewReturn"/>).
/// <para>
/// <see cref="Length"/> is the path's length in its own dimensionless units (natural-log units of the view's size);
/// <see cref="At"/> takes a parameter 0…<see cref="Length"/>. Distances are treated as the view's width (the overview's
/// field of view is fixed, so they are proportional). Deterministic and engine-free.
/// </para>
/// </summary>
public sealed class ZoomPanPath
{
    /// <summary>The curvature ρ: √2, the value the paper found best in user trials (higher pulls back further).</summary>
    public const float Rho = 1.41421356f;

    private readonly OverviewView _from, _to;
    private readonly Vector3 _direction;
    private readonly double _w0, _r0, _sign;
    private readonly bool _zoomOnly;

    public ZoomPanPath(OverviewView from, OverviewView to)
    {
        _from = from;
        _to = to;
        _w0 = Math.Max(from.DistanceMetres, 1e-4);
        var w1 = Math.Max(to.DistanceMetres, 1e-4);
        var delta = to.Focus - from.Focus;
        var u1 = (double)delta.Length();
        const double rho = Rho;
        // Below a millionth of the views' size the pan is nothing: a pure zoom (the general formulas divide by u1).
        _zoomOnly = u1 < 1e-6 * Math.Max(_w0, w1);
        if (_zoomOnly)
        {
            _direction = Vector3.Zero;
            _sign = w1 >= _w0 ? 1 : -1;
            Length = (float)(Math.Abs(Math.Log(w1 / _w0)) / rho);
            return;
        }

        _direction = delta / (float)u1;
        // The paper's eq. 9–10: b0, b1 and r0, r1 (with u0 = 0), then S = (r1 − r0) / ρ.
        var rho2 = rho * rho;
        var rho4u2 = rho2 * rho2 * u1 * u1;
        var b0 = (w1 * w1 - _w0 * _w0 + rho4u2) / (2 * _w0 * rho2 * u1);
        var b1 = (w1 * w1 - _w0 * _w0 - rho4u2) / (2 * w1 * rho2 * u1);
        _r0 = Math.Log(-b0 + Math.Sqrt(b0 * b0 + 1));
        var r1 = Math.Log(-b1 + Math.Sqrt(b1 * b1 + 1));
        Length = (float)Math.Max(0, (r1 - _r0) / rho);
    }

    /// <summary>The path's length, dimensionless (≥ 0).</summary>
    public float Length { get; }

    /// <summary>The view at <paramref name="s"/> along the path (clamped to 0…<see cref="Length"/>).</summary>
    public OverviewView At(float s)
    {
        if (Length <= 0) return s > 0 ? _to : _from;
        if (s <= 0) return _from;
        if (s >= Length) return _to;
        const double rho = Rho;
        if (_zoomOnly) return new OverviewView(_from.Focus, (float)(_w0 * Math.Exp(_sign * rho * s)));
        // Eq. 8: u(s) = w0/ρ² (cosh r0 tanh(ρs + r0) − sinh r0), w(s) = w0 cosh r0 / cosh(ρs + r0).
        var x = rho * s + _r0;
        var u = _w0 / (rho * rho) * (Math.Cosh(_r0) * Math.Tanh(x) - Math.Sinh(_r0));
        var w = _w0 * Math.Cosh(_r0) / Math.Cosh(x);
        return new OverviewView(_from.Focus + _direction * (float)u, (float)w);
    }
}

/// <summary>
/// Esc (or T) from the overview back to where you stand (PLANET-111, AD-82). DJ (sixth pass): "Currently the camera slides
/// across space and then cuts to the surface. Let the movement lead into the place where I arrive." Before, the overview
/// was removed at once and the camera's pose was blended in 1.1 s straight from the overview (40 km above the planet,
/// whose far clip is 4 km) to the walking camera: about 0.9 s of empty sky, then the planet popped in for the last
/// fraction of the blend.
/// <para>
/// Now the way back is two continuous parts. <b>In the overview</b>, the camera flies along a <see cref="ZoomPanPath"/>
/// from wherever it is to <see cref="CloseRadii"/> body radii from your world (the opening's own starting point), the
/// map still drawn, eased in and out (<see cref="Ease"/>), over <see cref="Duration"/>; a soft light rises over its last part
/// (<see cref="FlashLevel"/>). <b>On the planet</b>, the camera starts where the overview left it, mapped onto the real
/// world (<see cref="SurfaceStart"/>): the same distance in radii, looking at the centre, so your world is the same size
/// and in the same place on screen; it sits above and behind the traveller, so the glide that follows (the walking
/// camera's blend, <see cref="SurfaceBlendSeconds"/>) comes down behind the character and settles into the usual view.
/// Both parts start and end at rest, so the junction is continuous in position and speed.
/// </para>
/// Engine-free (<c>OverviewCameraTests</c>); <c>PlanetWorld.Universe.cs</c> runs it. A change must keep the two parts
/// meeting exactly (the same apparent size and screen position of your world at the hand-over) and the start eye
/// outside the planet with a clear line down to the walking camera.
/// </summary>
public static class OverviewReturn
{
    /// <summary>The overview part ends this many body radii from your world's centre (the opening starts there too).</summary>
    public const float CloseRadii = 3f;

    /// <summary>The overview part's speed along its path, path units per second (a 100 × zoom is ≈ 3.3 units: ≈ 1.6 s). By eye.</summary>
    public const float PathUnitsPerSecond = 2f;

    /// <summary>The overview part takes at least this long, seconds (a short way back still reads as a move), and at most
    /// <see cref="MaxSeconds"/> (a long one never keeps you waiting). By eye.</summary>
    public const float MinSeconds = 0.9f, MaxSeconds = 1.8f;

    /// <summary>A second Esc or T during the overview part finishes it in this long, seconds (a hurry, never a cut).</summary>
    public const float HurriedSeconds = 0.35f;

    /// <summary>The glide on the planet, from the hand-over to the walking camera, seconds (the establishing shot takes 2.6; a
    /// landing's blend 0.6). By eye.</summary>
    public const float SurfaceBlendSeconds = 1.5f;

    /// <summary>The hand-over eye's angle from straight above the traveller, degrees, tilted back behind it (so the glide
    /// comes down behind the character, as the establishing shot does).</summary>
    public const float BehindFromVerticalDegrees = 25f;

    /// <summary>The soft light's peak level (opacity ≈ level² × 0.85: ≈ 26 %): enough to cover the overview's world turning
    /// into the real one, never a white-out.</summary>
    public const float FlashPeakLevel = 0.55f;

    /// <summary>The light starts rising at this share of the overview part (0…1).</summary>
    public const float FlashFromProgress = 0.6f;

    /// <summary>How long the overview part takes for <paramref name="path"/>.</summary>
    public static TimeSpan Duration(ZoomPanPath path) =>
        Units.Durations.FromSeconds(Math.Clamp(path.Length / PathUnitsPerSecond, MinSeconds, MaxSeconds));

    /// <summary>Progress 0…1 → path share 0…1, eased in and out (smoothstep): starts and ends at rest.</summary>
    public static float Ease(float progress)
    {
        var u = Math.Clamp(progress, 0f, 1f);
        return u * u * (3f - 2f * u);
    }

    /// <summary>The soft light's level at <paramref name="progress"/> (0…1) through the overview part.</summary>
    public static float FlashLevel(float progress) =>
        progress <= FlashFromProgress ? 0f : FlashPeakLevel * Ease((progress - FlashFromProgress) / (1f - FlashFromProgress));

    /// <summary>
    /// The hand-over pose on the planet (planet frame: its centre at the origin): an eye <paramref name="closeRadii"/> ×
    /// <paramref name="planetRadius"/> from the centre, <see cref="BehindFromVerticalDegrees"/> back from above the
    /// traveller (<paramref name="up"/> is the traveller's up, <paramref name="facing"/> its facing, both unit and
    /// perpendicular), looking at the centre, with the screen's up toward the facing.
    /// </summary>
    public static (Vector3 Eye, Vector3 Forward, Vector3 ScreenUp) SurfaceStart(Vector3 up, Vector3 facing, float planetRadius, float closeRadii = CloseRadii)
    {
        var tilt = BehindFromVerticalDegrees * MathF.PI / 180f;
        var dir = Vector3.Normalize(up * MathF.Cos(tilt) - facing * MathF.Sin(tilt));
        var forward = -dir;
        // The traveller's up made perpendicular to the view: it points up the screen and leans toward the facing.
        var screenUp = Vector3.Normalize(up - Vector3.Dot(up, forward) * forward);
        return (dir * (closeRadii * planetRadius), forward, screenUp);
    }

    /// <summary>The angular radius of a sphere of <paramref name="radius"/> seen from <paramref name="distance"/> (both metres), radians.</summary>
    public static float ApparentRadius(float radius, float distance) => MathF.Asin(Math.Clamp(radius / distance, 0f, 1f));
}
