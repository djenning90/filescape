using FileScape.App.Planets;
using Godot;

namespace FileScape.App;

/// <summary>
/// The navigation and file-action part of the state file for the real-input probe (<c>tools/os-planets-probe.ps1
/// -Play</c>, 2026-11-16): the trip (PLANET-40) and its Stop button, which panels are open (the overlapping-dialogs case,
/// PLANET-41), Locations' "you are here" row, the Destinations rows the probe clicks, a solution file's object on screen
/// and what the recording test shell was asked to do (PLANET-43). It only reads; nothing here changes the session, so
/// a change must keep every value a plain observation (window pixels for positions, like the rest of the state file).
/// </summary>
public partial class Main
{
    private string StateJsonNavigation()
    {
        var scale = GetWindow().ContentScaleFactor;
        string Px(Vector2 v) => System.FormattableString.Invariant($"[{v.X * scale:0},{v.Y * scale:0}]");
        var p = _planets;
        var locHere = -1;
        for (var i = 0; i < _hud.LocationRowCount; i++) if (_hud.LocationRowDisabled(i)) { locHere = i; break; }

        // The first solution file standing on this world, where it is on screen (null when behind the camera).
        var sln = "null";
        if (_experience == Experience.Planets && p.Composition is { } comp && _tree != null)
        {
            foreach (var item in comp.Items)
            {
                if (!_tree.Contains(item.NodeId) || !_tree[item.NodeId].Name.EndsWith(".sln", System.StringComparison.OrdinalIgnoreCase)) continue;
                // Only when it is inside the window (with a 40-unit margin) and not under a HUD panel (the selection card), not merely
                // in front of the camera: a click there would land on the panel.
                if (p.ItemCentre(item.NodeId) is { } c && !p.Camera.IsPositionBehind(c) && GetViewport().GetVisibleRect().Grow(-40).HasPoint(p.Camera.UnprojectPosition(c))
                    && !_hud.BlockingRects.Any(r => r.Grow(8).HasPoint(p.Camera.UnprojectPosition(c))))
                    sln = Px(p.Camera.UnprojectPosition(c));
                break;
            }
        }

        var log = ShellLog;
        var rows = string.Join(",", p.VisibleDestinationRows().Take(40).Select(r =>
            System.FormattableString.Invariant($"{{\"name\":\"{JsonEscape(r.Name)}\",\"at\":{Px(r.Rect.GetCenter())},\"arrow\":{Px(r.Arrow)}}}")));
        return System.FormattableString.Invariant(
            $"\"nav\":{{\"tripActive\":{(p.TripActive ? "true" : "false")},\"tripHops\":{p.TripHops},\"tripReached\":{p.TripReached},\"tripOutcome\":\"{JsonEscape(p.LastTripOutcome)}\",\"tripText\":\"{JsonEscape(p.TripPanelText)}\",\"tripStop\":{Px(p.TripStopRect.GetCenter())},") + System.FormattableString.Invariant(
            $"\"listOpen\":{(p.ListOpen ? "true" : "false")},\"locationsOpen\":{(_hud.LocationsVisible ? "true" : "false")},\"locRows\":{_hud.LocationRowCount},\"locHere\":{locHere},\"locHereAt\":{(locHere >= 0 ? Px(_hud.LocationRowRect(locHere).GetCenter()) : "null")},") + System.FormattableString.Invariant(
            $"\"sln\":{sln},\"shellCount\":{log.Count},\"shellLast\":\"{JsonEscape(log.Count > 0 ? log[^1] : "")}\",\"lastClick\":\"{JsonEscape(p.LastClickTrace)}\",\"phase\":\"{JsonEscape(p.PhaseName)}\",\"destButton\":{Px(_hud.DestinationsButtonRect.GetCenter())},\"locButton\":{Px(_hud.LocationsButtonRect.GetCenter())},\"rows\":[{rows}]}},");
    }
}
