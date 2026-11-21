using System.Numerics;
using FileScape.Core.Planets;
using Xunit.Abstractions;
using static FileScape.Core.Tests.HazardGapWorldTests;
using static FileScape.Core.Tests.HazardMovementTests;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-61..63, AD-65: the real walker's movement envelope on a Hazard World, the recovery twirl, and the three kinds of
/// gap measured against it. DJ (2026-11-17): "some gaps look jumpable from the takeoff edge even though I can't make them
/// … distinguish gaps a normal jump can clear, gaps that reward an advanced move, and gaps that are there to steer me
/// toward a bridge", and "when I barely miss an edge and start to fall, let a well-timed twirl give me a brief lift".
/// Every number the terrain is sized by (HazardTerrain's constants) is checked here against <see cref="SphereWalker"/>
/// itself: a step is cleared by any plain jump, a leap by no plain jump at walking pace but by a twirl at the top of the
/// jump, a chasm by nothing at walking pace; and no tempting gap in between is left on the worlds.
/// </summary>
public class HazardMovementTests(ITestOutputHelper output)
{
    internal static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);

    /// <summary>A hazard-sized planet (the sample's Event Horizon is ≈ 20 m, the Debris Field ≈ 23 m).</summary>
    private const float R = 22f;

    // ------------------------------------------------------------------ the envelope, over a clean gap

    /// <summary>Two round lands joined by a bridge with one gap of <paramref name="gap"/> metres in its middle (a plain one: no ledges in the way of the measurement are needed, but they are there as in the game).</summary>
    private static (PlanetSurface Surface, ShellSpan Span, float G0, float G1) Gap(float gap)
    {
        var a = PlanetFrame.FromPolar(0.45f, 0);
        var b = PlanetFrame.FromPolar(0.45f, MathF.PI);
        var lands = new List<ShellLand> { new(a, 3f / R, 1, 0), new(b, 3f / R, 2, 0) };
        var len = PlanetShape.Angle(a, b) * R;
        var g0 = len / 2 - gap / 2;
        var g1 = len / 2 + gap / 2;
        var span = new ShellSpan(a, b, 1.2f, R, 3, [(g0, g1)], gap > 4 ? ShellGapClass.Advanced : ShellGapClass.Plain);
        var shell = new BrokenShell(lands, [a, b], [span], [new ShellLink(0, 1, 0, true)], Vector3.UnitX, R);
        return (new PlanetSurface(PlanetShape.Sphere(R), [], shell), span, g0, g1);
    }

    /// <summary>One attempt at a gap, as a player would make it.</summary>
    /// <param name="Run">Shift held.</param>
    /// <param name="PressBefore">Space pressed this far before the edge, metres (negative: never, the walker walks off).</param>
    /// <param name="Hold">Space held this long, seconds.</param>
    /// <param name="TwirlAt">A second press this long after leaving the ground, seconds (negative: none).</param>
    /// <param name="TwirlHold">The second press held this long, seconds.</param>
    /// <param name="RecoverAt">A press this long after the feet drop below the rim's level over open sky, seconds (negative: none).</param>
    private readonly record struct Attempt(bool Run, float PressBefore, float Hold, float TwirlAt = -1, float TwirlHold = 0.3f, float RecoverAt = -1);

    /// <summary>What an attempt came to: across, the fall committed, and how many recoveries and last efforts it made; the lowest the feet went (metres, below the rim negative).</summary>
    private sealed record Outcome(bool Made, bool Committed, int Recoveries, int LastEfforts, float Lowest);

    /// <summary>Runs at the gap from 6 m before it along the bridge; made when standing beyond it without the fall committing.</summary>
    private static Outcome Try(float gap, Attempt a)
    {
        var (surface, span, g0, g1) = Gap(gap);
        var w = new SphereWalker();
        var start = span.PointAlong(g0 - 6f);
        var target = span.PointAlong(g1 + 3f);
        w.Teleport(surface, start, span.DirectionAt(start));
        bool pressed = false, twirled = false, recovered = false;
        var hold = 0f;
        var leftAt = -1f;
        var t = 0f;
        var lowest = 0f;
        for (var i = 0; i < 60 * 6; i++, t += 1 / 60f)
        {
            var wish = Vector3.Normalize(target - w.Up * Vector3.Dot(target, w.Up));
            var along = span.Measure(w.Up).AlongMetres;
            var press = false;
            if (!pressed && a.PressBefore >= 0 && along >= g0 - a.PressBefore) { press = pressed = true; hold = a.Hold; }
            if (!w.Grounded && leftAt < 0) leftAt = t;
            if (a.TwirlAt >= 0 && leftAt >= 0 && !twirled && t - leftAt >= a.TwirlAt) { press = twirled = true; hold = a.TwirlHold; }
            if (a.RecoverAt >= 0 && !recovered && w.BelowRimSeconds >= a.RecoverAt) { press = recovered = true; hold = 0.1f; }
            var held = hold > 0;
            hold -= 1 / 60f;
            w.Update(new SphereWalkInput(wish, a.Run, press, held), Frame);
            lowest = MathF.Min(lowest, w.Height.Value);
            if (w.FallCommitted) return new Outcome(false, true, w.Recoveries, w.LastEfforts, lowest);
            if (leftAt >= 0 && w.Grounded && span.Measure(w.Up).AlongMetres > g1) return new Outcome(true, false, w.Recoveries, w.LastEfforts, lowest);
            if (leftAt >= 0 && w.Grounded && span.Measure(w.Up).AlongMetres < g0) return new Outcome(false, false, w.Recoveries, w.LastEfforts, lowest); // back on the near side
        }

        return new Outcome(false, false, w.Recoveries, w.LastEfforts, lowest);
    }

    private static readonly float[] AnyPress = Enumerable.Range(0, 16).Select(i => i * 0.1f).ToArray();
    private static readonly float[] AnyHold = [0.08f, 0.2f, 0.3f, 2f];

    /// <summary>
    /// The widest gap (to 0.1 m) any of <paramref name="attempts"/> makes. The gaps are tried side by side (TEST-12): each
    /// <see cref="Try"/> builds its own walker and surface, and the result is the same widest gap the serial sweep found
    /// (the same float steps from 0.5 m, every one of them tried, so a gap made beyond a missed one still counts).
    /// </summary>
    private static float Widest(IEnumerable<Attempt> attempts, float upTo = 16f)
    {
        var list = attempts.ToList();
        var gaps = new List<float>();
        for (var g = 0.5f; g <= upTo; g += 0.1f) gaps.Add(g);
        var made = TestParallel.Map(gaps, g => list.Any(a => Try(g, a).Made));
        var best = 0f;
        for (var i = 0; i < gaps.Count; i++)
        {
            if (made[i]) best = gaps[i];
        }

        return best;
    }

    private static IEnumerable<Attempt> Plain(bool run) => from p in AnyPress from h in AnyHold select new Attempt(run, p, h);

    private static IEnumerable<Attempt> PlainAndRecovery(bool run) =>
        from p in AnyPress.Where((_, i) => i % 2 == 0) from h in new[] { 0.3f, 2f } from k in new[] { 0f, 0.03f, 0.06f, 0.1f, 0.15f, 0.2f, 0.3f }
        select new Attempt(run, p, h, RecoverAt: k);

    private static IEnumerable<Attempt> Twirls(bool run) =>
        from p in AnyPress.Where((_, i) => i % 2 == 0) from h in new[] { 0.12f, 0.3f, 2f } from d in Enumerable.Range(1, 16).Select(i => i * 0.05f)
        from th in new[] { 0.12f, 0.3f, 2f } select new Attempt(run, p, h, d, th);

    private static IEnumerable<Attempt> TwirlsAndRecovery(bool run) =>
        from p in new[] { 0.2f, 0.6f, 1.0f } from d in new[] { 0.3f, 0.45f, 0.6f, 0.75f } from k in new[] { 0f, 0.05f, 0.1f }
        select new Attempt(run, p, 2f, d, 2f, k);

    [Trait(TestKinds.Trait, TestKinds.Slow)]
    [Fact]
    public void The_envelope_at_walking_pace_sizes_the_three_kinds_of_gap()
    {
        var plain = Widest(Plain(false));
        var plainRecovery = Widest(PlainAndRecovery(false));
        var twirl = Widest(Twirls(false));
        // The widest over a union is the larger of the two widests, so the twirl sweep is not run twice.
        var everything = Math.Max(twirl, Widest(TwirlsAndRecovery(false)));
        output.WriteLine(F($"walking: plain jump {plain:0.0} m at best; with a recovery twirl {plainRecovery:0.0} m; jump and twirl {twirl:0.0} m; with a recovery too {everything:0.0} m"));
        output.WriteLine(F($"running: plain jump {Widest(Plain(true)):0.0} m at best; jump and twirl {Widest(Twirls(true).Where((_, i) => i % 7 == 0)):0.0} m (sampled)"));

        // A step (with its jagged ends) is well inside a plain jump; a hop too.
        (HazardTerrain.MaxJumpGapMetres + 2 * ShellSpan.GapJagMetres).Should().BeLessThan(plain - 1.5f);
        HazardTerrain.HopMaxMetres.Should().BeLessThan(plain - 2f);
        // A leap is beyond any plain jump at walking pace, saved by a recovery or not.
        HazardTerrain.LeapMinMetres.Should().BeGreaterThan(plain + 1f, "no plain jump at walking pace makes a leap");
        HazardTerrain.LeapMinMetres.Should().BeGreaterThan(plainRecovery, "nor a plain jump saved by a recovery twirl");
        // A chasm is beyond everything at walking pace.
        HazardTerrain.SteeringMinMetres.Should().BeGreaterThan(everything, "nothing at walking pace crosses a chasm");
    }

    [Fact]
    public void Any_plain_jump_clears_a_step_and_a_hop()
    {
        foreach (var gap in new[] { HazardTerrain.MaxJumpGapMetres + 2 * ShellSpan.GapJagMetres, HazardTerrain.HopMaxMetres })
        {
            foreach (var run in new[] { false, true })
            {
                // Space anywhere from just before the edge to 1.3 m before it, held; a quick tap (a short hop) up to 0.7 m before.
                foreach (var (press, hold) in new[] { (0.25f, 0.3f), (0.7f, 0.3f), (1.3f, 0.3f), (0.25f, 2f), (0.7f, 2f), (1.3f, 2f), (0.25f, 0.15f), (0.7f, 0.15f) })
                {
                    Try(gap, new Attempt(run, press, hold)).Made.Should().BeTrue(F($"a {gap:0.0} m gap, Space {press} m before the edge held {hold} s, {(run ? "running" : "walking")}"));
                }
            }
        }
    }

    [Fact]
    public void A_twirl_at_the_top_of_the_jump_clears_a_leap_and_a_plain_jump_never_does()
    {
        // At walking pace: Space 0.2–0.6 m before the edge (the marked take-off board), the twirl at the top of the jump
        // (0.35–0.5 s after take-off; later it is the catch, which has no ground in reach over a leap's middle).
        var made = 0;
        var tries = 0;
        foreach (var press in new[] { 0.2f, 0.4f, 0.6f })
        {
            foreach (var at in new[] { 0.35f, 0.42f, 0.5f })
            {
                foreach (var hold in new[] { 0.3f, 2f })
                {
                    tries++;
                    var o = Try(HazardTerrain.LeapMaxMetres + ShellSpan.GapJagMetres, new Attempt(false, press, hold, at, 0.3f));
                    if (o.Made) made++;
                    else output.WriteLine(F($"missed: Space {press} m before, held {hold} s, twirl at {at} s"));
                }
            }
        }

        output.WriteLine($"a leap at walking pace with a twirl at the top: {made} of {tries} attempts");
        made.Should().BeGreaterThanOrEqualTo(tries * 5 / 6, "a twirl at the top of the jump is the way across");
        foreach (var a in Plain(false).Concat(PlainAndRecovery(false)))
        {
            Try(HazardTerrain.LeapMinMetres, a).Made.Should().BeFalse($"no plain jump at walking pace makes a leap ({a})");
        }

        Try(HazardTerrain.LeapMaxMetres + ShellSpan.GapJagMetres, new Attempt(true, 0.5f, 2f)).Made.Should().BeTrue("a running jump is the other advanced way across");
    }

    // ------------------------------------------------------------------ the recovery twirl

    /// <summary>The jump a player makes at a gap: walking, Space 0.3 m before the edge, held.</summary>
    private static readonly Attempt Leap = new(false, 0.3f, 2f);

    /// <summary>A gap that jump just misses: 0.3 m wider than the widest it makes.</summary>
    private static float BarelyMissed()
    {
        var g = 4f;
        while (Try(g, Leap).Made) g += 0.1f;
        return g + 0.2f;
    }

    [Fact]
    public void A_barely_missed_edge_is_caught_by_a_twirl_in_time()
    {
        var gap = BarelyMissed();
        output.WriteLine(F($"barely missed: {gap:0.0} m"));
        var miss = Try(gap, Leap);
        miss.Made.Should().BeFalse("without the twirl the jump falls short");
        miss.Committed.Should().BeTrue();
        foreach (var k in new[] { 0f, 0.03f, 0.06f })
        {
            var o = Try(gap, Leap with { RecoverAt = k });
            output.WriteLine(F($"recover {k} s below the rim: made {o.Made}, committed {o.Committed}, recoveries {o.Recoveries}, last efforts {o.LastEfforts}, lowest {o.Lowest:0.00} m"));
            o.Made.Should().BeTrue(F($"Space {k} s after dropping below the far rim catches it"));
            o.Recoveries.Should().Be(1);
            o.LastEfforts.Should().Be(0);
            o.Lowest.Should().BeLessThan(-0.05f, "it really was below the rim");
        }
    }

    [Fact]
    public void Too_late_or_too_far_is_a_last_effort_and_the_fall_goes_on()
    {
        // Too late: after the window (or deeper than the most the lift saves).
        var late = Try(BarelyMissed(), Leap with { RecoverAt = (float)HazardMoveParams.Default.Window.TotalSeconds + 0.02f });
        late.Made.Should().BeFalse();
        late.Committed.Should().BeTrue();
        late.Recoveries.Should().Be(0);
        // Too far: no ground within reach of where the walker is (a leap-wide gap, a plain jump short by metres).
        var far = Try(9f, new Attempt(false, 0.3f, 2f, RecoverAt: 0.02f));
        far.Made.Should().BeFalse();
        far.Committed.Should().BeTrue();
        far.LastEfforts.Should().Be(1, "Space pressed in time but far from any rim is a last effort");
        far.Recoveries.Should().Be(0);
    }

    [Fact]
    public void Walking_off_an_edge_a_twirl_in_time_puts_you_back_on_it()
    {
        var (surface, span, g0, _) = Gap(6f);
        foreach (var pressAfter in new[] { 0.13f, 0.17f })
        {
            var w = new SphereWalker();
            var start = span.PointAlong(g0 - 3f);
            w.Teleport(surface, start, span.DirectionAt(start));
            var target = span.PointAlong(g0 + 3f);
            var leftAt = -1f;
            var pressed = false;
            var back = false;
            for (var i = 0; i < 60 * 4; i++)
            {
                var t = i / 60f;
                var wish = leftAt < 0 ? Vector3.Normalize(target - w.Up * Vector3.Dot(target, w.Up)) : Vector3.Zero; // stops pushing on once off
                if (!w.Grounded && leftAt < 0) leftAt = t;
                var press = !pressed && leftAt >= 0 && t - leftAt >= pressAfter;
                pressed |= press;
                w.Update(new SphereWalkInput(wish, false, press, press), Frame);
                w.FallCommitted.Should().BeFalse(F($"pressed {pressAfter} s after walking off"));
                if (pressed && w.Grounded) { back = true; break; }
            }

            back.Should().BeTrue(F($"pressed {pressAfter} s after walking off, the walker is back on the rim"));
            w.Recoveries.Should().Be(1);
            span.Measure(w.Up).AlongMetres.Should().BeLessThan(g0, "on the side it walked off");
        }
    }

    [Fact]
    public void On_a_whole_planet_a_late_press_is_the_ordinary_twirl()
    {
        var surface = new PlanetSurface(PlanetShape.Sphere(R), []);
        var w = new SphereWalker();
        w.Teleport(surface, Vector3.UnitY, Vector3.UnitX);
        var twirled = false;
        for (var i = 0; i < 120; i++)
        {
            var press = i == 0 || (i == 32 && !twirled); // 0.53 s: past the top, falling
            w.Update(new SphereWalkInput(Vector3.UnitX, false, press, i < 20 || (i >= 32 && i < 42)), Frame);
            if (w.LastEvent.Kind == Core.Movement.JumpEventKind.AirJump) twirled = true;
        }

        twirled.Should().BeTrue("a press while falling is the twirl, as ever");
        (w.Recoveries + w.LastEfforts).Should().Be(0);
        w.FallCommitted.Should().BeFalse();
        w.BelowRimSeconds.Should().Be(-1);
    }

}

