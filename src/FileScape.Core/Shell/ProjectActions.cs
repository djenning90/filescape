using System.Text;
using FileScape.Core.Places;
using FileScape.Core.Scanning;

namespace FileScape.Core.Shell;

/// <summary>The bounded result of reading a README deliberately (DEV-02).</summary>
public sealed record ReadmeText(string Path, string Text, bool Truncated, string? Error, string Format);

/// <summary>
/// DEV-02 (AD-33): reads a README only when the user asks, bounded in size, as text. Nothing in it is executed,
/// followed or fetched: links stay text, images become their alt text, HTML is shown as written.
/// </summary>
public static class Readme
{
    public const int MaxBytes = 256 * 1024;
    public const int MaxLines = 4000;

    public static readonly string[] Formats = ["", ".md", ".markdown", ".txt", ".rst", ".adoc", ".org"];

    /// <summary>True for README names FileScape will show (others are listed but not offered).</summary>
    public static bool IsSupported(string name) =>
        PlaceKinds.IsReadme(name) && Formats.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

    public static ReadmeText Read(string path, int maxBytes = MaxBytes)
    {
        var format = Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown" ? "markdown" : "plain text";
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16 * 1024, FileOptions.SequentialScan);
            var buf = new byte[Math.Min(maxBytes + 1, (int)Math.Min(f.Length + 1, int.MaxValue))];
            var n = 0;
            while (n < buf.Length && f.Read(buf, n, buf.Length - n) is var r and > 0) n += r;
            var truncated = n > maxBytes;
            if (truncated) n = maxBytes;
            if (Array.IndexOf(buf, (byte)0, 0, Math.Min(n, 8000)) >= 0) return new ReadmeText(path, "", false, "This file looks binary, so it is not shown as text.", format);
            var text = new UTF8Encoding(false, false).GetString(buf, 0, n).Replace("\r\n", "\n");
            var lines = text.Split('\n');
            if (lines.Length > MaxLines)
            {
                text = string.Join('\n', lines.Take(MaxLines));
                truncated = true;
            }

            return new ReadmeText(path, text, truncated, null, format);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new ReadmeText(path, "", false, $"Could not read it: {e.Message}", format);
        }
    }

    /// <summary>
    /// Markdown (or plain text) → Godot BBCode for a read-only view. Every '[' of the source is escaped first, so no tag
    /// in the file can become markup; only the converter's own tags format headings, emphasis, code and lists.
    /// </summary>
    public static string ToBbcode(string text, bool markdown)
    {
        var sb = new StringBuilder();
        var inCode = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Replace("[", "[lb]");
            if (!markdown)
            {
                sb.Append(line).Append('\n');
                continue;
            }

            if (raw.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                sb.Append(inCode ? "[/code][/bgcolor]\n" : "[bgcolor=#1c2333][code]");
                inCode = !inCode;
                continue;
            }

            if (inCode)
            {
                sb.Append(line).Append('\n');
                continue;
            }

            var t = line.TrimStart();
            var hashes = t.TakeWhile(c => c == '#').Count();
            if (hashes is > 0 and <= 6 && t.Length > hashes && t[hashes] == ' ')
            {
                var size = hashes switch { 1 => 26, 2 => 22, 3 => 19, _ => 17 };
                sb.Append($"[font_size={size}][b]").Append(Inline(t[(hashes + 1)..])).Append("[/b][/font_size]\n");
                continue;
            }

            if (t.StartsWith("- ", StringComparison.Ordinal) || t.StartsWith("* ", StringComparison.Ordinal))
            {
                sb.Append(new string(' ', (line.Length - t.Length) * 2)).Append(" • ").Append(Inline(t[2..])).Append('\n');
                continue;
            }

            sb.Append(Inline(line)).Append('\n');
        }

        if (inCode) sb.Append("[/code][/bgcolor]");
        return sb.ToString();
    }

    /// <summary>Inline markdown on an already escaped line: images to alt text, links to "text ‹url›", code, bold.</summary>
    private static string Inline(string s)
    {
        s = System.Text.RegularExpressions.Regex.Replace(s, @"!\[lb\]([^\]]*)\]\(([^)]*)\)", m => $"[i](image: {m.Groups[1].Value})[/i]");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\[lb\]([^\]]+)\]\(([^)]*)\)", m => $"[u]{m.Groups[1].Value}[/u] [color=#8899aa]‹{m.Groups[2].Value}›[/color]");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"`([^`]+)`", m => $"[code]{m.Groups[1].Value}[/code]");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\*\*([^*]+)\*\*", m => $"[b]{m.Groups[1].Value}[/b]");
        return s;
    }
}

