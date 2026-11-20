using FileScape.Core.Model;
using FileScape.Core.Planets;
using FileScape.Core.Scanning;

namespace FileScape.Core.Tests;

/// <summary>
/// Media worlds (PLANET-130, PLANET-135, AD-86): which folders take a gallery's character — by the marker, by what they
/// hold, by being (or being inside) a picture library such as a Pictures folder redirected into OneDrive, or by standing
/// inside a gallery — and how a cloud-synced picture that is on this PC differs from one that is only in the cloud.
/// Written before the code (failing first). The trees mirror the shape of a real OneDrive Pictures folder (counts and
/// kinds only; the names here are generic).
/// </summary>
public sealed class MediaWorldTests
{
    private const string Pictures = @"C:\Users\someone\OneDrive\Pictures";
    private static readonly PictureLibraries Libraries = new([Pictures]);

    private static FsTree Live(Spec root, string path) => root.Build(new DataSourceInfo(DataOrigin.LiveFilesystem, path, "test"));

    /// <summary>A OneDrive Pictures folder: 45 pictures (most online-only) and 4 videos at the top, desktop.ini, and the
    /// usual children: an empty Saved Pictures, a Screenshots folder of 31, a Camera Roll of one, a project folder.</summary>
    private static FsTree OneDrivePictures()
    {
        const NodeFlags online = NodeFlags.CloudPlaceholder | NodeFlags.CloudSynced | NodeFlags.ReparsePoint | NodeFlags.Sparse;
        const NodeFlags local = NodeFlags.CloudSynced | NodeFlags.ReparsePoint;
        var root = new Spec("Pictures");
        for (var i = 0; i < 45; i++) root.File($"picture-{i:D2}{(i % 3 == 0 ? ".PNG" : i % 7 == 0 ? ".GIF" : ".jpg")}", 200_000 + i * 1000, i % 10 == 0 ? local : online);
        for (var i = 0; i < 4; i++) root.File($"clip-{i}.mp4", 9_000_000, online);
        root.File("desktop.ini", 500, NodeFlags.Hidden | NodeFlags.System);
        root.Dir(new Spec("Saved Pictures"));
        var shots = new Spec("Screenshots 1");
        for (var i = 0; i < 31; i++) shots.File($"shot-{i:D2}.png", 400_000, online);
        root.Dir(shots);
        root.Dir(new Spec("Camera Roll").File("one.jpg", 3_000_000, local));
        root.Dir(new Spec("Video Projects").File("project.json", 900));
        root.Dir(new Spec("Profile Pictures").File("a.png", 50_000).File("b.png", 50_000).File("notes.txt", 100).File("c.docx", 9000).File("d.pdf", 9000));
        return Live(root, Pictures);
    }

    private static int Id(FsTree t, string name) => t.Find(name).Id;

    private static bool NotHidden(FsNode n) => (n.Flags & (NodeFlags.Hidden | NodeFlags.System)) == 0;

    // ------------------------------------------------------------------ cloud files (PLANET-135)

    [Fact]
    public void A_cloud_synced_file_on_this_PC_is_not_a_placeholder()
    {
        // OneDrive tags every file it manages (IO_REPARSE_TAG_CLOUD_*), on this PC or not: the tag alone says "synced".
        var file = Reparse.Classify(false, 0x9000301A, () => null);
        file.Kind.Should().Be(NodeKind.File);
        file.ExtraFlags.Should().HaveFlag(NodeFlags.CloudSynced).And.NotHaveFlag(NodeFlags.CloudPlaceholder, "a hydrated photo is an ordinary local file");
        // Whether it is only in the cloud comes from its attributes (0x401620 as listed: recall on data access, offline, sparse, reparse).
        DirectoryScanner.FlagsFor((FileAttributes)0x00401620).Should().HaveFlag(NodeFlags.CloudPlaceholder);
        DirectoryScanner.FlagsFor((FileAttributes)0x00000420).Should().NotHaveFlag(NodeFlags.CloudPlaceholder, "reparse + archive: synced and on this PC");
        // Folders keep their caution (unchanged: Overview discovery leaves cloud folders alone).
        var folder = Reparse.Classify(true, 0x9000701A, () => null);
        folder.ExtraFlags.Should().HaveFlag(NodeFlags.CloudPlaceholder).And.HaveFlag(NodeFlags.CloudSynced);
    }

