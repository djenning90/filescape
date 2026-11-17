using FileScape.Core.Interaction;
using FileScape.Core.Model;

namespace FileScape.Core.Places;

/// <summary>What a row of the Destinations tree stands for (PLANET-39).</summary>
public enum DestinationRowKind
{
    /// <summary>The way back up to the current folder's parent.</summary>
    WayBack,

    /// <summary>A section heading (the favourites and repositories).</summary>
    Section,

    /// <summary>A pinned folder or a known repository, anywhere (PLANET-41).</summary>
    Favourite,

    /// <summary>A folder: a child of where you are (depth 0) or a descendant in an expanded branch.</summary>
    Folder,

    /// <summary>A directory link (junction, symlink): followed only on purpose, never expanded here.</summary>
    Link,

    /// <summary>The "Files here" group of the current folder.</summary>
    FilesGroup,

    /// <summary>A file in the current folder.</summary>
    File,

    /// <summary>A line of explanation inside a branch: empty, listing, access denied, more rows than shown.</summary>
    Note,
}

/// <summary>
/// One row of the Destinations tree. <see cref="Key"/> is stable across rebuilds (so selection, expansion and scroll
/// survive a tree that grows while it is open): <c>f{id}</c> for a folder or link, <c>files</c>, <c>fav</c>,
/// <c>p:{path}</c> for a pin, <c>r{id}</c> for a repository, <c>up</c>, and <c>n…</c> for notes.
/// <see cref="Depth"/> is the indentation level (0 = the current folder's own entries).
/// </summary>
public sealed record DestinationRow(
    string Key,
    DestinationRowKind Kind,
    int Id,
    int Depth,
    string Name,
    string Detail,
    bool CanExpand = false,
    bool Expanded = false,
    ListingState State = ListingState.Open,
    bool IsHere = false,
    bool IsRepository = false,
    bool Warning = false,
    string? Path = null);

/// <summary>Why a place is in the favourites section: the player's pin, or a repository FileScape has seen.</summary>
public enum FavouriteKind
{
    Pinned,
    Repository,
}

/// <summary>A place the player cares about: <see cref="Id"/> is −1 when it is not in the tree yet (a pin from an earlier session: reached by path).</summary>
public sealed record FavouritePlace(FavouriteKind Kind, string Path, string Name, int Id);

/// <summary>
/// What to build the Destinations tree from. <see cref="Expanded"/> holds row keys the player opened; <c>!files</c>
/// and <c>!fav</c> record that the player closed a group that is open by default.
/// </summary>
public sealed record DestinationQuery
{
    public required int Current { get; init; }
    public IReadOnlySet<string> Expanded { get; init; } = new HashSet<string>();
    public string Filter { get; init; } = "";

    /// <summary>The shared visibility rule (hidden and system entries, AD-19).</summary>
    public Func<FsNode, bool> Visible { get; init; } = _ => true;

    public IReadOnlyList<FavouritePlace> Favourites { get; init; } = [];

    /// <summary>Folders known to be repositories (their row is marked).</summary>
    public IReadOnlySet<int> Repositories { get; init; } = new HashSet<int>();

    /// <summary>
    /// At most this many folder rows per expanded branch (and per filter): a branch with thousands of folders stays
    /// usable and cheap to rebuild; the rest are named in a note and found by typing (rationale for 250: a few screens
    /// of dense rows).
    /// </summary>
    public int MaxBranchRows { get; init; } = 250;

    /// <summary>At most this many file rows (the previous list's limit, kept: the filter narrows the rest).</summary>
    public int MaxFileRows { get; init; } = 5000;

    /// <summary>At most this many favourites (pins first): enough for a working set, small enough to scan by eye.</summary>
    public int MaxFavouriteRows { get; init; } = 60;
}

