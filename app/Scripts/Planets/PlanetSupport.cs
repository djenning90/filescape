using FileScape.Core.Planets;
using FileScape.Core.Shell;
using Godot;

namespace FileScape.App.Planets;

/// <summary>Words for the planetary world (kept short: the landscape should do most of the talking).</summary>
public static class PlanetText
{
    /// <summary>A world's short descriptive name by its theme ("a library world"), shown beside the folder's own name.</summary>
    public static string WorldName(PlanetTheme t) => t switch
    {
        PlanetTheme.Hearth => "a crossroads world",
        PlanetTheme.Meadow => "a picture meadow",
        PlanetTheme.Workshop => "a workshop world",
        PlanetTheme.Library => "a library world",
        PlanetTheme.Chime => "a world of bell flowers",
        PlanetTheme.Theatre => "a lantern theatre",
        PlanetTheme.Vault => "a vault world",
        PlanetTheme.Crystal => "a crystal world",
        PlanetTheme.Wild => "a wild garden",
        PlanetTheme.Quiet => "a quiet moon: empty",
        PlanetTheme.Veiled => "a veiled world",
        _ => "a world",
    };

    /// <summary>Extensions previewed as text (case-insensitive); anything else needs its contents checked or is not previewed.</summary>
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".rst", ".log", ".csv", ".json", ".yaml", ".yml", ".ini", ".toml", ".xml", ".html", ".css",
        ".cs", ".csproj", ".sln", ".py", ".js", ".ts", ".go", ".rs", ".c", ".h", ".cpp", ".java", ".gd", ".sh", ".ps1", ".bat",
        ".cmd", ".m3u", ".gitignore", ".env", ".cfg", ".conf", ".tsv", ".svg",
    };

    /// <summary>Shown as text in the preview (by extension; the contents are checked for binary bytes as they are read).</summary>
    public static bool LooksLikeText(string name) =>
        TextExtensions.Contains(Path.GetExtension(name)) || (Path.GetExtension(name).Length == 0 && name.StartsWith('.'));

    /// <summary>The first few thousand bytes of a text file (read-only, bounded, shared read), or why not.</summary>
    public static string ReadPreview(string path, int maxBytes)
    {
        var r = Readme.Read(path, maxBytes);
        if (r.Error != null) return r.Error;
        return r.Truncated ? r.Text + "\n…" : r.Text;
    }
}

/// <summary>
/// Picture thumbnails, read off the main thread (bounded size, read-only). The image is decoded and shrunk on a worker
/// thread; the texture is made on the main thread by the caller. Every read is bounded by bytes (the caller's limit) and
/// by pixels: since PLANET-96 the size in the picture's header is checked against <see cref="MaxDecodedPixels"/> before
/// decoding (<see cref="ImageHeader"/>), so a small file that would decode into a huge buffer is refused without decoding.
/// </summary>
public static class Thumbnails
{
    /// <summary>A picture larger than this many pixels (≈ 7746 × 7746) is refused: by its header before decoding (PLANET-96),
    /// or, for a header this cannot read, after decoding and before resizing. Not worth the memory (≈ 240 MB decoded).</summary>
    public const int MaxDecodedPixels = 60_000_000;

    /// <summary>A picture read, or why not (said to the player as it is: "too many pixels", "could not be decoded").</summary>
    public readonly record struct Result(Image? Image, int SourceWidth, int SourceHeight, string? Refusal);

