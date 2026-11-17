using FileScape.App.Places;
using FileScape.App.Visual;
using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The planet's Destinations list (J, or ☰ Destinations; PLANET-13, PLANET-39..41, AD-56): one dense tree of where you
/// can go from here. Its rows are the way back, the ★ favourites and repositories (pins and the repositories found so
/// far, anywhere), every world here — each expandable in place, level after level, its children listed on demand — and
/// the files here in a group. A pile or the telescope still opens the same panel as a flat list of one kind of file.
/// <para>
/// Why an inline tree and not hover submenus (AD-56): rows open with a click on ▸ or with → and close with ←, nothing
/// opens or closes because the pointer drifted, a long branch stays one scrollable column, and the filter and the
/// keyboard work on the whole of it. Rows come from <see cref="DestinationTree"/> (pure, Core); this file owns the
/// Godot <see cref="Tree"/>, the keyboard and the mouse, and asks the scanner to list exactly the folders the player
/// opens (<see cref="Core.Scanning.WorldExplorer.Expand"/>).
/// </para>
/// <para>
/// Commit (Enter, double-click, Go): a world here launches as before; a folder deeper down starts a trip
/// (<see cref="StartTrip"/>, hop by hop); Shift+Enter or "Straight there" warps directly; a favourite warps (or is
/// reached by its path when it is not in the tree yet); a file here is approached. Every commit dismisses the
/// navigation dialogs first (<see cref="IWorldSession.DismissNavigationDialogs"/>), so nothing covers the flight.
/// </para>
/// <para>
/// The keyboard (PLANET-51, AD-62): opening puts the filter in edit mode with the keyboard; while the list is open it owns
/// every key no control used (<see cref="ListKey"/>, called before any shortcut), so typing never fires a game shortcut,
/// even after a click on the world; in the results, Backspace edits the filter and Home/End/Page keys move, never travel.
/// </para>
/// Preserve: the panel is rebuilt at most every <see cref="RebuildIntervalSeconds"/> and only when its rows change, with
/// the open branches and the scroll kept (the selection too, unless the filter changed), so it stays usable while
/// scanning and discovery go on; the keyboard contract the walkthrough and the OS probes use (J, type, ↓, Enter) is
/// unchanged; no key typed while the list is open reaches a shortcut except J (when the filter is not focused), Esc,
/// Ctrl/Alt chords and the F keys.
/// </summary>
public partial class PlanetWorld
{
    private LineEdit _contentsFilter = null!;
    private ItemList _contentsList = null!;
    private Tree _destTree = null!;
    private Label _contentsTitle = null!, _contentsHint = null!;
    private Button _goButton = null!, _warpButton = null!;
    private readonly List<int> _contentsIds = [];
    private FileCategory? _contentsCategory;

    /// <summary>Row keys the player opened (and <c>!files</c> / <c>!fav</c> for groups closed that are open by default).</summary>
    private readonly HashSet<string> _destExpanded = [];
    private List<DestinationRow> _destRows = [];
    private string _destSignature = "";
    private double _destRebuiltAt = -1;
    private int _destTreeVersion = -1;
    private int _destHere;
    private bool _destBuilding, _destDirty;

    /// <summary>The list's rows rebuild at most this often while it is open, seconds: a growing scan or discovery shows within a few frames, and a rebuild never runs every frame (rationale for 0.25 s: by eye).</summary>
    private const double RebuildIntervalSeconds = 0.25;

    /// <summary>The flat list (a pile, the telescope) shows at most this many rows (the previous list's limit; the title then says to type to filter, and that text says "5,000": change both together).</summary>
    private const int MaxListRows = 5000;

    // ---- density (PLANET-39; by eye at UI scale 1)
    /// <summary>The rows' font size, pixels (the previous list used 16 with 18 px icons: about 27 px a row; now about 20).</summary>
    private const int DestRowFontSize = 13;

    /// <summary>The detail column's font size and minimum width, pixels.</summary>
    private const int DestDetailFontSize = 11, DestDetailColumnPixels = 132;

    /// <summary>Space between rows and the indent per level, pixels: tight rows, a deep branch still fits the panel.</summary>
    private const int DestRowGapPixels = 1, DestIndentPixels = 13;

    /// <summary>The list's top edge, viewport units: at least 150 (below the toolbar and place bar), else 10 below the place bar.</summary>
    private const float ListTopPixels = 150f, ListGapPixels = 10f;

    /// <summary>The keyboard route under the tree (PLANET-51): typing, into the results, opening folders, going.</summary>
    private const string TreeHint = "Type to filter · ↓ or Tab: into the results · ↑↓ choose · → opens a folder, ← closes · Enter goes (Shift+Enter: straight there) · Backspace edits the filter · Esc closes";

