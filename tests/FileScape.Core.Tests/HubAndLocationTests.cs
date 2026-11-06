using FileScape.Core.Scanning;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>LOC-06: "This PC" and its other names are the hub, never a path; ordinary paths stay paths.</summary>
public class HubAndLocationTests
{
    [Theory]
    [InlineData("This PC")]
    [InlineData("this computer")]
    [InlineData("  Computer ")]
    [InlineData("My Computer")]
    [InlineData("hub")]
    [InlineData("shell:MyComputerFolder")]
    [InlineData("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    [InlineData("shell:::{20d04fe0-3aea-1069-a2d8-08002b30309d}")]
    [InlineData("\"This PC\"")]
    public void This_PC_and_its_aliases_name_the_hub(string input)
    {
        var r = LocationRequest.Parse(input)!;
        r.IsHub.Should().BeTrue();
        r.Path.Should().BeNull("the hub has no filesystem path and none is invented");
    }

    [Theory]
    [InlineData(@"C:\Users\Public")]
    [InlineData(@"D:\")]
    [InlineData(@"\\wsl.localhost\Ubuntu-24.04\home")]
    [InlineData("/home/someone/src")]
    [InlineData(@"C:\This PC")]
    [InlineData("Computer Science")]
    public void Ordinary_paths_stay_paths(string input)
    {
        var r = LocationRequest.Parse(input)!;
        r.Kind.Should().Be(LocationTargetKind.Path);
        r.Path.Should().Be(input.Trim());
    }

    [Fact]
    public void Blank_input_is_no_request() => LocationRequest.Parse("   ").Should().BeNull();
}
