using FileScape.Core.Places;
using FileScape.Core.Interaction;
using Godot;

namespace FileScape.App;

/// <summary>
/// The user's preferences (CTRL-01..03): loaded at start, applied to mouse look and the walker, saved whenever they
/// change. Under --automate nothing is read or written unless --prefs names a file, so probes never alter DJ's settings.
/// </summary>
public partial class Main
{
    private Preferences _prefs = Preferences.Default;
    private string? _prefsPath;

    public Preferences Preferences => _prefs;
    public string? PreferencesPath => _prefsPath;

    /// <summary>
    /// Finds the preferences file (--prefs, else %APPDATA%\FileScape\preferences.json, else none under automation), loads
    /// it (falling back to defaults with a logged reason) and applies it without saving. Prints the <c>[prefs]</c> line.
    /// </summary>
    private void LoadPreferences(CommandLine args)
    {
        _prefsPath = args.PreferencesFile is { Length: > 0 } explicitPath
            ? explicitPath
            : args.Automation == null
                ? System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "FileScape", "preferences.json")
                : null;
        string? problem = null;
        var loaded = _prefsPath != null ? Preferences.Load(_prefsPath, out problem) : Preferences.Default;
        if (problem != null) GD.PrintErr($"[prefs] could not read {_prefsPath}: {problem} — using defaults");
        ApplyPreferences(loaded, save: false);
        _hud.PreferencesChanged += p => SetPreferences(p);
        _hud.SettingsToggled += () => _hud.ToggleSettings();
        GD.Print($"[prefs] {_prefs.Describe()} ({_prefsPath ?? "not saved: automation"})");
        if (_prefsPath != null && System.IO.File.Exists(_prefsPath) && _prefs.WalkSpeed.Value == WalkParams.PreviousDefaultSpeeds.Walk && _prefs.RunSpeed.Value == WalkParams.PreviousDefaultSpeeds.Run)
        {
            // KD-28: saved speeds are never overwritten; the saved pair is the old default, so say what changed, once per start
            // (a toast 3 s after start, shown for 10 s).
            GetTree().CreateTimer(3.0).Timeout += () => _hud.Toast(
                "Places are more compact now. Your saved speeds (walk 8, run 16 m/s) were the old defaults; Settings (F4) → Suggested speeds sets walk 5.5, run 11 m/s.",
                new Color(1f, 0.9f, 0.6f), 10);
        }
    }

    /// <summary>Preferences and the Settings checkboxes (centres in window pixels) for the OS-level probes' state file.</summary>
    private string StateJsonSettings()
    {
        var scale = GetWindow().ContentScaleFactor;
        var h = _hud.ReverseHorizontalRect.GetCenter() * scale;
        var v = _hud.ReverseVerticalRect.GetCenter() * scale;
        var lv = _hud.LevelViewRect.GetCenter() * scale;
        var row0 = _hud.DestinationsOpen && _hud.DestinationCount > 0 ? _hud.DestinationItemRect(0).GetCenter() * scale : Vector2.Zero;
        return System.FormattableString.Invariant($"\"reverseH\":{(_prefs.ReverseLookHorizontal ? "true" : "false")},\"reverseV\":{(_prefs.ReverseLookVertical ? "true" : "false")},\"levelView\":{(_prefs.LevelViewOnRelease ? "true" : "false")},\"walkSpeed\":{_prefs.WalkSpeed.Value:0.##},\"runSpeed\":{_prefs.RunSpeed.Value:0.##},") +
            System.FormattableString.Invariant($"\"settingsOpen\":{(_hud.SettingsVisible ? "true" : "false")},\"settingsH\":[{h.X:0},{h.Y:0}],\"settingsV\":[{v.X:0},{v.Y:0}],\"settingsLevel\":[{lv.X:0},{lv.Y:0}],\"uiScale\":{scale:0.##},\"gateChip\":\"{JsonEscape(_hud.GateChipText)}\",\"prefsFile\":\"{JsonEscape(_prefsPath ?? "")}\",") +
            System.FormattableString.Invariant($"\"destinationsOpen\":{(_hud.DestinationsOpen ? "true" : "false")},\"legendVisible\":{(_hud.LegendVisible ? "true" : "false")},\"aerialShown\":{(_aerialShown ? "true" : "false")},\"altitude\":{(_place != null ? CameraAltitude : 0):0.##},\"signsNamed\":{_signs.ShownIds.Count},\"islands\":{(_aerialShown ? _aerial.IslandCount : 0)},\"destRow0\":[{row0.X:0},{row0.Y:0}]");
    }

    /// <summary>Applies and saves new preferences (the Settings panel; probes use it only to set up a case).</summary>
    public void SetPreferences(Preferences p) => ApplyPreferences(p, save: true);

    /// <summary>Normalises and applies preferences (walker speeds, HUD), and saves them when asked and a file is in use; a failed save is toasted, not fatal.</summary>
    private void ApplyPreferences(Preferences p, bool save)
    {
        _prefs = p.Normalized();
        _walker.SetSpeeds(_prefs.WalkSpeed, _prefs.RunSpeed);
        _hud.SetPreferences(_prefs, _prefsPath);
        if (!save || _prefsPath == null) return;
        try
        {
            _prefs.Save(_prefsPath);
        }
        catch (System.Exception e) when (e is System.IO.IOException or System.UnauthorizedAccessException)
        {
            _hud.Toast($"Settings could not be saved ({e.Message}); they apply until FileScape closes.");
        }
    }
}
