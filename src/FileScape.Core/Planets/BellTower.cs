using System.Numerics;

namespace FileScape.Core.Planets;

/// <summary>
/// The Chime world's landmark, an open bell tower, as one set of measurements shared by its drawing and its collision
/// (PLANET-56, DJ 2026-11-17: "its platform looks walkable, but the collision blocks the whole structure"). Until then
/// every landmark was one solid cylinder of <see cref="PlanetParams.LandmarkRadius"/> (1.6 m) up to 10 m, which for the
/// tower stopped you at the edge of its round base while its four pillars stood outside the solid. Now the base is a
/// low platform you step onto, the four pillars are the only tall solids, and the space under the hanging bell is open:
/// that is where a jump and twirl rings it (the app: <c>PlanetWorld.Bell.cs</c>).
/// <para>
/// Measurements are in the landmark mesh's own metres with its base at y = 0 (<c>PlanetArt.BellTower</c> draws from
/// these constants), before the app sinks the landmark <see cref="SinkMetres"/> into the ground. A change here moves the
/// drawing and the collision together; keep the pillars clear of the space under the bell
/// (<see cref="PillarOffsetMetres"/> × √2 − <see cref="PillarSolidRadiusMetres"/> &gt; <see cref="BellRimRadiusMetres"/> +
/// the walker's 0.35 m body) and the base top within the walker's 0.45 m step height.
/// </para>
/// </summary>
public static class BellTower
{
    /// <summary>The app stands every landmark this far into the ground, metres (so its foot never floats on a slope).</summary>
    public const float SinkMetres = 0.15f;

    /// <summary>The round stone base: its radius and its top in mesh metres (0.30 m above the ground once sunk: one easy step).</summary>
    public const float BaseRadiusMetres = 2.3f, BaseTopMeshMetres = 0.45f;

    /// <summary>The four pillars stand at (±this, ±this) in the mesh's x and z, metres: 2.12 m from the axis.</summary>
    public const float PillarOffsetMetres = 1.5f;

    /// <summary>A pillar's drawn radius at its foot (0.18 m shaft) and its solid: the brass collar at its foot, 0.28 m, metres.</summary>
    public const float PillarRadiusMetres = 0.18f, PillarSolidRadiusMetres = 0.28f;

    /// <summary>The pillars' top (where the roof beams sit), mesh metres: far above any jump, so they are walls.</summary>
    public const float PillarTopMeshMetres = 6.6f;

    /// <summary>Where the bell hangs from (its pivot when it swings), mesh metres above the base's foot.</summary>
    public const float BellPivotMeshMetres = 6.35f;

    /// <summary>
    /// The bell's mouth: its radius and how far below the pivot its rim is, metres (the rim is 4.65 m above the ground once
    /// sunk). Kept above a twirl from the base: its apex is ≈ 2.7 m over the base and the traveller is ≈ 1.4 m tall with its
    /// antenna (it put its head in a 2.05 m bell, 2026-11-17).
    /// </summary>
    public const float BellRimRadiusMetres = 1.2f, BellDropMetres = 1.55f;

    /// <summary>The traveller's height with its antenna, metres (AvatarRig: 1.1 m plus the antenna and lantern); a twirl under the bell must clear it.</summary>
    public const float TravellerHeightMetres = 1.45f;

    /// <summary>The base's top above the local ground, metres.</summary>
    public static float BaseTopMetres => BaseTopMeshMetres - SinkMetres;

    /// <summary>The pillars' top above the local ground, metres.</summary>
    public static float PillarTopMetres => PillarTopMeshMetres - SinkMetres;

    /// <summary>The bell's rim above the local ground, metres.</summary>
    public static float BellRimMetres => BellPivotMeshMetres - BellDropMetres - SinkMetres;

    /// <summary>
    /// The tower's solids at <paramref name="direction"/> on a planet of <paramref name="radius"/>, oriented as the app
    /// stands the landmark (its −Z towards <see cref="PlanetFrame.North"/>, +X east): the base (a step) and the four
    /// pillars, each tagged with <paramref name="id"/>.
    /// </summary>
    public static IReadOnlyList<SurfaceObstacle> Solids(Vector3 direction, float radius, int id)
    {
        var north = PlanetFrame.North(direction);
        var east = PlanetFrame.East(direction);
        var solids = new List<SurfaceObstacle> { new(direction, BaseRadiusMetres, BaseTopMetres, id) };
        foreach (var (x, z) in Pillars)
        {
            // Mesh +X is east and mesh −Z is north (PlanetVisual.Stand); the mesh is flat, so its planar offset is
            // measured along the surface (a 2.1 m offset on a ≥ 10 m planet differs from the arc by under 1 cm).
            var tangent = east * x - north * z;
            solids.Add(new SurfaceObstacle(PlanetFrame.Move(direction, Vector3.Normalize(tangent), tangent.Length(), radius), PillarSolidRadiusMetres, PillarTopMetres, id));
        }

        return solids;
    }

    /// <summary>The pillars' (x, z) in mesh metres.</summary>
    public static IReadOnlyList<(float X, float Z)> Pillars { get; } =
    [
        (-PillarOffsetMetres, -PillarOffsetMetres), (PillarOffsetMetres, -PillarOffsetMetres),
        (-PillarOffsetMetres, PillarOffsetMetres), (PillarOffsetMetres, PillarOffsetMetres),
    ];

    /// <summary>True when a walker at <paramref name="up"/> is under the bell: within its mouth's radius of the axis.</summary>
    public static bool UnderBell(PlanetLandmark landmark, Vector3 up, float radius) =>
        landmark.Bell && PlanetShape.Angle(up, landmark.Direction) * radius <= BellRimRadiusMetres;
}
