namespace FileScape.Core.Planets;

/// <summary>Where the gallery's looking stands (PLANET-159, AD-94): walking, the small preview beside the world, or the large viewer.</summary>
public enum BrowseMode
{
    /// <summary>Nothing is being looked at: the world is the game's.</summary>
    Walking,

    /// <summary>The small preview panel shows one file beside the world (E, a click on a picture); play goes on under it.</summary>
    Preview,

    /// <summary>The large picture viewer covers the window (R, ⤢, a double-click on a picture, a click on the preview's picture).</summary>
    Viewer,
}

/// <summary>What a pointer gesture on the world landed on, as the browsing rule tells them apart.</summary>
public enum BrowseTarget
{
    /// <summary>Empty ground, sky or scenery: nothing selectable.</summary>
    Nothing,

    /// <summary>A picture standing on this world (an easel, a canvas): in-game inspection is its first meaning.</summary>
    Picture,

    /// <summary>Any other file standing here (a document, a sound, code).</summary>
    File,

    /// <summary>A folder or link: a pad, a world in the sky, a card over the bench.</summary>
    Place,
}

/// <summary>A gesture the rule answers: on the world (clicks), from the keyboard (E, R, Enter, Esc) or in the viewer.</summary>
public enum BrowseGesture
{
    /// <summary>A left click released without a drag.</summary>
    Click,

    /// <summary>The second press of a double-click (its first press already arrived as a <see cref="Click"/>).</summary>
    DoubleClick,

    /// <summary>E on the thing in focus (or the twirl's preview): look closer, or put the preview away when it is already on it.</summary>
    Inspect,

    /// <summary>R, the preview's ⤢ / "View large" button, or a click on the preview's picture: the large viewer.</summary>
    Expand,

    /// <summary>Enter or the "Open with Windows ↗" button: hand the file to Windows (the only way to leave FileScape for it).</summary>
    OpenWithWindows,

    /// <summary>Esc (or the panel's own close button): the topmost look closes, one level at a time.</summary>
    Close,

    /// <summary>Anything that travels (a launch, a path, a view switch): every look closes, the preview does not come back.</summary>
    Travel,
}

/// <summary>What the app does for one answer of the rule; several may apply in order (see <see cref="BrowseStep.Effects"/>).</summary>
public enum BrowseEffect
{
    /// <summary>Select the target in the session (the selection card and the ring follow).</summary>
    Select,

    /// <summary>Show the target in the small preview (replacing what it showed).</summary>
    ShowPreview,

    /// <summary>Put the small preview away.</summary>
    ClosePreview,

    /// <summary>Open the large viewer on the target (stepping through its folder's pictures).</summary>
    OpenViewer,

    /// <summary>Close the large viewer; the preview comes back on the last picture looked at (not after <see cref="BrowseGesture.Travel"/>).</summary>
    CloseViewer,

    /// <summary>Open the target with Windows through the session's one Open path (its confirmations apply).</summary>
    OpenWithWindows,

    /// <summary>Go to the folder or link (the departure planner's launch, AD-84).</summary>
    GoThere,

    /// <summary>The gesture is used up by the rule (nothing else may act on it: no selection behind a dismissal, AD-83).</summary>
    Consume,
}

/// <summary>Where browsing stands: the mode and the file it shows (−1 while walking).</summary>
public readonly record struct BrowseState(BrowseMode Mode, int Shown)
{
    public static readonly BrowseState Walking = new(BrowseMode.Walking, -1);

    public static BrowseState Previewing(int id) => new(BrowseMode.Preview, id);

    public static BrowseState Viewing(int id) => new(BrowseMode.Viewer, id);
}

/// <summary>One answer of the rule: the state after the gesture, and what the app does for it, in order.</summary>
public readonly record struct BrowseStep(BrowseState Next, IReadOnlyList<BrowseEffect> Effects)
{
    public bool Has(BrowseEffect e) => Effects.Contains(e);

    public static BrowseStep Stay(BrowseState s) => new(s, []);
}

