using FileScape.Core.Audio;
using FileScape.Core.Planets;

namespace FileScape.Core.Tests;

/// <summary>The bell tower's toll (PLANET-56): on the music's grid, in the world's key, a long ring that stands clear of the music, bounded when rung again and again.</summary>
public partial class SkySynthTests
{
    [Theory]
    [MemberData(nameof(Identities))]
    public void The_bell_toll_strikes_on_the_grid_in_the_world_s_key_and_answers_a_beat_later(MusicIdentity id)
    {
        var grid = FramesPerSixteenth(id) / 2;
        foreach (var preroll in new[] { 1000, 23_456, 51_000 })
        {
            var s = SynthFor(11, PlanetTheme.Chime, id, musicVolume: 0);
            s.Render(preroll);
            s.BellToll();
            var buf = s.Render(Frames(3.0));
            var first = FirstSound(buf, preroll);
            Math.Abs(first - Math.Round(first / grid) * grid).Should().BeLessThan(8, $"{id}: the strike is on the 32nd grid (preroll {preroll})");
            (first - preroll).Should().BeLessThanOrEqualTo((long)(SkySynth.QuantisedLatencyBound.TotalSeconds * Rate), "it answers the twirl at once");
            s.LastBellToll.Frame.Should().BeCloseTo(first, 2, "the toll reports its strike (the first audible sample may follow it by one)");

            var key = s.Key;
            var scale = key.IntervalsSemitones.Select(i => (key.RootMidiNote + i) % 12).ToHashSet();
            s.LastBellToll.MidiNotes.Should().OnlyContain(n => scale.Contains(n % 12), $"{id}: every partial is in {key.Mode} on {key.RootMidiNote}");
            s.LastBellToll.MidiNotes.Should().Contain(key.RootMidiNote, "the strike is on the tonic");

            // The answer: a second onset one beat (eight 32nds) after the strike, and the bell still rings 2 s on.
            double Rms(double fromSeconds, double toSeconds)
            {
                var a = (int)((fromSeconds * Rate) + (first - preroll)) * 2;
                var b = (int)((toSeconds * Rate) + (first - preroll)) * 2;
                double sum = 0;
                for (var i = a; i < b; i++) sum += buf[i] * (double)buf[i];
                return Math.Sqrt(sum / (b - a));
            }

            var beat = 8 * grid / Rate;
            Rms(beat, beat + 0.03).Should().BeGreaterThan(Rms(beat - 0.05, beat - 0.02) * 1.15, $"{id}: the bell swings back and strikes again on the beat");
            Rms(2.0, 2.4).Should().BeGreaterThan(Rms(0, 0.4) * 0.05, $"{id}: it rings on (a bell, not a blip)");
            buf.Max(MathF.Abs).Should().BeLessThan(0.5f);
        }
    }

    [Theory]
    [MemberData(nameof(Identities))]
    public void The_bell_toll_stands_clear_of_every_world_s_music(MusicIdentity id)
    {
        foreach (var at in new[] { 4.0, 4.31 })
        {
            float[] Run(float music)
            {
                var s = SynthFor(30, PlanetTheme.Chime, id, musicVolume: music);
                s.Render(Frames(at));
                s.BellToll();
                return s.Render(Frames(1.0));
            }

            var cue = Run(0);
            var with = Run(1);
            double c = 0, m = 0;
            for (var i = 0; i < cue.Length; i++)
            {
                c += cue[i] * (double)cue[i];
                m += Math.Pow(with[i] - cue[i], 2);
            }

            var snr = 10 * Math.Log10(c / Math.Max(1e-12, m));
            output.WriteLine($"{id} at {at} s: the toll {snr:+0.0;-0.0} dB over the ducked music");
            snr.Should().BeGreaterThan(4, $"{id}: the bell is heard over the music");
        }
    }

    [Fact]
    public void Ringing_the_bell_again_and_again_stays_within_the_voice_budget_and_never_clips()
    {
        var s = new SkySynth(seed: 3, PlanetTheme.Chime) { MusicVolume = 1 };
        s.SetMasterVolume(1);
        float peak = 0;
        for (var i = 0; i < 20; i++)
        {
            s.BellToll();
            s.AirJump();
            foreach (var x in s.Render(Frames(0.25)))
            {
                float.IsFinite(x).Should().BeTrue();
                peak = MathF.Max(peak, MathF.Abs(x));
            }

            s.ActiveVoices.Should().BeLessThanOrEqualTo(SkySynth.MaxVoices);
        }

        output.WriteLine($"twenty tolls in 5 s: peak {peak:0.000}");
        peak.Should().BeLessThan(1f);
    }
}
