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

    /// <summary>A fragment: an outcrop broken off a bigger island, a plain hop from it (AD-65); never bridged, reached by jumping.</summary>
    Fragment,
}

/// <summary>
/// A destination the terrain must carry: its waypoint (where the route meets it), the centre of its land, how far round
/// that centre its content reaches (metres; the land's nearest tooth stays <see cref="HazardTerrain.EdgeMarginMetres"/>
/// beyond), and what it is.
/// </summary>
public readonly record struct HazardSite(Vector3 Waypoint, Vector3 Centre, float ContentMetres, HazardSiteKind Kind);

/// <summary>
/// Designs a Hazard World's broken shell around its destinations (PLANET-46, AD-59, amending AD-53; PLANET-61, PLANET-63,
/// AD-65): DJ's brief (2026-11-16) was "land and immediately see the black hole, the missing surface, and the route",
/// danger that feels close, and a route a player can learn. So instead of a few holes cut out of a whole planet, the ground
/// is what is left: one jagged island per destination (the arrival, the way back, each launcher, the landmark and overlook,
/// the file plateaus), outcrops — most of them fragments broken off a bigger island, a plain hop away, the rest far out —
/// so the world keeps roughly a third of its surface (30–42 % by seed; more when crowded), all joined by narrow bridges
/// with drops on both sides — a spanning tree from the arrival, so every destination is reachable — with a gap to jump on
/// one to three of them. Straight ahead of the arrival the ground falls away into open sky, the black hole's jet rises
/// through that opening (<see cref="BrokenShell.JetAxis"/>), and a ridge with a gap runs ahead-right across it to the
/// landmark, so the danger and the route are the first things you see.
/// <para>
/// <b>Three kinds of gap</b> (AD-65; DJ 2026-11-17: "some gaps look jumpable from the takeoff edge even though I can't
/// make them … distinguish gaps a normal jump can clear, gaps that reward an advanced move, and gaps that are there to
/// steer me toward a bridge"), sized against the real walker's envelope at walking pace (HazardMovementTests: a plain jump
/// clears 3.9 m whenever you press, 5.2 m at best; with the recovery twirl 6.7 m; a twirl at the top of the jump 7.9 m,
/// later 9.2 m): <b>steps</b> (<see cref="ShellGapClass.Plain"/>, <see cref="MinJumpGapMetres"/>–<see cref="MaxJumpGapMetres"/>,
/// a ledge to land on) on the tree's bridges; <b>leaps</b> (<see cref="ShellGapClass.Advanced"/>,
/// <see cref="LeapMinMetres"/>–<see cref="LeapMaxMetres"/>, a marked take-off board and a wide landing ledge) only on an
/// extra bridge that shortens a long way round, never on the only way; and <b>chasms</b>: wherever two pieces of ground
/// face each other across open sky with a long walk between them, the sky is either a plain hop
/// (≤ <see cref="HopMaxMetres"/>) or at least <see cref="SteeringMinMetres"/> — beyond everything at walking pace — so it
/// reads as "find the bridge". Pieces that would face each other across the misleading middle grow spurs towards each other
/// until the sky between them is a hop.
/// </para>
/// <para>
/// Deterministic: everything comes from the seed (the folder's name) and the sites. Planet frame (+Y the arrival pole,
/// azimuth 0 straight ahead), metres along the surface. What a change must preserve, all checked by HazardWorldTests and
/// HazardMovementTests: every site's content stays <see cref="EdgeMarginMetres"/> inside its land; every bridge is at least
/// <see cref="MinBridgeWidthMetres"/> wide; every step is between <see cref="MinJumpGapMetres"/> and
/// <see cref="MaxJumpGapMetres"/> long (a plain jump with margin) and every leap between <see cref="LeapMinMetres"/> and
/// <see cref="LeapMaxMetres"/>; the arrival reaches every site without a leap; the way back is reached without a jump; no
/// tempting open sky between <see cref="HopMaxMetres"/> and <see cref="SteeringMinMetres"/> is left; the jet's opening is
/// open sky.
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
    /// A step (a plain gap) is this long at its shortest, metres. A plain jump at walking pace clears 3.9 m wherever you press
    /// Space within 1.3 m of the edge (HazardMovementTests), so even the longest step (with its jagged ends) leaves a metre
    /// of slack.
    /// </summary>
    public const float MinJumpGapMetres = 1.4f;

    /// <summary>The longest step, metres, before its jagged ends (each adds up to <see cref="ShellSpan.GapJagMetres"/>).</summary>
    public const float MaxJumpGapMetres = 2.2f;

    /// <summary>A step keeps at least this much open bridge before and after it, metres: room to line up and to land.</summary>
    private const float JumpShoulderMetres = 1.5f;

    /// <summary>
    /// A leap (an advanced gap) is at least this long, metres: beyond the best plain jump at walking pace (5.2 m) and the
    /// plain jump saved by a recovery twirl (6.8 m with the catch's 1.5 m reach, 2026-11-17; leaps were 6.7–7.0 m with 1.1 m) (HazardMovementTests).
    /// </summary>
    public const float LeapMinMetres = 6.9f;

    /// <summary>The longest leap, metres: a twirl at the top of a walking jump clears it with room (7.9 m; HazardMovementTests).</summary>
    public const float LeapMaxMetres = 7.2f;

    /// <summary>A leap keeps at least this much bridge before and after it, metres, and at most <see cref="LeapShoulderMaxMetres"/>: a run-up and a ledge.</summary>
    private const float LeapShoulderMinMetres = 1.4f, LeapShoulderMaxMetres = 4.5f;

    /// <summary>A leap's bridge is this wide (before its ledges), metres.</summary>
    private const float LeapBridgeWidthMetres = 2.2f;

    /// <summary>
    /// A leap is worth its bridge when walking round is at least this many times longer than its straight way, plus
    /// <see cref="LeapDetourSlackMetres"/> (a shortcut, not a second way to the same place).
    /// </summary>
    private const float LeapDetourRatio = 1.6f, LeapDetourSlackMetres = 6f;

    /// <summary>
    /// Open sky between two pieces of ground with a long walk between them may be at most this, metres: a plain hop (a plain
    /// jump clears 3.9 m wherever you press; a tap 3.5 m).
    /// </summary>
    public const float HopMaxMetres = 2.6f;

    /// <summary>
    /// …or at least this, metres: a chasm, beyond everything at walking pace (a late twirl 9.2 m; a twirl and a recovery
    /// 9.2 m; HazardMovementTests), so it reads as "find the bridge". A running twirl can still carry further (≈ 15 m): an
    /// expert's stunt, never needed.
    /// </summary>
    public const float SteeringMinMetres = 11f;

    /// <summary>A spur leaves a hop of this much (plus up to <see cref="SpurHopSpreadMetres"/> by seed), metres.</summary>
    private const float SpurHopMetres = 1.5f, SpurHopSpreadMetres = 0.6f;

    /// <summary>A spur's tip is about this wide each side, metres, plus this fraction of its length (a broad tongue of rock, not a needle).</summary>
    private const float SpurTipHalfMetres = 1.2f, SpurWidthPerLength = 0.35f;

    /// <summary>A spur's side keeps this far outside a bridge's edge beside its gap, metres, so it never closes the gap (it may reach the gap's ledges).</summary>
    private const float SpurGapClearMetres = 0.3f;

    /// <summary>
    /// A step (other than the ridge's) or a leap goes only where other ground keeps at least this far from it and its
    /// ledges, metres: what stands nearer faces it across a gap that spurs then turn into a hop (and a spur cannot grow
    /// into a gap), so nearer still would leave a misleading gap.
    /// </summary>
    private const float GapClearanceMetres = 6f;

    /// <summary>The same for a leap's open stretch (its shoulders and gap), metres: a leap's bridge only stands where there is room for one.</summary>
    private const float LeapClearanceMetres = 4f;

    /// <summary>Rounds of growing spurs (each looks again: a spur can bring a third piece closer).</summary>
    private const int SpurRounds = 6;

    /// <summary>Lands placed here (plateaus, far outcrops) keep this much open space from any other land's farthest tooth, metres.</summary>
    private const float LandGapMetres = 2.5f;

    /// <summary>A fragment (an outcrop broken off a bigger island) keeps this crack from it, metres, plus up to <see cref="FragmentCrackSpreadMetres"/> by seed: a plain hop.</summary>
    private const float FragmentCrackMetres = 1.3f, FragmentCrackSpreadMetres = 0.8f;

    /// <summary>What an outcrop is (AD-65): a lobe grown onto an island, a fragment a crack (a hop) away, or an island far out (a chasm away).</summary>
    private enum OutcropKind { Lobe, Crack, Far }

    /// <summary>Of the outcrops, this share are lobes grown onto an island and this share fragments a crack away; the rest stand far out, <see cref="SteeringMinMetres"/> or more from everything.</summary>
    private const float LobeShare = 0.7f, CrackShare = 0f;

    /// <summary>A lobe overlaps the island it grows on by about this fraction of its own reach (at most <see cref="LobeOverlapMax"/>).</summary>
    private const float LobeOverlap = 0.45f, LobeOverlapMax = 0.8f;

    /// <summary>An outcrop's base radius: this plus up to <see cref="OutcropSpreadMetres"/>, metres (by seed).</summary>
    private const float OutcropMinMetres = 3.2f, OutcropSpreadMetres = 2.6f;

    /// <summary>A fragment's base radius: this plus up to <see cref="FragmentSpreadMetres"/>, metres (by seed): smaller pieces of rock.</summary>
    private const float FragmentMinMetres = 2.6f, FragmentSpreadMetres = 1.8f;

    /// <summary>
    /// The walkable share of the lands alone that the outcrops make up to: this plus up to <see cref="WalkableSpread"/> (by
    /// seed). DJ: about a third can be dramatic. Bridges, ledges and spurs add a few per cent (0.30 + 0.12 before AD-65).
    /// </summary>
    private const float WalkableTarget = 0.27f, WalkableSpread = 0.10f;

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
    /// one per entry of <paramref name="plateauContentMetres"/> (largest first), and returned in that order. Steps go on one
    /// to three bridges (more on a crowded world), a leap on an extra bridge where one fits.
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

        bool NearJet(Vector3 c, float reachMetres)
        {
            foreach (var j in jetExits)
            {
                if (PlanetShape.Angle(c, j) * radius < reachMetres + JetClearMetres) return true;
            }

            return false;
        }

        bool FitsLand(Vector3 c, float maxReachMetres, float gapMetres)
        {
            foreach (var l in lands)
            {
                if (PlanetShape.Angle(c, l.Centre) * radius < maxReachMetres + l.MaxRadiusRadians * radius + gapMetres) return false;
            }

            return !NearJet(c, maxReachMetres) && !InOpening(c, maxReachMetres);
        }

        // ---- plateaus for the files: nearest the arrival that fits (destinations stay close), largest first
        var candidates = PlanetComposer.FibonacciSphere(600);
        var plateaus = new List<Vector3>();
        foreach (var content in plateauContentMetres)
        {
            var landSeed = rng.Next();
            var wobble = ShellLand.WobbleFor(landSeed) * PlateauRoughness;
            var baseMetres = (content + EdgeMarginMetres) / (1 - wobble);
            var reach = baseMetres * (1 + wobble);
            var best = -1;
            for (var i = 0; i < candidates.Length; i++)
            {
                if (best >= 0 && candidates[i].Y <= candidates[best].Y) continue; // nearest the arrival pole wins
                if (FitsLand(candidates[i], reach, LandGapMetres)) best = i;
            }

            if (best < 0) continue; // no room left (cannot happen at these radii): the files fall back on whatever ground there is
            var c = candidates[best];
            plateaus.Add(c);
            all.Add(new HazardSite(c, c, content, HazardSiteKind.Plateau));
            lands.Add(new ShellLand(c, baseMetres / radius, landSeed, PlateauRoughness));
        }

        // ---- outcrops, until the world keeps its share of ground (estimated from the lands alone; bridges add a little):
        // most are fragments a plain hop off an island (broken ground), the rest far out, a chasm away from everything
        var target = WalkableTarget + WalkableSpread * (float)rng.NextDouble();
        var fractionPoints = PlanetComposer.FibonacciSphere(FractionSamples);
        var covered = new bool[fractionPoints.Length];
        var coveredCount = 0;
        float Cover(ShellLand l)
        {
            var cosReach = MathF.Cos(MathF.Min(MathF.PI, l.MaxRadiusRadians));
            for (var i = 0; i < fractionPoints.Length; i++)
            {
                if (covered[i] || Vector3.Dot(fractionPoints[i], l.Centre) < cosReach || !l.Contains(fractionPoints[i])) continue;
                covered[i] = true;
                coveredCount++;
            }

            return coveredCount / (float)fractionPoints.Length;
        }

        var fraction = 0f;
        foreach (var l in lands) fraction = Cover(l);
        for (var guard = 0; guard < 60 && fraction < target; guard++)
        {
            // Three kinds: a lobe grown onto an island (its outline more broken, no new face across the sky), a fragment a
            // crack away (a plain hop), or an island far out (a chasm away from everything, joined by a bridge).
            var roll = rng.NextDouble();
            var kind = roll < LobeShare ? OutcropKind.Lobe : roll < LobeShare + CrackShare ? OutcropKind.Crack : OutcropKind.Far;
            var fragment = kind != OutcropKind.Far;
            var baseMetres = kind == OutcropKind.Far ? OutcropMinMetres + OutcropSpreadMetres * (float)rng.NextDouble() : FragmentMinMetres + FragmentSpreadMetres * (float)rng.NextDouble();
            var landSeed = rng.Next();
            var reach = baseMetres * (1 + ShellLand.WobbleFor(landSeed));
            var want = kind switch
            {
                OutcropKind.Lobe => -LobeOverlap * reach,
                OutcropKind.Crack => FragmentCrackMetres + FragmentCrackSpreadMetres * (float)rng.NextDouble(),
                _ => SteeringMinMetres + 1.5f,
            };
            var minGap = kind switch
            {
                OutcropKind.Lobe => -LobeOverlapMax * reach,
                OutcropKind.Crack => FragmentCrackMetres,
                _ => SteeringMinMetres,
            };
            var best = -1;
            var bestScore = float.MaxValue;
            for (var i = 0; i < candidates.Length; i++)
            {
                if (!FitsLand(candidates[i], reach, minGap)) continue;
                var gap = float.MaxValue;
                foreach (var l in lands) gap = MathF.Min(gap, PlanetShape.Angle(candidates[i], l.Centre) * radius - l.MaxRadiusRadians * radius - reach);
                var score = MathF.Abs(gap - want);
                if (score < bestScore) { bestScore = score; best = i; }
            }

            if (best < 0) continue;
            var c = candidates[best];
            all.Add(new HazardSite(c, c, 0, fragment ? HazardSiteKind.Fragment : HazardSiteKind.Outcrop));
            lands.Add(new ShellLand(c, baseMetres / radius, landSeed));
            fraction = Cover(lands[^1]);
        }

        // ---- the tree: the way back and the ridge first, then the cheapest bridge to a land not yet joined (Prim)
        var nLands = lands.Count;
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

        var joined = new bool[nLands];
        joined[0] = true;
        for (var i = 1; i < nLands; i++) joined[i] = all[i].Kind == HazardSiteKind.Fragment; // reached by a hop, never a bridge
        var edges = new List<(int A, int B, HazardSiteKind Role)>();
        for (var i = 1; i < nLands; i++)
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
            for (var a = 0; a < nLands; a++)
            {
                if (!joined[a] || all[a].Kind == HazardSiteKind.Fragment) continue;
                for (var b = 0; b < nLands; b++)
                {
                    if (joined[b]) continue;
                    var c = Cost(a, b);
                    if (c < best.Cost) best = (a, b, c);
                }
            }

            edges.Add((best.A, best.B, HazardSiteKind.Launcher));
            joined[best.B] = true;
        }

        // ---- steps: on bridges with a long enough open stretch (never the way back): the ridge first (the jump you see
        // from the arrival, on the way to the overlook), then others by seed
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
            // A step reads as a step only with open sky round it: other ground nearer than a chasm would face it across a
            // misleading gap that no spur can fix without closing the step (the ridge's step is kept regardless: it crosses
            // the black hole's window, which is open by design).
            if (role != HazardSiteKind.Landmark && Clearance(all[a].Waypoint, all[b].Waypoint, mid - gap / 2 - ShellSpan.LedgeLengthMetres, mid + gap / 2 + ShellSpan.LedgeLengthMetres, a, b, lands, edges, all, radius) < GapClearanceMetres) continue;
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
        var shell = CloseCoveredGaps(new BrokenShell(lands, waypoints, spans, links, jetAxis, radius) { FrontOpening = (arrivalReach, openingFar, FrontOpeningHalfDeg) });

        // ---- leaps: an extra bridge with an advanced gap where a long way round could be cut short
        shell = AddLeaps(shell, all, rng, crowded ? 2 : 1, CrossesOpening);

        // ---- spurs, so no gap misleads: open sky between pieces with a long walk between them is a hop or a chasm
        bool Blocked(Vector3 p, float reach) => NearJet(p, reach) || InOpening(p, reach);
        var beforeSpurs = shell;
        shell = GrowSpurs(shell, rng, Blocked);
        // A leap that leaves a misleading gap beside it (a spur cannot grow into its gap) is not worth it: without it, again.
        var leftOver = Misleading(shell).Select(m => (ShellSeparations.SpanOf(shell, m.PieceA).Span, ShellSeparations.SpanOf(shell, m.PieceB).Span))
            .SelectMany(p => new[] { p.Item1, p.Item2 }).Where(k => k >= 0 && shell.Spans[k].GapClass == ShellGapClass.Advanced && shell.Spans[k].Gaps.Count > 0)
            .Select(k => shell.Spans[k]).ToHashSet();
        if (leftOver.Count > 0)
        {
            var keptSpans = beforeSpurs.Spans.Where(s => !leftOver.Any(l => l.A == s.A && l.B == s.B && l.GapClass == s.GapClass)).ToList();
            var keptLinks = new List<ShellLink>();
            foreach (var l in beforeSpurs.Links)
            {
                var span = beforeSpurs.Spans[l.Span];
                var k = keptSpans.IndexOf(span);
                if (k >= 0) keptLinks.Add(l with { Span = k });
            }

            shell = GrowSpurs(beforeSpurs.With(spans: keptSpans, links: keptLinks), rng, Blocked);
        }
        return (CloseCoveredGaps(shell), plateaus);
    }

    /// <summary>The open sky left misleading (ShellSeparations): longer than a hop, shorter than a chasm, with a long walk round, and neither a bridge's own gap nor the black hole's window.</summary>
    public static IEnumerable<ShellSeparation> Misleading(BrokenShell shell) =>
        ShellSeparations.Find(shell, SteeringMinMetres, HopMaxMetres)
            .Where(s => !s.Designed && !s.Opening && s.Tempting && s.Metres > HopMaxMetres && s.Metres < SteeringMinMetres);

    /// <summary>A gap that another bridge or land happens to cover is not a gap: that bridge is made whole again (a leap's bridge is dropped).</summary>
    private static BrokenShell CloseCoveredGaps(BrokenShell shell)
    {
        var spans = shell.Spans.ToList();
        var links = shell.Links.ToList();
        var changed = false;
        var drop = new List<int>();
        for (var k = 0; k < spans.Count; k++)
        {
            foreach (var (g0, g1) in spans[k].Gaps)
            {
                var probe = spans[k].PointAlong((g0 + g1) / 2);
                var width = spans[k].HalfWidthMetres * 0.8f;
                var across = Vector3.Cross(spans[k].DirectionAt(probe), probe);
                var covered = !shell.OverHole(probe) || !shell.OverHole(PlanetFrame.Move(probe, across, width, shell.Radius)) || !shell.OverHole(PlanetFrame.Move(probe, -across, width, shell.Radius));
                if (!covered) continue;
                changed = true;
                if (spans[k].GapClass == ShellGapClass.Advanced) { drop.Add(k); break; }
                spans[k] = spans[k].Whole();
                var li = links.FindIndex(l => l.Span == k);
                if (li >= 0) links[li] = links[li] with { Jump = false, Advanced = false };
                break;
            }
        }

        if (!changed) return shell;
        foreach (var k in drop.OrderByDescending(k => k))
        {
            spans.RemoveAt(k);
            links.RemoveAll(l => l.Span == k);
            for (var i = 0; i < links.Count; i++) if (links[i].Span > k) links[i] = links[i] with { Span = links[i].Span - 1 };
        }

        return shell.With(spans: spans, links: links);
    }

    /// <summary>
    /// Adds up to <paramref name="wanted"/> leap bridges (<see cref="ShellGapClass.Advanced"/>): between two lands not
    /// already joined, where the straight way between their waypoints crosses one stretch of open sky long enough for a leap
    /// and its shoulders and no other ground, it keeps out of the openings, and walking round is a long way
    /// (<see cref="LeapDetourRatio"/>). The route never takes them (<see cref="ShellLink.Advanced"/>).
    /// </summary>
    private static BrokenShell AddLeaps(BrokenShell shell, List<HazardSite> all, Random rng, int wanted, Func<Vector3, Vector3, bool> crossesOpening)
    {
        var r = shell.Radius;
        var lands = shell.Lands.ToList();
        var found = new List<(int A, int B, float S0, float S1, float Score)>();
        for (var a = 0; a < lands.Count; a++)
        {
            for (var b = a + 1; b < lands.Count; b++)
            {
                if (shell.Links.Any(l => (l.From == a && l.To == b) || (l.From == b && l.To == a))) continue;
                var wa = shell.Waypoints[a];
                var wb = shell.Waypoints[b];
                var length = PlanetShape.Angle(wa, wb) * r;
                if (length > 2 * LeapShoulderMaxMetres + LeapMaxMetres + 2 * (lands[a].MaxRadiusRadians + lands[b].MaxRadiusRadians) * r) continue;
                if (crossesOpening(wa, wb)) continue;
                // One open stretch, and nothing but open sky either side of it along the whole way between the two lands.
                var probe = new ShellSpan(wa, wb, LeapBridgeWidthMetres / 2, r, 0);
                float? s0 = null, s1 = null;
                var clean = true;
                for (var s = 0f; s <= length && clean; s += 0.25f)
                {
                    var p = probe.PointAlong(s);
                    var onA = lands[a].Contains(p);
                    var onB = lands[b].Contains(p);
                    if (onA || onB)
                    {
                        if (s0 != null && s1 == null && onB) s1 = s;
                        if (onA && s0 != null) clean = false; // back onto A after leaving it: not star-shaped along this way
                        continue;
                    }

                    if (s0 == null) s0 = s;
                    if (s1 != null) { clean = false; break; } // open sky after B: past it
                    // Open sky here and a bridge's width (plus a step) to either side: nothing else is near.
                    var across = Vector3.Cross(probe.DirectionAt(p), p);
                    foreach (var side in new[] { 0f, 1f, -1f })
                    {
                        var q = side == 0 ? p : PlanetFrame.Move(p, across * side, LeapBridgeWidthMetres / 2 + ShellSpan.AdvancedLedgeMetres + 1.2f, r);
                        if (!shell.OverHole(q)) { clean = false; break; }
                    }
                }

                if (!clean || s0 == null || s1 == null) continue;
                var open = s1.Value - s0.Value;
                if (open < LeapMinMetres + 2 * LeapShoulderMinMetres || open > LeapMaxMetres + 2 * LeapShoulderMaxMetres) continue;
                // Open sky round it, a chasm wide: a leap is read from its own ledges, and nothing else faces it.
                var tree = shell.Links.Select(l => (l.From, l.To, HazardSiteKind.Launcher)).ToList();
                if (Clearance(wa, wb, s0.Value, s1.Value, a, b, lands, tree, all, r) < LeapClearanceMetres) continue;
                // Worth it: walking round is a long way.
                var walk = 0f;
                var from = wa;
                foreach (var step in shell.Route(wa, wb)) { walk += PlanetShape.Angle(from, step.Point) * r; from = step.Point; }
                if (walk < LeapDetourRatio * length + LeapDetourSlackMetres) continue;
                found.Add((a, b, s0.Value, s1.Value, walk / length));
            }
        }

        if (found.Count == 0) return shell;
        var spans = shell.Spans.ToList();
        var links = shell.Links.ToList();
        var used = new HashSet<int>();
        foreach (var (a, b, s0, s1, _) in found.OrderByDescending(f => f.Score).ThenBy(_ => rng.Next()))
        {
            if (links.Count(l => l.Advanced) >= wanted) break;
            if (used.Contains(a) || used.Contains(b)) continue; // one leap per land: they stay special
            var gap = LeapMinMetres + (LeapMaxMetres - LeapMinMetres) * (float)rng.NextDouble();
            var mid = (s0 + s1) / 2;
            var span = new ShellSpan(shell.Waypoints[a], shell.Waypoints[b], LeapBridgeWidthMetres / 2, r, rng.Next(), [(mid - gap / 2, mid + gap / 2)], ShellGapClass.Advanced);
            // Its shoulders must not reach other ground (a leap is read from its own ledges).
            spans.Add(span);
            links.Add(new ShellLink(a, b, spans.Count - 1, true, true));
            used.Add(a);
            used.Add(b);
        }

        return shell.With(spans: spans, links: links);
    }

    /// <summary>
    /// Grows spurs where two pieces of ground face each other across open sky longer than a hop and shorter than a chasm
    /// with a long walk between them (<see cref="ShellSeparations"/>, tempting): each grows towards the other until the gap
    /// is a hop of <see cref="SpurHopMetres"/>–+<see cref="SpurHopSpreadMetres"/>. A spur keeps out of the openings
    /// (<paramref name="blocked"/>) and away from gaps; when one side cannot grow, the other grows the whole way. A few
    /// rounds, since a spur can bring a third piece closer.
    /// </summary>
    private static BrokenShell GrowSpurs(BrokenShell shell, Random rng, Func<Vector3, float, bool> blocked)
    {
        var r = shell.Radius;
        for (var round = 0; round < SpurRounds; round++)
        {
            var misleading = Misleading(shell).OrderBy(s => s.Metres).ToList();
            if (misleading.Count == 0) break;
            var lands = shell.Lands.ToList();
            var spans = shell.Spans.ToList();
            var grown = new HashSet<int>();
            foreach (var s in misleading)
            {
                if (grown.Contains(s.PieceA) || grown.Contains(s.PieceB)) continue; // one spur per piece per round; look again next round
                var hop = SpurHopMetres + SpurHopSpreadMetres * (float)rng.NextDouble();
                var need = s.Metres - hop;
                var dirAB = Tangent(s.A, s.B);
                var dirBA = Tangent(s.B, s.A);
                var spanA = ShellSeparations.SpanOf(shell, s.PieceA).Span;
                var spanB = ShellSeparations.SpanOf(shell, s.PieceB).Span;
                var okA = CanGrow(shell, s.A, dirAB, need / 2, blocked, spanA);
                var okB = CanGrow(shell, s.B, dirBA, need / 2, blocked, spanB);
                float growA = 0, growB = 0;
                if (okA && okB) { growA = need / 2; growB = need / 2; }
                else
                {
                    // One side is hemmed in: each grows as far as it may; together they must close the gap to a hop.
                    var maxA = MaxGrowth(shell, s.A, dirAB, need, blocked, spanA);
                    var maxB = MaxGrowth(shell, s.B, dirBA, need, blocked, spanB);
                    if (maxA + maxB >= need)
                    {
                        growA = MathF.Min(maxA, need);
                        growB = need - growA;
                    }
                    else if (CanArch(shell, s.A, s.B, blocked))
                    {
                        // Neither can reach: a rock arch joins them (ground, not a route), so the long way round is no longer the only way.
                        spans.Add(new ShellSpan(PlanetFrame.Move(s.A, -dirAB, ArchOverlapMetres, r), PlanetFrame.Move(s.B, -dirBA, ArchOverlapMetres, r),
                            ArchWidthMetres / 2, r, rng.Next()));
                        grown.Add(s.PieceA);
                        grown.Add(s.PieceB);
                        continue;
                    }
                    else continue; // hemmed in on both sides (by the openings or a gap): left as it is
                }

                if (growA > 0.05f) Grow(shell, lands, spans, s.PieceA, s.A, s.B, growA);
                if (growB > 0.05f) Grow(shell, lands, spans, s.PieceB, s.B, s.A, growB);
                grown.Add(s.PieceA);
                grown.Add(s.PieceB);
            }

            if (grown.Count == 0) break;
            shell = shell.With(lands, spans);
        }

        return shell;
    }

    /// <summary>
    /// The open sky round the stretch <paramref name="from"/>..<paramref name="to"/> (metres along the straight way
    /// <paramref name="wa"/>→<paramref name="wb"/>): the least distance from it to any land but <paramref name="a"/> and
    /// <paramref name="b"/> (to its farthest tooth) and to any bridge of <paramref name="edges"/> not ending at them (to
    /// its centre line less a bridge's half-width and rag), metres.
    /// </summary>
    private static float Clearance(Vector3 wa, Vector3 wb, float from, float to, int a, int b, List<ShellLand> lands,
        List<(int A, int B, HazardSiteKind Role)> edges, List<HazardSite> all, float radius)
    {
        var line = new ShellSpan(wa, wb, 1f, radius, 0);
        var best = float.MaxValue;
        for (var d = MathF.Max(0, from); d <= MathF.Min(line.LengthMetres, to) + 1e-3f; d += 0.5f)
        {
            var p = line.PointAlong(d);
            for (var i = 0; i < lands.Count; i++)
            {
                if (i == a || i == b) continue;
                best = MathF.Min(best, (PlanetShape.Angle(p, lands[i].Centre) - lands[i].MaxRadiusRadians) * radius);
            }

            foreach (var (ea, eb, _) in edges)
            {
                if (ea == a || ea == b || eb == a || eb == b) continue;
                best = MathF.Min(best, PlanetFrame.DistanceToArc(p, all[ea].Waypoint, all[eb].Waypoint, radius) - (MinBridgeWidthMetres + BridgeWidthSpreadMetres) / 2 - ShellSpan.RagMetres);
            }
        }

        return best;
    }

    /// <summary>A rock arch (where two hemmed-in pieces cannot grow to a hop) is this wide, metres, and reaches this far into each piece.</summary>
    private const float ArchWidthMetres = 2.4f, ArchOverlapMetres = 0.6f;

    /// <summary>How far a spur from <paramref name="at"/> along <paramref name="dir"/> may grow, up to <paramref name="need"/> metres (to a tenth of it).</summary>
    private static float MaxGrowth(BrokenShell shell, Vector3 at, Vector3 dir, float need, Func<Vector3, float, bool> blocked, int ownSpan)
    {
        if (CanGrow(shell, at, dir, need, blocked, ownSpan)) return need;
        float lo = 0, hi = need;
        for (var i = 0; i < 5; i++)
        {
            var mid = (lo + hi) / 2;
            if (CanGrow(shell, at, dir, mid, blocked, ownSpan)) lo = mid; else hi = mid;
        }

        return lo;
    }

    /// <summary>An arch from <paramref name="a"/> to <paramref name="b"/> keeps out of the openings and away from every gap.</summary>
    private static bool CanArch(BrokenShell shell, Vector3 a, Vector3 b, Func<Vector3, float, bool> blocked)
    {
        var r = shell.Radius;
        var len = PlanetShape.Angle(a, b) * r;
        for (var d = 0f; d <= len; d += 0.5f)
        {
            if (blocked(PlanetFrame.Move(a, Tangent(a, b), d, r), ArchWidthMetres / 2)) return false;
        }

        foreach (var s in shell.Spans)
        {
            foreach (var (g0, g1) in s.Gaps)
            {
                for (var along = g0; along <= g1 + 0.01f; along += 0.5f)
                {
                    if (PlanetFrame.DistanceToArc(s.PointAlong(along), a, b, r) < ArchWidthMetres / 2 + s.HalfWidthMetres + SpurGapClearMetres) return false;
                }
            }
        }

        return true;
    }

    /// <summary>The unit tangent at <paramref name="a"/> towards <paramref name="b"/>.</summary>
    private static Vector3 Tangent(Vector3 a, Vector3 b)
    {
        var t = b - a * Vector3.Dot(b, a);
        return t.LengthSquared() > 1e-12f ? Vector3.Normalize(t) : PlanetShape.Tangents(a).A;
    }

    /// <summary>A spur from edge point <paramref name="at"/> growing <paramref name="metres"/> along <paramref name="dir"/> keeps out of the openings and away from every gap.</summary>
    private static bool CanGrow(BrokenShell shell, Vector3 at, Vector3 dir, float metres, Func<Vector3, float, bool> blocked, int ownSpan = -1)
    {
        var r = shell.Radius;
        var tip = PlanetFrame.Move(at, dir, metres, r);
        var half = SpurTipHalfMetres + SpurWidthPerLength * metres;
        if (blocked(tip, half)) return false;
        for (var k = 0; k < shell.Spans.Count; k++)
        {
            if (k == ownSpan) continue; // a bridge's own gaps cut through its spurs (ShellSpan.InsideMetres): it cannot close them
            var s = shell.Spans[k];
            foreach (var (g0, g1) in s.Gaps)
            {
                for (var along = g0; along <= g1 + 0.01f; along += 0.5f)
                {
                    var p = s.PointAlong(along);
                    if (PlanetFrame.DistanceToArc(p, at, tip, r) < half + s.HalfWidthMetres + SpurGapClearMetres) return false;
                }
            }
        }

        return true;
    }

    /// <summary>Grows piece <paramref name="piece"/> (a land or a bridge stretch) from its edge point <paramref name="at"/> by <paramref name="metres"/> towards <paramref name="towards"/>.</summary>
    private static void Grow(BrokenShell shell, List<ShellLand> lands, List<ShellSpan> spans, int piece, Vector3 at, Vector3 towards, float metres)
    {
        var r = shell.Radius;
        if (piece < lands.Count)
        {
            var l = lands[piece];
            var bearing = l.BearingOf(towards);
            var rimMetres = l.BoundaryRadians(bearing) * r;
            // How far the rim must move out along this bearing for its point to reach `metres` beyond `at` towards the target.
            var reachMetres = PlanetShape.Angle(l.Centre, PlanetFrame.Move(at, Tangent(at, towards), metres, r)) * r;
            var extra = MathF.Max(0.2f, reachMetres - rimMetres);
            var half = MathF.Atan2(SpurTipHalfMetres + SpurWidthPerLength * metres, MathF.Max(0.5f, rimMetres));
            lands[piece] = l.WithSpur(new ShellSpur(bearing, extra / r, half));
            return;
        }

        var (k, _) = ShellSeparations.SpanOf(shell, piece);
        var span = spans[k];
        var (along, side) = span.Measure(at);
        var left = side > 0;
        var (_, sideT) = span.Measure(PlanetFrame.Move(at, Tangent(at, towards), metres, r));
        var extraSide = MathF.Max(0.2f, MathF.Abs(sideT) - span.HalfWidthAt(along, left));
        spans[k] = span.WithSpur(new SpanSpur(along, left, extraSide, SpurTipHalfMetres + SpurWidthPerLength * metres));
    }

    /// <summary>A plateau's edge is this rough (a fraction of the full strength): files want room more than teeth.</summary>
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
        var baseMetres = (s.ContentMetres + EdgeMarginMetres) / (1 - ShellLand.WobbleFor(seed) * roughness);
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