using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// The hazard world's art (PLANET-34, AD-53; original): the broken shell's mesh — the ground with its holes cut out along
/// <see cref="ShellHole.BoundaryRadians"/> (the same edge the walker falls at), scorched near the edges, a dark underside
/// and broken rim walls showing the crust's strata down to a glowing seam — the debris chunks, and the black hole's parts
/// (<see cref="BlackHoleArt"/>). Mesh data is plain maths so it is made on the ground worker with the rest of the ground.
/// </summary>
public static partial class PlanetArt
{
    // ---- the crust's colours (sRGB; chosen by eye, 2026-11-15): a dark lip, warm soil, violet-grey rock bands, and a
    // glowing seam at the bottom lit by the black hole. The underside is nearly black with a faint violet glow.
    private static readonly Color RimLip = C(0.30f, 0.22f, 0.30f);
    private static readonly Color RimSoil = C(0.52f, 0.33f, 0.26f);
    private static readonly Color RimRockA = C(0.40f, 0.33f, 0.50f);
    private static readonly Color RimRockB = C(0.60f, 0.48f, 0.56f);
    private static readonly Color RimRockC = C(0.28f, 0.23f, 0.38f);
    private static readonly Color RimSeam = C(1.00f, 0.52f, 0.22f);
    private static readonly Color Underside = C(0.13f, 0.10f, 0.19f);
    private static readonly Color Scorch = C(0.26f, 0.18f, 0.27f);

    /// <summary>The ground is scorched within this many metres of an edge (fading in from the outer value to full at the inner).</summary>
    private const float ScorchOuterMetres = 3f, ScorchInnerMetres = 0.3f, ScorchAmount = 0.85f;

    /// <summary>The rim walls are sampled about this often along each edge, metres (finer than the ground's ≈ 1 m edges, so the teeth show).</summary>
    private const float RimSampleMetres = 0.3f;

    /// <summary>The rim's rows, as fractions of <see cref="BrokenShell.CrustMetres"/> below the ground (the last is pushed lower or higher per sample: a ragged bottom).</summary>
    private static readonly float[] RimDepths = [0f, 0.1f, 0.32f, 0.55f, 0.78f, 1f];

    /// <summary>The ragged bottom: ± this fraction of the crust, metres per metre; the teeth also lean in and out by ± this many metres.</summary>
    private const float RimBottomRagFraction = 0.35f, RimLeanMetres = 0.18f;

    /// <summary>
    /// Cuts the shell's holes out of ground vertex data (<see cref="PlanetMeshArrays"/>) and adds the underside and the rim
    /// walls. Triangles wholly over a hole are dropped; a triangle crossing an edge keeps its corners on solid ground and
    /// moves the others onto the edge along their bearing from the hole's centre, so the drawn edge follows the walked
    /// one to within a chord. Thread-safe (plain maths), like the rest of the ground.
    /// </summary>
    public static GroundArrays BreakShell(GroundArrays g, PlanetShape shape, BrokenShell shell)
    {
        var n = g.Vertices.Length;
        var verts = new List<Vector3>(n * 3);
        var normals = new List<Vector3>(n * 3);
        var colors = new List<Color>(n * 3);
        var tris = new List<int>(g.Indices.Length * 2);
        var scorch = Linear(Scorch);
        var holeOf = new int[n];
        for (var i = 0; i < n; i++)
        {
            var d = g.Vertices[i].Normalized();
            var nd = new NVector3(d.X, d.Y, d.Z);
            holeOf[i] = -1;
            for (var h = 0; h < shell.Holes.Count; h++)
            {
                if (shell.Holes[h].Contains(nd)) { holeOf[i] = h; break; }
            }

            verts.Add(g.Vertices[i]);
            normals.Add(g.Normals[i]);
            var e = shell.EdgeMetres(nd);
            colors.Add(g.Colors[i].Lerp(scorch, ScorchAmount * (1 - Smooth(ScorchInnerMetres, ScorchOuterMetres, e))));
        }

        // Corners over a hole are moved onto its edge (one copy per corner, shared by its triangles).
        var snapped = new Dictionary<int, int>();
        int Snap(int i)
        {
            if (snapped.TryGetValue(i, out var s)) return s;
            var hole = shell.Holes[holeOf[i]];
            var d = g.Vertices[i].Normalized();
            var b = hole.BoundaryPoint(hole.BearingOf(new NVector3(d.X, d.Y, d.Z)));
            var nrm = shape.Normal(b);
            verts.Add(new Vector3(b.X, b.Y, b.Z) * shape.GroundRadius(b));
            normals.Add(new Vector3(nrm.X, nrm.Y, nrm.Z));
            colors.Add(scorch);
            return snapped[i] = verts.Count - 1;
        }

        for (var t = 0; t < g.Indices.Length; t += 3)
        {
            int a = g.Indices[t], b = g.Indices[t + 1], c = g.Indices[t + 2];
            var over = (holeOf[a] >= 0 ? 1 : 0) + (holeOf[b] >= 0 ? 1 : 0) + (holeOf[c] >= 0 ? 1 : 0);
            if (over == 3) continue;
            tris.Add(holeOf[a] >= 0 ? Snap(a) : a);
            tris.Add(holeOf[b] >= 0 ? Snap(b) : b);
            tris.Add(holeOf[c] >= 0 ? Snap(c) : c);
        }

        // The underside: the kept ground again, a crust's thickness lower, facing inwards (seen through the holes and
        // from inside during a fall). Faintly glowing (vertex alpha), as if lit from below by the disc.
        var outerCount = verts.Count;
        var under = Glowing(Linear(Underside), 0.18f);
        for (var i = 0; i < outerCount; i++)
        {
            var d = verts[i].Normalized();
            verts.Add(verts[i] - d * BrokenShell.CrustMetres);
            normals.Add(-d);
            colors.Add(under);
        }

        var outerTris = tris.Count;
        for (var t = 0; t < outerTris; t += 3)
        {
            tris.Add(tris[t] + outerCount);
            tris.Add(tris[t + 2] + outerCount); // reversed: faces the centre
            tris.Add(tris[t + 1] + outerCount);
        }

        foreach (var hole in shell.Holes) AddRim(hole, shape, verts, normals, colors, tris);
        return new GroundArrays(verts.ToArray(), normals.ToArray(), colors.ToArray(), tris.ToArray());
    }

