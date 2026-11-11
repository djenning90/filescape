using FileScape.Core.Planets;
using FileScape.Core.Shell;
using Godot;

namespace FileScape.App.Planets;

/// <summary>Words for the planetary world (kept short: the landscape should do most of the talking).</summary>
public static class PlanetText
{
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
/// thread; the texture is made on the main thread by the caller.
/// </summary>
public static class Thumbnails
{
    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> of a file, sharing it with writers and deleters (read-only, never
    /// blocking another program), or null if it is larger or cannot be read.
    /// </summary>
    public static byte[]? ReadBounded(string path, long maxBytes)
    {
        try
        {
            using var f = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
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

    public static Task<Image?> LoadAsync(string path, int maxPixels, long maxBytes = 24L * 1024 * 1024) => Task.Run(() =>
    {
        try
        {
            if (ReadBounded(path, maxBytes) is not { } bytes) return null;
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
            if (err != Error.Ok || img.IsEmpty()) return null;
            if ((long)img.GetWidth() * img.GetHeight() > 60_000_000) return null; // a pathological picture: not worth the memory
            var w = img.GetWidth();
            var h = img.GetHeight();
            var k = MathF.Min(1f, maxPixels / (float)Math.Max(w, h));
            if (k < 1f) img.Resize(Math.Max(1, (int)(w * k)), Math.Max(1, (int)(h * k)), Image.Interpolation.Bilinear);
            img.GenerateMipmaps();
            return img;
        }
        catch (Exception)
        {
            return null;
        }
    });
}

/// <summary>The route you fly, drawn as you fly it (research E3): a ribbon of fading light behind the traveller.</summary>
public partial class FlightTrail : MeshInstance3D
{
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

    public void Begin(Vector3 at)
    {
        _points.Clear();
        _points.Add((at, 1f, 0f));
        _flying = true;
    }

    public void Add(Vector3 at, float width)
    {
        if (!_flying) return;
        if (_points.Count > 0 && _points[^1].P.DistanceTo(at) < 0.35f * width) return;
        _points.Add((at, width, 0f));
        if (_points.Count > 400) _points.RemoveAt(0);
    }

    public void End() => _flying = false;

    public void Tick(float dt)
    {
        for (var i = 0; i < _points.Count; i++) _points[i] = (_points[i].P, _points[i].W, _points[i].Age + dt);
        _points.RemoveAll(p => p.Age > (_flying ? 3.5f : 1.2f));
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
            var side = along.Cross((cam.GlobalPosition - p).Normalized()).Normalized() * (0.32f * w);
            var fade = Mathf.Clamp(1f - age / (_flying ? 3.5f : 1.2f), 0, 1) * (i / (float)_points.Count);
            var c = new Color(1f, 0.82f, 0.45f, 0.75f * fade);
            _mesh.SurfaceSetColor(c);
            _mesh.SurfaceAddVertex(p - side);
            _mesh.SurfaceSetColor(c);
            _mesh.SurfaceAddVertex(p + side);
        }

        _mesh.SurfaceEnd();
    }
}

/// <summary>The landing ring: a quick bright ring that spreads over the ground where you touch down.</summary>
public partial class LandingBurst : MeshInstance3D
{
    private float _t = 99f;
    private StandardMaterial3D _mat = null!;

    public override void _Ready()
    {
        Mesh = new TorusMesh { InnerRadius = 0.9f, OuterRadius = 1f, Rings = 48, RingSegments = 6 };
        _mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add };
        MaterialOverride = _mat;
        CastShadow = ShadowCastingSetting.Off;
        Visible = false;
    }

    public void Burst(Vector3 at, Vector3 up, Color color)
    {
        var x = up.Cross(Vector3.Right);
        if (x.LengthSquared() < 1e-4f) x = up.Cross(Vector3.Forward);
        x = x.Normalized();
        var z = x.Cross(up).Normalized();
        Transform = new Transform3D(new Basis(x, up, z), at + up * 0.05f);
        _mat.AlbedoColor = color;
        _t = 0;
        Visible = true;
    }

    public void Tick(float dt)
    {
        if (_t > 0.7f) { Visible = false; return; }
        _t += dt;
        var k = _t / 0.7f;
        var s = 0.6f + 3.2f * (1 - (1 - k) * (1 - k));
        Scale = new Vector3(s, 1, s);
        _mat.AlbedoColor = _mat.AlbedoColor with { A = 0.9f * (1 - k) };
    }
}
