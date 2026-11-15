using FileScape.Core.Planets;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The look of a world's character (PLANET-28, AD-48): its tint over the content palette, its sky details, and the few
/// props that say what kind of place it is — the Windows volume's pane gateway, the WSL distribution's terminal monolith,
/// the repository's workbench, and the waymarker that counts the depth band on ordinary folders. Original art.
/// </summary>
public static partial class PlanetArt
{
    /// <summary>
    /// The colours of a character: the tint over the content palette's sky, light and accent, and how strongly each of the
    /// sky shader's optional details shows (0 = off; the strengths are shader parameters of planet_sky.gdshader, about 0–1).
    /// </summary>
    /// <param name="SkyTint">Colour the palette's sky top is pulled towards.</param>
    /// <param name="SkyTintAmount">How far (0–1) the sky is pulled towards <paramref name="SkyTint"/>; 0.6 of it also tints the ambient light.</param>
    /// <param name="Sun">The character's light colour (it colours the dawn glow).</param>
    /// <param name="Accent">The character's signature colour (nebula tint, commit graph).</param>
    /// <param name="AuroraA">The aurora's first colour.</param>
    /// <param name="AuroraB">The aurora's second colour.</param>
    /// <param name="Dawn">Strength of the dawn glow on the horizon (the hub).</param>
    /// <param name="Aurora">Strength of the aurora curtains (the roots, deep bands).</param>
    /// <param name="Lattice">Strength of the window-pane lattice (the Windows volume).</param>
    /// <param name="Scan">Strength of the terminal scanlines (a WSL distribution).</param>
    /// <param name="Graph">Strength of the commit-graph constellation (a repository, faintly inside one).</param>
    /// <param name="Stars">Multiplier on the star brightness (1 = the palette's own).</param>
    public sealed record CharacterLook(Color SkyTint, float SkyTintAmount, Color Sun, Color Accent, Color AuroraA, Color AuroraB, float Dawn, float Aurora, float Lattice, float Scan, float Graph, float Stars);

    private static readonly Color[] BandColors =
    [
        C(0.45f, 0.95f, 0.85f), // 1 the outskirts: fresh mint
        C(1.00f, 0.72f, 0.38f), // 2 the trails: amber
        C(0.72f, 0.55f, 1.00f), // 3 the deep woods: violet
        C(0.35f, 0.85f, 1.00f), // 4 the underglow: bioluminescent cyan
    ];

    /// <summary>A depth band's colour (1–4), the colour of its waymarker and its sky accent.</summary>
    public static Color BandColor(int band) => BandColors[Math.Clamp(band, 1, 4) - 1];

    /// <summary>
    /// The teal of a project (AD-50): the repository's sky accent, the workbench's inlay ring, commit beads and screen line,
    /// and the bead on a waymarker inside a project, so "you are in a repository" reads the same everywhere.
    /// </summary>
    private static readonly Color RepositoryTeal = C(0.4f, 1f, 0.75f);

    /// <summary>
    /// A world character's look: the four special kinds (hub, Windows volume, WSL distribution, repository) are authored
    /// by hand; ordinary folders get their depth band's look (<see cref="BandLook"/>). Rationale for the individual values not recorded.
    /// </summary>
    public static CharacterLook Look(WorldCharacter c) => c.Kind switch
    {
        WorldKind.Hub => new(C(0.95f, 0.62f, 0.5f), 0.18f, C(1f, 0.86f, 0.7f), C(1f, 0.75f, 0.45f), C(1f, 0.7f, 0.45f), C(1f, 0.5f, 0.6f), Dawn: 0.75f, Aurora: 0, Lattice: 0, Scan: 0, Graph: 0, Stars: 1f),
        WorldKind.WindowsRoot => new(C(0.25f, 0.5f, 0.95f), 0.32f, C(0.85f, 0.93f, 1f), C(0.45f, 0.8f, 1f), C(0.35f, 0.75f, 1f), C(0.55f, 0.5f, 1f), Dawn: 0, Aurora: 0.8f, Lattice: 0.9f, Scan: 0, Graph: 0, Stars: 1.1f),
        WorldKind.WslRoot => new(C(0.55f, 0.18f, 0.32f), 0.32f, C(1f, 0.84f, 0.66f), C(1f, 0.58f, 0.25f), C(1f, 0.55f, 0.2f), C(0.85f, 0.3f, 0.55f), Dawn: 0, Aurora: 0.8f, Lattice: 0, Scan: 1f, Graph: 0, Stars: 1.1f),
        WorldKind.Repository => new(C(0.1f, 0.42f, 0.4f), 0.3f, C(0.9f, 1f, 0.92f), RepositoryTeal, C(0.35f, 1f, 0.75f), C(0.4f, 0.6f, 1f), Dawn: 0, Aurora: 0.15f, Lattice: 0, Scan: 0, Graph: 1f, Stars: 1.2f),
        // A broken world (PLANET-34): a bruised violet sky with an ember-orange glow low on the horizon (the disc's light)
        // and a warm caution-amber light: dramatic, but no alarm red. By eye (2026-11-15).
        WorldKind.Hazard => new(C(0.16f, 0.03f, 0.22f), 0.6f, C(1f, 0.6f, 0.35f), C(1f, 0.55f, 0.25f), C(1f, 0.45f, 0.2f), C(0.75f, 0.3f, 1f), Dawn: 0.85f, Aurora: 0.3f, Lattice: 0, Scan: 0, Graph: 0, Stars: 1.25f),
        _ => BandLook(c.Band, c.InsideRepository),
    };

