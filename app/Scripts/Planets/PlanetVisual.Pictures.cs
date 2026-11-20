using FileScape.Core.Interaction;
using FileScape.Core.Model;
using FileScape.Core.Planets;
using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// The pictures on a planet's canvases (PLANET-131..133, AD-86; this replaces PLANET-96's "two at a time, 32 at most, 192 px"
/// pump). Every picture or video object's face wears <c>picture_face.gdshader</c>: the picture fitted inside its mat with
/// its shape kept, or the canvas's state (waiting, a slow sheen while loading, a dim slash when it could not be shown).
/// <para>
/// On a gallery world (<see cref="PlanetComposition.Media"/>, or the app's own Starter Sky) the canvases fill by
/// themselves: a few times a second <see cref="PumpPictures"/> describes each picture to <see cref="ThumbnailPlan"/>
/// (distance from the camera, in view or not, what it shows) and starts what the plan says — a small thumbnail for every
/// picture in view, nearest first, then a sharp one for the nearest few — through <see cref="GalleryThumbnails"/>
/// (the session cache, then Windows' thumbnails, then FileScape's own decoder). Sharp ones left behind go back to small.
/// Elsewhere a canvas shows only a picture you looked at (<see cref="ShowThumbnail"/>, from the preview).
/// </para>
/// Preserve: Godot objects are touched on the main thread only (workers' answers are queued); every count is bounded by
/// <see cref="Budget"/>; a picture that failed is not asked again on this planet; non-gallery worlds read nothing by
/// themselves (FILE-02).
/// </summary>
public partial class PlanetVisual
{
    /// <summary>The bounds for this planet's pictures (<see cref="ThumbBudget"/>'s defaults: 2 in flight, 64 shown, 8 sharp).</summary>
    public static readonly ThumbBudget Budget = new();

    /// <summary>How often the plan is asked while nothing finishes, seconds (5 a second: walking 1 m between asks at a run;
    /// cheap for 64 pictures). A finished request asks again at once (measured: 62 canvases from Windows' cache took
    /// 5.9 s at one plan per 0.2 s, the cache answering in 8 ms).</summary>
    private const float PlanEverySeconds = 0.2f;

    /// <summary>The largest cloud-only picture fetched just to fill its canvas, bytes (8 MiB: the old automatic read
    /// limit; a 64-canvas world then fetches at most ≈ 0.5 GB, typically far less — photos are 1–5 MB).</summary>
    private const long AutoFetchMaxBytes = 8L * 1024 * 1024;

    private static Shader? _faceShader;

    /// <summary>One canvas: its material, what it shows and loads, the last failure, where its picture came from.</summary>
    private sealed class Canvas
    {
        public required ShaderMaterial Material;
        public ThumbTier Shown, Loading, Failed;
        public string? Refusal;
        public string Source = "";
        public Vector2I Pixels;
    }

    private readonly Dictionary<int, Canvas> _canvases = [];
    private readonly System.Collections.Concurrent.ConcurrentQueue<(int Id, ThumbTier Tier, GalleryThumbnails.Answer Answer)> _answers = new();
    private float _planIn;
    private List<ThumbSlot> _lastSlots = [];

    /// <summary>Where a picture's canvas middle is, world metres (probes).</summary>
    public Vector3 CanvasPosition(int id) => _items.TryGetValue(id, out var e) ? e.Mesh.GlobalTransform * new Vector3(0, 0.65f, 0) : Vector3.Zero;

    /// <summary>The planner's last view of a picture (probes: why it is or is not sharp).</summary>
    public string PlanDebug(int id) => _lastSlots.FirstOrDefault(s => s.Id == id) is { Id: > 0 } s || _lastSlots.Any(x => x.Id == id)
        ? System.FormattableString.Invariant($"{_lastSlots.First(x => x.Id == id)}")
        : "not planned";

    /// <summary>Pictures asked for by themselves on this planet (probes), canvases ever showing a picture (probes, diagnostics),
    /// and the most in flight, shown and sharp at once (the bounds AD-86 reports).</summary>
    public int AutoThumbnailsRequested { get; private set; }

    public int ThumbnailsShown { get; private set; }

    public int PeakInFlight { get; private set; }

    public int PeakShown { get; private set; }

