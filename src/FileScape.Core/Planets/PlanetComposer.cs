using System.Globalization;
using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;

namespace FileScape.Core.Planets;

/// <summary>
/// Parameters of a planet's composition (planet frame, metres; angles named "…Deg" are degrees). Defaults follow the
/// Planets brief as revised by the playtests: launchers ≤ 28 on rings (PLANET-19, AD-44; the first brief had 10), belt
/// moons ≤ 48, individual items ≤ 64 (PLANET-16).
/// <para>
/// Several values are tied to the walker (<see cref="SphereWalkParams"/>, <see cref="Movement.JumpParams"/>): pads and
/// stones must stay below its 0.45 m step height, gaps wider than its 0.7 m body, and the overlook within a jump and
/// boost. <see cref="PlanetComposer.Compose"/> checks the stone rise; the rest is held by the composer tests.
/// </para>
/// </summary>
public sealed record PlanetParams
{
    public static readonly PlanetParams Default = new();

    /// <summary>Child launchers at most; further child folders become belt moons.</summary>
    public int MaxLaunchers { get; init; } = 28;

    /// <summary>The first launcher ring's distance from the arrival point along the surface, metres (2026-11-11: launchers in rings round the arrival).</summary>
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

    /// <summary>A launcher pad's radius along the surface, metres.</summary>
    public float PadRadius { get; init; } = 1.3f;

    /// <summary>A launcher pad's height, metres: well under the walker's step height, so you walk onto it.</summary>
    public float PadTop { get; init; } = 0.2f;

    /// <summary>Radius around a launcher kept free of items and solid scenery, metres (covers the return stand point).</summary>
    public float LauncherClearRadius { get; init; } = 3.3f;

    /// <summary>
    /// How far behind the arrival (along +Z) the way back stood, metres. No longer read by the composer: since AD-44 the
    /// way back is on the first launcher ring at 6 o'clock (<see cref="RingFirst"/> out).
    /// </summary>
    public float ParentBehind { get; init; } = 5.5f;

    /// <summary>The landmark's angle from the arrival pole straight ahead, degrees.</summary>
    public float LandmarkPolarDeg { get; init; } = 40f;

    /// <summary>The landmark's footprint radius, metres.</summary>
    public float LandmarkRadius { get; init; } = 1.6f;

    /// <summary>The landmark's height, metres: tall enough to show over the horizon from the arrival.</summary>
    public float LandmarkTop { get; init; } = 10f;

    /// <summary>The gap between the landmark and the overlook platform, metres.</summary>
    public float LandmarkOverlookGap { get; init; } = 1.5f;

    /// <summary>The overlook platform's height, metres: above a single jump, below a jump and boost (a jump target).</summary>
    public float OverlookTop { get; init; } = 2.4f;

    /// <summary>The overlook platform's radius, metres.</summary>
    public float OverlookRadius { get; init; } = 2.2f;

    /// <summary>Stepping stones up to the overlook (6–8); they rise evenly from the ground to the platform.</summary>
    public int StepCount { get; init; } = 7;

    /// <summary>A stepping stone's radius, metres.</summary>
    public float StepRadius { get; init; } = 0.6f;

    /// <summary>The largest rise between stones (the walker's step height is 0.45 m).</summary>
    public float MaxStepRise { get; init; } = 0.4f;

    /// <summary>A single jump reaches this high, metres (the app's walker; recorded for the overlook's design).</summary>
    public float JumpHeight { get; init; } = 1.2f;

    /// <summary>A jump and boost reaches this high, metres: above <see cref="OverlookTop"/>, so the overlook is a jump target.</summary>
    public float JumpBoostHeight { get; init; } = 2.7f;

    /// <summary>A child's body hangs this far above the ground over its launcher (range), metres.</summary>
    public float SkyHeightMin { get; init; } = 18f;

    /// <summary>The top of that range, metres (each child's height is picked from its name and id, so it never changes).</summary>
    public float SkyHeightMax { get; init; } = 30f;

    /// <summary>The smallest child world in the sky, metres (an empty folder).</summary>
    public float SkyRadiusMin { get; init; } = 3f;

    /// <summary>The largest child world in the sky, metres (reached at 10,000 entries below it; log scale).</summary>
    public float SkyRadiusMax { get; init; } = 8f;

    /// <summary>The belt's radius as a multiple of the planet's (kept outside the launcher bodies).</summary>
    public float BeltRadiusFactor { get; init; } = 2.3f;

    /// <summary>The belt's tilt from the planet's equator, degrees (its turn about the pole comes from the folder's name).</summary>
    public float BeltTiltDeg { get; init; } = 22f;

    /// <summary>What the platform and the user say about places, for ranking launchers (PROM-01).</summary>
    public ProminenceContext Prominence { get; init; } = ProminenceContext.Empty;
}

/// <summary>
/// Composes one folder as a small walkable planet (the Planets experience, PLANET-10; see <see cref="PlanetComposition"/>).
/// The result depends only on the folder, its direct entries, their own direct entries (for themes and sizes) and the
/// parent, so it is deterministic and cheap; it never asks for a listing.
/// <para>
/// <see cref="Compose"/> works outwards from the arrival pole (+Y): rank the child folders into launchers, belt moons and
/// omitted ones; share the item budget between the file groves; size the planet from what it shows; lay the launcher
/// rings round the arrival (AD-44); put the landmark and overlook beyond them; hang each child's world above its pad;
/// then keep routes, pads and the meadow clear while the groves grow and the scenery is scattered (the <c>Free</c>
/// check). The app (<c>PlanetVisual</c>, <c>PlanetWorld</c>) only draws and walks the result.
/// </para>
/// <para>
/// Everything "random" comes from hashes of the folder's name and the children's names and ids (<see cref="NameSeed"/>,
/// <see cref="Frac"/>), so a folder is the same planet on every visit and on every machine. Changing a constant here
/// changes every planet; the composer tests check the invariants (reachability, gaps, budgets), not the exact picture,
/// and the behaviour baseline catches any unintended change.
/// </para>
/// </summary>
public static class PlanetComposer
{
    /// <summary>How far beyond its pad's edge one stands when coming back from a launcher's target, metres.</summary>
    public const float ReturnStandOff = 1.2f;

