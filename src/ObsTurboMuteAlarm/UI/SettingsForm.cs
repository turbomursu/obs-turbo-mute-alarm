using ObsTurboMuteAlarm.Obs;

namespace ObsTurboMuteAlarm.UI;

/// <summary>Settings window: OBS connection, warning appearance and ICP brightness.
/// Inputs and displays are chosen in <see cref="ChooserForm"/>.</summary>
internal sealed class SettingsForm : Form
{
    private const string AppName = "OBS Turbo Mute Alarm";

    private readonly Func<AppSettings> _current;
    private readonly Action<AppSettings> _apply;
    private readonly Action _test;
    private readonly Action _openChooser;

    private readonly TextBox _host = new() { Width = 160 };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Width = 80 };
    private readonly TextBox _password = new() { Width = 160, UseSystemPasswordChar = true };
    private readonly Button _testConnection = new() { Text = "Test connection", AutoSize = true };
    private readonly Label _obsStatus = new() { AutoSize = true, MaximumSize = new Size(300, 0) };
    private readonly Label _selection = new() { AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(3, 3, 3, 6) };
    private readonly Button _choose = new() { Text = "Choose inputs and displays…", AutoSize = true };
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
    private CancellationTokenSource? _testCts;

    public SettingsForm(Func<AppSettings> current, Action<AppSettings> apply, Action test, Action openChooser)
    {
        _current = current;
        _apply = apply;
        _test = test;
        _openChooser = openChooser;

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

        BuildLayout();
        LoadValues(current());

        _testConnection.Click += async (_, _) => await TestConnectionAsync();
        _choose.Click += (_, _) => _openChooser();
        _icpAfterEnabled.CheckedChanged += (_, _) => _icpAfter.Enabled = _icpAfterEnabled.Checked;
        Activated += (_, _) => UpdateSelectionSummary();
        FormClosed += (_, _) => _testCts?.Cancel();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };

        var obs = Grid();
        Row(obs, "Host", _host);
        Row(obs, "Port", _port);
        Row(obs, "Password", _password);
        Row(obs, "", Hint("Leave empty to use the password from this PC's OBS settings."));
        var testRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        testRow.Controls.Add(_testConnection);
        _obsStatus.Margin = new Padding(6, 7, 3, 3);
        testRow.Controls.Add(_obsStatus);
        Row(obs, "", testRow);
        root.Controls.Add(Group("OBS WebSocket (OBS → Tools → WebSocket Server Settings)", obs));

        var selection = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
        selection.Controls.Add(_selection);
        selection.Controls.Add(_choose);
        root.Controls.Add(Group("Inputs and displays", selection));

        var look = Grid();
        Row(look, "Popup text", _popupText);
        Row(look, "Position", _position);
        Row(look, "Size", _size);
        Row(look, "Opacity %", _opacity);
        Row(look, "", _clickThrough);
        Row(look, "Flash interval (ms)", _flashMs);
        root.Controls.Add(Group("Popup window", look));

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
        ok.Click += (_, _) => { if (ApplyValues()) Close(); };
        test.Click += (_, _) => { if (ApplyValues()) _test(); };
        buttons.Controls.AddRange(new Control[] { cancel, ok, test });
        root.Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(root);

        foreach (var p in Enum.GetValues<PopupPosition>()) _position.Items.Add(p);
        foreach (var s in Enum.GetValues<PopupSize>()) _size.Items.Add(s);
    }

    private static TableLayoutPanel Grid()
    {
        var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Width = 460 };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    private static void Row(TableLayoutPanel t, string label, Control c)
    {
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) });
        t.Controls.Add(c);
    }

    private static Label Hint(string text) =>
        new() { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(300, 0) };

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
        UpdateSelectionSummary();
    }

    private void UpdateSelectionSummary()
    {
        var s = _current();
        string inputs = s.Sources.Count == 0 ? "none" : string.Join(", ", s.Sources.Select(x => x.ToString()));
        _selection.Text = $"Watching: {inputs}\nWarning on: {TrayApp.DescribeOutputs(s)}";
    }

    private ObsConfig CurrentObsConfig() =>
        new() { Host = _host.Text.Trim(), Port = (int)_port.Value, Password = _password.Text };

    private async Task TestConnectionAsync()
    {
        _testCts?.Cancel();
        var cts = _testCts = new CancellationTokenSource();
        _testConnection.Enabled = false;
        _obsStatus.ForeColor = SystemColors.ControlText;
        _obsStatus.Text = "Connecting…";
        try
        {
            var inputs = await ObsMuteMonitor.FetchAudioInputsAsync(CurrentObsConfig(), TimeSpan.FromSeconds(8), cts.Token);
            if (IsDisposed) return;
            _obsStatus.ForeColor = Color.DarkGreen;
            _obsStatus.Text = $"Connected ({inputs.Count(i => ChooserForm.IsAudioCapture(i.Kind))} audio inputs).";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _obsStatus.ForeColor = Color.Firebrick;
            _obsStatus.Text = ex is TimeoutException ? "Timed out." : ex.Message;
        }
        finally
        {
            if (!IsDisposed) _testConnection.Enabled = true;
        }
    }

    private bool ApplyValues()
    {
        var s = _current().Clone();
        s.Obs = CurrentObsConfig();
        s.PopupText = _popupText.Text.Trim();
        s.PopupPosition = (PopupPosition)_position.SelectedItem!;
        s.PopupSize = (PopupSize)_size.SelectedItem!;
        s.PopupOpacityPercent = (int)_opacity.Value;
        s.PopupClickThrough = _clickThrough.Checked;
        s.FlashIntervalMs = (int)_flashMs.Value;
        s.IcpWarningBrightness = (byte)_icpBrightness.Value;
        s.IcpBrightnessAfterWarning = _icpAfterEnabled.Checked ? (byte)_icpAfter.Value : null;
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
}
