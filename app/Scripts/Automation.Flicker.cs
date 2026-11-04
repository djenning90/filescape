using System.Globalization;
using System.Text;
using FileScape.App.Visual;
using Godot;
using GEnvironment = Godot.Environment;

namespace FileScape.App;

/// <summary>
/// VIS-11 investigation: DJ saw a sun-like bright source and/or its reflection flash on and off while moving in
/// District. This script flies a fixed street-level path towards the sun's azimuth and measures, per frame, how
/// many pixels are bright and how many are *single-frame flashes* (bright now, dark in a 3×3 neighbourhood in both
/// the previous and the next frame). It repeats the path with one effect changed at a time, plus a stationary
/// control, so the cause can be isolated rather than assumed.
/// </summary>
public partial class Automation
{
    private const int FlickerFrames = 200;
    private const int SampleW = 320, SampleH = 180;
    private const float BrightThreshold = 0.82f;

    private async Task Flicker()
    {
        await WaitForScan(TimeSpan.FromSeconds(120));
        var district = _args.Visualization is null or 1;
        _main.SetVisualization(district ? 1 : 0);
        _main.SetQuality(Quality.High);
        var withSelection = _args.OpenTest is "selection" or "street" or "beamtop";
        var configs = new List<(string Name, Action Apply, bool Moving)>
        {
            // "pre-fix" restores the settings DJ saw before VIS-11 (sun specular 1.0, puddle roughness 0.04).
            ("pre-fix", () => { _main.Sun.LightSpecular = 1f; ((ShaderMaterial)_main.Ground.MaterialOverride).SetShaderParameter("puddle_roughness", 0.04f); }, true),
            ("baseline", () => { }, true),
            ("stationary", () => { }, false),
            ("ssr-off", () => Env().SsrEnabled = false, true),
            ("sun-disc-off", () => _main.Sun.SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly, true),
            ("sun-specular-0", () => _main.Sun.LightSpecular = 0, true),
            ("sun-hidden", () => _main.Sun.Visible = false, true),
            ("volumetric-fog-off", () => Env().VolumetricFogEnabled = false, true),
            ("volfog-reprojection-off", () => Env().VolumetricFogTemporalReprojectionEnabled = false, true),
            ("glow-off", () => Env().GlowEnabled = false, true),
            ("ssao-off", () => Env().SsaoEnabled = false, true),
            ("msaa-off", () => _main.GetViewport().Msaa3D = Viewport.Msaa.Disabled, true),
            ("sun-shadows-off", () => _main.Sun.ShadowEnabled = false, true),
            ("low-quality", () => _main.SetQuality(Quality.Low), true),
        };
        if (_args.OpenTest == "beamtop")
        {
            // The selection beam's top in view: the suspected NaN source (pow of a negative base at the top cap).
            configs =
            [
                ("pre-fix", () => { PreFix(); Beam(clamp: false); }, true),
                ("beam-unclamped-only", () => Beam(clamp: false), true),
                ("baseline", () => { }, true),
                ("stationary", () => { }, false),
                ("pre-fix-stationary", () => { PreFix(); Beam(clamp: false); }, false),
                ("glow-off-pre-fix", () => { PreFix(); Beam(clamp: false); Env().GlowEnabled = false; }, true),
                ("beam-off", () => ((ShaderMaterial)_main.SelectionBeam.MaterialOverride).SetShaderParameter("color", new Color(0, 0, 0, 0)), true),
            ];
        }
        else if (withSelection)
        {
            // Selection-specific suspects: the spotlight above the selection, its specular, the beam, puddle roughness.
            configs.Insert(2, ("spot-energy-0", () => _main.SelectionSpot.LightEnergy = 0, true));
            configs.Insert(3, ("spot-specular-0", () => _main.SelectionSpot.LightSpecular = 0, true));
            configs.Insert(4, ("beam-off", () => ((ShaderMaterial)_main.SelectionBeam.MaterialOverride).SetShaderParameter("color", new Color(0, 0, 0, 0)), true));
            configs.Insert(5, ("puddle-roughness-0.15", () => ((ShaderMaterial)_main.Ground.MaterialOverride).SetShaderParameter("puddle_roughness", 0.15f), true));
            configs.Insert(6, ("puddle-roughness-0.25", () => ((ShaderMaterial)_main.Ground.MaterialOverride).SetShaderParameter("puddle_roughness", 0.25f), true));
        }
        if (_args.StressDistrictOnly)
        {
            configs = configs.Take(2).ToList();
        }

        // Instrument: the HUD and 2D labels are hidden so only the 3D scene is measured.
        _main.Hud.Visible = false;
        _main.SetLabelsVisible(false);
        await SunView();
        var summary = new StringBuilder("config,moving,frames,mean_bright_px,max_bright_px,mean_flash_px,max_flash_px,frames_with_flash_ge_4,max_bright_jump,cell_spikes,frames_with_cell_spike,max_cell_deviation\n");
        Log($"flicker: vis={_main.ActiveVisualization.Name} selection={withSelection} sun_rot={_main.Sun.RotationDegrees} sun_energy={_main.Sun.LightEnergy} sky_mode={_main.Sun.SkyMode} sample={SampleW}x{SampleH} bright>{BrightThreshold}");
        foreach (var (name, apply, moving) in configs)
        {
            _main.SetQuality(Quality.High); // resets the environment, sun and MSAA to the shipped High state
            _main.Sun.Visible = true;
            _main.SelectionSpot.LightEnergy = 9;
            _main.SelectionSpot.LightSpecular = 0.15f;
            ((ShaderMaterial)_main.SelectionBeam.MaterialOverride).SetShaderParameter("color", _main.ActiveVisualization.SelectionColor with { A = 0.35f });
            Beam(clamp: true);
            if (withSelection && _main.Tree != null)
            {
                _main.Select(PathSelection());
            }

            apply();
            await Frames(10);
            var frames = new List<bool[]>(FlickerFrames);
            var cells = new List<float[]>(FlickerFrames);
            var bright = new List<int>(FlickerFrames);
            for (var i = 0; i < FlickerFrames; i++)
            {
                Pose(moving ? i : 0, withSelection);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var img = _main.GetViewport().GetTexture().GetImage();
                if (i == FlickerFrames / 2) img.SavePng($"{_out}/flicker-{name}-mid.png");
                img.Resize(SampleW, SampleH, Image.Interpolation.Bilinear);
                img.Convert(Image.Format.Rgb8);
                var data = img.GetData();
                var mask = new bool[SampleW * SampleH];
                var count = 0;
                for (var p = 0; p < mask.Length; p++)
                {
                    var l = (0.2126f * data[p * 3] + 0.7152f * data[p * 3 + 1] + 0.0722f * data[p * 3 + 2]) / 255f;
                    if (l > BrightThreshold) { mask[p] = true; count++; }
                }

                frames.Add(mask);
                bright.Add(count);
                cells.Add(CellLuminance(data));
            }

            var flashes = new int[FlickerFrames];
            var worst = 1;
            for (var i = 1; i < FlickerFrames - 1; i++)
            {
                var n = 0;
                for (var y = 2; y < SampleH - 2; y++)
                {
                    for (var x = 2; x < SampleW - 2; x++)
                    {
                        var p = y * SampleW + x;
                        if (frames[i][p] && !Near(frames[i - 1], x, y) && !Near(frames[i + 1], x, y)) n++;
                    }
                }

                flashes[i] = n;
                if (n > flashes[worst]) worst = i;
            }

            // Blob-scale spikes: a 16×9 grid of cells (80×80 px at 1280×720); a spike is a cell whose luminance differs
            // from the average of the previous and next frame by more than 0.06 — a flash of a large area, which
            // sub-pixel window twinkle averages away.
            var spikes = new int[FlickerFrames];
            float maxSpike = 0;
            for (var i = 1; i < FlickerFrames - 1; i++)
            {
                for (var c = 0; c < CellsX * CellsY; c++)
                {
                    var d = MathF.Abs(cells[i][c] - 0.5f * (cells[i - 1][c] + cells[i + 1][c]));
                    maxSpike = MathF.Max(maxSpike, d);
                    if (d > 0.06f) spikes[i]++;
                }
            }

            var jumps = bright.Zip(bright.Skip(1), (a, b) => Math.Abs(a - b)).DefaultIfEmpty(0).Max();
            var csv = new StringBuilder("frame,bright_px,flash_px\n");
            for (var i = 0; i < FlickerFrames; i++) csv.Append(CultureInfo.InvariantCulture, $"{i},{bright[i]},{flashes[i]}\n");
            System.IO.File.WriteAllText($"{_out}/flicker-{name}.csv", csv.ToString());
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{name},{moving},{FlickerFrames},{bright.Average():0.0},{bright.Max()},{flashes.Average():0.00},{flashes.Max()},{flashes.Count(f => f >= 4)},{jumps},{spikes.Sum()},{spikes.Count(x => x > 0)},{maxSpike:0.000}");
            summary.AppendLine(line);
            Log($"flicker {line} worst_frame={worst}");

            // Re-render the worst frame and its neighbours at full size, so the flash can be looked at.
            if (flashes[worst] >= 4)
            {
                for (var k = -1; k <= 1; k++)
                {
                    Pose(moving ? worst + k : 0, withSelection);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    _main.GetViewport().GetTexture().GetImage().SavePng($"{_out}/flicker-{name}-worst{(k < 0 ? "-prev" : k > 0 ? "-next" : "")}.png");
                }
            }
        }

        System.IO.File.WriteAllText($"{_out}/flicker-summary.csv", summary.ToString());
        _main.SetQuality(Quality.High);
        _main.Hud.Visible = true;
        _main.SetLabelsVisible(true);
    }