    /// <summary>
    /// An ordinary folder's look by depth band (1–4): deeper means a darker, richer sky, more stars and a stronger glow
    /// of the band's colour, so a long journey down visibly changes the sky.
    /// </summary>
    private static CharacterLook BandLook(int band, bool inRepo)
    {
        var col = BandColor(band);
        // Deeper: a darker, richer sky with more stars and a stronger glow of the band's colour.
        var tint = band switch { 1 => C(0.3f, 0.6f, 0.75f), 2 => C(0.75f, 0.42f, 0.3f), 3 => C(0.3f, 0.18f, 0.5f), _ => C(0.02f, 0.08f, 0.22f) };
        var amount = 0.12f + 0.07f * band; // sky tint 0.19 at band 1 rising to 0.40 at band 4
        var sun = band switch { 1 => C(1f, 0.97f, 0.9f), 2 => C(1f, 0.85f, 0.66f), 3 => C(0.88f, 0.82f, 1f), _ => C(0.7f, 0.9f, 1f) };
        // Band 2 (the trails) has a faint dawn; the aurora appears from band 3 (0.18) and doubles at band 4 (0.36); stars
        // brighten 1.08× → 1.62×; inside a project the commit graph shows faintly (0.35 of a repository's).
        return new(tint, amount, sun, col, col, col.Lerp(C(0.8f, 0.5f, 1f), 0.5f), Dawn: band == 2 ? 0.35f : 0, Aurora: band >= 3 ? 0.18f * (band - 2) : 0, Lattice: 0, Scan: 0,
            Graph: inRepo ? 0.35f : 0, Stars: 0.9f + 0.18f * band);
    }

    /// <summary>The sky material for a content palette in a character: the palette's sky tinted, with the character's details.</summary>
    public static ShaderMaterial SkyMaterial(PlanetPalette p, WorldCharacter c)
    {
        var look = Look(c);
        var m = SkyMaterial(p);
        var top = p.SkyTop.Lerp(look.SkyTint, look.SkyTintAmount);
        m.SetShaderParameter("top_color", top);
        m.SetShaderParameter("bottom_color", top.Darkened(0.4f));
        m.SetShaderParameter("nebula_a", p.Accent.Lerp(C(0.85f, 0.35f, 0.8f), 0.6f).Lerp(look.Accent, 0.35f));
        m.SetShaderParameter("star_brightness", 1.8f * look.Stars); // 1.8: the base brightness every character scales
        m.SetShaderParameter("dawn_strength", look.Dawn);
        m.SetShaderParameter("dawn_color", look.Sun.Lerp(C(1f, 0.55f, 0.35f), 0.5f));
        m.SetShaderParameter("aurora_strength", look.Aurora);
        m.SetShaderParameter("aurora_color", look.AuroraA);
        m.SetShaderParameter("aurora_color_b", look.AuroraB);
        m.SetShaderParameter("lattice_strength", look.Lattice);
        m.SetShaderParameter("scan_strength", look.Scan);
        m.SetShaderParameter("graph_strength", look.Graph);
        m.SetShaderParameter("graph_color", look.Accent);
        return m;
    }

