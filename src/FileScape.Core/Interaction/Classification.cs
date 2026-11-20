namespace FileScape.Core.Interaction;

/// <summary>
/// FSN's file-age colour bands. Appendix A records the shipped defaults: cut-offs at 7, 14, 31, 92, 182 and 365
/// days, giving seven bands. FileScape measures age from last-write time, inclusive at the upper bound. That is a
/// documented choice, not a historical fact: FSN's exact timestamp and inclusivity are unverified.
/// </summary>
public static class AgeBands
{
    /// <summary>Upper bounds of bands 0–5, whole days (one week, two weeks, a month, three months, six months, a year); band 6 is everything older.</summary>
    public static readonly int[] CutoffDays = [7, 14, 31, 92, 182, 365];

    /// <summary>The legend text for each band, index for index with <see cref="Band"/>.</summary>
    public static readonly string[] Labels = ["≤ 1 week", "≤ 2 weeks", "≤ 1 month", "≤ 3 months", "≤ 6 months", "≤ 1 year", "> 1 year"];

    /// <summary>Returns a band from 0 (newest) to 6 (oldest). Unknown times return 6.</summary>
    public static int Band(DateTime? modifiedUtc, DateTime referenceUtc)
    {
        if (modifiedUtc is null)
        {
            return CutoffDays.Length;
        }

        var days = (referenceUtc - modifiedUtc.Value).TotalDays;
        for (var i = 0; i < CutoffDays.Length; i++)
        {
            if (days <= CutoffDays[i])
            {
                return i;
            }
        }

        return CutoffDays.Length;
    }
}

/// <summary>The coarse file types the District map colours by (<see cref="FileCategories.Of"/>).</summary>
public enum FileCategory
{
    Document,
    Image,
    Audio,
    Video,
    Archive,
    Code,
    Executable,
    Data,
    Other,
}

/// <summary>A coarse file type taken from the extension. It is used for the District colour mapping only. Contents are never read.</summary>
public static class FileCategories
{
    private static readonly Dictionary<string, FileCategory> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = FileCategory.Document, [".md"] = FileCategory.Document, [".pdf"] = FileCategory.Document,
        [".doc"] = FileCategory.Document, [".docx"] = FileCategory.Document, [".xlsx"] = FileCategory.Document,
        [".pptx"] = FileCategory.Document, [".rtf"] = FileCategory.Document, [".odt"] = FileCategory.Document,
        [".jpg"] = FileCategory.Image, [".jpeg"] = FileCategory.Image, [".png"] = FileCategory.Image,
        [".gif"] = FileCategory.Image, [".bmp"] = FileCategory.Image, [".svg"] = FileCategory.Image,
        [".webp"] = FileCategory.Image, [".psd"] = FileCategory.Image, [".ico"] = FileCategory.Image,
        // Pictures Windows' thumbnails show (PLANET-131): a phone's HEIC, scans' TIFF, AVIF, JFIF.
        [".heic"] = FileCategory.Image, [".heif"] = FileCategory.Image, [".tif"] = FileCategory.Image, [".tiff"] = FileCategory.Image,
        [".avif"] = FileCategory.Image, [".jfif"] = FileCategory.Image,
        [".mp3"] = FileCategory.Audio, [".flac"] = FileCategory.Audio, [".wav"] = FileCategory.Audio,
        [".ogg"] = FileCategory.Audio, [".m4a"] = FileCategory.Audio,
        [".mp4"] = FileCategory.Video, [".mkv"] = FileCategory.Video, [".mov"] = FileCategory.Video,
        [".avi"] = FileCategory.Video, [".webm"] = FileCategory.Video,
        [".zip"] = FileCategory.Archive, [".7z"] = FileCategory.Archive, [".rar"] = FileCategory.Archive,
        [".gz"] = FileCategory.Archive, [".tar"] = FileCategory.Archive, [".nupkg"] = FileCategory.Archive,
        [".iso"] = FileCategory.Archive, [".vhd"] = FileCategory.Archive, [".vhdx"] = FileCategory.Archive,
        [".img"] = FileCategory.Archive, [".tpz"] = FileCategory.Archive,
        [".cs"] = FileCategory.Code, [".py"] = FileCategory.Code, [".js"] = FileCategory.Code,
        [".ts"] = FileCategory.Code, [".go"] = FileCategory.Code, [".cpp"] = FileCategory.Code,
        [".h"] = FileCategory.Code, [".c"] = FileCategory.Code, [".rs"] = FileCategory.Code,
        [".java"] = FileCategory.Code, [".gd"] = FileCategory.Code, [".xml"] = FileCategory.Code,
        [".html"] = FileCategory.Code, [".css"] = FileCategory.Code, [".csproj"] = FileCategory.Code,
        [".exe"] = FileCategory.Executable, [".dll"] = FileCategory.Executable, [".msi"] = FileCategory.Executable,
        [".bat"] = FileCategory.Executable, [".cmd"] = FileCategory.Executable, [".ps1"] = FileCategory.Executable,
        [".so"] = FileCategory.Executable, [".sys"] = FileCategory.Executable,
        [".json"] = FileCategory.Data, [".db"] = FileCategory.Data, [".sqlite"] = FileCategory.Data,
        [".csv"] = FileCategory.Data, [".log"] = FileCategory.Data, [".ini"] = FileCategory.Data,
        [".yaml"] = FileCategory.Data, [".yml"] = FileCategory.Data, [".lock"] = FileCategory.Data,
        [".bin"] = FileCategory.Data, [".dat"] = FileCategory.Data, [".pdb"] = FileCategory.Data,
    };

    /// <summary>The category of a file name's extension (case-insensitive); unknown or no extension is <see cref="FileCategory.Other"/>.</summary>
    public static FileCategory Of(string name) =>
        ByExtension.TryGetValue(Path.GetExtension(name), out var c) ? c : FileCategory.Other;
}

/// <summary>Byte-size formatting that always keeps the exact value available alongside the short form.</summary>
public static class SizeFormat
{
    private static readonly string[] Units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];

    /// <summary>
    /// Binary units (1 KiB = 1024 B) with about three significant figures: "512 B", "1.50 KiB", "23.4 MiB", "640 GiB".
    /// </summary>
    public static string Short(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < Units.Length - 1)
        {
            v /= 1024;
            u++;
        }

        return v >= 100 ? $"{v:0} {Units[u]}" : v >= 10 ? $"{v:0.0} {Units[u]}" : $"{v:0.00} {Units[u]}";
    }

    /// <summary>The exact count with digit grouping ("1,234,567 bytes"), shown beside the short form.</summary>
    public static string Exact(long bytes) => $"{bytes:N0} bytes";
}