/// <summary>
/// The gallery's browsing as one rule (PLANET-159..164, AD-94): approach → look (the small preview) → view large (the
/// viewer) → step → back to the preview → walk on, with every gesture's meaning written once. PlanetWorld asks it for
/// every click on the world, every E / R / Enter / Esc that concerns a file, every walking frame while the preview is
/// open, and the viewer's close and steps; it carries out the effects in order and nothing else.
/// <para>
/// Why a rule in Core: before it, each gesture's meaning lived in a different handler, and they disagreed — a click on
/// another easel selected it while the preview went on showing the first picture; a double-click on a picture launched
/// Windows' Photos while R inspected in-game; E toggled but a click never closed. DJ (seventh pass): "make approaching,
/// inspecting, expanding, browsing, closing, and returning to walking feel like one coherent experience … keep external
/// opening visibly distinct from in-game inspection".
/// </para>
/// <para>
/// The decisions (AD-94): a click on a picture looks at it (selects it and previews it, from walking or with the preview
/// on another file); a double-click on a picture views it large in-game — never Windows; for every other file a
/// double-click is still the deliberate Open (AD-57); Windows opens a file only on Enter or the button labelled
/// "Open with Windows ↗"; the preview always shows the selection — a click on another file moves it there, a click on a
/// folder, a world or empty ground puts it away (the click on empty ground does nothing else); Esc closes one level at a
/// time (viewer → preview → walking); travel closes everything.
/// </para>
/// Preserve: Windows is reached only through <see cref="BrowseGesture.OpenWithWindows"/> or a double-click on a file that
/// is not a picture; nothing here reads a file (the app's preview and viewer keep their bounded loading, AD-86); the
/// preview never shows something other than the selection after an answer.
/// </summary>
public static class GalleryBrowse
{
    private static readonly BrowseEffect[] LookAt = [BrowseEffect.Select, BrowseEffect.ShowPreview];

    /// <summary>
    /// The answer to <paramref name="gesture"/> on <paramref name="target"/> (node <paramref name="id"/>; −1 for
    /// <see cref="BrowseTarget.Nothing"/>) while browsing stands at <paramref name="state"/>. Gestures in the viewer
    /// (<see cref="InViewer"/>) and walking (<see cref="OnWalk"/>) have their own entry points.
    /// </summary>
    public static BrowseStep On(BrowseState state, BrowseGesture gesture, BrowseTarget target, int id)
    {
        if (gesture == BrowseGesture.Travel) return Travel(state);
        if (state.Mode == BrowseMode.Viewer) return InViewer(state, gesture, id);
        var previewing = state.Mode == BrowseMode.Preview;
        switch (gesture)
        {
            case BrowseGesture.Click:
                return target switch
                {
                    BrowseTarget.Picture => previewing && state.Shown == id ? BrowseStep.Stay(state) : new(BrowseState.Previewing(id), LookAt),
                    // Any file: the preview follows the selection when it is open; otherwise a click only selects (the card).
                    BrowseTarget.File => previewing ? new(BrowseState.Previewing(id), LookAt) : new(state, [BrowseEffect.Select]),
                    BrowseTarget.Place => previewing ? new(BrowseState.Walking, [BrowseEffect.Select, BrowseEffect.ClosePreview]) : new(state, [BrowseEffect.Select]),
                    // Empty ground beside an open preview: the click puts it away and does nothing else (AD-83's outside click).
                    _ => previewing ? new(BrowseState.Walking, [BrowseEffect.ClosePreview, BrowseEffect.Consume]) : BrowseStep.Stay(state),
                };
            case BrowseGesture.DoubleClick:
                return target switch
                {
                    BrowseTarget.Picture => new(BrowseState.Viewing(id), [BrowseEffect.Select, BrowseEffect.ClosePreview, BrowseEffect.OpenViewer]),
                    BrowseTarget.File => new(state, [BrowseEffect.Select, BrowseEffect.OpenWithWindows]), // AD-57: the deliberate Open
                    BrowseTarget.Place => new(BrowseState.Walking, previewing ? [BrowseEffect.Select, BrowseEffect.ClosePreview, BrowseEffect.GoThere] : [BrowseEffect.Select, BrowseEffect.GoThere]),
                    _ => BrowseStep.Stay(state),
                };
            case BrowseGesture.Inspect:
                if (target is not (BrowseTarget.Picture or BrowseTarget.File)) return BrowseStep.Stay(state);
                return previewing && state.Shown == id ? new(BrowseState.Walking, [BrowseEffect.ClosePreview]) : new(BrowseState.Previewing(id), LookAt);
            case BrowseGesture.Expand:
                // Only a picture opens the viewer; a text file's ⤢ is the reading view's (the app's own, unchanged).
                return previewing && target == BrowseTarget.Picture && state.Shown == id
                    ? new(BrowseState.Viewing(id), [BrowseEffect.ClosePreview, BrowseEffect.OpenViewer])
                    : BrowseStep.Stay(state);
            case BrowseGesture.OpenWithWindows:
                return id >= 0 && target is BrowseTarget.Picture or BrowseTarget.File ? new(state, [BrowseEffect.OpenWithWindows]) : BrowseStep.Stay(state);
            case BrowseGesture.Close:
                return previewing ? new(BrowseState.Walking, [BrowseEffect.ClosePreview]) : BrowseStep.Stay(state);
        }

        return BrowseStep.Stay(state);
    }

