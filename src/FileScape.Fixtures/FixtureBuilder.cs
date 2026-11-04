using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace FileScape.Fixtures;

/// <summary>What the fixture contains, so tests and people can check a scan against it.</summary>
public sealed class FixtureManifest
{
    public required string Root { get; init; }

    /// <summary>Counts as the scanner should see them: entries inside the access-denied directory are excluded, the marker file included.</summary>
    public int ExpectedDirectories { get; set; } = 1;
    public int ExpectedFiles { get; set; }
    public int ExpectedLinks { get; set; }
    public long ExpectedLogicalBytes { get; set; }

    public string? JunctionLoop { get; set; }
    public string? DirectorySymlink { get; set; }
    public string? FileSymlink { get; set; }
    public string? DeniedDirectory { get; set; }
    public string? VanishingDirectory { get; set; }
    public string? SparseFile { get; set; }
    public string? HardLink { get; set; }
    public long SparseLogicalSize { get; set; }
    public string? LongPathFile { get; set; }
    public List<string> UnicodeNames { get; } = [];
    public List<string> HiddenOrSystem { get; } = [];
    public List<string> Skipped { get; } = [];

    public override string ToString() =>
        $"Fixture at {Root}\n  expected (visible to scan): {ExpectedDirectories} dirs, {ExpectedFiles} files, {ExpectedLinks} links, {ExpectedLogicalBytes:N0} logical bytes\n" +
        $"  junction loop: {JunctionLoop ?? "-"}\n  dir symlink: {DirectorySymlink ?? "-"}\n  file symlink: {FileSymlink ?? "-"}\n" +
        $"  access denied: {DeniedDirectory ?? "-"}\n  sparse: {SparseFile ?? "-"} ({SparseLogicalSize:N0} B)\n  long path: {LongPathFile?.Length.ToString() ?? "-"} chars\n" +
        (Skipped.Count > 0 ? "  skipped: " + string.Join("; ", Skipped) + "\n" : "");
}

/// <summary>
/// Creates a deterministic edge-case tree inside a directory FileScape owns. The fixture is identified by a marker
/// file, and cleanup refuses to touch any directory without that marker. The only permission change is a deny ACE
/// on the fixture's own "denied" directory, and it is removed before deletion.
/// </summary>
public static class FixtureBuilder
{
    public const string MarkerName = ".filescape-fixture";

    public static readonly string[] UnicodeFileNames =
    [
        "日本語のファイル.txt", "Ελληνικά σημειώσεις.md", "emoji-🚀-launch.log", "עברית-קובץ.txt",
        "combining-éclair.txt", "кириллица-отчёт.docx", "한국어-문서.pdf", "mixed-Ꮳℌℰℛ🜲.dat",
    ];

    public static string DefaultParent => Path.Combine(Path.GetTempPath(), "FileScape-fixtures");

