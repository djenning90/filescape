using System.Globalization;
using System.Text;
using FileScape.App.Visual;
using FileScape.Core.Model;
using FileScape.Core.Scanning;
using FileScape.Core.Shell;
using Godot;

namespace FileScape.App;

/// <summary>
/// Scripted runs for repeatable evidence: screenshots, journey checks, and frame-time CSVs (docs/evidence.md).
/// Enabled only by --automate=journey|perf|scanperf|live. Results are written to --out. Every check prints
/// PASS/FAIL, so a reviewer can see what was asserted rather than trusting a summary.
/// </summary>
public partial class Automation : Node
{
    private readonly Main _main;
    private readonly string _script;
    private readonly string _out;
    private readonly CommandLine _args;
    private readonly StringBuilder _log = new();
    private int _failures;

    public Automation(Main main, string script, string outDir, CommandLine args)
    {
        _main = main;
        _script = script;
        _out = outDir.Replace('\\', '/');
        _args = args;
    }

    public override async void _Ready()
    {
        DirAccess.MakeDirRecursiveAbsolute(_out);
        _main.Perf.Probe = () => (RenderingServer.ViewportGetMeasuredRenderTimeGpu(_main.GetViewport().GetViewportRid()), _main.Tree?.Count ?? 0);
        Log($"automation={_script} out={_out} adapter={RenderingServer.GetVideoAdapterName()} api={RenderingServer.GetVideoAdapterApiVersion()} window={DisplayServer.WindowGetSize()} ui_scale={GetWindow().ContentScaleFactor}");
        try
        {
            switch (_script)
            {
                case "journey": await Journey(); break;
                case "perf": await PerfRuns(); break;
                case "scanperf": await ScanPerf(); break;
                case "live": await Live(); break;
                case "qualitystress": await QualityStress(); break;
                default: Fail($"unknown automation '{_script}'"); break;
            }
        }
        catch (Exception ex)
        {
            Fail($"automation threw {ex}");
        }

        Log($"done failures={_failures}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(_out, $"{_script}.log"), _log.ToString());
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ------------------------------------------------------------ scripts

    private async Task Journey()
    {
        await WaitForScan(TimeSpan.FromSeconds(120));
        await Frames(20);
        await Settle();
        await Shot("01-classic-home");
        _main.Overview();
        await Settle();
        await Shot("01b-classic-overview-birdseye");
        _main.ResetView();
        await Settle();

        var tree = _main.Tree!;
        var target = FindByName(tree, "hundred-mib.iso", "four-gib.mkv") ?? tree.Nodes.Where(n => n.Kind == NodeKind.File).MaxBy(n => n.Size)!;
        _main.Select(target.Id, fly: true);
        await Settle();
        await Shot("02-classic-selected");
        Check(_main.Selection.Selected == target.Id, $"selected {tree.FullPath(target.Id)}");

        _main.SetVisualization(1);
        await Settle();
        Check(_main.Selection.Selected == target.Id, "selection preserved after switching to District (same node id)");
        await Shot("03-district-selected");

        _main.ResetView();
        await Settle();
        await Shot("04-district-overview");

        // Disorientation: far away, looking almost straight down at empty ground.
        _main.Rig.SetDesired(new Vector3(4000, 0, 3000), 9000, 137, 85);
        _main.Rig.Snap();
        await Frames(5);
        await Shot("05-disoriented");
        _main.ResetView();
        await Settle();
        await Shot("06-after-reset");
        var root = _main.Layout!.NodeBoxes[0];
        var rootTop = new Vector3(root.Center.X, root.Max.Y, root.Center.Z);
        var cam = _main.Rig.Camera;
        var rootScreen = cam.UnprojectPosition(rootTop);
        var onScreen = !cam.IsPositionBehind(rootTop) && _main.GetViewport().GetVisibleRect().HasPoint(rootScreen);
        Check(onScreen && _main.PickAt(rootScreen) == 0, $"reset view shows the root pedestal on screen and pickable (screen {rootScreen}, distance {_main.Rig.Distance:0})");
        Check(PickedAtCenterOfAnyNode(), "after reset, nodes are on screen and pickable");
        _main.Overview();
        await Settle();
        await Shot("06b-district-overview-birdseye");

        Check(_main.Selection.Parent(tree) && _main.Selection.Selected == target.ParentId, "parent navigation selects the containing directory");
        Check(_main.Selection.Back() && _main.Selection.Selected == target.Id, "back returns to the file");

        await PickingAccuracy("district");
        _main.SetVisualization(0);
        await Settle();
        Check(_main.Selection.Selected == target.Id, "selection preserved after switching back to Classic");
        _main.FlyToSelection();
        await Settle();
        await PickingAccuracy("classic");

        // Edge cases are visible and inspectable.
        foreach (var name in new[] { "access-denied", "junction-to-root", "emoji-🚀-launch.log" })
        {
            var n = FindByName(tree, name);
            Check(n != null, $"synthetic edge case present: {name}");
            if (n == null) continue;
            _main.Select(n.Id, fly: true);
            await Settle();
            await Shot($"07-edge-{Slug(name)}");
        }

        var deep = FindByName(tree, "level-40");
        if (deep != null)
        {
            _main.Select(deep.Id, fly: true);
            await Settle();
            await Shot("08-deep-level-40");
        }

        var wide = FindByName(tree, "wide-2000-files");
        if (wide != null)
        {
            _main.SetVisualization(1);
            _main.Select(wide.Id, fly: true);
            await Settle();
            await Shot("09-district-wide-2000");
            _main.SetVisualization(0);
        }

        // Mock open for synthetic data never claims success.
        _main.Select(target.Id);
        _main.RequestOpen();
        Check(_main.LastShellMessage?.StartsWith("MOCK", StringComparison.Ordinal) == true, $"synthetic Open is reported as a mock: {_main.LastShellMessage}");

        _main.SetQuality(Quality.Low);
        _main.SetVisualization(1);
        _main.FlyToSelection();
        await Settle();
        await Shot("10-district-low-quality");
    }

    private async Task PerfRuns()
    {
        await WaitForScan(TimeSpan.FromSeconds(300));
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        var summary = new StringBuilder("visualization,quality,frames,mean_ms,p50_ms,p95_ms,p99_ms,max_ms,gpu_mean_ms,gpu_p95_ms,nodes,instances\n");
        foreach (var vis in new[] { 0, 1 })
        {
            foreach (var q in new[] { Quality.High, Quality.Low })
            {
                _main.SetVisualization(vis);
                _main.SetQuality(q);
                _main.ResetView(snap: true);
                await Frames(90);
                _main.Perf.StartRecording();
                await CameraPath(20);
                var samples = _main.Perf.StopRecording();
                var name = $"perf-{(vis == 0 ? "classic" : "district")}-{q.ToString().ToLowerInvariant()}";
                WriteSamples(name, samples);
                var s = PerfRecorder.Summarize(samples.Select(x => x.FrameMs).ToList());
                var g = PerfRecorder.Summarize(samples.Select(x => x.GpuMs).ToList());
                summary.Append(CultureInfo.InvariantCulture, $"{(vis == 0 ? "classic" : "district")},{q},{s.Count},{s.Mean:0.00},{s.P50:0.00},{s.P95:0.00},{s.P99:0.00},{s.Max:0.00},{g.Mean:0.00},{g.P95:0.00},{_main.Tree!.Count},{_main.InstanceCount}\n");
                Log($"{name}: frames={s.Count} mean={s.Mean:0.00} p95={s.P95:0.00} p99={s.P99:0.00} max={s.Max:0.00} gpu_mean={g.Mean:0.00} gpu_p95={g.P95:0.00}");
                await Shot(name);
            }
        }

        System.IO.File.WriteAllText(System.IO.Path.Combine(_out, "perf-summary.csv"), summary.ToString());
        LogMemory();
    }

    private async Task ScanPerf()
    {
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        _main.Perf.StartRecording();
        var started = Time.GetTicksMsec();
        while (_main.Session is { IsFinished: false } && Time.GetTicksMsec() - started < 300_000)
        {
            var t = (Time.GetTicksMsec() - started) / 1000.0;
            if (_main.Layout != null)
            {
                var b = _main.Layout.Bounds;
                _main.Rig.SetDesired(Visual.Visualization.Gd(b.Center) with { Y = 0 }, Mathf.Max(b.Size.X, -b.Min.Z) * 0.8f + 30, (float)(t * 18), 30);
            }

            await Frames(1);
        }

        await Frames(30);
        var samples = _main.Perf.StopRecording();
        WriteSamples("scanperf", samples);
        var during = samples.Where(s => s.Scanning).Select(s => s.FrameMs).ToList();
        var after = samples.Where(s => !s.Scanning).Select(s => s.FrameMs).ToList();
        var relayouts = samples.Where(s => s.RelayoutMs > 0).Select(s => s.RelayoutMs).ToList();
        var a = PerfRecorder.Summarize(during);
        var r = PerfRecorder.Summarize(relayouts);
        Log($"scanperf during-scan frames={a.Count} mean={a.Mean:0.00} p95={a.P95:0.00} p99={a.P99:0.00} max={a.Max:0.00}; relayouts={r.Count} relayout_mean={r.Mean:0.0}ms max={r.Max:0.0}ms; after-scan frames={after.Count}");
        var p = _main.Session!.Snapshot();
        Log($"scan state={p.State} nodes={p.Nodes} elapsed={p.Elapsed.TotalSeconds:0.00}s");
        await Shot("scanperf-final");
        LogMemory();
    }

    /// <summary>A live run on a real directory: scan time, screenshots, cancellation timing, and an optional real Open.</summary>
    private async Task Live()
    {
        await WaitForScan(TimeSpan.FromSeconds(300));
        var p = _main.Session!.Snapshot();
        Check(_main.Tree!.Source.IsLive, $"live source: {_main.Tree.Source.RootDisplayPath}");
        Log($"live scan state={p.State} nodes={p.Nodes} dirs={p.Directories} files={p.Files} links={p.Links} bytes={p.LogicalBytes} problems={p.Problems} elapsed={p.Elapsed.TotalMilliseconds:0}ms");
        await Frames(20);
        await Settle();
        await Shot("live-01-classic-overview");
        var tree = _main.Tree!;
        var biggestDir = tree.Nodes.Where(n => n.IsDirectory && !n.IsRoot).MaxBy(n => n.DirectFileCount);
        if (biggestDir != null)
        {
            _main.Select(biggestDir.Id, fly: true);
            await Settle();
            await Shot("live-02-classic-dir");
            _main.SetVisualization(1);
            await Settle();
            Check(_main.Selection.Selected == biggestDir.Id, "live: selection preserved across visualization switch");
            await Shot("live-03-district-dir");
            _main.SetVisualization(0);
        }

        await PickingAccuracy("live-classic");
        LogMemory();

        if (_args.OpenTest is { } openName)
        {
            var file = FindByName(tree, openName);
            Check(file != null, $"open-test file present: {openName}");
            if (file != null)
            {
                _main.Select(file.Id, fly: true);
                await Settle();
                var r = new WindowsShellActions().Open(tree.FullPath(file.Id));
                Check(r.Succeeded && !r.WasMock, $"ShellExecuteEx reported: {r.Message}");
                await Shot("live-04-after-open");
            }
        }

        // Cancellation timing on a fresh scan of the same root.
        var root = tree.Source.RootDisplayPath;
        _main.StartScan(new DirectoryScanner(root));
        await Frames(1);
        var requestedAt = Time.GetTicksUsec();
        _main.CancelScan();
        var uiReturnUs = Time.GetTicksUsec() - requestedAt;
        var waited = 0;
        while (_main.Session is { IsFinished: false } && waited++ < 600)
        {
            await Frames(1);
        }

        var c = _main.Session!.Snapshot();
        Log($"cancel: Cancel() returned to the UI thread in {uiReturnUs} µs; source stopped {c.CancelAcknowledgedAfter?.TotalMilliseconds:0.0} ms after the request; state={c.State} nodes_at_stop={c.Nodes}");
        Check(c.State is ScanState.Cancelled or ScanState.Completed, $"cancel ended in {c.State} (Completed means the scan finished before the cancel landed)");
        await Frames(10);
        await Shot("live-05-after-cancel");
    }

    /// <summary>
    /// A stability probe for the intermittent Vulkan device loss (docs/implementation-status.md, KD-01): toggles
    /// quality repeatedly in both visualizations. Godot crashes outright on device loss, so a completed log is the
    /// pass signal.
    /// </summary>
    private async Task QualityStress()
    {
        await WaitForScan(TimeSpan.FromSeconds(120));
        if (_args.NoVsync)
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        }

        var toggles = 0;
        for (var round = 0; round < 40; round++)
        {
            if (!_args.StressDistrictOnly)
            {
                _main.SetVisualization(round % 4 < 2 ? 1 : 0);
            }
            else
            {
                _main.SetVisualization(1);
            }

            _main.SetQuality(round % 2 == 0 ? Quality.Low : Quality.High);
            toggles++;
            await Frames(20);
            Log($"toggle {toggles}: vis={_main.ActiveVisualization.Name} quality={_main.Quality} msaa={_main.GetViewport().Msaa3D}");
        }

        Check(true, $"survived {toggles} quality/visualization toggles");
    }

    // ------------------------------------------------------------ helpers

    private async Task PickingAccuracy(string label)
    {
        await Frames(3);
        var tree = _main.Tree!;
        var layout = _main.Layout!;
        var cam = _main.Rig.Camera;
        var vp = _main.GetViewport().GetVisibleRect();
        var blocked = _main.Hud.BlockingRects.ToList();
        var rng = new Random(42);
        int tested = 0, hits = 0, zeroTested = 0, zeroHits = 0;
        var candidates = Enumerable.Range(0, tree.Count).OrderBy(_ => rng.Next()).ToList();
        foreach (var id in candidates)
        {
            if (tested >= 40) break;
            var box = layout.NodeBoxes[id];
            // Aim at the top face centre, the part most likely to be unoccluded.
            var top = new Vector3(box.Center.X, box.Max.Y - 0.01f, box.Center.Z);
            if (cam.IsPositionBehind(top)) continue;
            var screen = cam.UnprojectPosition(top);
            if (!vp.HasPoint(screen) || blocked.Any(r => r.HasPoint(screen))) continue;
            // Only count targets whose top centre is not covered by another node from this viewpoint.
            var picked = _main.PickAt(screen);
            var occluded = picked >= 0 && picked != id && layout.NodeBoxes[picked].Intersect(Visual.Visualization.Num(cam.GlobalPosition), Visual.Visualization.Num((top - cam.GlobalPosition).Normalized())) is { } t && t < (top - cam.GlobalPosition).Length() - 0.05f;
            if (occluded) continue;
            tested++;
            var ok = picked == id;
            if (ok) hits++;
            if (tree[id].Kind == NodeKind.File && tree[id].Size == 0) { zeroTested++; if (ok) zeroHits++; }
        }

        Log($"picking[{label}]: {hits}/{tested} unoccluded on-screen targets resolved to the intended node (zero-byte: {zeroHits}/{zeroTested})");
        Check(tested >= 10, $"picking[{label}] had enough on-screen targets ({tested})");
        Check(tested == 0 || hits == tested, $"picking[{label}] accuracy {hits}/{tested}");
    }

    private bool PickedAtCenterOfAnyNode()
    {
        var size = _main.GetViewport().GetVisibleRect().Size;
        for (var y = 0.15f; y < 0.9f; y += 0.03f)
        {
            for (var x = 0.05f; x < 0.75f; x += 0.03f)
            {
                if (_main.PickAt(new Vector2(size.X * x, size.Y * y)) >= 0) return true;
            }
        }

        return false;
    }

    private async Task CameraPath(double seconds)
    {
        var b = _main.Layout!.Bounds;
        var centre = Visual.Visualization.Gd(b.Center) with { Y = 0 };
        var far = Mathf.Max(b.Size.X, b.Size.Z) * 0.75f + 20;
        var start = Time.GetTicksMsec();
        while ((Time.GetTicksMsec() - start) / 1000.0 < seconds)
        {
            var t = (Time.GetTicksMsec() - start) / 1000.0 / seconds;
            // The first half orbits the overview. The second half dollies in to street level and orbits closer.
            var dist = t < 0.5 ? far : Mathf.Lerp(far, 25, (float)((t - 0.5) * 2));
            var pitch = t < 0.5 ? 34 : Mathf.Lerp(34, 14, (float)((t - 0.5) * 2));
            _main.Rig.SetDesired(centre, dist, (float)(t * 540), pitch);
            _main.Rig.Snap();
            await Frames(1);
        }
    }

    private void WriteSamples(string name, List<PerfRecorder.Sample> samples)
    {
        var sb = new StringBuilder("time_s,frame_ms,gpu_ms,scanning,relayout_ms,nodes\n");
        foreach (var s in samples)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{s.TimeS:0.0000},{s.FrameMs:0.000},{s.GpuMs:0.000},{(s.Scanning ? 1 : 0)},{s.RelayoutMs:0.0},{s.Nodes}\n");
        }

        System.IO.File.WriteAllText(System.IO.Path.Combine(_out, name + ".csv"), sb.ToString());
    }