    private static readonly Color DestDetailColor = new(0.62f, 0.68f, 0.84f);
    private static readonly Color DestWarningColor = new(1f, 0.72f, 0.5f);
    private static readonly Color DestNoteColor = new(0.66f, 0.72f, 0.86f);
    private static readonly Color DestFavouriteColor = new(1f, 0.85f, 0.45f);
    private static readonly Color DestRepositoryColor = new(0.5f, 0.95f, 0.85f);
    private static readonly Color DestHereColor = new(1f, 0.95f, 0.7f);

    public bool ListOpen => _contentsPanel.Visible;

    /// <summary>Entries of this place the list reaches (folders, links and files that pass the filter; or the flat list's rows).</summary>
    public int ListCount => _contentsCategory != null ? _contentsIds.Count : _destHere;

    /// <summary>The rows as built (probes).</summary>
    public IReadOnlyList<DestinationRow> DestinationRows => _destRows;

    /// <summary>Folders the list asked the scanner to list because the player opened them (probes: nothing else is asked for).</summary>
    public readonly List<int> ExpandRequests = [];

    private void BuildContentsPanel(Control root)
    {
        _contentsPanel = new PanelContainer { Visible = false };
        _contentsPanel.AddThemeStyleboxOverride("panel", PanelStyle());
        _contentsPanel.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        _contentsPanel.OffsetLeft = 12; _contentsPanel.OffsetRight = 452; _contentsPanel.OffsetTop = 150; _contentsPanel.OffsetBottom = -70;
        root.AddChild(_contentsPanel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 5);
        _contentsPanel.AddChild(v);
        var head = new HBoxContainer();
        _contentsTitle = MakeLabel(16, new Color(1f, 0.93f, 0.75f), HorizontalAlignment.Left, outline: 0);
        _contentsTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(_contentsTitle);
        var close = new Button { Text = "×", Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Close (J or Esc). What you opened is kept." };
        close.Pressed += CloseContents;
        head.AddChild(close);
        _contentsFilter = new LineEdit { PlaceholderText = "Type to filter · ↓ or Tab: the results · Enter: go", ClearButtonEnabled = true };
        _contentsFilter.AddThemeFontSizeOverride("font_size", 13);
        _contentsFilter.TextChanged += _ => OnFilterChanged();
        _contentsFilter.GuiInput += OnFilterKey;

        _contentsList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SelectMode = ItemList.SelectModeEnum.Single, FixedIconSize = new Vector2I(16, 16), Visible = false };
        _contentsList.AddThemeFontSizeOverride("font_size", DestRowFontSize);
        _contentsList.ItemSelected += i => { if (i >= 0 && i < _contentsIds.Count) { _s.Select(_contentsIds[(int)i]); _audio.Select(); } };
        _contentsList.ItemActivated += i =>
        {
            if (i < 0 || i >= _contentsIds.Count) return;
            var id = _contentsIds[(int)i];
            CloseContents();
            Activate(id, sky: false);
        };

        _destTree = new Tree
        {
            HideRoot = true, Columns = 2, SelectMode = Tree.SelectModeEnum.Row, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            ScrollHorizontalEnabled = false, AllowReselect = true, FocusMode = Control.FocusModeEnum.All,
        };
        _destTree.SetColumnExpand(0, true);
        _destTree.SetColumnExpand(1, false);
        _destTree.SetColumnCustomMinimumWidth(1, DestDetailColumnPixels);
        _destTree.AddThemeFontSizeOverride("font_size", DestRowFontSize);
        _destTree.AddThemeConstantOverride("v_separation", DestRowGapPixels);
        _destTree.AddThemeConstantOverride("item_margin", DestIndentPixels);
        _destTree.AddThemeConstantOverride("draw_relationship_lines", 1);
        _destTree.AddThemeColorOverride("relationship_line_color", new Color(0.5f, 0.55f, 0.75f, 0.35f));
        _destTree.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        _destTree.ItemSelected += OnDestinationSelected;
        _destTree.ItemActivated += () => CommitDestination(Godot.Input.IsKeyPressed(Key.Shift));
        _destTree.ItemCollapsed += OnDestinationCollapsed;
        _destTree.GuiInput += OnTreeKey;

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 6);
        _goButton = new Button { Text = "Go (Enter)", FocusMode = Control.FocusModeEnum.None, TooltipText = "Go to the chosen row: a world here launches; a folder deeper down is a trip, one hop at a time (Esc stops it)." };
        _goButton.Pressed += () => CommitDestination(direct: false);
        _warpButton = new Button { Text = "Straight there (Shift+Enter)", FocusMode = Control.FocusModeEnum.None, TooltipText = "Warp straight to the chosen folder, without the hops." };
        _warpButton.Pressed += () => CommitDestination(direct: true);
        buttons.AddChild(_goButton);
        buttons.AddChild(_warpButton);

        _contentsHint = new Label { Text = TreeHint, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _contentsHint.AddThemeFontSizeOverride("font_size", 11);
        _contentsHint.AddThemeColorOverride("font_color", new Color(0.7f, 0.76f, 0.9f));
        v.AddChild(head);
        v.AddChild(_contentsFilter);
        v.AddChild(_destTree);
        v.AddChild(_contentsList);
        v.AddChild(buttons);
        v.AddChild(_contentsHint);
    }

