using System.Numerics;

namespace FileScape.Core.Planets;

/// <summary>
/// The open sky between two pieces of a Hazard World's ground (PLANET-61, AD-65): pieces <see cref="PieceA"/> and
/// <see cref="PieceB"/> (see <see cref="ShellSeparations"/> for the numbering) come within <see cref="Metres"/> of each
/// other, at the edge points <see cref="A"/> and <see cref="B"/> (unit directions). <see cref="Designed"/>: the two are the
/// two sides of one bridge's own gap. <see cref="Tempting"/>: walking from one to the other is a long way round compared with
/// how close they are (so a player would think of jumping it). <see cref="Opening"/>: the sky between them is the black
/// hole's window (the opening ahead of the arrival, or a jet's opening), kept open on purpose.
/// </summary>
public readonly record struct ShellSeparation(int PieceA, int PieceB, Vector3 A, Vector3 B, float Metres, bool Designed, bool Tempting, bool Opening = false);

/// <summary>
/// Finds where the pieces of a broken shell face each other across open sky (PLANET-61, AD-65): DJ, 2026-11-17, "some gaps
/// look jumpable from the takeoff edge even though I can't make them". A piece is a land (numbered as in
/// <see cref="BrokenShell.Lands"/>) or a stretch of a bridge between its gaps (numbered after the lands, bridge by bridge,
/// stretch by stretch). Each piece is sampled along its real edge (points with open sky just beyond them, so a land's
/// rim under a bridge does not count), and for every pair of pieces the closest pair of samples with open sky between
/// them is kept. <c>HazardTerrain.Design</c> uses it to grow spurs where a gap would mislead, and the tests use it to check
/// that none is left.
/// <para>
/// Tempting: in a graph whose nodes are the pieces (at their centres) and whose edges join pieces that share ground or are
/// within a plain hop of each other, the shortest way between the two is more than <see cref="TemptRatio"/> times their
/// straight distance plus <see cref="TemptSlackMetres"/>. Planet frame, metres along the surface. Deterministic and
/// read-only. What a change must preserve: a separation reported is open sky all the way across (its middle is checked),
/// and its length is within a sample spacing of the true closest approach.
/// </para>
/// </summary>
public static class ShellSeparations
{
    /// <summary>A land's rim is sampled at this many bearings (7.5° apart: ≈ 0.5 m on a 4 m island).</summary>
    private const int LandSamples = 48;

    /// <summary>A bridge's sides are sampled this often along it, metres, and its gap faces this often across.</summary>
    private const float SpanSampleMetres = 0.5f;

    /// <summary>A sample counts as edge when open sky lies this far beyond it, metres.</summary>
    private const float EdgeProbeMetres = 0.08f;

    /// <summary>Sky within this of the front opening or a jet's clear opening, metres, belongs to the black hole's window (a spur there would be too: HazardTerrain keeps spurs out).</summary>
    private const float OpeningReachMetres = 1.5f;

    /// <summary>A pair is tempting when the way round is more than this many times the straight distance…</summary>
    public const float TemptRatio = 2f;

    /// <summary>…plus this many metres (a short walk round never tempts).</summary>
    public const float TemptSlackMetres = 4f;

    /// <summary>One piece of ground as sampled: its centre and a bound round it (metres), its real edge points, its core points (well inside), and its bridge (−1 for a land).</summary>
    private sealed class Piece
    {
        public required Vector3 Centre;
        public required float BoundMetres;
        public readonly List<Vector3> Edge = [];
        public readonly List<Vector3> Core = []; // points well inside it, for "shares ground with"
        public int Span = -1;
    }