    /// <summary>
    /// The environment for a content palette in a character: <see cref="SkyEnvironment(PlanetPalette)"/> with the
    /// character's sky, and the ambient light nudged towards a lightened sky tint (at 0.6 of the sky's tint amount, so the
    /// ground takes on the character's mood more gently than the sky does).
    /// </summary>
    public static Godot.Environment SkyEnvironment(PlanetPalette p, WorldCharacter c)
    {
        var env = SkyEnvironment(p);
        env.Sky.SkyMaterial = SkyMaterial(p, c);
        var look = Look(c);
        env.AmbientLightColor = p.Ambient.Lerp(look.SkyTint.Lightened(0.4f), look.SkyTintAmount * 0.6f);
        return env;
    }

    // ------------------------------------------------------------------ props

    /// <summary>
    /// The repository workbench (replaces the overlook on a repository world; same footprint, radius
    /// <see cref="OverlookRadiusMetres"/>, top at <paramref name="top"/> metres, so the walker's platform is unchanged): a
    /// planked deck on a stone drum, a bench with a vise and tools at the far (−Z) side, a lamp, a little glowing screen,
    /// and a brass "branch tree" with commit beads — the project's history as a sculpture. Base at y = 0 (the drum sinks
    /// 0.3 m below it).
    /// </summary>
    public static ArrayMesh Workbench(PlanetPalette palette, float top)
    {
        var accent = RepositoryTeal;
        var h = top;
        const float r = OverlookRadiusMetres;
        var b = new MeshBuilder();
        b.Lathe([new(0, -0.3f), new(r * 1.1f, -0.3f), new(r * 1.1f, 0.15f), new(r * 0.98f, 0.4f), new(r * 0.95f, h - 0.3f), new(r + 0.05f, h - 0.22f), new(r + 0.05f, h - 0.02f), new(r, h), new(r, h), new(0, h)],
            (_, i) => i < 3 ? StoneDark : i < 5 ? Stone : i < 7 ? Wood : WoodLight, 28);
        // Plank rings on the deck and a teal inlay ring (the "commit" ring).
        for (var k = 1; k <= 3; k++) b.Torus(T(new Vector3(0, h + 0.004f, 0)), r * k / 3.6f, 0.012f, Wood.Darkened(0.15f), 28, 3);
        b.Torus(T(new Vector3(0, h + 0.01f, 0)), r * 0.92f, 0.035f, Glowing(accent, 0.6f), 36, 4);
        // The bench: a thick top on four legs, at the far (−Z) side, a vise on its end, tools on top.
        var bench = new Vector3(0, h, -1.15f);
        b.RoundBox(bench + new Vector3(0, 0.82f, 0), new Vector3(1.9f, 0.12f, 0.7f), 0.03f, WoodLight);
        foreach (var x in new[] { -0.85f, 0.85f })
        {
            foreach (var z in new[] { -0.28f, 0.28f }) b.Cylinder(bench + new Vector3(x, 0, z), bench + new Vector3(x, 0.78f, z), 0.05f, 0.05f, Wood, 6);
        }

        b.Box(bench + new Vector3(1.0f, 0.82f, 0.25f), new Vector3(0.18f, 0.16f, 0.22f), Ink.Lightened(0.3f));
        b.Cylinder(bench + new Vector3(1.0f, 0.86f, 0.42f), bench + new Vector3(1.0f, 0.86f, 0.62f), 0.025f, 0.025f, Brass, 6);
        b.Cylinder(bench + new Vector3(-0.5f, 0.9f, 0.1f), bench + new Vector3(-0.1f, 0.9f, 0.2f), 0.02f, 0.02f, Brass, 6); // a screwdriver
        b.RoundBox(bench + new Vector3(-0.62f, 0.91f, -0.12f), new Vector3(0.28f, 0.05f, 0.2f), 0.02f, Cream);              // a notebook
        // A little screen on the bench, glowing with three lines of "code".
        b.RoundBox(bench + new Vector3(0.1f, 1.12f, -0.1f), new Vector3(0.62f, 0.4f, 0.05f), 0.02f, Ink, new Vector3(-12, 0, 0));
        for (var k = 0; k < 3; k++) b.Box(bench + new Vector3(0.0f + 0.04f * k, 1.2f - 0.08f * k, -0.07f + 0.017f * k), new Vector3(0.36f - 0.08f * k, 0.025f, 0.01f), Glowing(k == 1 ? accent : C(0.7f, 0.9f, 1f), 0.9f));
        // The lamp: an arm and a glowing shade.
        b.Cylinder(bench + new Vector3(-0.85f, 0.88f, -0.2f), bench + new Vector3(-0.8f, 1.5f, -0.1f), 0.025f, 0.025f, Brass, 6);
        b.Cylinder(bench + new Vector3(-0.8f, 1.5f, -0.1f), bench + new Vector3(-0.5f, 1.55f, 0.05f), 0.025f, 0.025f, Brass, 6);
        b.Ellipsoid(bench + new Vector3(-0.48f, 1.5f, 0.07f), new Vector3(0.14f, 0.1f, 0.14f), Glowing(palette.Glow), 10, 6);
        // The branch tree: a brass trunk with commit beads; a branch leaves and merges back.
        var root = new Vector3(1.25f, h, 0.75f);
        var trunk = new List<Vector3>();
        for (var k = 0; k <= 6; k++) trunk.Add(root + new Vector3(0, 0.3f * k, 0));
        b.Tube(trunk, _ => 0.045f, _ => Brass, 8);
        for (var k = 1; k <= 6; k++) b.Ellipsoid(trunk[k], new Vector3(0.09f, 0.09f, 0.09f), Glowing(k % 3 == 0 ? accent : C(1f, 0.85f, 0.5f), 0.85f), 10, 6);
        var branch = new List<Vector3> { trunk[1], trunk[1] + new Vector3(-0.35f, 0.3f, 0.1f), trunk[3] + new Vector3(-0.4f, 0.1f, 0.12f), trunk[4] };
        b.Tube(branch, _ => 0.035f, _ => Brass.Darkened(0.1f), 6);
        b.Ellipsoid(branch[1], new Vector3(0.075f, 0.075f, 0.075f), Glowing(C(1f, 0.6f, 0.85f), 0.85f), 8, 5);
        b.Ellipsoid(branch[2], new Vector3(0.075f, 0.075f, 0.075f), Glowing(C(1f, 0.6f, 0.85f), 0.85f), 8, 5);
        return Commit(b);
    }

