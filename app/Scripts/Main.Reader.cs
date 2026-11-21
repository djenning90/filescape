using FileScape.App.Planets;
using FileScape.App.Ui;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Shell;
using Godot;

namespace FileScape.App;

/// <summary>
/// The reading view's session side (PLANET-52/53, AD-62): reading a file into the HUD's reading view (bounded, off the
/// main thread, Markdown rendered with usable links), following a clicked link, and Open with Windows from the reader.
/// A link is followed only because it was clicked: an anchor scrolls (the HUD does that itself), a relative path is
/// checked on disk then read here (a text file), gone to (a folder, or another file's folder with it selected), or said
/// to be missing; http, https and mailto go to the browser or mail program through the shell (recorded, not launched,
/// under <c>--test-shell=record</c>); anything else is refused with the reason. Images are never loaded, remote or local.
/// Preserve: nothing here runs on its own — every read and every launch follows a deliberate key or click — and every
/// outcome is said in the reader's status line and as a toast drawn above the reader.
/// </summary>
public partial class Main
{
    /// <summary>The largest file a link opens in the reader rather than going to its folder, bytes (the preview's text limit; rationale not recorded beyond that).</summary>
    private const long ReaderLinkMaxBytes = 4L * 1024 * 1024;

    /// <summary>Folders climbed from a document to find its repository root for a link written with a leading '/' (a deep
    /// docs tree is rarely deeper; each step is one existence check, on a click).</summary>
    private const int ProjectRootSearchDepth = 12;

    /// <summary>Called once the HUD exists: the reader's link clicks and its Open button.</summary>
    private void ConnectReader()
    {
        _hud.ReaderLinkClicked += FollowReaderLink;
        _hud.ReaderOpenRequested += OpenFromReader;
    }

    /// <summary>
    /// Reads <paramref name="path"/> (bounded: <see cref="Readme.MaxBytes"/>, <see cref="Readme.MaxLines"/>) on a worker
    /// and shows it in the reading view, at the heading <paramref name="anchor"/> if given. Markdown is rendered with usable
    /// links; anything else is plain text. A file cut at a limit says so at its end and in the footer.
    /// </summary>
    public void ShowDocument(string path, string title, string? anchor = null)
    {
        var markdown = PlanetWorld.IsMarkdown(path);
        Task.Run(() => Readme.Read(path)).ContinueWith(t => Callable.From(() =>
        {
            var r = t.Result;
            LastReadme = r;
            GD.Print($"[reader] {path}: {r.Text.Length} chars, {(markdown ? "markdown" : "plain")}, truncated={r.Truncated}, error={r.Error ?? "-"}");
            ReleaseAllInput();
            if (r.Error != null)
            {
                _hud.ShowReader(new ReaderDocument(title, ReaderText.ErrorBody(r.Error), "Nothing was shown.", path));
                return;
            }

            var doc = markdown ? MarkdownText.Render(r.Text) : new MarkdownDocument(MarkdownText.PlainToBbcode(r.Text), [], new Dictionary<string, int>());
            var body = doc.Bbcode;
            if (r.Truncated)
            {
                // PLANET-52: a genuine limit is said where the reading stops, not only in the footer.
                body += ReaderText.CutNote;
            }

            var footer = ReaderText.Footer(markdown, r.Truncated);
            _hud.ShowReader(new ReaderDocument(title, body, footer, path, doc.Links, doc.Anchors), anchor);
            ReaderViews++;
        }).CallDeferred());
    }

    /// <summary>Documents shown in the reading view this session (probes).</summary>
    public int ReaderViews { get; private set; }

    /// <summary>A title for a document: its folder's name and its own.</summary>
    private static string DocumentTitle(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var cut = trimmed.LastIndexOfAny(['\\', '/']);
        var name = trimmed[(cut + 1)..];
        var folder = cut > 0 ? trimmed[..cut].TrimEnd('\\', '/') : "";
        var fcut = folder.LastIndexOfAny(['\\', '/']);
        return (fcut >= 0 ? folder[(fcut + 1)..] + " · " : "") + name;
    }

    /// <summary>The folder above <paramref name="documentPath"/> that holds a .git (GitHub's root for a link starting '/'), or null.</summary>
    private static string? ProjectRootOf(string documentPath)
    {
        var dir = System.IO.Path.GetDirectoryName(documentPath);
        for (var i = 0; i < ProjectRootSearchDepth && !string.IsNullOrEmpty(dir); i++, dir = System.IO.Path.GetDirectoryName(dir))
        {
            if (System.IO.Directory.Exists(System.IO.Path.Combine(dir, ".git")) || System.IO.File.Exists(System.IO.Path.Combine(dir, ".git"))) return dir;
        }

        return null;
    }

    /// <summary>The links followed this session, newest last (probes): "kind address".</summary>
    public readonly List<string> FollowedLinks = [];