    // ------------------------------------------------------------------ opening and closing

    /// <summary>J (or ☰ Destinations): the list, open or closed.</summary>
    public void ToggleContents()
    {
        if (_contentsPanel.Visible && _contentsCategory == null) CloseContents();
        else OpenContents(null);
    }

    /// <summary>Closes the list and gives the keyboard back to walking (what was opened in the tree is kept for next time).</summary>
    public void CloseContents()
    {
        _contentsPanel.Visible = false;
        _contentsFilter.ReleaseFocus();
        _destTree.ReleaseFocus();
        _contentsList.ReleaseFocus();
        _s.Hud.SetDestinationsButton(false);
    }

    /// <summary>Opens the Destinations tree (<paramref name="category"/> null) or a flat list of one kind of file, with an empty filter that has keyboard focus.</summary>
    private void OpenContents(FileCategory? category)
    {
        _contentsCategory = category;
        _contentsFilter.Text = "";
        ClosePreview();
        _contentsPanel.Visible = true;
        _destTree.Visible = category == null;
        _contentsList.Visible = category != null;
        _goButton.GetParent<Control>().Visible = category == null;
        _contentsHint.Text = category == null
            ? TreeHint
            : "Click: select (the card shows it) · double-click or Enter: stand by it · Esc closes";
        if (category != null) FillContents();
        else RebuildDestinations(force: true);
        FocusFilter();
        _s.Hud.SetDestinationsButton(category == null);
    }

    /// <summary>
    /// The keyboard goes to the filter, editing, caret at the end (PLANET-51): the first letter typed after J or the ☰
    /// button lands there. Godot's LineEdit only takes typed text while in edit mode, so edit mode is entered explicitly
    /// rather than relying on GrabFocus doing it "for compatibility".
    /// </summary>
    private void FocusFilter()
    {
        _contentsFilter.GrabFocus();
        if (!_contentsFilter.IsEditing()) _contentsFilter.Edit();
        _contentsFilter.CaretColumn = _contentsFilter.Text.Length;
    }

    /// <summary>The filter is typed into from elsewhere (a key that reached the list's catch-all or the tree): the
    /// text is appended (or, for Backspace, its last character removed) and the keyboard returns to the filter.</summary>
    private void TypeIntoFilter(string? text)
    {
        FocusFilter();
        if (text == null)
        {
            if (_contentsFilter.Text.Length > 0) _contentsFilter.Text = _contentsFilter.Text[..^1]; // Text= does not emit TextChanged
            OnFilterChanged();
        }
        else _contentsFilter.InsertTextAtCaret(text); // emits TextChanged
        _contentsFilter.CaretColumn = _contentsFilter.Text.Length;
    }

    /// <summary>
    /// The filter's text changed: the rows follow it, and a row chosen before the change is let go, so Enter from the
    /// filter goes to the best match for what is typed now, never to a row chosen for an earlier filter (found 2026-11-17:
    /// "Lantern" then Enter started a trip to Constellations, a row left selected from before).
    /// </summary>
    private void OnFilterChanged()
    {
        if (_contentsCategory != null) FillContents();
        else
        {
            _destTree.DeselectAll();
            RebuildDestinations(force: true);
        }
    }

    /// <summary>From the filter or a row: the keyboard moves into the results, on the best match (the tree) or the first row (a flat list).</summary>
    private void FocusResults()
    {
        if (_contentsCategory != null)
        {
            if (_contentsList.ItemCount == 0) return;
            _contentsList.GrabFocus();
            if (_contentsList.GetSelectedItems().Length == 0) _contentsList.Select(0);
            return;
        }

        if (FirstSelectable(preferMatch: true) is not { } first) return;
        _destTree.GrabFocus();
        first.Select(0);
        _destTree.ScrollToItem(first);
    }

    /// <summary>
    /// While the list is open it owns the keys that reach the game's shortcuts (AD-62, PLANET-51): from
    /// <see cref="UnhandledInput"/>, before any shortcut. A typed character goes into the filter (whatever had the
    /// keyboard: a click on the world, the flat list); Backspace edits the filter; ↓ or Tab moves into the results;
    /// Enter goes to the chosen row; PageUp, Home and the other keys that would move you are kept from the game. J (to
    /// close it), Esc, Ctrl/Alt chords and the F keys still reach the game. Returns true when the key was used.
    /// </summary>
    private bool ListKey(InputEvent e)
    {
        if (!_contentsPanel.Visible || e is not InputEventKey { Pressed: true } k) return false;
        if (k.CtrlPressed || k.AltPressed || k.MetaPressed) return false;
        if (k.Keycode is Key.Escape || k.Keycode is >= Key.F1 and <= Key.F12) return false;
        if (k.PhysicalKeycode == Key.J && !_contentsFilter.HasFocus()) return false; // J closes the list (ToggleContents)
        switch (k.Keycode)
        {
            case Key.Backspace:
                TypeIntoFilter(null);
                return true;
            case Key.Down or Key.Tab:
                FocusResults();
                return true;
            case Key.Up:
                FocusFilter();
                return true;
            case Key.Enter or Key.KpEnter:
                if (_contentsCategory != null)
                {
                    var sel = _contentsList.GetSelectedItems();
                    if (sel.Length > 0 && sel[0] < _contentsIds.Count) { var id = _contentsIds[sel[0]]; CloseContents(); Activate(id, sky: false); }
                    return true;
                }

                if (SelectedRow() == null && FirstSelectable(preferMatch: true) is { } top) top.Select(0);
                CommitDestination(k.ShiftPressed);
                return true;
        }

        if (k.Unicode >= 32)
        {
            TypeIntoFilter(char.ConvertFromUtf32((int)k.Unicode));
            return true;
        }

        return true; // Home, PageUp, Delete, arrows…: never a shortcut while the list is open
    }

