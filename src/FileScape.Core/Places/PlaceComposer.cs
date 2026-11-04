using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Layout;
using FileScape.Core.Model;

namespace FileScape.Core.Places;

/// <summary>Spatial parameters of the futuristic plaza composition (AD-14). Units are roughly metres.</summary>
public sealed record PlaceParams
{
    public float FileCell { get; init; } = 2.0f;
    public float FileFootprint { get; init; } = 1.25f;
    public float FileInnerRadius { get; init; } = 8f;

    /// <summary>File quarters fill south ± this angle (the near half, around the arrival point).</summary>
    public float FileHalfSpanDeg { get; init; } = 65f;

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
    public float TierRise { get; init; } = 0.6f;

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
    public float ArrivalEyeHeight { get; init; } = 4.6f;
    public float ArrivalLookPitch { get; init; } = -5f;

    /// <summary>Eye height when walking (return and approach poses).</summary>
    public float EyeHeight { get; init; } = 2.6f;
    public float ReturnStandOff { get; init; } = 7f;
    public float ApproachStandOff { get; init; } = 9f;
    public SizeScale Scale { get; init; } = SizeScale.Place;

    public static readonly PlaceParams Default = new();
}

/// <summary>
/// Composes one folder as a local place (PLACE-02..05, PLACE-09, AD-14). The result depends only on the folder, its
/// children and grandchildren, and the parent's name and largest files, so it is deterministic and cheap: the whole
/// tree is never laid out. Every immediate child directory or directory link gets exactly one gate.
/// </summary>
public static class PlaceComposer
{
    private static readonly IComparer<string> NameOrder = StringComparer.OrdinalIgnoreCase;

    public static PlaceComposition Compose(FsTree tree, int folderId, PlaceParams? parameters = null)
    {
        var p = parameters ?? PlaceParams.Default;
        var folder = tree[folderId];
        if (!folder.IsDirectory)
        {
            throw new ArgumentException($"#{folderId} is not a directory", nameof(folderId));
        }

        var fileIds = new List<int>();
        var childIds = new List<int>();
        foreach (var c in folder.Children)
        {
            var n = tree[c];
            if (n.IsDirectory || (n.Kind == NodeKind.Link && (n.Flags & NodeFlags.DirectoryLink) != 0)) childIds.Add(c);
            else fileIds.Add(c); // files and file links are structures
        }

        childIds.Sort((a, b) => NameOrder.Compare(tree[a].Name, tree[b].Name) is var k && k != 0 ? k : a.CompareTo(b));

        var identity = PlaceIdentity.For(tree.DisplayName(folderId));
        var landmark = new Landmark(Vector3.Zero, LandmarkHeight(folder), p.LandmarkRadius, identity);

        var (files, quarters, fileRadius) = ComposeFiles(tree, fileIds, p);
        var (gates, tiers) = ComposeGates(tree, childIds, p);

        var vistas = new List<Vista>();
        var avenues = new List<Avenue>();
        if (tiers.Count <= 1)
        {
            // Neighbouring vistas must not overlap: their radius is limited by the arc spacing at vista distance.
            var gap = gates.Count > 1 ? (gates[1].AngleDeg - gates[0].AngleDeg) * MathF.PI / 180f : MathF.PI;
            foreach (var g in gates)
            {
                if (g.State == ListingState.Link)
                {
                    continue;
                }

                var dist = g.Position.Length() + p.VistaDistance;
                var radius = Math.Clamp(0.42f * gap * dist, 4f, 14f);
                var center = g.Outward * dist;
                vistas.Add(ComposeVista(tree, g.TargetId, center, g.AngleDeg, radius, p));
                var start = g.Outward * (p.LandmarkRadius + 1.6f);
                avenues.Add(new Avenue(g.TargetId, [start, g.Position, center - g.Outward * radius]));
            }
        }

        Gate? parentGate = null;
        Vista? parentVista = null;
        var southReach = MathF.Max(fileRadius + 5f, p.MinGateRadius);
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

        return new PlaceComposition
        {
            FolderId = folderId,
            State = Listing.Of(folder),
            Landmark = landmark,
            Arrival = PlaceGeometry.FacingPose(PlaceGeometry.Direction(180) * (southReach - p.ArrivalSetback), 0, p.ArrivalEyeHeight, p.ArrivalLookPitch),
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

        var span = 2 * p.FileHalfSpanDeg;
        // Every quarter needs room for at least one cell on the innermost row; widen the first row if necessary.
        var inner = MathF.Max(p.FileInnerRadius, groups.Count * p.FileCell * 1.6f / (span * MathF.PI / 180f));
        var minAngle = p.FileCell * 1.5f / inner * 180f / MathF.PI;
        var spare = span - minAngle * groups.Count;
        var start = 180 - p.FileHalfSpanDeg;
        var total = fileIds.Count;
        var maxRadius = 0f;
        foreach (var (category, ids) in groups)
        {
            var width = minAngle + spare * ids.Count / total;
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

            // The sign stands at the quarter's outer edge, the side that faces the arrival terrace.
            quarters.Add(new FileQuarter(category, ids.Count, a, b, PlaceGeometry.Direction((a + b) / 2) * (outer + 1.6f)));
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
            PlaceGeometry.RotatedBounds(pos, angleDeg, p.GateWidth, p.GateDepth, p.GateHeight));
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
        dirIds.Sort((a, b) => NameOrder.Compare(tree[a].Name, tree[b].Name));
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
