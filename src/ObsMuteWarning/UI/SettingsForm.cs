using System.Text.RegularExpressions;
using ObsMuteWarning.Obs;

namespace ObsMuteWarning.UI;

/// <summary>Settings window: OBS connection, watched sources, warning outputs and appearance.</summary>
internal sealed class SettingsForm : Form
{
    private sealed record SourceItem(string Name, string Uuid, string Label)
    {
        public override string ToString() => Label;
        public bool Matches(WatchedSource s) =>
            (Uuid.Length > 0 && string.Equals(Uuid, s.Uuid, StringComparison.OrdinalIgnoreCase)) ||
            (s.Uuid.Length == 0 && Name == s.Name);
    }

    private sealed record OutputItem(string? MonitorDeviceName, string Label)
    {
        public bool IsIcp => MonitorDeviceName == null;
        public override string ToString() => Label;
    }

    private const string AppName = "OBS Mute Warning";

    private readonly AppSettings _original;
    private readonly Action<AppSettings> _apply;
    private readonly Action _test;

    private readonly TextBox _host = new() { Width = 160 };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Width = 80 };
    private readonly TextBox _password = new() { Width = 160, UseSystemPasswordChar = true };
    private readonly Button _refresh = new() { Text = "Connect && list sources", AutoSize = true };
    private readonly Label _obsStatus = new() { AutoSize = true, MaximumSize = new Size(440, 0) };
    private readonly CheckedListBox _sources = new() { CheckOnClick = true, Height = 130, Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly CheckBox _showAll = new() { Text = "Show all sources with audio (media, browser, game capture…)", AutoSize = true };
    private IReadOnlyList<ObsAudioInput> _inputs = Array.Empty<ObsAudioInput>();
    private readonly CheckedListBox _outputs = new() { CheckOnClick = true, Height = 90, Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Button _identify = new() { Text = "Identify monitors", AutoSize = true };
    private readonly TextBox _popupText = new() { Width = 160, MaxLength = 40 };
    private readonly ComboBox _position = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly ComboBox _size = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly NumericUpDown _opacity = new() { Minimum = 20, Maximum = 100, Width = 80 };
    private readonly CheckBox _clickThrough = new() { Text = "Clicks pass through the popup", AutoSize = true };
    private readonly NumericUpDown _flashMs = new() { Minimum = 100, Maximum = 5000, Increment = 50, Width = 80 };
    private readonly NumericUpDown _icpBrightness = new() { Minimum = 0, Maximum = 255, Width = 80 };
    private readonly CheckBox _icpAfterEnabled = new() { Text = "After the warning, set backlight to", AutoSize = true };
    private readonly NumericUpDown _icpAfter = new() { Minimum = 0, Maximum = 255, Width = 80 };
    private readonly CheckBox _startup = new() { Text = "Start with Windows", AutoSize = true };
    private CancellationTokenSource? _refreshCts;

    public SettingsForm(AppSettings settings, Action<AppSettings> apply, Action test)
    {
        _original = settings.Clone();
        _apply = apply;
        _test = test;

        Text = $"{AppName} – Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(8);
        ShowInTaskbar = true;

        BuildLayout();
        LoadValues(settings);

        _refresh.Click += async (_, _) => await RefreshSourcesAsync();
        _identify.Click += (_, _) => IdentifyMonitors();
        _showAll.CheckedChanged += (_, _) => RebuildSourceList();
        _icpAfterEnabled.CheckedChanged += (_, _) => _icpAfter.Enabled = _icpAfterEnabled.Checked;
        Shown += async (_, _) => await RefreshSourcesAsync();
        FormClosed += (_, _) => _refreshCts?.Cancel();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };

        // OBS connection
        var obs = Grid();
        Row(obs, "Host", _host);
        Row(obs, "Port", _port);
        Row(obs, "Password", _password);
        Row(obs, "", Hint("Leave empty to use the password from this PC's OBS settings."));
        Row(obs, "", _refresh);
        Row(obs, "", _obsStatus);
        root.Controls.Add(Group("OBS WebSocket (OBS → Tools → WebSocket Server Settings)", obs));

        // Sources
        var src = Grid(single: true);
        src.Controls.Add(Hint("Warn when ANY of the ticked audio sources is muted:"));
        src.Controls.Add(_sources);
        src.Controls.Add(_showAll);
        root.Controls.Add(Group("Audio sources", src));

        // Outputs
        var outs = Grid(single: true);
        outs.Controls.Add(Hint("Show the warning on:"));
        outs.Controls.Add(_outputs);
        outs.Controls.Add(_identify);
        root.Controls.Add(Group("Warning appears on", outs));

        // Appearance
        var look = Grid();
        Row(look, "Popup text", _popupText);
        Row(look, "Position", _position);
        Row(look, "Size", _size);
        Row(look, "Opacity %", _opacity);
        Row(look, "", _clickThrough);
        Row(look, "Flash interval (ms)", _flashMs);
        root.Controls.Add(Group("Popup window", look));

        // ICP
        var icp = Grid();
        Row(icp, "Backlight while flashing", _icpBrightness);
        var after = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        after.Controls.Add(_icpAfterEnabled);
        after.Controls.Add(_icpAfter);
        Row(icp, "", after);
        root.Controls.Add(Group("WinWing ViperAce ICP (0–255)", icp));

        root.Controls.Add(_startup);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "Save", AutoSize = true };
        var test = new Button { Text = "Apply && test (5 s)", AutoSize = true };
        ok.Click += (_, _) => { if (ApplyValues(confirm: true)) Close(); };
        test.Click += (_, _) => { if (ApplyValues(confirm: false)) _test(); };
        buttons.Controls.AddRange(new Control[] { cancel, ok, test });
        root.Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(root);

        foreach (var p in Enum.GetValues<PopupPosition>()) _position.Items.Add(p);
        foreach (var s in Enum.GetValues<PopupSize>()) _size.Items.Add(s);
    }

    private static TableLayoutPanel Grid(bool single = false)
    {
        var t = new TableLayoutPanel { ColumnCount = single ? 1 : 2, AutoSize = true, Dock = DockStyle.Fill, Width = 460 };
        if (!single) t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    private static void Row(TableLayoutPanel t, string label, Control c)
    {
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) });
        t.Controls.Add(c);
    }

    private static Label Hint(string text) =>
        new() { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(440, 0) };

    private static GroupBox Group(string title, Control content)
    {
        var g = new GroupBox { Text = title, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(8), MinimumSize = new Size(480, 0) };
        g.Controls.Add(content);
        return g;
    }

    private void LoadValues(AppSettings s)
    {
        _host.Text = s.Obs.Host;
        _port.Value = Math.Clamp(s.Obs.Port, 1, 65535);
        _password.Text = s.Obs.Password;
        foreach (var w in s.Sources)
            _sources.Items.Add(new SourceItem(w.Name, w.Uuid, $"{w} (connecting…)"), true);

        foreach (var item in OutputItems(s))
            _outputs.Items.Add(item, item.IsIcp ? s.IcpEnabled : s.Monitors.Contains(item.MonitorDeviceName!, StringComparer.OrdinalIgnoreCase));

        _popupText.Text = s.PopupText;
        _position.SelectedItem = s.PopupPosition;
        _size.SelectedItem = s.PopupSize;
        _opacity.Value = s.PopupOpacityPercent;
        _clickThrough.Checked = s.PopupClickThrough;
        _flashMs.Value = s.FlashIntervalMs;
        _icpBrightness.Value = s.IcpWarningBrightness;
        _icpAfterEnabled.Checked = s.IcpBrightnessAfterWarning != null;
        _icpAfter.Value = s.IcpBrightnessAfterWarning ?? 0;
        _icpAfter.Enabled = _icpAfterEnabled.Checked;
        _startup.Checked = WindowsStartup.IsEnabled;
    }

    private static IEnumerable<OutputItem> OutputItems(AppSettings s)
    {
        var screens = Screen.AllScreens.OrderBy(sc => MonitorNumber(sc.DeviceName)).ToList();
        foreach (var sc in screens)
            yield return new OutputItem(sc.DeviceName, MonitorLabel(sc));
        foreach (var missing in s.Monitors.Where(m => !screens.Any(sc => string.Equals(sc.DeviceName, m, StringComparison.OrdinalIgnoreCase))))
            yield return new OutputItem(missing, $"{missing} (not connected – primary monitor is used)");
        yield return new OutputItem(null, "WinWing ViperAce ICP (DED display)");
    }

    internal static int MonitorNumber(string deviceName) =>
        Regex.Match(deviceName, @"(\d+)\s*$") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    private static string MonitorLabel(Screen s) =>
        $"Monitor {MonitorNumber(s.DeviceName)}: {s.Bounds.Width}×{s.Bounds.Height}{(s.Primary ? " (primary)" : "")}";

    private ObsConfig CurrentObsConfig() =>
        new() { Host = _host.Text.Trim(), Port = (int)_port.Value, Password = _password.Text };

    /// <summary>Microphones, desktop audio and application audio capture (not media/browser sources).</summary>
    internal static bool IsAudioCapture(string kind) =>
        new[] { "wasapi", "audio", "pulse", "alsa", "jack", "coreaudio" }
            .Any(k => kind.Contains(k, StringComparison.OrdinalIgnoreCase));

    private void RebuildSourceList()
    {
        var ticked = _sources.CheckedItems.Cast<SourceItem>()
            .Select(i => new WatchedSource { Name = i.Name, Uuid = i.Uuid }).ToList();

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
            _sources.Items.Add(new SourceItem(t.Name, t.Uuid, $"{t} (not found in OBS)"), true);
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
            var inputs = await ObsMuteMonitor.FetchAudioInputsAsync(CurrentObsConfig(), TimeSpan.FromSeconds(8), cts.Token);
            if (IsDisposed) return;
            _inputs = inputs;
            RebuildSourceList();
            _obsStatus.ForeColor = Color.DarkGreen;
            _obsStatus.Text = $"Connected. {inputs.Count(i => IsAudioCapture(i.Kind))} audio capture source(s), " +
                              $"{inputs.Count} sources with audio in total.";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _obsStatus.ForeColor = Color.Firebrick;
            _obsStatus.Text = ex is TimeoutException ? "Timed out connecting to OBS." : ex.Message;
        }
        finally
        {
            if (!IsDisposed) _refresh.Enabled = true;
        }
    }

    private AppSettings Gather()
    {
        var s = _original.Clone();
        s.Obs = CurrentObsConfig();
        s.Sources = _sources.CheckedItems.Cast<SourceItem>()
            .Select(i => new WatchedSource { Name = i.Name, Uuid = i.Uuid }).ToList();
        var outs = _outputs.CheckedItems.Cast<OutputItem>().ToList();
        s.Monitors = outs.Where(o => !o.IsIcp).Select(o => o.MonitorDeviceName!).ToList();
        s.IcpEnabled = outs.Any(o => o.IsIcp);
        s.PopupText = _popupText.Text.Trim();
        s.PopupPosition = (PopupPosition)_position.SelectedItem!;
        s.PopupSize = (PopupSize)_size.SelectedItem!;
        s.PopupOpacityPercent = (int)_opacity.Value;
        s.PopupClickThrough = _clickThrough.Checked;
        s.FlashIntervalMs = (int)_flashMs.Value;
        s.IcpWarningBrightness = (byte)_icpBrightness.Value;
        s.IcpBrightnessAfterWarning = _icpAfterEnabled.Checked ? (byte)_icpAfter.Value : null;
        return s;
    }

    private bool ApplyValues(bool confirm)
    {
        var s = Gather();
        if (confirm && (s.Sources.Count == 0 || (s.Monitors.Count == 0 && !s.IcpEnabled)))
        {
            string what = s.Sources.Count == 0 ? "No audio source is ticked" : "No monitor or ICP is ticked";
            if (MessageBox.Show(this, $"{what}, so no warning will be shown. Save anyway?", AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return false;
        }
        try
        {
            if (_startup.Checked != WindowsStartup.IsEnabled) WindowsStartup.Set(_startup.Checked);
            _apply(s);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save settings:\n{ex.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private static void IdentifyMonitors()
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
                Text = MonitorNumber(screen.DeviceName).ToString(),
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