/// <summary>The rows to show; <see cref="HereCount"/> counts the current folder's own entries that pass the filter (folders, links, files); <see cref="NeedsListing"/> are expanded folders nobody has listed yet.</summary>
public sealed record DestinationList(IReadOnlyList<DestinationRow> Rows, int HereCount, IReadOnlyList<int> NeedsListing);

/// <summary>
/// The Destinations tree (PLANET-39, AD-56): the current folder's worlds as rows, each folder expandable in place to
/// its own folders, and so on down; the way back; the files here in a group; the favourites and repositories
/// (PLANET-41). Pure: it reads the tree and the query and never lists anything itself — an expanded folder that has
/// not been listed is reported in <see cref="DestinationList.NeedsListing"/> and shown as "listing…", so the app asks
/// the scanner for exactly the folders the player opened.
/// <para>
/// Every folder row says what is behind it before it is opened: its counts, or "empty", "not listed yet", "listing…",
/// "access denied". A branch whose folder turns out to hold no folders says so in a note instead of looking blank. The
/// filter matches folder names (case-insensitive) anywhere below where you are among what is already listed (bounded:
/// <see cref="Search.MaxSearchFolders"/>) and opens the way to each match; files are matched in this folder only.
/// </para>
/// Preserve: rows are built in display order; keys are stable for the same node; nothing here costs more than the rows
/// it returns plus the children of expanded folders (bounded by <see cref="DestinationQuery.MaxBranchRows"/>).
/// </summary>
public static class DestinationTree
{
    public static DestinationList Build(FsTree tree, DestinationQuery q)
    {
        var rows = new List<DestinationRow>();
        var needs = new List<int>();
        if (!tree.Contains(q.Current)) return new DestinationList(rows, 0, needs);
        var filter = q.Filter.Trim();
        var filtering = filter.Length > 0;
        bool Match(string s) => s.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
        var cur = tree[q.Current];
        var here = 0;

        if (cur.ParentId >= 0 && tree.Contains(cur.ParentId))
        {
            var name = $"Back up to {tree.DisplayName(cur.ParentId)}";
            if (!filtering || Match(name)) rows.Add(new DestinationRow("up", DestinationRowKind.WayBack, cur.ParentId, 0, name, "the way back (U)"));
        }

        if (!filtering) AddFavourites(rows, q, filter);

        var kids = cur.Children.Where(c => tree[c].IsDirectory || tree[c].Kind == NodeKind.Link).Where(c => q.Visible(tree[c]))
            .OrderBy(c => tree[c].Name, NaturalNameOrder.Instance).ToList();
        var search = filtering ? Search.Over(tree, q, filter) : null;
        var shown = 0;
        foreach (var c in kids)
        {
            if (shown >= q.MaxBranchRows && !filtering) break;
            var block = FolderRows(tree, q, c, 0, search, needs);
            if (block.Count == 0) continue;
            here++;
            if (shown < q.MaxBranchRows) rows.AddRange(block);
            shown++;
        }

        if (!filtering && kids.Count > shown) rows.Add(Note("more-here", 0, $"… {kids.Count - shown:N0} more worlds: type to filter"));
        if (search is { Truncated: true }) rows.Add(Note("more-search", 0, $"searched the first {Search.MaxSearchFolders:N0} folders below here"));

        var files = cur.Children.Where(c => tree[c].Kind == NodeKind.File && q.Visible(tree[c]))
            .Where(c => !filtering || Match(tree[c].Name)).OrderBy(c => tree[c].Name, NaturalNameOrder.Instance).ToList();
        here += files.Count;
        if (files.Count > 0)
        {
            var open = filtering || (q.Expanded.Contains("files") || (!q.Expanded.Contains("!files") && kids.Count == 0));
            var total = cur.Children.Count(c => tree[c].Kind == NodeKind.File && q.Visible(tree[c]));
            rows.Add(new DestinationRow("files", DestinationRowKind.FilesGroup, q.Current, 0, filtering ? $"Files here matching “{filter}”" : "Files here",
                filtering ? $"{files.Count:N0} of {total:N0}" : $"{total:N0} file{(total == 1 ? "" : "s")}", CanExpand: true, Expanded: open));
            if (open)
            {
                foreach (var f in files.Take(q.MaxFileRows))
                {
                    var n = tree[f];
                    rows.Add(new DestinationRow($"f{f}", DestinationRowKind.File, f, 1, n.Name, SizeText(n.Size)));
                }

                if (files.Count > q.MaxFileRows) rows.Add(Note("more-files", 1, $"… {files.Count - q.MaxFileRows:N0} more files: type to filter"));
            }
        }

        if (filtering) AddFavourites(rows, q, filter);
        return new DestinationList(rows, here, needs);
    }

