using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RectangleWinPlus.SelfTest;

internal static class Program
{
    private static int _passed;
    private static readonly List<string> Failures = new();

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // Child mode: just be a window for the parent to shove around.
        if (args.Contains("--window")) return RunTargetWindow();

        // Layout check: show the real settings dialog and screenshot it.
        int shot = Array.IndexOf(args, "--settings-shot");
        if (shot >= 0)
        {
            if (shot + 1 >= args.Length) { Console.WriteLine("usage: --settings-shot <out.png>"); return 2; }
            return CaptureSettings(args[shot + 1]);
        }

        // End-to-end mode: launch the real app and press real keys at it.
        int e2e = Array.IndexOf(args, "--e2e");
        if (e2e >= 0)
        {
            if (e2e + 1 >= args.Length) { Console.WriteLine("usage: --e2e <path-to-RectangleWinPlus.exe>"); return 2; }
            return EndToEnd.Run(args[e2e + 1], Environment.ProcessPath!);
        }

        Console.WriteLine("RectangleWinPlus self-test\n");

        BindingTests();
        GeometryTests();
        ChordTests();
        RecordingTests();

        if (!args.Contains("--no-live")) LiveWindowTests();

        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {Failures.Count} failed");
        foreach (string failure in Failures) Console.WriteLine($"  FAIL  {failure}");

