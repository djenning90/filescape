using System.Globalization;
using System.Text;
using Godot;

namespace FileScape.App;

/// <summary>
/// Shared plumbing for in-app probes: injected input through <see cref="Input.ParseInputEvent"/> (InputMap → GUI →
/// Main._Input/_UnhandledInput, the same path as real devices after the OS), frame waits, screenshots, checks and a
/// log. Steps that call app methods directly are labelled "[direct]" by the probes.
/// <para>
/// Timing assumptions every probe inherits: a key or click is held for a couple of frames so the app's _Process sees
/// it at least once between press and release (an event pressed and released in the same frame can be missed by
/// polled actions); waits are by frames or by seconds, and seconds are game time under <see cref="SimulatedTime"/>
/// (<c>--sim-clock</c> with the engine's <c>--fixed-fps 60</c>, or a movie being written; AD-91), else the wall clock.
/// A probe's own timing loops use <see cref="ClockSeconds"/> (game time) or <see cref="Deadline"/> (a timeout on both
/// clocks), never Time.GetTicksMsec directly, unless they time real background work or audio, which is wall-clock by
/// nature (those journeys stay real-time in tools/verify.sh). Under automation injected input continues while the
/// window is unfocused (KD-31), and focus changes are logged next to the checks. The process exit code is the failure count's verdict (0 = every check passed),
/// which tools/fs.sh reads.
/// </para>
/// </summary>
public abstract partial class ProbeBase : Node
{
    protected readonly Main App;
    protected readonly string Out;
    protected readonly string Mode;
    protected readonly StringBuilder LogText = new();
    protected int Failures;
    private int _shot;

    protected ProbeBase(Main main, string outDir, string mode)
    {
        App = main;
        Mode = mode;
        Out = outDir.Replace('\\', '/');
    }

