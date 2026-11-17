using System.Numerics;
using FileScape.Core.Planets;
using FileScape.Core.Shell;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>PLANET-43: a refused open says what happened; the test shell can fake one without launching anything.</summary>
public class FileActionTests
{
    [Fact]
    public void A_refused_open_names_the_file_and_the_reason_in_plain_words()
    {
        var m = ShellErrors.OpenFailed(@"C:\src\FileScape\FileScape.sln", 1155, "No application is associated with the specified file for this operation.");
        m.Should().StartWith("Could not open FileScape.sln: no app is set to open .sln files on this PC");
        m.Should().Contain("error 1155").And.Contain("No application is associated");
        ShellErrors.OpenFailed(@"\\wsl.localhost\Ubuntu\home\x\notes.md", 2, "The system cannot find the file specified.").Should().Contain("notes.md: it is no longer there");
        ShellErrors.OpenFailed(@"C:\x\secret.txt", 5, "Access is denied.").Should().Contain("Windows denied access");
        ShellErrors.OpenFailed(@"C:\x\Folder\", 1155, "x").Should().Contain("Could not open Folder:");
        ShellErrors.OpenFailed(@"C:\x\Makefile", 1155, "x").Should().Contain("this kind of item");
    }

    [Fact]
    public void The_recording_shell_fails_once_when_asked_and_launches_nothing()
    {
        var shell = new RecordingShellActions { FailNextOpenWith = 1155 };
        var r = shell.Open(@"C:\p\Lantern.sln");
        r.Succeeded.Should().BeFalse();
        r.WasMock.Should().BeFalse();
        r.Message.Should().Contain("Could not open Lantern.sln");
        shell.Open(@"C:\p\Lantern.sln").Succeeded.Should().BeTrue("the failure is for the next open only");
        shell.Log.Should().Equal(@"open-failed C:\p\Lantern.sln", @"open C:\p\Lantern.sln");
    }
}

/// <summary>PLANET-42: a repository's workbench stands in the arrival composition, on the first launcher ring.</summary>
public class ArrivalCompositionTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(12, true)]
    public void The_workbench_takes_a_first_ring_slot_front_right_and_every_folder_keeps_its_pad(int folders, bool underParent)
    {
        var proj = new Spec("proj").Dir(new Spec(".git")).File("README.md").File("proj.sln");
        for (var i = 0; i < folders; i++) proj = proj.Dir(new Spec($"f{i:00}"));
        var t = underParent ? new Spec("home").Dir(proj).Build() : proj.Build();
        var id = t.Find("proj").Id;
        var c = PlanetComposer.Compose(t, id);
        var r = c.Shape.Radius;
        var ov = c.Overlook;
        var fromArrival = PlanetShape.Angle(c.Arrival.Up, ov.Direction) * r;
        fromArrival.Should().BeApproximately(PlanetParams.Default.RingFirst, 0.05f, "on the first ring, in view from the arrival");
        var az = PlanetFrame.AzimuthOf(ov.Direction) * 180 / MathF.PI;
        az.Should().BeInRange(-90f, 90f, "in front of you, not behind");
        c.Launchers.Count(l => !l.IsParent).Should().Be(folders + 1, "every folder (and .git) keeps a pad");
        foreach (var l in c.Launchers)
        {
            (PlanetShape.Angle(l.Direction, ov.Direction) * r).Should().BeGreaterThan(ov.Radius + l.PadRadius + 1f, "no pad under or against the bench");
        }

        c.Items.Should().NotContain(i => c.Surface.SurfaceDistance(i.Direction, ov.Direction) < ov.Radius + i.Radius, "nothing stands on the bench");
        (PlanetShape.Angle(c.Landmark.Direction, ov.Direction) * r).Should().BeGreaterThan(ov.Radius + c.Landmark.Radius, "the landmark stands apart");
    }

    [Fact]
    public void A_plain_folder_keeps_its_overlook_by_the_landmark()
    {
        var t = new Spec("plain").Dir(new Spec("a")).Dir(new Spec("b")).File("README.md").Build();
        var c = PlanetComposer.Compose(t, 0);
        var p = PlanetParams.Default;
        (PlanetShape.Angle(c.Landmark.Direction, c.Overlook.Direction) * c.Shape.Radius).Should().BeApproximately(p.LandmarkRadius + p.LandmarkOverlookGap + p.OverlookRadius, 0.05f);
    }
}