        return Failures.Count == 0 ? 0 : 1;
    }

    private static int RunTargetWindow()
    {
        Application.EnableVisualStyles();
        Application.Run(new Form
        {
            Text = "RectangleWinPlus self-test target",
            StartPosition = FormStartPosition.Manual,
            Location = new Point(180, 160),
            Size = new Size(720, 520),
            MinimumSize = new Size(200, 150),
            FormBorderStyle = FormBorderStyle.Sizable,
        });
        return 0;
    }

    /// <summary>Shows the real SettingsForm, screenshots it, closes it. Verifies layout, not logic.</summary>
    private static int CaptureSettings(string outPath)
    {
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);

        using var engine = new HotkeyEngine(run => run());  // no hook: we only want the pixels
        using var form = new SettingsForm(engine, AppConfig.CreateDefault());

        var shutter = new System.Windows.Forms.Timer { Interval = 900 };
        shutter.Tick += (_, _) =>
        {
            shutter.Stop();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
                var bounds = form.Bounds;
                Console.WriteLine($"form bounds: {bounds}");

                if (bounds.Width <= 0 || bounds.Height <= 0)
                    Console.WriteLine("form has no size; nothing to capture");
                else
                {
                    using var bitmap = new Bitmap(bounds.Width, bounds.Height);
                    using (var g = Graphics.FromImage(bitmap))
                        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                    bitmap.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
                    Console.WriteLine($"wrote {outPath} ({bounds.Width}x{bounds.Height})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"capture failed: {ex}");
            }
            finally
            {
                form.Close();
            }
        };

        form.Shown += (_, _) => { form.TopMost = true; form.Activate(); shutter.Start(); };
        Application.Run(form);
        shutter.Dispose();
        return 0;
    }

    // ---------------------------------------------------------------- assertions

    private static void Check(bool condition, string what)
    {
        if (condition) { _passed++; Console.WriteLine($"  ok    {what}"); }
        else { Failures.Add(what); Console.WriteLine($"  FAIL  {what}"); }
    }

    private static void CheckEqual<T>(T actual, T expected, string what) =>
        Check(EqualityComparer<T>.Default.Equals(actual, expected), $"{what} (got {actual}, want {expected})");

    // ---------------------------------------------------------------- bindings

    private static void BindingTests()
    {
        Console.WriteLine("Binding parsing");

        Check(Binding.TryParse("Ctrl+Win+Left+Up", out var chord, out _), "parses Ctrl+Win+Left+Up");
        CheckEqual(chord!.ToString(), "Ctrl+Win+Left+Up", "round-trips");

        Check(Binding.TryParse("win + ctrl + up + left", out var scrambled, out _), "parses scrambled order");
        CheckEqual(scrambled!.Signature, chord.Signature, "scrambled order is the same binding");
        CheckEqual(scrambled.ToString(), "Ctrl+Win+Left+Up", "canonicalises to reading order");

        Check(Binding.TryParse("Ctrl+Win+Enter", out var enter, out _), "parses Ctrl+Win+Enter");
        CheckEqual(enter!.ToString(), "Ctrl+Win+Enter", "Enter round-trips");

        Check(Binding.TryParse("Ctrl+Win+Num7", out var num, out _), "parses numpad");
        CheckEqual(num!.ToString(), "Ctrl+Win+Num7", "numpad round-trips");

        Check(!Binding.TryParse("Ctrl+Win", out _, out _), "rejects modifiers with no key");
        Check(!Binding.TryParse("Left", out _, out _), "rejects a bare key with no modifier");
        Check(!Binding.TryParse("Ctrl+Win+Left+Up+Down", out _, out _), "rejects three keys");
        Check(!Binding.TryParse("Ctrl+Win+Nonsense", out _, out _), "rejects an unknown key name");

        var defaults = AppConfig.CreateDefault().ResolveBindings(new List<string>());
        CheckEqual(defaults.Count, 9, "all nine defaults resolve");

        var problems = new List<string>();
        var clashing = new AppConfig
        {
            Shortcuts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["LeftHalf"] = "Ctrl+Win+Left",
                ["RightHalf"] = "Ctrl+Win+Left",
            },
        };
        CheckEqual(clashing.ResolveBindings(problems).Count, 1, "a duplicate shortcut is dropped");
        Check(problems.Count == 1, "the duplicate is reported");
    }

    // ---------------------------------------------------------------- geometry

    private static void GeometryTests()
    {
        Console.WriteLine("\nSnap geometry");

        var work = Rect(0, 0, 1920, 1040);  // 1080p minus a taskbar

        var tl = Snapper.ComputeTarget(work, SnapAction.TopLeft, 0);
        var br = Snapper.ComputeTarget(work, SnapAction.BottomRight, 0);
        CheckEqual(tl.ToString(), Rect(0, 0, 960, 520).ToString(), "top-left quarter, no gap");
        CheckEqual(br.ToString(), Rect(960, 520, 1920, 1040).ToString(), "bottom-right quarter, no gap");

        var left = Snapper.ComputeTarget(work, SnapAction.LeftHalf, 0);
        var right = Snapper.ComputeTarget(work, SnapAction.RightHalf, 0);
        CheckEqual(left.Right, right.Left, "halves meet exactly, no gap and no overlap");
        CheckEqual(left.Width + right.Width, work.Width, "halves cover the full width");

        var max = Snapper.ComputeTarget(work, SnapAction.Maximize, 0);
        CheckEqual(max.ToString(), work.ToString(), "maximize fills the work area");

        // Odd width must still tile seamlessly; one side just gets the spare pixel.
        var odd = Rect(0, 0, 1921, 1041);
        var oddLeft = Snapper.ComputeTarget(odd, SnapAction.LeftHalf, 0);
        var oddRight = Snapper.ComputeTarget(odd, SnapAction.RightHalf, 0);
        CheckEqual(oddLeft.Right, oddRight.Left, "odd width still tiles seamlessly");
        CheckEqual(oddLeft.Width + oddRight.Width, odd.Width, "odd width loses no pixels");

        // Negative-origin monitor (a display to the left of the primary).
        var secondary = Rect(-1920, -200, 0, 880);
        var secTopRight = Snapper.ComputeTarget(secondary, SnapAction.TopRight, 0);
        CheckEqual(secTopRight.ToString(), Rect(-960, -200, 0, 340).ToString(), "quadrants work at negative coordinates");

        // Gaps: every visible gutter should measure exactly `gap`, at the edges and between windows.
        const int gap = 12;
        var gLeft = Snapper.ComputeTarget(work, SnapAction.LeftHalf, gap);
        var gRight = Snapper.ComputeTarget(work, SnapAction.RightHalf, gap);
        CheckEqual(gLeft.Left - work.Left, gap, "gap: left edge");
        CheckEqual(work.Right - gRight.Right, gap, "gap: right edge");
        CheckEqual(gRight.Left - gLeft.Right, gap, "gap: gutter between the two halves");

        var gTopLeft = Snapper.ComputeTarget(work, SnapAction.TopLeft, gap);
        var gBottomLeft = Snapper.ComputeTarget(work, SnapAction.BottomLeft, gap);
        var gTopRight = Snapper.ComputeTarget(work, SnapAction.TopRight, gap);
        CheckEqual(gTopLeft.Top - work.Top, gap, "gap: top edge");
        CheckEqual(gBottomLeft.Top - gTopLeft.Bottom, gap, "gap: horizontal gutter between quadrants");
        CheckEqual(gTopRight.Left - gTopLeft.Right, gap, "gap: vertical gutter between quadrants");
        CheckEqual(work.Bottom - gBottomLeft.Bottom, gap, "gap: bottom edge");

        var gMax = Snapper.ComputeTarget(work, SnapAction.Maximize, gap);
        CheckEqual(gMax.ToString(), Rect(gap, gap, 1920 - gap, 1040 - gap).ToString(), "gap: maximize insets on all sides");
    }

    // ---------------------------------------------------------------- chords

    private sealed class Rig
    {
        public HotkeyEngine Engine { get; }
        public FakeChordTimer Timer { get; } = new();
        public List<SnapAction> Fired { get; } = new();

        public Rig(int chordWindowMs = 120)
        {
            Engine = new HotkeyEngine(run => run(), Timer, () => 1);
            Engine.ActionTriggered += (action, _) => Fired.Add(action);
            Engine.SetBindings(AppConfig.CreateDefault().ResolveBindings(new List<string>()), chordWindowMs);
        }

        public bool Down(int vk) => Engine.ProcessKey(true, vk);
        public bool Up(int vk) => Engine.ProcessKey(false, vk);

        public Rig HoldCtrlWin()
        {
            Down(VK.LControl);
            Down(VK.LWin);
            return this;
        }
    }

    private static void ChordTests()
    {
        Console.WriteLine("\nChord engine");

        {
            var r = new Rig().HoldCtrlWin();
            Check(r.Down(VK.Left), "Ctrl+Win+Left is swallowed (Windows never sees it)");
            Check(r.Fired.Count == 0, "a lone arrow does not act until the chord window closes");
            Check(r.Timer.Running, "the chord window is open");
            CheckEqual(r.Timer.Interval, 120, "the chord window honours the configured delay");
            r.Timer.Fire();
            CheckEqual(string.Join(",", r.Fired), "LeftHalf", "the arrow alone snaps to the left half");
        }

        {
            var r = new Rig().HoldCtrlWin();
            r.Down(VK.Left);
            r.Down(VK.Up);
            CheckEqual(string.Join(",", r.Fired), "TopLeft", "Left+Up inside the window snaps to the top-left quadrant");
            Check(!r.Timer.Running, "a complete quadrant fires immediately, without waiting out the window");
        }

        {
            var r = new Rig().HoldCtrlWin();
            r.Down(VK.Right);
            r.Down(VK.Down);
            CheckEqual(string.Join(",", r.Fired), "BottomRight", "Right+Down snaps to the bottom-right quadrant");
        }

        {
            var r = new Rig().HoldCtrlWin();
            Check(r.Down(VK.Return), "Ctrl+Win+Enter is swallowed (Narrator never sees it)");
            CheckEqual(string.Join(",", r.Fired), "Maximize", "Enter maximizes with no delay");
            Check(!r.Timer.Running, "Enter does not open a chord window");
        }

        {
            // The forgiving case: the user is slow, so the half already fired. Pressing the second
            // arrow while the first is still held must still reach the quadrant.
            var r = new Rig().HoldCtrlWin();
            r.Down(VK.Left);
            r.Timer.Fire();
            r.Down(VK.Up);
            CheckEqual(string.Join(",", r.Fired), "LeftHalf,TopLeft", "a late second arrow still upgrades to the quadrant");
        }

        {
            var r = new Rig().HoldCtrlWin();
            r.Down(VK.Left);
            Check(r.Down(VK.Left), "auto-repeat of a held arrow is swallowed");
            r.Timer.Fire();
            CheckEqual(r.Fired.Count, 1, "auto-repeat does not snap twice");
        }

        {
            var r = new Rig().HoldCtrlWin();
            r.Down(VK.Left);
            r.Down(VK.Right);
            CheckEqual(string.Join(",", r.Fired), "LeftHalf", "an impossible pair falls back to the arrow pressed first");
        }

        {
            var r = new Rig(chordWindowMs: 0).HoldCtrlWin();
            r.Down(VK.Left);
            CheckEqual(string.Join(",", r.Fired), "LeftHalf", "a zero chord window snaps halves instantly");
            Check(!r.Timer.Running, "a zero chord window never waits");

            r.Down(VK.Up);
            CheckEqual(string.Join(",", r.Fired), "LeftHalf,TopLeft", "a zero chord window still reaches quadrants, via the half");
        }

        {
            var r = new Rig().HoldCtrlWin();
            Check(!r.Down(0x44), "Ctrl+Win+D passes through to Windows (new virtual desktop still works)");
            Check(r.Fired.Count == 0, "an unbound key snaps nothing");
        }

        {
            var r = new Rig();
            r.Down(VK.LControl);  // Ctrl only, no Win
            Check(!r.Down(VK.Left), "Ctrl+Left passes through (text selection still works)");
            Check(r.Fired.Count == 0, "the wrong modifiers snap nothing");
        }

        {
            var r = new Rig();
            r.Down(VK.RControl);
            r.Down(VK.RWin);
            r.Down(VK.Left);
            r.Timer.Fire();
            CheckEqual(string.Join(",", r.Fired), "LeftHalf", "the right-hand Ctrl and Win keys work too");
        }

        {
            var r = new Rig().HoldCtrlWin();
            r.Down(VK.Left);
            Check(r.Up(VK.Left), "the key-up of a swallowed key is swallowed as well");
            Check(!r.Up(0x44), "the key-up of a key we let through is not swallowed");
        }

        {
            // Releasing one Ctrl while the other is held must not drop the Ctrl modifier.
            var r = new Rig();
            r.Down(VK.LControl);
            r.Down(VK.RControl);
            r.Up(VK.LControl);
            r.Down(VK.LWin);
            r.Down(VK.Left);
            r.Timer.Fire();
            CheckEqual(string.Join(",", r.Fired), "LeftHalf", "holding both Ctrl keys and releasing one keeps Ctrl down");
        }
    }

    // ---------------------------------------------------------------- recording

    private static void RecordingTests()
    {
        Console.WriteLine("\nShortcut recording");

        {
            var r = new Rig();
            Binding? captured = null;
            string? error = null;
            r.Engine.RecordingFinished += (b, e) => { captured = b; error = e; };

            r.Engine.BeginRecording();
            Check(r.Down(VK.LWin), "the Win key is swallowed while recording (Start menu stays shut)");
            r.Down(VK.LControl);
            r.Down(VK.Left);
            r.Down(VK.Up);
            r.Up(VK.Up);
            r.Up(VK.Left);
            Check(captured is null, "nothing is committed until every key is released");
            r.Up(VK.LControl);
            r.Up(VK.LWin);

            Check(captured is not null, "releasing everything commits the shortcut");
            CheckEqual(captured?.ToString(), "Ctrl+Win+Left+Up", "the recorded chord is Ctrl+Win+Left+Up");
            Check(error is null, "a valid chord records without complaint");
            Check(r.Fired.Count == 0, "recording never snaps a window");
        }

        {
            var r = new Rig();
            Binding? captured = null;
            bool finished = false;
            r.Engine.RecordingFinished += (b, _) => { captured = b; finished = true; };

            r.Engine.BeginRecording();
            r.Down(VK.Escape);
            Check(finished && captured is null, "Esc cancels recording");
        }

        {
            var r = new Rig();
            string? error = null;
            r.Engine.RecordingFinished += (_, e) => error = e;

            r.Engine.BeginRecording();
            r.Down(0x41);  // A, no modifier
            r.Up(0x41);
            Check(error is not null, "a shortcut without a modifier is refused");
        }

        {
            // Cancelling with Ctrl still held must keep swallowing until the user lets go,
            // otherwise a suppressed key-down pairs with a live key-up.
            var r = new Rig();
            r.Engine.BeginRecording();
            r.Down(VK.LWin);
            r.Down(VK.Escape);
            Check(r.Up(VK.LWin), "after cancelling, the still-held Win key-up is swallowed");
            Check(!r.Down(0x41), "once every key is released, typing works again");
        }
    }

    // ---------------------------------------------------------------- live window

    private static void LiveWindowTests()
    {
        Console.WriteLine("\nLive window snapping");

        string? exe = Environment.ProcessPath;
        if (exe is null) { Failures.Add("cannot locate own executable for the live test"); return; }

        using var child = Process.Start(new ProcessStartInfo(exe, "--window") { UseShellExecute = false })!;
        try
        {
            IntPtr hwnd = WaitForWindow(child, TimeSpan.FromSeconds(15));
            if (hwnd == IntPtr.Zero) { Failures.Add("the self-test target window never appeared"); return; }

            if (!TryGetWorkArea(hwnd, out var work)) { Failures.Add("could not read the monitor work area"); return; }
            Console.WriteLine($"  target window {hwnd:X}, work area {work}");

            foreach (int gap in new[] { 0, 12 })
            {
                foreach (SnapAction action in Enum.GetValues<SnapAction>())
                {
                    var result = Snapper.Apply(hwnd, action, gap);
                    if (result != SnapResult.Ok)
                    {
                        Failures.Add($"live: {action} (gap {gap}) returned {result}");
                        Console.WriteLine($"  FAIL  live: {action} (gap {gap}) returned {result}");
                        continue;
                    }

                    var want = Snapper.ComputeTarget(work, action, gap);
                    var got = SettleFrame(hwnd, want, tolerance: 2, TimeSpan.FromSeconds(2));

                    bool close = Near(got.Left, want.Left) && Near(got.Top, want.Top)
                              && Near(got.Right, want.Right) && Near(got.Bottom, want.Bottom);

                    Check(close, $"live: {action} (gap {gap}) lands on {want}" + (close ? "" : $", got {got}"));
                }
            }

            // Pressing Maximize twice must not restore-then-remaximize.
            Snapper.Apply(hwnd, SnapAction.Maximize, 0);
            var again = Snapper.Apply(hwnd, SnapAction.Maximize, 0);
            Check(again == SnapResult.Ok, "live: maximizing an already-maximized window succeeds");
            Check(Native.IsZoomed(hwnd), "live: it stays maximized rather than flickering back");

            // Snapping a minimized window must restore it first.
            Native.ShowWindow(hwnd, 6 /* SW_MINIMIZE */);
            Thread.Sleep(150);
            var fromMinimized = Snapper.Apply(hwnd, SnapAction.TopRight, 0);
            Check(fromMinimized == SnapResult.Ok, "live: a minimized window can be snapped");
            var wantTr = Snapper.ComputeTarget(work, SnapAction.TopRight, 0);
            var gotTr = SettleFrame(hwnd, wantTr, 2, TimeSpan.FromSeconds(2));
            Check(!Native.IsIconic(hwnd) && Math.Abs(gotTr.Left - wantTr.Left) <= 2 && Math.Abs(gotTr.Right - wantTr.Right) <= 2,
                $"live: a minimized window restores onto the quadrant (got {gotTr}, want {wantTr})");
        }
        finally
        {
            try { if (!child.HasExited) child.Kill(); } catch { /* best effort */ }
        }

        static bool Near(int a, int b) => Math.Abs(a - b) <= 2;
    }

    private static IntPtr WaitForWindow(Process child, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            child.Refresh();
            IntPtr h = child.MainWindowHandle;
            if (h != IntPtr.Zero && Native.IsWindowVisible(h))
            {
                Thread.Sleep(250);  // let the window finish laying out
                return h;
            }
            Thread.Sleep(50);
        }
        return IntPtr.Zero;
    }

    /// <summary>Polls the window's visible frame until it matches, or we give up and report what it is.</summary>
    private static Native.RECT SettleFrame(IntPtr hwnd, Native.RECT want, int tolerance, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Native.RECT got = default;

        while (DateTime.UtcNow < deadline)
        {
            got = FrameBounds(hwnd);
            if (Math.Abs(got.Left - want.Left) <= tolerance && Math.Abs(got.Top - want.Top) <= tolerance
                && Math.Abs(got.Right - want.Right) <= tolerance && Math.Abs(got.Bottom - want.Bottom) <= tolerance)
                return got;

            Thread.Sleep(30);
        }

        return got;
    }

    /// <summary>The frame the user actually sees, excluding the invisible resize border.</summary>
    private static Native.RECT FrameBounds(IntPtr hwnd)
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

    private static Native.RECT Rect(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };
}
