using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using FileScape.Core.Units;

namespace FileScape.Core.Places;

/// <summary>
/// Spatial parameters of the futuristic plaza composition (AD-14). Places world space: 1 world unit = 1 m, Y up; plain
/// float lengths here are metres, angles named "…Deg" are place angles in degrees clockwise from north (−Z)
/// (docs/units.md). File tower heights come from <see cref="SizeScale"/> — a visual encoding of byte counts, expressed
/// in metres but not a physical size of anything.
/// </summary>
public sealed record PlaceParams
{
    /// <summary>Centre spacing of file towers; with the footprint this leaves ≈ 1.15 m alleys a walker fits through (WALK-05).</summary>
    public float FileCell { get; init; } = 2.4f;
    public float FileFootprint { get; init; } = 1.25f;
    public float FileInnerRadius { get; init; } = 8f;

    /// <summary>File quarters fill south ± this angle (the near half, around the arrival point).</summary>
    public float FileHalfSpanDeg { get; init; } = 60f;

    /// <summary>Half-width of the clear corridor from the landmark south to the parent gate.</summary>
    public float CorridorHalfWidth { get; init; } = 2.4f;

    public float GateWidth { get; init; } = 5f;
    public float GateDepth { get; init; } = 0.9f;
    public float GateHeight { get; init; } = 7f;

    /// <summary>The minimum distance between neighbouring gates on a ring, centre to centre.</summary>
    public float GateSpacing { get; init; } = 8f;

    /// <summary>Child gates occupy north ± this angle.</summary>
    public float GateHalfSpanDeg { get; init; } = 80f;

    /// <summary>The largest angular gap between gates when there are only a few, so they spread but stay in view together.</summary>
    public float MaxGateGapDeg { get; init; } = 40f;

    public float MinGateRadius { get; init; } = 24f;

    /// <summary>Up to this many children sit on one ring with vistas; more are arranged on tiers (PLACE-05).</summary>
    public int MaxSingleRingGates { get; init; } = 24;

    public float FirstTierRadius { get; init; } = 40f;
    public float TierGap { get; init; } = 13f;
    /// <summary>Each outer tier stands on a terrace this much higher, so it can be seen over the inner ones.</summary>
    public float TierRise { get; init; } = 0.42f; // below the step height, so tiers are walked up without jumping

    /// <summary>The symbolic distance from a gate to the vista of the place behind it (AD-14: not true geography).</summary>
    public float VistaDistance { get; init; } = 70f;
    public float ParentVistaDistance { get; init; } = 58f;
    public int VistaMaxTowers { get; init; } = 28;
    public int VistaMaxBeacons { get; init; } = 16;

    public float LandmarkRadius { get; init; } = 0.85f;

    /// <summary>
    /// Arrival is an establishing view: on a raised terrace just inside the entrance (the south edge, where the parent
    /// gate is), looking north down the avenue of file towers to the landmark and the child gates beyond.
    /// </summary>
    public float ArrivalSetback { get; init; } = 6f;

    /// <summary>
    /// The way back (▲ gate) stands on the gate ring, at <see cref="MinGateRadius"/>, with the file quarters continuing
    /// beside and beyond it, instead of at the outer edge of the files. The arrival terrace then sits among the files,
    /// a few metres in front of the way back, rather than beyond them all (PLACE-14).
    /// </summary>
    public bool ParentGateOnRing { get; init; }

    /// <summary>
    /// Metres east of the avenue's centre line for the arrival terrace, so a close landmark does not sit in the middle of
    /// the arrival view hiding the gates behind it (compact layout: the landmark is ≈ 7 m ahead).
    /// </summary>
    public float ArrivalSideOffset { get; init; }

    /// <summary>The arrival terrace's floor height; the eye is <see cref="EyeHeight"/> above it.</summary>
    public float TerraceTop { get; init; } = 1.6f;

    /// <summary>Half-width of the terrace; it must stay inside the clear corridor south of the landmark.</summary>
    public float TerraceHalfWidth { get; init; } = 2.3f;

    public float TerraceHalfDepth { get; init; } = 2.5f;
    /// <summary>Looking slightly down from the terrace, so the avenue, landmark and gates all fit the arrival view.</summary>
    public Degrees ArrivalLookPitch { get; init; } = Degrees.Of(-6f);

