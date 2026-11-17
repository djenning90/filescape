using System.Numerics;

namespace FileScape.Core.Planets;

/// <summary>What a piece of a Hazard World's ground carries (AD-59): it decides its size, its edge and how it is joined.</summary>
public enum HazardSiteKind
{
    /// <summary>The arrival meadow: land 0, where every visit starts.</summary>
    Arrival,

    /// <summary>The way back's launcher: joined to the arrival by a wide bridge without a gap (the first walk anyone takes).</summary>
    WayBack,

    /// <summary>A child folder's launcher.</summary>
    Launcher,

    /// <summary>The landmark and overlook: joined to the arrival by the ridge ahead-right, past the black hole's jet.</summary>
    Landmark,

    /// <summary>A plateau for the files (made here, sized for the groves).</summary>
    Plateau,

    /// <summary>An outcrop: extra ground made here so a world keeps its share of walkable surface.</summary>
    Outcrop,
}

/// <summary>
/// A destination the terrain must carry: its waypoint (where the route meets it), the centre of its land, how far round
/// that centre its content reaches (metres; the land's nearest tooth stays <see cref="HazardTerrain.EdgeMarginMetres"/>
/// beyond), and what it is.
/// </summary>
public readonly record struct HazardSite(Vector3 Waypoint, Vector3 Centre, float ContentMetres, HazardSiteKind Kind);

/// <summary>
/// Designs a Hazard World's broken shell around its destinations (PLANET-46, AD-59, amending AD-53): DJ's brief (2026-11-16)
/// was "land and immediately see the black hole, the missing surface, and the route", danger that feels close, and a
/// route a player can learn. So instead of a few holes cut out of a whole planet, the ground is what is left: one jagged
/// island per destination (the arrival, the way back, each launcher, the landmark and overlook, the file plateaus), a few
/// outcrops so the world keeps roughly a third of its surface (30–42 % by seed; more when crowded), all joined by narrow
/// bridges with drops on both sides — a spanning tree from the arrival, so every destination is reachable — with a gap
/// to jump on one to three of them. Straight ahead of the arrival the ground falls away into open sky, the black hole's jet
/// rises through that opening (<see cref="BrokenShell.JetAxis"/>), and a ridge with a gap runs ahead-right across it to the
/// landmark, so the danger and the route are the first things you see.
/// <para>
/// Deterministic: everything comes from the seed (the folder's name) and the sites. Planet frame (+Y the arrival pole,
/// azimuth 0 straight ahead), metres along the surface. What a change must preserve, all checked by HazardWorldTests:
/// every site's content stays <see cref="EdgeMarginMetres"/> inside its land; every bridge is at least
/// <see cref="MinBridgeWidthMetres"/> wide; every gap is between <see cref="MinJumpGapMetres"/> and
/// <see cref="MaxJumpGapMetres"/> long (within a walking jump with margin); the arrival reaches every site; the way back
/// is reached without a jump; the jet's opening is open sky.
/// </para>
/// </summary>
public static class HazardTerrain
{
    /// <summary>
    /// The least solid ground between what a land carries and its nearest tooth, metres: a body (0.35 m) and a stumble.
    /// Close on purpose (DJ: danger should feel close); the edge warning starts well before (4.5 m).
    /// </summary>
    public const float EdgeMarginMetres = 0.8f;

    /// <summary>
    /// The narrowest a bridge is, metres (its guaranteed width; the rag only adds). The walker falls when its centre is
    /// past an edge, so this is the width its centre has to stay within: ± 0.9 m, about a body and a half each side.
    /// </summary>
    public const float MinBridgeWidthMetres = 1.8f;

    /// <summary>The way back's bridge is this wide, metres: forgiving, the first walk anyone takes.</summary>
    public const float WayBackBridgeWidthMetres = 3.0f;

    /// <summary>The ridge from the arrival to the landmark is this wide, metres: narrow, straight ahead, past the jet.</summary>
    public const float RidgeWidthMetres = 2.0f;

