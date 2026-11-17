using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// Sky motion (PLANET-60, AD-64): the celestial sphere's extremely slow turn and the occasional faint shooting star,
/// written into the current world's sky material each frame (planet_sky.gdshader's <c>sky_turn</c> and <c>meteor_*</c>).
/// Owns the timing (rate, intervals, durations) and where a shooting star appears. Why: DJ asked for a sky that is a
/// little alive without being a show. What a change must preserve: the turn stays far below the speed at which a
/// still sky would read as the camera drifting (see <see cref="SkyTurnDegreesPerMinute"/>); a shooting star is rare,
/// short and faint, never during a launch's wind-up or the first seconds after a landing, and never in the overview;
/// the nebulae, twinkling stars, galaxies and the commit-graph constellation are kept (they turn with the sphere).
/// </summary>
public partial class PlanetWorld
{
    // ---- the turn
    /// <summary>
    /// The celestial sphere turns this fast, degrees per minute (a full turn in 6 hours; the real night sky turns
    /// 0.25°/min). At the walking camera's 62° view on a 900 px tall window a star moves ≈ 0.24 px/s at most: about
    /// 0.5 arcmin/s of visual angle at a desk, under the ≈ 1–2 arcmin/s at which slow motion is noticed against a fixed
    /// horizon, so standing still it never reads as the camera drifting; over a few minutes the stars and nebulae have
    /// visibly moved on (≈ 3° in 3 minutes), which is the point. Kept after judging (AD-64).
    /// </summary>
    public const float SkyTurnDegreesPerMinute = 1f;

    /// <summary>
    /// The axis the sky turns about, world frame: ≈ 49° from the arrival's zenith (+Y), so stars rise and set on a slant
    /// as in a real sky rather than sliding sideways the way a camera's yaw would move them.
    /// </summary>
    private static readonly Vector3 SkyTurnAxis = new Vector3(0.55f, 0.65f, -0.52f).Normalized();

    // ---- shooting stars
    /// <summary>Seconds between shooting stars: uniformly random in this range (≈ one a minute; most minutes have one, some none).</summary>
    public const float MeteorMinIntervalSeconds = 40f, MeteorMaxIntervalSeconds = 90f;

    /// <summary>A shooting star lasts this long, seconds (a glance's worth: it is over before you could turn to it).</summary>
    public const float MeteorSeconds = 0.8f;

    /// <summary>It travels this far across the sky in its life, degrees, trailing a streak of <see cref="MeteorTrailDegrees"/>.</summary>
    private const float MeteorArcDegrees = 14f, MeteorTrailDegrees = 6f;

    /// <summary>
    /// Peak brightness of its head, in the sky shader's units: a bright star's core is ≈ 1.6–2.0 (star_brightness 1.8),
    /// so a shooting star is no brighter than the brightest stars and stays under the glow threshold (1.1 after tone
    /// mapping) for most of its life: faint, never a flash.
    /// </summary>
    private const float MeteorStrength = 1.1f;

    /// <summary>No shooting star within this long after a landing or while travelling (the arrival is its own moment), seconds.</summary>
    private const float MeteorQuietAfterLandingSeconds = 10f;

    /// <summary>
    /// A shooting star starts in the upper part of the view, as fractions of the window: across 0.15–0.85 of its width
    /// and 0.08–0.4 of its height from the top (in view, rarely central), on open sky (a start whose line of sight meets
    /// the planet is drawn again, up to <see cref="MeteorPlacementTries"/> times, else skipped).
    /// </summary>
    private const float MeteorMinX = 0.15f, MeteorMaxX = 0.85f, MeteorMinY = 0.08f, MeteorMaxY = 0.4f;

    private const int MeteorPlacementTries = 6;

    /// <summary>It falls on a slant on screen: between this many degrees below the horizontal, left or right.</summary>
    private const float MeteorMinDipDegrees = 20f, MeteorMaxDipDegrees = 55f;

    private readonly RandomNumberGenerator _skyRng = new();
    private float _skyTurnRadians;
    private float _meteorAge = -1f, _nextMeteorIn = -1f, _sinceLandingSeconds;
    private Vector3 _meteorFrom = Vector3.Up, _meteorAlong = Vector3.Right;

    /// <summary>The sky's turn so far, degrees (probes).</summary>
    public float SkyTurnDegrees => Mathf.RadToDeg(_skyTurnRadians);

    /// <summary>Shooting stars shown so far this session (probes).</summary>
    public int MeteorsShown { get; private set; }

    /// <summary>A shooting star is crossing the sky now (probes).</summary>
    public bool MeteorShowing => _meteorAge >= 0;

    /// <summary>Where the current or last shooting star started, a world direction (probes).</summary>
    public Vector3 MeteorFrom => _meteorFrom;

