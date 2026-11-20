using FileScape.Core.Planets;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The way back's marker (PLANET-95, AD-78): what makes the parent's launcher recognisable at a glance among ordinary
/// destinations, in a crowded world and from across the planet. Two parts, both in the way back's own pale sky blue
/// (no other pad uses it, and it is not the walkthrough's lime): a <b>glowing rim</b> round the pad, so its outline
/// differs from every pink or themed pad at ground level, and a slim <b>spire of rising rings</b> over it — rings that
/// climb, narrow and vanish into a small light at the top, so the stack reads as an upward arrow (the ▲ of "up") and
/// shows over the curve of the world when the pad itself is below the horizon. It is a silhouette made of thin lines, not
/// a column of light: the walkthrough's beacon is a column, and the two must never be mistaken for each other.
/// Preserve: one marker per world, only on the planet you stand on (<see cref="_full"/>); nothing solid (no collider, no
/// shadow: you walk and fly through it); restrained brightness (bloom must not wash out the pad's name or the pads
/// beside it).
/// </summary>
public partial class PlanetVisual
{
    /// <summary>The way back's marker colour: the pad tint's pale blue, a little more saturated so it holds against a pale sky.</summary>
    private static readonly Color WayBackMarkColor = new(0.55f, 0.78f, 1f);

    /// <summary>The rim: a glowing ring lying round the pad this far beyond its edge, metres, this thick (tube radius,
    /// metres), at this height above the ground (the pad's side), at this glow energy with a slow pulse of this depth.</summary>
    private const float WayBackRimGapMetres = 0.2f, WayBackRimTubeMetres = 0.06f, WayBackRimLiftMetres = 0.12f, WayBackRimEnergy = 1.5f, WayBackRimPulse = 0.25f;

    /// <summary>The spire stands from this height to this height above the pad's top, metres: its foot clears a jumping
    /// traveller's head, and its top (≈ 8.5 m) shows over a 20–30 m world's curve from beyond the outermost ring.</summary>
    private const float SpireFootMetres = 2.2f, SpireTopMetres = 8.2f;

    /// <summary>How many rings climb the spire at once, and the lowest ring's radius, metres (a ring's radius shrinks
    /// linearly to nothing at the top, which makes the ▲ silhouette).</summary>
    private const int SpireRings = 4;

    private const float SpireFootRingMetres = 0.95f;

    /// <summary>The rings' tube radius, metres (a fine line; thicker read as a stack of tyres) and their glow energy.</summary>
    private const float SpireRingTubeMetres = 0.07f, SpireRingEnergy = 1.8f;

    /// <summary>How fast the rings climb, metres per second: a ring takes ≈ 10 s from foot to top and the stack moves
    /// on one ring-spacing every ≈ 2.5 s — calm, a sign rather than an alarm.</summary>
    private const float SpireRiseMetresPerSecond = 0.6f;

    /// <summary>The thread joining pad and top (radius, metres; glow energy: faint) and the light at the top (radius,
    /// metres; energy) with its soft halo (quad side, metres; additive energy).</summary>
    private const float SpireThreadMetres = 0.035f, SpireThreadEnergy = 1.1f, SpireStarMetres = 0.16f, SpireStarEnergy = 2.4f, SpireHaloMetres = 1.8f, SpireHaloEnergy = 0.6f;

    private Node3D? _wayBack;
    private readonly List<MeshInstance3D> _spireRings = [];