    /// <summary>Other bridges are this wide plus up to <see cref="BridgeWidthSpreadMetres"/> (by seed), metres.</summary>
    private const float BridgeWidthSpreadMetres = 0.8f;

    /// <summary>
    /// A jump gap is this long (shortest), metres. The walker's held jump at walking pace carries ≈ 4.7 m (6 m/s for
    /// ≈ 0.78 s, JumpParams), so even the longest gap leaves more than 2 m of slack for when you press Space.
    /// </summary>
    public const float MinJumpGapMetres = 1.4f;

    /// <summary>The longest jump gap, metres (see <see cref="MinJumpGapMetres"/>; checked against the real walker by HazardWorldTests).</summary>
    public const float MaxJumpGapMetres = 2.2f;

    /// <summary>A gap keeps at least this much open bridge before and after it, metres: room to line up and to land.</summary>
    private const float JumpShoulderMetres = 1.5f;

    /// <summary>Lands placed here (plateaus, outcrops) keep this much open space from any other land's farthest tooth, metres.</summary>
    private const float LandGapMetres = 2.5f;

    /// <summary>Outcrops are placed about a bridge of this length from the existing ground, metres (they grow the world outwards gradually).</summary>
    private const float PreferredBridgeMetres = 5f;

    /// <summary>An outcrop's base radius: this plus up to <see cref="OutcropSpreadMetres"/>, metres (by seed).</summary>
    private const float OutcropMinMetres = 3.2f, OutcropSpreadMetres = 2.6f;

    /// <summary>The walkable share the outcrops make up to: this plus up to <see cref="WalkableSpread"/> (by seed). DJ: about a third can be dramatic.</summary>
    private const float WalkableTarget = 0.30f, WalkableSpread = 0.12f;

    /// <summary>The sample count for the walkable share while outcrops are added (coarse; the tests use 4000).</summary>
    private const int FractionSamples = 1500;

    /// <summary>
    /// The opening straight ahead: no plateau, outcrop or (unless it must) bridge within this azimuth of straight ahead,
    /// degrees, between the arrival's land and the landmark's. Launcher slots keep out of it too (PlanetComposer).
    /// </summary>
    public const float FrontOpeningHalfDeg = 50f;

    /// <summary>
    /// The jet leaves the shell straight ahead of the arrival, this far beyond the arrival land's farthest tooth, metres: it
    /// climbs out of the opening in front of you as you land (2026-11-16, first screenshots: off to one side, it crossed
    /// the sky as a flat line and did not read as the black hole's).
    /// </summary>
    private const float JetBeyondArrivalMetres = 3.5f;

    /// <summary>The jet's bearing from straight ahead, degrees (a little left: the ridge runs ahead-right, and the jet keeps <see cref="JetClearMetres"/> of open sky from it).</summary>
    private const float JetBearingDeg = -8f;

    /// <summary>Open sky round each of the jet's two openings, metres (no land or bridge within).</summary>
    public const float JetClearMetres = 3.5f;

    /// <summary>A bridge whose straight way crosses the front opening or a jet's opening counts this many times its length when choosing the tree (it may still be chosen when nothing else connects).</summary>
    private const float OpeningPenalty = 3f;