    /// <summary>Eye height above the supporting surface for composed poses (WALK-01); the walker's own value, so the two never differ.</summary>
    public Meters EyeHeight { get; init; } = WalkParams.Default.EyeHeight;

    /// <summary>The overlook (JUMP-02): in the free sector between the file quarters and the gates.</summary>
    public float OverlookAngleDeg { get; init; } = 261f;

    public float OverlookRadius { get; init; } = 16f;
    public float OverlookTop { get; init; } = 2.4f;
    public float OverlookHalfSize { get; init; } = 2.5f;

    /// <summary>Length of the overlook's walking ramp (it runs inward from the platform, so it must end clear of the landmark).</summary>
    public float OverlookRampLength { get; init; } = 7.5f;
    public float ReturnStandOff { get; init; } = 7f;
    public float ApproachStandOff { get; init; } = 9f;
    public SizeScale Scale { get; init; } = SizeScale.Place;

    /// <summary>
    /// The layout before 2026-11-06 (KD-28 comparison): gates at least 24 m out, outer tiers from 40 m, vistas 70 m beyond
    /// the gates. Kept for comparison (<c>--layout=spacious</c>); with it DJ found the walks long enough to need 8–16 m/s.
    /// </summary>
    public static readonly PlaceParams Spacious = new();

    /// <summary>
    /// PLACE-14 (AD-34): the intimate, human-scale layout. Gates stand 14 m from the landmark (a 16–20 m walk from the
    /// arrival terrace instead of 30–36 m), 6.5 m apart and a little lower, so the ring reads as a set of doorways around
    /// a square; tiers start at 24 m; the overlook and file quarters move in; vistas sit 46 m beyond their gates, close
    /// enough to read as the next place rather than the horizon. Tower alleys (WALK-05), the corridor, gate clearance and
    /// the step height are unchanged, so every route keeps its clearance.
    /// </summary>
    public static readonly PlaceParams Compact = new()
    {
        FileInnerRadius = 6f,
        GateWidth = 4.4f,
        GateHeight = 6f,
        GateSpacing = 6.5f,
        MinGateRadius = 14f,
        FirstTierRadius = 24f,
        TierGap = 9f,
        VistaDistance = 46f,
        ParentVistaDistance = 40f,
        ArrivalSetback = 7f,
        ParentGateOnRing = true,
        ArrivalSideOffset = 1f,
        TerraceHalfWidth = 1.8f,
        LandmarkRadius = 0.5f,
        CorridorHalfWidth = 3f,
        OverlookRadius = 12f,
        OverlookRampLength = 6f,
        OverlookAngleDeg = 262f,
        GateHalfSpanDeg = 70f,
        FileHalfSpanDeg = 55f,
        ReturnStandOff = 5f,
        ApproachStandOff = 6.5f,
    };

    public static readonly PlaceParams Default = Compact;
}

/// <summary>
/// Composes one folder as a local place (PLACE-02..05, PLACE-09, AD-14). The result depends only on the folder, its
/// children and grandchildren, and the parent's name and largest files, so it is deterministic and cheap: the whole
/// tree is never laid out. Every immediate child directory or directory link gets exactly one gate.
/// </summary>
public static class PlaceComposer
{
    /// <summary>Gates, file rows and beacons in natural name order (ORD-01): "Folder 2" before "Folder 10".</summary>
    private static readonly IComparer<string> NameOrder = NaturalNameOrder.Instance;

