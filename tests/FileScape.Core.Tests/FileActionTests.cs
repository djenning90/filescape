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