    /// <summary>
    /// The Windows volume's gateway (≈ 7 m at full size; stands beside the arrival at 0.55, ≈ 3.9 m): a tall arch framing four
    /// panes of glass, lit from within — a gateway into the volume, in the planets' rounded style. Base at y = 0, the panes
    /// in the XY plane (it reads the same from either side).
    /// </summary>
    public static ArrayMesh PaneGateway(PlanetPalette palette)
    {
        var glass = C(0.45f, 0.8f, 1f);
        var frame = Stone.Lerp(C(0.7f, 0.82f, 1f), 0.4f);
        var b = new MeshBuilder();
        const float w = 2.6f, h = 6.4f, depth = 0.5f;
        // Two pillars and a lintel, rounded.
        foreach (var x in new[] { -w, w }) b.RoundBox(new Vector3(x, h / 2, 0), new Vector3(0.7f, h, depth + 0.2f), 0.18f, frame);
        b.RoundBox(new Vector3(0, h + 0.25f, 0), new Vector3(2 * w + 0.9f, 0.6f, depth + 0.3f), 0.2f, frame.Lightened(0.08f));
        b.Lathe([new(0, -0.3f), new(w + 1.4f, -0.3f), new(w + 1.4f, 0.15f), new(w + 1.1f, 0.3f), new(0, 0.3f)], (_, i) => StoneDark, 24);
        // Four panes in a 2 × 2 window, with mullions; the panes glow softly (they are the "window" into the volume).
        const float paneW = 2.3f, paneH = 2.75f, gap = 0.16f;
        for (var ix = 0; ix < 2; ix++)
        {
            for (var iy = 0; iy < 2; iy++)
            {
                var cx = (ix == 0 ? -1 : 1) * (paneW / 2 + gap / 2);
                var cy = 0.55f + paneH / 2 + iy * (paneH + gap);
                b.Box(new Vector3(cx, cy, 0), new Vector3(paneW, paneH, 0.06f), Glowing(glass.Lerp(C(0.7f, 0.6f, 1f), 0.25f * (ix + iy)), 0.55f));
            }
        }

        b.Box(new Vector3(0, 0.55f + paneH + gap / 2, 0), new Vector3(2 * paneW + gap, gap, 0.16f), frame.Darkened(0.05f));
        b.Box(new Vector3(0, 0.55f + paneH + gap / 2, 0), new Vector3(gap, 2 * paneH + gap, 0.16f), frame.Darkened(0.05f));
        // Lanterns on the pillars.
        foreach (var x in new[] { -w, w }) b.Ellipsoid(new Vector3(x, h + 0.75f, 0), new Vector3(0.28f, 0.28f, 0.28f), Glowing(glass.Lightened(0.3f)), 10, 6);
        return Commit(b);
    }

