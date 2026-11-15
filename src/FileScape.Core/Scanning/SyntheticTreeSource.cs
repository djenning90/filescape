using FileScape.Core.Model;

namespace FileScape.Core.Scanning;

/// <summary>
/// Generates a deterministic, shareable tree in memory. The output depends only on <see cref="Seed"/> and
/// <see cref="TargetNodes"/>. Every node carries <see cref="NodeFlags.Synthetic"/> and paths use the
/// synthetic:// root, so the data cannot be mistaken for the live filesystem (UX-06). Problem cases
/// (access denied, links, cloud) are simulated by flags only. Real Windows behaviour is covered by the on-disk
/// fixture (FileScape.Fixtures).
/// <para>
/// It is the default world when no folder is given, the sample for screenshots and the load generator for frame-time
/// probes (<see cref="NodesPerSecond"/>). Any change to the generator (an extra random draw, a reordered table, a new
/// literal) changes every synthetic tree, so the tests and probes that count its nodes must be updated with it.
/// </para>
/// </summary>
public sealed class SyntheticTreeSource : IScanSource
{
    /// <summary>The fixed age reference, so synthetic colours never drift.</summary>
    public static readonly DateTime ReferenceUtc = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>The generator seed; the default 1965 is the tree every test and screenshot expects (choice of value not recorded).</summary>
    public int Seed { get; init; } = 1965;

    /// <summary>
    /// The total node count. The fixed edge cases alone need about 2,330 nodes. Below that, the tree contains just
    /// those cases, and the seed only affects their timestamps and sizes.
    /// </summary>
    public int TargetNodes { get; init; } = 12_000;

    /// <summary>When greater than 0, emission is throttled to simulate a slow scan (for measuring frame time during scans).</summary>
    public int NodesPerSecond { get; init; }

    public DataSourceInfo Describe() =>
        new(DataOrigin.Synthetic, $"synthetic://seed-{Seed}", $"Synthetic sample data (seed {Seed}, ~{TargetNodes:N0} nodes)")
        {
            AgeReferenceUtc = ReferenceUtc,
        };

    /// <summary>A generated node before ids are assigned in breadth-first order.</summary>
    public sealed class Spec
    {
        public required string Name;
        public NodeKind Kind;
        public NodeFlags Flags;
        public long Size;
        public DateTime Modified;
        public DirScanState State = DirScanState.Complete;
        public string? Error;
        public List<Spec> Children = [];
    }

    /// <summary>
    /// Generates the whole tree up front, then emits it breadth first, one directory per batch, as a real scan would;
    /// when <see cref="NodesPerSecond"/> is set it sleeps so the cumulative node count tracks that rate.
    /// </summary>
    public void Run(ScanContext context, CancellationToken ct)
    {
        var root = Generate();
        var queue = new Queue<(Spec Spec, int Id, int Depth)>();
        var batch = new ScanBatch();
        batch.NewNodes.Add(Record(root, 0, -1, 0));
        context.Nodes = 1;
        context.Directories = 1;
        context.Emit(batch);
        queue.Enqueue((root, 0, 0));
        var nextId = 1;
        var started = DateTime.UtcNow;

        while (queue.Count > 0)
        {
            var (dir, dirId, depth) = queue.Dequeue();
            context.Pending = queue.Count + 1;
            if (ct.IsCancellationRequested)
            {
                var cancelled = new ScanBatch();
                cancelled.DirectoryUpdates.Add(new DirectoryUpdate(dirId, DirScanState.Cancelled, NodeFlags.Incomplete));
                while (queue.Count > 0)
                {
                    cancelled.DirectoryUpdates.Add(new DirectoryUpdate(queue.Dequeue().Id, DirScanState.Cancelled, NodeFlags.Incomplete));
                }

                context.Pending = 0;
                context.Emit(cancelled);
                return;
            }

            context.BeginOperation(dir.Name);
            batch = new ScanBatch();
            foreach (var child in dir.Children)
            {
                var id = nextId++;
                batch.NewNodes.Add(Record(child, id, dirId, depth + 1));
                context.Nodes++;
                switch (child.Kind)
                {
                    case NodeKind.Directory: context.Directories++; break;
                    case NodeKind.File: context.Files++; context.LogicalBytes += child.Size; break;
                    default: context.Links++; break;
                }

                if (child.Kind == NodeKind.Directory && child.State == DirScanState.Complete)
                {
                    queue.Enqueue((child, id, depth + 1));
                }
                else if (child.Kind == NodeKind.Directory && child.Error != null)
                {
                    context.Problems++;
                }
            }

            batch.DirectoryUpdates.Add(new DirectoryUpdate(dirId, DirScanState.Complete));
            context.EndOperation();
            context.Emit(batch);

            if (NodesPerSecond > 0)
            {
                var due = started + TimeSpan.FromSeconds(context.Nodes / (double)NodesPerSecond);
                var wait = due - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    ct.WaitHandle.WaitOne(wait);
                }
            }
        }