    /// <summary>
    /// Designs the shell. <paramref name="sites"/> must start with the arrival; plateaus for the files are placed here,
    /// one per entry of <paramref name="plateauContentMetres"/> (largest first), and returned in that order. Gaps to jump
    /// go on one to three bridges (more on a crowded world).
    /// </summary>
    public static (BrokenShell Shell, IReadOnlyList<Vector3> Plateaus) Design(float radius, int seed, IReadOnlyList<HazardSite> sites,
        IReadOnlyList<float> plateauContentMetres)
    {
        if (sites.Count == 0 || sites[0].Kind != HazardSiteKind.Arrival) throw new ArgumentException("the arrival comes first", nameof(sites));
        var rng = new Random(seed ^ 0x4A2D);
        var all = new List<HazardSite>(sites);
        var lands = new List<ShellLand>();
        foreach (var s in sites) lands.Add(LandFor(s, radius, rng.Next()));

        // ---- the opening ahead and the jet through it
        var landmark = sites.FirstOrDefault(s => s.Kind == HazardSiteKind.Landmark);
        var hasLandmark = landmark.Kind == HazardSiteKind.Landmark;
        var arrivalReach = lands[0].MaxRadiusRadians * radius;
        var openingFar = hasLandmark ? PlanetShape.Angle(Vector3.UnitY, landmark.Centre) * radius - lands[sites.ToList().IndexOf(landmark)].MinRadiusRadians * radius : arrivalReach + 10f;
        var jetPolar = (arrivalReach + JetBeyondArrivalMetres) / radius;
        var jetExit = PlanetFrame.FromPolar(jetPolar, JetBearingDeg * MathF.PI / 180f);
        Vector3[] jetExits = [jetExit, -jetExit];

        bool InOpening(Vector3 c, float reachMetres)
        {
            var polar = PlanetFrame.PolarOf(c) * radius;
            if (polar + reachMetres < arrivalReach || polar - reachMetres > openingFar) return false;
            var az = MathF.Abs(MathF.IEEERemainder(PlanetFrame.AzimuthOf(c), 2 * MathF.PI)) * 180f / MathF.PI;
            var ring = MathF.Max(1f, MathF.Sin(PlanetFrame.PolarOf(c)) * radius);
            return az - reachMetres / ring * 180f / MathF.PI < FrontOpeningHalfDeg;
        }

        bool FitsLand(Vector3 c, float maxReachMetres)
        {
            foreach (var l in lands)
            {
                if (PlanetShape.Angle(c, l.Centre) * radius < maxReachMetres + l.MaxRadiusRadians * radius + LandGapMetres) return false;
            }

            foreach (var j in jetExits)
            {
                if (PlanetShape.Angle(c, j) * radius < maxReachMetres + JetClearMetres) return false;
            }

            return !InOpening(c, maxReachMetres);
        }

        // ---- plateaus for the files: nearest the arrival that fits (destinations stay close), largest first
        var candidates = PlanetComposer.FibonacciSphere(600);
        var plateaus = new List<Vector3>();
        foreach (var content in plateauContentMetres)
        {
            var landSeed = rng.Next();
            var baseMetres = (content + EdgeMarginMetres) / (1 - ShellLand.Wobble * PlateauRoughness);
            var reach = baseMetres * (1 + ShellLand.Wobble * PlateauRoughness);
            var best = -1;
            for (var i = 0; i < candidates.Length; i++)
            {
                if (best >= 0 && candidates[i].Y <= candidates[best].Y) continue; // nearest the arrival pole wins
                if (FitsLand(candidates[i], reach)) best = i;
            }

            if (best < 0) continue; // no room left (cannot happen at these radii): the files fall back on whatever ground there is
            var c = candidates[best];
            plateaus.Add(c);
            all.Add(new HazardSite(c, c, content, HazardSiteKind.Plateau));
            lands.Add(new ShellLand(c, baseMetres / radius, landSeed, PlateauRoughness));
        }

        // ---- outcrops, until the world keeps its share of ground (estimated from the lands alone; bridges add a little)
        var target = WalkableTarget + WalkableSpread * (float)rng.NextDouble();
        float LandFraction()
        {
            var pts = PlanetComposer.FibonacciSphere(FractionSamples);
            var n = 0;
            foreach (var p in pts)
            {
                foreach (var l in lands)
                {
                    if (l.Contains(p)) { n++; break; }
                }
            }

            return n / (float)pts.Length;
        }

        var fraction = LandFraction();
        for (var guard = 0; guard < 40 && fraction < target; guard++)
        {
            var baseMetres = OutcropMinMetres + OutcropSpreadMetres * (float)rng.NextDouble();
            var reach = baseMetres * (1 + ShellLand.Wobble);
            var best = -1;
            var bestScore = float.MaxValue;
            for (var i = 0; i < candidates.Length; i++)
            {
                if (!FitsLand(candidates[i], reach)) continue;
                var gap = float.MaxValue;
                foreach (var l in lands) gap = MathF.Min(gap, PlanetShape.Angle(candidates[i], l.Centre) * radius - l.MaxRadiusRadians * radius - reach);
                var score = MathF.Abs(gap - PreferredBridgeMetres);
                if (score < bestScore) { bestScore = score; best = i; }
            }

            if (best < 0) break;
            var c = candidates[best];
            all.Add(new HazardSite(c, c, 0, HazardSiteKind.Outcrop));
            lands.Add(new ShellLand(c, baseMetres / radius, rng.Next()));
            fraction = LandFraction();
        }

        // ---- the tree: the way back and the ridge first, then the cheapest bridge to a land not yet joined (Prim)
        var n = lands.Count;
        float Cost(int a, int b)
        {
            var wa = all[a].Waypoint;
            var wb = all[b].Waypoint;
            var centres = PlanetShape.Angle(lands[a].Centre, lands[b].Centre) * radius;
            var open = MathF.Max(0, centres - (lands[a].MaxRadiusRadians + lands[b].MaxRadiusRadians) * radius);
            var cost = open + 0.1f * PlanetShape.Angle(wa, wb) * radius;
            if (CrossesOpening(wa, wb)) cost *= OpeningPenalty;
            return cost;
        }

        bool CrossesOpening(Vector3 a, Vector3 b)
        {
            foreach (var j in jetExits)
            {
                if (PlanetFrame.DistanceToArc(j, a, b, radius) < JetClearMetres + MinBridgeWidthMetres) return true;
            }

            var steps = Math.Max(2, (int)(PlanetShape.Angle(a, b) * radius / 1.5f));
            for (var i = 1; i < steps; i++)
            {
                var p = Vector3.Normalize(Vector3.Lerp(a, b, i / (float)steps));
                if (InOpening(p, 0) && !lands.Any(l => l.Contains(p))) return true;
            }

            return false;
        }

        var joined = new bool[n];
        joined[0] = true;
        var edges = new List<(int A, int B, HazardSiteKind Role)>();
        for (var i = 1; i < n; i++)
        {
            if (all[i].Kind is HazardSiteKind.WayBack or HazardSiteKind.Landmark)
            {
                edges.Add((0, i, all[i].Kind));
                joined[i] = true;
            }
        }

        while (joined.Any(j => !j))
        {
            var best = (A: -1, B: -1, Cost: float.MaxValue);
            for (var a = 0; a < n; a++)
            {
                if (!joined[a]) continue;
                for (var b = 0; b < n; b++)
                {
                    if (joined[b]) continue;
                    var c = Cost(a, b);
                    if (c < best.Cost) best = (a, b, c);
                }
            }

            edges.Add((best.A, best.B, HazardSiteKind.Launcher));
            joined[best.B] = true;
        }

        // ---- gaps to jump: on bridges with a long enough open stretch (never the way back): the ridge first (the jump you
        // see from the arrival, on the way to the overlook), then others by seed
        var crowded = sites.Count(s => s.Kind == HazardSiteKind.Launcher) >= 8;
        var wanted = 1 + (rng.NextDouble() < 0.5 ? 1 : 0) + (crowded ? 1 : 0);
        var gapPlan = new Dictionary<int, (float From, float To)>();
        var order = Enumerable.Range(0, edges.Count).OrderBy(e => edges[e].Role == HazardSiteKind.Landmark ? -1 : rng.Next()).ToList();
        foreach (var e in order)
        {
            if (gapPlan.Count >= wanted) break;
            var (a, b, role) = edges[e];
            if (role == HazardSiteKind.WayBack) continue;
            var length = PlanetShape.Angle(all[a].Waypoint, all[b].Waypoint) * radius;
            var (s0, s1) = OpenStretch(all[a].Waypoint, all[b].Waypoint, length, radius, lands);
            var gap = MinJumpGapMetres + (MaxJumpGapMetres - MinJumpGapMetres) * (float)rng.NextDouble();
            if (s1 - s0 < gap + 2 * JumpShoulderMetres) continue;
            var mid = (s0 + s1) / 2;
            gapPlan[e] = (mid - gap / 2, mid + gap / 2);
        }

        // ---- bridges
        var spans = new List<ShellSpan>();
        var links = new List<ShellLink>();
        for (var e = 0; e < edges.Count; e++)
        {
            var (a, b, role) = edges[e];
            var width = role switch
            {
                HazardSiteKind.WayBack => WayBackBridgeWidthMetres,
                HazardSiteKind.Landmark => RidgeWidthMetres,
                _ => MinBridgeWidthMetres + BridgeWidthSpreadMetres * (float)rng.NextDouble(),
            };
            var gaps = gapPlan.TryGetValue(e, out var g) ? new[] { g } : [];
            spans.Add(new ShellSpan(all[a].Waypoint, all[b].Waypoint, width / 2, radius, rng.Next(), gaps));
            links.Add(new ShellLink(a, b, e, gaps.Length > 0));
        }

        var waypoints = all.Select(s => s.Waypoint).ToList();
        var jetAxis = Vector3.Normalize(jetExit);
        var shell = new BrokenShell(lands, waypoints, spans, links, jetAxis, radius);

        // A gap that another bridge or land happens to cover is not a gap: make that bridge whole again.
        var fixedSpans = spans.ToList();
        var fixedLinks = links.ToList();
        var changed = false;
        for (var k = 0; k < spans.Count; k++)
        {
            foreach (var (g0, g1) in spans[k].Gaps)
            {
                var probe = spans[k].PointAlong((g0 + g1) / 2);
                if (!shell.OverHole(probe))
                {
                    fixedSpans[k] = new ShellSpan(spans[k].A, spans[k].B, spans[k].HalfWidthMetres, radius, spans[k].Seed);
                    fixedLinks[k] = links[k] with { Jump = false };
                    changed = true;
                }
            }
        }

        if (changed) shell = new BrokenShell(lands, waypoints, fixedSpans, fixedLinks, jetAxis, radius);
        return (shell, plateaus);
    }

