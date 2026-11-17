using FileScape.Core.Interaction;

namespace FileScape.Core.Tests;

/// <summary>PLANET-47 (AD-60): the sound-off preference is saved, read back, defaults to on, and older files keep the sound on.</summary>
public class SoundPreferenceTests
{
    [Fact]
    public void Sound_off_round_trips_and_defaults_to_on()
    {
        Preferences.Default.SoundMuted.Should().BeFalse("FileScape starts with its sound on");
        var muted = Preferences.Default with { SoundMuted = true };
        muted.Should().NotBe(Preferences.Default, "equality includes the sound");
        var back = Preferences.FromJson(muted.ToJson(), out var problem);
        problem.Should().BeNull();
        back.SoundMuted.Should().BeTrue();
        back.Should().Be(muted);
        muted.Describe().Should().EndWith("; sound off");
        Preferences.Default.Describe().Should().NotContain("sound", "the summary only mentions a silenced FileScape");
        Preferences.FromJson("{\"version\":1,\"showWelcome\":false}", out _).SoundMuted.Should().BeFalse("an older file has no sound setting: sound on");
        Preferences.FromJson("{\"soundMuted\":\"yes\"}", out _).SoundMuted.Should().BeFalse("a malformed value is ignored");
    }
}
