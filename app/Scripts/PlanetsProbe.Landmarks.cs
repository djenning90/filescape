using FileScape.App.Planets;
using FileScape.Core.Planets;
using FileScape.Core.Scanning;
using FileScape.Core.Shell;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-landmarks --root=&lt;%TEMP%\FileScape-fixtures\dir with spaces&gt;
/// --wsl-root=&lt;\\wsl.localhost\Distro\tmp\filescape-wsl-fixtures\dir with spaces&gt; --vis=planets --test-shell=record</c>
/// (PLANET-45, AD-58): the root landmarks as a developer's entry point. On the Windows fixture shown with a Windows
/// root's look (a probe-only override: evidence runs never enter C:\), and on the WSL fixture shown with a WSL root's
/// look: the cue is faint from the arrival and brightens on the walk up (real keys); the mark is solid (holding W into
/// it stops at its face); E opens the panel (nothing started yet); 1 records the terminal command for this folder, 2 and
/// 3 the two path forms (recorded, never copied: the person's clipboard is untouched); the filesystem's places are
/// offered. Then the WSL panel's "/ the root" travels to the distribution's real root, whose own monolith opens a
/// terminal at /. The test shell records every start; nothing is started.
/// </summary>
public partial class PlanetsProbe
{
    private async Task LandmarksRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.5); // the establishing shot
        _recording = true;
        var winDir = Tree.FullPath(App.Navigator.Current);
        Check(winDir.Contains("FileScape-fixtures", StringComparison.OrdinalIgnoreCase) && winDir.Contains(' '), $"started in the Windows fixture with a space in its name: {winDir}");
        Check(App.ShellLog.Count == 0, "nothing started yet");

        // ---- the Windows look on the fixture (probe-only override), the gateway
        await StandIn(WorldKind.WindowsRoot);
        var winTerm = TerminalLaunch.For(winDir, TerminalLaunch.FindWindowsTerminal())!;
        await MarkVisit("windows", expectTerminal: $"start {winTerm.Program} {WindowsShellActions.CommandLine(winTerm.Args)}",
            expectCopies: [winDir, PathForms.MountPath(winDir)!], expectPlaces: ["Home", "Documents"]);
        Log($"the Windows terminal would be: {winTerm.Program} {WindowsShellActions.CommandLine(winTerm.Args)}");
        await Key(Godot.Key.Escape);
        await Frames(3);
        Check(!P.RootMarkPanelOpen, "Esc closes the panel");
        P.ProbeCharacter = null;

        // ---- the WSL fixture (Ctrl+L), with a WSL root's look: the monolith
        var wsl = System.Environment.GetCommandLineArgs().Concat(OS.GetCmdlineUserArgs()).FirstOrDefault(a => a.StartsWith("--wsl-root=", StringComparison.Ordinal))?["--wsl-root=".Length..];
        if (string.IsNullOrEmpty(wsl))
        {
            Check(false, "a WSL fixture folder to visit (--wsl-root=…)");
            return;
        }

        await GoToPath(wsl);
        await Until(() => !P.Busy && PathIdentityOf(App.Navigator.Current) == PathIdentityOf(wsl), 15);
        await Seconds(1);
        var owner = App.GetViewport().GuiGetFocusOwner();
        Log($"keyboard focus after Ctrl+L: {owner?.GetType().Name ?? "none"} {(owner != null ? $"(visible {owner.IsVisibleInTree()})" : "")}");
        App.GetViewport().GuiReleaseFocus(); // [instrument] what a click on the world does: the keys go back to walking
        var wslDir = Tree.FullPath(App.Navigator.Current);
        var distro = EditorLaunch.Distribution(wslDir)!;
        var linux = ListAhead.LinuxPath(wslDir)!;
        Check(linux.Contains(' '), $"at the WSL fixture with a space in its name: {linux} in {distro}");
        await StandIn(WorldKind.WslRoot);
        await MarkVisit("wsl", expectTerminal: $"start wsl.exe -d {distro} --cd \"{linux}\"",
            expectCopies: [linux, wslDir], expectPlaces: ["~  home", "/tmp", "/mnt/c", "/  the root"]);

        // ---- a place: "/ the root" travels to the distribution's real root (the override ends first)
        var rootIndex = P.RootMarkActions.ToList().IndexOf("/  the root");
        P.ProbeCharacter = null;
        await Key(Godot.Key.Key1 + rootIndex);
        await Until(() => P.Busy, 6);
        await Until(() => !P.Busy && Tree[App.Navigator.Current].Location == Core.Model.LocationKind.Wsl, 15);
        await Seconds(1.2);
        Check(Tree[App.Navigator.Current].Location == Core.Model.LocationKind.Wsl && P.Character.Kind == WorldKind.WslRoot, $"the panel's \"/  the root\" travelled to {Tree.FullPath(App.Navigator.Current)} ({P.Character.Describe()})");
        var mark = P.Composition?.RootMark;
        Check(mark is { Kind: WorldKind.WslRoot } && mark.ClearanceMetres >= 0, F($"the real root's monolith stands clear of every route: {P.Composition!.Launchers.Count} launchers, clearance {mark?.ClearanceMetres ?? -1:0.00} m, {PlanetFrame.PolarOf(mark?.Direction ?? NVector3.UnitY) * P.Composition.Shape.Radius:0.0} m from the arrival at {PlanetFrame.AzimuthOf(mark?.Direction ?? NVector3.UnitY) * 180 / MathF.PI:0}°"));
        await Shot("landmarks-wsl-root-arrival");
        await MarkVisit("wsl-root", expectTerminal: $"start wsl.exe -d {distro} --cd /", expectCopies: ["/", Locations.WslPrefix + distro + "\\"], expectPlaces: ["/tmp", "/mnt/c"], copies: false);
        await Key(Godot.Key.E);
        await Frames(3);
        Check(!P.RootMarkPanelOpen, "E again closes the panel");
        Log($"shell requests (recorded, none started): {string.Join(" | ", App.ShellLog)}");
        Log($"clipboard (recorded, not copied): {string.Join(" | ", App.ClipboardLog)}");
    }

    /// <summary>The canonical identity of a node's path, for comparing where the navigator is with a typed path.</summary>
    private string PathIdentityOf(int id) => Core.Interaction.PathIdentity.Key(Tree.FullPath(id));

    private string PathIdentityOf(string path) => Core.Interaction.PathIdentity.Key(path);

    /// <summary>[instrument: override] Shows the current folder with a root's look, landmark included, and checks it stands clear.</summary>
    private async Task StandIn(WorldKind kind)
    {
        P.ProbeCharacter = new WorldCharacter(kind, 0, 0, -1);
        P.ProbeRebuild();
        await Seconds(1.5);
        var m = P.Composition?.RootMark;
        Check(P.Character.Kind == kind && m?.Kind == kind, $"[instrument: override] the folder shown with {P.Character.Describe()}'s look and landmark");
        Check(m != null && m.ClearanceMetres >= 0, F($"its landmark stands clear of the routes (clearance {m?.ClearanceMetres ?? -1:0.00} m)"));
    }

    /// <summary>
    /// The visit: faint cue at the arrival, walk up with W (it brightens; the prompt offers E), into it (it stops you), E
    /// (the panel; nothing started), 1 (the terminal recorded), 2 and 3 (the path forms recorded), the places listed.
    /// </summary>
    private async Task MarkVisit(string tag, string expectTerminal, string[] expectCopies, string[] expectPlaces, bool copies = true)
    {
        var m = P.Composition!.RootMark!;
        var r = P.Composition.Shape.Radius;
        // From the arrival, facing the mark: it is in view and its cue is faint.
        var up = P.Walker.Up;
        var toward = m.Direction - up * NVector3.Dot(m.Direction, up);
        if (toward.LengthSquared() > 1e-8f)
        {
            P.Walker.Teleport(P.Composition.Surface, up, NVector3.Normalize(toward)); // [direct] what a drag of the camera does
            P.FaceCameraAlong(NVector3.Normalize(toward));
        }

        await Seconds(0.8);
        var (_, startFace) = P.RootMarkBearing();
        var farGlow = P.RootMarkGlow;
        Check(!P.RootMarkFocused && !P.PromptText.Contains("terminal"), F($"{tag}: from {startFace:0.0} m away the mark is not in reach yet (prompt \"{P.PromptText}\", cue {farGlow:0.00})"));
        await Shot($"landmarks-{tag}-approach");

        // Walk up and into it (real keys): W held, steering with A/D; it must stop you at its face.
        var closest = float.MaxValue;
        var shell0 = App.ShellLog.Count;
        HoldKey(Godot.Key.W, true);
        var held = Godot.Key.None;
        var touchedAt = -1;
        var shotNear = false;
        // Up to 12 s at 60 Hz to walk up; then 1.5 s more pushing into it (longer, a narrow mark's round solids let you
        // slide off round its end, as a pillar would).
        for (var frame = 0; frame < 720 && (touchedAt < 0 || frame < touchedAt + 90); frame++)
        {
            var (_, face) = P.RootMarkBearing();
            closest = MathF.Min(closest, face);
            if (touchedAt < 0 && face < 0.6f) touchedAt = frame;
            if (!shotNear && face < 2.5f) { shotNear = true; await Shot($"landmarks-{tag}-near"); }
            up = P.Walker.Up;
            var to = m.Direction - up * NVector3.Dot(m.Direction, up);
            var camF = -P.Camera.GlobalBasis.Z;
            var f = new NVector3(camF.X, camF.Y, camF.Z);
            f -= up * NVector3.Dot(f, up);
            var right = NVector3.Cross(f, up);
            var side = to.LengthSquared() > 1e-8f && right.LengthSquared() > 1e-8f ? NVector3.Dot(NVector3.Normalize(to), NVector3.Normalize(right)) : 0;
            var want = side > 0.12f ? Godot.Key.D : side < -0.12f ? Godot.Key.A : Godot.Key.None;
            if (want != held)
            {
                if (held != Godot.Key.None) HoldKey(held, false);
                if (want != Godot.Key.None) HoldKey(want, true);
                held = want;
            }

            await Frames(1);
        }

        HoldKey(Godot.Key.W, false);
        if (held != Godot.Key.None) HoldKey(held, false);
        await Frames(20);
        var (_, standFace) = P.RootMarkBearing();
        var onFront = NVector3.Dot(P.Walker.Up - m.Direction, m.Facing) > 0;
        Check(closest > 0.2f && onFront, F($"{tag}: the mark is solid: W towards it, then 1.5 s more into it, stops {closest:0.00} m from its face (the walker is 0.35 m round), still in front of it"));
        Check(P.RootMarkFocused && P.PromptText.StartsWith("E · terminal here", StringComparison.Ordinal), F($"{tag}: within reach ({standFace:0.0} m) the prompt offers it: \"{P.PromptText}\""));
        Check(P.RootMarkGlow > 0.8f && P.RootMarkGlow > farGlow + 0.5f, F($"{tag}: the cue brightened from {farGlow:0.00} to {P.RootMarkGlow:0.00}"));
        Check(App.ShellLog.Count == shell0, $"{tag}: walking up to it and into it started nothing");

        // E: the panel; nothing runs yet.
        await Key(Godot.Key.E);
        await Frames(4);
        var actions = P.RootMarkActions;
        Check(P.RootMarkPanelOpen && App.ShellLog.Count == shell0, $"{tag}: E opens the panel and starts nothing: {string.Join(" | ", actions.Select((a, i) => $"{i + 1} {a}"))}");
        Check(actions.Count > 0 && actions[0] == "Open terminal here", $"{tag}: the first row opens a terminal here");
        foreach (var p in expectPlaces) Check(actions.Contains(p), $"{tag}: the panel offers {p.Trim()}");
        await Seconds(0.5);
        await Shot($"landmarks-{tag}-panel");

        // 1: the terminal (recorded by the test shell, nothing started).
        await Key(Godot.Key.Key1);
        await Frames(3);
        var last = App.ShellLog.Count > shell0 ? App.ShellLog[^1] : "(nothing)";
        Check(App.ShellLog.Count == shell0 + 1 && last == expectTerminal, $"{tag}: 1 asks for the terminal in this folder: {last}");
        if (!copies) return;

        // 2 and 3: the two forms of the path (recorded, never put on the clipboard).
        var clip0 = App.ClipboardLog.Count;
        await Key(Godot.Key.Key2);
        await Frames(2);
        await Key(Godot.Key.Key3);
        await Frames(2);
        var copied = App.ClipboardLog.Skip(clip0).ToList();
        Check(copied.SequenceEqual(expectCopies), $"{tag}: 2 and 3 copy the path's two forms: [{string.Join(" | ", copied)}]");
    }
}
