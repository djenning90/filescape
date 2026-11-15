using FileScape.App.Ui;
using FileScape.App.Visual;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// The walkthrough's hooks into the planet (PLANET-33, AD-54): a visible target in the world — a column of light with a
/// bobbing ring and a pinned name, standing where the current lesson wants you to go — and the few read-only facts the
/// lesson checks (how far the camera has turned, how far you are from a spot, which launcher is which). The lessons
/// themselves (what to say, when a step is done) live in <c>Main.Tutorial.cs</c>; this file only draws and measures.
/// </summary>
public partial class PlanetWorld
{
    private Node3D? _beacon;
    private MeshInstance3D? _beaconRing;
    private NVector3? _beaconDir;
    private float _beaconLift;
    private string _beaconLabel = "";
    private float _beaconTime;

    /// <summary>How tall the beacon's column of light stands, metres: visible over the curve of a small world.</summary>
    private const float BeaconHeightMetres = 9f;

    /// <summary>The beacon's colour: the walkthrough's own warm gold, distinct from selection rings and launchers.</summary>
    private static readonly Color BeaconColor = new(1f, 0.82f, 0.35f);

    /// <summary>
    /// Shows the walkthrough's target at a surface direction on the current planet (<paramref name="liftMetres"/> above the
    /// ground, e.g. on a pad or a deck), named <paramref name="label"/>; null hides it.
    /// </summary>
    public void SetTutorialTarget(NVector3? surfaceDirection, string label = "", float liftMetres = 0f)
    {
        _beaconDir = surfaceDirection;
        _beaconLabel = label;
        _beaconLift = liftMetres;
        if (surfaceDirection == null || _current == null)
        {
            _beacon?.QueueFree();
            _beacon = null;
            _beaconRing = null;
            return;
        }

        if (_beacon == null)
        {
            _beacon = new Node3D { Name = "TutorialBeacon" };
            AddChild(_beacon);
            var glow = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = BeaconColor with { A = 0.28f },
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            };
            _beacon.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.7f, Height = BeaconHeightMetres, RadialSegments = 16, Rings = 1, CapTop = false, CapBottom = false },
                Position = new Godot.Vector3(0, BeaconHeightMetres / 2, 0),
                MaterialOverride = glow,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            _beaconRing = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = 0.9f, OuterRadius = 1.15f, Rings = 24, RingSegments = 8 },
                MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = BeaconColor, EmissionEnabled = true, Emission = BeaconColor, EmissionEnergyMultiplier = 2f },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _beacon.AddChild(_beaconRing);
        }

        PlaceBeacon();
    }

    /// <summary>Re-stands the beacon on the current planet (after a rebuild or a move of the target).</summary>
    private void PlaceBeacon()
    {
        if (_beacon == null || _beaconDir is not { } dir || _current == null) return;
        _beacon.Transform = PlanetVisual.Stand(_current.Comp.Surface, dir, PlanetFrame.North(dir), 1f, height: _beaconLift, sink: 0f);
    }

    /// <summary>Per frame: the ring bobs and turns so the target catches the eye; the name is added with the other signs.</summary>
    private void AnimateBeacon(float dt)
    {
        if (_beacon == null || _beaconRing == null) return;
        _beaconTime += dt;
        // A slow bob (≈ 0.8 Hz, ±0.35 m) and a turn: movement draws the eye without flashing.
        _beaconRing.Position = new Godot.Vector3(0, 1.2f + 0.35f * Mathf.Sin(_beaconTime * 5f), 0);
        _beaconRing.RotationDegrees = new Godot.Vector3(0, _beaconTime * 60f, 0);
    }

    /// <summary>The beacon's name on screen (with the other signs, so it is pinned, never overlapped by them).</summary>
    private void AddBeaconSign()
    {
        if (_beacon == null || _beaconLabel.Length == 0) return;
        var at = _beacon.GlobalPosition + _beacon.GlobalBasis.Y * (BeaconHeightMetres * 0.45f);
        _signs.Add(new SignCandidate(-300_000, at, _beaconLabel, "the walkthrough", BeaconColor, IconKind.Folder, true, 80f, FixedAnchor: true));
    }

    /// <summary>Surface distance (metres) from where you stand to a direction on this planet, or +∞ when there is no planet.</summary>
    public float DistanceTo(NVector3 surfaceDirection) =>
        _current == null ? float.PositiveInfinity : PlanetShape.Angle(_walker.Up, surfaceDirection) * _current.Comp.Shape.Radius;

    /// <summary>
    /// While a lesson card shows at the top centre, the arrival title moves down under it (title at 150 px, or 270 px
    /// with the card) so neither covers the other.
    /// </summary>
    public void MakeRoomForLesson(bool lessonShown)
    {
        _titleBox.OffsetTop = lessonShown ? 270 : 150;
        _titleBox.OffsetBottom = _titleBox.OffsetTop + 90;
    }

    /// <summary>A little celebration where you stand when a lesson step is done: the inspect chime and a burst of light.</summary>
    public void CelebrateLessonStep()
    {
        _audio.Inspect();
        _landFx.Burst(Gd(_walker.Feet), Gd(_walker.Up), BeaconColor);
    }

    /// <summary>The overview's orbit angle, radians (the walkthrough checks that you looked round it).</summary>
    public float UniverseYaw => _uniYaw;
}
