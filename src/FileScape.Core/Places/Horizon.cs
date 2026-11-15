using System.Numerics;
using FileScape.Core.Model;

namespace FileScape.Core.Places;

/// <summary>What a horizon mark stands for (HORIZON-01).</summary>
public enum HorizonKind
{
    /// <summary>A folder one level beyond a vista's beacons: a recognisable dark silhouette, sized by what it holds.</summary>
    Silhouette,

    /// <summary>More structure further out in that direction (its subtree beyond what is drawn): restrained haze.</summary>
    Haze,

    /// <summary>A folder whose contents are not known yet: a pale veil, never drawn as if empty (PLACE-11).</summary>
    Unknown,
}

/// <param name="Position">Base of the mark on the ground (place space, metres).</param>
/// <param name="Height">Metres.</param>
/// <param name="Radius">Metres.</param>
/// <param name="Weight">0–1: how much it stands for (log of the folders and files beyond), for brightness.</param>
/// <param name="FolderId">The folder it stands for (the deepest known one).</param>
public readonly record struct HorizonMark(HorizonKind Kind, Vector3 Position, float Height, float Radius, float Weight, int FolderId);

/// <summary>
/// HORIZON-01..03 (AD-35): the extent of the tree beyond the immediate destinations, without drawing everything at equal
/// prominence. Immediate destinations are the gates; their vistas show the next places with beacons for those places'
/// own folders; the horizon adds one more level as silhouettes behind each beacon, and the size of everything deeper as
/// haze further out along the same bearing. Only what is known is drawn; unknown places are veiled, not empty.
/// Deterministic and bounded (<see cref="MaxMarks"/>), from the composition and the tree only.
/// </summary>
public static class Horizon
{
    /// <summary>The most marks per place (AD-35): a stated bound, however wide the folders beyond are.</summary>
    public const int MaxMarks = 220;

    /// <summary>Silhouettes per vista beacon (a folder's own folders, largest first).</summary>
    public const int SilhouettesPerBeacon = 3;

    // ---- where the marks stand: rings beyond each vista, along the bearing from the place's centre (metres past the vista's edge)
    /// <summary>The silhouettes' ring, metres beyond the vista's edge: behind its beacons (which stand ≈ 9 m out).</summary>
    private const float SilhouetteSetback = 14f;

    /// <summary>The haze band, metres beyond the vista's edge: further out than the silhouettes, so it reads as distance.</summary>
    private const float HazeSetback = 36f;

    /// <summary>The most sideways space per beacon's group, metres; narrower vistas share 2.2 × their radius among the beacons.</summary>
    private const float MaxBeaconGroupSpacing = 3.2f;

    private const float BeaconGroupSpreadPerRadius = 2.2f;

    // ---- an unknown folder's veil (PLACE-11: pale, never drawn as empty), metres; weight 0.5 = mid brightness
    private const float UnknownVeilHeight = 9f;
    private const float UnknownVeilRadius = 1.6f;
    private const float UnknownVeilWeight = 0.5f;

    /// <summary>A silhouette whose folder is not known yet: shorter than the beacon's own veil.</summary>
    private const float UnknownSilhouetteHeight = 7f;

    // ---- a silhouette's size from its subtree (folders and files): base + per tenfold more entries, metres
    private const float SilhouetteBaseHeight = 5f;
    private const float SilhouetteHeightPerDecade = 4.5f;
    private const float SilhouetteBaseRadius = 0.9f;
    private const float SilhouetteRadiusPerDecade = 0.25f;

    /// <summary>A beacon's silhouettes stand this far apart sideways and step this far back each, metres, so they stand apart.</summary>
    private const float SilhouetteSideStep = 1.6f;

    private const float SilhouetteBackStep = 2.5f;

    /// <summary>
    /// Brightness weight = log10(1 + count) / this, clamped: 4 decades (≈ 10,000 entries) reach full weight. The same
    /// value for silhouettes (entries) and haze (folders), separately named because they count different things.
    /// </summary>
    private const float SilhouetteFullWeightDecades = 4f;

    private const float HazeFullWeightDecades = 4f;

    /// <summary>The dimmest a silhouette or the haze is drawn (weight), so a small one is still visible.</summary>
    private const float MinSilhouetteWeight = 0.1f;

