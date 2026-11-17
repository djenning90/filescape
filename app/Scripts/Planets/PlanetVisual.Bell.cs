using FileScape.Core.Planets;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The bell tower's bell (PLANET-56): hung as its own mesh from its pivot under the roof, it swings when rung
/// (<see cref="RingBell"/>, called by the world on a twirl underneath) — a big first swing that dies away over a few
/// seconds, with a little sideways wobble so it does not look like a hinge. Nothing here affects collision: the bell is
/// above any jump (<see cref="BellTower.BellRimMetres"/>).
/// </summary>
public partial class PlanetVisual
{
    // ---- the swing (by eye, 2026-11-17)
    /// <summary>The first swing's angle, radians (≈ 16°), and the sideways wobble's share of it.</summary>
    private const float BellSwingRadians = 0.28f, BellWobbleShare = 0.25f;

    /// <summary>The swing's angular frequency, radians per second (≈ 1.1 Hz: a heavy bell, slower than a bell flower's 2.9 Hz).</summary>
    private const float BellSwingRadiansPerSecond = 7f;

    /// <summary>The swing dies away with this time constant, seconds (it is still visible after ≈ 3 s).</summary>
    private const float BellSwingDecaySeconds = 1.3f;

    /// <summary>A rung swing is over (the bell hangs still again) after this long, seconds.</summary>
    private const float BellSwingSeconds = 5f;

    private MeshInstance3D? _bell;
    private float _bellT = -1; // seconds since it was rung, or −1 when still

    /// <summary>The bell's swing now, radians from hanging straight (probes check that a ring moves it).</summary>
    public float BellSwing { get; private set; }

    /// <summary>The bell hangs from its pivot under the roof, a child of the landmark (so it stands and sinks with it).</summary>
    private void HangBell(Node3D landmark, PlanetTheme theme)
    {
        _bell = new MeshInstance3D { Name = "Bell", Mesh = Shared($"bell-{theme}", () => PlanetArt.BellTowerBell(theme)), Position = new Vector3(0, BellTower.BellPivotMeshMetres, 0) };
        landmark.AddChild(_bell);
    }

    /// <summary>
    /// The bell is rung: it swings out from where it hangs (a ring while it swings starts a fresh swing). It swings across
    /// the view from <paramref name="viewer"/> (world position: the camera), so the one who rang it sees it swing, not
    /// nod towards them.
    /// </summary>
    public void RingBell(Vector3 viewer)
    {
        _bellT = 0;
        if (_bell?.GetParent() is not Node3D landmark) return;
        var to = landmark.ToLocal(viewer) - _bell.Position;
        to.Y = 0;
        _bellAxis = to.LengthSquared() > 1e-6f ? to.Normalized() : Vector3.Right;
    }

    private Vector3 _bellAxis = Vector3.Right;

    /// <summary>Per frame: the swing, a decaying sine about the axis towards the viewer (seen side to side), with a smaller wobble about the other horizontal axis.</summary>
    private void AnimateBell(float dt)
    {
        if (_bell == null || _bellT < 0) return;
        _bellT += dt;
        if (_bellT > BellSwingSeconds)
        {
            _bellT = -1;
            BellSwing = 0;
            _bell.Basis = Basis.Identity;
            return;
        }

        var envelope = BellSwingRadians * Mathf.Exp(-_bellT / BellSwingDecaySeconds);
        BellSwing = envelope * Mathf.Sin(_bellT * BellSwingRadiansPerSecond);
        var wobble = envelope * BellWobbleShare * Mathf.Sin(_bellT * BellSwingRadiansPerSecond * 1.5f);
        _bell.Basis = new Basis(_bellAxis, BellSwing) * new Basis(_bellAxis.Cross(Vector3.Up).Normalized(), wobble);
    }
}
