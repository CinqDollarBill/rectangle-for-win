using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RectangleWinPlus.SelfTest;

/// <summary>
/// Drives the real, running app with synthetic keystrokes: hook installed, tray icon up, config on
/// disk. Everything else in this harness tests the pieces; this tests that they add up.
/// </summary>
internal static class EndToEnd
{
    private const uint KeyUp = 0x0002;
    private const uint Extended = 0x0001;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public static int Run(string appExe, string selfExe)
    {
        int passed = 0;
        var failures = new List<string>();

        void Check(bool ok, string what)
        {
            if (ok) { passed++; Console.WriteLine($"  ok    {what}"); }
            else { failures.Add(what); Console.WriteLine($"  FAIL  {what}"); }
        }

        if (!File.Exists(appExe)) { Console.WriteLine($"  FAIL  app not found at {appExe}"); return 1; }

        Console.WriteLine("End-to-end: real app, real keystrokes\n");

        using var app = Process.Start(new ProcessStartInfo(appExe) { UseShellExecute = false })!;
        using var target = Process.Start(new ProcessStartInfo(selfExe, "--window") { UseShellExecute = false })!;

        try
        {
            try { app.WaitForInputIdle(8000); } catch { /* no main window; that is the point */ }
            Thread.Sleep(1200);  // let the hook install and the tray icon appear
            Check(!app.HasExited, "the app is still running after startup");

            IntPtr hwnd = WaitForWindow(target, TimeSpan.FromSeconds(15));
            if (hwnd == IntPtr.Zero) { Console.WriteLine("  FAIL  target window never appeared"); return 1; }

            if (!Focus(hwnd)) { Console.WriteLine("  FAIL  could not bring the target window to the foreground"); return 1; }
            if (!TryGetWorkArea(hwnd, out var work)) { Console.WriteLine("  FAIL  no work area"); return 1; }
            Console.WriteLine($"  target {hwnd:X}, work area {work}\n");

            // Probe with a combo Windows does not reserve. If the hook is dead this is a no-op
            // rather than something destructive, and we stop before touching Ctrl+Win+Left.
            bool hookLive = Chord(hwnd, work, SnapAction.TopHalf, Check, "Ctrl+Win+Up", VK.Up);
            if (!hookLive)
            {
                Console.WriteLine("\n  the keyboard hook is not intercepting keys; skipping the reserved combos");
                return 1;
            }

            // Only now is it safe to send combos Windows would otherwise act on.
            Chord(hwnd, work, SnapAction.LeftHalf, Check, "Ctrl+Win+Left (taken from virtual desktops)", VK.Left);
            Chord(hwnd, work, SnapAction.TopLeft, Check, "Ctrl+Win+Left+Up", VK.Left, VK.Up);
            Chord(hwnd, work, SnapAction.BottomRight, Check, "Ctrl+Win+Right+Down", VK.Right, VK.Down);
            Chord(hwnd, work, SnapAction.RightHalf, Check, "Ctrl+Win+Right", VK.Right);
            Chord(hwnd, work, SnapAction.Maximize, Check, "Ctrl+Win+Enter (taken from Narrator)", VK.Return);
        }
        finally
        {
            Kill(target);
            Kill(app);
        }

        Console.WriteLine($"\n{passed} passed, {failures.Count} failed");
        foreach (string f in failures) Console.WriteLine($"  FAIL  {f}");
        return failures.Count == 0 ? 0 : 1;
    }

    /// <summary>Presses Ctrl+Win plus the given keys, then checks where the window landed.</summary>
    private static bool Chord(IntPtr hwnd, Native.RECT work, SnapAction expected,
                              Action<bool, string> check, string label, params int[] keys)
    {
        // Park the window somewhere neutral so a stale position cannot look like a pass.
        Native.SetWindowPos(hwnd, IntPtr.Zero, work.Left + 300, work.Top + 220, 700, 480,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        Thread.Sleep(120);

        Press(VK.LControl);
        Press(VK.LWin, extended: true);
        foreach (int vk in keys) { Press(vk, extended: IsExtended(vk)); Thread.Sleep(25); }
        foreach (int vk in keys.Reverse()) Release(vk, extended: IsExtended(vk));
        Release(VK.LWin, extended: true);
        Release(VK.LControl);

        var want = Snapper.ComputeTarget(work, expected, 0);
        var got = Settle(hwnd, want, TimeSpan.FromSeconds(3));

        bool ok = Near(got.Left, want.Left) && Near(got.Top, want.Top)
               && Near(got.Right, want.Right) && Near(got.Bottom, want.Bottom);

        check(ok, $"{label} → {expected}" + (ok ? "" : $" (got {got}, want {want})"));
        return ok;

        static bool Near(int a, int b) => Math.Abs(a - b) <= 2;
    }

    private static bool IsExtended(int vk) => vk is VK.Left or VK.Right or VK.Up or VK.Down;

    private static void Press(int vk, bool extended = false) =>
        keybd_event((byte)vk, 0, extended ? Extended : 0, UIntPtr.Zero);

    private static void Release(int vk, bool extended = false) =>
        keybd_event((byte)vk, 0, (extended ? Extended : 0) | KeyUp, UIntPtr.Zero);

    private static bool Focus(IntPtr hwnd)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
        while (DateTime.UtcNow < deadline)
        {
            SetForegroundWindow(hwnd);
            if (Native.GetForegroundWindow() == hwnd) return true;
            Thread.Sleep(120);
        }
        return false;
    }

    private static IntPtr WaitForWindow(Process child, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            child.Refresh();
            IntPtr h = child.MainWindowHandle;
            if (h != IntPtr.Zero && Native.IsWindowVisible(h)) { Thread.Sleep(250); return h; }
            Thread.Sleep(50);
        }
        return IntPtr.Zero;
    }

    private static Native.RECT Settle(IntPtr hwnd, Native.RECT want, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Native.RECT got = default;

        while (DateTime.UtcNow < deadline)
        {
            got = Frame(hwnd);
            if (Math.Abs(got.Left - want.Left) <= 2 && Math.Abs(got.Top - want.Top) <= 2
                && Math.Abs(got.Right - want.Right) <= 2 && Math.Abs(got.Bottom - want.Bottom) <= 2)
                return got;
            Thread.Sleep(40);
        }
        return got;
    }

    private static Native.RECT Frame(IntPtr hwnd)
    {
        int size = Marshal.SizeOf<Native.RECT>();
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, size) == 0)
            return frame;
        Native.GetWindowRect(hwnd, out var outer);
        return outer;
    }

    private static bool TryGetWorkArea(IntPtr hwnd, out Native.RECT work)
    {
        work = default;
        IntPtr monitor = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return false;

        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return false;

        work = info.rcWork;
        return true;
    }

    private static void Kill(Process p)
    {
        try { if (!p.HasExited) { p.Kill(); p.WaitForExit(3000); } } catch { /* best effort */ }
    }
}