    public static PlaceComposition Compose(FsTree tree, int folderId, PlaceParams? parameters = null, Func<FsNode, bool>? visible = null, PlaceKindInfo? kind = null)
    {
        var p = parameters ?? PlaceParams.Default;
        var folder = tree[folderId];
        if (!folder.IsDirectory)
        {
            throw new ArgumentException($"#{folderId} is not a directory", nameof(folderId));
        }

        var fileIds = new List<int>();
        var childIds = new List<int>();
        var hidden = 0;
        foreach (var c in folder.Children)
        {
            var n = tree[c];
            if (visible != null && !visible(n))
            {
                hidden++; // filtered from the place only: totals, selection and history are unaffected (VISI-04)
                continue;
            }

            if (n.IsDirectory || (n.Kind == NodeKind.Link && (n.Flags & NodeFlags.DirectoryLink) != 0)) childIds.Add(c);
            else fileIds.Add(c); // files and file links are structures
        }

        childIds.Sort((a, b) => NameOrder.Compare(tree[a].Name, tree[b].Name) is var k && k != 0 ? k : a.CompareTo(b));

        var identity = PlaceIdentity.For(tree.DisplayName(folderId));
        var landmark = new Landmark(Vector3.Zero, LandmarkHeight(folder), p.LandmarkRadius, identity);

        // A project's README and manifests form its "core" by the avenue, the first thing you walk past (IDENT-04).
        var core = kind?.Kind == PlaceKind.Project ? kind.CoreFiles.Where(fileIds.Contains).ToList() : [];
        var (files, quarters, fileRadius) = ComposeFiles(tree, fileIds.Where(id => !core.Contains(id)).ToList(), p);
        float[] coreAngles = [148, 212, 136, 224, 124, 236];
        for (var i = 0; i < core.Count && i < coreAngles.Length; i++)
        {
            // Beside the avenue, clear of the corridor whatever its width (6.2 m out at the spacious corridor).
            var coreRadius = MathF.Max(6.2f, (p.CorridorHalfWidth + p.FileFootprint / 2 + 0.25f) / MathF.Sin(32f * MathF.PI / 180f));
            var c = PlaceGeometry.Direction(coreAngles[i]) * coreRadius;
            files.Add(new PlacedFile(core[i], Box.FromBase(c, p.FileFootprint, p.Scale.Height(tree[core[i]].Size), p.FileFootprint), FileCategories.Of(tree[core[i]].Name), Core: true));
        }
        var (gates, tiers) = ComposeGates(tree, childIds, p);

        var vistas = new List<Vista>();
        var avenues = new List<Avenue>();
        if (tiers.Count <= 1)
        {
            // Neighbouring vistas must not overlap: their radius is limited by the arc spacing at vista distance.
            var gap = gates.Count > 1 ? (gates[1].AngleDeg - gates[0].AngleDeg) * MathF.PI / 180f : MathF.PI;
            foreach (var g in gates)
            {
                // A resolved link previews its destination (LINK-03); other links have no vista.
                var previewId = g.TargetId;
                if (g.State == ListingState.Link)
                {
                    if (tree[g.TargetId].Link is not { Status: LinkStatus.Resolved, TargetId: >= 0 } li || !tree.Contains(li.TargetId) || !tree[li.TargetId].IsDirectory) continue;
                    previewId = li.TargetId;
                }

                var dist = g.Position.Length() + p.VistaDistance;
                var radius = Math.Clamp(0.42f * gap * dist, 4f, 14f);
                var center = g.Outward * dist;
                vistas.Add(ComposeVista(tree, previewId, center, g.AngleDeg, radius, p));
                var start = g.Outward * (p.LandmarkRadius + 1.6f);
                avenues.Add(new Avenue(g.TargetId, [start, g.Position, center - g.Outward * radius]));
            }
        }

        Gate? parentGate = null;
        Vista? parentVista = null;
        var southReach = p.ParentGateOnRing ? p.MinGateRadius : MathF.Max(fileRadius + 5f, p.MinGateRadius);
        if (!folder.IsRoot)
        {
            var pos = PlaceGeometry.Direction(180) * southReach;
            parentGate = new Gate(folder.ParentId, pos, 180, p.GateWidth, p.GateHeight, -1, ListingState.Open, 0,
                PlaceGeometry.RotatedBounds(pos, 180, p.GateWidth, p.GateDepth, p.GateHeight), IsParent: true);
            var center = PlaceGeometry.Direction(180) * (southReach + p.ParentVistaDistance);
            parentVista = ComposeVista(tree, folder.ParentId, center, 180, 12f, p, excludeChild: folderId);
            avenues.Add(new Avenue(folder.ParentId, [PlaceGeometry.Direction(180) * (p.LandmarkRadius + 1.6f), pos, center - PlaceGeometry.Direction(180) * 12f]));
        }

        var plazaRadius = MathF.Max(southReach + 2f, MathF.Max(fileRadius + 2f, (tiers.Count > 0 ? tiers[0].Radius : p.MinGateRadius) + 4f));
        var bounds = landmark.Bounds;
        foreach (var f in files) bounds = bounds.Union(f.Box);
        foreach (var g in gates) bounds = bounds.Union(g.Bounds);
        if (parentGate != null) bounds = bounds.Union(parentGate.Bounds);
        foreach (var v in vistas.Concat(parentVista is null ? [] : [parentVista]))
        {
            bounds = bounds.Union(new Box(v.Center - new Vector3(v.Radius, 0, v.Radius), v.Center + new Vector3(v.Radius, v.LandmarkHeight, v.Radius)));
        }

        var arrivalGround = PlaceGeometry.Direction(180) * (southReach - p.ArrivalSetback) + new Vector3(p.ArrivalSideOffset, 0, 0);
        var (terrain, overlook) = ComposeTerrain(files, gates, tiers, parentGate, landmark, arrivalGround, p);

        return new PlaceComposition
        {
            FolderId = folderId,
            State = Listing.Of(folder),
            Landmark = landmark,
            Arrival = PlaceGeometry.FacingPose(arrivalGround with { Y = p.TerraceTop }, 0, p.EyeHeight, p.ArrivalLookPitch),
            Terrain = terrain,
            Overlook = overlook,
            HiddenCount = hidden,
            Kind = kind ?? PlaceKindInfo.Ordinary,
            Files = files,
            Quarters = quarters,
            Gates = gates,
            Tiers = tiers,
            ParentGate = parentGate,
            Vistas = vistas,
            ParentVista = parentVista,
            Avenues = avenues,
            PlazaRadius = plazaRadius,
            FileRadius = fileRadius,
            Bounds = bounds,
            TreeVersion = tree.Version,
            Signature = Signature(tree, folderId),
        };
    }

