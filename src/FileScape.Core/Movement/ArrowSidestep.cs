namespace FileScape.Core.Movement;

/// <summary>
/// The keyboard's sideways step for arrow-key players (PLANET-165..166, AD-94): on foot, ← → turn (the camera on a
/// planet, the walker's heading in Places, WALK-03) and A D step sideways; holding <b>Ctrl</b> makes ← → step sideways
/// too, so someone walking with the arrows can slide along a row of easels without leaving them. Both walkers ask
/// <see cref="Resolve"/> each frame (PlanetWorld.Walk and Main.Walk) and pass its answer on as their strafe and turn.
/// <para>
/// Why Ctrl (AD-94): Shift is already run (and Shift + ← → runs while turning); Alt + Space opens Windows' window menu,
/// so an Alt sidestep would steal the jump; Ctrl + Space, Ctrl + ↑ ↓ and Ctrl + ← → reach nothing in FileScape (the
/// chords it binds are Ctrl+L, Ctrl+O and Ctrl+Home), and the right Ctrl sits beside the arrow keys. Panels that own the
/// keyboard (AD-83) see Ctrl + ← → as their own (a text field's word jump); movement is read only when none does.
/// </para>
/// Preserve: without Ctrl nothing changes (← → turn, A D step); with Ctrl the arrows never turn; the result's axes stay
/// in −1..1 (an arrow and A D the same way do not step faster, opposite ways cancel).
/// </summary>
public static class ArrowSidestep
{
    /// <summary>
    /// The strafe and turn axes for one frame: <paramref name="sideKeys"/> is A D (−1 left … +1 right),
    /// <paramref name="arrows"/> ← → the same way, <paramref name="sidestepHeld"/> whether Ctrl is held.
    /// </summary>
    public static (float Strafe, float Turn) Resolve(float sideKeys, float arrows, bool sidestepHeld) =>
        sidestepHeld ? (Math.Clamp(sideKeys + arrows, -1f, 1f), 0f) : (sideKeys, arrows);
}