    /// <summary>
    /// One hole's rim wall: a strip round the edge from just above the ground down through the crust in
    /// <see cref="RimDepths"/> rows (lip, soil, three rock bands, the glowing seam), with a ragged bottom and teeth that
    /// lean in and out, facing into the hole.
    /// </summary>
    private static void AddRim(ShellHole hole, PlanetShape shape, List<Vector3> verts, List<Vector3> normals, List<Color> colors, List<int> tris)
    {
        var perimeter = 2 * MathF.PI * MathF.Sin(hole.RadiusRadians) * shape.Radius;
        var samples = Math.Max(96, (int)MathF.Ceiling(perimeter / RimSampleMetres));
        var rows = RimDepths.Length;
        Color[] rowColour = [Linear(RimLip), Linear(RimSoil), Linear(RimRockA), Linear(RimRockB), Linear(RimRockC), Glowing(Linear(RimSeam), 0.85f)];
        var first = verts.Count;
        var centre = new Vector3(hole.Centre.X, hole.Centre.Y, hole.Centre.Z);
        for (var k = 0; k < samples; k++)
        {
            var th = 2 * MathF.PI * k / samples;
            var b = hole.BoundaryPoint(th);
            var up = new Vector3(b.X, b.Y, b.Z);
            var inward = (centre - up * centre.Dot(up)).Normalized(); // along the ground, into the hole
            var ground = shape.GroundRadius(b);
            var rag = (Noise(up * 9.3f + new Vector3(hole.Seed % 97, 1.3f, 2.1f)) - 0.5f) * 2f;
            var lean = (Noise(up * 4.1f + new Vector3(5.2f, hole.Seed % 89, 0.7f)) - 0.5f) * 2f * RimLeanMetres;
            for (var j = 0; j < rows; j++)
            {
                var depth = RimDepths[j] * BrokenShell.CrustMetres;
                if (j == rows - 1) depth *= 1 + RimBottomRagFraction * rag;
                var lift = j == 0 ? 0.04f : 0f; // the lip sits a hair above the ground so the seam between them never shows a gap
                var p = up * (ground + lift - depth) + inward * (lean * RimDepths[j]);
                verts.Add(p);
                normals.Add(inward);
                colors.Add(rowColour[j]);
            }
        }

        for (var k = 0; k < samples; k++)
        {
            var k1 = (k + 1) % samples;
            for (var j = 0; j < rows - 1; j++)
            {
                int a = first + k * rows + j, b = first + k1 * rows + j, c = first + k1 * rows + j + 1, d = first + k * rows + j + 1;
                AddFacing(verts, tris, a, b, c, normals[a]);
                AddFacing(verts, tris, a, c, d, normals[a]);
            }
        }
    }