    /// <summary>Lists one kind of file here (a pile, the telescope), filtered, up to <see cref="MaxListRows"/>.</summary>
    private void FillContents()
    {
        var tree = _s.Tree;
        if (tree == null || _current == null) return;
        var folder = tree[_current.FolderId];
        var filter = _contentsFilter.Text.Trim();
        _contentsIds.Clear();
        _contentsList.Clear();
        var rows = folder.Children.Select(c => tree[c]).Where(_s.IsVisible)
            .Where(n => _contentsCategory == null || (n.Kind == NodeKind.File && FileCategories.Of(n.Name) == _contentsCategory))
            .Where(n => filter.Length == 0 || n.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.IsDirectory || n.Kind == NodeKind.Link ? 0 : 1).ThenBy(n => n.Name, NaturalNameOrder.Instance)
            .Take(MaxListRows)
            .ToList();
        foreach (var n in rows)
        {
            var i = _contentsList.AddItem($"{n.Name}    {SizeFormat.Short(n.Size)}", FileIcons.Get(FileIcons.For(n)));
            _contentsList.SetItemTooltip(i, tree.FullPath(n.Id));
            _contentsIds.Add(n.Id);
        }

        var what = _contentsCategory is { } c ? PlaceText.CategoryName(c).ToLowerInvariant() : "files";
        _contentsTitle.Text = $"{tree.DisplayName(_current.FolderId)} · {rows.Count:N0} {what}" + (rows.Count >= MaxListRows ? " (first 5,000: type to filter)" : "") + (folder.IsListingIncomplete ? " · partly listed" : "");
    }

    // ------------------------------------------------------------------ the tree

    /// <summary>Per frame while the list is open: rebuild when the tree or the favourites changed (throttled), and keep discovery going.</summary>
    private void UpdateDestinationsPanel()
    {
        if (!_contentsPanel.Visible) return;
        // Below the place bar, which grows while the scan status shows (it covered the list's title before).
        _contentsPanel.OffsetTop = Mathf.Max(ListTopPixels, _s.Hud.PlaceBarBottom + ListGapPixels);
        if (_contentsCategory != null) return;
        var tree = _s.Tree;
        if (tree == null) return;
        var now = _s.Now.TotalSeconds;
        if (now - _destRebuiltAt < RebuildIntervalSeconds) return;
        if (_destTreeVersion == tree.Version && !_destDirty && !_s.FavouritesChanged) return;
        RebuildDestinations(force: false);
    }

