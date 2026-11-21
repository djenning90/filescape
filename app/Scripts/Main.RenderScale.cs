using FileScape.Core.Interaction;
using Godot;

namespace FileScape.App;

/// <summary>
/// Main's part of the automatic 3D render scale (PLANET-180, AD-96): whenever the window's size or the quality changes,
/// the viewport draws its 3D world at <see cref="RenderScalePolicy.ScaleFor"/> of the window (1 up to 2560×1440; less for
/// larger windows, upscaled with FSR 1; the GUI and text always at full resolution), or at <c>--render-scale=</c>. The
/// choice is printed once per change and shown in the quality line, so it is never silent.
/// </summary>
public partial class Main
{
    private float? _requestedRenderScale;
    private float _appliedRenderScale = 1f;

    /// <summary>The 3D render scale now (probes, the quality line).</summary>
    public float RenderScale => _appliedRenderScale;

    private void ApplyRenderScale()
    {
        var vp = GetViewport();
        var size = DisplayServer.WindowGetSize();
        var scale = RenderScalePolicy.ScaleFor(size.X, size.Y, _requestedRenderScale);
        vp.Scaling3DMode = scale < 1f ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        vp.Scaling3DScale = scale;
        if (Mathf.IsEqualApprox(scale, _appliedRenderScale)) return;
        _appliedRenderScale = scale;
        GD.Print($"[render] 3D drawn at {scale * 100:0} % of a {size.X}×{size.Y} window ({(_requestedRenderScale != null ? "--render-scale" : "automatic: more than 2560×1440")})");
        _hud?.SetQuality(QualityLine);
    }

    /// <summary>The HUD's quality text: the level, and the 3D scale when it is not 100 %.</summary>
    private string QualityLine => _quality + (_appliedRenderScale < 0.999f ? $" · 3D {_appliedRenderScale * 100:0} %" : "");
}
