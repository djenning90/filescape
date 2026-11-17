using System.Diagnostics;
using System.Runtime.InteropServices;
using FileScape.Core.Scanning;
using FileScape.Core.Shell;
using FluentAssertions;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>
/// PLANET-45 (AD-58): what the root landmark's panel offers — the terminal command for a folder, the forms of its path,
/// the useful places — and, on Windows, the directory each terminal command really starts in. The Windows tests start the
/// same argument lists the app would (joined by <see cref="WindowsShellActions.CommandLine"/>), with a command that prints
/// the directory and exits instead of an interactive shell: no window opens. Fixture folders with spaces and an
/// apostrophe live under %TEMP%\FileScape-fixtures and /tmp/filescape-wsl-fixtures and are left there (empty folders,
/// reused by the planets-landmarks probe).
/// </summary>
public class DeveloperActionsTests
{
    // ------------------------------------------------------------------ the commands (any platform)

    [Fact]
    public void A_wsl_folder_opens_its_own_distribution_in_its_linux_path()
    {
        var t = TerminalLaunch.For(@"\\wsl.localhost\Ubuntu-24.04\tmp\filescape-wsl-fixtures\dir with spaces", windowsTerminal: @"C:\wt.exe")!;
        t.Program.Should().Be("wsl.exe", "a Linux folder opens a Linux shell, never through Windows Terminal's Windows profile");
        t.Args.Should().Equal("-d", "Ubuntu-24.04", "--cd", "/tmp/filescape-wsl-fixtures/dir with spaces");
        t.StartsIn.Should().Be("/tmp/filescape-wsl-fixtures/dir with spaces");
        TerminalLaunch.For(@"\\wsl$\Debian", null)!.Args.Should().Equal("-d", "Debian", "--cd", "/");
        WindowsShellActions.CommandLine(t.Args).Should().Be("-d Ubuntu-24.04 --cd \"/tmp/filescape-wsl-fixtures/dir with spaces\"");
    }