    /// <summary>
    /// Builds the rows (Core) and, when they differ from what is shown, the Godot tree: the selection, the open branches
    /// (they are the rows' own state) and the scroll position survive. Folders opened but never listed are asked for here.
    /// </summary>
    private void RebuildDestinations(bool force)
    {
        var tree = _s.Tree;
        if (tree == null || _current == null || _contentsCategory != null) return;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            Rebuild(tree, force);
        }
        finally
        {
            var ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            // The first build of a session includes compiling this code (JIT): kept apart so the steady cost is what is judged.
            if (DestinationBuilds++ == 0) FirstDestinationBuildMs = ms;
            else MaxDestinationBuildMs = Math.Max(MaxDestinationBuildMs, ms);
            if (ms > 8) GD.Print(FormattableString.Invariant($"[destinations] rebuild {ms:0.0} ms, {_destRows.Count} rows"));
        }
    }

    /// <summary>The session's first rebuild, milliseconds (it includes JIT compilation).</summary>
    public double FirstDestinationBuildMs { get; private set; }

    /// <summary>For probes: rebuilds so far and the slowest, milliseconds (rows, favourites and the Godot tree together).</summary>
    public int DestinationBuilds { get; private set; }

    public double MaxDestinationBuildMs { get; set; }

    private void Rebuild(FsTree tree, bool force)
    {
        _destRebuiltAt = _s.Now.TotalSeconds;
        _destTreeVersion = tree.Version;
        _destDirty = false;
        var (favs, repos) = _s.Favourites();
        var list = DestinationTree.Build(tree, new DestinationQuery
        {
            Current = _current!.FolderId,
            Expanded = _destExpanded,
            Filter = _contentsFilter.Text,
            Visible = _s.IsVisible,
            Favourites = favs,
            Repositories = repos,
        });
        foreach (var id in list.NeedsListing) RequestListing(id);
        _destHere = list.HereCount;
        var folder = tree[_current.FolderId];
        _contentsTitle.Text = $"{tree.DisplayName(_current.FolderId)} · {list.HereCount:N0} {(_contentsFilter.Text.Trim().Length > 0 ? "matching" : "worlds and files")}" + (folder.IsListingIncomplete ? " · partly listed" : "");
        var sig = string.Join('\n', list.Rows.Select(r => $"{r.Key}|{r.Name}|{r.Detail}|{r.Expanded}|{r.CanExpand}|{r.IsHere}"));
        if (!force && sig == _destSignature) return;
        _destSignature = sig;
        _destRows = list.Rows.ToList();
        FillDestinationTree(tree);
    }

    /// <summary>Asks the scanner to list a folder the player opened (once; only in the connected world).</summary>
    private void RequestListing(int id)
    {
        if (ExpandRequests.Contains(id)) return;
        ExpandRequests.Add(id);
        _s.Explorer?.Expand(id);
    }

    private void FillDestinationTree(FsTree tree)
    {
        var selectedKey = SelectedRow()?.Key;
        var scroll = TreeScrollBar()?.Value ?? 0;
        _destBuilding = true;
        _destTree.Clear();
        var root = _destTree.CreateItem();
        var parents = new List<TreeItem> { root };
        TreeItem? toSelect = null;
        for (var i = 0; i < _destRows.Count; i++)
        {
            var r = _destRows[i];
            // parents[d] is the newest row at depth d − 1 (parents[0] the hidden root): a row hangs under the last row one level up.
            while (parents.Count > r.Depth + 1) parents.RemoveAt(parents.Count - 1);
            var item = _destTree.CreateItem(parents[^1]);
            parents.Add(item);
            item.SetMetadata(0, i);
            StyleRow(tree, item, r);
            if (r.CanExpand)
            {
                if (!r.Expanded) _destTree.CreateItem(item).SetMetadata(0, -1); // a placeholder, so the ▸ shows before the branch is built
                item.Collapsed = !r.Expanded;
            }

            if (r.Key == selectedKey) toSelect = item;
        }

        _destBuilding = false;
        if (toSelect != null)
        {
            _destBuilding = true;
            toSelect.Select(0);
            _destBuilding = false;
        }

        if (TreeScrollBar() is { } bar) bar.Value = scroll;
    }

    /// <summary>One row's text, icon, colours and tooltip.</summary>
    private void StyleRow(FsTree tree, TreeItem item, DestinationRow r)
    {
        var name = r.Name;
        var nameColor = new Color(0.9f, 0.93f, 1f);
        var detail = r.Detail;
        Texture2D? icon = null;
        var tip = "";
        switch (r.Kind)
        {
            case DestinationRowKind.WayBack:
                icon = FileIcons.Get(IconKind.Up);
                nameColor = WayBackListColor;
                tip = "The way back: the launcher behind the arrival meadow (U).";
                break;
            case DestinationRowKind.Section:
                nameColor = DestFavouriteColor;
                tip = "Folders you pinned (P on a folder's card) and the repositories FileScape has found so far, anywhere. Enter or double-click goes straight there.";
                break;
            case DestinationRowKind.Favourite:
                icon = FileIcons.Get(r.IsRepository ? IconKind.Code : IconKind.Folder);
                nameColor = r.IsRepository ? DestRepositoryColor : DestFavouriteColor;
                if (!r.IsRepository) name = "★ " + name;
                if (r.IsHere) detail = "you are here";
                tip = (r.Path ?? "") + (r.Id < 0 ? "\nNot reached yet this session: going there finds it by its path." : "");
                break;
            case DestinationRowKind.Folder or DestinationRowKind.Link:
            {
                var n = tree[r.Id];
                icon = FileIcons.Get(FileIcons.For(n));
                nameColor = r.Kind == DestinationRowKind.Link ? new Color(0.6f, 0.92f, 0.95f) : PlanetArt.Palette(PlanetComposer.ThemeOf(tree, r.Id, _s.IsVisible)).Accent.Lightened(0.25f);
                if (r.Kind == DestinationRowKind.Link) detail = PlaceText.LinkShort(tree, n);
                var mark = _s.ProminenceOf(r.Id) is { IsProminent: true } p ? p.Mark + " " : "";
                name = mark + name;
                if (r.IsRepository) detail = "repository · " + detail;
                var how = r.Depth == 0
                    ? (_current!.Comp.Launchers.Any(l => l.TargetId == r.Id) ? "A launcher on this planet." : "A world in this sky.")
                    : $"{r.Depth + 1} levels down: Enter flies there hop by hop (Shift+Enter: straight there).";
                tip = $"{how}\n{tree.FullPath(r.Id)}";
                break;
            }
            case DestinationRowKind.FilesGroup:
                icon = FileIcons.Get(IconKind.Document);
                nameColor = new Color(0.85f, 0.88f, 0.98f);
                tip = "The files here. Enter or double-click a file: stand by it.";
                break;
            case DestinationRowKind.File:
            {
                var n = tree[r.Id];
                icon = FileIcons.Get(FileIcons.For(n));
                nameColor = new Color(0.86f, 0.89f, 0.97f);
                tip = tree.FullPath(r.Id);
                break;
            }
            case DestinationRowKind.Note:
                nameColor = r.Warning ? DestWarningColor : DestNoteColor;
                item.SetSelectable(0, false);
                item.SetSelectable(1, false);
                break;
        }

        if (r.IsHere && r.Kind != DestinationRowKind.Favourite) detail = "you are here";
        item.SetText(0, name);
        item.SetTextOverrunBehavior(0, TextServer.OverrunBehavior.TrimEllipsis);
        item.SetCustomColor(0, r.IsHere ? DestHereColor : nameColor);
        if (icon != null)
        {
            item.SetIcon(0, icon);
            item.SetIconMaxWidth(0, 15);
        }

        item.SetText(1, detail);
        item.SetTextAlignment(1, HorizontalAlignment.Right);
        item.SetTextOverrunBehavior(1, TextServer.OverrunBehavior.TrimEllipsis);
        item.SetCustomFontSize(1, DestDetailFontSize);
        item.SetCustomColor(1, r.Warning ? DestWarningColor : DestDetailColor);
        if (tip.Length > 0)
        {
            item.SetTooltipText(0, tip);
            item.SetTooltipText(1, tip);
        }
    }

    /// <summary>The internal vertical scroll bar of the tree (to keep the scroll position across a rebuild).</summary>
    private VScrollBar? TreeScrollBar() => _destTree.GetChildren(includeInternal: true).OfType<VScrollBar>().FirstOrDefault();

    private DestinationRow? RowOf(TreeItem? item)
    {
        if (item == null) return null;
        var i = item.GetMetadata(0).AsInt32();
        return i >= 0 && i < _destRows.Count ? _destRows[i] : null;
    }

    private DestinationRow? SelectedRow() => RowOf(_destTree.GetSelected());

    private void OnDestinationSelected()
    {
        if (_destBuilding) return;
        if (SelectedRow() is { Id: >= 0, Kind: DestinationRowKind.Folder or DestinationRowKind.Link or DestinationRowKind.File or DestinationRowKind.WayBack } r && _s.Tree is { } tree && tree.Contains(r.Id))
        {
            _s.Select(r.Id);
            _audio.Select();
        }
    }

    /// <summary>▸ clicked (or → / ← below): remember the branch as open or closed and rebuild after this signal.</summary>
    private void OnDestinationCollapsed(TreeItem item)
    {
        if (_destBuilding || RowOf(item) is not { CanExpand: true } r) return;
        SetExpanded(r, !item.Collapsed);
    }

    private void SetExpanded(DestinationRow r, bool open)
    {
        var defaultOpen = r.Key is "files" or "fav";
        if (defaultOpen)
        {
            _destExpanded.Remove(open ? "!" + r.Key : r.Key);
            _destExpanded.Add(open ? r.Key : "!" + r.Key);
        }
        else if (open) _destExpanded.Add(r.Key);
        else _destExpanded.Remove(r.Key);

        if (open) _audio.Select();
        Callable.From(() => RebuildDestinations(force: true)).CallDeferred(); // never rebuild the tree inside its own signal
    }

    // ------------------------------------------------------------------ keyboard

    /// <summary>The filter's keys: ↓ moves into the list (its first row), Enter goes to the first row, Esc closes.</summary>
    private void OnFilterKey(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } k) return;
        switch (k.Keycode)
        {
            case Key.Escape:
                CloseContents();
                _contentsFilter.AcceptEvent();
                break;
            case Key.Down or Key.Tab:
                // Into the results (PLANET-51): Tab as well as ↓, so the way from typing to choosing is the key people try.
                if (_contentsCategory != null && _contentsList.ItemCount > 0) _contentsList.Select(0);
                FocusResults();
                _contentsFilter.AcceptEvent();
                break;
            case Key.Enter or Key.KpEnter when _contentsCategory == null:
                if (SelectedRow() == null && FirstSelectable(preferMatch: true) is { } top) top.Select(0);
                CommitDestination(k.ShiftPressed);
                _contentsFilter.AcceptEvent();
                break;
        }
    }

    /// <summary>
    /// The tree's own keys, before its defaults: → opens a branch (or steps into it), ← closes it (or steps to its
    /// parent), Shift+Enter goes straight there, Esc closes, ↑ on the first row returns to the filter, and typing goes
    /// to the filter.
    /// </summary>
    private void OnTreeKey(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } k) return;
        var item = _destTree.GetSelected();
        var r = RowOf(item);
        switch (k.Keycode)
        {
            case Key.Right when item != null && r != null:
                if (r.CanExpand && !r.Expanded) SetExpanded(r, true);
                else if (r.Expanded && item.GetFirstChild() is { } child && RowOf(child) is { Kind: not DestinationRowKind.Note }) { child.Select(0); _destTree.ScrollToItem(child); }
                _destTree.AcceptEvent();
                return;
            case Key.Left when item != null && r != null:
                if (r.Expanded) SetExpanded(r, false);
                else if (item.GetParent() is { } p && RowOf(p) != null) { p.Select(0); _destTree.ScrollToItem(p); }
                _destTree.AcceptEvent();
                return;
            case Key.Enter or Key.KpEnter when k.ShiftPressed:
                CommitDestination(direct: true);
                _destTree.AcceptEvent();
                return;
            case Key.Escape:
                CloseContents();
                _destTree.AcceptEvent();
                return;
            case Key.Up when item != null && item == FirstSelectable():
            case Key.Tab:
                FocusFilter();
                _destTree.AcceptEvent();
                return;
            case Key.Backspace:
                // Editing the filter from the results, never "back" (PLANET-51: it reached History back before).
                TypeIntoFilter(null);
                _destTree.AcceptEvent();
                return;
            case Key.Home or Key.End or Key.Pageup or Key.Pagedown:
                // Moving within the results (PageUp and Home reached Up and Home-world before, 2026-11-17).
                MoveSelection(k.Keycode);
                _destTree.AcceptEvent();
                return;
        }

        if (k.Unicode >= 32 && !k.CtrlPressed && !k.AltPressed && k.Keycode is not (Key.Space or Key.Enter or Key.KpEnter))
        {
            // Typing in the tree types in the filter (as the list's type-ahead did, but across every row).
            TypeIntoFilter(char.ConvertFromUtf32((int)k.Unicode));
            _destTree.AcceptEvent();
        }
    }

    /// <summary>Home / End: the first / last row; PageUp / PageDown: a list's height of rows up or down (selectable rows only).</summary>
    private void MoveSelection(Key key)
    {
        var rows = new List<TreeItem>();
        for (var it = _destTree.GetRoot()?.GetFirstChild(); it != null; it = it.GetNextVisible())
        {
            if (RowOf(it) is { Kind: not DestinationRowKind.Note }) rows.Add(it);
        }

        if (rows.Count == 0) return;
        var at = Math.Max(0, rows.IndexOf(_destTree.GetSelected()!));
        var page = Math.Max(1, DestinationRowsThatFit - 1);
        var to = key switch { Key.Home => 0, Key.End => rows.Count - 1, Key.Pageup => at - page, _ => at + page };
        var target = rows[Math.Clamp(to, 0, rows.Count - 1)];
        target.Select(0);
        _destTree.ScrollToItem(target);
    }

    /// <summary>
    /// The first row the keyboard can land on. While filtering, the best matching row (a deep match comes after the folders
    /// that lead to it, and ↓ or Enter should reach the match, not its ancestor): a whole-name match first, then a name that
    /// starts with the filter, then the shallowest, then list order — so "lantern" picks Lantern Workshop here before
    /// Lantern Moth three levels down (real-input route, 2026-11-17: A→Z order alone flew to the deep one).
    /// </summary>
    private TreeItem? FirstSelectable(bool preferMatch = false)
    {
        var filter = _contentsFilter.Text.Trim();
        TreeItem? first = null, best = null;
        var bestScore = int.MaxValue;
        for (var it = _destTree.GetRoot()?.GetFirstChild(); it != null; it = it.GetNextVisible())
        {
            if (RowOf(it) is not { } r || r.Kind == DestinationRowKind.Note) continue;
            first ??= it;
            if (!preferMatch || filter.Length == 0) return first;
            if (r.Kind == DestinationRowKind.Section || !r.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)) continue;
            // Lower is better: whole name 0, prefix 1, elsewhere 2; then depth (a strict order, ties keep list order).
            var kind = string.Equals(r.Name, filter, StringComparison.CurrentCultureIgnoreCase) ? 0 : r.Name.StartsWith(filter, StringComparison.CurrentCultureIgnoreCase) ? 1 : 2;
            var score = kind * 1000 + Math.Min(r.Depth, 999);
            if (score < bestScore) { bestScore = score; best = it; }
        }

        return best ?? first;
    }

    // ------------------------------------------------------------------ commit

    /// <summary>
    /// Goes to the chosen row (see the class summary). <paramref name="direct"/>: straight there in one warp instead of
    /// a trip. A section or the files group opens or closes instead.
    /// </summary>
    private void CommitDestination(bool direct)
    {
        var tree = _s.Tree;
        if (tree == null || _current == null || SelectedRow() is not { } r) return;
        switch (r.Kind)
        {
            case DestinationRowKind.Section or DestinationRowKind.FilesGroup:
                if (r.CanExpand) SetExpanded(r, !r.Expanded);
                return;
            case DestinationRowKind.Note:
                return;
        }

        if (r.IsHere)
        {
            _s.Toast("You are already here.");
            return;
        }

        if (Busy) return;
        switch (r.Kind)
        {
            case DestinationRowKind.WayBack:
                CommitAndClose();
                _s.TravelUp();
                return;
            case DestinationRowKind.Favourite:
                CommitAndClose();
                if (r.Id >= 0 && tree.Contains(r.Id)) _s.TravelInto(r.Id);
                else if (r.Path != null) _s.GoToPath(r.Path);
                return;
            case DestinationRowKind.File:
                CommitAndClose();
                _s.Select(r.Id);
                Approach(r.Id);
                return;
            case DestinationRowKind.Link:
                CommitAndClose();
                _s.Select(r.Id);
                if (r.Depth == 0) LaunchToSelection();
                else _s.TravelInto(r.Id);
                return;
            case DestinationRowKind.Folder when r.Depth == 0:
                CommitAndClose();
                _s.Select(r.Id);
                LaunchToSelection();
                return;
            case DestinationRowKind.Folder:
                CommitAndClose();
                _s.Select(r.Id);
                if (direct) _s.TravelInto(r.Id);
                else StartTrip(r.Id);
                return;
        }
    }

    /// <summary>A destination was committed: the list and every navigation dialog go, so nothing covers the flight (PLANET-41).</summary>
    private void CommitAndClose()
    {
        CloseContents();
        _s.DismissNavigationDialogs();
    }
}