    /// <summary>A plateau's edge is this rough (a fraction of the full <see cref="ShellLand.Wobble"/>): files want room more than teeth.</summary>
    private const float PlateauRoughness = 0.6f;

    /// <summary>The landmark's land is this rough: it carries the overlook's ring of stones, so its edge stays near round.</summary>
    private const float LandmarkRoughness = 0.5f;

    /// <summary>The land for a site: big enough that its nearest tooth keeps <see cref="EdgeMarginMetres"/> beyond the content.</summary>
    private static ShellLand LandFor(HazardSite s, float radius, int seed)
    {
        var roughness = s.Kind switch
        {
            HazardSiteKind.Landmark => LandmarkRoughness,
            HazardSiteKind.Plateau => PlateauRoughness,
            _ => 1f,
        };
        var baseMetres = (s.ContentMetres + EdgeMarginMetres) / (1 - ShellLand.Wobble * roughness);
        return new ShellLand(s.Centre, baseMetres / radius, seed, roughness);
    }

    /// <summary>
    /// The longest stretch of the straight way a→b (metres along it from a) that no land covers: where a bridge is a
    /// bridge, with drops on both sides.
    /// </summary>
    private static (float From, float To) OpenStretch(Vector3 a, Vector3 b, float length, float radius, List<ShellLand> lands)
    {
        const float step = 0.25f; // metres
        var span = new ShellSpan(a, b, 1f, radius, 0);
        (float, float) best = (0, 0);
        var start = -1f;
        for (var s = 0f; s <= length; s += step)
        {
            var p = span.PointAlong(s);
            var covered = false;
            foreach (var l in lands)
            {
                if (l.Contains(p)) { covered = true; break; }
            }

            if (!covered && start < 0) start = s;
            if ((covered || s + step > length) && start >= 0)
            {
                if (s - start > best.Item2 - best.Item1) best = (start, s);
                start = -1;
            }
        }

        return best;
    }
}
