using FileScape.Core.Planets;
using FileScape.Core.Samples;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace FileScape.App;

/// <summary>
/// Learning by playing (PLANET-33, AD-54): the welcome dialog at a person's first launch and the walkthrough in the
/// Starter Sky. Each lesson is one short instruction, a visible target in the world (<c>PlanetWorld.Tutorial.cs</c> draws
/// it), a check of the real action — never a "Next" button — and a small celebration when it is done. You can skip a
/// step, start over or leave at any time; if you wander off, the card offers a way back to the current lesson (a normal
/// navigation to the lesson's folder) without restarting. The card is small and sits at the top centre, so the world
/// stays in view. The lessons use the controls the build ships with (AD-47): left-drag look, WASD, Space, U, J,
/// Backspace, T, Esc, E.
/// </summary>
public partial class Main
{
    /// <summary>One step of the walkthrough: what to do, where (the folder it happens in), what to aim at, and how we know it is done.</summary>
    private sealed record Lesson(string Title, string Instruction, Func<int> Folder, Func<(NVector3 Dir, string Label, float Lift)?> Target, Func<bool> Done, Action? Enter = null);

    private List<Lesson>? _lessons;
    private int _lesson = -1;
    private bool _tutorialWaiting;
    private bool _tutorialFinished;
    private float _lessonDoneFor;
    private float _offRouteFor;

    // What a step measures from: the camera heading at the step's start, jumps so far, where you stood when the overview opened.
    private float _tutHeadingTurned, _tutLastHeading, _tutYaw0;
    private int _tutJumps0;
    private NVector3 _tutFeetBeforeOverview;

    /// <summary>How long a finished step's "✓" shows before the next step begins, seconds: long enough to read, short enough to keep the pace.</summary>
    private const float StepCelebrationSeconds = 1.4f;

    /// <summary>How long you must be somewhere else (not travelling) before the card offers "Back to the lesson", seconds: brief hops do not nag.</summary>
    private const float OffRouteGraceSeconds = 1.0f;

    /// <summary>How far the camera must turn in the first lesson, degrees: a third of a turn shows the dragging was understood.</summary>
    private const float LookAroundDegrees = 120f;

    /// <summary>How close to the landmark counts as "there", metres beyond its footprint.</summary>
    private const float LandmarkReachMetres = 3f;

    /// <summary>The target counts as out of view (the card says which way to turn) beyond this bearing, degrees: about the
    /// edge of the 62° view plus a margin.</summary>
    private const float TurnHintDegrees = 50f;

    /// <summary>How far the overview must be orbited, radians (≈ 34°).</summary>
    private const float OverviewOrbitRadians = 0.6f;

    /// <summary>How close to the spot you left counts as "back where you stood" after the overview, metres.</summary>
    private const float BackWhereYouStoodMetres = 1.5f;

    private PanelContainer _lessonCard = null!;
    private Label _lessonTitle = null!, _lessonText = null!, _lessonFeedback = null!;
    private Button _lessonBack = null!, _lessonSkip = null!, _lessonRestart = null!, _lessonLeave = null!, _lessonExplore = null!, _lessonStay = null!;
    private PanelContainer _welcome = null!;
    private bool _welcomeOpen;

    /// <summary>The walkthrough is running (a lesson card shows).</summary>
    public bool TutorialActive => _lesson >= 0 || _tutorialWaiting || _tutorialFinished;

    /// <summary>The current step (0-based), −1 when none (probes).</summary>
    public int TutorialStep => _lesson;

    /// <summary>The current step's title (probes).</summary>
    public string TutorialTitle => _lesson >= 0 && _lessons != null ? _lessons[_lesson].Title : _tutorialFinished ? "done" : "";

    public bool TutorialOffRoute => _lessonBack.Visible;

    public bool WelcomeOpen => _welcomeOpen;

    public Rect2 WelcomeButtonRect(int index) => ((Button)_welcomeButtons.GetChild(index)).GetGlobalRect();

    public Rect2 LessonButtonRect(string which) => (which switch { "back" => _lessonBack, "skip" => _lessonSkip, "restart" => _lessonRestart, "leave" => _lessonLeave, "explore" => _lessonExplore, _ => _lessonStay }).GetGlobalRect();

    private HBoxContainer _welcomeButtons = null!;

