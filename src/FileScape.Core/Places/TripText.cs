namespace FileScape.Core.Places;

/// <summary>
/// The trip panel's text (PLANET-40, AD-56; PLANET-152): where a multi-level trip goes, which hop this is, the route with
/// the reached part lit, and how the trip ended. Pure, so what the panel says about folder names is tested in Core; the
/// planets world (PlanetWorld.Trip) only puts the result in its RichTextLabel.
/// Preserve: every folder name and reason is literal text, never markup (it reaches a BBCode label).
/// </summary>
public static class TripText
{
    /// <summary>The colours of the route's parts: passed, the hop in the air, still ahead; and of a stop's reason.</summary>
    private const string PassedColor = "#9fb3d8", CurrentColor = "#ffe6a0", AheadColor = "#7f8db0", StopColor = "#ffb880";

    /// <summary>The route line's size, pixels at UI scale 1 (quieter than the 15 px heading line).</summary>
    private const int RoutePixels = 12;

    /// <summary>The panel while the trip is under way: the destination, the hop, and the route (names in order, the
    /// first <paramref name="reached"/> landed).</summary>
    public static string Progress(IReadOnlyList<string> hopNames, int reached, bool stopRequested)
    {
        var hop = Math.Min(reached + 1, hopNames.Count);
        var route = string.Join(" › ", hopNames.Select((name, i) =>
            i < reached ? $"[color={PassedColor}]{Bb(name)}[/color]"
            : i == reached ? $"[b][color={CurrentColor}]{Bb(name)}[/color][/b]"
            : $"[color={AheadColor}]{Bb(name)}[/color]"));
        var status = stopRequested ? $" · [color={StopColor}]stopping after this landing[/color]" : "";
        return $"Trip to [b]{Bb(hopNames[^1])}[/b] · hop {hop} of {hopNames.Count}{status}\n[font_size={RoutePixels}]{route}[/font_size]";
    }

    /// <summary>How the trip ended: "arrived", "stopped", "unavailable" (with <paramref name="unavailableWhy"/>) or a
    /// refused launch; <paramref name="originName"/> is offered for Backspace once a hop has landed.</summary>
    public static string Outcome(string outcome, string hereName, string originName, int hops, int reached, string unavailableWhy)
    {
        var back = reached > 0 ? $" · Backspace: back to {Bb(originName)}" : "";
        return outcome switch
        {
            "arrived" => $"[b]Arrived at {Bb(hereName)}[/b] · {hops} hops{back}",
            "stopped" => $"[b]Trip stopped at {Bb(hereName)}[/b] (hop {reached} of {hops}){back}",
            "unavailable" => $"[b]Trip stopped at {Bb(hereName)}[/b] · [color={StopColor}]{Bb(unavailableWhy)}[/color]{back}",
            _ => $"[b]Trip stopped at {Bb(hereName)}[/b] (the next hop could not start){back}",
        };
    }

    private static string Bb(string s) => Interaction.UiText.Escape(s);
}