    /// <summary>
    /// A cheap fingerprint of everything a composition depends on: the folder's entries and the state and entry count
    /// of each child. The app recomposes the place only when it changes (PLACE-12).
    /// </summary>
    public static long Signature(FsTree tree, int folderId)
    {
        var f = tree[folderId];
        unchecked
        {
            long h = 1469598103934665603L;
            void Mix(long v) { h ^= v; h *= 1099511628211L; }
            Mix(f.Children.Count);
            Mix((long)f.ScanState);
            Mix((long)f.Flags);
            foreach (var c in f.Children)
            {
                var n = tree[c];
                if (n.Kind == NodeKind.File) continue;
                Mix(c);
                Mix(n.Children.Count);
                Mix((long)n.ScanState);
                Mix((long)n.Flags);
                if (n.Link is { } li)
                {
                    Mix((long)li.Status);
                    Mix(li.TargetId);
                    if (li.TargetId >= 0 && tree.Contains(li.TargetId)) Mix(tree[li.TargetId].Children.Count);
                }
            }

            if (!f.IsRoot)
            {
                Mix(tree[f.ParentId].Children.Count);
            }

            return h;
        }
    }

    public static float LandmarkHeight(FsNode folder) => MathF.Min(7f + 1.5f * MathF.Log10(1 + folder.SubtreeFileCount + folder.SubtreeDirCount), 16f);

    /// <summary>
    /// The number of single-child levels below a folder: a/b/c/d where each has exactly one child folder (and nothing
    /// else) is a chain of 3 below a. Gates announce long chains so deep structures are recognisable (PLACE-05).
    /// </summary>
    public static int ChainLength(FsTree tree, int folderId, int max = 1000)
    {
        var length = 0;
        var cur = tree[folderId];
        while (length < max && cur.Children.Count == 1 && tree[cur.Children[0]].IsDirectory)
        {
            cur = tree[cur.Children[0]];
            length++;
        }

        return length;
    }

    // ------------------------------------------------------------------ files

    /// <summary>
    /// The narrowest a type's wedge may be, in file cells at the innermost row: one cell and a little margin. A wider
    /// minimum (1.5–1.6 before 2026-11-05 evening) pushed the first row out to 22 m for nine small types.
    /// </summary>
    private const float FileWedgeCells = 1.15f;