    private void LogMemory()
    {
        using var proc = System.Diagnostics.Process.GetCurrentProcess();
        GC.Collect();
        Log($"memory: managed_after_gc={GC.GetTotalMemory(true) / 1048576.0:0.0} MB private={proc.PrivateMemorySize64 / 1048576.0:0.0} MB working_set={proc.WorkingSet64 / 1048576.0:0.0} MB video={Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0:0.0} MB nodes={_main.Tree?.Count} instances={_main.InstanceCount}");
    }

    private async Task WaitForScan(TimeSpan timeout)
    {
        var start = Time.GetTicksMsec();
        while (_main.Session is { IsFinished: false } && Time.GetTicksMsec() - start < timeout.TotalMilliseconds)
        {
            await Frames(1);
        }

        // Let the main thread drain the remaining batches and re-lay out.
        for (var i = 0; i < 600 && (_main.Session!.HasPendingBatches || _main.Layout == null || _main.Layout.TreeVersion != _main.Tree!.Version); i++)
        {
            await Frames(1);
        }

        var p = _main.Session!.Snapshot();
        Log($"scan finished state={p.State} nodes={p.Nodes} elapsed={p.Elapsed.TotalMilliseconds:0}ms layout_ms={_main.LastLayoutMs:0.0} build_ms={_main.LastBuildMs:0.0} instances={_main.InstanceCount}");
    }

