using Microsoft.Win32;
using ObsTurboMuteAlarm.Icp;
using ObsTurboMuteAlarm.Obs;

namespace ObsTurboMuteAlarm.UI;

/// <summary>The main window: pick the OBS audio inputs to watch and the displays the warning appears on.</summary>
internal sealed class ChooserForm : Form
{
    private sealed record SourceItem(string Name, string Uuid, string Label)
    {
        public override string ToString() => Label;
        public bool Matches(WatchedSource s) =>
            (Uuid.Length > 0 && string.Equals(Uuid, s.Uuid, StringComparison.OrdinalIgnoreCase)) ||
            (s.Uuid.Length == 0 && Name == s.Name);
    }

    private const string AppName = "OBS Turbo Mute Alarm";

    private readonly Func<AppSettings> _current;
    private readonly Action<AppSettings> _apply;
    private readonly Action _test;
    private readonly Action _openSettings;

    private readonly Label _obsStatus = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 12, 3) };
    private readonly Button _refresh = new() { Text = "Refresh", AutoSize = true };
    private readonly LinkLabel _connectionLink = new() { Text = "OBS connection settings…", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };
    private readonly CheckedListBox _sources = new() { CheckOnClick = true, Size = new Size(380, 260), IntegralHeight = false };
    private readonly CheckBox _showAll = new() { Text = "Show all sources with audio (media, browser, game capture…)", AutoSize = true };
    private readonly MonitorPicker _monitors = new() { Size = new Size(440, 220), Margin = new Padding(3, 3, 3, 6) };
    private readonly Button _identify = new() { Text = "Identify monitors", AutoSize = true };
    private readonly LinkLabel _disconnected = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 7, 3, 3), Visible = false };
    private readonly CheckBox _icp = new() { AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold), Margin = new Padding(3, 8, 3, 3) };
    private readonly Label _summary = new() { AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(560, 0), Margin = new Padding(3, 9, 3, 3) };
    private IReadOnlyList<ObsAudioInput> _inputs = Array.Empty<ObsAudioInput>();
    private CancellationTokenSource? _refreshCts;

    public ChooserForm(Func<AppSettings> current, Action<AppSettings> apply, Action test, Action openSettings)
    {
        _current = current;
        _apply = apply;
        _test = test;
        _openSettings = openSettings;

        Text = $"{AppName} – Choose inputs and displays";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(8);

        BuildLayout();
        var s = current();
        foreach (var w in s.Sources)
            _sources.Items.Add(new SourceItem(w.Name, w.Uuid, $"{w}   (connecting…)"), true);
        _monitors.SetSelected(s.Monitors);
        _icp.Checked = s.IcpEnabled;
        UpdateIcpLabel();
        UpdateSummary();

        _refresh.Click += async (_, _) => await RefreshSourcesAsync();
        _connectionLink.LinkClicked += (_, _) => _openSettings();
        _showAll.CheckedChanged += (_, _) => RebuildSourceList();
        _sources.ItemCheck += (_, _) => BeginInvoke(UpdateSummary); // ItemCheck fires before the change
        _monitors.SelectionChanged += (_, _) => UpdateSummary();
        _icp.CheckedChanged += (_, _) => UpdateSummary();
        _identify.Click += (_, _) => IdentifyMonitors();
        _disconnected.LinkClicked += (_, _) => _monitors.ForgetDisconnected();
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        Shown += async (_, _) => await RefreshSourcesAsync();
        Activated += (_, _) => UpdateIcpLabel();
        FormClosed += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
            _refreshCts?.Cancel();
        };
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };

        var top = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
        top.Controls.AddRange(new Control[] { _obsStatus, _refresh, _connectionLink });
        root.Controls.Add(top);

        var columns = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };

        var inputs = Stack();
        inputs.Controls.Add(Hint("Tick the OBS audio inputs to watch. The warning shows when ANY of them is muted."));
        inputs.Controls.Add(_sources);
        inputs.Controls.Add(_showAll);
        columns.Controls.Add(Group("1. Audio inputs", inputs));

        var displays = Stack();
        displays.Controls.Add(Hint("Click the monitors where the warning should pop up (red = selected). " +
                                   "The layout matches Windows display settings."));
        displays.Controls.Add(_monitors);
        var monitorButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        monitorButtons.Controls.Add(_identify);
        monitorButtons.Controls.Add(_disconnected);
        displays.Controls.Add(monitorButtons);
        displays.Controls.Add(_icp);
        displays.Controls.Add(Hint("Big flashing \"MUTE\" on the ICP's DED display at full backlight."));
        columns.Controls.Add(Group("2. Displays", displays));
        root.Controls.Add(columns);

        var bottom = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_summary);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var test = new Button { Text = "Apply && test (5 s)", AutoSize = true };
        var save = new Button { Text = "Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        test.Click += (_, _) => { if (ApplyValues(confirm: false)) _test(); };
        save.Click += (_, _) => { if (ApplyValues(confirm: true)) Close(); };
        buttons.Controls.AddRange(new Control[] { test, save, cancel });
        bottom.Controls.Add(buttons);
        root.Controls.Add(bottom);

        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(root);
    }

    private static FlowLayoutPanel Stack() =>
        new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };

    private static Label Hint(string text) =>
        new() { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(440, 0), Margin = new Padding(3, 0, 3, 6) };

    private static GroupBox Group(string title, Control content)
    {
        var g = new GroupBox { Text = title, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(8) };
        g.Font = new Font(g.Font, FontStyle.Bold);
        content.Font = new Font(g.Font, FontStyle.Regular);
        g.Controls.Add(content);
        return g;
    }

    /// <summary>Microphones, desktop audio and application audio capture (not media/browser sources).</summary>
    internal static bool IsAudioCapture(string kind) =>
        new[] { "wasapi", "audio", "pulse", "alsa", "jack", "coreaudio" }
            .Any(k => kind.Contains(k, StringComparison.OrdinalIgnoreCase));

    private List<WatchedSource> TickedSources() =>
        _sources.CheckedItems.Cast<SourceItem>().Select(i => new WatchedSource { Name = i.Name, Uuid = i.Uuid }).ToList();

    private void RebuildSourceList()
    {
        var ticked = TickedSources();
        _sources.BeginUpdate();
        _sources.Items.Clear();
        foreach (var input in _inputs.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new SourceItem(input.Name, input.Uuid, $"{input.Name}   [{(input.Muted ? "muted" : "live")}]");
            bool isTicked = ticked.Any(item.Matches);
            if (isTicked || _showAll.Checked || IsAudioCapture(input.Kind))
                _sources.Items.Add(item, isTicked);
        }
        foreach (var t in ticked.Where(t => !_inputs.Any(i => new SourceItem(i.Name, i.Uuid, "").Matches(t))))
            _sources.Items.Add(new SourceItem(t.Name, t.Uuid, $"{t}   (not found in OBS)"), true);
        _sources.EndUpdate();
    }

    private async Task RefreshSourcesAsync()
    {
        _refreshCts?.Cancel();
        var cts = _refreshCts = new CancellationTokenSource();
        _refresh.Enabled = false;
        _obsStatus.ForeColor = SystemColors.ControlText;
        _obsStatus.Text = "Connecting to OBS…";
        try
        {
            var inputs = await ObsMuteMonitor.FetchAudioInputsAsync(_current().Obs, TimeSpan.FromSeconds(8), cts.Token);
            if (IsDisposed) return;
            _inputs = inputs;
            RebuildSourceList();
            _obsStatus.ForeColor = Color.DarkGreen;
            _obsStatus.Text = $"Connected to OBS – {inputs.Count(i => IsAudioCapture(i.Kind))} audio inputs.";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _obsStatus.ForeColor = Color.Firebrick;
            _obsStatus.Text = "Can't connect to OBS: " + (ex is TimeoutException ? "timed out." : ex.Message);
        }
        finally
        {
            if (!IsDisposed) _refresh.Enabled = true;
        }
    }

    private void OnDisplaysChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        BeginInvoke(() =>
        {
            _monitors.RefreshScreens();
            UpdateSummary();
        });
    }

    private void UpdateIcpLabel()
    {
        bool present;
        try { present = IcpHidDevice.IsPresent(); }
        catch { present = false; }
        _icp.Text = $"WinWing ViperAce ICP  ({(present ? "connected" : "not found")})";
    }

    private void UpdateSummary()
    {
        int sources = _sources.CheckedItems.Count;
        var outputs = _monitors.SelectedDevices
            .Where(d => !_monitors.DisconnectedSelection.Contains(d, StringComparer.OrdinalIgnoreCase))
            .Select(d => $"Monitor {MonitorPicker.MonitorNumber(d)}")
            .OrderBy(x => x, StringComparer.CurrentCulture).ToList();
        if (_icp.Checked) outputs.Add("ICP");

        _summary.ForeColor = sources == 0 || outputs.Count == 0 ? Color.Firebrick : SystemColors.ControlText;
        _summary.Text = sources == 0 ? "Tick at least one audio input."
            : outputs.Count == 0 ? "Select at least one monitor or the ICP."
            : $"Watching {sources} input{(sources == 1 ? "" : "s")}. Warning on: {string.Join(", ", outputs)}.";

        int missing = _monitors.DisconnectedSelection.Count();
        _disconnected.Visible = missing > 0;
        _disconnected.Text = $"Forget {missing} disconnected monitor{(missing == 1 ? "" : "s")}";
    }

    private bool ApplyValues(bool confirm)
    {
        var s = _current().Clone();
        s.Sources = TickedSources();
        s.Monitors = _monitors.SelectedDevices.ToList();
        s.IcpEnabled = _icp.Checked;

        if (confirm && (s.Sources.Count == 0 || (s.Monitors.Count == 0 && !s.IcpEnabled)))
        {
            string what = s.Sources.Count == 0 ? "No audio input is ticked" : "No monitor or ICP is selected";
            if (MessageBox.Show(this, $"{what}, so no warning will be shown. Save anyway?", AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return false;
        }
        try
        {
            _apply(s);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save settings:\n{ex.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    /// <summary>Shows each monitor's number on the monitor itself for a few seconds.</summary>
    internal static void IdentifyMonitors()
    {
        foreach (var screen in Screen.AllScreens)
        {
            var f = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                TopMost = true,
                StartPosition = FormStartPosition.Manual,
                BackColor = Color.FromArgb(0, 90, 200),
                Bounds = new Rectangle(screen.Bounds.Left + 40, screen.Bounds.Top + 40, 260, 200),
            };
            f.Controls.Add(new Label
            {
                Text = MonitorPicker.MonitorNumber(screen.DeviceName).ToString(),
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 110, FontStyle.Bold, GraphicsUnit.Pixel),
            });
            var timer = new System.Windows.Forms.Timer { Interval = 2500 };
            timer.Tick += (_, _) => { timer.Dispose(); f.Close(); };
            f.FormClosed += (_, _) => f.Dispose();
            f.Show();
            timer.Start();
        }
    }
}