    /// <summary>Builds the way back's rim and spire over the parent's launcher, if this world has one.</summary>
    private void BuildWayBackMarker(PlanetSurface surf)
    {
        if (_comp.ParentLauncher is not { } up) return;
        _wayBack = new Node3D { Name = "WayBackMarker" };
        _wayBack.Transform = Stand(surf, up.Direction, PlanetFrame.North(up.Direction), 1f, sink: 0f);
        AddChild(_wayBack);
        var rim = new MeshInstance3D
        {
            Name = "WayBackRim",
            Mesh = new TorusMesh { InnerRadius = up.PadRadius + WayBackRimGapMetres - WayBackRimTubeMetres, OuterRadius = up.PadRadius + WayBackRimGapMetres + WayBackRimTubeMetres, Rings = 64, RingSegments = 8 },
            MaterialOverride = PlanetArt.GlowMaterial(WayBackMarkColor, WayBackRimEnergy, WayBackRimPulse),
            Position = new Vector3(0, WayBackRimLiftMetres, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _wayBack.AddChild(rim);
        var foot = up.PadTop + SpireFootMetres;
        var top = up.PadTop + SpireTopMetres;
        var thread = new MeshInstance3D
        {
            Name = "WayBackThread",
            Mesh = new CylinderMesh { TopRadius = SpireThreadMetres * 0.5f, BottomRadius = SpireThreadMetres, Height = top - up.PadTop, RadialSegments = 6, Rings = 1 },
            MaterialOverride = PlanetArt.GlowMaterial(WayBackMarkColor, SpireThreadEnergy),
            Position = new Vector3(0, (top + up.PadTop) / 2, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _wayBack.AddChild(thread);
        var ringMesh = new TorusMesh { InnerRadius = 1f - SpireRingTubeMetres / SpireFootRingMetres, OuterRadius = 1f + SpireRingTubeMetres / SpireFootRingMetres, Rings = 40, RingSegments = 6 };
        var ringMat = PlanetArt.GlowMaterial(WayBackMarkColor, SpireRingEnergy);
        for (var i = 0; i < SpireRings; i++)
        {
            var ring = new MeshInstance3D { Name = $"WayBackRing{i}", Mesh = ringMesh, MaterialOverride = ringMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _wayBack.AddChild(ring);
            _spireRings.Add(ring);
        }

        var star = new MeshInstance3D
        {
            Name = "WayBackStar",
            Mesh = new SphereMesh { Radius = SpireStarMetres, Height = SpireStarMetres * 2, RadialSegments = 10, Rings = 6 },
            MaterialOverride = PlanetArt.GlowMaterial(WayBackMarkColor.Lightened(0.3f), SpireStarEnergy, 0.2f),
            Position = new Vector3(0, top, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _wayBack.AddChild(star);
        _wayBack.AddChild(new MeshInstance3D
        {
            Name = "WayBackHalo",
            Mesh = new QuadMesh { Size = new Vector2(SpireHaloMetres, SpireHaloMetres) },
            MaterialOverride = PlanetArt.HaloMaterial(WayBackMarkColor, SpireHaloEnergy),
            Position = new Vector3(0, top, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        _spireFoot = foot;
        _spireTop = top;
        PlaceSpireRings();
        WayBackMarked = true;
    }

    private float _spireFoot, _spireTop;

    /// <summary>Shows or hides the marker: shown only on the planet you stand on (a planet hanging in the sky — the parent
    /// built in full for a landing — would otherwise wear a spire sticking out sideways).</summary>
    public void ShowWayBack(bool shown)
    {
        if (_wayBack != null && _wayBack.Visible != shown) _wayBack.Visible = shown;
    }

    /// <summary>Per frame: the spire's rings climb; each narrows as it rises and vanishes at the top, then starts again at the foot.</summary>
    private void AnimateWayBack()
    {
        if (_wayBack == null) return;
        PlaceSpireRings();
    }

    private void PlaceSpireRings()
    {
        var span = _spireTop - _spireFoot;
        for (var i = 0; i < _spireRings.Count; i++)
        {
            // Evenly spaced along the span, all moving up together; u is 0 at the foot and 1 at the top.
            var u = ((_time * SpireRiseMetresPerSecond / span) + i / (float)_spireRings.Count) % 1f;
            var radius = SpireFootRingMetres * (1f - u);
            var ring = _spireRings[i];
            ring.Visible = radius > SpireRingTubeMetres;
            ring.Transform = new Transform3D(Basis.Identity.Scaled(new Vector3(radius, radius, radius)), new Vector3(0, _spireFoot + u * span, 0));
        }
    }
}
