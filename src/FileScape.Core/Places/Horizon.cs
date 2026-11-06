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
    public const int MaxMarks = 220;

    /// <summary>Silhouettes per vista beacon (a folder's own folders, largest first).</summary>
    public const int SilhouettesPerBeacon = 3;

    public static IReadOnlyList<HorizonMark> Compose(FsTree tree, PlaceComposition place, PlaceParams p)
    {
        var marks = new List<HorizonMark>();
        foreach (var v in place.Vistas)
        {
            if (marks.Count >= MaxMarks) break;
            var bearing = Vector3.Normalize(v.Center with { Y = 0 });
            var side = new Vector3(-bearing.Z, 0, bearing.X);
            var silhouetteRing = v.Center + bearing * (v.Radius + 14f);
            var hazeRing = v.Center + bearing * (v.Radius + 36f);
            var deeper = 0;
            for (var bi = 0; bi < v.Beacons.Count && marks.Count < MaxMarks; bi++)
            {
                var b = v.Beacons[bi];
                var node = tree[b.Id];
                var across = (bi - (v.Beacons.Count - 1) / 2f) * MathF.Min(3.2f, 2.2f * v.Radius / MathF.Max(1, v.Beacons.Count));
                var basePos = silhouetteRing + side * across;
                if (Listing.IsUnknown(Listing.Of(node)) && Listing.Of(node) != ListingState.Partial)
                {
                    marks.Add(new HorizonMark(HorizonKind.Unknown, basePos, 9f, 1.6f, 0.5f, b.Id));
                    continue;
                }

                var kids = node.Children.Where(c => tree[c].IsDirectory)
                    .OrderByDescending(c => tree[c].SubtreeFileCount + tree[c].SubtreeDirCount).ThenBy(c => c).ToList();
                for (var k = 0; k < kids.Count && k < SilhouettesPerBeacon && marks.Count < MaxMarks; k++)
                {
                    var g = tree[kids[k]];
                    var size = g.SubtreeFileCount + g.SubtreeDirCount;
                    var h = 5f + 4.5f * MathF.Log10(1 + size);
                    var offset = side * ((k - (MathF.Min(kids.Count, SilhouettesPerBeacon) - 1) / 2f) * 1.6f) + bearing * (k * 2.5f);
                    var unknown = Listing.IsUnknown(Listing.Of(g)) && Listing.Of(g) != ListingState.Partial;
                    marks.Add(new HorizonMark(unknown ? HorizonKind.Unknown : HorizonKind.Silhouette, basePos + offset, unknown ? 7f : h, 0.9f + 0.25f * MathF.Log10(1 + size), Math.Clamp(MathF.Log10(1 + size) / 4f, 0.1f, 1f), g.Id));
                    deeper += Math.Max(0, g.SubtreeDirCount);
                }

                deeper += Math.Max(0, kids.Count - SilhouettesPerBeacon);
            }

            if (deeper > 0 && marks.Count < MaxMarks)
            {
                // Everything deeper in this direction, as one restrained band of haze whose size and brightness follow its
                // count: the density of the tree beyond, not its detail.
                var w = Math.Clamp(MathF.Log10(1 + deeper) / 4f, 0.08f, 1f);
                marks.Add(new HorizonMark(HorizonKind.Haze, hazeRing, 10f + 22f * w, MathF.Max(6f, v.Radius * (0.8f + w)), w, v.FolderId));
            }
        }

        return marks;
    }
}
