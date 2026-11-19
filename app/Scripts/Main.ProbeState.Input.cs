using Godot;

namespace FileScape.App;

/// <summary>
/// The keyboard-and-reader part of the state file for the real-input probe (<c>tools/os-planets-probe.ps1 -Input</c>,
/// PLANET-51..53): where the keyboard is (the Destinations filter, its results, the reading view), what the filter
/// holds, how many key presses reached the game's shortcuts (none may while you type), the keyboard panel that owns the
/// keys and the counters of AD-68 (keys kept from the game, commands, view changes; the Open… and Settings buttons to
/// click, PLANET-70), the small preview's and the reading view's rectangles and scroll positions (to aim the wheel and
/// check it scrolled), the chase camera's distance (to check the wheel did not zoom it), the reader's status line and whether a toast is drawn above it.
/// Preserve: it only reads; positions are window pixels, like the rest of the state file.
/// </summary>
public partial class Main
{
    private string StateJsonInput()
    {
        var scale = GetWindow().ContentScaleFactor;
        string Px(Vector2 v) => System.FormattableString.Invariant($"[{v.X * scale:0},{v.Y * scale:0}]");
        string Rect(Rect2 r) => System.FormattableString.Invariant($"[{r.Position.X * scale:0},{r.Position.Y * scale:0},{r.Size.X * scale:0},{r.Size.Y * scale:0}]");
        string B(bool b) => b ? "true" : "false";
        var p = _planets;
        var focus = GetViewport().GuiGetFocusOwner() switch { null => "", LineEdit l when l == _hud.PathEdit => "path", ItemList when _hud.LocationListFocused => "locationRows", LineEdit => "filter", Godot.Tree => "results", RichTextLabel => "text", var c => c.GetType().Name };
        var last = string.Join(",", UnhandledKeys.TakeLast(8).Select(k => $"\"{JsonEscape(k)}\""));
        return System.FormattableString.Invariant(
            $"\"input\":{{\"focus\":\"{focus}\",\"filterFocused\":{B(p.DestinationFilterFocused)},\"filterEditing\":{B(p.DestinationFilterEditing)},\"filterText\":\"{JsonEscape(p.DestinationFilterText)}\",\"resultsFocused\":{B(p.DestinationTreeFocused)},\"selectedRow\":\"{JsonEscape(p.SelectedDestinationKey)}\",\"unhandledKeys\":{UnhandledKeyCount},\"keyPanel\":\"{KeyboardPanel}\",\"panelConsumed\":{PanelConsumedKeyCount},\"commands\":{ShortcutCommandCount},\"viewChanges\":{ExperienceChangeCount},\"experience\":\"{_experience}\",\"locationsOpen\":{B(_hud.LocationsVisible)},\"settingsOpen\":{B(_hud.SettingsVisible)},\"listOpen\":{B(p.ListOpen)},\"openButton\":{Px(_hud.OpenLocationButtonRect.GetCenter())},\"settingsButton\":{Px(_hud.SettingsButtonRect.GetCenter())},\"unhandledLast\":[{last}],") + System.FormattableString.Invariant(
            $"\"previewOpen\":{B(p.PreviewOpen)},\"previewText\":{Rect(p.PreviewTextRect)},\"previewScroll\":{p.PreviewScroll:0},\"previewExpand\":{(p.PreviewExpandRect.Size.X > 0 ? Px(p.PreviewExpandRect.GetCenter()) : "null")},\"camDistance\":{p.ChaseDistanceMetres:0.###},") + System.FormattableString.Invariant(
            $"\"readerOpen\":{B(_hud.ReadmeVisible)},\"readerFocused\":{B(_hud.ReaderFocused)},\"readerText\":{Rect(_hud.ReadmeTextRect)},\"readerScroll\":{_hud.ReadmeScroll:0},\"readerAtEnd\":{B(_hud.ReadmeAtEnd)},\"readerPath\":\"{JsonEscape(_hud.Reader?.Path ?? "")}\",\"readerStatus\":\"{JsonEscape(_hud.ReaderStatusText)}\",\"readerOpenButton\":{Px(_hud.ReadmeOpenRect.GetCenter())},\"readerClose\":{Px(_hud.ReadmeCloseRect.GetCenter())},\"toastAboveReader\":{B(_hud.ToastAboveReader)},\"toast\":\"{JsonEscape(_hud.ShownToast)}\"}},");
    }
}
