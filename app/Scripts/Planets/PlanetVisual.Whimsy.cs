using FileScape.Core.Planets;
using Godot;
using CoreDecoration = FileScape.Core.Planets.DecorationKind;

namespace FileScape.App.Planets;

/// <summary>
/// What the lighthouse and the answering scenery look like when they act (PLANET-74, PLANET-75, AD-70); when they act is
/// <c>PlanetWorld.Whimsy.cs</c>. <b>The lighthouse</b> (Hearth's landmark): two soft beams from the lamp room sweep round
/// one and a half turns over <see cref="BeamSeconds"/> and fade, and a light at the lamp flares. <b>Scenery</b>: a
/// mushroom squashes and springs back, a crystal and a lamp sway a little; each flares with a light of its own colour.
/// The pieces stay in their MultiMesh (one slot each moves, nothing is rebuilt). <b>The shimmer</b>: on the nearest
/// answering piece the world names (<see cref="SceneryCue"/>), a soft breathing light and two little glowing motes that
/// circle it like fireflies — the cue that it answers (the light alone was invisible on a lantern at night, 2026-11-19).
/// What a change must preserve: every effect is brief and ends where it began (the piece back in its built place, the
/// beam hidden); nothing here touches collision.
/// </summary>
public partial class PlanetVisual
{
    // ---- the lighthouse's beam (by eye, 2026-11-19)
    /// <summary>The beam pivots at the lamp room's middle, mesh metres (PlanetArt.Lighthouse: the lamp room is 7.15–8.45 m).</summary>
    private const float BeamPivotMetres = 7.8f;

    /// <summary>Each beam is a cone this long, metres, from this radius at the lamp to this one at its far end.</summary>
    private const float BeamLengthMetres = 16f, BeamNearRadiusMetres = 0.3f, BeamFarRadiusMetres = 2.2f;

    /// <summary>The beams tip down this much, radians (≈ 8°), so on a small world they sweep the ground's horizon, not empty sky.</summary>
    private const float BeamTiltRadians = 0.14f;

    /// <summary>A lighting lasts this long, seconds; the beams sweep this many turns in it (easing out), fading in over the first <see cref="BeamFadeInSeconds"/> and out over the last <see cref="BeamFadeOutSeconds"/>.</summary>
    private const float BeamSeconds = 3.2f, BeamTurns = 1.5f, BeamFadeInSeconds = 0.25f, BeamFadeOutSeconds = 0.9f;

    /// <summary>The beams' opacity at the lamp at full (additive, fading along the beam: <c>beam.gdshader</c>), so a soft haze, not a solid cone.</summary>
    private const float BeamAlpha = 0.55f;

    /// <summary>The beam's colour: the lamp's glow lifted this far towards white (the plain glow read as a tan solid on the night sky, 2026-11-19).</summary>
    private const float BeamWhiten = 0.45f;

    /// <summary>The lamp's flare: a light of this energy and range, metres, falling over the lighting.</summary>
    private const float LampFlareEnergy = 4f, LampFlareRangeMetres = 10f;

    private Node3D? _beamPivot;
    private ShaderMaterial? _beamMaterial;
    private OmniLight3D? _lampLight;
    private float _beamT = -1; // seconds since it was lit, or −1

    /// <summary>The lighthouse's beam is sweeping now (probes).</summary>
    public bool LighthouseBeam => _beamT >= 0;

    // ---- scenery
    /// <summary>A reply's motion lasts this long, seconds (pleasant to repeat at once).</summary>
    private const float SceneryPlaySeconds = 0.9f;

    /// <summary>A mushroom's squash: this deep at first (a fraction of its height), springing at this angular rate (radians per second, ≈ 2.9 Hz) and settling with this time constant, seconds.</summary>
    private const float MushroomSquash = 0.32f, MushroomSpringRadiansPerSecond = 18f, MushroomSettleSeconds = 0.22f;

