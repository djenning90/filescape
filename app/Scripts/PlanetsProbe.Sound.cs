using FileScape.App.Planets;
using FileScape.Core.Planets;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// <c>--automate=planets-sound --root=sample --vis=planets --test-shell=record</c> (PLANET-47, AD-60): the sound button. On
/// the Starter Sky the music is heard (the output meter and the Master bus peak); a click on the toolbar's speaker silences
/// FileScape and the button becomes the amber "Sound off" indicator; then, muted, a launch to Event Horizon (wind-up,
/// launch, flight, landing, arrival), the inspect and chime cues, the edge warning and a fall with its return all play into
/// a silent output (each cue is triggered: voices sound in the synth, the output stays at zero); N brings the sound back
/// and the button with it; the button works in the overview; a legacy view (silent) has none, and the state carries back.
/// Screenshots of the top bar in both states (run at --ui-scale=1 and 1.25 to check the crowded bar still fits).
/// Measurements: <see cref="SkyAudio.OutputPeak"/> (every sample pushed to Godot) and the Master bus's own peak meter.
/// </summary>
public partial class PlanetsProbe
{
    /// <summary>Silence, as the probe judges it: every pushed sample below 1e-4 (−80 dBFS) once the 15 ms mute ramp is over.</summary>
    private const float SilentPeak = 1e-4f;

    /// <summary>The Master bus peak, dB, below which the bus counts as silent (−80 dB, as <see cref="SilentPeak"/>).</summary>
    private const float SilentBusDb = -80f;

    /// <summary>The music counts as heard when the output peaks above this in a second and a half (≈ −46 dBFS; the mix peaks ≈ 0.3).</summary>
    private const float HeardPeak = 0.005f;

    /// <summary>The loudest Master bus peak seen since the last <see cref="ResetMeters"/>, dB (sampled every frame by <see cref="MeterFrames"/>).</summary>
    private float _busPeakDb = -200f;

    private void ResetMeters()
    {
        P.Audio.ResetOutputPeak();
        _busPeakDb = -200f;
    }

    /// <summary>Waits <paramref name="seconds"/> while sampling the Master bus peak each frame.</summary>
    private async Task MeterSeconds(double seconds)
    {
        var start = Time.GetTicksMsec();
        while ((Time.GetTicksMsec() - start) / 1000.0 < seconds)
        {
            SampleBus();
            await Frames(1);
        }
    }

    private void SampleBus()
    {
        var bus = AudioServer.GetBusIndex("Master");
        _busPeakDb = MathF.Max(_busPeakDb, MathF.Max(AudioServer.GetBusPeakVolumeLeftDb(bus, 0), AudioServer.GetBusPeakVolumeRightDb(bus, 0)));
    }

    /// <summary><see cref="Until"/> that samples the bus while it waits.</summary>
    private async Task MeterUntil(Func<bool> condition, double timeoutSeconds)
    {
        var start = Time.GetTicksMsec();
        while (!condition() && (Time.GetTicksMsec() - start) / 1000.0 < timeoutSeconds)
        {
            SampleBus();
            await Frames(1);
        }
    }

    private string Meters() => F($"output peak {P.Audio.OutputPeak:0.000000} ({20 * MathF.Log10(MathF.Max(1e-9f, P.Audio.OutputPeak)):0} dBFS), Master bus peak {_busPeakDb:0} dB");

    private bool Silent() => P.Audio.OutputPeak < SilentPeak && _busPeakDb < SilentBusDb;

