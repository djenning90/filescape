using System.Diagnostics;
using System.Globalization;
using System.Text;
using Godot;

namespace FileScape.App;

/// <summary>The named parts of a frame that <see cref="FrameDiag"/> times (one CSV column each, in this order).</summary>
public enum FrameSection
{
    RefreshCard,
    Drain,
    PlanetsTick,
    Tutorial,
    PlaceHud,
    Finish,
    Sky,
    Deliveries,
    Universe,
    Discovery,
    Neighbourhood,
    Recompose,
    ApplyRecompose,
    Walk,
    Travel,
    Overlay,
    Trip,
    DestPanel,
    Signs,
    Thumbs,
    Animate,
    BuildCurrent,
    Compose,
    StateWrite,
}

/// <summary>
/// A supported measurement option (PLANET-179, AD-96; first written as the seventh pass's stall reproduction):
/// <c>--diag-frames=PATH</c> writes one CSV row per frame saying where the main thread's time went, so a long frame can
/// be attributed instead of guessed. Each row has the gap since the previous frame (what the player sees), the time inside
/// <c>Main._Process</c>, the time in each <see cref="FrameSection"/>, the .NET GC collections per generation and their
/// pause, the bytes allocated, the renderer's measured CPU and GPU time, the caller's context columns
/// (<see cref="Context"/>: the experience, the tree, the planets' phase and walker), the probe's <see cref="Segment"/>
/// label and free-text notes (<see cref="Note"/>: a build, a compose, a swap, with its milliseconds).
/// <para>
/// Summarise a file with <c>tools/frame-diag-summary.py</c> (p50/p95/p99/max, counts over 33/100/300 ms, the worst
/// frames and their notes, per segment). Why it exists: the PerfRecorder keeps frame times only, and DJ's "half a second
/// about every eight seconds" needed a cause (found: a recompose that rebuilt every neighbour on the main thread).
/// </para>
/// <para>
/// Preserve: it is off unless the option is given, and then every call is one timestamp read and a branch; call it from
/// the main thread only (its accumulators are not synchronised); a row is written at the start of the next frame, so
/// each row's sections belong to the frame whose gap they caused; nothing it writes names a file or folder (notes carry
/// node ids and milliseconds only, so a measurement on a real root logs no names).
/// </para>
/// </summary>
public static class FrameDiag
{
    private static readonly string[] SectionNames =
        Enum.GetNames<FrameSection>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]).ToArray();

    /// <summary>True while a CSV is being written.</summary>
    public static bool On { get; private set; }

    private static StreamWriter? _writer;
    private static readonly double[] SectionMs = new double[SectionNames.Length];
    private static readonly StringBuilder Notes = new();
    private static long _lastFrameTicks, _processStartTicks, _frames;
    private static double _lastProcessMs;
    private static int _gen0, _gen1, _gen2;
    private static TimeSpan _gcPause;
    private static long _allocatedBytes;
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    /// <summary>Rows are flushed to disk every this many frames (about 2 s at 60 Hz), so a crash loses little.</summary>
    private const int FlushEveryFrames = 120;

    /// <summary>The caller's context columns for each row (Main sets it once); must not contain commas.</summary>
    public static Func<string>? Context { get; set; }

    /// <summary>The CSV header of <see cref="Context"/>'s columns, comma-separated, in order.</summary>
    public static string ContextHeader { get; set; } = "";

    /// <summary>What the run is doing now (a probe's route step: "walk", "land", "overview"), written in every row.</summary>
    public static string Segment { get; set; } = "";

    /// <summary>Starts writing to <paramref name="path"/> (created or replaced) and turns render-time measurement on.</summary>
    public static void Start(string path, Viewport viewport)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        _writer = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 16);
        _writer.WriteLine("frame,tS,gapMs,processMs,otherMs," + string.Join(',', SectionNames)
            + ",gc0,gc1,gc2,gcPauseMs,allocKB,renderCpuMs,renderGpuMs," + (ContextHeader.Length > 0 ? ContextHeader + "," : "") + "segment,notes");
        (_gen0, _gen1, _gen2, _gcPause, _allocatedBytes) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration(), GC.GetTotalAllocatedBytes(false));
        RenderingServer.ViewportSetMeasureRenderTime(viewport.GetViewportRid(), true);
        On = true;
    }

    /// <summary>A timestamp to pass to <see cref="Lap"/>.</summary>
    public static long Now => Stopwatch.GetTimestamp();

    /// <summary>Milliseconds between two <see cref="Now"/> timestamps.</summary>
    public static double Ms(long from, long to) => (to - from) * TicksToMs;

    /// <summary>Adds the time since <paramref name="t0"/> to <paramref name="section"/>; returns now (the next lap's start).</summary>
    public static long Lap(FrameSection section, long t0)
    {
        var t = Stopwatch.GetTimestamp();
        if (On) SectionMs[(int)section] += (t - t0) * TicksToMs;
        return t;
    }

    /// <summary>Adds an event to this frame's notes (no names: ids and milliseconds).</summary>
    public static void Note(string text)
    {
        if (On) Notes.Append(text.Replace(',', ' ')).Append(';');
    }

    /// <summary>Called first in <c>Main._Process</c>: writes the row of the frame that just ended, then starts this one.</summary>
    public static void FrameStart(Viewport viewport)
    {
        if (!On || _writer == null) return;
        var t = Stopwatch.GetTimestamp();
        if (_lastFrameTicks != 0) WriteRow(viewport, t);
        Array.Clear(SectionMs);
        Notes.Clear();
        _frames++;
        _lastFrameTicks = t;
        _processStartTicks = t;
    }

    private static void WriteRow(Viewport viewport, long t)
    {
        var gap = (t - _lastFrameTicks) * TicksToMs;
        var (g0, g1, g2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var pause = GC.GetTotalPauseDuration();
        var alloc = GC.GetTotalAllocatedBytes(false);
        var rid = viewport.GetViewportRid();
        var sb = new StringBuilder(320);
        sb.Append(CultureInfo.InvariantCulture, $"{_frames},{t / (double)Stopwatch.Frequency:0.0000},{gap:0.00},{_lastProcessMs:0.00},{gap - _lastProcessMs:0.00}");
        foreach (var ms in SectionMs) sb.Append(CultureInfo.InvariantCulture, $",{ms:0.00}");
        sb.Append(CultureInfo.InvariantCulture, $",{g0 - _gen0},{g1 - _gen1},{g2 - _gen2},{(pause - _gcPause).TotalMilliseconds:0.00},{(alloc - _allocatedBytes) / 1024}");
        sb.Append(CultureInfo.InvariantCulture, $",{RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid):0.00},{RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid):0.00}");
        if (ContextHeader.Length > 0)
        {
            string context;
            try { context = Context?.Invoke() ?? ""; }
            catch (Exception e) { context = "context-error " + e.GetType().Name; } // a measurement must never stop the game
            sb.Append(',').Append(context);
        }

        sb.Append(',').Append(Segment.Replace(',', ' ')).Append(',').Append(Notes);
        _writer!.WriteLine(sb.ToString());
        (_gen0, _gen1, _gen2, _gcPause, _allocatedBytes) = (g0, g1, g2, pause, alloc);
        if (_frames % FlushEveryFrames == 0) _writer.Flush();
    }

    /// <summary>Called at the end of <c>Main._Process</c>: the main-thread time of this frame so far.</summary>
    public static void ProcessEnd()
    {
        if (On) _lastProcessMs = (Stopwatch.GetTimestamp() - _processStartTicks) * TicksToMs;
    }

    /// <summary>Flushes and closes the file (at exit).</summary>
    public static void Stop()
    {
        On = false;
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
    }
}