    /// <summary>
    /// Every pair of pieces closer than <paramref name="upToMetres"/> across open sky, with whether they are tempting given
    /// that a plain hop crosses up to <paramref name="hopMetres"/>.
    /// </summary>
    public static List<ShellSeparation> Find(BrokenShell shell, float upToMetres, float hopMetres)
    {
        var r = shell.Radius;
        var pieces = Pieces(shell);
        var n = pieces.Count;

        // Closest approach per pair.
        var found = new List<ShellSeparation>();
        var touch = new bool[n, n];
        var near = new List<Vector3>();
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                var pi = pieces[i];
                var pj = pieces[j];
                var centres = PlanetShape.Angle(pi.Centre, pj.Centre) * r;
                if (centres - pi.BoundMetres - pj.BoundMetres > upToMetres) continue;
                // Only pieces whose bounds overlap can share ground.
                if (centres < pi.BoundMetres + pj.BoundMetres && Shares(shell, pi, pj, i, j)) { touch[i, j] = touch[j, i] = true; continue; }
                // Only edge points within reach of the other piece's bound can be the closest pair.
                var cosNearJ = MathF.Cos(MathF.Min(MathF.PI, (pj.BoundMetres + upToMetres) / r));
                var cosNearI = MathF.Cos(MathF.Min(MathF.PI, (pi.BoundMetres + upToMetres) / r));
                near.Clear();
                foreach (var b in pj.Edge) if (Vector3.Dot(b, pi.Centre) >= cosNearI) near.Add(b);
                var bestDot = -2f;
                Vector3 ba = default, bb = default;
                foreach (var a in pi.Edge)
                {
                    if (Vector3.Dot(a, pj.Centre) < cosNearJ) continue;
                    foreach (var b in near)
                    {
                        var d = Vector3.Dot(a, b);
                        if (d > bestDot) { bestDot = d; ba = a; bb = b; }
                    }
                }

                if (bestDot < -1.5f) continue;
                var metres = PlanetShape.Angle(ba, bb) * r;
                if (metres > upToMetres) continue;
                var mid = Vector3.Normalize(ba + bb);
                if (metres > 0.3f && !shell.OverHole(mid)) continue; // other ground lies between: not one gap
                var designed = pi.Span >= 0 && pi.Span == pj.Span;
                var opening = false;
                for (var k = 1; k < 6 && !opening; k++)
                {
                    var q = Vector3.Normalize(Vector3.Lerp(ba, bb, k / 6f));
                    opening = shell.InFrontOpening(q, OpeningReachMetres) || PlanetShape.Angle(q, shell.JetAxis) * r < HazardTerrain.JetClearMetres + OpeningReachMetres
                        || PlanetShape.Angle(q, -shell.JetAxis) * r < HazardTerrain.JetClearMetres + OpeningReachMetres;
                }
                found.Add(new ShellSeparation(i, j, ba, bb, metres, designed, false, opening));
            }
        }

        // The ground graph: shared ground, and plain hops (designed gaps of a plain class included; leaps are not plain).
        var cost = new float[n, n];
        for (var i = 0; i < n; i++) for (var j = 0; j < n; j++) cost[i, j] = float.PositiveInfinity;
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                if (touch[i, j]) cost[i, j] = PlanetShape.Angle(pieces[i].Centre, pieces[j].Centre) * r;
            }
        }

        foreach (var s in found)
        {
            if (s.Metres > hopMetres) continue;
            var c = PlanetShape.Angle(pieces[s.PieceA].Centre, pieces[s.PieceB].Centre) * r + s.Metres;
            cost[s.PieceA, s.PieceB] = cost[s.PieceB, s.PieceA] = MathF.Min(cost[s.PieceA, s.PieceB], c);
        }

        var result = new List<ShellSeparation>(found.Count);
        var distCache = new Dictionary<int, float[]>();
        foreach (var s in found)
        {
            if (!distCache.TryGetValue(s.PieceA, out var dist)) distCache[s.PieceA] = dist = Dijkstra(cost, s.PieceA);
            var straight = PlanetShape.Angle(pieces[s.PieceA].Centre, pieces[s.PieceB].Centre) * r;
            result.Add(s with { Tempting = dist[s.PieceB] > TemptRatio * straight + TemptSlackMetres });
        }

        return result;
    }

    /// <summary>The number of pieces (lands, then bridge stretches) <see cref="Find"/> numbers.</summary>
    public static int PieceCount(BrokenShell shell) => shell.Lands.Count + shell.Spans.Sum(s => s.Gaps.Count + 1);

    /// <summary>For a piece number of a bridge stretch: the bridge's index and the stretch (0 before its first gap); (−1, −1) for a land.</summary>
    public static (int Span, int Stretch) SpanOf(BrokenShell shell, int piece)
    {
        var k = piece - shell.Lands.Count;
        if (k < 0) return (-1, -1);
        for (var s = 0; s < shell.Spans.Count; s++)
        {
            var count = shell.Spans[s].Gaps.Count + 1;
            if (k < count) return (s, k);
            k -= count;
        }

        return (-1, -1);
    }

    private static List<Piece> Pieces(BrokenShell shell)
    {
        var r = shell.Radius;
        var list = new List<Piece>();
        foreach (var l in shell.Lands)
        {
            var p = new Piece { Centre = l.Centre, BoundMetres = l.MaxRadiusRadians * r };
            var (ta, tb) = PlanetShape.Tangents(l.Centre);
            for (var i = 0; i < LandSamples; i++)
            {
                var th = 2 * MathF.PI * i / LandSamples;
                var e = l.BoundaryPoint(th);
                var outward = ta * MathF.Cos(th) + tb * MathF.Sin(th);
                // Just inside, for "shares ground"; the edge when open sky lies just beyond.
                var inside = Vector3.Normalize(Vector3.Lerp(e, l.Centre, 0.03f));
                p.Core.Add(inside);
                var o = e - l.Centre * Vector3.Dot(e, l.Centre);
                var beyond = o.LengthSquared() > 1e-10f ? PlanetFrame.Move(e, Vector3.Normalize(o), EdgeProbeMetres, r) : e;
                if (shell.OverHole(beyond)) p.Edge.Add(e);
            }

            p.Core.Add(l.Centre);
            list.Add(p);
        }

        for (var k = 0; k < shell.Spans.Count; k++)
        {
            var s = shell.Spans[k];
            var bounds = new List<(float From, float To)>();
            var from = 0f;
            foreach (var (g0, g1) in s.Gaps) { bounds.Add((from, g0)); from = g1; }
            bounds.Add((from, s.LengthMetres));
            for (var seg = 0; seg < bounds.Count; seg++)
            {
                var (a0, a1) = bounds[seg];
                var midAlong = (a0 + a1) / 2;
                var p = new Piece { Centre = s.PointAlong(midAlong), BoundMetres = (a1 - a0) / 2 + s.HalfWidthMetres + ShellSpan.RagMetres + ShellSpan.AdvancedLedgeMetres + 4f, Span = k };
                var steps = Math.Max(1, (int)MathF.Ceiling((a1 - a0) / SpanSampleMetres));
                for (var i = 0; i <= steps; i++)
                {
                    var along = a0 + (a1 - a0) * i / steps;
                    p.Core.Add(s.PointAlong(along));
                    foreach (var left in new[] { true, false })
                    {
                        var w = s.HalfWidthAt(along, left);
                        var sign = left ? 1f : -1f;
                        var e = s.PointAt(along, sign * (w - 0.02f));
                        if (shell.OverHole(s.PointAt(along, sign * (w + EdgeProbeMetres)))) p.Edge.Add(e);
                    }
                }

                // The faces across the bridge at its gaps.
                for (var gi = 0; gi < s.Gaps.Count; gi++)
                {
                    foreach (var start in new[] { true, false })
                    {
                        var faceAlong = start ? s.Gaps[gi].FromMetres : s.Gaps[gi].ToMetres;
                        if ((start && MathF.Abs(faceAlong - a1) > 1e-3f) || (!start && MathF.Abs(faceAlong - a0) > 1e-3f)) continue;
                        var hw = s.HalfWidthMetres + ShellSpan.LedgeMetres;
                        for (var side = -hw; side <= hw + 1e-3f; side += SpanSampleMetres)
                        {
                            var jag = s.GapJagAt(gi, start, side);
                            var at = start ? faceAlong - jag - 0.02f : faceAlong + jag + 0.02f;
                            var e = s.PointAt(at, side);
                            if (s.InsideMetres(e) <= 0) continue;
                            if (shell.OverHole(s.PointAt(start ? at + EdgeProbeMetres + 0.02f : at - EdgeProbeMetres - 0.02f, side))) p.Edge.Add(e);
                        }
                    }
                }

                list.Add(p);
            }
        }

        return list;
    }

    /// <summary>True when two pieces share ground: a point well inside one lies on the other.</summary>
    private static bool Shares(BrokenShell shell, Piece a, Piece b, int ia, int ib)
    {
        var lands = shell.Lands.Count;
        bool On(Piece p, int index, Vector3 q)
        {
            if (index < lands) return shell.Lands[index].Contains(q);
            var s = shell.Spans[p.Span];
            if (Vector3.Dot(q, s.Mid) < s.CosBound) return false;
            if (s.InsideMetres(q) <= 0) return false;
            // On this bridge; and on this stretch of it (not across one of its gaps).
            var along = s.Measure(q).AlongMetres;
            return PieceAt(shell, p.Span, along) == index - lands;
        }

        foreach (var q in b.Core) if (On(a, ia, q)) return true;
        foreach (var q in a.Core) if (On(b, ib, q)) return true;
        return false;
    }

    /// <summary>The piece offset (after the lands) of bridge <paramref name="span"/>'s stretch at <paramref name="along"/>.</summary>
    private static int PieceAt(BrokenShell shell, int span, float along)
    {
        var k = 0;
        for (var s = 0; s < span; s++) k += shell.Spans[s].Gaps.Count + 1;
        return k + shell.Spans[span].Gaps.Count(g => g.ToMetres <= along);
    }

    private static float[] Dijkstra(float[,] cost, int from)
    {
        var n = cost.GetLength(0);
        var dist = new float[n];
        Array.Fill(dist, float.PositiveInfinity);
        dist[from] = 0;
        var done = new bool[n];
        for (var it = 0; it < n; it++)
        {
            var u = -1;
            for (var i = 0; i < n; i++) if (!done[i] && (u < 0 || dist[i] < dist[u])) u = i;
            if (u < 0 || float.IsPositiveInfinity(dist[u])) break;
            done[u] = true;
            for (var v = 0; v < n; v++)
            {
                var c = cost[u, v];
                if (!float.IsPositiveInfinity(c) && dist[u] + c < dist[v]) dist[v] = dist[u] + c;
            }
        }

        return dist;
    }
}
