using FileScape.Core.Model;

namespace FileScape.App.Planets;

/// <summary>
/// The neighbourhood's background and sliced work (AD-96). Placeholder for the measurement build: filled in by the fix.
/// </summary>
public partial class PlanetWorld
{
    public int NeighbourComposeWaiting => 0;
    public bool NeighbourComposerBusy => false;
    public int NeighbourSlicesWaiting => 0;

    private void PumpNeighbourhood(FsTree tree)
    {
    }
}