    /// <summary>
    /// The WSL distribution's terminal monolith (≈ 6 m; stands at 0.55, ≈ 3.5 m): a dark rounded slab with a warm phosphor
    /// prompt — a chevron and a cursor block — on its +Z face, and a ring of amber lamps at its foot. Base at y = 0. The
    /// cursor blinks (see <see cref="TerminalCursor"/>).
    /// </summary>
    public static ArrayMesh TerminalMonolith(PlanetPalette palette)
    {
        var amber = C(1f, 0.62f, 0.22f);
        var slab = C(0.2f, 0.12f, 0.2f);
        var b = new MeshBuilder();
        b.Lathe([new(0, -0.3f), new(2.6f, -0.3f), new(2.6f, 0.12f), new(2.3f, 0.3f), new(0, 0.3f)], (_, i) => StoneDark.Lerp(slab, 0.3f), 24);
        b.RoundBox(new Vector3(0, 3.3f, 0), new Vector3(3.4f, 6.0f, 0.9f), 0.35f, slab);
        b.RoundBox(new Vector3(0, 3.6f, 0.42f), new Vector3(2.8f, 3.4f, 0.12f), 0.15f, Ink.Darkened(0.4f));
        // The prompt: a chevron ">" made of two bars, and an underscore.
        b.RoundBox(new Vector3(-0.85f, 4.35f, 0.5f), new Vector3(0.75f, 0.16f, 0.06f), 0.04f, Glowing(amber), new Vector3(0, 0, -35));
        b.RoundBox(new Vector3(-0.85f, 3.95f, 0.5f), new Vector3(0.75f, 0.16f, 0.06f), 0.04f, Glowing(amber), new Vector3(0, 0, 35));
        for (var k = 0; k < 3; k++) b.Box(new Vector3(-0.6f + 0.55f * k, 3.0f - 0.32f * k, 0.5f), new Vector3(0.9f + 0.3f * (k % 2), 0.08f, 0.04f), Glowing(amber.Darkened(0.25f), 0.7f));
        for (var k = 0; k < 8; k++)
        {
            var a = k * Mathf.Tau / 8;
            b.Ellipsoid(new Vector3(Mathf.Sin(a) * 2.35f, 0.42f, Mathf.Cos(a) * 2.35f), new Vector3(0.16f, 0.16f, 0.16f), Glowing(amber), 8, 5);
        }

        return Commit(b);
    }

    /// <summary>
    /// The blinking cursor block of the terminal monolith, a separate mesh so it can blink (<see cref="PlanetVisual"/> toggles
    /// it); the position is in the monolith's local frame, just in front of the screen after the chevron.
    /// </summary>
    public static (ArrayMesh Mesh, Vector3 Position) TerminalCursor()
    {
        var b = new MeshBuilder();
        b.Box(Vector3.Zero, new Vector3(0.36f, 0.62f, 0.05f), Glowing(C(1f, 0.75f, 0.35f)));
        return (Commit(b), new Vector3(-0.05f, 4.15f, 0.52f));
    }

    /// <summary>
    /// A depth band's waymarker (≈ 1.8 m, stands beside the arrival): a cairn post with one glowing ring per band — the
    /// sign you see again and again on a deep journey, counting how far down you are; a teal bead on top inside a project.
    /// </summary>
    public static ArrayMesh Waymarker(int band, bool inRepository)
    {
        var col = BandColor(band);
        var b = new MeshBuilder();
        b.Lathe([new(0, -0.2f), new(0.55f, -0.2f), new(0.6f, 0.15f), new(0.42f, 0.32f), new(0, 0.34f)], (_, _) => StoneDark, 14);
        b.Cylinder(new Vector3(0, 0.3f, 0), new Vector3(0, 1.75f, 0), 0.13f, 0.1f, Wood, 8);
        for (var k = 0; k < Math.Clamp(band, 1, 4); k++) b.Torus(T(new Vector3(0, 0.62f + 0.28f * k, 0)), 0.2f, 0.045f, Glowing(col, 0.9f), 18, 5);
        b.Ellipsoid(new Vector3(0, 1.85f, 0), new Vector3(0.17f, 0.17f, 0.17f), inRepository ? Glowing(RepositoryTeal) : Glowing(col.Lightened(0.3f), 0.8f), 10, 6);
        return Commit(b);
    }
}
