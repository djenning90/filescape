using System.Runtime.InteropServices;
using FileScape.Core.Model;

namespace FileScape.Core.Scanning;

/// <summary>
/// Identifies what a reparse point actually is from its tag (LINK-01, AD-18) instead of treating every reparse point
/// as a symbolic link. Directory enumeration does not expose the tag, so it is read per reparse entry with
/// FindFirstFileExW (dwReserved0) on Windows. On other platforms a reparse point is a POSIX symbolic link.
/// </summary>
public static class Reparse
{
    // The reparse tags FileScape tells apart (IO_REPARSE_TAG_* in winnt.h; the values are fixed by Windows, not tuning).
    public const uint TagMountPoint = 0xA0000003;
    public const uint TagSymlink = 0xA000000C;
    public const uint TagLxSymlink = 0xA000001D;
    public const uint TagAppExecLink = 0x8000001B;
    public const uint TagAfUnix = 0x80000023;
    public const uint TagLxFifo = 0x80000024;
    public const uint TagLxChr = 0x80000025;
    public const uint TagLxBlk = 0x80000026;

    /// <summary>Cloud Files tags are 0x9000x01A (the x varies by provider subtype).</summary>
    public static bool IsCloudTag(uint tag) => (tag & 0xFFFF0FFF) == 0x9000001A;

    /// <summary>How an entry should be represented, given its attributes, whether it is a directory and its tag.</summary>
    public readonly record struct Classification(NodeKind Kind, NodeFlags ExtraFlags, LinkInfo? Link);

    /// <summary>
    /// Classifies a reparse entry. <paramref name="tag"/> is null when unknown (non-Windows): treated as a symbolic link.
    /// <paramref name="readTarget"/> returns the link's target text (or null) and is only called for real links.
    /// </summary>
    public static Classification Classify(bool isDirectory, uint? tag, Func<string?> readTarget)
    {
        if (tag is null)
        {
            return new(NodeKind.Link, isDirectory ? NodeFlags.DirectoryLink : 0, new LinkInfo(LinkKind.SymbolicLink, 0, readTarget()));
        }

        var t = tag.Value;
        switch (t)
        {
            case TagSymlink:
                return new(NodeKind.Link, isDirectory ? NodeFlags.DirectoryLink : 0, new LinkInfo(LinkKind.SymbolicLink, t, readTarget()));
            case TagMountPoint:
            {
                var target = readTarget();
                var kind = target != null && (target.Contains("Volume{", StringComparison.OrdinalIgnoreCase)) ? LinkKind.MountedVolume : LinkKind.Junction;
                return new(NodeKind.Link, NodeFlags.DirectoryLink, new LinkInfo(kind, t, target));
            }

            case TagLxSymlink:
                // Windows cannot read or follow these through the 9P redirector (AD-18): kept as identified, unsupported links.
                return new(NodeKind.Link, 0, new LinkInfo(LinkKind.LinuxSymlink, t, null, LinkStatus.Unsupported,
                    Message: "A Linux symbolic link. Windows' WSL access (\\\\wsl.localhost) does not expose where it points, so FileScape cannot follow it."));
            case TagAppExecLink:
                return new(NodeKind.File, NodeFlags.AppAlias, null);
            case TagAfUnix or TagLxFifo or TagLxChr or TagLxBlk:
                return new(NodeKind.File, NodeFlags.LinuxSpecial, null);
        }

        if (IsCloudTag(t))
        {
            // A file or folder managed by a sync provider is ordinary, not a link. The tag says only "synced": OneDrive keeps
            // it on files that are on this PC too, so a file is a placeholder only by its recall/offline attributes
            // (DirectoryScanner.FlagsFor). Before PLANET-135 every synced file was called a placeholder, so every photo in
            // a OneDrive Pictures folder carried a "may download" warning. Folders keep the placeholder caution unchanged
            // (Overview discovery leaves cloud folders alone).
            return isDirectory
                ? new(NodeKind.Directory, NodeFlags.CloudPlaceholder | NodeFlags.CloudSynced, null)
                : new(NodeKind.File, NodeFlags.CloudSynced, null);
        }

        // Other tags (deduplication, compression providers, containers…): files behave as files; directories are
        // provider-specific and are not entered, honestly labelled with their tag.
        return isDirectory
            ? new(NodeKind.Link, NodeFlags.DirectoryLink, new LinkInfo(LinkKind.Other, t, null, LinkStatus.Unsupported,
                Message: $"A provider-specific reparse point (tag 0x{t:X8}). FileScape does not enter it."))
            : new(NodeKind.File, 0, null);
    }

    /// <summary>The reparse tag of a path (Windows), or null if it cannot be read or this is not Windows.</summary>
    public static uint? ReadTag(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var h = FindFirstFileExW(path, 1 /* FindExInfoBasic */, out var data, 0, IntPtr.Zero, 0);
        if (h == new IntPtr(-1))
        {
            return null;
        }

        FindClose(h);
        return (data.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ? data.Reserved0 : null;
    }

    /// <summary>The target text of a symbolic link or junction, or null.</summary>
    public static string? ReadTarget(string path, bool isDirectory)
    {
        try
        {
            return isDirectory ? new DirectoryInfo(path).LinkTarget : new FileInfo(path).LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// WIN32_FIND_DATAW, laid out field for field for P/Invoke (260 = MAX_PATH, 14 = the 8.3 name buffer). Only
    /// <see cref="Attributes"/> and <see cref="Reserved0"/> (dwReserved0: the reparse tag when the entry is a reparse point) are read.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Win32FindData
    {
        public uint Attributes;
        public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint SizeHigh, SizeLow;
        public uint Reserved0, Reserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string FileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string AlternateFileName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileExW(string fileName, int infoLevel, out Win32FindData data, int searchOp, IntPtr filter, int flags);

    [DllImport("kernel32.dll")]
    private static extern bool FindClose(IntPtr handle);
}