    /// <summary>A grove's pile has this item scale: larger than a typical file (scale 1), so it stands out in its grove.</summary>
    private const float PileScale = 1.3f;

    /// <summary>The flattened radius round each grove's centre, metres (its sign stands there; scenery keeps off it).</summary>
    private const float GroveFlatRadius = 1.5f;

    private static readonly IComparer<string> NameOrder = NaturalNameOrder.Instance;

    /// <summary>An item's solid footprint radius at a scale, metres (0.45 m at scale 1).</summary>
    public static float ItemRadius(float scale) => 0.45f * scale;

    /// <summary>An item's solid height at a scale, metres (1.1 m at scale 1).</summary>
    public static float ItemTop(float scale) => 1.1f * scale;

    /// <summary>An item's scale from its size: 0.6 at ≤ 100 B rising with log10 to 1.6 at ≥ 1 GB.</summary>
    /// <remarks>(log10(bytes) − 2) / 7 runs 0…1 over the seven decades from 10² to 10⁹ bytes.</remarks>
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

    /// <summary>
    /// The share of a folder's visible files one category needs for its theme; below it the planet is
    /// <see cref="PlanetTheme.Wild"/> (a mix). Raising it makes more planets Wild. (Rationale for 45 % not recorded.)
    /// </summary>
    private const float DominantThemeShare = 0.45f;

    /// <summary>
    /// The theme a folder's planet takes from what is known of its own entries (never asks for a listing). In order: a
    /// link takes its target's theme (or Veiled); the hub is Hearth; empty is Quiet; unknown or unlisted is Veiled; project
    /// evidence makes a Workshop; folders only is Hearth; otherwise the dominant file category, or Wild.
    /// </summary>
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

        return bestCount >= DominantThemeShare * files ? best : PlanetTheme.Wild;
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
            // FNV-1a, 64-bit (offset basis and prime), over whole values rather than bytes: cheap, stable across runs and
            // machines (unlike string.GetHashCode), and a fingerprint, not security.
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

    /// <summary>A child folder or directory link competing for a launcher: what ranks it, and what its launcher will say.</summary>
    /// <param name="Size">Entries below it (through a resolved link, below its target), for ranking and its world's size.</param>
    private sealed record Candidate(int Id, FsNode Node, bool IsLink, bool Followable, int LinkTargetId, ProminenceInfo Prominence, long Size);

    /// <summary>A solid already placed, for gap checks (items, solid scenery, the landmark complex).</summary>
    private readonly record struct Solid(Vector3 Direction, float Radius);

    /// <summary>
    /// The repository workbench's deck height, metres: above the walker's step height (0.45 m), so walking does not climb
    /// it, and within a tapped hop's reach (peak ≈ 0.5 m plus the 0.3 m air-ledge tolerance), so any jump lands on it.
    /// </summary>
    public const float RepositoryBenchTop = 0.7f;

    // ---- planet size: radius = PlanetRadiusBase + PlanetRadiusPerSqrtShown × √(items + LauncherWeight × launchers),
    // clamped to MinPlanetRadius…MaxPlanetRadius. The square root keeps the surface area roughly proportional to what
    // stands on it; the bounds are the "small round worlds (16–34 m)" of docs/galaxy-research.md (a lap of the largest
    // is ≈ 214 m, under 20 s at the run speed).
    /// <summary>An empty folder's planet (Quiet), metres: a small moon.</summary>
    private const float QuietPlanetRadius = 10f;

    /// <summary>An unknown folder's planet with nothing to show (Veiled), metres.</summary>
    private const float VeiledEmptyPlanetRadius = 12f;

    /// <summary>The size formula's base, metres.</summary>
    private const float PlanetRadiusBase = 13f;

    /// <summary>Growth per √(shown things), metres.</summary>
    private const float PlanetRadiusPerSqrtShown = 1.5f;

    /// <summary>A launcher counts as this many items in the size formula: many child folders grow a planet faster than many files (rationale not recorded).</summary>
    private const float LauncherWeight = 4f;

    /// <summary>The smallest planet with something on it, metres.</summary>
    private const float MinPlanetRadius = 16f;

    /// <summary>The largest planet, metres.</summary>
    private const float MaxPlanetRadius = 34f;

    /// <summary>Hill height on Quiet and Veiled planets, metres: nearly smooth.</summary>
    private const float QuietHillAmplitude = 0.25f;

    /// <summary>Hill height elsewhere: this plus up to <see cref="HillAmplitudeSpread"/> (from the name), metres.</summary>
    private const float HillAmplitudeMin = 0.6f;

    /// <summary>See <see cref="HillAmplitudeMin"/>, metres.</summary>
    private const float HillAmplitudeSpread = 0.3f;

    // ---- where the landmark stands
    /// <summary>Clear ground between the outermost ring's pads and the landmark's foot, metres.</summary>
    private const float LandmarkBeyondRings = 4.5f;

    /// <summary>The landmark's bearing is searched in whole degrees this far either side of straight ahead.</summary>
    private const int LandmarkBearingSearchDeg = 60;

    /// <summary>The landmark never stands further than this from the arrival pole, degrees (on a planet crowded with rings it stops short of the antipode).</summary>
    private const float LandmarkMaxPolarDeg = 150f;

    // ---- the child worlds in the sky (AD-44: on their pad's bearing, round the horizon)
    /// <summary>A child world is moved up to ± half this east–west off its pad's bearing (fixed per child, from its name and id), metres.</summary>
    private const float SkyJitterMetres = 4f;

    /// <summary>The chase camera's eye height above the arrival, metres, for the horizon test (rationale for the exact value not recorded).</summary>
    private const float ChaseEyeHeightMetres = 3.4f;

    /// <summary>A child world stays at least this far above that horizon, degrees.</summary>
    private const float HorizonMarginDeg = 3f;

    /// <summary>A child world is at least this much further from the pole than its pad, degrees (this wins over the horizon limit: it never hangs straight above its pad).</summary>
    private const float SkyMinBeyondPadDeg = 4f;

    /// <summary>A child world is at most this much further from the pole than its pad, degrees (AD-44: "32° further out").</summary>
    private const float SkyMaxBeyondPadDeg = 32f;