    /// <summary>The read buffer, bytes (64 KiB: large sequential reads, few system calls).</summary>
    private const int ReadBufferBytes = 64 * 1024;

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> of a file, sharing it with writers and deleters (read-only, never
    /// blocking another program), or null if it is larger or cannot be read.
    /// </summary>
    public static byte[]? ReadBounded(string path, long maxBytes)
    {
        try
        {
            using var f = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, ReadBufferBytes, FileOptions.SequentialScan);
            if (f.Length > maxBytes) return null;
            var buf = new byte[f.Length];
            var n = 0;
            while (n < buf.Length && f.Read(buf, n, buf.Length - n) is var r and > 0) n += r;
            return n == buf.Length ? buf : buf[..n];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads and decodes a PNG, JPEG, WebP or BMP on a worker thread, shrunk so its longer side is at most
    /// <paramref name="maxPixels"/>, with mipmaps; null for a file over <paramref name="maxBytes"/> (24 MiB by default),
    /// an unknown format, a decoding error or a pathologically large picture. Never throws.
    /// </summary>
    public static Task<Image?> LoadAsync(string path, int maxPixels, long maxBytes = 24L * 1024 * 1024) => Task.Run(() => Load(path, maxPixels, maxBytes).Image);

    /// <summary>As <see cref="LoadAsync"/>, with the picture's own size and the reason when it is refused.</summary>
    public static Task<Result> LoadWithReasonAsync(string path, int maxPixels, long maxBytes) => Task.Run(() => Load(path, maxPixels, maxBytes));

    private static Result Load(string path, int maxPixels, long maxBytes)
    {
        try
        {
            if (ReadBounded(path, maxBytes) is not { } bytes) return new Result(null, 0, 0, "it could not be read here (too large, or not readable)");
            var size = ImageHeader.TryReadSize(bytes.AsSpan(0, Math.Min(bytes.Length, ImageHeader.ProbeBytes)));
            if (size is { } s && (long)s.Width * s.Height > MaxDecodedPixels)
                return new Result(null, s.Width, s.Height, $"it is {s.Width:N0} × {s.Height:N0} pixels, more than the {MaxDecodedPixels / 1_000_000} million FileScape decodes");
            var img = new Image();
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var err = ext switch
            {
                ".png" => img.LoadPngFromBuffer(bytes),
                ".jpg" or ".jpeg" => img.LoadJpgFromBuffer(bytes),
                ".webp" => img.LoadWebpFromBuffer(bytes),
                ".bmp" => img.LoadBmpFromBuffer(bytes),
                _ => Error.FileUnrecognized,
            };
            if (err != Error.Ok || img.IsEmpty()) return new Result(null, size?.Width ?? 0, size?.Height ?? 0, "it could not be decoded (damaged, or a kind of picture FileScape does not read)");
            var w = img.GetWidth();
            var h = img.GetHeight();
            if ((long)w * h > MaxDecodedPixels) return new Result(null, w, h, "it has more pixels than FileScape decodes"); // a pathological picture: not worth the memory
            var k = MathF.Min(1f, maxPixels / (float)Math.Max(w, h));
            if (k < 1f) img.Resize(Math.Max(1, (int)(w * k)), Math.Max(1, (int)(h * k)), Image.Interpolation.Bilinear);
            img.GenerateMipmaps();
            return new Result(img, w, h, null);
        }
        catch (Exception)
        {
            return new Result(null, 0, 0, "it could not be decoded");
        }
    }
}

/// <summary>
/// The route you fly, drawn as you fly it (research E3): a ribbon of fading light behind the traveller. The controller
/// calls <see cref="Begin"/> at a launch, <see cref="Add"/> each frame of the flight with the traveller's scale as the
/// width, <see cref="End"/> on landing and <see cref="Tick"/> every frame; the ribbon is rebuilt each tick, turned to face
/// the camera (additive, unlit, no shadow).
/// </summary>
public partial class FlightTrail : MeshInstance3D
{
    /// <summary>A new point is added only once the traveller has moved this many widths from the last one.</summary>
    private const float MinSpacingWidths = 0.35f;

    /// <summary>The most points kept (the oldest is dropped first).</summary>
    private const int MaxPoints = 400;

    /// <summary>How long a point lives while flying, seconds: the ribbon trails ≈ 3.5 s of the route behind you.</summary>
    private const float FlyingLifeSeconds = 3.5f;

    /// <summary>After landing every point's life is cut to this, seconds, so the ribbon clears soon after touchdown.</summary>
    private const float LandedLifeSeconds = 1.2f;

    /// <summary>Half the ribbon's width per unit of traveller scale (a slim 0.22 m ribbon at scale 1; see the note in <see cref="Tick"/>).</summary>
    private const float HalfWidthPerScale = 0.11f;

    /// <summary>Near the camera the ribbon vanishes: invisible within this many widths, fully visible after <see cref="NearFadeWidths"/> more.</summary>
    private const float NearHiddenWidths = 2f, NearFadeWidths = 4f;

    /// <summary>The ribbon's warm gold, and its opacity at the newest, farthest-from-camera point.</summary>
    private static readonly Color TrailGold = new(1f, 0.84f, 0.5f);

    private const float TrailPeakOpacity = 0.6f;

    private readonly List<(Vector3 P, float W, float Age)> _points = [];
    private ImmediateMesh _mesh = null!;
    private bool _flying;

    public override void _Ready()
    {
        _mesh = new ImmediateMesh();
        Mesh = _mesh;
        CastShadow = ShadowCastingSetting.Off;
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
    }

    /// <summary>Starts a new trail at the launch point (any previous trail is dropped).</summary>
    public void Begin(Vector3 at)
    {
        _points.Clear();
        _points.Add((at, 1f, 0f));
        _flying = true;
    }

    /// <summary>Adds the traveller's position during a flight; <paramref name="width"/> is its current scale. Ignored when not flying.</summary>
    public void Add(Vector3 at, float width)
    {
        if (!_flying) return;
        if (_points.Count > 0 && _points[^1].P.DistanceTo(at) < MinSpacingWidths * width) return;
        _points.Add((at, width, 0f));
        if (_points.Count > MaxPoints) _points.RemoveAt(0);
    }

    /// <summary>The flight is over: the remaining ribbon fades out over <see cref="LandedLifeSeconds"/>.</summary>
    public void End() => _flying = false;

    /// <summary>Ages the points, drops the expired ones and rebuilds the ribbon facing the current camera.</summary>
    public void Tick(float dt)
    {
        var lifeSeconds = _flying ? FlyingLifeSeconds : LandedLifeSeconds;
        for (var i = 0; i < _points.Count; i++) _points[i] = (_points[i].P, _points[i].W, _points[i].Age + dt);
        _points.RemoveAll(p => p.Age > lifeSeconds);
        _mesh.ClearSurfaces();
        if (_points.Count < 2) return;
        var cam = GetViewport().GetCamera3D();
        if (cam == null) return;
        _mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);
        for (var i = 0; i < _points.Count; i++)
        {
            var (p, w, age) = _points[i];
            var next = _points[Math.Min(i + 1, _points.Count - 1)].P;
            var prev = _points[Math.Max(i - 1, 0)].P;
            var along = (next - prev).Normalized();
            // A slim ribbon (2026-11-11, after watching recorded flights: at 0.32 m × scale it read as a broad beam once the
            // traveller grew into a parent's scale), fading out close to the camera so it never fills the view.
            var side = along.Cross((cam.GlobalPosition - p).Normalized()).Normalized() * (HalfWidthPerScale * w);
            var nearCamera = Mathf.Clamp((cam.GlobalPosition.DistanceTo(p) - NearHiddenWidths * w) / (NearFadeWidths * w), 0, 1);
            // Fades with age, towards the tail (the oldest point is index 0) and near the camera.
            var fade = Mathf.Clamp(1f - age / lifeSeconds, 0, 1) * (i / (float)_points.Count) * nearCamera;
            var c = TrailGold with { A = TrailPeakOpacity * fade };
            _mesh.SurfaceSetColor(c);
            _mesh.SurfaceAddVertex(p - side);
            _mesh.SurfaceSetColor(c);
            _mesh.SurfaceAddVertex(p + side);
        }

        _mesh.SurfaceEnd();
    }
}

/// <summary>
/// The landing ring: a quick bright ring that spreads over the ground where you touch down (<see cref="Burst"/>), then
/// fades; <see cref="Tick"/> must be called every frame. One instance is reused for every landing.
/// </summary>
public partial class LandingBurst : MeshInstance3D
{
    /// <summary>The whole burst, seconds: the ring spreads and fades over this, then hides.</summary>
    private const float DurationSeconds = 0.7f;

