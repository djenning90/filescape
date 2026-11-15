using FileScape.Core.Model;
using FileScape.Core.Scanning;

namespace FileScape.Core.Tests;

/// <summary>
/// Marks a test that exercises real Windows filesystem or shell behaviour. On other hosts (such as WSL) it is skipped
/// with a visible reason, never passed: a Linux run says nothing about NTFS.
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows-only: run with Windows dotnet.exe (see docs/windows-workflow.md).";
        }
    }
}

/// <summary>
/// Marks a test that needs POSIX symbolic links without privileges (Linux/WSL). Skipped with a reason on Windows; the
/// Windows link cases (junctions) are covered by Windows-only tests.
/// </summary>
public sealed class PosixFactAttribute : FactAttribute
{
    public PosixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "POSIX-only (unprivileged symlinks): run with tools/fs.sh test-wsl. Windows link cases: WindowsWorldTests.";
        }
    }
}

/// <summary>A Windows test against a prepared WSL fixture (tools/make-wsl-fixture.sh, FILESCAPE_WSL_FIXTURE); skipped with a reason otherwise.</summary>
public sealed class WslFixtureFactAttribute : FactAttribute
{
    public WslFixtureFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows-only: reads WSL through \\\\wsl.localhost.";
        }
        else if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FILESCAPE_WSL_FIXTURE")))
        {
            Skip = "Set FILESCAPE_WSL_FIXTURE to the \\\\wsl.localhost path printed by tools/make-wsl-fixture.sh.";
        }
    }
}

/// <summary>
/// Test helpers for running a scan source the way the app does (start, wait, apply every batch in order) and for
/// finding nodes by name in the resulting tree. <see cref="Find"/> expects the name to be unique in the tree.
/// </summary>
public static class ScanRunner
{
    /// <summary>Runs a source to completion synchronously and applies every batch, as the app does on its main thread.</summary>
    public static (FsTree Tree, ScanProgress Progress) Run(IScanSource source, int maxNodes = ScanSession.DefaultMaxNodes)
    {
        using var session = new ScanSession(source, maxNodes);
        var tree = new FsTree(session.Source);
        session.Start();
        session.WaitForExit(TimeSpan.FromMinutes(2)).Should().BeTrue("the scan should finish");
        while (session.TryDequeue(out var batch))
        {
            tree.Apply(batch);
        }

        return (tree, session.Snapshot());
    }

    public static FsNode Find(this FsTree tree, string name) =>
        tree.Nodes.Single(n => n.Name == name);

    public static FsNode? FindOrNull(this FsTree tree, string name) =>
        tree.Nodes.SingleOrDefault(n => n.Name == name);
}
