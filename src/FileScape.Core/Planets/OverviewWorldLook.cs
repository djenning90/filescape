using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Model;

namespace FileScape.Core.Planets;

/// <summary>The surface pattern of a miniature world in the overview (PLANET-92): drawn by one shared shader.</summary>
public enum WorldPattern
{
    /// <summary>A mottled rocky ball: small or plain folders.</summary>
    Plain,

    /// <summary>Latitude bands, like a gas giant: folders made of folders.</summary>
    Bands,

    /// <summary>Spots of the second colour: folders of mostly one kind of file.</summary>
    Spots,

    /// <summary>Polar caps of the second colour and a darker equator.</summary>
    Caps,

    /// <summary>Twisted bands: folders that mix three or more kinds of file.</summary>
    Swirl,
}

/// <summary>
/// One miniature world's look: a base and a second colour (linear RGB, 0…1 per channel), a pattern and a phase (0…1:
/// where the pattern starts and how fine it is). Packed by the app into a MultiMesh instance's colour and custom data.
/// </summary>
public readonly record struct WorldLook(Vector3 Base, Vector3 Second, WorldPattern Pattern, float Phase);

/// <summary>
/// The overview's miniature worlds' looks (PLANET-92, AD-77): DJ (fifth playtest follow-up) saw "many miniature planets
/// look uniformly pink" — every folder that holds only folders was a Hearth world, and its palette's accent is a salmon
/// pink, so a drive root's systems were one colour. Each world now takes a colour family and a pattern from its folder's
/// character and a seeded identity:
/// <list type="bullet">
/// <item><b>What it holds:</b> the dominant kind of file among its direct entries picks the hue (images rose, documents
/// blue, code amber, audio violet, video indigo, archives rust, data cyan…), a strong second kind the second colour;
/// a folder of folders takes one of eight mineral families (sand, slate, sage, ochre, teal, rust, lilac, stone);
/// a project (<see cref="PlanetComposer.HasProjectEvidence"/>) is the workshop's teal; an empty folder is pale.</item>
/// <item><b>Its size:</b> a bigger subtree is a little deeper in colour, and decides between banded and plain.</item>
/// <item><b>Its identity:</b> a seed from the case-folded names of the folder and its parent (FNV-1a), never the node
/// id, so the same folder looks the same in every run and on every machine, however the listings arrived; it jitters
/// hue, saturation and value a little and sets the pattern's phase.</item>
/// </list>
/// Cheap by design: the app draws every world with one shared material and passes this as per-instance data, so 1,500
/// worlds stay one draw (no material per world). Deterministic and engine-free (<c>OverviewWorldLookTests</c>).
/// Changing a family or threshold changes how every overview looks; keep looks stable for a given folder.
/// </summary>
public static class OverviewWorldLook
{
    /// <summary>Hue (0…1 round the colour wheel), saturation and value of each kind of file's colour family.</summary>
    private static (float H, float S, float V) KindColour(FileCategory c) => c switch
    {
        FileCategory.Image => (0.97f, 0.55f, 0.92f),      // rose
        FileCategory.Document => (0.60f, 0.55f, 0.88f),   // blue
        FileCategory.Code => (0.10f, 0.70f, 0.92f),       // amber
        FileCategory.Audio => (0.76f, 0.50f, 0.90f),      // violet
        FileCategory.Video => (0.66f, 0.55f, 0.75f),      // indigo
        FileCategory.Archive => (0.06f, 0.60f, 0.70f),    // rust brown
        FileCategory.Executable => (0.01f, 0.65f, 0.85f), // red
        FileCategory.Data => (0.52f, 0.60f, 0.90f),       // cyan
        _ => (0.72f, 0.22f, 0.80f),                        // lilac grey
    };

    /// <summary>Folders of folders: eight mineral families, picked by the seed (HSV as above). Deliberately no pink.</summary>
    private static readonly (float H, float S, float V)[] FolderFamilies =
    [
        (0.11f, 0.38f, 0.90f), // sand
        (0.60f, 0.32f, 0.80f), // slate blue
        (0.30f, 0.36f, 0.78f), // sage
        (0.09f, 0.58f, 0.86f), // ochre
        (0.48f, 0.48f, 0.78f), // teal
        (0.04f, 0.52f, 0.78f), // rust
        (0.74f, 0.30f, 0.84f), // lilac
        (0.08f, 0.14f, 0.82f), // stone
    ];