    // ------------------------------------------------------------------ building the UI

    private void BuildTutorialUi()
    {
        var root = _hud.UiRoot;
        _lessonCard = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _lessonCard.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _lessonCard.OffsetLeft = -290; _lessonCard.OffsetRight = 290; _lessonCard.OffsetTop = 150;
        _lessonCard.GrowHorizontal = Control.GrowDirection.Both;
        root.AddChild(_lessonCard);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 4);
        _lessonCard.AddChild(v);
        _lessonTitle = new Label();
        _lessonTitle.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.45f));
        _lessonTitle.AddThemeFontSizeOverride("font_size", 15);
        _lessonText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(540, 0) };
        _lessonText.AddThemeFontSizeOverride("font_size", 17);
        _lessonFeedback = new Label { Visible = false };
        _lessonFeedback.AddThemeColorOverride("font_color", new Color(0.6f, 1f, 0.7f));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        Button B(string text, Action act, string tip)
        {
            var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, TooltipText = tip };
            b.Pressed += act;
            row.AddChild(b);
            return b;
        }

        _lessonBack = B("↩ Back to the lesson", ReturnToLesson, "Take me to where this lesson happens (a normal trip; nothing is lost).");
        _lessonSkip = B("Skip step", () => AdvanceLesson(celebrate: false), "Go on to the next step.");
        _lessonRestart = B("Start over", () => StartTutorial(), "Replay the walkthrough from the beginning.");
        _lessonExplore = B("Explore This computer", () =>
        {
            EndTutorial();
            StartLive(null);
        }, "Your drives and Linux distributions, as worlds.");
        _lessonStay = B("Stay in the sample world", EndTutorial, "Close the walkthrough and keep playing here.");
        _lessonLeave = B("Leave", EndTutorial, "Close the walkthrough (Help → Learn to play brings it back).");
        v.AddChild(_lessonTitle);
        v.AddChild(_lessonText);
        v.AddChild(_lessonFeedback);
        v.AddChild(row);

        _welcome = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _welcome.SetAnchorsPreset(Control.LayoutPreset.Center);
        _welcome.GrowHorizontal = Control.GrowDirection.Both;
        _welcome.GrowVertical = Control.GrowDirection.Both;
        root.AddChild(_welcome);
        var w = new VBoxContainer();
        w.AddThemeConstantOverride("separation", 10);
        _welcome.AddChild(w);
        var title = new Label { Text = "Welcome to FileScape", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.88f, 0.6f));
        var body = new Label
        {
            Text = "Every folder is a little world you can run around. Its folders hang in the sky, waiting for you to fly there.\nWould you like a few minutes of guided play first?",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(520, 0),
        };
        _welcomeButtons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _welcomeButtons.AddThemeConstantOverride("separation", 10);
        Button W(string text, Action act)
        {
            var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
            b.Pressed += act;
            _welcomeButtons.AddChild(b);
            return b;
        }

        W("▶ Learn to play", () => { CloseWelcome(); StartTutorial(); }).AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.45f));
        W("Explore on my own", CloseWelcome);
        W("Don't show this again", () =>
        {
            SetPreferences(_prefs with { ShowWelcome = false });
            CloseWelcome();
        });
        w.AddChild(title);
        w.AddChild(body);
        w.AddChild(_welcomeButtons);
        _hud.LearnToPlayRequested += () => StartTutorial();
        _hud.AddBlocking(_lessonCard);
        _hud.AddBlocking(_welcome);
    }

    /// <summary>
    /// The welcome dialog, on a person's ordinary launch only (no automation, no explicit folder or view: a scripted or
    /// deliberate launch goes straight in) and while the preference allows it.
    /// </summary>
    private void MaybeOfferWelcome(CommandLine args)
    {
        if (args.Automation != null || args.Root != null || args.Experience != null || !_prefs.ShowWelcome) return;
        _welcome.Visible = true;
        _welcomeOpen = true;
    }

    private void CloseWelcome()
    {
        _welcome.Visible = false;
        _welcomeOpen = false;
    }

    // ------------------------------------------------------------------ the walkthrough

    /// <summary>Starts (or restarts) the walkthrough: opens the Starter Sky in Planets if needed, then the first lesson.</summary>
    public void StartTutorial()
    {
        CloseWelcome();
        _tutorialFinished = false;
        _lesson = -1;
        if (!InStarterSkyRoot())
        {
            OpenStarterSky();
        }
        else
        {
            if (_experience != Experience.Planets) SetExperience(Experience.Planets);
            // The lessons start at the Starter Sky's top world: go there (a normal trip) if you are deeper in.
            if (_nav.Current != _nav.Root && _tree != null) _nav.GoTo(_tree, _nav.Root);
        }

        _tutorialWaiting = true;
        _lessonCard.Visible = true;
        ShowLesson("Learn to play", "Opening the sample world…", "", feedback: null);
    }

    private bool InStarterSkyRoot() =>
        _tree is { Count: > 0 } t && t.Source.IsLive && t.Contains(_nav.Root) && SampleSky.IsSample(t.FullPath(_nav.Root));

    private void EndTutorial()
    {
        _lesson = -1;
        _tutorialWaiting = false;
        _tutorialFinished = false;
        _lessonCard.Visible = false;
        _planets.SetTutorialTarget(null);
        _planets.MakeRoomForLesson(false);
    }

    /// <summary>The lessons, built against the Starter Sky that is loaded now (folder ids are looked up by name).</summary>
    private List<Lesson> BuildLessons()
    {
        var t = _tree!;
        var root = _nav.Root;
        int Child(string name) => t[root].Children.FirstOrDefault(c => string.Equals(t[c].Name, name, StringComparison.OrdinalIgnoreCase), -1);
        var workshop = Child("Lantern Workshop");
        var musicBox = Child("Music Box");
        int Here() => _nav.Current;
        (NVector3, string, float)? Pad(int target, string label) =>
            _planets.Composition?.Launchers.FirstOrDefault(l => l.TargetId == target) is { } l ? (l.Direction, label, 0.35f) : null;

        return
        [
            new("1 · Look around", "Hold the left mouse button and drag to look around.", () => root, () => null,
                () => _tutHeadingTurned >= LookAroundDegrees,
                () => { _tutHeadingTurned = 0; _tutLastHeading = _planets.CameraHeadingDegrees(); }),
            new("1 · Walk to the tower", "Walk to the glowing marker with W A S D. You run where the camera looks.", () => root,
                () => _planets.Composition?.Landmark is { } lm ? (lm.Direction, "Walk here", 0f) : null,
                () => _planets.Composition?.Landmark is { } lm && _planets.DistanceTo(lm.Direction) <= lm.Radius + LandmarkReachMetres),
            new("2 · Jump", "Press Space to jump. Press it again in the air to twirl higher.", () => root, () => null,
                () => _planets.Walker.Jumps > _tutJumps0,
                () => _tutJumps0 = _planets.Walker.Jumps),
            new("2 · Fly to a folder", "Folders hang in the sky above flower launchers. Stand on the Lantern Workshop launcher and press Space.", () => root,
                () => Pad(workshop, "Lantern Workshop launcher"),
                () => Here() == workshop && !_planets.Busy),
            new("3 · A software project", "Lantern Workshop is a project, so its lookout is a workbench. Jump onto the bench.", () => workshop,
                () => _planets.Composition?.Overlook is { } ov ? (ov.Direction, "Workbench", ov.Top) : null,
                () => _planets.BenchRevealed),
            new("3 · Read the README", "Press E on the bench to read the project's README. (Feeling playful? Spin: Space, then Space again in the air.)", () => workshop,
                () => _planets.Composition?.Overlook is { } ov ? (ov.Direction, "Workbench", ov.Top) : null,
                () => _planets.PreviewOpen && _selection.Selected >= 0 && t.Contains(_selection.Selected) && t[_selection.Selected].Name.StartsWith("README", StringComparison.OrdinalIgnoreCase)),
            new("4 · Up a level", "Press U to go up to the parent world: the Starter Sky.", () => workshop, () => null,
                () => Here() == root && !_planets.Busy),
            new("4 · Straight to a folder", "Press J for this world's list, then choose Music Box (double-click, or ↓ and Enter).", () => root, () => null,
                () => Here() == musicBox && !_planets.Busy),
            new("4 · Up is the parent", "Press U. Up always goes to the folder above: the Starter Sky.", () => musicBox, () => null,
                () => Here() == root && !_planets.Busy),
            new("4 · Back is your history", "Now press Backspace. Back retraces your steps: to Music Box, where you just were — not further up.", () => root, () => null,
                () => Here() == musicBox && !_planets.Busy),
            new("5 · The overview", "Press T to see every folder you know as star systems.", () => musicBox, () => null,
                () => _planets.UniverseOpen,
                () => _tutFeetBeforeOverview = _planets.Walker.Feet),
            new("5 · Look round it", "Drag with the left mouse button to orbit. Click a world to select it if you like.", () => musicBox, () => null,
                () => _planets.UniverseOpen && MathF.Abs(_planets.UniverseYaw - _tutYaw0) >= OverviewOrbitRadians,
                () => _tutYaw0 = _planets.UniverseYaw),
            new("5 · Back where you stood", "Press Esc to come back down to the exact spot you left.", () => musicBox, () => null,
                () => !_planets.UniverseOpen && !_planets.Busy && NVector3.Distance(_planets.Walker.Feet, _tutFeetBeforeOverview) <= BackWhereYouStoodMetres),
        ];
    }

    /// <summary>Per frame (Planets): waits for the Starter Sky, checks the current step, moves the target, notices a wrong turn.</summary>
    private void TickTutorial(float dt)
    {
        if (!TutorialActive) return;
        _planets.MakeRoomForLesson(_lessonCard.Visible);
        if (_tutorialWaiting)
        {
            if (!InStarterSkyRoot() || _planets.Composition == null || _planets.Busy || _nav.Current != _nav.Root) return;
            _tutorialWaiting = false;
            _lessons = BuildLessons();
            _lesson = 0;
            EnterLesson();
            return;
        }

        if (_tutorialFinished || _lessons == null || _lesson < 0) return;
        var lesson = _lessons[_lesson];

        // The look-around step counts every degree the camera turns, whichever way.
        var heading = _planets.CameraHeadingDegrees();
        var turned = MathF.Abs(Mathf.Wrap(heading - _tutLastHeading, -180f, 180f));
        if (turned < 45f) _tutHeadingTurned += turned; // a jump of more than 45° in a frame is a teleport, not a look
        _tutLastHeading = heading;

        if (_lessonDoneFor > 0)
        {
            _lessonDoneFor -= dt;
            if (_lessonDoneFor <= 0) AdvanceLesson(celebrate: false);
            return;
        }

        // The overview was closed early: back to "open it".
        if (lesson.Title.StartsWith("5 · Look round", StringComparison.Ordinal) && !_planets.UniverseOpen)
        {
            _lesson--;
            EnterLesson();
            return;
        }

        var target = _nav.Current == lesson.Folder() && !_planets.UniverseOpen ? lesson.Target() : null;
        if (target is { } tg) _planets.SetTutorialTarget(tg.Dir, tg.Label, tg.Lift);
        else _planets.SetTutorialTarget(null);

        // Which way to turn when the marker is out of view (beyond ≈ 50° of the camera's heading): one line, no arrow
        // floating in the world to chase.
        if (target is { } t2 && _lessonDoneFor <= 0)
        {
            var bearing = _planets.CameraTurnTo(t2.Dir);
            _lessonFeedback.Text = MathF.Abs(bearing) > TurnHintDegrees ? (bearing > 0 ? "The marker is to your right →  (drag, or press →)" : "←  The marker is to your left  (drag, or press ←)") : "";
            _lessonFeedback.Visible = _lessonFeedback.Text.Length > 0;
        }

        if (lesson.Done())
        {
            _planets.CelebrateLessonStep();
            _lessonFeedback.Text = "✓ Nice!";
            _lessonFeedback.Visible = true;
            _lessonDoneFor = StepCelebrationSeconds;
            _planets.SetTutorialTarget(null);
            return;
        }

        // A wrong turn: somewhere other than this lesson's folder (and not on the way there) for a moment.
        var off = !_planets.Busy && !_planets.UniverseOpen && _nav.Current != lesson.Folder();
        _offRouteFor = off ? _offRouteFor + dt : 0;
        _lessonBack.Visible = _offRouteFor >= OffRouteGraceSeconds;
    }

    private void EnterLesson()
    {
        var lesson = _lessons![_lesson];
        lesson.Enter?.Invoke();
        _offRouteFor = 0;
        _lessonBack.Visible = false;
        ShowLesson($"Learn to play · {lesson.Title}", lesson.Instruction, $"step {_lesson + 1} of {_lessons.Count}", feedback: null);
    }

    private void AdvanceLesson(bool celebrate)
    {
        if (_lessons == null) return;
        _lessonDoneFor = 0;
        if (celebrate) _planets.CelebrateLessonStep();
        _lesson++;
        if (_lesson >= _lessons.Count)
        {
            _lesson = -1;
            _tutorialFinished = true;
            _planets.SetTutorialTarget(null);
            ShowLesson("Learn to play · done", "You've got the basics: look, run, jump, fly, read a project's README, go up and back, and the overview. Explore your own computer next?", "", feedback: "✓ All done!");
            return;
        }

        EnterLesson();
    }

    /// <summary>"Back to the lesson": a normal trip to the folder where the current step happens.</summary>
    private void ReturnToLesson()
    {
        if (_lessons == null || _lesson < 0 || _tree == null) return;
        var folder = _lessons[_lesson].Folder();
        if (_tree.Contains(folder)) _nav.GoTo(_tree, folder);
        _lessonBack.Visible = false;
        _offRouteFor = 0;
    }

    private void ShowLesson(string title, string text, string progress, string? feedback)
    {
        _lessonCard.Visible = true;
        _lessonTitle.Text = progress.Length > 0 ? $"{title}    ({progress})" : title;
        _lessonText.Text = text;
        _lessonFeedback.Text = feedback ?? "";
        _lessonFeedback.Visible = feedback != null;
        var running = !_tutorialFinished;
        _lessonSkip.Visible = running && _lesson >= 0;
        _lessonRestart.Visible = true;
        _lessonLeave.Visible = running;
        _lessonExplore.Visible = _tutorialFinished;
        _lessonStay.Visible = _tutorialFinished;
        _lessonBack.Visible = false;
    }

    /// <summary>The welcome dialog, the walkthrough and their buttons (centres in window pixels) for the real-input probe's state file.</summary>
    private string StateJsonTutorial()
    {
        var scale = GetWindow().ContentScaleFactor;
        string At(Rect2 r) { var c = r.GetCenter() * scale; return System.FormattableString.Invariant($"[{c.X:0},{c.Y:0}]"); }
        var atHub = _tree is { Count: > 0 } t && t.Contains(_nav.Current) && t[_nav.Current].IsHub;
        // Where the current lesson's target is from here (the probe steers by it, as a player steers by the beacon).
        var bearing = 0f;
        var distance = -1f;
        if (_lessons != null && _lesson >= 0 && _experience == Experience.Planets && _nav.Current == _lessons[_lesson].Folder() && _lessons[_lesson].Target() is { } tg)
        {
            bearing = _planets.CameraTurnTo(tg.Dir);
            distance = _planets.DistanceTo(tg.Dir);
        }

        return System.FormattableString.Invariant(
            $"\"welcome\":{(_welcomeOpen ? "true" : "false")},\"showWelcomePref\":{(_prefs.ShowWelcome ? "true" : "false")},\"atHub\":{(atHub ? "true" : "false")},\"tutorialActive\":{(TutorialActive ? "true" : "false")},\"tutorialStep\":{_lesson},\"tutorialTitle\":\"{JsonEscape(TutorialTitle)}\",\"offRoute\":{(TutorialOffRoute ? "true" : "false")},\"helpOpen\":{(_hud.HelpVisible ? "true" : "false")},\"targetBearing\":{bearing:0.#},\"targetDistance\":{distance:0.##},") +
            $"\"welcomeLearn\":{At(WelcomeButtonRect(0))},\"welcomeOwn\":{At(WelcomeButtonRect(1))},\"welcomeNever\":{At(WelcomeButtonRect(2))},\"helpLearn\":{At(_hud.LearnToPlayButtonRect)},\"lessonLeave\":{At(LessonButtonRect("leave"))},\"lessonBack\":{At(LessonButtonRect("back"))},\"legacyMenu\":{At(_hud.LegacyMenuButtonRect)},\"legacyItem0\":{At(_hud.VisualizationButtonRect(-1))},";
    }
}