    /// <summary>A crystal's or lamp's sway: this peak angle (radians, ≈ 7°) at this angular rate (radians per second, ≈ 2.2 Hz), dying away linearly.</summary>
    private const float ScenerySwayRadians = 0.12f, ScenerySwayRadiansPerSecond = 14f;

    /// <summary>The reply's flare: a light of this energy and range, metres, falling over the reply.</summary>
    private const float SceneryFlareEnergy = 3f, SceneryLightRangeMetres = 3.5f;

    /// <summary>The shimmer on the nearest answering piece: its energy breathes between these at this angular rate (radians per second, a breath ≈ 2.6 s).</summary>
    private const float ShimmerLowEnergy = 0.25f, ShimmerHighEnergy = 0.7f, ShimmerRadiansPerSecond = 2.4f;

    /// <summary>The shimmer fades in at this rate (energy share per second) when it moves to another piece.</summary>
    private const float ShimmerFadeInPerSecond = 2f;

    /// <summary>The motes: this radius, metres, circling this far from the piece's axis at this angular rate (radians per second, a lap ≈ 3.9 s), bobbing this high, metres.</summary>
    private const float MoteRadiusMetres = 0.09f, MoteOrbitMetres = 0.5f, MoteRadiansPerSecond = 1.6f, MoteBobMetres = 0.15f;

    /// <summary>The motes' glow energy (the launcher hearts' is 2.4).</summary>
    private const float MoteEnergy = 3f;

    private MeshInstance3D[] _motes = [];

    /// <summary>The answering pieces: index in the composition's decorations → their MultiMesh, slot and built transform.</summary>
    private readonly Dictionary<int, (MultiMesh Multi, int Slot, Transform3D Built)> _scenery = [];

    /// <summary>Pieces answering now: index → seconds since.</summary>
    private readonly Dictionary<int, float> _sceneryPlays = [];

    private OmniLight3D? _sceneryLight;
    private int _sceneryLightOn = -1; // the piece the light is on now
    private float _shimmer; // 0..1: the shimmer's fade-in on its current piece

    /// <summary>The kinds that answer a twirl (AD-70; the same three as <c>PlanetWorld.VoiceOf</c>).</summary>
    private static bool Answers(CoreDecoration k) => k is CoreDecoration.Mushroom or CoreDecoration.Crystal or CoreDecoration.Lamp;

    /// <summary>The nearest answering piece to shimmer (its decoration index), or −1 (the world sets it every focus update).</summary>
    public int SceneryCue { get; set; } = -1;

    /// <summary>Some piece is answering now (probes).</summary>
    public bool SceneryPlaying => _sceneryPlays.Count > 0;

    /// <summary>The lighthouse's beams and lamp light, hidden, as children of its landmark (so they stand and turn with it).</summary>
    private void BuildLighthouseBeam(Node3D landmark)
    {
        // The selection spotlight's shader (an existing asset): additive, unshaded, bright at the mesh's bottom and fading
        // towards its top, for a unit-height mesh.
        _beamMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/beam.gdshader") };
        _beamMaterial.SetShaderParameter("color", new Color(BeamColor, 0));
        _beamPivot = new Node3D { Name = "LighthouseBeam", Position = new Vector3(0, BeamPivotMetres, 0), Visible = false };
        var cone = new CylinderMesh { TopRadius = BeamFarRadiusMetres, BottomRadius = BeamNearRadiusMetres, Height = 1, RadialSegments = 16, Rings = 1, CapTop = false, CapBottom = false };
        for (var k = 0; k < 2; k++)
        {
            // The unit cylinder's axis is Y, its narrow bright bottom at −½: stretched to the beam's length along Y, turned
            // −90° about Z (Y onto +X) and moved half its length along +X, it starts at the lamp and widens outwards; the
            // second beam faces the other way.
            var holder = new Node3D { Basis = new Basis(Vector3.Up, k * Mathf.Pi) * new Basis(Vector3.Back, -BeamTiltRadians) };
            holder.AddChild(new MeshInstance3D
            {
                Mesh = cone,
                MaterialOverride = _beamMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Transform = new Transform3D(new Basis(Vector3.Back, -Mathf.Pi / 2) * Basis.FromScale(new Vector3(1, BeamLengthMetres, 1)), new Vector3(BeamLengthMetres / 2, 0, 0)),
            });
            _beamPivot.AddChild(holder);
        }

        landmark.AddChild(_beamPivot);
        _lampLight = new OmniLight3D { Name = "LampLight", LightColor = _pal.Glow, LightEnergy = 0, OmniRange = LampFlareRangeMetres, Position = new Vector3(0, BeamPivotMetres, 0) };
        landmark.AddChild(_lampLight);
    }