    /// <summary>
    /// While filtering: the folders below where you are whose names match, and every folder on the way to one
    /// (<see cref="Keep"/>), found among what is already listed — breadth first, at most <see cref="MaxSearchFolders"/>
    /// folders, so typing stays instant on a large tree. Nothing is listed to search.
    /// </summary>
    private sealed class Search
    {
        /// <summary>The most folders one filter looks through below where you are (well under a frame; rationale: a frame's budget with room to spare).</summary>
        public const int MaxSearchFolders = 20_000;

        public required string Filter;
        public readonly HashSet<int> Keep = [];
        public bool Truncated;

        public bool Matches(string name) => name.Contains(Filter, StringComparison.CurrentCultureIgnoreCase);

        public static Search Over(FsTree tree, DestinationQuery q, string filter)
        {
            var s = new Search { Filter = filter };
            var queue = new Queue<int>();
            queue.Enqueue(q.Current);
            var seen = 0;
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                foreach (var c in tree[id].Children)
                {
                    var n = tree[c];
                    if (!(n.IsDirectory || n.Kind == NodeKind.Link) || !q.Visible(n)) continue;
                    if (++seen > MaxSearchFolders)
                    {
                        s.Truncated = true;
                        return s;
                    }

                    if (s.Matches(n.Name))
                    {
                        // The match and the folders on the way to it (stops early where an earlier match already marked the way).
                        for (var a = c; a >= 0 && a != q.Current && s.Keep.Add(a); a = tree[a].ParentId)
                        {
                        }
                    }

                    if (n.IsDirectory) queue.Enqueue(c);
                }
            }

