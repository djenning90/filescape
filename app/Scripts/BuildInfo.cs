using Godot;

namespace FileScape.App;

/// <summary>
/// The identity of the running build: the first line of build-info.txt written by tools/fs.sh export next to the exe
/// ("build &lt;commit&gt;[+N-uncommitted] &lt;UTC time&gt;"), or "development" when running from the editor/project.
/// </summary>
public static class BuildInfo
{
    private static string? _identifier;

    /// <summary>The build identity (read once, then cached): shown in Help and the start-up log, so a report names the build it came from.</summary>
    public static string Identifier => _identifier ??= Read();

    private static string Read()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
            var file = System.IO.Path.Combine(dir, "build-info.txt");
            if (!OS.HasFeature("editor") && System.IO.File.Exists(file))
            {
                var first = System.IO.File.ReadLines(file).FirstOrDefault() ?? "";
                return first.StartsWith("build ", StringComparison.Ordinal) ? first[6..] : first;
            }
        }
        catch (System.IO.IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return "development (not an exported build)";
    }
}
