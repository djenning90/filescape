using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// A Hazard World's art (PLANET-34, PLANET-46; AD-53, AD-59; original): the broken shell's mesh — the ground cut along
/// <see cref="BrokenShell.EdgeMetres"/> (the same edge the walker falls at) into islands and bridges, scorched near the
/// edges, with a faintly lit stripe along every bridge's route, a dark underside and broken rim walls showing the crust's
/// strata down to a glowing seam — the debris chunks, and the black hole's parts (<see cref="BlackHoleArt"/>). Mesh data
/// is plain maths so it is made on the ground worker with the rest of the ground; the rim's outline is handed on in
/// <see cref="GroundArrays.Rim"/> for the glowing edge line (<c>PlanetVisual.Hazard</c>).
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
    private const float ScorchOuterMetres = 1.6f, ScorchInnerMetres = 0.15f, ScorchAmount = 0.75f;

    /// <summary>
    /// The route's stripe along every bridge (AD-59, "follow the lit path"): full within this half-width of the centre line,
    /// gone by the outer value, metres; this much of the palette's path colour, glowing this much (vertex alpha).
    /// </summary>
    private const float RouteStripeInnerMetres = 0.12f, RouteStripeOuterMetres = 0.38f, RouteStripeAmount = 0.7f, RouteStripeGlow = 0.18f;

    /// <summary>The rim's rows, as fractions of <see cref="BrokenShell.CrustMetres"/> below the ground (the last is pushed lower or higher per sample: a ragged bottom).</summary>
    private static readonly float[] RimDepths = [0f, 0.1f, 0.32f, 0.55f, 0.78f, 1f];

    /// <summary>The ragged bottom: ± this fraction of the crust; the wall also leans in and out by ± this many metres.</summary>
    private const float RimBottomRagFraction = 0.35f, RimLeanMetres = 0.18f;

    /// <summary>Snapping a corner onto the edge: Newton steps on the edge distance, the gradient sampled this far each way (metres), to within this (metres).</summary>
    private const int SnapIterations = 6;

    private const float SnapProbeMetres = 0.04f, SnapToleranceMetres = 0.01f;

    /// <summary>
    /// The outline of a broken shell's ground: <see cref="Points"/> on the edge (planet frame, at ground height),
    /// <see cref="Out"/> the unit tangent at each pointing off the ground into open sky, <see cref="Segments"/> pairs of
    /// indices into them (each a piece of edge), and the glowing edge line's ribbon, made here on the worker so the main
    /// thread only uploads it (<see cref="GlowVertices"/>, <see cref="GlowNormals"/>, <see cref="GlowIndices"/>).
    /// </summary>
    public sealed record RimOutline(Vector3[] Points, Vector3[] Out, int[] Segments, Vector3[] GlowVertices, Vector3[] GlowNormals, int[] GlowIndices);

    /// <summary>The glowing edge line: this wide, metres, lying across the edge (a quarter on the ground, the rest over the drop), this far above the ground.</summary>
    private const float RimLineWidthMetres = 0.35f, RimLineLiftMetres = 0.04f;

    /// <summary>The glowing edge line's ribbon along the outline: two vertices per outline point, a quad per segment in both windings (the glow shader culls back faces, and segments run either way round).</summary>
    private static (Vector3[] Vertices, Vector3[] Normals, int[] Indices) RimGlowRibbon(List<Vector3> points, List<Vector3> outs, List<int> segs)
    {
        var v = new Vector3[points.Count * 2];
        var nrm = new Vector3[points.Count * 2];
        for (var i = 0; i < points.Count; i++)
        {
            var up = points[i].Normalized();
            var p = points[i] + up * RimLineLiftMetres;
            v[2 * i] = p - outs[i] * (RimLineWidthMetres * 0.25f);
            v[2 * i + 1] = p + outs[i] * (RimLineWidthMetres * 0.75f) - up * 0.06f;
            nrm[2 * i] = nrm[2 * i + 1] = up;
        }

        var idx = new int[segs.Count / 2 * 12];
        var k = 0;
        for (var s = 0; s < segs.Count; s += 2)
        {
            int iu = 2 * segs[s], ou = iu + 1, iv = 2 * segs[s + 1], ov = iv + 1;
            foreach (var x in new[] { iu, ou, ov, iu, ov, iv, iu, ov, ou, iu, iv, ov }) idx[k++] = x;
        }

        return (v, nrm, idx);
    }

    /// <summary>
    /// Cuts a broken shell out of ground vertex data (<see cref="PlanetMeshArrays"/>): triangles wholly over open sky are
    /// dropped; a triangle crossing an edge keeps its solid corners and moves the others onto the edge (Newton steps on
    /// <see cref="BrokenShell.EdgeMetres"/>), so the drawn edge follows the walked one to within a chord (≈ 0.3 m at the
    /// subdivision hazard worlds use). Adds the route stripe, the scorch, the underside and the rim walls, and returns the
    /// outline. Thread-safe (plain maths), like the rest of the ground.
    /// </summary>
    public static GroundArrays BreakShell(GroundArrays g, PlanetShape shape, BrokenShell shell, PlanetPalette palette)
    {
        var n = g.Vertices.Length;
        var verts = new List<Vector3>(n * 2);
        var normals = new List<Vector3>(n * 2);
        var colors = new List<Color>(n * 2);
        var tris = new List<int>(g.Indices.Length * 2);
        var scorch = Linear(Scorch);
        var stripe = Linear(palette.Path.Lerp(Colors.White, 0.25f));
        var edge = new float[n];
        for (var i = 0; i < n; i++)
        {
            var nd = N(g.Vertices[i].Normalized());
            edge[i] = shell.EdgeMetres(nd);
            verts.Add(g.Vertices[i]);
            normals.Add(g.Normals[i]);
            colors.Add(GroundColour(g.Colors[i], nd, edge[i], shell, scorch, stripe));
        }

        // Corners over open sky are moved onto the edge (one copy per corner, shared by its triangles).
        var snapped = new Dictionary<int, int>();
        int Snap(int i, int towards)
        {
            if (snapped.TryGetValue(i, out var s)) return s;
            var b = SnapToEdge(shell, N(g.Vertices[i].Normalized()), N(g.Vertices[towards].Normalized()));
            var nrm = shape.Normal(b);
            verts.Add(G(b) * shape.GroundRadius(b));
            normals.Add(G(nrm));
            colors.Add(GroundColour(g.Colors[i], b, 0, shell, scorch, stripe));
            return snapped[i] = verts.Count - 1;
        }

        for (var t = 0; t < g.Indices.Length; t += 3)
        {
            int a = g.Indices[t], b = g.Indices[t + 1], c = g.Indices[t + 2];
            bool sa = edge[a] > 0, sb = edge[b] > 0, sc = edge[c] > 0;
            if (!sa && !sb && !sc) continue;
            var solid = sa ? a : sb ? b : c;
            tris.Add(sa ? a : Snap(a, solid));
            tris.Add(sb ? b : Snap(b, solid));
            tris.Add(sc ? c : Snap(c, solid));
        }

        // The outline: edges used by one kept triangle only (the rest of the sphere is closed). Each vertex's "out" is the
        // sum of its edges' outward normals, pointing away from the kept triangle's third corner.
        var use = new Dictionary<(int, int), (int Count, int Third)>();
        for (var t = 0; t < tris.Count; t += 3)
        {
            for (var k = 0; k < 3; k++)
            {
                int u = tris[t + k], v = tris[t + (k + 1) % 3], w = tris[t + (k + 2) % 3];
                var key = u < v ? (u, v) : (v, u);
                use[key] = use.TryGetValue(key, out var e) ? (e.Count + 1, w) : (1, w);
            }
        }

        var rimIndex = new Dictionary<int, int>();
        var rimPoints = new List<Vector3>();
        var rimOut = new List<Vector3>();
        var rimSegs = new List<int>();
        int RimOf(int v)
        {
            if (rimIndex.TryGetValue(v, out var r)) return r;
            rimPoints.Add(verts[v]);
            rimOut.Add(Vector3.Zero);
            return rimIndex[v] = rimPoints.Count - 1;
        }

        foreach (var ((u, v), (count, third)) in use)
        {
            if (count != 1 || verts[u].IsEqualApprox(verts[v])) continue;
            int ru = RimOf(u), rv = RimOf(v);
            var mid = (verts[u] + verts[v]) * 0.5f;
            var up = mid.Normalized();
            var along = verts[v] - verts[u];
            var o = along.Cross(up).Normalized();
            if (o.Dot(verts[third] - mid) > 0) o = -o; // away from the ground
            rimOut[ru] += o;
            rimOut[rv] += o;
            rimSegs.Add(ru);
            rimSegs.Add(rv);
        }

        for (var i = 0; i < rimOut.Count; i++)
        {
            var up = rimPoints[i].Normalized();
            var o = rimOut[i] - up * rimOut[i].Dot(up);
            rimOut[i] = o.LengthSquared() > 1e-10f ? o.Normalized() : Vector3.Zero;
        }

        // The underside: the kept ground again, a crust's thickness lower, facing inwards (seen across the open sky and
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

        AddRimWalls(rimPoints, rimOut, rimSegs, verts, normals, colors, tris);
        var (gv, gn, gi) = RimGlowRibbon(rimPoints, rimOut, rimSegs);
        return new GroundArrays(verts.ToArray(), normals.ToArray(), colors.ToArray(), tris.ToArray(), new RimOutline(rimPoints.ToArray(), rimOut.ToArray(), rimSegs.ToArray(), gv, gn, gi));
    }

    /// <summary>A ground vertex's colour on a broken shell: scorched towards an edge, and the lit route stripe along the bridges.</summary>
    private static Color GroundColour(Color c, NVector3 up, float edgeMetres, BrokenShell shell, Color scorch, Color stripe)
    {
        c = c.Lerp(scorch, ScorchAmount * (1 - Smooth(ScorchInnerMetres, ScorchOuterMetres, edgeMetres)));
        var near = float.PositiveInfinity;
        foreach (var s in shell.Spans)
        {
            if (PlanetShape.Angle(up, s.Mid) > s.BoundRadians) continue;
            var (along, side) = s.Measure(up);
            if (along < 0 || along > s.LengthMetres) continue;
            near = MathF.Min(near, MathF.Abs(side));
        }

        var k = 1 - Smooth(RouteStripeInnerMetres, RouteStripeOuterMetres, near);
        if (k <= 0) return c;
        var lit = c.Lerp(stripe, RouteStripeAmount * k);
        return new Color(lit.R, lit.G, lit.B, Mathf.Lerp(c.A, 1f - RouteStripeGlow, k));
    }

    /// <summary>
    /// The point of the edge nearest <paramref name="p"/> (a direction over open sky): Newton steps along the edge
    /// distance's gradient; if that does not settle, halving along the way to <paramref name="solid"/> (a corner on ground).
    /// </summary>
    private static NVector3 SnapToEdge(BrokenShell shell, NVector3 p, NVector3 solid)
    {
        var r = shell.Radius;
        var q = p;
        for (var it = 0; it < SnapIterations; it++)
        {
            var e = shell.EdgeMetres(q);
            if (MathF.Abs(e) < SnapToleranceMetres) return q;
            var (a, b) = PlanetShape.Tangents(q);
            var ga = (shell.EdgeMetres(PlanetFrame.Move(q, a, SnapProbeMetres, r)) - shell.EdgeMetres(PlanetFrame.Move(q, -a, SnapProbeMetres, r))) / (2 * SnapProbeMetres);
            var gb = (shell.EdgeMetres(PlanetFrame.Move(q, b, SnapProbeMetres, r)) - shell.EdgeMetres(PlanetFrame.Move(q, -b, SnapProbeMetres, r))) / (2 * SnapProbeMetres);
            var g2 = ga * ga + gb * gb;
            if (g2 < 1e-6f) break;
            var grad = a * ga + b * gb;
            var next = PlanetFrame.Move(q, grad, -e / MathF.Sqrt(g2), r);
            if (PlanetShape.Angle(next, p) * r > 1.2f) break; // wandered off: fall back on halving
            q = next;
        }

        if (MathF.Abs(shell.EdgeMetres(q)) < 0.05f) return q;
        // Halving between the corner (open) and its solid neighbour.
        NVector3 lo = p, hi = solid;
        for (var it = 0; it < 14; it++)
        {
            var mid = NVector3.Normalize(lo + hi);
            if (shell.EdgeMetres(mid) > 0) hi = mid;
            else lo = mid;
        }

        return hi;
    }

    /// <summary>
    /// The rim walls: from every outline point a column down through the crust in <see cref="RimDepths"/> rows (lip, soil,
    /// three rock bands, the glowing seam), leaning out and with a ragged bottom, and a strip of quads per outline segment,
    /// facing out over the open sky.
    /// </summary>
    private static void AddRimWalls(List<Vector3> points, List<Vector3> outs, List<int> segs, List<Vector3> verts, List<Vector3> normals, List<Color> colors, List<int> tris)
    {
        var rows = RimDepths.Length;
        Color[] rowColour = [Linear(RimLip), Linear(RimSoil), Linear(RimRockA), Linear(RimRockB), Linear(RimRockC), Glowing(Linear(RimSeam), 0.85f)];
        var first = verts.Count;
        for (var i = 0; i < points.Count; i++)
        {
            var up = points[i].Normalized();
            var ground = points[i].Length();
            var o = outs[i];
            var rag = (Noise(up * 37f) - 0.5f) * 2f;
            var lean = (Noise(up * 17f + new Vector3(5.2f, 1.1f, 0.7f)) - 0.5f) * 2f * RimLeanMetres;
            for (var j = 0; j < rows; j++)
            {
                var depth = RimDepths[j] * BrokenShell.CrustMetres;
                if (j == rows - 1) depth *= 1 + RimBottomRagFraction * rag;
                var lift = j == 0 ? 0.03f : 0f; // the lip sits a hair above the ground so the seam between them never shows a gap
                verts.Add(up * (ground + lift - depth) + o * (lean * RimDepths[j]));
                normals.Add(o);
                colors.Add(rowColour[j]);
            }
        }

        for (var s = 0; s < segs.Count; s += 2)
        {
            int u = segs[s], v = segs[s + 1];
            var front = (outs[u] + outs[v]).Normalized();
            for (var j = 0; j < rows - 1; j++)
            {
                int a = first + u * rows + j, b = first + v * rows + j, c = first + v * rows + j + 1, d = first + u * rows + j + 1;
                AddFacing(verts, tris, a, b, c, front);
                AddFacing(verts, tris, a, c, d, front);
            }
        }
    }

    private static NVector3 N(Vector3 v) => new(v.X, v.Y, v.Z);

    private static Vector3 G(NVector3 v) => new(v.X, v.Y, v.Z);

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