    /// <summary>The beam's colour (see <see cref="BeamWhiten"/>).</summary>
    private Color BeamColor => _pal.Glow.Lerp(Colors.White, BeamWhiten);

    /// <summary>The lighthouse is lit: the beams sweep round from wherever they are and the lamp flares (a lighting while one runs starts afresh).</summary>
    public void SweepLighthouse()
    {
        if (_beamPivot == null) return;
        _beamT = 0;
        _beamPivot.Visible = true;
    }

    /// <summary>The piece <paramref name="index"/> answers: it moves and its light flares (a reply while one runs starts afresh).</summary>
    public void PlayScenery(int index)
    {
        if (!_scenery.ContainsKey(index)) return;
        _sceneryPlays[index] = 0;
    }

    /// <summary>Per frame: the beam's sweep, each answering piece's motion, and the one light (a reply's flare, else the shimmer).</summary>
    private void AnimateWhimsy(float dt)
    {
        if (_beamPivot != null && _beamT >= 0)
        {
            _beamT += dt;
            if (_beamT >= BeamSeconds)
            {
                _beamT = -1;
                _beamPivot.Visible = false;
                _lampLight!.LightEnergy = 0;
            }
            else
            {
                var u = _beamT / BeamSeconds;
                _beamPivot.Rotation = new Vector3(0, BeamTurns * Mathf.Tau * (1 - (1 - u) * (1 - u)), 0); // eases out: quick at first, settling
                var fade = MathF.Min(1, _beamT / BeamFadeInSeconds) * MathF.Min(1, (BeamSeconds - _beamT) / BeamFadeOutSeconds);
                _beamMaterial!.SetShaderParameter("color", new Color(BeamColor, BeamAlpha * fade));
                _lampLight!.LightEnergy = LampFlareEnergy * fade;
            }
        }

        var flareOn = -1;
        var flare = 0f;
        foreach (var index in _sceneryPlays.Keys.ToList())
        {
            var t = _sceneryPlays[index] + dt;
            var (multi, slot, built) = _scenery[index];
            if (t >= SceneryPlaySeconds)
            {
                _sceneryPlays.Remove(index);
                multi.SetInstanceTransform(slot, built); // back exactly where it was built
                continue;
            }

            _sceneryPlays[index] = t;
            var kind = _comp.Decorations[index].Kind;
            if (kind == CoreDecoration.Mushroom)
            {
                var squash = MushroomSquash * MathF.Exp(-t / MushroomSettleSeconds) * MathF.Cos(t * MushroomSpringRadiansPerSecond);
                var y = 1 - squash;
                var xz = 1 / MathF.Sqrt(MathF.Max(0.3f, y)); // keep its volume: squashed wider, stretched thinner
                multi.SetInstanceTransform(slot, new Transform3D(built.Basis * Basis.FromScale(new Vector3(xz, y, xz)), built.Origin));
            }
            else
            {
                var sway = MathF.Sin(t * ScenerySwayRadiansPerSecond) * ScenerySwayRadians * (1 - t / SceneryPlaySeconds);
                multi.SetInstanceTransform(slot, new Transform3D(built.Basis * new Basis(Vector3.Forward, sway), built.Origin));
            }

            var f = 1 - t / SceneryPlaySeconds;
            if (f * f > flare) { flare = f * f; flareOn = index; }
        }

        AnimateShimmer(dt, flareOn, flare);
    }