    /// <summary>Advances the turn and the shooting stars, and writes them into the current sky material.</summary>
    private void UpdateSky(float dt)
    {
        _skyTurnRadians = Mathf.Wrap(_skyTurnRadians + Mathf.DegToRad(SkyTurnDegreesPerMinute / 60f) * dt, 0f, Mathf.Tau);
        _sinceLandingSeconds = Busy ? 0 : _sinceLandingSeconds + dt;
        if (_nextMeteorIn < 0) _nextMeteorIn = _skyRng.RandfRange(MeteorMinIntervalSeconds, MeteorMaxIntervalSeconds);
        if (_meteorAge >= 0)
        {
            _meteorAge += dt;
            if (_meteorAge > MeteorSeconds) _meteorAge = -1;
        }
        else if ((_nextMeteorIn -= dt) <= 0)
        {
            // Due: show one if this is a quiet moment, else wait for the next interval.
            if (_universe == null && _sinceLandingSeconds > MeteorQuietAfterLandingSeconds && !_meteorsPaused) StartMeteor();
            _nextMeteorIn = _skyRng.RandfRange(MeteorMinIntervalSeconds, MeteorMaxIntervalSeconds);
        }

        if (_worldEnv?.Environment?.Sky?.SkyMaterial is not ShaderMaterial m) return;
        m.SetShaderParameter("sky_turn", new Basis(SkyTurnAxis, _skyTurnRadians));
        m.SetShaderParameter("meteor_age", _meteorAge);
        if (_meteorAge < 0) return;
        m.SetShaderParameter("meteor_from", _meteorFrom);
        m.SetShaderParameter("meteor_along", _meteorAlong);
        m.SetShaderParameter("meteor_seconds", MeteorSeconds);
        m.SetShaderParameter("meteor_arc", Mathf.DegToRad(MeteorArcDegrees));
        m.SetShaderParameter("meteor_trail", Mathf.DegToRad(MeteorTrailDegrees));
        m.SetShaderParameter("meteor_strength", MeteorStrength);
    }

    /// <summary>Probes recording a clip pause the random shooting stars and show one when they choose.</summary>
    private bool _meteorsPaused;

    /// <summary>Pauses or resumes the random shooting stars (probes: a clip shows one at a chosen moment).</summary>
    public void PauseMeteors(bool paused) => _meteorsPaused = paused;

    /// <summary>
    /// Starts a shooting star now in the upper part of the view: <paramref name="xFraction"/> and
    /// <paramref name="yFraction"/> (0…1) pick where in that band, <paramref name="dipFraction"/> its slant,
    /// <paramref name="leftward"/> which way it falls; random when omitted (the app's own shooting stars). False when no
    /// start on open sky was found (the view is all ground).
    /// </summary>
    public bool StartMeteor(float? xFraction = null, float? yFraction = null, float? dipFraction = null, bool? leftward = null)
    {
        var size = GetViewport().GetVisibleRect().Size;
        var cam = _camera.GlobalPosition;
        for (var tries = 0; tries < MeteorPlacementTries; tries++)
        {
            var at = new Vector2(size.X * Mathf.Lerp(MeteorMinX, MeteorMaxX, xFraction ?? _skyRng.Randf()), size.Y * Mathf.Lerp(MeteorMinY, MeteorMaxY, yFraction ?? _skyRng.Randf()));
            var from = _camera.ProjectRayNormal(at);
            if (_current != null && RayPlanet(cam, from, _current.Comp.Shape.Radius + _current.Comp.Shape.HillAmplitude) != null) continue; // ground there
            // Its direction of travel: a slant down-left or down-right on screen, made a tangent of the sky at its start.
            var dip = Mathf.DegToRad(Mathf.Lerp(MeteorMinDipDegrees, MeteorMaxDipDegrees, dipFraction ?? _skyRng.Randf()));
            var sign = leftward ?? _skyRng.Randf() < 0.5f ? -1f : 1f;
            var next = _camera.ProjectRayNormal(at + new Vector2(sign * Mathf.Cos(dip), Mathf.Sin(dip)) * 40f);
            var along = next - from * next.Dot(from);
            if (along.LengthSquared() < 1e-10f) continue;
            _meteorFrom = from;
            _meteorAlong = along.Normalized();
            _meteorAge = 0;
            MeteorsShown++;
            return true;
        }

        return false;
    }

    /// <summary>[direct] Makes the next random shooting star due now (probes: it must not show in the overview or just after a landing).</summary>
    public void MeteorDueNow() => _nextMeteorIn = 0.001f;

    /// <summary>[direct] Turns the sky on by this many minutes' worth (probes: stills a few minutes apart without waiting).</summary>
    public void AdvanceSkyTurn(float minutes) =>
        _skyTurnRadians = Mathf.Wrap(_skyTurnRadians + Mathf.DegToRad(SkyTurnDegreesPerMinute * minutes), 0f, Mathf.Tau);
}
