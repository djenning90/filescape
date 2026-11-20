using System.Reflection;
using System.Runtime.InteropServices;

namespace FileScape.Core.Shell;

/// <summary>
/// Starts something the way a double-click on the desktop does: the request is carried out by the desktop's own File
/// Explorer process (IShellWindows → the desktop's shell view → IShellDispatch2.ShellExecute), so what starts is
/// Explorer's child, not FileScape's (PLANET-89, AD-76).
/// <para>
/// Why: under FileScape.console.exe (Godot's console wrapper) FileScape runs in a job object; the wrapper waits until
/// every process in the job has ended and kills them all when its job closes (Ctrl+C). An editor or viewer FileScape
/// started with ShellExecuteEx joined that job, so the terminal stayed busy after the window's X until that app closed,
/// and Ctrl+C then killed the person's app. A process started by Explorer is outside the job.
/// </para>
/// <para>
/// Preserve: call from a thread with COM initialised as single-threaded (the app's main thread; WindowsShellActions does
/// CoInitializeEx). It is fire-and-forget — Explorer reports nothing back, so callers check what they can beforehand
/// (the file exists) and say that Windows was asked. False (with the reason) when there is no desktop shell to ask (no
/// Explorer, another shell): callers fall back to their own ShellExecuteEx.
/// </para>
/// </summary>
public static class DesktopShell
{
    /// <summary>Asks the desktop's Explorer to ShellExecute <paramref name="file"/> (default verb when <paramref name="verb"/> is empty).</summary>
    public static bool TryShellExecute(string file, string? args, string? directory, string verb, out string error)
    {
        error = "";
        if (!OperatingSystem.IsWindows()) { error = "not Windows"; return false; }
        object? windows = null, desktop = null, browser = null, view = null, background = null, app = null;
        try
        {
            var type = Type.GetTypeFromCLSID(ClsidShellWindows, throwOnError: true)!;
            windows = Activator.CreateInstance(type)!;
            object loc = CsidlDesktop, root = null!;
            desktop = ((IShellWindows)windows).FindWindowSW(ref loc, ref root, SwcDesktop, out _, SwfoNeedDispatch);
            if (desktop == null) { error = "no desktop shell window"; return false; }
            var sid = SidTopLevelBrowser; var iid = IidShellBrowser;
            ((IServiceProvider)desktop).QueryService(ref sid, ref iid, out browser);
            ((IShellBrowser)browser).QueryActiveShellView(out view);
            var dispatch = IidDispatch;
            ((IShellView)view).GetItemObject(SvgioBackground, ref dispatch, out background);
            app = background.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, background, null)!;
            app.GetType().InvokeMember("ShellExecute", BindingFlags.InvokeMethod, null, app, [file, args ?? "", directory ?? "", verb, SwShowNormal]);
            return true;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or TargetInvocationException or MissingMethodException or ArgumentException)
        {
            error = $"{e.GetType().Name}: {(e.InnerException ?? e).Message}";
            return false;
        }
        finally
        {
            foreach (var o in new[] { app, background, view, browser, desktop, windows })
            {
                if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
            }
        }
    }

    // Windows' fixed identifiers and values (exdisp.h, shlobj.h, shobjidl.h, servprov.h).
    private static readonly Guid ClsidShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IidShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid IidDispatch = new("00020400-0000-0000-C000-000000000046");
    private const int CsidlDesktop = 0, SwcDesktop = 8, SwfoNeedDispatch = 1, SwShowNormal = 1;
    private const uint SvgioBackground = 0;

    /// <summary>IShellWindows (dual): only FindWindowSW is called; the methods before it hold its vtable slot.</summary>
    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        void Count_();
        void Item_();
        void NewEnum_();
        void Register_();
        void RegisterPending_();
        void Revoke_();
        void OnNavigate_();
        void OnActivated_();

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW([In] ref object pvarLoc, [In] ref object pvarLocRoot, int swClass, out int phwnd, int swfwOptions);
    }

    /// <summary>COM's IServiceProvider: the desktop window's way to its shell browser.</summary>
    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        void QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppvObject);
    }

    /// <summary>IShellBrowser: only QueryActiveShellView (the 13th method, after IOleWindow's two) is called.</summary>
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow_();
        void ContextSensitiveHelp_();
        void InsertMenusSB_();
        void SetMenuSB_();
        void RemoveMenusSB_();
        void SetStatusTextSB_();
        void EnableModelessSB_();
        void TranslateAcceleratorSB_();
        void BrowseObject_();
        void GetViewStateStream_();
        void GetControlWindow_();
        void SendControlMsg_();
        void QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out object ppshv);
    }

    /// <summary>IShellView: only GetItemObject (the 13th method, after IOleWindow's two) is called.</summary>
    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow_();
        void ContextSensitiveHelp_();
        void TranslateAccelerator_();
        void EnableModeless_();
        void UIActivate_();
        void Refresh_();
        void CreateViewWindow_();
        void DestroyViewWindow_();
        void GetCurrentInfo_();
        void AddPropertySheetPages_();
        void SaveViewState_();
        void SelectItem_();
        void GetItemObject(uint uItem, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
    }
}