    public static FixtureManifest Create(string? parent = null, bool includeVanishing = true)
    {
        parent ??= DefaultParent;
        var root = Path.Combine(parent, "fixture-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(root);
        var m = new FixtureManifest { Root = root };
        WriteFile(m, Path.Combine(root, MarkerName), "FileScape test fixture. Safe to delete.\n"u8.ToArray());

        // Wide: many siblings in one directory.
        var wide = MakeDir(m, root, "wide-600-files");
        for (var i = 0; i < 600; i++)
        {
            WriteFile(m, Path.Combine(wide, $"item-{i:D4}.txt"), new byte[i % 7 * 100]);
        }

        // Deep: a 30-level chain.
        var cursor = MakeDir(m, root, "deep-30-levels");
        for (var level = 1; level <= 30; level++)
        {
            cursor = MakeDir(m, cursor, $"level-{level:D2}");
            WriteFile(m, Path.Combine(cursor, $"note-{level:D2}.txt"), new byte[level * 10]);
        }

        var zero = MakeDir(m, root, "zero-byte");
        for (var i = 0; i < 50; i++)
        {
            WriteFile(m, Path.Combine(zero, $"empty-{i:D2}.lock"), []);
        }

        var unicode = MakeDir(m, root, "unicode-名前-Ünïcödé");
        foreach (var name in UnicodeFileNames)
        {
            WriteFile(m, Path.Combine(unicode, name), new byte[name.Length * 3]);
            m.UnicodeNames.Add(name);
        }

        // Long path: more than 300 characters overall.
        cursor = MakeDir(m, root, "long-path");
        for (var i = 0; cursor.Length < 320; i++)
        {
            cursor = MakeDir(m, cursor, $"a-rather-long-directory-name-segment-{i:D2}-ünicode");
        }

        m.LongPathFile = Path.Combine(cursor, "deeply-nested-file-beyond-max-path.txt");
        WriteFile(m, m.LongPathFile, new byte[1234]);

        var outliers = MakeDir(m, root, "size-outliers");
        WriteFile(m, Path.Combine(outliers, "one-byte.bin"), [42]);
        WriteFile(m, Path.Combine(outliers, "one-mib.bin"), new byte[1 << 20]);
        CreateSparse(m, Path.Combine(outliers, "sparse-10-gib.img"), 10L << 30);
        if (OperatingSystem.IsWindows())
        {
            // A second name for one-mib.bin. FileScape counts each name, so totals double count it (FS-07).
            var hard = Path.Combine(outliers, "hard-link-to-one-mib.bin");
            if (CreateHardLinkW(hard, Path.Combine(outliers, "one-mib.bin"), IntPtr.Zero))
            {
                m.HardLink = hard;
                m.ExpectedFiles++;
                m.ExpectedLogicalBytes += 1 << 20;
            }
            else
            {
                m.Skipped.Add($"hard link (error {Marshal.GetLastWin32Error()})");
            }
        }

        var special = MakeDir(m, root, "special");
        var hidden = Path.Combine(special, "hidden-file.txt");
        WriteFile(m, hidden, "hidden"u8.ToArray());
        File.SetAttributes(hidden, FileAttributes.Hidden);
        m.HiddenOrSystem.Add(hidden);
        var system = Path.Combine(special, "system-file.dat");
        WriteFile(m, system, "system"u8.ToArray());
        File.SetAttributes(system, FileAttributes.System | FileAttributes.Hidden);
        m.HiddenOrSystem.Add(system);
        var ro = Path.Combine(special, "read-only.txt");
        WriteFile(m, ro, "ro"u8.ToArray());
        File.SetAttributes(ro, FileAttributes.ReadOnly);

        if (OperatingSystem.IsWindows())
        {
            CreateJunction(m, Path.Combine(special, "junction-loop-to-fixture-root"), root);
            try
            {
                var dl = Path.Combine(special, "dir-symlink-to-wide");
                Directory.CreateSymbolicLink(dl, wide);
                m.DirectorySymlink = dl;
                m.ExpectedLinks++;
            }
            catch (IOException ex)
            {
                m.Skipped.Add($"directory symlink ({ex.Message.Trim()})");
            }
            catch (UnauthorizedAccessException ex)
            {
                m.Skipped.Add($"directory symlink ({ex.Message.Trim()})");
            }

            try
            {
                var fl = Path.Combine(special, "file-symlink-to-hidden.txt");
                File.CreateSymbolicLink(fl, hidden);
                m.FileSymlink = fl;
                m.ExpectedLinks++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                m.Skipped.Add($"file symlink ({ex.Message.Trim()})");
            }

            CreateDenied(m, Path.Combine(special, "access-denied"));
        }
        else
        {
            m.Skipped.Add("junction, symlinks and access-denied cases need Windows");
        }

        if (includeVanishing)
        {
            m.VanishingDirectory = MakeDir(m, special, "vanishing");
            WriteFile(m, Path.Combine(m.VanishingDirectory, "will-be-gone.txt"), [1, 2, 3]);
        }

        return m;
    }

    /// <summary>Deletes a fixture. It refuses any directory that lacks the marker, or one outside a "FileScape-fixtures" parent.</summary>
    public static void Delete(string root)
    {
        if (!File.Exists(Path.Combine(root, MarkerName)) ||
            !root.Contains("FileScape-fixtures", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to delete {root}: not a FileScape fixture.");
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var denied in new[] { Path.Combine(root, "special", "access-denied"), Path.Combine(root, PlacesFixture.RootName, "locked") })
            {
                if (Directory.Exists(denied))
                {
                    RunIcacls(denied, "/remove:d", "*" + CurrentUserSid());
                }
            }

            // Remove links first, so a recursive delete can never reach their targets.
            foreach (var link in new[] { Path.Combine("special", "junction-loop-to-fixture-root"), Path.Combine("special", "dir-symlink-to-wide"), Path.Combine(PlacesFixture.RootName, "link-to-documents") })
            {
                var p = Path.Combine(root, link);
                if (Directory.Exists(p))
                {
                    Directory.Delete(p, recursive: false);
                }
            }
        }

        var ro = Path.Combine(root, "special", "read-only.txt");
        if (File.Exists(ro))
        {
            File.SetAttributes(ro, FileAttributes.Normal);
        }

        Directory.Delete(root, recursive: true);
    }

    /// <summary>Deletes leftover fixtures (from interrupted runs) under the default parent. It only touches marked fixtures.</summary>
    public static int DeleteAllUnderDefaultParent()
    {
        if (!Directory.Exists(DefaultParent))
        {
            return 0;
        }

        var n = 0;
        foreach (var dir in Directory.GetDirectories(DefaultParent, "fixture-*").Concat(Directory.GetDirectories(DefaultParent, "places-*")))
        {
            if (File.Exists(Path.Combine(dir, MarkerName)))
            {
                Delete(dir);
                n++;
            }
        }

        return n;
    }

    internal static string MakeDir(FixtureManifest m, string parent, string name)
    {
        var path = Path.Combine(parent, name);
        Directory.CreateDirectory(path);
        m.ExpectedDirectories++;
        return path;
    }

    internal static void WriteFile(FixtureManifest m, string path, byte[] content)
    {
        File.WriteAllBytes(path, content);
        m.ExpectedFiles++;
        m.ExpectedLogicalBytes += content.Length;
    }

    internal static void CreateJunction(FixtureManifest m, string link, string target)
    {
        // mklink /J needs no elevation or Developer Mode.
        var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add("mklink");
        psi.ArgumentList.Add("/J");
        psi.ArgumentList.Add(link);
        psi.ArgumentList.Add(target);
        using var p = Process.Start(psi)!;
        p.WaitForExit(10_000);
        if (p.ExitCode == 0 && Directory.Exists(link))
        {
            m.JunctionLoop = link;
            m.ExpectedLinks++;
        }
        else
        {
            m.Skipped.Add($"junction (mklink exit {p.ExitCode}: {p.StandardError.ReadToEnd().Trim()})");
        }
    }

    internal static void CreateDenied(FixtureManifest m, string path)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "secret.txt"), "not visible to the scan");
        m.ExpectedDirectories++; // the directory itself is visible; its contents are not
        if (RunIcacls(path, "/deny", $"*{CurrentUserSid()}:(OI)(CI)(RX)"))
        {
            m.DeniedDirectory = path;
        }
        else
        {
            m.Skipped.Add("access-denied ACE (icacls failed)");
            m.ExpectedFiles++;
            m.ExpectedLogicalBytes += "not visible to the scan".Length;
        }
    }

    private static string CurrentUserSid() =>
        OperatingSystem.IsWindows() ? WindowsIdentity.GetCurrent().User!.Value : throw new PlatformNotSupportedException();

    private static bool RunIcacls(string path, string op, string arg)
    {
        var psi = new ProcessStartInfo("icacls.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(path);
        psi.ArgumentList.Add(op);
        psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi)!;
        p.WaitForExit(10_000);
        return p.ExitCode == 0;
    }

    internal static void CreateSparse(FixtureManifest m, string path, long length)
    {
        if (!OperatingSystem.IsWindows())
        {
            m.Skipped.Add("sparse file (Windows only)");
            return;
        }

        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);
        if (!DeviceIoControl(fs.SafeFileHandle, FsctlSetSparse, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
        {
            m.Skipped.Add($"sparse flag (error {Marshal.GetLastWin32Error()}); left as a 0-byte file");
            m.ExpectedFiles++;
            return;
        }

        fs.SetLength(length);
        m.SparseFile = path;
        m.SparseLogicalSize = length;
        m.ExpectedFiles++;
        m.ExpectedLogicalBytes += length;
    }

    private const uint FsctlSetSparse = 0x000900C4;

#pragma warning disable SYSLIB1054
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLinkW(string newFile, string existingFile, IntPtr security);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr inBuf, int inSize, IntPtr outBuf, int outSize, out int returned, IntPtr overlapped);
#pragma warning restore SYSLIB1054
}
