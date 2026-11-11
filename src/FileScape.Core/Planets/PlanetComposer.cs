using System.Globalization;
using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;

namespace FileScape.Core.Planets;

/// <summary>
/// Parameters of a planet's composition (planet frame, metres; angles named "…Deg" are degrees). Defaults follow the
/// Planets brief: launchers ≤ 10, belt moons ≤ 48, individual items ≤ 64.
/// </summary>
public sealed record PlanetParams
{
    public static readonly PlanetParams Default = new();

    /// <summary>Child launchers at most; further child folders become belt moons.</summary>
    public int MaxLaunchers { get; init; } = 28;

    /// <summary>The first launcher ring's distance from the arrival point along the surface, metres (2026-11-12: launchers in rings round the arrival).</summary>
    public float RingFirst { get; init; } = 9f;

    /// <summary>Distance between launcher rings, metres.</summary>
    public float RingGap { get; init; } = 7f;

    /// <summary>Least distance between neighbouring pads on a ring, metres (centre to centre).</summary>
    public float RingSpacing { get; init; } = 6.4f;

    /// <summary>Belt moons at most; further child folders are counted in <see cref="PlanetComposition.OmittedFolders"/>.</summary>
    public int MaxBelt { get; init; } = 48;

    /// <summary>Individual items (files and file links) at most, shared between the groves; piles stand for the rest.</summary>
    public int MaxItems { get; init; } = 64;

    /// <summary>The fewest individual items a grove gets when the budget is shared (if it has that many files).</summary>
    public int MinItemsPerGrove { get; init; } = 2;

    /// <summary>Spacing of the slot set groves grow over, metres.</summary>
    public float GroveSpacing { get; init; } = 2.2f;

    /// <summary>
    /// The least gap between two solids that block walking (items, trunks, the landmark, the overlook), metres: wider
    /// than a body (0.7 m) with room to spare, so every gap is a way through.
    /// </summary>
    public float SolidGap { get; init; } = 1.25f;

    /// <summary>Half-width of the clear routes from the arrival to each launcher and to the overlook's steps, metres.</summary>
    public float PathHalfWidth { get; init; } = 1.0f;

    /// <summary>Flat, item-free radius of the arrival meadow, metres.</summary>
    public float ArrivalRadius { get; init; } = 4f;

    public float PadRadius { get; init; } = 1.3f;
    public float PadTop { get; init; } = 0.2f;

    /// <summary>Radius around a launcher kept free of items and solid scenery, metres (covers the return stand point).</summary>
    public float LauncherClearRadius { get; init; } = 3.3f;

    /// <summary>How far behind the arrival (along +Z) the way back stands, metres.</summary>
    public float ParentBehind { get; init; } = 5.5f;

    /// <summary>The landmark's angle from the arrival pole straight ahead, degrees.</summary>
    public float LandmarkPolarDeg { get; init; } = 40f;

    public float LandmarkRadius { get; init; } = 1.6f;
    public float LandmarkTop { get; init; } = 10f;

    /// <summary>The gap between the landmark and the overlook platform, metres.</summary>
    public float LandmarkOverlookGap { get; init; } = 1.5f;

    public float OverlookTop { get; init; } = 2.4f;
    public float OverlookRadius { get; init; } = 2.2f;

    /// <summary>Stepping stones up to the overlook (6–8); they rise evenly from the ground to the platform.</summary>
    public int StepCount { get; init; } = 7;

    public float StepRadius { get; init; } = 0.6f;

    /// <summary>The largest rise between stones (the walker's step height is 0.45 m).</summary>
    public float MaxStepRise { get; init; } = 0.4f;

    /// <summary>A single jump reaches this high, metres (the app's walker; recorded for the overlook's design).</summary>
    public float JumpHeight { get; init; } = 1.2f;

    /// <summary>A jump and boost reaches this high, metres: above <see cref="OverlookTop"/>, so the overlook is a jump target.</summary>
    public float JumpBoostHeight { get; init; } = 2.7f;

    /// <summary>A child's body hangs this far above the ground over its launcher (range), metres.</summary>
    public float SkyHeightMin { get; init; } = 18f;

    public float SkyHeightMax { get; init; } = 30f;
    public float SkyRadiusMin { get; init; } = 3f;
    public float SkyRadiusMax { get; init; } = 8f;

    /// <summary>The belt's radius as a multiple of the planet's (kept outside the launcher bodies).</summary>
    public float BeltRadiusFactor { get; init; } = 2.3f;

    public float BeltTiltDeg { get; init; } = 22f;

    /// <summary>What the platform and the user say about places, for ranking launchers (PROM-01).</summary>
    public ProminenceContext Prominence { get; init; } = ProminenceContext.Empty;
}

/// <summary>
/// Composes one folder as a small walkable planet (the Planets experience; see <see cref="PlanetComposition"/>). The
/// result depends only on the folder, its direct entries, their own direct entries (for themes and sizes) and the
/// parent, so it is deterministic and cheap; it never asks for a listing.
/// </summary>
public static class PlanetComposer
{
    /// <summary>How far beyond its pad's edge one stands when coming back from a launcher's target, metres.</summary>
    public const float ReturnStandOff = 1.2f;

    private const float PileScale = 1.3f;
    private const float GroveFlatRadius = 1.5f;
    private static readonly IComparer<string> NameOrder = NaturalNameOrder.Instance;

    public static float ItemRadius(float scale) => 0.45f * scale;
    public static float ItemTop(float scale) => 1.1f * scale;

    /// <summary>An item's scale from its size: 0.6 at ≤ 100 B rising with log10 to 1.6 at ≥ 1 GB.</summary>
    public static float ScaleFor(long bytes) => 0.6f + Math.Clamp((MathF.Log10(MathF.Max(1, bytes)) - 2f) / 7f, 0, 1);

    private static float Deg(float degrees) => degrees * MathF.PI / 180f;

    // ------------------------------------------------------------------ theme

    private static readonly string[] ProjectExtensions = [".sln", ".slnx", ".csproj"];
    private static readonly string[] ProjectNames = ["package.json", "Cargo.toml", "pyproject.toml", "go.mod"];