    public int PeakSharp { get; private set; }

    /// <summary>Canvases now showing a picture, sharp ones, ones that could not be shown, and their textures' bytes (with mipmaps).</summary>
    public int CanvasesShown => _canvases.Values.Count(c => c.Shown != ThumbTier.None);

    public int CanvasesSharp => _canvases.Values.Count(c => c.Shown == ThumbTier.Large);

    public int CanvasesFailed => _canvases.Values.Count(c => c.Failed == ThumbTier.Small);

    public long TextureBytes => _canvases.Values.Where(c => c.Shown != ThumbTier.None).Sum(c => (long)c.Pixels.X * c.Pixels.Y * 4 * 4 / 3);

    /// <summary>What a canvas shows ("none", "loading", "small", "sharp", "failed: why") and where it came from (probes, prompt).</summary>
    public (string State, string Source, string? Refusal) CanvasState(int id) =>
        !_canvases.TryGetValue(id, out var c) ? ("none", "", null)
        : c.Shown == ThumbTier.Large ? ("sharp", c.Source, null)
        : c.Shown == ThumbTier.Small ? (c.Loading != ThumbTier.None ? "sharpening" : "small", c.Source, null)
        : c.Loading != ThumbTier.None ? ("loading", "", null)
        : c.Failed != ThumbTier.None ? ("failed", "", c.Refusal)
        : ("waiting", "", null);

    /// <summary>The face material for a picture or video object (one each, so each canvas has its own picture).</summary>
    private ShaderMaterial FaceMaterial(FileArt art)
    {
        _faceShader ??= GD.Load<Shader>("res://Shaders/picture_face.gdshader");
        var m = new ShaderMaterial { Shader = _faceShader };
        m.SetShaderParameter("face_aspect", art.FaceSize.Y > 0 ? art.FaceSize.X / art.FaceSize.Y : 1f);
        m.SetShaderParameter("state", 0f);
        return m;
    }

    /// <summary>True for an object whose canvas can show its file: a picture, or (in a gallery) a video's frame.</summary>
    private static bool ShowsPicture(FsNode n, FileArt art) =>
        art.FaceSurface >= 0 && n.Kind == NodeKind.File && (GalleryRules.IsPicture(n.Name) || FileCategories.Of(n.Name) == FileCategory.Video);

    private Canvas? CanvasFor(int id)
    {
        if (_canvases.TryGetValue(id, out var c)) return c;
        if (!_items.TryGetValue(id, out var e) || e.Art.FaceSurface < 0) return null;
        c = new Canvas { Material = FaceMaterial(e.Art) };
        e.Mesh.SetSurfaceOverrideMaterial(e.Art.FaceSurface, c.Material);
        _canvases[id] = c;
        return c;
    }

    private void Apply(int id, Canvas c, Image img, ThumbTier tier, string source)
    {
        c.Material.SetShaderParameter("picture", ImageTexture.CreateFromImage(img));
        c.Material.SetShaderParameter("picture_aspect", img.GetHeight() > 0 ? img.GetWidth() / (float)img.GetHeight() : 1f);
        c.Material.SetShaderParameter("state", 2f);
        if (c.Shown == ThumbTier.None) ThumbnailsShown++;
        c.Shown = tier;
        c.Source = source;
        c.Pixels = new Vector2I(img.GetWidth(), img.GetHeight());
    }

    private static void SetState(Canvas c)
    {
        var state = c.Shown != ThumbTier.None ? 2f : c.Loading != ThumbTier.None ? 1f : c.Failed != ThumbTier.None ? 3f : 0f;
        c.Material.SetShaderParameter("state", state);
        if (state < 2f) c.Material.SetShaderParameter("picture", default(Variant));
    }

    /// <summary>A picture read deliberately (E, the preview) is shown on its canvas too, as a small one.</summary>
    public void ShowThumbnail(int id, Image img)
    {
        if (CanvasFor(id) is not { } c || c.Shown == ThumbTier.Large) return;
        Apply(id, c, img, ThumbTier.Small, "preview");
    }