    /// <summary>A project's world: the workshop's teal (the repository workbench's colour family).</summary>
    private static readonly (float H, float S, float V) ProjectColour = (0.47f, 0.62f, 0.80f);

    /// <summary>An empty folder: pale, nearly white lavender (the Quiet world).</summary>
    private static readonly (float H, float S, float V) EmptyColour = (0.70f, 0.10f, 0.92f);

    /// <summary>The seed's jitter: hue ± 0.03 (≈ ±11°), saturation ± 0.08, value ± 0.06. Enough that neighbours of one
    /// family differ, not so much that a family stops reading as itself.</summary>
    private const float HueJitter = 0.03f, SaturationJitter = 0.08f, ValueJitter = 0.06f;

    /// <summary>A second kind of file this share of the files or more becomes the second colour; below it the second
    /// colour is a shade of the base.</summary>
    private const float SecondKindShare = 0.2f;

    /// <summary>One kind of file this share of the files or more makes a spotted world.</summary>
    private const float OneKindShare = 0.7f;

    /// <summary>A folder of folders with at least this many entries below it is banded (a gas giant), else plain or capped.</summary>
    private const int BandedSubtreeEntries = 40;

    /// <summary>How much darker a world gets per tenfold more entries below it (value), and the darkest it gets.</summary>
    private const float ValuePerDecade = 0.04f, MinValue = 0.55f;

    /// <summary>The look of folder <paramref name="id"/> as listed now (unlisted folders are drawn as ghosts, not with this).</summary>
    public static WorldLook Of(FsTree tree, int id, Func<FsNode, bool>? visible = null)
    {
        var n = tree[id];
        var seed = Seed(tree, id);
        var counts = new int[(int)FileCategory.Other + 1];
        int files = 0, dirs = 0;
        foreach (var c in n.Children)
        {
            var k = tree[c];
            if (visible != null && !visible(k)) continue;
            if (k.Kind == NodeKind.File)
            {
                files++;
                counts[(int)FileCategories.Of(k.Name)]++;
            }
            else
            {
                dirs++;
            }
        }

        // The dominant and second kinds of file.
        int first = -1, second = -1;
        for (var i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 0) continue;
            if (first < 0 || counts[i] > counts[first]) (second, first) = (first, i);
            else if (second < 0 || counts[i] > counts[second]) second = i;
        }

        var kinds = counts.Count(x => x > 0);
        var size = n.SubtreeFileCount + n.SubtreeDirCount;
        var u1 = Unit(seed, 1);
        var u2 = Unit(seed, 2);
        var u3 = Unit(seed, 3);
        (float H, float S, float V) baseHsv;
        (float H, float S, float V)? secondHsv = null;
        WorldPattern pattern;
        if (files + dirs == 0)
        {
            baseHsv = EmptyColour;
            pattern = WorldPattern.Plain;
        }
        else if (PlanetComposer.HasProjectEvidence(tree, n))
        {
            baseHsv = ProjectColour;
            secondHsv = KindColour(FileCategory.Code);
            pattern = WorldPattern.Swirl;
        }
        else if (files == 0)
        {
            baseHsv = FolderFamilies[(int)(seed % (uint)FolderFamilies.Length)];
            pattern = size >= BandedSubtreeEntries ? WorldPattern.Bands : u3 < 0.5f ? WorldPattern.Caps : WorldPattern.Plain;
        }
        else
        {
            var share = counts[first] / (float)files;
            baseHsv = KindColour((FileCategory)first);
            if (second >= 0 && counts[second] >= files * SecondKindShare) secondHsv = KindColour((FileCategory)second);
            // A folder with as many subfolders as files leans toward its mineral family: it is as much a system as a store.
            if (dirs >= files) baseHsv = Mix(baseHsv, FolderFamilies[(int)(seed % (uint)FolderFamilies.Length)], 0.35f);
            pattern = kinds >= 3 ? WorldPattern.Swirl : share >= OneKindShare ? WorldPattern.Spots : u3 < 0.5f ? WorldPattern.Bands : WorldPattern.Caps;
        }

