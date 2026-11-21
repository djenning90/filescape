using FileScape.Core.Audio;
using FileScape.Core.Interaction;
using FileScape.Core.Planets;
using Godot;
using CoreDecoration = FileScape.Core.Planets.DecorationKind;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App.Planets;

/// <summary>
/// The lighthouse and the scenery that answers (PLANET-74, PLANET-75, AD-70; DJ 2026-11-19: "I tried the telescope and
/// lighthouse and nothing happened … choose a few decorative objects that could respond delightfully to a deliberate
/// action"). Owns when they act and what the prompt says; <see cref="PlanetVisual"/> (PlanetVisual.Whimsy.cs) draws the
/// beam, the bounce and the glow, and <see cref="SkyAudio"/> plays the in-key replies.
/// <para>
/// <b>The lighthouse</b> (Hearth's landmark): at its foot the prompt says "E or twirl · light the lighthouse"; either
/// lights it — its beam sweeps round once and a half, the lamp flares, a soft horn and two bells sound. It opens nothing.
/// <b>Scenery</b>: mushrooms, crystals and lamps (lanterns) answer a twirl beside them — a mushroom bounces with a boing,
/// a crystal glows and rings three glassy notes, a lamp flares and chimes. The nearest such piece within a few metres
/// shimmers (a soft breathing light), and while a kind is new the prompt says so beside it (the first
/// <see cref="SceneryPromptTimes"/> replies of each kind); after that the shimmer alone is the cue, so groves stay quiet.
/// </para>
/// <para>
/// What a change must preserve: files and pads keep E and the twirl (a file in reach wins: its preview, never the
/// scenery); the twirl is still movement and nothing here opens or starts anything; every effect is brief (≤ 3 s) and
/// can be repeated at once; walking routes are untouched (nothing here adds a solid).
/// </para>
/// </summary>
public partial class PlanetWorld
{
    // ---- the lighthouse (by eye, 2026-11-19)
    /// <summary>Within this far of the lighthouse's footprint, metres (its rocks and a step beyond), its prompt shows and E or a twirl lights it.</summary>
    private const float LighthouseReachMetres = 1.8f;

    // ---- scenery
    /// <summary>A twirl this close to a piece's centre, metres along the ground (beside it, or over a mushroom), makes it answer.</summary>
    private const float SceneryReachMetres = 1.7f;

    /// <summary>The nearest answering piece within this far shimmers, metres (seen a few steps before you reach it).</summary>
    private const float SceneryShimmerMetres = 4.5f;

    /// <summary>A kind's prompt shows beside it until it has answered this many times in this run (then the shimmer alone cues it).</summary>
    private const int SceneryPromptTimes = 2;

    /// <summary>Times the lighthouse has been lit, and scenery has answered, in this run (probes).</summary>
    public int LighthouseLights { get; private set; }

    /// <inheritdoc cref="LighthouseLights"/>
    public int SceneryReplies { get; private set; }

    /// <summary>The last piece of scenery to answer: its kind, or null before any (probes).</summary>
    public CoreDecoration? LastSceneryKind { get; private set; }

    private readonly Dictionary<CoreDecoration, int> _sceneryRepliesByKind = [];

    /// <summary>At the lighthouse's foot with nothing nearer in focus: E lights it (set by <see cref="UpdateLighthousePrompt"/>).</summary>
    private bool _focusLighthouse;

    /// <summary>The scenery kinds that answer a twirl and their voices (AD-70: three kinds the themes already scatter; trees, rocks, grass and flowers stay still).</summary>
    private static SceneryVoice? VoiceOf(CoreDecoration k) => k switch
    {
        CoreDecoration.Mushroom => SceneryVoice.Mushroom,
        CoreDecoration.Crystal => SceneryVoice.Crystal,
        CoreDecoration.Lamp => SceneryVoice.Lamp,
        _ => null,
    };

    /// <summary>What the prompt calls a kind (Lamp is drawn as a lantern post on most themes).</summary>
    private static string SceneryName(CoreDecoration k) => k switch
    {
        CoreDecoration.Mushroom => "mushroom",
        CoreDecoration.Crystal => "crystal",
        _ => "lantern",
    };

    /// <summary>This world has a lighthouse (Hearth's landmark).</summary>
    private bool HasLighthouse => _current is { Comp: { Theme: PlanetTheme.Hearth, Landmark: not null } };

    /// <summary>Metres from the lighthouse's axis, or null without one.</summary>
    private float? LighthouseMetres() =>
        HasLighthouse ? PlanetShape.Angle(_walker.Up, _current!.Comp.Landmark!.Direction) * _current.Comp.Shape.Radius : null;

    /// <summary>At the lighthouse's foot (within <see cref="LighthouseReachMetres"/> of its footprint).</summary>
    private bool AtLighthouse() => LighthouseMetres() is { } d && d <= _current!.Comp.Landmark!.Radius + LighthouseReachMetres;

    /// <summary>From <see cref="UpdateLandmarkApproachPrompt"/>: at the lighthouse's foot, its prompt (true), and E lights it.</summary>
    private bool UpdateLighthousePrompt()
    {
        if (!AtLighthouse()) return false;
        _focusLighthouse = true;
        SetPrompt(LighthousePrompt, "the lighthouse");
        return true;
    }

    /// <summary>The lighthouse's prompt (probes check it).</summary>
    public const string LighthousePrompt = "{E} or twirl ({Space}, {Space}) · light the lighthouse";

