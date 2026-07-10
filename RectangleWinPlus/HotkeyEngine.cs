using System.Runtime.InteropServices;

namespace RectangleWinPlus;

/// <summary>
/// Watches the keyboard through a WH_KEYBOARD_LL hook and turns key chords into snap actions.
///
/// A low-level hook rather than RegisterHotKey, for two reasons:
///   1. RegisterHotKey binds exactly one key plus modifiers, so it cannot express Ctrl+Win+Left+Up.
///   2. Windows already owns Ctrl+Win+Left/Right (virtual desktops) and Ctrl+Win+Enter (Narrator),
///      and refuses to register them. A hook sees keys before the shell does, so we can claim them.
///
/// The hook callback stays cheap — it only mutates a few sets and posts work elsewhere. If it ever
/// blocked past LowLevelHooksTimeout (300ms by default) Windows would silently evict the hook.
/// </summary>
internal sealed class HotkeyEngine : IDisposable
{
    /// <summary>Fired on the UI thread with the window that was in front when the chord completed.</summary>
    public event Action<SnapAction, IntPtr>? ActionTriggered;

    public event Action<string>? RecordingChanged;
    public event Action<Binding?, string?>? RecordingFinished;

    private readonly Native.LowLevelKeyboardProc _proc;  // kept alive; the hook holds a raw pointer
    private readonly Action<Action> _post;
    private readonly IChordTimer _chordTimer;
    private readonly Func<IntPtr> _foregroundWindow;

    private IntPtr _hook = IntPtr.Zero;

    // Current physical keyboard state.
    private readonly HashSet<int> _heldModVks = new();
    private Mods _heldMods = Mods.None;

    // Keys we have swallowed and must keep swallowing until they come back up, in press order.
    private readonly HashSet<int> _swallowed = new();
    private readonly List<int> _swallowedOrder = new();

    // Bindings, indexed for the hot path.
    private Dictionary<string, SnapAction> _actions = new(StringComparer.Ordinal);
    private HashSet<(Mods, int)> _participating = new();
    private HashSet<string> _extendable = new(StringComparer.Ordinal);
    private int _chordWindowMs = 120;

    // The chord being assembled right now.
    private bool _chordActive;
    private Mods _chordMods;
    private readonly List<int> _chordKeys = new();

    // Shortcut-recording mode (settings dialog).
    private bool _recording;
    private bool _draining;
    private Mods _recMods;
    private readonly List<int> _recKeys = new();
    private readonly HashSet<int> _recHeld = new();

    public bool IsRecording => _recording;
    public bool HookInstalled => _hook != IntPtr.Zero;

    /// <param name="post">Queues work onto the UI thread, to run after the hook callback returns.</param>
    /// <param name="chordTimer">Defaults to a WinForms timer on the calling thread.</param>
    /// <param name="foregroundWindow">Seam for tests; defaults to GetForegroundWindow.</param>
    public HotkeyEngine(Action<Action> post, IChordTimer? chordTimer = null, Func<IntPtr>? foregroundWindow = null)
    {
        _proc = HookProc;
        _post = post;
        _chordTimer = chordTimer ?? new FormsChordTimer();
        _foregroundWindow = foregroundWindow ?? Native.GetForegroundWindow;
        _chordTimer.Elapsed += FireChord;
    }

    public bool Install()
    {
        if (_hook != IntPtr.Zero) return true;

        _hook = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandleW(null), 0);
        if (_hook == IntPtr.Zero)
            Log.Error($"SetWindowsHookEx failed: win32 error {Marshal.GetLastWin32Error()}");

