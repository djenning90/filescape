namespace FileScape.Fixtures;

/// <summary>
/// The vast fixture for the overview's attention (PLANET-101..109, AD-81, sixth pass): an arrival root "Vast Reach" far
/// larger than any budget the overview had, with many distant branches and three routes much deeper than the depth where
/// DJ's overview stopped revealing children (≈ 8 levels below a distribution's root). Sixteen regions, each six sectors ×
/// six clusters × six systems × <c>moons</c> moons (12 by default: ≈ 45,600 folders), and in regions 03, 09 and 14 a
/// "Deep Route" of sixteen nested passages (the last one 18 levels below the root), each passage with four alcoves of two
/// nooks. A small file in every system and passage. Owned, marked directory under the fixtures parent; names and sizes
/// are synthetic. Used by <c>planets-overview-attention</c> (the journey) and <c>planets-overview-bodies</c> (renderer
/// measurements).
/// </summary>
public static class VastFixture
{
    public const string RootName = "Vast Reach";

    /// <summary>Regions below the root; sectors, clusters and systems per level; passages on each deep route.</summary>
    public const int Regions = 16, Fanout = 6, Passages = 16;

    /// <summary>The regions holding a deep route.</summary>
    public static readonly int[] DeepRegions = [3, 9, 14];

    public sealed record Result(string FixtureDir, string Root, int Folders, FixtureManifest Manifest)
    {
        public override string ToString() => $"vast fixture: {FixtureDir}\n  root: {Root}\n  folders below the root: {Folders}\n{Manifest}";
    }

    public static Result Create(string? parent = null, int moons = 12)
    {
        parent ??= FixtureBuilder.DefaultParent;
        var dir = Path.Combine(parent, "vast-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        var m = new FixtureManifest { Root = dir };
        FixtureBuilder.WriteFile(m, Path.Combine(dir, FixtureBuilder.MarkerName), "FileScape vast fixture. Safe to delete.\n"u8.ToArray());
        var root = FixtureBuilder.MakeDir(m, dir, RootName);
        var folders = 0;
        string Dir(string p, string name)
        {
            folders++;
            return FixtureBuilder.MakeDir(m, p, name);
        }

        for (var r = 1; r <= Regions; r++)
        {
            var region = Dir(root, $"Region {r:D2}");
            for (var s = 0; s < Fanout; s++)
            {
                var sector = Dir(region, $"Sector {(char)('A' + s)}");
                for (var c = 1; c <= Fanout; c++)
                {
                    var cluster = Dir(sector, $"Cluster {c}");
                    for (var y = 1; y <= Fanout; y++)
                    {
                        var system = Dir(cluster, $"System {y}");
                        FixtureBuilder.WriteFile(m, Path.Combine(system, "survey.txt"), new byte[64 + y]);
                        for (var k = 0; k < moons; k++) Dir(system, $"Moon {(char)('a' + k)}");
                    }
                }
            }

            if (!DeepRegions.Contains(r)) continue;
            var passage = Dir(region, "Deep Route");
            for (var p = 1; p <= Passages; p++)
            {
                passage = Dir(passage, $"Passage {p:D2}");
                FixtureBuilder.WriteFile(m, Path.Combine(passage, "log.txt"), new byte[80 + p]);
                for (var a = 0; a < 4; a++)
                {
                    var alcove = Dir(passage, $"Alcove {(char)('a' + a)}");
                    for (var n = 1; n <= 2; n++) Dir(alcove, $"Nook {n}");
                }
            }
        }

        return new Result(dir, root, folders, m);
    }
}