    private async Task SoundRun()
    {
        await Until(() => App.Tree is { Count: > 1 } && App.Experience == Experience.Planets && P.Composition != null, 30);
        await WaitSettled();
        await Seconds(3.5); // the establishing shot
        var root = App.Navigator.Current;
        var eh = Child(root, "Event Horizon");
        var scale = GetWindow().ContentScaleFactor;
        Log(F($"UI scale {scale:0.00}, window {GetWindow().Size}"));

        if (App.SoundMuted)
        {
            await RestoredMuteRun();
            return;
        }

        // ---- sound on at start: the speaker, the music heard
        Check(!App.SoundMuted && !P.Audio.Muted, "the sound is on at start (automation: default preferences)");
        Check(App.Hud.SoundButtonVisible && App.Hud.SoundButtonText == "🔊 Sound", $"the toolbar shows the speaker: \"{App.Hud.SoundButtonText}\"");
        Check(App.Hud.SoundButtonTooltip.Contains("press N", StringComparison.Ordinal), $"its tooltip names the key: \"{App.Hud.SoundButtonTooltip}\"");
        CheckTopBarFits("sound on");
        ResetMeters();
        await MeterSeconds(1.5);
        Check(P.Audio.OutputPeak > HeardPeak && _busPeakDb > -60f, $"the music is heard: {Meters()}");
        await Shot("sound-on-toolbar");

        // ---- one click: silence
        await Click(App.Hud.SoundButtonRect.GetCenter());
        Check(App.SoundMuted && P.Audio.Muted, "one click on the speaker turns the sound off");
        Check(App.Hud.SoundButtonText.Contains("Sound off", StringComparison.Ordinal), $"the button becomes the muted indicator: \"{App.Hud.SoundButtonText}\"");
        CheckTopBarFits("sound off");
        await Seconds(0.1); // the 15 ms ramp, and what was queued before the click (≈ 50 ms)
        ResetMeters();
        await MeterSeconds(1.5);
        Check(Silent(), $"muted, the music is silent: {Meters()}");
        await Shot("sound-off-toolbar");

        // ---- muted: travel (wind-up, launch, flight, landing, arrival) to Event Horizon
        Check(eh >= 0, "the Starter Sky has Event Horizon");
        if (eh < 0) return;
        App.Selection.Select(eh); // [direct] the selection a click makes
        await Frames(2);
        ResetMeters();
        var transfers = P.Transfers;
        await Key(Godot.Key.G);
        await MeterUntil(() => P.Busy, 2);
        var voices = 0;
        while (P.Busy)
        {
            voices = Math.Max(voices, P.Audio.Synth.ActiveVoices);
            SampleBus();
            await Frames(1);
        }

        await MeterSeconds(1.0); // the arrival chord and the hazard world's gong
        Check(P.Transfers == transfers + 1 && App.Navigator.Current == eh, "muted: flew to Event Horizon (G)");
        Check(voices > 0, $"the travel's cues were played (up to {voices} voices in the synth)");
        Check(Silent(), $"muted, the wind-up, launch, flight, landing and arrival are silent: {Meters()}");
        await Shot("sound-off-arrived");

        // ---- muted: inspect, a chime, a selection ([direct]: the calls E, a bell flower and a click make)
        ResetMeters();
        P.Audio.Inspect();
        P.Audio.Chime(3);
        P.Audio.Select();
        await MeterSeconds(1.0);
        Check(Silent(), $"muted, the inspect chime, a bell flower and the selection tick are silent: {Meters()}");

        // ---- muted: near an edge (the warning), off it (the fall), back at the Starter Sky (the return)
        var shell = P.Shell;
        var comp = P.Composition!;
        Check(shell != null, "Event Horizon is a Hazard World");
        if (shell == null) return;
        var r = comp.Shape.Radius;
        // The first open sky from the arrival towards the jet (as PlanetsProbe.Hazard does on the AD-59 terrain).
        var edge = Enumerable.Range(1, 400).Select(i => PlanetFrame.FromPolar(i * 0.025f / r, -25f * MathF.PI / 180f)).First(shell.OverHole);
        var inward = NVector3.Normalize(NVector3.UnitY - edge * edge.Y);
        var stand = PlanetFrame.Move(edge, inward, 1.3f, r);
        var toward = -NVector3.Normalize(inward - stand * NVector3.Dot(inward, stand));
        P.Walker.Teleport(comp.Surface, stand, toward); // [direct] stand 1.3 m from the edge, facing it
        P.FaceCameraAlong(toward);
        ResetMeters();
        await MeterSeconds(1.0);
        Check(P.EdgeNearness > 0.4f, F($"near the edge the warning is called (nearness {P.EdgeNearness:0.00})"));
        Check(Silent(), $"muted, the edge warning is silent: {Meters()}");
        var falls = P.FallsStarted;
        var returns = P.FallReturns;
        ResetMeters();
        voices = 0;
        HoldKey(Godot.Key.W, true);
        await MeterUntil(() => P.Falling, 3);
        HoldKey(Godot.Key.W, false);
        while (P.FallReturns == returns || P.Busy)
        {
            voices = Math.Max(voices, P.Audio.Synth.ActiveVoices);
            SampleBus();
            await Frames(1);
            if (P.FallsStarted == falls) break; // no fall: the check below says so
        }

        await MeterSeconds(1.0);
        Check(P.FallsStarted == falls + 1 && P.FallReturns == returns + 1 && App.Navigator.Current == root, $"muted: off the edge, the fall and the return to the Starter Sky (falls {P.FallsStarted - falls}, returns {P.FallReturns - returns}, at {Tree.DisplayName(App.Navigator.Current)})");
        Check(voices > 0, $"the fall's cues were played (up to {voices} voices in the synth)");
        Check(Silent(), $"muted, the crack, the fall and the return are silent: {Meters()}");

        // ---- N: sound back, the button with it
        await Key(Godot.Key.N);
        Check(!App.SoundMuted && !P.Audio.Muted && App.Hud.SoundButtonText == "🔊 Sound", $"N turns the sound back on, and the button shows it: \"{App.Hud.SoundButtonText}\"");
        ResetMeters();
        await MeterSeconds(1.5);
        Check(P.Audio.OutputPeak > HeardPeak && _busPeakDb > -60f, $"the music is back: {Meters()}");
        await Shot("sound-back-on");
        await Key(Godot.Key.N);
        Check(App.SoundMuted && App.Hud.SoundButtonText.Contains("Sound off", StringComparison.Ordinal), "N again: off (the key and the button are one state)");
        await Key(Godot.Key.N);
        Check(!App.SoundMuted, "and on");

        // ---- in the overview: the button (and N) work there too
        await Key(Godot.Key.T);
        await Until(() => P.UniverseOpen, 3);
        await Seconds(1.0);
        await Click(App.Hud.SoundButtonRect.GetCenter());
        Check(P.UniverseOpen && App.SoundMuted && P.Audio.Muted, "in the overview the button silences too");
        await Seconds(0.1);
        ResetMeters();
        await MeterSeconds(1.0);
        Check(Silent(), $"silent in the overview: {Meters()}");
        await Shot("sound-off-overview");
        await Key(Godot.Key.N);
        Check(!App.SoundMuted, "N in the overview turns it back on");
        await Key(Godot.Key.T);
        await Until(() => !P.UniverseOpen && !P.Busy, 4);

        // ---- a legacy view (Places) is silent and has no sound button; the state carries across
        await Click(App.Hud.SoundButtonRect.GetCenter());
        Check(App.SoundMuted, "muted on the planets before switching");
        App.SetExperience(Experience.Places); // [direct] what the Legacy views menu's Places item does
        await Until(() => App.Experience == Experience.Places, 5);
        await Seconds(1.5);
        Check(!App.Hud.SoundButtonVisible, "Places (silent) shows no sound button");
        CheckTopBarFits("Places");
        await Shot("places-toolbar");
        App.SetExperience(Experience.Planets); // [direct] the toolbar's Planets button
        await WaitSettled();
        await Seconds(0.3);
        Check(P.Audio.Muted && App.Hud.SoundButtonVisible && App.Hud.SoundButtonText.Contains("Sound off", StringComparison.Ordinal), "back on the planets: still muted, and the indicator shows it");
        ResetMeters();
        await MeterSeconds(1.5);
        Check(Silent(), $"and silent: {Meters()}");
        await Click(App.Hud.SoundButtonRect.GetCenter());
        Check(!App.SoundMuted && !P.Audio.Muted, "one click brings the sound back");
        ResetMeters();
        await MeterSeconds(1.5);
        Check(P.Audio.OutputPeak > HeardPeak, $"heard again: {Meters()}");
        Check(App.PreferencesPath == null, "automation: nothing was saved (no preferences file)");
    }