/// <summary>
/// The worlds half of <see cref="HazardMovementTests"/> (PLANET-61..63, AD-65): on each composed Hazard World no gap
/// misleads (the closest approaches and an independent ray audit), and a leap takes the twirl and is never the only way.
/// Its own class (TEST-10, AD-90), so its own xUnit collection: the six worlds' audits are ≈ 40 s of work, and they no
/// longer wait behind the envelope sweeps. A new test on a composed world goes here; one on the bare gap, above.
/// </summary>
public class HazardGapWorldTests(ITestOutputHelper output)
{
    // ------------------------------------------------------------------ the worlds

    /// <summary>The ray audit's misleading rays must stay under this fraction of the count before AD-65 (measured 0.26–0.70, 2026-11-17).</summary>
    private const float RayAuditCeiling = 0.75f;

    [Trait(TestKinds.Trait, TestKinds.Slow)]
    [Theory]
    [MemberData(nameof(HazardWorldTests.Worlds), MemberType = typeof(HazardWorldTests))]
    public void No_gap_on_a_world_misleads(string kind)
    {
        var (t, id) = HazardWorldTests.HazardFor(kind);
        var shell = PlanetComposer.Compose(t, id).Surface.Shell!;
        var seps = ShellSeparations.Find(shell, HazardTerrain.SteeringMinMetres + 2, HazardTerrain.HopMaxMetres);
        var misleading = seps.Where(s => !s.Designed && !s.Opening && s.Tempting && s.Metres > HazardTerrain.HopMaxMetres && s.Metres < HazardTerrain.SteeringMinMetres).ToList();
        output.WriteLine($"{kind}: {seps.Count(s => !s.Designed && s.Metres <= HazardTerrain.HopMaxMetres)} hops, {seps.Count(s => !s.Designed && s.Tempting && s.Metres >= HazardTerrain.SteeringMinMetres)} chasms in view, {seps.Count(s => s.Opening)} across the black hole's window, {misleading.Count} misleading");
        foreach (var s in misleading) output.WriteLine(F($"  misleading: {s.PieceA} → {s.PieceB}, {s.Metres:0.0} m"));

        // The independent check: rays from every edge, the way round walked on a grid (hops allowed).
        var audit = GapAudit.Run(shell);
        var bands = audit.Misleading.GroupBy(m => m.Metres < 5.2f ? "2.6-5.2" : m.Metres < 7f ? "5.2-7.0" : "7.0-10").OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}");
        output.WriteLine($"AUDIT {kind}: walkable {shell.WalkableFraction():P0}, lands {shell.Lands.Count}, bridges {shell.Spans.Count}; ray audit {audit.Hops} hops, {audit.Chasms} chasms, {audit.Window} window, {audit.Misleading.Count} misleading ({string.Join(", ", bands)})");
        foreach (var m in audit.Misleading.Take(8)) output.WriteLine(F($"  ray audit misleading: {m.Metres:0.0} m, walk {m.WalkMetres:0.0} m"));
        misleading.Should().BeEmpty($"{kind}: open sky between pieces with a long walk between them is a hop or a chasm");
        // The ray audit sees more than the closest approach of each pair: every stretch of edge facing other ground. Measured on
        // the terrain before AD-65 (a0524bc), it found this many misleading rays; spurs and lobes must keep well under it.
        var before = new Dictionary<string, int> { ["sample"] = 408, ["few"] = 625, ["crowded"] = 1473, ["files"] = 584, ["empty"] = 397, ["debris"] = 816 };
        audit.Misleading.Count.Should().BeLessThan((int)(before[kind] * RayAuditCeiling), $"{kind}: well under the {before[kind]} misleading rays before AD-65");
    }

    [Trait(TestKinds.Trait, TestKinds.Slow)]
    [Theory]
    [MemberData(nameof(HazardWorldTests.Worlds), MemberType = typeof(HazardWorldTests))]
    public void A_leap_on_a_world_takes_the_twirl_and_is_never_the_only_way(string kind)
    {
        var (t, id) = HazardWorldTests.HazardFor(kind);
        var c = PlanetComposer.Compose(t, id);
        var shell = c.Surface.Shell!;
        // The route never takes a leap: every launcher and the overlook's foot are reached by steps and walking.
        foreach (var l in c.Launchers)
        {
            var route = shell.Route(c.Arrival.Up, l.Direction);
            var from = c.Arrival.Up;
            foreach (var step in route)
            {
                foreach (var k in Enumerable.Range(0, shell.Spans.Count).Where(k => shell.Spans[k].GapClass == ShellGapClass.Advanced && shell.Spans[k].Gaps.Count > 0))
                {
                    var s = shell.Spans[k];
                    var (g0, g1) = s.Gaps[0];
                    var mid = s.PointAlong((g0 + g1) / 2);
                    PlanetFrame.DistanceToArc(mid, from, step.Point, shell.Radius).Should().BeGreaterThan(1f, $"{kind}: the way to {l.Name} does not cross a leap");
                }

                from = step.Point;
            }
        }

        foreach (var link in shell.Links.Where(l => l.Advanced))
        {
            var s = shell.Spans[link.Span];
            var (g0, g1) = s.Gaps[0];
            output.WriteLine(F($"{kind}: a leap of {g1 - g0:0.00} m"));
            foreach (var (from, to) in new[] { (s.PointAlong(g0 - 2.2f), s.PointAlong(g1 + 2.5f)), (s.PointAlong(g1 + 2.2f), s.PointAlong(g0 - 2.5f)) })
            {
                var plainMade = 0;
                var twirlMade = 0;
                foreach (var press in new[] { 0.2f, 0.5f, 0.9f })
                {
                    if (Cross(c.Surface, from, to, press, 2f, -1)) plainMade++;
                    foreach (var at in new[] { 0.4f, 0.5f }) if (Cross(c.Surface, from, to, press, 2f, at)) twirlMade++;
                }

                plainMade.Should().Be(0, $"{kind}: no plain jump at walking pace makes the leap");
                twirlMade.Should().BeGreaterThanOrEqualTo(4, $"{kind}: a twirl at the top of the jump makes it (of 6)");
            }
        }
    }

    /// <summary>Walks from <paramref name="from"/> towards <paramref name="to"/> across a gap on the real world: Space <paramref name="press"/> m before the edge, a twirl <paramref name="twirlAt"/> s after take-off (negative: none). True when it stands near <paramref name="to"/>.</summary>
    private static bool Cross(PlanetSurface surface, Vector3 from, Vector3 to, float press, float hold, float twirlAt)
    {
        var shell = surface.Shell!;
        var w = new SphereWalker();
        w.Teleport(surface, from, Vector3.Normalize(to - from * Vector3.Dot(to, from)));
        bool pressed = false, twirled = false;
        var h = 0f;
        var leftAt = -1f;
        for (var i = 0; i < 60 * 6; i++)
        {
            var t = i / 60f;
            var wish = to - w.Up * Vector3.Dot(to, w.Up);
            wish = wish.LengthSquared() > 1e-10f ? Vector3.Normalize(wish) : Vector3.Zero;
            var jump = false;
            if (!pressed && w.Grounded && shell.OverHole(PlanetFrame.Move(w.Up, wish, press, shell.Radius))) { jump = pressed = true; h = hold; }
            if (!w.Grounded && leftAt < 0) leftAt = t;
            if (twirlAt >= 0 && !twirled && leftAt >= 0 && t - leftAt >= twirlAt) { jump = twirled = true; h = 0.3f; }
            var held = h > 0;
            h -= 1 / 60f;
            w.Update(new SphereWalkInput(wish, false, jump, held), Frame);
            if (w.FallCommitted) return false;
            if (pressed && w.Grounded && surface.SurfaceDistance(w.Up, to) < 1f) return true;
        }

        return false;
    }

    internal static string F(FormattableString s) => FormattableString.Invariant(s);
}

