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
/// polled actions); waits are by frames or by seconds, and seconds are simulated time while a movie is being written
/// (<see cref="RecordingMovie"/>). Injected input is ignored while the window is unfocused (KD-31), so focus changes
/// are logged next to the checks. The process exit code is the failure count's verdict (0 = every check passed),
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
        Log($"{Mode} probe: adapter={RenderingServer.GetVideoAdapterName()} api={RenderingServer.GetVideoAdapterApiVersion()} window={DisplayServer.WindowGetSize()} ui_scale={GetWindow().ContentScaleFactor}");
        try
        {
            await RunProbe();
        }
        catch (Exception ex)
        {
            Check(false, $"probe threw {ex}");
        }

        ProbeTier.WaitForSaves();
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

    /// <summary>Seconds since <paramref name="startTicksMsec"/>: wall clock live, simulated time while recording a movie.</summary>
    private double Since(ulong startTicksMsec, double startSimSeconds) =>
        RecordingMovie ? _simSeconds - startSimSeconds : (Time.GetTicksMsec() - startTicksMsec) / 1000.0;

    private double _simSeconds;

    private bool _lastFocused = true;
    private bool _lossIgnored;

    public override void _Process(double delta)
    {
        _simSeconds += delta;
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

    /// <summary>Waits <paramref name="s"/> seconds (simulated time while recording a movie).</summary>
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
        var start = Time.GetTicksMsec();
        var sim = _simSeconds;
        while (!condition() && Since(start, sim) < timeoutSeconds) await Frames(1);
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

    /// <summary>A time-stamped line to the console (tagged with the mode) and to the probe's log file.</summary>
    protected void Log(string line)
    {
        var stamped = $"[{DateTime.Now:HH:mm:ss.fff}] {line}";
        GD.Print($"[{Mode}] " + stamped);
        LogText.AppendLine(stamped);
    }
}