    private async Task Settle()
    {
        for (var i = 0; i < 240 && !_main.Rig.IsSettled; i++)
        {
            await Frames(1);
        }

        await Frames(8);
    }

    private async Task Frames(int n)
    {
        for (var i = 0; i < n; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = _main.GetViewport().GetTexture().GetImage();
        var path = $"{_out}/{name}.png";
        img.SavePng(path);
        Log($"screenshot {path} ({img.GetWidth()}x{img.GetHeight()}) vis={_main.ActiveVisualization.Name} quality={_main.Quality} selected={(_main.Selection.Selected >= 0 ? _main.Tree!.FullPath(_main.Selection.Selected) : "-")}");
    }

    private static FsNode? FindByName(FsTree tree, params string[] names)
    {
        foreach (var name in names)
        {
            var n = tree.Nodes.FirstOrDefault(x => x.Name == name);
            if (n != null) return n;
        }

        return null;
    }

    private static string Slug(string s) => new(s.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());

    private void Check(bool ok, string what)
    {
        if (!ok) _failures++;
        Log($"{(ok ? "PASS" : "FAIL")} {what}");
    }

    private void Fail(string what) => Check(false, what);

    private void Log(string line)
    {
        var stamped = $"[{DateTime.Now:HH:mm:ss.fff}] {line}";
        GD.Print("[auto] " + stamped);
        _log.AppendLine(stamped);
    }
}