    /// <summary>The one scenery light: on a flaring piece at its flare, else breathing softly on <see cref="SceneryCue"/>, else off.</summary>
    private void AnimateShimmer(float dt, int flareOn, float flare)
    {
        var on = flareOn >= 0 ? flareOn : SceneryCue;
        if (on < 0 || !_scenery.ContainsKey(on))
        {
            if (_sceneryLight != null) _sceneryLight.LightEnergy = 0;
            foreach (var m in _motes) m.Visible = false;
            _sceneryLightOn = -1;
            return;
        }

        if (_sceneryLight == null)
        {
            _sceneryLight = new OmniLight3D { Name = "SceneryLight", OmniRange = SceneryLightRangeMetres, LightEnergy = 0, ShadowEnabled = false };
            AddChild(_sceneryLight);
            var mote = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 8, Rings = 4 };
            _motes = [new MeshInstance3D { Mesh = mote, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }, new MeshInstance3D { Mesh = mote, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }];
            foreach (var m in _motes) AddChild(m);
        }

        var d = _comp.Decorations[on];
        if (on != _sceneryLightOn)
        {
            _sceneryLightOn = on;
            _shimmer = 0;
            var built = _scenery[on].Built;
            _sceneryLight.Position = built.Origin + built.Basis.Y * LightHeight(d.Kind); // Basis.Y carries the piece's scale
            _sceneryLight.LightColor = d.Kind switch
            {
                CoreDecoration.Mushroom => new Color(1f, 0.78f, 0.9f),
                CoreDecoration.Crystal => new Color(0.7f, 0.92f, 1f),
                _ => _pal.Glow,
            };
            foreach (var m in _motes) m.MaterialOverride = PlanetArt.GlowMaterial(_sceneryLight.LightColor, MoteEnergy);
        }

        _shimmer = MathF.Min(1, _shimmer + dt * ShimmerFadeInPerSecond);
        var breath = ShimmerLowEnergy + (ShimmerHighEnergy - ShimmerLowEnergy) * (0.5f + 0.5f * MathF.Sin(_time * ShimmerRadiansPerSecond));
        _sceneryLight.LightEnergy = MathF.Max(on == flareOn ? SceneryFlareEnergy * flare : 0, on == SceneryCue ? breath * _shimmer : 0);

        // The motes circle the piece at its light's height while it is the cue (and while it answers).
        var b = _scenery[on].Built;
        var up = b.Basis.Y.Normalized();
        var ax = b.Basis.X.Normalized();
        var az = b.Basis.Z.Normalized();
        var centre = b.Origin + b.Basis.Y * LightHeight(d.Kind);
        for (var k = 0; k < _motes.Length; k++)
        {
            var a = _time * MoteRadiansPerSecond + k * Mathf.Pi;
            var size = MoteRadiusMetres * (on == SceneryCue ? _shimmer : 1);
            _motes[k].Visible = size > 0.005f;
            _motes[k].Transform = new Transform3D(Basis.FromScale(new Vector3(size, size, size)), centre + (ax * MathF.Cos(a) + az * MathF.Sin(a)) * MoteOrbitMetres + up * (MoteBobMetres * MathF.Sin(2 * a + k)));
        }
    }

    /// <summary>Where a piece's light sits over its base, mesh metres: a mushroom's cap, a crystal's middle, a lantern's lamp.</summary>
    private static float LightHeight(CoreDecoration k) => k switch
    {
        CoreDecoration.Mushroom => 0.6f,
        CoreDecoration.Crystal => 0.8f,
        _ => 2.0f,
    };
}