    /// <summary>A link clicked in the reading view (anchors are the HUD's): see the class summary.</summary>
    private void FollowReaderLink(ReaderDocument doc, MarkdownLink link)
    {
        if (doc.Path == null) return;
        var target = MarkdownLinks.Resolve(link.Target, doc.Path, link.Target.TrimStart().StartsWith('/') ? ProjectRootOf(doc.Path) : null);
        FollowedLinks.Add($"{target.Kind} {target.Address}{(target.Fragment is { Length: > 0 } f ? "#" + f : "")}");
        GD.Print($"[reader] link “{link.Text}” → {target.Kind} {target.Address} #{target.Fragment}");
        switch (target.Kind)
        {
            case MarkdownLinkKind.Anchor:
                _hud.JumpToAnchor(target.Fragment ?? "");
                return;
            case MarkdownLinkKind.Refused:
                ReaderSays($"Not followed: {link.Target} ({target.Reason}).", bad: true);
                return;
            case MarkdownLinkKind.Web:
            {
                var r = _shell.Open(target.Address);
                LastShellMessage = r.Message;
                LastShellResult = r;
                ReaderSays(r.Succeeded ? $"{MarkdownLinks.Describe(target)} ({r.Message})" : r.Message, bad: !r.Succeeded && !r.WasMock);
                return;
            }
        }

        // A local file or folder: checked now, on the click.
        var path = target.Address;
        try
        {
            if (System.IO.Directory.Exists(path))
            {
                _hud.HideReadme();
                _hud.Toast($"Going to {DocumentTitle(path)}…");
                OnPathSubmitted(path);
                return;
            }

            if (System.IO.File.Exists(path))
            {
                var size = new System.IO.FileInfo(path).Length;
                if (PlanetText.LooksLikeText(System.IO.Path.GetFileName(path)) && size <= ReaderLinkMaxBytes)
                {
                    ShowDocument(path, DocumentTitle(path), target.Fragment);
                    return;
                }

                _hud.HideReadme();
                _hud.Toast($"Going to {DocumentTitle(path)} (it is not text to read here: Enter there opens it with Windows)…");
                OnPathSubmitted(path); // a file: its folder, with it selected
                return;
            }
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            ReaderSays($"Could not reach {link.Target}: {e.Message}", bad: true);
            return;
        }

        ReaderSays($"Not found: {link.Target} (looked for {path}).", bad: true);
    }

    /// <summary>The outcome of something done in the reader: its status line, and a toast drawn above it.</summary>
    private void ReaderSays(string text, bool bad)
    {
        _hud.SetReaderStatus(text, bad, sticky: true);
        _hud.Toast(text, bad ? new Color(1f, 0.5f, 0.45f) : new Color(0.6f, 1f, 0.7f), bad ? ShellFailureToastSeconds : 6);
    }

    /// <summary>
    /// Open with Windows from the reader (its button, or Enter while reading). The file being read is the selection when
    /// it came from the world, so the usual Open runs (with its confirmations); a file reached through a link is opened
    /// by its path, after the same check for files that run code.
    /// </summary>
    private void OpenFromReader(string path)
    {
        if (_tree != null && _selection.Selected >= 0 && string.Equals(_tree.FullPath(_selection.Selected), path, StringComparison.OrdinalIgnoreCase))
        {
            RequestOpen();
        }
        else if (LaunchRisk.RunsCode(path))
        {
            Confirm($"“{System.IO.Path.GetFileName(path)}” can run a program or script.\nWindows will execute it with your permissions.\n\nOpen it with Windows?", () => ReportShell(_shell.Open(path)));
            return;
        }
        else
        {
            ReportShell(_shell.Open(path));
        }

        if (ConfirmShowing) _hud.SetReaderStatus("Asking first: see the question above.", bad: false, sticky: true);
        else if (LastShellResult is { } r) _hud.SetReaderStatus(r.Message, bad: !r.Succeeded && !r.WasMock, sticky: true);
    }

    /// <summary>
    /// Keys while the reading view is open (PLANET-52), before any shortcut: Enter opens the file with Windows, R or Esc
    /// closes it (R from the planets' preview expands, so R again collapses), the arrows and Page keys scroll it (its text
    /// takes the keyboard back if a click elsewhere took it), and no other key reaches the game. F keys and Ctrl chords pass.
    /// </summary>
    private bool ReaderKey(InputEvent e)
    {
        if (!_hud.ReadmeVisible || e is not InputEventKey { Pressed: true } k) return false;
        if (k.CtrlPressed || k.AltPressed || k.MetaPressed || k.Keycode is >= Key.F1 and <= Key.F12 || k.Keycode == Key.Escape) return false;
        if (k.Echo && k.Keycode is not (Key.Up or Key.Down or Key.Pageup or Key.Pagedown)) return true;
        switch (k.Keycode)
        {
            case Key.Enter or Key.KpEnter:
                _hud.RequestReaderOpen();
                return true;
        }

        if (k.IsAction(InputBindings.Readme))
        {
            _hud.HideReadme();
            return true;
        }

        _hud.FocusReader(); // the next arrow scrolls it
        return true;
    }
}