/// <summary>
/// An independent check of AD-65's gap rule, sharing nothing with ShellSeparations but <see cref="BrokenShell.OverHole"/>:
/// the ground is a grid of points ≈ 0.4 m apart; from every edge point rays go out across the open sky (straight out and
/// 15°/30° to either side) to the first ground beyond; the way round between the two ends is walked on the grid (steps
/// between neighbouring ground points, hops of up to HopMaxMetres across open sky). A gap longer than a hop and shorter
/// than a chasm whose way round is more than twice its length plus 4 m is misleading — unless it is a bridge's own gap
/// or it lies across the black hole's window.
/// </summary>
internal static class GapAudit
{
    /// <summary>The audit's counts: rays that end within a hop, rays into a chasm, rays across the black hole's window, and each misleading ray (its open-sky metres and the walk round).</summary>
    public sealed record Result(int Hops, int Chasms, int Window, List<(float Metres, float WalkMetres)> Misleading);

    private const float SpacingMetres = 0.4f;

    public static Result Run(BrokenShell shell)
    {
        var r = shell.Radius;
        var n = (int)(4 * MathF.PI * r * r / (SpacingMetres * SpacingMetres));
        var pts = PlanetComposer.FibonacciSphere(n);
        var solid = pts.Select(p => !shell.OverHole(p)).ToArray();
        // A coarse spatial hash on the unit sphere's cube (cells ≈ 1.3 m).
        var cell = 1.3f / r;
        (int, int, int) Key(Vector3 p) => ((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Y / cell), (int)MathF.Floor(p.Z / cell));
        var grid = new Dictionary<(int, int, int), List<int>>();
        for (var i = 0; i < pts.Length; i++)
        {
            if (!solid[i]) continue;
            var k = Key(pts[i]);
            if (!grid.TryGetValue(k, out var list)) grid[k] = list = [];
            list.Add(i);
        }

        IEnumerable<int> Near(Vector3 p, float metres)
        {
            var (x, y, z) = Key(p);
            var reach = (int)MathF.Ceiling(metres / r / cell);
            var cos = MathF.Cos(metres / r);
            for (var dx = -reach; dx <= reach; dx++)
            for (var dy = -reach; dy <= reach; dy++)
            for (var dz = -reach; dz <= reach; dz++)
            {
                if (!grid.TryGetValue((x + dx, y + dy, z + dz), out var list)) continue;
                foreach (var j in list) if (Vector3.Dot(pts[j], p) >= cos) yield return j;
            }
        }

        // The walk graph: neighbours within 1.5 spacings; hops from edge points across open sky up to HopMaxMetres.
        var edges = new Dictionary<int, List<(int, float)>>();
        var edgePoints = new List<int>();
        foreach (var list in grid.Values)
        {
            foreach (var i in list)
            {
                var adj = new List<(int, float)>();
                var isEdge = false;
                foreach (var j in Near(pts[i], SpacingMetres * 1.6f))
                {
                    if (j != i) adj.Add((j, PlanetShape.Angle(pts[i], pts[j]) * r));
                }

                // An edge point: open sky a spacing away somewhere round it.
                var (ta, tb) = PlanetShape.Tangents(pts[i]);
                for (var b = 0; b < 8 && !isEdge; b++)
                {
                    var th = b * MathF.PI / 4;
                    isEdge = shell.OverHole(PlanetFrame.Move(pts[i], ta * MathF.Cos(th) + tb * MathF.Sin(th), SpacingMetres, r));
                }

                if (isEdge)
                {
                    edgePoints.Add(i);
                    foreach (var j in Near(pts[i], HazardTerrain.HopMaxMetres))
                    {
                        var d = PlanetShape.Angle(pts[i], pts[j]) * r;
                        if (d > SpacingMetres * 1.6f && shell.OverHole(Vector3.Normalize(pts[i] + pts[j]))) adj.Add((j, d + 1f));
                    }
                }

                edges[i] = adj;
            }
        }

        // Rays from edge points (every other one), deduplicated by where they start and end (1.5 m cells).
        var seen = new HashSet<((int, int, int), (int, int, int))>();
        int hops = 0, chasms = 0, window = 0;
        var misleading = new List<(float, float)>();
        var coarse = 1.5f / r;
        (int, int, int) Coarse(Vector3 p) => ((int)MathF.Floor(p.X / coarse), (int)MathF.Floor(p.Y / coarse), (int)MathF.Floor(p.Z / coarse));
        for (var e = 0; e < edgePoints.Count; e += 2)
        {
            var i = edgePoints[e];
            var p0 = pts[i];
            var (ta, tb) = PlanetShape.Tangents(p0);
            // Outward: towards the open sky round it.
            var outward = Vector3.Zero;
            for (var b = 0; b < 16; b++)
            {
                var th = b * MathF.PI / 8;
                var dir = ta * MathF.Cos(th) + tb * MathF.Sin(th);
                if (shell.OverHole(PlanetFrame.Move(p0, dir, SpacingMetres * 1.5f, r))) outward += dir;
            }

            if (outward.LengthSquared() < 1e-6f) continue;
            outward = Vector3.Normalize(outward);
            foreach (var turn in new[] { -30f, -15f, 0f, 15f, 30f })
            {
                var tr = turn * MathF.PI / 180f;
                var dir = outward * MathF.Cos(tr) + Vector3.Cross(p0, outward) * MathF.Sin(tr);
                var open = -1f;
                for (var d = 0.1f; d < HazardTerrain.SteeringMinMetres + 2; d += 0.1f)
                {
                    var q = PlanetFrame.Move(p0, dir, d, r);
                    var hole = shell.OverHole(q);
                    if (hole && open < 0) open = d;
                    if (hole || open < 0) continue;
                    var w = d - open;
                    if (!seen.Add((Coarse(p0), Coarse(q)))) break;
                    if (w <= HazardTerrain.HopMaxMetres) { hops++; break; }
                    if (Designed(shell, p0, q)) break;
                    if (AcrossWindow(shell, p0, q)) { window++; break; }
                    // The way round, on the grid, from the ground point nearest each end.
                    var qi = Near(q, SpacingMetres * 1.2f).OrderBy(j => PlanetShape.Angle(pts[j], q)).DefaultIfEmpty(-1).First();
                    if (qi < 0) break;
                    var limit = 2 * w + 4f;
                    var walk = Walk(edges, pts, i, qi, limit + 0.5f, r);
                    if (walk > limit) misleading.Add((w, walk));
                    break;
                }

                if (open > 0 && open + 0.1f >= HazardTerrain.SteeringMinMetres) chasms++;
            }
        }

        return new Result(hops, chasms, window, misleading);
    }

