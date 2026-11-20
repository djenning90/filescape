using FileScape.App.Planets;
using FileScape.Core.Interaction;
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
    /// <summary>
    /// One step of the walkthrough: what to do, the keys it uses (shown as key caps on the card), where (the folder it
    /// happens in), where to go (a <see cref="TutorialTarget"/>, or null for a step with no place to go: the card's key
    /// caps are then the cue, AD-55), and how we know it is done.
    /// </summary>
    private sealed record Lesson(string Title, string Instruction, string[] Keys, Func<int> Folder, Func<TutorialTarget?> Target, Func<bool> Done, Action? Enter = null);

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

    /// <summary>Beyond this bearing, degrees, the card's hint says the beacon is behind you (the edge arrow says the same).</summary>
    private const float BehindHintDegrees = 120f;

    /// <summary>Where the screen chevron floats over a spot on open ground, a launcher pad and the bench, metres above the spot.</summary>
    private const float MarkerOverGroundMetres = 3.2f, MarkerOverPadMetres = 3f, MarkerOverBenchMetres = 2.6f;

    /// <summary>The ring round the tower, metres beyond its footprint: a little inside <see cref="LandmarkReachMetres"/>,
    /// so stepping into the ring always completes the step.</summary>
    private const float LandmarkRingMetres = 2.2f;

    /// <summary>The bench's ring, metres beyond the deck's radius (standing on its rim counts as there).</summary>
    private const float BenchRimMetres = 0.6f;

    /// <summary>How far the overview must be orbited, radians (≈ 34°).</summary>
    private const float OverviewOrbitRadians = 0.6f;

    /// <summary>How close to the spot you left counts as "back where you stood" after the overview, metres.</summary>
    private const float BackWhereYouStoodMetres = 1.5f;

    /// <summary>
    /// The zoom step (PLANET-54): the overview camera must be at least this many times as far out as when the step began,
    /// and must have eased most of the way there (so you see it), and at least <see cref="ZoomMoreWorlds"/> more worlds
    /// must be in view than at the start. Measured on the sample from Music Box at 1600 × 900 (2026-11-17): it opens 30 m
    /// out with 2 worlds in view; each wheel notch is ×1.18; 4 worlds until ≈ 130 m, then the Starter Sky's neighbouring
    /// systems come in — 14 worlds at 150 m (12 notches), 41 at 210 m. Fewer notches only showed the next world along.
    /// </summary>
    private const float ZoomOutFactor = 3f, ZoomSettledShare = 0.85f;

    /// <summary>How many more worlds the zoom step must bring into view (enough that it is the system, not a neighbour).</summary>
    private const int ZoomMoreWorlds = 10;

    /// <summary>The zoom step's "keep scrolling" line shows once the wheel has pulled back this much (one notch is ×1.18).</summary>
    private const float ZoomProgressFrom = 1.1f;

    private float _tutZoom0;
    private int _tutWorlds0;

    /// <summary>The zoom step's test: well out, settled, and more worlds in view.</summary>
    private bool ZoomedOutEnough()
    {
        var (now, target) = _planets.UniverseZoom;
        // When the overview opened already showing nearly the whole sky (found with real input, 2026-11-17: skipping to the
        // overview step framed all 93 worlds from 1256 m, 84 in view), ten more cannot come into view: then the zoom alone counts.
        // Only drawn worlds can come into view: the level of detail folds distant systems as you zoom out (PLANET-91; found
        // with real input on the merged build, 2026-11-19: 30 notches out, 51 -> 53 worlds in view), so the bar is
        // ten fewer than the overview draws now, never more than ten above where you started.
        var more = Math.Min(_tutWorlds0 + ZoomMoreWorlds, Math.Max(0, _planets.UniverseDrawnCount - ZoomMoreWorlds));
        return target >= _tutZoom0 * ZoomOutFactor && now >= target * ZoomSettledShare && _planets.UniverseWorldsInView >= more;
    }

    private PanelContainer _lessonCard = null!;
    private Label _lessonTitle = null!, _lessonFeedback = null!;

    /// <summary>The instruction (PLANET-94): BBCode from <see cref="LessonText"/> — the current action in gold, keys as caps,
    /// a short tip under it.</summary>
    private RichTextLabel _lessonText = null!;

    /// <summary>The card's text sizes at UI scale 1, pixels: the action line (one step up from the 17 px of before, so the
    /// caps read at a glance) and the quieter tip line under it.</summary>
    private const int LessonActionPixels = 18, LessonTipPixels = 15;

    /// <summary>The tip line's colour: a pale blue-grey, quieter than the action.</summary>
    private const string LessonTipColor = "#bcc6e2";
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

    /// <summary>The lesson card's rectangle, viewport units (probes check the cue stays clear of it).</summary>
    public Rect2 LessonCardRect => _lessonCard.GetGlobalRect();

    /// <summary>The card's hint line as shown ("" when hidden): the turn hint, the text backup for the cue (probes).</summary>
    public string LessonHint => _lessonFeedback.Visible ? _lessonFeedback.Text : "";

    public bool WelcomeOpen => _welcomeOpen;

    /// <summary>The card's instruction as shown, with its key caps (probes: the card must name its keys).</summary>
    public string LessonShownText => _lessonText.GetParsedText();

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
        _lessonText = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(540, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _lessonText.AddThemeFontSizeOverride("normal_font_size", LessonActionPixels);
        _lessonText.AddThemeFontSizeOverride("bold_font_size", LessonActionPixels);
        _lessonText.AddThemeConstantOverride("line_separation", 4); // room for the caps' backgrounds between wrapped lines
        var textRow = _lessonText;
        _lessonFeedback = new Label { Visible = false };
        _lessonFeedback.AddThemeFontSizeOverride("font_size", LessonTipPixels);
        _lessonFeedback.AddThemeColorOverride("font_color", TutorialCue.CueColor);
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
        v.AddChild(textRow);
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
        _planets.MakeRoomForLesson(0);
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
        TutorialTarget? Pad(int target, string action) =>
            _planets.Composition?.Launchers.FirstOrDefault(l => l.TargetId == target) is { } l ? new TutorialTarget(l.Direction, action, l.PadTop, l.PadRadius, MarkerOverPadMetres) : null;
        // The bench's ring stands on the ground round it (a ring round the raised deck floats in the air); you count as
        // there on the deck's rim too.
        TutorialTarget? Bench(string action) =>
            _planets.Composition?.Overlook is { } ov ? new TutorialTarget(ov.Direction, action, 0f, ov.Radius + BenchRimMetres, ov.Top + MarkerOverBenchMetres) : null;

        return
        [
            new("1 · Look around", "*Look around:* hold the left mouse button and {drag}.\nOr turn with {←} {→}.", ["drag", "← →"], () => root, () => null,
                () => _tutHeadingTurned >= LookAroundDegrees,
                () => { _tutHeadingTurned = 0; _tutLastHeading = _planets.CameraHeadingDegrees(); }),
            new("1 · Walk to the tower", "*Walk into the green ring* round the tower: {W A S D}.\nYou run where the camera looks.", ["W A S D"], () => root,
                () => _planets.Composition?.Landmark is { } lm ? new TutorialTarget(lm.Direction, "Walk into the ring", 0f, lm.Radius + LandmarkRingMetres, lm.Top + MarkerOverGroundMetres) : null,
                () => _planets.Composition?.Landmark is { } lm && _planets.DistanceTo(lm.Direction) <= lm.Radius + LandmarkReachMetres),
            new("2 · Jump", "*Jump:* press {Space}.\nPress it again in the air to twirl higher.", ["Space"], () => root, () => null,
                () => _planets.Walker.Jumps > _tutJumps0,
                () => _tutJumps0 = _planets.Walker.Jumps),
            new("2 · Fly to a folder", "*Fly to Lantern Workshop:* stand on its launcher and press {Space}.\nFollow the green beacon. Each folder hangs in the sky above a flower launcher.", ["Space"], () => root,
                () => Pad(workshop, "Stand here · press Space to fly"),
                () => Here() == workshop && !_planets.Busy),
            new("3 · A software project", "*Jump onto the workbench:* run at it and press {Space}.\nLantern Workshop is a software project, so its lookout is a workbench.", ["Space"], () => workshop,
                () => Bench("Run at it and press Space"),
                () => _planets.BenchRevealed),
            new("3 · Read the README", "*Read the README:* press {E} on the bench.\nFeeling playful? Spin instead: {Space}, then {Space} again in the air.", ["E"], () => workshop,
                () => Bench("Press E to read the README"),
                () => _planets.PreviewOpen && _selection.Selected >= 0 && t.Contains(_selection.Selected) && t[_selection.Selected].Name.StartsWith("README", StringComparison.OrdinalIgnoreCase)),
            new("4 · Up a level", "*Go up a level:* press {U}.\nUp takes you to the parent world, the Starter Sky.", ["U"], () => workshop, () => null,
                () => Here() == root && !_planets.Busy),
            new("4 · Straight to a folder", "*Go straight to Music Box:* press {J}, then {↓} and {Enter}.\nJ lists every world you can reach. Double-clicking a row works too.", ["J", "↓", "Enter"], () => root, () => null,
                () => Here() == musicBox && !_planets.Busy),
            new("4 · Up is the parent", "*Go up:* press {U}.\nUp always goes to the folder above: the Starter Sky.", ["U"], () => musicBox, () => null,
                () => Here() == root && !_planets.Busy),
            new("4 · Back is your history", "*Go back:* press {Backspace}.\nBack retraces your steps: to Music Box, where you just were, not further up.", ["Backspace"], () => root, () => null,
                () => Here() == musicBox && !_planets.Busy),
            new("5 · The overview", "*Open the overview:* press {T}.\nEvery folder you know, drawn as star systems.", ["T"], () => musicBox, () => null,
                () => _planets.UniverseOpen,
                () => _tutFeetBeforeOverview = _planets.Walker.Feet),
            new("5 · Look round it", "*Orbit the overview:* {drag} with the left mouse button.\nClick a world to select it if you like.", ["drag"], () => musicBox, () => null,
                () => _planets.UniverseOpen && MathF.Abs(_planets.UniverseYaw - _tutYaw0) >= OverviewOrbitRadians,
                () => _tutYaw0 = _planets.UniverseYaw),
            // PLANET-54 (DJ 2026-11-17: "When Overview opens zoomed in, I don't know I can see more. Prompt me to scroll down
            // to zoom out"): done once the wheel has pulled the camera well back and you can see more worlds than before.
            new("5 · Zoom out", "*Zoom out:* scroll the {wheel ↓} until the Starter Sky's worlds are around you.\nScrolling up zooms back in.", ["wheel ↓"], () => musicBox, () => null,
                () => _planets.UniverseOpen && ZoomedOutEnough(),
                () => { var z = _planets.UniverseZoom; _tutZoom0 = MathF.Max(z.Now, z.Target); _tutWorlds0 = _planets.UniverseWorldsInView; }),
            new("5 · Back where you stood", "*Come back down:* press {Esc}.\nYou land on the exact spot you left.", ["Esc"], () => musicBox, () => null,
                () => !_planets.UniverseOpen && !_planets.Busy && NVector3.Distance(_planets.Walker.Feet, _tutFeetBeforeOverview) <= BackWhereYouStoodMetres),
        ];
    }

    /// <summary>Per frame (Planets): waits for the Starter Sky, checks the current step, moves the target, notices a wrong turn.</summary>
    private void TickTutorial(float dt)
    {
        if (!TutorialActive) return;
        _planets.MakeRoomForLesson(_lessonCard.Visible ? _lessonCard.GetGlobalRect().End.Y : 0);
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

        // The overview was closed early (looking round it or zooming out): back to "open it".
        if ((lesson.Title.StartsWith("5 · Look round", StringComparison.Ordinal) || lesson.Title.StartsWith("5 · Zoom out", StringComparison.Ordinal)) && !_planets.UniverseOpen)
        {
            _lesson = _lessons.FindIndex(l => l.Title.StartsWith("5 · The overview", StringComparison.Ordinal));
            EnterLesson();
            return;
        }

        var target = _nav.Current == lesson.Folder() && !_planets.UniverseOpen ? lesson.Target() : null;
        _planets.SetTutorialTarget(target);

        // The text backup for the beacon and the edge arrow (AD-55): which way to turn when the beacon is out of view
        // (beyond ≈ 50° of the camera's heading), and that it is behind you beyond 120°.
        if (target is { } t2 && _lessonDoneFor <= 0)
        {
            var bearing = _planets.CameraTurnTo(t2.Direction);
            var behind = MathF.Abs(bearing) > BehindHintDegrees;
            _lessonFeedback.Text = MathF.Abs(bearing) <= TurnHintDegrees ? ""
                : bearing > 0 ? (behind ? "The green beacon is behind you: turn right →  (drag, or press →)" : "The green beacon is to your right →  (drag, or press →)")
                : (behind ? "←  The green beacon is behind you: turn left  (drag, or press ←)" : "←  The green beacon is to your left  (drag, or press ←)");
            _lessonFeedback.Visible = _lessonFeedback.Text.Length > 0;
        }

        // The zoom step's progress (PLANET-54): once the wheel has started to pull back, say how many worlds you see, so a
        // few notches that only show the next world along read as "keep going", not as nothing happening.
        if (lesson.Title.StartsWith("5 · Zoom out", StringComparison.Ordinal) && _planets.UniverseOpen && _lessonDoneFor <= 0)
        {
            var (_, zoomTarget) = _planets.UniverseZoom;
            _lessonFeedback.Text = zoomTarget > _tutZoom0 * ZoomProgressFrom ? $"Keep scrolling ↓ · {_planets.UniverseWorldsInView} worlds in view" : "";
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

    /// <summary>The current step's keys as shown on the card, joined with "+" (probes and the state file).</summary>
    public string TutorialKeys => _lesson >= 0 && _lessons != null ? string.Join("+", _lessons[_lesson].Keys) : "";

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
            ShowLesson("Learn to play · done", "*You've got the basics!*\nLook, run, jump, fly, read a README, go up and back, and the overview. Explore your own computer next?", "", feedback: "✓ All done!");
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
        _lessonText.Text = LessonBbcode(text);
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

    /// <summary>
    /// A lesson's text as the card shows it (PLANET-94): the first line is the action, with its keys as caps
    /// (<see cref="LessonText"/>); a second line after a line break is a short tip, smaller and quieter.
    /// </summary>
    private static string LessonBbcode(string lesson)
    {
        var cut = lesson.IndexOf('\n');
        if (cut < 0) return LessonText.ToBbcode(lesson);
        return LessonText.ToBbcode(lesson[..cut]) + $"\n[font_size={LessonTipPixels}][color={LessonTipColor}]{LessonText.ToBbcode(lesson[(cut + 1)..])}[/color][/font_size]";
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
            bearing = _planets.CameraTurnTo(tg.Direction);
            distance = _planets.DistanceTo(tg.Direction);
        }

        // The screen cue (AD-55): mode none|onscreen|horizon|edge, where it is drawn (window pixels), the edge arrow's
        // screen angle (degrees, 0 = right, 90 = down, 180 = left), distance to the spot's edge (m), phase far|near|here.
        var cue = _experience == Experience.Planets ? _planets.TutorialCue : TutorialCueState.Hidden;
        var cueAt = cue.Anchor * scale;
        var cueJson = System.FormattableString.Invariant(
            $"\"cue\":{{\"mode\":\"{cue.Mode.ToString().ToLowerInvariant()}\",\"x\":{cueAt.X:0},\"y\":{cueAt.Y:0},\"angle\":{cue.AngleDegrees:0},\"distance\":{cue.DistanceMetres:0.##},\"phase\":\"{cue.Phase.ToString().ToLowerInvariant()}\",\"overHorizon\":{(cue.OverHorizon ? "true" : "false")},\"action\":\"{JsonEscape(cue.Action)}\",\"keys\":\"{JsonEscape(TutorialKeys)}\"}},");

        return System.FormattableString.Invariant(
            $"\"welcome\":{(_welcomeOpen ? "true" : "false")},\"showWelcomePref\":{(_prefs.ShowWelcome ? "true" : "false")},\"atHub\":{(atHub ? "true" : "false")},\"tutorialActive\":{(TutorialActive ? "true" : "false")},\"tutorialStep\":{_lesson},\"tutorialTitle\":\"{JsonEscape(TutorialTitle)}\",\"offRoute\":{(TutorialOffRoute ? "true" : "false")},\"helpOpen\":{(_hud.HelpVisible ? "true" : "false")},\"targetBearing\":{bearing:0.#},\"targetDistance\":{distance:0.##},") + cueJson +
            $"\"welcomeLearn\":{At(WelcomeButtonRect(0))},\"welcomeOwn\":{At(WelcomeButtonRect(1))},\"welcomeNever\":{At(WelcomeButtonRect(2))},\"helpLearn\":{At(_hud.LearnToPlayButtonRect)},\"lessonLeave\":{At(LessonButtonRect("leave"))},\"lessonSkip\":{At(LessonButtonRect("skip"))},\"lessonBack\":{At(LessonButtonRect("back"))},\"legacyMenu\":{At(_hud.LegacyMenuButtonRect)},\"legacyItem0\":{At(_hud.VisualizationButtonRect(-1))},";
    }
}
