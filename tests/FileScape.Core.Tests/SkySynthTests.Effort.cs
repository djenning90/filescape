using FileScape.Core.Audio;
using FileScape.Core.Planets;
using static FileScape.Core.Tests.SkySynthKit;
using Xunit.Abstractions;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-72, AD-69: a Hazard World's last effort is heard over what is playing when it comes — the hazard score and the
/// fall's own roar (DJ, 2026-11-19: "pressing Space produced no visible effort"). Measured as the existing cue checks are:
/// the effort rendered alone against the same moment with the music and the fall, the difference being what it competes with.
/// </summary>
public class SkySynthEffortTests(ITestOutputHelper output)
{
    /// <summary>The effort is measured over this long from its start, seconds (its figure and most of its glide).</summary>
    private const double EffortWindowSeconds = 0.35;

    [Theory]
    [InlineData(MusicIdentity.Hazard, 6)]
    [InlineData(MusicIdentity.Hazard, 15)]
    public void A_last_effort_stands_clear_of_the_fall_and_the_hazard_score(MusicIdentity id, int bar)
    {
        var at = bar * 16 * ThemeFramesPerSixteenth(id) / Rate + 0.05;
        const int fallFramesBefore = 18; // the effort comes 0.3 s into the fall (60 Hz frames)
        float[] Run(bool background)
        {
            var s = SynthFor(30, PlanetTheme.Hearth, id, musicVolume: background ? 1 : 0);
            RenderSeconds(s, at);
            if (background) s.FallStart();
            for (var f = 0; f < fallFramesBefore; f++)
            {
                if (background) s.Fall(f / 156f);
                s.Render(735);
            }

            s.LastEffort();
            var all = new List<float>();
            for (var f = 0; f < (int)(EffortWindowSeconds * 60); f++)
            {
                if (background) s.Fall((fallFramesBefore + f) / 156f); // 2.6 s of fall at 60 Hz
                all.AddRange(s.Render(735));
            }

            return [.. all];
        }

        var cue = Run(false);
        var with = Run(true);
        double c = 0, m = 0;
        for (var i = 0; i < cue.Length; i++)
        {
            c += cue[i] * (double)cue[i];
            m += Math.Pow(with[i] - cue[i], 2);
        }

        var snr = 10 * Math.Log10(c / Math.Max(1e-12, m));
        output.WriteLine($"{id} bar {bar}: a last effort 0.3 s into the fall stands {snr:+0.0;-0.0} dB over the fall and the ducked music");
        snr.Should().BeGreaterThan(3, "the effort stands clear of everything it plays over (+3.5 dB measured, 2026-11-19)");
    }
}