    /// <summary>The ring's radius at touchdown and how much it grows, metres (the unit torus is scaled: 0.6 m → 3.8 m, easing out).</summary>
    private const float StartRadiusMetres = 0.6f, SpreadMetres = 3.2f;

    /// <summary>The opacity at touchdown, falling linearly to 0.</summary>
    private const float PeakOpacity = 0.9f;

    /// <summary>Lift above the ground, metres, so the flat ring never flickers into the ground it lies on.</summary>
    private const float LiftMetres = 0.05f;

    private float _t = 99f; // seconds since the last burst; starts far past DurationSeconds, i.e. idle
    private StandardMaterial3D _mat = null!;

    public override void _Ready()
    {
        // A thin ring of radius 1 (scaled per frame in the ground plane only).
        Mesh = new TorusMesh { InnerRadius = 0.9f, OuterRadius = 1f, Rings = 48, RingSegments = 6 };
        _mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add };
        MaterialOverride = _mat;
        CastShadow = ShadowCastingSetting.Off;
        Visible = false;
    }

    /// <summary>Starts a burst of <paramref name="color"/> lying flat on the ground at <paramref name="at"/> (local up <paramref name="up"/>, unit length).</summary>
    public void Burst(Vector3 at, Vector3 up, Color color)
    {
        // Any tangent will do for a ring; fall back to another reference axis when up is nearly along ±X (the cross product vanishes).
        var x = up.Cross(Vector3.Right);
        if (x.LengthSquared() < 1e-4f) x = up.Cross(Vector3.Forward);
        x = x.Normalized();
        var z = x.Cross(up).Normalized();
        Transform = new Transform3D(new Basis(x, up, z), at + up * LiftMetres);
        _mat.AlbedoColor = color;
        _t = 0;
        Visible = true;
    }

    /// <summary>Advances the burst: the radius eases out (fast, then slowing: 1 − (1 − k)²) while the opacity falls linearly.</summary>
    public void Tick(float dt)
    {
        if (_t > DurationSeconds) { Visible = false; return; }
        _t += dt;
        var k = _t / DurationSeconds;
        var s = StartRadiusMetres + SpreadMetres * (1 - (1 - k) * (1 - k));
        Scale = new Vector3(s, 1, s);
        _mat.AlbedoColor = _mat.AlbedoColor with { A = PeakOpacity * (1 - k) };
    }
}
