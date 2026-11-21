using System.Globalization;

namespace FileScape.App.Planets;

/// <summary>
/// The planets' context columns in the per-frame measurement (<see cref="FileScape.App.FrameDiag"/>, PLANET-179): the
/// phase, whether the overview is open and how many bodies it draws, background discovery's counts and state, the
/// recompositions swapped in so far, the worlds in the sky, the neighbourhood's background and sliced work waiting
/// (AD-96), and the walker's grounding and speed. Reads only; counts, never names. Keep <see cref="DiagHeader"/> and
/// <see cref="DiagContext"/> in the same order, and <see cref="EmptyDiagContext"/> the same width.
/// </summary>
public partial class PlanetWorld
{
    public const string DiagHeader = "phase,overview,uniBodies,discRequested,discListed,discState,swaps,skyWorlds,composeWaiting,composerBusy,slicesWaiting,grounded,speedMps";

    /// <summary>The planets' columns when there is no planet world (as many empty cells as <see cref="DiagHeader"/> names).</summary>
    public static readonly string EmptyDiagContext = new(',', DiagHeader.Count(c => c == ','));

    public string DiagContext()
    {
        var w = _walker;
        return string.Create(CultureInfo.InvariantCulture,
            $"{PhaseName},{(_universe != null ? 1 : 0)},{_uniBodies.Count},{_discovery.Requested},{_discovery.Listed},{_discovery.State},{RecomposeSwaps},{_children.Count + (_parent != null ? 1 : 0)},{NeighbourComposeWaiting},{(NeighbourComposerBusy ? 1 : 0)},{NeighbourSlicesWaiting},{(w.Grounded ? 1 : 0)},{w.Velocity.Length():0.00}");
    }
}