            return s;
        }
    }

    /// <summary>
    /// A folder's row and, when it is open, its branch (empty when the filter rules it out). While filtering, a folder on
    /// the way to a match opens by itself to show the way; a match the player opened shows its branch whole (you found
    /// it; now look inside).
    /// </summary>
    private static List<DestinationRow> FolderRows(FsTree tree, DestinationQuery q, int id, int depth, Search? search, List<int> needs)
    {
        var n = tree[id];
        if (search != null && !search.Keep.Contains(id)) return [];
        var state = Listing.Of(n);
        var isLink = n.Kind == NodeKind.Link;
        var subCount = isLink ? 0 : n.Children.Count(c => (tree[c].IsDirectory || tree[c].Kind == NodeKind.Link) && q.Visible(tree[c]));
        var unknown = state is ListingState.Unlisted or ListingState.NotScanned or ListingState.Pending or ListingState.Listing;
        var canExpand = !isLink && (subCount > 0 || unknown);
        var userOpen = canExpand && q.Expanded.Contains($"f{id}");
        var matches = search == null || search.Matches(n.Name);
        var leadsOn = search != null && !isLink && n.Children.Any(search.Keep.Contains);
        var branch = new List<DestinationRow>();
        var open = false;
        if (userOpen && matches)
        {
            branch = Branch(tree, q, id, depth + 1, null, needs);
            open = true;
        }
        else if (leadsOn)
        {
            branch = Branch(tree, q, id, depth + 1, search, needs);
            open = true;
        }

        var row = new DestinationRow($"f{id}", isLink ? DestinationRowKind.Link : DestinationRowKind.Folder, id, depth, n.Name, Detail(n, state),
            CanExpand: canExpand, Expanded: open, State: state, IsHere: id == q.Current, IsRepository: q.Repositories.Contains(id),
            Warning: state is ListingState.Denied or ListingState.Failed or ListingState.Unavailable);
        var result = new List<DestinationRow>(1 + branch.Count) { row };
        if (open) result.AddRange(branch);
        return result;
    }

    /// <summary>An open folder's own folders (bounded), or a note saying why there are none to show.</summary>
    private static List<DestinationRow> Branch(FsTree tree, DestinationQuery q, int id, int depth, Search? search, List<int> needs)
    {
        var rows = new List<DestinationRow>();
        var n = tree[id];
        var state = Listing.Of(n);
        var filtering = search != null;
        if (state is ListingState.Unlisted or ListingState.NotScanned)
        {
            needs.Add(id);
            if (!filtering) rows.Add(Note($"n{id}", depth, "listing…"));
            return rows;
        }

        var subs = n.Children.Where(c => (tree[c].IsDirectory || tree[c].Kind == NodeKind.Link) && q.Visible(tree[c]))
            .OrderBy(c => tree[c].Name, NaturalNameOrder.Instance).ToList();
        var shown = 0;
        foreach (var c in subs)
        {
            if (shown >= q.MaxBranchRows) break;
            var block = FolderRows(tree, q, c, depth, search, needs);
            if (block.Count == 0) continue;
            rows.AddRange(block);
            shown++;
        }

        if (filtering) return rows;
        if (subs.Count > shown) rows.Add(Note($"n{id}-more", depth, $"… {subs.Count - shown:N0} more: type to filter"));
        if (subs.Count == 0)
        {
            var note = state switch
            {
                ListingState.Pending or ListingState.Listing => "listing…",
                ListingState.Denied => "can't look inside: access denied",
                ListingState.Failed or ListingState.Unavailable or ListingState.Cloud => $"can't look inside: {Listing.Describe(state)}",
                ListingState.Empty => "empty",
                _ => n.DirectFileCount > 0 ? $"no folders inside · {n.DirectFileCount:N0} file{(n.DirectFileCount == 1 ? "" : "s")}" : "no folders inside",
            };
            rows.Add(Note($"n{id}", depth, note, warning: state is ListingState.Denied or ListingState.Failed or ListingState.Unavailable));
        }
        else if (state is ListingState.Partial or ListingState.Pending or ListingState.Listing)
        {
            rows.Add(Note($"n{id}-partial", depth, state == ListingState.Partial ? "partly listed" : "still listing…"));
        }

        return rows;
    }

    private static void AddFavourites(List<DestinationRow> rows, DestinationQuery q, string filter)
    {
        var filtering = filter.Length > 0;
        var list = q.Favourites.Where(f => !filtering || f.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) || f.Path.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (list.Count == 0 && filtering) return;
        var open = filtering || !q.Expanded.Contains("!fav");
        var pins = q.Favourites.Count(f => f.Kind == FavouriteKind.Pinned);
        var repos = q.Favourites.Count - pins;
        var detail = list.Count == 0 ? "pin a folder (P) · repositories appear as you explore" : $"{pins:N0} pinned · {repos:N0} repositor{(repos == 1 ? "y" : "ies")}";
        rows.Add(new DestinationRow("fav", DestinationRowKind.Section, -1, 0, "★ Favourites & repositories", detail, CanExpand: list.Count > 0, Expanded: open && list.Count > 0));
        if (!open) return;
        foreach (var f in list.Take(q.MaxFavouriteRows))
        {
            var key = f.Kind == FavouriteKind.Pinned ? $"p:{f.Path}" : $"r{f.Id}";
            var where = ParentText(f.Path);
            rows.Add(new DestinationRow(key, DestinationRowKind.Favourite, f.Id, 1, f.Name,
                (f.Kind == FavouriteKind.Pinned ? "pinned" : "repository") + (where.Length > 0 ? " · " + where : ""),
                IsHere: f.Id >= 0 && f.Id == q.Current, IsRepository: f.Kind == FavouriteKind.Repository, Path: f.Path));
        }

        if (list.Count > q.MaxFavouriteRows) rows.Add(Note("more-fav", 1, $"… {list.Count - q.MaxFavouriteRows:N0} more: type to filter"));
    }

    /// <summary>A short "in …" for a favourite: its parent folder's name, so two repositories with one name can be told apart.</summary>
    private static string ParentText(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var cut = trimmed.LastIndexOfAny(['\\', '/']);
        if (cut <= 0) return "";
        var parent = trimmed[..cut].TrimEnd('\\', '/');
        var p2 = parent.LastIndexOfAny(['\\', '/']);
        return "in " + (p2 >= 0 ? parent[(p2 + 1)..] : parent);
    }

    private static DestinationRow Note(string key, int depth, string text, bool warning = false) =>
        new(key, DestinationRowKind.Note, -1, depth, text, "", Warning: warning);

    /// <summary>What a folder row says before it is opened: its own counts, or the state of its listing.</summary>
    public static string Detail(FsNode n, ListingState state)
    {
        if (n.Kind == NodeKind.Link) return "link";
        switch (state)
        {
            case ListingState.Unlisted or ListingState.NotScanned: return "not listed yet";
            case ListingState.Pending or ListingState.Listing: return "listing…";
            case ListingState.Empty: return "empty";
            case ListingState.Denied or ListingState.Failed or ListingState.Unavailable or ListingState.Cloud: return Listing.Describe(state);
        }

        var dirs = n.DirectDirCount + n.DirectLinkCount;
        var files = n.DirectFileCount;
        var parts = new List<string>(2);
        if (dirs > 0) parts.Add($"{dirs:N0} folder{(dirs == 1 ? "" : "s")}");
        if (files > 0) parts.Add($"{files:N0} file{(files == 1 ? "" : "s")}");
        var text = parts.Count == 0 ? "empty" : string.Join(" · ", parts);
        return state == ListingState.Partial ? text + " · partly listed" : text;
    }

    /// <summary>A compact size for a file row (B, KiB, MiB, GiB; binary units as the rest of FileScape).</summary>
    private static string SizeText(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KiB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MiB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GiB",
    };
}

