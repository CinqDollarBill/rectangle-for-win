using System.ComponentModel;

namespace RectangleWinPlus;

internal sealed class SettingsForm : Form
{
    private static readonly (SnapAction Action, string Label)[] Rows =
    {
        (SnapAction.LeftHalf, "Left half"),
        (SnapAction.RightHalf, "Right half"),
        (SnapAction.TopHalf, "Top half"),
        (SnapAction.BottomHalf, "Bottom half"),
        (SnapAction.TopLeft, "Top-left quarter"),
        (SnapAction.TopRight, "Top-right quarter"),
        (SnapAction.BottomLeft, "Bottom-left quarter"),
        (SnapAction.BottomRight, "Bottom-right quarter"),
        (SnapAction.Maximize, "Maximize"),
    };

    private readonly HotkeyEngine _engine;
    private readonly Dictionary<SnapAction, RecorderButton> _recorders = new();
    private readonly NumericUpDown _gap = NumericField(0, 200, 1);
    private readonly NumericUpDown _chordWindow = NumericField(0, 1000, 10);
    private readonly CheckBox _autoStart = new();
    private readonly Label _status = new();

    private RecorderButton? _recording;

    public AppConfig? Result { get; private set; }

    public SettingsForm(HotkeyEngine engine, AppConfig config)
    {
        _engine = engine;

        Text = "RectangleWinPlus";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;  // scales the fixed size below on high-DPI displays
        ClientSize = new Size(512, 792);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Padding = new Padding(20, 16, 20, 16);

        Controls.Add(BuildLayout());
        LoadFrom(config);

        _engine.RecordingChanged += OnRecordingChanged;
        _engine.RecordingFinished += OnRecordingFinished;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindowChrome(Handle);
    }

    // ---------------------------------------------------------------- layout

    private Control BuildLayout()
    {
        // Explicit rows rather than an AutoSize chain: a container that auto-sizes around docked
        // children collapses to a sliver, and the failure is silent.
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, BackColor = Theme.Background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 6; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // spacer pushes the buttons down
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _status.AutoSize = true;
        _status.MaximumSize = new Size(460, 0);
        _status.ForeColor = Theme.Subtle;
        _status.Margin = new Padding(2, 2, 2, 0);
        _status.BackColor = Color.Transparent;

        root.Controls.Add(SectionHeader("Shortcuts", top: 0), 0, 0);
        root.Controls.Add(BuildShortcutsCard(), 0, 1);
        root.Controls.Add(SectionHeader("Behaviour", top: 18), 0, 2);
        root.Controls.Add(BuildBehaviourCard(), 0, 3);
        root.Controls.Add(BuildNote(), 0, 4);
        root.Controls.Add(_status, 0, 5);
        root.Controls.Add(new Panel { Height = 1, Margin = Padding.Empty, BackColor = Color.Transparent }, 0, 6);
        root.Controls.Add(BuildButtons(), 0, 7);

        return root;
    }