    [Fact]
    public void Picture_verdicts_from_metadata()
    {
        var t = new Spec("p")
            .File("local.jpg", 100_000, NodeFlags.CloudSynced | NodeFlags.ReparsePoint)
            .File("online.jpg", 100_000, NodeFlags.CloudPlaceholder | NodeFlags.CloudSynced | NodeFlags.ReparsePoint)
            .File("plain.png", 100_000)
            .File("link.png", 100, NodeFlags.ReparsePoint)
            .File("phone.heic", 2_000_000)
            .File("huge.png", GalleryRules.MaxPictureBytes + 1)
            .File("huge-online.jpg", GalleryRules.MaxPictureBytes + 1, NodeFlags.CloudPlaceholder | NodeFlags.CloudSynced | NodeFlags.ReparsePoint)
            .Build();
        GalleryRules.Verdict(t.Find("local.jpg")).Should().Be(GalleryRules.PictureVerdict.Viewable, "on this PC, so read like any file");
        GalleryRules.Verdict(t.Find("online.jpg")).Should().Be(GalleryRules.PictureVerdict.Download, "DJ allowed pictures to be fetched from their cloud");
        GalleryRules.Verdict(t.Find("plain.png")).Should().Be(GalleryRules.PictureVerdict.Viewable);
        GalleryRules.Verdict(t.Find("link.png")).Should().Be(GalleryRules.PictureVerdict.Link, "a real link is still never read through");
        GalleryRules.Verdict(t.Find("phone.heic")).Should().Be(GalleryRules.PictureVerdict.WindowsOnly, "FileScape cannot decode it; Windows' thumbnail shows it");
        GalleryRules.Verdict(t.Find("huge.png")).Should().Be(GalleryRules.PictureVerdict.TooLarge);
        GalleryRules.Verdict(t.Find("huge-online.jpg")).Should().Be(GalleryRules.PictureVerdict.TooLarge, "never a 24 MiB download for a picture");
        GalleryRules.IsPicture("phone.HEIC").Should().BeTrue();
        GalleryRules.IsPicture("anim.gif").Should().BeTrue();
        GalleryRules.Decodes("anim.gif").Should().BeFalse();
        GalleryRules.Decodes("a.JPG").Should().BeTrue();
        // A cloud picture asks no question before Windows opens it (opening downloads it, as DJ allowed); a cloud document still does.
        GalleryRules.OpensWithoutAsking(t.Find("online.jpg")).Should().BeTrue();
    }

    // ------------------------------------------------------------------ media character (PLANET-130)

    [Fact]
    public void A_redirected_pictures_library_and_its_children_are_galleries_when_their_contents_support_it()
    {
        var t = OneDrivePictures();
        var root = MediaWorld.Classify(t, 0, Libraries, NotHidden);
        root.Character.Should().Be(MediaCharacter.Gallery);
        root.Reason.Should().Be(MediaReason.MostlyPictures);
        root.Pictures.Should().Be(45);
        root.Files.Should().Be(49, "desktop.ini is hidden and not counted");

        MediaWorld.Classify(t, Id(t, "Screenshots 1"), Libraries, NotHidden).Character.Should().Be(MediaCharacter.Gallery);
        var roll = MediaWorld.Classify(t, Id(t, "Camera Roll"), Libraries, NotHidden);
        roll.Character.Should().Be(MediaCharacter.Gallery, "one picture is enough inside a picture library");
        roll.Reason.Should().Be(MediaReason.PictureLibrary);
        var profile = MediaWorld.Classify(t, Id(t, "Profile Pictures"), Libraries, NotHidden);
        profile.Character.Should().Be(MediaCharacter.Gallery, "2 of 5 inside a library is enough (a mixed folder in a picture library)");
        MediaWorld.Classify(t, Id(t, "Saved Pictures"), Libraries, NotHidden).Character.Should().Be(MediaCharacter.None, "empty: nothing to hang");
        MediaWorld.Classify(t, Id(t, "Video Projects"), Libraries, NotHidden).Character.Should().Be(MediaCharacter.None, "no pictures");
    }