/// <summary>Which platform a project lives on, and therefore how it may be opened (DEV-03).</summary>
public enum ProjectPlatform
{
    Windows,
    Wsl,
    Other,
}

/// <summary>One way to open a project, described before anything runs. <see cref="Args"/> are passed as separate arguments.</summary>
public sealed record EditorOption(string Label, string Explanation, string Program, IReadOnlyList<string> Args, bool ViaAssociation);

/// <summary>
/// DEV-03 (AD-33): the explicit ways to open a project in an editor already on this computer, platform-aware: a Windows
/// solution with its Windows association (Visual Studio, Rider), a Windows folder in VS Code, and a WSL project in VS Code
/// through its WSL remote — never a Linux project through a Windows file association. Nothing is installed; when no
/// editor is found, only the folder can be shown. Builds and scripts are never run.
/// </summary>
public static class EditorLaunch
{
    public static ProjectPlatform PlatformOf(string path) =>
        ListAhead.LinuxPath(path) != null && path.StartsWith(@"\\", StringComparison.Ordinal) ? ProjectPlatform.Wsl
        : path.Length >= 2 && path[1] == ':' || path.StartsWith(@"\\", StringComparison.Ordinal) ? ProjectPlatform.Windows
        : ProjectPlatform.Other;

    /// <summary>The WSL distribution name in a <c>\\wsl.localhost\Distro\…</c> path.</summary>
    public static string? Distribution(string path)
    {
        foreach (var prefix in new[] { @"\\wsl.localhost\", @"\\wsl$\" })
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = path[prefix.Length..];
            var slash = rest.IndexOf('\\');
            return slash < 0 ? rest : rest[..slash];
        }

        return null;
    }

    /// <param name="projectPath">The project folder.</param>
    /// <param name="solutions">Solution files in it (.sln, .slnx), by name.</param>
    /// <param name="vsCode">Path of an installed VS Code executable (Code.exe), or null.</param>
    public static IReadOnlyList<EditorOption> Options(string projectPath, IReadOnlyList<string> solutions, string? vsCode)
    {
        var list = new List<EditorOption>();
        switch (PlatformOf(projectPath))
        {
            case ProjectPlatform.Windows:
                foreach (var s in solutions.Take(2))
                {
                    list.Add(new EditorOption($"Open {s}", $"Opens the solution with the app Windows associates with it (for example Visual Studio or Rider).",
                        projectPath.TrimEnd('\\') + "\\" + s, [], ViaAssociation: true));
                }

                if (vsCode != null) list.Add(new EditorOption("Open folder in VS Code", "Opens this folder in Visual Studio Code.", vsCode, [projectPath], false));
                break;
            case ProjectPlatform.Wsl when vsCode != null && Distribution(projectPath) is { } distro:
                list.Add(new EditorOption($"Open in VS Code (WSL: {distro})",
                    "Opens the folder inside the Linux distribution with VS Code's WSL remote, so Linux tools build it. If VS Code lacks its WSL extension, VS Code itself offers to add it; FileScape installs nothing.",
                    vsCode, ["--remote", $"wsl+{distro}", ListAhead.LinuxPath(projectPath)!], false));
                break;
        }

        return list;
    }
}