    /// <summary>A child world is never further than this from the arrival pole, degrees (it stays on the arrival's side of the planet).</summary>
    private const float SkyMaxPolarDeg = 84f;

    /// <summary>The parent's world: this far from the pole, straight behind the arrival, degrees.</summary>
    private const float ParentSkyPolarDeg = 35f;

    /// <summary>The parent's world: this high above the base radius, metres.</summary>
    private const float ParentSkyLift = 24f;

    /// <summary>The parent's world is at least this big, metres, however small the parent is (rationale not recorded).</summary>
    private const float ParentSkyMinRadius = 5f;

    /// <summary>Extra room between the belt and the highest, largest child world, metres.</summary>
    private const float BeltClearance = 4f;

    // ---- signs: label heights above the ground, metres
    private const float LauncherSignHeight = 2.8f;
    private const float ParentSignHeight = 2.4f;
    private const float GroveSignHeight = 2.6f;

    /// <summary>The route to the overlook ends this far outside the first stepping stone, metres.</summary>
    private const float StoneRouteStandOff = 0.8f;

    /// <summary>The route to the workbench ends this far outside its rim, metres (the jump up is yours).</summary>
    private const float BenchRouteStandOff = 0.8f;

    /// <summary>Flattened margin round the overlook complex beyond its stones, metres.</summary>
    private const float OverlookFlatMargin = 0.3f;

    /// <summary>Flattened margin round a launcher pad, metres, so the pad sits level on level ground.</summary>
    private const float PadFlatMargin = 0.7f;