    [Fact]
    public void Outside_a_library_the_contents_decide_and_a_mixed_folder_stays_ordinary()
    {
        var downloads = new Spec("Downloads");
        for (var i = 0; i < 3; i++) downloads.File($"img-{i}.png", 90_000);
        for (var i = 0; i < 10; i++) downloads.File($"doc-{i}.pdf", 90_000);
        var trip = new Spec("Trip");
        for (var i = 0; i < 5; i++) trip.File($"trip-{i}.jpg", 2_000_000);
        trip.File("route.txt", 300);
        trip.Dir(new Spec("Day 2").File("x.jpg", 1).File("y.jpg", 1).File("notes.txt", 1).File("plan.txt", 1));
        trip.Dir(new Spec("Receipts").File("r.jpg", 1).File("a.pdf", 1).File("b.pdf", 1).File("c.pdf", 1).File("d.pdf", 1).File("e.pdf", 1));
        var two = new Spec("Two").File("a.png", 1).File("b.png", 1);
        var t = Live(new Spec("someone").Dir(downloads).Dir(trip).Dir(two), @"C:\Users\someone");

        var d = MediaWorld.Classify(t, Id(t, "Downloads"), Libraries);
        d.Character.Should().Be(MediaCharacter.SomePictures, "3 of 13: pictures among other files");
        var tr = MediaWorld.Classify(t, Id(t, "Trip"), Libraries);
        tr.Character.Should().Be(MediaCharacter.Gallery);
        tr.Reason.Should().Be(MediaReason.MostlyPictures);
        var day2 = MediaWorld.Classify(t, Id(t, "Day 2"), Libraries);
        day2.Character.Should().Be(MediaCharacter.Gallery, "half pictures, inside a gallery");
        day2.Reason.Should().Be(MediaReason.InsideGallery);
        MediaWorld.Classify(t, Id(t, "Receipts"), Libraries).Character.Should().Be(MediaCharacter.SomePictures, "1 of 6 is not a gallery even inside one");
        MediaWorld.Classify(t, Id(t, "Two"), Libraries).Character.Should().Be(MediaCharacter.SomePictures, "two pictures alone are too few to call a gallery");
    }

    [Fact]
    public void Library_paths_match_whole_folders_without_case_or_a_trailing_separator()
    {
        var libs = new PictureLibraries([@"c:\users\someone\onedrive\pictures\"]);
        libs.Contains(Pictures).Should().BeTrue();
        libs.Contains(Pictures + @"\Screenshots").Should().BeTrue();
        libs.Contains(@"C:\Users\someone\OneDrive\Pictures2\x").Should().BeFalse("a whole folder, not a prefix of a name");
        libs.Contains(@"C:\Users\someone\Pictures").Should().BeFalse("the old, unredirected folder is not the library");
        new PictureLibraries(["/home/someone/Pictures"]).Contains("/home/someone/pictures").Should().BeFalse("POSIX paths keep their case");
        PictureLibraries.Empty.Contains(Pictures).Should().BeFalse();

        var t = Live(new Spec("Holiday").File("a.png", 1).File("a.txt", 1).File("b.txt", 1).File("c.txt", 1), Pictures + @"\Holiday");
        MediaWorld.Classify(t, 0, libs).Character.Should().Be(MediaCharacter.Gallery, "a quarter pictures, inside the library");
        MediaWorld.Classify(t, 0, PictureLibraries.Empty).Character.Should().Be(MediaCharacter.SomePictures);
    }

    [Fact]
    public void The_marker_still_makes_a_gallery_and_unknown_listings_make_nothing()
    {
        var t = new Spec("top")
            .Dir(new Spec("Marked").File(GalleryRules.MarkerName, 0).File("a.txt", 1))
            .Dir(new Spec("Waiting") { State = DirScanState.Pending })
            .Build();
        var m = MediaWorld.Classify(t, Id(t, "Marked"), PictureLibraries.Empty);
        m.Character.Should().Be(MediaCharacter.Gallery);
        m.Reason.Should().Be(MediaReason.Marker);
        m.Files.Should().Be(1, "the marker is not one of the folder's files");
        MediaWorld.Classify(t, Id(t, "Waiting"), Libraries).Character.Should().Be(MediaCharacter.None);
        MediaWorld.Classify(t, 0, Libraries).Character.Should().Be(MediaCharacter.None, "folders only");
    }
}