    private const float MinHazeWeight = 0.08f;

    // ---- the haze band: height base + span × weight, radius at least the minimum or (factor + weight) × the vista's radius, metres
    private const float HazeBaseHeight = 10f;
    private const float HazeHeightSpan = 22f;
    private const float MinHazeRadius = 6f;
    private const float HazeRadiusFactor = 0.8f;

    /// <summary>
    /// The horizon marks of a place: for each vista (only a single-ring place has vistas), up to
    /// <see cref="SilhouettesPerBeacon"/> silhouettes behind each of its beacons, then one haze band for everything
    /// deeper in that direction. Reads only the tree's aggregates; lists nothing.
    /// </summary>
    public static IReadOnlyList<HorizonMark> Compose(FsTree tree, PlaceComposition place, PlaceParams p)
    {
        var marks = new List<HorizonMark>();
        foreach (var v in place.Vistas)
        {
            if (marks.Count >= MaxMarks) break;
            var bearing = Vector3.Normalize(v.Center with { Y = 0 });
            var side = new Vector3(-bearing.Z, 0, bearing.X);
            var silhouetteRing = v.Center + bearing * (v.Radius + SilhouetteSetback);
            var hazeRing = v.Center + bearing * (v.Radius + HazeSetback);
            var deeper = 0;
            for (var bi = 0; bi < v.Beacons.Count && marks.Count < MaxMarks; bi++)
            {
                var b = v.Beacons[bi];
                var node = tree[b.Id];
                // Each beacon's group is centred on the bearing, side by side in beacon order.
                var across = (bi - (v.Beacons.Count - 1) / 2f) * MathF.Min(MaxBeaconGroupSpacing, BeaconGroupSpreadPerRadius * v.Radius / MathF.Max(1, v.Beacons.Count));
                var basePos = silhouetteRing + side * across;
                if (Listing.IsUnknown(Listing.Of(node)) && Listing.Of(node) != ListingState.Partial)
                {
                    marks.Add(new HorizonMark(HorizonKind.Unknown, basePos, UnknownVeilHeight, UnknownVeilRadius, UnknownVeilWeight, b.Id));
                    continue;
                }

                var kids = node.Children.Where(c => tree[c].IsDirectory)
                    .OrderByDescending(c => tree[c].SubtreeFileCount + tree[c].SubtreeDirCount).ThenBy(c => c).ToList();
                for (var k = 0; k < kids.Count && k < SilhouettesPerBeacon && marks.Count < MaxMarks; k++)
                {
                    var g = tree[kids[k]];
                    var size = g.SubtreeFileCount + g.SubtreeDirCount;
                    var h = SilhouetteBaseHeight + SilhouetteHeightPerDecade * MathF.Log10(1 + size);
                    var offset = side * ((k - (MathF.Min(kids.Count, SilhouettesPerBeacon) - 1) / 2f) * SilhouetteSideStep) + bearing * (k * SilhouetteBackStep);
                    var unknown = Listing.IsUnknown(Listing.Of(g)) && Listing.Of(g) != ListingState.Partial;
                    marks.Add(new HorizonMark(unknown ? HorizonKind.Unknown : HorizonKind.Silhouette, basePos + offset, unknown ? UnknownSilhouetteHeight : h, SilhouetteBaseRadius + SilhouetteRadiusPerDecade * MathF.Log10(1 + size), Math.Clamp(MathF.Log10(1 + size) / SilhouetteFullWeightDecades, MinSilhouetteWeight, 1f), g.Id));
                    deeper += Math.Max(0, g.SubtreeDirCount);
                }

                deeper += Math.Max(0, kids.Count - SilhouettesPerBeacon);
            }

            if (deeper > 0 && marks.Count < MaxMarks)
            {
                // Everything deeper in this direction, as one restrained band of haze whose size and brightness follow its
                // count: the density of the tree beyond, not its detail.
                var w = Math.Clamp(MathF.Log10(1 + deeper) / HazeFullWeightDecades, MinHazeWeight, 1f);
                marks.Add(new HorizonMark(HorizonKind.Haze, hazeRing, HazeBaseHeight + HazeHeightSpan * w, MathF.Max(MinHazeRadius, v.Radius * (HazeRadiusFactor + w)), w, v.FolderId));
            }
        }

        return marks;
    }
}
