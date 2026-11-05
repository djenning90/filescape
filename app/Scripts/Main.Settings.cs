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
    }

    /// <summary>Preferences and the Settings checkboxes (centres in window pixels) for the OS-level probes' state file.</summary>
    private string StateJsonSettings()
    {
        var scale = GetWindow().ContentScaleFactor;
        var h = _hud.ReverseHorizontalRect.GetCenter() * scale;
        var v = _hud.ReverseVerticalRect.GetCenter() * scale;
        return System.FormattableString.Invariant($"\"reverseH\":{(_prefs.ReverseLookHorizontal ? "true" : "false")},\"reverseV\":{(_prefs.ReverseLookVertical ? "true" : "false")},\"walkSpeed\":{_prefs.WalkSpeed.Value:0.##},\"runSpeed\":{_prefs.RunSpeed.Value:0.##},") +
            System.FormattableString.Invariant($"\"settingsOpen\":{(_hud.SettingsVisible ? "true" : "false")},\"settingsH\":[{h.X:0},{h.Y:0}],\"settingsV\":[{v.X:0},{v.Y:0}],\"uiScale\":{scale:0.##},\"gateChip\":\"{JsonEscape(_hud.GateChipText)}\",\"prefsFile\":\"{JsonEscape(_prefsPath ?? "")}\"");
    }

    /// <summary>Applies and saves new preferences (the Settings panel; probes use it only to set up a case).</summary>
    public void SetPreferences(Preferences p) => ApplyPreferences(p, save: true);

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
