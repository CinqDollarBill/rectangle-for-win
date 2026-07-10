using System.Diagnostics;

namespace RectangleWinPlus;

internal sealed class TrayApp : ApplicationContext
{
    private readonly Control _marshaller = new();
    private readonly HotkeyEngine _engine;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly FileSystemWatcher _watcher;
    private readonly System.Windows.Forms.Timer _reloadDebounce = new() { Interval = 300 };

    private Icon? _icon;
    private AppConfig _config;
    private SettingsForm? _settings;
    private long _ignoreWatcherUntil;
    private bool _warnedAboutElevation;

    public TrayApp()
    {
        // Forces a handle on the UI thread; everything below marshals through it.
        _ = _marshaller.Handle;

        Directory.CreateDirectory(AppConfig.Directory);
        _config = AppConfig.Load(out var problems);

        _engine = new HotkeyEngine(action => _marshaller.BeginInvoke(action));
        _engine.ActionTriggered += OnActionTriggered;

        _icon = TrayIcons.Create();
        _autoStartItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleAutoStart())
        {
            CheckOnClick = true,
            Checked = AutoStart.IsEnabled(),
        };

        _tray = new NotifyIcon
        {
            Icon = _icon,
            Text = "RectangleWinPlus",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => ShowSettings();

        _reloadDebounce.Tick += (_, _) => { _reloadDebounce.Stop(); ReloadFromDisk(); };

        _watcher = new FileSystemWatcher(AppConfig.Directory, "config.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            SynchronizingObject = _marshaller,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnConfigFileTouched;
        _watcher.Created += OnConfigFileTouched;
        _watcher.Renamed += OnConfigFileTouched;

        if (!_engine.Install())
        {
            Notify("RectangleWinPlus could not install its keyboard hook. Shortcuts will not work.",
                ToolTipIcon.Error);
        }

        ApplyConfig(problems);
        Log.Info("Started.");
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("RectangleWinPlus") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Edit config file", null, (_, _) => OpenPath(AppConfig.FilePath));
        menu.Items.Add("Reload config", null, (_, _) => ReloadFromDisk());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add("Open log", null, (_, _) => OpenPath(Log.FilePath));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        return menu;
    }

    private void OnActionTriggered(SnapAction action, IntPtr hwnd)
    {
        var result = Snapper.Apply(hwnd, action, _config.Gap);

        if (result == SnapResult.AccessDenied && !_warnedAboutElevation)
        {
            _warnedAboutElevation = true;
            Notify("That window belongs to an app running as administrator. Run RectangleWinPlus as " +
                   "administrator too if you need to snap it.", ToolTipIcon.Warning);
        }
        else if (result is SnapResult.Failed)
        {
            Log.Warn($"{action} failed on window {hwnd:X}.");
        }
    }

    private void OnConfigFileTouched(object sender, FileSystemEventArgs e)
    {
        if (Environment.TickCount64 < _ignoreWatcherUntil) return;
        _reloadDebounce.Stop();
        _reloadDebounce.Start();  // editors touch the file several times per save
    }

    private void ReloadFromDisk()
    {
        _config = AppConfig.Load(out var problems);
        _autoStartItem.Checked = AutoStart.IsEnabled();
        ApplyConfig(problems);
    }

    private void ApplyConfig(List<string> problems)
    {
        var bindings = _config.ResolveBindings(problems);
        _engine.SetBindings(bindings, _config.ChordWindowMs);

        foreach (string problem in problems) Log.Warn(problem);

        if (problems.Count > 0)
            Notify(string.Join(Environment.NewLine, problems.Take(3)), ToolTipIcon.Warning);
        else if (bindings.Count == 0)
            Notify("No shortcuts are bound. Open Settings to add some.", ToolTipIcon.Warning);
    }

    private void ShowSettings()
    {
        if (_settings is { IsDisposed: false })
        {
            _settings.Activate();
            return;
        }

        // Modal: ShowDialog runs its own message pump, so the keyboard hook and the chord timer
        // keep working while the dialog is up, which is what makes live shortcut recording possible.
        using var form = new SettingsForm(_engine, _config);
        _settings = form;
        try
        {
            if (form.ShowDialog() == DialogResult.OK && form.Result is { } updated) SaveAndApply(updated);
        }
        finally
        {
            _settings = null;
        }
    }

    private void SaveAndApply(AppConfig updated)
    {
        _ignoreWatcherUntil = Environment.TickCount64 + 1500;
        _config = updated;

        try
        {
            _config.Save();
        }
        catch (Exception ex)
        {
            Log.Error("Saving config failed", ex);
            Notify($"Could not save config.json: {ex.Message}", ToolTipIcon.Error);
        }

        AutoStart.Set(_config.StartWithWindows);
        _autoStartItem.Checked = _config.StartWithWindows;
        ApplyConfig(new List<string>());
    }

    private void ToggleAutoStart()
    {
        _config.StartWithWindows = _autoStartItem.Checked;
        AutoStart.Set(_config.StartWithWindows);
        _ignoreWatcherUntil = Environment.TickCount64 + 1500;
        try { _config.Save(); } catch (Exception ex) { Log.Error("Saving config failed", ex); }
    }

    private void Notify(string message, ToolTipIcon icon) =>
        _tray.ShowBalloonTip(4000, "RectangleWinPlus", message, icon);

    private static void OpenPath(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, string.Empty);
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open {path}", ex);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watcher.Dispose();
            _reloadDebounce.Dispose();
            _engine.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            TrayIcons.Destroy(_icon);
            _icon = null;
            _marshaller.Dispose();
            Log.Info("Exited.");
        }

        base.Dispose(disposing);
    }
}