    private static Label SectionHeader(string text, int top) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.Header,
        ForeColor = Theme.Text,
        BackColor = Color.Transparent,
        Margin = new Padding(2, top, 2, 7),
    };

    private Control BuildShortcutsCard()
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = Rows.Length,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 208));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        foreach (var (action, label) in Rows)
        {
            var caption = new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = Theme.Text,
                BackColor = Color.Transparent,
                Margin = new Padding(2, 9, 12, 3),
            };

            var recorder = new RecorderButton(action) { Width = 202, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 6, 3) };
            recorder.Click += (_, _) => StartRecording(recorder);
            _recorders[action] = recorder;

            var clear = new SoftButton(ButtonKind.Ghost) { Text = "Clear", Width = 58, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 0, 3) };
            clear.Click += (_, _) =>
            {
                recorder.Value = null;
                recorder.UpdateText();
                _status.Text = $"{label} is now unbound.";
            };

            grid.Controls.Add(caption);
            grid.Controls.Add(recorder);
            grid.Controls.Add(clear);
        }

        var card = new Card { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = Padding.Empty };
        card.Controls.Add(grid);
        return card;
    }

    private Control BuildBehaviourCard()
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        grid.Controls.Add(FieldLabel("Gap between windows"), 0, 0);
        grid.Controls.Add(_gap, 1, 0);
        grid.Controls.Add(FieldLabel("Chord window (ms)"), 0, 1);
        grid.Controls.Add(_chordWindow, 1, 1);

        var hint = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            ForeColor = Theme.Subtle,
            BackColor = Color.Transparent,
            Margin = new Padding(2, 4, 2, 10),
            Text = "How long a single arrow waits for a second arrow before snapping to a half. "
                 + "At 0 halves snap instantly and quadrants still work, but the window visibly "
                 + "lands on the half first.",
        };
        grid.Controls.Add(hint, 0, 2);
        grid.SetColumnSpan(hint, 2);

        _autoStart.Text = "Start with Windows";
        _autoStart.AutoSize = true;
        _autoStart.ForeColor = Theme.Text;
        _autoStart.BackColor = Color.Transparent;
        _autoStart.FlatStyle = FlatStyle.System;
        _autoStart.Margin = new Padding(2, 0, 2, 2);
        grid.Controls.Add(_autoStart, 0, 3);
        grid.SetColumnSpan(_autoStart, 2);

        var card = new Card { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = Padding.Empty };
        card.Controls.Add(grid);
        return card;
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        ForeColor = Theme.Text,
        BackColor = Color.Transparent,
        Margin = new Padding(2, 8, 12, 6),
    };

    private static NumericUpDown NumericField(int min, int max, int step) => new()
    {
        Minimum = min,
        Maximum = max,
        Increment = step,
        Width = 78,
        Font = Theme.Body,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.Control,
        ForeColor = Theme.Text,
        TextAlign = HorizontalAlignment.Center,
        Margin = new Padding(0, 4, 2, 6),
    };

    private Control BuildNote() => new Label
    {
        AutoSize = true,
        MaximumSize = new Size(460, 0),
        ForeColor = Theme.Subtle,
        BackColor = Color.Transparent,
        Margin = new Padding(2, 16, 2, 2),
        Text = "While RectangleWinPlus runs it claims Ctrl+Win+Left/Right, so those no longer switch "
             + "virtual desktops. Ctrl+Win+D and Ctrl+Win+F4 still work.",
    };

    private Control BuildButtons()
    {
        var save = new SoftButton(ButtonKind.Primary) { Text = "Save", Width = 96, DialogResult = DialogResult.None, Margin = new Padding(8, 6, 0, 0) };
        save.Click += (_, _) => TrySave();

        var cancel = new SoftButton(ButtonKind.Secondary) { Text = "Cancel", Width = 96, DialogResult = DialogResult.Cancel, Margin = new Padding(8, 6, 0, 0) };

        var defaults = new SoftButton(ButtonKind.Ghost) { Text = "Restore defaults", Width = 128, DialogResult = DialogResult.None, Margin = new Padding(8, 6, 0, 0) };
        defaults.Click += (_, _) => LoadFrom(AppConfig.CreateDefault());

        AcceptButton = save;
        CancelButton = cancel;

        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        panel.Controls.Add(save);
        panel.Controls.Add(cancel);
        panel.Controls.Add(defaults);
        return panel;
    }

    // ---------------------------------------------------------------- behaviour

    private void LoadFrom(AppConfig config)
    {
        _gap.Value = Math.Clamp(config.Gap, 0, 200);
        _chordWindow.Value = Math.Clamp(config.ChordWindowMs, 0, 1000);
        _autoStart.Checked = config.StartWithWindows || AutoStart.IsEnabled();

        var bindings = config.ResolveBindings(new List<string>());
        foreach (var (action, recorder) in _recorders)
        {
            recorder.Value = bindings.GetValueOrDefault(action);
            recorder.UpdateText();
        }

        _status.Text = string.Empty;
    }

    private void StartRecording(RecorderButton recorder)
    {
        if (_recording is not null) _engine.CancelRecording();

        _recording = recorder;
        recorder.Highlighted = true;
        recorder.Text = "Press the shortcut…";
        recorder.Invalidate();
        _status.Text = "Hold the keys, then let go. Esc cancels.";
        _engine.BeginRecording();
    }

    private void OnRecordingChanged(string preview)
    {
        if (_recording is null) return;
        _recording.Text = preview;
        _recording.Invalidate();
    }

    private void OnRecordingFinished(Binding? binding, string? error)
    {
        var recorder = _recording;
        _recording = null;
        if (recorder is null) return;

        if (binding is not null) recorder.Value = binding;
        recorder.Highlighted = false;
        recorder.UpdateText();
        _status.Text = error ?? string.Empty;
    }

    private void TrySave()
    {
        var owners = new Dictionary<string, SnapAction>(StringComparer.Ordinal);

        foreach (var (action, recorder) in _recorders)
        {
            if (recorder.Value is not { } binding) continue;

            if (owners.TryGetValue(binding.Signature, out var other))
            {
                MessageBox.Show(this,
                    $"{binding} is assigned to both “{Snapper.FriendlyName(other)}” and “{Snapper.FriendlyName(action)}”.\n\n"
                    + "Give one of them a different shortcut.",
                    "Duplicate shortcut", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            owners[binding.Signature] = action;
        }

        var config = new AppConfig
        {
            Gap = (int)_gap.Value,
            ChordWindowMs = (int)_chordWindow.Value,
            StartWithWindows = _autoStart.Checked,
            Shortcuts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

        foreach (var (action, _) in Rows)
            config.Shortcuts[action.ToString()] = _recorders[action].Value?.ToString() ?? string.Empty;

        Result = config;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// Recording swallows every keystroke system-wide. If the user clicks away mid-recording the
    /// keyboard would look dead, so losing focus always ends it.
    /// </summary>
    protected override void OnDeactivate(EventArgs e)
    {
        _engine.CancelRecording();
        base.OnDeactivate(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _engine.CancelRecording();
        _engine.RecordingChanged -= OnRecordingChanged;
        _engine.RecordingFinished -= OnRecordingFinished;
        base.OnFormClosing(e);
    }

    /// <summary>A chip whose face is the shortcut it holds; clicking it records a new one.</summary>
    private sealed class RecorderButton : SoftButton
    {
        public RecorderButton(SnapAction action) : base(ButtonKind.Chip) => Action = action;

        public SnapAction Action { get; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Binding? Value { get; set; }

        public void UpdateText()
        {
            Text = Value?.ToString() ?? "Not set";
            Muted = Value is null;
            Invalidate();
        }
    }
}
