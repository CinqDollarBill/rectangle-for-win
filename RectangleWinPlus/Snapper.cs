using System.Runtime.InteropServices;
using System.Text;

namespace RectangleWinPlus;

public enum SnapAction
{
    LeftHalf,
    RightHalf,
    TopHalf,
    BottomHalf,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Maximize,
}

public enum SnapResult
{
    Ok,
    NoWindow,
    NotSnappable,
    AccessDenied,
    Failed,
}

internal static class Snapper
{
    /// <summary>Windows we must never move: the desktop, the taskbar, the task-switcher shell.</summary>
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "MultitaskingViewFrame",
        "Windows.UI.Core.CoreWindow",
        "ForegroundStaging",
    };

    public static string FriendlyName(SnapAction action) => action switch
    {
        SnapAction.LeftHalf => "Left half",
        SnapAction.RightHalf => "Right half",
        SnapAction.TopHalf => "Top half",
        SnapAction.BottomHalf => "Bottom half",
        SnapAction.TopLeft => "Top-left quarter",
        SnapAction.TopRight => "Top-right quarter",
        SnapAction.BottomLeft => "Bottom-left quarter",
        SnapAction.BottomRight => "Bottom-right quarter",
        SnapAction.Maximize => "Maximize",
        _ => action.ToString(),
    };

    public static SnapResult Apply(IntPtr hwnd, SnapAction action, int gap)
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return SnapResult.NoWindow;
        if (!IsSnappable(hwnd)) return SnapResult.NotSnappable;

        // Maximizing an already-maximized window: leave it alone rather than restore-then-remaximize,
        // which the user sees as a flicker.
        if (action == SnapAction.Maximize && gap <= 0 && Native.IsZoomed(hwnd) && !Native.IsIconic(hwnd))
            return SnapResult.Ok;

        // A maximized or minimized window ignores SetWindowPos geometry, and its DWM frame
        // bounds describe the maximized frame rather than the restored one. Restore first.
        if (Native.IsIconic(hwnd) || Native.IsZoomed(hwnd))
            Native.ShowWindow(hwnd, Native.SW_RESTORE);

        if (!TryGetWorkArea(hwnd, out var work)) return SnapResult.Failed;

        // With no gap, a true maximize is better than a work-area-sized rectangle: the window
        // reports itself maximized, gets the restore button, and cooperates with autohide taskbars.
        if (action == SnapAction.Maximize && gap <= 0)
        {
            Native.ShowWindow(hwnd, Native.SW_MAXIMIZE);
            if (Native.IsZoomed(hwnd)) return SnapResult.Ok;
            // Fall through to the manual path, which reports a usable error code.
        }

        var target = ComputeTarget(work, action, gap);
        var (dl, dt, dr, db) = FrameOffsets(hwnd);

        bool moved = Native.SetWindowPos(
            hwnd, IntPtr.Zero,
            target.Left - dl,
            target.Top - dt,
            target.Width + dl + dr,
            target.Height + dt + db,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);

        if (moved) return SnapResult.Ok;

        int err = Marshal.GetLastWin32Error();
        Log.Warn($"SetWindowPos failed for {action}: win32 error {err}");
        return err == Native.ERROR_ACCESS_DENIED ? SnapResult.AccessDenied : SnapResult.Failed;
    }

    /// <summary>Where the window's visible frame should end up. Pure geometry, no Win32 state.</summary>
    internal static Native.RECT ComputeTarget(Native.RECT work, SnapAction action, int gap) =>
        ApplyGap(work, Cell(work, action), gap);

    /// <summary>The rectangle this action occupies within the monitor work area, before gaps.</summary>
    private static Native.RECT Cell(Native.RECT w, SnapAction action)
    {
        int midX = w.Left + w.Width / 2;
        int midY = w.Top + w.Height / 2;

        return action switch
        {
            SnapAction.LeftHalf => Rect(w.Left, w.Top, midX, w.Bottom),
            SnapAction.RightHalf => Rect(midX, w.Top, w.Right, w.Bottom),
            SnapAction.TopHalf => Rect(w.Left, w.Top, w.Right, midY),
            SnapAction.BottomHalf => Rect(w.Left, midY, w.Right, w.Bottom),
            SnapAction.TopLeft => Rect(w.Left, w.Top, midX, midY),
            SnapAction.TopRight => Rect(midX, w.Top, w.Right, midY),
            SnapAction.BottomLeft => Rect(w.Left, midY, midX, w.Bottom),
            SnapAction.BottomRight => Rect(midX, midY, w.Right, w.Bottom),
            _ => w,
        };
    }

    /// <summary>
    /// Insets a cell so that every visible gutter is exactly <paramref name="gap"/> wide: a full gap
    /// against the screen edge, half a gap on each side of a seam two windows share.
    /// </summary>
    private static Native.RECT ApplyGap(Native.RECT work, Native.RECT cell, int gap)
    {
        if (gap <= 0) return cell;
        int half = gap / 2;

        return Rect(
            cell.Left + (cell.Left == work.Left ? gap : half),
            cell.Top + (cell.Top == work.Top ? gap : half),
            cell.Right - (cell.Right == work.Right ? gap : half),
            cell.Bottom - (cell.Bottom == work.Bottom ? gap : half));
    }

    /// <summary>
    /// How far the window's real frame sits inside the rectangle GetWindowRect reports. Modern
    /// windows carry an invisible resize border (~7px at 100% DPI) on the left, right and bottom;
    /// without compensating, a "flush" snap leaves a visible seam.
    /// </summary>
    private static (int Left, int Top, int Right, int Bottom) FrameOffsets(IntPtr hwnd)
    {
        if (!Native.GetWindowRect(hwnd, out var outer)) return (0, 0, 0, 0);

        int size = Marshal.SizeOf<Native.RECT>();
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, size) != 0)
            return (0, 0, 0, 0);
        if (frame.Width <= 0 || frame.Height <= 0) return (0, 0, 0, 0);

        return (Sane(frame.Left - outer.Left),
                Sane(frame.Top - outer.Top),
                Sane(outer.Right - frame.Right),
                Sane(outer.Bottom - frame.Bottom));

        // Guard against windows that report a frame outside their own bounds.
        static int Sane(int delta) => delta is < 0 or > 48 ? 0 : delta;
    }

    private static bool TryGetWorkArea(IntPtr hwnd, out Native.RECT work)
    {
        work = default;

        IntPtr monitor = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return false;

        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return false;

        work = info.rcWork;
        return work.Width > 0 && work.Height > 0;
    }

    private static bool IsSnappable(IntPtr hwnd)
    {
        if (!Native.IsWindowVisible(hwnd)) return false;
        if (hwnd == Native.GetShellWindow() || hwnd == Native.GetDesktopWindow()) return false;

        if ((Native.GetWindowLongSafe(hwnd, Native.GWL_STYLE) & Native.WS_CHILD) != 0) return false;
        if ((Native.GetWindowLongSafe(hwnd, Native.GWL_EXSTYLE) & Native.WS_EX_TOOLWINDOW) != 0) return false;

        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == (uint)Environment.ProcessId) return false;

        var cls = new StringBuilder(256);
        Native.GetClassNameW(hwnd, cls, cls.Capacity);
        return !ShellClasses.Contains(cls.ToString());
    }

    private static Native.RECT Rect(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };
}