    /// <summary>The two ends lie on one bridge, either side of its own gap.</summary>
    private static bool Designed(BrokenShell shell, Vector3 a, Vector3 b)
    {
        var mid = Vector3.Normalize(a + b);
        foreach (var s in shell.Spans)
        {
            var (along, side) = s.Measure(mid);
            foreach (var (g0, g1) in s.Gaps)
            {
                if (along > g0 - ShellSpan.GapJagMetres - 0.5f && along < g1 + ShellSpan.GapJagMetres + 0.5f && MathF.Abs(side) < s.HalfWidthMetres + ShellSpan.AdvancedLedgeMetres + ShellSpan.RagMetres) return true;
            }
        }

        return false;
    }

    private static bool AcrossWindow(BrokenShell shell, Vector3 a, Vector3 b)
    {
        for (var k = 1; k < 6; k++)
        {
            var q = Vector3.Normalize(Vector3.Lerp(a, b, k / 6f));
            if (shell.InFrontOpening(q, 1.5f) || PlanetShape.Angle(q, shell.JetAxis) * shell.Radius < HazardTerrain.JetClearMetres + 1.5f
                || PlanetShape.Angle(q, -shell.JetAxis) * shell.Radius < HazardTerrain.JetClearMetres + 1.5f) return true;
        }

        return false;
    }

    /// <summary>Dijkstra on the walk graph from <paramref name="from"/> to <paramref name="to"/>, giving up beyond <paramref name="limit"/> metres.</summary>
    private static float Walk(Dictionary<int, List<(int, float)>> edges, Vector3[] pts, int from, int to, float limit, float r)
    {
        var dist = new Dictionary<int, float> { [from] = 0 };
        var queue = new PriorityQueue<int, float>();
        queue.Enqueue(from, 0);
        while (queue.TryDequeue(out var u, out var du))
        {
            if (u == to) return du;
            if (du > dist[u]) continue;
            if (du > limit) break;
            if (!edges.TryGetValue(u, out var adj)) continue;
            foreach (var (v, c) in adj)
            {
                var dv = du + c;
                if (dist.TryGetValue(v, out var old) && old <= dv) continue;
                dist[v] = dv;
                queue.Enqueue(v, dv);
            }
        }

        return float.PositiveInfinity;
    }
}