    public override async void _Ready()
    {
        DirAccess.MakeDirRecursiveAbsolute(Out);
        App.Perf.Probe = () => (RenderingServer.ViewportGetMeasuredRenderTimeGpu(App.GetViewport().GetViewportRid()), App.Tree?.Count ?? 0);
        RenderingServer.ViewportSetMeasureRenderTime(App.GetViewport().GetViewportRid(), true); // for the pace line (LogClock)
        Log($"{Mode} probe: adapter={RenderingServer.GetVideoAdapterName()} api={RenderingServer.GetVideoAdapterApiVersion()} window={DisplayServer.WindowGetSize()} ui_scale={GetWindow().ContentScaleFactor}");
        StartSimClock();
        try
        {
            await RunProbe();
        }
        catch (Exception ex)
        {
            Check(false, $"probe threw {ex}");
        }

        RestoreClipboard();
        ProbeTier.WaitForSaves();
        LogClock();
        Log($"done failures={Failures}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Out, $"{Mode}.log"), LogText.ToString());
        Finish();
        GetTree().Quit(Failures == 0 ? 0 : 1);
    }

    /// <summary>The probe's journey: inject input, wait, <see cref="Check"/>. An exception is recorded as a failed check.</summary>
    protected abstract Task RunProbe();

    /// <summary>Runs after the log is written and before the app quits (for a probe's own report files).</summary>

    protected virtual void Finish()
    {
    }

    protected static void Inject(InputEvent e) => Input.ParseInputEvent(e);

    /// <summary>
    /// One key event with the logical and the physical code set alike, so a binding to either matches. A letter also carries its
    /// lower-case character, as a real keyboard's would, so text fields and shortcuts both see it.
    /// </summary>
    /// <summary>Keys a probe holds down now: re-pressed after the OS takes focus away (Godot releases pressed input then).</summary>
    private static readonly HashSet<Key> HeldKeys = [];

    protected static void Key1(Key key, bool pressed, bool ctrl = false)
    {
        if (pressed) HeldKeys.Add(key); else HeldKeys.Remove(key);
        Key1Raw(key, pressed, ctrl);
    }

    private static void Key1Raw(Key key, bool pressed, bool ctrl = false) =>
        Inject(new InputEventKey
        {
            Keycode = key,
            PhysicalKeycode = key,
            Pressed = pressed,
            CtrlPressed = ctrl,
            Unicode = pressed && !ctrl && key is >= Godot.Key.A and <= Godot.Key.Z ? char.ToLowerInvariant((char)key) : 0,
        });

    /// <summary>Re-presses every key the probe holds (after a focus change released them), without counting them again.</summary>
    private static void RepressHeldKeys()
    {
        foreach (var k in HeldKeys) Key1Raw(k, true);
    }

    /// <summary>Holds or releases a key (both its logical and physical code).</summary>
    protected static void HoldKey(Key key, bool down) => Key1(key, down);

    /// <summary>A tap: pressed for <see cref="TapFrames"/> frames, then released and given as long to take effect.</summary>
    protected async Task Key(Key key)
    {
        Key1(key, true);
        await Frames(TapFrames);
        Key1(key, false);
        await Frames(TapFrames);
    }

    /// <summary>
    /// Frames a tapped key or button stays down, and frames given to the release: two, so at least one whole _Process
    /// sees the press whichever point in the frame the event lands (rationale for two rather than one not recorded).
    /// </summary>
    private const int TapFrames = 2;

    /// <summary>A Ctrl+key chord: Ctrl goes down first and comes up last, as a person's fingers do.</summary>
    protected async Task KeyCombo(Key key, bool ctrl)
    {
        Inject(new InputEventKey { Keycode = Godot.Key.Ctrl, PhysicalKeycode = Godot.Key.Ctrl, Pressed = true, CtrlPressed = true });
        Key1(key, true, ctrl);
        await Frames(TapFrames);
        Key1(key, false, ctrl);
        Inject(new InputEventKey { Keycode = Godot.Key.Ctrl, PhysicalKeycode = Godot.Key.Ctrl, Pressed = false });
        await Frames(TapFrames);
    }

    /// <summary>
    /// Writing a movie (--write-movie with --fixed-fps) runs slower than real time: waits then count simulated time
    /// (frame deltas), so a recorded journey moves exactly as a live one (2026-11-11). Live runs keep the wall clock.
    /// </summary>
    protected static bool RecordingMovie => !string.IsNullOrEmpty(Engine.GetWriteMoviePath());

    /// <summary>
    /// <c>--sim-clock</c> (an app argument, TEST-14, AD-91), given by tools/verify.sh together with the engine arguments
    /// <c>--fixed-fps 60 --disable-vsync</c>: every frame is exactly 1/60 s of game time and frames run as fast as the
    /// machine draws them, so a journey's in-game outcomes (walk distances, jump timing, easing) do not depend on load,
    /// and the journey may run faster than real time. <see cref="Seconds"/> and <see cref="Hold"/> then count game time;
    /// <see cref="Until"/>'s timeout needs both clocks (<see cref="FileScape.Core.Timing.JourneyWait"/>).
    /// </summary>
    protected static readonly bool SimClockArg = OS.GetCmdlineUserArgs().Contains("--sim-clock");

    /// <summary>True while waits count game time: <c>--sim-clock</c>, or a movie being written.</summary>
    protected static bool SimulatedTime => SimClockArg || RecordingMovie;

    /// <summary>The fixed frame rate <c>--sim-clock</c> expects from the engine's <c>--fixed-fps</c> (frames per second).</summary>
    private const int SimFramesPerSecond = 60;

    /// <summary>Frames whose delta is checked against the fixed step at the start of a <c>--sim-clock</c> run.</summary>
    private const int SimStepCheckFrames = 30;

    /// <summary>The lowest 3D render scale <c>--sim-render-scale</c> accepts (a fraction of the window's resolution).</summary>
    private const float MinSimRenderScale = 0.25f;

    private int _simFramesChecked, _simFramesOff;

    /// <summary>Seconds since <paramref name="startTicksMsec"/>: wall clock live, simulated time under <see cref="SimulatedTime"/>.</summary>
    private double Since(ulong startTicksMsec, double startSimSeconds) =>
        SimulatedTime ? _simSeconds - startSimSeconds : (Time.GetTicksMsec() - startTicksMsec) / 1000.0;

    private readonly ulong _wallStartUsec = Time.GetTicksUsec();
    private readonly ulong _framesAtStart = Engine.GetProcessFrames();

    /// <summary>
    /// The run's pace (TEST-16): frames, game seconds and wall seconds since the probe started, and their ratio (above 1:
    /// the simulated clock ran faster than real time). Logged last, before "done".
    /// </summary>
    private void LogClock()
    {
        var wall = (Time.GetTicksUsec() - _wallStartUsec) / 1e6;
        var n = Math.Max(_paceFrames, 1);
        Log(F($"clock: {(SimulatedTime ? "simulated" : "wall")}, {Engine.GetProcessFrames() - _framesAtStart} frames, {_simSeconds:0.0} s of game time in {wall:0.0} s ({_simSeconds / Math.Max(wall, 1e-3):0.00}× real time); per frame: render CPU {_paceCpuMs / n:0.0} ms, GPU {_paceGpuMs / n:0.0} ms"));
    }

    private long _paceFrames;
    private double _paceCpuMs, _paceGpuMs;

    /// <summary>Adds the viewport's measured render times (CPU, GPU) to the pace totals.</summary>
    private void SamplePace()
    {
        var rid = App.GetViewport().GetViewportRid();
        _paceFrames++;
        _paceCpuMs += RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid);
        _paceGpuMs += RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
    }