/// <summary>The Destinations tree as probes see it: row rectangles, the ▸ to click, the selection, density.</summary>
public partial class PlanetWorld
{
    private TreeItem? ItemOf(string key)
    {
        for (var it = _destTree.GetRoot()?.GetFirstChild(); it != null; it = it.GetNextVisible())
        {
            if (RowOf(it)?.Key == key) return it;
        }

        return null;
    }

    /// <summary>The row's rectangle on screen (viewport units), or an empty rectangle when it is not shown.</summary>
    public Rect2 DestinationRowRect(string key)
    {
        if (ItemOf(key) is not { } it) return new Rect2();
        var r = _destTree.GetItemAreaRect(it);
        return new Rect2(_destTree.GetGlobalRect().Position + r.Position, r.Size);
    }

    /// <summary>Where the row's ▸ is drawn: at the start of its indent (a click there opens or closes the branch).</summary>
    public Vector2 DestinationArrowPoint(string key)
    {
        if (ItemOf(key) is not { } it) return new Vector2(-1, -1);
        var r = DestinationRowRect(key);
        var depth = RowOf(it)!.Depth;
        return new Vector2(r.Position.X + depth * DestIndentPixels + DestIndentPixels / 2f, r.GetCenter().Y);
    }

    public string SelectedDestinationKey => SelectedRow()?.Key ?? "";