    /// <summary>Adds a triangle wound so its front faces along <paramref name="front"/> (Godot's front faces wind clockwise seen from the front).</summary>
    private static void AddFacing(List<Vector3> verts, List<int> tris, int a, int b, int c, Vector3 front)
    {
        var g = (verts[b] - verts[a]).Cross(verts[c] - verts[a]);
        if (g.Dot(front) > 0) (b, c) = (c, b);
        tris.Add(a);
        tris.Add(b);
        tris.Add(c);
    }

    /// <summary>
    /// A chunk of broken crust (≈ 1 m across at scale 1, centred on its origin): a lumpy, faceted rock with a dark lip on
    /// one side and rock bands below, the same colours as the rim. Deterministic per <paramref name="seed"/>.
    /// </summary>
    public static ArrayMesh DebrisChunk(int seed)
    {
        var (dirs, idx) = Icosphere(1);
        var rng = new Random(seed);
        var scale = new Vector3(0.8f + 0.5f * (float)rng.NextDouble(), 0.5f + 0.4f * (float)rng.NextDouble(), 0.7f + 0.5f * (float)rng.NextDouble());
        var b = new SurfaceTool();
        b.Begin(Mesh.PrimitiveType.Triangles);
        var pos = dirs.Select(d => d * (0.4f + 0.22f * (float)rng.NextDouble()) * scale).ToArray();
        for (var t = 0; t < idx.Count; t += 3)
        {
            var a = pos[idx[t]];
            var bb = pos[idx[t + 1]];
            var c = pos[idx[t + 2]];
            var mid = (a + bb + c) / 3f;
            var col = mid.Y > 0.12f ? RimLip : mid.Y > -0.05f ? RimSoil : mid.Y > -0.2f ? RimRockA : RimRockB;
            var normal = (c - a).Cross(bb - a).Normalized(); // faceted: one normal per face (outward, for the clockwise winding)
            if (normal.Dot(mid) < 0) normal = -normal;
            b.SetColor(Linear(col));
            foreach (var v in new[] { a, bb, c })
            {
                b.SetNormal(normal);
                b.AddVertex(v);
            }
        }

        var mesh = b.Commit();
        mesh.SurfaceSetMaterial(0, PropMaterial());
        return mesh;
    }

    /// <summary>The accretion disc: a flat annulus in the XZ plane from <paramref name="innerMetres"/> to <paramref name="outerMetres"/>, drawn by black_hole_disc.gdshader.</summary>
    public static MeshInstance3D AccretionDisc(float innerMetres, float outerMetres, float energy)
    {
        const int segments = 96;
        var b = new SurfaceTool();
        b.Begin(Mesh.PrimitiveType.Triangles);
        for (var i = 0; i < segments; i++)
        {
            float a0 = Mathf.Tau * i / segments, a1 = Mathf.Tau * (i + 1) / segments;
            var i0 = new Vector3(MathF.Cos(a0), 0, MathF.Sin(a0)) * innerMetres;
            var o0 = new Vector3(MathF.Cos(a0), 0, MathF.Sin(a0)) * outerMetres;
            var i1 = new Vector3(MathF.Cos(a1), 0, MathF.Sin(a1)) * innerMetres;
            var o1 = new Vector3(MathF.Cos(a1), 0, MathF.Sin(a1)) * outerMetres;
            foreach (var v in new[] { i0, o0, o1, i0, o1, i1 })
            {
                b.SetNormal(Vector3.Up);
                b.AddVertex(v);
            }
        }

        var m = new ShaderMaterial { Shader = Load("black_hole_disc") };
        m.SetShaderParameter("outer_radius", outerMetres);
        m.SetShaderParameter("inner_fraction", innerMetres / outerMetres);
        m.SetShaderParameter("energy", energy);
        return new MeshInstance3D { Name = "AccretionDisc", Mesh = b.Commit(), MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    /// <summary>The glow round the core: a camera-facing quad whose photon ring sits at the core's silhouette (black_hole_glow.gdshader).</summary>
    public static MeshInstance3D BlackHoleGlow(float coreMetres, float energy)
    {
        const float ring = 0.3f; // the ring at 0.3 of the quad's half-size: the quad is the core's radius ÷ 0.3 across each way
        var m = new ShaderMaterial { Shader = Load("black_hole_glow") };
        m.SetShaderParameter("ring", ring);
        m.SetShaderParameter("energy", energy);
        m.SetShaderParameter("push", coreMetres * 1.05f);
        var half = coreMetres / ring;
        return new MeshInstance3D
        {
            Name = "PhotonRing",
            Mesh = new QuadMesh { Size = new Vector2(half * 2, half * 2) },
            MaterialOverride = m,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}