    /// <summary>
    /// Composes the folder's planet (see the class summary for the order of the passes). Throws if
    /// <paramref name="folderId"/> is not a directory, or if the parameters would make a stepping stone rise more than
    /// <see cref="PlanetParams.MaxStepRise"/>. On a repository the overlook is lowered to the workbench
    /// (<see cref="RepositoryBenchTop"/>) and has no stones.
    /// </summary>
    /// <param name="visible">The visibility filter (hidden files and the like): hidden entries are counted, not shown.</param>
    public static PlanetComposition Compose(FsTree tree, int folderId, PlanetParams? parameters = null, Func<FsNode, bool>? visible = null)
    {
        var p = parameters ?? PlanetParams.Default;
        var folder = tree[folderId];
        if (!folder.IsDirectory) throw new ArgumentException($"#{folderId} is not a directory", nameof(folderId));
        // A repository's overlook is its workbench (AD-50): low enough for one plain jump, and no stepping stones (you jump on).
        var bench = WorldCharacter.RepositoryMarker(tree, folder) != null;
        if (bench && p.OverlookTop > RepositoryBenchTop) p = p with { OverlookTop = RepositoryBenchTop };
        if (p.StepCount < 1 || p.OverlookTop / (p.StepCount + 1) > p.MaxStepRise + 1e-4f)
        {
            throw new ArgumentException("The overlook's stepping stones would rise more than MaxStepRise.", nameof(parameters));
        }

        // A Hazard World (PLANET-34, PLANET-46, AD-59) carries fewer pads and files on its scarce ground: the rest of its
        // child folders hang in the belt (click and G, or the list), the rest of its files go to the groves' piles.
        var hazard = HazardRules.Classify(tree, folderId);
        if (hazard != null) p = p with { MaxLaunchers = Math.Min(p.MaxLaunchers, HazardMaxPads), MaxItems = Math.Min(p.MaxItems, HazardMaxItems), ArrivalRadius = MathF.Min(p.ArrivalRadius, HazardArrivalRadius) };

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
        var radius = theme == PlanetTheme.Quiet ? QuietPlanetRadius
            : theme == PlanetTheme.Veiled && nothingShown ? VeiledEmptyPlanetRadius
            : Math.Clamp(PlanetRadiusBase + PlanetRadiusPerSqrtShown * MathF.Sqrt(itemCount + LauncherWeight * launched.Count), MinPlanetRadius, MaxPlanetRadius);
        // A Hazard World (PLANET-34) is never small: its islands, bridges and open sky need room.
        if (hazard != null) radius = MathF.Max(radius, HazardMinPlanetRadius);
        var seed = NameSeed(name);
        // Frac's salts (1 here, 4 for the belt) pick independent "random" fractions from the one name seed.
        var hillAmplitude = theme is PlanetTheme.Quiet or PlanetTheme.Veiled ? QuietHillAmplitude : HillAmplitudeMin + HillAmplitudeSpread * Frac(seed, 1);

        // ---- arrival, landmark, overlook and its steps
        var pole = Vector3.UnitY;
        var hasParent = !folder.IsRoot;
        var rings = hazard != null ? HazardLauncherSlots(launched.Count, hasParent, radius) : LauncherRings(launched.Count, hasParent, radius, p);
        var slotCount = rings.Count(r => !r.Parent);
        if (slotCount < launched.Count)
        {
            // More launchers than a Hazard World's rings hold (AD-59): the lowest ranked join the belt.
            launched = ranked.Take(slotCount).OrderBy(c => c.Node.Name, NameOrder).ThenBy(c => c.Id).ToList();
            belt = ranked.Skip(slotCount).Take(p.MaxBelt).OrderBy(c => c.Node.Name, NameOrder).ThenBy(c => c.Id).ToList();
            omitted = Math.Max(0, ranked.Count - slotCount - p.MaxBelt);
        }

        // The landmark stands ahead beyond the outermost ring (it used to stand at 40°, among the launchers).
        var outer = rings.Count == 0 ? 0f : rings.Max(r => r.DistanceMetres);
        // On a Hazard World the landmark stands straight ahead at the end of the ridge, across the open sky (AD-59).
        var landmarkPolar = hazard != null ? HazardLandmarkMetres / radius : MathF.Max(Deg(p.LandmarkPolarDeg), (outer + p.PadRadius + p.LandmarkRadius + LandmarkBeyondRings) / radius);
        // On the bearing near ahead farthest from every pad's bearing, so the walk out to it passes between pads.
        // "Farthest" is measured as the sideways distance at each pad's ring (bearing difference × ring distance,
        // metres); within 1 cm the bearing nearer straight ahead wins.
        var landmarkAz = 0f;
        if (rings.Count > 0 && hazard == null)
        {
            var best = -1f;
            for (var deg = -LandmarkBearingSearchDeg; deg <= LandmarkBearingSearchDeg; deg++)
            {
                var az = Deg(deg);
                var clear = rings.Min(r => MathF.Abs(MathF.IEEERemainder(az - PlanetFrame.AzimuthOf(r.Direction), 2 * MathF.PI)) * r.DistanceMetres);
                if (clear > best + 0.01f || (MathF.Abs(clear - best) <= 0.01f && MathF.Abs(az) < MathF.Abs(landmarkAz))) { best = clear; landmarkAz = az; }
            }
        }

        var landmarkDir = PlanetFrame.FromPolar(MathF.Min(landmarkPolar, Deg(LandmarkMaxPolarDeg)), landmarkAz);
        var overlookDir = PlanetFrame.Move(landmarkDir, PlanetFrame.East(landmarkDir), p.LandmarkRadius + p.LandmarkOverlookGap + p.OverlookRadius, radius);
        var steps = new List<SurfaceObstacle>();
        {
            var north = PlanetFrame.North(overlookDir);
            var east = PlanetFrame.East(overlookDir);
            var ring = p.OverlookRadius + p.StepRadius;
            var stepAngle = 2 * MathF.Asin(p.StepRadius / ring) * 1.03f; // a hair more than touching: the sphere draws stones together
            var start = Deg(10f); // the first stone faces the arrival, a little away from the landmark (west)
            var rise = p.OverlookTop / (p.StepCount + 1);
            for (var i = 0; i < (bench ? 0 : p.StepCount); i++)
            {
                var a = start + i * stepAngle;
                var d = PlanetFrame.Move(overlookDir, north * MathF.Cos(a) + east * MathF.Sin(a), ring, radius);
                steps.Add(new SurfaceObstacle(d, p.StepRadius, rise * (i + 1)));
            }
        }

        if (hazard != null)
        {
            // On a Hazard World the ridge runs ahead-right, past the opening straight ahead where the black hole's jet climbs
            // out (AD-59): turn the landmark, the overlook and its stones about the pole so the walk's end (by the first
            // stone, or the bench) is at HazardRidgeBearingDeg. A turn about the pole keeps every distance between them.
            var turn = Deg(HazardRidgeBearingDeg) - PlanetFrame.AzimuthOf(steps.Count > 0 ? steps[0].Direction : overlookDir);
            landmarkDir = PlanetFrame.TurnAboutPole(landmarkDir, turn);
            overlookDir = PlanetFrame.TurnAboutPole(overlookDir, turn);
            for (var i = 0; i < steps.Count; i++) steps[i] = steps[i] with { Direction = PlanetFrame.TurnAboutPole(steps[i].Direction, turn) };
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
            var jitter = (Frac(h, 3) - 0.5f) * SkyJitterMetres;
            // On the same bearing as its pad but further out (2026-11-11): the worlds stand round the horizon as a crown
            // you see from the arrival; walking towards one brings you to its pad on the ring.
            var padPolar = PlanetFrame.PolarOf(d);
            // Kept above the arrival's horizon as the chase camera sees it (eye ≈ 3.4 m), whole body and a margin. On a
            // sphere of radius R a point h above it is seen from the eye's height e as long as its angle from the pole is
            // under acos(R/(R+e)) + acos(R/(R+h)) (the two tangent angles to the horizon); the body's own angular radius
            // and the margin come off that.
            var bodyR = SkyRadius(c.Size, p);
            var horizon = MathF.Acos(radius / (radius + ChaseEyeHeightMetres)) + MathF.Acos(radius / (radius + lift)) - MathF.Asin(MathF.Min(1, bodyR / (radius + lift))) - Deg(HorizonMarginDeg);
            var skyPolar = MathF.Max(padPolar + Deg(SkyMinBeyondPadDeg), MathF.Min(MathF.Min(padPolar + Deg(SkyMaxBeyondPadDeg), Deg(SkyMaxPolarDeg)), horizon));
            var skyDir = PlanetFrame.FromPolar(skyPolar, PlanetFrame.AzimuthOf(d));
            var pos = skyDir * (radius + lift) + PlanetFrame.East(skyDir) * jitter;
            sky.Add(new SkyBody(c.Id, pos, SkyRadius(c.Size, p), childTheme, childState, launchers.Count));
            launchers.Add(new PlanetLauncher(c.Id, d, p.PadRadius, p.PadTop, childState, c.Node.Name, sky.Count - 1,
                IsLink: c.IsLink, Followable: c.Followable, LinkTargetId: c.LinkTargetId, Prominence: c.Prominence));
            paths.Add(new PlanetPath(c.Id, [pole, d]));
            signs.Add(new PlanetSign(d, LauncherSignHeight, c.Node.Name, LauncherDetail(tree, c), c.Id));
        }

        if (!folder.IsRoot)
        {
            var parent = tree[folder.ParentId];
            // The way back has a fixed place: on the first ring, straight behind the arrival (6 o'clock; AD-44). Before
            // the rings it stood just behind the arrival point, then (2026-11-11 screenshots: the chase camera stood over
            // its petals, which filled the bottom of the first view) behind and to the right; see ParentBehind. Its world
            // hangs high behind you, on the same bearing.
            var d = rings.First(r => r.Parent).Direction;
            var parentSize = parent.SubtreeFileCount + parent.SubtreeDirCount;
            var pos = PlanetFrame.FromPolar(Deg(ParentSkyPolarDeg), MathF.PI) * (radius + ParentSkyLift);
            sky.Add(new SkyBody(parent.Id, pos, MathF.Max(ParentSkyMinRadius, SkyRadius(parentSize, p)), ThemeOf(tree, parent.Id, visible), Listing.Of(parent), launchers.Count, IsParent: true));
            launchers.Add(new PlanetLauncher(parent.Id, d, p.PadRadius, p.PadTop, Listing.Of(parent), tree.DisplayName(parent.Id), sky.Count - 1, IsParent: true));
            signs.Add(new PlanetSign(d, ParentSignHeight, tree.DisplayName(parent.Id), "the way back", parent.Id));
        }

        // Belt: tiny moons on a tilted ring, outside the launcher bodies.
        if (belt.Count > 0)
        {
            var ringRadius = MathF.Max(p.BeltRadiusFactor * radius, radius + p.SkyHeightMax + p.SkyRadiusMax + BeltClearance);
            var tilt = Matrix4x4.CreateRotationX(Deg(p.BeltTiltDeg)) * Matrix4x4.CreateRotationY(Deg(Frac(seed, 4) * 360f));
            for (var i = 0; i < belt.Count; i++)
            {
                var c = belt[i];
                var a = 2 * MathF.PI * i / belt.Count;
                var local = new Vector3(MathF.Sin(a), 0, -MathF.Cos(a)) * ringRadius;
                var size = 0.5f + 0.7f * Math.Clamp(MathF.Log10(1 + c.Size) / 4f, 0, 1); // 0.5 m empty … 1.2 m at 10,000 entries
                var t = c.IsLink && c.LinkTargetId < 0 ? PlanetTheme.Veiled : ThemeOf(tree, c.LinkTargetId >= 0 ? c.LinkTargetId : c.Id, visible);
                sky.Add(new SkyBody(c.Id, Vector3.Transform(local, tilt), size, t, Listing.Of(c.Node), -1, InBelt: true));
            }
        }

        // The route to the overlook: from the arrival to just outside the first stone.
        if (steps.Count > 0)
        {
            var foot = steps[0].Direction;
            var outward = foot - overlookDir;
            var footOut = PlanetFrame.Move(foot, outward - foot * Vector3.Dot(outward, foot), p.StepRadius + StoneRouteStandOff, radius);
            paths.Add(new PlanetPath(-1, [pole, footOut, foot]));
        }
        else
        {
            // The workbench: the route ends a step from its rim, on the arrival's side (the jump is yours).
            var towardPole = pole - overlookDir * Vector3.Dot(pole, overlookDir);
            var foot = PlanetFrame.Move(overlookDir, towardPole, p.OverlookRadius + BenchRouteStandOff, radius);
            paths.Add(new PlanetPath(-1, [pole, foot]));
        }

        // ---- exclusion zones (no items or solid scenery)
        var keepOut = new List<Solid>
        {
            new(pole, p.ArrivalRadius),
            new(landmarkDir, p.LandmarkRadius + p.SolidGap),
            new(overlookDir, complexRadius + p.SolidGap),
        };
        foreach (var l in launchers) keepOut.Add(new Solid(l.Direction, p.LauncherClearRadius));

        // ---- a Hazard World's broken shell (PLANET-46, AD-59): islands round the destinations, bridges between them; the
        // drawn routes become the walks along the bridges, and files and scenery stand only on solid ground
        BrokenShell? shell = null;
        IReadOnlyList<Vector3> plateaus = [];
        if (hazard != null)
        {
            var overlookRoute = paths[^1].Points; // pole, the stand-off before the first stone, the stone; or pole, the step before the bench
            var routeFoot = overlookRoute.Count > 2 ? overlookRoute[^2] : overlookRoute[^1];
            (shell, plateaus) = DesignShell(radius, seed, launchers, landmarkDir, overlookDir, complexRadius, routeFoot, groups, budget, p);
            paths.Clear();
            foreach (var l in launchers)
            {
                var route = shell.Route(pole, l.Direction).Select(s => s.Point);
                paths.Add(new PlanetPath(l.IsParent ? -1 : l.TargetId, [pole, .. route]));
            }

            foreach (var s in shell.Spans) paths.Add(new PlanetPath(-1, [s.A, s.B]));
            foreach (var w in shell.Waypoints) keepOut.Add(new Solid(w, HazardWaypointClearMetres));
        }

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
        // On a Hazard World the rings are not walking routes (the bridges are), so nothing is kept off them.
        var ringDistances = hazard != null ? [] : rings.Select(r => r.DistanceMetres).Distinct().ToArray();
        // Distances are compared through dot products first (one multiply-add per solid), and measured exactly only
        // for the few candidates that are close: composition runs whenever a listing changes, so it must stay cheap.
        bool Free(Vector3 d, float r, float gap, List<Solid> extraZones)
        {
            // Broad phase: a cone a little wider (3 m) than the largest zone plus this radius; only zones inside it are measured.
            var nearCos = MathF.Cos(MathF.Min(MathF.PI, (maxZone + r + 3f) / radius));
            foreach (var z in keepOut)
            {
                if (Vector3.Dot(d, z.Direction) >= nearCos && PlanetShape.Angle(d, z.Direction) * radius < z.Radius + r) return false;
            }

            foreach (var z in extraZones)
            {
                if (PlanetShape.Angle(d, z.Direction) * radius < z.Radius + r) return false;
            }

            // The launcher rings are walking routes: nothing stands on them (2026-11-11 review).
            var fromArrival = PlanetFrame.PolarOf(d) * radius;
            foreach (var ringDistance in ringDistances)
            {
                if (MathF.Abs(fromArrival - ringDistance) < p.PathHalfWidth + r) return false;
            }

            // A point farther than the clearance from a route's whole great circle (|d·n| ≥ sin of the clearance angle)
            // cannot be near its arc, so the exact arc distance is measured only for the rest.
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

            // On a Hazard World everything stands on solid ground, back from the edges.
            if (shell != null && shell.EdgeMetres(d) < r + HazardSceneryEdgeMetres) return false;

            return true;
        }

        // ---- groves
        var items = new List<PlanetItem>();
        var groves = new List<PlanetGrove>();
        if (groups.Count > 0)
        {
            PlaceGroves(tree, groups, budget, radius, p, Free, solids, items, groves, plateaus);
        }

        foreach (var g in groves)
        {
            signs.Add(new PlanetSign(g.CentreDirection, GroveSignHeight, g.Label, g.Hidden > 0 ? $"{N(g.Shown)} shown · {N(g.Hidden)} in the pile" : "", -1));
        }

        // ---- flat spots and shape
        var flats = new List<(Vector3 Direction, float Radius)> { (pole, p.ArrivalRadius), (overlookDir, complexRadius + OverlookFlatMargin) };
        foreach (var l in launchers) flats.Add((l.Direction, p.PadRadius + PadFlatMargin));
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
            Surface = new PlanetSurface(shape, obstacles, shell),
            Hazard = hazard,
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

    /// <summary>A Hazard World's smallest radius, metres: room for islands, bridges and open sky round the arrival (18 m: by trial on the sample, 2026-11-15; kept for AD-59).</summary>
    private const float HazardMinPlanetRadius = 18f;

    /// <summary>A Hazard World holds at most this many launcher pads (AD-59): more than its scarce ground carries comfortably; the rest join the belt.</summary>
    private const int HazardMaxPads = 10;

    /// <summary>A Hazard World shows at most this many files one by one (the rest in piles): its file plateaus stay compact.</summary>
    private const int HazardMaxItems = 24;

    /// <summary>A Hazard World's landmark stands this far ahead of the arrival, metres: past the arrival's island, a ridge of ≈ 8 m and its own island.</summary>
    private const float HazardLandmarkMetres = 23f;

    /// <summary>The ridge to a Hazard World's landmark runs this many degrees right of straight ahead (clear of the jet's opening straight ahead).</summary>
    private const float HazardRidgeBearingDeg = 32f;

    /// <summary>A Hazard World's arrival meadow is this wide (radius, metres; 4 elsewhere): the drop ahead starts a few steps away, the landing still well inside solid ground.</summary>
    private const float HazardArrivalRadius = 3f;

    /// <summary>On a Hazard World, files and scenery keep this far from an edge, metres (plus their own radius).</summary>
    private const float HazardSceneryEdgeMetres = 1.0f;

    /// <summary>Round every waypoint of a Hazard World's route this much stays clear of files and scenery, metres: where bridges meet you can walk through.</summary>
    private const float HazardWaypointClearMetres = 1.5f;

    /// <summary>How far a launcher's land must carry round its pad, metres: the pad, the stand point coming back from the child (<see cref="ReturnStandOff"/>) and a body (0.35 m).</summary>
    private static float LauncherContentMetres(PlanetParams p) => p.PadRadius + ReturnStandOff + 0.35f;

    /// <summary>A file plateau's area per file shown, in grove cells (GroveSpacing²): 1.6 (the slots are ≈ GroveSpacing apart, and the bridges' clear bands take a share).</summary>
    private const float HazardPlateauCellsPerItem = 1.6f;

    /// <summary>At most this many file plateaus; further groves grow on whatever ground is left.</summary>
    private const int HazardMaxPlateaus = 4;

    /// <summary>
    /// The broken shell for a Hazard World (<see cref="HazardTerrain.Design"/>): one site per destination — the arrival
    /// meadow, every launcher with its return stand point, the landmark with the overlook (its waypoint is the route's
    /// foot before the overlook) — and one plateau per grove, sized for the files it shows.
    /// </summary>
    private static (BrokenShell Shell, IReadOnlyList<Vector3> Plateaus) DesignShell(float radius, int seed, List<PlanetLauncher> launchers,
        Vector3 landmarkDir, Vector3 overlookDir, float complexRadius, Vector3 routeFoot, List<(FileCategory Category, List<int> Ids)> groups,
        int[] budget, PlanetParams p)
    {
        var pole = Vector3.UnitY;
        var sites = new List<HazardSite> { new(pole, pole, p.ArrivalRadius, HazardSiteKind.Arrival) };
        foreach (var l in launchers) sites.Add(new HazardSite(l.Direction, l.Direction, LauncherContentMetres(p), l.IsParent ? HazardSiteKind.WayBack : HazardSiteKind.Launcher));
        var mid = Vector3.Normalize(landmarkDir + overlookDir);
        var complex = MathF.Max(
            MathF.Max(PlanetShape.Angle(mid, landmarkDir) * radius + p.LandmarkRadius, PlanetShape.Angle(mid, overlookDir) * radius + complexRadius),
            PlanetShape.Angle(mid, routeFoot) * radius + HazardWaypointClearMetres);
        sites.Add(new HazardSite(routeFoot, mid, complex, HazardSiteKind.Landmark));
        var plateauContent = new List<float>();
        for (var g = 0; g < groups.Count && g < HazardMaxPlateaus; g++)
        {
            var shown = budget[g] + (budget[g] < groups[g].Ids.Count ? 1 : 0);
            if (shown == 0) continue;
            var area = shown * HazardPlateauCellsPerItem * p.GroveSpacing * p.GroveSpacing;
            plateauContent.Add(MathF.Sqrt(area / MathF.PI) + HazardWaypointClearMetres);
        }

        return HazardTerrain.Design(radius, seed, sites, plateauContent);
    }

    /// <summary>A Hazard World's first launcher ring stands this far from the arrival, metres, and further rings this much further out (AD-59: room for an island each and a bridge between).</summary>
    private const float HazardRingFirstMetres = 15f, HazardRingGapMetres = 11f;

    /// <summary>Neighbouring pads on a Hazard World's ring stand at least this far apart, metres (two islands and a short bridge).</summary>
    private const float HazardPadSpacingMetres = 12f;

    /// <summary>A Hazard World has at most this many launcher rings; launchers that do not fit join the belt.</summary>
    private const int HazardMaxRings = 3;

    /// <summary>
    /// Launcher slots on a Hazard World (AD-59): rings like <see cref="LauncherRings"/>, read like a clock from 6 o'clock
    /// clockwise, but wider spaced, and never in the opening straight ahead (±<see cref="HazardTerrain.FrontOpeningHalfDeg"/>),
    /// where the ground falls away to show the black hole's jet and the ridge to the landmark. The way back is at 6
    /// o'clock on the first ring. Returns fewer child slots than <paramref name="n"/> when the rings are full.
    /// </summary>
    private static List<RingSlot> HazardLauncherSlots(int n, bool hasParent, float radius)
    {
        var slots = new List<RingSlot>();
        var h = Deg(HazardTerrain.FrontOpeningHalfDeg);
        var usable = 2 * MathF.PI - 2 * h; // the arc from 6 o'clock clockwise round to the opening's left side, then on from its right side back to 6
        var remaining = n;
        for (var ring = 0; ring < HazardMaxRings && (remaining > 0 || (ring == 0 && hasParent)); ring++)
        {
            var distance = HazardRingFirstMetres + ring * HazardRingGapMetres;
            var polar = MathF.Min(distance / radius, Deg(MaxRingPolarDeg));
            var capacity = Math.Max(1, (int)(usable * radius * MathF.Sin(polar) / HazardPadSpacingMetres));
            var reserveBack = ring == 0 && hasParent;
            var here = Math.Max(0, Math.Min(remaining, capacity - (reserveBack ? 1 : 0)));
            // Fraction f of the usable arc is azimuth −π + f·usable up to the opening's left side, and h + … past its right
            // side. With the way back at 6 o'clock it takes the arc's two ends, the children evenly between.
            float Az(float f)
            {
                var x = f * usable;
                return x < MathF.PI - h ? -MathF.PI + x : h + (x - (MathF.PI - h));
            }

            for (var i = 0; i < here; i++)
            {
                var f = reserveBack ? (i + 1f) / (here + 1) : (i + 0.5f) / here;
                slots.Add(new RingSlot(PlanetFrame.FromPolar(polar, Az(f)), distance, false));
            }

            if (reserveBack) slots.Add(new RingSlot(PlanetFrame.FromPolar(polar, MathF.PI), distance, true));
            remaining -= here;
        }

        return slots;
    }

    /// <summary>A child world's radius from the entries below it: log scale from SkyRadiusMin (empty) to SkyRadiusMax (≈ 10,000).</summary>
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

    /// <summary>The farthest a launcher ring stands from the arrival pole, degrees (rings beyond it share this angle; rationale for 120° not recorded).</summary>
    private const float MaxRingPolarDeg = 120f;

    /// <summary>The fewest pads a ring holds, even when its circumference is short (rationale not recorded).</summary>
    private const int MinPadsPerRing = 4;

    /// <summary>One slot on a launcher ring: its direction, its ring's surface distance from the arrival, and whether it is the way back.</summary>
    private readonly record struct RingSlot(Vector3 Direction, float DistanceMetres, bool Parent);

    /// <summary>
    /// Launchers in rings round the arrival meadow, read like a clock (2026-11-11, DJ's playtest: on the Ubuntu root
    /// "home" took a while to find): child folders in name order, clockwise from straight ahead (12 o'clock) on the
    /// first ring, then the next ring out; the way back always at 6 o'clock on the first ring. Every pad is within a
    /// few rings of where you land, so a named launcher is found from the arrival without circling the planet.
    /// </summary>
    /// <remarks>
    /// A ring's capacity is its circumference over <see cref="PlanetParams.RingSpacing"/> (at least
    /// <see cref="MinPadsPerRing"/>). Ring distances are surface distances from the arrival; on a small planet the ring
    /// angle stops at <see cref="MaxRingPolarDeg"/> and any further rings share it.
    /// </remarks>
    private static List<RingSlot> LauncherRings(int n, bool hasParent, float radius, PlanetParams p)
    {
        var slots = new List<RingSlot>();
        var remaining = n;
        var distance = p.RingFirst;
        var first = true;
        var ring = 0;
        while (remaining > 0 || (first && hasParent))
        {
            var polar = MathF.Min(distance / radius, Deg(MaxRingPolarDeg));
            var circumference = 2 * MathF.PI * radius * MathF.Sin(polar);
            var capacity = Math.Max(MinPadsPerRing, (int)(circumference / p.RingSpacing));
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
                // Rings after the first are turned so no outer pad shares an inner pad's bearing (their worlds would
                // stack on one line of sight, and the straight walk out would cross inner pads).
                var phase = 0f;
                if (!first)
                {
                    // The phase (within one pitch, sampled at 64 steps) that keeps this ring's pads farthest from every
                    // bearing already used; the earliest sample wins a tie, so the result is deterministic.
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

    /// <summary>
    /// Whether a solid of radius <c>r</c> may stand at <c>d</c>: clear of the keep-out zones (meadow, landmark, overlook,
    /// launchers), of <c>extraZones</c>, of the launcher rings and routes, and at least <c>gap</c> from every solid placed
    /// so far (metres). Compose's local <c>Free</c> is the one implementation.
    /// </summary>
    private delegate bool FreeCheck(Vector3 d, float r, float gap, List<Solid> extraZones);

    /// <summary>One grove's growth: its seed slot and its frontier (slots next to the grove, nearest the seed first).</summary>
    private sealed class GroveGrowth
    {
        public required Vector3 Seed;
        public readonly PriorityQueue<int, float> Frontier = new();
    }

    /// <summary>The largest grove's seed is the open slot nearest this point: 34° from the arrival pole …</summary>
    private const float FirstGrovePolarDeg = 34f;

    /// <summary>… and 55° to the left of straight ahead (front-left; rationale for the exact angles not recorded).</summary>
    private const float FirstGroveAzimuthDeg = -55f;

    /// <summary>
    /// Grows one contiguous patch per category over a Fibonacci-sphere slot set (largest grove first), round-robin so
    /// no grove crowds out another, and fills each with its files in name order from its centre outwards, then its pile.
    /// </summary>
    private static void PlaceGroves(FsTree tree, List<(FileCategory Category, List<int> Ids)> groups, int[] budget, float radius,
        PlanetParams p, FreeCheck free, List<Solid> solids, List<PlanetItem> items, List<PlanetGrove> groves, IReadOnlyList<Vector3>? seedHints = null)
    {
        // As many slots as hexagonal cells of the grove spacing cover the sphere (a hexagonal cell of pitch s has area
        // (√3/2)·s² ≈ 0.866·s²), so neighbouring slots are about GroveSpacing apart.
        var slots = FibonacciSphere((int)MathF.Round(4 * MathF.PI * radius * radius / (0.866f * p.GroveSpacing * p.GroveSpacing)));
        // Neighbours on demand: Fibonacci points are ordered by height, so a slot's neighbours lie within a window of
        // indices (|Δy| ≤ the chord); only the slots a grove grows through are ever asked for. 2·sin(0.8·s/R) is the
        // chord of a 1.6·s arc: slots up to ≈ 1.6 grove spacings apart count as neighbours (rationale for 1.6 not
        // recorded; it is wider than the ≈ 1 spacing between neighbouring slots).
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
        // (and no farther: then nearest the arrival). "As far as a grove needs", cap, is the diameter (metres) of a disc
        // holding the largest grove's items (at most 80 counted) at 1.4 slot areas each, plus 4 m.
        var cap = 2 * MathF.Sqrt(groups.Max(g => g.Ids.Count > 0 ? Math.Min(g.Ids.Count, 80) : 1) * 1.4f * p.GroveSpacing * p.GroveSpacing / MathF.PI) + 4f;
        var preferred = PlanetFrame.FromPolar(Deg(FirstGrovePolarDeg), Deg(FirstGroveAzimuthDeg));
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
                if (seedHints != null && g < seedHints.Count)
                {
                    score = Vector3.Dot(slots[s], seedHints[g]); // a Hazard World's plateau for this grove (AD-59)
                }
                else if (growth.Count == 0)
                {
                    score = Vector3.Dot(slots[s], preferred);
                }
                else
                {
                    // As far from the other seeds as a grove needs (dot ≤ capCos), then nearest the arrival.
                    var maxDot = -1f;
                    foreach (var o in growth) maxDot = MathF.Max(maxDot, Vector3.Dot(slots[s], o.Seed));
                    // × 1000: separation decides; height (Y, nearness to the arrival pole) only breaks ties among slots
                    // already far enough (all of which score -capCos × 1000).
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

    // ---- scenery density and sizes
    /// <summary>Scenery pieces per metre of planet radius on a Veiled planet (sparse: its surface is unknown).</summary>
    private const float VeiledDecorationsPerMetre = 0.6f;

    /// <summary>Scenery pieces per metre of planet radius on a planet with a theme (a Quiet one has 1 per metre).</summary>
    private const float DecorationsPerMetre = 1.5f;

    /// <summary>Random positions tried per wanted piece before giving up (a crowded planet gets fewer pieces, never a hang).</summary>
    private const int DecorationTriesPerPiece = 40;

    /// <summary>A piece's scale: this plus up to <see cref="DecorationScaleSpread"/> (uniform).</summary>
    private const float DecorationScaleMin = 0.75f;

    /// <summary>See <see cref="DecorationScaleMin"/>: scales run 0.75…1.35.</summary>
    private const float DecorationScaleSpread = 0.6f;

    /// <summary>Soft scenery's centre stays this far outside any solid's footprint, metres.</summary>
    private const float SoftSceneryClearance = 0.3f;

    /// <summary>Soft scenery pieces stand at least this far apart, centre to centre, metres.</summary>
    private const float SoftScenerySpacing = 0.8f;

    /// <summary>
    /// Scatters the theme's scenery (<see cref="Mix"/>) at random but repeatable places (a <see cref="Random"/> seeded
    /// from the name): solid pieces keep <see cref="PlanetParams.SolidGap"/> from everything, soft ones only stay off flat
    /// spots, items and routes. Adds the solid pieces to <paramref name="solids"/>.
    /// </summary>
    private static List<PlanetDecoration> Decorate(PlanetTheme theme, float radius, int seed, List<PlanetGrove> groves, FreeCheck free, List<Solid> solids, PlanetParams p)
    {
        var target = theme switch
        {
            PlanetTheme.Veiled => (int)MathF.Round(radius * VeiledDecorationsPerMetre),
            PlanetTheme.Quiet => (int)MathF.Round(radius),
            _ => (int)MathF.Round(radius * DecorationsPerMetre),
        };
        var weights = Mix(theme);
        var total = weights.Sum();
        // The name seed XOR a fixed salt, so the scenery's stream is unrelated to the hills' (PlanetShape seeds its own
        // Random with the plain seed); the salt's value is arbitrary.
        var rng = new Random(seed ^ 0x2D7E5A1);
        var list = new List<PlanetDecoration>(target);
        var groveZones = groves.Select(g => new Solid(g.CentreDirection, GroveFlatRadius)).ToList();
        var soft = new List<Solid>(); // non-colliding scenery: kept apart a little so it does not pile up
        for (var tries = 0; list.Count < target && tries < target * DecorationTriesPerPiece; tries++)
        {
            var d = PlanetShape.RandomDirection(rng);
            var pick = rng.Next(total);
            var kind = Kinds[^1];
            for (var k = 0; k < weights.Length; k++)
            {
                if (pick < weights[k]) { kind = Kinds[k]; break; }
                pick -= weights[k];
            }

            var scale = DecorationScaleMin + DecorationScaleSpread * (float)rng.NextDouble();
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
                // A gap of −r + clearance makes Free's solid test "centre at least the clearance outside the solid".
                if (!free(d, r, -r + SoftSceneryClearance, groveZones)) continue;
                if (soft.Any(s => PlanetShape.Angle(d, s.Direction) * radius < SoftScenerySpacing)) continue;
                soft.Add(new Solid(d, r));
            }

            list.Add(new PlanetDecoration(kind, d, scale, pieceSeed, collides));
        }

        return list;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Evenly spread unit directions (the Fibonacci sphere), deterministic: point i is at height 1 − 2(i + ½)/count (equal
    /// areas per point, ordered from +Y down, which <c>PlaceGroves</c> relies on) and turned by i golden angles.
    /// </summary>
    public static Vector3[] FibonacciSphere(int count)
    {
        count = Math.Max(1, count);
        var points = new Vector3[count];
        var golden = MathF.PI * (3f - MathF.Sqrt(5f)); // the golden angle, ≈ 137.5° in radians: successive points never line up
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

    /// <summary>A child's seed: its name's, mixed with its id times a prime (7919, the 1000th) so same-named siblings differ.</summary>
    private static int SeedOf(string name, int id) => NameSeed(name) ^ (id * 7919);

    /// <summary>A deterministic fraction in [0, 1) from a seed and a salt.</summary>
    /// <remarks>
    /// The seed and salt are spread by odd multipliers (Knuth's 2654435761 and 40503), then mixed by an xxHash32-style
    /// avalanche (shift 15, × 2246822519, shift 13) so every input bit reaches the low bits. The low 24 bits over 2²⁴
    /// give a fraction a float holds exactly. Changing any constant moves every child world and belt, and changes hill heights.
    /// </remarks>
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