    /// <summary>True when the folder's own entries name a project (a solution, a C# project, a manifest, or .git).</summary>
    public static bool HasProjectEvidence(FsTree tree, FsNode folder)
    {
        foreach (var c in folder.Children)
        {
            var n = tree[c];
            if (n.Name == ".git") return true;
            if (n.Kind != NodeKind.File) continue;
            if (ProjectNames.Any(m => m.Equals(n.Name, StringComparison.OrdinalIgnoreCase))) return true;
            if (ProjectExtensions.Any(e => n.Name.EndsWith(e, StringComparison.OrdinalIgnoreCase))) return true;
        }

        return false;
    }

    /// <summary>The theme a folder's planet takes from what is known of its own entries (never asks for a listing).</summary>
    public static PlanetTheme ThemeOf(FsTree tree, int folderId, Func<FsNode, bool>? visible = null)
    {
        var folder = tree[folderId];
        if (folder.Kind == NodeKind.Link)
        {
            return folder.Link is { Status: LinkStatus.Resolved, TargetId: >= 0 } li && tree.Contains(li.TargetId) && tree[li.TargetId].IsDirectory
                ? ThemeOf(tree, li.TargetId, visible)
                : PlanetTheme.Veiled;
        }

        if (!folder.IsDirectory) return PlanetTheme.Veiled;
        if (IsHub(folder)) return PlanetTheme.Hearth;
        var state = Listing.Of(folder);
        if (state == ListingState.Empty) return PlanetTheme.Quiet;
        if (state is not (ListingState.Open or ListingState.Partial) || folder.Children.Count == 0) return PlanetTheme.Veiled;
        if (HasProjectEvidence(tree, folder)) return PlanetTheme.Workshop;

        Span<int> counts = stackalloc int[(int)PlanetTheme.Veiled + 1];
        int files = 0, entries = 0;
        foreach (var c in folder.Children)
        {
            var n = tree[c];
            if (visible != null && !visible(n)) continue;
            entries++;
            if (n.Kind != NodeKind.File) continue;
            files++;
            counts[(int)ThemeFor(FileCategories.Of(n.Name))]++;
        }

        if (entries == 0) return PlanetTheme.Quiet;
        if (files == 0) return PlanetTheme.Hearth;
        var best = PlanetTheme.Wild;
        var bestCount = 0;
        for (var t = 0; t < counts.Length; t++)
        {
            if (counts[t] > bestCount) { bestCount = counts[t]; best = (PlanetTheme)t; }
        }

        return bestCount >= 0.45f * files ? best : PlanetTheme.Wild;
    }

    private static PlanetTheme ThemeFor(FileCategory c) => c switch
    {
        FileCategory.Image => PlanetTheme.Meadow,
        FileCategory.Code => PlanetTheme.Workshop,
        FileCategory.Document => PlanetTheme.Library,
        FileCategory.Audio => PlanetTheme.Chime,
        FileCategory.Video => PlanetTheme.Theatre,
        FileCategory.Archive or FileCategory.Executable => PlanetTheme.Vault,
        FileCategory.Data => PlanetTheme.Crystal,
        _ => PlanetTheme.Wild,
    };

    private static bool IsHub(FsNode n) => n.IsHub || (n.IsRoot && n.Name == "This computer");

    // ------------------------------------------------------------------ signature