        context.Pending = 0;
    }

    private static NodeRecord Record(Spec s, int id, int parentId, int depth)
    {
        var state = s.Kind == NodeKind.Directory
            ? (s.State == DirScanState.Complete ? DirScanState.Pending : s.State)
            : DirScanState.NotApplicable;
        var record = new NodeRecord(id, parentId, s.Name, s.Kind, s.Flags | NodeFlags.Synthetic, 0, s.Size,
            s.Modified, s.Modified, s.Modified, depth, state);
        return record;
    }

    /// <summary>Builds the whole spec tree. It is public so tests can compute the expected counts.</summary>
    public Spec Generate()
    {
        var rng = new SplitMix(Seed);
        var root = Dir("synthetic-demo");
        var budget = TargetNodes - 1;

        void Add(Spec parent, Spec child)
        {
            parent.Children.Add(child);
            budget--;
        }

        // Fixed edge cases come first, so they always appear even in small trees.
        var edge = Dir("edge-cases");
        Add(root, edge);
        Add(edge, File("hidden-settings.ini", 412, rng, NodeFlags.Hidden));
        Add(edge, File("system-thumbs.db", 28_672, rng, NodeFlags.Hidden | NodeFlags.System));
        Add(edge, new Spec { Name = "junction-to-root", Kind = NodeKind.Link, Flags = NodeFlags.ReparsePoint | NodeFlags.DirectoryLink, Modified = Date(rng) });
        Add(edge, new Spec { Name = "symlink-readme.md", Kind = NodeKind.Link, Flags = NodeFlags.ReparsePoint, Modified = Date(rng) });
        Add(edge, new Spec { Name = "access-denied", Kind = NodeKind.Directory, Flags = NodeFlags.AccessDenied, State = DirScanState.Failed, Error = "Access to the path is denied. (simulated)", Modified = Date(rng) });
        Add(edge, new Spec { Name = "vanished-during-scan", Kind = NodeKind.Directory, Flags = NodeFlags.Vanished, State = DirScanState.Failed, Error = "Could not find a part of the path. (simulated)", Modified = Date(rng) });
        Add(edge, File("cloud-placeholder.pptx", 18_400_000, rng, NodeFlags.CloudPlaceholder));
        Add(edge, new Spec { Name = "cloud-folder", Kind = NodeKind.Directory, Flags = NodeFlags.CloudPlaceholder | NodeFlags.CloudNotTraversed, State = DirScanState.NotTraversed, Modified = Date(rng) });
        Add(edge, File("sparse-disk-image.vhdx", 64L << 30, rng, NodeFlags.Sparse));

        var outliers = Dir("size-outliers");
        Add(root, outliers);
        foreach (var (name, size) in new[]
                 {
                     ("one-byte.bin", 1L), ("ten-kib.dat", 10L << 10), ("hundred-mib.iso", 100L << 20),
                     ("four-gib.mkv", 4L << 30), ("fifty-gib.vhd", 50L << 30), ("two-tib-simulated.img", 2L << 40),
                 })
        {
            Add(outliers, File(name, size, rng));
        }

        var zero = Dir("zero-byte-200");
        Add(root, zero);
        for (var i = 0; i < 200; i++)
        {
            Add(zero, File($"empty-{i:D3}.lock", 0, rng));
        }

        var unicode = Dir("unicode-名前-Ünïcödé");
        Add(root, unicode);
        foreach (var name in new[]
                 {
                     "日本語のファイル.txt", "Ελληνικά σημειώσεις.md", "emoji-🚀-launch.log", "עברית-קובץ.txt",
                     "combining-éclair.txt", "кириллица-отчёт.docx", "한국어-문서.pdf", "हिन्दी-नोट्स.txt",
                     "trailing space .txt", "mixed-Ꮳℌℰℛ🜲.dat",
                 })
        {
            Add(unicode, File(name, rng.NextLong(10, 2_000_000), rng));
        }

        var deep = Dir("deep-40-levels");
        Add(root, deep);
        var cursor = deep;
        for (var level = 1; level <= 40; level++)
        {
            var next = Dir($"level-{level:D2}");
            Add(cursor, next);
            Add(next, File($"note-{level:D2}.txt", rng.NextLong(100, 50_000), rng));
            cursor = next;
        }

        var longPath = Dir("long-path");
        Add(root, longPath);
        cursor = longPath;
        for (var i = 0; i < 8; i++)
        {
            var next = Dir($"a-rather-long-directory-name-segment-{i:D2}-ünicode");
            Add(cursor, next);
            cursor = next;
        }

        Add(cursor, File("deeply-nested-file-whose-full-path-exceeds-the-classic-260-character-windows-limit.txt", 1234, rng));

        var wide = Dir("wide-2000-files");
        Add(root, wide);
        for (var i = 0; i < 2000; i++)
        {
            Add(wide, File($"frame-{i:D4}.{(i % 3 == 0 ? "png" : "jpg")}", rng.NextLong(20_000, 900_000), rng));
        }

        // Random "projects" fill the remaining budget.
        var topics = new[] { "photos", "music", "projects", "documents", "games", "downloads", "archive", "research", "media", "code" };
        var t = 0;
        while (budget > 0)
        {
            var top = Dir($"{topics[t % topics.Length]}{(t >= topics.Length ? $"-{t / topics.Length + 1}" : "")}");
            Add(root, top);
            t++;
            FillRandom(top, 1, rng, ref budget);
        }

        return root;
    }

    private static readonly string[] Words =
    [
        "alpha", "beacon", "cinder", "delta", "ember", "fjord", "gamma", "harbor", "iris", "juniper", "kepler",
        "lumen", "meridian", "nebula", "orbit", "prism", "quartz", "relay", "sierra", "tundra", "umbra", "vector",
        "willow", "xenon", "yarrow", "zephyr", "atlas", "basalt", "cobalt", "drift",
    ];

    /// <summary>
    /// File types and their size distribution: sizes are log-normal, e^(LogMean + LogSd·z) bytes, so LogMean 8 is a typical
    /// ~3 KB text file, 14 a ~1.2 MB photo and 19 a ~180 MB video; LogSd is how widely sizes spread around that.
    /// </summary>
    private static readonly (string Ext, double LogMean, double LogSd)[] FileTypes =
    [
        ("txt", 8, 1.5), ("md", 8, 1.2), ("pdf", 13, 1.5), ("docx", 11.5, 1.2), ("xlsx", 11, 1.3),
        ("jpg", 14, 0.8), ("png", 12.5, 1.5), ("mp3", 15.5, 0.5), ("flac", 17, 0.5), ("mp4", 19, 1.5),
        ("zip", 16, 2), ("cs", 8.5, 1), ("py", 8, 1), ("json", 8, 2), ("dll", 13, 1.5), ("exe", 15, 1.5),
        ("log", 10, 2.5), ("psd", 18, 1),
    ];

    /// <summary>
    /// Fills one folder of a random "project" until the node budget runs out: up to 30 files near the top (12 below
    /// depth 4), 2–6 subfolders at depths 1–2 and 0–3 below, recursion stopping at depth 7. The draw order matters: it
    /// fixes the tree for a seed.
    /// </summary>
    private static void FillRandom(Spec dir, int depth, SplitMix rng, ref int budget)
    {
        var fileCount = (int)Math.Min(budget, rng.NextInt(depth == 1 ? 2 : 0, depth > 4 ? 12 : 30));
        var type = FileTypes[rng.NextInt(0, FileTypes.Length)];
        for (var i = 0; i < fileCount && budget > 0; i++)
        {
            // Directories tend to hold one dominant type with some variety (70 % of files are the folder's type).
            var ft = rng.NextDouble() < 0.7 ? type : FileTypes[rng.NextInt(0, FileTypes.Length)];
            var size = (long)Math.Exp(ft.LogMean + ft.LogSd * rng.NextGaussian());
            dir.Children.Add(File($"{Words[rng.NextInt(0, Words.Length)]}-{rng.NextInt(1, 999):D3}.{ft.Ext}", Math.Max(0, size), rng));
            budget--;
        }

        if (depth >= 7 || budget <= 0)
        {
            return;
        }

        var subdirs = rng.NextInt(depth <= 2 ? 2 : 0, depth <= 2 ? 7 : 4);
        for (var i = 0; i < subdirs && budget > 0; i++)
        {
            var sub = Dir($"{Words[rng.NextInt(0, Words.Length)]}-{Words[rng.NextInt(0, Words.Length)]}");
            dir.Children.Add(sub);
            budget--;
            FillRandom(sub, depth + 1, rng, ref budget);
        }

        // Unique, deterministic names within a directory.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in dir.Children)
        {
            var name = c.Name;
            for (var n = 2; !seen.Add(name); n++)
            {
                name = $"{Path.GetFileNameWithoutExtension(c.Name)}-{n}{Path.GetExtension(c.Name)}";
            }

            c.Name = name;
        }

        dir.Children.Sort(static (a, b) =>
        {
            var k = (a.Kind == NodeKind.Directory ? 0 : 1).CompareTo(b.Kind == NodeKind.Directory ? 0 : 1);
            return k != 0 ? k : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        });
    }

    private static Spec Dir(string name) => new() { Name = name, Kind = NodeKind.Directory, Modified = ReferenceUtc.AddDays(-3) };

    private static Spec File(string name, long size, SplitMix rng, NodeFlags flags = NodeFlags.None) =>
        new() { Name = name, Kind = NodeKind.File, Size = size, Flags = flags, Modified = Date(rng) };

    /// <summary>
    /// Ages are skewed so every FSN age band (one week up to more than one year) is well represented: up to 900 days
    /// old, u^2.2 crowding most files towards recent dates, plus a random time of day.
    /// </summary>
    private static DateTime Date(SplitMix rng) => ReferenceUtc.AddDays(-Math.Pow(rng.NextDouble(), 2.2) * 900).AddSeconds(-rng.NextInt(0, 86_400));

    /// <summary>
    /// A small deterministic PRNG. It avoids depending on System.Random's seeding behaviour across .NET versions.
    /// The constants are SplitMix64's (Steele, Lea and Flood): 0x9E3779B97F4A7C15 is 2⁶⁴/φ, the step that walks the
    /// state, and the two multipliers are its output mixer; the second constant in the seeding just offsets small seeds.
    /// </summary>
    public sealed class SplitMix(int seed)
    {
        private ulong _state = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

        public ulong NextULong()
        {
            var z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform in [0, 1): the top 53 bits, exactly a double's mantissa.</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));
        public int NextInt(int minInclusive, int maxExclusive) => minInclusive + (int)(NextDouble() * (maxExclusive - minInclusive));
        public long NextLong(long minInclusive, long maxExclusive) => minInclusive + (long)(NextDouble() * (maxExclusive - minInclusive));

        /// <summary>A standard normal sample (Box–Muller); 1 − u keeps the logarithm's argument above 0.</summary>
        public double NextGaussian()
        {
            var u1 = 1.0 - NextDouble();
            var u2 = NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }
}
