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
