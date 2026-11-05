using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FileScape.Core.Units;

namespace FileScape.Core.Interaction;

/// <summary>
/// The user's saved preferences (CTRL-01..03): how mouse look turns in Places and how fast walking and running are.
/// Immutable; the app holds the current value and saves it whenever it changes. Unknown or out-of-range values in the
/// file are ignored or clamped, never fatal.
/// </summary>
public sealed record Preferences
{
    /// <summary>
    /// Reverse horizontal mouse look in Places. Off: moving the mouse right turns you right (the usual first-person
    /// convention, the behaviour before 2026-11-05 evening). On: moving the mouse right turns you left, as if dragging
    /// the scene. On by default for this iteration because DJ found the previous direction reversed (KD-20).
    /// </summary>
    public bool ReverseLookHorizontal { get; init; } = true;

    /// <summary>Reverse vertical mouse look in Places. Off: moving the mouse down looks down. On: moving it down looks up. On by default (KD-20).</summary>
    public bool ReverseLookVertical { get; init; } = true;

    /// <summary>
    /// After a mouse look ends (right button released, or a left-button drag let go), ease the view back to level so
    /// you are not left looking at the sky or the ground (CTRL-04). The heading you turned to is kept. On by default.
    /// </summary>
    public bool LevelViewOnRelease { get; init; } = true;

    /// <summary>
    /// Flight: W moves where you look (climbing when looking up, diving when looking down), as before 2026-11-06. Off by
    /// default: W/S/A/D move level and only E/Space and Q/C change height (FLY-03), so looking down from above never
    /// makes you dive.
    /// </summary>
    public bool FlyAlongGaze { get; init; }

    /// <summary>Walking speed on foot (W). The default is <see cref="WalkParams.Default"/>'s.</summary>
    public MetersPerSecond WalkSpeed { get; init; } = Places.WalkParams.Default.WalkSpeed;

    /// <summary>Running speed on foot (Shift held). Never below the walking speed.</summary>
    public MetersPerSecond RunSpeed { get; init; } = Places.WalkParams.Default.RunSpeed;

    /// <summary>
    /// The allowed walking and running speeds: 2 m/s is a stroll; 30 m/s is faster than any human and still integrates
    /// safely (0.25 m per 120 Hz sub-step, under the walker's 0.35 m radius, so it cannot pass through a gate pillar).
    /// </summary>
    public static readonly MetersPerSecond MinSpeed = MetersPerSecond.Of(2f), MaxSpeed = MetersPerSecond.Of(30f);

    public static readonly Preferences Default = new();

    /// <summary>Clamps speeds into range (running never slower than walking) and replaces non-finite values.</summary>
    public Preferences Normalized()
    {
        static MetersPerSecond Clamp(MetersPerSecond v, MetersPerSecond fallback) =>
            !v.IsFinite ? fallback : v < MinSpeed ? MinSpeed : v > MaxSpeed ? MaxSpeed : v;

        var walk = Clamp(WalkSpeed, Default.WalkSpeed);
        var run = Clamp(RunSpeed, Default.RunSpeed);
        return this with { WalkSpeed = walk, RunSpeed = run < walk ? walk : run };
    }

    /// <summary>
    /// The look change for a mouse movement of <paramref name="screenPixels"/> (physical pixels, x right, y down) at
    /// <paramref name="degreesPerPixel"/>, in the camera conventions (yaw positive = turn left, pitch positive = up;
    /// docs/units.md). Only the mouse mapping is reversed: arrow-key turning and the coordinate conventions are not.
    /// </summary>
    public (Degrees Yaw, Degrees LookPitch) LookDelta(Vector2 screenPixels, float degreesPerPixel)
    {
        var yaw = -screenPixels.X * degreesPerPixel;   // usual: right → turn right (yaw decreases)
        var pitch = -screenPixels.Y * degreesPerPixel; // usual: down → look down
        return (Degrees.Of(ReverseLookHorizontal ? -yaw : yaw), Degrees.Of(ReverseLookVertical ? -pitch : pitch));
    }

    // ------------------------------------------------------------------ persistence (JSON)

    public string ToJson()
    {
        var o = new JsonObject
        {
            ["version"] = 1,
            ["reverseLookHorizontal"] = ReverseLookHorizontal,
            ["reverseLookVertical"] = ReverseLookVertical,
            ["levelViewOnRelease"] = LevelViewOnRelease,
            ["flyAlongGaze"] = FlyAlongGaze,
            ["walkSpeedMetersPerSecond"] = Math.Round(WalkSpeed.Value, 2),
            ["runSpeedMetersPerSecond"] = Math.Round(RunSpeed.Value, 2),
        };
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Reads preferences; missing fields keep their defaults, malformed text gives the defaults (and <paramref name="problem"/>).</summary>
    public static Preferences FromJson(string json, out string? problem)
    {
        problem = null;
        var p = Default;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o)
            {
                problem = "not a JSON object";
                return p;
            }

            bool? Bool(string k) => o[k] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
            float? Num(string k) => o[k] is JsonValue v && v.TryGetValue<double>(out var d) ? (float)d : null;
            if (Bool("reverseLookHorizontal") is { } h) p = p with { ReverseLookHorizontal = h };
            if (Bool("reverseLookVertical") is { } vv) p = p with { ReverseLookVertical = vv };
            if (Bool("levelViewOnRelease") is { } lv) p = p with { LevelViewOnRelease = lv };
            if (Bool("flyAlongGaze") is { } fg) p = p with { FlyAlongGaze = fg };
            if (Num("walkSpeedMetersPerSecond") is { } w) p = p with { WalkSpeed = MetersPerSecond.Of(w) };
            if (Num("runSpeedMetersPerSecond") is { } r) p = p with { RunSpeed = MetersPerSecond.Of(r) };
        }
        catch (JsonException e)
        {
            problem = e.Message;
        }

        return p.Normalized();
    }

    /// <summary>Loads from <paramref name="path"/>; a missing file is the defaults without a problem.</summary>
    public static Preferences Load(string path, out string? problem)
    {
        problem = null;
        try
        {
            return File.Exists(path) ? FromJson(File.ReadAllText(path), out problem) : Default;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = e.Message;
            return Default;
        }
    }

    /// <summary>Writes atomically (temporary file, then replace), creating the folder if needed.</summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson());
        File.Move(tmp, path, overwrite: true);
    }

    public string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"look H {(ReverseLookHorizontal ? "reversed" : "usual")}, V {(ReverseLookVertical ? "reversed" : "usual")}, level on release {(LevelViewOnRelease ? "on" : "off")}, fly {(FlyAlongGaze ? "along gaze" : "level")}; walk {WalkSpeed.Value:0.#} m/s, run {RunSpeed.Value:0.#} m/s");
}
