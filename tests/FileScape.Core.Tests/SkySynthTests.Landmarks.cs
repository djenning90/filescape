using FileScape.Core.Audio;
using FileScape.Core.Planets;
using static FileScape.Core.Tests.SkySynthKit;
using Xunit.Abstractions;

namespace FileScape.Core.Tests;

/// <summary>
/// The lighthouse's call and the scenery's replies (PLANET-74/75, AD-70): on the music's grid, every note in the world's
/// key, brief (gone well within 2 s, so a repeat is pleasant) and never loud.
/// </summary>
public class SkySynthLandmarkTests
{
    public static TheoryData<int> LandmarkReplies() => new(-1, 0, 1, 2); // −1: the lighthouse; 0..2: SceneryVoice

    [Trait(TestKinds.Trait, TestKinds.Slow)]
    [Theory]
    [MemberData(nameof(LandmarkReplies))]
    public void Landmark_and_scenery_replies_are_on_the_grid_in_key_and_brief(int which)
    {
        foreach (var id in new[] { MusicIdentity.Hub, MusicIdentity.Trails, MusicIdentity.DeepWoods, MusicIdentity.Hazard })
        {
            var grid = FramesPerSixteenth(id) / 2;
            foreach (var (preroll, variant) in new[] { (1000, 0), (23_456, 7), (51_000, -4) })
            {
                var s = SynthFor(17, PlanetTheme.Hearth, id, musicVolume: 0);
                s.Render(preroll);
                if (which < 0) s.LighthouseCall();
                else s.SceneryReply((SceneryVoice)which, variant);
                var buf = s.Render(Frames(4.0));
                var first = FirstSound(buf, preroll);
                first.Should().BeGreaterThan(0, $"{id}/{which}: it sounds");
                Math.Abs(first - Math.Round(first / grid) * grid).Should().BeLessThan(8, $"{id}/{which}: on the 32nd grid (preroll {preroll})");
                s.LastLandmarkReply.Frame.Should().BeCloseTo(first, 2);

                var key = s.Key;
                var scale = key.IntervalsSemitones.Select(i => (key.RootMidiNote + i) % 12).ToHashSet();
                s.LastLandmarkReply.MidiNotes.Should().NotBeEmpty().And.OnlyContain(n => scale.Contains(((n % 12) + 12) % 12), $"{id}/{which}: every note is in {key.Mode} on {key.RootMidiNote}");

                double Rms(double fromSeconds, double toSeconds)
                {
                    var a = (int)((fromSeconds * Rate) + (first - preroll)) * 2;
                    var b = (int)((toSeconds * Rate) + (first - preroll)) * 2;
                    double sum = 0;
                    for (var i = a; i < b; i++) sum += buf[i] * (double)buf[i];
                    return Math.Sqrt(sum / (b - a));
                }

                var loud = Rms(0, 0.3);
                // Brief: by 2.5 s the reply has died to under a tenth of its first 0.3 s (pleasant to repeat, never a drone).
                Rms(2.5, 3.0).Should().BeLessThan(loud * 0.1, $"{id}/{which}: it is brief");
                buf.Max(MathF.Abs).Should().BeLessThan(0.35f, $"{id}/{which}: and never loud");
            }
        }
    }

    [Fact]
    public void A_scenery_reply_varies_by_piece_and_repeats_the_same_for_the_same_piece()
    {
        var s = SynthFor(3, PlanetTheme.Hearth, MusicIdentity.Hub, musicVolume: 0);
        s.SceneryReply(SceneryVoice.Crystal, 0);
        var a = s.LastLandmarkReply.MidiNotes.ToList();
        s.Render(Frames(1.0));
        s.SceneryReply(SceneryVoice.Crystal, 1);
        var b = s.LastLandmarkReply.MidiNotes.ToList();
        s.Render(Frames(1.0));
        s.SceneryReply(SceneryVoice.Crystal, 3);
        var c = s.LastLandmarkReply.MidiNotes.ToList();
        b.Should().NotEqual(a, "a neighbour answers on another degree");
        c.Should().Equal(a, "variants 0 and 3 are the same degree (three degrees in turn)");
    }
}
