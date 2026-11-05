namespace FileScape.App;

/// <summary>Locations, paths and visibility (LOC-01, VISI-04). Filled in by the connected-world slice.</summary>
public partial class Main
{
    private void ToggleHiddenVisibility() => _hud.Toast("Hidden-item visibility is not available yet.");

    private void ShowGoToPath() => _hud.Toast("Go to path is not available yet.");

    private void ShowLocations() => _hud.Toast("Locations are not available yet.");
}