    private GEnvironment Env() => _main.World.Environment;

    private const int CellsX = 16, CellsY = 9;

    private static float[] CellLuminance(byte[] rgb)
    {
        var sums = new float[CellsX * CellsY];
        var cw = SampleW / CellsX;
        var ch = SampleH / CellsY;
        for (var y = 0; y < SampleH; y++)
        {
            for (var x = 0; x < SampleW; x++)
            {
                var p = (y * SampleW + x) * 3;
                var l = (0.2126f * rgb[p] + 0.7152f * rgb[p + 1] + 0.0722f * rgb[p + 2]) / 255f;
                sums[Math.Min(y / ch, CellsY - 1) * CellsX + Math.Min(x / cw, CellsX - 1)] += l;
            }
        }

        for (var i = 0; i < sums.Length; i++) sums[i] /= cw * ch;
        return sums;
    }

    private static bool Near(bool[] mask, int x, int y)
    {
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                if (mask[(y + dy) * SampleW + x + dx]) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A slow dolly over the middle of the layout, above the tallest towers (cap 14 u) so the camera never enters
    /// geometry (the first run's path did, which produced false "flashes"), heading towards the sun's azimuth and
    /// looking down at the wet ground, with a gentle pan.
    /// </summary>
    private void PathPose(int frame)
    {
        var b = _main.Layout!.Bounds;
        var towardSun = TowardSun();
        var yaw = Mathf.RadToDeg(Mathf.Atan2(-towardSun.X, -towardSun.Z));
        var start = Visualization.Gd(b.Center) with { Y = 0 } - towardSun * 30f;
        var t = frame / (float)FlickerFrames;
        var pos = start + towardSun * (t * 36f) + new Vector3(0, 17f, 0);
        var lookYaw = yaw + 10f * Mathf.Sin(t * Mathf.Tau);
        const float elevation = 24f; // looking down at the wet ground ahead
        _main.Rig.SetPose(pos, lookYaw, -elevation, 30f);
    }

    private void PreFix()
    {
        _main.Sun.LightSpecular = 1f;
        ((ShaderMaterial)_main.Ground.MaterialOverride).SetShaderParameter("puddle_roughness", 0.04f);
    }

    private void Beam(bool clamp) => ((ShaderMaterial)_main.SelectionBeam.MaterialOverride).SetShaderParameter("clamp_fade", clamp);

    private void Pose(int frame, bool orbitSelection)
    {
        if (_args.OpenTest == "beamtop" && _main.Selection.Selected >= 0)
        {
            // Far enough and low enough that the whole 80 u beam, including its top, is in frame while orbiting.
            var sb = _main.Layout!.NodeBoxes[_main.Selection.Selected];
            _main.Rig.SetDesired(Visualization.Gd(sb.Center) with { Y = 30 }, 120f, frame * 0.9f, 8f);
            _main.Rig.Snap();
            return;
        }

        if (_args.OpenTest == "street")
        {
            StreetPose(frame);
            return;
        }

        if (!orbitSelection || _main.Selection.Selected < 0)
        {
            PathPose(frame);
            return;
        }

        // Orbit the selection at a typical inspection distance, as DJ would after selecting something.
        var b = _main.Layout!.NodeBoxes[_main.Selection.Selected];
        _main.Rig.SetDesired(Visualization.Gd(b.Center) with { Y = 0 }, 30f, frame * 0.9f, 20f);
        _main.Rig.Snap();
    }

    /// <summary>Walking height along the longest street connector, looking along it (how DJ moves with WASD).</summary>
    private void StreetPose(int frame)
    {
        var layout = _main.Layout!;
        var best = layout.Connectors.SelectMany(c => c.Points.Zip(c.Points.Skip(1))).MaxBy(seg => System.Numerics.Vector3.Distance(seg.First, seg.Second));
        var a = Visualization.Gd(best.First) with { Y = 2.5f };
        var b = Visualization.Gd(best.Second) with { Y = 2.5f };
        var t = frame / (float)FlickerFrames;
        var pos = a.Lerp(b, 0.1f + 0.7f * t);
        var dir = (b - a).Normalized();
        var yaw = Mathf.RadToDeg(Mathf.Atan2(-dir.X, -dir.Z)) + 8f * Mathf.Sin(t * Mathf.Tau);
        _main.Rig.SetPose(pos, yaw, -6f, 20f);
    }

    private Vector3 TowardSun()
    {
        var sunForward = -_main.Sun.GlobalTransform.Basis.Z; // the direction the light travels
        return new Vector3(-sunForward.X, 0, -sunForward.Z).Normalized();
    }

    /// <summary>A still image looking at the sky towards the sun, to see whether a sun disc is drawn at all.</summary>
    private async Task SunView()
    {
        var b = _main.Layout!.Bounds;
        var towardSun = TowardSun();
        var yaw = Mathf.RadToDeg(Mathf.Atan2(-towardSun.X, -towardSun.Z));
        _main.Rig.SetPose(Visualization.Gd(b.Center) with { Y = 20 }, yaw, 25f, 30f);
        await Frames(5);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        _main.GetViewport().GetTexture().GetImage().SavePng($"{_out}/flicker-sun-view.png");
        _main.Rig.SetPose(Visualization.Gd(b.Center) with { Y = 20 }, yaw, -30f, 30f);
        await Frames(5);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        _main.GetViewport().GetTexture().GetImage().SavePng($"{_out}/flicker-sun-reflection-view.png");
    }

    /// <summary>A file near the middle of the layout, so the orbit stays inside the city.</summary>
    private int PathSelection()
    {
        var tree = _main.Tree!;
        var layout = _main.Layout!;
        var c = layout.Bounds.Center;
        var best = 0;
        var bestD = float.MaxValue;
        for (var i = 1; i < layout.Count; i++)
        {
            if (tree[i].IsDirectory) continue;
            var d = System.Numerics.Vector3.Distance(layout.NodeBoxes[i].Center, c);
            if (d < bestD) { bestD = d; best = i; }
        }

        return best;
    }
}
