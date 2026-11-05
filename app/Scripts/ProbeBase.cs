using System.Globalization;
using System.Text;
using Godot;

namespace FileScape.App;

/// <summary>
/// Shared plumbing for in-app probes: injected input through <see cref="Input.ParseInputEvent"/> (InputMap → GUI →
/// Main._Input/_UnhandledInput, the same path as real devices after the OS), frame waits, screenshots, checks and a
/// log. Steps that call app methods directly are labelled "[direct]" by the probes.
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

        Log($"done failures={Failures}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Out, $"{Mode}.log"), LogText.ToString());
        Finish();
        GetTree().Quit(Failures == 0 ? 0 : 1);
    }

    protected abstract Task RunProbe();

    protected virtual void Finish()
    {
    }

    protected static void Inject(InputEvent e) => Input.ParseInputEvent(e);

    protected static void Key1(Key key, bool pressed, bool ctrl = false) =>
        Inject(new InputEventKey
        {
            Keycode = key,
            PhysicalKeycode = key,
            Pressed = pressed,
            CtrlPressed = ctrl,
            Unicode = pressed && !ctrl && key is >= Godot.Key.A and <= Godot.Key.Z ? char.ToLowerInvariant((char)key) : 0,
        });

    protected async Task Key(Key key)
    {
        Key1(key, true);
        await Frames(2);
        Key1(key, false);
        await Frames(2);
    }

    protected async Task KeyCombo(Key key, bool ctrl)
    {
        Inject(new InputEventKey { Keycode = Godot.Key.Ctrl, PhysicalKeycode = Godot.Key.Ctrl, Pressed = true, CtrlPressed = true });
        Key1(key, true, ctrl);
        await Frames(2);
        Key1(key, false, ctrl);
        Inject(new InputEventKey { Keycode = Godot.Key.Ctrl, PhysicalKeycode = Godot.Key.Ctrl, Pressed = false });
        await Frames(2);
    }

    /// <summary>Holds keys together for a duration (pressed in order, released together).</summary>
    protected async Task Hold(double seconds, params Key[] keys)
    {
        var start = Time.GetTicksMsec();
        foreach (var k in keys) Key1(k, true);
        while ((Time.GetTicksMsec() - start) / 1000.0 < seconds) await Frames(1);
        foreach (var k in keys) Key1(k, false);
        await Frames(1);
    }

    protected static void Mouse(MouseButton button, bool pressed, Vector2 position)
    {
        var mask = pressed ? (MouseButtonMask)(1 << ((int)button - 1)) : 0;
        Inject(new InputEventMouseButton { ButtonIndex = button, Pressed = pressed, Position = position, GlobalPosition = position, ButtonMask = mask });
    }

    protected static void Motion(Vector2 relative) =>
        Inject(new InputEventMouseMotion { Relative = relative, ScreenRelative = relative });

    protected async Task Click(Vector2 at)
    {
        Inject(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        Mouse(MouseButton.Left, true, at);
        await Frames(2);
        Mouse(MouseButton.Left, false, at);
        await Frames(3);
    }

    protected async Task TypeText(string text)
    {
        foreach (var ch in text)
        {
            Inject(new InputEventKey { Pressed = true, Unicode = ch, Keycode = Godot.Key.None });
            Inject(new InputEventKey { Pressed = false, Unicode = ch, Keycode = Godot.Key.None });
        }

        await Frames(3);
    }

    protected async Task Frames(int n)
    {
        for (var i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    protected async Task Seconds(double s)
    {
        var start = Time.GetTicksMsec();
        while ((Time.GetTicksMsec() - start) / 1000.0 < s) await Frames(1);
    }

    protected async Task Until(Func<bool> condition, double timeoutSeconds)
    {
        var start = Time.GetTicksMsec();
        while (!condition() && (Time.GetTicksMsec() - start) / 1000.0 < timeoutSeconds) await Frames(1);
    }

    protected async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = App.GetViewport().GetTexture().GetImage();
        var path = $"{Out}/{++_shot:D2}-{name}.png";
        img.SavePng(path);
        var tree = App.Tree;
        Log($"screenshot {path} current={(tree != null && tree.Count > 0 ? tree.FullPath(App.Navigator.Current) : "-")} selected={(tree != null && App.Selection.Selected >= 0 ? tree[App.Selection.Selected].Name : "-")}");
    }

    protected static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    protected bool Check(bool ok, string what)
    {
        if (!ok) Failures++;
        Log($"{(ok ? "PASS" : "FAIL")} {what}");
        return ok;
    }

    protected void Log(string line)
    {
        var stamped = $"[{DateTime.Now:HH:mm:ss.fff}] {line}";
        GD.Print($"[{Mode}] " + stamped);
        LogText.AppendLine(stamped);
    }
}