    private static (List<PlacedFile> Files, List<FileQuarter> Quarters, float Radius) ComposeFiles(FsTree tree, List<int> fileIds, PlaceParams p)
    {
        var files = new List<PlacedFile>(fileIds.Count);
        var quarters = new List<FileQuarter>();
        if (fileIds.Count == 0)
        {
            return (files, quarters, 0);
        }

        var groups = fileIds
            .GroupBy(id => FileCategories.Of(tree[id].Name))
            .OrderBy(g => g.Key)
            .Select(g => (Category: g.Key, Ids: g.OrderBy(id => tree[id].Name, NameOrder).ThenBy(id => id).ToList()))
            .ToList();

        // The avenue from the landmark south to the ▲ gate stays clear. With two or more types, the types are split
        // into a west and an east side of it (in type order, balanced by count), so no type's wedge straddles the
        // avenue: a narrow wedge there lost its inner cells and was pushed outward a row at a time (30 files once
        // reached 44 m, putting the arrival point 43 m from the landmark). A single type keeps the whole span.
        var clear = p.CorridorHalfWidth + p.FileFootprint / 2 + 0.1f;
        var sides = new List<(float From, float To, List<(FileCategory Category, List<int> Ids)> Groups)>();
        float inner;
        if (groups.Count == 1)
        {
            inner = p.FileInnerRadius;
            sides.Add((180 - p.FileHalfSpanDeg, 180 + p.FileHalfSpanDeg, groups));
        }
        else
        {
            var total0 = fileIds.Count;
            var split = 1;
            var best = float.MaxValue;
            for (int k = 1, run = 0; k < groups.Count; k++)
            {
                run += groups[k - 1].Ids.Count;
                var d = MathF.Abs(run - total0 / 2f);
                if (d < best) { best = d; split = k; }
            }

            var west = groups.Take(split).ToList();
            var east = groups.Skip(split).ToList();
            var most = Math.Max(west.Count, east.Count);
            inner = p.FileInnerRadius;
            var gapDeg = 0f;
            for (var iter = 0; iter < 4; iter++)
            {
                gapDeg = MathF.Asin(MathF.Min(1, clear / inner)) * 180f / MathF.PI;
                var sideRad = (p.FileHalfSpanDeg - gapDeg) * MathF.PI / 180f;
                inner = MathF.Max(p.FileInnerRadius, most * p.FileCell * FileWedgeCells / sideRad);
            }

            gapDeg = MathF.Asin(MathF.Min(1, clear / inner)) * 180f / MathF.PI;
            sides.Add((180 - p.FileHalfSpanDeg, 180 - gapDeg, west));
            sides.Add((180 + gapDeg, 180 + p.FileHalfSpanDeg, east));
        }

        var maxRadius = 0f;
        foreach (var (from, to, sideGroups) in sides)
        {
            var span = to - from;
            var minAngle = p.FileCell * FileWedgeCells / inner * 180f / MathF.PI;
                        var spare = MathF.Max(0, span - minAngle * sideGroups.Count);
            var start = from;
            var total = sideGroups.Sum(g => g.Ids.Count);
            foreach (var (category, ids) in sideGroups)
            {
                var width = sideGroups.Count == 1 ? span : minAngle + spare * ids.Count / total;
                var a = start;
                var b = start + width;
                start = b;
                var placed = 0;
                var outer = inner;
                for (var row = 0; placed < ids.Count; row++)
                {
                    var r = inner + row * p.FileCell;
                    var step = p.FileCell / r * 180f / MathF.PI;
                    for (var theta = a + step / 2; theta + step / 2 <= b + 1e-3f && placed < ids.Count; theta += step)
                    {
                        var c = PlaceGeometry.Direction(theta) * r;
                        if (MathF.Abs(c.X) < p.CorridorHalfWidth + p.FileFootprint / 2 && c.Z > 0)
                        {
                            continue; // keep the route from the landmark to the parent gate clear
                        }

                        var id = ids[placed++];
                        var h = p.Scale.Height(tree[id].Size);
                        files.Add(new PlacedFile(id, Box.FromBase(c, p.FileFootprint, h, p.FileFootprint), category));
                        maxRadius = MathF.Max(maxRadius, r + p.FileFootprint);
                        outer = MathF.Max(outer, r);
                    }

                    if (row > 100_000)
                    {
                        throw new InvalidOperationException("file quarter did not converge"); // defensive: geometry bug
                    }
                }

                // The sign stands at the quarter's outer edge, the side that faces the arrival terrace — never in the
                // avenue to the ▲ gate, where it stood in front of the gate for anyone arriving from below.
                var sign = PlaceGeometry.Direction((a + b) / 2) * (outer + 1.6f);
                var avenue = p.CorridorHalfWidth + 1.2f;
                if (sign.Z > 0 && MathF.Abs(sign.X) < avenue) sign = sign with { X = (sign.X < 0 || (sign.X == 0 && (a + b) / 2 > 180) ? -avenue : avenue) };
                quarters.Add(new FileQuarter(category, ids.Count, a, b, sign));
            }
        }

        return (files, quarters, maxRadius);
    }

