using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileScape.Core.Shell;

/// <summary>A thumbnail from Windows: RGBA8 pixels, top row first, alpha straight (opaque for photos).</summary>
public sealed record ShellThumbnail(byte[] Rgba, int Width, int Height, bool FromCache, double Milliseconds);

/// <summary>
/// Picture thumbnails from Windows' own thumbnail machinery (PLANET-131, AD-86): <c>IShellItemImageFactory::GetImage</c>,
/// which reads the system thumbnail cache (thumbcache_*.db, the one Explorer fills) and, on a miss, runs the file type's
/// thumbnail handler and stores the result there. Chosen over decoding every file ourselves because Windows already has
/// most pictures' thumbnails cached at 256 and 1024 px, decodes every format it has a handler for (HEIC, GIF, TIFF, RAW
/// with codecs) and, for files a cloud provider keeps online only, asks the provider for a thumbnail instead of
/// downloading the whole file (OneDrive serves them from the cloud; measured in AD-86).
/// <para>
/// Each request first asks the cache only (<c>SIIGBF_INCACHEONLY</c>: no file is opened; a few milliseconds), then, when
/// <c>cacheOnly</c> is false, lets Windows extract it (<c>SIIGBF_THUMBNAILONLY</c>: never an icon instead). Requests run
/// on <see cref="WorkerCount"/> dedicated single-threaded-apartment threads (thumbnail handlers are COM objects that
/// expect an STA), queued in order; the caller bounds how many it asks for at once (<c>ThumbnailPlan</c>).
/// </para>
/// Preserve: read-only — nothing here changes a file, its attributes or its pin state; a result is null (never an
/// exception) when Windows has no thumbnail; the HBITMAP is always freed; Windows only (null elsewhere).
/// </summary>
public static class ShellThumbnails
{
    /// <summary>Worker threads (STA). Two: Explorer's own thumbnail extraction is parallel, but more would compete with
    /// the game's frame for the disk and the CPU; the planner keeps at most this many requests in flight.</summary>
    public const int WorkerCount = 2;

    private static readonly BlockingCollection<Action> Queue = new();
    private static int _started;

    /// <summary>Requests served from the cache, extracted, and refused (no thumbnail), since start (diagnostics).</summary>
    public static int CacheHits => Volatile.Read(ref _cacheHits);

    public static int Extracted => Volatile.Read(ref _extracted);

    public static int Misses => Volatile.Read(ref _misses);

    private static int _cacheHits, _extracted, _misses;

    /// <summary>
    /// A thumbnail of <paramref name="path"/> fitting <paramref name="pixels"/> × <paramref name="pixels"/> (aspect kept),
    /// or null. <paramref name="cacheOnly"/>: only Windows' cache is asked (no file is opened).
    /// </summary>
    public static Task<ShellThumbnail?> GetAsync(string path, int pixels, bool cacheOnly = false)
    {
        if (!OperatingSystem.IsWindows()) return Task.FromResult<ShellThumbnail?>(null);
        Start();
        var done = new TaskCompletionSource<ShellThumbnail?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Add(() =>
        {
            try { done.SetResult(Get(path, pixels, cacheOnly)); }
            catch (Exception e) { done.TrySetException(e); }
        });
        return done.Task;
    }

    private static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        for (var i = 0; i < WorkerCount; i++)
        {
            var t = new Thread(() =>
            {
                foreach (var work in Queue.GetConsumingEnumerable()) work();
            })
            { IsBackground = true, Name = $"shell-thumbnails-{i}", Priority = ThreadPriority.BelowNormal };
            if (OperatingSystem.IsWindows()) t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }
    }

    /// <summary>The synchronous request (on an STA thread): the cache first, then extraction unless <paramref name="cacheOnly"/>.</summary>
    public static ShellThumbnail? Get(string path, int pixels, bool cacheOnly)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var sw = Stopwatch.StartNew();
        IShellItemImageFactory? factory = null;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory) != 0 || factory == null) { Interlocked.Increment(ref _misses); return null; }
            var size = new Size { Cx = pixels, Cy = pixels };
            if (factory.GetImage(size, ThumbnailOnly | InCacheOnly, out var bmp) == 0 && bmp != IntPtr.Zero)
            {
                Interlocked.Increment(ref _cacheHits);
                return ToPixels(bmp, true, sw);
            }

            if (cacheOnly) { Interlocked.Increment(ref _misses); return null; }
            if (factory.GetImage(size, ThumbnailOnly, out bmp) == 0 && bmp != IntPtr.Zero)
            {
                Interlocked.Increment(ref _extracted);
                return ToPixels(bmp, false, sw);
            }

            Interlocked.Increment(ref _misses);
            return null;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException or UnauthorizedAccessException or IOException)
        {
            Interlocked.Increment(ref _misses);
            return null;
        }
        finally
        {
            if (factory != null && Marshal.IsComObject(factory)) Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>Copies a 32-bit HBITMAP's pixels as RGBA, top row first, and frees it. Windows gives BGRA, premultiplied
    /// where there is alpha; a photo's thumbnail often has alpha 0 throughout (no alpha channel): then it is made opaque.</summary>
    private static ShellThumbnail? ToPixels(IntPtr bmp, bool fromCache, Stopwatch sw)
    {
        try
        {
            if (GetObject(bmp, Marshal.SizeOf<Bitmap>(), out var info) == 0 || info.Width <= 0 || info.Height == 0) return null;
            int w = info.Width, h = Math.Abs(info.Height);
            var header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = w, Height = -h, Planes = 1, BitCount = 32, Compression = 0,
            };
            var px = new byte[w * h * 4];
            var dc = GetDC(IntPtr.Zero);
            try
            {
                if (GetDIBits(dc, bmp, 0, (uint)h, px, ref header, 0) == 0) return null;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }

            var anyAlpha = false;
            for (var i = 3; i < px.Length; i += 4) if (px[i] != 0) { anyAlpha = true; break; }
            for (var i = 0; i < px.Length; i += 4)
            {
                (px[i], px[i + 2]) = (px[i + 2], px[i]); // BGRA → RGBA
                if (!anyAlpha) px[i + 3] = 255;
                else if (px[i + 3] is > 0 and < 255 and var a)
                {
                    // Un-premultiply, so the texture's straight alpha blends as Windows drew it.
                    px[i] = (byte)Math.Min(255, px[i] * 255 / a);
                    px[i + 1] = (byte)Math.Min(255, px[i + 1] * 255 / a);
                    px[i + 2] = (byte)Math.Min(255, px[i + 2] * 255 / a);
                }
            }

            return new ShellThumbnail(px, w, h, fromCache, sw.Elapsed.TotalMilliseconds);
        }
        finally
        {
            DeleteObject(bmp);
        }
    }

    // ------------------------------------------------------------------ Win32 (values from ShObjIdl_core.h, wingdi.h)

    /// <summary>SIIGBF_THUMBNAILONLY: a thumbnail or nothing, never the file type's icon.</summary>
    private const int ThumbnailOnly = 0x08;

    /// <summary>SIIGBF_INCACHEONLY: only what the thumbnail cache already holds; no handler runs, no file is opened.</summary>
    private const int InCacheOnly = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int Cx, Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public ushort Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(Size size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr h, int c, out Bitmap pv);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader bmi, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr h);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
}