    private double _simSeconds;

    /// <summary>
    /// The probe's clock now, seconds: game time under <see cref="SimulatedTime"/>, the wall clock otherwise. For a
    /// probe's own loops that time something in the game (a walk of N seconds, a fall's duration); a wait for real
    /// background work uses <see cref="Deadline"/> or a Stopwatch instead.
    /// </summary>
    protected double ClockSeconds => SimulatedTime ? _simSeconds : Time.GetTicksUsec() / 1e6;

    /// <summary>
    /// A timeout for a probe's own condition loop: true once <paramref name="seconds"/> have passed by the rule of
    /// <see cref="FileScape.Core.Timing.JourneyWait.TimedOut"/> (both clocks under simulated time, the wall clock live).
    /// </summary>
    protected Func<bool> Deadline(double seconds)
    {
        var wall0 = Time.GetTicksUsec();
        var sim0 = _simSeconds;
        var timeout = FileScape.Core.Units.Durations.FromSeconds(seconds);
        return () => FileScape.Core.Timing.JourneyWait.TimedOut(
            FileScape.Core.Units.Durations.FromSeconds(_simSeconds - sim0),
            FileScape.Core.Units.Durations.FromSeconds((Time.GetTicksUsec() - wall0) / 1e6),
            timeout, SimulatedTime);
    }