    /// <summary>
    /// A cheap fingerprint of everything the planet shows: the folder's entries and state, each child folder's or link's
    /// state, entry counts and file count (its theme and size), link resolution, and the parent's. The app recomposes
    /// the planet only when it changes.
    /// </summary>
    public static string Signature(FsTree tree, int folderId)
    {
        var f = tree[folderId];
        unchecked
        {
            var h = 1469598103934665603UL;
            void Mix(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
            Mix(f.Children.Count);
            Mix((long)f.ScanState);
            Mix((long)f.Flags);
            Mix(f.SubtreeFileCount);
            Mix(f.SubtreeDirCount);
            Mix(f.SubtreeUnsettledDirs);
            foreach (var c in f.Children)
            {
                var n = tree[c];
                if (n.Kind == NodeKind.File) continue;
                Mix(c);
                Mix(n.Children.Count);
                Mix((long)n.ScanState);
                Mix((long)n.Flags);
                Mix(n.DirectFileCount);
                Mix(n.SubtreeFileCount + n.SubtreeDirCount);
                Mix(n.SubtreeUnsettledDirs);
                if (n.Link is { } li)
                {
                    Mix((long)li.Status);
                    Mix(li.TargetId);
                    if (li.TargetId >= 0 && tree.Contains(li.TargetId))
                    {
                        var t = tree[li.TargetId];
                        Mix(t.Children.Count);
                        Mix((long)t.ScanState);
                    }
                }
            }

            if (!f.IsRoot)
            {
                var parent = tree[f.ParentId];
                Mix(parent.Children.Count);
                Mix((long)parent.ScanState);
                Mix(parent.SubtreeFileCount + parent.SubtreeDirCount);
            }

            return h.ToString("x16", CultureInfo.InvariantCulture);
        }
    }

    // ------------------------------------------------------------------ compose

    private sealed record Candidate(int Id, FsNode Node, bool IsLink, bool Followable, int LinkTargetId, ProminenceInfo Prominence, long Size);

    /// <summary>A solid already placed, for gap checks (items, solid scenery, the landmark complex).</summary>
    private readonly record struct Solid(Vector3 Direction, float Radius);

    public static PlanetComposition Compose(FsTree tree, int folderId, PlanetParams? parameters = null, Func<FsNode, bool>? visible = null)
    {
        var p = parameters ?? PlanetParams.Default;
        var folder = tree[folderId];
        if (!folder.IsDirectory) throw new ArgumentException($"#{folderId} is not a directory", nameof(folderId));
        if (p.StepCount < 1 || p.OverlookTop / (p.StepCount + 1) > p.MaxStepRise + 1e-4f)
        {
            throw new ArgumentException("The overlook's stepping stones would rise more than MaxStepRise.", nameof(parameters));
        }

        var state = Listing.Of(folder);
        var name = tree.DisplayName(folderId);
        var theme = ThemeOf(tree, folderId, visible);

        // ---- entries
        var candidates = new List<Candidate>();
        var fileIds = new List<int>();
        int hidden = 0, links = 0;
        foreach (var c in folder.Children)
        {
            var n = tree[c];
            if (visible != null && !visible(n)) { hidden++; continue; }
            if (n.Kind == NodeKind.Link) links++;
            if (n.IsDirectory || (n.Kind == NodeKind.Link && (n.Flags & NodeFlags.DirectoryLink) != 0))
            {
                var isLink = n.Kind == NodeKind.Link;
                var linkTarget = isLink && n.Link is { Status: LinkStatus.Resolved, TargetId: >= 0 } li && tree.Contains(li.TargetId) ? li.TargetId : -1;
                var sizeNode = linkTarget >= 0 ? tree[linkTarget] : n;
                var prominence = n.IsDirectory ? Prominence.For(tree, c, p.Prominence) : ProminenceInfo.None;
                candidates.Add(new Candidate(c, n, isLink, !isLink || (n.Link?.CanFollow ?? false), linkTarget, prominence,
                    sizeNode.IsDirectory ? sizeNode.SubtreeFileCount + sizeNode.SubtreeDirCount : 0));
            }
            else
            {
                fileIds.Add(c);
            }
        }

        var counts = new PlanetChildCounts(candidates.Count(c => !c.IsLink), fileIds.Count, links, hidden);

        // ---- launchers, belt, omitted (ranked: launchable, prominence, size, name)
        var ranked = candidates
            .OrderByDescending(c => c.Followable)
            .ThenByDescending(c => c.Prominence.Level)
            .ThenByDescending(c => c.Size)
            .ThenBy(c => c.Node.Name, NameOrder)
            .ThenBy(c => c.Id)
            .ToList();
        var launched = ranked.Take(p.MaxLaunchers).OrderBy(c => c.Node.Name, NameOrder).ThenBy(c => c.Id).ToList();
        var belt = ranked.Skip(p.MaxLaunchers).Take(p.MaxBelt).OrderBy(c => c.Node.Name, NameOrder).ThenBy(c => c.Id).ToList();
        var omitted = Math.Max(0, ranked.Count - p.MaxLaunchers - p.MaxBelt);

        // ---- file budget per grove
        var groups = fileIds
            .GroupBy(id => FileCategories.Of(tree[id].Name))
            .Select(g => (Category: g.Key, Ids: g.OrderBy(id => tree[id].Name, NameOrder).ThenBy(id => id).ToList()))
            .OrderByDescending(g => g.Ids.Count)
            .ThenBy(g => g.Category)
            .ToList();
        var budget = ShareBudget(groups.Select(g => g.Ids.Count).ToList(), p.MaxItems, p.MinItemsPerGrove);
        var itemCount = budget.Sum() + groups.Where((g, i) => budget[i] < g.Ids.Count).Count();

        // ---- radius and shape parameters
        var nothingShown = itemCount == 0 && launched.Count == 0;
        var radius = theme == PlanetTheme.Quiet ? 10f
            : theme == PlanetTheme.Veiled && nothingShown ? 12f
            : Math.Clamp(13f + 1.5f * MathF.Sqrt(itemCount + 4f * launched.Count), 16f, 34f);
        var seed = NameSeed(name);
        var hillAmplitude = theme is PlanetTheme.Quiet or PlanetTheme.Veiled ? 0.25f : 0.6f + 0.3f * Frac(seed, 1);

        // ---- arrival, landmark, overlook and its steps
        var pole = Vector3.UnitY;
        var hasParent = !folder.IsRoot;
        var rings = LauncherRings(launched.Count, hasParent, radius, p);
        // The landmark stands ahead beyond the outermost ring (it used to stand at 40°, among the launchers).
        var outer = rings.Count == 0 ? 0f : rings.Max(r => r.DistanceMetres);
        var landmarkPolar = MathF.Max(Deg(p.LandmarkPolarDeg), (outer + p.PadRadius + p.LandmarkRadius + 4.5f) / radius);
        // On the bearing near ahead farthest from every pad's bearing, so the walk out to it passes between pads.
        var landmarkAz = 0f;
        if (rings.Count > 0)
        {
            var best = -1f;
            for (var deg = -60; deg <= 60; deg++)
            {
                var az = Deg(deg);
                var clear = rings.Min(r => MathF.Abs(MathF.IEEERemainder(az - PlanetFrame.AzimuthOf(r.Direction), 2 * MathF.PI)) * r.DistanceMetres);
                if (clear > best + 0.01f || (MathF.Abs(clear - best) <= 0.01f && MathF.Abs(az) < MathF.Abs(landmarkAz))) { best = clear; landmarkAz = az; }
            }
        }

        var landmarkDir = PlanetFrame.FromPolar(MathF.Min(landmarkPolar, Deg(150f)), landmarkAz);
        var overlookDir = PlanetFrame.Move(landmarkDir, PlanetFrame.East(landmarkDir), p.LandmarkRadius + p.LandmarkOverlookGap + p.OverlookRadius, radius);
        var steps = new List<SurfaceObstacle>();
        {
            var north = PlanetFrame.North(overlookDir);
            var east = PlanetFrame.East(overlookDir);
            var ring = p.OverlookRadius + p.StepRadius;
            var stepAngle = 2 * MathF.Asin(p.StepRadius / ring) * 1.03f; // a hair more than touching: the sphere draws stones together
            var start = Deg(10f); // the first stone faces the arrival, a little away from the landmark (west)
            var rise = p.OverlookTop / (p.StepCount + 1);
            for (var i = 0; i < p.StepCount; i++)
            {
                var a = start + i * stepAngle;
                var d = PlanetFrame.Move(overlookDir, north * MathF.Cos(a) + east * MathF.Sin(a), ring, radius);
                steps.Add(new SurfaceObstacle(d, p.StepRadius, rise * (i + 1)));
            }
        }

        var overlook = new PlanetOverlook(overlookDir, p.OverlookTop, p.OverlookRadius, steps);
        var landmark = new PlanetLandmark(landmarkDir, p.LandmarkRadius, p.LandmarkTop);
        var complexRadius = p.OverlookRadius + 2 * p.StepRadius; // the overlook with its ring of stones

        // ---- launcher directions
        var launcherDirs = rings.Where(r => !r.Parent).Select(r => r.Direction).ToList();
        var launchers = new List<PlanetLauncher>();
        var sky = new List<SkyBody>();
        var paths = new List<PlanetPath>();
        var signs = new List<PlanetSign>();
        for (var i = 0; i < launched.Count; i++)
        {
            var c = launched[i];
            var d = launcherDirs[i];
            var childState = Listing.Of(c.Node);
            var themeNode = c.LinkTargetId >= 0 ? c.LinkTargetId : c.Id;
            var childTheme = c.IsLink && c.LinkTargetId < 0 ? PlanetTheme.Veiled : ThemeOf(tree, themeNode, visible);
            var h = SeedOf(c.Node.Name, c.Id);
            var lift = p.SkyHeightMin + (p.SkyHeightMax - p.SkyHeightMin) * Frac(h, 2);
            var jitter = (Frac(h, 3) - 0.5f) * 4f;
            // On the same bearing as its pad but further out (2026-11-12): the worlds stand round the horizon as a crown
            // you see from the arrival; walking towards one brings you to its pad on the ring.
            var padPolar = PlanetFrame.PolarOf(d);
            // Kept above the arrival's horizon as the chase camera sees it (eye ≈ 3.4 m), whole body and a margin.
            var bodyR = SkyRadius(c.Size, p);
            var horizon = MathF.Acos(radius / (radius + 3.4f)) + MathF.Acos(radius / (radius + lift)) - MathF.Asin(MathF.Min(1, bodyR / (radius + lift))) - Deg(3f);
            var skyPolar = MathF.Max(padPolar + Deg(4f), MathF.Min(MathF.Min(padPolar + Deg(32f), Deg(84f)), horizon));
            var skyDir = PlanetFrame.FromPolar(skyPolar, PlanetFrame.AzimuthOf(d));
            var pos = skyDir * (radius + lift) + PlanetFrame.East(skyDir) * jitter;
            sky.Add(new SkyBody(c.Id, pos, SkyRadius(c.Size, p), childTheme, childState, launchers.Count));
            launchers.Add(new PlanetLauncher(c.Id, d, p.PadRadius, p.PadTop, childState, c.Node.Name, sky.Count - 1,
                IsLink: c.IsLink, Followable: c.Followable, LinkTargetId: c.LinkTargetId, Prominence: c.Prominence));
            paths.Add(new PlanetPath(c.Id, [pole, d]));
            signs.Add(new PlanetSign(d, 2.8f, c.Node.Name, LauncherDetail(tree, c), c.Id));
        }

        if (!folder.IsRoot)
        {
            var parent = tree[folder.ParentId];
            // Behind and a little to the right of the arrival (2026-11-11, from the first-view screenshots: straight behind,
            // the chase camera stood over its petals, which filled the bottom of the first view).
            // The way back has a fixed place: on the first ring, straight behind the arrival (6 o'clock).
            var d = rings.First(r => r.Parent).Direction;
            var parentSize = parent.SubtreeFileCount + parent.SubtreeDirCount;
            var pos = PlanetFrame.FromPolar(Deg(35f), MathF.PI) * (radius + 24f);
            sky.Add(new SkyBody(parent.Id, pos, MathF.Max(5f, SkyRadius(parentSize, p)), ThemeOf(tree, parent.Id, visible), Listing.Of(parent), launchers.Count, IsParent: true));
            launchers.Add(new PlanetLauncher(parent.Id, d, p.PadRadius, p.PadTop, Listing.Of(parent), tree.DisplayName(parent.Id), sky.Count - 1, IsParent: true));
            signs.Add(new PlanetSign(d, 2.4f, tree.DisplayName(parent.Id), "the way back", parent.Id));
        }

        // Belt: tiny moons on a tilted ring, outside the launcher bodies.
        if (belt.Count > 0)
        {
            var ringRadius = MathF.Max(p.BeltRadiusFactor * radius, radius + p.SkyHeightMax + p.SkyRadiusMax + 4f);
            var tilt = Matrix4x4.CreateRotationX(Deg(p.BeltTiltDeg)) * Matrix4x4.CreateRotationY(Deg(Frac(seed, 4) * 360f));
            for (var i = 0; i < belt.Count; i++)
            {
                var c = belt[i];
                var a = 2 * MathF.PI * i / belt.Count;
                var local = new Vector3(MathF.Sin(a), 0, -MathF.Cos(a)) * ringRadius;
                var size = 0.5f + 0.7f * Math.Clamp(MathF.Log10(1 + c.Size) / 4f, 0, 1);
                var t = c.IsLink && c.LinkTargetId < 0 ? PlanetTheme.Veiled : ThemeOf(tree, c.LinkTargetId >= 0 ? c.LinkTargetId : c.Id, visible);
                sky.Add(new SkyBody(c.Id, Vector3.Transform(local, tilt), size, t, Listing.Of(c.Node), -1, InBelt: true));
            }
        }

        // The route to the overlook: from the arrival to just outside the first stone.
        {
            var foot = steps[0].Direction;
            var outward = foot - overlookDir;
            var footOut = PlanetFrame.Move(foot, outward - foot * Vector3.Dot(outward, foot), p.StepRadius + 0.8f, radius);
            paths.Add(new PlanetPath(-1, [pole, footOut, foot]));
        }

        // ---- exclusion zones (no items or solid scenery)
        var keepOut = new List<Solid>
        {
            new(pole, p.ArrivalRadius),
            new(landmarkDir, p.LandmarkRadius + p.SolidGap),
            new(overlookDir, complexRadius + p.SolidGap),
        };
        foreach (var l in launchers) keepOut.Add(new Solid(l.Direction, p.LauncherClearRadius));

        var solids = new List<Solid>();
        var segments = new List<(Vector3 A, Vector3 B, Vector3 Normal)>();
        foreach (var path in paths)
        {
            for (var k = 1; k < path.Points.Count; k++)
            {
                var n = Vector3.Cross(path.Points[k - 1], path.Points[k]);
                segments.Add((path.Points[k - 1], path.Points[k], n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.Zero));
            }
        }

        var maxZone = keepOut.Max(z => z.Radius);
        var ringDistances = rings.Select(r => r.DistanceMetres).Distinct().ToArray();
        // Distances are compared through dot products first (one multiply-add per solid), and measured exactly only
        // for the few candidates that are close: composition runs whenever a listing changes, so it must stay cheap.
        bool Free(Vector3 d, float r, float gap, List<Solid> extraZones)
        {
            var nearCos = MathF.Cos(MathF.Min(MathF.PI, (maxZone + r + 3f) / radius));
            foreach (var z in keepOut)
            {
                if (Vector3.Dot(d, z.Direction) >= nearCos && PlanetShape.Angle(d, z.Direction) * radius < z.Radius + r) return false;
            }

            foreach (var z in extraZones)
            {
                if (PlanetShape.Angle(d, z.Direction) * radius < z.Radius + r) return false;
            }

            // The launcher rings are walking routes: nothing stands on them (2026-11-12 review).
            var fromArrival = PlanetFrame.PolarOf(d) * radius;
            foreach (var ringDistance in ringDistances)
            {
                if (MathF.Abs(fromArrival - ringDistance) < p.PathHalfWidth + r) return false;
            }

            var pathSin = MathF.Sin((p.PathHalfWidth + r) / radius);
            foreach (var (a, b, n) in segments)
            {
                if (n != Vector3.Zero && MathF.Abs(Vector3.Dot(d, n)) >= pathSin) continue; // farther from the whole great circle
                if (PlanetFrame.DistanceToArc(d, a, b, radius) < p.PathHalfWidth + r) return false;
            }

            var solidCos = MathF.Cos(MathF.Min(MathF.PI, (r + gap + 2f) / radius)); // solids are at most ≈ 1.5 m in radius
            foreach (var s in solids)
            {
                if (Vector3.Dot(d, s.Direction) < solidCos) continue;
                if (PlanetShape.Angle(d, s.Direction) * radius < s.Radius + r + gap) return false;
            }

            return true;
        }

        // ---- groves
        var items = new List<PlanetItem>();
        var groves = new List<PlanetGrove>();
        if (groups.Count > 0)
        {
            PlaceGroves(tree, groups, budget, radius, p, Free, solids, items, groves);
        }

        foreach (var g in groves)
        {
            signs.Add(new PlanetSign(g.CentreDirection, 2.6f, g.Label, g.Hidden > 0 ? $"{N(g.Shown)} shown · {N(g.Hidden)} in the pile" : "", -1));
        }

        // ---- flat spots and shape
        var flats = new List<(Vector3 Direction, float Radius)> { (pole, p.ArrivalRadius), (overlookDir, complexRadius + 0.3f) };
        foreach (var l in launchers) flats.Add((l.Direction, p.PadRadius + 0.7f));
        foreach (var g in groves) flats.Add((g.CentreDirection, GroveFlatRadius));
        var shape = new PlanetShape(radius, seed, hillAmplitude, flats);

        // ---- decorations
        var decorations = Decorate(theme, radius, seed, groves, Free, solids, p);

        // ---- obstacles
        var obstacles = new List<SurfaceObstacle>
        {
            new(landmarkDir, p.LandmarkRadius, p.LandmarkTop, folderId),
            new(overlookDir, p.OverlookRadius, p.OverlookTop),
        };
        obstacles.AddRange(steps);
        foreach (var l in launchers) obstacles.Add(new SurfaceObstacle(l.Direction, l.PadRadius, l.PadTop, l.TargetId));
        foreach (var it in items) obstacles.Add(new SurfaceObstacle(it.Direction, it.Radius, it.Top, it.NodeId));
        foreach (var d in decorations.Where(d => d.Collides))
        {
            var (r, top) = DecorationSolid(d.Kind, d.Scale);
            obstacles.Add(new SurfaceObstacle(d.Direction, r, top));
        }

        return new PlanetComposition
        {
            FolderId = folderId,
            Name = name,
            Theme = theme,
            Shape = shape,
            Surface = new PlanetSurface(shape, obstacles),
            State = state,
            Summary = FolderSummary.Place(folder, state),
            Arrival = new PlanetPose(pole, -Vector3.UnitZ),
            ArrivalRadius = p.ArrivalRadius,
            Landmark = landmark,
            Overlook = overlook,
            Launchers = launchers,
            SkyBodies = sky,
            OmittedFolders = omitted,
            Items = items,
            Groves = groves,
            Decorations = decorations,
            Signs = signs,
            Paths = paths,
            Counts = counts,
            TreeVersion = tree.Version,
            Signature = Signature(tree, folderId),
        };
    }

    private static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    private static float SkyRadius(long size, PlanetParams p) =>
        p.SkyRadiusMin + (p.SkyRadiusMax - p.SkyRadiusMin) * Math.Clamp(MathF.Log10(1 + size) / 4f, 0, 1);

    private static string LauncherDetail(FsTree tree, Candidate c)
    {
        if (!c.IsLink) return FolderSummary.Gate(c.Node, Listing.Of(c.Node));
        if (!c.Followable) return "link · cannot be followed";
        if (c.LinkTargetId >= 0)
        {
            var t = tree[c.LinkTargetId];
            return $"link to {tree.DisplayName(c.LinkTargetId)} · {FolderSummary.Gate(t, Listing.Of(t))}";
        }

        return c.Node.Link?.TargetText is { } text ? $"link to {text}" : "link";
    }

    /// <summary>
    /// Shares <paramref name="max"/> individual items between groups in proportion to their counts (largest remainder),
    /// with at least <paramref name="min"/> each when the group has that many; never more than a group has.
    /// </summary>
    private static int[] ShareBudget(List<int> counts, int max, int min)
    {
        var shown = new int[counts.Count];
        var total = counts.Sum();
        if (total <= max)
        {
            for (var i = 0; i < counts.Count; i++) shown[i] = counts[i];
            return shown;
        }

        var floor = new int[counts.Count];
        for (var i = 0; i < counts.Count; i++) floor[i] = Math.Min(counts[i], min);
        var left = Math.Max(0, max - floor.Sum());
        var extra = counts.Select((c, i) => (double)left * c / total).ToArray();
        for (var i = 0; i < counts.Count; i++)
        {
            shown[i] = Math.Min(counts[i], floor[i] + (int)Math.Floor(extra[i]));
        }

        var remaining = max - shown.Sum();
        foreach (var i in Enumerable.Range(0, counts.Count).OrderByDescending(i => extra[i] - Math.Floor(extra[i])).ThenBy(i => i))
        {
            if (remaining <= 0) break;
            if (shown[i] < counts[i]) { shown[i]++; remaining--; }
        }

        // When the minimum alone exceeds the budget, trim the smallest groups' extras (never below one).
        for (var i = counts.Count - 1; shown.Sum() > max && i >= 0; i--)
        {
            while (shown.Sum() > max && shown[i] > 1) shown[i]--;
        }

        return shown;
    }

    /// <summary>One slot on a launcher ring: its direction, its ring's surface distance from the arrival, and whether it is the way back.</summary>
    private readonly record struct RingSlot(Vector3 Direction, float DistanceMetres, bool Parent);

    /// <summary>
    /// Launchers in rings round the arrival meadow, read like a clock (2026-11-12, DJ's playtest: on the Ubuntu root
    /// "home" took a while to find): child folders in name order, clockwise from straight ahead (12 o'clock) on the
    /// first ring, then the next ring out; the way back always at 6 o'clock on the first ring. Every pad is within a
    /// few rings of where you land, so a named launcher is found from the arrival without circling the planet.
    /// </summary>
    private static List<RingSlot> LauncherRings(int n, bool hasParent, float radius, PlanetParams p)
    {
        var slots = new List<RingSlot>();
        var remaining = n;
        var distance = p.RingFirst;
        var first = true;
        var ring = 0;
        while (remaining > 0 || (first && hasParent))
        {
            var polar = MathF.Min(distance / radius, Deg(120f));
            var circumference = 2 * MathF.PI * radius * MathF.Sin(polar);
            var capacity = Math.Max(4, (int)(circumference / p.RingSpacing));
            var reserveBack = first && hasParent;
            var here = Math.Min(remaining, capacity - (reserveBack ? 1 : 0));
            var total = here + (reserveBack ? 1 : 0);
            // Without the way back: even slots, the first name straight ahead, then clockwise (increasing azimuth = to your
            // right as you stand). With it: the way back exactly behind (6 o'clock) and the names evenly across the rest,
            // from behind-left round through straight ahead to behind-right, so they read left to right in front of you.
            if (reserveBack)
            {
                var gap = MathF.PI / total;
                for (var i = 0; i < here; i++)
                {
                    var az = -MathF.PI + gap + (2 * MathF.PI - 2 * gap) * (i + 0.5f) / here;
                    slots.Add(new RingSlot(PlanetFrame.FromPolar(polar, az), distance, false));
                }

                slots.Add(new RingSlot(PlanetFrame.FromPolar(polar, MathF.PI), distance, true));
            }
            else
            {
                // Rings after the first are turned by the golden angle, so no outer pad shares an inner pad's bearing
                // (their worlds would stack on one line of sight, and the straight walk out would cross inner pads).
                var phase = 0f;
                if (!first)
                {
                    // The phase (within one pitch) that keeps this ring's pads farthest from every bearing already used.
                    var pitch = 2 * MathF.PI / here;
                    var bestSep = -1f;
                    for (var k = 0; k < 64; k++)
                    {
                        var ph = pitch * k / 64f;
                        var sep = float.MaxValue;
                        for (var i = 0; i < here && sep > bestSep; i++)
                        {
                            foreach (var used in slots) sep = MathF.Min(sep, MathF.Abs(MathF.IEEERemainder(ph + pitch * i - PlanetFrame.AzimuthOf(used.Direction), 2 * MathF.PI)));
                        }

                        if (sep > bestSep) { bestSep = sep; phase = ph; }
                    }
                }

                for (var i = 0; i < here; i++) slots.Add(new RingSlot(PlanetFrame.FromPolar(polar, phase + 2 * MathF.PI * i / here), distance, false));
            }

            remaining -= here;
            distance += p.RingGap;
            first = false;
            ring++;
        }

        return slots;
    }

    private delegate bool FreeCheck(Vector3 d, float r, float gap, List<Solid> extraZones);

    private sealed class GroveGrowth
    {
        public required Vector3 Seed;
        public readonly PriorityQueue<int, float> Frontier = new();
    }

    /// <summary>
    /// Grows one contiguous patch per category over a Fibonacci-sphere slot set (largest grove first), round-robin so
    /// no grove crowds out another, and fills each with its files in name order from its centre outwards, then its pile.
    /// </summary>
    private static void PlaceGroves(FsTree tree, List<(FileCategory Category, List<int> Ids)> groups, int[] budget, float radius,
        PlanetParams p, FreeCheck free, List<Solid> solids, List<PlanetItem> items, List<PlanetGrove> groves)
    {
        var slots = FibonacciSphere((int)MathF.Round(4 * MathF.PI * radius * radius / (0.866f * p.GroveSpacing * p.GroveSpacing)));
        // Neighbours on demand: Fibonacci points are ordered by height, so a slot's neighbours lie within a window of
        // indices (|Δy| ≤ the chord); only the slots a grove grows through are ever asked for.
        var chord = 2 * MathF.Sin(0.8f * p.GroveSpacing / radius);
        var window = (int)MathF.Ceiling(chord * slots.Length / 2f) + 1;
        var chord2 = chord * chord;
        IEnumerable<int> NeighboursOf(int i)
        {
            for (int j = Math.Max(0, i - window), end = Math.Min(slots.Length - 1, i + window); j <= end; j++)
            {
                if (j != i && Vector3.DistanceSquared(slots[i], slots[j]) <= chord2) yield return j;
            }
        }

        var taken = new bool[slots.Length];
        var noZones = new List<Solid>();

        // Seeds: the largest grove front-left of the arrival; each next one as far from the others as a grove needs
        // (and no farther: then nearest the arrival).
        var cap = 2 * MathF.Sqrt(groups.Max(g => g.Ids.Count > 0 ? Math.Min(g.Ids.Count, 80) : 1) * 1.4f * p.GroveSpacing * p.GroveSpacing / MathF.PI) + 4f;
        var preferred = PlanetFrame.FromPolar(Deg(34f), Deg(-55f));
        var growth = new List<GroveGrowth>();
        var open = new bool[slots.Length];
        for (var s = 0; s < slots.Length; s++) open[s] = free(slots[s], ItemRadius(1f), p.SolidGap, noZones);
        var capCos = MathF.Cos(MathF.Min(MathF.PI, cap / radius));
        for (var g = 0; g < groups.Count; g++)
        {
            var bestSlot = -1;
            var bestScore = float.MinValue;
            for (var s = 0; s < slots.Length; s++)
            {
                if (!open[s] || taken[s]) continue;
                float score;
                if (growth.Count == 0)
                {
                    score = Vector3.Dot(slots[s], preferred);
                }
                else
                {
                    // As far from the other seeds as a grove needs (dot ≤ capCos), then nearest the arrival.
                    var maxDot = -1f;
                    foreach (var o in growth) maxDot = MathF.Max(maxDot, Vector3.Dot(slots[s], o.Seed));
                    score = -MathF.Max(maxDot, capCos) * 1000f + slots[s].Y;
                }

                if (score > bestScore) { bestScore = score; bestSlot = s; }
            }

            if (bestSlot < 0) bestSlot = 0; // no free slot at all (cannot happen at these radii): degrade, never throw
            var gg = new GroveGrowth { Seed = slots[bestSlot] };
            gg.Frontier.Enqueue(bestSlot, 0);
            growth.Add(gg);
        }

        // Next slot for a grove: the nearest free one on its frontier (contiguous), or the nearest free one anywhere.
        int Next(GroveGrowth gg, float r)
        {
            while (true)
            {
                if (gg.Frontier.Count == 0)
                {
                    var nearest = -1;
                    var best = float.MaxValue;
                    for (var s = 0; s < slots.Length; s++)
                    {
                        if (taken[s]) continue;
                        var a = 1f - Vector3.Dot(slots[s], gg.Seed);
                        if (a < best) { best = a; nearest = s; }
                    }

                    if (nearest < 0) return -1;
                    gg.Frontier.Enqueue(nearest, best);
                }

                var slot = gg.Frontier.Dequeue();
                if (taken[slot]) continue;
                taken[slot] = true;
                foreach (var nb in NeighboursOf(slot))
                {
                    if (!taken[nb]) gg.Frontier.Enqueue(nb, 1f - Vector3.Dot(slots[nb], gg.Seed)); // nearest the seed first
                }

                if (!free(slots[slot], r, p.SolidGap, noZones)) continue;
                return slot;
            }
        }

        var shown = new int[groups.Count];
        var pending = new List<(int Grove, int NodeId, PlanetItemKind Kind, float Scale, Vector3 Dir, int Hidden)>();
        var exhausted = new bool[groups.Count];
        var maxRounds = budget.Length == 0 ? 0 : budget.Max();
        for (var round = 0; round < maxRounds; round++)
        {
            for (var g = 0; g < groups.Count; g++)
            {
                if (round >= budget[g] || exhausted[g]) continue;
                var id = groups[g].Ids[round];
                var n = tree[id];
                var scale = ScaleFor(n.Size);
                var slot = Next(growth[g], ItemRadius(scale));
                if (slot < 0) { exhausted[g] = true; continue; }
                solids.Add(new Solid(slots[slot], ItemRadius(scale)));
                pending.Add((g, id, n.Kind == NodeKind.Link ? PlanetItemKind.Link : PlanetItemKind.File, scale, slots[slot], 0));
                shown[g]++;
            }
        }

        // One pile per grove with more files than it shows.
        for (var g = 0; g < groups.Count; g++)
        {
            var rest = groups[g].Ids.Count - shown[g];
            if (rest <= 0) continue;
            var slot = Next(growth[g], ItemRadius(PileScale));
            if (slot >= 0)
            {
                solids.Add(new Solid(slots[slot], ItemRadius(PileScale)));
                pending.Add((g, -1, PlanetItemKind.Pile, PileScale, slots[slot], rest));
                continue;
            }

            // Out of room (degenerate): the grove's last item makes way for the pile, keeping the counts exact.
            var last = pending.FindLastIndex(x => x.Grove == g && x.Kind != PlanetItemKind.Pile);
            if (last < 0) continue;
            var dir = pending[last].Dir;
            pending.RemoveAt(last);
            shown[g]--;
            pending.Add((g, -1, PlanetItemKind.Pile, MathF.Min(PileScale, 1f), dir, rest + 1));
        }

        for (var g = 0; g < groups.Count; g++)
        {
            var sum = Vector3.Zero;
            foreach (var x in pending) if (x.Grove == g) sum += x.Dir;
            var centre = sum.LengthSquared() > 1e-8f ? Vector3.Normalize(sum) : growth[g].Seed;
            groves.Add(new PlanetGrove(groups[g].Category, centre, groups[g].Ids.Count, shown[g], $"{CategoryLabel(groups[g].Category)} · {N(groups[g].Ids.Count)}"));
        }

        foreach (var x in pending)
        {
            var centre = groves[x.Grove].CentreDirection;
            var toward = centre - x.Dir;
            var tangent = toward - x.Dir * Vector3.Dot(toward, x.Dir);
            var heading = PlanetShape.Angle(centre, x.Dir) * radius < 0.5f || tangent.LengthSquared() < 1e-10f ? 0f : PlanetFrame.HeadingOf(x.Dir, Vector3.Normalize(tangent));
            items.Add(new PlanetItem(x.NodeId, x.Kind, groups[x.Grove].Category, x.Dir, heading, x.Scale, x.Grove, x.Hidden));
        }
    }

    public static string CategoryLabel(FileCategory c) => c switch
    {
        FileCategory.Document => "Documents",
        FileCategory.Image => "Images",
        FileCategory.Audio => "Audio",
        FileCategory.Video => "Videos",
        FileCategory.Archive => "Archives",
        FileCategory.Code => "Code",
        FileCategory.Executable => "Programs",
        FileCategory.Data => "Data",
        _ => "Other files",
    };

    // ------------------------------------------------------------------ decorations

    private static readonly DecorationKind[] Kinds = Enum.GetValues<DecorationKind>();

    /// <summary>The relative weights of each decoration kind (in <see cref="DecorationKind"/> order) for a theme.</summary>
    private static int[] Mix(PlanetTheme t) => t switch
    {
        //                         Tree Rock Flower Grass Mushroom Crystal Lamp
        PlanetTheme.Hearth => [3, 1, 3, 3, 1, 0, 2],
        PlanetTheme.Meadow => [2, 1, 6, 4, 1, 0, 0],
        PlanetTheme.Workshop => [1, 2, 1, 2, 0, 1, 4],
        PlanetTheme.Library => [3, 1, 2, 3, 0, 0, 2],
        PlanetTheme.Chime => [1, 1, 5, 3, 2, 0, 1],
        PlanetTheme.Theatre => [1, 1, 2, 2, 2, 0, 4],
        PlanetTheme.Vault => [0, 4, 1, 2, 0, 3, 2],
        PlanetTheme.Crystal => [0, 2, 1, 2, 0, 6, 0],
        PlanetTheme.Wild => [2, 2, 2, 2, 2, 1, 1],
        PlanetTheme.Quiet => [0, 4, 0, 3, 1, 1, 0],
        _ => [0, 4, 0, 1, 0, 2, 0], // Veiled
    };

    private static bool Collides(DecorationKind k) => k is DecorationKind.Tree or DecorationKind.Rock or DecorationKind.Crystal or DecorationKind.Lamp;

    /// <summary>A colliding decoration's solid: footprint radius and top, metres.</summary>
    public static (float Radius, float Top) DecorationSolid(DecorationKind k, float scale) => k switch
    {
        DecorationKind.Tree => (0.35f * scale, 3.2f * scale),
        DecorationKind.Lamp => (0.2f, 2.4f),
        DecorationKind.Crystal => (0.4f * scale, 1.3f * scale),
        DecorationKind.Rock => (0.55f * scale, 0.5f * scale),
        _ => (0.3f * scale, 0f),
    };

    private static List<PlanetDecoration> Decorate(PlanetTheme theme, float radius, int seed, List<PlanetGrove> groves, FreeCheck free, List<Solid> solids, PlanetParams p)
    {
        var target = theme switch
        {
            PlanetTheme.Veiled => (int)MathF.Round(radius * 0.6f),
            PlanetTheme.Quiet => (int)MathF.Round(radius),
            _ => (int)MathF.Round(radius * 1.5f),
        };
        var weights = Mix(theme);
        var total = weights.Sum();
        var rng = new Random(seed ^ 0x2D7E5A1);
        var list = new List<PlanetDecoration>(target);
        var groveZones = groves.Select(g => new Solid(g.CentreDirection, GroveFlatRadius)).ToList();
        var soft = new List<Solid>(); // non-colliding scenery: kept apart a little so it does not pile up
        for (var tries = 0; list.Count < target && tries < target * 40; tries++)
        {
            var d = PlanetShape.RandomDirection(rng);
            var pick = rng.Next(total);
            var kind = Kinds[^1];
            for (var k = 0; k < weights.Length; k++)
            {
                if (pick < weights[k]) { kind = Kinds[k]; break; }
                pick -= weights[k];
            }

            var scale = 0.75f + 0.6f * (float)rng.NextDouble();
            var pieceSeed = rng.Next();
            var collides = Collides(kind);
            var (r, _) = DecorationSolid(kind, scale);
            if (collides)
            {
                if (!free(d, r, p.SolidGap, groveZones)) continue;
                solids.Add(new Solid(d, r));
            }
            else
            {
                // Soft scenery may stand nearer solids (it does not block) but never on a flat spot, an item or a route.
                if (!free(d, r, -r + 0.3f, groveZones)) continue;
                if (soft.Any(s => PlanetShape.Angle(d, s.Direction) * radius < 0.8f)) continue;
                soft.Add(new Solid(d, r));
            }

            list.Add(new PlanetDecoration(kind, d, scale, pieceSeed, collides));
        }

        return list;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Evenly spread unit directions (the Fibonacci sphere), deterministic.</summary>
    public static Vector3[] FibonacciSphere(int count)
    {
        count = Math.Max(1, count);
        var points = new Vector3[count];
        var golden = MathF.PI * (3f - MathF.Sqrt(5f));
        for (var i = 0; i < count; i++)
        {
            var y = 1f - 2f * (i + 0.5f) / count;
            var r = MathF.Sqrt(MathF.Max(0, 1 - y * y));
            var a = golden * i;
            points[i] = new Vector3(r * MathF.Cos(a), y, r * MathF.Sin(a));
        }

        return points;
    }

    /// <summary>For each point, the others within <paramref name="maxAngleRadians"/> (a spatial hash, so it is near linear).</summary>
    public static List<int>[] Neighbours(Vector3[] points, float maxAngleRadians)
    {
        var chord = 2 * MathF.Sin(maxAngleRadians / 2);
        var cell = chord;
        var grid = new Dictionary<(int, int, int), List<int>>();
        (int, int, int) Key(Vector3 v) => ((int)MathF.Floor(v.X / cell), (int)MathF.Floor(v.Y / cell), (int)MathF.Floor(v.Z / cell));
        for (var i = 0; i < points.Length; i++)
        {
            var k = Key(points[i]);
            if (!grid.TryGetValue(k, out var l)) grid[k] = l = [];
            l.Add(i);
        }

        var result = new List<int>[points.Length];
        var chord2 = chord * chord;
        for (var i = 0; i < points.Length; i++)
        {
            var (x, y, z) = Key(points[i]);
            var l = new List<int>(8);
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            for (var dz = -1; dz <= 1; dz++)
            {
                if (!grid.TryGetValue((x + dx, y + dy, z + dz), out var cellList)) continue;
                foreach (var j in cellList)
                {
                    if (j != i && Vector3.DistanceSquared(points[i], points[j]) <= chord2) l.Add(j);
                }
            }

            result[i] = l;
        }

        return result;
    }

    /// <summary>FNV-1a over the case-folded name (string.GetHashCode is randomised per process).</summary>
    private static int NameSeed(string name)
    {
        var h = 2166136261u;
        foreach (var ch in name.ToUpperInvariant())
        {
            h ^= ch;
            h *= 16777619u;
        }

        return (int)(h & 0x7FFFFFFF);
    }

    private static int SeedOf(string name, int id) => NameSeed(name) ^ (id * 7919);

    /// <summary>A deterministic fraction in [0, 1) from a seed and a salt.</summary>
    private static float Frac(int seed, int salt)
    {
        unchecked
        {
            var h = (uint)seed * 2654435761u ^ (uint)salt * 40503u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