    // ------------------------------------------------------------------ gates

    private static (List<Gate> Gates, List<GateTier> Tiers) ComposeGates(FsTree tree, List<int> childIds, PlaceParams p)
    {
        var gates = new List<Gate>(childIds.Count);
        var tiers = new List<GateTier>();
        var n = childIds.Count;
        if (n == 0)
        {
            return (gates, tiers);
        }

        var spanRad = 2 * p.GateHalfSpanDeg * MathF.PI / 180f;
        if (n <= p.MaxSingleRingGates)
        {
            // Keep due north free: the landmark stands there as seen from the arrival terrace, and a gate behind it
            // would be hidden. An even count is symmetric with a gap at the centre; an odd count uses n + 1 slots and
            // leaves the slot just east of centre empty.
            var slots = n % 2 == 0 ? n : n + 1;
            var gapDeg = MathF.Min(p.MaxGateGapDeg, 2 * p.GateHalfSpanDeg / (slots - 1));
            var gapRad = gapDeg * MathF.PI / 180f;
            var radius = MathF.Max(p.MinGateRadius, p.GateSpacing / (2 * MathF.Sin(gapRad / 2)));
            var first = -gapDeg * (slots - 1) / 2;
            var skip = n % 2 == 0 ? -1 : slots / 2;
            for (int i = 0, slot = 0; i < n; slot++)
            {
                if (slot == skip) continue;
                gates.Add(MakeGate(tree, childIds[i++], first + slot * gapDeg, radius, 0, 0, p));
            }

            tiers.Add(new GateTier(0, radius, 0, n, tree[childIds[0]].Name, tree[childIds[^1]].Name, first, -first));
            return (gates, tiers);
        }

        var index = 0;
        for (var t = 0; index < n; t++)
        {
            var radius = p.FirstTierRadius + t * p.TierGap;
            var rise = t * p.TierRise;
            // Chord spacing must reach GateSpacing: m gates over the span leave (m-1) gaps.
            var minGapRad = 2 * MathF.Asin(MathF.Min(1, p.GateSpacing / (2 * radius)));
            var capacity = Math.Max(2, (int)MathF.Floor(spanRad / minGapRad * 0.999f) + 1);
            var m = Math.Min(capacity, n - index);
            var gapDeg = m == 1 ? 0 : 2 * p.GateHalfSpanDeg / (m - 1);
            if (m > 1 && 2 * radius * MathF.Sin(gapDeg * MathF.PI / 360f) < p.GateSpacing - 1e-3f)
            {
                throw new InvalidOperationException("tier capacity too high"); // defensive; tests cover spacing
            }

            // The last tier may be partly filled; centre it so it reads as a deliberate group.
            if (m < capacity && m > 1)
            {
                gapDeg = MathF.Min(gapDeg, 2 * p.GateHalfSpanDeg / (capacity - 1) * 1.6f);
            }

            var first = -gapDeg * (m - 1) / 2;
            for (var i = 0; i < m; i++)
            {
                gates.Add(MakeGate(tree, childIds[index + i], first + i * gapDeg, radius, t, rise, p));
            }

            tiers.Add(new GateTier(t, radius, rise, m, tree[childIds[index]].Name, tree[childIds[index + m - 1]].Name, first, -first));
            index += m;
        }

        return (gates, tiers);
    }

    private static Gate MakeGate(FsTree tree, int childId, float angleDeg, float radius, int tier, float rise, PlaceParams p)
    {
        var child = tree[childId];
        var pos = PlaceGeometry.Direction(angleDeg) * radius + new Vector3(0, rise, 0);
        var state = Listing.Of(child);
        var chain = child.IsDirectory ? ChainLength(tree, childId) : 0;
        return new Gate(childId, pos, angleDeg, p.GateWidth, p.GateHeight, tier, state, chain,
            PlaceGeometry.RotatedBounds(pos, angleDeg, p.GateWidth, p.GateDepth, p.GateHeight),
            Followable: child.Kind != NodeKind.Link || (child.Link?.CanFollow ?? false));
    }

    // ------------------------------------------------------------------ terrain (AD-16, WALK-05, JUMP-02)