/// <summary>
/// A multi-hop trip (PLANET-40, AD-56): the folders to fly through, one level at a time, from where you are to a
/// descendant chosen in the Destinations tree, and the check made before each hop.
/// </summary>
public static class TripPlan
{
    /// <summary>
    /// The hops from <paramref name="from"/> down to <paramref name="to"/>: the child of <paramref name="from"/> on the
    /// way, then each level, ending with <paramref name="to"/>. Null when <paramref name="to"/> is not a folder strictly
    /// below <paramref name="from"/>.
    /// </summary>
    public static List<int>? Hops(FsTree tree, int from, int to)
    {
        if (!tree.Contains(from) || !tree.Contains(to) || from == to || !tree[to].IsDirectory) return null;
        var hops = new List<int>();
        for (var id = to; id >= 0; id = tree[id].ParentId)
        {
            if (id == from)
            {
                hops.Reverse();
                return hops;
            }

            hops.Add(id);
        }

        return null;
    }

    /// <summary>Why the hop from <paramref name="at"/> to <paramref name="next"/> cannot be made now, or null when it can.</summary>
    public static string? Unavailable(FsTree tree, int at, int next)
    {
        if (!tree.Contains(next)) return "it is no longer known";
        var n = tree[next];
        if (n.ParentId != at) return "it is no longer inside the folder you reached";
        if (!n.IsDirectory) return "it is no longer a folder";
        if ((n.Flags & NodeFlags.Vanished) != 0) return "it is no longer there";
        if ((n.Flags & NodeFlags.Unavailable) != 0) return "its location cannot be reached";
        return null;
    }
}
