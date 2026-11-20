using FileScape.Core.Planets;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// What the travel probes read (PLANET-81..83, <c>PlanetsProbe.Travel.cs</c>): the app clock the launch log uses, and
/// where a pad is from the traveller. Read-only.
/// </summary>
public partial class PlanetWorld
{
    /// <summary>The session clock, seconds (the same clock as <see cref="LaunchLog"/>).</summary>
    public double NowSeconds => _s.Now.TotalSeconds;

    /// <summary>The angle along the ground from the traveller to <paramref name="direction"/>, radians (× the radius: metres).</summary>
    public float AngleFromWalker(NVector3 direction) => PlanetShape.Angle(_walker.Up, direction);

    /// <summary>The folder hangs in this world's sky as a neighbour you fly to (not a warp).</summary>
    public bool HasWorldInSky(int folderId) => _children.ContainsKey(folderId);
}