    /// <summary>
    /// The walkable ground: structures that block (towers, landmark, gate pillars), the arrival terrace with stairs
    /// front and back, the overlook with its walking ramp and jump-block shortcut, tier rings, and the boundary.
    /// </summary>
    private static (PlaceTerrain Terrain, Overlook Overlook) ComposeTerrain(
        List<PlacedFile> files, List<Gate> gates, List<GateTier> tiers, Gate? parentGate, Landmark landmark, Vector3 arrivalGround, PlaceParams p)
    {
        var boxes = new List<TerrainBox>(files.Count + gates.Count * 2 + 32);
        var ramps = new List<TerrainRamp>();
        var rings = new List<TerrainRing>();
        static Vector2 Xz(Vector3 v) => new(v.X, v.Z);

        foreach (var f in files)
        {
            var c = f.Box.Center;
            boxes.Add(new TerrainBox(new Vector2(c.X, c.Z), new Vector2(f.Box.Size.X / 2, f.Box.Size.Z / 2), 0, f.Box.Min.Y, f.Box.Max.Y, TerrainKind.Tower, f.Id));
        }

        boxes.Add(new TerrainBox(Xz(landmark.Base), new Vector2(landmark.Radius, landmark.Radius), 0, 0, landmark.Height, TerrainKind.Landmark));

        // Gate pillars: the opening between them is walkable; the lintel is above any head.
        const float pillar = 0.55f;
        foreach (var g in parentGate is null ? gates : gates.Append(parentGate))
        {
            foreach (var side in new[] { -1f, 1f })
            {
                var c = g.Position + g.Tangent * (side * (g.Width / 2 - pillar / 2));
                boxes.Add(new TerrainBox(Xz(c), new Vector2(pillar / 2, p.GateDepth / 2), g.AngleDeg, g.Position.Y, g.Position.Y + g.Height, TerrainKind.Pillar, g.TargetId));
            }
        }

        // Arrival terrace (north is -Z): stairs down to the plaza in front and down to the ▲ gate behind.
        var t = p.TerraceTop;
        var tc = Xz(arrivalGround);
        boxes.Add(new TerrainBox(tc, new Vector2(p.TerraceHalfWidth, p.TerraceHalfDepth), 0, 0, t, TerrainKind.Platform));
        const int stepsCount = 4;
        const float stepDepth = 0.62f, stairHalf = 1.6f;
        for (var i = 0; i < stepsCount; i++)
        {
            var top = t * (stepsCount - i) / (stepsCount + 1);
            var front = tc.Y - p.TerraceHalfDepth - stepDepth * (i + 0.5f);
            var back = tc.Y + p.TerraceHalfDepth + stepDepth * (i + 0.5f);
            boxes.Add(new TerrainBox(new Vector2(tc.X, front), new Vector2(stairHalf, stepDepth / 2), 0, 0, top, TerrainKind.Step));
            if (parentGate != null)
            {
                boxes.Add(new TerrainBox(new Vector2(tc.X, back), new Vector2(stairHalf, stepDepth / 2), 0, 0, top, TerrainKind.Step));
            }
        }

        // The overlook: a platform with rails on its outer and north sides, a ramp from the plaza (walking route) and two
        // jump blocks along its south side (the optional shortcut).
        var oDir = PlaceGeometry.Direction(p.OverlookAngleDeg);
        var oTan = new Vector3(-oDir.Z, 0, oDir.X);
        var oc = oDir * p.OverlookRadius;
        var h = p.OverlookHalfSize;
        var top0 = p.OverlookTop;
        boxes.Add(new TerrainBox(Xz(oc), new Vector2(h, h), p.OverlookAngleDeg, 0, top0, TerrainKind.Platform));
        boxes.Add(new TerrainBox(Xz(oc + oDir * (h - 0.08f)), new Vector2(h, 0.08f), p.OverlookAngleDeg, top0, top0 + 1.0f, TerrainKind.Rail));
        boxes.Add(new TerrainBox(Xz(oc + oTan * (h - 0.08f)), new Vector2(0.08f, h), p.OverlookAngleDeg, top0, top0 + 1.0f, TerrainKind.Rail));
        var rampStart = oc - oDir * (h + p.OverlookRampLength);
        var rampEnd = oc - oDir * h;
        ramps.Add(new TerrainRamp(Xz(rampStart), Xz(rampEnd), 1.25f, 0, top0));
        var b1 = oc - oDir * (h + 4.2f) - oTan * 2.35f; // low block, further in
        var b2 = oc - oDir * (h + 1.6f) - oTan * 2.35f; // middle block, by the platform's corner
        boxes.Add(new TerrainBox(Xz(b1), new Vector2(0.7f, 0.7f), p.OverlookAngleDeg, 0, top0 / 3, TerrainKind.JumpBlock));
        boxes.Add(new TerrainBox(Xz(b2), new Vector2(0.7f, 0.7f), p.OverlookAngleDeg, 0, top0 * 2 / 3, TerrainKind.JumpBlock));
        var overlook = new Overlook(oc with { Y = top0 }, p.OverlookAngleDeg, h, Xz(rampStart), [Xz(b1), Xz(b2)]);

        foreach (var tier in tiers.Where(x => x.Rise > 0))
        {
            // Rings meet their neighbours (half the tier gap each way), so every tier is a step up from the one inside
            // it; narrower rings left ground-level gaps that a third tier could not be climbed from (found 2026-11-06).
            var half = MathF.Max(3.5f, p.TierGap / 2);
            rings.Add(new TerrainRing(tier.Radius - half, tier.Radius + half, tier.Rise, tier.StartDeg - 4, tier.EndDeg + 4));
        }

        var reach = MathF.Max(p.MinGateRadius, p.OverlookRadius + h + 2);
        foreach (var g in gates) reach = MathF.Max(reach, g.Position.Length());
        if (parentGate != null) reach = MathF.Max(reach, parentGate.Position.Length());
        foreach (var f in files) reach = MathF.Max(reach, new Vector2(f.Box.Center.X, f.Box.Center.Z).Length() + 1);
        return (new PlaceTerrain(boxes, ramps, rings, reach + 2.5f), overlook);
    }