    /// <summary>
    /// Under <c>--sim-clock</c>: vsync off and no frame cap, so frames run as fast as the machine allows (the engine's
    /// <c>--disable-vsync</c> does the same; this covers a launch without it), and a log line saying which clock runs.
    /// </summary>
    private void StartSimClock()
    {
        if (!SimClockArg) return;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        // --sim-render-scale=F (0.25–1): the 3D scene drawn at F of the window's resolution, upscaled (the UI stays sharp).
        // On the Radeon 890M the GPU took ≈ 19 ms of a ≈ 30 ms frame at 1600×900 (2026-11-21), so a lower scale lets a
        // simulated journey run faster. No assertion reads 3D pixels; only the screenshots' 3D detail is softer.
        var scaleArg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--sim-render-scale=", StringComparison.Ordinal));
        if (scaleArg != null && float.TryParse(scaleArg["--sim-render-scale=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
        {
            GetViewport().Scaling3DScale = Math.Clamp(scale, MinSimRenderScale, 1f);
            Log(F($"render: 3D at {GetViewport().Scaling3DScale:0.##} of the window's resolution (--sim-render-scale)"));
        }

        Log($"clock: simulated (--sim-clock: waits count game time at a fixed {SimFramesPerSecond} frames per second; timeouts need the wall clock too)");
    }

    /// <summary>
    /// The first <see cref="SimStepCheckFrames"/> frames of a <c>--sim-clock</c> run must have the fixed step: without the
    /// engine's <c>--fixed-fps</c> the deltas are measured and game time would be load-dependent again. An instrument
    /// defect (the launch), reported as a failed check so the run cannot pass silently on the wrong clock.
    /// </summary>
    private void CheckSimStep(double delta)
    {
        if (!SimClockArg || _simFramesChecked >= SimStepCheckFrames) return;
        if (!FileScape.Core.Timing.JourneyWait.IsFixedStep(delta, SimFramesPerSecond)) _simFramesOff++;
        if (++_simFramesChecked == SimStepCheckFrames)
            Check(_simFramesOff == 0, F($"--sim-clock: the first {SimStepCheckFrames} frames have the fixed 1/{SimFramesPerSecond} s step ({_simFramesOff} did not; the launch needs the engine argument --fixed-fps {SimFramesPerSecond})"));
    }

    private bool _lastFocused = true;
    private bool _lossIgnored;

    public override void _Process(double delta)
    {
        _simSeconds += delta;
        SamplePace();
        CheckSimStep(delta);
        // KD-31 diagnostics: injected input does nothing while the window is not focused (by design), so every focus
        // change is logged; a failure next to "lost focus" is the environment, not the product.
        // Under --automate the app keeps taking injected input while the OS window is unfocused (Main: focus loss is
        // ignored for input, 2026-11-17), so a person using the machine no longer breaks a probe; the change is still
        // logged, and keys held across it are pressed again because Godot releases pressed input on a focus change.
        if (App.OsWindowFocused != _lastFocused)
        {
            _lastFocused = App.OsWindowFocused;
            Log(_lastFocused ? "window focus regained" : App.WindowFocused ? "window focus lost (input continues under automation)" : "window focus LOST (injected input is ignored until it returns)");
            // Only a loss the app ignored: a probe that tests focus loss itself (HonourFocusLoss) expects keys forgotten.
            if (!_lastFocused) _lossIgnored = App.WindowFocused;
            if (_lossIgnored) RepressHeldKeys();
        }
    }

    /// <summary>Holds keys together for a duration (pressed in order, released together).</summary>
    protected async Task Hold(double seconds, params Key[] keys)
    {
        var start = Time.GetTicksMsec();
        var sim = _simSeconds;
        foreach (var k in keys) Key1(k, true);
        while (Since(start, sim) < seconds) await Frames(1);
        foreach (var k in keys) Key1(k, false);
        await Frames(1);
    }

    /// <summary>A mouse button event at a window-pixel position; the button mask says the button is held while pressed.</summary>
    protected static void Mouse(MouseButton button, bool pressed, Vector2 position)
    {
        var mask = pressed ? (MouseButtonMask)(1 << ((int)button - 1)) : 0;
        Inject(new InputEventMouseButton { ButtonIndex = button, Pressed = pressed, Position = position, GlobalPosition = position, ButtonMask = mask });
    }

    /// <summary>A relative mouse move, window pixels (the mouse-look path: captured mouse, no absolute position).</summary>
    protected static void Motion(Vector2 relative) =>
        Inject(new InputEventMouseMotion { Relative = relative, ScreenRelative = relative });

    /// <summary>
    /// UI rectangles (Control.GetGlobalRect) are in viewport units; injected events are in window pixels, which the
    /// window divides by its content scale. Converting here keeps clicks on target at any UI scale (found at 125 %).
    /// </summary>
    protected Vector2 ToWindow(Vector2 viewportPoint) => viewportPoint * GetWindow().ContentScaleFactor;

    /// <summary>
    /// A left click on a UI point (viewport units): the pointer moves there first and is given a frame, so hover state
    /// (which Control is under the pointer) is current before the press; the button is held for a tap and the click is
    /// given three frames to act (rationale for three not recorded).
    /// </summary>
    protected async Task Click(Vector2 uiPoint)
    {
        var at = ToWindow(uiPoint);
        Inject(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        Mouse(MouseButton.Left, true, at);
        await Frames(TapFrames);
        Mouse(MouseButton.Left, false, at);
        await Frames(3);
    }

    /// <summary>Types characters only (no key codes), so text fields receive them and no shortcut fires.</summary>
    protected async Task TypeText(string text)
    {
        foreach (var ch in text)
        {
            Inject(new InputEventKey { Pressed = true, Unicode = ch, Keycode = Godot.Key.None });
            Inject(new InputEventKey { Pressed = false, Unicode = ch, Keycode = Godot.Key.None });
        }

        await Frames(3);
    }

    /// <summary>Waits for <paramref name="n"/> process frames.</summary>
    protected async Task Frames(int n)
    {
        for (var i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>Waits <paramref name="s"/> seconds: game time under <see cref="SimulatedTime"/> (--sim-clock, a movie), else the wall clock.</summary>
    protected async Task Seconds(double s)
    {
        var start = Time.GetTicksMsec();
        var sim = _simSeconds;
        while (Since(start, sim) < s) await Frames(1);
    }

    /// <summary>
    /// Waits until <paramref name="condition"/> holds or the timeout passes, checked once a frame. It does not fail on
    /// timeout: the caller's next <see cref="Check"/> says what was not reached, so the log names the real expectation.
    /// </summary>
    protected async Task Until(Func<bool> condition, double timeoutSeconds)
    {
        var timedOut = Deadline(timeoutSeconds);
        while (!condition() && !timedOut()) await Frames(1);
    }

    /// <summary>
    /// Captures the next drawn frame as NN-name.png in --out (numbered in order taken) and logs where the app was, so
    /// the evidence journey reads in sequence. Saving happens on a worker thread (<see cref="ProbeTier.SaveAsync"/>).
    /// </summary>
    protected async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = App.GetViewport().GetTexture().GetImage();
        var path = $"{Out}/{++_shot:D2}-{name}.png";
        ProbeTier.SaveAsync(img, path);
        var tree = App.Tree;
        Log($"screenshot {path} current={(tree != null && tree.Count > 0 ? tree.FullPath(App.Navigator.Current) : "-")} selected={(tree != null && App.Selection.Selected >= 0 ? tree[App.Selection.Selected].Name : "-")}");
    }

    /// <summary>Formats with the invariant culture, so logged numbers parse the same on any machine's locale.</summary>
    protected static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    /// <summary>Logs PASS or FAIL with what was asserted and counts failures; returns <paramref name="ok"/>.</summary>
    protected bool Check(bool ok, string what)
    {
        if (!ok) Failures++;
        Log($"{(ok ? "PASS" : "FAIL")} {what}");
        return ok;
    }

    /// <summary>
    /// The text a probe's Ctrl+V pastes (2026-11-20, the clipboard-log incident: a paste into Destinations' filter put
    /// the user's real clipboard into a probe log). Before the first paste the probe puts this text on the clipboard and
    /// keeps what was there, unlogged, to put back when it finishes (<see cref="RestoreClipboard"/>).
    /// </summary>
    protected const string ClipboardFixtureText = "fs-probe-paste-fixture";

    private enum ClipboardUse { Untouched, Fixture, Skip }
    private ClipboardUse _clipboardUse;
    private string? _clipboardSaved;

    /// <summary>
    /// Makes the clipboard hold <see cref="ClipboardFixtureText"/> before a probe pastes; false when the clipboard holds
    /// something other than text (an image), which a probe could not put back, so the caller skips the paste and the
    /// clipboard is left alone. What a change must preserve: the user's text is never logged, and it is restored.
    /// </summary>
    protected bool UseFixtureClipboard()
    {
        if (_clipboardUse == ClipboardUse.Untouched)
        {
            if (DisplayServer.ClipboardHasImage())
            {
                _clipboardUse = ClipboardUse.Skip;
                Log("clipboard holds an image: pastes are skipped and the clipboard is left untouched");
            }
            else
            {
                _clipboardSaved = DisplayServer.ClipboardGet();
                DisplayServer.ClipboardSet(ClipboardFixtureText);
                _clipboardUse = ClipboardUse.Fixture;
                Log("clipboard: fixture text set for pastes (the previous text is restored at the end and never logged)");
            }
        }

        return _clipboardUse == ClipboardUse.Fixture;
    }

    /// <summary>Puts the user's clipboard text back after <see cref="UseFixtureClipboard"/>, unless something else was copied since.</summary>
    private void RestoreClipboard()
    {
        if (_clipboardUse != ClipboardUse.Fixture) return;
        if (DisplayServer.ClipboardGet() == ClipboardFixtureText) DisplayServer.ClipboardSet(_clipboardSaved ?? "");
        Log("clipboard restored");
        _clipboardSaved = null;
        _clipboardUse = ClipboardUse.Untouched;
    }

    /// <summary>A time-stamped line to the console (tagged with the mode) and to the probe's log file.</summary>
    protected void Log(string line)
    {
        // A safety net for the rule above: the saved clipboard text never reaches a log, whatever pasted it.
        if (_clipboardSaved is { Length: >= 4 } saved && line.Contains(saved, StringComparison.Ordinal))
            line = line.Replace(saved, "[clipboard text removed]", StringComparison.Ordinal);
        var stamped = $"[{DateTime.Now:HH:mm:ss.fff}] {line}";
        GD.Print($"[{Mode}] " + stamped);
        LogText.AppendLine(stamped);
    }
}