        var h = Wrap(baseHsv.H + (u1 - 0.5f) * 2 * HueJitter);
        var s = Math.Clamp(baseHsv.S + (u2 - 0.5f) * 2 * SaturationJitter, 0f, 1f);
        var v = Math.Clamp(baseHsv.V - ValuePerDecade * MathF.Log10(1 + size) + (u3 - 0.5f) * 2 * ValueJitter, MinValue, 1f);
        var b = Rgb(h, s, v);
        // The second colour: a strong second kind's colour, else a deeper, slightly turned shade of the base.
        var sc = secondHsv is { } x ? Rgb(x.H, x.S, Math.Clamp(x.V * 0.9f, MinValue, 1f)) : Rgb(Wrap(h + (u2 < 0.5f ? -0.05f : 0.05f)), Math.Clamp(s * 1.15f, 0f, 1f), v * 0.68f);
        return new WorldLook(b, sc, pattern, Unit(seed, 4));
    }

    /// <summary>
    /// The world's identity seed: FNV-1a over the case-folded names of the folder and its parent (so two "src" folders
    /// in different projects differ), stable across runs and machines (string.GetHashCode is randomised per process).
    /// </summary>
    public static uint Seed(FsTree tree, int id)
    {
        var h = 2166136261u;
        void Mix(string s)
        {
            foreach (var ch in s.ToUpperInvariant())
            {
                h ^= ch;
                h *= 16777619u;
            }

            h ^= '/';
            h *= 16777619u;
        }

        var n = tree[id];
        if (n.ParentId >= 0 && tree.Contains(n.ParentId)) Mix(tree[n.ParentId].Name);
        Mix(n.Name);
        return h;
    }

    /// <summary>A fraction in [0, 1) from the seed and a salt (a second multiplicative hash, so salts are unrelated).</summary>
    private static float Unit(uint seed, uint salt)
    {
        var x = (seed ^ (salt * 0x9E3779B9u)) * 2654435761u;
        x ^= x >> 15;
        return (x & 0xFFFFFF) / (float)0x1000000;
    }

    private static float Wrap(float h) => h - MathF.Floor(h);

    /// <summary>Mixes two HSV colours, the hue the short way round the wheel.</summary>
    private static (float H, float S, float V) Mix((float H, float S, float V) a, (float H, float S, float V) b, float t)
    {
        var dh = b.H - a.H;
        if (dh > 0.5f) dh -= 1;
        if (dh < -0.5f) dh += 1;
        return (Wrap(a.H + dh * t), a.S + (b.S - a.S) * t, a.V + (b.V - a.V) * t);
    }

    /// <summary>HSV (each 0…1) to RGB (0…1), the standard hexcone conversion.</summary>
    public static Vector3 Rgb(float h, float s, float v)
    {
        var i = (int)MathF.Floor(h * 6) % 6;
        var f = h * 6 - MathF.Floor(h * 6);
        var p = v * (1 - s);
        var q = v * (1 - f * s);
        var t = v * (1 - (1 - f) * s);
        return i switch
        {
            0 => new Vector3(v, t, p),
            1 => new Vector3(q, v, p),
            2 => new Vector3(p, v, t),
            3 => new Vector3(p, q, v),
            4 => new Vector3(t, p, v),
            _ => new Vector3(v, p, q),
        };
    }

    /// <summary>RGB (0…1) to its hue (0…1), for tests and probes.</summary>
    public static float HueOf(Vector3 c)
    {
        var max = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
        var min = MathF.Min(c.X, MathF.Min(c.Y, c.Z));
        var d = max - min;
        if (d < 1e-6f) return 0;
        float h;
        if (max == c.X) h = (c.Y - c.Z) / d % 6;
        else if (max == c.Y) h = (c.Z - c.X) / d + 2;
        else h = (c.X - c.Y) / d + 4;
        return Wrap(h / 6);
    }
}