    /// <summary>Lights the lighthouse: the beam sweeps, the lamp flares, the horn and bells sound. Opens nothing.</summary>
    private void LightLighthouse()
    {
        if (!HasLighthouse) return;
        LighthouseLights++;
        _audio.LighthouseCall();
        _current!.Visual?.SweepLighthouse();
        GD.Print($"[planets] the lighthouse is lit ({LighthouseLights})");
    }

    /// <summary>
    /// A twirl that the bench, the bell and a root landmark did not take: a file in focus previews (PLANET-43, unchanged);
    /// otherwise at the lighthouse's foot it lights; otherwise the nearest answering scenery within reach answers.
    /// </summary>
    private void TwirlNearby()
    {
        if (_focusItem != null || _focusLauncher != null)
        {
            TwirlNearFile();
            return;
        }

        if (AtLighthouse())
        {
            LightLighthouse();
            return;
        }

        if (NearestScenery() is { } n && n.Metres <= SceneryReachMetres) ReplyScenery(n.Index);
    }

    /// <summary>The scenery piece <paramref name="index"/> (in the composition's decorations) answers: its voice, its motion and glow.</summary>
    private void ReplyScenery(int index)
    {
        var d = _current!.Comp.Decorations[index];
        if (VoiceOf(d.Kind) is not { } voice) return;
        SceneryReplies++;
        LastSceneryKind = d.Kind;
        _sceneryRepliesByKind[d.Kind] = _sceneryRepliesByKind.GetValueOrDefault(d.Kind) + 1;
        _audio.SceneryReply(voice, d.Seed);
        _current.Visual?.PlayScenery(index);
        GD.Print($"[planets] the {SceneryName(d.Kind)} answers ({SceneryReplies})");
    }

    /// <summary>The nearest answering piece of scenery: its index in the composition's decorations, kind and metres along the ground; null without one.</summary>
    private (int Index, CoreDecoration Kind, float Metres)? NearestScenery()
    {
        if (_current == null) return null;
        var r = _current.Comp.Shape.Radius;
        var decorations = _current.Comp.Decorations;
        (int, CoreDecoration, float)? best = null;
        for (var i = 0; i < decorations.Count; i++)
        {
            var d = decorations[i];
            if (VoiceOf(d.Kind) == null) continue;
            var m = PlanetShape.Angle(_walker.Up, d.Direction) * r;
            if (best is not { } b || m < b.Item3) best = (i, d.Kind, m);
        }

        return best;
    }

    /// <summary>Every focus update: the nearest answering piece within <see cref="SceneryShimmerMetres"/> shimmers (or none does).</summary>
    private void UpdateSceneryCue()
    {
        if (_current?.Visual is not { } v) return;
        var n = NearestScenery();
        v.SceneryCue = n is { } x && x.Metres <= SceneryShimmerMetres ? x.Index : -1;
    }

    /// <summary>
    /// From <see cref="UpdateFocus"/> when nothing else is in focus: beside an answering piece whose kind is still new
    /// (fewer than <see cref="SceneryPromptTimes"/> replies), the prompt says how to make it answer (true).
    /// </summary>
    private bool UpdateSceneryPrompt()
    {
        if (NearestScenery() is not { } n || n.Metres > SceneryReachMetres) return false;
        if (_sceneryRepliesByKind.GetValueOrDefault(n.Kind) >= SceneryPromptTimes) return false;
        SetPrompt($"{K("Space")}, {K("Space")} · twirl by the {SceneryName(n.Kind)}", "a little scenery that answers");
        return true;
    }

    /// <summary>The probe state's lighthouse and scenery fields (a JSON fragment ending in a comma): where to steer, and what answered.</summary>
    private string WhimsyStateJson()
    {
        var (lb, ld) = HasLighthouse ? (CameraTurnTo(_current!.Comp.Landmark!.Direction), LighthouseMetres() ?? -1) : (0f, -1f);
        var n = NearestScenery();
        var (sb, sd, sk) = n is { } x ? (CameraTurnTo(_current!.Comp.Decorations[x.Index].Direction), x.Metres, x.Kind.ToString()) : (0f, -1f, "");
        return System.FormattableString.Invariant(
            $"\"lighthouse\":{(HasLighthouse ? "true" : "false")},\"lighthouseBearing\":{lb:0.#},\"lighthouseDistance\":{ld:0.##},\"lighthouseLights\":{LighthouseLights},\"lighthouseBeam\":{((_current?.Visual?.LighthouseBeam ?? false) ? "true" : "false")},\"sceneryKind\":\"{sk}\",\"sceneryBearing\":{sb:0.#},\"sceneryDistance\":{sd:0.##},\"sceneryReplies\":{SceneryReplies},\"sceneryLast\":\"{LastSceneryKind}\",\"sceneryPlaying\":{((_current?.Visual?.SceneryPlaying ?? false) ? "true" : "false")},");
    }

    /// <summary>The caption line now (probes check the telescope's caption).</summary>
    public string HintText => _hintPlain;

    /// <summary>The piece of scenery shimmering now (its decoration index), or −1 (probes).</summary>
    public int SceneryCueIndex => _current?.Visual?.SceneryCue ?? -1;

    /// <summary>The lighthouse's beam is sweeping now (probes).</summary>
    public bool LighthouseBeam => _current?.Visual?.LighthouseBeam ?? false;

    /// <summary>Some piece of scenery is answering now (probes).</summary>
    public bool SceneryPlaying => _current?.Visual?.SceneryPlaying ?? false;

    /// <summary>The nearest answering scenery's surface direction, or null (probes walk to it).</summary>
    public NVector3? NearestSceneryDirection => NearestScenery() is { } n ? _current!.Comp.Decorations[n.Index].Direction : null;
}
