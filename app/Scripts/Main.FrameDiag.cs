using System.Globalization;
using Godot;

namespace FileScape.App;

/// <summary>
/// Main's part of the per-frame measurement (<see cref="FrameDiag"/>, PLANET-179): starts it for <c>--diag-frames=PATH</c>
/// and supplies the context columns every row carries (the experience, the tree's size and version, whether the scan
/// still runs or has batches waiting, then the planets' own columns from <see cref="Planets.PlanetWorld.DiagContext"/>).
/// Reads only; counts and ids, never names.
/// </summary>
public partial class Main
{
    private void StartFrameDiag(string path)
    {
        FrameDiag.ContextHeader = "experience,treeCount,treeVersion,scanning,batchesWaiting," + FileScape.App.Planets.PlanetWorld.DiagHeader;
        FrameDiag.Context = () => string.Create(CultureInfo.InvariantCulture,
            $"{_experience},{_tree?.Count ?? 0},{_tree?.Version ?? 0},{(_session is { IsFinished: false } ? 1 : 0)},{(_session?.HasPendingBatches == true ? 1 : 0)},")
            + (_planets != null ? _planets.DiagContext() : FileScape.App.Planets.PlanetWorld.EmptyDiagContext);
        FrameDiag.Start(path, GetViewport());
        GD.Print("[diag] per-frame CSV: " + path);
    }
}