    [Fact]
    public void A_windows_folder_opens_windows_terminal_when_installed_else_powershell()
    {
        var wt = TerminalLaunch.For(@"C:\Users\dj\my project", @"C:\Users\dj\AppData\Local\Microsoft\WindowsApps\wt.exe")!;
        wt.Program.Should().EndWith("wt.exe");
        wt.Args.Should().Equal("-d", @"C:\Users\dj\my project");
        TerminalLaunch.For("D:", @"C:\wt.exe")!.Args.Should().Equal("-d", @"D:\");
        TerminalLaunch.For(@"C:\odd;name", @"C:\wt.exe")!.Args.Should().Equal("-d", @"C:\odd\;name");
        var ps = TerminalLaunch.For(@"\\server\share\it's here", null)!;
        ps.Program.Should().Be("powershell.exe");
        ps.Args.Should().Equal("-NoLogo", "-NoExit", "-Command", @"Set-Location -LiteralPath '\\server\share\it''s here'");
        TerminalLaunch.For("/home/dj", null).Should().BeNull("a POSIX path on Linux: no terminal is offered");
    }

    [Fact]
    public void The_path_is_offered_in_the_native_form_first_and_the_other_sides_form_second()
    {
        PathForms.For(@"\\wsl.localhost\Ubuntu-24.04\home\dj\src").Select(f => f.Text).Should().Equal("/home/dj/src", @"\\wsl.localhost\Ubuntu-24.04\home\dj\src");
        PathForms.For(@"\\wsl$\Ubuntu-24.04").Select(f => f.Text).Should().Equal("/", @"\\wsl.localhost\Ubuntu-24.04\");
        PathForms.For(@"C:\Users\dj\a b").Select(f => f.Text).Should().Equal(@"C:\Users\dj\a b", "/mnt/c/Users/dj/a b");
        PathForms.For(@"D:\").Select(f => f.Text).Should().Equal(@"D:\", "/mnt/d");
        PathForms.For(@"\\server\share").Select(f => f.Text).Should().Equal(@"\\server\share");
    }

    [Fact]
    public void Useful_places_are_the_filesystems_own_and_leave_out_where_you_are()
    {
        var wsl = RootPlaces.ForWsl("Ubuntu-24.04", "/");
        wsl.Select(p => p.Label).Should().Equal("~  home", "/tmp", "/mnt/c");
        wsl[0].Path.Should().Be(RootPlaces.WslHomeMarker, "home is resolved only when chosen");
        wsl[1].Path.Should().Be(@"\\wsl.localhost\Ubuntu-24.04\tmp");
        wsl[2].Path.Should().Be(@"C:\", "/mnt/c is the Windows drive itself, listed natively");
        RootPlaces.ForWsl("Ubuntu-24.04", "/tmp/x").Select(p => p.Label).Should().Contain("/  the root");
        var known = new Dictionary<string, string> { [@"C:\Users\dj"] = "Home", [@"C:\Users\dj\Documents"] = "Documents", [@"C:\Users\dj\Music"] = "Music", [@"D:\Downloads"] = "Downloads" };
        RootPlaces.ForWindows(@"C:\", known).Select(p => p.Label).Should().Equal("Home", "Documents", "Downloads");
        RootPlaces.ForWindows(@"C:\Users\dj", known).Select(p => p.Label).Should().Equal(@"C:\", "Documents", "Downloads");
    }

    [Fact]
    public void Home_comes_from_the_default_users_passwd_line()
    {
        const string passwd = "root:x:0:0:root:/root:/bin/bash\nnobody:x:65534:65534:nobody:/nonexistent:/usr/sbin/nologin\ndj:x:1000:1000:DJ,,,:/home/dj:/bin/bash\r\n";
        WslHome.FromPasswd(passwd, 1000).Should().Be("/home/dj");
        WslHome.FromPasswd(passwd, 0).Should().Be("/root");
        WslHome.FromPasswd(passwd, 1001).Should().BeNull();
        WslHome.FromPasswd("garbage\n::::\n", 1000).Should().BeNull();
    }

    [Fact]
    public void The_recording_shell_logs_the_program_and_its_command_line()
    {
        var shell = new RecordingShellActions();
        var t = TerminalLaunch.For(@"\\wsl.localhost\Ubuntu-24.04\tmp\a b", null)!;
        shell.Start(t.Program, t.Args).Succeeded.Should().BeTrue();
        shell.Log.Should().Equal("start wsl.exe -d Ubuntu-24.04 --cd \"/tmp/a b\"");
    }

    // ------------------------------------------------------------------ the directory each really starts in (Windows)

    /// <summary>Fixture folder names: spaces, an apostrophe and an ampersand (each special to some shell).</summary>
    public static IEnumerable<object[]> FixtureNames() => [["dir with spaces"], ["it's here & there"]];

    [WindowsTheory]
    [MemberData(nameof(FixtureNames))]
    public void PowerShell_starts_in_the_windows_folder(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "FileScape-fixtures", name);
        Directory.CreateDirectory(dir);
        var interactive = TerminalLaunch.For(dir, windowsTerminal: null)!;
        interactive.Args.Should().Contain("-NoExit").And.Contain(TerminalLaunch.SetLocation(dir), "the person's terminal runs the same Set-Location");
        var printed = Run(TerminalLaunch.PowerShellExe, TerminalLaunch.PowerShellArgs(dir, keepOpen: false, then: "(Get-Location).ProviderPath"));
        printed.Should().Be(dir);
    }

    [WindowsTheory]
    [MemberData(nameof(FixtureNames))]
    public void Windows_terminal_receives_the_folder_as_one_argument(string name)
    {
        // Windows Terminal cannot be started without a window; what it receives is checked instead: the command line the
        // app passes, split the way Windows splits it for the program (CommandLineToArgvW).
        var dir = Path.Combine(Path.GetTempPath(), "FileScape-fixtures", name);
        var t = TerminalLaunch.For(dir, @"C:\wt.exe")!;
        Split("wt.exe " + WindowsShellActions.CommandLine(t.Args)).Should().Equal("wt.exe", "-d", dir);
    }

    [WslFixtureTheory]
    [MemberData(nameof(FixtureNames))]
    public void Wsl_starts_in_the_linux_folder(string name)
    {
        // The distribution the fixture lives in, as the app knows distributions (Locations, from the registry).
        var fixtureDistro = EditorLaunch.Distribution(Environment.GetEnvironmentVariable("FILESCAPE_WSL_FIXTURE")!);
        var distro = Locations.WslDistributions().Single(d => d.Equals(fixtureDistro, StringComparison.OrdinalIgnoreCase));
        var unc = Locations.WslPrefix + distro + @"\tmp\filescape-wsl-fixtures\" + name;
        Directory.CreateDirectory(unc);
        var t = TerminalLaunch.For(unc, TerminalLaunch.FindWindowsTerminal())!;
        t.Program.Should().Be(TerminalLaunch.WslExe);
        // The very arguments the app starts, with `pwd` as the command to run there instead of the login shell.
        Run(t.Program, [.. t.Args, "pwd"]).Should().Be("/tmp/filescape-wsl-fixtures/" + name);
    }

    /// <summary>Runs a program with the app's command line (no window, output captured), waits up to 60 s, returns its trimmed output.</summary>
    private static string Run(string program, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(program, WindowsShellActions.CommandLine(args))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(60_000))
        {
            p.Kill(entireProcessTree: true); // only the process this test started
            throw new TimeoutException($"{program} did not finish");
        }

        p.ExitCode.Should().Be(0, error.Result);
        return output.Result.Trim();
    }

    /// <summary>The argument vector Windows gives a program for <paramref name="commandLine"/>.</summary>
    private static string[] Split(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var n);
        try
        {
            var result = new string[n];
            for (var i = 0; i < n; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!;
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

#pragma warning disable SYSLIB1054 // a test helper; DllImport keeps it short
    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string cmdLine, out int numArgs);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);
#pragma warning restore SYSLIB1054
}

/// <summary>A Windows-only theory (skipped with a reason elsewhere, never passed), like <see cref="WindowsFactAttribute"/>.</summary>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows-only: run with Windows dotnet.exe (see docs/windows-workflow.md).";
    }
}

/// <summary>A Windows theory against the prepared WSL fixture (FILESCAPE_WSL_FIXTURE), like <see cref="WslFixtureFactAttribute"/>.</summary>
public sealed class WslFixtureTheoryAttribute : TheoryAttribute
{
    public WslFixtureTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows-only: starts wsl.exe.";
        else if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FILESCAPE_WSL_FIXTURE"))) Skip = "Set FILESCAPE_WSL_FIXTURE to the \\\\wsl.localhost path printed by tools/make-wsl-fixture.sh.";
    }
}