    // ------------------------------------------------------------------ vistas

    private static Vista ComposeVista(FsTree tree, int folderId, Vector3 center, float facingDeg, float radius, PlaceParams p, int excludeChild = -1)
    {
        var folder = tree[folderId];
        var identity = PlaceIdentity.For(tree.DisplayName(folderId));
        var state = Listing.Of(folder);
        var towers = new List<VistaTower>();
        var beacons = new List<VistaBeacon>();
        var fileIds = new List<int>();
        var dirIds = new List<int>();
        foreach (var c in folder.Children)
        {
            if (tree[c].IsDirectory) dirIds.Add(c);
            else if (tree[c].Kind == NodeKind.File) fileIds.Add(c);
        }

        // Skyline: the largest files (ties by name), placed on a sunflower spiral so the cluster is compact and stable.
        var largest = fileIds.OrderByDescending(id => tree[id].Size).ThenBy(id => tree[id].Name, NameOrder).Take(p.VistaMaxTowers).ToList();
        var golden = MathF.PI * (3 - MathF.Sqrt(5));
        var footprint = 1.1f;
        for (var i = 0; i < largest.Count; i++)
        {
            var r = MathF.Min(radius - footprint, 2.2f + 1.45f * MathF.Sqrt(i + 1));
            var a = (i + 1) * golden;
            var offset = new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r);
            var id = largest[i];
            towers.Add(new VistaTower(id, center + offset, footprint, p.Scale.Height(tree[id].Size), FileCategories.Of(tree[id].Name)));
        }

        // Beacons for the folder's own children: an arc beyond the vista, away from the viewer.
        dirIds.Sort((a, b) => NameOrder.Compare(tree[a].Name, tree[b].Name) is var k && k != 0 ? k : a.CompareTo(b));
        var shown = dirIds.Where(d => d != excludeChild).Take(p.VistaMaxBeacons).ToList();
        var outward = PlaceGeometry.Direction(facingDeg);
        for (var i = 0; i < shown.Count; i++)
        {
            var spread = shown.Count == 1 ? 0 : -50f + 100f * i / (shown.Count - 1);
            var pos = center + PlaceGeometry.Direction(facingDeg + spread) * (radius + 9f) + outward * 4f;
            beacons.Add(new VistaBeacon(shown[i], pos, 4f + 1.2f * MathF.Log10(2 + tree[shown[i]].SubtreeFileCount), Listing.Of(tree[shown[i]])));
        }

        var hidden = dirIds.Count(d => d != excludeChild) - shown.Count;
        return new Vista(folderId, center, radius, identity, LandmarkHeight(folder), state, towers, beacons, hidden, folder.DirectFileCount, folder.DirectDirCount);
    }
}