        return _hook != IntPtr.Zero;
    }

    public void SetBindings(IReadOnlyDictionary<SnapAction, Binding> bindings, int chordWindowMs)
    {
        _chordWindowMs = Math.Clamp(chordWindowMs, 0, 1000);

        var actions = new Dictionary<string, SnapAction>(StringComparer.Ordinal);
        var participating = new HashSet<(Mods, int)>();
        var extendable = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (action, binding) in bindings)
        {
            if (!actions.TryAdd(binding.Signature, action))
            {
                Log.Warn($"Shortcut {binding} is bound twice; ignoring it for {action}.");
                continue;
            }

            foreach (int vk in binding.Keys)
            {
                participating.Add((binding.Mods, vk));

                // Every proper subset of a multi-key binding is a prefix we must wait on rather
                // than act upon: Ctrl+Win+Left might still become Ctrl+Win+Left+Up.
                if (binding.Keys.Count > 1)
                    extendable.Add(Binding.MakeSignature(binding.Mods, new[] { vk }));
            }
        }

        _actions = actions;
        _participating = participating;
        _extendable = extendable;
        ResetChord();
    }

    public void BeginRecording()
    {
        ResetChord();
        _recording = true;
        _draining = false;
        _recMods = Mods.None;
        _recKeys.Clear();
        _recHeld.Clear();
        RecordingChanged?.Invoke("Press the shortcut…");
    }

    public void CancelRecording()
    {
        if (_recording) EndRecording(null, null);
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode != Native.HC_ACTION) return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        try
        {
            int msg = (int)wParam;
            bool isDown = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            bool isUp = msg is Native.WM_KEYUP or Native.WM_SYSKEYUP;

            if (isDown || isUp)
            {
                int vk = (int)Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam).vkCode;
                if (ProcessKey(isDown, vk)) return 1;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Keyboard hook threw", ex);
        }

        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>
    /// The whole decision procedure, independent of Win32 plumbing. Returns true when the key
    /// should be swallowed rather than passed on to the focused application and the shell.
    /// </summary>
    internal bool ProcessKey(bool isDown, int vk)
    {
        bool isMod = TryModifier(vk, out _);

        if (isMod)
        {
            if (isDown) _heldModVks.Add(vk); else _heldModVks.Remove(vk);
            _heldMods = RecomputeMods();
        }

        // After a cancelled recording, keep eating keys until the user lets go, so a suppressed
        // Win keydown never pairs with a live Win keyup (which the shell reads as "open Start").
        if (_draining)
        {
            if (!isMod && !isDown) _recHeld.Remove(vk);
            if (_heldMods == Mods.None && _recHeld.Count == 0) _draining = false;
            return true;
        }

        if (_recording) return HandleRecording(isDown, vk, isMod);
        if (isMod) return false;

        if (isDown)
        {
            if (_swallowed.Contains(vk)) return true;  // auto-repeat of a key we already took

            if (_participating.Contains((_heldMods, vk)))
            {
                _swallowed.Add(vk);
                _swallowedOrder.Add(vk);
                StartOrExtendChord(_heldMods, vk);
                return true;
            }

            return false;
        }

        if (_swallowed.Remove(vk))
        {
            _swallowedOrder.Remove(vk);
            return true;
        }

        return false;
    }

    private void StartOrExtendChord(Mods mods, int vk)
    {
        if (!_chordActive || _chordMods != mods)
        {
            _chordActive = true;
            _chordMods = mods;
            _chordKeys.Clear();

            // Seed with keys still physically held. This is what lets a slow Ctrl+Win+Left … Up
            // (past the chord window, so the left half already fired) still resolve to the
            // top-left quadrant rather than to the top half.
            foreach (int held in _swallowedOrder)
            {
                if (held == vk || _chordKeys.Count >= Binding.MaxKeys) continue;
                if (_participating.Contains((mods, held))) _chordKeys.Add(held);
            }
        }

        if (!_chordKeys.Contains(vk) && _chordKeys.Count < Binding.MaxKeys) _chordKeys.Add(vk);

        // Wait only if some longer binding could still absorb what we have so far. A completed
        // quadrant, or a key like Enter that no binding extends, fires with zero latency.
        if (_chordWindowMs > 0 && _extendable.Contains(Binding.MakeSignature(_chordMods, _chordKeys)))
            _chordTimer.Restart(_chordWindowMs);
        else
            FireChord();
    }

    private void FireChord()
    {
        _chordTimer.Stop();
        if (!_chordActive) return;

        _chordActive = false;

        if (_actions.TryGetValue(Binding.MakeSignature(_chordMods, _chordKeys), out var action))
        {
            Dispatch(action);
        }
        else if (_chordKeys.Count > 1
                 && _actions.TryGetValue(Binding.MakeSignature(_chordMods, _chordKeys.Take(1)), out action))
        {
            // A nonsense pair such as Left+Right: honour whichever arrow was pressed first.
            Dispatch(action);
        }

        _chordKeys.Clear();
    }

    private void Dispatch(SnapAction action)
    {
        IntPtr hwnd = _foregroundWindow();
        // Posted, not invoked: SetWindowPos on a foreign window can block, and this is a hook
        // callback. Blocking past LowLevelHooksTimeout would make Windows evict the hook silently.
        _post(() => ActionTriggered?.Invoke(action, hwnd));
    }

    private bool HandleRecording(bool isDown, int vk, bool isMod)
    {
        if (isDown)
        {
            if (vk == VK.Escape) { EndRecording(null, null); return true; }

            if (!isMod)
            {
                _recHeld.Add(vk);
                if (!_recKeys.Contains(vk) && _recKeys.Count < Binding.MaxKeys) _recKeys.Add(vk);
                _recMods = _heldMods;
            }

            RecordingChanged?.Invoke(_recKeys.Count == 0
                ? Binding.Describe(_heldMods, Array.Empty<int>())
                : Binding.Describe(_recMods, _recKeys));

            return true;
        }

        if (!isMod) _recHeld.Remove(vk);

        // Commit once the user has let go of everything.
        if (_heldMods == Mods.None && _recHeld.Count == 0)
        {
            if (_recKeys.Count == 0)
                EndRecording(null, null);
            else if (_recMods == Mods.None)
                EndRecording(null, "A shortcut needs at least one of Ctrl, Alt, Shift or Win.");
            else
                EndRecording(new Binding(_recMods, _recKeys), null);
        }

        return true;
    }

    private void EndRecording(Binding? binding, string? error)
    {
        _recording = false;
        _draining = _heldMods != Mods.None || _recHeld.Count > 0;
        _recKeys.Clear();
        _recMods = Mods.None;
        RecordingFinished?.Invoke(binding, error);
    }

    private void ResetChord()
    {
        _chordTimer.Stop();
        _chordActive = false;
        _chordKeys.Clear();
        _swallowed.Clear();
        _swallowedOrder.Clear();
    }

    private Mods RecomputeMods()
    {
        var mods = Mods.None;
        foreach (int vk in _heldModVks)
            if (TryModifier(vk, out var m)) mods |= m;
        return mods;
    }

    private static bool TryModifier(int vk, out Mods mod)
    {
        switch (vk)
        {
            case VK.LShift or VK.RShift or VK.Shift: mod = Mods.Shift; return true;
            case VK.LControl or VK.RControl or VK.Control: mod = Mods.Ctrl; return true;
            case VK.LMenu or VK.RMenu or VK.Menu: mod = Mods.Alt; return true;
            case VK.LWin or VK.RWin: mod = Mods.Win; return true;
            default: mod = Mods.None; return false;
        }
    }

    public void Dispose()
    {
        _chordTimer.Elapsed -= FireChord;
        (_chordTimer as IDisposable)?.Dispose();

        if (_hook == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }
}