    /// <summary>
    /// Per frame on the planet you stand on (main thread): applies finished answers, then — when
    /// <paramref name="automatic"/> (a gallery, or the Starter Sky) — asks the plan what to load next.
    /// <paramref name="camera"/> gives the view; distances are from <paramref name="traveller"/> (the walker's feet: the
    /// chase camera trails a few metres behind, so the picture you stand at is the near one, not the camera's).
    /// </summary>
    public void PumpPictures(FsTree tree, float dt, bool automatic, Camera3D camera, Vector3 traveller)
    {
        while (_answers.TryDequeue(out var a))
        {
            _planIn = 0; // a request finished: its slot is free now, so plan again this frame (not up to 0.2 s later)
            if (CanvasFor(a.Id) is not { } c) continue;
            c.Loading = ThumbTier.None;
            if (a.Answer.Image is { } img)
            {
                if (tree.Contains(a.Id)) GalleryThumbnails.Keep(tree, tree[a.Id], a.Tier == ThumbTier.Large ? Budget.LargePixels : Budget.SmallPixels, img);
                if (a.Tier >= c.Shown) Apply(a.Id, c, img, a.Tier, a.Answer.Source);
            }
            else
            {
                c.Failed = a.Tier;
                c.Refusal = a.Answer.Refusal;
                SetState(c);
            }
        }

        if (!automatic) return;
        _planIn -= dt;
        if (_planIn > 0) return;
        _planIn = PlanEverySeconds;
        var slots = new List<ThumbSlot>();
        foreach (var (id, e) in _items)
        {
            if (id < 0 || !tree.Contains(id) || !ShowsPicture(tree[id], e.Art)) continue;
            var at = e.Mesh.GlobalTransform * new Vector3(0, 0.65f, 0); // the canvas's middle, in the object's own metres
            var c = _canvases.GetValueOrDefault(id);
            slots.Add(new ThumbSlot(id, traveller.DistanceTo(at), camera.IsPositionInFrustum(at), c?.Shown ?? ThumbTier.None, c?.Loading ?? ThumbTier.None, c?.Failed ?? ThumbTier.None));
        }

        _lastSlots = slots;
        foreach (var id in ThumbnailPlan.Release(slots, Budget))
        {
            var c = _canvases[id];
            if (GalleryThumbnails.Cached(tree, tree[id], Budget.SmallPixels) is { } small) Apply(id, c, small, ThumbTier.Small, "cache");
            else { c.Shown = ThumbTier.None; SetState(c); }
        }

        foreach (var id in ThumbnailPlan.Evict(slots, Budget))
        {
            var c = _canvases[id];
            c.Shown = ThumbTier.None;
            SetState(c);
        }

        foreach (var (id, tier) in ThumbnailPlan.Next(slots, Budget))
        {
            var n = tree[id];
            var c = CanvasFor(id)!;
            var pixels = tier == ThumbTier.Large ? Budget.LargePixels : Budget.SmallPixels;
            if (GalleryThumbnails.Cached(tree, n, pixels) is { } hit)
            {
                Apply(id, c, hit, tier, "cache");
                continue;
            }

            c.Loading = tier;
            if (c.Shown == ThumbTier.None) SetState(c);
            AutoThumbnailsRequested++;
            var video = FileCategories.Of(n.Name) == FileCategory.Video;
            // A cloud-only picture Windows has no thumbnail of is fetched for its canvas only when small (DJ allowed
            // downloads; walking past a gallery of large photos must not pull gigabytes): larger ones wait for E or R.
            var mayFetch = GalleryRules.Verdict(n) != GalleryRules.PictureVerdict.Download || n.Size <= AutoFetchMaxBytes;
            _ = GalleryThumbnails.RequestAsync(tree, n, pixels, decode: !video && mayFetch).ContinueWith(t => _answers.Enqueue((id, tier, t.IsCompletedSuccessfully ? t.Result : new GalleryThumbnails.Answer(null, "none", 0, 0, "it could not be read", 0))));
        }

        PeakInFlight = Math.Max(PeakInFlight, _canvases.Values.Count(c => c.Loading != ThumbTier.None));
        PeakShown = Math.Max(PeakShown, _canvases.Values.Count(c => c.Shown != ThumbTier.None || c.Loading != ThumbTier.None));
        PeakSharp = Math.Max(PeakSharp, _canvases.Values.Count(c => c.Shown == ThumbTier.Large || c.Loading == ThumbTier.Large));
    }
}
