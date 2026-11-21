namespace FileScape.Core.Samples;

/// <summary>
/// The "Markup Names" fixture (PLANET-152, PLANET-156): a small folder whose folder and file names, and whose text
/// files' contents, look like BBCode, Markdown, HTML and FileScape's own interface markup, plus one very long name. The
/// <c>planets-text</c> journey walks it to show each text surface renders them literally, and screenshots each surface
/// at the representative window sizes. Every name is legal on Windows (no <c>&lt; &gt; : " / \ | ? *</c>), so the same
/// fixture works on NTFS and through \\wsl.localhost.
/// Preserve: deterministic (the same names and bytes every time); written only under the directory it is given.
/// </summary>
public static class MarkupNamesSample
{
    /// <summary>The fixture's own folder name.</summary>
    public const string RootName = "Markup Names";

    /// <summary>The folders (each holds one small file so it is a world with something in it).</summary>
    public static readonly string[] Folders =
    [
        "[b]Bold folder", "[url=https]Link folder", "{Space} keycap folder", "~quiet~ & [color=red]red", "# heading-like",
        "[center]centred[lb]", LongName,
    ];

    /// <summary>The text files at the root and what each holds (the reader and the preview must show it as written).</summary>
    public static readonly (string Name, string Text)[] Files =
    [
        ("notes [b].md", "# Notes [b]not bold[/b]\n\nA *real* emphasis, then [url=https://example.com]a fake BBCode link[/url] and [img]C:/x.png[/img].\n\n- {Space} is not a key cap here\n- ~tilde~ & <i>html</i> &amp; entity\n"),
        ("plain [i]text.txt", "[b]This is not bold[/b] & <b>nor this</b>.\n{E} or twirl · *not emphasised* · ~not quiet~\n\\[lb] and [rb] stay as written.\n"),
        ("a & b; c.txt", "ampersands & semicolons; fine\n"),
        ("{E} or twirl.txt", "a name that looks like a prompt's key cap\n"),
        ("_under_ `code`.md", "`code` and _under_ and **bold** are Markdown here, rendered.\n"),
    ];

    /// <summary>A name long enough to need shortening on a one-line prompt (130 characters).</summary>
    public const string LongName = "A very long folder name that keeps going well past what a one-line prompt can show whole, so that the prompt has to shorten it [end]";

    /// <summary>Writes the fixture under <paramref name="parentDirectory"/> (replacing an earlier one's files in place) and
    /// returns its root.</summary>
    public static string Create(string parentDirectory)
    {
        var root = Path.Combine(Path.GetFullPath(parentDirectory), RootName);
        Directory.CreateDirectory(root);
        foreach (var f in Folders)
        {
            var d = Directory.CreateDirectory(Path.Combine(root, f));
            File.WriteAllText(Path.Combine(d.FullName, "inside [b].txt"), "a file inside " + f + "\n");
        }

        foreach (var (name, text) in Files) File.WriteAllText(Path.Combine(root, name), text);
        return root;
    }
}