    /// <summary>The height of one row, pixels (viewport units): from the first two rows' tops.</summary>
    public float DestinationRowHeight
    {
        get
        {
            var a = FirstSelectable();
            var b = a?.GetNextVisible();
            return a == null || b == null ? 0 : _destTree.GetItemAreaRect(b).Position.Y - _destTree.GetItemAreaRect(a).Position.Y;
        }
    }

    /// <summary>How many rows fit in the list as it is laid out now.</summary>
    public int DestinationRowsThatFit => DestinationRowHeight > 0 ? (int)(_destTree.Size.Y / DestinationRowHeight) : 0;

    public bool DestinationTreeFocused => _destTree.HasFocus();

    /// <summary>The rows shown now, top to bottom (the real-input probe clicks them): name, the row's rectangle, its ▸ point.</summary>
    public IEnumerable<(string Name, Rect2 Rect, Vector2 Arrow)> VisibleDestinationRows()
    {
        if (!_contentsPanel.Visible) yield break;
        for (var it = _destTree.GetRoot()?.GetFirstChild(); it != null; it = it.GetNextVisible())
        {
            if (RowOf(it) is not { } row) continue;
            var rect = DestinationRowRect(row.Key);
            // Only rows inside the list's visible area (a row scrolled out of view has a rectangle off the panel).
            if (rect.Size.Y > 0 && _destTree.GetGlobalRect().HasPoint(rect.GetCenter())) yield return (row.Name, rect, DestinationArrowPoint(row.Key));
        }
    }
}

public partial class PlanetWorld
{
    /// <summary>The list's Go button on screen (probes).</summary>
    public Rect2 DestinationGoRect => _goButton.GetGlobalRect();

    /// <summary>The Destinations filter's text (probes: the first letter typed after J must land here).</summary>
    public string DestinationFilterText => _contentsFilter.Text;

    /// <summary>The Destinations filter has keyboard focus (probes and the state file).</summary>
    public bool DestinationFilterFocused => _contentsFilter.HasFocus();

    /// <summary>The filter is in Godot's edit mode (focused but not editing, it lets typed letters through to the shortcuts).</summary>
    public bool DestinationFilterEditing => _contentsFilter.IsEditing();
}