    /// <summary>
    /// Run with <c>--prefs=</c> a file saying <c>"soundMuted": true</c>: a mute restored at launch is obvious (the amber
    /// indicator from the start, a toast saying how to undo it), silent, and one click brings the sound back and saves it.
    /// </summary>
    private async Task RestoredMuteRun()
    {
        Check(P.Audio.Muted && App.Hud.SoundButtonText.Contains("Sound off", StringComparison.Ordinal), $"a saved mute is restored at launch, and the toolbar shows it: \"{App.Hud.SoundButtonText}\"");
        await Until(() => App.Hud.ShownToast.Contains("Sound is off", StringComparison.Ordinal), 5);
        Check(App.Hud.ShownToast.Contains("press N", StringComparison.Ordinal), $"a toast says the sound is off and how to turn it on: \"{App.Hud.ShownToast}\"");
        await Shot("restored-mute");
        ResetMeters();
        await MeterSeconds(1.5);
        Check(Silent(), $"silent from the start: {Meters()}");
        await Click(App.Hud.SoundButtonRect.GetCenter());
        Check(!App.SoundMuted && !P.Audio.Muted && App.Hud.SoundButtonText == "🔊 Sound", "one click: sound on");
        ResetMeters();
        await MeterSeconds(1.5);
        Check(P.Audio.OutputPeak > HeardPeak, $"heard: {Meters()}");
        var saved = App.PreferencesPath is { } path && System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : "";
        Check(saved.Contains("\"soundMuted\": false", StringComparison.Ordinal), $"saved: sound on ({App.PreferencesPath})");
    }

    /// <summary>The top bar stays inside the window and on one row, and the sound button is wholly inside it.</summary>
    private void CheckTopBarFits(string when)
    {
        var view = App.GetViewport().GetVisibleRect();
        var bar = App.Hud.TopBarRect;
        var button = App.Hud.SoundButtonRect;
        var shown = App.Hud.SoundButtonVisible;
        Check(bar.End.X <= view.End.X + 0.5f && bar.Size.Y < 60 && (!shown || bar.Encloses(button)), F($"{when}: the top bar fits ({bar.Size.X:0}×{bar.Size.Y:0} in {view.Size.X:0}×{view.Size.Y:0}){(shown ? F($"; the sound button at x {button.Position.X:0}–{button.End.X:0}") : "")}"));
    }
}