    /// <summary>
    /// A gesture while the viewer is open: Esc / R / ✕ / a click outside the picture close it back to the preview on the
    /// picture shown last (<paramref name="id"/>: the viewer's current picture), selected; Enter opens that picture with
    /// Windows and the viewer stays; a step selects the new picture (<see cref="Step"/>).
    /// </summary>
    public static BrowseStep InViewer(BrowseState state, BrowseGesture gesture, int id)
    {
        if (state.Mode != BrowseMode.Viewer) return BrowseStep.Stay(state);
        return gesture switch
        {
            BrowseGesture.Close or BrowseGesture.Expand => new(BrowseState.Previewing(id), [BrowseEffect.CloseViewer, BrowseEffect.Select, BrowseEffect.ShowPreview]),
            BrowseGesture.OpenWithWindows => new(state with { Shown = id }, [BrowseEffect.OpenWithWindows]),
            BrowseGesture.Travel => Travel(state),
            _ => BrowseStep.Stay(state), // clicks on the picture are the viewer's (zoom, pan): nothing reaches the world
        };
    }

    /// <summary>The viewer stepped to <paramref name="id"/>: the world selects it (its easel shows the ring).</summary>
    public static BrowseStep Stepped(BrowseState state, int id) =>
        state.Mode == BrowseMode.Viewer ? new(BrowseState.Viewing(id), [BrowseEffect.Select]) : BrowseStep.Stay(state);

    private static BrowseStep Travel(BrowseState state) => state.Mode switch
    {
        BrowseMode.Viewer => new(BrowseState.Walking, [BrowseEffect.CloseViewer, BrowseEffect.ClosePreview]),
        BrowseMode.Preview => new(BrowseState.Walking, [BrowseEffect.ClosePreview]),
        _ => BrowseStep.Stay(state),
    };

    /// <summary>
    /// The preview stays open while you walk if you have not gone this much farther from the file it shows than you were
    /// when it opened (or than the focus reach, whichever is more), metres along the ground: you can click an easel across
    /// the grove and walk up to it with its preview open; walking off puts it away (rationale for the exact slack: a stride
    /// and a half at walking speed, not tuned further).
    /// </summary>
    public const float PreviewKeepSlackMetres = 0.75f;

    /// <summary>What a walking frame tells the rule while a preview may be open.</summary>
    /// <param name="Focus">The thing in focus now (what E would act on), or −1.</param>
    /// <param name="FocusIsPicture">The focus is a picture standing on this world.</param>
    /// <param name="OnGallery">This world is a gallery (its canvases fill by themselves, AD-86).</param>
    /// <param name="ShownDistanceMetres">How far the previewed file is now, metres along the ground to its edge.</param>
    /// <param name="OpenedDistanceMetres">How far it was when the preview opened on it, metres.</param>
    /// <param name="ReachMetres">The focus reach, metres (PlanetWorld.FocusReachMetres).</param>
    public readonly record struct Walk(int Focus, bool FocusIsPicture, bool OnGallery, float ShownDistanceMetres, float OpenedDistanceMetres, float ReachMetres);

    /// <summary>
    /// A frame in which you moved (walked, strafed, jumped) with the preview open. In a gallery, walking past the easels
    /// with a picture's preview open moves the preview to the picture now in focus ("browse by walking": the canvases
    /// there are already loaded, so it reads nothing new beyond the preview's own bounded thumbnail); elsewhere a file is
    /// never read because you walked by it. The preview stays while you approach or stay by the file it shows, and closes
    /// once you have walked off (<see cref="PreviewKeepSlackMetres"/>).
    /// </summary>
    public static BrowseStep OnWalk(BrowseState state, Walk w)
    {
        if (state.Mode != BrowseMode.Preview) return BrowseStep.Stay(state);
        if (w.Focus == state.Shown) return BrowseStep.Stay(state);
        if (w.OnGallery && w.FocusIsPicture && w.Focus >= 0) return new(BrowseState.Previewing(w.Focus), LookAt);
        // A file that does not stand here one by one (in a pile, the bench's README): any move puts its preview away, as before.
        if (!float.IsFinite(w.ShownDistanceMetres)) return new(BrowseState.Walking, [BrowseEffect.ClosePreview]);
        var keep = MathF.Max(w.OpenedDistanceMetres, w.ReachMetres) + PreviewKeepSlackMetres;
        return w.ShownDistanceMetres > keep ? new(BrowseState.Walking, [BrowseEffect.ClosePreview]) : BrowseStep.Stay(state);
    }

    /// <summary>
    /// The picture <paramref name="delta"/> places from <paramref name="index"/> among <paramref name="count"/> (wrapping
    /// round the folder, as Photos does); <see cref="int.MinValue"/> is the first and <see cref="int.MaxValue"/> the last
    /// (Home, End). −1 when there are none.
    /// </summary>
    public static int Step(int index, int delta, int count)
    {
        if (count <= 0) return -1;
        if (delta == int.MinValue) return 0;
        if (delta == int.MaxValue) return count - 1;
        var i = index < 0 || index >= count ? 0 : index;
        return ((i + delta) % count + count) % count;
    }
}
